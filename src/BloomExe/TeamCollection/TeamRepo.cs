using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using L10NSharp;
using Sentry;
using SIL.IO;

namespace Bloom.TeamCollection
{
	/// <summary>
	/// Abstract class, of which currently FolderTeamRepo is the only existing or planned implementation.
	/// The goal is to put here the logic that is independent of exactly how the shared data is stored
	/// and sharing is accomplished, to minimize what has to be reimplemented if we offer another option.
	/// The idea is to leave open the possibility of other implementations, for example, based on
	/// a DVCS.
	/// </summary>
	public abstract class TeamRepo
	{
		// special value for BookStatus.lockedBy when the book is newly created and not in the repo at all.
		public const string FakeUserIndicatingNewBook = "this user";
		private readonly string _localCollectionFolder; // The unshared folder that this repo syncs with

		public TeamRepo(string localCollectionFolder)
		{
			_localCollectionFolder = localCollectionFolder;
		}

		// This is the value the book must be locked to for a local checkout.
		// For all the Sharing code, this should be the one place we know how to find that user.
		public string CurrentUser => SIL.Windows.Forms.Registration.Registration.Default.Email;

		/// <summary>
		/// The folder-implementation-specific part of PutBook, the public method below.
		/// Exactly how it is written is implementation specific, but GetBook must be able to get it back.
		/// </summary>
		/// <param name="sourceBookFolderPath">See PutBook</param>
		/// <param name="newStatus">Updated status to write in new book</param>
		/// <param name="inLostAndFound">See PutBook</param>
		/// <remarks>Usually PutBook should be used; this method is meant for use by TeamRepo methods.</remarks>
		protected abstract void PutBookInRepo(string sourceBookFolderPath, BookStatus newStatus, bool inLostAndFound = false);

		/// <summary>
		/// Put the book into the repo. Usually includes unlocking it. Its new status, with new checksum,
		/// is written to the repo and also to a file in the local collection for later comparisons.
		/// </summary>
		/// <param name="folderPath">The root folder for the book, typically ending in its title,
		///     typically in the current collection folder.</param>
		/// <param name="checkin">If true, the book will no longer be checked out</param>
		/// <param name="inLostAndFound">If true, put the book into the Lost-and-found folder,
		///     if necessary generating a unique name for it. If false, put it into the main shared
		///     folder, overwriting any existing book.</param>
		/// <returns>Updated book status</returns>
		public BookStatus PutBook(string folderPath, bool checkin = false, bool inLostAndFound = false)
		{
			var bookFolderName = Path.GetFileName(folderPath);
			var checksum = MakeChecksum(folderPath);
			var status = GetStatus(bookFolderName).WithChecksum(checksum);
			if (status.lockedBy == FakeUserIndicatingNewBook) // bogus owner we get on book never before checked in
			{
				status.lockedBy = null; // Todo: should be CurrentUser, at least if not checking in? But makes tests fail.
			}

			if (checkin)
				status = status.WithLockedBy(null);
			PutBookInRepo(folderPath, status, inLostAndFound);
			WriteLocalStatus(Path.GetFileName(folderPath), status);
			return status;
		}

		// Return a list of all the books in the collection (the shared one, not
		// the local one)
		public abstract string[] GetBookList();

		// Unzip one book in the collection to the specified destination. Usually GetBook should be used
		protected abstract void GetBookFromRepo(string destinationCollectionFolder, string bookName);

		public void CopyBookFromSharedToLocal(string bookName, string destinationCollectionFolder = null)
		{
			GetBookFromRepo(destinationCollectionFolder ?? _localCollectionFolder, bookName);
			WriteLocalStatus(bookName, GetStatus(bookName), destinationCollectionFolder ?? _localCollectionFolder);
		}

		// Write the specified file, typically collection settings, to the repo.
		public abstract void PutFile(string pathName);

		// Read the specified file, typically collection settings, from the repo.
		public abstract void GetFile(string pathName);

		// Get a list of all the email addresses of people who have locked books
		// in the collection.
		// Commented out as not yet used.
		//public abstract string[] GetPeople();

		/// <summary>
		/// Start monitoring the repo so we can get notifications of new and changed books.
		/// </summary>
		protected internal abstract void StartMonitoring();
		/// <summary>
		/// Stop monitoring (new and changed book notifications will no longer be used).
		/// </summary>
		protected internal abstract void StopMonitoring();

		/// <summary>
		/// Common part of getting book status as recorded in the repo, or if it is not in the repo
		/// but there is such a book locally, treat as locked by FakeUserIndicatingNewBook.
		/// </summary>
		/// <param name="bookName"></param>
		/// <returns></returns>
		public BookStatus GetStatus(string bookName)
		{
			var statusString = GetBookStatusJsonFromRepo(bookName);

			if (String.IsNullOrEmpty(statusString))
			{
				var bookFolder = Path.Combine(_localCollectionFolder, Path.GetFileNameWithoutExtension(bookName));
				if (Directory.Exists(bookFolder))
				{
					// book exists only locally. Treat as checked out to FakeUserIndicatingNewBook
					return new BookStatus() { lockedBy = FakeUserIndicatingNewBook, lockedWhere = Environment.MachineName};
				}
				else
				{
					return new BookStatus();
				}
			}
			return BookStatus.FromJson(statusString);
		}

		/// <summary>
		/// Write the book's status, both in shared repo and in its own folder
		/// </summary>
		public void WriteBookStatus(string bookName, BookStatus status)
		{
			WriteBookStatusJsonToRepo(bookName, status.ToJson());
			WriteLocalStatus(bookName, status);
		}

		/// <summary>
		/// Get the raw status data from however the repo implementation stores it.
		/// </summary>
		protected abstract string GetBookStatusJsonFromRepo(string bookName);

		/// <summary>
		/// Set the raw status data to however the repo implementation stores it.
		/// </summary>
		protected abstract void WriteBookStatusJsonToRepo(string bookName, string status);

		/// <summary>
		/// Event raised when a new book is added to the repo remotely (that is, not by our own
		/// PutBook or SetStatus code).
		/// </summary>
		public event EventHandler<NewBookEventArgs> NewBook;

		/// <summary>
		/// Event raised when a book is modified in the repo remotely (that is, not by our own
		/// PutBook or SetStatus code).
		/// </summary>
		public event EventHandler<BookStateChangeEventArgs> BookStateChange;

		/// <summary>
		/// Get all the books in the repo copied into the local folder.
		/// </summary>
		/// <param name="destinationCollectionFolder">Default null means the local collection folder.</param>
		public void CopyAllBooksFromSharedToLocalFolder(string destinationCollectionFolder = null)
		{
			var dest = destinationCollectionFolder ?? _localCollectionFolder;
			foreach (var path in GetBookList())
			{
				CopyBookFromSharedToLocal(Path.GetFileNameWithoutExtension(path), dest);
			}
		}

		// Unlock the book, making it available for anyone to edit.
		public void UnlockBook(string bookName)
		{
			WriteBookStatus(bookName, GetStatus(bookName).WithLockedBy(null));
		}

		// Lock the book, making it available for the specified user to edit. Return true if successful.
		public bool AttemptLock(string bookName, string email = null)
		{
			var whoBy = email ?? CurrentUser;
			var status = GetStatus(bookName);
			if (String.IsNullOrEmpty(status.lockedBy))
			{
				status = status.WithLockedBy(whoBy);
				WriteBookStatus(bookName, status);
			}

			return IsCheckedOutHereBy(status, whoBy);
		}

		// Get the email of the user, if any, who has the book locked. Returns null if not locked.
		// As a special case, if the book exists only locally, we return TeamRepo.kThisUser.
		public string WhoHasBookLocked(string bookName)
		{
			return GetStatus(bookName).lockedBy;
		}

		// Gives the time when someone locked the book. DateTime.MaxValue if not locked.
		public DateTime WhenWasBookLocked(string bookName)
		{
			var status = GetStatus(bookName);
			DateTime result;
			if (DateTime.TryParse(status.lockedWhen, out result))
				return result;
			return DateTime.MaxValue;
		}

		/// <summary>
		/// Records the computer which locked a book. Allows us to distinguish between
		/// "you have this book locked" and "you locked this book on another computer"
		/// </summary>
		public string WhatComputerHasBookLocked(string bookName)
		{
			return GetStatus(bookName).lockedWhere;
		}

		/// <summary>
		/// A string, insensitive to various unimportant changes, which can be compared to
		/// ones made with other versions of the book to tell whether it has changed
		/// significantly. It may still be changed by some unimportant changes. Indeed, if
		/// a later version of Bloom uses another algorithm, it doesn't matter much. The
		/// worst result of them wrongly not matching is usually some extra copying.
		/// (In some pathological situations, it might lead to our unnecessarily treating
		/// changes as conflicting. These cases are already unlikely.)
		/// </summary>
		/// <param name="bookName"></param>
		/// <returns></returns>
		public string GetChecksum(string bookName)
		{
			return GetStatus(bookName).checksum;
		}

		/// <summary>
		/// Get anything we need from the repo to the local folder (except actual books).
		/// Enhance: at least, also whatever we need for decodable and leveled readers.
		/// Most likely we should have a way to ask the repo for all non-book content and
		/// retrieve it all.
		/// </summary>
		/// <param name="localCollectionFolder"></param>
		public void CopySharedCollectionFilesToLocal(string localCollectionFolder)
		{
			GetFile(Path.Combine(localCollectionFolder, "customCollectionStyles.css"));
			GetFile(CollectionPath(localCollectionFolder));
		}

		/// <summary>
		/// Gets the path to the bloomCollection file, given the folder.
		/// </summary>
		/// <param name="localCollectionFolder"></param>
		/// <returns></returns>
		public static string CollectionPath(string localCollectionFolder)
		{
			var collectionHame = Path.GetFileName(localCollectionFolder);
			var collectionPath = Path.Combine(localCollectionFolder, Path.ChangeExtension(collectionHame, "bloomCollection"));
			return collectionPath;
		}

		/// <summary>
		/// Send anything other than books that should be shared from local to the repo.
		/// Enhance: as for CopySharedCollectionFilesToLocal, also, we want this to be
		/// restricted to a specified set of emails, by default, the creator of the repo.
		/// </summary>
		/// <param name="localCollectionFolder"></param>
		public void CopySharedCollectionFilesFromLocal(string localCollectionFolder)
		{
			PutFile(Path.Combine(localCollectionFolder, "customCollectionStyles.css"));
			var collectionHame = Path.GetFileName(localCollectionFolder);
			PutFile(Path.Combine(localCollectionFolder, Path.ChangeExtension(collectionHame, "bloomCollection")));

		}

		protected void RaiseNewBook(string bookName)
		{
			NewBook?.Invoke(this, new NewBookEventArgs() {BookName = bookName});
		}

		protected void RaiseBookStateChange(string bookName)
		{
			BookStateChange?.Invoke(this, new BookStateChangeEventArgs() { BookName = bookName });
		}

		/// <summary>
		/// Gets things going so that Bloom will be notified of remote changes to the repo.
		/// </summary>
		public void SetupMonitoringBehavior()
		{
			NewBook += (sender, args) => { HandleNewBook(args); };
			BookStateChange += (sender, args) => HandleModifiedFile(args);
			StartMonitoring();
		}

		/// <summary>
		/// Handle a notification that a file has been modified. If it's a bloom book file
		/// that we don't have checked out, we can just update it in the local folder.
		/// For now, more complex cases are handled by asking the user to restart. There
		/// are probably things that can go wrong if he doesn't.
		/// </summary>
		/// <param name="args"></param>
		private void HandleModifiedFile(BookStateChangeEventArgs args)
		{
			if (args.BookName.EndsWith(".bloom"))
			{
				if (!IsCheckedOutHereBy(GetLocalStatus(args.BookName)))
				{
					// Just update things locally.
					CopyBookFromSharedToLocal(args.BookName);
					return;
				}
			}

			AskUserToRestart();
		}

		/// <summary>
		/// Handle a new book we have detected from NewBook event.
		/// Might be a new book from remote user. If so unpack to local.
		/// Just possibly might be a new book from a remote user whose name conflicts
		/// with a book created locally and not yet checked in. If so, we could rename the local
		/// book folder. But for that we'd have to be sure we're not doing something with it.
		/// Seems safest to just warn. But if our user goes ahead and checks in, that would be
		/// an overwrite, so we may need to do something more.
		/// </summary>
		/// <param name="localCollectionFolder"></param>
		/// <param name="args"></param>
		public void HandleNewBook(NewBookEventArgs args)
		{
			var newBookPath = args.BookName;
			var newBookName = Path.GetFileNameWithoutExtension(newBookPath);
			if (args.BookName.EndsWith(".bloom"))
			{
				var bookFolder = Path.Combine(_localCollectionFolder, newBookName);
				if (!Directory.Exists(bookFolder))
				{
					CopyBookFromSharedToLocal(newBookName, _localCollectionFolder);
					// Enhance: add it to the local collection
					return;
				}
			}

			AskUserToRestart();
		}

		private void AskUserToRestart()
		{
			MessageBox.Show(LocalizationManager.GetString("TeamCollection.RequestRestart",
					"Bloom has detected that other users have made changes in your team collection folder. When convenient, please restart Bloom to see the changes."),
				LocalizationManager.GetString("TeamCollection.RemoteChanges", "Remote Changes"));
			// Enhance: there are cases where we need to force a restart immediately.
			// For example, if the user is editing the book that has been modified elsewhere.
			// If this happens, when the user returns to the collection tab, Bloom will show who currently
			// has it checked out on the shared folder, so the user will in that case be prevented
			// from checking in (though probably puzzled). But, if it's been modified elsewhere and is no longer checked
			// out there, this user would be still allowed to check it in. That's unlikely because we should have
			// detected either at startup on on checkout that it got checked out. But the user might have been
			// offline then, or stayed in edit mode through the whole time some other user checked out, modified content,
			// and checked in again (ignoring both warnings).
			// I'm thinking that for version 0.1, we can maybe get away with warning users not to ignore
			// this warning!
		}

		internal string GetStatusFilePath(string bookName, string collectionFolder)
		{
			var bookFolderName = Path.GetFileNameWithoutExtension(bookName);
			var statusFile = Path.Combine(collectionFolder, bookFolderName, "book.status");
			return statusFile;
		}

		internal void WriteLocalStatus(string bookName, BookStatus status, string collectionFolder = null)
		{
			var statusFilePath = GetStatusFilePath(bookName, collectionFolder ?? _localCollectionFolder);
			RobustFile.WriteAllText(statusFilePath, status.ToJson(), Encoding.UTF8);
		}

		internal BookStatus GetLocalStatus(string bookName, string collectionFolder = null)
		{
			var statusFilePath = GetStatusFilePath(bookName, collectionFolder ?? _localCollectionFolder);
			if (File.Exists(statusFilePath))
			{
				return BookStatus.FromJson(RobustFile.ReadAllText(statusFilePath, Encoding.UTF8));
			}
			return new BookStatus();
		}

		// Original calculation, from content, of the version code we store in book status.
		internal static string MakeChecksum(string folderPath)
		{
			var bookFolderName = Path.GetFileName(folderPath);
			var sourceBookPath = Path.Combine(folderPath, Path.ChangeExtension(bookFolderName, "htm"));
			return Book.Book.MakeVersionCode(RobustFile.ReadAllText(sourceBookPath), sourceBookPath);
		}

		/// <summary>
		/// Answer true if the indicated status represents a checkout on the current computer
		/// by the specified user (by default, the current user). If it is in fact the current
		/// user, this means editing is allowed.
		/// </summary>
		/// <param name="status"></param>
		/// <param name="email"></param>
		/// <returns></returns>
		internal bool IsCheckedOutHereBy(BookStatus status, string email = null)
		{
			var whoBy = email ?? CurrentUser;
			return status.IsCheckedOutHereBy(whoBy);
		}

		/// <summary>
		/// Run this when Bloom starts up to get the repo and local directories as sync'd as possible.
		/// </summary>
		/// <returns>List of warnings, if any problems occurred.</returns>
		public List<string> SyncAtStartup()
		{
			var warnings = new List<string>(); // accumulates any warning messages
			// Delete books that we think have been deleted remotely from the repo.
			foreach (var path in Directory.EnumerateDirectories(_localCollectionFolder))
			{
				try
				{
					var fileName = Path.GetFileName(path);
					var status = GetBookStatusJsonFromRepo(fileName);
					if (status == null)
					{
						// no sign of book in repo...should we delete it?
						var statusFilePath = GetStatusFilePath(fileName, _localCollectionFolder);
						if (File.Exists(statusFilePath))
						{
							// If there's no local status, presume it's a newly created local file and keep it
							// On this branch, there is local status, so the book has previously been shared.
							// Since it's now missing from the repo, we assume it's been deleted.
							// Unless it's checked out to the current user on the current computer, delete
							// the local version.
							var statusLocal = GetLocalStatus(fileName);
							if (statusLocal.lockedBy != CurrentUser
							    || statusLocal.lockedWhere != Environment.MachineName)
							{
								SIL.IO.RobustIO.DeleteDirectoryAndContents(path);
							}

							// If it's checked out here, assume current user wants it and keep it.
							// If he checks it in, that will undo the delete...may annoy the user
							// who deleted it, but that's life in a shared collection.
						}
					}
				}
				catch (Exception ex)
				{
					// Something went wrong with dealing with this book, but we'd like to carry on with
					// syncing the rest of the collection
					var msg = String.Format("Something went wrong trying to sync with the book {0} in your Team Collection.", path);
					SentrySdk.AddBreadcrumb(msg);
					SentrySdk.CaptureException(ex);
					warnings.Add(msg);
				}
			}

			// Now looking at each book that is already shared...
			// Note: a number of 'continue' statements here are redundant. But they serve as
			// a useful marker that we are satisfied we've done all that is needed in a particular
			// situation we've identified.
			foreach (var bookName in GetBookList())
			{
				try {
				var localFolderPath = Path.Combine(_localCollectionFolder, bookName);
				if (!Directory.Exists(localFolderPath))
				{
					// brand new book! Get it.
					CopyBookFromSharedToLocal(bookName);
					continue;
				}
				var sharedStatus = GetStatus(bookName); // we know it's in the repo, so status will certainly be from there.
				var statusFilePath = GetStatusFilePath(bookName, _localCollectionFolder);
				if (!File.Exists(statusFilePath))
				{
					var currentChecksum = MakeChecksum(localFolderPath);
					if (currentChecksum == sharedStatus.checksum)
					{
						// We have the same book with the same name and content in both places, but no local status.
						// Somehow the book was copied not using the TeamCollection. Possibly, we are merging
						// two versions of the same collection. Clean up by copying status.
						WriteLocalStatus(bookName, sharedStatus);
					}
					else
					{
						// The remote book has the same name as an independently-created new local book. Move the new local book
						int count = 0;
						string renameFolder = "";
						do
						{
							count++;
							renameFolder = localFolderPath + count;
						} while (Directory.Exists(renameFolder));

						Directory.Move(localFolderPath, renameFolder);
						var renamePath = Path.Combine(renameFolder,
							Path.ChangeExtension(Path.GetFileName(renameFolder), "htm"));
						var oldBookPath = Path.Combine(renameFolder, Path.ChangeExtension(bookName, "htm"));
						RobustFile.Move(oldBookPath, renamePath);

						CopyBookFromSharedToLocal(bookName); // Get shared book and status
						// Review: does this deserve a warning?
						continue;
					}
				}

				// We know there's a local book by this name and both have status.
				var localStatus = GetLocalStatus(bookName);

				if (!IsCheckedOutHereBy(localStatus)) {
				    if (localStatus.checksum != sharedStatus.checksum)
				    {
					    // Changed and not checked out. Just bring it up to date.
					    CopyBookFromSharedToLocal(bookName); // updates everything local.
				    }
					// whether or not we updated it, if it's not checked out there's no more to do.
				    continue;
				}

				// At this point, we know there's a version of the book in the repo
				// and a local version that is checked out here according to local status.
				if (IsCheckedOutHereBy(sharedStatus))
				{
					// the repo agrees. We could check that the checksums match, but there's no
					// likely scenario for them not to. Everything is consistent, so we can move on
					continue;
				}

				// Now we know there's some sort of conflict. The local and repo status of this
				// book don't match.
				if (localStatus.checksum == sharedStatus.checksum)
				{
					if (string.IsNullOrEmpty(sharedStatus.lockedBy))
					{
						// Likely someone started a checkout remotely, but changed their mind without making edits.
						// Just restore our checkout.
						WriteBookStatus(bookName, localStatus);
						continue;
					}
					else
					{
						// Checked out by someone else in the shared folder. They win.
						// Do we need to save local edits?
						var currentChecksum = MakeChecksum(localFolderPath);
						if (currentChecksum != localStatus.checksum)
						{
							// Edited locally while someone else has it checked out. Copy current local to lost and found
							PutBook(localFolderPath, inLostAndFound:true);
							// warn the user
							var msgTemplate = LocalizationManager.GetString("TeamCollection.ConflictingCheckout",
								"The book '{0}' is checked out to someone else. Your changes are saved to Lost-and-found.");
							warnings.Add(string.Format(msgTemplate, bookName));
							// Make the local folder match the repo (this is where 'they win')
							CopyBookFromSharedToLocal(bookName);
							continue;
						}
						else
						{
							// No local edits yet. Just correct the local checkout status.
							WriteBookStatus(bookName, sharedStatus);
							continue;
						}
					}
				}
				else
				{
					// Book has been changed remotely. They win, whether or not currently checked out.
					var currentChecksum = MakeChecksum(localFolderPath);
					if (currentChecksum == localStatus.checksum)
					{
						// not edited locally. No warning needed, but we need to update it. We can keep local checkout.
						CopyBookFromSharedToLocal(bookName);
						WriteBookStatus(bookName, localStatus.WithChecksum(sharedStatus.checksum));
						continue;
					}
					// Copy current local to lost and found
					PutBook(Path.Combine(_localCollectionFolder, bookName), inLostAndFound:true);
					// copy shared book and status to local
					CopyBookFromSharedToLocal(bookName);
					// warn the user
					var msgTemplate = LocalizationManager.GetString("TeamCollection.ConflictingEdit",
						"The book '{0}' was modified in the collection. Your changes are saved to Lost-and-found.");
					warnings.Add(string.Format(msgTemplate, bookName));
					continue;
				}
				}
				catch (Exception ex)
				{
					// Something went wrong with dealing with this book, but we'd like to carry on with
					// syncing the rest of the collection
					var msg = String.Format("Something went wrong trying to sync with the book {0} in your Team Collection.", bookName);
					SentrySdk.AddBreadcrumb(msg);
					SentrySdk.CaptureException(ex);
					warnings.Add(msg);
				}
			}

			return warnings;
		}


		/// <summary>
		/// Figure out where the specified local folder's team collection (shared) folder is, if
		/// it has one...if not return null.
		/// </summary>
		public static string GetSharedFolder(string collectionFolder)
		{
			var sharedSettingsPath = Path.Combine(collectionFolder, "TeamSettings.xml");
			if (File.Exists(sharedSettingsPath))
			{
				try
				{
					var doc = new XmlDocument();
					doc.Load(sharedSettingsPath);
					return doc.DocumentElement.GetElementsByTagName("sharingFolder").Cast<XmlElement>()
						.First().InnerText;
				}
				catch (Exception ex)
				{
					NonFatalProblem.Report(ModalIf.All, PassiveIf.All, "Bloom found shared collection settings but could not process them", null, ex, true);
				}
			}

			return null;
		}

		/// <summary>
		/// Main entry point called before creating CollectionSettings; updates local folder to match
		/// shared one, if any.
		/// </summary>
		/// <param name="projectPath"></param>
		public static void MergeSharedData(string projectPath)
		{
			var collectionFolder = Path.GetDirectoryName(projectPath);
			var sharedSettingsPath = GetSharedFolder(collectionFolder);
			if (sharedSettingsPath != null)
			{
				// This is a temporary repo to perform the SyncAtStartup. It will get garbage
				// collected and we'll make a new one in SharingApi's constructor to be TheOneInstance
				// (as long as we're working on this collection).
				var repo = new FolderTeamRepo(collectionFolder, sharedSettingsPath);
				var problems = repo.SyncAtStartup();
				if (problems.Count > 0)
				{
					// Todo: localize. Not adding to XLF now because I'm almost sure JohnH will want to design something better.
					MessageBox.Show("Bloom found some problems while loading changes from your team collection:"
					                + Environment.NewLine + String.Join("," + Environment.NewLine, problems),
						"Merge Problems");
				}
			}
		}
	}
}
