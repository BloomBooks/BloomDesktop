using System;
using System.IO;
using Bloom.ImageProcessing;
using SIL.Code;
using SIL.Core.ClearShare;
using SIL.IO;
using SIL.Windows.Forms.ClearShare;

namespace Bloom
{
    /// <summary>
    /// Provides a more robust version of various IO methods.
    /// The original intent of this class is to attempt to mitigate issues
    /// where we attempt IO but the file is locked by another application.
    /// Our theory is that some anti-virus software locks files while it scans them.
    ///
    /// There is a similar class in SIL.IO, but that handles more generic calls
    /// which would not require additional dependencies.
    /// </summary>
    public class RobustFileIO
    {
        /// <summary>
        /// True if e says a file could not be opened because something else (another program,
        /// or another part of Bloom) has it open: a Windows sharing or lock violation, as opposed
        /// to the file being bad or missing.
        /// </summary>
        public static bool IsFileInUseError(Exception e)
        {
            const int kErrorSharingViolation = 32;
            const int kErrorLockViolation = 33;
            var win32Error = e.HResult & 0xFFFF;
            return e is IOException
                && (win32Error == kErrorSharingViolation || win32Error == kErrorLockViolation);
        }

        /// <summary>
        /// Run write, which writes to the file at path. If that fails because something else has
        /// the file open, wait for the file to come free and try once more; if it does not come
        /// free, rethrow the in-use error. A file that was written recently (a picture just
        /// pasted, say) may still be held open by something else (antivirus, a thumbnailer, a sync
        /// program) for longer than libpalaso's own retries wait. RobustIO.IsFileLocked waits
        /// about five seconds for the file before saying it is still locked. (BL-17093)
        /// </summary>
        public static void RetryOnceIfFileInUse(string path, Action write)
        {
            try
            {
                write();
            }
            catch (Exception e) when (IsFileInUseError(e))
            {
                SIL.Reporting.Logger.WriteEvent(
                    $"{Path.GetFileName(path)} is in use; waiting for it to come free"
                );
                if (RobustIO.IsFileLocked(path))
                    throw;
                write();
            }
        }

        /// <summary>
        /// Replace all the metadata in the image file at path with just the intellectual-property
        /// fields (copyright, license, creator...) of meta. Existing tags are cleared first because
        /// stray tags can trip up TagLib when we write (BL-16058). The file has usually just been
        /// written, so this waits for it if something else still has it open (BL-17093).
        /// </summary>
        public static void ReplaceMetadataWithIntellectualPropertyOnly(Metadata meta, string path)
        {
            RetryOnceIfFileInUse(
                path,
                () =>
                {
                    using (var tagFile = CreateTaglibFile(path))
                    {
                        tagFile.RemoveTags(TagLib.TagTypes.AllTags);
                        SaveTaglibFile(tagFile);
                    }
                    meta.WriteIntellectualPropertyOnly(path);
                }
            );
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
                    RobustFile.Copy(path, jpegFile.Path, true);
                    return MetadataFromFileInternal(jpegFile.Path);
                }
            }
            if (ImageUtils.HasJpegExtension(path) && ImageUtils.IsPngFile(path))
            {
                using (var pngFile = TempFile.WithExtension(".png"))
                {
                    RobustFile.Copy(path, pngFile.Path, true);
                    return MetadataFromFileInternal(pngFile.Path);
                }
            }
            // Assume everything is okay.
            return MetadataFromFileInternal(path);
        }

        private static Metadata MetadataFromFileInternal(string path)
        {
            return RetryUtility.Retry(() => Metadata.FromFile(path));
        }

        /// <summary>
        /// Create a TagLib file for reading or writing image metadata, retrying if the file is
        /// temporarily locked. TagLib# keeps mutable static state that is not thread-safe (see
        /// MetadataCore.RunUnderTagLibLock in libpalaso), so this serializes the underlying
        /// TagLib.File.Create behind the same process-wide lock that libpalaso's ClearShare
        /// metadata code uses. Any Save() on the returned file must likewise go through
        /// SaveTaglibFile so that it too is serialized against all other TagLib access.
        /// </summary>
        public static TagLib.File CreateTaglibFile(string path)
        {
            return MetadataCore.RunUnderTagLibLock(() =>
                RetryUtility.Retry(() => TagLib.File.Create(path))
            );
        }

        /// <summary>
        /// Save a TagLib file (such as one obtained from <see cref="CreateTaglibFile"/>), retrying
        /// if the file is temporarily locked. Saving renders the metadata, which mutates TagLib's
        /// shared static state, so it runs under the same lock as CreateTaglibFile. Use this rather
        /// than calling file.Save() directly on a TagLib file.
        /// </summary>
        public static void SaveTaglibFile(TagLib.File file)
        {
            MetadataCore.RunUnderTagLibLock(() => RetryUtility.Retry(() => file.Save()));
        }

        /// <summary>
        /// Create a new file and open it for writing, with no sharing, as long as it does not
        /// already exist. Returns null if the file already exists, so callers can use this as an
        /// exclusive claim on a name (another process that tries the same name gets null). Any
        /// other failure, such as a file briefly locked by anti-virus software, is retried, and
        /// if it persists the exception is thrown.
        /// </summary>
        public static FileStream TryCreateNewExclusive(string path)
        {
            return RetryUtility.Retry(
                () =>
                {
                    try
                    {
                        // robustfile-hook: allow FileStream
                        // FileMode.CreateNew is what makes this an exclusive claim, and
                        // FileShare.None keeps every other process out while the caller holds it;
                        // RobustFile has no equivalent.
                        return new FileStream(
                            path,
                            FileMode.CreateNew,
                            FileAccess.Write,
                            FileShare.None
                        );
                    }
                    catch (IOException) when (RobustFile.Exists(path))
                    {
                        // Someone else created it first. Trying again would never succeed.
                        return null;
                    }
                },
                memo: "Create " + path
            );
        }

        /// <summary>
        /// Attempts to delete a directory once. Returns false if an exception prevents deletion,
        /// rather than retrying or throwing — suitable for best-effort cleanup during shutdown.
        /// </summary>
        public static bool TryDeleteDirectory(string path, bool recursive = false)
        {
            try
            {
                Directory.Delete(path, recursive);
                return true;
            }
            catch (Exception ex)
            {
                Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(ex);
                return false;
            }
        }
    }
}
