using Bloom.Api;
using Bloom.MiscUI;
using Bloom.web;
using L10NSharp;
using Sentry;
using SIL.IO;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Bloom.ToPalaso;
using SIL.Progress;

namespace Bloom.TeamCollection
{
	/// <summary>
	/// Abstract class, of which currently FolderTeamRepo is the only existing or planned implementation.
	/// The goal is to put here the logic that is independent of exactly how the shared data is stored
	/// and sharing is accomplished, to minimize what has to be reimplemented if we offer another option.
	/// The idea is to leave open the possibility of other implementations, for example, based on
	/// a DVCS.
	/// </summary>
	public abstract class TeamCollection: IDisposable
	{
		// special value for BookStatus.lockedBy when the book is newly created and not in the repo at all.
		public const string FakeUserIndicatingNewBook = "this user";
		protected readonly string _localCollectionFolder; // The unshared folder that this collection syncs with

		public TeamCollection(string localCollectionFolder)
		{
			_localCollectionFolder = localCollectionFolder;
		}

		/// <summary>
		/// The folder-implementation-specific part of PutBook, the public method below.
		/// Exactly how it is written is implementation specific, but GetBook must be able to get it back.
		/// </summary>
		/// <param name="sourceBookFolderPath">See PutBook</param>
		/// <param name="newStatus">Updated status to write in new book</param>
		/// <param name="inLostAndFound">See PutBook</param>
		/// <remarks>Usually PutBook should be used; this method is meant for use by TeamCollection methods.</remarks>
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

		/// <summary>
		/// Sync every book from the local collection (every folder that has a corresponding htm file) from local
		/// to repo, unless status files exist indicating they are already in sync. (This is typically used when
		/// creating a TeamCollection from an existing local collection. Usually it is a new folder and all
		/// books are copied.)
		/// </summary>
		public void SynchronizeBooksFromLocalToRepo()
		{
			foreach (var path in Directory.EnumerateDirectories(_localCollectionFolder))
			{
				try
				{
					var fileName = Path.GetFileName(path);
					var status = GetStatus(fileName);
					var localStatus = GetLocalStatus(fileName);
					var localHtmlFilePath = Path.Combine(path, Path.ChangeExtension(Path.GetFileName(path), "htm"));
					if ((status?.checksum == null || status.checksum != localStatus?.checksum) && RobustFile.Exists(localHtmlFilePath))
					{
						PutBook(path);
					}
				}
				catch (Exception ex)
				{
					// Something went wrong with dealing with this book, but we'd like to carry on with
					// syncing the rest of the collection
					var msg = String.Format("Something went wrong trying to copy the book {0} to your Team Collection.", path);
					NonFatalProblem.Report(ModalIf.All, PassiveIf.All, msg, null, ex);
				}
			}
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

		// Write the specified file to the repo's collection files.
		public abstract void PutCollectionFile(string pathName);

		// Read the specified file from the repo's collection files.
		public abstract void GetCollectionFile(string pathName);

		/// <summary>
		/// Get the names of all collection files
		/// </summary>
		/// <returns></returns>
		public abstract string[] CollectionFiles();

		// Get a list of all the email addresses of people who have locked books
		// in the collection.
		// Commented out as not yet used.
		//public abstract string[] GetPeople();

		private bool _monitoring = false;

		/// <summary>
		/// Start monitoring the repo so we can get notifications of new and changed books.
		/// </summary>
		protected virtual internal void StartMonitoring()
		{
			_monitoring = true;
		}

		/// <summary>
		/// Stop monitoring (new and changed book notifications will no longer be used).
		/// </summary>
		protected virtual internal void StopMonitoring()
		{
			_monitoring = false;
		}

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
			var whoBy = email ?? TeamCollectionManager.CurrentUser;
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
			foreach (var name in CollectionFiles())
			{
				GetCollectionFile(Path.Combine(localCollectionFolder, name));
			}
		}

		/// <summary>
		/// Gets the path to the bloomCollection file, given the folder.
		/// If the folder name ends in " - TC" we will strip that off.
		/// </summary>
		/// <param name="parentFolder"></param>
		/// <returns></returns>
		public static string CollectionPath(string parentFolder)
		{
			var collectionHame = GetLocalCollectionNameFromTcName(Path.GetFileName(parentFolder));
			var collectionPath = Path.Combine(parentFolder, Path.ChangeExtension(collectionHame, "bloomCollection"));
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
			var collectionStylesPath = Path.Combine(localCollectionFolder, "customCollectionStyles.css");
			if (RobustFile.Exists(collectionStylesPath))
				PutCollectionFile(collectionStylesPath);
			var collectionName = Path.GetFileName(localCollectionFolder);
			PutCollectionFile(Path.Combine(localCollectionFolder, Path.ChangeExtension(collectionName, "bloomCollection")));

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
			var bookFolderPath = Path.Combine(collectionFolder, bookFolderName);
			if (!Directory.Exists(bookFolderPath))
				Directory.CreateDirectory(bookFolderPath);
			var statusFile = Path.Combine(bookFolderPath, "book.status");
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
			var whoBy = email ?? TeamCollectionManager.CurrentUser;
			return status.IsCheckedOutHereBy(whoBy);
		}

		/// <summary>
		/// Run this when Bloom starts up to get the repo and local directories as sync'd as possible.
		/// Also run when first joining an existing collection to merge them. A few behaviors are
		/// different in this case.
		/// </summary>
		/// <returns>List of warnings, if any problems occurred.</returns>
		public List<string> SyncAtStartup(IWebSocketProgress progress, bool firstTimeJoin = false)
		{
			var warnings = new List<string>(); // accumulates any warning messages
			// Delete books that we think have been deleted remotely from the repo.
			// If it's a join collection merge, check new books in instead.
			foreach (var path in Directory.EnumerateDirectories(_localCollectionFolder))
			{
				try
				{
					var fileName = Path.GetFileName(path);
					var status = GetBookStatusJsonFromRepo(fileName);
					if (status == null)
					{
						if (firstTimeJoin)
						{
							// We want to copy all local books into the repo
							PutBook(path, true);
							continue;
						}
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
							if (statusLocal.lockedBy != TeamCollectionManager.CurrentUser
							    || statusLocal.lockedWhere != Environment.MachineName)
							{
								progress.Message("DeleteLocal", "{0} is a filename",
									String.Format("Deleting '{0}' from local folder as it is no longer in the Team Collection", fileName), MessageKind.Progress);
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
					progress.MessageWithoutLocalizing(msg, MessageKind.Error);
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
					var msg = String.Format(
						"Fetching a new book '{0}' from the Team Collection", bookName);
					progress.MessageWithoutLocalizing(msg);
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
						// The remote book has the same name as a local book that is not known to be in the team collection.
						if (firstTimeJoin)
						{
							// We don't know the previous history of the collection. Quite likely it was duplicated some
							// other way and these books have been edited independently. Treat it as a conflict.
							PutBook(localFolderPath, inLostAndFound: true);
							// warn the user
							var msgTemplate = LocalizationManager.GetString("TeamCollection.ConflictingCheckout",
								"Found different versions of '{0}' in both collections. The team version has been copied to your local collection, and the old local version to Lost and Found");
							var msg = String.Format(msgTemplate, bookName);
							warnings.Add(msg);
							progress.MessageWithoutLocalizing(msg, MessageKind.Warning);
							// Make the local folder match the repo (this is where 'they win')
							CopyBookFromSharedToLocal(bookName);
							continue;
							}
						else
						{
							// Presume it is newly created locally, coincidentally with the same name. Move the new local book
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
							var msg = String.Format(
								"Renaming the local book '{0}' because there is a new one with the same name from the Team Collection",
								bookName);
							progress.MessageWithoutLocalizing(msg);
							RobustFile.Move(oldBookPath, renamePath);

							CopyBookFromSharedToLocal(bookName); // Get shared book and status
							// Review: does this deserve a warning?
							continue;
						}
					}
				}

				// We know there's a local book by this name and both have status.
				var localStatus = GetLocalStatus(bookName);

				if (!IsCheckedOutHereBy(localStatus)) {
					if (localStatus.checksum != sharedStatus.checksum)
					{
						// Changed and not checked out. Just bring it up to date.
						var msg = String.Format(
							"Updating '{0}' to match the Team Collection", bookName);
						progress.MessageWithoutLocalizing(msg);
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
					if (String.IsNullOrEmpty(sharedStatus.lockedBy))
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
							PutBook(localFolderPath, inLostAndFound: true);
							// warn the user
							var msgTemplate = LocalizationManager.GetString("TeamCollection.ConflictingCheckout",
								"The book '{0}', which you have checked out and edited, is checked out to someone else in the team collection. Your changes have been overwritten, but are saved to Lost-and-found.");
							var msg = String.Format(msgTemplate, bookName);
							warnings.Add(msg);
							progress.MessageWithoutLocalizing(msg, MessageKind.Warning);
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
						var msg1 = String.Format(
							"Updating '{0}' to match the Team Collection", bookName);
						progress.MessageWithoutLocalizing(msg1);
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
						"The book '{0}', which you have checked out and edited, was modified in the team collection by someone else. Your changes have been overwritten, but are saved to Lost-and-found.");
					var msg = String.Format(msgTemplate, bookName);
					progress.MessageWithoutLocalizing(msg, MessageKind.Warning);
					warnings.Add(msg);
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
					progress.MessageWithoutLocalizing(msg, MessageKind.Error);
				}
			}

			return warnings;
		}

		// must match what is in IndependentProgressDialog.tsx passed as clientContext to ProgressBox.
		// (At least until we generalize that dialog for different Progress tasks...then, it will need
		// to be configured to use this.)
		private const string kWebSocketContext = "teamCollectionMerge";

		public BloomWebSocketServer SocketServer;



		/// <summary>
		/// Main entry point called before creating CollectionSettings; updates local folder to match
		/// shared one, if any. Not unit tested, as it mainly handles wrapping SyncAtStartup with a
		/// progress dialog.
		/// </summary>
		public void SynchronizeSharedAndLocal()
		{
			var url = BloomFileLocator.GetBrowserFile(false, "utils", "IndependentProgressDialog.html").ToLocalhost()
			          + "?title=Team Collection Activity";
			var progress = new WebSocketProgress(SocketServer, kWebSocketContext);
			var logPath = Path.ChangeExtension(_localCollectionFolder + " Sync", "log");
			Program.CloseSplashScreen(); // Enhance: maybe not right away? Maybe we can put our dialog on top? But it seems to work pretty well...
			using (var progressLogger = new ProgressLogger(logPath, progress))
			using (var dlg = new BrowserDialog(url))
			{
				dlg.WebSocketServer = SocketServer;
				dlg.Width = 500;
				dlg.Height = 300;
				// We REALLY don't want this dialog getting closed before the background task finishes.
				// Waiting for this dialog to close is what keeps this thread from proceeding, typically
				// to load the collection.
				// Having a background task manipulating files in the collection while Bloom is loading
				// it would be a recipe for rare race-condition bugs we can't reproduce.
				dlg.ControlBox = false;
				// With no title and no other title bar controls, the title bar disappears (good!) but
				// we can't drag the dialog (bad!). (We don't WANT a title because we're doing a prettier
				// one in HTML.) For now we decided to go with 'no drag'.
				//dlg.Text = "  ";
				var worker = new BackgroundWorker();
				worker.DoWork += (sender, args) =>
				{
					// A way of waiting until the dialog is ready to receive progress messages
					while (!SocketServer.IsSocketOpen(kWebSocketContext))
						Thread.Sleep(50);
					var now = DateTime.Now;
					// Not useful to have the date and time in the progress dialog, but definitely
					// handy to record at the start of each section in the saved log. Tells us when anything it
					// had to do to sync things actually happened.
					progress.Message("StartingSync", "",
						"Starting sync with Team Collection", MessageKind.Progress);
					progressLogger.Log("Starting sync with Team Collection at " + now.ToShortDateString() + " " +  now.ToShortTimeString(),
						MessageKind.Progress);

					bool doingJoinCollectionMerge = TeamCollectionManager.NextMergeIsJoinCollection;
					TeamCollectionManager.NextMergeIsJoinCollection = false;
					// don't want messages about the collection being changed while we're synchronizing,
					// and in some cases we might be the source of several changes (for example, multiple
					// check ins while joining a collection). Normally we suppress notifications for
					// our own checkins, but remembering the last thing we checked in might not be
					// enough when we do several close together.
					StopMonitoring();

					var problems = SyncAtStartup(progressLogger, doingJoinCollectionMerge);

					// It's just possible there are one, or even more, file change notifications we
					// haven't yet received from the OS. Wait till things settle down to start monitoring again.
					Application.Idle += StartMonitoringOnIdle;

					progress.Message("Done", "Done");

					// Review: are any of the cases we don't treat as warnings or errors important enough to wait
					// for the user to read them and close the dialog manually?
					if (problems.Count > 0)
					{
						// Now the user is allowed to close the dialog or report problems.
						// (IndependentProgressDialog in JS-land is watching for this message, which causes it to turn
						// on the buttons that allow the dialog to be manually closed (or a problem to be reported).
						SocketServer.SendBundle(kWebSocketContext, "show-buttons", new DynamicJson());
					}
					else
					{
						// Nothing very important...close it automatically.
						dlg.Invoke((Action)(() =>
						{
							dlg.Close();
						}));
					}

				};

				worker.RunWorkerAsync();
				dlg.ShowDialog();
			}
		}

		private void StartMonitoringOnIdle(object sender, EventArgs e)
		{
			Application.Idle -= StartMonitoringOnIdle;
			StartMonitoring();
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposing)
			{
				if (_monitoring)
				{
					StopMonitoring();
				}
			}
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		~TeamCollection()
		{
			Dispose(false);
		}

		/// <summary>
		/// Given the name (with or without preceding path) of a team collection folder,
		/// or at least the folder that contains the .JoinBloomTC file,
		/// get the name of the corresponding .bloomCollection file. Currently this involves
		/// removing the trailing " - TC" if present, and adding the .bloomcollection
		/// extension.
		/// This is something of a leak of knowledge about the FolderTeamCollection
		/// implementation into the TeamCollection class. Unfortunately it is used by
		/// two necessarily static methods involved in handling .JoinBloomTC...methods
		/// invoked before we are able to create an instance. It may be that these methods,
		/// and everything to do with .JoinBloomTC, will get moved down into FolderTeamCollection.
		/// Perhaps a completely different strategy will be used to join a TeamCollection
		/// not implemented as a shared folder, if we ever make such an implementation.
		/// However, most of the logic involved in joining a collection is common, so I hate
		/// to do that. Inclined to wait until we DO have an alternative implementation,
		/// when it may be clearer how to refactor.
		/// If we ever get another implementation that dose use .JoinBloomTC, thought will be needed as to
		/// how to get a local collection name from the .JoinBloomTC file path.
		/// </summary>
		/// <param name="tcName"></param>
		/// <returns></returns>
		public static string GetLocalCollectionNameFromTcName(string tcName)
		{
			if (tcName.EndsWith(" - TC"))
				return tcName.Substring(0, tcName.Length - 5);
			return tcName;
		}
	}
}
