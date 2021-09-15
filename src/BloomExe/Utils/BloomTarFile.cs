using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Tar;
using SIL.IO;

namespace Bloom.Utils
{
	/// <summary>
	/// This just provides a convenient wrapper over a zipping library. It's not really specific to Bloom.
	/// To Use, create a BloomTarFile, add files and directories, then call Save().
	/// </summary>
	public class BloomTarFile: BloomArchiveFile
	{
		private TarOutputStream _tarStream;

		public BloomTarFile(string path)
		{
			var fsOut = RobustFile.Create(path);
			_tarStream = new TarOutputStream(fsOut, Encoding.UTF8);
		}

		public override void Save()
		{
			_tarStream.IsStreamOwner = true; // makes the Close() also close the underlying stream
			_tarStream.Close();
		}

		protected override string CleanName(string entryName) => entryName;

		protected override bool ShouldCompress(string fileFullPath) => false;

		protected override void AddFile(string path, string entryName, bool compress=true)
		{
			if (compress == true)
			{
				throw new ArgumentException("Compress option is not supported for tar files.");
			}

			var fi = new FileInfo(path);

			TarEntry newEntry = TarEntry.CreateEntryFromFile(path);
			newEntry.TarHeader.Name = entryName;
			newEntry.Size = fi.Length;

			_tarStream.PutNextEntry(newEntry);

			// Zip the file in buffered chunks
			var buffer = new byte[4096];
			using (var streamReader = RobustFile.OpenRead(path))
			{
				StreamUtils.Copy(streamReader, _tarStream, buffer);
			}

			_tarStream.CloseEntry();
		}
	}	
}
