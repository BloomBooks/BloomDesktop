using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ICSharpCode.SharpZipLib.Zip;
using SIL.Reporting;

namespace Bloom.Utils
{
    /// <summary>
    /// Abstract class providing some implementation and obligations for Bloom's wrappers around the SharpZipLib archives such as zip, tar, etc.
    /// </summary>
    public abstract class BloomArchiveFile
    {
        #region Abstract Method obligations
        /// <summary>
        /// Saves / finalizes the archive after everything has been written to it.
        /// </summary>
        public abstract void Save();

        /// <summary>
        /// Adds a file to the archive
        /// </summary>
        /// <param name="path">The path to the file contents</param>
        /// <param name="entryName">The name to use in the archive</param>
        /// <param name="compressIfAble">True to compress the file, if compression is supported by the archive format.</param>
        protected abstract void AddFile(string path, string entryName, bool compressIfAble = true);

        /// <summary>
        /// Cleans a name making it conform to the file conventions of the archive format.
        /// </summary>
        protected abstract string CleanName(string entryName);

        protected abstract bool ShouldCompress(string fileFullPath);
        #endregion

        public void AddTopLevelFile(string path, bool compress = true)
        {
            AddFile(path, Path.GetFileName(path), compress);
        }

        /// <summary>
        /// Adds a directory's contents (all files and subdirectories), but not the directory itself.
        /// </summary>
        /// <param name="directoryPath">The directory to add recursively</param>
        /// <param name="extensionsToExclude">An array of extensions to exlude from the zip file, null excludes nothing. Casing doesn't matter</param>
        public void AddDirectoryContents(string directoryPath, string[] extensionsToExclude = null)
        {
            AddDirectory(directoryPath, directoryPath.Length + 1, extensionsToExclude, null);
        }

        /// <summary>
        /// Adds a directory, along with all files and subdirectories
        /// </summary>
        /// <param name="directoryPath">The directory to add recursively</param>
        /// <param name="extensionsToExclude">An array of extensions to exlude from the zip file, null excludes nothing. Casing doesn't matter</param>
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
        public void AddDirectory(
            string directoryPath,
            int dirNameOffest,
            string[] extensionsToExclude,
            Action<float> progressCallback = null
        )
        {
            var count = 0;
            var done = 0;
            Action<string> perFileCallback = null;
            if (progressCallback != null)
            {
                count = AddDirectory(directoryPath, dirNameOffest, extensionsToExclude, true);
                // if count is zero, perFileCallback will never be called, so we don't need to fear
                // divide by zero.
                perFileCallback = (path) => progressCallback((float)(++done) / count);
            }

            AddDirectory(directoryPath, dirNameOffest, extensionsToExclude, false, perFileCallback);
        }

        /// <summary>
        /// Add everything in directoryPath, including subfolders recursively, to the zip.
        /// The names of the zip entries are made by removing dirNameOffset chars from the start
        /// of the full path to the file. See the other overload for a fuller explanation.
        /// Files with the specified extensions will be excluded.
        /// <returns>The number of files added (or that would be added, if justCount were false)</returns>
        /// <param name="extensionsToExclude">An array of extensions to exlude from the zip file, null excludes nothing. Casing doesn't matter</param>
        /// <param name="justCount">If true, returns the count without actually adding them. This is usually
        /// to prepare for a second call with the next param a function that will report progress</param>
        /// <param name="perFileCallback">A callback that will be invoked with the full path of each file added,
        /// once the addition has been done</param>
        /// </summary>
        public int AddDirectory(
            string directoryPath,
            int dirNameOffest,
            IList<string> extensionsToExclude,
            bool justCount = false,
            Action<string> perFileCallback = null
        )
        {
            extensionsToExclude = extensionsToExclude?.Select(x => x.ToLowerInvariant()).ToList();

            var count = 0;
            var files = Directory.GetFiles(directoryPath);
            var currentFilename = "";
            try
            {
                foreach (var path in files)
                {
                    if (RobustZip.ShouldFileBeIgnored(path))
                        continue; //don't add hidden files (BL-12680)
                    currentFilename = path;
                    var entryName = path.Substring(dirNameOffest);
                    string zipEntryName = ZipEntry.CleanName(entryName);
                    entryName = CleanName(entryName);
                    var fileExtension = Path.GetExtension(entryName).ToLowerInvariant();
                    if (extensionsToExclude != null)
                    {
                        if (extensionsToExclude.Contains(fileExtension))
                            continue;
                    }
                    if (!justCount)
                        AddFile(path, entryName, ShouldCompress(path));
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
            }
            catch (ZipException ze)
            {
                var msg =
                    $"ZipException, there was an error writing {currentFilename} to a TC .bloom file.";
                if (ze.Message.ToLowerInvariant().Contains("crc"))
                    msg += ": CRC check failed";
                Logger.WriteError(msg, ze);
                throw ze;
            }

            return count;
        }
    }
}
