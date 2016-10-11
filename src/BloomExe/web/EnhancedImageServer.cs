// Copyright (c) 2014-2015 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Bloom.Book;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using Bloom.ImageProcessing;
using BloomTemp;
using L10NSharp;
using Microsoft.Win32;
using SIL.IO;
using Bloom.Collection;
using Bloom.Publish;
using Newtonsoft.Json;
using SIL.Reporting;
using SIL.Extensions;

namespace Bloom.Api
{
	/// <summary>
	/// A local http server that can serve (low-res) images plus other files.
	/// </summary>
	/// <remarks>geckofx makes concurrent requests of URLs which this class handles. This means
	/// that the methods of this class get called on different threads, so it has to be
	/// thread-safe.</remarks>
	public class EnhancedImageServer: ImageServer
	{
		private const string OriginalImageMarker = "OriginalImages"; // Inserted into simulated page urls to suppress image processing
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

		/// <summary>
		/// This is only used in a few special cases where we need one to pass as an argument but it won't be fully used.
		/// </summary>
		internal EnhancedImageServer(BookSelection bookSelection) : this( new RuntimeImageProcessor(new BookRenamedEvent()), null, bookSelection)
		{ }

		public EnhancedImageServer(RuntimeImageProcessor cache, BookThumbNailer thumbNailer, BookSelection bookSelection,  BloomFileLocator fileLocator = null) : base(cache)
		{
			_thumbNailer = thumbNailer;
			_bookSelection = bookSelection;
			_fileLocator = fileLocator;
		}


		/// <summary>
		/// Get called when a client (i.e. javascript) does an HTTP api call
		/// </summary>
		/// <param name="pattern">Simple string or regex to match APIs that this can handle. This must match what comes after the ".../api/" of the URL</param>
		/// <param name="handler">The method to call</param>
		/// <param name="handleOnUiThread">For safety, this defaults to true, but that can kill performance if you don't need it (BL-3452) </param>
		public void RegisterEndpointHandler(string pattern, EndpointHandler handler, bool handleOnUiThread = true)
		{
			_endpointRegistrations[pattern.Trim(new char[] {'/'})] = new EndpointRegistration()
			{
				Handler = handler,
				HandleOnUIThread = handleOnUiThread
			};
		}

		// We use two different locks to synchronize access to the methods of this class.
		// This allows certain methods to run concurrently.

		// used to synchronize access to I18N methods
		private object I18NLock = new object();
		// used to synchronize access to various other methods
		private object SyncObj = new object();
		private static string _keyToCurrentPage;

		public string CurrentPageContent { get; set; }
		public string ToolboxContent { get; set; }
		public bool AuthorMode { get; set; }

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
		/// <param name="escapeUrlAsNeededForSrcAttribute">If this is true, the url will be inserted by JavaScript into
		/// a src attr for an IFrame. We need to account for this because un-escaped quotation marks in the
		/// URL can cause errors in JavaScript strings.</param>
		/// <returns></returns>
		public static SimulatedPageFile MakeSimulatedPageFileInBookFolder(HtmlDom dom, bool escapeUrlAsNeededForSrcAttribute = false, bool setAsCurrentPageForDebugging = false)
		{
			var simulatedPageFileName = Path.ChangeExtension(Guid.NewGuid().ToString(), ".htm");
			var pathToSimulatedPageFile = simulatedPageFileName; // a default, if there is no special folder
			if (dom.BaseForRelativePaths != null)
			{
				pathToSimulatedPageFile = Path.Combine(dom.BaseForRelativePaths, simulatedPageFileName).Replace('\\', '/');
			}
			// FromLocalHost is smart about doing nothing if it is not a localhost url. In case it is, we
			// want the OriginalImageMarker (if any) after the localhost stuff.
			pathToSimulatedPageFile = pathToSimulatedPageFile.FromLocalhost();
			if (dom.UseOriginalImages)
				pathToSimulatedPageFile = OriginalImageMarker + "/" + pathToSimulatedPageFile;
			var url = pathToSimulatedPageFile.ToLocalhost();
			var key = pathToSimulatedPageFile.Replace('\\', '/');
			if (escapeUrlAsNeededForSrcAttribute)
			{
				// We need to UrlEncode the single and double quote characters so they will play nicely with JavaScript. 
				url = EscapeUrlQuotes(url);
				// When JavaScript inserts our path into the html it replaces the three magic html characters with these substitutes.
				// We need to modify our key so that when the JavaScript comes looking for the page its modified url will
				// generate the right key.
				key = SimulateJavaScriptHandlingOfHtml(key);
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

		/// <summary>
		/// When JavaScript inserts a url into an html document, it replaces the three magic html characters
		/// with these substitutes.
		/// </summary>
		/// <remarks>Also used by PretendRequestInfo for testing</remarks>
		public static string SimulateJavaScriptHandlingOfHtml(string url)
		{
			return url.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
		}

		private static string EscapeUrlQuotes(string originalUrl)
		{
			return originalUrl.Replace("'", "%27").Replace("\"", "%22");
		}

		private static string UnescapeUrlQuotes(string escapedUrl)
		{
			return escapedUrl.Replace("%27", "'").Replace("%22", "\"");
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
					lock(SyncObj)
					{
						return ApiRequest.Handle(pair.Value, info, CurrentCollectionSettings, _bookSelection.CurrentSelection);
					}
				}
			}

			//OK, no more obvious simple API requests, dive into the rat's nest of other possibilities
			if (base.ProcessRequest(info))
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

			if (localPath.StartsWith("error", StringComparison.InvariantCulture))
			{
				ProcessError(info);
				return true;
			}
			else if (localPath.StartsWith("i18n/", StringComparison.InvariantCulture))
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
				localPath = BloomFileLocator.GetBrowserFile("jquery.min.js");
			}
			//Firefox debugger, looking for a source map, was prefixing in this unexpected 
			//way.
			localPath = localPath.Replace("output/browser/", "");

			return ProcessContent(info, localPath);
		}

		/// <summary>
		/// Adjust the 'localPath' obtained from a request in a platform-dependent way to a path
		/// that can actually be used to retrieve a file (or test for its existence).
		/// </summary>
		/// <param name="localPath"></param>
		/// <returns></returns>
		private static string LocalHostPathToFilePath(string localPath)
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

		private static string AdjustPossibleLocalHostPathToFilePath(string path)
		{
			if (!path.StartsWith("localhost/", StringComparison.InvariantCulture))
				return path;
			return LocalHostPathToFilePath(path);
		}

		private static void ProcessError(IRequestInfo info)
		{
			// pop-up the error messages if a debugger is attached or an environment variable is set
			var popUpErrors = Debugger.IsAttached || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DEBUG_BLOOM"));

			var post = info.GetPostDataWhenFormEncoded();

			// log the error message
			var errorMsg = post["message"] + Environment.NewLine + "File: " + post["url"].FromLocalhost()
				+ Environment.NewLine + "Line: " + post["line"] + " Column: " + post["column"] + Environment.NewLine;

			Logger.WriteMinorEvent(errorMsg);
			Console.Out.WriteLine(errorMsg);

			if (popUpErrors)
				Shell.DisplayProblemToUser(errorMsg);
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
				case "authorMode":
					info.ContentType = "text/plain";
					info.WriteCompleteOutput(AuthorMode ? "true" : "false");
					return true;
				case "topics":
					return GetTopicList(info);
			}
			return ProcessAnyFileContent(info, localPath);
		}

		private static bool GetTopicList(IRequestInfo info)
		{
			var keyToLocalizedTopicDictionary = new Dictionary<string, string>();
			foreach (var topic in BookInfo.TopicsKeys)
			{
				var localized = LocalizationManager.GetDynamicString("Bloom", "Topics." + topic, topic,
					@"shows in the topics chooser in the edit tab");
				keyToLocalizedTopicDictionary.Add(topic, localized);
			}
			string localizedNoTopic = LocalizationManager.GetDynamicString("Bloom", "Topics.NoTopic", "No Topic",
				@"shows in the topics chooser in the edit tab");
			var arrayOfKeyValuePairs = from key in keyToLocalizedTopicDictionary.Keys
				orderby keyToLocalizedTopicDictionary[key]
				select string.Format("\"{0}\": \"{1}\"",key,keyToLocalizedTopicDictionary[key]);
			var pairs = arrayOfKeyValuePairs.Concat(",");
			info.ContentType = "application/json";
			var data = string.Format("{{\"NoTopic\": \"{0}\", {1} }}", localizedNoTopic, pairs);

			info.WriteCompleteOutput(data);
			/*			var data = new {NoTopic = localizedNoTopic, pairs = arrayOfKeyValuePairs};
			 * var serializeObject = JsonConvert.SerializeObject(data, new JsonSerializerSettings
						{
							TypeNameHandling = TypeNameHandling.None,
							TypeNameAssemblyFormat = FormatterAssemblyStyle.Simple,
						});
						*/
			//info.WriteCompleteOutput(serializeObject);



			return true;
		}

		private bool ProcessAnyFileContent(IRequestInfo info, string localPath)
		{
			string modPath = localPath;
			string path = null;
			// When JavaScript inserts our path into the html it replaces the three magic html characters with these substitutes.
			// We need to convert back in order to match our key. Then, reverse the change we made to deal with quotation marks.
			string tempPath = UnescapeUrlQuotes(modPath.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&"));
			if (RobustFile.Exists(tempPath))
				modPath = tempPath;
			try
			{
				if (localPath.Contains("favicon.ico")) //need something to pacify Chrome
					path = FileLocator.GetFileDistributedWithApplication("BloomPack.ico");

				// Is this request the full path to an image file? For most images, we just have the filename. However, in at
				// least one use case, the image we want isn't in the folder of the PDF we're looking at. That case is when 
				// we are looking at a "folio", a book that gathers up other books into one big PDF. In that case, we want
				// to find the image in the correct book folder.  See AddChildBookContentsToFolio();
				var possibleFullImagePath = localPath;
				// "OriginalImages/" at the beginning means we're generating a pdf and want full images,
				// but it has nothing to do with the actual file location.
				if (localPath.StartsWith("OriginalImages/"))
					possibleFullImagePath = localPath.Substring(15);
				if (info.GetQueryParameters()["generateThumbnaiIfNecessary"] == "true")
					return FindOrGenerateImage(info, localPath);
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
					path = FileLocator.GetFileDistributedWithApplication(BloomFileLocator.BrowserRoot, modPath);
				}
			}
			catch (ApplicationException)
			{
				// ignore. Assume this means that this class/method cannot serve that request, but something else may.
			}

			//There's probably a eventual way to make this problem go away,
			// but at the moment FF, looking for source maps to go with css, is
			// looking for those maps where we said the css was, which is in the actual
			// book folders. So instead redirect to our browser file folder.
			if (string.IsNullOrEmpty(path) || !RobustFile.Exists(path))
			{
				var startOfBookLayout = localPath.IndexOf("bookLayout");
				if (startOfBookLayout > 0)
					path = BloomFileLocator.GetBrowserFile(localPath.Substring(startOfBookLayout));
				var startOfBookEdit = localPath.IndexOf("bookEdit");
				if (startOfBookEdit > 0)
					path = BloomFileLocator.GetBrowserFile(localPath.Substring(startOfBookEdit));
			}

			if (!RobustFile.Exists(path) && localPath.StartsWith("pageChooser/") && IsImageTypeThatCanBeReturned(localPath))
			{
				// if we're in the page chooser dialog and looking for a thumbnail representing an image in a
				// template page, look for that thumbnail in the book that is the template source,
				// rather than in the folder that stores the page choose dialog HTML and code.
				var templatePath = Path.Combine(_bookSelection.CurrentSelection.FindTemplateBook().FolderPath,
					localPath.Substring("pageChooser/".Length));
				if (RobustFile.Exists(templatePath))
				{
					info.ReplyWithImage(templatePath);
					return true;
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
			if (!RobustFile.Exists(path) && IsImageTypeThatCanBeReturned(localPath))
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
				if(path == null)
				{
					path = "(was null)";
				}
				var stuffToIgnore = new[] {
					//browser/debugger stuff
					"favicon.ico", ".map",
					// Audio files may well be missing because we look for them as soon
					// as we define an audio ID, but they wont' exist until we record something.
					"/audio/",					
					// This is readium stuff that we don't ship with, because they are needed by the original reader to support display and implementation 
					// of controls we hide for things like adding books to collection, displaying the collection, playing audio (that last we might want back one day).
					EpubMaker.kEPUBExportFolder.ToLowerInvariant()
				};

				if(stuffToIgnore.Any(s => (localPath.ToLowerInvariant().Contains(s))))
					return false;

				// we have any number of incidences where something asks for a page after we've navigated from it. E.g. BL-3715, BL-3769.
				// I suspect our disposal algorithm is just flawed: the page is removed from the _url cache as soon as we navigated away, 
				// which is too soon. But that will take more research and we're trying to finish 3.7. 
				// So for now, let's just not to bother the user about an error that is only going to effect thumbnailing.
				if (IsSimulatedFileUrl(localPath)) 
				{
					//even beta users should not be confronted with this
					NonFatalProblem.Report(ModalIf.Alpha, PassiveIf.Beta, "Page expired", "Server no longer has this page in the memory: " + localPath);
				}
				else
				{
					NonFatalProblem.Report(ModalIf.Beta, PassiveIf.All, "Cannot Find File", "Server could not find the file " + path + ". LocalPath was " + localPath + System.Environment.NewLine );
				}
				return false;
			}
			info.ContentType = GetContentType(Path.GetExtension(modPath));
			info.ReplyWithFileContent(path);
			return true;
		}

		protected bool IsSimulatedFileUrl(string localPath)
		{
			var extension = Path.GetExtension(localPath);
			if(extension != null && !extension.StartsWith("htm"))
				return false;

			// a good improvement might be to make these urls more obviously cache requests. But for now, let's just see if they are filename guids
			var filename = Path.GetFileNameWithoutExtension(localPath);
			Guid result;
			return Guid.TryParse(filename, out result);
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

		/// <summary>
		/// Enhancing the code to not generate a new Book object every time we call Book.FindTemplateBook (BL-3782)
		/// exposed a threading bug in the Mono library.  The same book can now being navigated at the same time
		/// on multiple threads.  This appears to work okay on Windows/.Net, but throws exceptions on Linux/Mono.
		/// (The error message in Mono even admitted it might reflect a bug in their XML library code.)
		/// Locking three different lines of code below fixes this problem.  The locking doesn't seem to hurt
		/// performance significantly, so I haven't tried to make it system specific.
		/// </summary>
		private static object templateLock = new object();

		/// <summary>
		/// Currently used in the Add Page dialog, a path with ?generateThumbnaiIfNecessary=true indicates a thumbnail for
		/// a template page. Usually we expect that a file at the same path but with extension .svg will
		/// be found and returned. Failing this we try for one ending in .png. If this still fails we
		/// start a process to generate an image from the template page content.
		/// </summary>
		/// <param name="path"></param>
		/// <returns>Should always return true, unless we really can't come up with an image at all.</returns>
		private bool FindOrGenerateImage(IRequestInfo info, string path)
		{
			var localPath = AdjustPossibleLocalHostPathToFilePath(path);
			var svgpath = Path.ChangeExtension(localPath, "svg");
			if (RobustFile.Exists(svgpath))
			{
				ReplyWithFileContentAndType(info, svgpath);
				return true;
			}
			var pngpath = Path.ChangeExtension(localPath, "png");
			if (RobustFile.Exists(pngpath))
			{
				ReplyWithFileContentAndType(info, pngpath);
				return true;
			}
			// We don't have an image; try to make one.
			// This is the one remaining place where the EIS is aware that there is such a thing as a current book.
			// Unfortunately it is part of a complex bit of logic that mostly doesn't have to do with current book,
			// so it doesn't feel right to move it to CurrentBookHandler, especially as it's not possible to
			// identify the queries which need the knowledge in the usual way (by a leading URL fragment).
			if (_bookSelection.CurrentSelection == null)
				return false; // paranoia
			var template = _bookSelection.CurrentSelection.FindTemplateBook();
			if (template == null)
				return false; // paranoia
			var caption = Path.GetFileNameWithoutExtension(path).Trim();
			var isLandscape = caption.EndsWith("-landscape"); // matches string in page-chooser.ts
			if (isLandscape)
				caption = caption.Substring(0, caption.Length - "-landscape".Length);
			// The Replace of & with + corresponds to a replacement made in page-chooser.ts method loadPagesFromCollection.
			IPage templatePage = null;
			lock (templateLock) { templatePage = template.GetPages().FirstOrDefault(page => page.Caption != null && page.Caption.Replace("&", "+") == caption); }
			if (templatePage == null)
				lock (templateLock) { templatePage = template.GetPages().FirstOrDefault(); }	// may get something useful?? or throw??

			Image thumbnail = null;
			lock (templateLock) { thumbnail = _thumbNailer.GetThumbnailForPage(template, templatePage, isLandscape); }

			// lock to avoid BL-3781 where we got a "Object is currently in use elsewhere" while doing the Clone() below.
			// Note: it would appear that the clone isn't even needed, since it was added in the past to overcome this
			// same contention problem (but, in hindsite, only partially, see?). But for some reason if we just lock the image
			// until it is saved, we get all grey rectangles. So for now, we just quickly do the clone and unlock.
			var resultPath = "";
			Bitmap clone;
			lock(thumbnail)
			{
				clone = new Bitmap((Image) thumbnail.Clone());
			}
			using(clone)
			{
					try
					{
						Directory.CreateDirectory(Path.GetDirectoryName(pngpath));
						SIL.IO.RobustIO.SaveImage(clone, pngpath);
						resultPath = pngpath;
					}
					catch(Exception)
					{
						using(var file = new TempFile())
						{
							SIL.IO.RobustIO.SaveImage(clone, file.Path);
							resultPath = file.Path;
						}
					}
			}
			ReplyWithFileContentAndType(info, resultPath);
			return true; // We came up with some reply
		}

		private static void ReplyWithFileContentAndType(IRequestInfo info, string path)
		{
			info.ContentType = GetContentType(Path.GetExtension(path));
			info.ReplyWithFileContent(path);
		}

		private bool ProcessCssFile(IRequestInfo info, string localPath)
		{
			// BL-2219: "OriginalImages" means we're generating a pdf and want full images,
			// but it has nothing to do with css files and defeats the following 'if'
			localPath = localPath.Replace("OriginalImages/", "");
			// is this request the full path to a real file?
			if (RobustFile.Exists(localPath) && Path.IsPathRooted(localPath))
			{
				// Typically this will be files in the book or collection directory, since the browser
				// is supplying the path.

				// currently this only applies to languageDisplay.css, settingsCollectionStyles.css, and customCollectionStyles.css
				var cssFile = Path.GetFileName(localPath);
				if ((cssFile == "languageDisplay.css") || (cssFile == "settingsCollectionStyles.css") || (cssFile == "customCollectionStyles.css"))
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
			var path = _fileLocator.LocateFile(fileName);

			// if still not found, and localPath is an actual file path, use it
			if (string.IsNullOrEmpty(path) && RobustFile.Exists(localPath)) path = localPath;

			if (string.IsNullOrEmpty(path))
			{
				// it's just possible we need to add BloomBrowserUI to the path (in the case of the AddPage dialog)
				var lastTry = FileLocator.GetFileDistributedWithApplication(true, BloomFileLocator.BrowserRoot, localPath);
				if(RobustFile.Exists(lastTry)) path = lastTry;
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
			}

			base.Dispose(fDisposing);
		}
	}
}
