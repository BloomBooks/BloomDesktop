using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using SIL.IO;

namespace Bloom.FontProcessing
{
    /// <summary>
    /// Reads the metadata of a font that Bloom stored as a file, and remembers what it read.
    ///
    /// Two things make this more than a call to the FontMetadata constructor. Reading a font file
    /// is slow, and the folders that hold stored fonts gain files while Bloom runs, which the
    /// font reading code in WPF does not cope with. See MakeIsolatedCopy.
    /// </summary>
    public static class StoredFontMetadataCache
    {
        private const string kReadFolderPrefix = "bloomStoredFontRead";

        private static readonly object _lock = new object();
        private static readonly Dictionary<string, FontMetadata> _cache =
            new Dictionary<string, FontMetadata>();
        private static string _folderForThisProcess;

        /// <summary>
        /// The metadata of the given stored font family, read from the file the group names as its
        /// normal face. A file that has not changed is read once per run of Bloom.
        /// </summary>
        /// <param name="family">the family name, which comes from the filename</param>
        /// <param name="group">the files of the family</param>
        /// <param name="source">FontMetadata.kSourceCollection or FontMetadata.kSourceBook</param>
        public static FontMetadata GetMetadata(string family, FontGroup group, string source)
        {
            var key = MakeKey(family, group.Normal, source);
            lock (_lock)
            {
                if (key != null && _cache.TryGetValue(key, out var cached))
                    return cached;
                var metadata = new FontMetadata(family, group, source);
                if (key != null)
                    _cache[key] = metadata;
                return metadata;
            }
        }

        /// <summary>
        /// Forget everything read so far. Tests use this to read the same file twice.
        /// </summary>
        public static void ClearForTests()
        {
            lock (_lock)
                _cache.Clear();
        }

        /// <summary>
        /// The number of entries held, which tells a test whether a read was a hit or a miss.
        /// </summary>
        public static int CountForTests
        {
            get
            {
                lock (_lock)
                    return _cache.Count;
            }
        }

        // A file is the same file only if its path, its length and its write time all match.
        private static string MakeKey(string family, string path, string source)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                    return null;
                return string.Join(
                    "|",
                    family,
                    source,
                    Path.GetFullPath(path).ToLowerInvariant(),
                    info.Length.ToString(),
                    info.LastWriteTimeUtc.Ticks.ToString()
                );
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Copy a font file into a folder of its own and return the path of the copy.
        ///
        /// WPF builds one DirectWrite font collection for the whole folder that holds a font file,
        /// the first time it reads any file in that folder, and it never refreshes that collection.
        /// A file added to the folder afterwards is not in the collection, so the read comes back
        /// null and throws. The folders that hold stored fonts gain files while Bloom runs, so each
        /// read has to happen in a folder that WPF has not seen before.
        ///
        /// The copy stays on disk after the read, because DirectWrite keeps the file mapped for as
        /// long as the collection lives. The folder of a run that has ended is deleted by the next
        /// run.
        /// </summary>
        /// <returns>the path of the copy, or the original path if the copy failed</returns>
        public static string MakeIsolatedCopy(string path)
        {
            try
            {
                var folder = Path.Combine(GetFolderForThisProcess(), Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                var copy = Path.Combine(folder, Path.GetFileName(path));
                RobustFile.Copy(path, copy, true);
                return copy;
            }
            catch (Exception)
            {
                // Reading the file where it sits is better than not reading it at all.
                return path;
            }
        }

        // One folder per run of Bloom, under the temp folder, named for the process that owns it.
        private static string GetFolderForThisProcess()
        {
            if (_folderForThisProcess != null)
                return _folderForThisProcess;
            var process = Process.GetCurrentProcess();
            var folder = Path.Combine(
                Path.GetTempPath(),
                kReadFolderPrefix + "-" + process.Id.ToString()
            );
            Directory.CreateDirectory(folder);
            _folderForThisProcess = folder;
            DeleteFoldersOfRunsThatHaveEnded(folder);
            return _folderForThisProcess;
        }

        // A folder whose run has ended holds no mapped files, so it deletes cleanly. A folder that
        // another run still owns refuses to delete, and that is the answer we want.
        private static void DeleteFoldersOfRunsThatHaveEnded(string folderForThisProcess)
        {
            try
            {
                foreach (
                    var folder in Directory.EnumerateDirectories(
                        Path.GetTempPath(),
                        kReadFolderPrefix + "-*"
                    )
                )
                {
                    if (
                        string.Equals(
                            folder,
                            folderForThisProcess,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                        continue;
                    try
                    {
                        Directory.Delete(folder, true);
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
        }
    }
}
