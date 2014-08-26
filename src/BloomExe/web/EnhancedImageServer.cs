// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System;
using Bloom.ImageProcessing;
using System.IO;
using Palaso.IO;
using Bloom.Collection;
using System.Text;
using System.Linq;

namespace Bloom.web
{
	/// <summary>
	/// A local http server that can serve (low-res) images plus other files.
	/// </summary>
	public class EnhancedImageServer: ImageServer
	{
		public CollectionSettings CurrentCollectionSettings { get; set; }

		public EnhancedImageServer(LowResImageCache cache): base(cache)
		{
		}

		public string CurrentPageContent { get; set; }
		public string AccordionContent { get; set; }

		protected override bool ProcessRequest(IRequestInfo info)
		{
			if (base.ProcessRequest(info))
				return true;

			var localPath = GetLocalPathWithoutQuery(info);

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

				case "loadReaderToolSettings":
					var settingsPath = CurrentCollectionSettings.DecodableLevelPathName;
					var decodableLeveledSettings = "{}";
					if (File.Exists(settingsPath))
						decodableLeveledSettings = File.ReadAllText(settingsPath, Encoding.UTF8);
					info.ContentType = "application/json";
					info.WriteCompleteOutput(decodableLeveledSettings);
					return true;

				case "getDefaultFont":
					var bookFontName = CurrentCollectionSettings.DefaultLanguage1FontName;
					if (string.IsNullOrEmpty(bookFontName)) bookFontName = "sans-serif";
					info.ContentType = "text/plain";
					info.WriteCompleteOutput(bookFontName);
					return true;

				case "getSampleTextsList":
					info.ContentType = "text/plain";
					info.WriteCompleteOutput(GetSampleTextsList());
					return true;

				case "getSampleFileContents":
					var fileName = info.GetQueryString()["data"];
					info.ContentType = "text/plain";
					info.WriteCompleteOutput(GetSampleFileContents(fileName));
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
			if (File.Exists(path))
			{
				info.ContentType = GetContentType(Path.GetExtension(localPath));

				info.ReplyWithFileContent(path);
				return true;
			}
			return false;
		}

		private string GetSampleTextsList()
		{
			var path = Path.Combine(Path.GetDirectoryName(CurrentCollectionSettings.SettingsFilePath), "Sample Texts");
			var fileList = "";

			if (Directory.Exists(path))
			{
				foreach (var file in Directory.GetFiles(path))
				{
					if (fileList.Length == 0) fileList = Path.GetFileName(file);
					else fileList += "\r" + Path.GetFileName(file);
				}
			}

			return fileList;
		}

		/// <summary>Gets the contents of a Sample Text file</summary>
		/// <param name="fileName"></param>
		private string GetSampleFileContents(string fileName)
		{
			var path = Path.Combine(Path.GetDirectoryName(CurrentCollectionSettings.SettingsFilePath), "Sample Texts");
			path = Path.Combine(path, fileName);

			// first try utf-8/ascii encoding (the .Net default)
			var text = File.ReadAllText(path);

			// If the "unknown" character (65533) is present, C# did not sucessfully decode the file. Try the system default encoding and codepage.
			if (text.Contains((char)65533))
				text = File.ReadAllText(path, Encoding.Default);

			return text;
		}
	}
}
