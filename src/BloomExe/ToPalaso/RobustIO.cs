using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Gtk;
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
				throw new ArgumentException($"The path '{path}' does not exist.");
			}
		}

		public static FileStream Open(string filePath, FileMode mode)
		{
			FileStream stream = null;
			RetryUtility.Retry(
				() =>
				{
					stream = File.Open(filePath, mode,
								mode == FileMode.Append ? FileAccess.Write : FileAccess.ReadWrite, FileShare.None);
				}, 5);
			return stream;
		}
		public static FileStream Open(string filePath, FileMode mode, FileAccess access, FileShare share)
		{
			FileStream stream = null;
			RetryUtility.Retry(
				() =>
				{
					stream = File.Open(filePath, mode, access, share);
				}, 5);
			return stream;
		}

		public static FileStream OpenRead(string path)
		{
			FileStream stream = null;
			RetryUtility.Retry(
				() =>
				{
					stream = File.OpenRead(path);
				}, 5);
			return stream;
		}

		public static bool IsFileLocked(string filePath)
		{
			try
			{
				// If something recently changed it we might get some spurious failures
				// to open it for modification.
				// BL-10139 indicated that the default 10 retries over two seconds
				// is sometimes not enough, so I've increased it here.
				// No guarantee that even 5s is enough if Dropbox is busy syncing a large
				// file across a poor internet, but I think after that it's better to give
				// the user a failed message.
				RetryUtility.Retry(() =>
				{
					using (File.Open(filePath, FileMode.Open))
					{
					}
				}, maxRetryAttempts: 25);
			}
			catch (IOException e)
			{
				Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(e);
				return true;
			}
			catch (UnauthorizedAccessException e)
			{
				Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(e);
				return true;
			}

			return false;
		}

		public static void AppendAllText(string path, string contents)
		{
			RetryUtility.Retry(() => { File.AppendAllText(path, contents); });
		}

		public static void WriteAllLines(string path, IEnumerable<string> contents)
		{
			// TODO - Create SIL.IO.RobustFile.WriteAllLines (where it might want even more robust-ness;
			// see other RobustFile.WriteX methods).
			RetryUtility.Retry(() => File.WriteAllLines(path, contents));
		}

		public static System.Security.AccessControl.FileSecurity GetAccessControl(string filePath)
		{
			return RetryUtility.Retry(() => File.GetAccessControl(filePath));
		}

		/// <summary>
		/// Reads all text (like RobustFile.ReadAllText) from a file. Works even if that file may
		/// be written to one or more times.
		/// e.g. reading the progress output file of ffmpeg while ffmpeg is running.
		/// </summary>
		/// <param name="path">path of the file to read</param>
		/// <returns>the contents of the file as a string</returns>
		public static string ReadAllTextFromFileWhichMightGetWrittenTo(string path)
		{
			return RetryUtility.Retry(() => ReadAllTextFromFileWhichMightGetWrittenToInternal(path));
		}

		private static string ReadAllTextFromFileWhichMightGetWrittenToInternal(string path)
		{
			using (FileStream logFileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			using (StreamReader logFileReader = new StreamReader(logFileStream))
			{
				StringBuilder sb = new StringBuilder();

				char[] buffer = new char[4096];
				while (!logFileReader.EndOfStream)
				{
					logFileReader.ReadBlock(buffer, 0, buffer.Length);
					sb.Append(buffer);
				}

				return sb.ToString();
			}
		}
	}
}
