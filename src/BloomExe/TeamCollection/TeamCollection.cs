using Bloom.Api;
using Bloom.MiscUI;
using Bloom.web;
using L10NSharp;
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
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Collection;
using Bloom.History;
using Bloom.Registration;
using Bloom.ToPalaso;
using Bloom.Utils;
using Bloom.web.controllers;
using SIL.Reporting;
using DesktopAnalytics;
using SIL.Code;

namespace Bloom.TeamCollection
{
    /// <summary>
    /// Abstract class, of which currently FolderTeamRepo is the only existing or planned implementation.
    /// The goal is to put here the logic that is independent of exactly how the shared data is stored
    /// and sharing is accomplished, to minimize what has to be reimplemented if we offer another option.
    /// The idea is to leave open the possibility of other implementations, for example, based on
    /// a DVCS.
    /// </summary>
    public abstract class TeamCollection : IDisposable, ISaveContext
    {
        // special value for BookStatus.lockedBy when the book is newly created and not in the repo at all.
        public const string FakeUserIndicatingNewBook = "this user";
        protected readonly ITeamCollectionManager _tcManager;
        private readonly TeamCollectionMessageLog _tcLog;
        protected readonly string _localCollectionFolder; // The unshared folder that this collection syncs with

        // These arrive on background threads (currently from a FileSystemWatcher), but we want to process them
        // in idle time on the main UI thread.
        private ConcurrentQueue<RepoChangeEventArgs> _pendingRepoChanges =
            new ConcurrentQueue<RepoChangeEventArgs>();

        // When we last prompted the user to restart (due to a change in the Team Collection)
        private DateTime LastRestartPromptTime { get; set; } = DateTime.MinValue;

        // Two minutes is arbitrary, and probably not long enough if changes are coming in frequently from outside.
        private static readonly TimeSpan kMaxRestartPromptFrequency = new TimeSpan(0, 2, 0);

        internal string LocalCollectionFolder => _localCollectionFolder;
        private BookCollectionHolder _bookCollectionHolder;

        protected bool _updatingCollectionFiles;

        /// <summary>
        /// Books that have been remotely renamed but not yet reloaded and renamed locally.
        /// </summary>
        private HashSet<string> _remotelyRenamedBooks = new HashSet<string>();

        public TeamCollection(
            ITeamCollectionManager manager,
            string localCollectionFolder,
            TeamCollectionMessageLog tcLog = null,
            BookCollectionHolder bookCollectionHolder = null
        )
        {
            _tcManager = manager;
            _localCollectionFolder = localCollectionFolder;
            _tcLog =
                tcLog
                ?? new TeamCollectionMessageLog(
                    TeamCollectionManager.GetTcLogPathFromLcPath(localCollectionFolder)
                );
            _bookCollectionHolder = bookCollectionHolder;
        }

        // For Moq
        // Alternatively,  you could make it implement an ITeamCollection interface instead.
        public TeamCollection()
        {
            if (!Program.RunningUnitTests)
            {
                throw new ApplicationException(
                    "Parameterless constructor is only for mocking purposes"
                );
            }
        }

        public string CollectionId;

        public TeamCollectionMessageLog MessageLog => _tcLog;

        /// <summary>
        /// The folder-implementation-specific part of PutBook, the public method below.
        /// Exactly how it is written is implementation specific, but GetBook must be able to get it back.
        /// </summary>
        /// <param name="sourceBookFolderPath">See PutBook</param>
        /// <param name="newStatus">Updated status to write in new book</param>
        /// <param name="inLostAndFound">See PutBook</param>
        /// <remarks>Usually PutBook should be used; this method is meant for use by TeamCollection methods.</remarks>
        protected abstract void PutBookInRepo(
            string sourceBookFolderPath,
            BookStatus newStatus,
            bool inLostAndFound = false,
            Action<float> progressCallback = null
        );

        public abstract bool KnownToHaveBeenDeleted(string oldName);

        /// <summary>
        /// Returns null if connection to repo is fine, otherwise, a message describing the problem.
        /// This default implementation assumes nothing useful can be done to check the connection.
        /// </summary>
        public virtual TeamCollectionMessage CheckConnection()
        {
            return null;
        }

        protected abstract void MoveRepoBookToLostAndFound(string bookName);

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

            if (
                repoStatus.lockedBy == TeamCollectionManager.CurrentUser
                && repoStatus.lockedWhere == TeamCollectionManager.CurrentMachine
            )
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
        /// Abandon all the user's changes and undo the checkout of the specified book.
        /// </summary>
        /// <returns>A list of book folders created and destroyed. Typically this is empty...
        /// the book just got checked in. If the changes being abandoned included renaming
        /// the book, it will include the paths to the current book folder [0] as well as the
        /// one it was renamed to [1]. Pathologically, if the user created in the meantime
        /// another book at the old name, it may include [2] the name of the folder to which
        /// the new book was renamed.</returns>
        public List<string> ForgetChangesCheckin(string bookName)
        {
            var foldersNeedingUpdate = new List<string>();
            var status = GetLocalStatus(bookName);
            var finalBookName = bookName;
            if (!string.IsNullOrEmpty(status.oldName))
            {
                finalBookName = status.oldName;
                var oldBookFolder = Path.Combine(_localCollectionFolder, finalBookName);
                foldersNeedingUpdate.Add(Path.Combine(oldBookFolder));
                foldersNeedingUpdate.Add(Path.Combine(_localCollectionFolder, bookName));

                if (Directory.Exists(oldBookFolder))
                {
                    // This is pathological, but it may not be obvious why.
                    // For example: we renamed NastyBook to NiceBook. We expect that there is a NastyBook.bloom
                    // in the repo, and a folder NiceBook with a file NiceBook.htm in the local folder,
                    // and a status file in the NiceBook folder indicating that it is checked out locally and is
                    // a rename of NastyBook.
                    // Since we're undoing everything since the checkout, we need to move NiceBook/NiceBook.htm
                    // back to NastyBook/NastyBook.htm
                    // We do NOT expect to find a NastyBook folder in local BEFORE we undo the rename! At this point
                    // the renamed book is, locally, in the NiceBook folder.
                    // But we did find a NastyBook folder, right where we want to put NiceBook when we undo renaming it.
                    // About the only way is that the user made a new book called NastyBook since renaming the old NastyBook.
                    // Undoing the checkout of the renamed NastyBook will result in two books called NastyBook.
                    // The original (renamed) NastyBook must get back the original folder name, because that matches
                    // the .bloom file in the repo. So something must be done about the unexpected one.
                    // It's not very obvious what to do about it. Actually, possibly we should have prevented
                    // creating the new NastyBook at that folder location, because if the user were to check it in
                    // before checking in the rename, the new NastyBook would try to overwrite the old, renamed one.
                    // Maybe we will do that one day. But for now, we have to somehow recover so that the original
                    // NastyBook is restored to the pre-checkout state. To do that we have to get the new NastyBook
                    // out of the way. We do that by moving it to the location it would have occupied if it had been
                    // created without first renaming the old NastyBook.
                    var newPathForExtraBook = BookStorage.MoveBookToAvailableName(oldBookFolder);
                    foldersNeedingUpdate.Add(newPathForExtraBook);
                }

                CopyBookFromRepoToLocal(status.oldName);
                status = status.WithOldName(null);
                // Get rid of the moved and possibly edited version
                SIL.IO.RobustIO.DeleteDirectoryAndContents(
                    Path.Combine(_localCollectionFolder, bookName),
                    true
                );
                // Todo: when we have the new implemetation of CollectionTab,
                // we need to tell it to update, getting rid of the book we
                // just renamed and adding it by the new name, and ideally
                // selecting it by the new name. This might be better done
                // by code in TeamCollectionApi, perhaps by having this method
                // return the restored name as an indication it is needed.
            }
            else
            {
                CopyBookFromRepoToLocal(bookName);
            }

            status = status.WithLockedBy(null);
            WriteBookStatus(finalBookName, status);
            return foldersNeedingUpdate;
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
        public BookStatus PutBook(
            string folderPath,
            bool checkin = false,
            bool inLostAndFound = false,
            Action<float> progressCallback = null
        )
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
            // Usually we want to delete the old repo file.
            if (!string.IsNullOrEmpty(oldName))
            {
                // Rename the old repo file (or whatever). The renamed file will immediately be
                // overwritten by the PutBookInRepo. But we hope that rename followed by overwrite
                // will work better than writing the new file and then deleting the old one.
                // Hopefully, it prevents any possibility of there being a time when (on a remote
                // computer) the book looks simply deleted: the old repo file is gone but the new
                // one has not arrived. (This is quite plausible, as a delete file message might be
                // much faster to transmit than a complete new file, even though the delete happens
                // later.) It's also possible that the backend can better optimize the data transfer
                // if it can recognize that the PutBook is overwriting an existing file.
                RenameBookInRepo(bookFolderName, oldName);
            }
            PutBookInRepo(folderPath, status, inLostAndFound, progressCallback);
            // If this is true, we're about to delete or overwrite the book, so no point
            // in updating its status (and we never call with this true in regard to a rename).
            if (inLostAndFound)
                return status;
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
            UpdateBookStatus(bookFolderName, true);
            return status;
        }

        // Get just one file from the repo version of the specified book.
        public abstract string GetRepoBookFile(string bookName, string fileName);

        /// <summary>
        /// Sync every book from the local collection (every folder that has a corresponding htm file) from local
        /// to repo, unless status files exist indicating they are already in sync. (This is typically used when
        /// creating a TeamCollection from an existing local collection. Usually it is a new folder and all
        /// books are copied.)
        /// </summary>
        public void SynchronizeBooksFromLocalToRepo(IWebSocketProgress progress)
        {
            foreach (var path1 in Directory.EnumerateDirectories(_localCollectionFolder))
            {
                var path = BookStorage.MoveBookToSafeName(path1);
                try
                {
                    var bookFolderName = Path.GetFileName(path);
                    // don't just use GetStatus().checksum here, since if the book isn't in the repo but DOES have a local
                    // status, perhaps from being previously in another TC, it will retrieve the local status, which
                    // will have the same checksum as localStatus, and we will wrongly conclude we don't need to copy
                    // the book to the repo.
                    // if it's corrupt, statusString will be null, and we'll try to overwrite.
                    TryGetBookStatusJsonFromRepo(bookFolderName, out string statusString);
                    var repoChecksum = string.IsNullOrEmpty(statusString)
                        ? null
                        : BookStatus.FromJson(statusString).checksum;
                    var localStatus = GetLocalStatus(bookFolderName);
                    var localHtmlFilePath = Path.Combine(
                        path,
                        BookStorage.FindBookHtmlInFolder(path)
                    );
                    if (
                        (repoChecksum == null || repoChecksum != localStatus?.checksum)
                        && RobustFile.Exists(localHtmlFilePath)
                    )
                    {
                        progress.MessageWithParams(
                            "SendingFile",
                            "",
                            "Adding {0} to the collection",
                            ProgressKind.Progress,
                            bookFolderName
                        );
                        PutBook(path);
                    }
                }
                catch (Exception ex)
                {
                    // Something went wrong with dealing with this book, but we'd like to carry on with
                    // syncing the rest of the collection
                    var msg = String.Format(
                        "Something went wrong trying to copy the book {0} to your Team Collection.",
                        path
                    );
                    NonFatalProblem.Report(ModalIf.All, PassiveIf.All, msg, null, ex);
                }
            }
        }

        // Return a list of all the books in the collection (the repo one, not
        // the local one)
        public abstract string[] GetBookList();

        // Unzip one book in the collection to the specified destination. Usually GetBook should be used
        protected abstract string FetchBookFromRepo(
            string destinationCollectionFolder,
            string bookName
        );

        public string CopyBookFromRepoToLocal(
            string bookName,
            string destinationCollectionFolder = null,
            bool dialogOnError = false
        )
        {
            var error = FetchBookFromRepo(
                destinationCollectionFolder ?? _localCollectionFolder,
                bookName
            );
            if (error != null)
            {
                if (dialogOnError)
                {
                    NonFatalProblem.Report(ModalIf.All, PassiveIf.All, error);
                }
                return error;
            }

            WriteLocalStatus(
                bookName,
                GetStatus(bookName),
                destinationCollectionFolder ?? _localCollectionFolder
            );
            return null;
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
            // (Form.ActiveForm should not be null when Bloom is running normally. However, it can be when we're displaying
            // a page in a browser, or when we're reloading Bloom after saving collection settings.)
            if (Form.ActiveForm != null)
            {
                SafeInvoke.InvokeIfPossible(
                    "Add SyncCollectionFilesToRepoOnIdle",
                    Form.ActiveForm,
                    false,
                    (Action)(
                        () =>
                            // Needs to be invoked on the main thread in order for the event handler to be invoked.
                            Application.Idle += SyncCollectionFilesToRepoOnIdle
                    )
                );
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

        public abstract void DeleteBookFromRepo(string bookFolderPath, bool makeTombstone = true);

        public abstract void RenameBookInRepo(string newBookFolderPath, string oldName);

        private void SyncCollectionFilesToRepoOnIdle(object sender, EventArgs e)
        {
            Application.Idle -= SyncCollectionFilesToRepoOnIdle;
            // The only time we should not have a TCManager is during unit testing.
            if (TCManager != null && !TCManager.CheckConnection())
            {
                return;
            }
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
        /// A good default, overridden in DisconnectedTeamCollection.
        /// </summary>
        public virtual bool IsDisconnected => false;

        /// <summary>
        /// Common part of getting book status as recorded in the repo, or if it is not in the repo
        /// but there is such a book locally, treat as locked by FakeUserIndicatingNewBook.
        /// </summary>
        /// <param name="bookFolderName"></param>
        /// <returns></returns>
        /// <remarks>Needs to be thread-safe; may be called on multiple threads at the same time,
        /// typically but not necessarily for different books.</remarks>
        public BookStatus GetStatus(string bookFolderName)
        {
            if (!TryGetBookStatusJsonFromRepo(bookFolderName, out string statusString))
                return new BookStatus() { hasInvalidRepoData = true, collectionId = CollectionId };
            if (String.IsNullOrEmpty(statusString))
            {
                // a book that doesn't exist (by this name) in the repo should only exist locally if it's new
                // (no status file) or renamed (the corresponding repo file is called oldName).
                var statusFilePath = GetStatusFilePath(bookFolderName, _localCollectionFolder);
                if (RobustFile.Exists(statusFilePath))
                {
                    // The book has to have been renamed since it has a status file.
                    // Or maybe it's been removed remotely, but the local collection hasn't caught up...
                    var bookStatus = BookStatus.FromJson(
                        RobustFile.ReadAllText(statusFilePath, Encoding.UTF8)
                    );
                    if (!String.IsNullOrEmpty(bookStatus.oldName))
                    {
                        // Use the book's original name to access the repo status.  (BL-9680)
                        statusString = GetBookStatusJsonFromRepo(bookStatus.oldName);
                        if (!String.IsNullOrEmpty(statusString))
                            return BookStatus.FromJson(statusString);
                    }
                    // Maybe it's a copied-in book we haven't cleaned up yet?
                    if (bookStatus.collectionId != this.CollectionId)
                        return BookStatus.NewBookStatus; // makes it be treated as a new local book, never checked in.
                    // This is a bizarre situation that should get corrected the next time Bloom starts up.
                    // For now, just return what we have by way of local status.
                    return bookStatus;
                }
                else if (Directory.Exists(Path.GetDirectoryName(statusFilePath)))
                {
                    // book exists only locally. Treat as checked out to FakeUserIndicatingNewBook
                    return BookStatus.NewBookStatus;
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
            // It doesn't currently matter whether the book status stored in the repo has the
            // right collectionID, but we will be storing it that way locally and like to
            // keep the two as consistent as possible.
            WriteBookStatusJsonToRepo(bookName, status.WithCollectionId(CollectionId).ToJson());
            WriteLocalStatus(bookName, status);
        }

        /// <summary>
        /// Get the raw status data from however the repo implementation stores it.
        /// </summary>
        protected abstract string GetBookStatusJsonFromRepo(string bookFolderName);

        /// <summary>
        /// Try to get the status, typically from a new or modified .bloom file.
        /// If not successful, an error is written to the log.
        /// Note that false indicates a repo file was found but we could not read it.
        /// We return TRUE (but status null) if there is no repo file at all.
        /// (That's a valid repo status...null indicates the book is not in the repo.)
        /// </summary>
        protected abstract bool TryGetBookStatusJsonFromRepo(
            string bookFolderName,
            out string status,
            bool reportFailure = true
        );

        /// <summary>
        /// Return true if the book exists in the repo.
        /// </summary>
        /// <returns></returns>
        public abstract bool IsBookPresentInRepo(string bookFolderName);

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

        /// <summary>
        /// Event raised when a book is deleted from the repo. Be careful, it's possible
        /// DropBox or other file sharing programs or our own zip file writing code
        /// does a 'delete' on a file as part of the process of writing a new version of it.
        /// This is a low-level event that is currently normally
        /// only used to push data into the _pendingRepoChanges queue. Add handlers to this
        /// cautiously...they are raised on background threads. Usually it is better to subscribe
        /// to the BookStatusChangeEvent (available from AutoFac), which is raised during idle
        /// time on the UI thread (by pulling from the queue) after we have done some basic
        /// analysis of the effect of the change. (It will be called with CheckedOutBy.Deleted
        /// if the book is really gone.)
        /// </summary>
        public event EventHandler<DeleteRepoBookFileEventArgs> DeleteRepoBookFile;

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
                CopyBookFromRepoToLocal(bookName, dest, true);
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
            if (String.IsNullOrEmpty(status.lockedBy) && !IsDisconnected)
            {
                status = status.WithLockedBy(
                    whoBy,
                    TeamCollectionManager.CurrentUserFirstName,
                    TeamCollectionManager.CurrentUserSurname
                );
                WriteBookStatus(bookName, status);
            }

            // If we succeeded, we definitely want various things to update to show it.
            // But there may be status changes to show if we failed, too...for example,
            // probably it's because the book was discovered to be checked out to
            // someone else, and we'd like things to show that.
            UpdateBookStatus(bookName, true);

            return IsCheckedOutHereBy(status, whoBy);
        }

        public void ForceUnlock(string bookName)
        {
            var status = GetStatus(bookName);
            status = status.WithLockedBy(null);
            WriteBookStatus(bookName, status);
            UpdateBookStatus(bookName, true);
        }

        // Get the email of the user, if any, who has the book locked. Returns null if not locked.
        // As a special case, if the book exists only locally, we return TeamRepo.kThisUser.
        public virtual string WhoHasBookLocked(string bookName)
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
        public virtual string WhatComputerHasBookLocked(string bookName)
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

        // internal for testing
        internal bool _haveShownRemoteSettingsChangeWarning;

        protected bool _stopSyncingCollectionFiles;

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
            if (_stopSyncingCollectionFiles)
                return;
            var repoModTime = LastRepoCollectionFileModifyTime;
            var savedSyncTime = LocalCollectionFilesRecordedSyncTime();
            if (atStartup && repoModTime != DateTime.MinValue)
            {
                // Theoretically it could be that local collection settings are newer.
                // But we write changes to them more-or-less immediately. The important
                // thing is that, as of each reload, the local settings match the repo ones.
                // In the future, we will have various limits to make conflicts even less likely.
                // For now, the important thing is that whatever wins in the repo wins everywhere.
                // One exception: customCollectionStyles.css changes while Bloom is not running
                // should be kept.
                var customCollectionStylesPath = Path.Combine(
                    _localCollectionFolder,
                    "customCollectionStyles.css"
                );
                var stylesModTime = new FileInfo(customCollectionStylesPath).LastWriteTime;
                if (stylesModTime > savedSyncTime)
                {
                    // OK, it got modified while we weren't looking. If this user is an admin,
                    // and no settings changed remotely, we'll let the local ones win.
                    if (_tcManager.OkToEditCollectionSettings && savedSyncTime >= repoModTime)
                    {
                        CopyRepoCollectionFilesFromLocal(_localCollectionFolder);
                        return;
                    }
                }
                CopyRepoCollectionFilesToLocal(_localCollectionFolder);
            }
            else
            {
                if (LocalCollectionFilesUpdated())
                {
                    if (repoModTime > savedSyncTime)
                    {
                        // We have a conflict we should warn the user about...if we haven't already.
                        if (!_haveShownRemoteSettingsChangeWarning)
                        {
                            _haveShownRemoteSettingsChangeWarning = true;
                            // if it's not a startup sync, it's happening because of a local change. It will get lost.
                            // Not sure this is worth localizing. Eventually only one or two users per collection will be
                            // allowed to make such changes. Collection settings should rarely be changed at all
                            // in Team Collections. This message will hopefully be seen rarely if at all.
                            const string msg =
                                "Collection settings have been changed remotely. Your recent "
                                + "collection settings changes will be lost the next time Bloom is restarted.";
                            BloomMessageBox.ShowInfo(msg);
                        }
                    }
                    else
                    {
                        CopyRepoCollectionFilesFromLocal(_localCollectionFolder);
                    }
                }
            }
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
            var files = RootLevelCollectionFilesIn(_localCollectionFolder);
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
            return RetryUtility.Retry(() => MakeChecksumOnFilesInternal(files));
        }

        private string MakeChecksumOnFilesInternal(IEnumerable<string> files)
        {
            using (var sha = SHA256Managed.Create())
            {
                // Order must be predictable but does not otherwise matter.
                foreach (var path in files.OrderBy(x => x))
                {
                    if (RobustFile.Exists(path)) // won't usually be passed ones that don't, but useful for unit testing at least.
                    {
                        using (var input = RobustIO.GetFileStream(path, FileMode.Open))
                        {
                            byte[] buffer = new byte[4096];
                            int count;
                            while ((count = input.Read(buffer, 0, 4096)) > 0)
                            {
                                sha.TransformBlock(buffer, 0, count, buffer, 0);
                            }
                        }
                    }
                }

                sha.TransformFinalBlock(new byte[0], 0, 0);
                return Convert.ToBase64String(sha.Hash);
            }
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
            RobustFile.WriteAllText(path, nowString + @";" + checksum);
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
            if (!RobustFile.Exists(path))
                return DateTime.MinValue; // assume local files are really old!
            DateTime result;
            if (DateTime.TryParse(RobustFile.ReadAllText(path).Split(';')[0], out result))
                return result;
            return DateTime.MinValue;
        }

        internal string LocalCollectionFilesSavedChecksum()
        {
            var path = GetCollectionFileSyncLocation();
            if (!RobustFile.Exists(path))
                return "";
            var parts = RobustFile.ReadAllText(path).Split(';');
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
            var wasMonitoring =
                _localFolderWatcher != null && _localFolderWatcher.EnableRaisingEvents;
            if (_localFolderWatcher != null)
                _localFolderWatcher.EnableRaisingEvents = false;
            try
            {
                CopyRepoCollectionFilesToLocalImpl(destFolder);
                RecordCollectionFilesSyncData();
            }
            finally
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
            if (RobustFile.Exists(collectionPath))
                return collectionPath;
            // occasionally, mainly when making a temp folder during joining, the bloomCollection file may not
            // have the expected name
            var result = Directory
                .EnumerateFiles(parentFolder, "*.bloomCollection")
                .FirstOrDefault();
            if (result == null)
                return collectionPath; // sometimes we use this method to get the expected path where there is no .bloomCollection
            return result;
        }

        public static List<string> RootLevelCollectionFilesIn(string folder)
        {
            var files = new List<string>();
            files.Add(Path.GetFileName(CollectionPath(folder)));
            foreach (var file in new[] { "customCollectionStyles.css", "configuration.txt" })
            {
                if (RobustFile.Exists(Path.Combine(folder, file)))
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
                var files = RootLevelCollectionFilesIn(localCollectionFolder);
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
            NewBook?.Invoke(this, new NewBookEventArgs() { BookFileName = bookName });
        }

        /// <param name="bookFileName">The book name, including the .bloom suffix</param>
        protected void RaiseBookStateChange(string bookFileName)
        {
            BookRepoChange?.Invoke(
                this,
                new BookRepoChangeEventArgs() { BookFileName = bookFileName }
            );
        }

        protected void RaiseDeleteRepoBookFile(string bookFileName)
        {
            DeleteRepoBookFile?.Invoke(
                this,
                new DeleteRepoBookFileEventArgs() { BookFileName = bookFileName }
            );
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
            NewBook += (sender, args) =>
            {
                QueuePendingBookChange(args);
            };
            BookRepoChange += (sender, args) => QueuePendingBookChange(args);
            RepoCollectionFilesChanged += (sender, args) =>
                QueuePendingBookChange(new RepoChangeEventArgs());
            DeleteRepoBookFile += (sender, args) => QueuePendingBookChange(args);
            Application.Idle += HandleRemoteBookChangesOnIdle;
            StartMonitoring();
        }

        internal void QueuePendingBookChange(RepoChangeEventArgs args)
        {
            _pendingRepoChanges.Enqueue(args);
        }

        internal void HandleRemoteBookChangesOnIdle(object sender, EventArgs e)
        {
            if (_pendingRepoChanges.TryDequeue(out RepoChangeEventArgs args))
            {
                // _pendingChanges is a single queue of things that happened in the Repo,
                // including both new books arriving and existing books changing.
                // The two event types have different classes of event args, which allows us
                // to split them here and handle each type differently.
                if (args is NewBookEventArgs newArgs)
                    HandleNewBook(newArgs);
                else if (args is DeleteRepoBookFileEventArgs delArgs)
                    HandleDeletedRepoFileAfterPause(delArgs);
                else if (args is BookRepoChangeEventArgs changeArgs)
                    HandleModifiedFile(changeArgs);
                else
                    HandleCollectionSettingsChange(args);
                // These "HandleX" methods above send a C# event, which is helpful for the C# end of things.
                // Unfortunately, a websocket message is needed to make sure that javascript-land is up-to-date
                // with any remote changes, for example, that the TeamCollection button updates (See BL-10270).
                // One of the selection changed handlers sends such a message.
                _tcManager.BookSelection?.InvokeSelectionChanged(false);
            }
        }

        private void HandleDeletedRepoFileAfterPause(DeleteRepoBookFileEventArgs delArgs)
        {
            // I'm nervous about allegedly deleted files. It's a common pattern to update a file
            // by writing a temp file, deleting the original, and renaming the temp.
            // Another issue is that the remote checkin might be a rename rather than a delete,
            // and the replacement file may take a while to arrive. If we process before
            // it arrives, we won't be able to report the rename correctly. We're not
            // in any hurry to update the UI when a repo file is deleted. So let's wait...and
            // then check it's really gone. This is too slow to unit test, so all the logic
            // is in the HandleDeletedRepoFile method.
            MiscUtils.SetTimeout(() => HandleDeletedRepoFile(delArgs.BookFileName), 5000);
        }

        /// <summary>
        /// Answer true if the current user has books really checked out.
        /// For this method, newly created books that are only local don't count.
        /// </summary>
        /// <returns></returns>
        public bool AnyBooksCheckedOutHereByCurrentUser
        {
            get
            {
                foreach (var path in Directory.EnumerateDirectories(_localCollectionFolder))
                {
                    try
                    {
                        if (!IsBloomBookFolder(path))
                            continue;
                        var localStatus = GetLocalStatus(Path.GetFileName(path));
                        if (localStatus.lockedBy == TeamCollection.FakeUserIndicatingNewBook)
                            continue;
                        if (localStatus.IsCheckedOutHereBy(TeamCollectionManager.CurrentUser))
                            return true;
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                }

                return false;
            }
        }

        internal void HandleDeletedRepoFile(string fileName)
        {
            var bookBaseName = GetBookNameWithoutSuffix(fileName);
            //Debug.WriteLine("Delete " + bookBaseName);
            // Maybe the deletion was just a temporary part of an update?
            if (IsBookPresentInRepo(bookBaseName))
            {
                //Debug.WriteLine("Delete false alarm: " + bookBaseName);
                return;
            }

            // Maybe the book is being renamed rather than deleted? Or something weird is going on?
            // Anyway, we'll only delete it locally if there is a tombstone in the TC indicating that
            // someone, somewhere, deliberately deleted it.
            if (!KnownToHaveBeenDeleted(bookBaseName))
            {
                //Debug.WriteLine("Delete already known: " + bookBaseName);
                return;
            }

            var status = GetLocalStatus(bookBaseName);
            if (status.IsCheckedOutHereBy(TeamCollectionManager.CurrentUser))
            {
                //Debug.WriteLine("Deleted checked out book: " + bookBaseName);
                // Argh! Somebody deleted the book I'm working on! This is an error, but Reloading the collection
                // won't help; I just need to check it in to undo the deletion, or delete the local copy myself.
                // So unlike most errors, having this in the message log is not cause to show the Reload button.
                _tcLog.WriteMessage(
                    MessageAndMilestoneType.ErrorNoReload,
                    "TeamCollection.RemoteDeleteConflict",
                    "One of your teammates has deleted the book \"{0}\". Since you have this book checked out, it has not been deleted locally. You can delete your copy if you wish, or restore it to the Team Collection by just checking in what you have.",
                    bookBaseName,
                    null
                );
                // Don't delete it; and there's been no local status change we need to worry about.
                return;
            }

            var deletedBookFolder = Path.Combine(_localCollectionFolder, bookBaseName);
            if (
                (_tcManager?.BookSelection?.CurrentSelection?.FolderPath ?? "") == deletedBookFolder
            )
            {
                //Debug.WriteLine("Deleted selected book: " + bookBaseName);
                // Argh! Somebody deleted the selected book! We might be right in the middle of any form
                // of publication, or generating a preview, or anything! Just keep it, and let the user know
                // a reload is needed.
                _tcLog.WriteMessage(
                    MessageAndMilestoneType.Error,
                    "TeamCollection.RemoteDeleteCurrent",
                    "One of your teammates has deleted the book \"{0}\". Since this book is selected, your copy will not be deleted until you reload the collection.",
                    bookBaseName
                );
                TeamCollectionManager.RaiseTeamCollectionStatusChanged();
                return;
            }
            //Debug.WriteLine("Deleting for real: " + bookBaseName);
            PathUtilities.DeleteToRecycleBin(deletedBookFolder);
            _bookCollectionHolder?.TheOneEditableCollection?.HandleBookDeletedFromCollection(
                deletedBookFolder
            );
            UpdateBookStatus(bookBaseName, true);
        }

        internal void HandleCollectionSettingsChange(RepoChangeEventArgs result)
        {
            _tcLog.WriteMessage(
                MessageAndMilestoneType.NewStuff,
                "TeamCollection.SettingsModifiedRemotely",
                "One of your teammates has made changes to the collection settings.",
                null,
                null
            );
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
        public virtual bool HasLocalChangesThatMustBeClobbered(string bookName)
        {
            var localStatus = GetLocalStatus(bookName);
            // We don't bother to check for this sort of problem unless we think the book is checked out locally
            // Not being checked out locally should guarantee that it has no local changes.
            if (!IsCheckedOutHereBy(localStatus))
                return false;
            var repoStatus = GetStatus(bookName);
            var bookPath = Path.Combine(_localCollectionFolder, bookName);
            var currentChecksum = MakeChecksum(bookPath);
            if (String.IsNullOrEmpty(currentChecksum))
            {
                Logger.WriteEvent(
                    $"*** TeamCollection.HasLocalChangesThatMustBeClobbered() got an empty checksum for the local book."
                );
                var haveHtm = BookStorage.FindBookHtmlInFolder(bookPath);
                Logger.WriteEvent(
                    $"*** TeamCollection.HasLocalChangesThatMustBeClobbered() found htm file = {haveHtm}"
                );
            }
            // If it hasn't actually been edited locally, we might have a problem, but not one that
            // requires clobbering.
            if (repoStatus.checksum == currentChecksum)
                return false;

            // We've checked it out and edited it...there's a problem if the repo disagrees about either content or status

            var isConflictingCheckedOutStatus = !IsCheckedOutHereBy(repoStatus);
            if (isConflictingCheckedOutStatus)
            {
                try //paranoia
                {
                    Logger.WriteEvent(
                        $"*** TeamCollection.HasLocalChangesThatMustBeClobbered(): conflicting checked out status, local is {localStatus.ToSanitizedJson()}, repo is {repoStatus.ToSanitizedJson()}."
                    );
                }
                catch (Exception)
                {
                    // Don't crash for a log
                }
            }

            // Note: localStatus.checksum is not the checksum of the local files,
            // but rather a local record of what the remote checksum was when we checked it out.
            var isConflictingCheckSum = repoStatus.checksum != localStatus.checksum;
            if (isConflictingCheckSum)
                Logger.WriteEvent(
                    $"*** TeamCollection.HasLocalChangesThatMustBeClobbered(): conflicting checksum. Current={currentChecksum} localStatus={localStatus.checksum} repoStatus={repoStatus.checksum}"
                );

            return isConflictingCheckedOutStatus || isConflictingCheckSum;
        }

        private bool HasCheckoutConflict(string bookName)
        {
            return IsCheckedOutHereBy(GetLocalStatus(bookName))
                && !IsCheckedOutHereBy(GetStatus(bookName));
        }

        public virtual bool HasBeenChangedRemotely(string bookName)
        {
            if (_remotelyRenamedBooks.Contains(bookName))
                return true;
            // It's debatable whether a case-only change should be considered a remote
            // rename or a remote change. Usually a rename results from a title change,
            // in which case the book content is also modified; but we do allow just
            // changing the name. Because a case-only change still allows us to find
            // matching repo and local files, it's more convenient to treat it as a change
            // that is not a rename. However, unlike the local checksum, the local name
            // can change because of local editing. So we don't consider that a remote
            // change if the book is checked out here.
            var localStatus = GetLocalStatus(bookName);
            return localStatus.checksum != GetStatus(bookName).checksum
                || (
                    DoLocalAndRemoteNamesDifferOnlyByCase(bookName)
                    && !IsCheckedOutHereBy(localStatus)
                );
        }

        /// <summary>
        /// Book has a clobber problem...we can't go on editing until we sort it out...
        /// if there are either conflicting edits or conflicting lock status.
        /// </summary>
        public bool HasClobberProblem(string bookName)
        {
            return HasLocalChangesThatMustBeClobbered(bookName) || HasCheckoutConflict(bookName);
        }

        public static string GenerateCollectionId()
        {
            return Guid.NewGuid().ToString();
        }

        /// <summary>
        /// Handle a notification that a file has been modified. If it's a bloom book file
        /// and there is no problem, add a NewStuff message. If there is a problem,
        /// add an error message. Send an UpdateBookStatus. (If it's the current book,
        /// a handler for book status may upgrade the problem to 'clobber pending'.)
        /// This is also called when we detect a rename in the shared folder.
        /// (For LAN collections, a rename is typically the last and only reported step in
        /// any modification to the file, because our zip writer makes a temp file and then
        /// renames it to overwrite the original.)
        /// </summary>
        /// <param name="args"></param>
        public void HandleModifiedFile(BookRepoChangeEventArgs args)
        {
            if (args.BookFileName.EndsWith(".bloom"))
            {
                var bookBaseName = GetBookNameWithoutSuffix(args.BookFileName);
                //Debug.WriteLine("Modified: " + bookBaseName);

                // It's quite likely in the case of a shared LAN folder that a file has been modified, but the
                // other Bloom instance hasn't finished writing it yet. If we can't get its status,
                // it's probably locked, and anything else we might try will fail. Try again in 2 seconds.
                // It's also possible that, due to some disaster like a power failure on the remote Bloom
                // or the LAN server, the .bloom file gets permanently locked or corrupted so that the comment
                // cannot be extracted from the zip file even though we can read the content. We thought about
                // trying to handle those cases. But
                // (a) we expect them to be rare, and this modified-file-handling is only about keeping
                // up-to-date status information on books that are not selected. It's not a very big problem
                // if it doesn't get updated in a rare case. When the user re-loads the collection or selects
                // the book he will find out that there's a problem with it.
                // (b) Continuing to check the book every couple of seconds, even for as long as Bloom is running,
                // does not seem like a dreadfully costly problem
                // (c) Reporting that a book is in one of these states (stuck locked, or zip file corrupted)
                // as a result of remote changes seems difficult to do in a way we're confident won't confuse
                // users. It would need to somehow be clear that an unexpectedly-occurring error was not due
                // to anything this user did...and no knowing what this user just did that he might think caused it.
                // (d) It would be somewhat complicated to track how long a book had been locked, considering
                // that multiple books could be remotely modified.
                // (e) It's not obvious what is a reasonable maximum time for a book to be locked, considering
                // that possibly a large book is being written by a Bloom running on a slow computer over a slow
                // network (or slow internet, if Dropbox is involved).
                // Considering these factors, we decided for now not to try to handle remotely-modified
                // books for which we can't get a status except by continuing to try to get it.
                if (!TryGetBookStatusJsonFromRepo(bookBaseName, out var status, false))
                {
                    MiscUtils.SetTimeout(() => HandleModifiedFile(args), 2000);
                    return;
                }

                // The most serious concern is that there are local changes to the book that must be clobbered.
                if (HasLocalChangesThatMustBeClobbered(bookBaseName))
                {
                    _tcLog.WriteMessage(
                        MessageAndMilestoneType.Error,
                        "TeamCollection.EditedFileChangedRemotely",
                        "One of your teammates has modified or checked out the book '{0}', which you have edited but not checked in. You need to reload the collection to sort things out.",
                        bookBaseName,
                        null
                    );
                }
                else
                // A lesser but still Error condition is that the repo has a conflicting notion of checkout status.
                if (HasCheckoutConflict(bookBaseName))
                {
                    _tcLog.WriteMessage(
                        MessageAndMilestoneType.Error,
                        "TeamCollection.ConflictingCheckout",
                        "One of your teammates has checked out the book '{0}'. This undoes your checkout.",
                        bookBaseName,
                        null
                    );
                }
                else if (!Directory.Exists(Path.Combine(_localCollectionFolder, bookBaseName)))
                {
                    if (HandlePossibleRename(bookBaseName))
                    {
                        //Debug.WriteLine("Detected rename in HandleModifiedFile");
                        return;
                    }
                    // No local version at all. Possibly it was just now created, and we will get a
                    // new book notification any moment, or already have one. Possibly there have
                    // been additional checkins between creation and when this user reloads.
                    // In any case, we don't need any new messages or status change beyond
                    // the NewBook message that should be generated at some point.
                    //Debug.WriteLine("No local version of " + bookBaseName);
                    return;
                }
                else if (HasBeenChangedRemotely(bookBaseName))
                {
                    _tcLog.WriteMessage(
                        MessageAndMilestoneType.NewStuff,
                        "TeamCollection.BookModifiedRemotely",
                        "One of your teammates has made changes to the book '{0}'",
                        bookBaseName,
                        null
                    );
                }

                //Debug.WriteLine("Updated status for " + bookBaseName);
                // This needs to be AFTER we update the message log, data which it may use.
                UpdateBookStatus(bookBaseName, true);
            }
        }

        /// <summary>
        /// Given that the specified book exists in both the repo and locally,
        /// if the names differ only by case, rename the local book to match the repo.
        /// </summary>
        public abstract void EnsureConsistentCasingInLocalName(string bookBaseName);

        public abstract bool DoLocalAndRemoteNamesDifferOnlyByCase(string bookBaseName);

        internal static string GetIdFrom(string metadataString, string file)
        {
            // This logic is irritatingly similar to methods in BookMetaData like
            // FromFolder, FromString, and FromFile. But none of them is quite right.
            // FromString and FromFile crash if an ID can't be obtained; this method
            // wants to just return null. FromFolder and FromFile both attempt to use
            // (and restore) a backup meta.json if the main one is corrupt, but I'm
            // nervous about using anything that might be out of date  in a TC situation
            // where the ID is so important, and also about modifying it in a situation
            // where we may not have the book checked out (though, currently, if there
            // is a backup and the main meta.json is corrupt, we will restore it while
            // loading the collection. But we may decide to change that. In any case, it
            // doesn't feel like a good thing to do while syncing a collection.)
            BookMetaData metaData = null;
            Exception ex = null;
            try
            {
                metaData = BookMetaData.FromStringUnchecked(metadataString);
            }
            catch (Exception e)
            {
                Logger.WriteError(
                    "Got error reading meta.json from "
                        + file
                        + " with contents '"
                        + metadataString
                        + "'",
                    e
                );
                ex = e;
            }

            if (metaData == null)
            {
                if (ex == null)
                    Logger.WriteEvent(
                        "Failed to read metadata from "
                            + file
                            + " with contents '"
                            + metadataString
                            + "'"
                    );
                // It's corrupted, but maybe it's in good enough shape to get an ID from?
                // For example, in BL-11821 we encountered meta.json files where various integers
                // were represented with decimals (0.0 instead of 0) which produced a JsonReaderException.
                // Note: it would very likely be more efficient to just do what this method does without trying to parse
                // the string as JSON. But (a) I want to capture any problems in the log, and (b)
                // I'd rather not use any unusual way to process meta.json when things are not in
                // an unusual state.
                // Enhance: if we can't get an id from metaDataString, we could try
                // meta.bak, if it exists.
                return BookMetaData.GetIdFromDamagedMetaDataString(metadataString);
            }
            return metaData?.Id;
        }

        private bool HandlePossibleRename(string bookBaseName)
        {
            var oldName = NewBookRenamedFrom(bookBaseName);
            if (oldName == null)
                return false;
            //Debug.WriteLine("Detected that " + bookBaseName + " was renamed from " + oldName);
            _remotelyRenamedBooks.Add(oldName);
            _tcLog.WriteMessage(
                MessageAndMilestoneType.NewStuff,
                "TeamCollection.RenameFromRemote",
                "The book \"{0}\" has been renamed to \"{1}\" by a teammate.",
                oldName,
                bookBaseName
            );
            // This needs to be AFTER we update the message log, data which it may use.
            UpdateBookStatus(oldName, true);
            return true;
        }

        /// <summary>
        /// Given that newBookName is the name of a book in the repo
        /// that does not occur locally, can we determine that it is a rename
        /// of a local book? If so, return the book it is renamed from,
        /// otherwise, return null.
        /// </summary>
        /// <param name="newBookName"></param>
        /// <returns></returns>
        private string NewBookRenamedFrom(string newBookName)
        {
            var meta = GetRepoBookFile(newBookName, "meta.json");
            if (string.IsNullOrEmpty(meta) || meta == "error")
                return null;
            var id = GetIdFrom(meta, newBookName);
            foreach (var path in Directory.EnumerateDirectories(_localCollectionFolder))
            {
                try
                {
                    if (!IsBloomBookFolder(path))
                        continue;
                    var bookFolderName = Path.GetFileName(path);
                    TryGetBookStatusJsonFromRepo(bookFolderName, out string statusJson);
                    if (!string.IsNullOrEmpty(statusJson))
                        continue; // matches book in repo, can't be source of rename
                    if (GetBookId(path) == id)
                        return Path.GetFileName(path);
                }
                catch (Exception)
                {
                    continue;
                }
            }

            return null;
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
        public void HandleNewBook(NewBookEventArgs args)
        {
            var bookBaseName = GetBookNameWithoutSuffix(args.BookFileName);
            //Debug.WriteLine("New: " + bookBaseName);
            // Bizarrely, we can get a new book notification when a book is being deleted.
            if (!IsBookPresentInRepo(bookBaseName))
            {
                //Debug.WriteLine("New book not found: " + bookBaseName);
                return;
            }

            if (args.BookFileName.EndsWith(".bloom"))
            {
                HandleNewBook(bookBaseName);
            }
        }

        private void HandleNewBook(string bookBaseName)
        {
            // It's quite likely in the case of a shared LAN folder that a new file has appeared, but the
            // other Bloom instance hasn't finished writing it yet. If we can't get its status,
            // it's probably locked, and anything else we might try will fail. Try again in 2 seconds.
            // There are cases in which we might keep retrying for a long time. See the longer explanation
            // in HandleModifiedFile() for why we decided not to try to do anything about this.
            if (!TryGetBookStatusJsonFromRepo(bookBaseName, out var status, false))
            {
                MiscUtils.SetTimeout(() => HandleNewBook(bookBaseName), 2000);
                //Debug.WriteLine("Could not get status for " + bookBaseName);
                return;
            }
            var statusFilePath = GetStatusFilePath(bookBaseName, _localCollectionFolder);
            // sometimes we get a new book notification when all that happened is it got checked in or out remotely.
            // If the book already exists and has status locally, then a new book notification is spurious,
            // so we don't want a message about it.
            if (!RobustFile.Exists(statusFilePath))
            {
                //Debug.WriteLine("could not find status at " + statusFilePath);
                if (!HandlePossibleRename(bookBaseName))
                {
                    //Debug.WriteLine("New book arrived: " + bookBaseName);
                    _tcLog.WriteMessage(
                        MessageAndMilestoneType.NewStuff,
                        "TeamCollection.NewBookArrived",
                        "A new book called '{0}' was added by a teammate.",
                        bookBaseName,
                        null
                    );
                }
            }
            //Debug.WriteLine("Updated status in NewBook for " + bookBaseName);
            // This needs to be AFTER we update the message log, data which it may use.
            // In case by any chance this is the only notification we get when checkout status changed
            // remotely, we do this even if we think the notiication is spurious.
            UpdateBookStatus(bookBaseName, true);
        }

        // This handles a LOCAL rename. Detecting a remote rename is handled in HandleModifiedFile.
        public void HandleBookRename(string oldName, string newName)
        {
            var status = GetLocalStatus(newName); // folder has already moved!
            if (
                status.lockedBy == TeamCollection.FakeUserIndicatingNewBook
                || string.IsNullOrEmpty(status.lockedBy)
            )
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
        internal static string GetStatusFilePath(string bookName, string collectionFolder)
        {
            // Don't use GetFileNameWithoutExtension here, what comes in might be a plain folder name
            // that doesn't have an extension, but might contain a period if the book title does.
            var bookFolderName = Path.GetFileName(bookName);
            if (bookFolderName.EndsWith(".bloom"))
                bookFolderName = bookFolderName.Substring(
                    0,
                    bookFolderName.Length - ".bloom".Length
                );
            var bookFolderPath = Path.Combine(collectionFolder, bookFolderName);
            return GetStatusFilePathFromBookFolderPath(bookFolderPath);
        }

        private static string GetStatusFilePathFromBookFolderPath(string bookFolderPath)
        {
            var statusFile = Path.Combine(bookFolderPath, "TeamCollection.status");
            return statusFile;
        }

        internal static bool IsBookKnownToTeamCollection(string bookFolderPath)
        {
            var statusFile = GetStatusFilePathFromBookFolderPath(bookFolderPath);
            return RobustFile.Exists(statusFile);
        }

        internal void WriteLocalStatus(
            string bookFolderName,
            BookStatus status,
            string collectionFolder = null,
            string collectionId = null
        )
        {
#if DEBUG
            // Except in unit tests, where we do all sorts of weird things to simulate particular situations,
            // it is VERY bad to give a book a local status file when it is not in the repo. Bloom will
            // delete the book the next time it starts up!
            if (!Program.RunningUnitTests)
            {
                // Check for a book being renamed.
                if (!String.IsNullOrEmpty(status.oldName))
                    Debug.Assert(
                        GetBookStatusJsonFromRepo(status.oldName) != null,
                        "Should never write local status for a renamed book that's not in repo for previous name"
                    );
                else
                    Debug.Assert(
                        GetBookStatusJsonFromRepo(bookFolderName) != null,
                        "Should never write local status for a book that's not in repo"
                    );
            }
#endif
            var statusFilePath = GetStatusFilePath(
                bookFolderName,
                collectionFolder ?? _localCollectionFolder
            );
            var statusToWrite = status.WithCollectionId(collectionId ?? CollectionId);
            RobustFile.WriteAllText(statusFilePath, statusToWrite.ToJson(), Encoding.UTF8);
        }

        /// <summary>
        /// Add to the list any TC-specific files that should be deleted or not copied
        /// when making most kinds of duplicates or publications of the book (folderPath is for book)
        /// and when making a bloompack (folderPath is for collection).
        /// </summary>
        public static void AddTCSpecificFiles(string folderPath, List<string> paths)
        {
            AddIfExists(paths, GetStatusFilePathFromBookFolderPath(folderPath));
            AddIfExists(paths, Path.Combine(folderPath, kLastcollectionfilesynctimeTxt));
            AddIfExists(
                paths,
                Path.Combine(folderPath, TeamCollectionManager.TeamCollectionLinkFileName)
            );
            AddIfExists(paths, Path.Combine(folderPath, "log.txt"));
            AddIfExists(paths, Path.Combine(folderPath, "impersonate.txt"));
        }

        static void AddIfExists(List<string> paths, string path)
        {
            if (RobustFile.Exists(path))
            {
                paths.Add(path);
            }
        }

        internal BookStatus GetLocalStatus(string bookFolderName, string collectionFolder = null)
        {
            var statusFilePath = GetStatusFilePath(
                bookFolderName,
                collectionFolder ?? _localCollectionFolder
            );
            if (RobustFile.Exists(statusFilePath))
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
            string result = null;
            // Wish we could retry at the level of reading individual files, but
            // each bit we successfully read modifies the state of the sha.
            // If something goes wrong, all we can really do is start over.
            RetryUtility.Retry(() =>
            {
                result = Book.Book.ComputeHashForAllBookRelatedFiles(sourceBookPath);
            });
            return result;
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
        // Note that errors logged here will not result in the TC dialog showing the Reload Collection
        // button, because we are here doing a reload, so all errors are logged as ErrorNoReload.
        void ReportProgressAndLog(
            IWebSocketProgress progress,
            ProgressKind kind,
            string l10nIdSuffix,
            string message,
            string param0 = null,
            string param1 = null
        )
        {
            var fullL10nId = string.IsNullOrEmpty(l10nIdSuffix)
                ? ""
                : "TeamCollection." + l10nIdSuffix;
            var msg = string.IsNullOrEmpty(l10nIdSuffix)
                ? message
                : string.Format(LocalizationManager.GetString(fullL10nId, message), param0, param1);
            progress.MessageWithoutLocalizing(msg, kind);
            _tcLog.WriteMessage(
                (kind == ProgressKind.Progress)
                    ? MessageAndMilestoneType.History
                    : MessageAndMilestoneType.ErrorNoReload,
                fullL10nId,
                message,
                param0,
                param1
            );
        }

        /// <summary>
        /// This overload reports the problem to the progress box, log, and Analytics. It should not be
        /// called directly; it is the common part of the two versions of ReportProblemSyncingBook which also
        /// save the report either in the book or collection history.
        /// </summary>
        void CoreReportProblemSyncingBook(
            IWebSocketProgress progress,
            ProgressKind kind,
            string l10nIdSuffix,
            string message,
            string param0 = null,
            string param1 = null
        )
        {
            ReportProgressAndLog(progress, kind, l10nIdSuffix, message, param0, param1);
            var msg = string.Format(message, param0, param1);
            Analytics.Track(
                "TeamCollectionError",
                new Dictionary<string, string> { { "message", msg } }
            );
        }

        /// <summary>
        /// This overload reports the problem to the progress box, log, and Analytics, and also makes an entry in
        /// the book's history.
        /// </summary>
        void ReportProblemSyncingBook(
            string folderPath,
            string bookId,
            IWebSocketProgress progress,
            ProgressKind kind,
            string l10nIdSuffix,
            string message,
            string param0 = null,
            string param1 = null,
            bool alsoMakeYouTrackIssue = false
        )
        {
            CoreReportProblemSyncingBook(progress, kind, l10nIdSuffix, message, param0, param1);
            var msg = string.Format(message, param0, param1);
            // The second argument is not the ideal name for the book, but unless it has no previous history,
            // the bookName will not be used. I don't think this is the place to be trying to instantiate
            // a Book object to get the ideal name for it. So I decided to live with using the file name.
            BookHistory.AddEvent(
                folderPath,
                Path.GetFileNameWithoutExtension(folderPath),
                bookId,
                BookHistoryEventType.SyncProblem,
                msg
            );
            if (alsoMakeYouTrackIssue)
                MakeYouTrackIssue(progress, msg);
        }

        /// <summary>
        /// This overload reports the problem to the progress box, log, and Analytics, and also makes an entry in
        /// the collection's book history. Use it when the problem will result in the book going away, so
        /// it can't be recorded in the book's own history.
        /// </summary>
        void ReportProblemSyncingBook(
            string collectionPath,
            string bookName,
            string bookId,
            IWebSocketProgress progress,
            ProgressKind kind,
            string l10nIdSuffix,
            string message,
            string param0 = null,
            string param1 = null,
            bool alsoMakeYouTrackIssue = false
        )
        {
            CoreReportProblemSyncingBook(progress, kind, l10nIdSuffix, message, param0, param1);
            var msg = string.Format(message, param0, param1);
            CollectionHistory.AddBookEvent(
                collectionPath,
                bookName,
                bookId,
                BookHistoryEventType.SyncProblem,
                msg
            );
            if (alsoMakeYouTrackIssue)
                MakeYouTrackIssue(progress, msg);
        }

        /// <summary>
        /// Make a YouTrack issue (unless we're running unit tests, or the user is unregistered,
        /// in which case don't bother, since the main point of creating the issue is so we
        /// can get in touch and offer help).
        /// </summary>
        private void MakeYouTrackIssue(IWebSocketProgress progress, string msg)
        {
            if (
                !Program.RunningUnitTests
                && !string.IsNullOrWhiteSpace(
                    SIL.Windows.Forms.Registration.Registration.Default.Email
                )
            )
            {
                var issue = new YouTrackIssueSubmitter(ProblemReportApi.YouTrackProjectKey);
                try
                {
                    var email = SIL.Windows.Forms.Registration.Registration.Default.Email;
                    var standardUserInfo = ProblemReportApi.GetStandardUserInfo(
                        email,
                        SIL.Windows.Forms.Registration.Registration.Default.FirstName,
                        SIL.Windows.Forms.Registration.Registration.Default.Surname
                    );
                    var lostAndFoundUrl =
                        "https://docs.bloomlibrary.org/team-collections-advanced-topics/#2488e17a8a6140bebcef068046cc57b7";
                    var admins = string.Join(
                        ", ",
                        (_tcManager?.Settings?.Administrators ?? new string[0]).Select(
                            e => ProblemReportApi.GetObfuscatedEmail(e)
                        )
                    );
                    // Note: there is deliberately no period after {msg} since msg usually ends with one already.
                    var fullMsg =
                        $"{standardUserInfo} \n(Admins: {admins}):\n\nThere was a book synchronization problem that required putting a version in Lost and Found:\n{msg}\n\nSee {lostAndFoundUrl}.";
                    var issueId = issue.SubmitToYouTrack("Book synchronization failed", fullMsg);
                    var issueLink = "https://issues.bloomlibrary.org/youtrack/issue/" + issueId;
                    ReportProgressAndLog(
                        progress,
                        ProgressKind.Note,
                        "ProblemReported",
                        "Bloom reported this problem to the developers."
                    );
                    // Originally added " You can see the report at {0}. Also see {1}", issueLink, lostAndFoundUrl); but JohnH says not to (BL-11867)
                }
                catch (Exception e)
                {
                    Debug.Fail(
                        "Submitting problem report to YouTrack failed with '" + e.Message + "'."
                    );
                }
            }
        }

        /// <summary>
        /// A list of strings known to occur in filenames Dropbox generates when it resolves conflicting changes.
        /// Not a completely reliable way to identify them, especially with an incomplete list of localizations,
        /// but it's the best we can do.
        /// </summary>
        private string[] _conflictMarkers = new[]
        {
            "Conflicted copy",
            "Copie en conflit", // French
            "Cópia em conflito", // Spanish
            "конфликтующая копия", // Russian
            "冲突副本", // zh-cn, mainland chinese
            "衝突複本" // zh-tx, taiwan
            // Probably many others
        };

        /// <summary>
        /// In our early TC alphas and very early 5.0 betas, the TeamCollection.status files we keep in each book's folder
        /// were called book.status. This code converts them. It can be discarded once all early adopters
        /// have used a version that has this once.
        /// </summary>
        public void MigrateStatusFiles()
        {
            foreach (var path in Directory.EnumerateDirectories(_localCollectionFolder))
            {
                try
                {
                    if (!IsBloomBookFolder(path))
                        continue;
                    var bookFolderName = Path.GetFileName(path);
                    var statusFilePath = GetStatusFilePath(bookFolderName, _localCollectionFolder);
                    // data migration
                    var obsoleteTcStatusPath = Path.Combine(path, "book.status");
                    if (RobustFile.Exists(obsoleteTcStatusPath))
                    {
                        if (RobustFile.Exists(statusFilePath))
                            RobustFile.Delete(obsoleteTcStatusPath); // somehow left behind
                        else
                            RobustFile.Move(obsoleteTcStatusPath, statusFilePath); // migrate
                    }
                }
                catch (Exception ex)
                {
                    NonFatalProblem.ReportSentryOnly(
                        ex,
                        $"failed to migrate status file for {path}"
                    );
                }
            }
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
            Debug.Assert(
                !string.IsNullOrEmpty(CollectionId),
                "Collection ID must get set before we start syncing books"
            );
            _tcLog.WriteMilestone(MessageAndMilestoneType.Reloaded);

            var hasProblems = false; //set true if we get any problems

            var repoBooksByIdMap = GetRepoBooksByIdMap();

            // The list of these that we maintain to track changes while we are running
            // is distinct from the list that is a local variable here and tracks ones
            // we actually are in the process of fixing.
            _remotelyRenamedBooks.Clear();
            var remotelyRenamedBooks = new HashSet<string>(); // Books actually found to be renamed (by new name)

            // Delete books that we think have been deleted remotely from the repo.
            // If it's a join collection merge, check new books in instead.
            var englishSomethingWrongMessage =
                "Something went wrong trying to sync with the book {0} in your Team Collection.";
            var oldBookNames = new HashSet<string>();
            foreach (var path1 in Directory.EnumerateDirectories(_localCollectionFolder))
            {
                // This should only affect new books (when firstTimeJoin is true),
                // at least once things stabilize so we only ever put things with
                // safe names in TCs.
                var path = BookStorage.MoveBookToSafeName(path1);
                try
                {
                    if (!IsBloomBookFolder(path))
                        continue;
                    var bookFolderName = Path.GetFileName(path);
                    var validRepoStatus = TryGetBookStatusJsonFromRepo(
                        bookFolderName,
                        out string statusJson
                    );
                    var localStatusFilePath = GetStatusFilePath(
                        bookFolderName,
                        _localCollectionFolder
                    );
                    if (statusJson == null) // includes cases where validRepoStatus is false
                    {
                        if (firstTimeJoin && validRepoStatus)
                        {
                            // There's no corresponding book in the repo (the null repo status is valid).
                            // We generally want to copy all local books into the repo.
                            // However, it's just possible that we have it in the repo by a different name.
                            // This catches some unlikely scenarios:
                            // - the book has been checked out and renamed locally (how is this possible if we're just now
                            // joining the repo for the first time?). We will leave it checked out.
                            // - the book was renamed remotely since we made the local collection we're merging with
                            // - the book was renamed locally since we made the local collection we're merging with
                            // (but independent of any checkout process)
                            // In the latter two cases we will keep the local version but not do an automatic checkin.
                            var id = GetBookId(bookFolderName);
                            var bookHasBeenRenamed =
                                id != null
                                && repoBooksByIdMap.TryGetValue(
                                    id,
                                    out Tuple<string, bool> repoState
                                )
                                && !repoState.Item2;
                            // We also don't want to add it back if it is known to have been deleted.
                            // It's somewhat questionable what should happen if it has local status but isn't one
                            // of the cases above. Suggests it has somehow gone away remotely, but not in one
                            // of the expected ways. Seems safest to go ahead and merge it.
                            if (!bookHasBeenRenamed && !KnownToHaveBeenDeleted(bookFolderName))
                            {
                                PutBook(path, true);
                                continue;
                            }

                            // the remote rename case is handled below.
                        }

                        // no sign of book in repo...should we delete it?
                        if (!RobustFile.Exists(localStatusFilePath))
                        {
                            var id = GetBookId(bookFolderName);
                            if (
                                id != null
                                && repoBooksByIdMap.TryGetValue(
                                    id,
                                    out Tuple<string, bool> repoState
                                )
                            )
                            {
                                // We have a book in the repo with this ID but a different name. This is a conflict,
                                // whether or not we have a local book by the same name, and whether or not we're
                                // joining for the first time
                                PutBook(path, inLostAndFound: true);
                                SIL.IO.RobustIO.DeleteDirectory(path, true);
                                hasProblems = true;
                                ReportProblemSyncingBook(
                                    _localCollectionFolder,
                                    bookFolderName,
                                    id,
                                    progress,
                                    ProgressKind.Error,
                                    "TeamCollection.ConflictingIdMove",
                                    "The book \"{0}\" was moved to Lost and Found, since it has the same ID as the book \"{1}\" in the team collection.",
                                    bookFolderName,
                                    repoState.Item1
                                );
                                // We will copy the conflicting book to local in the second loop.
                                continue;
                            }
                            // If there's no local status (and no id conflict), presume it's a newly created local book and keep it
                            continue;
                        }

                        var statusLocal = GetLocalStatus(bookFolderName);
                        if (statusLocal.collectionId != CollectionId)
                        {
                            // The local status is bogus...it was not written by operations in this collection
                            // Most likely, the book was copied from another TC using Explorer or similar.
                            // We'll treat it like any other locally newly-created book by getting rid of the
                            // bogus status.
                            RobustFile.Delete(localStatusFilePath);
                            continue;
                        }

                        // On this branch, there is valid local status, so the book has previously been shared.
                        // If the repo file is corrupt, it's safest to do nothing more.
                        if (!validRepoStatus)
                            continue;
                        // It's now missing from the repo. Explore more options.
                        if (
                            statusLocal.lockedBy == TeamCollectionManager.CurrentUser
                            && statusLocal.lockedWhere == TeamCollectionManager.CurrentMachine
                        )
                        {
                            // existing book folder checked out with status file, but nothing matching in repo.
                            // Most likely it is in the process of being renamed. In that case, not only
                            // should we not delete it, we should avoid re-creating the local book it was
                            // renamed from, for which we most likely have a .bloom in the repo.
                            // Here we just remember the name.
                            var oldName = GetLocalStatus(bookFolderName).oldName;
                            if (!string.IsNullOrEmpty(oldName))
                            {
                                oldBookNames.Add(oldName);
                                continue;
                            }

                            // If it's checked out here, assume current user wants it and keep it.
                            // If he checks it in, that will undo the delete...may annoy the user
                            // who deleted it, but that's life in a shared collection.
                            // However, since it's not in the repo, it's most natural for it to be in the
                            // 'newly created book' state, that is, with no status. So get rid of the local status.
                            RobustFile.Delete(localStatusFilePath);
                            continue;
                        }

                        // It's not checked out here and has local status (implying it was once shared) and isn't in the repo now.
                        // Can we identify it as a rename?
                        var localId = BookMetaData
                            .FromFolder(Path.Combine(_localCollectionFolder, bookFolderName))
                            .Id;
                        if (
                            repoBooksByIdMap.TryGetValue(localId, out Tuple<string, bool> val)
                            && !val.Item2
                        )
                        {
                            // We have the same book in the repo under a new name and no corresponding local book, report a remote rename.
                            var newName1 = val.Item1;
                            ReportProgressAndLog(
                                progress,
                                ProgressKind.Progress,
                                "RenameFromRemote",
                                "The book \"{0}\" has been renamed to \"{1}\" by a teammate.",
                                bookFolderName,
                                newName1
                            );
                            remotelyRenamedBooks.Add(newName1);
                            // It may have been edited too. We'll copy the new version to local in the other pass.
                            SIL.IO.RobustIO.DeleteDirectoryAndContents(path);
                            continue;
                        }

                        // Can we confirm that someone deliberately deleted it? If so we will get rid of it.
                        if (KnownToHaveBeenDeleted(bookFolderName))
                        {
                            ReportProgressAndLog(
                                progress,
                                ProgressKind.Warning,
                                "DeleteLocal",
                                "Moving '{0}' to your recycle bin as it was deleted in the Team Collection.",
                                bookFolderName
                            );
                            PathUtilities.DeleteToRecycleBin(path);
                            continue;
                        }

                        // Gone from the repo, but we don't know why. The safest thing seems to be to
                        // treat it as a new locally-created book.
                        ReportProgressAndLog(
                            progress,
                            ProgressKind.Warning,
                            "RemoteBookMissing",
                            "The book '{0}' is no longer in the Team Collection. It has been kept in your local collection.",
                            bookFolderName
                        );
                        RobustFile.Delete(localStatusFilePath);
                    }
                }
                catch (Exception ex)
                {
                    // Something went wrong with dealing with this book, but we'd like to carry on with
                    // syncing the rest of the collection
                    ReportProgressAndLog(
                        progress,
                        ProgressKind.Error,
                        "SomethingWentWrong",
                        englishSomethingWrongMessage,
                        path,
                        null
                    );
                    ReportProgressAndLog(progress, ProgressKind.Error, null, ex.Message);
                    Logger.WriteError(ex);
                    NonFatalProblem.ReportSentryOnly(
                        ex,
                        string.Format(englishSomethingWrongMessage, path)
                    );
                    hasProblems = true;
                }
            }

            // Now looking at each book that is already shared...
            // Note: a number of 'continue' statements here are redundant. But they serve as
            // a useful marker that we are satisfied we've done all that is needed in a particular
            // situation we've identified.
            foreach (var bookName in GetBookList())
            {
                try
                {
                    var localFolderPath = Path.Combine(_localCollectionFolder, bookName);
                    if (!Directory.Exists(localFolderPath))
                    {
                        if (oldBookNames.Contains(bookName))
                        {
                            // it's a book we're in the process of renaming, but hasn't yet been
                            // checked in using the new name. Leave it alone.
                            continue;
                        }

                        var nameLc = bookName.ToLowerInvariant();
                        if (_conflictMarkers.Any(m => nameLc.Contains(m.ToLowerInvariant())))
                        {
                            // Book looks like a DropBox conflict file. Typically results when two users checked
                            // in changes while both were offline.
                            ReportProgressAndLog(
                                progress,
                                ProgressKind.Error,
                                "ResolvedDropboxConflict",
                                "Two members of your team had a book checked out at the same time, so the Team Collection got two different versions of it. Bloom has moved \"{0}\" to the Lost & Found.",
                                bookName
                            );
                            MoveRepoBookToLostAndFound(bookName);
                            hasProblems = true;
                            continue;
                        }

                        // brand new book! Get it.
                        hasProblems |= !CopyBookFromRepoToLocalAndReport(
                            progress,
                            bookName,
                            () =>
                            {
                                if (!remotelyRenamedBooks.Contains(bookName))
                                {
                                    // Report the new book, unless we already reported it as a rename.
                                    ReportProgressAndLog(
                                        progress,
                                        ProgressKind.Progress,
                                        "FetchedNewBook",
                                        "Fetching a new book '{0}' from the Team Collection",
                                        bookName
                                    );
                                }
                            }
                        );

                        continue;
                    }

                    var repoStatus = GetStatus(bookName); // we know it's in the repo, so status will certainly be from there.
                    var statusFilePath = GetStatusFilePath(bookName, _localCollectionFolder);
                    if (!RobustFile.Exists(statusFilePath))
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
                            // The remote book has the same name as a local book that is not known to be in the Team Collection.
                            if (firstTimeJoin)
                            {
                                // We don't know the previous history of the collection. Quite likely it was duplicated some
                                // other way and these books have been edited independently. Treat it as a conflict.
                                PutBook(localFolderPath, inLostAndFound: true);
                                var bookId = GetBookId(bookName);
                                // warn the user
                                hasProblems = true;
                                // Make the local folder match the repo (this is where 'they win')
                                CopyBookFromRepoToLocalAndReport(
                                    progress,
                                    bookName,
                                    () =>
                                        ReportProblemSyncingBook(
                                            localFolderPath,
                                            bookId,
                                            progress,
                                            ProgressKind.Error,
                                            "ConflictingCheckout",
                                            "Found different versions of '{0}' in the local and team collections. The team version has been copied to the local collection, and the old local version to Lost and Found",
                                            bookName
                                        )
                                );
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

                                SIL.IO.RobustIO.MoveDirectory(localFolderPath, renameFolder);
                                // Don't use ChangeExtension here, bookName may have arbitrary periods.
                                var renamePath = Path.Combine(
                                    renameFolder,
                                    Path.GetFileName(renameFolder) + ".htm"
                                );
                                var oldBookPath = Path.Combine(renameFolder, bookName + ".htm");
                                ReportProgressAndLog(
                                    progress,
                                    ProgressKind.Warning,
                                    "RenamingBook",
                                    "Renaming the local book '{0}' because there is a new one with the same name from the Team Collection",
                                    bookName
                                );
                                RobustFile.Move(oldBookPath, renamePath);

                                hasProblems |= !CopyBookFromRepoToLocalAndReport(
                                    progress,
                                    bookName,
                                    () => { }
                                ); // Get repo book and status
                                // Review: does this deserve a warning?
                                continue;
                            }
                        }
                    }

                    // We know there's a local book by this name and both have status.
                    var localStatus = GetLocalStatus(bookName);

                    if (!IsCheckedOutHereBy(localStatus))
                    {
                        if (localStatus.checksum != repoStatus.checksum)
                        {
                            // Changed and not checked out. Just bring it up to date.
                            hasProblems |= !CopyBookFromRepoToLocalAndReport(
                                progress,
                                bookName,
                                () =>
                                    ReportProgressAndLog(
                                        progress,
                                        ProgressKind.Progress,
                                        "Updating",
                                        "Updating '{0}' to match the Team Collection",
                                        bookName
                                    )
                            ); // updates everything local.
                        }
                        // Possibly it has been renamed remotely, but we did not detect it normally because
                        // the only change is the case of letters in the name, which Windows ignores.
                        EnsureConsistentCasingInLocalName(bookName);

                        // whether or not we updated it, if it's not checked out there's no more to do.
                        continue;
                    }

                    var localAndRepoChecksumsMatch = localStatus.checksum == repoStatus.checksum;

                    // At this point, we know there's a version of the book in the repo
                    // and a local version that is checked out here according to local status.
                    if (IsCheckedOutHereBy(repoStatus))
                    {
                        if (localAndRepoChecksumsMatch)
                            continue;
                        else
                        {
                            // We don't expect this to happen. But it did in BL-12590
                            // (though it is possible that was the result of a "super user" doing file manipulation).
                            // Anyway, if it happens, better be safe and move the local version to lost and found.
                            // That's what the UI already said we would do. We just hadn't hooked up the back end to do it.
                        }
                    }

                    // Now we know there's some sort of conflict. The local and repo status of this
                    // book don't match.
                    if (localAndRepoChecksumsMatch)
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
                                var bookId = GetBookId(bookName);
                                // warn the user
                                hasProblems = true;
                                // Make the local folder match the repo (this is where 'they win')
                                CopyBookFromRepoToLocalAndReport(
                                    progress,
                                    bookName,
                                    () =>
                                        ReportProblemSyncingBook(
                                            localFolderPath,
                                            bookId,
                                            progress,
                                            ProgressKind.Error,
                                            "ConflictingCheckout",
                                            "The book '{0}', which was checked out and edited, was checked out to someone else in the Team Collection. Local changes have been overwritten, but are saved to Lost-and-found.",
                                            bookName,
                                            alsoMakeYouTrackIssue: true
                                        )
                                );
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
                            hasProblems |= !CopyBookFromRepoToLocalAndReport(
                                progress,
                                bookName,
                                () =>
                                    ReportProgressAndLog(
                                        progress,
                                        ProgressKind.Progress,
                                        "Updating",
                                        "Updating '{0}' to match the Team Collection",
                                        bookName
                                    )
                            );
                            WriteBookStatus(
                                bookName,
                                localStatus.WithChecksum(repoStatus.checksum)
                            );
                            continue;
                        }

                        // Copy current local to lost and found
                        var bookFolder = Path.Combine(_localCollectionFolder, bookName);
                        PutBook(bookFolder, inLostAndFound: true);
                        var bookId = GetBookId(bookName);
                        // copy repo book and status to local
                        // warn the user
                        hasProblems = true;
                        CopyBookFromRepoToLocalAndReport(
                            progress,
                            bookName,
                            () =>
                                ReportProblemSyncingBook(
                                    bookFolder,
                                    bookId,
                                    progress,
                                    ProgressKind.Error,
                                    "ConflictingEdit",
                                    "The book '{0}', which was checked out and edited on this computer, was modified in the Team Collection by someone else. Local changes have been overwritten, but are saved to Lost-and-found.",
                                    bookName,
                                    alsoMakeYouTrackIssue: true
                                )
                        );

                        continue;
                    }
                }
                catch (Exception ex)
                {
                    // Something went wrong with dealing with this book, but we'd like to carry on with
                    // syncing the rest of the collection
                    ReportProgressAndLog(
                        progress,
                        ProgressKind.Error,
                        "SomethingWentWrong",
                        englishSomethingWrongMessage,
                        bookName
                    );
                    ReportProgressAndLog(progress, ProgressKind.Error, null, ex.Message);
                    Logger.WriteError(ex);
                    NonFatalProblem.ReportSentryOnly(
                        ex,
                        string.Format(englishSomethingWrongMessage, bookName)
                    );
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

        protected string GetBookId(string bookFolderName)
        {
            return BookMetaData
                .FromFolder(Path.Combine(_localCollectionFolder, bookFolderName))
                ?.Id;
        }

        /// <summary>
        /// Return a dictionary of all books in the repo which do not correspond to a local book folder.
        /// key: book GUID; value: (book name in repo (without extension), haveCorrespondingLocalBook).
        /// </summary>
        private Dictionary<string, Tuple<string, bool>> GetRepoBooksByIdMap()
        {
            var newBooks = new Dictionary<string, Tuple<string, bool>>();

            foreach (var bookName in GetBookList())
            {
                var localFolderPath = Path.Combine(_localCollectionFolder, bookName);
                try
                {
                    var meta = GetRepoBookFile(bookName, "meta.json");
                    if (!string.IsNullOrEmpty(meta) && meta != "error")
                    {
                        var id = GetIdFrom(meta, bookName);
                        if (id != null)
                            newBooks[id] = Tuple.Create(
                                bookName,
                                Directory.Exists(localFolderPath)
                            );
                        // we just won't treat it as a possible rename or conflict if we can't extract an ID from the meta.json.
                    }
                }
                catch (Exception e)
                    when (e is ICSharpCode.SharpZipLib.Zip.ZipException || e is IOException)
                {
                    // we just won't treat it as a possible rename or conflict if we can't get the meta.json.
                }
            }

            return newBooks;
        }

        // Return true if copied successfully, false if there is a problem (which this method will report).
        private bool CopyBookFromRepoToLocalAndReport(
            IWebSocketProgress progress,
            string bookName,
            Action reportSuccess
        )
        {
            var error = CopyBookFromRepoToLocal(bookName);
            if (error != null)
            {
                ReportProgressAndLog(progress, ProgressKind.Error, "", error);
                return false;
            }
            else
            {
                reportSuccess();
            }

            return true;
        }

        public BloomWebSocketServer SocketServer;
        public TeamCollectionManager TCManager;
        private FileSystemWatcher _localFolderWatcher;

        protected void ShowProgressDialog(
            string title,
            Func<IWebSocketProgress, BackgroundWorker, bool> doWhat,
            Action<Form> doWhenMainActionFalse = null
        )
        {
            // If you want to change this to use the new overload where the ProgressDialog is embedded in
            // the open window, remember:
            // - there has to be an HTML window open that has an EmbeddedProgressDialog somewhere
            // - the new overload returns before doWhat finishes, so you will have to modify callers
            // and turn whatever happens after this method returns into an action that can be passed
            // as doWhenDialogCloses.
            BrowserProgressDialog.DoWorkWithProgressDialog(
                SocketServer,
                () =>
                    new ReactDialog(
                        "progressDialogBundle",
                        // props to send to the react component
                        // N.B. BloomExe\TeamCollection has a difference "casing" than BloomBrowserUI\teamCollection !
                        new
                        {
                            title,
                            titleIcon = "/bloom/teamCollection/Team Collection.svg",
                            titleColor = "white",
                            titleBackgroundColor = Palette.kBloomBlueHex,
                            showReportButton = "if-error"
                        },
                        "Sync Team Collection"
                    )
                    // winforms dialog properties
                    {
                        Width = 620,
                        Height = 550
                    },
                doWhat,
                doWhenMainActionFalse
            );
        }

        /// <summary>
        /// Main entry point called before creating CollectionSettings; updates local folder to match
        /// repo one, if any. Not unit tested, as it mainly handles wrapping SyncAtStartup with a
        /// progress dialog.
        /// </summary>
        public void SynchronizeRepoAndLocal(Action whenDone = null)
        {
            Analytics.Track(
                "TeamCollectionOpen",
                new Dictionary<string, string>()
                {
                    { "CollectionId", CollectionId },
                    { "CollectionName", _tcManager?.Settings?.CollectionName },
                    { "Backend", GetBackendType() },
                    { "User", TeamCollectionManager.CurrentUser }
                }
            );

            var title = "Syncing Team Collection"; // todo l10n
            ShowProgressDialog(
                title,
                (progress, worker) =>
                {
                    // Not useful to have the date and time in the progress dialog, but definitely
                    // handy to record at the start of each section in the saved log. Tells us when anything it
                    // had to do to sync things actually happened.
                    progress.Message(
                        "StartingSync",
                        "",
                        "Starting sync with Team Collection",
                        ProgressKind.Progress
                    );

                    bool doingFirstTimeJoinCollectionMerge =
                        TeamCollectionManager.NextMergeIsFirstTimeJoinCollection;
                    TeamCollectionManager.NextMergeIsFirstTimeJoinCollection = false;
                    // don't want messages about the collection being changed while we're synchronizing,
                    // and in some cases we might be the source of several changes (for example, multiple
                    // check ins while joining a collection). Normally we suppress notifications for
                    // our own checkins, but remembering the last thing we checked in might not be
                    // enough when we do several close together.
                    StopMonitoring();

                    MigrateStatusFiles();
                    var waitForUserToCloseDialogOrReportProblems = SyncAtStartup(
                        progress,
                        doingFirstTimeJoinCollectionMerge
                    );

                    // Now that we've finished synchronizing, update these icons based on the post-sync result
                    // REVIEW: What do we want to happen if exception throw here? Should we add to {problems} list?
                    UpdateStatusOfAllCheckedOutBooks();

                    progress.Message("Done", "Done");

                    // Tasks that are waiting for the sync may be done now, whether or not it had errors.
                    whenDone?.Invoke();
                    // The dialog may continue to show for a bit, but other idle-time startup tasks
                    // (possibly queued during whenDone()) may continue.
                    StartupScreenManager.ConsiderCurrentTaskDone();

                    // Review: are any of the cases we don't treat as warnings or errors important enough to wait
                    // for the user to read them and close the dialog manually?
                    // Currently it stays open only if we detected problems.
                    return waitForUserToCloseDialogOrReportProblems;
                },
                (dlg) =>
                {
                    // When we would normally close the splash screen, close the progress dialog.
                    StartupScreenManager.DoWhenSplashScreenShouldClose(() =>
                    {
                        // Not dlg.Close(); that may not clear ReactDialog.CurrentOpenModal fast enough.
                        dlg.Invoke((Action)(() => ReactDialog.CloseCurrentModal()));
                    });
                }
            );

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

        public bool CanSaveChanges(BookInfo info)
        {
            return !NeedCheckoutToEdit(info.FolderPath);
        }

        public virtual bool CanChangeBookInstanceId(BookInfo info)
        {
            return !IsBookPresentInRepo(info.FolderName);
        }

        /// <summary>
        /// Given the name (with or without preceding path) of a Team Collection folder,
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
        /// If we ever get another implementation that does use .JoinBloomTC, thought will be needed as to
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

        public virtual TeamCollectionStatus CollectionStatus => _tcLog.TeamCollectionStatus;

        // A description of the repo, typically useful for locating it, for example, the path to its folder.
        public abstract string RepoDescription { get; }

        /// <summary>
        /// Causes a notification to be sent to the UI to update the checkout status icon for {bookName}
        /// </summary>
        /// <param name="bookName">The name of the book</param>
        /// <param name="shouldNotifyIfNotCheckedOut">If true, will also send an event that a book isn't checked out
        /// (This is necessary when books are checked in).</param>
        public void UpdateBookStatus(string bookName, bool shouldNotifyIfNotCheckedOut)
        {
            Debug.Assert(
                !bookName.EndsWith(".bloom"),
                $"UpdateBookStatus was passed bookName=\"{bookName}\", which has a .bloom suffix. This is probably incorrect. This function wants only the bookBaseName"
            );

            var bookFolder = Path.Combine(_localCollectionFolder, bookName);
            if (!Directory.Exists(bookFolder))
            {
                RaiseBookStatusChanged(bookName, CheckedOutBy.Deleted);
                return;
            }

            BookStatus status;
            try
            {
                status = GetStatus(bookName);
            }
            catch (Exception ex)
            {
                Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(ex);

                // We may want to do more, such as put a red circle on the book. But at least don't crash.
                // The case where we observed this, a corrupt zip file that can't be read, caused
                // plenty of other errors, so I'm thinking just giving up is enough. This is just
                // trying to give the user a quick idea of status.
                return;
            }

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
            _tcManager.RaiseBookStatusChanged(
                new BookStatusChangeEventArgs(bookName, checkedOutByWhom)
            );

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
                "You will need to register this copy of Bloom with an email address before participating in a Team Collection"
            );
        }

        public virtual bool CannotDeleteBecauseDisconnected(string bookFolderPath)
        {
            return false;
        }

        public abstract string GetBackendType();
    }
}
