// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System;
using System.Diagnostics;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
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

		/// <summary>
		/// There can really only be one of these globally, since ReadersHandler is static. But for now that's true anyway
		/// because we use a fixed port. See comments on the ReadersHandler property.
		/// </summary>
		public Book.Book CurrentBook
		{
			get { return ReadersHandler.CurrentBook; }
			set { ReadersHandler.CurrentBook = value; }
		}

		protected override bool ProcessRequest(IRequestInfo info)
		{
			if (base.ProcessRequest(info))
				return true;

			var localPath = GetLocalPathWithoutQuery(info);

			// routing
			if (localPath.StartsWith("readers/"))
			{
				if (ReadersHandler.HandleRequest(localPath, info, CurrentCollectionSettings)) return true;
			}
			else if (localPath.StartsWith("i18n/"))
			{
				if (I18NHandler.HandleRequest(localPath, info, CurrentCollectionSettings)) return true;
			}
			else if (localPath.StartsWith("directoryWatcher/"))
			{
				var dirName = info.GetPostData()["dir"];

				if (dirName == "Sample Texts")
				{
					if (CheckForSampleTextChanges(info)) return true;
				}

				return false;
			}
			else if (localPath.StartsWith("DistFiles/"))
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

				// If we don't provide the path of the browser, i.e. Process.Start(url + queryPart), we get file not found exception.
				// If we prepend "file:///", the anchor part of the link (#xxx) is not sent.
				// This is the same behavior when simply typing a url into the Run command on Windows.
				// If we fail to get the browser path for some reason, we still load the page, just without navigating to the anchor.
				// TODO: need Linux-specific code here -- possibly to simply call Process.Start(url + queryPart)
				string defaultBrowserPath;
				if (TryGetDefaultBrowserPath(out defaultBrowserPath))
					Process.Start(defaultBrowserPath, url + queryPart);
				else
					Process.Start(url);

				return true;
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
					InstalledFontCollection installedFontCollection = new InstalledFontCollection();
					info.WriteCompleteOutput(string.Join(",", installedFontCollection.Families.Select(f => f.Name)));
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
