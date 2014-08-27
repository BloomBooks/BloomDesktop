using System;
using System.Collections.Generic;
using System.IO;
using Bloom.Collection;

namespace Bloom.web
{
	/// <summary>
	/// This class is the server-side protion of the JavaScript directoryWatcher class. It returns the header information for
	/// each file in the requested directory.
	/// </summary>
	public static class DirectoryWatcherHandler
	{
		private const string JavaDateTimeFormat = @"yyyy-MM-dd\THH:mm:ss.fff\Z";

		public static bool HandleRequest(string localPath, IRequestInfo info, CollectionSettings currentCollectionSettings)
		{
			var dirName = info.GetPostData()["dir"];

			if (dirName == "Sample Texts")
			{
				var responseString = GetSampleFileData(currentCollectionSettings.SettingsFilePath);

				info.ContentType = "application/json";
				info.WriteCompleteOutput(responseString);
				return true;
			}

			return false;
		}

		/// <summary>
		/// If files are found, returns a json string like this that contains file name, size and last modified:
		///   { "file1.txt": [ 1024, "2012-04-23T18:25:43.511Z" ], "file2.txt": [ 2048, "2012-04-23T18:25:43.511Z" ] }
		/// </summary>
		/// <param name="settingsFilePath"></param>
		/// <returns></returns>
		public static string GetSampleFileData(string settingsFilePath)
		{
			var files = new List<string>();
			const string format = "\"{0}\":[{1},\"{2}\"]";

			var path = Path.Combine(Path.GetDirectoryName(settingsFilePath), "Sample Texts");
			foreach (var file in Directory.GetFiles(path))
			{
				var info = new FileInfo(file);
				files.Add(string.Format(format, info.Name, info.Length, info.LastWriteTimeUtc.ToString(JavaDateTimeFormat)));
			}

			if (files.Count == 0) return "{}";

			return "{" + String.Join(",", files.ToArray()) + "}";
		}
	}
}
