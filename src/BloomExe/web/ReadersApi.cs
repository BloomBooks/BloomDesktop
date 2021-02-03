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
using Amazon.Runtime.Internal.Util;
using Bloom.Book;
using Bloom.Edit;
using Bloom.Properties;
using Bloom.web.controllers;
using L10NSharp;
using Newtonsoft.Json.Linq;
using SIL.PlatformUtilities;

namespace Bloom.Api
{
	/// <summary>
	/// This class handles requests from the Decodable and Leveled Readers tools, as well as from the Reader Setup dialog.
	/// It reads and writes the reader tools settings file, and retrieves files and other information used by the
	/// reader tools.
	/// </summary>
	class ReadersApi : IDisposable
	{
		private readonly BookSelection _bookSelection;
		private const string kSynphonyFileNameSuffix = "_lang_data.js";
		private static readonly IEqualityComparer<string> _equalityComparer = new InsensitiveEqualityComparer();
		private static readonly char[] _allowedWordsDelimiters = {',', ';', ' ', '\t', '\r', '\n'};

		private FileSystemWatcher _sampleTextsWatcher;
		private bool _sampleTextsChanged = true;

		public ReadersApi(BookSelection _bookSelection)
		{
			this._bookSelection = _bookSelection;
		}

		private enum WordFileType
		{
			SampleFile,
			AllowedWordsFile
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler("collection/defaultFont", request =>
			{
				var bookFontName = _bookSelection.CurrentSelection.BookData.Language1.FontName;
				if(String.IsNullOrEmpty(bookFontName))
					bookFontName = "sans-serif";
				request.ReplyWithText(bookFontName);
			}, handleOnUiThread: false);

			apiHandler.RegisterEndpointHandler("readers/ui/.*", HandleRequest, true);
			apiHandler.RegisterEndpointHandler("readers/io/.*", HandleRequest, false);
			apiHandler.RegisterEndpointHandler("directoryWatcher/", ProcessDirectoryWatcher, false);

			//we could do them all like this:
			//server.RegisterEndpointHandler("readers/loadReaderToolSettings", r=> r.ReplyWithJson(GetDefaultReaderSettings(r.CurrentCollectionSettings)));
		}

		// The current book we are editing. Currently this is needed so we can return all the text, to enable JavaScript to update
		// whole-book counts. If we ever support having more than one book open, ReadersHandler will need to stop being static, or
		// some similar change. But by then, we may have the whole book in the main DOM, anyway, and getTextOfPages may be obsolete.
		private Book.Book CurrentBook { get { return _bookSelection.CurrentSelection; } }


		public void HandleRequest(ApiRequest request)
		{
			if (CurrentBook == null)
			{
				Debug.Fail("BL-836 reproduction?");
				// ReSharper disable once HeuristicUnreachableCode
				request.Failed("CurrentBook is null");
				return;
			}
			if (request.CurrentCollectionSettings == null)
			{
				Debug.Fail("BL-836 reproduction?");
				// ReSharper disable once HeuristicUnreachableCode
				request.Failed("CurrentBook.CollectionSettings is null");
				return;
			}

			var lastSegment = request.LocalPath().Split(new char[] {'/'}).Last();

			switch (lastSegment)
			{
				case "test":
					request.PostSucceeded();
					break;

				case "readerToolSettings":
					if(request.HttpMethod == HttpMethods.Get)
						request.ReplyWithJson(GetReaderSettings(request.CurrentBook.BookData));
					else
					{
						var path = DecodableReaderToolSettings.GetReaderToolsSettingsFilePath(request.CurrentCollectionSettings);
						var content = request.RequiredPostJson();
						RobustFile.WriteAllText(path, content, Encoding.UTF8);
						request.PostSucceeded();
					}
					break;


				//note, this endpoint is confusing because it appears that ultimately we only use the word list out of this file (see "sampleTextsList").
				//This ends up being written to a ReaderToolsWords-xyz.json (matching its use, if not it contents).
				case "synphonyLanguageData":
					//This is the "post". There is no direct "get", but the name of the file is given in the "sampleTextList" reply, below.
					// We've had situations (BL-4313 and friends) where reading the posted data fails. This seems to be due to situations
					// where we have a very large block of data and are rapidly switching between books. But as far as I can tell, the only
					// case where it's at all important to capture the new language data is if the user has been changing settings and
					// in particular editing the word list. Timing out the save in that situation seems very unlikely to fail.
					// So, in the interests of preventing the crash when switching books fast, we will ignore failure to read all the
					// json, and just not update the file. We would in any case keep only the version of the data sent to us by
					// the last book which sends it, and that one is unlikely to get interrupted.
					string langdata;
					try
					{
						langdata = request.RequiredPostJson();
					}
					catch (IOException e)
					{
						SIL.Reporting.Logger.WriteError("Saving synphonyLanguageData failed to get Json", e);
						break;
					}

					SaveSynphonyLanguageData(langdata);
					request.PostSucceeded();
					break;

				case "sampleTextsList":
					//note, as part of this reply, we send the path of the "ReaderToolsWords-xyz.json" which is *written* by the "synphonyLanguageData" endpoint above
					request.ReplyWithText(GetSampleTextsList(request.CurrentCollectionSettings.SettingsFilePath));
					break;

				case "sampleFileContents":
					request.ReplyWithText(GetTextFileContents(request.RequiredParam("fileName"), WordFileType.SampleFile));
					break;

				case "textOfContentPages":
					request.ReplyWithText(GetTextOfContentPagesAsJson());
					break;

				case "makeLetterAndWordList":
					MakeLetterAndWordList(request.RequiredPostValue("settings"), request.RequiredPostValue("allWords"));
					request.PostSucceeded();
					break;

				case "openTextsFolder":
					OpenTextsFolder();
					request.PostSucceeded();
					break;

				case "chooseAllowedWordsListFile":
					lock (request)
					{
						request.ReplyWithText(ShowSelectAllowedWordsFileDialog());
					}
					break;

				case "allowedWordsList":
					switch (request.HttpMethod)
					{
						case HttpMethods.Delete:
							RecycleAllowedWordListFile(request.RequiredParam("fileName"));
							request.PostSucceeded();
							break;
						case HttpMethods.Get:
							var fileName = request.RequiredParam("fileName");
							request.ReplyWithText(RemoveEmptyAndDupes(GetTextFileContents(fileName, WordFileType.AllowedWordsFile)));
							break;
						default:
							request.Failed("Http verb not handled");
							break;
					}
					break;
				case "defaultLevel":
					if (request.HttpMethod == HttpMethods.Get)
					{
						request.ReplyWithText(Settings.Default.CurrentLevel.ToString());
					}
					else
					{
						int level;
						if (int.TryParse(request.RequiredParam("level"), out level))
						{
							Settings.Default.CurrentLevel = level;
							Settings.Default.Save();
						}
						else
						{
							// Don't think any sort of runtime failure is worthwhile here.
							Debug.Fail("could not parse level number");
						}
						request.PostSucceeded(); // technically it didn't if we didn't parse the number
					}
					break;
				case "defaultStage":
					if (request.HttpMethod == HttpMethods.Get)
					{
						request.ReplyWithText(Settings.Default.CurrentStage.ToString());
					}
					else
					{
						int stage;
						if (int.TryParse(request.RequiredParam("stage"), out stage))
						{
							Settings.Default.CurrentStage = stage;
							Settings.Default.Save();
						}
						else
						{
							// Don't think any sort of runtime failure is worthwhile here.
							Debug.Fail("could not parse stage number");
						}
						request.PostSucceeded(); // technically it didn't if we didn't parse the number
					}
					break;
				default:
					request.Failed("Don't understand '" + lastSegment + "' in " + request.LocalPath());
					break;
			}
		}

		/// <summary>
		/// Needs to return a json string with the page guid and the bloom-content1 text of each non-x-matter page
		/// </summary>
		/// <returns></returns>
		private string GetTextOfContentPagesAsJson()
		{
			var pageTexts = new List<string>();

			var pages = CurrentBook.RawDom.SafeSelectNodes("//div[" + GenerateXPathClassStringSearch("bloom-page") + "]")
				.Cast<XmlElement>()
				.Where(p =>
				{
					var cls = " " + p.Attributes["class"].Value + " ";
					return !cls.Contains(" bloom-frontMatter ") && !cls.Contains(" bloom-backMatter ");
				});
			var xpathToTextContent = ".//div[not(" + GenerateXPathClassStringSearch("bloom-imageDescription") + ")]/div[" + GenerateXPathClassStringSearch("bloom-content1") + "]";
			foreach (var page in pages)
			{
				var pageWords = String.Empty;
				foreach (XmlElement node in page.SafeSelectNodes(xpathToTextContent))
					pageWords += " " + node.InnerText;

				pageTexts.Add("\"" + page.GetAttribute("id") + "\":\"" + EscapeJsonValue(pageWords.Trim()) + "\"");
			}

			return "{" + String.Join(",", pageTexts.ToArray()) + "}";
		}

		private static string GenerateXPathClassStringSearch(string className)
		{
			return $"contains(concat(' ', @class, ' '), ' {className} ')";
		}

		private static string EscapeJsonValue(string value)
		{
			// As http://stackoverflow.com/questions/42068/how-do-i-handle-newlines-in-json attempts to explain,
			// it takes TWO backslashes before r or n in JSON to get an actual embedded newline into the string.
			// That's from the perspective of seeing it in C# or the debugger.  It's only one real backslash.
			// See also https://silbloom.myjetbrains.com/youtrack/issue/BL-3498.  If we put two real backslashes
			// in the string, represented by four backslashes here, before the r or n, then javascript receives
			// two real backslashes followed by r or n, not a carriage return or linefeed character.
			return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
		}

		private string GetSampleTextsList(string settingsFilePath)
		{
			var path = Path.Combine(Path.GetDirectoryName(settingsFilePath), "Sample Texts");
			if (!Directory.Exists(path))
				Directory.CreateDirectory(path);

			var fileList1 = new List<string>();
			var langFileName = String.Format(DecodableReaderToolSettings.kSynphonyLanguageDataFileNameFormat,
				CurrentBook.BookData.Language1.Iso639Code);
			var langFile = Path.Combine(path, langFileName);

			// if the Sample Texts directory is empty, check for ReaderToolsWords-<iso>.json in ProjectContext.GetBloomAppDataFolder()
			if (DirectoryHelper.IsEmpty(path, true))
			{
				var bloomAppDirInfo = new DirectoryInfo(ProjectContext.GetBloomAppDataFolder());

				// get the most recent file
				var foundFile = bloomAppDirInfo.GetFiles(langFileName, SearchOption.AllDirectories).OrderByDescending(fi => fi.LastWriteTime).FirstOrDefault();

				if (foundFile != null)
				{
					// copy it
					RobustFile.Copy(Path.Combine(foundFile.DirectoryName, foundFile.Name), langFile);
				}

				return String.Empty;
			}

			// first look for ReaderToolsWords-<iso>.json
			if (RobustFile.Exists(langFile))
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

			return String.Join("\r", fileList1.ToArray());
		}

		/// <summary>Gets the contents of a Text file</summary>
		/// <param name="fileName"></param>
		/// <param name="wordFileType"></param>
		private string GetTextFileContents(string fileName, WordFileType wordFileType)
		{
			var path = Path.Combine(Path.GetDirectoryName(CurrentBook.CollectionSettings.SettingsFilePath),
				wordFileType == WordFileType.AllowedWordsFile ? "Allowed Words" : "Sample Texts");

			path = Path.Combine(path, fileName);

			if (!RobustFile.Exists(path)) return String.Empty;

			// first try utf-8/ascii encoding (the .Net default)
			var text = RobustFile.ReadAllText(path);

			// If the "unknown" character (65533) is present, C# did not sucessfully decode the file. Try the system default encoding and codepage.
			if (text.Contains((char)65533))
				text = RobustFile.ReadAllText(path, Encoding.Default);

			return text;
		}

		private static string GetReaderSettings(BookData bookData)
		{
			var settingsPath = DecodableReaderToolSettings.GetReaderToolsSettingsFilePath(bookData.CollectionSettings);
			var jsonSettings = "";
			// if file exists, return current settings
			if (RobustFile.Exists(settingsPath))
			{
				var result = RobustFile.ReadAllText(settingsPath, Encoding.UTF8);
				if (!string.IsNullOrWhiteSpace(result))
					jsonSettings = result;
			}

			if (jsonSettings.Length > 0)
			{
				dynamic fixedSettings = JsonConvert.DeserializeObject(jsonSettings);
				fixedSettings.writingSystemTag = bookData.Language1.Iso639Code;
				return JsonConvert.SerializeObject(fixedSettings);
			}
			// file does not exist, so make a new one
			// The literal string here defines our default reader settings for a collection.
			var settingsString = "{\"writingSystemTag\": \"" + bookData.Language1.Iso639Code+"\", "
			                     +"\"letters\":\"a b c d e f g h i j k l m n o p q r s t u v w x y z\","
				+ $"\"lang\":\"{bookData.Language1.Iso639Code}\","
			                     + "\"moreWords\":\"\","
				+ "\"stages\":[{\"letters\":\"\",\"sightWords\":\"\"}],"
				+ "\"levels\":[{\"maxWordsPerSentence\":2,\"maxWordsPerPage\":2,\"maxWordsPerBook\":20,\"maxUniqueWordsPerBook\":0,\"thingsToRemember\":[]},"
					+ "{\"maxWordsPerSentence\":5,\"maxWordsPerPage\":5,\"maxWordsPerBook\":23,\"maxUniqueWordsPerBook\":8,\"thingsToRemember\":[]},"
					+ "{\"maxWordsPerSentence\":7,\"maxWordsPerPage\":10,\"maxWordsPerBook\":72,\"maxUniqueWordsPerBook\":16,\"thingsToRemember\":[]},"
					+ "{\"maxWordsPerSentence\":8,\"maxWordsPerPage\":18,\"maxWordsPerBook\":206,\"maxUniqueWordsPerBook\":32,\"thingsToRemember\":[]},"
					+ "{\"maxWordsPerSentence\":12,\"maxWordsPerPage\":25,\"maxWordsPerBook\":500,\"maxUniqueWordsPerBook\":64,\"thingsToRemember\":[]},"
					+ "{\"maxWordsPerSentence\":20,\"maxWordsPerPage\":50,\"maxWordsPerBook\":1000,\"maxUniqueWordsPerBook\":0,\"thingsToRemember\":[]}]}";
			RobustFile.WriteAllText(settingsPath, settingsString);

			return settingsString;
		}

		/// <summary>
		/// The SaveFileDialog must run on a STA thread.
		/// </summary>
		private void MakeLetterAndWordList(string jsonSettings, string allWords)
		{
			// load the settings
			dynamic settings = JsonConvert.DeserializeObject(jsonSettings);

			// format the output
			var sb = new StringBuilder();
			var str = LocalizationManager.GetString("EditTab.Toolbox.DecodableReaderTool.LetterWordReportMessage",
				"The following is a generated report of the decodable stages for {0}.  You can make any changes you want to this file, but Bloom will not notice your changes.  It is just a report.");
			sb.AppendLineFormat(str, CurrentBook.BookData.Language1.Name);

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
				sb.AppendLineFormat(LocalizationManager.GetString("EditTab.Toolbox.DecodableReaderTool.LetterWordReportNewDecodableWords", "New Decodable Words: {0}"), String.Join(" ", stageWords));
				sb.AppendLine();
			}

			// complete word list
			var words = allWords.Split(new[] { '\t' });
			Array.Sort(words);
			sb.AppendLine();
			sb.AppendLine(LocalizationManager.GetString("EditTab.Toolbox.DecodableReaderTool.LetterWordReportWordList", "Complete Word List"));
			sb.AppendLine(String.Join(" ", words));

			// write the file
			var fileName = Path.Combine(CurrentBook.CollectionSettings.FolderPath, "Decodable Books Letters and Words.txt");
			RobustFile.WriteAllText(fileName, sb.ToString(), Encoding.UTF8);

			// open the file
			PathUtilities.OpenFileInApplication(fileName);
		}

		/// <summary></summary>
		/// <param name="jsonString">The theOneLanguageDataInstance as json</param>
		/// <returns>OK</returns>
		private void SaveSynphonyLanguageData(string jsonString)
		{
			// insert LangName and LangID if missing
			if (jsonString.Contains("\"LangName\":\"\""))
				jsonString = jsonString.Replace("\"LangName\":\"\"", "\"LangName\":\"" + CurrentBook.BookData.Language1.Name + "\"");

			if (jsonString.Contains("\"LangID\":\"\""))
				jsonString = jsonString.Replace("\"LangID\":\"\"", "\"LangID\":\"" + CurrentBook.BookData.Language1.Iso639Code + "\"");

			var fileName = String.Format(DecodableReaderToolSettings.kSynphonyLanguageDataFileNameFormat, CurrentBook.BookData.Language1.Iso639Code);
			fileName = Path.Combine(CurrentBook.CollectionSettings.FolderPath, fileName);

			RobustFile.WriteAllText(fileName, jsonString, Encoding.UTF8);
		}

		private void OpenTextsFolder()
		{
			if (CurrentBook.CollectionSettings.SettingsFilePath == null)
				return;
			var path = Path.Combine(Path.GetDirectoryName(CurrentBook.CollectionSettings.SettingsFilePath), "Sample Texts");
			if (!Directory.Exists(path))
				Directory.CreateDirectory(path);

			PathUtilities.OpenDirectoryInExplorer(path);
			CommonApi.BringFolderToFrontInLinux("Sample Texts");
		}

		private string ShowSelectAllowedWordsFileDialog()
		{
			var returnVal = "";

			var destPath = Path.Combine(Path.GetDirectoryName(CurrentBook.CollectionSettings.SettingsFilePath), "Allowed Words");
			if (!Directory.Exists(destPath)) Directory.CreateDirectory(destPath);

			var textFiles = LocalizationManager.GetString("EditTab.Toolbox.DecodableReaderTool.FileDialogTextFiles", "Text files");
			var dlg = new DialogAdapters.OpenFileDialogAdapter
			{
				Multiselect = false,
				CheckFileExists = true,
				Filter = String.Format("{0} (*.txt;*.csv;*.tab)|*.txt;*.csv;*.tab", textFiles)
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
						while (RobustFile.Exists(Path.Combine(destPath, destFile)))
						{
							destFile = Path.GetFileName(srcFile);
							var fileExt = Path.GetExtension(srcFile);
							destFile = destFile.Substring(0, destFile.Length - fileExt.Length) + " - Copy";
							if (++i > 1) destFile += " " + i;
							destFile += fileExt;
						}

						RobustFile.Copy(srcFile, Path.Combine(destPath, destFile));
					}

					returnVal = destFile;
				}
			}

			return returnVal;
		}

		private void RecycleAllowedWordListFile(string fileName)
		{
			var folderPath = Path.Combine(Path.GetDirectoryName(CurrentBook.CollectionSettings.SettingsFilePath), "Allowed Words");
			var fullFileName = Path.Combine(folderPath, fileName);

			if (RobustFile.Exists(fullFileName))
				PathUtilities.DeleteToRecycleBin(fullFileName);
		}

		private static string RemoveEmptyAndDupes(string fileText)
		{
			// this splits the text into an array of individual words and removes empty entries
			var words = fileText.Split(_allowedWordsDelimiters, StringSplitOptions.RemoveEmptyEntries);

			// this removes duplicates entries from the array using case-insensiteve comparison
			words = words.Distinct(_equalityComparer).ToArray();

			// join the words back into a delimited string to be sent to the browser
			return String.Join(",", words);
		}


		public void ProcessDirectoryWatcher(ApiRequest request)
		{
			// thread synchronization is done in the calling BloomApiHandler.
			var dirName = request.RequiredPostValue("dir");
			if (dirName == "Sample Texts")
			{
				CheckForSampleTextChanges(request);
			}
			else
			{
				request.Failed();
			}
		}

		private void CheckForSampleTextChanges(ApiRequest request)
		{
			if (_sampleTextsWatcher == null)
			{
				if (string.IsNullOrEmpty(request.CurrentCollectionSettings?.SettingsFilePath))
				{
					// We've had cases (BL-4744) where this is apparently called before CurrentCollectionSettings is
					// established. I'm not sure how this can happen but if we haven't even established a current collection
					// yet I think it's pretty safe to say its sample texts haven't changed since we last read them.
					request.ReplyWithText("no");
					return;
				}
				var path = Path.Combine(Path.GetDirectoryName(request.CurrentCollectionSettings.SettingsFilePath), "Sample Texts");
				if (!Directory.Exists(path))
					Directory.CreateDirectory(path);

				_sampleTextsWatcher = new FileSystemWatcher { Path = path };
				_sampleTextsWatcher.Created += SampleTextsOnChange;
				_sampleTextsWatcher.Changed += SampleTextsOnChange;
				_sampleTextsWatcher.Renamed += SampleTextsOnChange;
				_sampleTextsWatcher.Deleted += SampleTextsOnChange;
				_sampleTextsWatcher.EnableRaisingEvents = true;
			}

			lock (_sampleTextsWatcher)
			{
				var hasChanged = _sampleTextsChanged;

				// Reset the changed flag.
				// NOTE: we are only resetting the flag if it was "true" when we checked in case the FileSystemWatcher detects a change
				// after we check the flag but we reset it to false before we check again.
				if (hasChanged)
					_sampleTextsChanged = false;

				request.ReplyWithText(hasChanged ? "yes" : "no");
			}
		}

		private void SampleTextsOnChange(object sender, FileSystemEventArgs fileSystemEventArgs)
		{
			lock (_sampleTextsWatcher)
			{
				_sampleTextsChanged = true;
			}
		}

		protected void Dispose(bool fDisposing)
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
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		/// <summary>Used when removing duplicates from word lists</summary>
		private class InsensitiveEqualityComparer : IEqualityComparer<string>
		{
			public bool Equals(string x, string y)
			{
				return String.Equals(x, y, StringComparison.InvariantCultureIgnoreCase);
			}

			public int GetHashCode(string obj)
			{
				return obj.ToLowerInvariant().GetHashCode();
			}
		}
	}
}
