using System.IO;
using System.Linq;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using SIL.IO;

namespace Bloom
{
	/// <summary>
	/// This just provides a convenient wrapper over a zipping library. It's not really specific to Bloom.
	/// To Use, create a BloomZipFile, add files and directories, then call Save().
	/// </summary>
	public class BloomZipFile
	{
		private ZipOutputStream _zipStream;

		public BloomZipFile(string path)
		{
			var fsOut = RobustFile.Create(path);
			_zipStream = new ZipOutputStream(fsOut);
			_zipStream.SetLevel(9); //REVIEW: what does this mean?
		}

		public void Save()
		{
			_zipStream.IsStreamOwner = true; // makes the Close() also close the underlying stream
			_zipStream.Close();
		}

		public void SetComment(string comment)
		{
			_zipStream.SetComment(comment);
		}

		public void AddTopLevelFile(string path, bool compress=true)
		{
			AddFile(path, Path.GetFileName(path), compress);
		}

		private void AddFile(string path, string entryName, bool compress=true)
		{
			var fi = new FileInfo(path);
			var newEntry = new ZipEntry(entryName) {DateTime = fi.LastWriteTime, Size = fi.Length, IsUnicodeText = true,
				CompressionMethod=compress?CompressionMethod.Deflated:CompressionMethod.Stored};

			_zipStream.PutNextEntry(newEntry);

			// Zip the file in buffered chunks
			var buffer = new byte[4096];
			using (var streamReader = RobustFile.OpenRead(path))
			{
				StreamUtils.Copy(streamReader, _zipStream, buffer);
			}

			_zipStream.CloseEntry();
		}

		/// <summary>
		/// Adds a directory's contents (all files and subdirectories), but not the directory itself.
		/// </summary>
		/// <param name="directoryPath">The directory to add recursively</param>
		/// <param name="extensionsToExclude">An array of extensions to exlude from the zip file, null excludes nothing.</param>
		public void AddDirectoryContents(string directoryPath, string[] extensionsToExclude = null)
		{
			AddDirectory(directoryPath, directoryPath.Length, extensionsToExclude);
		}

		/// <summary>
		/// Adds a directory, along with all files and subdirectories
		/// </summary>
		/// <param name="directoryPath">The directory to add recursively</param>
		/// <param name="extensionsToExclude">An array of extensions to exlude from the zip file, null excludes nothing.</param>
		public void AddDirectory(string directoryPath, string[] extensionsToExclude = null)
		{
			var rootName = Path.GetFileName(directoryPath);
			if (rootName == null)
				return;

			var dirNameOffset = directoryPath.Length - rootName.Length;
			AddDirectory(directoryPath, dirNameOffset, extensionsToExclude);
		}

		public void AddDirectory(string directoryPath, int dirNameOffest, string[] extensionsToExclude)
		{
			var files = Directory.GetFiles(directoryPath);
			foreach (var path in files)
			{
				var entryName = path.Substring(dirNameOffest);
				entryName = ZipEntry.CleanName(entryName); // Removes drive from name and fixes slash direction
				if (extensionsToExclude != null)
				{
					var fileExtension = Path.GetExtension(entryName).ToLowerInvariant();
					if (extensionsToExclude.Contains(fileExtension))
						continue;
				}
				AddFile(path, entryName);
			}

			var folders = Directory.GetDirectories(directoryPath);

			foreach (var folder in folders)
			{
				var dirName = Path.GetFileName(folder);
				if (dirName == null)
					continue; // Don't want to bundle these up

				AddDirectory(folder, dirNameOffest, extensionsToExclude);
			}
		}
	}
}
