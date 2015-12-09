using System;
using System.Diagnostics;
using System.Text;
using System.Linq;
using System.IO;
using System.Xml;
using Bloom.Collection;
using Newtonsoft.Json;
using SIL.Xml;
using SIL.IO;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using Bloom.Edit;
using L10NSharp;
using Newtonsoft.Json.Linq;

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
		private static readonly IEqualityComparer<string> _equalityComparer = new InsensitiveEqualityComparer();
		private static readonly char[] _allowedWordsDelimiters = {',', ';', ' ', '\t', '\r', '\n'};

		private enum WordFileType
		{
			SampleFile,
			AllowedWordsFile
		}

		// The current book we are editing. Currently this is needed so we can return all the text, to enable JavaScript to update
		// whole-book counts. If we ever support having more than one book open, ReadersHandler will need to stop being static, or
		// some similar change. But by then, we may have the whole book in the main DOM, anyway, and getTextOfPages may be obsolete.
		public static Book.Book CurrentBook { get { return Server.CurrentBook; } }

		/// <summary>
		/// Needs to know the one and only image server to get the current book from it.
		/// </summary>
		public static EnhancedImageServer Server { get; set; }

		public static bool HandleRequest(string localPath, IRequestInfo info, CollectionSettings currentCollectionSettings)
		{
			if (CurrentBook == null || CurrentBook.CollectionSettings == null)
			{
				Debug.Fail("BL-836 reproduction?");
				// ReSharper disable once HeuristicUnreachableCode
				return false;
			}
			var lastSep = localPath.IndexOf("/", StringComparison.Ordinal);
			var lastSegment = (lastSep > -1) ? localPath.Substring(lastSep + 1) : localPath;

			switch (lastSegment)
			{
				case "loadReaderToolSettings":
					info.ContentType = "application/json";
					info.WriteCompleteOutput(GetDefaultReaderSettings(currentCollectionSettings));
					return true;

				case "saveReaderToolSettings":
					var path = DecodableReaderTool.GetDecodableLevelPathName(currentCollectionSettings);
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
					info.ContentType = "text/plain";
					info.WriteCompleteOutput(GetTextFileContents(info.GetQueryString()["data"], WordFileType.SampleFile));
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

				case "selectStageAllowedWordsFile":
					lock (info)
					{
						ChooseAllowedWordListFile(info);
					}
					return true;

				case "getAllowedWordsList":
					info.ContentType = "text/plain";
					info.WriteCompleteOutput(RemoveEmptyAndDupes(GetTextFileContents(info.GetQueryString()["data"], WordFileType.AllowedWordsFile)));
					return true;

				case "recycleAllowedWordsFile":
					RecycleAllowedWordListFile(info.GetPostData()["data"]);
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
			var langFileName = string.Format(DecodableReaderTool.kReaderToolsWordsFileNameFormat, CurrentBook.CollectionSettings.Language1Iso639Code);
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

		/// <summary>Gets the contents of a Text file</summary>
		/// <param name="fileName"></param>
		/// <param name="wordFileType"></param>
		private static string GetTextFileContents(string fileName, WordFileType wordFileType)
		{
			var path = Path.Combine(Path.GetDirectoryName(CurrentBook.CollectionSettings.SettingsFilePath),
				wordFileType == WordFileType.AllowedWordsFile ? "Allowed Words" : "Sample Texts");

			path = Path.Combine(path, fileName);

			if (!File.Exists(path)) return string.Empty;

			// first try utf-8/ascii encoding (the .Net default)
			var text = File.ReadAllText(path);

			// If the "unknown" character (65533) is present, C# did not sucessfully decode the file. Try the system default encoding and codepage.
			if (text.Contains((char)65533))
				text = File.ReadAllText(path, Encoding.Default);

			return text;
		}

		private static string GetDefaultReaderSettings(CollectionSettings currentCollectionSettings)
		{
			var settingsPath = DecodableReaderTool.GetDecodableLevelPathName(currentCollectionSettings);

			// if file exists, return current settings
			if (File.Exists(settingsPath))
				return File.ReadAllText(settingsPath, Encoding.UTF8);

			// file does not exist, so make a new one
			// The literal string here defines our default reader settings for a collection.
			var settingsString = "{\"letters\":\"a b c d e f g h i j k l m n o p q r s t u v w x y z\","
				+ "\"moreWords\":\"\","
				+ "\"stages\":[{\"letters\":\"\",\"sightWords\":\"\"}],"
				+ "\"levels\":[{\"maxWordsPerSentence\":2,\"maxWordsPerPage\":2,\"maxWordsPerBook\":20,\"maxUniqueWordsPerBook\":0,\"thingsToRemember\":[]},"
					+ "{\"maxWordsPerSentence\":5,\"maxWordsPerPage\":5,\"maxWordsPerBook\":23,\"maxUniqueWordsPerBook\":8,\"thingsToRemember\":[]},"
					+ "{\"maxWordsPerSentence\":7,\"maxWordsPerPage\":10,\"maxWordsPerBook\":72,\"maxUniqueWordsPerBook\":16,\"thingsToRemember\":[]},"
					+ "{\"maxWordsPerSentence\":8,\"maxWordsPerPage\":18,\"maxWordsPerBook\":206,\"maxUniqueWordsPerBook\":32,\"thingsToRemember\":[]},"
					+ "{\"maxWordsPerSentence\":12,\"maxWordsPerPage\":25,\"maxWordsPerBook\":500,\"maxUniqueWordsPerBook\":64,\"thingsToRemember\":[]},"
					+ "{\"maxWordsPerSentence\":20,\"maxWordsPerPage\":50,\"maxWordsPerBook\":1000,\"maxUniqueWordsPerBook\":0,\"thingsToRemember\":[]}]}";
			File.WriteAllText(settingsPath, settingsString);

			return settingsString;
		}

		/// <summary>
		/// The SaveFileDialog must run on a STA thread.
		/// </summary>
		private static void MakeLetterAndWordList(string jsonSettings, string allWords)
		{
			// load the settings
			dynamic settings = JsonConvert.DeserializeObject(jsonSettings);

			// format the output
			var sb = new StringBuilder();
			var str = LocalizationManager.GetString("EditTab.Toolbox.DecodableReaderTool.LetterWordReportMessage",
				"The following is a generated report of the decodable stages for {0}.  You can make any changes you want to this file, but Bloom will not notice your changes.  It is just a report.");
			sb.AppendLineFormat(str, CurrentBook.CollectionSettings.Language1Name);

			var idx = 1;
			foreach (var stage in settings.stages)
			{
				sb.AppendLine();
				sb.AppendLineFormat(LocalizationManager.GetString("EditTab.Toolbox.DecodableReaderTool.LetterWordReportStage", "Stage {0}"), idx++);
				sb.AppendLine();
				string letters = stage.letters ?? "";
				sb.AppendLineFormat(LocalizationManager.GetString("EditTab.Toolbox.DecodableReaderTool.LetterWordReportLetters", "Letters: {0}"), letters.Replace(" ", ", "));
				sb.AppendLine();
				string sightWords = stage.sightWords;
				sb.AppendLineFormat(LocalizationManager.GetString("EditTab.Toolbox.DecodableReaderTool.LetterWordReportSightWords", "New Sight Words: {0}"), sightWords.Replace(" ", ", "));

				JArray rawWords = stage.words;
				string[] stageWords = rawWords.Select(x => x.ToString()).ToArray();
				Array.Sort(stageWords);
				sb.AppendLineFormat(LocalizationManager.GetString("EditTab.Toolbox.DecodableReaderTool.LetterWordReportNewDecodableWords", "New Decodable Words: {0}"), string.Join(" ", stageWords));
				sb.AppendLine();
			}

			// complete word list
			var words = allWords.Split(new[] { '\t' });
			Array.Sort(words);
			sb.AppendLine();
			sb.AppendLine(LocalizationManager.GetString("EditTab.Toolbox.DecodableReaderTool.LetterWordReportWordList", "Complete Word List"));
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
				Thread.Sleep(0);

			try
			{
				_savingReaderWords = true;

				// insert LangName and LangID if missing
				if (jsonString.Contains("\"LangName\":\"\""))
					jsonString = jsonString.Replace("\"LangName\":\"\"", "\"LangName\":\"" + CurrentBook.CollectionSettings.Language1Name + "\"");

				if (jsonString.Contains("\"LangID\":\"\""))
					jsonString = jsonString.Replace("\"LangID\":\"\"", "\"LangID\":\"" + CurrentBook.CollectionSettings.Language1Iso639Code + "\"");

				var fileName = string.Format(DecodableReaderTool.kReaderToolsWordsFileNameFormat, CurrentBook.CollectionSettings.Language1Iso639Code);
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

			// BL-673: Make sure the folder comes to the front in Linux
			if (SIL.PlatformUtilities.Platform.IsLinux)
			{
				// allow the external process to execute
				Thread.Sleep(100);

				// if the system has wmctrl installed, use it to bring the folder to the front
				Process.Start(new ProcessStartInfo()
				{
					FileName = "wmctrl",
					Arguments = "-a \"Sample Texts\"",
					UseShellExecute = false,
					ErrorDialog = false // do not show a message if not successful
				});
			}
		}

		private static void ChooseAllowedWordListFile(IRequestInfo info)
		{
			var frm = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Shell);
			// ReSharper disable once PossibleNullReferenceException
			frm.Invoke(new Action<IRequestInfo>(ShowSelectAllowedWordsFileDialog), info);
		}

		private static void ShowSelectAllowedWordsFileDialog(IRequestInfo info)
		{
			var returnVal = "";

			var destPath = Path.Combine(Path.GetDirectoryName(CurrentBook.CollectionSettings.SettingsFilePath), "Allowed Words");
			if (!Directory.Exists(destPath)) Directory.CreateDirectory(destPath);

			var textFiles = LocalizationManager.GetString("EditTab.Toolbox.DecodableReaderTool.FileDialogTextFiles", "Text files");
			var dlg = new OpenFileDialog
			{
				Multiselect = false,
				CheckFileExists = true,
				Filter = string.Format("{0} (*.txt;*.csv;*.tab)|*.txt;*.csv;*.tab", textFiles)
			};
			var result = dlg.ShowDialog();
			if (result == DialogResult.OK)
			{
				var srcFile = dlg.FileName;
				var destFile = Path.GetFileName(srcFile);
				if (destFile != null)
				{
					// if file is in the "Allowed Words" directory, do not try to copy it again.
					if (Path.GetFullPath(srcFile) != Path.Combine(destPath, destFile))
					{
						var i = 0;

						// get a unique destination file name
						while (File.Exists(Path.Combine(destPath, destFile)))
						{
							destFile = Path.GetFileName(srcFile);
							var fileExt = Path.GetExtension(srcFile);
							destFile = destFile.Substring(0, destFile.Length - fileExt.Length) + " - Copy";
							if (++i > 1) destFile += " " + i;
							destFile += fileExt;
						}

						File.Copy(srcFile, Path.Combine(destPath, destFile));
					}

					returnVal = destFile;
				}
			}

			// send to browser
			info.ContentType = "text/plain";
			info.WriteCompleteOutput(returnVal);
		}

		private static void RecycleAllowedWordListFile(string fileName)
		{
			var folderPath = Path.Combine(Path.GetDirectoryName(CurrentBook.CollectionSettings.SettingsFilePath), "Allowed Words");
			var fullFileName = Path.Combine(folderPath, fileName);

			if (File.Exists(fullFileName))
				PathUtilities.DeleteToRecycleBin(fullFileName);
		}

		private static string RemoveEmptyAndDupes(string fileText)
		{
			// this splits the text into an array of individual words and removes empty entries
			var words = fileText.Split(_allowedWordsDelimiters, StringSplitOptions.RemoveEmptyEntries);

			// this removes duplicates entries from the array using case-insensiteve comparison
			words = words.Distinct(_equalityComparer).ToArray();

			// join the words back into a delimited string to be sent to the browser
			return string.Join(",", words);
		}

		/// <summary>Used when removing duplicates from word lists</summary>
		private class InsensitiveEqualityComparer : IEqualityComparer<string>
		{
			public bool Equals(string x, string y)
			{
				return string.Equals(x, y, StringComparison.InvariantCultureIgnoreCase);
			}

			public int GetHashCode(string obj)
			{
				return obj.ToLowerInvariant().GetHashCode();
			}
		}
	}
}
