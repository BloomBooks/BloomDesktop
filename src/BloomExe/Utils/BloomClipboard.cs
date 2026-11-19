using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SIL.IO;
using SIL.Windows.Forms.ImageToolbox;
using SIL.Windows.Forms.Miscellaneous;

namespace Bloom.Utils
{
    /// <summary>
    /// Every clipboard read and write in Bloom's C# goes through this class
    /// (src/BloomExe/BannedSymbols.txt bans the classes it wraps).
    ///
    /// In a normal run it uses the Windows clipboard, through libpalaso's PortableClipboard.
    /// Under --e2e (Program.RunningE2eTests) it uses an in-memory clipboard instead, which a test
    /// fills and reads through the e2e/clipboard endpoint (E2eTestingApi). So an e2e run never
    /// touches the clipboard of the Windows session it runs in: it cannot replace what the
    /// developer copied, and two e2e runs on one machine cannot change each other's clipboard.
    ///
    /// The in-memory clipboard answers the way the Windows clipboard answers for the same
    /// contents. In particular, a picture comes either from the picture itself or from a copied
    /// file or text path that names an existing picture file, as in libpalaso's WindowsClipboard.
    /// The page's own clipboard calls go through utils/bloomClipboard.ts, which uses the same
    /// in-memory clipboard under --e2e.
    /// </summary>
    public static class BloomClipboard
    {
#pragma warning disable RS0030 // This class is the one place allowed to use the clipboard classes it wraps.

        /// <summary>
        /// What the in-memory clipboard holds. Each field is one of the forms a Windows clipboard
        /// can offer at once; a field that is null is a form the clipboard does not have.
        /// </summary>
        public class E2eContents
        {
            /// <summary>Plain text.</summary>
            public string Text { get; set; }

            /// <summary>HTML, as a browser's paste event offers it (no CF_HTML header).</summary>
            public string Html { get; set; }

            /// <summary>
            /// The path of a file that holds the picture on the clipboard. A picture copied
            /// without a file (from a canvas, say) is saved to a temporary file first.
            /// </summary>
            public string PicturePath { get; set; }

            /// <summary>Copied files, as Explorer's Copy offers them.</summary>
            public string[] Files { get; set; }
        }

        private static readonly object s_lock = new object();
        private static E2eContents s_e2eContents = new E2eContents();

        // Pictures that CopyImageToClipboard had to save to disk under --e2e, kept until Bloom
        // exits because the in-memory clipboard can still name them.
        private static readonly List<TempFile> s_e2eTempPictures = new List<TempFile>();

        // The extensions WindowsClipboard accepts when it looks for a picture in a copied file or
        // a text path.
        private static readonly HashSet<string> s_imageExtensions = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ".jpg",
            ".jpeg",
            ".gif",
            ".png",
            ".bmp",
            ".tif",
            ".tiff",
        };

        private static bool UseE2eClipboard => Program.RunningE2eTests;

        /// <summary>
        /// Replace what the in-memory clipboard holds. Only for the e2e/clipboard endpoint, and
        /// only meaningful under --e2e.
        /// </summary>
        public static void SetE2eContents(E2eContents contents)
        {
            lock (s_lock)
                s_e2eContents = contents ?? new E2eContents();
        }

        /// <summary>What the in-memory clipboard holds. Only for the e2e/clipboard endpoint.</summary>
        public static E2eContents GetE2eContents()
        {
            lock (s_lock)
                return s_e2eContents;
        }

        /// <summary>True when the clipboard holds plain text that is not empty.</summary>
        public static bool ContainsText()
        {
            if (!UseE2eClipboard)
                return PortableClipboard.ContainsText();
            return !string.IsNullOrEmpty(GetE2eContents().Text);
        }

        /// <summary>The clipboard's plain text, or an empty string when it has none.</summary>
        public static string GetText()
        {
            if (!UseE2eClipboard)
                return PortableClipboard.GetText();
            return GetE2eContents().Text ?? "";
        }

        /// <summary>Replace whatever the clipboard holds with this plain text.</summary>
        public static void SetText(string text)
        {
            if (!UseE2eClipboard)
            {
                PortableClipboard.SetText(text);
                return;
            }
            SetE2eContents(new E2eContents { Text = text });
        }

        /// <summary>
        /// True when the clipboard holds a picture, or a copied file or a text path that names an
        /// existing picture file.
        /// </summary>
        public static bool CanGetImage()
        {
            if (!UseE2eClipboard)
                return PortableClipboard.CanGetImage();
            return GetE2ePicturePath(GetE2eContents(), true) != null;
        }

        /// <summary>
        /// The picture on the clipboard (see CanGetImage), or null when there is none. Throws when
        /// there is one but it cannot be loaded.
        /// </summary>
        public static PalasoImage GetImageFromClipboardWithExceptions()
        {
            if (!UseE2eClipboard)
                return PortableClipboard.GetImageFromClipboardWithExceptions();
            var path = GetE2ePicturePath(GetE2eContents(), false);
            return path == null ? null : PalasoImage.FromFileRobustly(path);
        }

        /// <summary>
        /// The picture on the clipboard (see CanGetImage), or null when there is none or it cannot
        /// be loaded.
        /// </summary>
        public static PalasoImage GetImageFromClipboard()
        {
            if (!UseE2eClipboard)
                return PortableClipboard.GetImageFromClipboard();
            try
            {
                return GetImageFromClipboardWithExceptions();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Replace whatever the clipboard holds with this picture. When the picture came from a
        /// file, the file's path goes on the clipboard beside it as text, as libpalaso does.
        /// </summary>
        public static void CopyImageToClipboard(PalasoImage image)
        {
            if (!UseE2eClipboard)
            {
                PortableClipboard.CopyImageToClipboard(image);
                return;
            }
            if (image == null)
                return;
            var originalPath = image.OriginalFilePath;
            if (image.Image == null)
            {
                if (!string.IsNullOrEmpty(originalPath))
                    SetE2eContents(new E2eContents { Files = new[] { originalPath } });
                return;
            }
            if (!string.IsNullOrEmpty(originalPath))
            {
                SetE2eContents(new E2eContents { PicturePath = originalPath, Text = originalPath });
                return;
            }
            var temp = TempFile.WithExtension(".png");
            image.Image.Save(temp.Path, System.Drawing.Imaging.ImageFormat.Png);
            lock (s_lock)
                s_e2eTempPictures.Add(temp);
            SetE2eContents(new E2eContents { PicturePath = temp.Path });
        }

        /// <summary>True when the clipboard holds copied files, as Explorer's Copy puts there.</summary>
        public static bool ContainsFileDropList()
        {
            if (!UseE2eClipboard)
                return Clipboard.ContainsFileDropList();
            return GetE2eContents().Files?.Length > 0;
        }

        /// <summary>The paths of the files on the clipboard; empty when there are none.</summary>
        public static string[] GetFileDropList()
        {
            if (!UseE2eClipboard)
                return Clipboard.GetFileDropList().Cast<string>().ToArray();
            return GetE2eContents().Files ?? Array.Empty<string>();
        }

        /// <summary>
        /// Where the in-memory clipboard's picture is, looked for in the order WindowsClipboard
        /// looks: the picture itself, then a copied file, then a text path to a file. Null when
        /// none of these names an existing file, so a picture whose file has gone counts as no
        /// picture. As in WindowsClipboard, CanGetImage
        /// (`requirePictureExtension` true) counts only a file with a picture's extension, while
        /// pasting takes any existing file and fails if it is not a picture.
        /// </summary>
        private static string GetE2ePicturePath(E2eContents contents, bool requirePictureExtension)
        {
            if (
                !string.IsNullOrEmpty(contents.PicturePath)
                && RobustFile.Exists(contents.PicturePath)
            )
                return contents.PicturePath;
            bool Qualifies(string path) =>
                !string.IsNullOrEmpty(path)
                && path.IndexOfAny(Path.GetInvalidPathChars()) < 0
                && (!requirePictureExtension || s_imageExtensions.Contains(Path.GetExtension(path)))
                && RobustFile.Exists(path);
            var file = contents.Files?.FirstOrDefault(Qualifies);
            if (file != null)
                return file;
            return Qualifies(contents.Text) ? contents.Text : null;
        }
#pragma warning restore RS0030
    }
}
