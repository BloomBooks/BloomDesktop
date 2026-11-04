using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Bloom.ImageProcessing;
using BloomTemp;
using SIL.IO;
using SIL.Windows.Forms.ImageToolbox;
using SIL.Windows.Forms.Miscellaneous;

namespace Bloom.Edit
{
    /// <summary>
    /// Wraps libpalaso's PortableClipboard to add support for pasting WebP images (BL-17048).
    /// PortableClipboard loads images through System.Drawing (GDI+), which has no WebP codec, so a
    /// WebP file copied in Windows Explorer, or the copied path to one, fails to paste. Here we
    /// notice such a file (judging by its content, not its name: web sites often serve WebP under
    /// a ".jpg" name) and convert it to a JPEG or PNG the same way we do for a WebP file chosen in
    /// the image chooser. Anything else is passed straight through to PortableClipboard.
    /// </summary>
    /// <remarks>
    /// A browser's "Copy image" command puts already-decoded bitmap data on the clipboard, so a
    /// WebP image copied that way never needed this help.
    /// </remarks>
    public static class BloomClipboard
    {
        /// <summary>
        /// Like PortableClipboard.GetImageFromClipboardWithExceptions(): returns null if nothing
        /// on the clipboard can reasonably be interpreted as an image, and throws if something
        /// looks like an image but cannot be loaded. If the image had to be converted from WebP,
        /// conversionFolder is set to the temporary folder holding the converted file, which is
        /// the returned image's OriginalFilePath. The caller must keep that folder until it is
        /// done with the image (e.g., it has been copied into the book), then dispose it.
        /// Otherwise conversionFolder is set to null.
        /// </summary>
        public static PalasoImage GetImageFromClipboardWithExceptions(
            out TemporaryFolder conversionFolder
        )
        {
            var image = GetWebPImage(GetFileDropPaths(), GetText(), out conversionFolder);
            return image ?? PortableClipboard.GetImageFromClipboardWithExceptions();
        }

        /// <summary>
        /// Like PortableClipboard.CanGetImage(): a quick answer, suitable for polling to enable a
        /// Paste button, as to whether GetImageFromClipboardWithExceptions() will find an image.
        /// It does not load or convert anything, and never throws.
        /// </summary>
        public static bool CanGetImage()
        {
            return PortableClipboard.CanGetImage() || ContainsWebPImageFile();
        }

        /// <summary>
        /// True if the clipboard holds a copied WebP file, or the path to one, that
        /// GetImageFromClipboardWithExceptions() would convert. Never throws.
        /// </summary>
        public static bool ContainsWebPImageFile()
        {
            try
            {
                return FindWebPImagePath(GetFileDropPaths(), GetText()) != null;
            }
            catch (Exception)
            {
                // Clipboard and file reads can fail at any moment (another process may hold the
                // clipboard or the file). Like PortableClipboard.CanGetImage(), we just treat
                // that as not having an image.
                return false;
            }
        }

        /// <summary>
        /// The testable core of GetImageFromClipboardWithExceptions(): if the given clipboard
        /// contents identify a WebP file (see FindWebPImagePath), convert it to a JPEG or PNG in a
        /// new temporary folder (returned in conversionFolder) and return it loaded as a
        /// PalasoImage. Otherwise return null and set conversionFolder to null. Throws if the
        /// conversion or loading fails.
        /// </summary>
        internal static PalasoImage GetWebPImage(
            IEnumerable<string> fileDropPaths,
            string clipboardText,
            out TemporaryFolder conversionFolder
        )
        {
            conversionFolder = null;
            var webPPath = FindWebPImagePath(fileDropPaths, clipboardText);
            if (webPPath == null)
                return null;
            var folder = new TemporaryFolder("BloomPasteConversion-" + Guid.NewGuid());
            try
            {
                var convertedPath = ImageUtils.ConvertWebpToJpegOrPngAsNeeded(
                    webPPath,
                    folder.FolderPath
                );
                var image = PalasoImage.FromFileRobustly(convertedPath);
                conversionFolder = folder;
                return image;
            }
            catch (Exception)
            {
                folder.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Return the path of the WebP file (judged by content) that the given clipboard contents
        /// identify, or null if they don't identify one. This follows PortableClipboard's choices
        /// of which file it would try to load: if any files were copied (e.g., in Windows
        /// Explorer), the first one that exists; otherwise, text that is the path to an existing
        /// file. Unlike PortableClipboard, we allow the path to be wrapped in quotes and
        /// whitespace, as Explorer's "Copy as path" does. A file that exists but can't be read
        /// (e.g., it is locked) is treated as not WebP, leaving the paste to PortableClipboard
        /// exactly as before this wrapper existed; the clipboard may hold usable bitmap data too.
        /// </summary>
        internal static string FindWebPImagePath(
            IEnumerable<string> fileDropPaths,
            string clipboardText
        )
        {
            string candidate;
            var files = fileDropPaths?.ToArray();
            if (files != null && files.Length > 0)
                candidate = files.FirstOrDefault(RobustFile.Exists);
            else
                candidate = clipboardText?.Trim().Trim('"');
            if (string.IsNullOrEmpty(candidate) || !RobustFile.Exists(candidate))
                return null;
            try
            {
                return IsWebPFile(candidate) ? candidate : null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return null;
            }
        }

        // The last file whose format IsWebPFile() read, so that polling (the Paste button's
        // enabled state is updated about every 110 ms) doesn't open the same copied file over
        // and over, which could pause editing if it is on a slow network share.
        private static string _lastSniffedPath;
        private static DateTime _lastSniffedWriteTimeUtc;
        private static bool _lastSniffedIsWebP;
        private static readonly object _sniffLock = new object();

        /// <summary>
        /// True if the existing file at path is WebP, judged by its content. The answer for the
        /// most recently checked file is remembered until its path or modification time changes.
        /// </summary>
        private static bool IsWebPFile(string path)
        {
            var writeTimeUtc = RobustFile.GetLastWriteTimeUtc(path);
            lock (_sniffLock)
            {
                if (path == _lastSniffedPath && writeTimeUtc == _lastSniffedWriteTimeUtc)
                    return _lastSniffedIsWebP;
            }
            var isWebP = ImageUtils.GetImageFileFormat(path) == ImageFileFormat.WebP;
            lock (_sniffLock)
            {
                _lastSniffedPath = path;
                _lastSniffedWriteTimeUtc = writeTimeUtc;
                _lastSniffedIsWebP = isWebP;
            }
            return isWebP;
        }

        private static string[] GetFileDropPaths()
        {
            return Clipboard.ContainsFileDropList()
                ? Clipboard.GetFileDropList().Cast<string>().ToArray()
                : null;
        }

        private static string GetText()
        {
            return PortableClipboard.ContainsText() ? PortableClipboard.GetText() : null;
        }
    }
}
