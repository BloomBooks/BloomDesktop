﻿using System;
using System.Collections.Generic;
using System.IO;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using SIL.Code;
using SIL.IO;

namespace Bloom.Utils
{
	/// <summary>
	/// Allows doing various zip operations more robustly than SharpZipLib.
	/// The chosen operations are a bit specific to Bloom in some cases, but so far this is
	/// a private library.
	/// </summary>
	public class RobustZip
	{
		public static string GetComment(string zipPath)
		{
			return RetryUtility.Retry(() =>
			{
				using (var zipFile = new ZipFile(zipPath))
				{
					return zipFile.ZipFileComment;
				}
			});
		}

		public static void WriteZipComment(string comment, string zipPath)
		{
			RetryUtility.Retry(() =>
			{
				using (var zipFile = new ZipFile(zipPath))
				{
					zipFile.BeginUpdate();
					zipFile.SetComment(comment);
					zipFile.CommitUpdate();
				}
			});
		}

		/// <summary>
		/// Get the content of the specified entry in the specified zip file as a string.
		/// </summary>
		public static string GetZipEntryContent(string zipPath, string entryName)
		{
			string result = "error";
			RetryUtility.Retry(() =>
			{
				using (var zipFile = new ZipFile(zipPath))
				{
					var entry = zipFile.GetEntry(entryName);
					if (entry == null)
						result = "";
					else
					{
						using (var instream = zipFile.GetInputStream(entry))
						{
							using (var reader = new StreamReader(instream))
							{
								// don't just use Return here; it returns from the function being retried, not this whole method.
								result = reader.ReadToEnd();
							}
						}
					}
				}
			});
			return result;
		}

		// We shouldn't store these in the zip, and we don't want to try to overwrite them if we have stored them.
		private static string[] _systemFilesToIgnore = new[] { "thumbs.db", "desktop.ini" };

		private static bool ShouldFileBeIgnored(string path)
		{
			if (RobustFile.Exists(path))
			{
				// Don't save or overwrite hidden files: Bloom never hides any of its files anyway.
				var attrs = RobustFile.GetAttributes(path);
				if (attrs.HasFlag(FileAttributes.Hidden))
					return true;
			}
			// The listed files are usually hidden, but let's be sure.
			return _systemFilesToIgnore.Contains(Path.GetFileName(path).ToLowerInvariant());
		}

		public static void UnzipDirectory(string destFolder, string zipPath)
		{
			byte[] buffer = new byte[4096]; // 4K is optimum
			RetryUtility.Retry(() =>
			{
				using (var zipFile = new ZipFile(zipPath))
				{
					foreach (ZipEntry entry in zipFile)
					{
						var fullOutputPath = Path.Combine(destFolder, entry.Name);
						if (entry.IsDirectory)
						{
							Directory.CreateDirectory(fullOutputPath);
							// In the SharpZipLib code, IsFile and IsDirectory are not defined exactly as inverse: a third
							// (or fourth) type of entry might be possible.  In practice in .bloomSource files, this should not be
							// an issue.
							continue;
						}
						if (ShouldFileBeIgnored(fullOutputPath))
							continue;

						var directoryName = Path.GetDirectoryName(fullOutputPath);
						if (!String.IsNullOrEmpty(directoryName))
							Directory.CreateDirectory(directoryName);
						using (var instream = zipFile.GetInputStream(entry))
						{
							using (var writer = RobustFile.Create(fullOutputPath))
							{
								StreamUtils.Copy(instream, writer, buffer);
							}
						}
					}
				}
			});
		}

		/// <summary>
		/// Write the named files in the specified source folder to the specified zip file.
		/// </summary>
		public static void WriteFilesToZip(string[] names, string sourceFolder, string destPath)
		{
			RetryUtility.Retry(() =>
			{
				Directory.CreateDirectory(Path.GetDirectoryName(destPath));
				var zipFile = new BloomZipFile(destPath);
				foreach (var name in names)
				{
					var path = Path.Combine(sourceFolder, name);
					if (!RobustFile.Exists(path))
						continue;
					if (ShouldFileBeIgnored(path))
						continue;
					zipFile.AddTopLevelFile(path, true);
				}

				zipFile.Save();
			});
		}

		public static void WriteAllTopLevelFilesToZip(string destPath, string sourceDir)
		{
			RetryUtility.Retry(() =>
			{
				Directory.CreateDirectory(Path.GetDirectoryName(destPath));
				var zipFile = new BloomZipFile(destPath);
				foreach (var path in Directory.EnumerateFiles(sourceDir))
				{
					if (ShouldFileBeIgnored(path))
						continue;
					zipFile.AddTopLevelFile(path);
				}

				zipFile.Save();
			});
		}

		/// <summary>
		/// Extract the files in the zip to the specified destination. Then, call filesToDeleteIfNotInZip
		/// and if any of the files it returned were not in the zip, delete them from the destFolder. 
		/// </summary>
		public static void ExtractFolderFromZip(string destFolder, string zipPath,
			Func<HashSet<string>> filesToDeleteIfNotInZip)
		{
			byte[] buffer = new byte[4096]; // 4K is optimum
			var filesInZip = new HashSet<string>();
			RetryUtility.Retry(() =>
			{
				using (var zipFile = new ZipFile(zipPath))
				{
					foreach (ZipEntry entry in zipFile)
					{
						filesInZip.Add(entry.Name);
						var fullOutputPath = Path.Combine(destFolder, entry.Name);
						if (ShouldFileBeIgnored(fullOutputPath))
							continue;

						var directoryName = Path.GetDirectoryName(fullOutputPath);
						if (!String.IsNullOrEmpty(directoryName))
							Directory.CreateDirectory(directoryName);
						using (var instream = zipFile.GetInputStream(entry))
						using (var writer = RobustFile.Create(fullOutputPath))
						{
							StreamUtils.Copy(instream, writer, buffer);
						}
					}
				}
			});

			// Remove any sharing-eligible files that are NOT in the zip
			var filesToDelete = filesToDeleteIfNotInZip();
			filesToDelete.ExceptWith(filesInZip);
			foreach (var discard in filesToDelete)
			{
				RobustFile.Delete(Path.Combine(destFolder, discard));
			}
		}
	}
}
