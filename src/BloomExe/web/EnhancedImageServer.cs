﻿// Copyright (c) 2014-2015 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Bloom.Book;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Bloom.ImageProcessing;
using BloomTemp;
using L10NSharp;
using Microsoft.Win32;
using SIL.Code;
using SIL.IO;
using Bloom.Collection;
using Bloom.Edit;
using Newtonsoft.Json;
using SIL.Reporting;
using SIL.Extensions;
using RestSharp.Contrib;

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
		private readonly ProjectContext _projectContext;

		// This dictionary ties API endpoints to functions that handle the requests.
		private Dictionary<string, EndpointHandler> _endpointHandlers = new Dictionary<string, EndpointHandler>();

		public CollectionSettings CurrentCollectionSettings { get; set; }

		/// <summary>
		/// This is only used in a few special cases where we need one to pass as an argument but it won't be fully used.
		/// </summary>
		internal EnhancedImageServer() : this( new RuntimeImageProcessor(new BookRenamedEvent()), null, null)
		{ }

		public EnhancedImageServer(RuntimeImageProcessor cache, BookThumbNailer thumbNailer,  BloomFileLocator fileLocator = null) : base(cache)
		{
			_thumbNailer = thumbNailer;
			_fileLocator = fileLocator;
			// Storing this in the ReadersHandler means there can only be one instance of EIS, since ReadersHandler is static. But for
			// now that's true anyway because we use a fixed port. If we need to change this we could just make an instance here.
			ReadersApi.Server = this;
		}


		public void RegisterEndpointHandler(string key, EndpointHandler handler)
		{
			_endpointHandlers[key.Trim(new char[] {'/'})] = handler;
		}

		/// <summary>
		/// This constructor is used for unit testing
		/// </summary>
		public EnhancedImageServer(RuntimeImageProcessor cache, BloomFileLocator fileLocator)
			: this(cache, null, fileLocator)
		{
		}

		// We use two different locks to synchronize access to the methods of this class.
		// This allows certain methods to run concurrently.

		// used to synchronize access to I18N methods
		private object I18NLock = new object();
		// used to synchronize access to various other methods
		private object SyncObj = new object();

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
		/// <param name="forSrcAttr">If this is true, the url will be inserted by JavaScript into
		/// a src attr for an IFrame. We need to account for this because un-escaped quotation marks in the
		/// URL can cause errors in JavaScript strings.</param>
		/// <returns></returns>
		public static SimulatedPageFile MakeSimulatedPageFileInBookFolder(HtmlDom dom, bool forSrcAttr = false)
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
			if (forSrcAttr)
			{
				// We need to UrlEncode the single and double quote characters so they will play nicely with JavaScript. 
				url = EscapeUrlQuotes(url);
				// When JavaScript inserts our path into the html it replaces the three magic html characters with these substitutes.
				// We need to modify our key so that when the JavaScript comes looking for the page its modified url will
				// generate the right key.
				key = SimulateJavaScriptHandlingOfHtml(key);
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
				File.Delete(uri.LocalPath);
				return;
			}
			lock (_urlToSimulatedPageContent)
			{
				_urlToSimulatedPageContent.Remove(key.FromLocalhost());
			}
		}
		const string OpenFileInBrowser = "openFileInBrowser/";

		// Every path should return false or send a response.
		// Otherwise we can get a timeout error as the browser waits for a response.
		//
		// NOTE: this method gets called on different threads!
		protected override bool ProcessRequest(IRequestInfo info)
		{
			var localPath = GetLocalPathWithoutQuery(info);
			if (localPath.ToLower().StartsWith("api/"))
			{
				var endpoint = localPath.Substring(3).ToLowerInvariant().Trim(new char[] {'/'});
				foreach (var pair in _endpointHandlers.Where(pair => Regex.Match(endpoint, pair.Key.ToLower()).Success))
				{
					lock(SyncObj)
					{
						return ApiRequest.Handle(pair.Value, info, CurrentCollectionSettings);
					}
				}
			}

			//OK, no more obvious simple API requests, dive into the rat's nest of other possibilities
			if (base.ProcessRequest(info))
				return true;

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
			else if (localPath.StartsWith("windows/useLongpress"))
			{
				var usingIP = false;

				if (SIL.PlatformUtilities.Platform.IsWindows)
				{
					// In order to detect an input processor, we need to execute this on the main UI thread.
					var frm = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Shell);
					if (frm != null)
					{
						usingIP = SIL.Windows.Forms.Keyboarding.KeyboardController.IsFormUsingInputProcessor(frm);
					}
				}

				// Send to browser
				info.ContentType = "text/plain";
				info.WriteCompleteOutput(usingIP ? "No" : "Yes");
				return true;
			}
			else if (localPath.StartsWith("directoryWatcher/", StringComparison.InvariantCulture))
				return ProcessDirectoryWatcher(info);
			else if (localPath.StartsWith(OpenFileInBrowser, StringComparison.InvariantCulture))
				return ProcessOpenFileInBrowser(info, localPath);
			else if (localPath.StartsWith("localhost/", StringComparison.InvariantCulture))
			{
				var temp = LocalHostPathToFilePath(localPath);
				if (File.Exists(temp))
					localPath = temp;
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

		/// <summary>
		/// Handles a url starting with openfileinbrowser by stripping off that prefix, searching for the file
		/// named in the remainder of the url, and opening it in some browser (passing on any anchor specified).
		/// </summary>
		/// <param name="info"></param>
		/// <param name="localPath1"></param>
		/// <returns></returns>
		private bool ProcessOpenFileInBrowser(IRequestInfo info, string localPath1)
		{
			string localPath = localPath1.Substring(OpenFileInBrowser.Length);
			lock (SyncObj)
			{
				var queryPart = string.Empty;
				if (info.RawUrl.Contains("?"))
					queryPart = "#" + info.RawUrl.Split('?')[1];
				var langCode = LocalizationManager.UILanguageId;
				var completeEnglishPath = FileLocator.GetFileDistributedWithApplication(localPath);
				var completeUiLangPath = GetUiLanguageFileVersion(completeEnglishPath, langCode);
				string url;
				if (langCode != "en" && File.Exists(completeUiLangPath))
					url = completeUiLangPath;
				else
					url = completeEnglishPath;
				var cleanUrl = url.Replace("\\", "/"); // allows jump to file to work

				string browser = string.Empty;
				if (SIL.PlatformUtilities.Platform.IsLinux)
				{
					// REVIEW: This opens HTML files in the browser. Do we have any non-html
					// files that this code needs to open in the browser? Currently they get
					// opened in whatever application the user has selected for that file type
					// which might well be an editor.
					browser = "xdg-open";
				}
				else
				{
					// If we don't provide the path of the browser, i.e. Process.Start(url + queryPart), we get file not found exception.
					// If we prepend "file:///", the anchor part of the link (#xxx) is not sent unless we provide the browser path too.
					// This is the same behavior when simply typing a url into the Run command on Windows.
					// If we fail to get the browser path for some reason, we still load the page, just without navigating to the anchor.
					string defaultBrowserPath;
					if (TryGetDefaultBrowserPath(out defaultBrowserPath))
					{
						browser = defaultBrowserPath;
					}
				}

				if (!string.IsNullOrEmpty(browser))
				{
					try
					{
						Process.Start(browser, "\"file:///" + cleanUrl + queryPart + "\"");
						return false;
					}
					catch (Exception)
					{
						Debug.Fail("Jumping to browser with anchor failed.");
						// Don't crash Bloom because we can't open an external file.
					}
				}
				// If the above failed, either for lack of default browser or exception, try this:
				Process.Start("\"" + cleanUrl + "\"");
				return false;
			}
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
				case "help":
					var post = info.GetPostDataWhenFormEncoded();
					// Help launches a separate process so it doesn't matter that we don't call
					// it on the UI thread
					HelpLauncher.Show(null, post["data"]);
					return true;
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
			if (File.Exists(tempPath))
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
				if(File.Exists(possibleFullImagePath) && Path.IsPathRooted(possibleFullImagePath))
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
			if (string.IsNullOrEmpty(path) || !File.Exists(path))
			{
				var startOfBookLayout = localPath.IndexOf("bookLayout");
				if (startOfBookLayout > 0)
					path = BloomFileLocator.GetBrowserFile(localPath.Substring(startOfBookLayout));
				var startOfBookEdit = localPath.IndexOf("bookEdit");
				if (startOfBookEdit > 0)
					path = BloomFileLocator.GetBrowserFile(localPath.Substring(startOfBookEdit));
			}
			if (!File.Exists(path))
			{
				NonFatalProblem.Report(ModalIf.Beta, PassiveIf.All, "Server could not find the file "+path,"LocalPath was "+localPath);
				return false;
			}
			info.ContentType = GetContentType(Path.GetExtension(modPath));
			info.ReplyWithFileContent(path);
			return true;
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
			if (File.Exists(svgpath))
			{
				ReplyWithFileContentAndType(info, svgpath);
				return true;
			}
			var pngpath = Path.ChangeExtension(localPath, "png");
			if (File.Exists(pngpath))
			{
				ReplyWithFileContentAndType(info, pngpath);
				return true;
			}
			// We don't have an image; try to make one.
			// This is the one remaining place where the EIS is aware that there is such a thing as a current book.
			// Unfortunately it is part of a complex bit of logic that mostly doesn't have to do with current book,
			// so it doesn't feel right to move it to CurrentBookHandler, especially as it's not possible to
			// identify the queries which need the knowledge in the usual way (by a leading URL fragment).
			if (CurrentBookHandler.CurrentBook == null)
				return false; // paranoia
			var template = CurrentBookHandler.CurrentBook.FindTemplateBook();
			if (template == null)
				return false; // paranoia
			var caption = Path.GetFileNameWithoutExtension(path).Trim();
			var isLandscape = caption.EndsWith("-landscape"); // matches string in page-chooser.ts
			if (isLandscape)
				caption = caption.Substring(0, caption.Length - "-landscape".Length);
			int dummy = 0;
			// The Replace of & with + corresponds to a replacement made in page-chooser.ts method loadPagesFromCollection.
			var templatePage = template.GetPages().FirstOrDefault(page => page.Caption.Replace("&", "+") == caption);
			if (templatePage == null)
				templatePage = template.GetPages().FirstOrDefault(); // may get something useful?? or throw??

			Image image = _thumbNailer.GetThumbnailForPage(template, templatePage, isLandscape);

			// The clone here is an attempt to prevent an unexplained exception complaining that the source image for the bitmap is in use elsewhere.
			using (Bitmap b = new Bitmap((Image)image.Clone()))
			{
				try
				{
					{
						Directory.CreateDirectory(Path.GetDirectoryName(pngpath));
						b.Save(pngpath);
					}
					ReplyWithFileContentAndType(info, pngpath);
				}
				catch (Exception)
				{
					using (var file = new TempFile())
					{
						b.Save(file.Path);
						ReplyWithFileContentAndType(info, file.Path);
					}
				}
			}
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
			if (File.Exists(localPath) && Path.IsPathRooted(localPath))
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
			if (string.IsNullOrEmpty(path) && File.Exists(localPath)) path = localPath;

			if (string.IsNullOrEmpty(path))
			{
				// it's just possible we need to add BloomBrowserUI to the path (in the case of the AddPage dialog)
				var lastTry = FileLocator.GetFileDistributedWithApplication(true, BloomFileLocator.BrowserRoot, localPath);
				if(File.Exists(lastTry)) path = lastTry;
			}

			// return false if the file was not found
			if (string.IsNullOrEmpty(path)) return false;

			info.ContentType = "text/css";
			info.ReplyWithFileContent(path);
			return true;
		}

		private static bool TryGetDefaultBrowserPath(out string defaultBrowserPath)
		{
			try
			{
				string key = @"HTTP\shell\open\command";
				using (RegistryKey registrykey = Registry.ClassesRoot.OpenSubKey(key, false))
					defaultBrowserPath = ((string)registrykey.GetValue(null, null)).Split('"')[1];
				return true;
			}
			catch
			{
				defaultBrowserPath = null;
				return false;
			}
		}

		private string GetUiLanguageFileVersion(string englishFileName, string langCode)
		{
			return englishFileName.Replace("-en.htm", "-" + langCode + ".htm");
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
