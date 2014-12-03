// Copyright (c) 2014-2015 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Xml;
using Bloom.Book;
using System.IO;
using Bloom.ImageProcessing;
using BloomTemp;
using L10NSharp;
using Microsoft.Win32;
using Palaso.IO;
using Bloom.Collection;
using Palaso.Xml;
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
		// A string unlikely to occur in a book name which we can substitute for a single quote
		// when making a book path name to insert into JavaScript.
		private const string QuoteSubstitute = "$xquotex$";
		private const string OriginalImageMarker = "OriginalImages"; // Inserted into simulated page urls to suppress image processing
		private FileSystemWatcher _sampleTextsWatcher;
		private bool _sampleTextsChanged = true;
		static Dictionary<string, string> _urlToSimulatedPageContent = new Dictionary<string, string>(); // see comment on MakeSimulatedPageFileInBookFolder

		public CollectionSettings CurrentCollectionSettings { get; set; }

		/// <summary>
		/// This is only used in a few special cases where we need one to pass as an argument but it won't be fully used.
		/// </summary>
		internal EnhancedImageServer() : base( new RuntimeImageProcessor(new BookRenamedEvent()))
		{ }

		public EnhancedImageServer(RuntimeImageProcessor cache): base(cache)
		{
		}

		// We use two different locks to synchronize access to the methods of this class.
		// This allows certain methods to run concurrently.

		// used to synchronize access to I18N methods
		private object I18NLock = new object();
		// used to synchronize access to various other methods
		private object SyncObj = new object();

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
		/// <returns></returns>
		public static SimulatedPageFile MakeSimulatedPageFileInBookFolder(HtmlDom dom)
		{
			var simulatedPageFileName = Path.ChangeExtension(Guid.NewGuid().ToString(), ".htm");
			var pathToSimulatedPageFile = simulatedPageFileName; // a default, if there is no special folder
			if (dom.BaseForRelativePaths != null)
			{
				pathToSimulatedPageFile = Path.Combine(dom.BaseForRelativePaths, simulatedPageFileName).Replace('\\', '/');
			}
			// It doesn't seem to matter if we make a url with most non-standard characters, but a single quote ends up as
			// a JavaScript constant (that we want to assign to be the page src) and terminates the constant
			// prematurely, with disastrous consequences.
			// FromLocalHost is smart about doing nothing if it is not a localhost url. In case it is, we
			// want the OriginalImageMarker (if any) after the localhost stuff.
			pathToSimulatedPageFile = pathToSimulatedPageFile.FromLocalhost().Replace("'", QuoteSubstitute);
			if (dom.UseOriginalImages)
				pathToSimulatedPageFile = OriginalImageMarker + "/" + pathToSimulatedPageFile;
			var url = pathToSimulatedPageFile.ToLocalhost();
			var key = pathToSimulatedPageFile.Replace('\\', '/');
			var html5String = TempFileUtils.CreateHtml5StringFromXml(dom.RawDom);
			lock (_urlToSimulatedPageContent)
			{
				_urlToSimulatedPageContent[key] = html5String;
			}
			return new SimulatedPageFile() {Key = url};
		}

		internal static void RemoveSimulatedPageFile(string key)
		{
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
			else if (localPath.StartsWith("i18n/", StringComparison.InvariantCulture))
			{
				if (ProcessI18N(localPath, info))
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

		private bool ProcessReaders(string localPath, IRequestInfo info)
		{
			lock (SyncObj)
			{
				return ReadersHandler.HandleRequest(localPath, info, CurrentCollectionSettings);
			}
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
			// as long as deal with simple string/bool properties or static methods we don't
			// have to lock
			switch (localPath)
			{
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

		private static bool ProcessAnyFileContent(IRequestInfo info, string localPath)
		{
			string modPath = localPath;
			string path = null;
			string tempPath = modPath.Replace(QuoteSubstitute, "'");
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
