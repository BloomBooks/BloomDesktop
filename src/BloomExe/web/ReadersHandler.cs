using System;
using System.Diagnostics;
using System.Text;
using System.Linq;
using System.IO;
using System.Xml;
using Bloom.Collection;
using Bloom.ReaderTools;
using Newtonsoft.Json;
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
		private const string kSynphonyFileNameSuffix = "_lang_data.js";

		// The current book we are editing. Currently this is needed so we can return all the text, to enable JavaScript to update
		// whole-book counts. If we ever support having more than one book open, ReadersHandler will need to stop being static, or
		// some similar change. But by then, we may have the whole book in the main DOM, anyway, and getTextOfPages may be obsolete.
		public static Book.Book CurrentBook { get; set; }

		public static bool HandleRequest(string localPath, IRequestInfo info, CollectionSettings currentCollectionSettings)
		{
			var lastSep = localPath.IndexOf("/", StringComparison.Ordinal);
			var lastSegment = (lastSep > -1) ? localPath.Substring(lastSep + 1) : localPath;

			switch (lastSegment)
			{
				case "loadReaderToolSettings":
					info.ContentType = "application/json";
					info.WriteCompleteOutput(GetDefaultReaderSettings(currentCollectionSettings));
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

				case "makeLetterAndWordList":
					MakeLetterAndWordList(info.GetPostData()["settings"], info.GetPostData()["allWords"]);
					info.ContentType = "text/plain";
					info.WriteCompleteOutput("OK");
					return true;

				case "openTextsFolder":
					OpenTextsFolder();
					info.ContentType = "text/plain";
					info.WriteCompleteOutput("OK");
					return true;
			}

			return false;
		}

		/// <summary>
		/// Needs to return a json string with the page guid and the bloom-content1 text of each non-x-matter page
		/// </summary>
		/// <returns></returns>
		private static string GetTextOfPages()
		{
			var pageTexts = new List<string>();

			var pages = CurrentBook.RawDom.SafeSelectNodes("//div[contains(concat(' ', @class, ' '), ' bloom-page ')]")
				.Cast<XmlElement>()
				.Where(p =>
				{
					var cls = " " + p.Attributes["class"].Value + " ";
					return !cls.Contains(" bloom-frontMatter ") && !cls.Contains(" bloom-backMatter ");
				});

			foreach (var page in pages)
			{
				var pageWords = string.Empty;
				foreach (XmlElement node in page.SafeSelectNodes(".//div[contains(concat(' ', @class, ' '), ' bloom-content1 ')]"))
					pageWords += " " + node.InnerText;

				pageTexts.Add("\"" + page.GetAttribute("id") + "\":\"" + EscapeJsonValue(pageWords.Trim()) + "\"");
			}

			return "{" + String.Join(",", pageTexts.ToArray()) + "}";
		}

		private static string EscapeJsonValue(string value)
		{
			return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
		}

		private static string GetSampleTextsList(string settingsFilePath)
		{
			var path = Path.Combine(Path.GetDirectoryName(settingsFilePath), "Sample Texts");
			if (!Directory.Exists(path))
				Directory.CreateDirectory(path);

			var fileList1 = new List<string>();
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
				fileList1.Add(langFile);

			// next look for <language_name>_lang_data.js
			foreach (var file in Directory.GetFiles(path, "*" + kSynphonyFileNameSuffix))
			{
				if (!fileList1.Contains(file))
					fileList1.Add(file);
			}

			// now add the rest
			foreach (var file in Directory.GetFiles(path))
			{
				if (!fileList1.Contains(file))
					fileList1.Add(file);
			}

			return string.Join("\r", fileList1.ToArray());
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

		private static string GetDefaultReaderSettings(CollectionSettings currentCollectionSettings)
		{
			var settingsPath = currentCollectionSettings.DecodableLevelPathName;

			// if file exists, return current settings
			if (File.Exists(settingsPath))
				return File.ReadAllText(settingsPath, Encoding.UTF8);

			// file does not exist, so make a new one
			var settings = new ReaderToolsSettings(true);
			var settingsString = settings.Json;
			File.WriteAllText(settingsPath, settingsString);

			return settingsString;
		}

		/// <summary>
		/// The SaveFileDialog must run on a STA thread.
		/// </summary>
		private static void MakeLetterAndWordList(string jsonSettings, string allWords)
		{
			// load the settings
			var settings = JsonConvert.DeserializeObject<ReaderToolsSettings>(jsonSettings);

			// format the output
			var sb = new StringBuilder();
			sb.AppendLineFormat("Letter and word list for making decodable readers in {0}", CurrentBook.CollectionSettings.Language1Name);

			var idx = 1;
			foreach (var stage in settings.Stages)
			{
				sb.AppendLine();
				sb.AppendLineFormat("Stage {0}", idx++);
				sb.AppendLine();
				sb.AppendLineFormat("Letters: {0}", stage.Letters.Replace(" ", ", "));
				sb.AppendLine();
				sb.AppendLineFormat("New Sight Words: {0}", stage.SightWords.Replace(" ", ", "));

				Array.Sort(stage.Words);
				sb.AppendLineFormat("Decodable Words: {0}", string.Join(" ", stage.Words));
				sb.AppendLine();

			}

			// complete word list
			var words = allWords.Split(new[] { '\t' });
			Array.Sort(words);
			sb.AppendLine();
			sb.AppendLine("Complete Word List");
			sb.AppendLine(string.Join(" ", words));

			// write the file
			var fileName = Path.Combine(CurrentBook.CollectionSettings.FolderPath, "Decodable Books Letters and Words.txt");
			File.WriteAllText(fileName, sb.ToString(), Encoding.UTF8);

			// open the file
			PathUtilities.OpenFileInApplication(fileName);
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

		private static void OpenTextsFolder()
		{
			if (CurrentBook.CollectionSettings.SettingsFilePath == null)
				return;
			var path = Path.Combine(Path.GetDirectoryName(CurrentBook.CollectionSettings.SettingsFilePath), "Sample Texts");
			if (!Directory.Exists(path))
				Directory.CreateDirectory(path);
			PathUtilities.OpenDirectoryInExplorer(path);
		}
	}
}
