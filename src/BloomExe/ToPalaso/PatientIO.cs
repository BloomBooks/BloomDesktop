using Bloom.ImageProcessing;
using SIL.IO;
using SIL.Windows.Forms.ClearShare;
using System;
using System.Collections.Generic;
using System.IO;
using TidyManaged;

namespace Bloom.Utils
{
	public class PatientIO
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
			Patient.Retry(() =>
				EnumerateFilesInDirectoryInternal(folderPath, searchPattern, option, fileSet),
				Patient.kDefaultMaxRetryAttempts,
				Patient.kDefaultRetryDelay,
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
			Patient.Retry(() =>
			{
				exists = Directory.Exists(path);
			});
			if (!exists)
			{
				throw new ArgumentException($"The path '{path}' does not exist.");
			}
		}

		public static Document DocumentFromFile(string filePath)
		{
			return Patient.Retry(() => Document.FromFile(filePath));
		}

		/// <summary>
		/// Get the image metadata from the file as reliably as possible.
		/// </summary>
		public static Metadata MetadataFromFile(string path)
		{
			// Books sometimes use image files that are mislabeled.  JPEG files sometimes have been given
			// .png extensions, and it's likely that PNG files have been given .jpg extensions.  TagLib crashes
			// trying to read the metadata in such cases, so we prevent that particular crash in this method.
			if (Path.GetExtension(path).ToLowerInvariant() == ".png" && ImageUtils.IsJpegFile(path))
			{
				using (var jpegFile = TempFile.WithExtension(".jpg"))
				{
					PatientFile.Copy(path, jpegFile.Path, true);
					return MetadataFromFileInternal(jpegFile.Path);
				}
			}
			if (ImageUtils.HasJpegExtension(path) && ImageUtils.IsPngFile(path))
			{
				using (var pngFile = TempFile.WithExtension(".png"))
				{
					PatientFile.Copy(path, pngFile.Path, true);
					return MetadataFromFileInternal(pngFile.Path);
				}
			}
			// Assume everything is okay.
			return MetadataFromFileInternal(path);
		}

		private static Metadata MetadataFromFileInternal(string path)
		{
			return Patient.Retry(() => Metadata.FromFile(path));
		}
	}
}
