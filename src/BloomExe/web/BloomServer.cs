//#define MEMORYCHECK
// Copyright (c) 2014-2018 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Net;
using System.Threading;
using System.Web;
using System.Windows.Forms;
using System.Xml;
using DesktopAnalytics;
using L10NSharp;
using Newtonsoft.Json;
using SIL.Code;
using SIL.IO;
using SIL.Reporting;
using BloomTemp;
using Bloom.Book;
using Bloom.ImageProcessing;
using Bloom.Collection;
using Bloom.Edit;
using Bloom.Publish.Epub;
using Bloom.Properties;
using Bloom.Publish;
using Bloom.Publish.Android;
using Bloom.web;
using SIL.PlatformUtilities;
using ThreadState = System.Threading.ThreadState;

namespace Bloom.Api
{
	/// <summary>
	/// A local http server that can serve (low-res) images plus other files.
	/// </summary>
	/// <remarks>geckofx makes concurrent requests of URLs which this class handles. This means
	/// that the methods of this class get called on different threads, so it has to be
	/// thread-safe.</remarks>
	public class BloomServer : IDisposable
	{
		public static int portForHttp;

		public static string ServerUrl
		{
			get { return "http://localhost:" + portForHttp.ToString(CultureInfo.InvariantCulture); }
		}

		/// <summary>
		/// Prefix we add to after the RootUrl in all our urls. This is just a legacy thing we could remove.
		/// </summary>
		internal const string BloomUrlPrefix = "/bloom/";

		public static string ServerUrlEndingInSlash
		{
			get { return ServerUrl + "/"; }
		}

		//We may stop using this one... the /bloom is superfluous since we own the port
		public static string ServerUrlWithBloomPrefixEndingInSlash
		{
			get { return ServerUrl + BloomUrlPrefix; }
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
		/// Pool of threads that pull a request from the _queue and processes it
		/// </summary>
		private readonly List<Thread> _workers = new List<Thread>();

		/// <summary>
		/// Notifies threads that they should stop because the BloomServer object is being disposed
		/// </summary>
		private readonly ManualResetEvent _stop;

		/// <summary>
		/// Notifies threads in the _workers pool that there is a request in the _queue
		/// </summary>
		private readonly ManualResetEvent _ready;
		public const string OriginalImageMarker = "OriginalImages"; // Inserted into paths to suppress image processing (for simulated pages and PDF creation)
		private RuntimeImageProcessor _cache;
		private bool _useCache;

		private const string SimulatedFileUrlMarker = "-memsim-";
		static Dictionary<string, string> _urlToSimulatedPageContent = new Dictionary<string, string>(); // see comment on MakeSimulatedPageFileInBookFolder
		private BloomFileLocator _fileLocator;
		private readonly BookSelection _bookSelection;

		public CollectionSettings CurrentCollectionSettings { get; private set; }

		public BloomApiHandler ApiHandler;

		// This is useful for debugging.
		public static Dictionary<string, string> SimulatedPageContent => _urlToSimulatedPageContent;

		private static BloomServer _theOneInstance;

		/// <summary>
		/// This is only used in a few special cases where we need one to pass as an argument but it won't be fully used.
		/// </summary>
		internal BloomServer(BookSelection bookSelection) : this( new RuntimeImageProcessor(new BookRenamedEvent()), bookSelection, new CollectionSettings())
		{ }

		public BloomServer(RuntimeImageProcessor cache, BookSelection bookSelection, CollectionSettings collectionSettings, BloomFileLocator fileLocator = null)
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
			CurrentCollectionSettings = collectionSettings;
			ApiHandler = new BloomApiHandler(bookSelection, collectionSettings);
			_theOneInstance = this;
		}

#if DEBUG
		/// <summary/>
		~BloomServer()
		{
			Dispose(false);
		}
#endif


		private static string _keyToCurrentPage;

		public string CurrentPageContent { get; set; }
		public string ToolboxContent { get; set; }

		public Book.Book CurrentBook => _bookSelection?.CurrentSelection;

		public enum SimulatedPageFileSource
		{
			Normal,     // Normal page preview
			Pub,        // PDF preview
			Thumb,      // Thumbnailer
			Pagelist,   // Initial list of page thumbs
			Epub,       // ePUB preview
			Nav,        // Navigating to a new page?
			Preview,    // Preview whole book
			Frame       // Editing View is updating single displayed page
		}

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
		/// - There may be several of these simulated pages around at the same time (e.g., when the thumbnailer is
		/// working on several threads).
		/// - The simulated files need to hang around for an unpredictable time (until the browser is done
		/// with them).
		/// The solution we have adopted is to make this server simulate files in the book folder.
		/// That is, the src for the page iframe is set to a localhost: url which maps to a file in the
		/// book folder. This means that any local hrefs (e.g., to images) will become server requests
		/// for the right file in the right folder. However, the page file never exists as a real file
		/// system file; instead, a request for the page file itself will be intercepted, and this server
		/// simply returns the content it has remembered.
		/// To manage the lifetime of the page data, we use a SimulatedPageFile object, which the Browser
		/// disposes of when it is no longer looking at that URL. Its dispose method tells this class
		/// to discard the simulated page data.
		/// To handle the need for multiple simulated page files and quickly check whether a particular
		/// url is one of them, we have a dictionary in which the urls are keys.
		/// A marker is inserted into the generated urls if the input HtmlDom wants to use original images.
		/// </summary>
		/// <param name="dom"></param>
		/// <param name="isCurrentPageContent">If this is true, the url will be inserted by JavaScript into
		/// a src attr for an IFrame. We need to account for this because un-escaped quotation marks in the
		/// URL can cause errors in JavaScript strings. Also, we want to use the same name each time
		/// for current page content, so Open Page in Browser works even after changing pages.</param>
		/// <param name="setAsCurrentPageForDebugging"></param>
		/// <param name="source">SimulatedPageFileSource enum</param>
		/// <returns></returns>
		public static SimulatedPageFile MakeSimulatedPageFileInBookFolder(HtmlDom dom, bool isCurrentPageContent = false, bool setAsCurrentPageForDebugging = false, BloomServer.SimulatedPageFileSource source = BloomServer.SimulatedPageFileSource.Normal)
		{
			var simulatedPageFileName = Path.ChangeExtension((isCurrentPageContent ? "currentPage" : Guid.NewGuid().ToString()) + SimulatedFileUrlMarker + source, ".html");
			var pathToSimulatedPageFile = simulatedPageFileName; // a default, if there is no special folder
			if (dom.BaseForRelativePaths != null)
			{
				pathToSimulatedPageFile = Path.Combine(dom.BaseForRelativePaths, simulatedPageFileName).Replace('\\', '/');
			}
			if (File.Exists(pathToSimulatedPageFile))
			{
				// Just in case someone perversely calls a book "currentPage" we will use another name.
				// (We want one that does NOT conflict with anything really in the folder.)
				// We only allow one HTML file per folder so we shouldn't need multiple attempts.
				pathToSimulatedPageFile = Path.Combine(dom.BaseForRelativePaths, "X" + simulatedPageFileName).Replace('\\', '/');
			}
			// FromLocalHost is smart about doing nothing if it is not a localhost url. In case it is, we
			// want the OriginalImageMarker (if any) after the localhost stuff.
			pathToSimulatedPageFile = pathToSimulatedPageFile.FromLocalhost();
			if (dom.UseOriginalImages)
				pathToSimulatedPageFile = OriginalImageMarker + "/" + pathToSimulatedPageFile;
			var url = pathToSimulatedPageFile.ToLocalhost();
			var key = pathToSimulatedPageFile.Replace('\\', '/');
			if (isCurrentPageContent)
			{
				// We need to UrlEncode the single and double quote characters, and the space character,
				// so they will play nicely with HTML.
				var urlPath = UrlPathString.CreateFromUnencodedString(url);
				url = urlPath.UrlEncodedForHttpPath;
			}
			if(setAsCurrentPageForDebugging)
			{
				_keyToCurrentPage = key;
			}

			// If we are creating a page thumbnail and we have videos,
			// replace them with our standard video placeholder image.
			if (source == SimulatedPageFileSource.Thumb ||
			    source == SimulatedPageFileSource.Pagelist ||
			    source == SimulatedPageFileSource.Epub)
			{
				ReplaceAnyVideoElementsWithPlaceholder(dom);
			}
			var html5String = TempFileUtils.CreateHtml5StringFromXml(dom.RawDom);
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

			return new SimulatedPageFile {Key = url};
		}

		private const string vidPlaceHolderDivContents =
			@"<img src='video-placeholder.svg' />";

		private static void ReplaceAnyVideoElementsWithPlaceholder(HtmlDom dom)
		{
			var vidNodes = dom.SafeSelectNodes("//div[contains(concat(' ', @class, ' '), ' bloom-videoContainer ')]");
			foreach (XmlNode vidNode in vidNodes)
			{
				var placeHolderNode = dom.RawDom.CreateElement("div");
				placeHolderNode.InnerXml = vidPlaceHolderDivContents;
				placeHolderNode.SetAttribute("class", "bloom-imageContainer");

				// When we get to this point and we are creating an epub, we have already generated the
				// temporary IDs needed to determine element visibility. We need to maintain the ID
				// so we don't try to look up IDs in the dom which don't exist and throw a js error.
				var vidNodeIdAttribute = vidNode.Attributes["id"];
				if (vidNodeIdAttribute != null)
				{
					var vidNodeId = vidNodeIdAttribute.Value;
					if (!string.IsNullOrEmpty(vidNodeId) && vidNodeId.StartsWith(PublishHelper.kTempIdMarker))
						placeHolderNode.SetAttribute("id", vidNodeId);
				}

				vidNode.ParentNode.ReplaceChild(placeHolderNode, vidNode);
			}
		}

		internal static void RemoveSimulatedPageFile(string key)
		{
			// There are potential race conditions where one server thread is asked to fetch a simulated page,
			// but meanwhile, some other thread disposes of it, so it can't be found. We therefore wait to dispose
			// of simulated pages until there are no busy worker threads and no queued actions.
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
				_theOneInstance._idleTasks.Enqueue(new IdleTaskQueueItem() {Id = realKey, WhatToDo = removeIt});
			}
		}

		// Every path should return false or send a response.
		// Otherwise we can get a timeout error as the browser waits for a response.
		//
		// NOTE: this method gets called on different threads!
		protected bool ProcessRequest(IRequestInfo info)
		{
			if (CurrentCollectionSettings != null && CurrentCollectionSettings.SettingsFilePath != null)
				info.DoNotCacheFolder = Path.GetDirectoryName(CurrentCollectionSettings.SettingsFilePath).Replace('\\','/');

			var localPath = GetLocalPathWithoutQuery(info);

			//enhance: something feeds back these branding logos with a weird URL, that shouldn't be.
			if (ApiHandler.IsInvalidApiCall(localPath))
				return false;

			// process request for directory index
			if (info.RawUrl.EndsWith("/") && (Directory.Exists(localPath)))
			{
				info.WriteError(403, "Directory listing denied");
				return true;
			}
			if (localPath.EndsWith("testconnection"))
			{
				info.WriteCompleteOutput("OK");
				return true;
			}

			if (ApiHandler.ProcessRequest(info, localPath))
				return true;

			// Handle image file requests.
			if (ProcessImageFileRequest(info))
				return true;

			if(localPath.Contains("CURRENTPAGE")) //useful when debugging. E.g. http://localhost:8091/bloom/CURRENTPAGE.htm will always show the page we're on.
			{
				localPath = _keyToCurrentPage;
			}

			if (localPath.Contains("writingSystemDisplayForUI.css"))
			{
				info.ContentType = "text/css";
				info.WriteCompleteOutput(CurrentCollectionSettings.GetWritingSystemDisplayForUICss());
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
				info.ContentType = "text/html";
				info.WriteCompleteOutput(content ?? "");
				return true;
			}

			if (localPath.StartsWith(OriginalImageMarker) && IsImageTypeThatCanBeDegraded(localPath))
			{
				// Path relative to simulated page file, and we want the file contents without modification.
				// (Note that the simulated page file's own URL starts with this, so it's important to check
				// for that BEFORE we do this check.)
				localPath = localPath.Substring(OriginalImageMarker.Length + 1);
				return ProcessAnyFileContent(info, localPath);
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
				return ProcessContent(info, localPath);
			}
			//Firefox debugger, looking for a source map, was prefixing in this unexpected
			//way.
			if(localPath.EndsWith("map"))
				localPath = localPath.Replace("output/browser/", "");

			return ProcessContent(info, localPath);
		}

		private bool ProcessImageFileRequest(IRequestInfo info)
		{
			if (!_useCache)
				return false;

			var imageFile = GetLocalPathWithoutQuery(info);

			// only process images
			var isSvg = imageFile.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
			if (!IsImageTypeThatCanBeDegraded(imageFile) && !isSvg)
				return false;

			imageFile = imageFile.Replace("thumbnail", "");

			var processImage = !isSvg;

			if (imageFile.StartsWith(OriginalImageMarker + "/"))
			{
				processImage = false;
				imageFile = imageFile.Substring((OriginalImageMarker + "/").Length);

				if (!RobustFile.Exists(imageFile))
				{
					// We didn't find the file here, and don't want to use the following else if or we could errantly
					// find it in the browser root. For example, this outer if (imageFile.StartsWith...) was added because
					// we were accidentally finding license.png in a template book. See BL-4290.
					return false;
				}
			}
			// This happens with the new way we are serving css files
			else if (!RobustFile.Exists(imageFile))
			{
				var fileName = Path.GetFileName(imageFile);
				var sourceDir = FileLocationUtilities.GetDirectoryDistributedWithApplication(BloomFileLocator.BrowserRoot);
				imageFile = Directory.EnumerateFiles(sourceDir, fileName, SearchOption.AllDirectories).FirstOrDefault();

				// image file not found
				if (String.IsNullOrEmpty(imageFile)) return false;

				// BL-2368: Do not process files from the BloomBrowserUI directory. These files are already in the state we
				//          want them. Running them through _cache.GetPathToResizedImage() is not necessary, and in PNG files
				//          it converts all white areas to transparent. This is resulting in icons which only contain white
				//          (because they are rendered on a dark background) becoming completely invisible.
				processImage = false;
			}

			var originalImageFile = imageFile;
			if (processImage)
			{
				// thumbnail requests have the thumbnail parameter set in the query string
				var thumb = info.GetQueryParameters()["thumbnail"] != null;
				var transparent = CurrentBook?.ImageFileShouldBeRenderedWithTransparency(imageFile);
				imageFile = _cache.GetPathToResizedImage(imageFile, thumb, transparent ?? false);

				if (String.IsNullOrEmpty(imageFile)) return false;
			}

			info.ReplyWithImage(imageFile, originalImageFile);
			return true;
		}

		protected static bool IsImageTypeThatCanBeDegraded(string path)
		{
			var extension = Path.GetExtension(path);
			if(!String.IsNullOrEmpty(extension))
				extension = extension.ToLower();
			//note, we're omitting SVG
			return (new[] { ".png", ".jpg", ".jpeg"}.Contains(extension));
		}

		static HashSet<string> _imageExtensions = new HashSet<string>(new[] { ".jpg", "jpeg", ".png", ".svg" });

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

			switch (localPath)
			{
				case "currentPageContent":
					info.ContentType = "text/html";
					info.WriteCompleteOutput(CurrentPageContent ?? "");
					return true;
				case "toolboxContent":
					info.ContentType = "text/html";
					info.WriteCompleteOutput(ToolboxContent ?? "");
					return true;
				case "availableFontNames":
					info.ContentType = "application/json";
					var list = new List<string>(Browser.NamesOfFontsThatBrowserCanRender());
					list.Sort();
					info.WriteCompleteOutput(JsonConvert.SerializeObject(new{fonts = list}));
					return true;
			}
			return ProcessAnyFileContent(info, localPath);
		}

		private bool ProcessAnyFileContent(IRequestInfo info, string localPath)
		{
			string modPath = localPath;
			string path = null;
			var urlPath = UrlPathString.CreateFromUrlEncodedString(modPath);
			var tempPath = urlPath.PathOnly.NotEncoded;
			if (RobustFile.Exists(tempPath))
				modPath = tempPath;
			try
			{
				if (localPath.Contains("favicon.ico")) //need something to pacify Chrome
					path = FileLocationUtilities.GetFileDistributedWithApplication("BloomPack.ico");

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
				if (RobustFile.Exists(possibleFullImagePath) && Path.IsPathRooted(possibleFullImagePath))
				{
					path = possibleFullImagePath;
				}
				else
				{
					// Surprisingly, this method will return localPath unmodified if it is a fully rooted path
					// (like C:\... or \\localhost\C$\...) to a file that exists. So this execution path
					// can return contents of any file that exists if the URL gives its full path...even ones that
					// are generated temp files most certainly NOT distributed with the application.
					path = FileLocationUtilities.GetFileDistributedWithApplication(BloomFileLocator.BrowserRoot, modPath);
				}
			}
			catch (ApplicationException e)
			{
				// Might be from GetFileDistributedWithApplication above, but we could be checking templates that
				// are NOT distributed with the application.
				// Otherwise ignore. Assume this means that this class/method cannot serve that request,
				// but something else may.
				if (e.Message.StartsWith("Could not locate the required file"))
				{
					// LocateFile includes userInstalledSearchPaths (e.g. a shortcut to a collection in a non-standard location)
					path = BloomFileLocator.sTheMostRecentBloomFileLocator?.LocateFile(localPath);
					if (String.IsNullOrEmpty(path))
						path = localPath;
				}
			}

			//There's probably a eventual way to make this problem go away,
			// but at the moment FF, looking for source maps to go with css, is
			// looking for those maps where we said the css was, which is in the actual
			// book folders. So instead redirect to our browser file folder.
			if (String.IsNullOrEmpty(path) || !RobustFile.Exists(path))
			{
				var startOfBookLayout = localPath.IndexOf("bookLayout");
				if (startOfBookLayout > 0)
					path = BloomFileLocator.GetBrowserFile(false, localPath.Substring(startOfBookLayout));
				var startOfBookEdit = localPath.IndexOf("bookEdit");
				if (startOfBookEdit > 0)
					path = BloomFileLocator.GetBrowserFile(false, localPath.Substring(startOfBookEdit));
			}

			if (!RobustFile.Exists(path) && localPath.StartsWith("pageChooser/") && IsImageTypeThatCanBeReturned(localPath))
			{
				// if we're in the page chooser dialog and looking for a thumbnail representing an image in a
				// template page, look for that thumbnail in the book that is the template source,
				// rather than in the folder that stores the page choose dialog HTML and code.
				var templateBook = _bookSelection.CurrentSelection.FindTemplateBook();
				if (templateBook != null)
				{
					var pathMinusPrefix = localPath.Substring("pageChooser/".Length);
					var templatePath = Path.Combine(templateBook.FolderPath, pathMinusPrefix);
					if (RobustFile.Exists(templatePath))
					{
						info.ReplyWithImage(templatePath);
						return true;
					}
					// Might be a page from a different template than the one we based this book on
					path = BloomFileLocator.sTheMostRecentBloomFileLocator.LocateFile(pathMinusPrefix);
					if (!String.IsNullOrEmpty(path))
					{
						info.ReplyWithImage(path);
						return true;
					}
				}
			}
			// Use '%25' to detect that the % in a Url encoded character (for example space encoded as %20) was encoded as %25.
			// In this example we would have %2520 in info.RawUrl and %20 in localPath instead of a space.  Note that if an
			// image has a % in the filename, like 'The other 50%', and it isn't doubly encoded, then this shouldn't be a
			// problem because we're triggering here only if the file isn't found.
			if (!RobustFile.Exists(localPath) && info.RawUrl.Contains("%25"))
			{
				// possibly doubly encoded?  decode one more time and try.  See https://silbloom.myjetbrains.com/youtrack/issue/BL-3835.
				// Some existing books have somehow acquired Url encoded coverImage data like the following:
				// <div data-book="coverImage" lang="*">
				//     The%20Moon%20and%20The%20Cap_Cover.png
				// </div>
				// This leads to data being stored doubly encoded in the program's run-time data.  The coverImage data is supposed to be
				// Html/Xml encoded (using &), not Url encoded (using %).
				path = HttpUtility.UrlDecode(localPath);
			}
			if (!RobustFile.Exists(path) && IsImageTypeThatCanBeReturned(localPath) && _bookSelection?.CurrentSelection != null)
			{
				// last resort...maybe we are in the process of renaming a book (BL-3345) and something mysteriously is still using
				// the old path. For example, I can't figure out what hangs on to the old path when an image is changed after
				// altering the main book title.
				var currentFolderPath = Path.Combine(_bookSelection.CurrentSelection.FolderPath, Path.GetFileName(localPath));
				if (RobustFile.Exists(currentFolderPath))
				{
					info.ReplyWithImage(currentFolderPath);
					return true;
				}
			}

			if (!RobustFile.Exists(path) && IsAudioFileWhichCanHaveCompressedCounterpart(path))
			{
				var possiblePublishableAudioPath = Path.ChangeExtension(path, AudioRecording.kPublishableExtension);
				if (RobustFile.Exists(possiblePublishableAudioPath))
				{
					path = possiblePublishableAudioPath;
					modPath = Path.ChangeExtension(modPath, AudioRecording.kPublishableExtension);
				}
			}

			if (!RobustFile.Exists(path) && path.Length > "/bloom/".Length)
			{
				// On developer machines, we can lose part of path earlier.  Try one more thing.
				path = info.LocalPathWithoutQuery.Substring("/bloom/".Length);
			}
			// We no longer copy this file to the book folder.  For Bloom Desktop, we get it from browser/templates/...
			// For Bloom Reader, bloom-player has its own copy.
			if (!RobustFile.Exists(path) && Path.GetFileName(path) == PublishHelper.kSimpleComprehensionQuizJs)
			{
				path = Path.Combine(BloomFileLocator.FactoryTemplateBookDirectory, "Activity", PublishHelper.kSimpleComprehensionQuizJs);
			}
			if (!RobustFile.Exists(path))
			{
				if (ShouldReportFailedRequest(info, CurrentBook?.FolderPath))
				{
					ReportMissingFile(localPath, path);
				}
				return false; // from here we head off to BloomServer.MakeReply() which now uses the same ShouldReportFailedRequest() method.
			}
			info.ContentType = GetContentType(Path.GetExtension(modPath));
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
				NonFatalProblem.Report(ModalIf.Alpha, PassiveIf.Beta, "Page expired", "Server no longer has this page in the memory: " + localPath);
			}
			else if (IsImageTypeThatCanBeReturned(localPath))
			{
				// Complain quietly about missing image files.  See http://issues.bloomlibrary.org/youtrack/issue/BL-3938.
				// The user visible message needs to be localized.  The detailed message is more developer oriented, so should stay in English.  (BL-4151)
				var userMsg = LocalizationManager.GetString("WebServer.Warning.NoImageFile", "Cannot Find Image File");
				var detailMsg = String.Format("Server could not find the image file {0}. LocalPath was {1}{2}", path, localPath, Environment.NewLine);
				NonFatalProblem.Report(ModalIf.None, PassiveIf.All, userMsg, detailMsg);
			}
			else
			{
				// The user visible message needs to be localized.  The detailed message is more developer oriented, so should stay in English.  (BL-4151)
				var userMsg = LocalizationManager.GetString("WebServer.Warning.NoFile", "Cannot Find File");
				var detailMsg = String.Format("Server could not find the file {0}. LocalPath was {1}{2}", path, localPath, Environment.NewLine);
				NonFatalProblem.Report(ModalIf.Beta, PassiveIf.All, userMsg, detailMsg);
			}
		}

		private static bool IsSimulatedFileUrl(string localPath)
		{
			var extension = Path.GetExtension(localPath);
			if(extension != null && !extension.StartsWith(".htm"))
				return false;

			// a good improvement might be to make these urls more obviously cache requests. But for now, let's just see if they are filename guids
			var filename = Path.GetFileNameWithoutExtension(localPath);
			return filename.Contains(SimulatedFileUrlMarker);
		}

		/// <summary>
		/// Requests with ?generateThumbnaiIfNecessary=true are potentially recursive in that we may have to navigate
		/// a browser to the template page in order to construct the thumbnail.
		/// </summary>
		/// <param name="context"></param>
		/// <returns></returns>
		protected bool IsRecursiveRequestContext(HttpListenerContext context)
		{
			return context.Request.QueryString["generateThumbnaiIfNecessary"] == "true";
		}

		private bool ProcessCssFile(IRequestInfo info, string incomingPath)
		{
			// BL-2219: "OriginalImages" means we're generating a pdf and want full images,
			// but it has nothing to do with css files and defeats the following 'if'
			var localPath = incomingPath.Replace(OriginalImageMarker + "/", "");
			// is this request the full path to a real file?
			if (RobustFile.Exists(localPath) && Path.IsPathRooted(localPath))
			{
				// Typically this will be files in the book directory, since the browser
				// is supplying the path.

				// currently this only applies to defaultLangStyles.css, and customCollectionStyles.css
				var cssFile = Path.GetFileName(localPath);
				if ((cssFile == "defaultLangStyles.css") || (cssFile == "customCollectionStyles.css"))
				{
					info.ContentType = "text/css";
					info.ReplyWithFileContent(localPath);
					return true;
				}
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
			// Similarly, we always want the branding.css found in this book.
			string path = "";
			var localWins = new string[] { "custombookstyles" };
			if (RobustFile.Exists(localPath) &&
			    localWins.Any(s => fileName.ToLowerInvariant().Contains(s)))
			{
				path = localPath;
			}
			else
			{
				// When developing brandings, it's convenient to just use the very-latest build
				// which will be sitting in the output/browser/branding/<branding name>/ folder,
				// rather than the one that is already in the book.
				if (fileName == "branding.css")
				{
					path = _fileLocator.GetBrandingFile(false, "branding.css");
				}
				else
				{
					path = _fileLocator.LocateFile(fileName);
				}
			}
			// if still not found, and localPath is an actual file path, use it
			// if still not found, and localPath is an actual file path, use it
			if (String.IsNullOrEmpty(path) && RobustFile.Exists(localPath))
			{
				path = localPath;
			}

			if (String.IsNullOrEmpty(path))
			{
				// it's just possible we need to add BloomBrowserUI to the path (in the case of the AddPage dialog)
				var p = FileLocationUtilities.GetFileDistributedWithApplication(true, BloomFileLocator.BrowserRoot, localPath);
				if(RobustFile.Exists(p)) path = p;
			}
			if (String.IsNullOrEmpty(path))
			{
				var p = FileLocationUtilities.GetFileDistributedWithApplication(true, BloomFileLocator.BrowserRoot, incomingPath);
				if (RobustFile.Exists(p))
					path = p;
			}


			// return false if the file was not found
			if (String.IsNullOrEmpty(path)) return false;

			info.ContentType = "text/css";
			info.ReplyWithFileContent(path);
			return true;
		}

		#region Startup

		public virtual void StartListening()
		{
			const int kStartingPort = 8089;
			const int kNumberOfPortsToTry = 10;
			bool success = false;
			const int kNumberOfPortsWeNeed = 2;//one for http, one for peakLevel webSocket

			//Note: while this will find a port for the http, it does not actually know if the accompanying
			//ports are available. It just assume they are.
			//So while it's an improvement, it's not yet as solid as we would like it
			//to be.  The ultimate solution is to run the websocket and http on the same port.
			//This could be done using this proxy thing that internally routes to different ports:
			// https://github.com/lifeemotions/websocketproxy
			// Another thing to check on is https://github.com/bryceg/Owin.WebSocket/pull/20 which
			// would give us an owin-compliant version of the fleck websocket server, and we could
			// switch to using an owin-compliant http server like NancyFx.
			for (var i=0; !success && i < kNumberOfPortsToTry; i++)
			{
				BloomServer.portForHttp = kStartingPort + (i*kNumberOfPortsWeNeed);
				success = AttemptToOpenPort();
			}

			if(!success)
			{

				ErrorReport.NotifyUserOfProblem(GetServerStartFailureMessage());
				Logger.WriteEvent("Error: Could not start up internal HTTP Server");
				Analytics.ReportException(new ApplicationException("Could not start server."));
				Application.Exit();
			}

			Logger.WriteEvent("Server will use " + ServerUrlEndingInSlash);
			_listenerThread.Start();

			for (var i = 0; i < Math.Max(Environment.ProcessorCount, 2); i++)
			{
				SpinUpAWorker();
			}

			VerifyWeAreNowListening();
		}

		/// <summary>
		/// Tries to start listening on the currently proposed server url
		/// </summary>
		private bool AttemptToOpenPort()
		{
			try
			{
				Logger.WriteMinorEvent("Attempting to start http listener on "+ ServerUrlEndingInSlash);
				_listener = new HttpListener {AuthenticationSchemes = AuthenticationSchemes.Anonymous};
				_listener.Prefixes.Add(ServerUrlEndingInSlash);
				_listener.Start();
				return true;
			}
			catch(HttpListenerException error)
			{
				Logger.WriteEvent("Here, file not found is actually what you get if the port is in use:" + error.Message);
				if (!Program.RunningUnitTests)
					NonFatalProblem.Report(ModalIf.None,PassiveIf.Alpha, "Could not open " + ServerUrlEndingInSlash, "Could not start server on that port", error);
				try
				{
					if(_listener != null)
					{
						//_listener.Stop();  this will always throw if we failed to start, so skip it and go to the close:
						_listener.Close();
					}
				}
				catch(Exception)
				{
					//that's ok, we're just trying to clean up
				}
				finally
				{
					_listener = null;
				}
				return false;
			}
		}

		private static void VerifyWeAreNowListening()
		{
			try
			{
				var x = new WebClientWithTimeout {Timeout = 3000};

				if("OK" != x.DownloadString(ServerUrlWithBloomPrefixEndingInSlash + "testconnection"))
				{
					throw new ApplicationException(GetServerStartFailureMessage());
				}
			}
			catch(Exception error)
			{
				ErrorReport.NotifyUserOfProblem(error,GetServerStartFailureMessage());
				Application.Exit();
			}
		}

		private static string GetServerStartFailureMessage()
		{
			var zoneAlarm = false;
			if(Platform.IsWindows)
			{
				zoneAlarm =
					Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
					                              "CheckPoint/ZoneAlarm")) ||
					         Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
					                              "CheckPoint/ZoneAlarm"));

				if(!zoneAlarm)
				{
					try
					{
						zoneAlarm =
							Process.GetProcesses().Any(p => p.Modules.Cast<ProcessModule>().Any(m => m.ModuleName.Contains("ZoneAlarm")));
					}
					catch(Exception error)
					{
						Logger.WriteError("GetServerStartFailureMessage() was unable to check for a running ZoneAlarm Process (BL-4055, Bl-4032, etc.)", error);
					}
				}
			}
			if(zoneAlarm)
			{
				return LocalizationManager.GetString("Errors.ZoneAlarm",
				                                     "Bloom cannot start properly, and this symptom has been observed on machines with ZoneAlarm installed. Note: disabling ZoneAlarm does not help. Nor does restarting with it turned off. Something about the installation of ZoneAlarm causes the problem, and so far only uninstalling ZoneAlarm has been shown to fix the problem.");
			}

			return LocalizationManager.GetString("Errors.CannotConnectToBloomServer",
			                                     "Bloom was unable to start its own HTTP listener that it uses to talk to its embedded Firefox browser. If this happens even if you just restarted your computer, then ask someone to investigate if you have an aggressive firewall product installed, which may need to be uninstalled before you can use Bloom.");
		}

		// After the initial startup, this should only be called inside a lock(_queue),
		// to avoid race conditions modifying the _workers collection.
		private void SpinUpAWorker()
		{
			var thread = new Thread(RequestProcessorLoop);
			thread.Name = "Server Worker Thread " + _workers.Count;
			_workers.Add(thread);
			_workers.Last().Start();
		}

		#endregion

		/// <summary>
		/// The _listenerThread runs this method, and exits when the _stop event is raised
		/// </summary>
		private void EnqueueIncomingRequests()
		{
			while (_listener.IsListening)
			{
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
					if (rawurl.Contains("accessibilityCheck") &&
					    (rawurl.Contains(".png") || rawurl.Contains(".jpg") || rawurl.Contains(".svg")))
					{
						var r = new RequestInfo(new BloomHttpListenerContext(context));
						r.WriteError(404);
						return;
					}

					// set lower priority for thumbnails in order to have less impact on the UI thread
					if (rawurl.Contains("thumbnail=1"))
						Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;

					MakeReply(new RequestInfo(new BloomHttpListenerContext(context)));
				}
				catch (HttpListenerException e)
				{
					// http://stackoverflow.com/questions/4801868/c-sharp-problem-with-httplistener
					Logger.WriteEvent(
						"At BloomServer: ListenerCallback(): HttpListenerException, which may indicate that the caller closed the connection before we could reply. msg=" +
						e.Message);
					Logger.WriteEvent("At BloomServer: ListenerCallback(): url=" + rawurl);
				}
				catch (Exception error)
				{
#if __MonoCS__
					// Something keeps closing the socket connection prematurely on Linux/Mono.  But I'm not sure
					// it's an important failure since the program appears to work okay, so we'll ignore the error.
					if (error is IOException && error.InnerException != null && error.InnerException is System.Net.Sockets.SocketException)
					{
						Logger.WriteEvent("At BloomServer: ListenerCallback(): IOException/SocketException, which may indicate that the caller closed the connection before we could reply. msg=" + error.Message + " / " + error.InnerException.Message);
						Logger.WriteEvent("At BloomServer: ListenerCallback(): url=" + rawurl);
					}
					else
#endif
					{
						Logger.WriteEvent("At BloomServer: ListenerCallback(): msg=" + error.Message);
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
		/// <param name="info"></param>
		internal void MakeReply(IRequestInfo info)
		{
			if (!ProcessRequest(info))
			{
				if (ShouldReportFailedRequest(info))
					ReportMissingFile(info);
				info.WriteError(404);	// Informing the caller is always needed.
			}
#if MEMORYCHECK
			// Check memory for the benefit of developers.  (Also see all requests as a side benefit.)
			var debugMsg = String.Format("after BloomServer.ProcessRequest(\"{0}\")", info.RawUrl);
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
		protected bool ShouldReportFailedRequest(IRequestInfo info, string currentBookFolderPath = null)
		{
			// images with src derived from Branding API img elements get this marker
			// in XMatterHelper.CleanupBrandingImages() to prevent spurious reports of
			// images that are intentionally optional.
			var hasOptionalQueryParam = info.GetQueryParameters().Get("optional") == "true";
			if (hasOptionalQueryParam)
				return false;

			var localPath = GetLocalPathWithoutQuery(info);
			// We don't need even a toast for missing images in the book folder. That's the user's problem and should be adequately
			// documented by the browser message saying the file is missing.
			if (currentBookFolderPath != null && localPath.StartsWith(currentBookFolderPath.Replace("\\", "/")))
				return false;

			// If it's in a deleted book (typically we're still trying to update the thumbnail of a book we just deleted),
			// we definitely don't want to bother the user.
			// (Case for CurrentCollectionSettings null is needed for unit tests.)
			var collectionPath = CurrentCollectionSettings?.FolderPath;
			if (currentBookFolderPath == null && !Directory.Exists(Path.GetDirectoryName(localPath))
			                                  && collectionPath != null
			                                  && localPath.StartsWith(collectionPath.Replace("\\", "/")))
			{
				return false;
			}

			var stuffToIgnore = new[] {
				// browser/debugger stuff
				"favicon.ico", ".map",
				// Audio files may well be missing because we look for them as soon
				// as we define an audio ID, but they wont' exist until we record something.
				"/audio/",
				// PageTemplatesApi creates a path containing this for a missing template.
				// it gets reported inside the page chooser dialog.
				"missingpagetemplate",
				// Branding image files are expected to be missing in the normal case.  Only organizations that care about branding would have these images.
				"/branding/image",
				// This is readium stuff that we don't ship with, because they are needed by the original reader to support display and implementation
				// of controls we hide for things like adding books to collection, displaying the collection, playing audio (that last we might want back one day).
				EpubMaker.kEPUBExportFolder.ToLowerInvariant(),
				// bloom-player always asks for questions.json for every book.
				// Being only for quiz pages, not every book has it, so we don't want spurious error reports.
				BloomReaderFileMaker.kQuestionFileName.ToLowerInvariant()
			};
			return !stuffToIgnore.Any(s => localPath.ToLowerInvariant().Contains(s));
		}

		protected internal static string GetLocalPathWithoutQuery(IRequestInfo info)
		{
			return GetLocalPathWithoutQuery(info.LocalPathWithoutQuery);
		}

		private static string GetLocalPathWithoutQuery(string localPath)
		{
			if (localPath.StartsWith(BloomUrlPrefix))
			{
				localPath = localPath.Substring(BloomUrlPrefix.Length);
				#if __MonoCS__
				if (localPath.StartsWith("tmp/ePUB"))
					localPath = "/" + localPath;	// restore leading slash for full path
				#endif
			}
			// and if the file is using localhost:1234/foo.js, at this point it will say "/foo.js", so let's strip off that leading slash
			else if (localPath.StartsWith("/"))
			{
				localPath = localPath.Substring(1);
			}
			if (localPath.Contains("?") && !RobustFile.Exists(localPath))
			{
				var idx = localPath.LastIndexOf("?", StringComparison.Ordinal);
				return localPath.Substring(0, idx);
			}
			return localPath;
		}

		public static string GetContentType(string extension)
		{
			switch (extension)
			{
			case ".css": return "text/css";
			case ".gif": return "image/gif";
			case ".htm":
			case ".html": return "text/html";
			case ".jpg":
			case ".jpeg": return "image/jpeg";
			case ".js": return "application/x-javascript";
			case ".png": return "image/png";
			case ".pdf": return "application/pdf";
			case ".txt": return "text/plain";
			case ".svg": return "image/svg+xml";
			case ".mp3": return "audio/mpeg";
			case ".ogg": return "audio/ogg";
			case ".woff": return "font/woff";
			case ".woff2": return "font/woff2";
			case ".xml": return "application/xml";
			case ".xhtml": return "application/xhtml+xml";
			default: return "application/octet-stream";
			}
		}

#region Disposable stuff

		private bool IsDisposed { get; set; }

		public void Dispose()
		{
			//Stop();
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool fDisposing)
		{
			Debug.WriteLineIf(!fDisposing, "****** Missing Dispose() call for " + GetType() + ". *******");
			if (fDisposing && !IsDisposed)
			{
				// dispose managed and unmanaged objects
				try
				{
					if (_listener != null)
					{
						//prompted by the mysterious BL 273, Crash while closing down the imageserver
						Guard.AgainstNull(_listenerThread, "_listenerThread");
						//prompted by the mysterious BL 273, Crash while closing down the imageserver
						Guard.AgainstNull(_stop, "_stop");

						// tell _listenerThread and the worker threads they should stop
						_stop.Set();

						var secondsToWait = 2.0;
						// wait for _listenerThread to stop
						if (_listenerThread.ThreadState != ThreadState.Unstarted)
						{
							if (!_listenerThread.Join((int)(secondsToWait * 1000)))
							{
								Logger.WriteError($"Could not kill a listener thread after waiting {secondsToWait} seconds.", new ApplicationException());
							}
						}


						// wait for each worker thread to stop
						foreach (var worker in _workers.Where(worker => (worker != null) && (worker.ThreadState != ThreadState.Unstarted)))
						{
							if (!worker.Join((int)(secondsToWait * 1000)))
							{
								Logger.WriteError("Could not kill a worker thread after waiting 2 seconds.", new ApplicationException());
								secondsToWait = secondsToWait / 2.0; // if one thing is broken, likely other are, so get less patient
							}
						}

						// stop listening for incoming http requests
						Debug.Assert(_listener.IsListening);
						if(_listener.IsListening)
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
					throw;
					#else             //just quietly report this
										DesktopAnalytics.Analytics.ReportException(e);
#endif
				}
			}
			IsDisposed = true;
		}

#endregion
	}

	class IdleTaskQueueItem
	{
		// The actual thing to do when idle
		// (currently typically to delete an obsolete simulated page)
		public Action WhatToDo;
		// An ID which can be used to identify obsolete idle tasks
		// (currently typically the Key of a simulated page)
		public string Id;
		// True if the idle task should not be done after all;
		// we need this because there is no API to simply remove an
		// item from a Queue.
		// (currently set when we add a new simulated page with the same
		// key as one we had queued for deletion but not yet deleted)
		public bool Cancelled;
	}
}
