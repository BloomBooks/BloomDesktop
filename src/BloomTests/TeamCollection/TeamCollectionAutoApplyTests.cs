using System.IO;
using System.Linq;
using Bloom.TeamCollection;
using BloomTemp;
using BloomTests.DataBuilders;
using Moq;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.TeamCollection
{
    // Batch item 4+5 (auto-apply remote changes): unit tests for the eligibility/re-verification
    // logic in TeamCollection.HandleModifiedFile / ProcessAutoApplyRemoteChange, exercised through
    // TestFolderTeamCollection's test-only CanAutoApplyRemoteChanges toggle (a real FolderTeamCollection
    // never sets this true; CloudTeamCollection is the only production backend that does, but it's
    // far too heavy to construct in a unit test). See RemoteBookAutoApplyQueueTests for the queue
    // class itself, and TeamCollectionTests for the folder-TC message-only path this must leave
    // completely unchanged when CanAutoApplyRemoteChanges is false (the default).
    public class TeamCollectionAutoApplyTests
    {
        private TemporaryFolder _sharedFolder;
        private TemporaryFolder _collectionFolder;
        private TestFolderTeamCollection _collection;
        private Mock<ITeamCollectionManager> _mockTcManager;
        private TeamCollectionMessageLog _tcLog;

        [SetUp]
        public void Setup()
        {
            TeamCollectionManager.ForceCurrentUserForTests("me@somewhere.org");

            _sharedFolder = new TemporaryFolder("TeamCollectionAutoApply_Shared");
            _collectionFolder = new TemporaryFolder("TeamCollectionAutoApply_Local");
            _tcLog = new TeamCollectionMessageLog(
                TeamCollectionManager.GetTcLogPathFromLcPath(_collectionFolder.FolderPath)
            );
            FolderTeamCollection.CreateTeamCollectionLinkFile(
                _collectionFolder.FolderPath,
                _sharedFolder.FolderPath
            );

            _mockTcManager = new Mock<ITeamCollectionManager>();
            _collection = new TestFolderTeamCollection(
                _mockTcManager.Object,
                _collectionFolder.FolderPath,
                _sharedFolder.FolderPath,
                _tcLog
            );
            _collection.CollectionId = Bloom.TeamCollection.TeamCollection.GenerateCollectionId();
            _collection.TestOnly_MakeAutoApplyQueueSynchronous();
        }

        [TearDown]
        public void TearDown()
        {
            TeamCollectionManager.ForceCurrentUserForTests(null);
            _collectionFolder.Dispose();
            _sharedFolder.Dispose();
        }

        // Puts a book in the repo, then rewrites its local content and local status record so
        // HasBeenChangedRemotely(bookFolderName) is true (mirrors
        // TeamCollectionTests.HandleModifiedFile_NoConflictBookChangedNotCheckedOut...'s setup).
        private void SetUpBookChangedRemotely(string bookFolderName, out string bookFolderPath)
        {
            var bookBuilder = new BookFolderBuilder()
                .WithRootFolder(_collectionFolder.FolderPath)
                .WithTitle(bookFolderName)
                .WithHtm("This is pretending to be new content from remote")
                .Build();
            bookFolderPath = bookBuilder.BuiltBookFolderPath;
            var status = _collection.PutBook(bookFolderPath);
            RobustFile.WriteAllText(
                bookBuilder.BuiltBookHtmPath,
                "This is pretending to be old content"
            );
            // pretending this (the pre-change checksum) is what local status recorded before
            // the remote change described above.
            _collection.WriteLocalStatus(
                bookFolderName,
                status.WithChecksum(
                    Bloom.TeamCollection.TeamCollection.MakeChecksum(bookFolderPath)
                )
            );
        }

        [Test]
        public void HandleModifiedFile_AutoApplyEnabled_UnlockedBookChangedRemotely_CopiesContentAndWritesNoNewStuffMessage()
        {
            const string bookFolderName = "Auto Apply Book";
            SetUpBookChangedRemotely(bookFolderName, out var bookFolderPath);
            _collection.AutoApplyRemoteChangesForTests = true;

            Assert.That(
                _collection.HasBeenChangedRemotely(bookFolderName),
                Is.True,
                "sanity: book should look changed remotely before the auto-apply pass runs"
            );
            var prevMessages = _tcLog.Messages.Count;

            // System Under Test // (queue is synchronous per Setup, so this call fully completes
            // the auto-apply pass before returning)
            _collection.HandleModifiedFile(
                new BookRepoChangeEventArgs() { BookFileName = $"{bookFolderName}.bloom" }
            );

            // Verification
            Assert.That(
                _tcLog.Messages.Count,
                Is.EqualTo(prevMessages),
                "auto-apply should succeed silently -- no 'click to see updates' message"
            );
            Assert.That(
                _collection.HasBeenChangedRemotely(bookFolderName),
                Is.False,
                "local content should now match the repo"
            );
            var htmlPath = Path.Combine(bookFolderPath, bookFolderName + ".htm");
            Assert.That(
                RobustFile.ReadAllText(htmlPath),
                Does.Contain("new content from remote"),
                "the book folder's actual content should have been overwritten with the repo version"
            );
        }

        [Test]
        public void HandleModifiedFile_AutoApplyDisabled_FolderTcDefault_LeavesContentAloneAndWritesNewStuffMessage()
        {
            // AutoApplyRemoteChangesForTests defaults to false -- this is exactly today's
            // production folder-TC behavior (see TeamCollectionTests for the equivalent test
            // against a real, non-test FolderTeamCollection).
            const string bookFolderName = "No Auto Apply Book";
            SetUpBookChangedRemotely(bookFolderName, out var bookFolderPath);
            var prevMessages = _tcLog.Messages.Count;

            // System Under Test //
            _collection.HandleModifiedFile(
                new BookRepoChangeEventArgs() { BookFileName = $"{bookFolderName}.bloom" }
            );

            // Verification
            Assert.That(
                _tcLog.Messages[prevMessages].MessageType,
                Is.EqualTo(MessageAndMilestoneType.NewStuff)
            );
            Assert.That(
                _tcLog.Messages[prevMessages].L10NId,
                Is.EqualTo("TeamCollection.BookModifiedRemotely")
            );
            Assert.That(
                _collection.HasBeenChangedRemotely(bookFolderName),
                Is.True,
                "without auto-apply, the local content must be left untouched until the user acts"
            );
        }

        [Test]
        public void ProcessAutoApplyRemoteChange_CheckedOutHereSinceQueueing_SkipsApply()
        {
            // Simulates the race the task calls out explicitly: HandleModifiedFile queues the book
            // while it looked safe, but by the time the worker actually runs, the user has checked
            // it out here. Re-verification must catch this and back off rather than clobbering
            // whatever the user is about to do with their checkout.
            const string bookFolderName = "Raced Checkout Book";
            SetUpBookChangedRemotely(bookFolderName, out _);
            _collection.AutoApplyRemoteChangesForTests = true;
            _collection.AttemptLock(bookFolderName); // checks it out here, to the current test user

            var prevMessages = _tcLog.Messages.Count;
            var prevInvocations = _mockTcManager.Invocations.Count;

            // System Under Test // (bypasses the queue -- see the method's own doc comment)
            _collection.TestOnly_ProcessAutoApplyRemoteChange(bookFolderName);

            // Verification: no copy attempted (repo checksum still doesn't match a book we never
            // actually fetched), and no NEW message or status notification beyond whatever the
            // AttemptLock call above itself already produced (this is a silent back-off, not an
            // error -- see the method's doc comment).
            Assert.That(
                _tcLog.Messages.Count,
                Is.EqualTo(prevMessages),
                "re-verification should silently decline to apply, not write any message"
            );
            Assert.That(
                _mockTcManager.Invocations.Count,
                Is.EqualTo(prevInvocations),
                "declining to apply should not touch book status either"
            );
        }

        [Test]
        public void ProcessAutoApplyRemoteChange_AlreadyUpToDate_SkipsApply()
        {
            // If the book is no longer considered changed remotely (e.g. a previous pass, or the
            // user, already brought it up to date), the worker must not redundantly re-copy it.
            const string bookFolderName = "Already Current Book";
            var bookBuilder = new BookFolderBuilder()
                .WithRootFolder(_collectionFolder.FolderPath)
                .WithTitle(bookFolderName)
                .Build();
            _collection.PutBook(bookBuilder.BuiltBookFolderPath); // local status matches repo already
            _collection.AutoApplyRemoteChangesForTests = true;

            Assert.That(
                _collection.HasBeenChangedRemotely(bookFolderName),
                Is.False,
                "sanity: nothing changed remotely for this book"
            );
            var prevMessages = _tcLog.Messages.Count;
            var prevInvocations = _mockTcManager.Invocations.Count;

            // System Under Test //
            _collection.TestOnly_ProcessAutoApplyRemoteChange(bookFolderName);

            Assert.That(_tcLog.Messages.Count, Is.EqualTo(prevMessages));
            Assert.That(
                _mockTcManager.Invocations.Count,
                Is.EqualTo(prevInvocations),
                "a book that's already current should not trigger any status notification"
            );
        }

        [Test]
        public void ProcessAutoApplyRemoteChange_CopyFails_FallsBackToNewStuffMessage()
        {
            // Simulates a copy failure (e.g. a corrupt/missing repo file) at the moment the worker
            // actually tries to apply the change. Even with auto-apply enabled, the user must still
            // end up with exactly the same fallback notification a non-auto-apply backend gives.
            const string bookFolderName = "Copy Fails Book";
            SetUpBookChangedRemotely(bookFolderName, out _);
            _collection.AutoApplyRemoteChangesForTests = true;

            // Make the repo copy fail while still leaving HasBeenChangedRemotely true: corrupt the
            // repo's zip file (rather than deleting it -- a MISSING repo file makes GetStatus fall
            // back to the local status record, which would make the book look already up to date
            // instead of exercising the copy-failure path this test wants).
            var repoBookPath = _collection.GetPathToBookFileInRepo(bookFolderName);
            RobustFile.WriteAllText(repoBookPath, "this is not a valid zip file");

            Assert.That(
                _collection.HasBeenChangedRemotely(bookFolderName),
                Is.True,
                "sanity: a corrupt repo file should still look different from the recorded local status"
            );

            var prevMessages = _tcLog.Messages.Count;

            // System Under Test //
            _collection.TestOnly_ProcessAutoApplyRemoteChange(bookFolderName);

            // Verification: the fallback message-only behavior appears among whatever else got
            // logged (a corrupt zip may also log its own ErrorNoReload as a side effect of the
            // eligibility checks reading repo status; this test only cares that the user still
            // ends up with the same fallback notification a non-auto-apply backend would give).
            var newMessages = _tcLog.Messages.Skip(prevMessages).ToList();
            Assert.That(
                newMessages.Any(m =>
                    m.MessageType == MessageAndMilestoneType.NewStuff
                    && m.L10NId == "TeamCollection.BookModifiedRemotely"
                ),
                Is.True,
                "a copy failure must still leave the user with the fallback 'click to see updates' message"
            );
        }
    }
}
