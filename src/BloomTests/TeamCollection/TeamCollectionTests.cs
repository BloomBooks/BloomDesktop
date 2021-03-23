using System;
using System.IO;
using System.Linq;
using Bloom.TeamCollection;
using BloomTemp;
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
			_tcLog = new TeamCollectionMessageLog(TeamCollectionManager.GetTcLogPathFromLcPath(_collectionFolder.FolderPath));
			FolderTeamCollection.CreateTeamCollectionSettingsFile(_collectionFolder.FolderPath,
				_sharedFolder.FolderPath);

			_mockTcManager = new Mock<ITeamCollectionManager>();
			_collection = new FolderTeamCollection(_mockTcManager.Object, _collectionFolder.FolderPath, _sharedFolder.FolderPath, _tcLog);
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
			var bookFolderPath = Path.Combine(_collectionFolder.FolderPath, "My new book");
			Directory.CreateDirectory(bookFolderPath);
			var bookPath = Path.Combine(bookFolderPath, "My new book.htm");
			RobustFile.WriteAllText(bookPath, "This is just a dummy");
			_collection.PutBook(bookFolderPath);
			SIL.IO.RobustIO.DeleteDirectoryAndContents(bookFolderPath);
			var prevMessages = _tcLog.Messages.Count;

			// SUT: We're mainly testing HandleNewbook, but it's convenient to check that this method calls it properly.
			_collection.QueuePendingBookChange(new NewBookEventArgs() { BookFileName = "My new book.bloom" });
			_collection.HandleRemoteBookChangesOnIdle(null, new EventArgs());


			Assert.That(_tcLog.Messages[prevMessages].MessageType, Is.EqualTo(MessageAndMilestoneType.NewStuff));
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
			var bookFolderPath = Path.Combine(_collectionFolder.FolderPath, bookFolderName);
			Directory.CreateDirectory(bookFolderPath);
			var bookPath = Path.Combine(bookFolderPath, "My book.htm");
			RobustFile.WriteAllText(bookPath, "This is just a dummy");
			var status = _collection.PutBook(bookFolderPath);
			// pretending we had nothing local before the change.
			RobustIO.DeleteDirectory(bookFolderPath, true);

			// Anticipate verification
			var prevMessages = _tcLog.Messages.Count;
			_mockTcManager.Setup(m => m.RaiseBookStatusChanged(It.IsAny<BookStatusChangeEventArgs>()))
				.Throws(new ArgumentException("RaiseBookStatus should not be called"));

			// System Under Test //
			_collection.HandleModifiedFile(new BookRepoChangeEventArgs() { BookFileName = $"{bookFolderName}.bloom" });

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
			var bookFolderPath = Path.Combine(_collectionFolder.FolderPath, bookFolderName);
			Directory.CreateDirectory(bookFolderPath);
			var bookPath = Path.Combine(bookFolderPath, "My book.htm");
			RobustFile.WriteAllText(bookPath, "This is just a dummy");

			var status = _collection.PutBook(bookFolderPath);
			// pretending this is what it was before the change.
			_collection.WriteLocalStatus(bookFolderName, status.WithLockedBy("fred@somewhere.org"));
			var prevMessages = _tcLog.Messages.Count;

			// System Under Test //
			_collection.HandleModifiedFile(new BookRepoChangeEventArgs() { BookFileName = $"{bookFolderName}.bloom" } );

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
			var bookFolderPath = Path.Combine(_collectionFolder.FolderPath, bookFolderName);
			Directory.CreateDirectory(bookFolderPath);
			var bookPath = Path.Combine(bookFolderPath, "My book.htm");
			RobustFile.WriteAllText(bookPath, "This is pretending to be new content from remote");

			var status = _collection.PutBook(bookFolderPath);
			RobustFile.WriteAllText(bookPath, "This is pretending to be old content");
			// pretending this is what it was before the change.
			_collection.WriteLocalStatus(bookFolderName, status.WithChecksum(Bloom.TeamCollection.TeamCollection.MakeChecksum(bookFolderPath)));
			var prevMessages = _tcLog.Messages.Count;

			// System Under Test //
			_collection.HandleModifiedFile(new BookRepoChangeEventArgs() { BookFileName = $"{bookFolderName}.bloom" });

			// Verification
			var eventArgs = (BookStatusChangeEventArgs)_mockTcManager.Invocations[0].Arguments[0];
			Assert.That(eventArgs.CheckedOutByWhom, Is.EqualTo(CheckedOutBy.None));

			Assert.That(_tcLog.Messages[prevMessages].MessageType, Is.EqualTo(MessageAndMilestoneType.NewStuff));
		}

		[Test]
		public void HandleModifiedFile_NoConflictBookCheckedOutRemotely_RaisesCheckedOutByOtherButNoNewStuffMessage()
		{
			// Setup //
			// Simulate (sort of) that a book was just overwritten with the following new contents,
			// including that book.status indicates a remote checkout
			const string bookFolderName = "My other book";
			var bookFolderPath = Path.Combine(_collectionFolder.FolderPath, bookFolderName);
			Directory.CreateDirectory(bookFolderPath);
			var bookPath = Path.Combine(bookFolderPath, "My other book.htm");
			RobustFile.WriteAllText(bookPath, "This is just a dummy");

			_collection.PutBook(bookFolderPath);
			_collection.AttemptLock("My other book", "nancy@somewhere.com");
			// Enhance: to make it more realistic, we could write a not-checked-out-here local status,
			// but it's not necessary for producing the effects we want to test here.
			var prevMessages = _tcLog.Messages.Count;

			// System Under Test //
			_collection.HandleModifiedFile(new BookRepoChangeEventArgs() { BookFileName = $"{bookFolderName}.bloom" });

			// Verification
			var eventArgs = (BookStatusChangeEventArgs)_mockTcManager.Invocations.Last().Arguments[0];
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
			var bookFolderPath = Path.Combine(_collectionFolder.FolderPath, bookFolderName);
			Directory.CreateDirectory(bookFolderPath);
			var bookPath = Path.Combine(bookFolderPath, "My conflict book.htm");
			RobustFile.WriteAllText(bookPath, "This is just a dummy");

			TeamCollectionManager.ForceCurrentUserForTests("me@somewhere.org");

			_collection.PutBook(bookFolderPath);
			// Temporarily, it looks locked by Nancy in both places.
			_collection.AttemptLock("My conflict book", "nancy@somewhere.com");
			var status = _collection.GetStatus("My conflict book").WithLockedBy(TeamCollectionManager.CurrentUser);
			// Now it is locally checked out to me. (The state changes are in the opposite order to what
			// we're trying to simulate, because we don't have an easy way to change remote checkout status without
			// changing local status to match at the same time.)
			_collection.WriteLocalStatus("My conflict book", status);
			var prevMessages = _tcLog.Messages.Count;

			// System Under Test...basically HandleModifiedFile, but this is a convenient place to
			// make sure we take the right path through this calling method.
			_collection.QueuePendingBookChange(
				new BookRepoChangeEventArgs() {BookFileName = $"{bookFolderName}.bloom"});
			_collection.HandleRemoteBookChangesOnIdle(null, new EventArgs());

			// Verification
			var eventArgs = (BookStatusChangeEventArgs)_mockTcManager.Invocations.Last().Arguments[0];
			Assert.That(eventArgs.CheckedOutByWhom, Is.EqualTo(CheckedOutBy.Other));

			Assert.That(_tcLog.Messages[prevMessages].MessageType, Is.EqualTo(MessageAndMilestoneType.Error));
			Assert.That(_tcLog.Messages[prevMessages].L10NId, Is.EqualTo("TeamCollection.ConflictingCheckout"));
			TeamCollectionManager.ForceCurrentUserForTests(null);
		}

		[Test]
		public void HandleModifiedFile_CheckedOutToMe_ContentChangedRemotely_RaisesCheckedOutByNoneAndErrorMessage()
		{
			// Setup //
			// Simulate a book that was checked out and modified by me, but then we get a remote change
			// notification.
			const string bookFolderName = "My conflicting change book";
			var bookFolderPath = Path.Combine(_collectionFolder.FolderPath, bookFolderName);
			Directory.CreateDirectory(bookFolderPath);
			var bookPath = Path.Combine(bookFolderPath, bookFolderName + ".htm");
			RobustFile.WriteAllText(bookPath, "We will be simulating a remote change to this.");

			_collection.PutBook(bookFolderPath);
			var pathToBookFileInRepo = _collection.GetPathToBookFileInRepo(bookFolderName);
			// Save the data we will eventually write back to the .bloom file to simulate the remote change.
			var remoteContent = RobustFile.ReadAllBytes(pathToBookFileInRepo);

			_collection.AttemptLock(bookFolderName);
			RobustFile.WriteAllText(bookPath, "Pretend this was the state when we checked it out.");
			_collection.PutBook(bookFolderPath);

			RobustFile.WriteAllText(bookPath, "This is a further change locally, not checked in anywhere");

			// But now it's been changed remotely to the other state. (Ignore the fact that it was a previous local state;
			// that was just a trick to get a valid alternative state.)
			RobustFile.WriteAllBytes(pathToBookFileInRepo, remoteContent);

			var prevMessages = _tcLog.Messages.Count;

			// System Under Test //
			_collection.HandleModifiedFile(new BookRepoChangeEventArgs() { BookFileName = $"{bookFolderName}.bloom" });

			// Verification
			var eventArgs = (BookStatusChangeEventArgs)_mockTcManager.Invocations[0].Arguments[0];
			Assert.That(eventArgs.CheckedOutByWhom, Is.EqualTo(CheckedOutBy.None));

			Assert.That(_tcLog.Messages[prevMessages].MessageType, Is.EqualTo(MessageAndMilestoneType.Error));
			Assert.That(_tcLog.Messages[prevMessages].L10NId, Is.EqualTo("TeamCollection.EditedFileChangedRemotely"));
		}

		[Test]
		public void HandleBookRename_CheckedOutToMe_FixesStatusProperly()
		{
			// Setup //
			const string originalBookName = "Hello. Goodbye!";
			var bookFolderPath = Path.Combine(_collectionFolder.FolderPath, originalBookName);
			Directory.CreateDirectory(bookFolderPath);
			var htmlPath = Path.Combine(bookFolderPath, originalBookName + ".htm");
			RobustFile.WriteAllText(htmlPath, "<html><body>This is just a dummy</body></html>");
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
			Assert.That(newStatus.checksum, Is.EqualTo(repoStatus.checksum), "checksums of local and remote match after rename");
			Assert.That(newStatus.lockedBy, Is.EqualTo(repoStatus.lockedBy), "lockedBy of local and remote match after rename");
			Assert.That(newStatus.lockedWhen, Is.EqualTo(repoStatus.lockedWhen), "lockedWhen of local and remote match after rename");
			Assert.That(newStatus.lockedWhere, Is.EqualTo(repoStatus.lockedWhere), "lockedWhere of local and remote match after rename");
			Assert.That(newStatus.oldName, Is.EqualTo(originalBookName), "local status has original name in oldName field after rename");
			Assert.That(repoStatus.oldName, Is.Null, "repo status still has null oldName field after rename");
		}
	}
}
