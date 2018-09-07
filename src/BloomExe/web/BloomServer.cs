// Copyright (c) 2014-2017 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Bloom.Book;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using Bloom.ImageProcessing;
using BloomTemp;
using L10NSharp;
using SIL.IO;
using Bloom.Collection;
using Bloom.Publish.Epub;
using Bloom.Workspace;
using Newtonsoft.Json;
using SIL.Reporting;
using SIL.Extensions;
using Bloom.Properties;

namespace Bloom.Api
{
	/// <summary>
	/// A local http server that can serve (low-res) images plus other files.
	/// </summary>
	/// <remarks>geckofx makes concurrent requests of URLs which this class handles. This means
	/// that the methods of this class get called on different threads, so it has to be
	/// thread-safe.</remarks>
	public class BloomServer : ServerBase
	{
		public const string OriginalImageMarker = "OriginalImages"; // Inserted into paths to suppress image processing (for simulated pages and PDF creation)
		private RuntimeImageProcessor _cache;
		private bool _useCache;

		private const string SimulatedFileUrlMarker = "-memsim-";
		private FileSystemWatcher _sampleTextsWatcher;
		private bool _sampleTextsChanged = true;
		static Dictionary<string, string> _urlToSimulatedPageContent = new Dictionary<string, string>(); // see comment on MakeSimulatedPageFileInBookFolder
		private BloomFileLocator _fileLocator;
		private readonly BookThumbNailer _thumbNailer;
		private readonly BookSelection _bookSelection;
		private readonly ProjectContext _projectContext;

		// This dictionary ties API endpoints to functions that handle the requests.
		private Dictionary<string, EndpointRegistration> _endpointRegistrations = new Dictionary<string, EndpointRegistration>();

		public CollectionSettings CurrentCollectionSettings { get; set; }

		// This is useful for debugging.
		public static Dictionary<string, string> SimulatedPageContent => _urlToSimulatedPageContent;

		/// <summary>
		/// This is only used in a few special cases where we need one to pass as an argument but it won't be fully used.
		/// </summary>
		internal BloomServer(BookSelection bookSelection) : this( new RuntimeImageProcessor(new BookRenamedEvent()), null, bookSelection)
		{ }

		public BloomServer(RuntimeImageProcessor cache, BookThumbNailer thumbNailer, BookSelection bookSelection,  BloomFileLocator fileLocator = null)
		{
			_thumbNailer = thumbNailer;
			_bookSelection = bookSelection;
			_fileLocator = fileLocator;
			_cache = cache;
			_useCache = Settings.Default.ImageHandler != "off";
		}


		/// <summary>
		/// Get called when a client (i.e. javascript) does an HTTP api call
		/// </summary>
		/// <param name="pattern">Simple string or regex to match APIs that this can handle. This must match what comes after the ".../api/" of the URL</param>
		/// <param name="handler">The method to call</param>
		/// <param name="handleOnUiThread">If true, the current thread will suspend until the UI thread can be used to call the method.
		/// This deliberately no longer has a default. It's something that should be thought about.
		/// Making it true can kill performance if you don't need it (BL-3452), and complicates exception handling and problem reporting (BL-4679).
		/// There's also danger of deadlock if something in the UI thread is somehow waiting for this request to complete.
		/// But, beware of race conditions or anything that manipulates UI controls if you make it false.</param>
		/// <param name="requiresSync">True if the handler wants the server to ensure no other thread is doing an api
		/// call while this one is running. This is our default behavior, ensuring that no API request can interfere with any
		/// other in any unexpected way...essentially all Bloom's data is safe from race conditions arising from
		/// server threads manipulating things on background threads. However, it makes it impossible for a new
		/// api call to interrupt a previous one. For example, when one api call is creating an epub preview
		/// and we get a new one saying we need to abort that (because one of the property buttons has changed),
		/// the epub that is being generated is obsolete and we want the new api call to go ahead so it can set a flag 
		/// to abort the one in progress. To avoid race conditions, api calls that set requiresSync false should be kept small
		/// and simple and be very careful about touching objects that other API calls might interact with.</param>
		public void RegisterEndpointHandler(string pattern, EndpointHandler handler, bool handleOnUiThread, bool requiresSync = true)
		{
			_endpointRegistrations[pattern.ToLowerInvariant().Trim(new char[] {'/'})] = new EndpointRegistration()
			{
				Handler = handler,
				HandleOnUIThread = handleOnUiThread,
				RequiresSync = requiresSync
			};
		}

		/// <summary>
		/// Handle simple boolean reads/writes
		/// </summary>
		public void RegisterBooleanEndpointHandler(string pattern, Func<ApiRequest, bool> readAction, Action<ApiRequest, bool> writeAction,
			bool handleOnUiThread, bool requiresSync = true)
		{
			RegisterEndpointHandler(pattern, request =>
			{
				if (request.HttpMethod == HttpMethods.Get)
				{
					request.ReplyWithBoolean(readAction(request));
				}
				else // post
				{
					writeAction(request, request.RequiredPostBooleanAsJson());
					request.PostSucceeded();
				}
			}, handleOnUiThread, requiresSync);
		}

		/// <summary>
		/// Handle enum reads/writes
		/// </summary>
		public void RegisterEnumEndpointHandler<T>(string pattern, Func<ApiRequest, T> readAction, Action<ApiRequest, T> writeAction,
			bool handleOnUiThread, bool requiresSync = true)
		{
			Debug.Assert(typeof(T).IsEnum, "Type passed to RegisterEnumEndpointHandler is not an Enum.");
			RegisterEndpointHandler(pattern, request =>
			{
				if (request.HttpMethod == HttpMethods.Get)
				{
					request.ReplyWithEnum(readAction(request));
				}
				else // post
				{
					writeAction(request, request.RequiredPostEnumAsJson<T>());
					request.PostSucceeded();
				}
			}, handleOnUiThread, requiresSync);
		}

		// We use two different locks to synchronize access to the methods of this class.
		// This allows certain methods to run concurrently.

		// used to synchronize access to I18N methods
		private object I18NLock = new object();
		// used to synchronize access to various other methods
		private object SyncObj = new object();
		// Special lock for making thumbnails. See discussion at the one point of usage.
		private object ThumbnailSyncObj = new object();
		private static string _keyToCurrentPage;

		public string CurrentPageContent { get; set; }
		public string ToolboxContent { get; set; }

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
		/// <returns></returns>
		public static SimulatedPageFile MakeSimulatedPageFileInBookFolder(HtmlDom dom, bool isCurrentPageContent = false, bool setAsCurrentPageForDebugging = false, string source="")
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
			var html5String = TempFileUtils.CreateHtml5StringFromXml(dom.RawDom);
			lock (_urlToSimulatedPageContent)
			{
				_urlToSimulatedPageContent[key] = html5String;
			}
			return new SimulatedPageFile() {Key = url};
		}

		internal static void RemoveSimulatedPageFile(string key)
		{
			if (key.StartsWith("file://"))
			{
				var uri = new Uri(key);
				RobustFile.Delete(uri.LocalPath);
				return;
			}
			lock (_urlToSimulatedPageContent)
			{
				_urlToSimulatedPageContent.Remove(key.FromLocalhost());
			}
		}

		// Every path should return false or send a response.
		// Otherwise we can get a timeout error as the browser waits for a response.
		//
		// NOTE: this method gets called on different threads!
		protected override bool ProcessRequest(IRequestInfo info)
		{
			if (CurrentCollectionSettings != null && CurrentCollectionSettings.SettingsFilePath != null)
				info.DoNotCacheFolder = Path.GetDirectoryName(CurrentCollectionSettings.SettingsFilePath).Replace('\\','/');

			var localPath = GetLocalPathWithoutQuery(info);

			//enhance: something feeds back these branding logos with a weird URL, that shouldn't be.
			if(localPath.IndexOf("api/branding") > 20) // this 20 is just arbitrary... the point is, if it doesn't start with api/branding, it is bogus
			{
				return false;
			}

			if (localPath.ToLower().StartsWith("api/"))
			{
				var endpoint = localPath.Substring(3).ToLowerInvariant().Trim(new char[] {'/'});
				foreach (var pair in _endpointRegistrations.Where(pair =>
						Regex.Match(endpoint,
							"^" + //must match the beginning
							pair.Key.ToLower()
						).Success))
				{
					if (pair.Value.RequiresSync)
					{
						// A single synchronization object won't do, because when processing a request to create a thumbnail,
						// we have to load the HTML page the thumbnail is based on. If the page content somehow includes
						// an api request (api/branding/image is one example), that request will deadlock if the
						// api/pageTemplateThumbnail request already has the main lock.
						// To the best of my knowledge, there's no shared data between the thumbnailing process and any
						// other api requests, so it seems safe to have one lock that prevents working on multiple
						// thumbnails at the same time, and one that prevents working on other api requests at the same time.
						var syncOn = SyncObj;
						if (localPath.ToLowerInvariant().StartsWith("api/pagetemplatethumbnail"))
							syncOn = ThumbnailSyncObj;
						lock (syncOn)
						{
							return ApiRequest.Handle(pair.Value, info, CurrentCollectionSettings, _bookSelection.CurrentSelection);
						}
					}
					else
					{
						// Up to api's that request no sync to do things right!
						return ApiRequest.Handle(pair.Value, info, CurrentCollectionSettings, _bookSelection.CurrentSelection);
					}
				}
			}

			//OK, no more obvious simple API requests, dive into the rat's nest of other possibilities
			if (base.ProcessRequest(info))
				return true;
			
			// Handle image file requests.
			if (ProcessImageFileRequest(info))
				return true;

			if(localPath.Contains("CURRENTPAGE")) //useful when debugging. E.g. http://localhost:8091/bloom/CURRENTPAGE.htm will always show the page we're on.
			{
				localPath = _keyToCurrentPage;
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

			if (localPath.StartsWith("i18n/", StringComparison.InvariantCulture))
			{
				if (ProcessI18N(localPath, info))
					return true;
			}
			else if (localPath.StartsWith("directoryWatcher/", StringComparison.InvariantCulture))
				return ProcessDirectoryWatcher(info);
			else if (localPath.StartsWith("localhost/", StringComparison.InvariantCulture))
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
				if (string.IsNullOrEmpty(imageFile)) return false;

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
				imageFile = _cache.GetPathToResizedImage(imageFile, thumb);

				if (string.IsNullOrEmpty(imageFile)) return false;
			}

			info.ReplyWithImage(imageFile, originalImageFile);
			return true;
		}

		protected static bool IsImageTypeThatCanBeDegraded(string path)
		{
			var extension = Path.GetExtension(path);
			if(!string.IsNullOrEmpty(extension))
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
			var drive = char.ToUpper(pathArray[0]);
			if (pathArray[1] == '$' && pathArray[2] == '/' && drive >= 'A' && drive <= 'Z')
				pathArray[1] = ':';
			return new String(pathArray);
#endif
		}

		private bool ProcessI18N(string localPath, IRequestInfo info)
		{
			lock (I18NLock)
			{
				return I18NHandler.HandleRequest(localPath, info, CurrentCollectionSettings);
			}
		}

		private bool ProcessDirectoryWatcher(IRequestInfo info)
		{
			// thread synchronization is done in CheckForSampleTextChanges.
			var dirName = info.GetPostDataWhenFormEncoded()["dir"];
			if (dirName == "Sample Texts")
			{
				if (CheckForSampleTextChanges(info))
					return true;
			}
			return false;
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
			var tempPath = urlPath.NotEncoded;
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
				if (localPath.StartsWith(OriginalImageMarker + "/"))
					possibleFullImagePath = localPath.Substring(15);
				if(RobustFile.Exists(possibleFullImagePath) && Path.IsPathRooted(possibleFullImagePath))
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
			if (string.IsNullOrEmpty(path) || !RobustFile.Exists(path))
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
					if (!string.IsNullOrEmpty(path))
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
				path = System.Web.HttpUtility.UrlDecode(localPath);
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

			if (!RobustFile.Exists(path))
			{
				// On developer machines, we can lose part of path earlier.  Try one more thing.
				path = info.LocalPathWithoutQuery.Substring(7); // skip leading "/bloom/");
			}
			if (!RobustFile.Exists(path))
			{
				if (ShouldReportFailedRequest(info, CurrentBook?.FolderPath))
				{
					ReportMissingFile(localPath, path);
				}
				return false; // from here we head off to ServerBase.MakeReply() which now uses the same ShouldReportFailedRequest() method.
			}
			info.ContentType = GetContentType(Path.GetExtension(modPath));
			info.ReplyWithFileContent(path);
			return true;
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
				var detailMsg = String.Format("Server could not find the image file {0}. LocalPath was {1}{2}", path, localPath, System.Environment.NewLine);
				NonFatalProblem.Report(ModalIf.None, PassiveIf.All, userMsg, detailMsg);
			}
			else
			{
				// The user visible message needs to be localized.  The detailed message is more developer oriented, so should stay in English.  (BL-4151)
				var userMsg = LocalizationManager.GetString("WebServer.Warning.NoFile", "Cannot Find File");
				var detailMsg = String.Format("Server could not find the file {0}. LocalPath was {1}{2}", path, localPath, System.Environment.NewLine);
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
		protected override bool IsRecursiveRequestContext(HttpListenerContext context)
		{
			return base.IsRecursiveRequestContext(context) || context.Request.QueryString["generateThumbnaiIfNecessary"] == "true";
		}

		private bool ProcessCssFile(IRequestInfo info, string incomingPath)
		{
			// BL-2219: "OriginalImages" means we're generating a pdf and want full images,
			// but it has nothing to do with css files and defeats the following 'if'
			var localPath = incomingPath.Replace(OriginalImageMarker + "/", "");
			// is this request the full path to a real file?
			if (RobustFile.Exists(localPath) && Path.IsPathRooted(localPath))
			{
				// Typically this will be files in the book or collection directory, since the browser
				// is supplying the path.

				// currently this only applies to settingsCollectionStyles.css, and customCollectionStyles.css
				var cssFile = Path.GetFileName(localPath);
				if ((cssFile == "settingsCollectionStyles.css") || (cssFile == "customCollectionStyles.css"))
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
			var path = fileName.ToLowerInvariant().Contains("custombookstyles") && RobustFile.Exists(localPath) ? localPath 
				: _fileLocator.LocateFile(fileName);

			// if still not found, and localPath is an actual file path, use it
			if (string.IsNullOrEmpty(path) && RobustFile.Exists(localPath)) path = localPath;

			if (string.IsNullOrEmpty(path))
			{
				// it's just possible we need to add BloomBrowserUI to the path (in the case of the AddPage dialog)
				var p = FileLocationUtilities.GetFileDistributedWithApplication(true, BloomFileLocator.BrowserRoot, localPath);
				if(RobustFile.Exists(p)) path = p;
			}
			if (string.IsNullOrEmpty(path))
			{
				var p = FileLocationUtilities.GetFileDistributedWithApplication(true, BloomFileLocator.BrowserRoot, incomingPath);
				if (RobustFile.Exists(p))
					path = p;
			}


			// return false if the file was not found
			if (string.IsNullOrEmpty(path)) return false;

			info.ContentType = "text/css";
			info.ReplyWithFileContent(path);
			return true;
		}


		private bool CheckForSampleTextChanges(IRequestInfo info)
		{
			lock (SyncObj)
			{
				if (_sampleTextsWatcher == null)
				{
					if (string.IsNullOrEmpty(CurrentCollectionSettings?.SettingsFilePath))
					{
						// We've had cases (BL-4744) where this is apparently called before CurrentCollectionSettings is
						// established. I'm not sure how this can happen but if we haven't even established a current collection
						// yet I think it's pretty safe to say its sample texts haven't changed since we last read them.
						info.ContentType = "text/plain";
						info.WriteCompleteOutput("no");
						return true;
					}
					var path = Path.Combine(Path.GetDirectoryName(CurrentCollectionSettings.SettingsFilePath), "Sample Texts");
					if (!Directory.Exists(path))
						Directory.CreateDirectory(path);

					_sampleTextsWatcher = new FileSystemWatcher { Path = path };
					_sampleTextsWatcher.Created += SampleTextsOnChange;
					_sampleTextsWatcher.Changed += SampleTextsOnChange;
					_sampleTextsWatcher.Renamed += SampleTextsOnChange;
					_sampleTextsWatcher.Deleted += SampleTextsOnChange;
					_sampleTextsWatcher.EnableRaisingEvents = true;
				}
			}

			lock (_sampleTextsWatcher)
			{
				var hasChanged = _sampleTextsChanged;

				// Reset the changed flag.
				// NOTE: we are only resetting the flag if it was "true" when we checked in case the FileSystemWatcher detects a change
				// after we check the flag but we reset it to false before we check again.
				if (hasChanged)
					_sampleTextsChanged = false;

				info.ContentType = "text/plain";
				info.WriteCompleteOutput(hasChanged ? "yes" : "no");

				return true;
			}
		}

		private void SampleTextsOnChange(object sender, FileSystemEventArgs fileSystemEventArgs)
		{
			lock (_sampleTextsWatcher)
			{
				_sampleTextsChanged = true;
			}
		}

		protected override void Dispose(bool fDisposing)
		{
			if (fDisposing)
			{
				if (_sampleTextsWatcher != null)
				{
					_sampleTextsWatcher.EnableRaisingEvents = false;
					_sampleTextsWatcher.Dispose();
					_sampleTextsWatcher = null;
				}
				if (_cache != null)
				{
					_cache.Dispose();
					_cache = null;
			}
			}

			base.Dispose(fDisposing);
		}
	}
}
