﻿using System;
using System.IO;
using System.Linq;
using Bloom.Book;
using Bloom.TeamCollection;
using BloomTemp;
using BloomTests.DataBuilders;
using Moq;
using NUnit.Framework;
using SIL.IO;
using Directory = System.IO.Directory;
using File = System.IO.File;

namespace BloomTests.TeamCollection
{
    // While this makes considerable use of FolderTeamCollection, the tests here are focused on the code in the TeamCollection class.
    // Some of the code in TeamCollection is more easily tested by methods in FolderTeamCollectionTests. Most of the tests
    // here focus on SyncAtStartup.
    // Note: in a very early version, TeamCollection was called TeamRepo. While we've mostly gotten rid of the 'repo'
    // name, it was extensively used in test comments to indicate "the version of X in the shared location". I don't have
    // a better short name for that so for now I have kept it.
    public class TeamCollectionTests
    {
        private TemporaryFolder _sharedFolder;
        private TemporaryFolder _collectionFolder;
        private FolderTeamCollection _collection;
        private Mock<ITeamCollectionManager> _mockTcManager;
        private TeamCollectionMessageLog _tcLog;

        [SetUp]
        public void Setup()
        {
            TeamCollectionManager.ForceCurrentUserForTests("me@somewhere.org");

            _sharedFolder = new TemporaryFolder("TeamCollection_Shared");
            _collectionFolder = new TemporaryFolder("TeamCollection_Local");
            _tcLog = new TeamCollectionMessageLog(
                TeamCollectionManager.GetTcLogPathFromLcPath(_collectionFolder.FolderPath)
            );
            FolderTeamCollection.CreateTeamCollectionLinkFile(
                _collectionFolder.FolderPath,
                _sharedFolder.FolderPath
            );

            _mockTcManager = new Mock<ITeamCollectionManager>();
            _collection = new FolderTeamCollection(
                _mockTcManager.Object,
                _collectionFolder.FolderPath,
                _sharedFolder.FolderPath,
                _tcLog
            );
            _collection.CollectionId = Bloom.TeamCollection.TeamCollection.GenerateCollectionId();
        }

        [TearDown]
        public void TearDown()
        {
            TeamCollectionManager.ForceCurrentUserForTests(null);
            _collectionFolder.Dispose();
            _sharedFolder.Dispose();
        }

        [Test]
        public void HandleNewBook_CreatesNewStuffMessage()
        {
            var bookBuilder = new BookFolderBuilder()
                .WithRootFolder(_collectionFolder.FolderPath)
                .WithTitle("My new book")
                .Build(); // Writes the book to disk based on the above specified values
            string bookFolderPath = bookBuilder.BuiltBookFolderPath; // Gets the location the book was written to

            _collection.PutBook(bookFolderPath);
            SIL.IO.RobustIO.DeleteDirectoryAndContents(bookFolderPath);
            var prevMessages = _tcLog.Messages.Count;

            // SUT: We're mainly testing HandleNewbook, but it's convenient to check that this method calls it properly.
            _collection.QueuePendingBookChange(
                new NewBookEventArgs() { BookFileName = "My new book.bloom" }
            );
            _collection.HandleRemoteBookChangesOnIdle(null, new EventArgs());

            Assert.That(
                _tcLog.Messages[prevMessages].MessageType,
                Is.EqualTo(MessageAndMilestoneType.NewStuff)
            );
        }

        [Test]
        public void HandleModifiedFile_NoLocalBook_DoesNothing()
        {
            // Setup //
            // Simulate that a book appeared remotely. We should eventually get a created notice.
            // Sometimes, for reasons we don't fully understand, we get a modified notice
            // first. Or the book might be modified again before we fetch it. In any case,
            // we don't need modify messages until we fetch a local copy.
            const string bookFolderName = "My book";
            var bookBuilder = new BookFolderBuilder()
                .WithRootFolder(_collectionFolder.FolderPath)
                .WithTitle(bookFolderName)
                .Build(); // Writes the book to disk based on the above specified values
            string bookFolderPath = bookBuilder.BuiltBookFolderPath;

            var status = _collection.PutBook(bookFolderPath);
            // pretending we had nothing local before the change.
            RobustIO.DeleteDirectory(bookFolderPath, true);

            // Anticipate verification
            var prevMessages = _tcLog.Messages.Count;
            _mockTcManager
                .Setup(m => m.RaiseBookStatusChanged(It.IsAny<BookStatusChangeEventArgs>()))
                .Throws(new ArgumentException("RaiseBookStatus should not be called"));

            // System Under Test //
            _collection.HandleModifiedFile(
                new BookRepoChangeEventArgs() { BookFileName = $"{bookFolderName}.bloom" }
            );

            // Verification
            Assert.That(_tcLog.Messages.Count, Is.EqualTo(prevMessages));
        }

        // TODO: Add a test for GivenModifiedToCheckedOutByOther. But, getting it set up has been proving more thorny than worth right now
        [Test]
        public void HandleModifiedFile_NoConflictBookNotChangedCheckedOutRemoved_RaisesCheckedOutByNoneButNoNewStuffMessage()
        {
            // Setup //
            // Simulate (sort of) that a book was just overwritten with the following new contents,
            // including that book.status does not indicate it's checked out
            const string bookFolderName = "My book";
            var bookBuilder = new BookFolderBuilder()
                .WithRootFolder(_collectionFolder.FolderPath)
                .WithTitle(bookFolderName)
                .Build(); // Writes the book to disk based on the above specified values
            string bookFolderPath = bookBuilder.BuiltBookFolderPath;

            var status = _collection.PutBook(bookFolderPath);
            // pretending this is what it was before the change.
            _collection.WriteLocalStatus(bookFolderName, status.WithLockedBy("fred@somewhere.org"));
            var prevMessages = _tcLog.Messages.Count;

            // System Under Test //
            _collection.HandleModifiedFile(
                new BookRepoChangeEventArgs() { BookFileName = $"{bookFolderName}.bloom" }
            );

            // Verification
            var eventArgs = (BookStatusChangeEventArgs)_mockTcManager.Invocations[0].Arguments[0];
            Assert.That(eventArgs.CheckedOutByWhom, Is.EqualTo(CheckedOutBy.None));

            Assert.That(_tcLog.Messages.Count, Is.EqualTo(prevMessages));
        }

        [Test]
        public void HandleModifiedFile_NoConflictBookChangedNotCheckedOut_RaisesCheckedOutByNoneAndNewStuffMessage()
        {
            // Simulate (sort of) that a book was just overwritten with the following new contents,
            // including that book.status does not indicate it's checked out
            const string bookFolderName = "My book";
            var bookBuilder = new BookFolderBuilder()
                .WithRootFolder(_collectionFolder.FolderPath)
                .WithTitle(bookFolderName)
                .WithHtm("This is pretending to be new content from remote")
                .Build();

            string bookFolderPath = bookBuilder.BuiltBookFolderPath;
            var status = _collection.PutBook(bookFolderPath);
            RobustFile.WriteAllText(
                bookBuilder.BuiltBookHtmPath,
                "This is pretending to be old content"
            );
            // pretending this is what it was before the change.
            _collection.WriteLocalStatus(
                bookFolderName,
                status.WithChecksum(
                    Bloom.TeamCollection.TeamCollection.MakeChecksum(bookFolderPath)
                )
            );
            var prevMessages = _tcLog.Messages.Count;

            // System Under Test //
            _collection.HandleModifiedFile(
                new BookRepoChangeEventArgs() { BookFileName = $"{bookFolderName}.bloom" }
            );

            // Verification
            var eventArgs = (BookStatusChangeEventArgs)_mockTcManager.Invocations[0].Arguments[0];
            Assert.That(eventArgs.CheckedOutByWhom, Is.EqualTo(CheckedOutBy.None));

            Assert.That(
                _tcLog.Messages[prevMessages].MessageType,
                Is.EqualTo(MessageAndMilestoneType.NewStuff)
            );
        }

        [Test]
        public void HandleModifiedFile_NoConflictBookCheckedOutRemotely_RaisesCheckedOutByOtherButNoNewStuffMessage()
        {
            // Setup //
            // Simulate (sort of) that a book was just overwritten with the following new contents,
            // including that book.status indicates a remote checkout
            const string bookFolderName = "My other book";
            var bookBuilder = new BookFolderBuilder()
                .WithRootFolder(_collectionFolder.FolderPath)
                .WithTitle(bookFolderName)
                .Build();
            string bookFolderPath = bookBuilder.BuiltBookFolderPath;

            _collection.PutBook(bookFolderPath);
            _collection.AttemptLock("My other book", "nancy@somewhere.com");
            // Enhance: to make it more realistic, we could write a not-checked-out-here local status,
            // but it's not necessary for producing the effects we want to test here.
            var prevMessages = _tcLog.Messages.Count;

            // System Under Test //
            _collection.HandleModifiedFile(
                new BookRepoChangeEventArgs() { BookFileName = $"{bookFolderName}.bloom" }
            );

            // Verification
            var eventArgs = (BookStatusChangeEventArgs)
                _mockTcManager.Invocations.Last().Arguments[0];
            Assert.That(eventArgs.CheckedOutByWhom, Is.EqualTo(CheckedOutBy.Other));

            Assert.That(_tcLog.Messages.Count, Is.EqualTo(prevMessages)); // checksums didn't change
        }

        [Test]
        public void HandleModifiedFile_CheckedOutToMe_RemotelyToOther_RaisesCheckedOutByOtherAndErrorMessage()
        {
            // Setup //
            // Simulate a book was just overwritten with contents indicating a remote checkout,
            // while locally it is checked out to me.
            const string bookFolderName = "My conflict book";
            var bookBuilder = new BookFolderBuilder()
                .WithRootFolder(_collectionFolder.FolderPath)
                .WithTitle(bookFolderName)
                .Build();
            string bookFolderPath = bookBuilder.BuiltBookFolderPath;

            TeamCollectionManager.ForceCurrentUserForTests("me@somewhere.org");

            _collection.PutBook(bookFolderPath);
            // Temporarily, it looks locked by Nancy in both places.
            _collection.AttemptLock("My conflict book", "nancy@somewhere.com");
            var status = _collection
                .GetStatus("My conflict book")
                .WithLockedBy(TeamCollectionManager.CurrentUser);
            // Now it is locally checked out to me. (The state changes are in the opposite order to what
            // we're trying to simulate, because we don't have an easy way to change remote checkout status without
            // changing local status to match at the same time.)
            _collection.WriteLocalStatus("My conflict book", status);
            var prevMessages = _tcLog.Messages.Count;

            // System Under Test...basically HandleModifiedFile, but this is a convenient place to
            // make sure we take the right path through this calling method.
            _collection.QueuePendingBookChange(
                new BookRepoChangeEventArgs() { BookFileName = $"{bookFolderName}.bloom" }
            );
            _collection.HandleRemoteBookChangesOnIdle(null, new EventArgs());

            // Verification
            var eventArgs = (BookStatusChangeEventArgs)_mockTcManager.Invocations[2].Arguments[0];
            Assert.That(eventArgs.CheckedOutByWhom, Is.EqualTo(CheckedOutBy.Other));

            Assert.That(
                _tcLog.Messages[prevMessages].MessageType,
                Is.EqualTo(MessageAndMilestoneType.Error)
            );
            Assert.That(
                _tcLog.Messages[prevMessages].L10NId,
                Is.EqualTo("TeamCollection.ConflictingCheckout")
            );
            TeamCollectionManager.ForceCurrentUserForTests(null);
        }

        [Test]
        public void HandleModifiedFile_CheckedOutToMe_ContentChangedRemotely_RaisesCheckedOutByNoneAndErrorMessage()
        {
            // Setup //
            // Simulate a book that was checked out and modified by me, but then we get a remote change
            // notification.
            const string bookFolderName = "My conflicting change book";
            var bookBuilder = new BookFolderBuilder()
                .WithRootFolder(_collectionFolder.FolderPath)
                .WithTitle(bookFolderName)
                .WithHtm("We will be simulating a remote change to this.")
                .Build();

            string bookFolderPath = bookBuilder.BuiltBookFolderPath;
            string bookPath = bookBuilder.BuiltBookHtmPath;

            _collection.PutBook(bookFolderPath);
            var pathToBookFileInRepo = _collection.GetPathToBookFileInRepo(bookFolderName);
            // Save the data we will eventually write back to the .bloom file to simulate the remote change.
            var remoteContent = RobustFile.ReadAllBytes(pathToBookFileInRepo);

            _collection.AttemptLock(bookFolderName);
            RobustFile.WriteAllText(bookPath, "Pretend this was the state when we checked it out.");
            _collection.PutBook(bookFolderPath);

            RobustFile.WriteAllText(
                bookPath,
                "This is a further change locally, not checked in anywhere"
            );

            // But now it's been changed remotely to the other state. (Ignore the fact that it was a previous local state;
            // that was just a trick to get a valid alternative state.)
            RobustFile.WriteAllBytes(pathToBookFileInRepo, remoteContent);

            var prevMessages = _tcLog.Messages.Count;

            // System Under Test //
            _collection.HandleModifiedFile(
                new BookRepoChangeEventArgs() { BookFileName = $"{bookFolderName}.bloom" }
            );

            // Verification
            var eventArgs = (BookStatusChangeEventArgs)_mockTcManager.Invocations[0].Arguments[0];
            Assert.That(eventArgs.CheckedOutByWhom, Is.EqualTo(CheckedOutBy.None));

            Assert.That(
                _tcLog.Messages[prevMessages].MessageType,
                Is.EqualTo(MessageAndMilestoneType.Error)
            );
            Assert.That(
                _tcLog.Messages[prevMessages].L10NId,
                Is.EqualTo("TeamCollection.EditedFileChangedRemotely")
            );
        }

        [Test]
        public void HandleBookRename_CheckedOutToMe_FixesStatusProperly()
        {
            // Setup //
            const string originalBookName = "Hello. Goodbye!";
            var bookBuilder = new BookFolderBuilder()
                .WithRootFolder(_collectionFolder.FolderPath)
                .WithTitle(originalBookName)
                .WithHtm("<html><body>This is just a dummy</body></html>")
                .Build();
            string bookFolderPath = bookBuilder.BuiltBookFolderPath;
            string htmlPath = bookBuilder.BuiltBookHtmPath;

            TeamCollectionManager.ForceCurrentUserForTests("steve@somewhere.org");
            _collection.PutBook(bookFolderPath);

            var locked = _collection.AttemptLock(originalBookName);

            Assert.That(locked, Is.True, "successfully checked out book to steve@somewhere.org");

            // SUT: rename changes status in local collection folder, but not in shared repo folder
            const string newBookName = "Testing is Fun. Sometimes";
            var newBookFolderPath = Path.Combine(_collectionFolder.FolderPath, newBookName);
            File.Move(htmlPath, Path.Combine(bookFolderPath, newBookName + ".htm"));
            Directory.Move(bookFolderPath, newBookFolderPath);

            _collection.HandleBookRename(originalBookName, newBookName);
            var newStatus = _collection.GetLocalStatus(newBookName);
            var repoStatus = _collection.GetStatus(newBookName);

            Assert.That(newStatus, Is.Not.Null, "local status of renamed book is not null");
            Assert.That(repoStatus, Is.Not.Null, "repo status of renamed book is not null");
            Assert.That(
                newStatus.checksum,
                Is.EqualTo(repoStatus.checksum),
                "checksums of local and remote match after rename"
            );
            Assert.That(
                newStatus.lockedBy,
                Is.EqualTo(repoStatus.lockedBy),
                "lockedBy of local and remote match after rename"
            );
            Assert.That(
                newStatus.lockedWhen,
                Is.EqualTo(repoStatus.lockedWhen),
                "lockedWhen of local and remote match after rename"
            );
            Assert.That(
                newStatus.lockedWhere,
                Is.EqualTo(repoStatus.lockedWhere),
                "lockedWhere of local and remote match after rename"
            );
            Assert.That(
                newStatus.oldName,
                Is.EqualTo(originalBookName),
                "local status has original name in oldName field after rename"
            );
            Assert.That(
                repoStatus.oldName,
                Is.Null,
                "repo status still has null oldName field after rename"
            );
        }

        [Test]
        public void HandleBookRename_CaseChangeOnly_WorksRight()
        {
            // Setup //
            const string originalBookName = "A new book";
            var bookBuilder = new BookFolderBuilder()
                .WithRootFolder(_collectionFolder.FolderPath)
                .WithTitle(originalBookName)
                .WithHtm("<html><body>This is just a dummy</body></html>")
                .Build();
            string bookFolderPath = bookBuilder.BuiltBookFolderPath;
            string htmlPath = bookBuilder.BuiltBookHtmPath;

            TeamCollectionManager.ForceCurrentUserForTests("steve@somewhere.org");
            _collection.PutBook(bookFolderPath);

            var locked = _collection.AttemptLock(originalBookName);

            Assert.That(locked, Is.True, "successfully checked out book to steve@somewhere.org");

            // SUT: rename changes status in local collection folder, but not in shared repo folder
            const string newBookName = "A New Book";
            var newBookFolderPath = Path.Combine(_collectionFolder.FolderPath, newBookName);
            File.Move(htmlPath, Path.Combine(bookFolderPath, newBookName + ".htm"));
            // renaming directory doesn't work when names are 'the same'
            var tempPath = Path.Combine(_collectionFolder.FolderPath, "tempxxyy");
            Directory.Move(bookFolderPath, tempPath);
            Directory.Move(tempPath, newBookFolderPath);

            _collection.HandleBookRename(originalBookName, newBookName);

            _collection.PutBook(newBookFolderPath, true);

            var newRepoPath = Path.Combine(
                _sharedFolder.FolderPath,
                "Books",
                newBookName + ".bloom"
            );
            // It should not have been deleted! This is a regression test for BL-10156.
            // The danger is that Windows considers the old and new names the same, so after
            // we move the file to the new name, if we go to delete the old name, we get rid of the new one.
            Assert.That(File.Exists(newRepoPath));

            // Did it get renamed?
            var matchingFiles = Directory
                .EnumerateFiles(
                    Path.Combine(_sharedFolder.FolderPath, "Books"),
                    newBookName + ".bloom"
                )
                .ToArray();
            Assert.That(
                matchingFiles[0],
                Is.EqualTo(Path.Combine(_sharedFolder.FolderPath, "Books", newBookName + ".bloom"))
            );

            var newStatus = _collection.GetLocalStatus(newBookName);
            var repoStatus = _collection.GetStatus(newBookName);

            Assert.That(newStatus, Is.Not.Null, "local status of renamed book is not null");
            Assert.That(repoStatus, Is.Not.Null, "repo status of renamed book is not null");
            Assert.That(
                newStatus.checksum,
                Is.EqualTo(repoStatus.checksum),
                "checksums of local and remote match after rename"
            );
            Assert.That(
                newStatus.lockedBy,
                Is.EqualTo(null),
                "lockedBy of local and remote match after rename"
            );
            Assert.That(
                newStatus.oldName,
                Is.Null,
                "local status has original name cleared after commit"
            );
        }

        [TestCase(null)]
        [TestCase("someone.else@nowhere.org")]
        public void HandleDeletedFile_NoConflictBook_DeletesAndRaisesCheckedOutByDeletedButNoMessage(
            string checkedOutTo
        )
        {
            // Simulate that a book was just deleted in the repo
            const string bookFolderName = "My book";
            var bookBuilder = new BookFolderBuilder()
                .WithRootFolder(_collectionFolder.FolderPath)
                .WithTitle(bookFolderName)
                .Build();
            string bookFolderPath = bookBuilder.BuiltBookFolderPath;

            var status = _collection.PutBook(bookFolderPath);
            if (checkedOutTo != null)
                _collection.AttemptLock(bookFolderName, checkedOutTo);
            _collection.DeleteBookFromRepo(bookFolderPath);

            var prevMessages = _tcLog.Messages.Count;
            var prevInvocations = _mockTcManager.Invocations.Count;

            // System Under Test //
            _collection.HandleDeletedRepoFile($"{bookFolderName}.bloom");

            // Verification
            // (This is a bit fragile, as it depends on how many times the method calls ANY function in the
            // mock TC manager, and in what order. Currently we call it once to ask for a book selection,
            // and then, (the call we're interested in) to raise book status changed.
            Assert.That(_mockTcManager.Invocations.Count, Is.EqualTo(prevInvocations + 2));
            var eventArgs = (BookStatusChangeEventArgs)
                _mockTcManager.Invocations[prevInvocations + 1].Arguments[0];
            Assert.That(eventArgs.CheckedOutByWhom, Is.EqualTo(CheckedOutBy.Deleted));
            Assert.That(_tcLog.Messages.Count, Is.EqualTo(prevMessages));
            Assert.That(
                Directory.Exists(bookFolderPath),
                Is.False,
                "The local book should have been deleted"
            );
        }

        [Test]
        public void DeleteBookFromRepo_CreatesTombstone()
        {
            const string bookFolderName = "Delete away";

            var bookBuilder = new BookFolderBuilder()
                .WithRootFolder(_collectionFolder.FolderPath)
                .WithTitle(bookFolderName)
                .Build();
            string bookFolderPath = bookBuilder.BuiltBookFolderPath;

            var status = _collection.PutBook(bookFolderPath);
            _collection.DeleteBookFromRepo(bookFolderPath);
            Assert.That(_collection.KnownToHaveBeenDeleted("Delete away"), Is.True);
        }

        [Test]
        public void HandleDeletedFile_BookNotDeleted_DoesNothing()
        {
            // Simulate that a book was reported as deleted in the repo, but actually, it's still there
            const string bookFolderName = "My book";
            var bookBuilder = new BookFolderBuilder()
                .WithRootFolder(_collectionFolder.FolderPath)
                .WithTitle(bookFolderName)
                .Build();
            string bookFolderPath = bookBuilder.BuiltBookFolderPath;

            var status = _collection.PutBook(bookFolderPath);

            var prevMessages = _tcLog.Messages.Count;
            var prevInvocations = _mockTcManager.Invocations.Count;

            // System Under Test: a spurious notification //
            _collection.HandleDeletedRepoFile($"{bookFolderName}.bloom");

            // Verification
            Assert.That(_mockTcManager.Invocations.Count, Is.EqualTo(prevInvocations));
            Assert.That(_tcLog.Messages.Count, Is.EqualTo(prevMessages));
            Assert.That(
                Directory.Exists(bookFolderPath),
                Is.True,
                "The local book should not have been deleted"
            );
        }

        [Test]
        public void HandleDeletedFile_BookDeletedButNoTombstone_DoesNothing()
        {
            // Simulate that a book is reported as deleted in the repo (and it is), but there is no tombstone
            const string bookFolderName = "My other book";
            var bookBuilder = new BookFolderBuilder()
                .WithRootFolder(_collectionFolder.FolderPath)
                .WithTitle(bookFolderName)
                .Build();
            string bookFolderPath = bookBuilder.BuiltBookFolderPath;

            var status = _collection.PutBook(bookFolderPath);

            // Delete but do NOT make tombstone
            _collection.DeleteBookFromRepo(bookFolderPath, false);

            var prevMessages = _tcLog.Messages.Count;
            var prevInvocations = _mockTcManager.Invocations.Count;

            // System Under Test: a valid notification, but one we want to ignore //
            _collection.HandleDeletedRepoFile($"{bookFolderName}.bloom");

            // Verification
            Assert.That(_mockTcManager.Invocations.Count, Is.EqualTo(prevInvocations));
            Assert.That(_tcLog.Messages.Count, Is.EqualTo(prevMessages));
            Assert.That(
                Directory.Exists(bookFolderPath),
                Is.True,
                "The local book should not have been deleted"
            );
        }

        [Test]
        public void HandleDeletedFile_ConflictBookDeleted_LogsProblem()
        {
            // Simulate that a book which is checked out here was just deleted in the repo
            const string bookFolderName = "My book";
            var bookBuilder = new BookFolderBuilder()
                .WithRootFolder(_collectionFolder.FolderPath)
                .WithTitle(bookFolderName)
                .Build();
            string bookFolderPath = bookBuilder.BuiltBookFolderPath;

            var status = _collection.PutBook(bookFolderPath);
            _collection.AttemptLock(bookFolderName);
            // But, in spite of that, it's gone!
            _collection.DeleteBookFromRepo(bookFolderPath);

            var prevMessages = _tcLog.Messages.Count;
            var prevInvocations = _mockTcManager.Invocations.Count;

            // System Under Test //
            _collection.HandleDeletedRepoFile($"{bookFolderName}.bloom");

            // Verification
            Assert.That(_mockTcManager.Invocations.Count, Is.EqualTo(prevInvocations));
            Assert.That(_tcLog.Messages.Count, Is.EqualTo(prevMessages + 1));
            Assert.That(
                _tcLog.Messages[prevMessages].MessageType,
                Is.EqualTo(MessageAndMilestoneType.ErrorNoReload)
            );
            Assert.That(
                Directory.Exists(bookFolderPath),
                Is.True,
                "The local book should not have been deleted"
            );
        }

        [Test]
        public void HandleDeletedFile_BookSelected_LogsProblem()
        {
            // Simulate that a book which is currently selected was just deleted in the repo
            const string bookFolderName = "My book";
            var bookBuilder = new BookFolderBuilder()
                .WithRootFolder(_collectionFolder.FolderPath)
                .WithTitle(bookFolderName)
                .Build();
            string bookFolderPath = bookBuilder.BuiltBookFolderPath;

            var status = _collection.PutBook(bookFolderPath);
            // But, although we have all the status to indicate it is in the repo, it's gone!
            _collection.DeleteBookFromRepo(bookFolderPath);

            // But it can't go away locally...it's the selected book!
            var book = new Mock<Bloom.Book.Book>();
            book.Setup(m => m.FolderPath).Returns(bookFolderPath);
            var selection = new Mock<BookSelection>();
            selection.Setup(m => m.CurrentSelection).Returns(book.Object);
            _mockTcManager.Setup(m => m.BookSelection).Returns(selection.Object);

            var prevMessages = _tcLog.Messages.Count;
            var prevInvocations = _mockTcManager.Invocations.Count;

            // System Under Test //
            _collection.HandleDeletedRepoFile($"{bookFolderName}.bloom");

            // Verification
            Assert.That(_tcLog.Messages.Count, Is.EqualTo(prevMessages + 1));
            Assert.That(
                _tcLog.Messages[prevMessages].MessageType,
                Is.EqualTo(MessageAndMilestoneType.Error)
            );
            Assert.That(
                Directory.Exists(bookFolderPath),
                Is.True,
                "The local book should not have been deleted"
            );
        }

        [TestCase("", null)]
        [TestCase(
            @"{""a11y_NoEssentialInfoByColor"":false,""a11y_NoTextIncludedInAnyImages"":false,""bookInstanceId"":""d62ce553-5ad3-4857-a33a-6d4092840018"",""suitableForMakingShells"":false,""suitableForMakingTemplates"":false,""suitableForVernacularLibrary"":true,""bloomdVersion"":1,""experimental"":false,""brandingProjectName"":""Local-Community"",""nameLocked"":false,""folio"":false,""isRtl"":false,""title"":""Comic drag test"",""allTitles"":""{\""en\"":\""Comic drag test\""}"",""originalTitle"":""Comic drag test"",""baseUrl"":null,""bookOrder"":null,""isbn"":"""",""bookLineage"":""056B6F11-4A6C-4942-B2BC-8861E62B03B3"",""downloadSource"":null,""license"":null,""formatVersion"":""2.1"",""licenseNotes"":null,""copyright"":null,""credits"":"""",""tags"":[],""pageCount"":0,""languages"":[],""langPointers"":null,""summary"":null,""allowUploadingToBloomLibrary"":true,""bookletMakingIsAppropriate"":true,""country"":""United States"",""province"":"""",""district"":"""",""uploader"":null,""tools"":[{""name"":""overlay"",""enabled"":true,""state"":null}],""currentTool"":""overlayTool"",""toolboxIsOpen"":true,""author"":null,""publisher"":null,""originalPublisher"":null,""subjects"":null,""hazards"":null,""a11yFeatures"":null,""a11yLevel"":null,""a11yCertifier"":null,""readingLevelDescription"":null,""typicalAgeRange"":null,""features"":[""comic""],""page-number-style"":""Decimal"",""language-display-names"":{""en"":""English"",""de"":""German, Standard""},""internetLimits"":null,""use-original-copyright"":false,""imported-book-source-url"":null,""phashOfFirstContentImage"":null}",
            "d62ce553-5ad3-4857-a33a-6d4092840018"
        )] // a real metadata
        [TestCase("This is absolute junk, with nothing that looks like a bookInstanceId", null)]
        // Here, as in one real case, bloomVersion is unexpectedly a float. We will fall back to looking for the ID by Regex
        [TestCase(
            @"{""bookInstanceId"":""d62ce553-5ad3-4857-a33a-6d4092840019"",""suitableForMakingShells"":false,""bloomdVersion"":1.0}",
            "d62ce553-5ad3-4857-a33a-6d4092840019"
        )]
        [TestCase(
            @"This is a corrupted mess, but""bookInstanceId"":""d62ce553-5ad3-4857-a33a-6d4092840019"" it does have something that looks like an ID}",
            "d62ce553-5ad3-4857-a33a-6d4092840019"
        )]
        public void GetIdFrom_YieldsResult(string metadata, string id)
        {
            Assert.That(
                Bloom.TeamCollection.TeamCollection.GetIdFrom(metadata, "fake"),
                Is.EqualTo(id)
            );
        }
    }
}
