using System.Text;
using System.Linq;
using System.IO;
using Bloom.Collection;

namespace Bloom.web
{
	/// <summary>
	/// This class handles requests from the Decodable and Leveled Readers tools, as well as from the Reader Setup dialog.
	/// It reads and writes the reader tools settings file, and retrieves files and other information used by the
	/// reader tools.
	/// </summary>
	static class ReadersHandler
	{
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
			}

			return false;
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
	}
}
