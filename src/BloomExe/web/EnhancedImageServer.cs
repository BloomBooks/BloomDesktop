// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System;
using System.Diagnostics;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Net;
using Bloom.ImageProcessing;
using System.IO;
using L10NSharp;
using Microsoft.Win32;
using Palaso.IO;
using Bloom.Collection;


namespace Bloom.web
{
	/// <summary>
	/// A local http server that can serve (low-res) images plus other files.
	/// </summary>
	public class EnhancedImageServer: ImageServer
	{
		private FileSystemWatcher _sampleTextsWatcher;
		private bool _sampleTextsChanged = true;

		public CollectionSettings CurrentCollectionSettings { get; set; }

		public EnhancedImageServer(LowResImageCache cache): base(cache)
		{
		}

		public string CurrentPageContent { get; set; }
		public string AccordionContent { get; set; }
		public bool AuthorMode { get; set; }

		/// <summary>
		/// There can really only be one of these globally, since ReadersHandler is static. But for now that's true anyway
		/// because we use a fixed port. See comments on the ReadersHandler property.
		/// </summary>
		public Book.Book CurrentBook
		{
			get { return ReadersHandler.CurrentBook; }
			set { ReadersHandler.CurrentBook = value; }
		}

		// Every path should return false or send a response.
		// Otherwise we can get a timeout error as the browser waits for a response.
		protected override bool ProcessRequest(IRequestInfo info)
		{
			if (base.ProcessRequest(info))
				return true;

			var localPath = GetLocalPathWithoutQuery(info);

			// routing
			if (localPath.StartsWith("readers/"))
			{
				if (ReadersHandler.HandleRequest(localPath, info, CurrentCollectionSettings))
					return true;
			}
			else if (localPath.StartsWith("i18n/"))
			{
				if (I18NHandler.HandleRequest(localPath, info, CurrentCollectionSettings))
					return true;
			}
			else if (localPath.StartsWith("directoryWatcher/"))
			{
				var dirName = info.GetPostData()["dir"];

				if (dirName == "Sample Texts")
				{
					if (CheckForSampleTextChanges(info))
						return true;
				}

				return false;
			}
			else if (localPath.StartsWith("leveledRTInfo/"))
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
			} else if (localPath.StartsWith("localhost/"))
			{
				// project on network mapped drive like localhost\C$.
				// URL was something like /bloom///localhost/C$/, but info.LocalPathWithoutQuery uses Uri.LocalPath
				// which for some reason drops the needed leading slashes.
				var temp = "//" + localPath;
				if (File.Exists(temp))
					localPath = temp;
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

				case "help":
					var post = info.GetPostData();
					HelpLauncher.Show(null, post["data"]);
					return true;

				case "getNextBookStyle":
					info.ContentType = "text/html";
					info.WriteCompleteOutput(CurrentBook.NextStyleNumber.ToString(CultureInfo.InvariantCulture));
					return true;
			}

			string path = null;
			try
			{
				// Surprisingly, this method will return localPath unmodified if it is a fully rooted path
				// (like C:\... or \\localhost\C$\...) to a file that exists. So this execution path
				// can return contents of any file that exists if the URL gives its full path...even ones that
				// are generated temp files most certainly NOT distributed with the application.
				path = FileLocator.GetFileDistributedWithApplication("BloomBrowserUI", localPath);
			}
			catch (ApplicationException)
			{
				// ignore
			}

			if (!File.Exists(path)) return false;

			info.ContentType = GetContentType(Path.GetExtension(localPath));
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
			if (_sampleTextsWatcher == null)
			{
				var path = Path.Combine(Path.GetDirectoryName(CurrentCollectionSettings.SettingsFilePath), "Sample Texts");
				if (!Directory.Exists(path))
					Directory.CreateDirectory(path);

				_sampleTextsWatcher = new FileSystemWatcher {Path = path};
				_sampleTextsWatcher.Created += SampleTextsOnChange;
				_sampleTextsWatcher.Changed += SampleTextsOnChange;
				_sampleTextsWatcher.Renamed += SampleTextsOnChange;
				_sampleTextsWatcher.Deleted += SampleTextsOnChange;
				_sampleTextsWatcher.EnableRaisingEvents = true;
			}

			var hasChanged = _sampleTextsChanged;

			// Reset the changed flag.
			// NOTE: we are only resetting the flag if it was "true" when we checked in case the FileSystemWatcher detects a change
			// after we check the flag but we reset it to false before we check again.
			if (hasChanged) _sampleTextsChanged = false;

			info.ContentType = "text/plain";
			info.WriteCompleteOutput(hasChanged ? "yes" : "no");

			return true;
		}

		private void SampleTextsOnChange(object sender, FileSystemEventArgs fileSystemEventArgs)
		{
			_sampleTextsChanged = true;
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
