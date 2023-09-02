using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Tar;
using SIL.IO; using Bloom.Utils;

namespace Bloom.Utils
{
	/// <summary>
	/// This just provides a convenient wrapper over SharpZipLib's TarArchive class. It's not really specific to Bloom.
	/// To Use, create a BloomTarArchive, add files and directories, then call Save().
	/// </summary>
	public class BloomTarArchive: BloomArchiveFile
	{
		private TarOutputStream _tarStream;

		public BloomTarArchive(string path)
		{
			var fsOut = PatientFile.Create(path);
			_tarStream = new TarOutputStream(fsOut, Encoding.UTF8);
		}

		public override void Save()
		{
			_tarStream.IsStreamOwner = true; // makes the Close() also close the underlying stream
			_tarStream.Close();
		}

		protected override string CleanName(string entryName) => entryName;

		protected override bool ShouldCompress(string fileFullPath) => false;

		/// <summary>
		/// Adds a file to the archive
		/// </summary>
		/// <param name="compressIfAble">This parameter is ignored because tar files don't support compression. It is recommended to pass "false" though.</param>
		protected override void AddFile(string path, string entryName, bool compressIfAble=true)
		{
			var fi = new FileInfo(path);

			TarEntry newEntry = TarEntry.CreateEntryFromFile(path);
			newEntry.TarHeader.Name = entryName;
			newEntry.Size = fi.Length;

			_tarStream.PutNextEntry(newEntry);

			// Add to the archive in buffered chunks
			var buffer = new byte[4096];
			using (var streamReader = PatientFile.OpenRead(path))
			{
				StreamUtils.Copy(streamReader, _tarStream, buffer);
			}

			_tarStream.CloseEntry();
		}
	}	
}
