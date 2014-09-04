// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System;
using System.Drawing.Text;
using System.Linq;
using Bloom.ImageProcessing;
using System.IO;
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

		private bool CheckForSampleTextChanges(IRequestInfo info)
		{
			if (_sampleTextsWatcher == null)
			{
				var path = Path.Combine(Path.GetDirectoryName(CurrentCollectionSettings.SettingsFilePath), "Sample Texts");

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

