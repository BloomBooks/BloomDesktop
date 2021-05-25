using System;
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
			AddDirectory(directoryPath, directoryPath.Length + 1, extensionsToExclude, null);
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
			AddDirectory(directoryPath, dirNameOffset, extensionsToExclude, null);
		}

		/// <summary>
		/// Add everything in directoryPath, including subfolders recursively, to the zip.
		/// The names of the zip entries are made by removing dirNameOffset from the full path
		/// to the file, so usually directoryPath.Length + 1 should be passed.
		/// Doing that will make a zip where the children of directoryPath are the root elements
		/// in the zip. Another common option is where directoryPath ends with a folder name
		/// that should be put as a folder into the zip. So for example, if a folder Foo
		/// contains Bar1 and Bar2, passing pathToFoo.Length + 1 will result in a zip file
		/// that directly contains the contents of Foo:
		/// - Bar1
		/// - Bar2
		/// while passing pathToFoo.Length - "Foo".Length will result in a zip that contains
		/// the folder Foo with its children:
		/// - Foo
		///    - Bar1
		///    - Bar2 
		/// Files with the specified extensions will be excluded.
		/// If progressCallback is non-null, it is invoked after adding each file,
		/// with a value that is the fraction of the total number of files.
		/// </summary>
		public void AddDirectory(string directoryPath, int dirNameOffest, string[] extensionsToExclude,
			Action<float> progressCallback = null)
		{
			var count = 0;
			var done = 0;
			Action<string> perFileCallback = null;
			if (progressCallback != null)
			{
				count = AddDirectory(directoryPath, dirNameOffest, null, true);
				// if count is zero, perFileCallback will never be called, so we don't need to fear
				// divide by zero.
				perFileCallback = (path) => progressCallback((float)(++done) / count);
			}

			AddDirectory(directoryPath, dirNameOffest, null, false, perFileCallback);
		}


		/// <summary>
		/// Add everything in directoryPath, including subfolders recursively, to the zip.
		/// The names of the zip entries are made by removing dirNameOffset chars from the start
		/// of the full path to the file. See the other overload for a fuller explanation.
		/// Files with the specified extensions will be excluded.
		/// <returns>The number of files added (or that would be added, if justCount were false)</returns>
		/// <param name="justCount">If true, returns the count without actually adding them. This is usually
		/// to prepare for a second call with the next param a function that will report progress</param>
		/// <param name="perFileCallback">A callback that will be invoked with the full path of each file added,
		/// once the addition has been done</param>
		/// </summary>
		public int AddDirectory(string directoryPath, int dirNameOffest, string[] extensionsToExclude, bool justCount = false, Action<string> perFileCallback = null)
		{
			var count = 0;
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
				if (!justCount)
					AddFile(path, entryName);
				perFileCallback?.Invoke(path);
				count++;
			}

			var folders = Directory.GetDirectories(directoryPath);

			foreach (var folder in folders)
			{
				var dirName = Path.GetFileName(folder);
				if (dirName == null)
					continue; // Don't want to bundle these up

				count += AddDirectory(folder, dirNameOffest, extensionsToExclude, justCount);
			}

			return count;
		}
	}
}
