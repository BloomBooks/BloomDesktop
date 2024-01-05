using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.CollectionCreating;
using Bloom.CollectionTab;
using Bloom.MiscUI;
using Bloom.Properties;
using Bloom.ToPalaso;
using Bloom.Utils;
using Bloom.WebLibraryIntegration;
using Bloom.Workspace;
using L10NSharp;
using Newtonsoft.Json;
using SIL.IO;
using SIL.Linq;

namespace Bloom.web.controllers
{
    public class CollectionApi
    {
        private readonly CollectionSettings _settings;
        private readonly CollectionModel _collectionModel;
        public const string kApiUrlPart = "collections/";
        private readonly BookSelection _bookSelection;
        private BookThumbNailer _thumbNailer;
        private BloomWebSocketServer _webSocketServer;
        private readonly EditBookCommand _editBookCommand;
        private Timer _clickTimer = new Timer();

        private int _thumbnailEventsToWaitFor = -1;

        private object _thumbnailEventsLock = new object();

        // public so that WorkspaceView can set it in constructor.
        // We'd prefer to just let the WorkspaceView be a constructor arg passed to this by Autofac,
        // but that throws an exception, probably there is some circularity.
        public WorkspaceView WorkspaceView;

        public CollectionApi(
            CollectionSettings settings,
            CollectionModel collectionModel,
            BookSelection bookSelection,
            EditBookCommand editBookCommand,
            BookThumbNailer thumbNailer,
            BloomWebSocketServer webSocketServer
        )
        {
            _settings = settings;
            _collectionModel = collectionModel;
            _bookSelection = bookSelection;
            _editBookCommand = editBookCommand;
            _thumbNailer = thumbNailer;
            _webSocketServer = webSocketServer;
            _clickTimer.Interval = SystemInformation.DoubleClickTime;
            _clickTimer.Tick += _clickTimer_Tick;
        }

        private void _clickTimer_Tick(object sender, EventArgs e)
        {
            _clickTimer.Stop();
            _webSocketServer.SendEvent("collections", "clickTimerElapsed");
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler(kApiUrlPart + "list", HandleListRequest, true);

            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "name",
                request =>
                {
                    // always null? request.ReplyWithText(_collection.Name);
                    request.ReplyWithText(_settings.CollectionName);
                },
                true
            );

            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "books",
                HandleBooksRequest,
                false,
                false
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "book/thumbnail",
                HandleThumbnailRequest,
                false,
                false
            );

            // used by visual regression tests to name screenshots
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "selected-book-info",
                request =>
                {
                    var book = _collectionModel.GetSelectedBookOrNull();
                    if (book == null)
                    {
                        request.Failed();
                        return;
                    }
                    var bookInfo = book.BookInfo;
                    var json = new
                    {
                        id = bookInfo.Id,
                        title = bookInfo.QuickTitleUserDisplay,
                        folderPath = bookInfo.FolderPath,
                        folderName = book.Storage.FolderName,
                    };
                    request.ReplyWithJson(json);
                },
                true
            );

            // Note: the get part of this doesn't need to run on the UI thread, or even requiresSync. If it gets called a lot, consider
            // using different patterns for get and set so we can not use the uI thread for get.
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "selected-book",
                request =>
                {
                    switch (request.HttpMethod)
                    {
                        case HttpMethods.Get:
                            request.ReplyWithText(
                                "" + _collectionModel.GetSelectedBookOrNull()?.ID
                            );
                            break;
                        case HttpMethods.Post:
                            // Various things done during this post block the UI thread in a way that can cause Javascript
                            // not to notice a double-click. But a second click that occurs before this timer fires will
                            // get processed before the timer-fired event. So Javascript is programmed to look for a click
                            // that happens after an initial click but before it is notified that this timer fired.
                            // Such clicks are treated as doubles.
                            // Note: we'll send this notification even if the API call was not made in response to a click.
                            // That is harmless; the only listener for this message does nothing but clear a 'waiting for
                            // double click' flag.
                            _clickTimer.Start();
                            // We're selecting the book, make sure everything is up to date.
                            // This first method does minimal processing to come up with the right collection and BookInfo object
                            // without actually loading all the files. We need the title for the Performance Measurement and later
                            // we'll use the BookInfo object to get the fully updated book.
                            BookInfo newBookInfo = null;
                            try
                            {
                                newBookInfo = GetBookInfoFromPost(request);
                                var titleString = newBookInfo.QuickTitleUserDisplay;
                                using (
                                    PerformanceMeasurement.Global?.Measure(
                                        "select book",
                                        titleString
                                    )
                                )
                                {
                                    // We could just put the PerformanceMeasurement in the CollectionModel.SelectBook() method,
                                    // but this GetUpdatedBookObjectFromBookInfo() actually does a non-trivial amount of work,
                                    // because it asks the CollectionModel to update the book files (including BringBookUpToDate).
                                    var book = GetUpdatedBookObjectFromBookInfo(newBookInfo);
                                    if (
                                        book.FolderPath
                                        != _bookSelection?.CurrentSelection?.FolderPath
                                    )
                                    {
                                        _collectionModel.SelectBook(book);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                if (newBookInfo?.FolderPath != null)
                                {
                                    var folderPath = newBookInfo.FolderPath;
                                    if (MiscUtils.ContainsSurrogatePairs(folderPath))
                                        folderPath = MiscUtils.QuoteUnicodeCodePointsInPath(
                                            folderPath
                                        );
                                    var msg = "Error selecting book: " + folderPath;
                                    var ex = new Exception(msg, e);
                                    // For some reason, BookInfo can't be serialized, so we'll add the pieces to the exception.
                                    ex.Data.Add("ErrorBookFolder", folderPath);
                                    if (
                                        MiscUtils.ContainsSurrogatePairs(
                                            newBookInfo.QuickTitleUserDisplay
                                        )
                                    )
                                        ex.Data.Add(
                                            "ErrorBookName",
                                            MiscUtils.QuoteUnicodeCodePointsInPath(
                                                newBookInfo.QuickTitleUserDisplay
                                            )
                                        );
                                    else
                                        ex.Data.Add(
                                            "ErrorBookName",
                                            newBookInfo.QuickTitleUserDisplay
                                        );
                                    throw ex;
                                }
                                throw e;
                            }

                            request.PostSucceeded();
                            break;
                    }
                },
                true
            );

            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "selectAndEditBook",
                request =>
                {
                    var book = GetBookObjectFromPost(request, true);
                    if (book.FolderPath != _bookSelection?.CurrentSelection?.FolderPath)
                    {
                        _collectionModel.SelectBook(book);
                    }
                    if (
                        book.IsSaveable
                        && GetCollectionOfRequest(request).Type
                            == BookCollection.CollectionType.TheOneEditableCollection
                    )
                    {
                        _editBookCommand.Raise(_bookSelection.CurrentSelection);
                    }

                    request.PostSucceeded();
                },
                true
            );

            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "duplicateBook/",
                (request) =>
                {
                    _collectionModel.DuplicateBook(GetBookObjectFromPost(request));
                    request.PostSucceeded();
                },
                true
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "deleteBook/",
                (request) =>
                {
                    var collection = GetCollectionOfRequest(request);
                    if (_collectionModel.DeleteBook(GetBookObjectFromPost(request), collection))
                        request.PostSucceeded();
                    else
                        request.Failed();
                },
                true
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "collectionProps/",
                HandleCollectionProps,
                false,
                false
            );

            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "makeShellBooksBloompack/",
                (request) =>
                {
                    _collectionModel.MakeBloomPack(forReaderTools: false);
                    request.PostSucceeded();
                },
                true
            );

            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "makeBloompack/",
                (request) =>
                {
                    _collectionModel.MakeReaderTemplateBloompack();
                    request.PostSucceeded();
                },
                true
            );

            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "doChecksOfAllBooks/",
                (request) =>
                {
                    _collectionModel.DoChecksOfAllBooks();
                    request.PostSucceeded();
                },
                true
            );

            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "rescueMissingImages/",
                (request) =>
                {
                    _collectionModel.RescueMissingImages();
                    request.PostSucceeded();
                },
                true
            );

            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "doUpdatesOfAllBooks/",
                (request) =>
                {
                    _collectionModel.DoUpdatesOfAllBooks();
                    request.PostSucceeded();
                },
                true
            );

            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "removeSourceCollection",
                HandleRemoveSourceCollection,
                false
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "addSourceCollection",
                HandleAddSourceCollection,
                true
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "removeSourceFolder",
                HandleRemoveSourceFolder,
                true
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "getBookOnBloomBadgeInfo",
                GetBookOnBloomBadgeInfo,
                false
            );
            // This one can take quite a long time and lock up the browser if no other thread can do anything. And it doesn't manipulate any
            // shared data. So we'll allow it to run on a background thread without locking.
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "getBookCountByLanguage",
                HandleGetBookCountByLanguage,
                true,
                false
            );
        }

        private void HandleRemoveSourceCollection(ApiRequest request)
        {
            var collectionFolderPath = request.RequiredPostString();
            var filename = Path.GetFileName(collectionFolderPath);
            var collectionsFolder = ProjectContext.GetInstalledCollectionsDirectory();
            var linkFile = Path.Combine(collectionsFolder, filename + ".lnk");
            if (RobustFile.Exists(linkFile))
            {
                RobustFile.Delete(linkFile);
                _collectionModel.ReloadCollections();
                request.PostSucceeded();
            }
            else
            {
                request.Failed();
            }
        }

        bool _updateAfterExplorerOpened;

        private void HandleRemoveSourceFolder(ApiRequest request)
        {
            var collectionFolderPath = request.RequiredPostString();
            if (Directory.Exists(collectionFolderPath))
            {
                request.PostSucceeded();
                _updateAfterExplorerOpened = true;
                ProcessExtra.SafeStartInFront(collectionFolderPath);
            }
            else
            {
                request.Failed();
                return;
            }
        }

        // Currently only used by Books on Blorg Progress Bar; if a Sign Language is defined, we use that.
        private void HandleGetBookCountByLanguage(ApiRequest request)
        {
            if (request.HttpMethod == HttpMethods.Post)
                return; // should be Get
            var langTag = string.IsNullOrEmpty(_settings.SignLanguageTag)
                ? _settings.Language1Tag
                : _settings.SignLanguageTag;
            var bloomLibraryApiClient = new BloomLibraryBookApiClient();
            int count;
            try
            {
                count = bloomLibraryApiClient.GetBookCountByLanguage(langTag);
            }
            catch
            {
                count = -1;
            }

            request.ReplyWithText(count.ToString());
        }

        internal void CheckForCollectionUpdates()
        {
            if (_updateAfterExplorerOpened)
            {
                // trigger a list request?.
                _collectionModel.ReloadCollections();
                dynamic result = new DynamicJson();
                result.success = true;
                result.list = GetCollectionList();
                _webSocketServer.SendBundle("collections", "updateCollectionList", result);
            }
        }

        internal void ResetUpdatingList()
        {
            _updateAfterExplorerOpened = false;
        }

        private void HandleAddSourceCollection(ApiRequest request)
        {
            if (!Directory.Exists(NewCollectionWizard.DefaultParentDirectoryForCollections))
            {
                Directory.CreateDirectory(NewCollectionWizard.DefaultParentDirectoryForCollections);
            }
            // We send the result through a websocket rather than simply returning it because
            // if the user is very slow (one site said FF times out after 90s) the browser may
            // abandon the request before it completes. The POST result is ignored and the
            // browser simply listens to the socket.
            request.PostSucceeded();
            var pathToCollectionFile = "";
            using (var dlg = new DialogAdapters.OpenFileDialogAdapter())
            {
                dlg.Title = LocalizationManager.GetString(
                    "CollectionTab.ChooseCollection",
                    "Choose Collection",
                    "This is the title of the file-open dialog that you use to choose a Bloom collection"
                );
                dlg.Filter =
                    LocalizationManager.GetString(
                        "OpenCreateNewCollectionsDialog.Bloom Collections",
                        "Bloom Collections",
                        "This shows in the file-open dialog that you use to open a different bloom collection"
                    ) + @"|*.bloomLibrary;*.bloomCollection";
                dlg.InitialDirectory = NewCollectionWizard.DefaultParentDirectoryForCollections;
                dlg.CheckFileExists = true;
                dlg.CheckPathExists = true;
                if (dlg.ShowDialog() == DialogResult.Cancel)
                    return;
                pathToCollectionFile = dlg.FileName;
            }
            var pathToCollectionDirectory = Path.GetDirectoryName(pathToCollectionFile);
            var collectionName = Path.GetFileNameWithoutExtension(pathToCollectionFile);
            var collectionsFolder = ProjectContext.GetInstalledCollectionsDirectory();
            // This overwrites any existing shortcut with the same collectionName.
            ShortcutMaker.CreateDirectoryShortcut(pathToCollectionDirectory, collectionsFolder);
            dynamic result = new DynamicJson();
            result.success = !String.IsNullOrEmpty(pathToCollectionFile);
            result.collection = new DynamicJson();
            result.collection.id = pathToCollectionDirectory;
            result.collection.name = collectionName;
            result.collection.shouldLocalizeName = false;
            result.collection.isLink = true;
            result.collection.isRemovableFolder = false;
            _collectionModel.ReloadCollections();
            _webSocketServer.SendBundle("collections", "addSourceCollection-results", result);
        }

        // needs to be thread-safe
        private void HandleCollectionProps(ApiRequest request)
        {
            var collection = GetCollectionOfRequest(request);
            if (collection == null)
                return; // request.Failed() has already been signaled.
            dynamic props = new ExpandoObject();
            props.isFactoryInstalled = collection.IsFactoryInstalled;
            props.containsDownloadedBooks = collection.ContainsDownloadedBooks;
            if (collection.PathToDirectory == _settings.FolderPath)
                props.languageFont = _settings.Language1.FontName;
            request.ReplyWithJson(JsonConvert.SerializeObject(props));
        }

        // List out all the collections we have loaded
        public void HandleListRequest(ApiRequest request)
        {
            dynamic output = GetCollectionList();
            request.ReplyWithJson(JsonConvert.SerializeObject(output));
        }

        private dynamic GetCollectionList()
        {
            dynamic output = new List<dynamic>();
            _collectionModel
                .GetBookCollections()
                .ForEach(c =>
                {
                    Debug.WriteLine($"collection: {c.Name}-->{c.PathToDirectory}");
                    // For this purpose there's no point in returning empty collections
                    // (except the editable one and books from bloom library, which might add items later),
                    // and in particular this filters out our xmatter folders which aren't really
                    // collections.
                    if (
                        c.Type == BookCollection.CollectionType.TheOneEditableCollection
                        || c.ContainsDownloadedBooks
                        || c.GetBookInfos().Any()
                    )
                    {
                        output.Add(
                            new
                            {
                                id = c.PathToDirectory,
                                name = c.Name,
                                shouldLocalizeName = c.PathToDirectory.StartsWith(
                                    BloomFileLocator.FactoryCollectionsDirectory
                                ) || c.ContainsDownloadedBooks,
                                isLink = c.Type
                                    != BookCollection.CollectionType.TheOneEditableCollection
                                    && IsFromLinkFile(c.PathToDirectory),
                                isRemovableFolder = c.Type
                                    != BookCollection.CollectionType.TheOneEditableCollection
                                    && !IsFromLinkFile(c.PathToDirectory)
                                    && !c.ContainsDownloadedBooks
                                    && !c.PathToDirectory.StartsWith(
                                        BloomFileLocator.FactoryCollectionsDirectory
                                    )
                            }
                        );
                    }
                });
            return output;
        }

        private bool IsFromLinkFile(string collectionFolderPath)
        {
            var collectionsFolder = ProjectContext.GetInstalledCollectionsDirectory();
            if (collectionFolderPath.StartsWith(collectionsFolder))
                return false;
            var linkFile = Path.Combine(
                collectionsFolder,
                Path.GetFileName(collectionFolderPath) + ".lnk"
            );
            return RobustFile.Exists(linkFile);
        }

        private string _currentCollectionPath;

        public void HandleBooksRequest(ApiRequest request)
        {
            var collection = GetCollectionOfRequest(request);
            if (collection == null)
            {
                return; // have already called request failed at this point
            }

            // Note: the winforms version used ImproveAndRefreshBookButtons(), which may load the whole book.

            var bookInfos = collection.GetBookInfos();
            // Load the initial values for the bloom library status of each book.
            if (collection.Type == BookCollection.CollectionType.TheOneEditableCollection)
            {
                var reload = request.Parameters["reload"] == "true";
                if (reload || collection.PathToDirectory != _currentCollectionPath)
                {
                    _currentCollectionPath = collection.PathToDirectory;
                    collection.UpdateBloomLibraryStatusOfBooks(
                        bookInfos.ToList(),
                        skipBadgeUpdate: true
                    );
                }
            }
            var jsonInfos = bookInfos
                .Where(
                    info =>
                        collection.Type == BookCollection.CollectionType.TheOneEditableCollection
                        || info.ShowThisBookAsSource()
                )
                .Select(info =>
                {
                    var title = info.QuickTitleUserDisplay;
                    if (collection.IsFactoryInstalled)
                        title = LocalizationManager.GetDynamicString(
                            "Bloom",
                            "TemplateBooks.BookName." + title,
                            title
                        );
                    return new
                    {
                        id = info.Id,
                        title,
                        collectionId = collection.PathToDirectory,
                        folderPath = info.FolderPath,
                        isFactory = collection.IsFactoryInstalled
                    };
                })
                .ToArray();

            // The goal here is to draw the book buttons before we tie up the UI thread for a long time loading
            // the previously selected book and showing it. So we're going to wait until we get a few requests
            // for the thumbnails used in book buttons. But we need to be sure we will actually get those requests,
            // otherwise, doing the selected book stuff and hiding the splash screen and so on could be permanently
            // prevented. Unfortunately, the buttons are lazy, and in a pathological case the user might drag the
            // splitters and size the window so only one of these buttons is visible. Even in such a case, there
            // should be at least one more visible in the templates area. So I think we can be confident of
            // getting at least two requests. Fortunately, waiting for those seems to be enough to make it look
            // as if we're prioritizing the whole primary collection.
            // We need the count to be at least one, even if the main collection is empty, so that the milestone
            // will always be reported.
            lock (_thumbnailEventsLock)
            {
                if (collection.Type == BookCollection.CollectionType.TheOneEditableCollection)
                    _thumbnailEventsToWaitFor = Math.Max(Math.Min(jsonInfos.Length, 2), 1);
            }

            var json = DynamicJson.Serialize(jsonInfos);
            request.ReplyWithJson(json);
        }

        // This needs to be thread-safe.
        private BookCollection GetCollectionOfRequest(ApiRequest request)
        {
            // Collection can be specified by id, which is the equal to the directory path on disk. If this is not specified, we default to the editable collection.
            var id = request.GetParamOrNull("collection-id")?.Trim();
            BookCollection collection;
            if (string.IsNullOrWhiteSpace(id))
            {
                collection = _collectionModel.CurrentEditableCollection;
            }
            else
            {
                collection = _collectionModel
                    .GetBookCollections()
                    .ToList()
                    .FirstOrDefault(c => c.PathToDirectory == id);
            }
            if (collection == null)
            {
                request.Failed($"Collection with path '{id}' was not found.");
            }

            return collection;
        }

        public void HandleThumbnailRequest(ApiRequest request)
        {
            lock (_thumbnailEventsLock)
            {
                if (_thumbnailEventsToWaitFor > 0)
                {
                    _thumbnailEventsToWaitFor--;
                    if (_thumbnailEventsToWaitFor == 0)
                        StartupScreenManager.StartupMilestoneReached("collectionButtonsDrawn");
                }
            }

            var bookInfo = GetBookInfoFromRequestParam(request);

            // Not sure what causes bookInfo to be null, but apparently it's possible: See BL-12354
            // Let's gracefully fail in this scenario
            if (bookInfo == null)
            {
                using (var stream = ConvertImageToStream(Resources.Error70x70))
                {
                    request.ReplyWithStreamContent(stream, "image/png");
                }
                return;
            }

            string path = Path.Combine(bookInfo.FolderPath, "thumbnail.png");
            if (!RobustFile.Exists(path))
            {
                // This is rarely if ever needed. Bloom already does this when selecting a book
                // or after editing it. One case (but it won't succeed) is when the book doesn't
                // HAVE an image on the cover.
                _thumbNailer.MakeThumbnailOfCover(_collectionModel.GetBookFromBookInfo(bookInfo));
            }
            if (RobustFile.Exists(path))
                request.ReplyWithImage(path);
            else
            {
                var errorImg = Resources.placeHolderBookThumbnail;
                if (_collectionModel.GetBookFromBookInfo(bookInfo).HasFatalError)
                    errorImg = Resources.Error70x70;
                using (var stream = ConvertImageToStream(errorImg))
                {
                    request.ReplyWithStreamContent(stream, "image/png");
                    //request.Failed("Thumbnail doesn't exist, and making a new thumbnail is not yet implemented.");
                }
            }
        }

        private void GetBookOnBloomBadgeInfo(ApiRequest apiRequest)
        {
            var bookId = apiRequest.RequiredParam("book-id");

            var infos = _collectionModel.TheOneEditableCollection
                .GetBookInfos()
                .Where(info => info.Id == bookId && info.BloomLibraryStatus != null)
                .ToList();
            if (infos.Count == 0)
            {
                apiRequest.ReplyWithJson(new { bookUrl = "", });
            }
            else if (infos.Count == 1)
            {
                var info = infos[0];
                apiRequest.ReplyWithJson(
                    new
                    {
                        bookUrl = info.BloomLibraryStatus.BloomLibraryBookUrl,
                        draft = info.BloomLibraryStatus.Draft,
                        inCirculation = !info.BloomLibraryStatus.NotInCirculation,
                        harvestState = info.BloomLibraryStatus.HarvesterState
                            .ToString()
                            .ToLowerInvariant()
                    }
                );
            }
            else
            {
                // This may duplicate the action in BloomParseCLient.GetLibraryStatusForBooks, but it doesn't
                // hurt to generate the url and harvest status twice.  The operation in BloomParseClient can
                // handle duplicate book ids in different collections while this one looks only at the current
                // collection.
                apiRequest.ReplyWithJson(
                    new
                    {
                        bookUrl = BloomLibraryUrls.BloomLibraryBooksWithMatchingIdListingUrl(
                            bookId
                        ),
                        draft = false,
                        inCirculation = true,
                        harvestState = HarvesterState.Multiple.ToString().ToLowerInvariant()
                    }
                );
            }
        }

        /// <summary>
        /// Convert an image bitmap into a MemoryStream
        /// </summary>
        /// <remarks>The caller is responsible for ensuring the stream gets closed somehow</remarks>
        private MemoryStream ConvertImageToStream(System.Drawing.Bitmap image)
        {
            var stream = new MemoryStream();
            image.Save(stream, ImageFormat.Png);
            stream.Seek(0L, SeekOrigin.Begin);
            return stream;
        }

        private BookInfo GetBookInfoFromRequestParam(ApiRequest request)
        {
            var bookId = request.RequiredParam("book-id");
            return GetCollectionOfRequest(request)
                .GetBookInfos()
                .FirstOrDefault(info => info.Id == bookId);
        }

        // Needs to be thread-safe
        private BookInfo GetBookInfoFromPost(ApiRequest request)
        {
            // We can specify the book by id or by path explicitly, or by just sending the id as the body of the post.
            Func<BookInfo, bool> predicate;
            if (request.GetParamOrNull("path") != null)
            {
                predicate = info => info.FolderPath == request.GetParamOrNull("path");
            }
            else if (request.GetParamOrNull("id") != null)
            {
                predicate = info => info.Id == request.GetParamOrNull("id");
            }
            else
            {
                // the original version of this just handled id as the body of the post
                predicate = info => info.Id == request.RequiredPostString();
            }
            var collection = GetCollectionOfRequest(request);
            return collection.GetBookInfos().FirstOrDefault(predicate);
        }

        private Book.Book GetBookObjectFromPost(
            ApiRequest request,
            bool fullyUpdateBookFiles = false
        )
        {
            var info = GetBookInfoFromPost(request);
            return _collectionModel.GetBookFromBookInfo(info, fullyUpdateBookFiles);
        }

        private Book.Book GetUpdatedBookObjectFromBookInfo(BookInfo info)
        {
            return _collectionModel.GetBookFromBookInfo(info, true);
        }
    }
}
