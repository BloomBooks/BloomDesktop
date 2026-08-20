//#define MEMORYCHECK
// Copyright (c) 2014-2018 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Edit;
using Bloom.FontProcessing;
using Bloom.ImageProcessing;
using Bloom.Properties;
using Bloom.Publish;
using Bloom.Publish.BloomPub;
using Bloom.Publish.Epub;
using Bloom.SafeXml;
using Bloom.web;
using Bloom.web.controllers;
using Bloom.WebLibraryIntegration;
using DesktopAnalytics;
using L10NSharp;
using Newtonsoft.Json;
using SIL.Code;
using SIL.IO;
using SIL.PlatformUtilities;
using SIL.Reporting;
using ThreadState = System.Threading.ThreadState;

namespace Bloom.Api
{
    // This interface allows the unit tests to mock the BloomServer
    // when it doesn't want to spin up a real one.
    public interface IBloomServer
    {
        /// <summary>
        /// See BloomServer.ReportThreadBlocking. Dispose the result when the blocking work is done;
        /// there is deliberately no separate "unblocked" call to forget or to get wrong.
        /// </summary>
        IDisposable ReportThreadBlocking();

        // ENHANCE: Add other methods as needed
    }

    /// <summary>
    /// A local http server that can serve (low-res) images plus other files.
    /// </summary>
    /// <remarks>geckofx makes concurrent requests of URLs which this class handles. This means
    /// that the methods of this class get called on different threads, so it has to be
    /// thread-safe.</remarks>
    public class BloomServer : IBloomServer, IDisposable
    {
        public static int portForHttp;
        public const int kNumberOfConsecutivePortsToReserve = 3;

        /// <summary>
        /// How many ports EnsureListening will try before giving up — and giving up means
        /// ProgramExit.Exit, not an exception someone can catch. Class-level and internal rather
        /// than a local, so that the tests can assert against the real number: they hold listeners
        /// open deliberately (see BloomTests' RetiredTestServers) and their budget has to be
        /// checked against this, not against a copy of it that could silently fall out of step.
        /// </summary>
        internal const int kNumberOfPortsToTry = 20;

        public static int WebSocketPort => portForHttp + 1;

        public static int RemoteDebuggingPort => portForHttp + 2;

        public static string ServerUrl
        {
            get { return "http://localhost:" + portForHttp.ToString(CultureInfo.InvariantCulture); }
        }

        /// <summary>
        /// Prefix we add to after the RootUrl in all our urls. This is just a legacy thing we could remove.
        /// </summary>
        const string kBloomPrefix = "/bloom/";
        internal const string WorkerThreadNamePrefix = "Server Worker Thread ";

        public static string ServerUrlEndingInSlash
        {
            get { return ServerUrl + "/"; }
        }

        //We may stop using this one... the /bloom is superfluous since we own the port
        public static string ServerUrlWithBloomPrefixEndingInSlash
        {
            get { return ServerUrl + kBloomPrefix; }
        }

        /// <summary>
        /// Listens for requests"
        /// </summary>
        private HttpListener _listener;

        /// <summary>
        /// Requests that come into the _listener are placed in the _queue so they can be processed
        /// </summary>
        private readonly Queue<HttpListenerContext> _queue;

        // tasks that should be postponed until no server actions are happening.

        private readonly Queue<IdleTaskQueueItem> _idleTasks = new Queue<IdleTaskQueueItem>(); // access locked with _queue

        /// <summary>
        /// Some requests which may be made to the server require other requests to be initiated
        /// and completed before the original request can be completed. Currently there is one
        /// example of this kind of request, when the server is asked for a thumbnail (image) and needs
        /// to create a new thumbnail. Creating the thumbnail involves a browser navigating to
        /// the HTML that represents the page. That html contains requests to the server.
        ///
        /// If multiple thumbnails are requested as a group (currently likely in the Add Page dialog),
        /// there is a danger of getting in a situation where all the threads are busy trying to
        /// retrieve (and hence create) thumbnails, so no threads are available to service the requests
        /// of the browser that is trying to navigate to the appropriate page to create the thumbnail.
        /// This is effectively a deadlock; the thumbnail-creation-navigation times-out and we
        /// don't get a thumbnail.
        ///
        /// I have chosen to designate such requests as 'recursive' in the sense that a recursive
        /// request is one that initiates other requests to the server in the course of producing
        /// its result. We keep track of the number of recursive requests that are under way,
        /// and spin up additional threads if we don't have at least a couple that are not tied up
        /// with recursive requests.
        ///
        /// This variable should only be accessed or modified inside a lock of _queue. It is the actual
        /// count of threads currently performing recursive requests (that is, it counts the threads
        /// that are processing contexts for which IsRecursiveRequestContext() returns true).
        /// </summary>
        private int _threadsDoingRecursiveRequests;

        /// <summary>
        /// Gets requests from _listener and puts them in the _queue to be processed
        /// </summary>
        private readonly Thread _listenerThread;

        /// <summary>
        /// Pool of threads that pull a request from the _queue and processes it.
        /// This is a ConcurrentDictionary (ManagedThreadId to thread) just so we can add and remove
        /// things from it without worrying about locking (or deadlocking).
        ///
        /// Two properties of this collection that other code relies on, so take care before changing
        /// either. Additions are made only under lock (_queue) (see SpinUpAWorker), which is what lets a
        /// caller re-check a count and act on it without another thread adding a worker underneath it. And
        /// an entry is only ever REMOVED for a thread that has already died (see the pruning in
        /// EnqueueIncomingRequests) -- nothing removes a live worker. That second property is what makes it
        /// safe for EnsureAWorkerCanStillTakeWork to count live workers WITHOUT the lock: a concurrent
        /// removal can only take away something that was not going to be counted as live anyway, so it
        /// cannot inflate the answer.
        /// </summary>
        private readonly ConcurrentDictionary<int, Thread> _workers = new();

        /// <summary>
        /// Notifies threads that they should stop because the BloomServer object is being disposed
        /// </summary>
        private readonly ManualResetEvent _stop;

        /// <summary>
        /// Notifies threads in the _workers pool that there is a request in the _queue
        /// </summary>
        private readonly ManualResetEvent _ready;

        /// <summary>
        /// Keeps track of the number of worker threads that are blocked
        /// Note: This is NOT automatically computed. Other code should call ReportThreadBlocking() and dispose the scope it returns
        ///        whenever it causes a thread which is or potentially is a server worker thread to block.
        /// Note: This is different than _busyThreads, because a thread may be busy but not blocked.
        /// </summary>
        private int _countBlockedThreads = 0;

        public const string OriginalImageMarker = "OriginalImages"; // Inserted into paths to suppress image processing (for in memory pages and PDF creation)
        private RuntimeImageProcessor _cache;
        private bool _useCache;

        private const string SimulatedFileUrlMarker = "-memsim-";
        private const string FixedSimulatedPathPrefix = "fixed-simulated/";
        static Dictionary<string, string> _urlToSimulatedPageContent =
            new Dictionary<string, string>(); // see comment on MakeInMemoryHtmlFileInBookFolder
        private BloomFileLocator _fileLocator;
        private readonly BookSelection _bookSelection;

        public CollectionSettings CurrentCollectionSettings { get; private set; }

        public BloomApiHandler ApiHandler;

        // This is useful for debugging.
        public static Dictionary<string, string> SimulatedPageContent => _urlToSimulatedPageContent;

        internal static BloomServer _theOneInstance { get; private set; }

        /// <summary>
        /// This is only used in a few special cases where we need one to pass as an argument but it won't be fully used.
        /// </summary>
        internal BloomServer(BookSelection bookSelection)
            : this(new RuntimeImageProcessor(new BookRenamedEvent()), bookSelection) { }

        public BloomServer(
            RuntimeImageProcessor cache,
            BookSelection bookSelection,
            BloomFileLocator fileLocator = null
        )
        {
            _queue = new Queue<HttpListenerContext>();
            _stop = new ManualResetEvent(false);
            _ready = new ManualResetEvent(false);
            _listenerThread = new Thread(EnqueueIncomingRequests);
            _listenerThread.Name = "BloomServer Listener Thread";
            _bookSelection = bookSelection;
            _fileLocator = fileLocator;
            _cache = cache;
            _useCache = Settings.Default.ImageHandler != "off";
            ApiHandler = new BloomApiHandler(bookSelection);
            _theOneInstance = this;
            if (_bookSelection != null) // maybe null in some tests?
                _bookSelection.SelectionChanged += (_, _) => _cache?.ClearAll();
        }

#if DEBUG
        /// <summary/>
        ~BloomServer()
        {
            Dispose(false);
        }
#endif

        /// <summary>
        /// Normally we would want this to be initialized by the constructor, but because BloomServer is
        /// a singleton for the whole application and we're creating it before the ProjectContext,
        /// we can't a get a CollectionSettings until we have the collection folder which allows us to
        /// make the ProjectContext and CollectionSettings. So we have this method that's just used once
        /// by the project context (plus tests).
        /// It would be better still if we could get the BloomServer not to know about the collection at all.
        /// John's idea is that any api about the collection should pass something that identifies it.
        /// </summary>
        /// <param name="collectionSettings"></param>
        public void SetCollectionSettingsDuringInitialization(CollectionSettings collectionSettings)
        {
            CurrentCollectionSettings = collectionSettings;
            ApiHandler.SetCollectionSettingsDuringInitialization(collectionSettings);

            // Ensure we get the new file locator if the collection changes.
            _fileLocator = null;
        }

        // I wish the server didn't have this knowledge about the current state of the workspace,
        // but have not yet found a way to make things like CURRENTPAGE.htm work without them.
        // In the long run, the CurrentPage and all similar URLs should probably have enough information
        // (path to book folder) to determine their state without relying on this knowledge being injected,
        // into the server, but that will be a big change.
        private static volatile string _keyToCurrentPage;
        private static volatile string _keyToWorkspaceRootForDebugging;
        private static volatile string _currentEditPageUrlForDebugging;
        private static volatile string _currentPageListUrlForDebugging;
        private static readonly string _jsAssetVersion = GetJsAssetVersion();

        /// <summary>
        /// We stick this as a param on JS asset URLs to force the browser to get a new version
        /// after a rebuild. In debug mode, we use the current time so that we get a new version
        /// on every run. In release mode, we use the assembly version so that we get a new version
        /// whenever we ship a new release, but not on every run. (Vite dev uses another strategy
        /// that is built-in to Vite.)
        private static string GetJsAssetVersion()
        {
#if DEBUG
            return DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
#else
            return typeof(BloomServer).Assembly.GetName().Version?.ToString() ?? "0";
#endif
        }

        public string CurrentPageContent { get; set; }
        public string ToolboxContent { get; set; }

        public static void SetCurrentEditPageUrlForDebugging(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            _currentEditPageUrlForDebugging = url;
        }

        public static void SetCurrentPageListUrlForDebugging(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            _currentPageListUrlForDebugging = url;
        }

        public static void SetWorkspaceRootUrlForDebugging(string urlOrPath)
        {
            if (string.IsNullOrWhiteSpace(urlOrPath))
                return;

            _keyToWorkspaceRootForDebugging = urlOrPath.FromLocalhost();
        }

        private static string SanitizeFixedSimulatedId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            return id.Replace("/", "_").Replace("\\", "_").Replace(" ", "_");
        }

        internal static string GetFixedSimulatedKeyForId(
            string id,
            InMemoryHtmlFileSource source = InMemoryHtmlFileSource.Frame
        )
        {
            var safeId = SanitizeFixedSimulatedId(id);
            return $"{FixedSimulatedPathPrefix}{safeId}{SimulatedFileUrlMarker}{source}.html";
        }

        internal static string GetFixedSimulatedUrlForId(
            string id,
            InMemoryHtmlFileSource source = InMemoryHtmlFileSource.Frame
        )
        {
            return GetFixedSimulatedKeyForId(id, source).ToLocalhost();
        }

        internal static string PutFixedSimulatedHtmlForId(
            string id,
            string html,
            InMemoryHtmlFileSource source = InMemoryHtmlFileSource.Frame
        )
        {
            var key = GetFixedSimulatedKeyForId(id, source);
            lock (_urlToSimulatedPageContent)
            {
                _urlToSimulatedPageContent[key] = html ?? "";
            }

            return key.ToLocalhost();
        }

        internal static string PutFixedSimulatedDomForId(
            string id,
            HtmlDom dom,
            InMemoryHtmlFileSource source = InMemoryHtmlFileSource.Frame
        )
        {
            if (dom == null)
                throw new ArgumentNullException(nameof(dom));

            XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(dom.RawDom);

            if (
                source == InMemoryHtmlFileSource.Thumb
                || source == InMemoryHtmlFileSource.Pagelist
                || source == InMemoryHtmlFileSource.JustCheckingPage
            )
            {
                ReplaceAnyVideoElementsWithPlaceholder(dom);
            }

            dom.Title = InMemoryHtmlFile.GetTitleForProcessExplorer(source) + " (InMemoryHtmlFile)";
            return PutFixedSimulatedHtmlForId(id, dom.getHtmlStringDisplayOnly(), source);
        }

        public Book.Book CurrentBook => _bookSelection?.CurrentSelection;

        /// <summary>
        /// This code sets things up so that we can edit (or make a thumbnail of, etc.) one page of a book.
        /// This is tricky because we have to satisfy several constraints:
        /// - We need to make this page content the 'src' of an iframe in a browser. So it has to be
        /// locatable by url.
        /// - It needs to appear to the browser to be a document in the book's folder. This allows local
        /// hrefs (e.g., src of images) that are normally relative to the whole-book file to locate
        /// the images. (We previously did this by making a file elsewhere and setting the 'base'
        /// for interpreting urls. But this fails for internal hrefs (starting with #)).
        /// - We don't want to risk leaving junk page files in the real book folder if anything goes wrong.
        /// - There may be several of these in memory pages around at the same time (e.g., when the thumbnailer is
        /// working on several threads).
        /// - The simulated files need to hang around for an unpredictable time (until the browser is done
        /// with them).
        /// The solution we have adopted is to make this server simulate files in the book folder.
        /// That is, the src for the page iframe is set to a localhost: url which maps to a file in the
        /// book folder. This means that any local hrefs (e.g., to images) will become server requests
        /// for the right file in the right folder. However, the page file never exists as a real file
        /// system file; instead, a request for the page file itself will be intercepted, and this server
        /// simply returns the content it has remembered.
        /// To manage the lifetime of the page data, we use a InMemoryHtmlFile object, which the Browser
        /// disposes of when it is no longer looking at that URL. Its dispose method tells this class
        /// to discard the in memory page data.
        /// To handle the need for multiple in memory page files and quickly check whether a particular
        /// url is one of them, we have a dictionary in which the urls are keys.
        /// A marker is inserted into the generated urls if the input HtmlDom wants to use original images.
        /// </summary>
        /// <param name="dom"></param>
        /// <param name="isCurrentPageContent">If this is true, the url will be inserted by JavaScript into
        /// a src attr for an IFrame. We need to account for this because un-escaped quotation marks in the
        /// URL can cause errors in JavaScript strings. Also, we want to use the same name each time
        /// for current page content, so Open Page in Browser works even after changing pages.</param>
        /// <param name="setAsCurrentPageForDebugging"></param>
        /// <param name="source">InMemoryHtmlFileSource enum</param>
        /// <returns></returns>
        public static InMemoryHtmlFile MakeInMemoryHtmlFileInBookFolder(
            HtmlDom dom,
            bool isCurrentPageContent = false,
            bool setAsCurrentPageForDebugging = false,
            InMemoryHtmlFileSource source = InMemoryHtmlFileSource.Normal,
            bool suppressBackgroundColors = false
        )
        {
            var simulatedPageFileName = Path.ChangeExtension(
                (isCurrentPageContent ? "currentPage" : Guid.NewGuid().ToString())
                    + SimulatedFileUrlMarker
                    + source,
                ".html"
            );
            var pathToInMemoryHtmlFile = simulatedPageFileName; // a default, if there is no special folder
            if (dom.BaseForRelativePaths != null)
            {
                pathToInMemoryHtmlFile = Path.Combine(
                        dom.BaseForRelativePaths,
                        simulatedPageFileName
                    )
                    .Replace('\\', '/');
            }
            if (RobustFileExistsWithCaseCheck(pathToInMemoryHtmlFile))
            {
                // Just in case someone perversely calls a book "currentPage" we will use another name.
                // (We want one that does NOT conflict with anything really in the folder.)
                // We only allow one HTML file per folder so we shouldn't need multiple attempts.
                pathToInMemoryHtmlFile = Path.Combine(
                        dom.BaseForRelativePaths,
                        "X" + simulatedPageFileName
                    )
                    .Replace('\\', '/');
            }
            // FromLocalHost is smart about doing nothing if it is not a localhost url. In case it is, we
            // want the OriginalImageMarker (if any) after the localhost stuff.
            pathToInMemoryHtmlFile = pathToInMemoryHtmlFile.FromLocalhost();
            if (dom.UseOriginalImages)
                pathToInMemoryHtmlFile = OriginalImageMarker + "/" + pathToInMemoryHtmlFile;
            var url = pathToInMemoryHtmlFile.ToLocalhost();
            var key = pathToInMemoryHtmlFile.Replace('\\', '/');
            if (isCurrentPageContent)
            {
                // We need to UrlEncode the single and double quote characters, and the space character,
                // so they will play nicely with HTML.
                // PossiblyEncoded, not Unencoded: ToLocalhost() above has already escaped each
                // path component, so we hand this an ENCODED string and rely on it being decoded
                // before UrlEncodedForHttpPath re-encodes it. Saying "unencoded" here would
                // double-encode the whole url.
                var urlPath = UrlPathString.CreateFromPossiblyEncodedString(url);
                url = urlPath.UrlEncodedForHttpPath;
            }
            if (setAsCurrentPageForDebugging)
            {
                _keyToCurrentPage = key;
            }

            // If we are creating a page thumbnail and we have videos,
            // replace them with our standard video placeholder image.
            if (
                source == InMemoryHtmlFileSource.Thumb
                || source == InMemoryHtmlFileSource.Pagelist
                || source == InMemoryHtmlFileSource.JustCheckingPage
            )
            {
                ReplaceAnyVideoElementsWithPlaceholder(dom);
            }
            dom.Title = InMemoryHtmlFile.GetTitleForProcessExplorer(source) + " (InMemoryHtmlFile)"; // makes this show up in Windows Process Explorer WebView2 listing
            var transparencyModifications = HtmlDom.AddTransparencyParamToImages(
                dom,
                suppressBackgroundColors
            );
            string html5String;
            try
            {
                html5String = dom.getHtmlStringDisplayOnly();
            }
            finally
            {
                HtmlDom.RestoreImageSrcs(transparencyModifications);
            }
            lock (_theOneInstance._queue)
            {
                foreach (var item in _theOneInstance._idleTasks)
                {
                    if (item.Id == key)
                    {
                        // Making a new value for this key AFTER we scheduled deleting it means we have
                        // to prevent the deletion, or we'll lose the NEW value. We'd prefer to just delete
                        // the item from the queue, but the Queue API doesn't support this.
                        item.Cancelled = true;
                    }
                }
            }
            lock (_urlToSimulatedPageContent)
            {
                _urlToSimulatedPageContent[key] = html5String;
            }

            return new InMemoryHtmlFile { Key = url };
        }

        private const string vidPlaceHolderDivContents = @"<img src='video-placeholder.svg' />";

        private static void ReplaceAnyVideoElementsWithPlaceholder(HtmlDom dom)
        {
            var vidNodes = dom.SafeSelectNodes(
                "//div[contains(concat(' ', @class, ' '), ' bloom-videoContainer ')]"
            );
            foreach (SafeXmlNode vidNode in vidNodes)
            {
                var placeHolderNode = dom.RawDom.CreateElement("div");
                placeHolderNode.InnerXml = vidPlaceHolderDivContents;
                placeHolderNode.SetAttribute("class", "bloom-imageContainer");

                // When we get to this point and we are creating an epub, we have already generated the
                // temporary IDs needed to determine element visibility. We need to maintain the ID
                // so we don't try to look up IDs in the dom which don't exist and throw a js error.
                var vidNodeId = vidNode.GetAttribute("id");
                if (!string.IsNullOrEmpty(vidNodeId))
                {
                    if (
                        !string.IsNullOrEmpty(vidNodeId)
                        && vidNodeId.StartsWith(PublishHelper.kTempIdMarker)
                    )
                        placeHolderNode.SetAttribute("id", vidNodeId);
                }

                vidNode.ParentNode.ReplaceChild(placeHolderNode, vidNode);
            }
        }

        internal static void RemoveInMemoryHtmlFile(string key)
        {
            // There are potential race conditions where one server thread is asked to fetch an in memory page,
            // but meanwhile, some other thread disposes of it, so it can't be found. We therefore wait to dispose
            // of in memory pages until there are no busy worker threads and no queued actions.
            var realKey = key.FromLocalhost();
            Action removeIt = () =>
            {
                if (key.StartsWith("file://"))
                {
                    var uri = new Uri(key);
                    RobustFile.Delete(uri.LocalPath);
                    return;
                }

                lock (_urlToSimulatedPageContent)
                {
                    _urlToSimulatedPageContent.Remove(realKey);
                }
            };
            lock (_theOneInstance._queue)
            {
                _theOneInstance._idleTasks.Enqueue(
                    new IdleTaskQueueItem() { Id = realKey, WhatToDo = removeIt }
                );
            }
        }

        private static string UrlPrefixForCurrentBookPage(string bookFolderPath) =>
            bookFolderPath.Replace("\\", "/") + "/page" + SimulatedFileUrlMarker;

        public static string UrlForCurrentBookPage(string bookFolderPath, string pageId)
        {
            return (UrlPrefixForCurrentBookPage(bookFolderPath) + pageId + ".htm").ToLocalhost();
        }

        public static string UrlForCurrentBookPageEncodedForIframeSrc(
            string bookFolderPath,
            string pageId
        )
        {
            // PossiblyEncoded because UrlForCurrentBookPage ends in ToLocalhost(), which has
            // already escaped the path components; see the note on CreateFromPossiblyEncodedString.
            var urlPath = UrlPathString.CreateFromPossiblyEncodedString(
                UrlForCurrentBookPage(bookFolderPath, pageId)
            );
            return urlPath.UrlEncodedForHttpPath;
        }

        // Every path should return false or send a response.
        // Otherwise we can get a timeout error as the browser waits for a response.
        //
        // NOTE: this method gets called on different threads!
        protected async Task<bool> ProcessRequestAsync(IRequestInfo request)
        {
            if (
                CurrentCollectionSettings != null
                && CurrentCollectionSettings.SettingsFilePath != null
            )
                request.DoNotCacheFolder = Path.GetDirectoryName(
                        CurrentCollectionSettings.SettingsFilePath
                    )
                    .Replace('\\', '/');

            var localPath = GetLocalPathWithoutQuery(request);

            // In external browsers (especially Chrome), stale cached ES module chunks can be mixed
            // with newer chunks after a rebuild, causing import/export mismatch errors.
            // Route all JS requests through a process-versioned URL once per startup.
            if (localPath.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            {
                var query = request.GetQueryParameters();
                if (string.IsNullOrWhiteSpace(query?.Get("assetv")))
                {
                    var separator = request.RawUrl.Contains("?", StringComparison.Ordinal)
                        ? "&"
                        : "?";
                    request.WriteRedirect(
                        request.RawUrl + separator + "assetv=" + _jsAssetVersion,
                        permanent: false
                    );
                    return true;
                }
            }

            // root of our UI from a web browser pointed at localhost:8089
            if (localPath == "")
            {
                request.ResponseContentType = "text/html";
                request.WriteCompleteOutput(GetHtmlForRootOfBloomUI());
                return true;
            }
            if (localPath == "test-dialog")
            {
                NonFatalProblem.Report(
                    ModalIf.All,
                    PassiveIf.None,
                    "Test of bringing dialog in front of Browser."
                );
                return true;
            }
            // NB: I (JH) am just migrating this here, I don't know what it's about. See commit d15088342a12a97fd5deb16032a456e4a74d82d1
            //enhance: something feeds back these branding logos with a weird URL, that shouldn't be.
            // this 20 is just arbitrary... the point is, if it doesn't start with api/branding, it is bogus.
            if (localPath.IndexOf("api/branding", StringComparison.InvariantCulture) > 20)
                return false;

            // this alias is used by the javascript preview pane
            if (localPath.StartsWith("book-preview"))
            {
                if (localPath == "book-preview")
                {
                    // if we're just working in a browser and forget that you have to have the index.htm
                    localPath = "book-preview/index.htm";
                }

                if (CurrentBook == null)
                {
                    request.WriteCompleteOutput("");
                    return true;
                }
                if (localPath.EndsWith("video-placeholder.svg"))
                {
                    Book.Book.EnsureVideoPlaceholderFile(_bookSelection.CurrentSelection);
                }

                if (localPath == "book-preview/index.htm")
                {
                    request.ResponseContentType = "text/html";
                    var previewDom = CurrentBook.GetPreviewHtmlFileForWholeBook();
                    var transparencyModifications = HtmlDom.AddTransparencyParamToImages(
                        previewDom
                    );
                    string html;
                    try
                    {
                        html = previewDom.getHtmlStringDisplayOnly();
                    }
                    finally
                    {
                        HtmlDom.RestoreImageSrcs(transparencyModifications);
                    }
                    request.WriteCompleteOutput(html);
                    return true;
                }
                else if (localPath == "book-preview/defaultLangStyles.css")
                {
                    // read in current defaultLangStyles.css content, add @font-face info to it if necessary.
                    var cssLangStyles = "";
                    var cssFilePath = Path.Combine(CurrentBook.FolderPath, "defaultLangStyles.css");
                    if (RobustFileExistsWithCaseCheck(cssFilePath))
                        cssLangStyles = RobustFile.ReadAllText(cssFilePath);
                    var serve = FontServe.GetInstance();
                    var fontFaceDeclarations = serve.GetAllFontFaceDeclarations();
                    if (!cssLangStyles.Contains(fontFaceDeclarations))
                    {
                        request.ResponseContentType = "text/css";
                        var cssBuilder = new StringBuilder();
                        cssBuilder.Append(fontFaceDeclarations);
                        cssBuilder.Append(cssLangStyles);
                        request.WriteCompleteOutput(cssBuilder.ToString());
                        return true;
                    }
                    localPath = localPath.Replace("book-preview", CurrentBook.FolderPath);
                }
                else if (localPath == "book-preview/appearance.css")
                {
                    // Use the current appearance-theme-default.css file if appearance.css doesn't exist.
                    var cssFilePath = Path.Combine(CurrentBook.FolderPath, "appearance.css");
                    if (RobustFileExistsWithCaseCheck(cssFilePath))
                        localPath = cssFilePath;
                    else
                        localPath = Path.Combine(
                            BloomFileLocator.GetFolderContainingAppearanceThemeFiles(),
                            "appearance-theme-default.css"
                        );
                }
                else
                {
                    localPath = localPath.Replace("book-preview", CurrentBook.FolderPath);
                }
            }

            // process request for directory index
            if (request.RawUrl.EndsWith("/") && (Directory.Exists(localPath)))
            {
                request.WriteError(403, "Directory listing denied");
                return true;
            }
            if (localPath.EndsWith("testconnection"))
            {
                request.WriteCompleteOutput("OK");
                return true;
            }

            if (await ApiHandler.ProcessRequestAsync(request, localPath))
                return true;

            // note: this is placed as low as I could before some things started to handle the request that should not
            if (
                CurrentCollectionSettings != null
                && CurrentBook != null
                && // during some unit tests, this is null
                ServerHandlerForBloomPlayer.TryToHandle(
                    request,
                    CurrentCollectionSettings.FolderPath,
                    CurrentBook.FolderPath
                )
            )
            {
                return true;
            }

            // Handle image file requests.
            if (ProcessImageFileRequest(request))
                return true;

            if (localPath.Contains("CURRENTPAGE"))
            {
                // This is a 'magic' URL that is useful in e2e tests and when debugging.
                // E.g. http://localhost:8089/bloom/CURRENTPAGE.htm will always show what the workspace is
                // currently showing in the main window, exactly like the 'open in Edge' command.
                // We do a redirect rather than trying to figure out exactly what the current root page
                // content should be because we need at least the mode param to make the startup code
                // put us in the right mode (collection, book, or page), and we already have code
                // that handles params for the current page and page list iframe sources,
                // so we may as well take advantage of it. This also means that CURRENTPAGE and
                // open-in-edge work the same way (in fact the URL we produce here is exactly the
                // same as the one open-in-edge produces).
                var hasCurrentPageKey = !string.IsNullOrWhiteSpace(_keyToCurrentPage);
                var hasWorkspaceRootKey = !string.IsNullOrWhiteSpace(
                    _keyToWorkspaceRootForDebugging
                );

                if (!hasCurrentPageKey && !hasWorkspaceRootKey)
                {
                    request.ResponseContentType = "text/html";
                    request.WriteCompleteOutput(
                        "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Bloom Not Initialized</title></head><body>Bloom is not sufficiently initialized to use CURRENTPAGE</body></html>"
                    );
                    return true;
                }

                var query = request.GetQueryParameters();
                var existingQuery = string.Empty;
                var rawUrlQueryStart = request.RawUrl.IndexOf("?", StringComparison.Ordinal);
                if (rawUrlQueryStart >= 0)
                {
                    existingQuery = request.RawUrl.Substring(rawUrlQueryStart);
                }

                var redirectBaseKey = hasWorkspaceRootKey
                    ? _keyToWorkspaceRootForDebugging
                    : _keyToCurrentPage;
                var redirectBaseUrl = redirectBaseKey.ToLocalhost();

                var redirectUrl = redirectBaseUrl + existingQuery;
                if (
                    string.IsNullOrWhiteSpace(query?.Get("pageListSrc"))
                    && !string.IsNullOrWhiteSpace(_currentPageListUrlForDebugging)
                )
                {
                    var separator = redirectUrl.Contains("?", StringComparison.Ordinal) ? "&" : "?";
                    redirectUrl +=
                        separator
                        + "pageListSrc="
                        + Uri.EscapeDataString(_currentPageListUrlForDebugging);
                }

                if (
                    string.IsNullOrWhiteSpace(query?.Get("pageSrc"))
                    && !string.IsNullOrWhiteSpace(_currentEditPageUrlForDebugging)
                )
                {
                    var separator = redirectUrl.Contains("?", StringComparison.Ordinal) ? "&" : "?";
                    redirectUrl +=
                        separator
                        + "pageSrc="
                        + Uri.EscapeDataString(_currentEditPageUrlForDebugging);
                }

                request.WriteRedirect(redirectUrl, permanent: false);
                return true;
            }
            if (localPath.ToLower().Contains("current-bloompub-url")) //useful when debugging. E.g. http://localhost:8089/bloom/current-bloompub-url will always show the page we're on.
            {
                request.ResponseContentType = "application/json";
                // send url:{PublishApi.PreviewUrl}
                request.WriteCompleteOutput("{\"url\":\"" + PublishApi.PreviewUrl + "\"}");

                return true;
            }

            if (localPath.Contains("writingSystemDisplayForUI.css"))
            {
                request.ResponseContentType = "text/css";
                request.WriteCompleteOutput(
                    CurrentCollectionSettings.GetWritingSystemDisplayForUICss()
                );
                return true;
            }

            string content;
            bool gotSimulatedPage;
            lock (_urlToSimulatedPageContent)
            {
                gotSimulatedPage = _urlToSimulatedPageContent.TryGetValue(localPath, out content);
            }
            if (gotSimulatedPage)
            {
                request.ResponseContentType = "text/html";
                request.WriteCompleteOutput(content ?? "");
                return true;
            }

            if (localPath.StartsWith(FixedSimulatedPathPrefix, StringComparison.Ordinal))
            {
                // Stable in-memory iframe URL exists but has not been populated yet.
                request.ResponseContentType = "text/html";
                request.WriteCompleteOutput("");
                return true;
            }

            if (
                CurrentBook?.FolderPath != null
                && localPath.StartsWith(UrlPrefixForCurrentBookPage(CurrentBook.FolderPath))
            )
            {
                var startIndex = UrlPrefixForCurrentBookPage(CurrentBook.FolderPath).Length;
                var pageId = localPath.Substring(
                    startIndex,
                    localPath.Length - startIndex - ".htm".Length
                );
                request.ResponseContentType = "text/html";
                request.WriteCompleteOutput(
                    EditingModel.GetEditPageIframeContents(CurrentBook, pageId)
                );
                return true;
            }

            if (localPath.StartsWith(OriginalImageMarker))
            {
                // Path relative to in memory page file, and we want the file contents without modification.
                // (Note that the in memory page file's own URL starts with this, so it's important to check
                // for that BEFORE we do this check.)
                // BL-11162 If we get here with the 'OriginalImageMarker' prefix and it's not an image type
                // that can be degraded, there's no point in continuing on with the prefix!
                localPath = localPath.Substring(OriginalImageMarker.Length + 1);
                if (IsImageTypeThatCanBeDegraded(localPath))
                {
                    return ProcessAnyFileContent(request, localPath);
                }
            }

            if (localPath.StartsWith("localhost/", StringComparison.InvariantCulture))
            {
                var temp = LocalHostPathToFilePath(localPath);
                if (RobustFile.Exists(temp))
                    localPath = temp;
            }
            // this is used only by the readium viewer
            else if (localPath.StartsWith("node_modules/jquery/dist/jquery.js"))
            {
                localPath = BloomFileLocator.GetBrowserFile(false, "jquery.min.js");
                // Avoid having "output/browser/" removed on Linux developer machines.
                // GetBrowserFile adds output to the path on developer machines, but not user installs.
                return ProcessContent(request, localPath);
            }

            // As of July 2022, map files are typically found with the corresponding JS bundle files
            // in output/debug. The browser correctly includes that part of the path to the JS file
            // when deriving a URL for the map, and removing it prevents the map file being found
            // and greatly complicates debugging.
            // The only reason I'm not completely deleting this code is I don't understand why
            // it was ever needed or what changed so that it became harmful, so PERHAPS leaving
            // it here commented out will provide a clue if we ever again encounter the situation
            // where it was helpful.
            //Firefox debugger, looking for a source map, was prefixing in this unexpected
            //way.
            //if(localPath.EndsWith("map"))
            //	localPath = localPath.Replace("output/browser/", "");

            if (localPath == "")
            {
                request.ResponseContentType = "text/html";
                request.WriteCompleteOutput(RobustFile.ReadAllText(@"D:\temp\test.htm"));
                return true;
            }

            return ProcessContent(request, localPath);
        }

        bool IsInBookFolder(string path)
        {
            if (CurrentBook == null || CurrentBook.FolderPath == null) // FolderPath may be null in unit tests
                return false;
            return path.Replace("\\", "/").StartsWith(CurrentBook.FolderPath.Replace("\\", "/"));
        }

        private bool TryHandlePlaceholderImageRequest(IRequestInfo info, string imageFile)
        {
            if (!ImageUtils.IsPlaceholderImageFilename(imageFile))
                return false;

            // We now use css to put in the placeholder images, but still use "placeHolder.png" to mark them.
            // So we actually don't want to provide an image file for this placeholder marker.
            // Return 204 No Content to avoid browser showing broken image icon.
            info.WriteNoContent();
            return true;
        }

        // Handle requests for image files, that is, URLs that end in one of our image extensions.
        // Returns true if this is, in fact, a request for an image, in which case it will have
        // been handled; any reporting of problems will have been done, and a response generated.
        private bool ProcessImageFileRequest(IRequestInfo info)
        {
            var imageFile = GetLocalPathWithoutQuery(info);

            // only process images
            var isSvg = imageFile.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
            if (!IsImageTypeThatCanBeDegraded(imageFile) && !isSvg)
                return false;

            if (TryHandlePlaceholderImageRequest(info, imageFile))
                return true;

            // This can't be right. At some point it may have had something to do with
            // images in page thumbnails, but that is now handled by a param.
            // But we definitely don't want Bloom to fail to find any picture of a thumbnail!
            // I'm leaving it in commented out for now in case there really was still a
            // purpose for it and having it here provides a clue when we're trying to debug
            // that problem.
            //imageFile = imageFile.Replace("thumbnail", "");

            var processImage = !isSvg;

            if (imageFile.StartsWith(OriginalImageMarker + "/"))
            {
                imageFile = imageFile.Substring((OriginalImageMarker + "/").Length);

                if (!RobustFileExistsWithCaseCheck(imageFile))
                {
                    // We didn't find the file here, and don't want to use the following else if or we could errantly
                    // find it in the browser root. For example, this outer if (imageFile.StartsWith...) was added because
                    // we were accidentally finding license.png in a template book. See BL-4290.
                    return false;
                }

                var transparentParam = info.GetQueryParameters()["transparent"];

                // bloom-transparent (transparent=force) must always be honored, even when
                // serving original images for PDF. Use the cache so format conversion
                // (e.g. jpg → png) is handled correctly.
                if (transparentParam == "force" && _useCache)
                {
                    var forcedFile = _cache.GetPathToAdjustedImage(
                        imageFile,
                        false,
                        ImageTransparencyMode.Force
                    );
                    if (!string.IsNullOrEmpty(forcedFile))
                    {
                        info.ReplyWithImage(forcedFile, imageFile);
                        return true;
                    }
                }

                if (
                    CurrentBook?.UserPrefs.IncludeBackgroundColors == true
                    && transparentParam == "yes"
                    && _useCache
                )
                {
                    // Use transparencyOnly so AdjustImageForDisplay skips resize and JPEG
                    // conversion and returns null (→ cached as a no-op) when the image isn't
                    // line art, avoiding an expensive reload on every subsequent request.
                    var autoFile = _cache.GetPathToAdjustedImage(
                        imageFile,
                        false,
                        ImageTransparencyMode.Auto,
                        transparencyOnly: true
                    );
                    // autoFile == imageFile means the cache confirmed this image is not line art;
                    // either way we reply with whatever the cache decided is correct.
                    info.ReplyWithImage(autoFile, imageFile);
                    return true;
                }
                // IncludeBackgroundColors is off, or no transparent param — serve the original
                // without any processing.
                info.ReplyWithImage(imageFile);
                return true;
            }
            // Not a case where we are forcing the use of an unmodified image in the book folder.
            if (!RobustFileExistsWithCaseCheck(imageFile))
            {
                // Generally, the path we started with will only work when the HTML file is the root file of a book,
                // or another file (other than preview) that is simulated to be in the book folder,
                // or if we're in an independent iframe where all src attrs are relative to the root HTML file,
                // like a widget.
                // So this branch deals with all the files that are part of Bloom's HTML UI, as well as files
                // that are part of a preview and therefore have book-preview as the next-to-last element
                // of their paths. If it's a bloom-UI file, we expect a path relative to the root folder
                // for Bloom's implementation HTML stuff; if it's part of a preview, we expect it to be
                // in the root folder of the current book.
                var bloomRoot = FileLocationUtilities.GetDirectoryDistributedWithApplication(
                    BloomFileLocator.BrowserRoot
                );
                var sourceDir = bloomRoot;

                if (GetLocalPathRoot(imageFile) == "book-preview")
                {
                    sourceDir = CurrentBook.FolderPath; // no way we should be making a book-preview without a current book
                    imageFile = GetLocalPathAfterRoot(imageFile);
                }

                imageFile = Path.Combine(sourceDir, imageFile);

                if (TryHandlePlaceholderImageRequest(info, imageFile))
                    return true;

                if (!RobustFileExistsWithCaseCheck(imageFile))
                {
                    // There are a few special cases where it's not desirable to change the source of the image
                    // in our source code.

                    // In this case the source is buried in the depths of ckeditor's implementation.
                    // (icons.png or icons_hidpi.png)  See BL-16474.
                    if (imageFile.Contains("ckeditor/skins/flat/icons"))
                    {
                        imageFile = imageFile.Replace("/flat/", "/icy_orange/");
                    }
                    // If the user does add a video or widget, these placeholder .svgs will get copied to the
                    // book folder and used from there. But we don't copy to the book folder while the user
                    // is still in origami in case the user doesn't actually add the video or widget.
                    // So while origami is open, it hits this path and we grab the .svgs from their
                    // original locations.
                    else if (imageFile.EndsWith("video-placeholder.svg"))
                    {
                        imageFile = Path.Combine(
                            bloomRoot,
                            "templates/template books/Sign Language/video-placeholder.svg"
                        );
                    }
                    else if (imageFile.EndsWith("widget-placeholder.svg"))
                    {
                        imageFile = Path.Combine(bloomRoot, "images/widget-placeholder.svg");
                    }

                    if (!RobustFileExistsWithCaseCheck(imageFile))
                    {
                        if (sourceDir != CurrentBook?.FolderPath)
                        {
                            // This could well represent a missing image in Bloom's implementation;
                            // possibly we should do something more conspicuous than this, which just logs it.
                            // But I'm nervous about changing that behavior; there was probably some reason
                            // we didn't want to make more fuss about missing files.
                            if (ShouldReportFailedRequest(info))
                            {
                                ReportMissingFile(info);
                            }
                        }

                        // If we have a missing image in the book folder, or for some other reason we don't want
                        // to bother the user with the problem, or after we HAVE reported it, just report failure
                        // to the browser.
                        info.WriteError(404);
                        return true; // it was an image URL, and we have made a response.
                    }
                }

                // BL-2368: Do not process files from the BloomBrowserUI directory. These files are already in the state we
                //          want them. Running them through _cache.GetPathToAdjustedImage() is not necessary, and in PNG files
                //          it converts all white areas to transparent. This is resulting in icons which only contain white
                //          (because they are rendered on a dark background) becoming completely invisible.
                // Things in the book folder are processed on demand: resized, format-converted, and optionally
                // made transparent, with results cached by GetPathToAdjustedImage / AdjustImageForDisplay.
                processImage = !isSvg && sourceDir == CurrentBook?.FolderPath;
            }

            var originalImageFile = imageFile;
            // Currently _useCache is always true. It appears likely that the intent
            // is not so much about caching, but whether we want image processing.
            if (processImage && _useCache)
            {
                var thumb = info.GetQueryParameters()["thumbnail"] != null;
                var transparentParam = info.GetQueryParameters()["transparent"];
                var transparencyMode =
                    transparentParam == "force" ? ImageTransparencyMode.Force
                    : transparentParam == "yes" ? ImageTransparencyMode.Auto
                    : ImageTransparencyMode.None;

                imageFile = _cache.GetPathToAdjustedImage(imageFile, thumb, transparencyMode);

                if (string.IsNullOrEmpty(imageFile))
                    return false;
            }

            // File served without image processing: either an SVG, a Bloom UI file (BL-2368),
            // or processImage was false because the file was found in bloomRoot (not the book folder).
            info.ReplyWithImage(imageFile, originalImageFile);
            return true;
        }

        protected static bool IsImageTypeThatCanBeDegraded(string path)
        {
            var extension = Path.GetExtension(path);
            if (!String.IsNullOrEmpty(extension))
                extension = extension.ToLower();
            //note, we're omitting SVG
            return (new[] { ".png", ".jpg", ".jpeg" }.Contains(extension));
        }

        static HashSet<string> _imageExtensions = new HashSet<string>(
            new[] { ".jpg", ".jpeg", ".png", ".svg" }
        );

        internal static bool IsImageTypeThatCanBeReturned(string path)
        {
            return _imageExtensions.Contains((Path.GetExtension(path) ?? "").ToLowerInvariant());
        }

        /// <summary>
        /// Adjust the 'localPath' obtained from a request in a platform-dependent way to a path
        /// that can actually be used to retrieve a file (or test for its existence).
        /// </summary>
        /// <param name="localPath"></param>
        /// <returns></returns>
        public static string LocalHostPathToFilePath(string localPath)
        {
#if __MonoCS__
            // The JSON format may use a string like this to reference a local path.
            // Try it without the leading marker.
            return localPath.Substring(10);
#else
            // URL was something like /bloom///localhost/C$/, but info.LocalPathWithoutQuery uses Uri.LocalPath
            // which for some reason drops the leading slashes for a network mapped drive.
            // network mapped drives don't work if the computer isn't on a network.
            // So we'll change the localhost\C$ to C: (same for other letters)
            var pathArray = localPath.Substring(10).ToCharArray();
            var drive = Char.ToUpper(pathArray[0]);
            if (pathArray[1] == '$' && pathArray[2] == '/' && drive >= 'A' && drive <= 'Z')
                pathArray[1] = ':';
            return new String(pathArray);
#endif
        }

        private bool ProcessContent(IRequestInfo info, string localPath)
        {
            if (localPath.EndsWith(".css"))
            {
                return ProcessCssFile(info, localPath);
            }
            if (localPath.Contains("/host/fonts/"))
            {
                return FontsApi.ProcessHostFontsRequest(info, localPath);
            }
            if (localPath.Contains("fonts/Andika"))
            {
                // Rightly or wrongly, Andika is in a different place in our
                // repo structure than the other UI fonts because it with the book fonts.
                // To keep from having to duplicate the Andika font files in both places,
                // this workaround maps the request for Andika as a UI font as if it were a book font.
                return FontsApi.ProcessHostFontsRequest(
                    info,
                    localPath.Replace("fonts/", "/host/fonts/")
                );
            }

            switch (localPath)
            {
                case "currentPageContent":
                    info.ResponseContentType = "text/html";
                    info.WriteCompleteOutput(CurrentPageContent ?? "");
                    return true;
                case "toolboxContent":
                    info.ResponseContentType = "text/html";
                    info.WriteCompleteOutput(ToolboxContent ?? "");
                    return true;
            }
            return ProcessAnyFileContent(info, localPath);
        }

        /// <summary>
        /// Try to find the full path to the requested file based on the input arguments.
        /// If the file is not found, return null.
        /// </summary>
        /// <remarks>
        /// This is becoming refactor-soup, hence the not so useful name.
        /// </remarks>
        private string LookForAFullPathToFile(string localPath, string modPath)
        {
            if (localPath.Contains("favicon.ico")) // browsers ask for this
                return BloomFileLocator.GetBrowserFile(false, "images", "favicon.ico");

            // Prefer JS files that exist directly under BrowserRoot (typically output/browser).
            // This avoids module-chunk collisions with generic names like "index.js" that may
            // also exist in other searchable locations such as node_modules.
            // Keep this narrow so we don't change long-standing lookup behavior for other types.
            if (
                !Path.IsPathRooted(modPath)
                && Path.GetExtension(modPath).Equals(".js", StringComparison.OrdinalIgnoreCase)
            )
            {
                // AbsoluteBrowserRoot, not BrowserRoot: the latter is relative, so this test would
                // resolve against the process's current working directory, which Bloom does not
                // control and which is often not the application folder (BL-16577, BL-16230).
                var browserFilePath = Path.Combine(BloomFileLocator.AbsoluteBrowserRoot, modPath);
                if (RobustFileExistsWithCaseCheck(browserFilePath))
                    return browserFilePath;
            }

            // Is this request the full path to an image file? For most images, we just have the filename. However, in at
            // least one use case, the image we want isn't in the folder of the PDF we're looking at. That case is when
            // we are looking at a "folio", a book that gathers up other books into one big PDF. In that case, we want
            // to find the image in the correct book folder.  See AddChildBookContentsToFolio();
            var possibleFullImagePath = localPath;
            // "OriginalImages/" at the beginning means we're generating a pdf and want full images,
            // but it has nothing to do with the actual file location.
            string OriginalImageMarkerWithSuffix = OriginalImageMarker + "/";
            if (localPath.StartsWith(OriginalImageMarkerWithSuffix))
                possibleFullImagePath = localPath.Substring(OriginalImageMarkerWithSuffix.Length);
            if (
                RobustFileExistsWithCaseCheck(possibleFullImagePath)
                && Path.IsPathRooted(possibleFullImagePath)
            )
            {
                return possibleFullImagePath;
            }
            else
            {
                // Surprisingly, this method will return localPath unmodified if it is a fully rooted path
                // (like C:\... or \\localhost\C$\...) to a file that exists. So this execution path
                // can return contents of any file that exists if the URL gives its full path...even ones that
                // are generated temp files most certainly NOT distributed with the application.
                return FileLocationUtilities.GetFileDistributedWithApplication(
                    true,
                    BloomFileLocator.BrowserRoot,
                    modPath
                );
            }
        }

        private int _missingMapFileCount = 0;

        private bool ProcessAnyFileContent(IRequestInfo info, string localPath)
        {
            string modPath = localPath;
            string path = null;
            var urlPath = UrlPathString.CreateFromUrlEncodedString(modPath);
            var tempPath = urlPath.PathOnly.NotEncoded;
            if (RobustFileExistsWithCaseCheck(tempPath))
                modPath = tempPath;
            path = LookForAFullPathToFile(localPath, modPath);
            if (String.IsNullOrEmpty(path))
            {
                // LocateFile includes userInstalledSearchPaths (e.g. a shortcut to a collection in a non-standard location)
                path = BloomFileLocator.sTheMostRecentBloomFileLocator?.LocateFile(localPath);
                if (String.IsNullOrEmpty(path))
                    path = localPath;
            }

            //There's probably a eventual way to make this problem go away,
            // but at the moment FF, looking for source maps to go with css, is
            // looking for those maps where we said the css was, which is in the actual
            // book folders. So instead redirect to our browser file folder.
            if (String.IsNullOrEmpty(path) || !RobustFileExistsWithCaseCheck(path))
            {
                var isMap = localPath.EndsWith(".map");
                var startOfBookLayout = localPath.IndexOf("bookLayout");
                if (startOfBookLayout > 0)
                    path = BloomFileLocator.GetBrowserFile(
                        isMap,
                        localPath.Substring(startOfBookLayout)
                    );
                var startOfBookEdit = localPath.IndexOf("bookEdit");
                if (startOfBookEdit > 0)
                    path = BloomFileLocator.GetBrowserFile(
                        isMap,
                        localPath.Substring(startOfBookEdit)
                    );
                if ((startOfBookLayout > 0 || startOfBookEdit > 0) && isMap && path == null)
                {
                    ReportMissingFile(info); // This logs the problem, but doesn't show it to the user.
                    ++_missingMapFileCount;
                    if (ApplicationUpdateSupport.IsDev)
                    {
                        if (_missingMapFileCount < 5) // report first four missing files via dialog
                        {
                            NonFatalProblem.Report(
                                ModalIf.All,
                                PassiveIf.None,
                                "Missing map file: " + localPath,
                                showSendReport: false,
                                skipSentryReport: true
                            );
                        }
                    }
                    else
                    {
                        if (_missingMapFileCount < 2) // report first missing file via toast
                        {
                            NonFatalProblem.Report(
                                ModalIf.None,
                                PassiveIf.All,
                                "Missing map file: " + localPath,
                                showSendReport: false,
                                skipSentryReport: true
                            );
                        }
                    }
                    return false;
                }
            }

            if (
                !RobustFileExistsWithCaseCheck(path)
                && localPath.StartsWith("pageChooser/")
                && IsImageTypeThatCanBeReturned(localPath)
            )
            {
                // if we're in the page chooser dialog and looking for a thumbnail representing an image in a
                // template page, look for that thumbnail in the book that is the template source,
                // rather than in the folder that stores the page choose dialog HTML and code.
                var templateBook = _bookSelection.CurrentSelection.FindTemplateBook();
                if (templateBook != null)
                {
                    var pathMinusPrefix = localPath.Substring("pageChooser/".Length);
                    var templatePath = Path.Combine(templateBook.FolderPath, pathMinusPrefix);
                    if (RobustFileExistsWithCaseCheck(templatePath))
                    {
                        info.ReplyWithImage(templatePath);
                        return true;
                    }
                    // Might be a page from a different template than the one we based this book on
                    path = BloomFileLocator.sTheMostRecentBloomFileLocator.LocateFile(
                        pathMinusPrefix
                    );
                    if (!String.IsNullOrEmpty(path))
                    {
                        info.ReplyWithImage(path);
                        return true;
                    }
                }
            }
            // This was REMOVED to fix BL-11319. Problems with it:
            // 1) it is now testing localPath AFTER we've already moved on to "path"
            // 2) it is tesing the infor.RawUrl, again, after we've already move on to locaPath and then path
            // 3) I can't reproduce the original problem of BL-3835 any more, if I remove it.
            // 4) The unit test that came with the PR now passes without this code. (https://github.com/BloomBooks/BloomDesktop/pull/1221)
            /*
             *
            // Use '%25' to detect that the % in a Url encoded character (for example space encoded as %20) was encoded as %25.
            // In this example we would have %2520 in info.RawUrl and %20 in localPath instead of a space.  Note that if an
            // image has a % in the filename, like 'The other 50%', and it isn't doubly encoded, then this shouldn't be a
            // problem because we're triggering here only if the file isn't found.
            //
            if (!RobustFile.Exists(localPath) && info.RawUrl.Contains("%25"))
            {
                // possibly doubly encoded?  decode one more time and try.  See https://issues.bloomlibrary.org/youtrack/issue/BL-3835.
                // Some existing books have somehow acquired Url encoded coverImage data like the following:
                // <div data-book="coverImage" lang="*">
                //     The%20Moon%20and%20The%20Cap_Cover.png
                // </div>
                // This leads to data being stored doubly encoded in the program's run-time data.  The coverImage data is supposed to be
                // Html/Xml encoded (using &), not Url encoded (using %).
                path = HttpUtility.UrlDecode(localPath);
            }
            */
            if (
                !RobustFileExistsWithCaseCheck(path)
                && IsImageTypeThatCanBeReturned(localPath)
                && _bookSelection?.CurrentSelection != null
            )
            {
                // last resort...maybe we are in the process of renaming a book (BL-3345) and something mysteriously is still using
                // the old path. For example, I can't figure out what hangs on to the old path when an image is changed after
                // altering the main book title.
                var currentFolderPath = Path.Combine(
                    _bookSelection.CurrentSelection.FolderPath,
                    Path.GetFileName(localPath)
                );
                if (RobustFileExistsWithCaseCheck(currentFolderPath))
                {
                    info.ReplyWithImage(currentFolderPath);
                    return true;
                }
            }

            if (
                !RobustFileExistsWithCaseCheck(path)
                && IsAudioFileWhichCanHaveCompressedCounterpart(path)
            )
            {
                var possiblePublishableAudioPath = Path.ChangeExtension(
                    path,
                    AudioRecording.kPublishableExtension
                );
                if (RobustFileExistsWithCaseCheck(possiblePublishableAudioPath))
                {
                    path = possiblePublishableAudioPath;
                    modPath = Path.ChangeExtension(modPath, AudioRecording.kPublishableExtension);
                }
            }

            if (
                !RobustFileExistsWithCaseCheck(path)
                && path.Length > kBloomPrefix.Length
                && path.StartsWith(kBloomPrefix)
            )
            {
                // On developer machines, we can lose part of path earlier.  Try one more thing, the
                // local path starts with this prefix.
                path = info.LocalPathWithoutQuery.Substring(kBloomPrefix.Length);
            }

            if (!RobustFileExistsWithCaseCheck(path))
            {
                if (ShouldReportFailedRequest(info, CurrentBook?.FolderPath))
                {
                    ReportMissingFile(localPath, path);
                }
                return false; // from here we head off to BloomServer.MakeReply() which now uses the same ShouldReportFailedRequest() method.
            }
            info.ResponseContentType = GetContentType(Path.GetExtension(modPath));
            info.ReplyWithFileContent(path);
            return true;
        }

        private bool IsAudioFileWhichCanHaveCompressedCounterpart(string path)
        {
            return path.EndsWith($".{AudioRecording.kRecordableExtension}");
        }

        private static void ReportMissingFile(string localPath, string path)
        {
            if (path == null)
            {
                path = "(was null)";
            }

            // we have any number of incidences where something asks for a page after we've navigated from it. E.g. BL-3715, BL-3769.
            // I suspect our disposal algorithm is just flawed: the page is removed from the _url cache as soon as we navigated away,
            // which is too soon. But that will take more research and we're trying to finish 3.7.
            // So for now, let's just not to bother the user about an error that is only going to effect thumbnailing.
            if (IsSimulatedFileUrl(localPath))
            {
                //even beta users should not be confronted with this
                // localization not really needed because this is seen only by beta testers.
                NonFatalProblem.Report(
                    ModalIf.Alpha,
                    PassiveIf.Beta,
                    "Page expired",
                    "Server no longer has this page in the memory: " + localPath
                );
            }
            else if (IsImageTypeThatCanBeReturned(localPath))
            {
                // Complain quietly about missing image files.  See http://issues.bloomlibrary.org/youtrack/issue/BL-3938.
                // The user visible message needs to be localized.  The detailed message is more developer oriented, so should stay in English.  (BL-4151)
                var userMsg = LocalizationManager.GetString(
                    "WebServer.Warning.NoImageFile",
                    "Cannot Find Image File"
                );
                var detailMsg = String.Format(
                    "Server could not find the image file {0}. LocalPath was {1}{2}",
                    path,
                    localPath,
                    Environment.NewLine
                );
                NonFatalProblem.Report(ModalIf.None, PassiveIf.All, userMsg, detailMsg);
            }
            else
            {
                // The user visible message needs to be localized.  The detailed message is more developer oriented, so should stay in English.  (BL-4151)
                var userMsg = LocalizationManager.GetString(
                    "WebServer.Warning.NoFile",
                    "Cannot Find File"
                );
                var detailMsg = String.Format(
                    "Server could not find the file {0}. LocalPath was {1}{2}{3}",
                    path,
                    localPath,
                    GetBareNameDiagnostics(localPath),
                    Environment.NewLine
                );
                NonFatalProblem.Report(ModalIf.Beta, PassiveIf.All, userMsg, detailMsg);
            }
        }

        /// <summary>
        /// Extra detail for the report when the request had no directory at all, e.g. "Checkbox.js".
        /// Those are almost always files we ship: a bundle we inject into a page at the server root
        /// (see Book.AddJavascriptFile) imports its sibling chunks by bare name, so they arrive here
        /// as a bare name too. Such a file should always be found under BrowserRoot, so if it isn't
        /// we want to know what the process's working directory was (some of our lookups used to
        /// resolve BrowserRoot against it) and whether the file is really absent from the install.
        /// See BL-16577. Returns the empty string for ordinary requests, which carry a directory.
        /// </summary>
        private static string GetBareNameDiagnostics(string localPath)
        {
            try
            {
                if (!String.IsNullOrEmpty(Path.GetDirectoryName(localPath)))
                    return "";
                var browserRoot = BloomFileLocator.AbsoluteBrowserRoot;
                var expectedPath = Path.Combine(browserRoot, localPath);
                return String.Format(
                    "{0}The request had no directory. BrowserRoot is {1} (exists: {2}); {3} exists: {4}; current directory is {5}",
                    Environment.NewLine,
                    browserRoot,
                    Directory.Exists(browserRoot),
                    expectedPath,
                    RobustFile.Exists(expectedPath),
                    Directory.GetCurrentDirectory()
                );
            }
            catch (Exception e)
            {
                // This is only diagnostics for a problem we are already reporting; never let it
                // become the thing that fails.
                return Environment.NewLine + "Could not gather diagnostics: " + e.Message;
            }
        }

        private static bool IsSimulatedFileUrl(string localPath)
        {
            var extension = Path.GetExtension(localPath);
            if (extension != null && !extension.StartsWith(".htm"))
                return false;

            // a good improvement might be to make these urls more obviously cache requests. But for now, let's just see if they are filename guids
            var filename = Path.GetFileNameWithoutExtension(localPath);
            return filename.Contains(SimulatedFileUrlMarker);
        }

        /// <summary>
        /// Requests with ?generateThumbnailIfNecessary=true are potentially recursive in that we may have to navigate
        /// a browser to the template page in order to construct the thumbnail.
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        protected bool IsRecursiveRequestContext(HttpListenerContext context)
        {
            return context.Request.QueryString["generateThumbnailIfNecessary"] == "true";
        }

        private bool ProcessCssFile(IRequestInfo info, string incomingPath)
        {
            // BL-2219: "OriginalImages" means we're generating a pdf and want full images,
            // but it has nothing to do with css files and defeats the following 'if'
            var localPath = incomingPath.Replace(OriginalImageMarker + "/", "");
            if (IsInBookFolder(localPath))
            {
                // Any CSS files that are in the book folder should be up to date so we'll just use them.
                info.ResponseContentType = "text/css";
                if (!RobustFile.Exists(localPath))
                {
                    // Some supporting css files, like editMode.css, are not copied to the book folder
                    // because they are not needed for viewing or publishing.
                    localPath = _bookSelection.CurrentSelection?.Storage.GetSupportingFile(
                        Path.GetFileName(localPath)
                    );
                }
                if (RobustFile.Exists(localPath))
                    info.ReplyWithFileContent(localPath);
                else
                {
                    info.WriteCompleteOutput("");
                }

                return true;
            }

            // if not a full path, try to find the correct file
            var fileName = Path.GetFileName(localPath);

            // try to find the css file in the xmatter and templates
            if (_fileLocator == null)
            {
                _fileLocator = Program.OptimizedFileLocator;
            }

            // In BL-5824, we got bit by a design decision we made that allows stylesheets installed via bloompack
            // to override local ones. This was done so that we could send out new custom stylesheets via webpack
            // and have those used in all the books. Fine. But that is indiscriminate; it also was grabbing
            // any "customBookStyles.css" from those sources and using it instead (here) and replacing that of your book (in BookStorage).
            // Also, we make sure in BookStorage.UpdateSupportFiles that the correct branding.css is present in the
            // book folder; searching our usual path might find an undesirable one in some other collection.
            string path = "";

            path = _fileLocator.LocateFile(fileName);
            // if still not found, and localPath is an actual file path, use it
            if (String.IsNullOrEmpty(path) && RobustFileExistsWithCaseCheck(localPath))
            {
                path = localPath;
            }

            if (String.IsNullOrEmpty(path))
            {
                // it's just possible we need to add BloomBrowserUI to the path (in the case of the AddPage dialog)
                var p = FileLocationUtilities.GetFileDistributedWithApplication(
                    true,
                    BloomFileLocator.BrowserRoot,
                    localPath
                );
                if (RobustFileExistsWithCaseCheck(p))
                    path = p;
            }
            if (String.IsNullOrEmpty(path))
            {
                var p = FileLocationUtilities.GetFileDistributedWithApplication(
                    true,
                    BloomFileLocator.BrowserRoot,
                    incomingPath
                );
                if (RobustFileExistsWithCaseCheck(p))
                    path = p;
            }

            // return false if the file was not found
            if (String.IsNullOrEmpty(path))
                return false;

            info.ResponseContentType = "text/css";
            info.ReplyWithFileContent(path);
            return true;
        }

        #region Startup

        /// <summary>
        /// If the server is not already listening, then starts it.
        /// Otherwise, does nothing, thereby avoiding an exception from starting listening multiple times.
        /// </summary>
        public virtual void EnsureListening()
        {
            if (_listener?.IsListening == true)
                return;
            const int kStartingPort = 8089;
            bool success = false;

            // Note: this now checks whether the following ports in the block are available,
            // but it still does not reserve them until the corresponding services start.
            // So while it's an improvement, it's not yet as solid as we would like it
            //to be.  The ultimate solution is to run the websocket and http on the same port.
            //This could be done using this proxy thing that internally routes to different ports:
            // https://github.com/lifeemotions/websocketproxy
            // Another thing to check on is https://github.com/bryceg/Owin.WebSocket/pull/20 which
            // would give us an owin-compliant version of the fleck websocket server, and we could
            // switch to using an owin-compliant http server like NancyFx.
            for (var i = 0; !success && i < kNumberOfPortsToTry; i++)
            {
                BloomServer.portForHttp = kStartingPort + (i * kNumberOfConsecutivePortsToReserve);
                if (
                    !CanOpenConsecutivePorts(
                        portForHttp + 1,
                        kNumberOfConsecutivePortsToReserve - 1
                    )
                )
                    continue;

                success = AttemptToOpenPort();
            }

            if (!success)
            {
                ErrorReport.NotifyUserOfProblem(GetServerStartFailureMessage());
                Logger.WriteEvent("Error: Could not start up internal HTTP Server");
                Analytics.ReportException(new ApplicationException("Could not start server."));
                ProgramExit.Exit();
            }

            Logger.WriteEvent("Server will use " + ServerUrlEndingInSlash);
            _listenerThread.Start();

            for (var i = 0; i < MinWorkerThreads; i++)
            {
                SpinUpAWorker();
            }

            VerifyWeAreNowListening();
            WriteAutomationStartupInfo();
        }

        private static void WriteAutomationStartupInfo()
        {
            if (!Program.StartupAutomation)
                return;

            Console.WriteLine(
                "BLOOM_AUTOMATION_READY "
                    + JsonConvert.SerializeObject(
                        new
                        {
                            processId = Process.GetCurrentProcess().Id,
                            httpPort = portForHttp,
                            cdpPort = RemoteDebuggingPort,
                        }
                    )
            );
        }

        private static int MinWorkerThreads => Math.Max(Environment.ProcessorCount, 2);

        /// <summary>
        /// Tries to start listening on the currently proposed server url
        /// </summary>
        internal static bool CanOpenConsecutivePorts(int startingPort, int numberOfPortsWeNeed)
        {
            if (numberOfPortsWeNeed <= 0)
                return true;

            if (
                startingPort < IPEndPoint.MinPort
                || startingPort > IPEndPoint.MaxPort - numberOfPortsWeNeed + 1
            )
            {
                return false;
            }

            var listeners = new List<TcpListener>();
            try
            {
                for (var offset = 0; offset < numberOfPortsWeNeed; offset++)
                {
                    var listener = new TcpListener(IPAddress.Loopback, startingPort + offset);
                    listener.Start();
                    listeners.Add(listener);
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                foreach (var listener in listeners)
                    listener.Stop();
            }
        }

        private bool AttemptToOpenPort()
        {
            try
            {
                Logger.WriteMinorEvent(
                    "Attempting to start http listener on " + ServerUrlEndingInSlash
                );
                _listener = new HttpListener
                {
                    AuthenticationSchemes = AuthenticationSchemes.Anonymous,
                };
                _listener.Prefixes.Add(ServerUrlEndingInSlash);
                _listener.Start();
                return true;
            }
            catch (HttpListenerException error)
            {
                Logger.WriteEvent(
                    "Here, file not found is actually what you get if the port is in use:"
                        + error.Message
                );
                return HandleExceptionOpeningPort(error);
            }
            catch (System.Net.Sockets.SocketException error)
            {
                Logger.WriteEvent(
                    $"Port already in use for {ServerUrlEndingInSlash}: {error.Message}"
                );
                return HandleExceptionOpeningPort(error);
            }
        }

        private bool HandleExceptionOpeningPort(Exception error)
        {
            Console.WriteLine(
                $"Cannot open {ServerUrlEndingInSlash}: {error.Message} ({error.GetType().Name})"
            );
            try
            {
                if (_listener != null)
                {
                    //_listener.Stop();  this will always throw if we failed to start, so skip it and go to the close:
                    _listener.Close();
                }
            }
            catch (Exception)
            {
                //that's ok, we're just trying to clean up
            }
            finally
            {
                _listener = null;
            }
            return false;
        }

        public static bool ServerIsListening { get; internal set; }

        private static void VerifyWeAreNowListening()
        {
            try
            {
                var x = new WebClientWithTimeout { Timeout = 3000 };

                if (
                    "OK"
                    != x.DownloadString(ServerUrlWithBloomPrefixEndingInSlash + "testconnection")
                )
                {
                    throw new ApplicationException(GetServerStartFailureMessage());
                }
            }
            catch (Exception error)
            {
                ErrorReport.NotifyUserOfProblem(error, GetServerStartFailureMessage());
                ProgramExit.Exit();
            }

            ServerIsListening = true;
        }

        private static string GetServerStartFailureMessage()
        {
            var zoneAlarm = false;
            if (Platform.IsWindows)
            {
                zoneAlarm =
                    Directory.Exists(
                        Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                            "CheckPoint/ZoneAlarm"
                        )
                    )
                    || Directory.Exists(
                        Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                            "CheckPoint/ZoneAlarm"
                        )
                    );

                if (!zoneAlarm)
                {
                    try
                    {
                        zoneAlarm = Process
                            .GetProcesses()
                            .Any(p =>
                                p.Modules.Cast<ProcessModule>()
                                    .Any(m => m.ModuleName.Contains("ZoneAlarm"))
                            );
                    }
                    catch (Exception error)
                    {
                        Logger.WriteError(
                            "GetServerStartFailureMessage() was unable to check for a running ZoneAlarm Process (BL-4055, Bl-4032, etc.)",
                            error
                        );
                    }
                }
            }
            if (zoneAlarm)
            {
                return LocalizationManager.GetString(
                    "Errors.ZoneAlarm",
                    "Bloom cannot start properly, and this symptom has been observed on machines with ZoneAlarm installed. Note: disabling ZoneAlarm does not help. Nor does restarting with it turned off. Something about the installation of ZoneAlarm causes the problem, and so far only uninstalling ZoneAlarm has been shown to fix the problem."
                );
            }

            return LocalizationManager.GetString(
                "Errors.CannotConnectToBloomServer.2",
                "Bloom was unable to start its own HTTP listener that it uses to talk to its embedded Web browser. If this happens even if you just restarted your computer, then ask someone to investigate if you have an aggressive firewall product installed, which may need to be uninstalled before you can use Bloom."
            );
        }

        private static int _serverIndex;

        // After the initial startup, this should only be called inside a lock(_queue),
        // to avoid race conditions modifying the _workers collection.
        private void SpinUpAWorker()
        {
            var thread = new Thread(RequestProcessorLoop);
            var newIndex = Interlocked.Increment(ref _serverIndex);
            thread.Name = WorkerThreadNamePrefix + newIndex;
            _workers.TryAdd(thread.ManagedThreadId, thread);
            thread.Start();
        }

        #endregion

        /// <summary>
        /// The _listenerThread runs this method, and exits when the _stop event is raised
        /// </summary>
        private void EnqueueIncomingRequests()
        {
            while (_listener.IsListening)
            {
                // We've found that sometimes one of our worker threads just dies. One way to force it to happen is to
                // uncomment the block of code that converts requests for .map files into 404s.
                // We know of no reason for a thread to die except for throwing an uncaught exception, and the method
                // in which this thread loops catches all exceptions that it can, and the handler does not fire in
                // this situation. Conceivably it is something like a stack overflow exception that can't be caught.
                // It's very bad if all our server threads die; Bloom freezes up and can't even quit (in edit mode) because
                // that requires a server request to obtain the page content. So we detect dead threads here and replace them.
                // This is not very satisfactory. We don't know what task if any was left incomplete by the dead thread,
                // nor whether it might have incremented but not decremented one of our counts of threads-in-use.
                // But it's better than freezing up.
                var deadThreads = _workers.Where(kvp => !kvp.Value.IsAlive);
                foreach (var kvp in deadThreads)
                {
                    //thread.Join(); Copilot suggested this but I don't think you can join a dead thread???
                    // Do we want a more drastic report? We don't know what went wrong with the thread, so a report
                    // is unlikely to be very informative. But it's remotely possible that it damaged some data.
                    // Could this be related to the wipeout bug?
                    Debug.WriteLine(
                        $"Worker thread {kvp.Key} ({kvp.Value.Name}) died unexpectedly. Spinning up a replacement"
                    );
                    _workers.TryRemove(kvp.Key, out Thread _);
                    // Seems like just making one would be enough, but preliminary testing still found the number
                    // declining slowly.
                    while (_workers.Count < MinWorkerThreads)
                        SpinUpAWorker();
                }

                var context = _listener.BeginGetContext(QueueRequest, null);

                if (0 == WaitHandle.WaitAny(new[] { _stop, context.AsyncWaitHandle }))
                    return;
            }
        }

        /// <summary>
        /// This method is called in the _listenerThread when we obtain an HTTP request from
        /// the _listener, and queues it for processing by a worker.
        /// </summary>
        /// <param name="ar"></param>
        private void QueueRequest(IAsyncResult ar)
        {
            // this can happen when shutting down
            // BL-2207 indicates it may be possible for the thread to be alive and the listener closed,
            // although the only way I know it gets closed happens after joining with that thread.
            // Still, one more check seems worthwhile...if we're far enough along in shutting down
            // to have closed the listener we certainly can't respond to any more requests.
            if (!_listenerThread.IsAlive || !_listener.IsListening)
                return;

            lock (_queue)
            {
                _queue.Enqueue(_listener.EndGetContext(ar));

                // Deal with a situation where all the workers are blocked,
                // but there is a request in the queue that would unblock the current workers
                // but that request can't run because it's stuck in queue
                // and none of the existing worker threads are able to make progress anymore.
                // Any worker added here is added before the _ready.Set() below, so it receives it.
                // (Monitor is reentrant, so it is fine that we already hold this lock.)
                EnsureAWorkerCanStillTakeWork();

                _ready.Set();
            }
        }

        private int _busyThreads; // access locked to _queue

        /// <summary>
        /// The worker threads run this function
        /// </summary>
        private void RequestProcessorLoop()
        {
            // _ready: indicates that there are requests in the queue that should be processed.
            // _stop:  indicates that the class is being disposed and the thread should terminate.
            WaitHandle[] wait = { _ready, _stop };

            // Wait until a request is ready or the thread is being stopped. The WaitAny will return 0 (the index of
            // _ready in the wait array) if a request is ready, and 1 when _stop is signaled, breaking us out of the loop.
            while (WaitHandle.WaitAny(wait) == 0)
            {
                HttpListenerContext context;
                bool isRecursiveRequestContext; // needs to be declared outside the lock but initialized afte we have the context.
                lock (_queue)
                {
                    if (_queue.Count > 0)
                    {
                        context = _queue.Dequeue();
                    }
                    else
                    {
                        _ready.Reset();
                        continue;
                    }

                    isRecursiveRequestContext = IsRecursiveRequestContext(context);
                    if (isRecursiveRequestContext)
                    {
                        _threadsDoingRecursiveRequests++;
                        // We've got to have some threads not doing recursive tasks.
                        // One non-recursive thread is probably enough to prevent deadlock but some of those
                        // threads are probably reading files so having a few of them
                        // is likely to speed up the recursive task.
                        if (_threadsDoingRecursiveRequests > _workers.Count - 3)
                            SpinUpAWorker();
                    }

                    _busyThreads++;
                }

                var rawurl = "unknown";
                try
                {
                    rawurl = context.Request.RawUrl;

                    // Enhance: the DAISY ACE accessibility report points at images in the epub, correctly and raw, like "tiger.png"
                    // However by the time they get here, the look like "/bloom/C$3A/dev/b43/output/browser/publish/accessibilityCheck/%5C%22tiger.png%5C%22"
                    // In other words, we (humans) can tell what it wants, but this code doesn't have chance.
                    // So for now, we just say "sorry, can't find it".
                    if (
                        rawurl.Contains("accessibilityCheck")
                        && (
                            rawurl.Contains(".png")
                            || rawurl.Contains(".jpg")
                            || rawurl.Contains(".svg")
                        )
                    )
                    {
                        var r = new RequestInfo(new BloomHttpListenerContext(context));

                        r.WriteError(404);

                        return;
                    }
                    // Uncommenting this is a way to cause lots of worker threads to die when an inspector is opened.
                    // Note, this is NOT the right place to handle missing map files; this blocks ALL map file requests,
                    // even if we DO have it. Just keeping the code as a record of a way to reproduce a very puzzling
                    // problem we may want to work on again.
                    //if (rawurl.EndsWith(".map"))
                    //{
                    //    var r = new RequestInfo(new BloomHttpListenerContext(context));
                    //    r.WriteError(404);
                    //    return;
                    //}

                    // set lower priority for thumbnails in order to have less impact on the UI thread
                    if (rawurl.Contains("thumbnail=1"))
                        Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;

                    MakeReply(new RequestInfo(new BloomHttpListenerContext(context)));
                }
                catch (HttpListenerException e)
                {
                    // http://stackoverflow.com/questions/4801868/c-sharp-problem-with-httplistener
                    Logger.WriteEvent(
                        "At BloomServer: ListenerCallback(): HttpListenerException, which may indicate that the caller closed the connection before we could reply. msg="
                            + e.Message
                    );
                    Logger.WriteEvent("At BloomServer: ListenerCallback(): url=" + rawurl);
                }
                catch (Exception error)
                {
#if __MonoCS__
                    // Something keeps closing the socket connection prematurely on Linux/Mono.  But I'm not sure
                    // it's an important failure since the program appears to work okay, so we'll ignore the error.
                    if (
                        error is IOException
                        && error.InnerException != null
                        && error.InnerException is System.Net.Sockets.SocketException
                    )
                    {
                        Logger.WriteEvent(
                            "At BloomServer: ListenerCallback(): IOException/SocketException, which may indicate that the caller closed the connection before we could reply. msg="
                                + error.Message
                                + " / "
                                + error.InnerException.Message
                        );
                        Logger.WriteEvent("At BloomServer: ListenerCallback(): url=" + rawurl);
                    }
                    else
#endif
                    {
                        Logger.WriteEvent(
                            "At BloomServer: ListenerCallback(): msg=" + error.Message
                        );
                        Logger.WriteEvent("At BloomServer: ListenerCallback(): url=" + rawurl);
                        Logger.WriteEvent("At BloomServer: ListenerCallback(): stack=");
                        Logger.WriteEvent(error.StackTrace);
#if DEBUG
                        //NB: "throw" here makes it impossible for even the programmer to continue and try to see how it happens
                        Debug.Fail("(Debug Only) " + error.Message);
#endif
                    }
                }
                finally
                {
                    Thread.CurrentThread.Priority = ThreadPriority.Normal;

                    // ENHANCE: I think this can be safely re-written to only acquire the lock once?
                    if (isRecursiveRequestContext)
                    {
                        lock (_queue)
                        {
                            _threadsDoingRecursiveRequests--;
                        }
                    }

                    lock (_queue)
                    {
                        _busyThreads--;
                    }
                }

                DoIdleTasksIfNoActivity();
            }
        }

        /// <summary>
        ///  If nothing is happening, perform any tasks we could not safely do while other workers
        /// were busy.
        /// </summary>
        internal void DoIdleTasksIfNoActivity()
        {
            for (; ; ) // as long as we find idleTasks to do (and no non-idle ones in progress or waiting)
            {
                // The lock makes sure that exactly one thread will take on a particular idle task,
                // and only if we have reached the safe state. (Since we're also checking _queue.Count
                // here, we need a lock on _queue. Rather than messing with two lock objects, I decided
                // to let _queue be used as a lock object for all access to either queue.)
                Action whatToDo = null;
                lock (_queue)
                {
                    while (_busyThreads == 0 && _queue.Count == 0 && _idleTasks.Count > 0)
                    {
                        var idleTaskQueueItem = _idleTasks.Dequeue();
                        if (!idleTaskQueueItem.Cancelled)
                        {
                            whatToDo = idleTaskQueueItem.WhatToDo;
                            break;
                        }
                    }
                }

                if (whatToDo == null)
                    break;
                // but, we don't need to lock up the queue while we actually do it. Of course, some worker
                // thread may become busy before we finish the idleTask. But that's OK. We just wanted
                // to be sure it wasn't done while something else that was started before it was still
                // in progress. In fact, it's important NOT to let the _queue be locked while we perform
                // the action. The one current instance of idleTask currently involves locking ANOTHER
                // data structure, and if we independently lock two objects, we risk deadlock.
                whatToDo.Invoke();
            }
        }

        /// <summary>
        /// This is designed to be easily unit testable by not taking actual HttpContext, but doing everything through this IRequestInfo object
        /// </summary>
        internal void MakeReply(IRequestInfo info)
        {
            // Since this is the top-level task for a server loop, we need to resolve async processing before returning to
            // the server loop. This would be very prone to deadlocks if called on the UI thread, but it is only
            // called in unit tests and on server threads.
            MakeReplyAsync(info).GetAwaiter().GetResult();
        }

        internal async Task MakeReplyAsync(IRequestInfo info)
        {
            if (!await ProcessRequestAsync(info))
            {
                if (ShouldReportFailedRequest(info))
                    ReportMissingFile(info);
                info.WriteError(404); // Informing the caller is always needed.
            }
#if MEMORYCHECK
            // Check memory for the benefit of developers.  (Also see all requests as a side benefit.)
            var debugMsg = String.Format(
                "after BloomServer.ProcessRequestAsync(\"{0}\")",
                info.RawUrl
            );
            Bloom.Utils.MemoryManagement.CheckMemory(false, debugMsg, false);
#endif
        }

        private void ReportMissingFile(IRequestInfo info)
        {
            var localPath = GetLocalPathWithoutQuery(info);
            Logger.WriteEvent("**{0}: File Missing: {1}", GetType().Name, localPath);
        }

        /// <summary>
        /// Check for files that may be missing but that we know aren't important enough to complain about.
        /// Includes files marked "?optional=true" (not currently used, but may be useful some day) and image files in the CurrentBook folder.
        /// </summary>
        protected bool ShouldReportFailedRequest(
            IRequestInfo info,
            string currentBookFolderPath = null
        )
        {
            // images with src derived from Branding API img elements get this marker
            // in XMatterHelper.CleanupBrandingImages() to prevent spurious reports of
            // images that are intentionally optional.
            var hasOptionalQueryParam = info.GetQueryParameters().Get("optional") == "true";
            if (hasOptionalQueryParam)
                return false;

            // If we are requesting another book, and that book is not there,
            // we don't need both bloom-player and Bloom reporting it.
            if (info.LocalPathWithoutQuery.StartsWith("/book/"))
                return false;

            var localPath = GetLocalPathWithoutQuery(info);
            var localFolderTestPath = localPath;
            // We don't need even a toast for missing files in the book folder. That's the user's problem
            // and should be adequately documented by the browser message saying the file is missing.
            // BL-11162 This includes showing up here with "OriginalImages" prefixed to the url for
            // publishing.
            if (localFolderTestPath.StartsWith(OriginalImageMarker))
            {
                localFolderTestPath = localFolderTestPath.Substring(OriginalImageMarker.Length + 1);
            }
            if (
                currentBookFolderPath != null
                && localFolderTestPath.StartsWith(currentBookFolderPath.Replace("\\", "/"))
            )
                return false;
            // Likewise if it's part of the current book we're publishing. If we didn't give a message about something being
            // missing while creating the book, it's just confusing to do so when they create a publication preview. See BL-9738
            // for one example.
            if (
                PublishApi.CurrentPublicationFolder != null
                && localPath.StartsWith(PublishApi.CurrentPublicationFolder.Replace("\\", "/"))
            )
            {
                return false;
            }

            // If it's in a deleted book (typically we're still trying to update the thumbnail of a book we just deleted),
            // we definitely don't want to bother the user.
            // (Case for CurrentCollectionSettings null is needed for unit tests.)
            var collectionPath = CurrentCollectionSettings?.FolderPath;
            if (
                currentBookFolderPath == null
                && !Directory.Exists(Path.GetDirectoryName(localPath))
                && collectionPath != null
                && localPath.StartsWith(collectionPath.Replace("\\", "/"))
            )
            {
                return false;
            }

            // If we don't have a book or collection established, we are probably in a
            // state where not everything is set up yet.  So don't complain about missing
            // either translation data or items for a problem report.  (BL-15676)
            if (currentBookFolderPath == null && collectionPath == null)
            {
                if (
                    localPath.ToLowerInvariant().Contains("/problemreport/")
                    || localPath.ToLowerInvariant().Contains("/i18n/translate")
                )
                {
                    Logger.WriteEvent(
                        $"BloomServer: neither CurrentBookFolder nor CurrentCollection is set. Cannot find {localPath}"
                    );
                    return false;
                }
            }

            var stuffToIgnore = new[]
            {
                // browser/debugger stuff
                "favicon.ico",
                ".map",
                // Audio files may well be missing because we look for them as soon
                // as we define an audio ID, but they wont' exist until we record something.
                "/audio/",
                // PageTemplatesApi creates a path containing this for a missing template.
                // it gets reported inside the page chooser dialog.
                "missingpagetemplate",
                // Branding image files are expected to be missing in the normal case.  Only organizations that care about branding would have these images.
                "/branding/image",
                // Files missing in the book-preview folder are really missing from the book folder.  See the comment above for checking localPath
                // against the currentBookFolderPath.
                "book-preview/",
                // Bogus file that webview2 has been asking for when launching the debugger
                ".well-known/appspecific/com.chrome.devtools.json",
                // Just like we skip missing files within the book folder and missing book files when we're create a preview in the if cases above,
                // we don't want to complain about missing parts of the book now when we're saving publication files.
                // There is also readium stuff that we don't ship with, because they are needed by the original reader to support display and implementation
                // of controls we hide for things like adding books to collection, displaying the collection, playing audio (that last we might want back one day).
                EpubMaker.kEPUBExportFolder.ToLowerInvariant(),
                BloomPubMaker.BRExportFolder.ToLowerInvariant(),
                BookUpload.kUploadStagingFolder.ToLowerInvariant(),
                // old quiz pages ask for this script, but it's now bundled with rest of edit code
                "simplecomprehensionquiz.js",
                // bloom-player always asks for questions.json for every book.
                // Being only for quiz pages, not every book has it, so we don't want spurious error reports.
                BloomPubMaker.kQuestionFileName.ToLowerInvariant(),
            };
            return !stuffToIgnore.Any(s => localPath.ToLowerInvariant().Contains(s));
        }

        protected internal static string GetLocalPathWithoutQuery(IRequestInfo info)
        {
            return GetLocalPathWithoutQuery(info.LocalPathWithoutQuery);
        }

        private static string GetLocalPathWithoutQuery(string localPath)
        {
            if (localPath.StartsWith(kBloomPrefix))
            {
                localPath = localPath.Substring(kBloomPrefix.Length);
#if __MonoCS__
                if (localPath.StartsWith("tmp/ePUB"))
                    localPath = "/" + localPath; // restore leading slash for full path
#endif
            }
            // and if the file is using localhost:1234/foo.js, at this point it will say "/foo.js", so let's strip off that leading slash
            else if (localPath.StartsWith("/"))
            {
                localPath = localPath.Substring(1);
            }
            if (localPath.Contains("?") && !RobustFileExistsWithCaseCheck(localPath))
            {
                var idx = localPath.LastIndexOf("?", StringComparison.Ordinal);
                return localPath.Substring(0, idx);
            }
            return localPath;
        }

        /// <summary>
        /// Given the localPath, returns the "root" (first directory) of the local path.
        /// </summary>
        /// <remarks>
        /// Can't use C#'s Path.GetPathRoot because "bloom-preview/..." returns ""
        /// </remarks>
        private static string GetLocalPathRoot(string localPath)
        {
            if (String.IsNullOrEmpty(localPath))
                return localPath;

            Debug.Assert(
                !localPath.StartsWith("/") && !localPath.StartsWith("\\"),
                "Precondition violated. localPath is not supposed to have a leading slash"
            );

            var firstDirSeparatorIndex = localPath.IndexOfAny(Extensions.kDirectorySeparators);
            if (firstDirSeparatorIndex < 0)
            {
                return "";
            }

            return localPath.Substring(0, firstDirSeparatorIndex);
        }

        /// <summary>
        /// Given the localPath, returns the part of the path after the "root" (first directory) of the local path.
        /// </summary>
        /// <returns>
        /// The path after the root (no leading slash)
        /// </returns>
        private static string GetLocalPathAfterRoot(string localPath)
        {
            if (String.IsNullOrEmpty(localPath))
                return localPath;

            Debug.Assert(
                !localPath.StartsWith("/") && !localPath.StartsWith("\\"),
                "Precondition violated. localPath is not supposed to have a leading slash"
            );

            var firstDirSeparatorIndex = localPath.IndexOfAny(Extensions.kDirectorySeparators);
            if (firstDirSeparatorIndex < 0)
            {
                return localPath;
            }

            return localPath.Substring(firstDirSeparatorIndex + 1);
        }

        public static string GetContentType(string extension)
        {
            switch (extension)
            {
                case ".css":
                    return "text/css";
                case ".gif":
                    return "image/gif";
                case ".htm":
                case ".html":
                    return "text/html";
                case ".jpg":
                case ".jpeg":
                    return "image/jpeg";
                case ".js":
                    return "application/x-javascript";
                case ".png":
                    return "image/png";
                case ".pdf":
                    return "application/pdf";
                case ".txt":
                    return "text/plain";
                case ".svg":
                    return "image/svg+xml";
                case ".mp3":
                    return "audio/mpeg";
                case ".ogg":
                    return "audio/ogg";
                case ".woff":
                    return "font/woff";
                case ".woff2":
                    return "font/woff2";
                case ".xml":
                    return "application/xml";
                case ".xhtml":
                    return "application/xhtml+xml";
                default:
                    return "application/octet-stream";
            }
        }

        /// <summary>
        /// Reports that the calling thread is about to block -- waiting for a lock, for a modal dialog to
        /// close, for an off-screen browser, and so on. Call it immediately before the blocking work and
        /// dispose the result as soon as that work is done, normally with a `using` block.
        ///
        /// Any code that is *sometimes* run by a server worker may call this; it need not know whether the
        /// current thread is one. Blocks reported by other threads are ignored, since they are not using up
        /// a worker.
        ///
        /// Why this returns a scope instead of having a matching "unblocked" method: whether a block counts
        /// depends on whether the caller is one of our workers, and that answer has to be REMEMBERED rather
        /// than worked out again at the end. Blocking work that contains an await can resume on a different
        /// thread -- a worker carries no synchronization context, so continuations land on the thread pool --
        /// and asking "am I a worker?" there would answer no and silently skip the decrement, inflating the
        /// count for the life of the process. The scope closes over the answer, so it cannot drift, and it
        /// releases on every exit path including an exception. Both bugs were real: see BL-16612.
        /// </summary>
        public IDisposable ReportThreadBlocking()
        {
            // Notably, ProblemReportApi can be invoked by both server and non-server code.
            if (!IsWorkerThread(Thread.CurrentThread))
                return NotBlockingAWorker;

            Interlocked.Increment(ref _countBlockedThreads);
            // Must not throw; if it did, the caller would never receive the scope that undoes the
            // increment above. See the guarantee inside it.
            EnsureAWorkerCanStillTakeWork();
            return new BlockedWorkerScope(this);
        }

        // Shared, stateless scope handed to callers whose thread is not one of our workers, so those
        // callers still get something disposable and need no special case.
        private static readonly IDisposable NotBlockingAWorker = new DoNothingScope();

        private sealed class DoNothingScope : IDisposable
        {
            public void Dispose() { }
        }

        /// <summary>
        /// Undoes exactly one counted block, once. Holding the server (rather than re-deriving anything
        /// from the current thread) is the whole point -- see ReportThreadBlocking.
        /// </summary>
        private sealed class BlockedWorkerScope : IDisposable
        {
            private BloomServer _server;

            public BlockedWorkerScope(BloomServer server)
            {
                _server = server;
            }

            public void Dispose()
            {
                // Taking the reference away atomically makes a second Dispose -- e.g. a `using` inside a
                // method whose caller also disposes -- harmless instead of double-decrementing.
                var server = Interlocked.Exchange(ref _server, null);
                if (server != null)
                    Interlocked.Decrement(ref server._countBlockedThreads);
            }
        }

        /// <summary>
        /// If every worker is now blocked, add one, so that some worker is still able to take a request off
        /// the queue. This matters because the request that would let the blocked workers finish is often
        /// itself sitting in the queue: in BL-16612 a publish held an api lock while every other worker
        /// waited for that same lock, so no worker was left to serve the page the publish was waiting for,
        /// and the whole server deadlocked.
        ///
        /// This is called from ReportThreadBlocking rather than being offered as a separate method for
        /// callers to remember, so that every caller which correctly reports that it is blocking gets the
        /// top-up automatically. There is deliberately no way to register a block WITHOUT it -- reporting
        /// the block is the only thing a caller has to get right.
        ///
        /// QueueRequest makes the same check as a request ARRIVES, which is not sufficient by itself: if
        /// the request that would break the deadlock is already in the queue, no new request need ever
        /// arrive to trigger the check. Checking here covers the other moment the pool can run out -- when
        /// a worker becomes blocked.
        ///
        /// Deliberately has no shutdown guard, unlike QueueRequest, which gives up once the listener has
        /// closed. A worker can therefore add one more worker while Dispose is joining threads. That is safe
        /// only because Dispose signals _stop without disposing it or _ready, so the new worker wakes
        /// immediately and exits -- see the note in Dispose, which must stay true for this to remain safe.
        /// </summary>
        private void EnsureAWorkerCanStillTakeWork()
        {
            // NOTHING in this method may throw, logging and the fast-path reads alike. The caller increments
            // the blocked count and only receives the scope that undoes it AFTER this returns, so an
            // exception escaping here would leak that count for the life of the process -- which would then
            // make every later block add yet another worker. Adding a worker is a safety net; neither
            // failing to add one nor failing to log it may turn into a failed request. The fast path is
            // inside the try so that guarantee is structural, rather than resting on an argument about what
            // ConcurrentDictionary.Count can do.
            //
            // REVIEWED DECISION (BL-16612): catching everything here, including around the logging in the
            // catch below, is a deliberate exception to this repo's "fail fast, don't be defensive"
            // guidance in AGENTS.md. Do not "clean this up" into a narrower catch without reading the rest
            // of this comment, because the obvious objection to it has already been raised and answered.
            //
            // The immediate reason: the caller increments the blocked count and only receives the scope
            // that undoes it after this returns, so an exception escaping here would leave that count
            // permanently high -- the very condition this method exists to relieve.
            //
            // That is not the only way to arrange things, and a cheaper alternative than we first thought
            // does exist: hand the caller its scope BEFORE calling this, dispose it and rethrow if this
            // throws, and then this method would be free to fail fast. That touches only
            // ReportThreadBlocking, not every call site. It was considered and declined, and the reason is
            // a judgement rather than a constraint: topping up the pool is an opportunistic safety net, and
            // a safety net failing is not itself a reason to fail the user's request that happened to
            // trigger it. Publishing a book should not die because the server could not create a spare
            // thread it may well not need. If you disagree with that trade, the restructure above is the
            // way to change it -- but change it deliberately, not by narrowing this catch and reintroducing
            // the leak.
            try
            {
                // Fast path that avoids _queue's lock, which the listener (enqueuing) and the workers
                // (dequeuing) are already contending for; this runs on every api request that waits for a
                // lock. Walking the workers to count the live ones is not free, but it is far cheaper than
                // joining the queue behind those two.
                //
                // Why this counts LIVE workers even out here, where a cheap approximation would normally be
                // fine: the only thing this test can do is SKIP the more careful check below, so the two
                // directions of error are not symmetric.
                //   Too LOW (say we race a SpinUpAWorker that has not added its thread yet): we decline to
                //     return, take the lock, and get the better answer. Self-correcting -- and the reason
                //     reading this unsynchronized is acceptable at all.
                //   Too HIGH: we return here and the check below never runs. The raw entry count is
                //     SYSTEMATICALLY too high, because a dead thread keeps its entry until the listener
                //     prunes it, so using it here left the careful check unreachable.
                // A concurrent removal cannot push us into that dangerous direction, because entries are
                // only removed for threads that are already dead; see the note on _workers.
                //
                // Be clear about what a live count does NOT buy, though. It is a fact about the instant it
                // was taken, and a worker can die immediately afterwards. The re-check under the lock is no
                // better in that respect: the lock covers changes to _workers, not thread liveness. So
                // neither reading is authoritative about how many workers are still alive by the time we
                // act on it. What counting live workers removes is the systematic over-count from lingering
                // dead entries -- not that race.
                //
                // Staleness in the blocked count is harmless: Interlocked.Increment gives the increments a
                // total order, so whichever thread performs the last one reads a count including every
                // earlier block. The worker that exhausts the pool therefore always sees the shortage, even
                // if the ones before it did not.
                if (Volatile.Read(ref _countBlockedThreads) < LiveWorkerCount())
                    return;

                var addedWorker = false;
                // SpinUpAWorker requires this lock, since it modifies _workers. Re-checking here means we
                // decide against a count taken after any concurrent add, rather than the one above -- but
                // per the note above, not against a count guaranteed still true when we act on it.
                lock (_queue)
                {
                    if (_countBlockedThreads >= LiveWorkerCount())
                    {
                        // REVIEWED DECISION (BL-16612): workers are never retired -- that predates this
                        // code -- and we accepted that the pool can therefore end a session larger than it
                        // started. During a long publish, several requests can queue behind the same lock
                        // and each one can add a worker, so growth is bounded by how many are actually
                        // waiting, not unbounded. We judged that a fair price for not deadlocking, but it
                        // is why thread counts in a diagnostic may look higher than you expect. Retiring
                        // idle workers would be the real fix and is a much larger change.
                        SpinUpAWorker();
                        addedWorker = true;
                    }
                }
                // Logged after releasing our own lock (QueueRequest's caller may still hold it), since
                // logging can be slow. This is the event to look for in a log when investigating a freeze,
                // so it is worth a line even though it is not an error.
                if (addedWorker)
                    Logger.WriteEvent(
                        "BloomServer: every worker was blocked, so added one to keep requests moving "
                            + $"({_workers.Count} workers, {_countBlockedThreads} blocked)."
                    );
            }
            catch (Exception e)
            {
                // This last attempt to record what went wrong could itself fail, and nothing may escape
                // (see above), so it is deliberately allowed to fail silently.
                try
                {
                    Logger.WriteEvent(
                        "BloomServer: trouble adding a worker while one was blocking: " + e.Message
                    );
                }
                catch { }
            }
        }

        /// <summary>
        /// How many workers are actually alive and so could still take a request off the queue. Bloom has
        /// seen worker threads die (see the pruning in EnqueueIncomingRequests), and a dead one leaves its
        /// entry in _workers until that pruning runs, so _workers.Count can overstate the pool.
        ///
        /// Counts the dictionary itself rather than its Values, which on a ConcurrentDictionary materialises
        /// a snapshot list -- worth avoiding on something EnsureAWorkerCanStillTakeWork calls for every api
        /// request that waits for a lock. Callers may hold lock (_queue) or not; see that method for why
        /// counting without it is safe.
        /// </summary>
        private int LiveWorkerCount() => _workers.Count(kvp => kvp.Value?.IsAlive == true);

        /// <summary>
        /// The number of worker threads in the pool, alive or not. For tests, which need to observe that a
        /// worker was added when they made every existing worker report itself blocked.
        /// </summary>
        internal int WorkerCount => _workers.Count;

        /// <summary>
        /// How many workers currently report themselves blocked. For tests, which need to prove that a
        /// reported block is undone even when the scope is disposed on a different thread than reported it.
        /// </summary>
        internal int BlockedWorkerCount => _countBlockedThreads;

        // Ordinal deliberately: the default overloads of both StartsWith and IndexOf(string) are
        // culture-sensitive, which is the wrong kind of comparison for a thread name we generated
        // ourselves -- and slower.
        private bool IsWorkerThread(Thread thread) =>
            thread?.Name?.StartsWith(WorkerThreadNamePrefix, StringComparison.Ordinal) == true;

        private string GetHtmlForRootOfBloomUI()
        {
            return ReactControl.GetHtmlForReactBundle(
                "appBundle",
                null,
                System.Drawing.Color.White,
                false
            );
        }

        /// <summary>
        /// Does a RobustFile.Exists() test. Also, if in Debug mode, we compare the actual filepath on disk with 'localPath'.
        /// If the case check fails, a message is logged or an Assert is thrown, depending on whether we're running tests or not.
        /// </summary>
        /// <remarks>Internal for testing.</remarks>
        internal static bool RobustFileExistsWithCaseCheck(string localPath)
        {
            var result = RobustFile.Exists(localPath);
            if (!result)
                return false;

#if DEBUG
            // AppData is for Windows and /tmp/ is for Linux (when we use it again)
            // Installed versions of Bloom are in AppData too, but we'll only make this check if we're in Debug mode.
            if (
                localPath.EndsWith(".htm")
                && (localPath.Contains("AppData") || localPath.Contains("/tmp/"))
            )
                return true; // probably the Temp folder and most likely a random temporary filename).

            // Check the case of the actual filename of the file on disk.
            var fullPath = Path.GetFullPath(localPath);
            var exactPathName = GetExactPathName(fullPath);

            if (!EqualsWithCaseAndNormalizedDirectorySeps(exactPathName, fullPath))
            {
                var msg = $"*** Case error occurred. {fullPath} does not match {exactPathName} ***";
                if (Program.RunningUnitTests)
                {
                    Logger.WriteEvent(msg);
                }
                else
                {
                    Debug.Fail(msg);
                }
            }
#endif

            return true;
        }

        // From https://stackoverflow.com/questions/325931/getting-actual-file-name-with-proper-casing-on-windows-with-net
        // Presumably it will also work on Linux as long as .Net is involved.
        private static string GetExactPathName(string pathName)
        {
            var di = new DirectoryInfo(pathName);

            if (di.Parent != null)
            {
                // The entry may not be found: the file can be deleted while we walk up the path,
                // and a directory enumeration does not always see a file that was only just
                // written. Indexing [0] blindly turned that into an IndexOutOfRangeException that
                // crashed the caller -- which is only a Debug-time check of the filename's case,
                // so it must never be the thing that brings a request (or a test) down. When we
                // can't confirm the on-disk spelling, keep the name we were given; that just means
                // the case check silently passes, which is the right way for a diagnostic to fail.
                var entries = di.Parent.GetFileSystemInfos(di.Name);
                var exactName = entries.Length > 0 ? entries[0].Name : di.Name;
                return Path.Combine(GetExactPathName(di.Parent.FullName), exactName);
            }
            else
            {
                return di.Name.ToUpper();
            }
        }

        private static string NormalizeDirectorySeparators(string path)
        {
            return path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        }

        private static bool EqualsWithCaseAndNormalizedDirectorySeps(string path1, string path2)
        {
            var normPath1 = NormalizeDirectorySeparators(path1);
            var normPath2 = NormalizeDirectorySeparators(path2);
            return String.Equals(normPath1, normPath2, StringComparison.Ordinal);
        }

        #region Disposable stuff

        private bool IsDisposed { get; set; }

        public void Dispose()
        {
            //Stop();
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Frees everything this server can free WITHOUT closing its HttpListener: it stops
        /// accepting requests, stops and joins its threads (which are foreground threads, so
        /// releasing them is what lets a process exit), and releases the image cache. Afterwards
        /// the only thing the server still holds is the listener, and therefore its port.
        ///
        /// Why this is separable from Dispose (BL-16667): closing the listener is the one step
        /// that can crash. A response body can still be going out after the worker that produced
        /// it has finished -- RequestInfo hands the tail of the send to the framework, which
        /// completes it on a callback whose only handler is `catch (Win32Exception)` -- and
        /// closing the listener underneath that makes it throw ObjectDisposedException on a thread
        /// none of our try/catch blocks cover, which kills the process. Everything in this method
        /// is safe to do at any time; only CloseListener has to wait until nothing is in flight.
        ///
        /// Production still calls Dispose, which is PreDispose followed immediately by
        /// CloseListener, so nothing about Bloom's behaviour changes. The tests use this to let
        /// the listener be closed later, once the requests it served are long finished --
        /// see BloomTests' RetiredTestServers.
        /// </summary>
        /// <remarks>
        /// Note the seam, since it is invisible: this goes straight to the private overload, so a
        /// subclass that overrode the protected virtual Dispose(bool) would be called on the real
        /// Dispose path but NOT on this one. Nothing derives from BloomServer today. If something
        /// ever does, make the two-argument overload the virtual one rather than leaving a subclass
        /// silently half-invoked.
        /// </remarks>
        public void PreDispose()
        {
            Dispose(true, closeListener: false);
        }

        protected virtual void Dispose(bool fDisposing)
        {
            Dispose(fDisposing, closeListener: true);
        }

        private void Dispose(bool fDisposing, bool closeListener)
        {
            Debug.WriteLineIf(
                !fDisposing,
                "****** Missing Dispose() call for " + GetType() + ". *******"
            );
            if (fDisposing && !IsDisposed)
            {
                // dispose managed and unmanaged objects
                try
                {
                    ServerIsListening = false;
                    if (_listener != null)
                    {
                        //prompted by the mysterious BL 273, Crash while closing down the imageserver
                        Guard.AgainstNull(_listenerThread, "_listenerThread");
                        //prompted by the mysterious BL 273, Crash while closing down the imageserver
                        Guard.AgainstNull(_stop, "_stop");

                        // tell _listenerThread and the worker threads they should stop
                        _stop.Set();

                        // Note (BL-16612): a worker part-way through a request can still report a block while
                        // we are joining threads below, and EnsureAWorkerCanStillTakeWork has no shutdown
                        // guard, so that can add a worker after the join has passed it. Two things make that
                        // survivable, and BOTH have to stay true:
                        //   1. We only SIGNAL _stop here and never dispose it or _ready, so a late worker's
                        //      WaitAny returns at once and it exits. Dispose those handles and it would die
                        //      instead of an ObjectDisposedException on a background thread, taking the
                        //      process with it.
                        //   2. We actually reach this line. Note that it is inside `if (_listener != null)`,
                        //      so if CloseListener() ran first -- it is public, nulls _listener, and its own
                        //      comment mentions the shutdown timer -- _stop is never signalled at all.
                        // Caveat worth knowing about (2): workers are FOREGROUND threads, so a worker still
                        // waiting on _ready/_stop keeps the process alive. Pre-existing workers have always
                        // been exposed to that; what the top-up adds is the chance of creating a NEW one
                        // during that window. Devin raised it; it is recorded rather than fixed here because
                        // the fix (guard the top-up on shutdown, or make added workers background threads)
                        // changes behaviour and was left for the developer to decide.
                        var secondsToWait = 2.0;
                        // wait for _listenerThread to stop
                        if (_listenerThread.ThreadState != ThreadState.Unstarted)
                        {
                            if (!_listenerThread.Join((int)(secondsToWait * 1000)))
                            {
                                Logger.WriteError(
                                    $"Could not kill a listener thread after waiting {secondsToWait} seconds.",
                                    new ApplicationException()
                                );
                            }
                        }

                        // wait for each worker thread to stop
                        foreach (
                            var kvp in _workers.Where(kvp =>
                                (kvp.Value != null)
                                && kvp.Value.IsAlive
                                && (kvp.Value.ThreadState != ThreadState.Unstarted)
                            )
                        )
                        {
                            if (!kvp.Value.Join((int)(secondsToWait * 1000)))
                            {
                                Logger.WriteError(
                                    "Could not kill a worker thread after waiting 2 seconds.",
                                    new ApplicationException()
                                );
                                secondsToWait = secondsToWait / 2.0; // if one thing is broken, likely other are, so get less patient
                            }
                        }

                        // The one step PreDispose leaves undone; see its comment.
                        if (closeListener)
                            CloseListener();
                    }
                    if (_cache != null)
                    {
                        _cache.Dispose();
                        _cache = null;
                    }
                }
                // ReSharper disable once RedundantCatchClause
                catch (Exception e)
                {
                    //prompted by the mysterious BL 273, Crash while closing down the imageserver
#if DEBUG
                    Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(e);
                    throw;
#else
                    //just quietly report this
                    DesktopAnalytics.Analytics.ReportException(e);
#endif
                }
            }
            // Deliberately NOT set by PreDispose: the server still owns its listener, so a later
            // real Dispose has to be allowed to run and close it. Everything above is safe to
            // repeat -- _stop is already set, the threads are already joined, the cache is already
            // gone -- so the second pass does nothing but the CloseListener that was skipped.
            if (closeListener)
                IsDisposed = true;
        }

        /// <summary>
        /// Close the listener and free up the port. Normally called only by Dispose.
        /// Also used when normal shutdown times out.
        /// </summary>
        public void CloseListener()
        {
            if (_listener == null)
                return; // probably called from shutdown timer, and normal shutdown got this far
            // stop listening for incoming http requests
            Debug.Assert(_listener.IsListening);
            if (_listener.IsListening)
            {
                //In BL-3290, a user quitely failed here each time he exited Bloom, with a Cannot access a disposed object.
                //according to http://stackoverflow.com/questions/11164919/why-httplistener-start-method-dispose-stuff-on-exception,
                //it's actually just responding to being closed, not disposed.
                //I don't know *why* for that user the listener was already stopped.
                _listener.Stop();
            }
            //if we keep getting that exception, we could move the Close() into the previous block
            _listener.Close();
            _listener = null;
        }

        internal bool DoesSimulatedFileExist(string pageListFileUrl)
        {
            bool gotFile = false;
            string content = null;
            lock (_urlToSimulatedPageContent)
            {
                gotFile = _urlToSimulatedPageContent.TryGetValue(pageListFileUrl, out content);
            }
            return gotFile && !string.IsNullOrEmpty(content);
        }

        #endregion
    }

    class IdleTaskQueueItem
    {
        // The actual thing to do when idle
        // (currently typically to delete an obsolete in memory page)
        public Action WhatToDo;

        // An ID which can be used to identify obsolete idle tasks
        // (currently typically the Key of an in memory page)
        public string Id;

        // True if the idle task should not be done after all;
        // we need this because there is no API to simply remove an
        // item from a Queue.
        // (currently set when we add a new in memory page with the same
        // key as one we had queued for deletion but not yet deleted)
        public bool Cancelled;
    }
}
