// Copyright (c) 2014-2015 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using Bloom.Book;
using System.IO;
using Bloom.ImageProcessing;
using BloomTemp;
using L10NSharp;
using Microsoft.Win32;
using Palaso.Code;
using Palaso.IO;
using Bloom.Collection;
using Palaso.Reporting;
using Palaso.Extensions;
using RestSharp.Contrib;

namespace Bloom.web
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
		private string[] _bloomBrowserUiCssFiles;

		public CollectionSettings CurrentCollectionSettings { get; set; }

		/// <summary>
		/// This is only used in a few special cases where we need one to pass as an argument but it won't be fully used.
		/// </summary>
		internal EnhancedImageServer() : base( new RuntimeImageProcessor(new BookRenamedEvent()))
		{ }

		public EnhancedImageServer(RuntimeImageProcessor cache): base(cache)
		{ }

		/// <summary>
		/// This constructor is used for unit testing
		/// </summary>
		public EnhancedImageServer(RuntimeImageProcessor cache, BloomFileLocator fileLocator)
			: base(cache)
		{
			_fileLocator = fileLocator;
		}

		// We use two different locks to synchronize access to the methods of this class.
		// This allows certain methods to run concurrently.

		// used to synchronize access to I18N methods
		private object I18NLock = new object();
		// used to synchronize access to various other methods
		private object SyncObj = new object();

		public string CurrentPageContent { get; set; }
		public string AccordionContent { get; set; }
		public bool AuthorMode { get; set; }

		/// <summary>
		/// There can really only be one of these globally, since ReadersHandler is static. But for
		/// now that's true anyway because we use a fixed port. See comments on the ReadersHandler
		/// property.
		/// </summary>
		public Book.Book CurrentBook
		{
			get { return ReadersHandler.CurrentBook; }
			set { ReadersHandler.CurrentBook = value; }
		}

		/// <summary>
		/// This code sets things up so that we can edit (or make a thumbnail of, etc.) one page of a book.
		/// This is trickly because we have to satisfy several constraints:
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
			}
			var html5String = TempFileUtils.CreateHtml5StringFromXml(dom.RawDom);
			lock (_urlToSimulatedPageContent)
			{
				_urlToSimulatedPageContent[key] = html5String;
			}
			return new SimulatedPageFile() {Key = url};
		}

		private static string EscapeUrlQuotes(string originalUrl)
		{
			return originalUrl.Replace("'", "%27").Replace("\"", "%22");
		}

		private static string UnescapeUrlQuotes(string escapedUrl)
		{
			return escapedUrl.Replace("%27", "'").Replace("%22", "\"");
		}

		/// <summary>
		/// Producing PDF with gecko doesn't work on Windows for some reason when referencing
		/// the localhost server.  So we need to use actual files, and refer to actual files in
		/// the links to the css files.
		/// </summary>
		/// <remarks>
		/// See http://jira.sil.org/browse/BL-932 for details on the bug.
		/// </remarks>
		public static SimulatedPageFile MakeRealPublishModeFileInBookFolder(HtmlDom dom)
		{
			var simulatedPageFileName = Path.ChangeExtension(Guid.NewGuid().ToString(), ".tmp");
			var pathToSimulatedPageFile = simulatedPageFileName; // a default, if there is no special folder
			if (dom.BaseForRelativePaths != null)
				pathToSimulatedPageFile = Path.Combine(dom.BaseForRelativePaths.FromLocalhost(), simulatedPageFileName);
			FixStyleLinkReferences(dom);
			var html5String = TempFileUtils.CreateHtml5StringFromXml(dom.RawDom);
			using (var writer = File.CreateText(pathToSimulatedPageFile))
			{
				writer.Write(html5String);
				writer.Close();
			}
			var uri = new Uri(pathToSimulatedPageFile);

			var absoluteUri = uri.AbsoluteUri;
			if (pathToSimulatedPageFile.StartsWith("//"))
			{
				// Path is something like //someserver/somefolder/book.
				// For some reason absoluteUri generates file://someserver...
				// But firefox needs three more slashes: file://///someserver...
				absoluteUri = "file:///" + absoluteUri.Substring("file:".Length);
			}
			return new SimulatedPageFile() { Key = absoluteUri };
		}

		private static void FixStyleLinkReferences(HtmlDom dom)
		{
			var links = dom.RawDom.SelectNodes("//links");
			if (links != null)
			{
				foreach (XmlNode xn in links)
				{
					var attrs = xn.Attributes;
					if (attrs == null)
						continue;
					var href = attrs["href"];
					if (href == null)
						continue;
					href.Value = href.Value.FromLocalhost();
				}
			}
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

		// Every path should return false or send a response.
		// Otherwise we can get a timeout error as the browser waits for a response.
		//
		// NOTE: this method gets called on different threads!
		protected override bool ProcessRequest(IRequestInfo info)
		{
			if (base.ProcessRequest(info))
				return true;

			var localPath = GetLocalPathWithoutQuery(info);

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

			if (localPath.StartsWith(OriginalImageMarker))
			{
				// Path relative to simulated page file, and we want the file contents without modification.
				// (Note that the simulated page file's own URL starts with this, so it's important to check
				// for that BEFORE we do this check.)
				localPath = localPath.Substring(OriginalImageMarker.Length + 1);
				return ProcessAnyFileContent(info, localPath);
			}
			// routing
			if (localPath.StartsWith("readers/", StringComparison.InvariantCulture))
			{
				if (ProcessReaders(localPath, info))
					return true;
			}
			if(localPath.StartsWith("imageInfo", StringComparison.InvariantCulture))
			{
				return ReplyWithImageInfo(info, localPath);
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

				if (Palaso.PlatformUtilities.Platform.IsWindows)
				{
					// In order to detect an input processor, we need to execute this on the main UI thread.
					var frm = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Shell);
					if (frm != null)
					{
						usingIP = Palaso.UI.WindowsForms.Keyboarding.KeyboardController.IsFormUsingInputProcessor(frm);
					}
				}

				// Send to browser
				info.ContentType = "text/plain";
				info.WriteCompleteOutput(usingIP ? "No" : "Yes");
				return true;
			}
			else if (localPath.StartsWith("directoryWatcher/", StringComparison.InvariantCulture))
				return ProcessDirectoryWatcher(info);
			else if (localPath.StartsWith("leveledRTInfo/", StringComparison.InvariantCulture))
				return ProcessLevedRTInfo(info, localPath);
			else if (localPath.StartsWith("localhost/", StringComparison.InvariantCulture))
			{
				// project on network mapped drive like localhost\C$.
				// URL was something like /bloom///localhost/C$/, but info.LocalPathWithoutQuery uses Uri.LocalPath
				// which for some reason drops the needed leading slashes.
				var temp = "//" + localPath;
				if (File.Exists(temp))
					localPath = temp;
			}

			return ProcessContent(info, localPath);
		}

		/// <summary>
		/// Get a json of stats about the image. It is used to populate a tooltip when you hover over an image container
		/// </summary>
		private bool ReplyWithImageInfo(IRequestInfo info, string localPath)
		{
			lock (SyncObj)
			{
				try
				{
					info.ContentType = "text/json";
					Require.That(info.RawUrl.Contains("?"));
					var query = info.RawUrl.Split('?')[1];
					var args = HttpUtility.ParseQueryString(query);
					Guard.AssertThat(args.Get("image") != null, "problem with image parameter");
					var fileName = args["image"];
					Guard.AgainstNull(CurrentBook, "CurrentBook");
					var path = Path.Combine(CurrentBook.FolderPath, fileName);
					RequireThat.File(path).Exists();
					var fileInfo = new FileInfo(path);
					dynamic result = new ExpandoObject();
					result.name = fileName;
					result.bytes = fileInfo.Length;

					// Using a stream this way, according to one source,
					// http://stackoverflow.com/questions/552467/how-do-i-reliably-get-an-image-dimensions-in-net-without-loading-the-image,
					// supposedly avoids loading the image into memory when we only want its dimensions
					using(var stream = File.OpenRead(path))
					using(var img = Image.FromStream(stream, false,false))
					{
						result.width = img.Width;
						result.height = img.Height;
					}
					info.WriteCompleteOutput(Newtonsoft.Json.JsonConvert.SerializeObject(result));
					return true;
				}
				catch (Exception e)
				{
					Logger.WriteEvent("Error in server imageInfo/: url was " + localPath);
					Logger.WriteEvent("Error in server imageInfo/: exception is " + e.Message);
				}
				return false;
			}
		}

		private bool ProcessReaders(string localPath, IRequestInfo info)
		{
			lock (SyncObj)
			{
				return ReadersHandler.HandleRequest(localPath, info, CurrentCollectionSettings);
			}
		}

		private static void ProcessError(IRequestInfo info)
		{
			// pop-up the error messages if a debugger is attached or an environment variable is set
			var popUpErrors = Debugger.IsAttached || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DEBUG_BLOOM"));

			var post = info.GetPostData();

			// log the error message
			var errorMsg = post["message"] + Environment.NewLine + "File: " + post["url"].FromLocalhost()
				+ Environment.NewLine + "Line: " + post["line"] + " Column: " + post["column"] + Environment.NewLine;

			Logger.WriteMinorEvent(errorMsg);
			Console.Out.WriteLine(errorMsg);

			if (popUpErrors)
				ErrorReport.NotifyUserOfProblem(errorMsg);
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
			var dirName = info.GetPostData()["dir"];
			if (dirName == "Sample Texts")
			{
				if (CheckForSampleTextChanges(info))
					return true;
			}
			return false;
		}

		private bool ProcessLevedRTInfo(IRequestInfo info, string localPath)
		{
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
				if (Palaso.PlatformUtilities.Platform.IsLinux)
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
				case "accordionContent":
					info.ContentType = "text/html";
					info.WriteCompleteOutput(AccordionContent ?? "");
					return true;
				case "availableFontNames":
					info.WriteCompleteOutput(string.Join(",", Browser.NamesOfFontsThatBrowserCanRender()));
					return true;
				case "authorMode":
					info.ContentType = "text/plain";
					info.WriteCompleteOutput(AuthorMode ? "true" : "false");
					return true;
				case "topics":
					return GetTopicList(info);
				case "help":
					var post = info.GetPostData();
					// Help launches a separate process so it doesn't matter that we don't call
					// it on the UI thread
					HelpLauncher.Show(null, post["data"]);
					return true;
				case "getNextBookStyle":
					info.ContentType = "text/html";
					info.WriteCompleteOutput(CurrentBook.NextStyleNumber.ToString(CultureInfo.InvariantCulture));
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
			info.ContentType = "text/json";
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

		private static bool ProcessAnyFileContent(IRequestInfo info, string localPath)
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
				// Surprisingly, this method will return localPath unmodified if it is a fully rooted path
				// (like C:\... or \\localhost\C$\...) to a file that exists. So this execution path
				// can return contents of any file that exists if the URL gives its full path...even ones that
				// are generated temp files most certainly NOT distributed with the application.
				path = FileLocator.GetFileDistributedWithApplication("BloomBrowserUI", modPath);
			}
			catch (ApplicationException)
			{
				// ignore
			}
			if (!File.Exists(path))
				return false;
			info.ContentType = GetContentType(Path.GetExtension(modPath));
			info.ReplyWithFileContent(path);
			return true;
		}

		private bool ProcessCssFile(IRequestInfo info, string localPath)
		{
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

			// try to find the css file in the BloomBrowserUI directory
			if (string.IsNullOrEmpty(path))
			{
				// collect the css files in the BloomBrowserUI directory
				if (_bloomBrowserUiCssFiles == null)
				{
					var sourceDir = FileLocator.GetDirectoryDistributedWithApplication("BloomBrowserUI");
					_bloomBrowserUiCssFiles = Directory.EnumerateFiles(sourceDir, "*.css", SearchOption.AllDirectories).ToArray();
				}

				path = _bloomBrowserUiCssFiles.FirstOrDefault(f => Path.GetFileName(f) == fileName);
			}

			// if still not found, and localPath is an actual file path, use it
			if (string.IsNullOrEmpty(path) && File.Exists(localPath)) path = localPath;

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
