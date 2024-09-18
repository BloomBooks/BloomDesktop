using System.Collections.Generic;
using System.IO;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using SIL.IO;

namespace Bloom.Utils
{
    /// <summary>
    /// This just provides a convenient wrapper over SharpZipLib's ZipFile class. It's not really specific to Bloom.
    /// To Use, create a BloomZipFile, add files and directories, then call Save().
    /// </summary>
    public class BloomZipFile : BloomArchiveFile
    {
        private ZipOutputStream _zipStream;

        public BloomZipFile(string path)
        {
            var fsOut = RobustFile.Create(path);
            _zipStream = new ZipOutputStream(fsOut);
            _zipStream.SetLevel(9); // the compression level (9 is the most compression)
        }

        public override void Save()
        {
            _zipStream.IsStreamOwner = true; // makes the Close() also close the underlying stream
            _zipStream.Close();
        }

        protected override string CleanName(string entryName)
        {
            return ZipEntry.CleanName(entryName); // Removes drive from name and fixes slash direction
        }

        // These file types are already highly compressed; further compression wastes time
        // and is unlikely to save space. It might also increase the likelihood of spurious
        // differences between file versions making Dropbox's sync less efficient.
        private static readonly HashSet<string> extensionsNotToCompress = new HashSet<string>(
            new[] { ".png", ".jpg", ".mp3", ".mp4", ".webm" }
        );

        /// <summary>
        /// Decide whether to compress the file based on its extension. Some file types are already compressed so much
        /// that it wastes time to compress them further.
        /// </summary>
        public static bool ShouldCompressByFiletype(string fileFullPath)
        {
            var fileExtension = Path.GetExtension(fileFullPath);
            return !extensionsNotToCompress.Contains(fileExtension);
        }

        protected override bool ShouldCompress(string fileFullPath)
        {
            return ShouldCompressByFiletype(fileFullPath);
        }

        public void SetComment(string comment)
        {
            _zipStream.SetComment(comment);
        }

        protected override void AddFile(string path, string entryName, bool compressIfAble = true)
        {
            if (RobustZip.ShouldFileBeIgnored(path))
                return; //don't add hidden files (BL-12680)
            var fi = new FileInfo(path);
            var newEntry = new ZipEntry(entryName)
            {
                DateTime = fi.LastWriteTime,
                Size = fi.Length,
                IsUnicodeText = true,
                CompressionMethod = compressIfAble
                    ? CompressionMethod.Deflated
                    : CompressionMethod.Stored
            };

            _zipStream.PutNextEntry(newEntry);

            // Zip the file in buffered chunks
            var buffer = new byte[4096];
            using (var streamReader = RobustFile.OpenRead(path))
            {
                StreamUtils.Copy(streamReader, _zipStream, buffer);
            }

            _zipStream.CloseEntry();
        }

        public void AddTopLevelFileWithText(string fileName, string contents)
        {
            var newEntry = new ZipEntry(fileName)
            {
                DateTime = System.DateTime.Now,
                Size = contents.Length,
                IsUnicodeText = true,
                CompressionMethod = CompressionMethod.Deflated
            };
            _zipStream.PutNextEntry(newEntry);
            var buffer = System.Text.Encoding.UTF8.GetBytes(contents);
            _zipStream.Write(buffer, 0, buffer.Length);
            _zipStream.CloseEntry();
        }
    }
}
