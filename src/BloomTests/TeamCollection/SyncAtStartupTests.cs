﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Bloom.TeamCollection;
using BloomTemp;
using BloomTests.DataBuilders;
using Moq;
using NUnit.Framework;
using NUnit.Framework.Constraints;
using SIL.IO;

namespace BloomTests.TeamCollection
{
	public class SyncAtStartupTests
	{
		private const string kConflictName = "My book (FRED SOMEONE's conflicted copy 2021-03-18)";
		private TemporaryFolder _repoFolder;
		protected TemporaryFolder _collectionFolder;
		protected Mock<ITeamCollectionManager> _mockTcManager;
		protected FolderTeamCollection _collection;
		private string _originalUser;
		private string _checkMeOutOriginalChecksum;
		protected List<string> _syncMessages;
		protected ProgressSpy _progressSpy;
		private TeamCollectionMessageLog _tcLog;

		[OneTimeSetUp]
		public void OneTimeSetup()
		{
			_repoFolder = new TemporaryFolder("SyncAtStartup_Repo");
			_collectionFolder = new TemporaryFolder("SyncAtStartup_Local");
			FolderTeamCollection.CreateTeamCollectionLinkFile(_collectionFolder.FolderPath,
				_repoFolder.FolderPath);
			_mockTcManager = new Mock<ITeamCollectionManager>();
			_tcLog = new TeamCollectionMessageLog(TeamCollectionManager.GetTcLogPathFromLcPath(_collectionFolder.FolderPath));
			_collection = new FolderTeamCollection(_mockTcManager.Object, _collectionFolder.FolderPath, _repoFolder.FolderPath, tcLog:_tcLog);
			_collection.CollectionId = Bloom.TeamCollection.TeamCollection.GenerateCollectionId();
			TeamCollectionManager.ForceCurrentUserForTests("test@somewhere.org");

			// Simulate a book that was once shared, but has been deleted from the repo folder.
			MakeBook("Should be deleted", "This should be deleted as it has local status but is not shared", true);
			var delPath = Path.Combine(_repoFolder.FolderPath, "Books", "Should be deleted.bloom");
			RobustFile.Delete(delPath);

			// Simulate a book newly created locally. Not in repo, but should not be deleted.
			MakeBook("A book", "This should survive as it has no local status", false);
			// By the way, like most new books, it got renamed early in life...twice
			SimulateRename(_collection, "A book", "An early name");
			SimulateRename(_collection, "An early name", "New book");

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

			// Simulate a book that was checked in, then checked out again and renamed,
			// but not yet checked in. Both "A renamed book" folder and content and "An old name.bloom"
			// should survive. (Except for an obscure reason when joining a TC...see comment in the test.)
			MakeBook("An old name", "Should be kept in both places with different names");
			_collection.AttemptLock("An old name", "test@somewhere.org");
			SimulateRename(_collection, "An old name", "an intermediate name");
			SimulateRename(_collection, "an intermediate name", "A renamed book");

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
			var statusFilePath = Bloom.TeamCollection.TeamCollection.GetStatusFilePath("Rename local", _collectionFolder.FolderPath);
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
			statusFilePath = Bloom.TeamCollection.TeamCollection.GetStatusFilePath("copy status", _collectionFolder.FolderPath);
			RobustFile.Delete(statusFilePath);

			// Simulate a book that was copied from another TC, using File Explorer.
			// It therefore has a book.status file, but with a different guid.
			// Test result: it should survive, and on a new collection sync get copied into the repo
			var copiedEx = "copied with Explorer";
			MakeBook(copiedEx, "This content is only local", false);
			_collection.WriteLocalStatus(copiedEx, new BookStatus(), collectionId: Bloom.TeamCollection.TeamCollection.GenerateCollectionId());

			// Simulate a book that appeared in DropBox when their software found a conflict.
			// It should NOT be copied locally, but instead moved to Lost and Found, with a report.
			MakeBook(kConflictName, "This content is only on the repo, apart from conflicting copies");
			var conflictFolderPath = Path.Combine(_collectionFolder.FolderPath, kConflictName);
			SIL.IO.RobustIO.DeleteDirectoryAndContents(conflictFolderPath);

			_collection.WriteLocalStatus(copiedEx, new BookStatus(), collectionId: Bloom.TeamCollection.TeamCollection.GenerateCollectionId());

			// Simulate a corrupt zip file, only in the repo
			File.WriteAllText(Path.Combine(_repoFolder.FolderPath, "Books", "new corrupt book.bloom"), "This is not a valid zip!");

			// Simulate a corrupt zip file that corresponds to a local book.
			var badZip = "has a bad zip in repo";
			MakeBook(badZip, "This book seems to be in both places, but the repo is corrupt");
			File.WriteAllText(Path.Combine(_repoFolder.FolderPath, "Books", badZip + ".bloom"), "This is also not a valid zip!");

			// Make a couple of folders that are legitimately present, but not books.
			var allowedWords = Path.Combine(_collectionFolder.FolderPath, "Allowed Words");
			Directory.CreateDirectory(allowedWords);
			File.WriteAllText(Path.Combine(allowedWords, "some sample.txt"), "This a fake word list");
			var sampleTexts = Path.Combine(_collectionFolder.FolderPath, "Sample Texts");
			Directory.CreateDirectory(sampleTexts);
			File.WriteAllText(Path.Combine(sampleTexts, "a sample.txt"), "This a fake sample text");

			_progressSpy = new ProgressSpy();

			// sut for the whole suite!
			Assert.That(_collection.SyncAtStartup(_progressSpy, FirstTimeJoin()), Is.True);
		}

		public static void SimulateRename(Bloom.TeamCollection.TeamCollection tc, string oldName, string newName)
		{
			var oldPath = Path.Combine(tc.LocalCollectionFolder, oldName);
			var newPath = Path.Combine(tc.LocalCollectionFolder, newName);
			RobustIO.MoveDirectory(oldPath, newPath);
			RobustFile.Move(Path.Combine(newPath, oldName + ".htm"), Path.Combine(newPath, newName + ".htm"));
			tc.HandleBookRename(oldName, newName);
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
			TeamCollectionManager.ForceCurrentUserForTests(null);
		}

		[Test]
		public virtual void SyncAtStartup_FoldersWithoutHtml_NotTurnedIntoBooks()
		{
			Assert.That(File.Exists(Path.Combine(_repoFolder.FolderPath, "Books", "Allowed Words.bloom")), Is.False);
			Assert.That(File.Exists(Path.Combine(_repoFolder.FolderPath, "Books", "Sample Texts.bloom")), Is.False);
		}

		/// <summary>
		/// The specific error messages we expect each have their own test. To make sure we don't get any
		/// additional, unexpected ones we check the count here.
		/// </summary>
		[Test]
		public virtual void SyncAtStartup_ProducesNoUnexpectedMessages()
		{
			Assert.That(_progressSpy.Warnings, Has.Count.EqualTo(2), "Unexpected number of progress warnings produced.");
			Assert.That(_progressSpy.Errors, Has.Count.EqualTo(5), "Unexpected number of progress errors produced. Did you mean to add one?");
			Assert.That(_progressSpy.ProgressMessages, Has.Count.EqualTo(3), "Unexpected number of progress messages produced. Did you mean to add one?");
		}

		[Test]
		public void SyncAtStartup_PutsExpectedMessagesInLog()
		{
			var messages = _tcLog.Messages;
			Assert.That(messages[0].MessageType,Is.EqualTo(MessageAndMilestoneType.Reloaded));
			// Many others are expected, individually checked through AssertProgress
			Assert.That(messages[messages.Count-1].MessageType, Is.EqualTo(MessageAndMilestoneType.LogDisplayed));
		}

		[Test]
		public void SyncAtStartup_BookNeedsNothingDone_Survives()
		{
			Assert.That(Directory.Exists(Path.Combine(_collectionFolder.FolderPath, "Keep me")), Is.True);
		}

		[Test]
		public void SyncAtStartup_CheckedOutBookRenamed_SurvivesBothPlaces()
		{
			var renamedBookFolder = Path.Combine(_collectionFolder.FolderPath, "A renamed book");
			Assert.That(Directory.Exists(renamedBookFolder), Is.True);
			Assert.That(File.Exists(Path.Combine(renamedBookFolder, "A renamed book.htm")));
			// This is debatable. We definitely should not delete the old repo book that has been renamed
			// in a normal sync. If we're doing a 'first time join', then it's weird to find a book in
			// the state of being checked out at all...that implies we're already connected to the repo.
			// Weirder still to find it checked out AND renamed. What currently happens is that the renamed
			// book looks new, and in a first time join, new books get checked in, and in the course of
			// the checkin, the old repo file gets deleted. Not certain this is the right behavior,
			// but it's plausible and falls naturally out of other decisions we made, so I'm leaving
			// both the code and the test that way. However, I'm not adding additional tests to verify
			// the the checkin happened, since I'm not sure we want it to.
			Assert.That(File.Exists(Path.Combine(_repoFolder.FolderPath, "Books", "An old name.bloom")),
				Is.EqualTo(!FirstTimeJoin()));
		}

		[Test]
		public void SyncAtStartup_CheckedOutBookRenamed_OldLocalNotReinstated()
		{
			Assert.That(Directory.Exists(Path.Combine(_collectionFolder.FolderPath, "An old name")), Is.False);
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
			AssertWarning("Renaming the local book '{0}' because there is a new one with the same name from the Team Collection", "Rename local");
		}

		[Test]
		public virtual void SyncAtStartup_BookDeletedRemotely_GetsDeletedLocally_UnlessJoin()
		{
			Assert.That(Directory.Exists(Path.Combine(_collectionFolder.FolderPath, "Should be deleted")), Is.False);
			AssertWarning("Deleting '{0}' from local folder as it is no longer in the Team Collection","Should be deleted");
		}

		[Test]
		public void SyncAtStartup_BookCreatedLocallyNotCheckedIn_Survives()
		{
			Assert.That(Directory.Exists(Path.Combine(_collectionFolder.FolderPath, "New book")), Is.True);
		}

		[Test]
		public void SyncAtStartup_BookCopiedFromAnotherTcWithExplorer_Survives()
		{
			Assert.That(Directory.Exists(Path.Combine(_collectionFolder.FolderPath, "copied with Explorer")), Is.True);
		}

		[Test]
		public virtual void SyncAtStartup_BookCreatedLocallyNotCheckedIn_CopiedToRepoOnlyOnJoin()
		{
			Assert.That(File.Exists(Path.Combine(_repoFolder.FolderPath, "Books" ,"New book.bloom")), Is.EqualTo(FirstTimeJoin()));
		}

		[Test]
		public virtual void SyncAtStartup_BookCopiedFromAnotherTcWithExplorer_CopiedToRepoOnlyOnJoin()
		{
			Assert.That(File.Exists(Path.Combine(_repoFolder.FolderPath, "Books", "copied with Explorer.bloom")), Is.EqualTo(FirstTimeJoin()));
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
			AssertProgress("Fetching a new book '{0}' from the Team Collection", "Add me");
		}

		[Test]
		public void SyncAtStartup_DropBoxConflictCreatedRemotely_MovedToLostAndFound()
		{
			AssertLostAndFound(kConflictName);
		}

		[Test]
		public void SyncAtStartup_DropBoxConflictCreatedRemotely_NotCopiedLocal()
		{
			Assert.That(!Directory.Exists(Path.Combine(_collectionFolder.FolderPath, kConflictName)));
		}

		[Test]
		public void SyncAtStartup_DropBoxConflictCreatedRemotely_GeneratesMessage()
		{
			AssertProgress("Two members of your team had a book checked out at the same time, so the Team Collection got two different versions of it. Bloom has moved \"{0}\" to the Lost & Found.",
				kConflictName,null, MessageAndMilestoneType.ErrorNoReload);
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
			AssertProgress("Updating '{0}' to match the Team Collection", "Update and checkout");
		}

		[Test]
		public void SyncAtStartup_ConflictingEditsAndCheckout_RemoteWinsWithWarning()
		{
			AssertLocalContent("Update content and status and warn", "This simulates new content on server");
			Assert.That(_collection.GetLocalStatus("Update content and status and warn").lockedBy, Is.EqualTo("fred@somewhere.org"));
			AssertProgress("The book '{0}', which you have checked out and edited, was modified in the Team Collection by someone else. Your changes have been overwritten, but are saved to Lost-and-found.",
				"Update content and status and warn", null, MessageAndMilestoneType.ErrorNoReload);
			AssertLostAndFound("Update content and status and warn");
		}

		[Test]
		public void SyncAtStartup_ConflictingCheckoutAndLocalEdit_RemoteWinsWithWarning()
		{
			AssertLocalContent("Update content and status and warn2", "This simulates new content on server");
			Assert.That(_collection.GetLocalStatus("Update content and status and warn2").lockedBy, Is.EqualTo("fred@somewhere.org"));
			AssertProgress("The book '{0}', which you have checked out and edited, is checked out to someone else in the Team Collection. Your changes have been overwritten, but are saved to Lost-and-found."
				, "Update content and status and warn2",null, MessageAndMilestoneType.ErrorNoReload);
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
			AssertProgress("Updating '{0}' to match the Team Collection", "Update me");
		}

		[Test]
		public void SyncAtStartup_BadBookInRepo_NoLocalBook_GivesError_NoChanges()
		{
			Assert.That(File.Exists(Path.Combine(_repoFolder.FolderPath, "Books", "new corrupt book.bloom")));
			Assert.That(Directory.Exists(Path.Combine(_collectionFolder.FolderPath, "new corrupt book")), Is.False);
			// I think this is better than spelling it out. We have a test for the method itself.
			var msg = _collection.GetBadZipFileMessage("new corrupt book");
			AssertProgress(msg, null, null,MessageAndMilestoneType.ErrorNoReload);
		}

		[Test]
		public void SyncAtStartup_BadBookInRepo_LocalBook_GivesError_NoChanges()
		{
			var badZip = "has a bad zip in repo";
			Assert.That(File.Exists(Path.Combine(_repoFolder.FolderPath, "Books", badZip + ".bloom")));
			// we didn't delete the local one!
			AssertLocalContent(badZip, "This book seems to be in both places, but the repo is corrupt");

			// I think this is better than spelling it out. We have a test for the method itself.
			var msg = _collection.GetBadZipFileMessage(badZip);
			AssertProgress(msg, null, null, MessageAndMilestoneType.ErrorNoReload);
		}

		// Check that the indicated message made it into the progress report, and ALSO
		// into the log.

		protected void AssertProgress(string msg, string param0 = null, string param1 = null,
			MessageAndMilestoneType expectedType = MessageAndMilestoneType.History)
		{
			var expectedMsg = string.Format(msg, param0, param1);
			
			if (expectedType == MessageAndMilestoneType.Error || expectedType == MessageAndMilestoneType.ErrorNoReload)
			{
				Assert.That(_progressSpy.Errors, Contains.Item(expectedMsg));
			}
			else
			{
				Assert.That(_progressSpy.ProgressMessages, Contains.Item(expectedMsg));
			}

			Assert.That(_tcLog.Messages, Has.Exactly(1).Matches<TeamCollectionMessage>(m =>
				m.RawEnglishMessageTemplate == msg && (m.Param0 ?? "") == (param0 ?? "") && (m.Param1 ?? "") == (param1 ?? "") &&
				m.MessageType == expectedType));
		}

		protected void AssertWarning(string msg, string param0 = null, string param1 = null)
		{
			var expectedMsg = string.Format(msg, param0, param1);

			// (JH May 2021) The mismatch between ProgressKinds and MessageAndMilestoneType
			// get's rather painful here. The spy is collecting based on the former, but this
			// is based on the later. So progresses that are warnings are in limbo.

			Assert.That(_progressSpy.Warnings, Contains.Item(expectedMsg));
			
			Assert.That(_tcLog.Messages, Has.Exactly(1).Matches<TeamCollectionMessage>(m =>
				m.RawEnglishMessageTemplate == msg && (m.Param0 ?? "") == (param0 ?? "") &&
				(m.Param1 ?? "") == (param1 ?? "")));
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
		public static string MakeFakeBook(string collectionFolder, string name, string content, string folderNameIfDifferent = null)
		{
			var bookBuilder = new BookFolderBuilder()
				.WithRootFolder(collectionFolder)
				.WithBookFolderName(folderNameIfDifferent)
				.WithTitle(name)
				.WithHtm("<html><body>" + content + "</body></html>")
				.Build();

			return bookBuilder.BuiltBookFolderPath;
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
			AssertProgress("Found different versions of '{0}' in both collections. The team version has been copied to your local collection, and the old local version to Lost and Found",
				"Rename local", null, MessageAndMilestoneType.ErrorNoReload);
			AssertLostAndFound("Rename local");
		}

		[Test]
		public override void SyncAtStartup_ProducesNoUnexpectedMessages()
		{
			Assert.That(_progressSpy.Warnings, Has.Count.EqualTo(0), "Unexpected number of progress warnings produced. We're not using warning any more");
			Assert.That(_progressSpy.Errors, Has.Count.EqualTo(6), "Unexpected number of progress errors produced. Did you mean to add one?");
			Assert.That(_progressSpy.ProgressMessages, Has.Count.EqualTo(3), "Unexpected number of progress messages produced. Did you mean to add one?");
		}

		[Test]
		public override void SyncAtStartup_BookDeletedRemotely_GetsDeletedLocally_UnlessJoin()
		{
			Assert.That(Directory.Exists(Path.Combine(_collectionFolder.FolderPath, "Should be deleted")), Is.True);
		}
	}
}
