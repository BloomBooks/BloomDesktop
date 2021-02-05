using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Bloom.TeamCollection;
using BloomTemp;
using NUnit.Framework;
using SIL.IO;
using Directory = System.IO.Directory;
using File = System.IO.File;

namespace BloomTests.TeamCollection
{
	// While this makes considerable use of FolderTeamRepo, the tests here are focused on the code in the TeamRepo class.
	// Some of the code in TeamRepo is more easily tested by methods in FolderTeamRepoTests. Most of the tests
	// here focus on SyncAtStartup.
	public class TeamRepoTests
	{
		private TemporaryFolder _sharedFolder;
		private TemporaryFolder _collectionFolder;
		private FolderTeamRepo _repo;
		private string _originalUser;
		private string _checkMeOutOriginalChecksum;
		private List<string> _syncMessages;

		[OneTimeSetUp]
		public void OneTimeSetup()
		{
			_sharedFolder = new TemporaryFolder("TeamRepo_Shared");
			_collectionFolder = new TemporaryFolder("TeamRepo_Local");
			_repo = new FolderTeamRepo(_collectionFolder.FolderPath,_sharedFolder.FolderPath);
			_originalUser = _repo.CurrentUser;
			if (string.IsNullOrEmpty(_originalUser))
			{
				SIL.Windows.Forms.Registration.Registration.Default.Email = "test@somewhere.org";
			}

			// Simulate a book that was once shared, but has been deleted from the shared folder.
			MakeBook("Should be deleted","This should be deleted as it has local status but is not shared", true);
			var delPath = Path.Combine(_sharedFolder.FolderPath, "Should be deleted.bloom");
			RobustFile.Delete(delPath);

			// Simulate a book newly created locally. Not in repo, but should not be deleted.
			MakeBook("New book", "This should survive as it has no local status", false);

			// Simulate a book that needs nothing done to it. It's the same locally and on the repo.
			MakeBook("Keep me", "This needs nothing done to it");

			// Simulate a book that is checked out locally to the current user, but the file has
			// been deleted on the repo.
			MakeBook("Keep me too", "This also needs nothing done", false);
			_repo.WriteLocalStatus("Keep me too", new BookStatus().WithLockedBy("test@somewhere.org"));

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
			// Test result: repo is updated to indicate the local checkout. Local changes are not lost.
			MakeBook("Check me out", "Local and remote checksums correspond to this");
			UpdateLocalBook("Check me out", "This is supposed to be a newer value from local editing", false);
			var oldLocalStatus = _repo.GetLocalStatus("Check me out");
			var newLocalStatus = oldLocalStatus.WithLockedBy(_repo.CurrentUser);
			_checkMeOutOriginalChecksum = oldLocalStatus.checksum;
			_repo.WriteLocalStatus("Check me out", newLocalStatus);

			// Simulate a book that appears newly-created locally (no local status) but is also in the
			// repo. This would indicate two people coincidentally creating a book with the same name.
			// Test result: the local book should get renamed (both folder and htm).
			MakeBook("Rename local", "This content is on the server");
			_repo.AttemptLock("Rename local", "fred@somewhere.org");
			UpdateLocalBook("Rename local", "This is a new book created independently");
			var statusFilePath = _repo.GetStatusFilePath("Rename local", _collectionFolder.FolderPath);
			RobustFile.Delete(statusFilePath);


			// Simulate a book that is checked out locally but also checked out, to a different user
			// or machine, on the repo. This would indicate some sort of manual intervention, perhaps
			// while this user was long offline. The book has not been modified locally, but the local
			// status is out of date.
			// Test result: local status is updated to reflect the remote checkout, book content updated to repo.
			MakeBook("Update and undo checkout", "This content is everywhere");
			_repo.AttemptLock("Update and undo checkout", "fred@somewhere.org");
			_repo.WriteLocalStatus("Update and undo checkout", _repo.GetStatus("Update and undo checkout").WithLockedBy(_repo.CurrentUser));

			// Simulate a book that is checked out locally and not on the server, but the repo and (old)
			// local checksums are different. The book has not been edited locally.
			// Test result: book is updated to match repo. Local and remote status should match...review: which wins?
			MakeBook("Update and checkout", "This content is on the server");
			UpdateLocalBook("Update and checkout", "This simulates older content changed remotely but not locally");
			_repo.WriteLocalStatus("Update and checkout", _repo.GetLocalStatus("Update and checkout").WithLockedBy(_repo.CurrentUser));

			// Simulate a book that is checked out and modified locally, but has also been modified
			// remotely.
			// Test result: current local state is saved in lost-and-found. Repo version of book and state
			// copied to local. Warning to user.
			MakeBook("Update content and status and warn", "This simulates new content on server");
			_repo.AttemptLock("Update content and status and warn", "fred@somewhere.org");
			UpdateLocalBook("Update content and status and warn", "This is supposed to be the newest value from local editing");
			var newStatus = _repo.GetStatus("Update content and status and warn").WithLockedBy(_repo.CurrentUser)
				.WithChecksum("different from either");
			_repo.WriteLocalStatus("Update content and status and warn", newStatus);

			// Simulate a book that is checked out and modified locally, but is also checked out by another
			// user or machine in the repo. It has not (yet) been modified remotely.
			// Test result: current local state is saved in lost-and-found. Repo version of book and state
			// copied to local. Warning to user.
			MakeBook("Update content and status and warn2", "This simulates new content on server");
			_repo.AttemptLock("Update content and status and warn2", "fred@somewhere.org");
			UpdateLocalBook("Update content and status and warn2", "This is supposed to be the newest value from local editing", false);
			newStatus = _repo.GetStatus("Update content and status and warn2").WithLockedBy(_repo.CurrentUser);
			_repo.WriteLocalStatus("Update content and status and warn2", newStatus);

			// Simulate a book which has no local status, but for which the computed checksum matches
			// the shared one. This could happen if a user obtained the same book independently,
			// or during initial merging of a local and shared collection, where much of the material
			// was previously duplicated.
			// Test result: status is copied to local
			MakeBook("copy status", "Same content in both places");
			_repo.AttemptLock("copy status", "fred@somewhere.org");
			statusFilePath = _repo.GetStatusFilePath("copy status", _collectionFolder.FolderPath);
			RobustFile.Delete(statusFilePath);

			// sut for much of suite!
			_syncMessages = _repo.SyncAtStartup();
		}

		[OneTimeTearDown]
		public void OneTimeTearDown()
		{
			_collectionFolder.Dispose();
			_sharedFolder.Dispose();
			SIL.Windows.Forms.Registration.Registration.Default.Email = _originalUser;
		}

		/// <summary>
		/// The specific error messages we expect each have their own test. To make sure we don't get any
		/// additional, unexpected ones we check the count here.
		/// </summary>
		[Test]
		public void SyntAtStartup_ProducesNoUnexpectedMessages()
		{
			Assert.That(_syncMessages, Has.Count.EqualTo(2), "Unexpected number of error messages produced. Did you mean to add one?");
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
		public void SyncAtStartup_BooksWithSameNameIndependentlyCreated_RenamesLocal()
		{
			Assert.That(Directory.Exists(Path.Combine(_collectionFolder.FolderPath, "Rename local1")), Is.True);
			AssertLocalContent("Rename local1", "This is a new book created independently");
			AssertLocalContent("Rename local", "This content is on the server");
			Assert.That(_repo.GetLocalStatus("Rename local").lockedBy, Is.EqualTo("fred@somewhere.org"));
		}

		[Test]
		public void SyncAtStartup_BookDeletedRemotely_GetsDeletedLocally()
		{
			Assert.That(Directory.Exists(Path.Combine(_collectionFolder.FolderPath, "Should be deleted")), Is.False);
		}

		[Test]
		public void SyncAtStartup_BookCreatedLocallyNotCheckedIn_Survives()
		{
			Assert.That(Directory.Exists(Path.Combine(_collectionFolder.FolderPath, "New book")), Is.True);
		}

		[Test]
		public void SyncAtStartup_SameBookLocallyAndShared_NoLocalStatus_KeepsBookAddsStatus()
		{
			// We don't want to move the book to a new folder.
			Assert.That(Directory.EnumerateDirectories(_collectionFolder.FolderPath, "copy status*").Count(),Is.EqualTo(1));
			Assert.That(_repo.GetLocalStatus("copy status").lockedBy, Is.EqualTo("fred@somewhere.org"));
		}

		[Test]
		public void SyncAtStartup_BookCreatedRemotely_CopiedLocal()
		{
			AssertLocalContent("Add me", "Fetch to local");
		}

		[Test]
		public void SyncAtStartup_BookNotChangedButCheckoutUserConflicts_RemoteWins()
		{
			Assert.That(_repo.GetLocalStatus("Update and undo checkout").lockedBy, Is.EqualTo("fred@somewhere.org"));
		}

		[Test]
		public void SyncAtStartup_BookCheckedOutLocallyChangedRemotelyNotChangedLocally_RemoteContentAndLocalCheckoutWin()
		{
			AssertLocalContent("Update and checkout", "This content is on the server");
			Assert.That(_repo.GetLocalStatus("Update and checkout").lockedBy, Is.EqualTo(_repo.CurrentUser));
		}

		[Test]
		public void SyncAtStartup_ConflictingEditsAndCheckout_RemoteWinsWithWarning()
		{
			AssertLocalContent("Update content and status and warn", "This simulates new content on server");
			Assert.That(_repo.GetLocalStatus("Update content and status and warn").lockedBy, Is.EqualTo("fred@somewhere.org"));
			Assert.That(_syncMessages, Contains.Item("The book 'Update content and status and warn' was modified in the collection. Your changes are saved to Lost-and-found."));
			AssertLostAndFound("Update content and status and warn");
		}

		[Test]
		public void SyncAtStartup_ConflictingCheckoutAndLocalEdit_RemoteWinsWithWarning()
		{
			AssertLocalContent("Update content and status and warn2", "This simulates new content on server");
			Assert.That(_repo.GetLocalStatus("Update content and status and warn2").lockedBy, Is.EqualTo("fred@somewhere.org"));
			Assert.That(_syncMessages, Contains.Item("The book 'Update content and status and warn2' is checked out to someone else. Your changes are saved to Lost-and-found."));
			AssertLostAndFound("Update content and status and warn2");
		}

		[Test]
		public void SyncAtStartup_RepoNotShowingCheckoutButNoRemoteChanges_Checkout()
		{
			AssertLocalContent("Check me out", "This is supposed to be a newer value from local editing");
			var localStatus = _repo.GetLocalStatus("Check me out");
			Assert.That(localStatus.checksum, Is.EqualTo(_checkMeOutOriginalChecksum));
			Assert.That(localStatus.lockedBy, Is.EqualTo(_repo.CurrentUser));
			var repoStatus = _repo.GetStatus("Check me out");
			Assert.That(localStatus.checksum, Is.EqualTo(_checkMeOutOriginalChecksum));
			Assert.That(localStatus.lockedBy, Is.EqualTo(_repo.CurrentUser));
		}

		void AssertLocalContent(string bookName, string expectedContent)
		{

			var bookFolder = Path.Combine(_collectionFolder.FolderPath, bookName);
			Assert.That(Directory.Exists(bookFolder), Is.True);
			var bookHtmlPath = Path.Combine(bookFolder, Path.ChangeExtension(bookName, "htm"));
			Assert.That(File.Exists(bookHtmlPath));
			Assert.That(File.ReadAllText(bookHtmlPath, Encoding.UTF8),Contains.Substring(expectedContent));
		}

		void AssertLostAndFound(string bookName)
		{
			var bookPath = Path.Combine(_sharedFolder.FolderPath, "Lost and Found", Path.ChangeExtension(bookName, "bloom"));
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
		}

		void MakeBook(string name, string content, bool toRepo=true, bool onlyRepo = false)
		{
			var folderPath = Path.Combine(_collectionFolder.FolderPath, name);
			var bookPath = Path.Combine(folderPath, Path.ChangeExtension(name, "htm"));
			Directory.CreateDirectory(folderPath);
			RobustFile.WriteAllText(bookPath, "<html><body>"+ content + "</body></html>");
			if (toRepo)
				_repo.PutBook(folderPath);
			if (onlyRepo)
				SIL.IO.RobustIO.DeleteDirectoryAndContents(folderPath);
		}

		void UpdateLocalBook(string name, string content, bool updateChecksum = true)
		{
			var folderPath = Path.Combine(_collectionFolder.FolderPath, name);
			var bookPath = Path.Combine(folderPath, Path.ChangeExtension(name, "htm"));
			RobustFile.WriteAllText(bookPath, "<html><body>" + content + "</body></html>");
			if (updateChecksum)
			{
				var status = _repo.GetLocalStatus(name);
				status.checksum = TeamRepo.MakeChecksum(folderPath);
				_repo.WriteLocalStatus(name, status);
			}
		}

		[Test]
		public void HandleNewBook_CopiesBookAndShaToLocal()
		{
			var bookFolderPath = Path.Combine(_collectionFolder.FolderPath, "My book");
			Directory.CreateDirectory(bookFolderPath);
			var bookPath = Path.Combine(bookFolderPath, "My book.htm");
			RobustFile.WriteAllText(bookPath, "This is just a dummy");
			_repo.PutBook(bookFolderPath);
			SIL.IO.RobustIO.DeleteDirectoryAndContents(bookFolderPath);

			_repo.HandleNewBook(new NewBookEventArgs(){BookName="My book.bloom"});

			var destBookFolder = Path.Combine(_collectionFolder.FolderPath, "My book");
			var destBookPath = Path.Combine(destBookFolder, "My book.htm");
			Assert.That(File.ReadAllText(destBookPath), Is.EqualTo("This is just a dummy"));
			//AssertChecksumsMatch(_collectionFolder.FolderPath, "My book");
		}

		//void AssertChecksumsMatch(string destFolder, string bookName)
		//{
		//	var checksumFileName = Path.ChangeExtension(bookName, "checksum");
		//	var path1 = Path.Combine(_sharedFolder.FolderPath, checksumFileName);
		//	var path2 = Path.Combine(destFolder, checksumFileName);
		//	Assert.That(File.Exists(path1));
		//	Assert.That(File.Exists(path2));
		//	Assert.That(File.ReadAllBytes(path1), Is.EqualTo(File.ReadAllBytes(path2)));
		//}
	}
}
