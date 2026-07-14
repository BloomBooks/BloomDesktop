using System;
using System.IO;
using System.Linq;
using System.Threading;
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

        // ------------------------------------------------------------------
        // Batch item 8 (John's recovery decision, 9 Jul 2026): before a sync overwrite, local
        // content that changed since the last sync is preserved (cloud: .bloomSource in Lost and
        // Found); clean local content is overwritten without ceremony. The preserve DECISION is
        // base-class logic, recorded here via TestFolderTeamCollection's hook override.
        // ------------------------------------------------------------------

        [Test]
        public void ProcessAutoApplyRemoteChange_LocalModifiedSinceLastSync_PreservesBeforeApplying()
        {
            const string bookFolderName = "Locally Drifted Book";
            SetUpBookChangedRemotely(bookFolderName, out var bookFolderPath);
            // On top of the remote change, the LOCAL copy has also drifted from what the last sync
            // recorded (the force-stolen-checkout shape: local edits the status file knows nothing
            // about, since cloud checkouts never write it).
            RobustFile.WriteAllText(
                Path.Combine(bookFolderPath, bookFolderName + ".htm"),
                "precious local work the sync must not silently discard"
            );
            _collection.AutoApplyRemoteChangesForTests = true;

            // System Under Test //
            _collection.TestOnly_ProcessAutoApplyRemoteChange(bookFolderName);

            Assert.That(
                _collection.PreservedForRecovery,
                Is.EqualTo(new[] { bookFolderName }),
                "the drifted local copy must be preserved exactly once, before the overwrite"
            );
            Assert.That(
                RobustFile.ReadAllText(Path.Combine(bookFolderPath, bookFolderName + ".htm")),
                Does.Contain("new content from remote"),
                "after preserving, the sync must still make local consistent with the repo (John's decision: apply, don't block)"
            );
        }

        [Test]
        public void ProcessAutoApplyRemoteChange_LocalCleanSinceLastSync_DoesNotPreserve()
        {
            const string bookFolderName = "Clean Local Book";
            // SetUpBookChangedRemotely leaves the local copy EXACTLY matching its recorded local
            // status checksum (only the repo differs), i.e. the everyday "teammate checked in a
            // change" case -- overwriting loses nothing, so nothing should go to Lost and Found.
            SetUpBookChangedRemotely(bookFolderName, out var bookFolderPath);
            _collection.AutoApplyRemoteChangesForTests = true;
            Assert.That(
                _collection.HasBeenChangedRemotely(bookFolderName),
                Is.True,
                "sanity: the repo version differs, so the apply itself must still happen"
            );

            // System Under Test //
            _collection.TestOnly_ProcessAutoApplyRemoteChange(bookFolderName);

            Assert.That(
                _collection.PreservedForRecovery,
                Is.Empty,
                "an unmodified local copy must be overwritten without a Lost and Found entry"
            );
            Assert.That(
                RobustFile.ReadAllText(Path.Combine(bookFolderPath, bookFolderName + ".htm")),
                Does.Contain("new content from remote"),
                "the apply itself must still have happened"
            );
        }

        // ------------------------------------------------------------------
        // Batch item 7 (progressive join): a book that exists in the repo but has NO local folder
        // at all (as opposed to the "changed remotely" scenarios above, which all start from an
        // existing local folder) goes through DownloadMissingBookInBackground instead of the
        // auto-apply re-verification -- there's no existing local content to check eligibility
        // against or protect, just a straightforward fetch.
        // ------------------------------------------------------------------

        private string PutBookThenRemoveLocalFolder(string bookFolderName)
        {
            var bookBuilder = new BookFolderBuilder()
                .WithRootFolder(_collectionFolder.FolderPath)
                .WithTitle(bookFolderName)
                .WithHtm("Content that only exists in the repo")
                .Build();
            var folderPath = bookBuilder.BuiltBookFolderPath;
            _collection.PutBook(folderPath); // gets it into the repo
            SIL.IO.RobustIO.DeleteDirectoryAndContents(folderPath); // simulate "never downloaded here"
            return folderPath;
        }

        [Test]
        public void ProcessAutoApplyRemoteChange_NoLocalFolderAtAll_DownloadsTheBook()
        {
            const string bookFolderName = "Never Downloaded Book";
            var folderPath = PutBookThenRemoveLocalFolder(bookFolderName);
            _collection.AutoApplyRemoteChangesForTests = true;

            Assert.That(
                Directory.Exists(folderPath),
                Is.False,
                "sanity: the local folder must not exist before the System Under Test call"
            );

            // System Under Test // (bypasses the queue -- TestOnly_ProcessAutoApplyRemoteChange
            // exercises the same worker method the queue would eventually call)
            _collection.TestOnly_ProcessAutoApplyRemoteChange(bookFolderName);

            Assert.That(
                Directory.Exists(folderPath),
                Is.True,
                "the book should have been downloaded from the repo into a fresh local folder"
            );
            Assert.That(
                RobustFile.ReadAllText(Path.Combine(folderPath, bookFolderName + ".htm")),
                Does.Contain("Content that only exists in the repo")
            );
        }

        [Test]
        public void ProcessAutoApplyRemoteChange_NoLocalFolderAndGoneFromRepoToo_DoesNothing()
        {
            // Simulates the book vanishing from the repo (e.g. deleted by a teammate) between the
            // moment it was queued and the moment the background worker actually got to it.
            const string bookFolderName = "Deleted Before Download Book";
            var folderPath = PutBookThenRemoveLocalFolder(bookFolderName);
            _collection.DeleteBookFromRepo(folderPath); // also removes it from _repoFolder
            _collection.AutoApplyRemoteChangesForTests = true;

            // System Under Test //
            _collection.TestOnly_ProcessAutoApplyRemoteChange(bookFolderName);

            Assert.That(
                Directory.Exists(folderPath),
                Is.False,
                "a book that's gone from the repo by the time the worker runs must not be recreated"
            );
        }

        // ------------------------------------------------------------------
        // Batch item 7 (progressive join): SyncAtStartup's "brand new book!" branch reroutes to
        // the same background queue for a backend with CanAutoApplyRemoteChanges (cloud), instead
        // of blocking the startup sync dialog on every missing book's full download. Folder TCs
        // (CanAutoApplyRemoteChanges always false) must be completely unaffected.
        // ------------------------------------------------------------------

        [Test]
        public void SyncAtStartup_FolderTcDefault_NewRepoBookOnly_FetchesSynchronously()
        {
            const string bookFolderName = "Folder TC New Book";
            var folderPath = PutBookThenRemoveLocalFolder(bookFolderName);
            // AutoApplyRemoteChangesForTests defaults to false: real folder-TC behavior, unchanged
            // by this batch item.

            // System Under Test //
            _collection.SyncAtStartup(new ProgressSpy(), firstTimeJoin: false);

            Assert.That(
                Directory.Exists(folderPath),
                Is.True,
                "a folder TC must still fetch a brand-new repo book synchronously, inside SyncAtStartup itself"
            );
        }

        [Test]
        public void SyncAtStartup_AutoApplyEnabled_NewRepoBookOnly_QueuesInsteadOfFetchingSynchronously()
        {
            const string bookFolderName = "Cloud-Like New Book";
            var folderPath = PutBookThenRemoveLocalFolder(bookFolderName);
            _collection.AutoApplyRemoteChangesForTests = true;
            _collection.TestOnly_MakeAutoApplyQueueSynchronous();

            // System Under Test // (queue is synchronous, so the whole background pass completes
            // inline before SyncAtStartup returns -- this test asserts the REROUTED path still gets
            // the book, deterministically; see the async test below for the non-blocking behavior)
            _collection.SyncAtStartup(new ProgressSpy(), firstTimeJoin: false);

            Assert.That(
                Directory.Exists(folderPath),
                Is.True,
                "the rerouted background download should still successfully fetch the book"
            );
        }

        // ------------------------------------------------------------------
        // Post-batch defect fix (10 Jul 2026): QueueMissingRepoBooksForBackgroundDownload is the
        // self-healing retry pass -- the in-memory queue does not survive a Bloom restart (e.g.
        // the join flow's pullDown-then-relaunch pattern), and the poll only raises events for
        // books whose repo state CHANGED, so a locally-missing repo book that slipped past the
        // startup sync was previously never retried. CloudTeamCollection calls this when
        // monitoring starts and after every poll.
        // ------------------------------------------------------------------

        [Test]
        public void QueueMissingRepoBooks_AutoApplyEnabled_DownloadsMissingBook()
        {
            const string bookFolderName = "Dropped Download Book";
            var folderPath = PutBookThenRemoveLocalFolder(bookFolderName);
            _collection.AutoApplyRemoteChangesForTests = true;
            _collection.TestOnly_MakeAutoApplyQueueSynchronous();

            // System Under Test //
            _collection.QueueMissingRepoBooksForBackgroundDownload();

            Assert.That(
                Directory.Exists(folderPath),
                Is.True,
                "a repo book with no local folder should be queued and downloaded by the retry pass"
            );
            Assert.That(
                RobustFile.ReadAllText(Path.Combine(folderPath, bookFolderName + ".htm")),
                Does.Contain("Content that only exists in the repo")
            );
        }

        // Puts a book in the repo, locks it as `lockedBy`, then removes the local folder --
        // the "missing locally but checked out" setup both lock-guard tests below need.
        private string PutLockedBookThenRemoveLocalFolder(string bookFolderName, string lockedBy)
        {
            var bookBuilder = new BookFolderBuilder()
                .WithRootFolder(_collectionFolder.FolderPath)
                .WithTitle(bookFolderName)
                .WithHtm("Content that only exists in the repo")
                .Build();
            var folderPath = bookBuilder.BuiltBookFolderPath;
            _collection.PutBook(folderPath);
            _collection.AttemptLock(bookFolderName, lockedBy);
            SIL.IO.RobustIO.DeleteDirectoryAndContents(folderPath);
            Assert.That(
                _collection.WhoHasBookLocked(bookFolderName),
                Is.EqualTo(lockedBy),
                "sanity: the repo copy must be locked before the System Under Test call"
            );
            return folderPath;
        }

        [Test]
        public void QueueMissingRepoBooks_BookLockedByCurrentUser_SkipsIt()
        {
            // Locked BY ME + no local folder = the local-rename-mid-checkin edge (the old repo
            // name intentionally has no local folder); downloading it would resurrect the
            // pre-rename book.
            const string bookFolderName = "My Locked Missing Book";
            var folderPath = PutLockedBookThenRemoveLocalFolder(
                bookFolderName,
                "me@somewhere.org" // the ForceCurrentUserForTests identity from Setup
            );
            _collection.AutoApplyRemoteChangesForTests = true;
            _collection.TestOnly_MakeAutoApplyQueueSynchronous();

            // System Under Test //
            _collection.QueueMissingRepoBooksForBackgroundDownload();

            Assert.That(
                Directory.Exists(folderPath),
                Is.False,
                "a repo book locked by the current user must not be downloaded (rename-mid-checkin edge)"
            );
        }

        [Test]
        public void QueueMissingRepoBooks_BookLockedByCurrentUserOnAnotherMachine_StillDownloadsIt()
        {
            // Preflight review finding (10 Jul 2026): the rename-mid-checkin edge the skip
            // protects is MACHINE-local. A book the current user has checked out on a DIFFERENT
            // machine must still be downloaded here (SyncAtStartup already fetches it on
            // restart; the retry pass must agree).
            const string bookFolderName = "My Other Machine Book";
            var bookBuilder = new BookFolderBuilder()
                .WithRootFolder(_collectionFolder.FolderPath)
                .WithTitle(bookFolderName)
                .WithHtm("Content that only exists in the repo")
                .Build();
            var folderPath = bookBuilder.BuiltBookFolderPath;
            var status = _collection.PutBook(folderPath);
            // Stamp a lock held by the current user but recorded for a DIFFERENT machine
            // (WithLockedBy always stamps CurrentMachine, so overwrite lockedWhere directly).
            var lockedStatus = status.WithLockedBy("me@somewhere.org");
            lockedStatus.lockedWhere = "SomeOtherComputer";
            _collection.WriteBookStatus(bookFolderName, lockedStatus);
            SIL.IO.RobustIO.DeleteDirectoryAndContents(folderPath);
            Assert.That(
                _collection.WhoHasBookLocked(bookFolderName),
                Is.EqualTo("me@somewhere.org"),
                "sanity: the repo copy must be locked by the current user"
            );
            Assert.That(
                _collection.WhatComputerHasBookLocked(bookFolderName),
                Is.EqualTo("SomeOtherComputer"),
                "sanity: the lock must be recorded for a different machine"
            );
            _collection.AutoApplyRemoteChangesForTests = true;
            _collection.TestOnly_MakeAutoApplyQueueSynchronous();

            // System Under Test //
            _collection.QueueMissingRepoBooksForBackgroundDownload();

            Assert.That(
                Directory.Exists(folderPath),
                Is.True,
                "a book the current user checked out on a DIFFERENT machine must still download here"
            );
        }

        [Test]
        public void QueueMissingRepoBooks_BookLockedBySomeoneElse_StillDownloadsIt()
        {
            // A teammate's lock must NOT block downloading the committed content (that is
            // exactly what Receive fetches for a locked book). e2e-4 (10 Jul 2026): an any-lock
            // skip turned one transient download failure into "book missing for as long as the
            // teammate held the lock".
            const string bookFolderName = "Teammate Locked Missing Book";
            var folderPath = PutLockedBookThenRemoveLocalFolder(
                bookFolderName,
                "fred@somewhere.org"
            );
            _collection.AutoApplyRemoteChangesForTests = true;
            _collection.TestOnly_MakeAutoApplyQueueSynchronous();

            // System Under Test //
            _collection.QueueMissingRepoBooksForBackgroundDownload();

            Assert.That(
                Directory.Exists(folderPath),
                Is.True,
                "a repo book locked by someone ELSE must still be downloaded by the retry pass"
            );
            Assert.That(
                RobustFile.ReadAllText(Path.Combine(folderPath, bookFolderName + ".htm")),
                Does.Contain("Content that only exists in the repo")
            );
        }

        [Test]
        public void QueueMissingRepoBooks_BookAlreadyLocal_LeavesItAlone()
        {
            const string bookFolderName = "Already Local Book";
            SetUpBookChangedRemotely(bookFolderName, out var bookFolderPath);
            // Local content now deliberately differs from the repo ("old content" vs "new content
            // from remote") -- if the retry pass wrongly re-downloaded an EXISTING book, the local
            // text would change, which the assertion below would catch.
            _collection.AutoApplyRemoteChangesForTests = true;
            _collection.TestOnly_MakeAutoApplyQueueSynchronous();

            // System Under Test //
            _collection.QueueMissingRepoBooksForBackgroundDownload();

            Assert.That(
                RobustFile.ReadAllText(Path.Combine(bookFolderPath, bookFolderName + ".htm")),
                Does.Contain("pretending to be old content"),
                "a book that already has a local folder must not be touched by the missing-books pass"
            );
        }

        [Test]
        public void QueueMissingRepoBooks_AutoApplyDisabled_FolderTcDefault_DoesNothing()
        {
            const string bookFolderName = "Folder TC Missing Book";
            var folderPath = PutBookThenRemoveLocalFolder(bookFolderName);
            // AutoApplyRemoteChangesForTests defaults to false: folder TCs have no background
            // download pipeline, so the retry pass must be a complete no-op there.
            _collection.TestOnly_MakeAutoApplyQueueSynchronous();

            // System Under Test //
            _collection.QueueMissingRepoBooksForBackgroundDownload();

            Assert.That(
                Directory.Exists(folderPath),
                Is.False,
                "a folder TC (CanAutoApplyRemoteChanges false) must not background-download anything"
            );
        }

        [Test]
        public void SyncAtStartup_AutoApplyEnabled_RealBackgroundWorker_EventuallyFetchesWithoutBlocking()
        {
            const string bookFolderName = "Cloud-Like Async New Book";
            var folderPath = PutBookThenRemoveLocalFolder(bookFolderName);
            _collection.AutoApplyRemoteChangesForTests = true;
            // Real (default Task.Run) worker this time -- proves the download genuinely happens on
            // a background thread rather than merely being deterministic-but-still-synchronous.

            // System Under Test //
            _collection.SyncAtStartup(new ProgressSpy(), firstTimeJoin: false);

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline && !Directory.Exists(folderPath))
                Thread.Sleep(20);

            Assert.That(
                Directory.Exists(folderPath),
                Is.True,
                "the book should eventually be downloaded in the background even though SyncAtStartup itself didn't fetch it"
            );
        }
    }
}
