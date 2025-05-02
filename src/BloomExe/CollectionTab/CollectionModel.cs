using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.History;
using Bloom.MiscUI;
using Bloom.Properties;
using Bloom.SafeXml;
using Bloom.TeamCollection;
using Bloom.ToPalaso;
using Bloom.ToPalaso.Experimental;
using Bloom.Utils;
using Bloom.web.controllers;
using DesktopAnalytics;
using L10NSharp;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;
using SIL.Windows.Forms;
using SIL.Windows.Forms.FileSystem;
using SIL.Xml;

namespace Bloom.CollectionTab
{
    public class CollectionModel
    {
        private readonly BookSelection _bookSelection;
        private readonly string _pathToCollection;
        private readonly CollectionSettings _collectionSettings;
        private readonly SourceCollectionsList _sourceCollectionsList;
        private readonly BookCollection.Factory _bookCollectionFactory;
        private readonly EditBookCommand _editBookCommand;
        private readonly BookServer _bookServer;
        private readonly CurrentEditableCollectionSelection _currentEditableCollectionSelection;
        private List<BookCollection> _bookCollections;
        private readonly BookThumbNailer _thumbNailer;
        private TeamCollectionManager _tcManager;
        private readonly BloomWebSocketServer _webSocketServer;
        private LocalizationChangedEvent _localizationChangedEvent;
        private BookCollectionHolder _bookCollectionHolder;

        public CollectionModel(
            string pathToCollection,
            CollectionSettings collectionSettings,
            BookSelection bookSelection,
            SourceCollectionsList sourceCollectionsList,
            BookCollection.Factory bookCollectionFactory,
            EditBookCommand editBookCommand,
            CreateFromSourceBookCommand createFromSourceBookCommand,
            BookServer bookServer,
            CurrentEditableCollectionSelection currentEditableCollectionSelection,
            BookThumbNailer thumbNailer,
            TeamCollectionManager tcManager,
            BloomWebSocketServer webSocketServer,
            BookCollectionHolder bookCollectionHolder,
            LocalizationChangedEvent localizationChangedEvent
        )
        {
            _bookSelection = bookSelection;
            _pathToCollection = pathToCollection;
            _collectionSettings = collectionSettings;
            _sourceCollectionsList = sourceCollectionsList;
            _bookCollectionFactory = bookCollectionFactory;
            _editBookCommand = editBookCommand;
            _bookServer = bookServer;
            _currentEditableCollectionSelection = currentEditableCollectionSelection;
            _thumbNailer = thumbNailer;
            _tcManager = tcManager;
            _webSocketServer = webSocketServer;
            _bookCollectionHolder = bookCollectionHolder;
            _localizationChangedEvent = localizationChangedEvent;

            createFromSourceBookCommand.Subscribe(CreateFromSourceBook);
        }

        public BookCollection CurrentEditableCollection =>
            _currentEditableCollectionSelection.CurrentSelection;

        /// <summary>
        /// The constructor of BookCommandsApi calls this to work around an Autofac circularity problem.
        /// </summary>
        public BookCommandsApi BookCommands { get; set; }

        public bool CanDeleteSelection
        {
            get
            {
                return _bookSelection.CurrentSelection != null
                    && _collectionSettings.AllowDeleteBooks
                    && _bookSelection.CurrentSelection.CanDelete;
            }
        }

        internal CollectionSettings CollectionSettings
        {
            get { return _collectionSettings; }
        }

        public string LanguageName
        {
            get { return _collectionSettings.Language1.Name; } // collection tab still uses collection language settings
        }

        private object _bookCollectionLock = new object(); // Locks creation of _bookCollections

        // List out all the collections we have loaded
        // 0) the editable collection of this ".bloomCollection" folder.
        // 1) "Templates"
        // etc.
        public IReadOnlyList<BookCollection> GetBookCollections(bool disposing = false)
        {
            lock (_bookCollectionLock)
            {
                if (_bookCollections == null)
                {
                    if (disposing)
                    {
                        // We don't want to create new collections when we're disposing of the model.
                        return new List<BookCollection>();
                    }
                    _bookCollections = new List<BookCollection>(GetBookCollectionsOnce());

                    //we want the templates to be second (after the editable collection) regardless of alphabetical sorting
                    var templates = _bookCollections.FirstOrDefault(c => c.Name == "Templates");
                    if (templates != null)
                    {
                        _bookCollections.Remove(templates);
                        _bookCollections.Insert(1, templates);
                    }
                }
                return _bookCollections;
            }
        }

        public BookInfo BookInfoFromCollectionAndId(string collectionPath, string bookId)
        {
            var collection = GetBookCollections()
                .FirstOrDefault(c => c.PathToDirectory == collectionPath);
            if (collection == null)
                return null;
            return collection.GetBookInfos().FirstOrDefault(bi => bi.Id == bookId);
        }

        public void ReloadCollections()
        {
            lock (_bookCollectionLock)
            {
                _bookCollections = null;
                GetBookCollections();
            }

            _webSocketServer.SendEvent(
                "editableCollectionList",
                "reload:" + _bookCollections[0].PathToDirectory
            );
        }

        public void DuplicateBook(Book.Book book)
        {
            var newBookDir = book.Storage.Duplicate();

            // Get rid of any TC status we copied from the original, so Bloom treats it correctly as a new book.
            BookStorage.RemoveLocalOnlyFiles(newBookDir);

            ReloadEditableCollection();

            var dupInfo = TheOneEditableCollection
                .GetBookInfos()
                .FirstOrDefault(info => info.FolderPath == newBookDir);
            if (dupInfo != null)
            {
                var newBook = GetBookFromBookInfo(dupInfo);
                SelectBook(newBook);
                BookHistory.AddEvent(
                    newBook,
                    BookHistoryEventType.Created,
                    $"Duplicated from existing book \"{book.Title}\""
                );
                newBook.UserPrefs.UploadAgreementsAccepted = false;
            }
        }

        public void moveBookIntoThisCollection(Book.Book origBook, BookCollection origCollection)
        {
            var possibleTCFilePath = TeamCollectionManager.GetTcLinkPathFromLcPath(
                origCollection.PathToDirectory
            );
            if (RobustFile.Exists(possibleTCFilePath))
            {
                // Original collection is a TC.
                // To remove a book from a TC, we would have "load up" the collection to check that the book is checked
                // out, the tc is connected, etc. So for now we don't allow it
                throw new ApplicationException(
                    $"{origCollection.Name} is a Team Collection. Cannot move a book that is currently in a Team Collection."
                );
            }

            var origBookFolderPath = origBook.FolderPath;
            var bookFolderName = Path.GetFileName(origBookFolderPath);
            var newCollectionDir = TheOneEditableCollection.PathToDirectory;
            var newBookFolderPath = Path.Combine(newCollectionDir, bookFolderName);
            SIL.IO.RobustIO.MoveDirectory(origBookFolderPath, newBookFolderPath);
            origCollection.HandleBookDeletedFromCollection(origBookFolderPath);
            ReloadEditableCollection();
            var movedBookInfo = TheOneEditableCollection
                .GetBookInfos()
                .FirstOrDefault(info => info.FolderPath == newBookFolderPath);
            var movedBook = GetBookFromBookInfo(movedBookInfo);
            SelectBook(movedBook);
            BookHistory.AddEvent(
                movedBook,
                BookHistoryEventType.Moved,
                $"Moved book from collection \"{origCollection.Name}\" to collection \"{TheOneEditableCollection.Name}\""
            );
        }

        /// <summary>
        /// Eventually this might entirely replace ReloadCollections, since we probably never need to reload anything ut the first.
        /// For now it actually reloads them all, but at least allows clients that definitely only need the first reloaded to do so.
        /// </summary>
        /// <param name="collection"></param>
        public void ReloadEditableCollection()
        {
            // I hope we can get rid of this when we retire the old LibraryListView, but for now we need to keep both views up to date.
            // optimize: we only need to reload the first (editable) collection; better yet, we only need to add the one new book to it.
            ReloadCollections();
        }

        public void UpdateLabelOfBookInEditableCollection(Book.Book book)
        {
            // We can find a more efficient way to do this if necessary.
            //ReloadEditableCollection();
            // This allows it to get resorted. Is that good? It may move dramatically, even disappear from the screen.
            TheOneEditableCollection.UpdateBookInfo(book.BookInfo);
            // This actually changes the label. (One would think that re-rendering the collection would do this,
            // but somehow the way we are caching the book title as state in anticipation of the following
            // message was preventing this. It might be redundant now.)
            BookCommandsApi.UpdateButtonTitle(
                _webSocketServer,
                book.BookInfo,
                book.NameBestForUserDisplay
            );

            // happens as a side effect
            //_webSocketServer.SendEvent("editableCollectionList", "reload:" + _bookCollections[0].PathToDirectory);
        }

        /// <summary>
        /// Titles of all the books in the vernacular collection.
        /// </summary>
        internal IEnumerable<string> BookTitles
        {
            get { return TheOneEditableCollection.GetBookInfos().Select(book => book.Title); }
        }

        public BookCollection TheOneEditableCollection
        {
            get
            {
                return GetBookCollections()
                    .First(c => c.Type == BookCollection.CollectionType.TheOneEditableCollection);
            }
        }

        public string VernacularCollectionNamePhrase
        {
            get { return _collectionSettings.VernacularCollectionNamePhrase; }
        }

        public bool ShowSourceCollections
        {
            get { return _collectionSettings.AllowNewBooks; }
        }

        private void SetupChangeNotifications(BookCollection collection)
        {
            collection.CollectionChanged += (sender, args) =>
            {
                _webSocketServer.SendEvent(
                    "editableCollectionList",
                    "reload:" + collection.PathToDirectory
                );
            };

            _localizationChangedEvent?.Subscribe(unused =>
            {
                if (collection.IsFactoryInstalled)
                {
                    _webSocketServer.SendEvent(
                        "editableCollectionList",
                        "reload:" + collection.PathToDirectory
                    );
                }
                else
                {
                    // This is tricky. Reloading the collection won't do it, because nothing has changed that would cause
                    // the buttons to re-render. But some of them may be showing a string like "Missing title" that
                    // is localizable. This is not very efficient, as we may process updates for many books that
                    // don't need it or don't even have buttons due to laziness. But changing UI language is really rare.
                    foreach (var info in collection.GetBookInfos())
                        BookCommands.RequestButtonLabelUpdate(collection.PathToDirectory, info.Id);
                }
            });
        }

        /// <summary>
        /// This may be called on any thread. Please leave things where the only call is in GetBookCollections,
        /// having claimed the appropriate lock.
        /// </summary>
        /// <returns></returns>
        private IEnumerable<BookCollection> GetBookCollectionsOnce()
        {
            BookCollection editableCollection;
            using (PerformanceMeasurement.Global?.Measure("Creating Primary Collection"))
            {
                editableCollection = _bookCollectionFactory(
                    _pathToCollection,
                    BookCollection.CollectionType.TheOneEditableCollection
                );
                if (_bookCollectionHolder != null)
                    _bookCollectionHolder.TheOneEditableCollection = editableCollection;
                SetupChangeNotifications(editableCollection);
            }

            _currentEditableCollectionSelection.SelectCollection(editableCollection);
            yield return editableCollection;
            // If we're locked to one downloaded book, we don't need to show the source collections, or even to load them.
            if (!_collectionSettings.EditingABlorgBook)
            {
                foreach (var bookCollection in _sourceCollectionsList.GetSourceCollectionsFolders())
                {
                    var collection = _bookCollectionFactory(
                        bookCollection,
                        BookCollection.CollectionType.SourceCollection
                    );
                    // Apart from the editable collection, I think only the downloaded books needs this (because books
                    // can be deleted from it and possibly added by new downloads); but it seems safest to set up for all.
                    SetupChangeNotifications(collection);
                    yield return collection;
                }
            }
        }

        private Form MainShell =>
            Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Shell);

        // Not entirely happy that this method which launches a dialog is in the Model.
        // but with the Collection UI moving to JS I don't see a good alternative
        public void MakeBloomPack(bool forReaderTools)
        {
            var initialPath = FilePathMemory.GetOutputFilePath(
                TheOneEditableCollection,
                ".BloomPack"
            );
            var destFileName = MiscUtils.GetOutputFilePathOutsideCollectionFolder(
                initialPath,
                "BloomPack files|*.BloomPack"
            );
            if (!string.IsNullOrEmpty(destFileName))
            {
                FilePathMemory.RememberOutputFilePath(
                    TheOneEditableCollection,
                    ".BloomPack",
                    destFileName
                );
                MakeBloomPack(destFileName, forReaderTools);
            }
        }

        public void RescueMissingImages()
        {
            var initialPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var selectedPath = BloomFolderChooser.ChooseFolder(
                initialPath,
                "Select the folder where replacement images can be found"
            );
            if (!string.IsNullOrEmpty(selectedPath))
                AttemptMissingImageReplacements(selectedPath);
        }

        public void MakeReaderTemplateBloompack()
        {
            using (var dlg = new MakeReaderTemplateBloomPackDlg())
            {
                dlg.SetLanguage(LanguageName);
                dlg.SetTitles(BookTitles);
                var mainWindow = MainShell;
                if (dlg.ShowDialog(mainWindow) != DialogResult.OK)
                    return;
                MakeBloomPack(forReaderTools: true);
            }
        }

        public void SelectBook(Book.Book book)
        {
            _bookSelection.SelectBook(book);
        }

        public Book.Book GetSelectedBookOrNull()
        {
            return _bookSelection.CurrentSelection;
        }

        public bool DeleteBook(Book.Book book, BookCollection collection = null)
        {
            if (collection == null)
                collection = TheOneEditableCollection;
            Debug.Assert(book.FolderPath == _bookSelection.CurrentSelection?.FolderPath);

            if (
                _bookSelection.CurrentSelection != null && _bookSelection.CurrentSelection.CanDelete
            )
            {
                if (IsCurrentBookInCollection())
                {
                    if (!_bookSelection.CurrentSelection.IsSaveable)
                    {
                        var msg = LocalizationManager.GetString(
                            "TeamCollection.CheckOutForDelete",
                            "Please check out the book before deleting it."
                        );
                        BloomMessageBox.ShowInfo(msg);
                        return false;
                    }
                    if (_tcManager.CannotDeleteBecauseDisconnected(_bookSelection.CurrentSelection))
                    {
                        var msg = LocalizationManager.GetString(
                            "TeamCollection.ConnectForDelete",
                            "Please connect to the Team Collection before deleting books that are part of it."
                        );
                        BloomMessageBox.ShowInfo(msg);
                        return false;
                    }
                }
                var bookName = _bookSelection.CurrentSelection.NameBestForUserDisplay;
                var bookId = _bookSelection.CurrentSelection.ID;
                var confirmRecycleDescription = L10NSharp.LocalizationManager.GetString(
                    "CollectionTab.ConfirmRecycleDescription",
                    "The book '{0}'"
                );
                if (
                    ConfirmRecycleDialog.JustConfirm(
                        string.Format(confirmRecycleDescription, bookName),
                        false,
                        "Palaso"
                    )
                )
                {
                    // The sequence of these is a bit arbitrary. We'd like to delete the book in both places.
                    // Either could conceivably fail. If something goes wrong with removing the selection
                    // from it (very unlikely), we may as well leave nothing changed. If we delete it from
                    // the local collection but fail to delete it from the repo, it will come back at the
                    // next startup. If we delete it from the repo but fail to delete it locally,
                    // it will just stick around, and at least the desired team collection result has
                    // been achieved and the local result won't be a surprise later. So it seems marginally
                    // better to do them in this order.
                    _bookSelection.SelectBook(null);
                    if (collection == TheOneEditableCollection)
                        _tcManager.CurrentCollection?.DeleteBookFromRepo(book.FolderPath);
                    collection.DeleteBook(book.BookInfo);
                    // We only want history in the main collection. In particular, it causes problems
                    // with our change watcher looking for new books in the downloads collection if
                    // we mess with history there.
                    if (collection.Type == BookCollection.CollectionType.TheOneEditableCollection)
                        CollectionHistory.AddBookEvent(
                            collection.PathToDirectory,
                            bookName,
                            bookId,
                            BookHistoryEventType.Deleted
                        );
                    return true;
                }
            }
            return false;
        }

        private bool IsCurrentBookInCollection()
        {
            var currentFolder = Path.GetDirectoryName(_bookSelection.CurrentSelection.FolderPath);
            return (currentFolder == _collectionSettings.FolderPath);
        }

        public void DoubleClickedBook()
        {
            // If we need the book to be checked out for editing, make sure it is. Do not allow double click
            // to check it out.
            if (_bookSelection.CurrentSelection?.IsSaveable ?? false)
            {
                _editBookCommand.Raise(_bookSelection.CurrentSelection);
            }
        }

        public void OpenFolderOnDisk()
        {
            try
            {
                ProcessExtra.ShowFileInExplorerInFront(_bookSelection.CurrentSelection.FolderPath);
            }
            catch (System.Runtime.InteropServices.COMException e)
            {
                SIL.Reporting.ErrorReport.NotifyUserOfProblem(
                    e,
                    "Bloom had a problem asking your operating system to show that folder. Sorry!"
                );
            }
        }

        public void BringBookUpToDate()
        {
            var b = _bookSelection.CurrentSelection;
            _bookSelection.SelectBook(null);

            using (var dlg = new ProgressDialogForeground()) //REVIEW: this foreground dialog has known problems in other contexts... it was used here because of its ability to handle exceptions well. TODO: make the background one handle exceptions well
            {
                // Since the user explicitly told us to do this again, we will, even if we think
                // it's already been done.
                dlg.ShowAndDoWork(progress => b.BringBookUpToDate(progress));
            }

            _bookSelection.SelectBook(b);
        }

        /// <summary>
        /// All we do at this point is make a file with a ".doc" extension and open it.
        /// </summary>
        /// <remarks>
        /// The .doc extension allows the operating system to recognize which program
        /// should open the file, and the program (whether Microsoft Word or LibreOffice
        /// or OpenOffice) seems to handle HTML content just fine.
        /// </remarks>
        public void ExportDocFormat(string destDocPath)
        {
            var outputFolder = Path.GetDirectoryName(destDocPath);
            if (Directory.Exists(outputFolder))
            {
                // Clean out everything from the destination folder unless it appears to be a bloom
                // book folder (which shouldn't happen).
                var appearsToBeBloomBookFolder = Directory
                    .EnumerateFiles(outputFolder, "*.htm")
                    .Any();
                if (appearsToBeBloomBookFolder)
                {
                    var msgTemplate = LocalizationManager.GetString(
                        "Spreadsheet.OverwriteBook",
                        "The folder named {0} already exists and looks like it might be a Bloom book folder!"
                    );
                    var msg = string.Format(msgTemplate, outputFolder);
                    var messageBoxButtons = new[]
                    {
                        new MessageBoxButton()
                        {
                            Text = "Cancel",
                            Id = "cancel",
                            Default = true
                        }
                    };
                    var formToInvokeOn = Application.OpenForms
                        .Cast<Form>()
                        .FirstOrDefault(f => f is Shell);
                    formToInvokeOn.Invoke(
                        (Action)(
                            () =>
                            {
                                BloomMessageBox.Show(
                                    formToInvokeOn,
                                    msg,
                                    messageBoxButtons,
                                    MessageBoxIcon.Warning
                                );
                            }
                        )
                    );
                    return;
                }
                if (Directory.EnumerateFileSystemEntries(outputFolder).Any())
                {
                    var msgTemplate = LocalizationManager.GetString(
                        "Spreadsheet.Overwrite",
                        "You are about to replace the existing folder named {0}"
                    );
                    var msg = string.Format(msgTemplate, outputFolder);
                    var messageBoxButtons = new[]
                    {
                        new MessageBoxButton() { Text = "Overwrite", Id = "overwrite" },
                        new MessageBoxButton()
                        {
                            Text = "Cancel",
                            Id = "cancel",
                            Default = true
                        }
                    };
                    string result = null;
                    var formToInvokeOn = Application.OpenForms
                        .Cast<Form>()
                        .FirstOrDefault(f => f is Shell);
                    formToInvokeOn.Invoke(
                        (Action)(
                            () =>
                            {
                                result = BloomMessageBox.Show(
                                    formToInvokeOn,
                                    msg,
                                    messageBoxButtons,
                                    MessageBoxIcon.Warning
                                );
                            }
                        )
                    );
                    if (result != "overwrite")
                        return;
                }
                // In case there's a previous export, get rid of it.
                SIL.IO.RobustIO.DeleteDirectoryAndContents(outputFolder);
            }
            Directory.CreateDirectory(outputFolder);

            var sourcePath = _bookSelection.CurrentSelection.GetPathHtmlFile();
            // Linux (Trusty) LibreOffice requires slightly different metadata at the beginning
            // of the file in order to recognize it as HTML.  Otherwise it opens the file as raw
            // HTML (See https://silbloom.myjetbrains.com/youtrack/issue/BL-2276 if you don't
            // believe me.)  I don't know any perfect way to add this information to the file,
            // but a simple string replace should be safe.  This change works okay for both
            // Windows and Linux and for all three programs (Word, OpenOffice and Libre Office).
            var content = RobustFile.ReadAllText(sourcePath);
            var fixedContent = content.Replace(
                "<meta charset=\"UTF-8\">",
                "<meta http-equiv=\"content-type\" content=\"text/html; charset=utf-8\">"
            );
            var xmlDoc = RepairWordVisibility(fixedContent);
            XmlHtmlConverter.SaveDOMAsHtml5(xmlDoc, destDocPath); // writes file and returns path
            // We need to copy the CSS and image files from the book's folder to the destination folder.
            // We don't need other files from there for this export. (audio, video, etc)
            var sourceFolder = Path.GetDirectoryName(sourcePath);
            foreach (var sourceFilePath in Directory.EnumerateFiles(sourceFolder, "*.*"))
            {
                var filename = Path.GetFileName(sourceFilePath);
                var extension = Path.GetExtension(filename).ToLowerInvariant();
                if (
                    BookCompressor.CompressableImageFileExtensions.Contains(extension)
                    || extension == ".svg"
                    || extension == ".css"
                )
                {
                    var destFilePath = Path.Combine(outputFolder, filename);
                    RobustFile.Copy(sourceFilePath, destFilePath, true);
                }
            }
        }

        // BL-5998 Apparently Word doesn't read our CSS rules for bloom-visibility correctly.
        // So we're forced to control visibility more directly with inline styles.
        private static SafeXmlDocument RepairWordVisibility(string content)
        {
            var xmlDoc = XmlHtmlConverter.GetXmlDomFromHtml(content);
            var dom = new HtmlDom(xmlDoc);
            var bloomEditableDivs = dom.RawDom.SafeSelectNodes(
                "//div[contains(@class, 'bloom-editable')]"
            );
            foreach (SafeXmlElement editableDiv in bloomEditableDivs)
            {
                HtmlDom.SetInlineStyle(
                    editableDiv,
                    editableDiv.HasClass("bloom-visibility-code-on")
                        ? "display: block;"
                        : "display: none;"
                );
            }

            return dom.RawDom;
        }

        public void UpdateThumbnailAsync(
            Book.Book book,
            HtmlThumbNailer.ThumbnailOptions thumbnailOptions,
            Action<Book.BookInfo, Image> callback,
            Action<Book.BookInfo, Exception> errorCallback
        )
        {
            if (!(book is ErrorBook))
            {
                _thumbNailer.RebuildThumbNailAsync(book, thumbnailOptions, callback, errorCallback);
            }
        }

        public void UpdateThumbnailAsync(Book.Book book)
        {
            UpdateThumbnailAsync(
                book,
                new HtmlThumbNailer.ThumbnailOptions(),
                RefreshOneThumbnail,
                HandleThumbnailerErrror
            );
        }

        private void RefreshOneThumbnail(Book.BookInfo bookInfo, Image image)
        {
            // The arguments here are not currently used (method signature is legacy),
            // but may be useful if we optimize.
            // optimize: I think this will reload all of them
            _webSocketServer.SendString("bookImage", "reload", bookInfo.Id);
        }

        private void HandleThumbnailerErrror(Book.BookInfo bookInfo, Exception error)
        {
            string path = Path.Combine(bookInfo.FolderPath, "thumbnail.png");
            try
            {
                Resources.Error70x70.Save(@path, ImageFormat.Png);
            }
            catch (Exception e)
            {
                Logger.WriteError("Could not save error icon for book", e);
            }

            RefreshOneThumbnail(bookInfo, Resources.Error70x70);
        }

        internal (string dirName, string dirPrefix) GetDirNameAndPrefixForCollectionBloomPack()
        {
            var dir = TheOneEditableCollection.PathToDirectory;
            return (dir, "");
        }

        public void MakeBloomPack(string outputPath, bool forReaderTools = false)
        {
            var (dirName, dirPrefix) = GetDirNameAndPrefixForCollectionBloomPack();
            var rootName = Path.GetFileName(dirName);
            if (rootName == null)
                return;
            Logger.WriteEvent($"Making BloomPack at {outputPath} forReaderTools={forReaderTools}");
            MakeBloomPackWithUI(outputPath, dirName, dirPrefix, forReaderTools, isCollection: true);
        }

        internal (string dirName, string dirPrefix) GetDirNameAndPrefixForSingleBookBloomPack(
            string inputBookFolder
        )
        {
            var rootName = Path.GetFileName(inputBookFolder);
            if (rootName != null)
                rootName += Path.DirectorySeparatorChar;

            return (inputBookFolder, rootName);
        }

        public void MakeSingleBookBloomPack(string outputPath, string inputBookFolder)
        {
            var (dirName, dirPrefix) = GetDirNameAndPrefixForSingleBookBloomPack(inputBookFolder);
            if (dirPrefix == null)
                return;
            Logger.WriteEvent(
                $"Making single book BloomPack at {outputPath} bookFolderPath={inputBookFolder}"
            );
            MakeBloomPackWithUI(outputPath, dirName, dirPrefix, false, isCollection: false);
        }

        private void MakeBloomPackWithUI(
            string outputPath,
            string sourceDirectory,
            string dirNamePrefix,
            bool forReaderTools,
            bool isCollection
        )
        {
            try
            {
                if (RobustFile.Exists(outputPath))
                {
                    // UI already got permission for this
                    RobustFile.Delete(outputPath);
                }
                using (var pleaseWait = new SimpleMessageDialog("Creating BloomPack...", "Bloom"))
                {
                    try
                    {
                        pleaseWait.Show();
                        pleaseWait.BringToFront();
                        Application.DoEvents(); // actually show it
                        Cursor.Current = Cursors.WaitCursor;

                        Logger.WriteEvent(
                            "BloomPack outputPath will be "
                                + outputPath
                                + ", made from "
                                + sourceDirectory
                                + " with rootName "
                                + Path.GetFileName(sourceDirectory)
                        );
                        MakeBloomPackInternal(
                            outputPath,
                            sourceDirectory,
                            dirNamePrefix,
                            forReaderTools,
                            isCollection
                        );

                        // show it
                        Logger.WriteEvent("Showing BloomPack on disk");
                        ProcessExtra.ShowFileInExplorerInFront(outputPath);
                        Analytics.Track("Create BloomPack");
                    }
                    finally
                    {
                        Cursor.Current = Cursors.Default;
                        pleaseWait.Close();
                    }
                }
            }
            catch (Exception e)
            {
                ErrorReport.NotifyUserOfProblem(e, "Could not make the BloomPack at " + outputPath);
            }
        }

        /// <summary>
        /// Makes a BloomPack of the specified sourceDirectory.
        /// </summary>
        /// <param name="outputPath">The outputPath to write to. Precondition: Must not exist.</param>
        internal void MakeBloomPackInternal(
            string outputPath,
            string sourceDirectory,
            string dirNamePrefix,
            bool forReaderTools,
            bool isCollection
        )
        {
            if (isCollection)
            {
                BookCompressor.CompressCollectionDirectory(
                    outputPath,
                    sourceDirectory,
                    dirNamePrefix,
                    forReaderTools
                );
            }
            else
            {
                BookCompressor.CompressBookDirectory(
                    outputPath,
                    sourceDirectory,
                    MakeBloomPackBookFileFilter(sourceDirectory),
                    dirNamePrefix,
                    forReaderTools
                );
            }
        }

        public static BookFileFilter MakeBloomPackBookFileFilter(string bookFolderPath)
        {
            var filter = new BookFileFilter(bookFolderPath)
            {
                IncludeFilesForContinuedEditing = true,
                // want audio in bloompack: see https://issues.bloomlibrary.org/youtrack/issue/BL-11741.
                NarrationLanguages = null, // all audio
                WantMusic = true,
                WantVideo = true
            };
            // these are artifacts of uploading book to BloomLibrary.org and not useful in BloomPubs
            filter.AlwaysReject(new Regex("^thumbnail-"));
            return filter;
        }

        public string GetSuggestedBloomPackPath()
        {
            return TheOneEditableCollection.Name + ".BloomPack";
        }

        public void DoUpdatesOfAllBooks()
        {
            using (var dlg = new ProgressDialogBackground())
            {
                dlg.ShowAndDoWork((progress, args) => DoUpdatesOfAllBooks(progress));
            }
        }

        public void DoUpdatesOfAllBooks(IProgress progress)
        {
            int i = 0;
            foreach (var bookInfo in TheOneEditableCollection.GetBookInfos())
            {
                i++;
                var book = _bookServer.GetBookFromBookInfo(bookInfo);
                //gets overwritten: progress.WriteStatus(book.NameBestForUserDisplay);
                progress.WriteMessage(
                    "Processing "
                        + book.NameBestForUserDisplay
                        + " "
                        + i
                        + "/"
                        + TheOneEditableCollection.GetBookInfos().Count()
                );
                // Since the user told us to do it, we'll do it even to books that we think are already
                // up to date. (EnsureUpToDate would do so anyway, since these are newly created Book objects, even if they are
                // for books we already have in memory.) But not to ones where saving is disabled (in a TC).
                if (book.IsSaveable)
                    book.BringBookUpToDate(progress);
            }
        }

        public void DoChecksOfAllBooks()
        {
            using (var dlg = new ProgressDialogBackground())
            {
                dlg.ShowAndDoWork((progress, args) => DoChecksOfAllBooksBackgroundWork(dlg, null));
                if (dlg.Progress.ErrorEncountered || dlg.Progress.WarningsEncountered)
                {
                    MessageBox.Show("Bloom will now open a list of problems it found.");
                    var path = Path.GetTempFileName() + ".txt";
                    RobustFile.WriteAllText(path, dlg.ProgressString.Text);
                    ProcessExtra.SafeStartInFront(path);
                }
                else
                {
                    MessageBox.Show("Bloom didn't find any problems.");
                }
            }
        }

        public void AttemptMissingImageReplacements(string pathToFolderOfReplacementImages = null)
        {
            using (var dlg = new ProgressDialogBackground())
            {
                dlg.ShowAndDoWork(
                    (progress, args) =>
                        DoChecksOfAllBooksBackgroundWork(dlg, pathToFolderOfReplacementImages)
                );
                if (dlg.Progress.ErrorEncountered || dlg.Progress.WarningsEncountered)
                {
                    MessageBox.Show(
                        "There were some problems. Bloom will now open a log of the attempt to replace missing images."
                    );
                }
                else
                {
                    MessageBox.Show(
                        "There are no more missing images. Bloom will now open a log of what it did."
                    );
                }

                var path = Path.GetTempFileName() + ".txt";
                RobustFile.WriteAllText(path, dlg.ProgressString.Text);
                try
                {
                    ProcessExtra.SafeStartInFront(path);
                }
                catch (System.OutOfMemoryException)
                {
                    // This has happened at least once.  See https://silbloom.myjetbrains.com/youtrack/issue/BL-3431.
                    MessageBox.Show(
                        "Bloom ran out of memory trying to open the log.  You should quit and restart the program.  (Your books should all be okay.)"
                    );
                }
            }
        }

        public void DoChecksOfAllBooksBackgroundWork(
            ProgressDialogBackground dialog,
            string pathToFolderOfReplacementImages
        )
        {
            var bookInfos = TheOneEditableCollection.GetBookInfos();
            var count = bookInfos.Count();
            if (count == 0)
                return;

            foreach (var bookInfo in bookInfos)
            {
                //not allowed in this thread: dialog.ProgressBar.Value++;
                dialog.Progress.ProgressIndicator.PercentCompleted += 100 / count;

                var book = _bookServer.GetBookFromBookInfo(bookInfo);

                dialog.Progress.WriteMessage("Checking " + book.NameBestForUserDisplay);
                book.CheckBook(dialog.Progress, pathToFolderOfReplacementImages);
                dialog.ProgressString.WriteMessage("");
            }
            dialog.Progress.ProgressIndicator.PercentCompleted = 100;
        }

        private void CreateFromSourceBook(Book.Book sourceBook)
        {
            try
            {
                var newBook = _bookServer.CreateFromSourceBook(
                    sourceBook,
                    TheOneEditableCollection.PathToDirectory
                );
                if (newBook == null)
                    return; //This can happen if there is a configuration dialog and the user clicks Cancel

                // We want it to have the name it's eventually going to BEFORE we add it to the collection.
                // Otherwise, there are race conditions between the code that is selecting it (and as a side
                // effect, bringing it up to date, which may rename it and its folder, if it already has a
                // title in the relevant language) and the code that wants to display a thumbnail of the new
                // item in the list.
                // (We can't fix this by reordering things, because there will also be problems if we attempt
                // to select an item before it is present in the collection to select.)
                // We know this is a new book object, so there's no point in using EnsureUpToDate.
                // When we later select it, that code uses EnsureUpToDate and will not do it again.
                newBook.BringBookUpToDate(new NullProgress(), false);

                TheOneEditableCollection.AddBookInfo(newBook.BookInfo);

                if (_bookSelection != null)
                {
                    Book.Book.SourceToReportForNextRename = Path.GetFileName(sourceBook.FolderPath);
                    _bookSelection.SelectBook(newBook, aboutToEdit: true);
                }
                //enhance: would be nice to know if this is a new shell
                if (!sourceBook.IsInEditableCollection)
                {
                    Analytics.Track(
                        "Create Book",
                        new Dictionary<string, string>()
                        {
                            { "Category", sourceBook.CategoryForUsageReporting },
                            { "BookId", newBook.ID },
                            { "Country", _collectionSettings.Country }
                        }
                    );
                }
                // Better reported in Book_BookTitleChanged as a side effect of selecting it.
                // BookHistory.AddEvent(newBook, BookHistoryEventType.Created, "New book created");
                _editBookCommand.Raise(newBook);
            }
            catch (Exception e)
            {
                SIL.Reporting.ErrorReport.NotifyUserOfProblem(
                    e,
                    "Bloom ran into an error while creating that book. (Sorry!)"
                );
            }
        }

        public BookInfo GetBookInfoByFolderPath(string path)
        {
            var collectionPath = Path.GetDirectoryName(path);
            // This might need adjustment if we ever get Linux/Mac versions working. But I think not. See the
            // comment in BookCollection.GetBookInfoByFolderPath.
            var collection = GetBookCollections()
                .FirstOrDefault(
                    c => c.PathToDirectory.ToLowerInvariant() == collectionPath.ToLowerInvariant()
                );
            if (collection == null)
                return null;
            return collection.GetBookInfoByFolderPath(path);
        }

        public Book.Book GetBookFromBookInfo(BookInfo bookInfo)
        {
            // If we're looking for the current book it's important to return the actual book object,
            // because it could end up modified in ways that make our one out-of-date if we modify another
            // instance based on the same folder. For example, Rename could make our FolderPath wrong.
            if (bookInfo.FolderPath == _bookSelection.CurrentSelection?.FolderPath)
                return _bookSelection.CurrentSelection;
            return _bookServer.GetBookFromBookInfo(bookInfo);
        }

        /// <summary>
        /// Zip up the book folder, excluding .pdf, .bloombookorder, .map, .bloompack, .db files.
        /// The resulting file should have a .bloomSource extension, but this depends on properly
        /// setting the value of destFileName before calling this method.  TeamCollection still
        /// uses the .bloom extension until we can figure out how to safely migrate teams as a
        /// whole to using .bloomSource.
        /// </summary>
        /// <param name="exception">any exception which occurs when trying to save the file</param>
        /// <returns>true if file was saved successfully; false otherwise</returns>
        /// <remarks>if return value is false, exception is non-null and vice versa</remarks>
        public static bool SaveAsBloomSourceFile(
            string srcFolderName,
            string destFileName,
            out Exception exception,
            string[] extraFilesToInclude = null
        )
        {
            exception = null;
            try
            {
                // Note, .bloombookorder files are no longer created; they are obsolete.
                var excludedExtensions = new[]
                {
                    ".pdf",
                    ".bloombookorder",
                    ".map",
                    ".bloompack",
                    ".db"
                };

                Logger.WriteEvent("Zipping up {0} ...", destFileName);
                var zipFile = new BloomZipFile(destFileName);
                zipFile.AddDirectoryContents(srcFolderName, excludedExtensions);
                foreach (var path in extraFilesToInclude ?? new string[0])
                {
                    zipFile.AddTopLevelFile(path);
                }

                Logger.WriteEvent("Saving {0} ...", destFileName);
                zipFile.Save();

                if (destFileName.EndsWith(".bloom"))
                    Logger.WriteEvent("Finished writing .bloom file.");
                else
                    Logger.WriteEvent("Finished writing .bloomSource file.");
            }
            catch (Exception e)
            {
                exception = e;
                return false;
            }
            return true;
        }
    }
}
