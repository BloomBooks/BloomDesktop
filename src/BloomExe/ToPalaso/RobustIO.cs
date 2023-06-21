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
			// Directory.EnumerateFiles returns files incrementally, not waiting until it has
			// accessed the whole directory. Thus retries of this method could return multiple
			// instances of some file paths, which is undesirable.  We accumulate the files in
			// a HashSet to avoid duplicates in case the operation has to be retried.  This
			// unavoidably slows things down since we have to wait until all the files are
			// gathered before any are returned.
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

		private static void EnumerateFilesInDirectoryInternal(string folderPath, string searchPattern, SearchOption option, HashSet<string> fileSet)
		{
			foreach (var file in System.IO.Directory.EnumerateFiles(folderPath, searchPattern, option))
				fileSet.Add(file);
		}

		public static void RequireThatDirectoryExists(string path)
		{
			bool exists = false;
			RetryUtility.Retry(() =>
			{
				exists = Directory.Exists(path);
			});
			if (!exists)
			{
				throw new ArgumentException("The path '\" + path + \"' does not exist.\"");
			}
		}
	}
}
