using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.TeamCollection;
using BloomTemp;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.TeamCollection
{
	public class SyncAtStartupTests
	{
		private TemporaryFolder _repoFolder;
		protected TemporaryFolder _collectionFolder;
		protected FolderTeamCollection _collection;
		private string _originalUser;
		private string _checkMeOutOriginalChecksum;
		protected List<string> _syncMessages;
		protected ProgressSpy _progressSpy;

		[OneTimeSetUp]
		public void OneTimeSetup()
		{
			_repoFolder = new TemporaryFolder("SyncAtStartup_Repo");
			_collectionFolder = new TemporaryFolder("SyncAtStartup_Local");
			FolderTeamCollection.CreateTeamCollectionSettingsFile(_collectionFolder.FolderPath, _repoFolder.FolderPath);
			_collection = new FolderTeamCollection(_collectionFolder.FolderPath, _repoFolder.FolderPath);
			_originalUser = Bloom.TeamCollection.TeamCollectionManager.CurrentUser;
			if (string.IsNullOrEmpty(_originalUser))
			{
				SIL.Windows.Forms.Registration.Registration.Default.Email = "test@somewhere.org";
			}

			// Simulate a book that was once shared, but has been deleted from the repo folder.
			MakeBook("Should be deleted", "This should be deleted as it has local status but is not shared", true);
			var delPath = Path.Combine(_repoFolder.FolderPath, "Books", "Should be deleted.bloom");
			RobustFile.Delete(delPath);

			// Simulate a book newly created locally. Not in repo, but should not be deleted.
			MakeBook("New book", "This should survive as it has no local status", false);

			// Simulate a book that needs nothing done to it. It's the same locally and on the repo.
			MakeBook("Keep me", "This needs nothing done to it");

			// Simulate a book that is checked out locally to the current user, but the file has
			// been deleted on the repo.
			MakeBook("Keep me too", "This also needs nothing done", false);
			_collection.WriteLocalStatus("Keep me too", new BookStatus().WithLockedBy("test@somewhere.org"));

			// Simlulate a book that is only in the team repo
			MakeBook("Add me", "Fetch to local");
			var delPathAddMe = Path.Combine(_collectionFolder.FolderPath, "Add me");
			SIL.IO.RobustIO.DeleteDirectoryAndContents(delPathAddMe);

			// Simulate a book that is not checked out locally and has been modified elsewhere
			MakeBook("Update me", "Needs to be become this locally");
			UpdateLocalBook("Update me", "This is supposed to be an older value, not edited locally");

			// Simulate a book that is checked out locally but not in the repo, and where the saved local
			// checksum equals the repo checksum, and it is not checked out in the repo. This would
			// typically indicate that someone remote forced a checkout, perhaps while this user was
			// offline, but checked in again without making changes.
			// Also pretend it has been modified locally.
			// Test result: collection is updated to indicate the local checkout. Local changes are not lost.
			MakeBook("Check me out", "Local and remote checksums correspond to this");
			UpdateLocalBook("Check me out", "This is supposed to be a newer value from local editing", false);
			var oldLocalStatus = _collection.GetLocalStatus("Check me out");
			var newLocalStatus = oldLocalStatus.WithLockedBy(Bloom.TeamCollection.TeamCollectionManager.CurrentUser);
			_checkMeOutOriginalChecksum = oldLocalStatus.checksum;
			_collection.WriteLocalStatus("Check me out", newLocalStatus);

			// Simulate a book that appears newly-created locally (no local status) but is also in the
			// repo. This would indicate two people coincidentally creating a book with the same name.
			// Test result: the local book should get renamed (both folder and htm).
			// When merging while joining a new TC, this case is treated as a conflict and the
			// local book is moved to Lost and Found.
			MakeBook("Rename local", "This content is on the server");
			_collection.AttemptLock("Rename local", "fred@somewhere.org");
			UpdateLocalBook("Rename local", "This is a new book created independently");
			var statusFilePath = _collection.GetStatusFilePath("Rename local", _collectionFolder.FolderPath);
			RobustFile.Delete(statusFilePath);

			// Simulate a book that is checked out locally but also checked out, to a different user
			// or machine, on the repo. This would indicate some sort of manual intervention, perhaps
			// while this user was long offline. The book has not been modified locally, but the local
			// status is out of date.
			// Test result: local status is updated to reflect the remote checkout, book content updated to repo.
			MakeBook("Update and undo checkout", "This content is everywhere");
			_collection.AttemptLock("Update and undo checkout", "fred@somewhere.org");
			_collection.WriteLocalStatus("Update and undo checkout", _collection.GetStatus("Update and undo checkout").WithLockedBy(Bloom.TeamCollection.TeamCollectionManager.CurrentUser));

			// Simulate a book that is checked out locally and not on the server, but the repo and (old)
			// local checksums are different. The book has not been edited locally.
			// Test result: book is updated to match repo. Local and remote status should match...review: which wins?
			MakeBook("Update and checkout", "This content is on the server");
			UpdateLocalBook("Update and checkout", "This simulates older content changed remotely but not locally");
			_collection.WriteLocalStatus("Update and checkout", _collection.GetLocalStatus("Update and checkout").WithLockedBy(Bloom.TeamCollection.TeamCollectionManager.CurrentUser));

			// Simulate a book that is checked out and modified locally, but has also been modified
			// remotely.
			// Test result: current local state is saved in lost-and-found. Repo version of book and state
			// copied to local. Warning to user.
			MakeBook("Update content and status and warn", "This simulates new content on server");
			_collection.AttemptLock("Update content and status and warn", "fred@somewhere.org");
			UpdateLocalBook("Update content and status and warn", "This is supposed to be the newest value from local editing");
			var newStatus = _collection.GetStatus("Update content and status and warn").WithLockedBy(Bloom.TeamCollection.TeamCollectionManager.CurrentUser)
				.WithChecksum("different from either");
			_collection.WriteLocalStatus("Update content and status and warn", newStatus);

			// Simulate a book that is checked out and modified locally, but is also checked out by another
			// user or machine in the repo. It has not (yet) been modified remotely.
			// Test result: current local state is saved in lost-and-found. Repo version of book and state
			// copied to local. Warning to user.
			MakeBook("Update content and status and warn2", "This simulates new content on server");
			_collection.AttemptLock("Update content and status and warn2", "fred@somewhere.org");
			UpdateLocalBook("Update content and status and warn2", "This is supposed to be the newest value from local editing", false);
			newStatus = _collection.GetStatus("Update content and status and warn2").WithLockedBy(Bloom.TeamCollection.TeamCollectionManager.CurrentUser);
			_collection.WriteLocalStatus("Update content and status and warn2", newStatus);

			// Simulate a book which has no local status, but for which the computed checksum matches
			// the repo one. This could happen if a user obtained the same book independently,
			// or during initial merging of a local and team collection, where much of the material
			// was previously duplicated.
			// Test result: status is copied to local
			MakeBook("copy status", "Same content in both places");
			_collection.AttemptLock("copy status", "fred@somewhere.org");
			statusFilePath = _collection.GetStatusFilePath("copy status", _collectionFolder.FolderPath);
			RobustFile.Delete(statusFilePath);

			_progressSpy = new ProgressSpy();

			// sut for the whole suite!
			_syncMessages = _collection.SyncAtStartup(_progressSpy, FirstTimeJoin());
		}

		protected virtual bool FirstTimeJoin()
		{
			return false;
		}

		[OneTimeTearDown]
		public void OneTimeTearDown()
		{
			_collectionFolder.Dispose();
			_repoFolder.Dispose();
			SIL.Windows.Forms.Registration.Registration.Default.Email = _originalUser;
		}

		/// <summary>
		/// The specific error messages we expect each have their own test. To make sure we don't get any
		/// additional, unexpected ones we check the count here.
		/// </summary>
		[Test]
		public virtual void SyncAtStartup_ProducesNoUnexpectedMessages()
		{
			Assert.That(_syncMessages, Has.Count.EqualTo(2), "Unexpected number of error messages produced. Did you mean to add one?");
			Assert.That(_progressSpy.Warnings, Has.Count.EqualTo(2), "Unexpected number of progress warnings produced. Did you mean to add one?");
			Assert.That(_progressSpy.Errors, Has.Count.EqualTo(0), "Unexpected number of progress errors produced.");
			Assert.That(_progressSpy.ProgressMessages, Has.Count.EqualTo(5), "Unexpected number of progress messages produced. Did you mean to add one?");
		}

		[Test]
		public void SyncAtStartup_BookNeedsNothingDone_Survives()
		{
			Assert.That(Directory.Exists(Path.Combine(_collectionFolder.FolderPath, "Keep me")), Is.True);
		}

		[Test]
		public void SyncAtStartup_BookLockedLocally_MissingFromServer_Survives()
		{
			Assert.That(Directory.Exists(Path.Combine(_collectionFolder.FolderPath, "Keep me too")), Is.True);
		}

		[Test]
		public virtual void SyncAtStartupBooksWithSameNameSyncAtStartupBooksWithSameNameIndependentlyCreatedRenamesLocalButPutInLfOnJoin()
		{
			Assert.That(Directory.Exists(Path.Combine(_collectionFolder.FolderPath, "Rename local1")), Is.True);
			AssertLocalContent("Rename local1", "This is a new book created independently");
			AssertLocalContent("Rename local", "This content is on the server");
			Assert.That(_collection.GetLocalStatus("Rename local").lockedBy, Is.EqualTo("fred@somewhere.org"));
			Assert.That(_progressSpy.ProgressMessages, Contains.Item("Renaming the local book 'Rename local' because there is a new one with the same name from the Team Collection"));
		}

		[Test]
		public virtual void SyncAtStartup_BookDeletedRemotely_GetsDeletedLocally_UnlessJoin()
		{
			Assert.That(Directory.Exists(Path.Combine(_collectionFolder.FolderPath, "Should be deleted")), Is.False);
			Assert.That(_progressSpy.ProgressMessages, Contains.Item("Deleting 'Should be deleted' from local folder as it is no longer in the Team Collection"));
		}

		[Test]
		public void SyncAtStartup_BookCreatedLocallyNotCheckedIn_Survives()
		{
			Assert.That(Directory.Exists(Path.Combine(_collectionFolder.FolderPath, "New book")), Is.True);
		}

		[Test]
		public virtual void SyncAtStartup_BookCreatedLocallyNotCheckedIn_CopiedLocalOnlyOnJoin()
		{
			Assert.That(File.Exists(Path.Combine(_repoFolder.FolderPath, "Books" ,"New book.bloom")), Is.EqualTo(FirstTimeJoin()));
		}

		[Test]
		public void SyncAtStartup_SameBookLocallyAndShared_NoLocalStatus_KeepsBookAddsStatus()
		{
			// We don't want to move the book to a new folder.
			Assert.That(Directory.EnumerateDirectories(_collectionFolder.FolderPath, "copy status*").Count(), Is.EqualTo(1));
			Assert.That(_collection.GetLocalStatus("copy status").lockedBy, Is.EqualTo("fred@somewhere.org"));
		}

		[Test]
		public void SyncAtStartup_BookCreatedRemotely_CopiedLocal()
		{
			AssertLocalContent("Add me", "Fetch to local");
			Assert.That(_progressSpy.ProgressMessages, Contains.Item("Fetching a new book 'Add me' from the Team Collection"));
		}

		[Test]
		public void SyncAtStartup_BookNotChangedButCheckoutUserConflicts_RemoteWins()
		{
			Assert.That(_collection.GetLocalStatus("Update and undo checkout").lockedBy, Is.EqualTo("fred@somewhere.org"));
		}

		[Test]
		public void SyncAtStartup_BookCheckedOutLocallyChangedRemotelyNotChangedLocally_RemoteContentAndLocalCheckoutWin()
		{
			AssertLocalContent("Update and checkout", "This content is on the server");
			Assert.That(_collection.GetLocalStatus("Update and checkout").lockedBy, Is.EqualTo(Bloom.TeamCollection.TeamCollectionManager.CurrentUser));
			Assert.That(_progressSpy.ProgressMessages, Contains.Item("Updating 'Update and checkout' to match the Team Collection"));
		}

		[Test]
		public void SyncAtStartup_ConflictingEditsAndCheckout_RemoteWinsWithWarning()
		{
			AssertLocalContent("Update content and status and warn", "This simulates new content on server");
			Assert.That(_collection.GetLocalStatus("Update content and status and warn").lockedBy, Is.EqualTo("fred@somewhere.org"));
			var expectedWarning = "The book 'Update content and status and warn', which you have checked out and edited, was modified in the team collection by someone else. Your changes have been overwritten, but are saved to Lost-and-found.";
			Assert.That(_syncMessages, Contains.Item(expectedWarning));
			Assert.That(_progressSpy.Warnings, Contains.Item(expectedWarning));
			AssertLostAndFound("Update content and status and warn");
		}

		[Test]
		public void SyncAtStartup_ConflictingCheckoutAndLocalEdit_RemoteWinsWithWarning()
		{
			AssertLocalContent("Update content and status and warn2", "This simulates new content on server");
			Assert.That(_collection.GetLocalStatus("Update content and status and warn2").lockedBy, Is.EqualTo("fred@somewhere.org"));
			var expectedWarning = "The book 'Update content and status and warn2', which you have checked out and edited, is checked out to someone else in the team collection. Your changes have been overwritten, but are saved to Lost-and-found.";
			Assert.That(_syncMessages, Contains.Item(expectedWarning));
			Assert.That(_progressSpy.Warnings, Contains.Item(expectedWarning));
			AssertLostAndFound("Update content and status and warn2");
		}

		[Test]
		public void SyncAtStartup_RepoNotShowingCheckoutButNoRemoteChanges_Checkout()
		{
			AssertLocalContent("Check me out", "This is supposed to be a newer value from local editing");
			var localStatus = _collection.GetLocalStatus("Check me out");
			Assert.That(localStatus.checksum, Is.EqualTo(_checkMeOutOriginalChecksum));
			Assert.That(localStatus.lockedBy, Is.EqualTo(Bloom.TeamCollection.TeamCollectionManager.CurrentUser));
			var repoStatus = _collection.GetStatus("Check me out");
			Assert.That(localStatus.checksum, Is.EqualTo(_checkMeOutOriginalChecksum));
			Assert.That(localStatus.lockedBy, Is.EqualTo(Bloom.TeamCollection.TeamCollectionManager.CurrentUser));
		}

		public void AssertLocalContent(string bookName, string expectedContent)
		{

			var bookFolder = Path.Combine(_collectionFolder.FolderPath, bookName);
			Assert.That(Directory.Exists(bookFolder), Is.True);
			var bookHtmlPath = Path.Combine(bookFolder, Path.ChangeExtension(bookName, "htm"));
			Assert.That(File.Exists(bookHtmlPath));
			Assert.That(File.ReadAllText(bookHtmlPath, Encoding.UTF8), Contains.Substring(expectedContent));
		}

		public void AssertLostAndFound(string bookName)
		{
			var bookPath = Path.Combine(_repoFolder.FolderPath, "Lost and Found", Path.ChangeExtension(bookName, "bloom"));
			Assert.That(File.Exists(bookPath));
			// We could try to check the content (from an extra argument). However, there's no plausible path
			// for the book to get to lost-and-found except by PutBook, and we have other tests to check
			// that PutBook saves content properly.
		}

		[Test]
		public void SyncAtStartup_BookModifiedRemotely_CopiedLocal()
		{
			var updateMePath = Path.Combine(_collectionFolder.FolderPath, "Update me");
			Assert.That(Directory.Exists(updateMePath), Is.True);
			var updateMeBookPath = Path.Combine(updateMePath, "Update me.htm");
			Assert.That(File.Exists(updateMeBookPath));
			Assert.That(File.ReadAllText(updateMeBookPath, Encoding.UTF8), Contains.Substring("Needs to be become this locally"));
			Assert.That(_progressSpy.ProgressMessages, Contains.Item("Updating 'Update me' to match the Team Collection"));
		}

		void MakeBook(string name, string content, bool toRepo = true, bool onlyRepo = false)
		{
			var folderPath = MakeFakeBook(_collectionFolder.FolderPath, name, content);
			if (toRepo)
				_collection.PutBook(folderPath);
			if (onlyRepo)
				SIL.IO.RobustIO.DeleteDirectoryAndContents(folderPath);
		}

		// Make a very trivial fake book. Not nearly good enough to make a Book object from,
		// but enough for most purposes of testing TeamCollection.
		public static string MakeFakeBook(string collectionFolder, string name, string content)
		{
			var folderPath = Path.Combine(collectionFolder, name);
			var bookPath = Path.Combine(folderPath, Path.ChangeExtension(name, "htm"));
			Directory.CreateDirectory(folderPath);
			RobustFile.WriteAllText(bookPath, "<html><body>" + content + "</body></html>");
			return folderPath;
		}

		void UpdateLocalBook(string name, string content, bool updateChecksum = true)
		{
			var folderPath = Path.Combine(_collectionFolder.FolderPath, name);
			var bookPath = Path.Combine(folderPath, Path.ChangeExtension(name, "htm"));
			RobustFile.WriteAllText(bookPath, "<html><body>" + content + "</body></html>");
			if (updateChecksum)
			{
				var status = _collection.GetLocalStatus(name);
				status.checksum = Bloom.TeamCollection.TeamCollection.MakeChecksum(folderPath);
				_collection.WriteLocalStatus(name, status);
			}
		}
	}

	/// <summary>
	/// This does much the same, but expected results are different in a few cases when joining a new
	/// TC for the first time.
	/// </summary>
	[TestFixture]
	public class SyncAtStartupNewTests : SyncAtStartupTests
	{
		// This is what makes the behavior different.
		protected override bool FirstTimeJoin()
		{
			return true;
		}

		[Test]
		public override void SyncAtStartupBooksWithSameNameSyncAtStartupBooksWithSameNameIndependentlyCreatedRenamesLocalButPutInLfOnJoin()
		{
			AssertLocalContent("Rename local", "This content is on the server");
			Assert.That(_collection.GetLocalStatus("Rename local").lockedBy, Is.EqualTo("fred@somewhere.org"));
			Assert.That(_progressSpy.Warnings,
				Contains.Item("Found different versions of 'Rename local' in both collections. The team version has been copied to your local collection, and the old local version to Lost and Found"));
			AssertLostAndFound("Rename local");
		}

		[Test]
		public override void SyncAtStartup_ProducesNoUnexpectedMessages()
		{
			Assert.That(_syncMessages, Has.Count.EqualTo(3), "Unexpected number of error messages produced. Did you mean to add one?");
			Assert.That(_progressSpy.Warnings, Has.Count.EqualTo(3), "Unexpected number of progress warnings produced. Did you mean to add one?");
			Assert.That(_progressSpy.Errors, Has.Count.EqualTo(0), "Unexpected number of progress errors produced.");
			Assert.That(_progressSpy.ProgressMessages, Has.Count.EqualTo(3), "Unexpected number of progress messages produced. Did you mean to add one?");
		}

		public override void SyncAtStartup_BookDeletedRemotely_GetsDeletedLocally_UnlessJoin()
		{
			Assert.That(Directory.Exists(Path.Combine(_collectionFolder.FolderPath, "Should be deleted")), Is.True);
		}
	}
}
