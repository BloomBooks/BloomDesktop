using System.Text;
using System.Linq;
using System.IO;
using System.Xml;
using Bloom.Collection;
using Bloom.ReaderTools;
using Palaso.Xml;

namespace Bloom.web
{
	/// <summary>
	/// This class handles requests from the Decodable and Leveled Readers tools, as well as from the Reader Setup dialog.
	/// It reads and writes the reader tools settings file, and retrieves files and other information used by the
	/// reader tools.
	/// </summary>
	static class ReadersHandler
	{
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
	}
}
