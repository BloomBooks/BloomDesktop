using Bloom.Api;
using Bloom.MiscUI;
using Bloom.web;
using L10NSharp;
using Sentry;
using SIL.IO;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Registration;
using Bloom.ToPalaso;
using Bloom.web.controllers;
using SIL.Reporting;

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
		protected readonly ITeamCollectionManager _tcManager;
		private readonly TeamCollectionMessageLog _tcLog;
		protected readonly string _localCollectionFolder; // The unshared folder that this collection syncs with
		// These arrive on background threads (currently from a FileSystemWatcher), but we want to process them
		// in idle time on the main UI thread.
		private ConcurrentQueue<RepoChangeEventArgs> _pendingRepoChanges = new ConcurrentQueue<RepoChangeEventArgs>();

		// When we last prompted the user to restart (due to a change in the Team Collection)
		private DateTime LastRestartPromptTime { get; set; } = DateTime.MinValue;

		// Two minutes is arbitrary, and probably not long enough if changes are coming in frequently from outside.
		private static readonly TimeSpan kMaxRestartPromptFrequency = new TimeSpan(0, 2, 0);

		internal string LocalCollectionFolder => _localCollectionFolder;

		protected bool _updatingCollectionFiles;

		public TeamCollection(ITeamCollectionManager manager, string localCollectionFolder,
			TeamCollectionMessageLog tcLog = null)
		{
			_tcManager = manager;
			_localCollectionFolder = localCollectionFolder;
			_tcLog = tcLog ?? new TeamCollectionMessageLog(TeamCollectionManager.GetTcLogPathFromLcPath(localCollectionFolder));
		}

		public TeamCollectionMessageLog MessageLog => _tcLog;

		/// <summary>
		/// The folder-implementation-specific part of PutBook, the public method below.
		/// Exactly how it is written is implementation specific, but GetBook must be able to get it back.
		/// </summary>
		/// <param name="sourceBookFolderPath">See PutBook</param>
		/// <param name="newStatus">Updated status to write in new book</param>
		/// <param name="inLostAndFound">See PutBook</param>
		/// <remarks>Usually PutBook should be used; this method is meant for use by TeamCollection methods.</remarks>
		protected abstract void PutBookInRepo(string sourceBookFolderPath, BookStatus newStatus, bool inLostAndFound = false);

		public bool OkToCheckIn(string bookName)
		{
			if (!TeamCollectionManager.IsRegistrationSufficient())
				return false; // under no circumstances allow checkin if we don't know who is doing it.
			var repoStatus = GetStatus(bookName);
			if (repoStatus.lockedBy == TeamCollection.FakeUserIndicatingNewBook)
				return true; // we can always check in a book that isn't in the repo at all.
			var localStatus = GetLocalStatus(bookName);
			if (repoStatus.checksum != localStatus.checksum)
			{
				// The book has changed elsewhere while we were editing.
				// We should not overwrite those changes.
				return false;
			}

			if (repoStatus.lockedBy == TeamCollectionManager.CurrentUser
			    && repoStatus.lockedWhere == TeamCollectionManager.CurrentMachine)
			{
				// normal case, repo still shows it checked out here.
				return true;
			}

			if (string.IsNullOrEmpty(repoStatus.lockedBy))
			{
				// Someone else stole the checkout, but they have undone that without changing
				// the book. We can go ahead.
				return true;
			}
			// It's checked out somewhere else according to the repo. They haven't changed it yet,
			// but the repo says they have the right to.
			return false;
		}

		/// <summary>
		/// Put the book into the repo. Usually includes unlocking it. Its new status, with new checksum,
		/// is written to the repo and also to a file in the local collection for later comparisons.
		/// </summary>
		/// <param name="folderPath">The root folder for the book, typically ending in its title,
		///     typically in the current collection folder.</param>
		/// <param name="checkin">If true, the book will no longer be checked out</param>
		/// <param name="inLostAndFound">If true, put the book into the Lost-and-found folder,
		///     if necessary generating a unique name for it. If false, put it into the main repo
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
			var oldName = GetLocalStatus(bookFolderName).oldName;
			PutBookInRepo(folderPath, status, inLostAndFound);
			// We want the local status to reflect the latest repo status.
			// In particular, it should have the correct checksum, and if
			// we've renamed the book, we should no longer record the old name.
			// For one thing, we're about to delete that repo file, so we don't need
			// its name any more. For another, if someone creates a book by the old name,
			// we don't want it to get deleted the next time this one is checked in.
			// For a third, if we rename this book again, we need to record the
			// current repo name as the thing to clean up, not something it once was.
			// All this is achieved by writing the new repo status to local, since we just
			// gave it the right checksum, and the repo status never has oldName.
			WriteLocalStatus(bookFolderName, status);
			if (!string.IsNullOrEmpty(oldName))
			{
				RemoveBook(oldName);
			}
			UpdateBookStatus(bookFolderName, true);
			return status;
		}

		public abstract void RemoveBook(string bookName);

		/// <summary>
		/// Sync every book from the local collection (every folder that has a corresponding htm file) from local
		/// to repo, unless status files exist indicating they are already in sync. (This is typically used when
		/// creating a TeamCollection from an existing local collection. Usually it is a new folder and all
		/// books are copied.)
		/// </summary>
		public void SynchronizeBooksFromLocalToRepo(IWebSocketProgress progress)
		{
			foreach (var path in Directory.EnumerateDirectories(_localCollectionFolder))
			{
				try
				{
					var bookFolderName = Path.GetFileName(path);
					var status = GetStatus(bookFolderName);
					var localStatus = GetLocalStatus(bookFolderName);
					var localHtmlFilePath = Path.Combine(path, BookStorage.FindBookHtmlInFolder(path));
					if ((status?.checksum == null || status.checksum != localStatus?.checksum) && RobustFile.Exists(localHtmlFilePath))
					{
						progress.MessageWithParams("SendingFile", "", "Adding {0} to the collection", MessageKind.Progress, bookFolderName);
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

		// Return a list of all the books in the collection (the repo one, not
		// the local one)
		public abstract string[] GetBookList();

		// Unzip one book in the collection to the specified destination. Usually GetBook should be used
		protected abstract void FetchBookFromRepo(string destinationCollectionFolder, string bookName);

		public void CopyBookFromRepoToLocal(string bookName, string destinationCollectionFolder = null)
		{
			FetchBookFromRepo(destinationCollectionFolder ?? _localCollectionFolder, bookName);
			WriteLocalStatus(bookName, GetStatus(bookName), destinationCollectionFolder ?? _localCollectionFolder);
		}

		// Write the specified file to the repo's collection files.
		public abstract void PutCollectionFiles(string[] names);

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

			// Set up monitoring for the local folder. Here we are looking for changes
			// to collection-level files that need to be saved to the repo.
			// Watching for changes to the repoFolder (or other similar store) are handled
			// by an override in the concrete subclass.
			_localFolderWatcher = new FileSystemWatcher();

			_localFolderWatcher.Path = _localCollectionFolder;
			// for AllowedWords, etc, though unfortunately it means we also get notifications
			// for changes within book folders.
			_localFolderWatcher.IncludeSubdirectories = true;

			// Watch for changes in LastWrite times. This seems to include creating files.
			// Conceivably we should do something to make sure we also see deletions.
			_localFolderWatcher.NotifyFilter = NotifyFilters.LastWrite;

			_localFolderWatcher.Changed += OnChanged;
			_localFolderWatcher.Created += OnChanged;

			// Begin watching.
			_localFolderWatcher.EnableRaisingEvents = true;
		}

		private void OnChanged(object sender, FileSystemEventArgs e)
		{
			// If it's in a book folder (any subfolder except the two we save as settings) ignore it.
			if (e.Name.Contains(Path.DirectorySeparatorChar))
			{
				if (!e.Name.StartsWith("Allowed Words") && !e.Name.StartsWith("Sample Texts"))
					return;
			}

			if (e.Name == kLastcollectionfilesynctimeTxt)
				return; // side effect of doing a sync!
			if (Directory.Exists(e.FullPath))
				return; // we seem to get frequent notifications that seem to be spurious for book folders.
			// We'll wait for the system to be idle before writing to the repo. This helps to ensure things
			// are in a consistent state, as we may get multiple write notifications during the process of
			// writing a file. It may also help to ensure that repo writing doesn't interfere somehow with
			// whatever is changing things.
			// (Form.ActiveForm should not be null in normal use. However, it can be when we're displaying
			// a page in Firefox.)
			if (Form.ActiveForm != null)
			{
				SafeInvoke.InvokeIfPossible("Add SyncCollectionFilesToRepoOnIdle", Form.ActiveForm, false,
					(Action) (() =>
						// Needs to be invoked on the main thread in order for the event handler to be invoked.
						Application.Idle += SyncCollectionFilesToRepoOnIdle
					));
			}
		}

		/// <summary>
		/// Returns true if the book must be checked out before editing it (etc.),
		/// that is, if it is NOT already checked out on this machine by this user.
		/// </summary>
		/// <param name="bookFolderPath"></param>
		/// <returns></returns>
		public bool NeedCheckoutToEdit(string bookFolderPath)
		{
			return !IsCheckedOutHereBy(GetStatus(Path.GetFileName(bookFolderPath)));
		}

		public abstract void DeleteBookFromRepo(string bookFolderPath);

		private void SyncCollectionFilesToRepoOnIdle(object sender, EventArgs e)
		{
			Application.Idle -= SyncCollectionFilesToRepoOnIdle;
			// We want to do this after all changes are finished.
			SyncLocalAndRepoCollectionFiles(false);
		}

		/// <summary>
		/// Stop monitoring (new and changed book notifications will no longer be used).
		/// </summary>
		protected virtual internal void StopMonitoring()
		{
			_monitoring = false;
			if (_localFolderWatcher != null)
			{
				_localFolderWatcher.EnableRaisingEvents = false;
				_localFolderWatcher.Dispose();
				_localFolderWatcher = null;
			}
		}

		/// <summary>
		/// Common part of getting book status as recorded in the repo, or if it is not in the repo
		/// but there is such a book locally, treat as locked by FakeUserIndicatingNewBook.
		/// </summary>
		/// <param name="bookFolderName"></param>
		/// <returns></returns>
		public BookStatus GetStatus(string bookFolderName)
		{
			var statusString = GetBookStatusJsonFromRepo(bookFolderName);
			if (String.IsNullOrEmpty(statusString))
			{
				// a book that doesn't exist (by this name) in the repo should only exist locally if it's new
				// (no status file) or renamed (the corresponding repo file is called oldName).
				var statusFilePath = GetStatusFilePath(bookFolderName, _localCollectionFolder);
				if (File.Exists(statusFilePath))
				{
					// The book has to have been renamed since it has a status file.
					// Or maybe it's been removed remotely, but the local collection hasn't caught up...
					var bookStatus = BookStatus.FromJson(RobustFile.ReadAllText(statusFilePath, Encoding.UTF8));
					if (!String.IsNullOrEmpty(bookStatus.oldName))
					{
						// Use the book's original name to access the repo status.  (BL-9680)
						statusString = GetBookStatusJsonFromRepo(bookStatus.oldName);
						if (!String.IsNullOrEmpty(statusString))
							return BookStatus.FromJson(statusString);
					}
					// This is a bizarre situation that should get corrected the next time Bloom starts up.
					// For now, just return what we have by way of local status.
					return bookStatus;
				}
				else if (Directory.Exists(Path.GetDirectoryName(statusFilePath)))
				{
					// book exists only locally. Treat as checked out to FakeUserIndicatingNewBook
					return new BookStatus() { lockedBy = FakeUserIndicatingNewBook, lockedWhere = TeamCollectionManager.CurrentMachine };
				}
				return new BookStatus();
			}
			return BookStatus.FromJson(statusString);
		}

		/// <summary>
		/// Write the book's status, both in repo and in its own folder
		/// </summary>
		public void WriteBookStatus(string bookName, BookStatus status)
		{
			WriteBookStatusJsonToRepo(bookName, status.ToJson());
			WriteLocalStatus(bookName, status);
		}

		/// <summary>
		/// Get the raw status data from however the repo implementation stores it.
		/// </summary>
		protected abstract string GetBookStatusJsonFromRepo(string bookFolderName);

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
		/// PutBook or SetStatus code). This is a low-level event that is currently normally
		/// only used to push data into the _pendingRepoChanges queue. Add handlers to this
		/// cautiously...they are raised on background threads. Usually it is better to subscribe
		/// to the BookStatusChangeEvent (available from AutoFac), which is raised during idle
		/// time on the UI thread (by pulling from the queue) after we have done some basic
		/// analysis of the effect of the change.
		/// </summary>
		public event EventHandler<BookRepoChangeEventArgs> BookRepoChange;

		public event EventHandler<EventArgs> RepoCollectionFilesChanged;

		/// <summary>
		/// Get all the books in the repo copied into the local folder.
		/// </summary>
		/// <param name="destinationCollectionFolder">Default null means the local collection folder.</param>
		public void CopyAllBooksFromRepoToLocalFolder(string destinationCollectionFolder = null)
		{
			var dest = destinationCollectionFolder ?? _localCollectionFolder;
			foreach (var bookName in GetBookList())
			{
				CopyBookFromRepoToLocal(bookName, dest);
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
			if (!PromptForSufficientRegistrationIfNeeded())
				return false;

			var whoBy = email ?? TeamCollectionManager.CurrentUser;
			var status = GetStatus(bookName);
			if (String.IsNullOrEmpty(status.lockedBy))
			{
				status = status.WithLockedBy(whoBy, TeamCollectionManager.CurrentUserFirstName, TeamCollectionManager.CurrentUserSurname);
				WriteBookStatus(bookName, status);
			}

			// If we succeeded, we definitely want various things to update to show it.
			// But there may be status changes to show if we failed, too...for example,
			// probably it's because the book was discovered to be checked out to
			// someone else, and we'd like things to show that.
			UpdateBookStatus(bookName, true);

			return IsCheckedOutHereBy(status, whoBy);
		}

		// Get the email of the user, if any, who has the book locked. Returns null if not locked.
		// As a special case, if the book exists only locally, we return TeamRepo.kThisUser.
		public string WhoHasBookLocked(string bookName)
		{
			return GetStatus(bookName).lockedBy;
		}

		// Get the first name of the user, if any, who has the book locked.
		// Returns null if not locked or we don't know the first name.
		public string WhoHasBookLockedFirstName(string bookName)
		{
			if (WhoHasBookLocked(bookName) == FakeUserIndicatingNewBook)
				return TeamCollectionManager.CurrentUserFirstName;
			return GetStatus(bookName).lockedByFirstName;
		}

		// Get the surname of the user, if any, who has the book locked.
		// Returns null if not locked or we don't know the surname.
		public string WhoHasBookLockedSurname(string bookName)
		{
			if (WhoHasBookLocked(bookName) == FakeUserIndicatingNewBook)
				return TeamCollectionManager.CurrentUserSurname;
			return GetStatus(bookName).lockedBySurname;
		}

		// Gives the time when someone locked the book. DateTime.MaxValue if not locked.
		public DateTime WhenWasBookLocked(string bookName)
		{
			var status = GetStatus(bookName);
			if (DateTime.TryParse(status.lockedWhen, out var result))
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

		private bool _haveShownRemoteSettingsChangeWarning;

		/// <summary>
		/// Bring the collection-level files in the repo and the local collection into sync.
		/// If the repo has been changed since the last sync, update the local files.
		/// Otherwise, if local files have been changed since the last sync, update the repo.
		/// (Updating the repo will eventually be conditional on a permission file.)
		/// This is done both when Bloom is starting up, before we create the local collection
		/// file, and when we detect a possibly-significant change to a local file while Bloom
		/// is running. Only at startup are we allowed to copy changes TO the local system,
		/// since changes to the collection-level files normally require a restart.
		/// </summary>
		/// <param name="atStartup"></param>
		public void SyncLocalAndRepoCollectionFiles(bool atStartup = true)
		{
			var repoModTime = LastRepoCollectionFileModifyTime;
			var savedSyncTime = LocalCollectionFilesRecordedSyncTime();
			if (repoModTime > savedSyncTime)
			{
				// We only modify local stuff at startup.
				if (atStartup)
				{
					// Theoretically, it's possible that localModTime is also greater than savedLocalModTime.
					// However, we monitor local changes and try to save them immediately, so it's highly unlikely,
					// except when the warning below has already been seen.
					CopyRepoCollectionFilesToLocal(_localCollectionFolder);
				}
				else if (!_haveShownRemoteSettingsChangeWarning)
				{
					_haveShownRemoteSettingsChangeWarning = true;
					// if it's not a startup sync, it's happening because of a local change. It will get lost.
					// Not sure this is worth localizing. Eventually only one or two users per collection will be
					// allowed to make such changes. Collection settings should rarely be changed at all
					// in team collections. This message will hopefully be seen rarely if at all.
					ErrorReport.NotifyUserOfProblem(
						"Collection settings have been changed remotely. Your recent changes will be lost when Bloom syncs the next time it starts up");
				}
			}
			else if (LocalCollectionFilesUpdated())
			{
				CopyRepoCollectionFilesFromLocal(_localCollectionFolder);
			}
			// Otherwise, nothing has changed since we last synced. Do nothing.
		}

		/// <summary>
		/// The files that we consider to be collection-wide settings files.
		/// These are
		/// - ones we care about changes to for deciding whether to Sync collection files to the repo
		/// - ones we will delete when syncing from the repo to local, if no longer in the repo
		/// </summary>
		/// <returns></returns>
		internal List<string> FilesToMonitorForCollection()
		{
			var collectionName = Path.GetFileName(_localCollectionFolder);
			var files = RootLevelCollectionFilesIn(_localCollectionFolder, collectionName);
			AddFiles(files, "Allowed Words");
			AddFiles(files, "Sample Texts");
			return files.Select(f => Path.Combine(_localCollectionFolder, f)).ToList();
		}

		private void AddFiles(List<string> accumulator, string folderName)
		{
			var folderPath = Path.Combine(_localCollectionFolder, folderName);
			if (!Directory.Exists(folderPath))
				return;
			accumulator.AddRange(Directory.EnumerateFiles(folderPath));
		}

		/// <summary>
		/// Name of a file we maintain to reduce spurious writes to repo collection files.
		/// </summary>
		const string kLastcollectionfilesynctimeTxt = "lastCollectionFileSyncData.txt";

		private string GetCollectionFileSyncLocation()
		{

			return Path.Combine(_localCollectionFolder, kLastcollectionfilesynctimeTxt);
		}

		private string MakeChecksumOnFiles(IEnumerable<string> files)
		{
			var sha = SHA256Managed.Create();

			// Order must be predictable but does not otherwise matter.
			foreach (var path in files.OrderBy(x => x))
			{
				using (var input = new FileStream(path, FileMode.Open))
				{
					byte[] buffer = new byte[4096];
					int count;
					while ((count = input.Read(buffer, 0, 4096)) > 0)
					{
						sha.TransformBlock(buffer, 0, count, buffer, 0);
					}
				}
			}

			sha.TransformFinalBlock(new byte[0], 0, 0);
			return Convert.ToBase64String(sha.Hash);
		}

		private void RecordCollectionFilesSyncData()
		{
			var files = FilesToMonitorForCollection();
			var checksum = MakeChecksumOnFiles(files);
			RecordCollectionFilesSyncDataInternal(checksum);
		}

		/// <summary>
		/// Record two pieces of data useful for determining whether collection-level files
		/// have changed, given that they are currently in sync. We record the current time,
		/// which allows an efficient check that nothing has changed, if none of the interesting
		/// files has a later modify time. However, Bloom rather too frequently writes files
		/// without really changing anything. To guard against this, we also record a sha of
		/// the files, and only consider that something has really changed if the sha does
		/// not match.
		/// </summary>
		/// <param name="checksum"></param>
		private void RecordCollectionFilesSyncDataInternal(string checksum)
		{
			var path = GetCollectionFileSyncLocation();
			var nowString = DateTime.UtcNow.ToString("o"); // good for round-tripping
			File.WriteAllText(path, nowString + @";" + checksum);
		}

		/// <summary>
		/// Return true if local collection-level files have changed and need to be copied
		/// to the repo. Usually we can determine this by a quick check of modify times.
		/// If that indicates a change, we verify it by comparing sha values.
		/// (If we need to check sha values and determine that there is NOT a real change,
		/// we update the time record to make the next check faster.)
		/// </summary>
		/// <returns></returns>
		internal bool LocalCollectionFilesUpdated()
		{
			var files = FilesToMonitorForCollection();
			var localModTime = files.Select(f => new FileInfo(f).LastWriteTime).Max();
			var savedModTime = LocalCollectionFilesRecordedSyncTime();
			if (localModTime <= savedModTime)
				return false;
			var currentChecksum = MakeChecksumOnFiles(files);
			var localFilesReallyUpdated = currentChecksum != LocalCollectionFilesSavedChecksum();
			if (!localFilesReallyUpdated && savedModTime >= LastRepoCollectionFileModifyTime)
			{
				// no need to sync either way; we can update the file to save computing
				// the checksum next time.
				// Review: Technically there may be a race condition here where the repo
				// collection gets modified between when we read LastRepoCollectionFileModifyTime
				// and the new sync time we are here writing. I think the chance is small
				// enough to live with. A wise team should be coordinating changes at the
				// collection level anyway.
				RecordCollectionFilesSyncDataInternal(currentChecksum);
			}
			return localFilesReallyUpdated;
		}

		internal DateTime LocalCollectionFilesRecordedSyncTime()
		{
			var path = GetCollectionFileSyncLocation();
			if (!File.Exists(path))
				return DateTime.MinValue; // assume local files are really old!
			DateTime result;
			if (DateTime.TryParse(File.ReadAllText(path).Split(';')[0], out result))
				return result;
			return DateTime.MinValue;
		}

		internal string LocalCollectionFilesSavedChecksum()
		{
			var path = GetCollectionFileSyncLocation();
			if (!File.Exists(path))
				return "";
			var parts = File.ReadAllText(path).Split(';');
			if (parts.Length > 1)
				return parts[1];
			return "";
		}

		/// <summary>
		/// Get anything we need from the repo to the local folder (except actual books).
		/// Enhance: at least, also whatever we need for decodable and leveled readers.
		/// Most likely we should have a way to ask the repo for all non-book content and
		/// retrieve it all.
		/// </summary>
		public void CopyRepoCollectionFilesToLocal(string destFolder)
		{
			var wasMonitoring = _localFolderWatcher != null && _localFolderWatcher.EnableRaisingEvents;
			if (_localFolderWatcher != null)
				_localFolderWatcher.EnableRaisingEvents = false;
			try
			{
				CopyRepoCollectionFilesToLocalImpl(destFolder);
				RecordCollectionFilesSyncData();
			} finally
			{
				if (_localFolderWatcher != null)
					_localFolderWatcher.EnableRaisingEvents = wasMonitoring;
			}
		}
		protected abstract void CopyRepoCollectionFilesToLocalImpl(string destFolder);

		/// <summary>
		/// Gets the path to the bloomCollection file, given the folder.
		/// If the folder name ends in " - TC" we will strip that off.
		/// </summary>
		/// <param name="parentFolder"></param>
		/// <returns></returns>
		public static string CollectionPath(string parentFolder)
		{
			var collectionName = GetLocalCollectionNameFromTcName(Path.GetFileName(parentFolder));
			// Avoiding use of ChangeExtension as it's just possible the collectionName could have period.
			var collectionPath = Path.Combine(parentFolder, collectionName + ".bloomCollection");
			if (File.Exists(collectionPath))
				return collectionPath;
			// occasionally, mainly when making a temp folder during joining, the bloomCollection file may not
			// have the expected name
			var result = Directory.EnumerateFiles(parentFolder, "*.bloomCollection").FirstOrDefault();
			if (result == null)
				return collectionPath; // sometimes we use this method to get the expected path where there is no .bloomCollection
			return result;
		}

		public static List<string> RootLevelCollectionFilesIn(string folder, string collectionNameOrNull = null)
		{
			var collectionName = collectionNameOrNull ?? Path.GetFileName(folder);
			var files = new List<string>();
			files.Add(Path.GetFileName(CollectionPath(folder)));
			foreach (var file in new[] {"customCollectionStyles.css", "configuration.txt"})
			{
				if (File.Exists(Path.Combine(folder, file)))
					files.Add(file);
			}
			foreach (var path in Directory.EnumerateFiles(folder, "ReaderTools*.json"))
			{
				files.Add(Path.GetFileName(path));
			}
			return files;
		}

		protected abstract DateTime LastRepoCollectionFileModifyTime { get; }

		/// <summary>
		/// Gets the book name without the .bloom suffix
		/// </summary>
		/// <param name="bookName">A book name, with our without the .bloom suffix</param>
		/// <returns>A string which is the book name without the .bloom suffix</returns>
		protected static string GetBookNameWithoutSuffix(string bookName)
		{
			if (bookName.EndsWith(".bloom"))
				return Path.GetFileNameWithoutExtension(bookName);

			return bookName;
		}

		/// <summary>
		/// Send anything other than books that should be shared from local to the repo.
		/// Enhance: as for CopyRepoCollectionFilesToLocal, also, we want this to be
		/// restricted to a specified set of emails, by default, the creator of the repo.
		/// </summary>
		/// <param name="localCollectionFolder"></param>
		public void CopyRepoCollectionFilesFromLocal(string localCollectionFolder)
		{
			try
			{
				_updatingCollectionFiles = true;
				var collectionName = Path.GetFileName(localCollectionFolder);
				var files = RootLevelCollectionFilesIn(localCollectionFolder, collectionName);
				// Review: there would be some benefit to atomicity in saving all of this to a single zip file.
				// But it feels cleaner to have a distinct repo store for each folder we need.
				PutCollectionFiles(files.ToArray());
				CopyLocalFolderToRepo("Allowed Words");
				CopyLocalFolderToRepo("Sample Texts");
				RecordCollectionFilesSyncData();
			}
			finally
			{
				_updatingCollectionFiles = false;
			}
		}

		protected abstract void CopyLocalFolderToRepo(string folderName);

		protected void RaiseNewBook(string bookName)
		{
			NewBook?.Invoke(this, new NewBookEventArgs() {BookFileName = bookName});
		}

		/// <param name="bookFileName">The book name, including the .bloom suffix</param>
		protected void RaiseBookStateChange(string bookFileName)
		{
			BookRepoChange?.Invoke(this, new BookRepoChangeEventArgs() { BookFileName = bookFileName });
		}

		protected void RaiseRepoCollectionFilesChanged()
		{
			RepoCollectionFilesChanged?.Invoke(this, new EventArgs());
		}

		/// <summary>
		/// Gets things going so that Bloom will be notified of remote changes to the repo.
		/// </summary>
		public void SetupMonitoringBehavior()
		{
			NewBook += (sender, args) => { QueuePendingBookChange(args); };
			BookRepoChange += (sender, args) => QueuePendingBookChange(args);
			RepoCollectionFilesChanged += (sender, args) => QueuePendingBookChange(new RepoChangeEventArgs());
			Application.Idle += HandleRemoteBookChangesOnIdle;
			StartMonitoring();
		}

		internal void QueuePendingBookChange(RepoChangeEventArgs args)
		{
			_pendingRepoChanges.Enqueue(args);
		}

		internal void HandleRemoteBookChangesOnIdle(object sender, EventArgs e)
		{
			if(_pendingRepoChanges.TryDequeue(out RepoChangeEventArgs args))
			{
				// _pendingChanges is a single queue of things that happened in the Repo,
				// including both new books arriving and existing books changing.
				// The two event types have different classes of event args, which allows us
				// to split them here and handle each type differently.
				if (args is NewBookEventArgs)
					HandleNewBook((NewBookEventArgs)args);
				else if (args is BookRepoChangeEventArgs)
					HandleModifiedFile((BookRepoChangeEventArgs) args);
				else HandleCollectionSettingsChange(args);
			}
		}

		internal void HandleCollectionSettingsChange(RepoChangeEventArgs result)
		{
			_tcLog.WriteMessage(MessageAndMilestoneType.NewStuff, "TeamCollection.SettingsModifiedRemotely",
				"One of your teammates has made changes to the collection settings.", null, null);
		}

		/// <summary>
		/// Return true if the book is in a state that should cause it to be 'clobbered'...
		/// that is, the local version will be written to Lost and Found and replaced with
		/// the repo version.
		/// This is true if there are local edits and either
		/// (a) the repo checksum is different from the local checksum
		/// (b) the repo lock status is not locked out here.
		/// </summary>
		/// <param name="bookName"></param>
		/// <returns></returns>
		public bool HasLocalChangesThatMustBeClobbered(string bookName)
		{
			var localStatus = GetLocalStatus(bookName);
			// We don't bother to check for this sort of problem unless we think the book is checked out locally
			// Not being checked out locally should guarantee that it has no local changes.
			if (!IsCheckedOutHereBy(localStatus))
				return false;
			var repoStatus = GetStatus(bookName);
			var currentChecksum = MakeChecksum(Path.Combine(_localCollectionFolder,bookName));
			// If it hasn't actually been edited locally, we might have a problem, but not one that
			// requires clobbering.
			if (repoStatus.checksum == currentChecksum)
				return false;
			// We've checked it out and edited it...there's a problem if the repo disagrees
			// about either content or status
			return (!IsCheckedOutHereBy(repoStatus) || repoStatus.checksum != localStatus.checksum);
		}

		private bool HasCheckoutConflict(string bookName)
		{
			return IsCheckedOutHereBy(GetLocalStatus(bookName)) && !IsCheckedOutHereBy(GetStatus(bookName));
		}

		public bool HasBeenChangedRemotely(string bookName)
		{
			return GetLocalStatus(bookName).checksum != GetStatus(bookName).checksum;
		}

		/// <summary>
		/// Book has a clobber problem...we can't go on editing until we sort it out...
		/// if there are either conflicting edits or conflicting lock status.
		/// </summary>
		public bool HasClobberProblem(string bookName)
		{
			return HasLocalChangesThatMustBeClobbered(bookName) || HasCheckoutConflict(bookName);
		}

		/// <summary>
		/// Handle a notification that a file has been modified. If it's a bloom book file
		/// and there is no problem, add a NewStuff message. If there is a problem,
		/// add an error message. Send an UpdateBookStatus. (If it's the current book,
		/// a handler for book status may upgrade the problem to 'clobber pending'.)
		/// </summary>
		/// <param name="args"></param>
		public void HandleModifiedFile(BookRepoChangeEventArgs args)
		{
			if (args.BookFileName.EndsWith(".bloom"))
			{
				var bookBaseName = GetBookNameWithoutSuffix(args.BookFileName);

				// The most serious concern is that there are local changes to the book that must be clobbered.
				if (HasLocalChangesThatMustBeClobbered(bookBaseName))
				{
					_tcLog.WriteMessage(MessageAndMilestoneType.Error, "TeamCollection.EditedFileChangedRemotely",
						"One of your teammates has modified or checked out the book '{0}', which you have edited but not checked in. You need to reload the collection to sort things out.",
						bookBaseName, null);
				} else

				// A lesser but still Error condition is that the repo has a conflicting notion of checkout status.
				if (HasCheckoutConflict(bookBaseName))
				{
					_tcLog.WriteMessage(MessageAndMilestoneType.Error, "TeamCollection.ConflictingCheckout",
						"One of your teammates has checked out the book '{0}'. This undoes your checkout.",
						bookBaseName, null);

				}
				else if (!Directory.Exists(Path.Combine(_localCollectionFolder, bookBaseName)))
				{
					// No local version at all. Possibly it was just now created, and we will get a
					// new book notification any moment, or already have one. Possibly there have
					// been additional checkins between creation and when this user reloads.
					// In any case, we don't need any new messages or status change beyond
					// the NewBook message that should be generated at some point.
					return;
				}
				else if (HasBeenChangedRemotely(bookBaseName))
				{
					_tcLog.WriteMessage(MessageAndMilestoneType.NewStuff, "TeamCollection.BookModifiedRemotely",
						"One of your teammates has made changes to the book '{0}'", bookBaseName, null);
				}
				// This needs to be AFTER we update the message log, data which it may use.
				UpdateBookStatus(bookBaseName, true);
			}
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
			var bookBaseName = GetBookNameWithoutSuffix(args.BookFileName);
			if (args.BookFileName.EndsWith(".bloom"))
			{
				_tcLog.WriteMessage(MessageAndMilestoneType.NewStuff, "TeamCollection.NewBookArrived",
					"A new book called '{0}' was added by a teammate.", bookBaseName, null);
			}
			// This needs to be AFTER we update the message log, data which it may use.
			UpdateBookStatus(bookBaseName, true);
		}

		public void HandleBookRename(string oldName, string newName)
		{
			var status = GetLocalStatus(newName); // folder has already moved!
			if (status.lockedBy == TeamCollection.FakeUserIndicatingNewBook || string.IsNullOrEmpty(status.lockedBy))
			{
				// new, no need to delete old in repo; more important, we do NOT want it
				// to have a local status until it is checked in, since that's how we know
				// it is new rather than deleted remotely.
				return;
			}

			if (!string.IsNullOrEmpty(status.oldName))
			{
				// We've already renamed this book in the current session!
				// We need to keep the ORIGINAL name of the repo book that needs deleting.
				return;
			}

			WriteLocalStatus(newName, status.WithOldName(oldName));
		}

		/// <summary>
		/// Returns a string representing a path to the book.status file of the specified book in the specified collection.
		/// </summary>
		/// <param name="bookName">Can have the .bloom extension or not, either way is fine.
		/// Other extensions are not supported, because of the possibility of getting simple
		/// folder names containing periods.</param>
		/// <param name="collectionFolder">The collection that contains the book</param>
		/// <returns></returns>
		internal string GetStatusFilePath(string bookName, string collectionFolder)
		{
			// Don't use GetFileNameWithoutExtension here, what comes in might be a plain folder name
			// that doesn't have an extension, but might contain a period if the book title does.
			var bookFolderName = Path.GetFileName(bookName);
			if (bookFolderName.EndsWith(".bloom"))
				bookFolderName = bookFolderName.Substring(0, bookFolderName.Length - ".bloom".Length);
			var bookFolderPath = Path.Combine(collectionFolder, bookFolderName);
			var statusFile = Path.Combine(bookFolderPath, "book.status");
			return statusFile;
		}

		internal void WriteLocalStatus(string bookFolderName, BookStatus status, string collectionFolder = null)
		{
#if DEBUG
			// Except in unit tests, where we do all sorts of weird things to simulate particular situations,
			// it is VERY bad to give a book a local status file when it is not in the repo. Bloom will
			// delete the book the next time it starts up!
			if (!Program.RunningUnitTests)
			{
				// Check for a book being renamed.
				if (!String.IsNullOrEmpty(status.oldName))
					Debug.Assert(GetBookStatusJsonFromRepo(status.oldName) != null, "Should never write local status for a renamed book that's not in repo for previous name");
				else
					Debug.Assert(GetBookStatusJsonFromRepo(bookFolderName) != null, "Should never write local status for a book that's not in repo");
			}
#endif
			var statusFilePath = GetStatusFilePath(bookFolderName, collectionFolder ?? _localCollectionFolder);
			RobustFile.WriteAllText(statusFilePath, status.ToJson(), Encoding.UTF8);
		}

		internal BookStatus GetLocalStatus(string bookFolderName, string collectionFolder = null)
		{
			var statusFilePath = GetStatusFilePath(bookFolderName, collectionFolder ?? _localCollectionFolder);
			if (File.Exists(statusFilePath))
			{
				return BookStatus.FromJson(RobustFile.ReadAllText(statusFilePath, Encoding.UTF8));
			}
			return new BookStatus();
		}

		// Original calculation, from content, of the version code we store in book status.
		internal static string MakeChecksum(string folderPath)
		{
			var sourceBookName = BookStorage.FindBookHtmlInFolder(folderPath);
			if (string.IsNullOrEmpty(sourceBookName))
			{
				// we sometimes may have an empty folder or one that, as yet, isn't a bloom book at all.
				// An empty checksum will cause it not to match.
				return "";
			}
			var sourceBookPath = Path.Combine(folderPath, sourceBookName);
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
			if (whoBy == null)
				return false;
			return status.IsCheckedOutHereBy(whoBy);
		}

		bool IsBloomBookFolder(string folderPath)
		{
			return !string.IsNullOrEmpty(BookStorage.FindBookHtmlInFolder(folderPath));
		}

		// During Startup, we want messages to go to both the current progress dialog and the permanent
		// change log. This method handles sending to both.
		void ReportProgressAndLog(IWebSocketProgress progress, string l10nIdSuffix, string message,
			string param0 = null, string param1= null, MessageKind kind = MessageKind.Progress)
		{
			var fullL10nId = "TeamCollection." + l10nIdSuffix;
			var msg = string.Format(LocalizationManager.GetString(fullL10nId, message), param0, param1);
			progress.MessageWithoutLocalizing(msg, kind);
			_tcLog.WriteMessage((kind == MessageKind.Progress) ? MessageAndMilestoneType.History : MessageAndMilestoneType.Error,
				fullL10nId, message, param0, param1);
		}

		/// <summary>
		/// Run this when Bloom starts up to get the repo and local directories as sync'd as possible.
		/// Also run when first joining an existing collection to merge them. A few behaviors are
		/// different in this case.
		/// </summary>
		/// <returns>true if progress messages were reported that are severe enough to warrant
		/// keeping the progress dialog open until the user responds</returns>
		public bool SyncAtStartup(IWebSocketProgress progress, bool firstTimeJoin = false)
		{
			_tcLog.WriteMilestone(MessageAndMilestoneType.Reloaded);
			var hasProblems = false; //set true if we get any problems
			// Delete books that we think have been deleted remotely from the repo.
			// If it's a join collection merge, check new books in instead.
			var englishSomethingWrongMessage = "Something went wrong trying to sync with the book {0} in your Team Collection.";
			var oldBookNames = new HashSet<string>();
			foreach (var path in Directory.EnumerateDirectories(_localCollectionFolder))
			{
				try
				{
					if (!IsBloomBookFolder(path))
						continue;
					var bookFolderName = Path.GetFileName(path);
					var statusJson = GetBookStatusJsonFromRepo(bookFolderName);
					if (statusJson == null)
					{
						if (firstTimeJoin)
						{
							// We want to copy all local books into the repo
							PutBook(path, true);
							continue;
						}
						// no sign of book in repo...should we delete it?
						var statusFilePath = GetStatusFilePath(bookFolderName, _localCollectionFolder);
						if (File.Exists(statusFilePath))
						{
							// If there's no local status, presume it's a newly created local file and keep it
							// On this branch, there is local status, so the book has previously been shared.
							// Since it's now missing from the repo, we assume it's been deleted.
							// Unless it's checked out to the current user on the current computer, delete
							// the local version.
							var statusLocal = GetLocalStatus(bookFolderName);
							if (statusLocal.lockedBy != TeamCollectionManager.CurrentUser
							    || statusLocal.lockedWhere != TeamCollectionManager.CurrentMachine)
							{
								ReportProgressAndLog(progress, "DeleteLocal",
									"Deleting '{0}' from local folder as it is no longer in the Team Collection",
									bookFolderName);
								SIL.IO.RobustIO.DeleteDirectoryAndContents(path);
								continue;
							}
							// existing book folder checked out with status file, but nothing matching in repo.
							// Most likely it is in the process of being renamed. In that case, not only
							// should we not delete it, we should avoid re-creating the local book it was
							// renamed from, for which we most likely have a .bloom in the repo.
							// Here we just remember the name.
							var oldName = GetLocalStatus(bookFolderName).oldName;
							if (!string.IsNullOrEmpty(oldName))
							{
								oldBookNames.Add(oldName);
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
					ReportProgressAndLog(progress, "SomethingWentWrong",englishSomethingWrongMessage,
						path, null, MessageKind.Error);
					SentrySdk.AddBreadcrumb(string.Format(englishSomethingWrongMessage, path));
					SentrySdk.CaptureException(ex);
					hasProblems= true;
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
					if (oldBookNames.Contains(bookName))
					{
						// it's a book we're in the process of renaming, but hasn't yet been
						// checked in using the new name. Leave it alone.
						continue;
					}
					// brand new book! Get it.
					ReportProgressAndLog(progress, "FetchedNewBook",
						"Fetching a new book '{0}' from the Team Collection", bookName);
					CopyBookFromRepoToLocal(bookName);
					continue;
				}

				var repoStatus = GetStatus(bookName); // we know it's in the repo, so status will certainly be from there.
				var statusFilePath = GetStatusFilePath(bookName, _localCollectionFolder);
				if (!File.Exists(statusFilePath))
				{
					var currentChecksum = MakeChecksum(localFolderPath);
					if (currentChecksum == repoStatus.checksum)
					{
						// We have the same book with the same name and content in both places, but no local status.
						// Somehow the book was copied not using the TeamCollection. Possibly, we are merging
						// two versions of the same collection. Clean up by copying status.
						WriteLocalStatus(bookName, repoStatus);
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
							hasProblems = true;
							ReportProgressAndLog(progress, "ConflictingCheckout",
								"Found different versions of '{0}' in both collections. The team version has been copied to your local collection, and the old local version to Lost and Found"
								, bookName, null, MessageKind.Error);
							// Make the local folder match the repo (this is where 'they win')
							CopyBookFromRepoToLocal(bookName);
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
							// Don't use ChangeExtension here, bookName may have arbitrary periods.
							var renamePath = Path.Combine(renameFolder, Path.GetFileName(renameFolder) + ".htm");
							var oldBookPath = Path.Combine(renameFolder, bookName +  ".htm");
							ReportProgressAndLog(progress, "RenamingBook",
								"Renaming the local book '{0}' because there is a new one with the same name from the Team Collection",
								bookName);
							RobustFile.Move(oldBookPath, renamePath);

							CopyBookFromRepoToLocal(bookName); // Get repo book and status
							// Review: does this deserve a warning?
							continue;
						}
					}
				}

				// We know there's a local book by this name and both have status.
				var localStatus = GetLocalStatus(bookName);

				if (!IsCheckedOutHereBy(localStatus)) {
					if (localStatus.checksum != repoStatus.checksum)
					{
						// Changed and not checked out. Just bring it up to date.
						ReportProgressAndLog(progress,"Updating",
							"Updating '{0}' to match the Team Collection", bookName);
						CopyBookFromRepoToLocal(bookName); // updates everything local.
					}

					// whether or not we updated it, if it's not checked out there's no more to do.
				    continue;
				}

				// At this point, we know there's a version of the book in the repo
				// and a local version that is checked out here according to local status.
				if (IsCheckedOutHereBy(repoStatus))
				{
					// the repo agrees. We could check that the checksums match, but there's no
					// likely scenario for them not to. Everything is consistent, so we can move on
					continue;
				}

				// Now we know there's some sort of conflict. The local and repo status of this
				// book don't match.
				if (localStatus.checksum == repoStatus.checksum)
				{
					if (String.IsNullOrEmpty(repoStatus.lockedBy))
					{
						// Likely someone started a checkout remotely, but changed their mind without making edits.
						// Just restore our checkout.
						WriteBookStatus(bookName, localStatus);
						continue;
					}
					else
					{
						// Checked out by someone else in the repo folder. They win.
						// Do we need to save local edits?
						var currentChecksum = MakeChecksum(localFolderPath);
						if (currentChecksum != localStatus.checksum)
						{
							// Edited locally while someone else has it checked out. Copy current local to lost and found
							PutBook(localFolderPath, inLostAndFound: true);
							// warn the user
							hasProblems= true;
							ReportProgressAndLog(progress, "ConflictingCheckout",
								"The book '{0}', which you have checked out and edited, is checked out to someone else in the team collection. Your changes have been overwritten, but are saved to Lost-and-found.",
								bookName, null, MessageKind.Error);
							// Make the local folder match the repo (this is where 'they win')
							CopyBookFromRepoToLocal(bookName);
							continue;
						}
						else
						{
							// No local edits yet. Just correct the local checkout status.
							WriteBookStatus(bookName, repoStatus);
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
						ReportProgressAndLog(progress, "Updating",
							"Updating '{0}' to match the Team Collection", bookName);
						CopyBookFromRepoToLocal(bookName);
						WriteBookStatus(bookName, localStatus.WithChecksum(repoStatus.checksum));
						continue;
					}

					// Copy current local to lost and found
					PutBook(Path.Combine(_localCollectionFolder, bookName), inLostAndFound:true);
					// copy repo book and status to local
					CopyBookFromRepoToLocal(bookName);
						// warn the user
						hasProblems = true;
						ReportProgressAndLog(progress, "ConflictingEdit",
						"The book '{0}', which you have checked out and edited, was modified in the team collection by someone else. Your changes have been overwritten, but are saved to Lost-and-found.",
						bookName, null, MessageKind.Error);
						continue;
				}
				}
				catch (Exception ex)
				{
					// Something went wrong with dealing with this book, but we'd like to carry on with
					// syncing the rest of the collection
					ReportProgressAndLog(progress, "SomethingWentWrong", englishSomethingWrongMessage,
						bookName, null, MessageKind.Error);
					SentrySdk.AddBreadcrumb(string.Format(englishSomethingWrongMessage, bookName));
					SentrySdk.CaptureException(ex);
					hasProblems = true;
				}
			}

			if (hasProblems)
			{
				// Not sure this is the best place for this. But currently the warnings/errors are
				// shown in the progress dialog if we return any, so a reasonable assumption is
				// that any we return here are immediately shown.
				_tcLog.WriteMilestone(MessageAndMilestoneType.LogDisplayed);
			}
			return hasProblems;
		}

		// must match what is in IndependentProgressDialog.tsx passed as clientContext to ProgressBox.
		// (At least until we generalize that dialog for different Progress tasks...then, it will need
		// to be configured to use this.)
		internal const string kWebSocketContext = "teamCollectionMerge";

		public BloomWebSocketServer SocketServer;
		private FileSystemWatcher _localFolderWatcher;


		/// <summary>
		/// Main entry point called before creating CollectionSettings; updates local folder to match
		/// repo one, if any. Not unit tested, as it mainly handles wrapping SyncAtStartup with a
		/// progress dialog.
		/// </summary>
		public void SynchronizeRepoAndLocal()
		{
			Program.CloseSplashScreen(); // Enhance: maybe not right away? Maybe we can put our dialog on top? But it seems to work pretty well...

			BrowserProgressDialog.DoWorkWithProgressDialog(SocketServer, TeamCollection.kWebSocketContext,
				"Team Collection Activity",
				progress =>
				{
					var now = DateTime.Now;
					// Not useful to have the date and time in the progress dialog, but definitely
					// handy to record at the start of each section in the saved log. Tells us when anything it
					// had to do to sync things actually happened.
					progress.Message("StartingSync", "",
						"Starting sync with Team Collection", MessageKind.Progress);

					bool doingJoinCollectionMerge = TeamCollectionManager.NextMergeIsJoinCollection;
					TeamCollectionManager.NextMergeIsJoinCollection = false;
					// don't want messages about the collection being changed while we're synchronizing,
					// and in some cases we might be the source of several changes (for example, multiple
					// check ins while joining a collection). Normally we suppress notifications for
					// our own checkins, but remembering the last thing we checked in might not be
					// enough when we do several close together.
					StopMonitoring();

					var waitForUserToCloseDialogOrReportProblems = SyncAtStartup(progress, doingJoinCollectionMerge);

					// Now that we've finished synchronizing, update these icons based on the post-sync result
					// REVIEW: What do we want to happen if exception throw here? Should we add to {problems} list?
					UpdateStatusOfAllCheckedOutBooks();

					progress.Message("Done", "Done");

					// Review: are any of the cases we don't treat as warnings or errors important enough to wait
					// for the user to read them and close the dialog manually?
					// Currently it stays open only if we detected problems.
					return waitForUserToCloseDialogOrReportProblems;
				});

			// It's just possible there are one, or even more, file change notifications we
			// haven't yet received from the OS. Wait till things settle down to start monitoring again.
			//
			// FYI, Needs to be invoked on the main thread in order for the event handler to be invoked.
			// Easier to have this after the dialog is closed (the dialog also requires the main thread and will block the main thread until closed),
			// rather than SafeInvoking it from {worker}, because that has way more risk of accidental deadlock...
			//    the worker would have some work that requires being on the main thread, but it also is the gatekeeper for the main thread being released.
			//    There was an issue where it would deadlock when the debugger breakpoints were set a certain way (presumably influencing the thread execution)
			Application.Idle += StartMonitoringOnIdle;
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

				Application.Idle -= HandleRemoteBookChangesOnIdle;
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

		/// <summary>
		/// Mainly to update the checkout status on startup. There's nothing to do to the ones that
		/// are not checked out.
		/// </summary>
		public void UpdateStatusOfAllCheckedOutBooks()
		{
			foreach (var path in Directory.EnumerateDirectories(_localCollectionFolder))
			{
				if (!IsBloomBookFolder(path))
					continue;
				UpdateBookStatus(Path.GetFileName(path), false);
			}
		}

		public TeamCollectionStatus CollectionStatus => _tcLog.TeamCollectionStatus;

		/// <summary>
		/// Causes a notification to be sent to the UI to update the checkout status icon for {bookName}
		/// </summary>
		/// <param name="bookName">The name of the book</param>
		/// <param name="shouldNotifyIfNotCheckedOut">If true, will also send an event that a book isn't checked out (This is necessary when books are checked in)</param>
		public void UpdateBookStatus(string bookName, bool shouldNotifyIfNotCheckedOut)
		{
			Debug.Assert(!bookName.EndsWith(".bloom"), $"UpdateBookStatus was passed bookName=\"{bookName}\", which has a .bloom suffix. This is probably incorrect. This function wants only the bookBaseName");

			var status = GetStatus(bookName);
			if (IsCheckedOutHereBy(status))
				RaiseBookStatusChanged(bookName, CheckedOutBy.Self);
			else if (status.IsCheckedOut())
				RaiseBookStatusChanged(bookName, CheckedOutBy.Other);
			else if (shouldNotifyIfNotCheckedOut)
				RaiseBookStatusChanged(bookName, CheckedOutBy.None);
		}

		/// <summary>
		/// Notifies that the book is checked out.
		/// </summary>
		/// <param name="isCheckedOutByCurrent">Should be true if checked out by current user/machine</param>
		private void RaiseBookStatusChanged(string bookName, CheckedOutBy checkedOutByWhom)
		{
			_tcManager.RaiseBookStatusChanged(new BookStatusChangeEventArgs(bookName, checkedOutByWhom));

			// ENHANCE: Right now, if the book selection is checked in or checked out by another user,
			// we will update the icon in LibraryListView, but not the one in the book preview pane.
			// It'd be nice to update the book preview pane data too.
		}

		/// <summary>
		/// Returns true if registration is sufficient (after prompting the user if needed); false otherwise
		/// </summary>
		public static bool PromptForSufficientRegistrationIfNeeded()
		{
			return RegistrationDialog.RequireRegistrationEmail(
				"You will need to register this copy of Bloom with an email address before participating in a Team Collection");
		}
	}
}
