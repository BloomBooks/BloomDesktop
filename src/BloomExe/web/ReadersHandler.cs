using System.Text;
using System.Linq;
using System.IO;
using System.Xml;
using Bloom.Collection;
using Palaso.Xml;
using Palaso.IO;
using System.Collections.Generic;

namespace Bloom.web
{
	/// <summary>
	/// This class handles requests from the Decodable and Leveled Readers tools, as well as from the Reader Setup dialog.
	/// It reads and writes the reader tools settings file, and retrieves files and other information used by the
	/// reader tools.
	/// </summary>
	static class ReadersHandler
	{
		private static bool _savingReaderWords;
		private const string _synphonyFileNameSuffix = "_lang_data.js";

		// The current book we are editing. Currently this is needed so we can return all the text, to enable JavaScript to update
		// whole-book counts. If we ever support having more than one book open, ReadersHandler will need to stop being static, or
		// some similar change. But by then, we may have the whole book in the main DOM, anyway, and getTextOfPages may be obsolete.
		public static Book.Book CurrentBook { get; set; }

		public static bool HandleRequest(string localPath, IRequestInfo info, CollectionSettings currentCollectionSettings)
		{
			var lastSep = localPath.IndexOf("/", System.StringComparison.Ordinal);
			var lastSegment = (lastSep > -1) ? localPath.Substring(lastSep + 1) : localPath;

			switch (lastSegment)
			{
				case "loadReaderToolSettings":
					var settingsPath = currentCollectionSettings.DecodableLevelPathName;
					var decodableLeveledSettings = "{}";
					if (File.Exists(settingsPath))
						decodableLeveledSettings = File.ReadAllText(settingsPath, Encoding.UTF8);
					info.ContentType = "application/json";
					info.WriteCompleteOutput(decodableLeveledSettings);
					return true;

				case "saveReaderToolSettings":
					var path = currentCollectionSettings.DecodableLevelPathName;
					var content = info.GetPostData()["data"];
					File.WriteAllText(path, content, Encoding.UTF8);
					info.ContentType = "text/plain";
					info.WriteCompleteOutput("OK");
					return true;

				case "getDefaultFont":
					var bookFontName = currentCollectionSettings.DefaultLanguage1FontName;
					if (string.IsNullOrEmpty(bookFontName)) bookFontName = "sans-serif";
					info.ContentType = "text/plain";
					info.WriteCompleteOutput(bookFontName);
					return true;

				case "getSampleTextsList":
					info.ContentType = "text/plain";
					info.WriteCompleteOutput(GetSampleTextsList(currentCollectionSettings.SettingsFilePath));
					return true;

				case "getSampleFileContents":
					var fileName = info.GetQueryString()["data"];
					info.ContentType = "text/plain";
					info.WriteCompleteOutput(GetSampleFileContents(fileName, currentCollectionSettings.SettingsFilePath));
					return true;

				case "getTextOfPages":
					info.ContentType = "text/plain";
					info.WriteCompleteOutput(GetTextOfPages());
					return true;

				case "saveReaderToolsWords":
					info.ContentType = "text/plain";
					info.WriteCompleteOutput(SaveReaderToolsWordsFile(info.GetPostData()["data"]));
					return true;

			}

			return false;
		}

		/// <summary>
		/// Needs to return a string with the bloom-content1 text of each non-x-matter page, separated by /r
		/// </summary>
		/// <returns></returns>
		private static string GetTextOfPages()
		{
			var pages = CurrentBook.RawDom.SafeSelectNodes("//div[contains(concat(' ', @class, ' '), ' bloom-page ')]")
				.Cast<XmlElement>()
				.Where(p =>
				{
					var cls = " " + p.Attributes["class"].Value + " ";
					return !cls.Contains(" bloom-frontMatter ") && !cls.Contains(" bloom-backMatter ");
				});
			var sb = new StringBuilder();
			foreach (var page in pages)
			{
				if (sb.Length > 0)
					sb.Append("\r");
				foreach (XmlElement node in page.SafeSelectNodes(".//div[contains(concat(' ', @class, ' '), ' bloom-content1 ')]"))
					sb.Append(node.InnerText.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " "));
			}
			var temp = sb.ToString();
			return temp;
		}

		private static string GetSampleTextsList(string settingsFilePath)
		{
			var path = Path.Combine(Path.GetDirectoryName(settingsFilePath), "Sample Texts");
			if (!Directory.Exists(path)) return string.Empty;

			var fileHashSet = new HashSet<string>();
			var fileList = "";
			var langFileName = string.Format(ProjectContext.kReaderToolsWordsFileNameFormat, CurrentBook.CollectionSettings.Language1Iso639Code);
			var langFile = Path.Combine(path, langFileName);

			// if the Sample Texts directory is empty, check for ReaderToolsWords-<iso>.json in ProjectContext.GetBloomAppDataFolder()
			if (DirectoryUtilities.DirectoryIsEmpty(path, true))
			{
				var bloomAppDirInfo = new DirectoryInfo(ProjectContext.GetBloomAppDataFolder());

				// get the most recent file
				var foundFile = bloomAppDirInfo.GetFiles(langFileName, SearchOption.AllDirectories).OrderByDescending(fi => fi.LastWriteTime).FirstOrDefault();

				if (foundFile != null)
				{
					// copy it
					File.Copy(Path.Combine(foundFile.DirectoryName, foundFile.Name), langFile);
				}

				return string.Empty;
			}

			// first look for ReaderToolsWords-<iso>.json
			if (File.Exists(langFile))
				fileHashSet.Add(langFile);

			// next look for <language_name>_lang_data.js
			foreach (var file in Directory.GetFiles(path, "*" + _synphonyFileNameSuffix))
			{
				fileHashSet.Add(file);
			}

			// now add the rest
			foreach (var file in Directory.GetFiles(path))
			{
				fileHashSet.Add(file);
			}

			fileList = string.Join("\r", fileHashSet.ToArray());

			return fileList;
		}

		/// <summary>Gets the contents of a Sample Text file</summary>
		/// <param name="fileName"></param>
		/// <param name="settingsFilePath"></param>
		private static string GetSampleFileContents(string fileName, string settingsFilePath)
		{
			var path = Path.Combine(Path.GetDirectoryName(settingsFilePath), "Sample Texts");
			path = Path.Combine(path, fileName);

			// first try utf-8/ascii encoding (the .Net default)
			var text = File.ReadAllText(path);

			// If the "unknown" character (65533) is present, C# did not sucessfully decode the file. Try the system default encoding and codepage.
			if (text.Contains((char)65533))
				text = File.ReadAllText(path, Encoding.Default);

			return text;
		}

		/// <summary></summary>
		/// <param name="jsonString">The contents of the ReaderToolsWords file</param>
		/// <returns>OK</returns>
		private static string SaveReaderToolsWordsFile(string jsonString)
		{
			while (_savingReaderWords)
				System.Threading.Thread.Sleep(0);

			try
			{
				_savingReaderWords = true;

				// insert LangName and LangID if missing
				if (jsonString.Contains("\"LangName\":\"\""))
					jsonString = jsonString.Replace("\"LangName\":\"\"", "\"LangName\":\"" + CurrentBook.CollectionSettings.Language1Name + "\"");

				if (jsonString.Contains("\"LangID\":\"\""))
					jsonString = jsonString.Replace("\"LangID\":\"\"", "\"LangID\":\"" + CurrentBook.CollectionSettings.Language1Iso639Code + "\"");

				var fileName = string.Format(ProjectContext.kReaderToolsWordsFileNameFormat, CurrentBook.CollectionSettings.Language1Iso639Code);
				fileName = Path.Combine(CurrentBook.CollectionSettings.FolderPath, fileName);

				File.WriteAllText(fileName, jsonString, Encoding.UTF8);
			}
			finally
			{
				_savingReaderWords = false;
			}

			return "OK";
		}

	}
}
