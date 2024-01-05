using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.TeamCollection;
using Bloom.ToPalaso;
using Bloom.WebLibraryIntegration;
using SIL.IO;
using SIL.Reporting;
using SIL.Windows.Forms.FileSystem;

namespace Bloom.Collection
{
    public class BookCollection
    {
        public enum CollectionType
        {
            TheOneEditableCollection,
            SourceCollection
        }

        public delegate BookCollection Factory(string path, CollectionType collectionType); //autofac uses this

        public EventHandler CollectionChanged;

        private readonly string _path;
        private List<BookInfo> _bookInfos;
        private TeamCollectionManager _tcManager;

        private readonly BookSelection _bookSelection;
        private Timer _folderChangeDebounceTimer;
        private static HashSet<string> _changingFolders = new HashSet<string>();
        private BloomWebSocketServer _webSocketServer;

        public static event EventHandler CollectionCreated;

        //for moq only
        public BookCollection() { }

        // For unit tests only.
        internal BookCollection(List<BookInfo> state)
        {
            _bookInfos = state;
        }

        public BookCollection(
            string path,
            CollectionType collectionType,
            BookSelection bookSelection,
            TeamCollectionManager tcm = null,
            BloomWebSocketServer webSocketServer = null
        )
        {
            _path = path;
            _bookSelection = bookSelection;
            _tcManager = tcm;
            _webSocketServer = webSocketServer;

            Type = collectionType;

            if (collectionType == CollectionType.TheOneEditableCollection)
            {
                MakeCollectionCSSIfMissing();
            }

            CollectionCreated?.Invoke(this, new EventArgs());

            if (ContainsDownloadedBooks)
            {
                WatchDirectory();
            }
        }

        /// <summary>
        /// Called when a file system watcher notices a new book (or some similar change) in our downloaded books folder.
        /// This will happen on a thread-pool thread.
        /// Since we are updating the UI in response we want to deal with it on the main thread.
        /// That also has the effect of a lock, preventing multiple threads trying to respond to changes.
        /// The main purpose of this method is to debounce such changes, since lots of them may
        /// happen in succession while downloading a book, and also some that we don't want
        /// to process may happen while we are selecting one. Debounced changes result in a websocket message
        /// that acts as an event for Javascript, and also raising a C# event.
        /// </summary>
        private void DebounceFolderChanged(string fullPath)
        {
            var shell = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Shell);
            SafeInvoke.InvokeIfPossible(
                "update downloaded books",
                shell,
                true,
                (Action)(
                    () =>
                    {
                        // We may notice a change to the downloaded books directory before the other Bloom instance has finished
                        // copying the new book there. Finishing should not take long, because the download is done...at worst
                        // we have to copy the book on our own filesystem. Typically we only have to move the directory.
                        // As a safeguard, wait half a second before we update things.
                        if (_folderChangeDebounceTimer != null)
                        {
                            // Things changed again before we started the update! Forget the old update and wait until things
                            // are stable for the required interval.
                            _folderChangeDebounceTimer.Stop();
                            _folderChangeDebounceTimer.Dispose();
                        }
                        _folderChangeDebounceTimer = new Timer();
                        _folderChangeDebounceTimer.Tick += (o, args) =>
                        {
                            try
                            {
                                _folderChangeDebounceTimer.Stop();
                                _folderChangeDebounceTimer.Dispose();
                                _folderChangeDebounceTimer = null;

                                // Updating the books involves selecting the modified book, which might involve changing
                                // some files (e.g., adding a branding image, BL-4914), which could trigger this again.
                                // So don't allow it to be triggered by changes to a folder we're already sending
                                // notifications about.
                                // (It's PROBABLY overkill to maintain a set of these...don't expect a notification about
                                // one folder to trigger a change to another...but better safe than sorry.)
                                // (Note that we don't need synchronized access to this dictionary, because all this
                                // happens only on the UI thread.)
                                if (!_changingFolders.Contains(fullPath))
                                {
                                    try
                                    {
                                        _changingFolders.Add(fullPath);
                                        _webSocketServer.SendEvent(
                                            "editableCollectionList",
                                            "reload:" + PathToDirectory
                                        );
                                        if (FolderContentChanged != null)
                                        {
                                            FolderContentChanged(
                                                this,
                                                new ProjectChangedEventArgs() { Path = fullPath }
                                            );
                                        }
                                    }
                                    finally
                                    {
                                        // Now we need to arrange to remove it again. Not right now, because
                                        // whatever changes might be made during event handling may get noticed slightly later.
                                        // But we don't want to miss it if the book gets downloaded again.
                                        RemoveFromChangingFoldersLater(fullPath);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                // Prevent unhandled errors from the event handler from causing fatal errors!
                                NonFatalProblem.Report(
                                    ModalIf.Alpha,
                                    PassiveIf.All,
                                    shortUserLevelMessage: "An error occurred while updating the downloaded book folder", // Since this is expected to be very rare, we don't bother localizing this, not even in LowPriority.xlf
                                    moreDetails: $"Folder: {fullPath}",
                                    exception: e
                                );
                            }
                        };
                        _folderChangeDebounceTimer.Interval = 500;
                        _folderChangeDebounceTimer.Start();
                    }
                )
            );
        }

        // Arrange that the specified path should be removed from changingFolders after 10s.
        // This means for 10s we wont pay attention to new changes to
        // this folder. Probably excessive, but this monitor is meant to catch new downloads;
        // it's unlikely the same book can be downloaded again within 10 seconds.
        private static void RemoveFromChangingFoldersLater(string fullPath)
        {
            var waitTimer = new Timer();
            waitTimer.Interval = 10000;
            waitTimer.Tick += (sender1, args1) =>
            {
                _changingFolders.Remove(fullPath);
                waitTimer.Stop();
                waitTimer.Dispose();
            };
            waitTimer.Start();
        }

        // We will ignore changes to this folder for 10s. This is typically because it
        // has just been selected. That may result in file mods as the book is brought
        // up to date, but we don't want to treat those changes like a new book or
        // version of a book arriving from BL.
        public static void TemporariliyIgnoreChangesToFolder(string fullPath)
        {
            _changingFolders.Add(fullPath);
            RemoveFromChangingFoldersLater(fullPath);
        }

        private void MakeCollectionCSSIfMissing()
        {
            string path = Path.Combine(_path, "customCollectionStyles.css");
            if (RobustFile.Exists(path))
                return;
            RobustFile.Copy(
                BloomFileLocator.GetBrowserFile(
                    false,
                    "bookLayout",
                    "collection styles override template.css"
                ),
                path
            );
        }

        public CollectionType Type { get; private set; }

        private void NotifyCollectionChanged()
        {
            if (CollectionChanged != null)
                CollectionChanged.Invoke(this, null);
        }

        public void DeleteBook(Book.BookInfo bookInfo)
        {
            var didDelete = ConfirmRecycleDialog.Recycle(bookInfo.FolderPath);
            if (!didDelete)
                return;

            Logger.WriteEvent("After BookStorage.DeleteBook({0})", bookInfo.FolderPath);
            HandleBookDeletedFromCollection(bookInfo.FolderPath);
            if (_bookSelection != null)
            {
                _bookSelection.SelectBook(null);
            }
        }

        /// <summary>
        /// Handles side effects of deleting a book (also used when remotely deleted)
        /// </summary>
        /// <param name="bookInfo"></param>
        public void HandleBookDeletedFromCollection(string folderPath)
        {
            var infoToDelete = _bookInfos.FirstOrDefault(b => b.FolderPath == folderPath);
            //Debug.Assert(_bookInfos.Contains(bookInfo)); this will occur if we delete a book from the BloomLibrary section
            if (infoToDelete != null) // for paranoia. We shouldn't be trying to delete a book that isn't there.
                _bookInfos.Remove(infoToDelete);

            if (CollectionChanged != null)
                CollectionChanged.Invoke(this, null);
        }

        public virtual string Name
        {
            get
            {
                var dirName = Path.GetFileName(_path);
                //the UI and existing Localizations want to see "templates", but on disk, "templates" is ambiguous, so the name there is "template books".
                return dirName == "template books" ? "Templates" : dirName;
            }
        }

        public string PathToDirectory
        {
            get { return _path; }
        }

        private object _bookInfoLock = new object();

        // Needs to be thread-safe
        public virtual IEnumerable<Book.BookInfo> GetBookInfos()
        {
            lock (_bookInfoLock)
            {
                if (_bookInfos == null)
                {
                    _watcherIsDisabled = true;
                    try
                    {
                        _bookInfos = new List<Book.BookInfo>();
                        var bookFolders = ProjectContext
                            .SafeGetDirectories(_path)
                            .Select(dir => new DirectoryInfo(dir))
                            .ToArray();

                        //var orderedBookFolders = bookFolders.OrderBy(f => f.Name);
                        var orderedBookFolders = bookFolders.OrderBy(
                            f => f.Name,
                            new NaturalSortComparer<string>()
                        );
                        foreach (var folder in orderedBookFolders)
                        {
                            if (Path.GetFileName(folder.FullName).StartsWith(".")) //as in ".hg"
                                continue;
                            // Don't want things in the templates/xmatter folder
                            // (even SIL-Cameroon-Mothballed, which no longer has xmatter in its filename)
                            // so filter on the whole path.
                            if (folder.FullName.ToLowerInvariant().Contains("xmatter"))
                                continue;
                            // Note: this used to be .bloom-ignore. We believe that is no longer used.
                            // It was changed because files starting with dot are normally invisible,
                            // which could make it hard to see why a book is skipped, and also because
                            // we were having trouble finding a way to get a file called .bloom-ignore
                            // included in the filesThatMightBeNeededInOutput list in gulpfile.js.
                            if (RobustFile.Exists(Path.Combine(folder.FullName, "BloomIgnore.txt")))
                                continue;
                            AddBookInfo(folder.FullName);
                        }
                        if (Type == CollectionType.TheOneEditableCollection)
                        {
                            UpdateBloomLibraryStatusOfBooks(_bookInfos, true);
                        }
                    }
                    finally
                    {
                        _watcherIsDisabled = false;
                    }
                }

                return _bookInfos;
            }
        }

        public void UpdateBookInfo(BookInfo info)
        {
            var oldIndex = _bookInfos.FindIndex(i => i.Id == info.Id);
            IComparer<string> comp = new NaturalSortComparer<string>();
            var newKey = Path.GetFileName(info.FolderPath);
            if (oldIndex >= 0)
            {
                // optimize: very often the new one will belong at the same index,
                // if that's the case we could just replace.
                _bookInfos.RemoveAt(oldIndex);
            }

            int newIndex = _bookInfos.FindIndex(
                x => comp.Compare(newKey, Path.GetFileName(x.FolderPath)) <= 0
            );
            if (newIndex < 0)
                newIndex = _bookInfos.Count;
            _bookInfos.Insert(newIndex, info);
            NotifyCollectionChanged();
        }

        public void AddBookInfo(BookInfo bookInfo)
        {
            _bookInfos.Add(bookInfo);
            NotifyCollectionChanged();
        }

        /// <summary>
        /// Insert a book into the appropriate place. If there is already a book with the same FolderPath, replace it.
        /// </summary>
        /// <param name="bookInfo"></param>
        public void InsertBookInfo(BookInfo bookInfo)
        {
            IComparer<string> comparer = new NaturalSortComparer<string>();
            for (int i = 0; i < _bookInfos.Count; i++)
            {
                var compare = comparer.Compare(_bookInfos[i].FolderPath, bookInfo.FolderPath);
                if (compare == 0)
                {
                    _bookInfos[i] = bookInfo; // Replace
                    return;
                }
                if (compare > 0)
                {
                    _bookInfos.Insert(i, bookInfo);
                    return;
                }
            }
            _bookInfos.Add(bookInfo);
        }

        private bool BackupFileExists(string folderPath)
        {
            var bakFiles = Directory.GetFiles(folderPath, BookStorage.BackupFilename);
            return bakFiles.Length == 1 && RobustFile.Exists(bakFiles[0]);
        }

        private void AddBookInfo(string folderPath)
        {
            try
            {
                //this is handy when windows explorer won't let go of the thumbs.db file, but we want to delete the folder
                if (
                    Directory.GetFiles(folderPath, "*.htm").Length == 0
                    && Directory.GetFiles(folderPath, "*.html").Length == 0
                    &&
                    // BL-6099: don't hide the book if we at least have a valid backup file
                    !BackupFileExists(folderPath)
                )
                    return;
                var editable = Type == CollectionType.TheOneEditableCollection;
                ISaveContext sc = editable
                    ? _tcManager?.CurrentCollectionEvenIfDisconnected
                        ?? new AlwaysEditSaveContext() as ISaveContext
                    : new NoEditSaveContext() as ISaveContext;
                // I think this is no longer true, but at one point we could sometimes (race condition) select a book
                // before its containing collection was initialized.
                // The bookInfo already in the book would eventually be more up-to-date (e.g., its AppearanceSettings gets
                // Initialized) than a new one we would create here. So use it instead of making a new one.
                var bookInfo =
                    (folderPath == _bookSelection.CurrentSelection?.FolderPath)
                        ? _bookSelection.CurrentSelection.BookInfo
                        : new BookInfo(folderPath, editable, sc);

                _bookInfos.Add(bookInfo);
            }
            catch (Exception e)
            {
                if (e.InnerException != null)
                {
                    e = e.InnerException;
                }
                var jsonPath = Path.Combine(folderPath, BookInfo.MetaDataFileName);
                Logger.WriteError("Reading " + jsonPath, e);
                try
                {
                    Logger.WriteEvent(
                        jsonPath
                            + " Contents: "
                            + System.Environment.NewLine
                            + RobustFile.ReadAllText(jsonPath)
                    );
                }
                catch (Exception readError)
                {
                    Logger.WriteError("Error reading " + jsonPath, readError);
                }

                //_books.Add(new ErrorBook(e, path, Type == CollectionType.TheOneEditableCollection));
                _bookInfos.Add(new ErrorBookInfo(folderPath, e) { });
            }
        }

        protected Color CoverColor
        {
            get { throw new NotImplementedException(); }
            set { throw new NotImplementedException(); }
        }

        public const string DownloadedBooksCollectionNameInEnglish = "Books From BloomLibrary.org";

        public bool ContainsDownloadedBooks
        {
            get { return Name == DownloadedBooksCollectionNameInEnglish; }
        }

        /// <summary>
        /// This includes everything in "factoryCollections" (i.e. Templates folder AND Sample Shells:Vaccinations folder)
        /// </summary>
        public bool IsFactoryInstalled
        {
            get { return BloomFileLocator.IsInstalledFileOrDirectory(PathToDirectory); }
        }

        private FileSystemWatcher _watcher;

        /// <summary>
        /// Watch for changes to your directory (currently just additions and updates). Raise FolderContentChanged if you see anything.
        /// </summary>
        public void WatchDirectory()
        {
            _watcher = new FileSystemWatcher();
            _watcher.Path = PathToDirectory;
            // The default filter, LastWrite|FileName|DirectoryName, is probably OK.
            // Watch everything for now.
            // _watcher.Filter = "*.txt";
            _watcher.Created += WatcherOnChange;
            _watcher.Changed += WatcherOnChange; // TODO: Actually, this raises events if the book folder or one of its file is deleted! Unfortunately, can't find easy way to filter these events out. See BL-12433
            // Begin watching.
            _watcher.EnableRaisingEvents = true;
        }

        /// <summary>
        /// This could plausibly be a Dispose(), but I don't want to make BookCollection Disposable, as most of them don't need it.
        /// </summary>
        public void StopWatchingDirectory()
        {
            if (_watcher != null)
            {
                _watcher.Dispose();
                _watcher = null;
            }
        }

        public event EventHandler<ProjectChangedEventArgs> FolderContentChanged;
        private bool _watcherIsDisabled = false;

        private void WatcherOnChange(object sender, FileSystemEventArgs fileSystemEventArgs)
        {
            if (_watcherIsDisabled)
                return;
            _bookInfos = null; // Possibly obsolete; next request will update it.
            DebounceFolderChanged(fileSystemEventArgs.FullPath);
        }

        public void UpdateBloomLibraryStatusOfBooks(
            List<BookInfo> bookInfos,
            bool skipBadgeUpdate = false
        )
        {
            if (bookInfos == null || bookInfos.Count == 0)
                return;
            // This queries Parse for the status of each book in bookInfos, adds (or clears)
            // the status to each BookInfo, and signals the UI to update the thumbnail badge
            // (unless told not to).
            var bloomLibraryApiClient = new BloomLibraryBookApiClient();
            var bloomLibraryStatusesById = bloomLibraryApiClient.GetLibraryStatusForBooks(
                bookInfos
            );
            // Now to store the data into the BookInfos and signal the UI to update.
            foreach (var bookInfo in bookInfos)
            {
                bool updateThumbnailBadge = false;
                if (bloomLibraryStatusesById.TryGetValue(bookInfo.Id, out var status))
                {
                    if (
                        bookInfo.BloomLibraryStatus == null || bookInfo.BloomLibraryStatus != status
                    )
                    {
                        bookInfo.BloomLibraryStatus = status;
                        updateThumbnailBadge = true;
                    }
                }
                else if (bookInfo.BloomLibraryStatus != null)
                {
                    bookInfo.BloomLibraryStatus = null;
                    updateThumbnailBadge = true;
                }
                if (updateThumbnailBadge && !skipBadgeUpdate)
                {
                    _webSocketServer.SendString("bookCollection", "updateBookBadge", bookInfo.Id);
                }
            }
        }

        public BookInfo GetBookInfoByFolderPath(string path)
        {
            return GetBookInfos().FirstOrDefault(b => b.FolderPath == path);
        }

        public BookInfo GetBookInfoById(string id)
        {
            return GetBookInfos().FirstOrDefault(b => b.Id == id);
        }
    }

    public class ProjectChangedEventArgs : EventArgs
    {
        public string Path { get; set; }
    }
}
