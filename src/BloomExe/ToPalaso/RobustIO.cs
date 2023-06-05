using System;
using System.Collections.Generic;
using System.IO;
using SIL.Code;

namespace Bloom.ToPalaso
{
	public class RobustIO
	{
		/// <summary>
		/// Robustly try to enumerate all of the files in a directory.  Unfortunately, this makes the
		/// method wait until all the files are gathered before any are returned.
		/// </summary>
		public static IEnumerable<string> EnumerateFilesInDirectory(string folderPath, string searchPattern = "*", SearchOption option=SearchOption.TopDirectoryOnly)
		{
			var fileSet = new HashSet<string>();
			RetryUtility.Retry(() =>
				EnumerateFilesInDirectoryInternal(folderPath, searchPattern, option, fileSet),
				RetryUtility.kDefaultMaxRetryAttempts,
				RetryUtility.kDefaultRetryDelay,
				new HashSet<Type>
				{
					Type.GetType("System.IO.IOException"),
					Type.GetType("System.Runtime.InteropServices.ExternalException")
				});
			return fileSet;
		}

		private static IEnumerable<string> EnumerateFilesInDirectoryInternal(string folderPath, string searchPattern, SearchOption option, HashSet<string> fileSet)
		{
			foreach (var file in System.IO.Directory.EnumerateFiles(folderPath, searchPattern, option))
				fileSet.Add(file);
			return fileSet;
		}
	}
}
