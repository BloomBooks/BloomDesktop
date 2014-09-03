// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System;
using Bloom.ImageProcessing;
using System.IO;
using Bloom.ReaderTools;
using Palaso.IO;
using Bloom.Collection;


namespace Bloom.web
{
	/// <summary>
	/// A local http server that can serve (low-res) images plus other files.
	/// </summary>
	public class EnhancedImageServer: ImageServer
	{
		private FileSystemWatcher _sampleTextsCacheWatcher;
		private bool _sampleTextsChanged = true;
		private SampleTexts _sampleTexts;

		public EnhancedImageServer(LowResImageCache cache): base(cache) {}

		public CollectionSettings CurrentCollectionSettings { get; set; }

		public void EnableSampleTexts()
		{
			_sampleTexts = new SampleTexts(CurrentCollectionSettings, StartSampleTextsCacheWatcher);
		}

        public string CurrentPageContent { get; set; }
        public string AccordionContent { get; set; }

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

		private void StartSampleTextsCacheWatcher()
		{
			if (_sampleTextsCacheWatcher == null)
			{
				var path = Path.Combine(Path.GetDirectoryName(CurrentCollectionSettings.SettingsFilePath), "Sample Texts");

				_sampleTextsCacheWatcher = new FileSystemWatcher { Path = path };
				_sampleTextsCacheWatcher.Created += SampleTextsOnChange;
				_sampleTextsCacheWatcher.Changed += SampleTextsOnChange;
				_sampleTextsCacheWatcher.Deleted += SampleTextsOnChange;
				_sampleTextsCacheWatcher.EnableRaisingEvents = true;
			}
		}

		private bool CheckForSampleTextChanges(IRequestInfo info)
		{
			if (_sampleTextsCacheWatcher == null)
				return false;

			var hasChanged = _sampleTextsChanged;

			// reset the changed flag
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
				if (_sampleTexts != null)
				{
					_sampleTexts.Dispose();
					_sampleTexts = null;
				}

				if (_sampleTextsCacheWatcher != null)
				{
					_sampleTextsCacheWatcher.EnableRaisingEvents = false;
					_sampleTextsCacheWatcher.Dispose();
					_sampleTextsCacheWatcher = null;
				}
			}

			base.Dispose(fDisposing);
		}
	}
}

