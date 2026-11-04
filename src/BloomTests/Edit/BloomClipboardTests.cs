using System;
using System.IO;
using Bloom.Edit;
using Bloom.ImageProcessing;
using BloomTemp;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.Edit
{
    /// <summary>
    /// Tests for the clipboard-independent parts of BloomClipboard, which lets the user paste a
    /// WebP image (BL-17048) by converting it to a JPEG or PNG that PortableClipboard's
    /// System.Drawing-based loading can't manage. The parts that read the real clipboard are
    /// not tested here, since a unit test shouldn't depend on (or clobber) the user's clipboard.
    /// </summary>
    [TestFixture]
    public class BloomClipboardTests
    {
        private const string _pathToTestImages = "src/BloomTests/ImageProcessing/images";

        private static string GetTestImagePath(string fileName)
        {
            return FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                fileName
            );
        }

        /// <summary>
        /// A WebP file copied in Explorer is found, judged by content: man-really-webp.jpg is a
        /// WebP file with a misleading name, as web sites often serve.
        /// </summary>
        [TestCase("bird-lossless.webp")]
        [TestCase("man-really-webp.jpg")]
        public void FindWebPImagePath_FileDropOfWebP_ReturnsIt(string fileName)
        {
            var path = GetTestImagePath(fileName);
            Assert.That(
                ImageUtils.GetImageFileFormat(path),
                Is.EqualTo(ImageFileFormat.WebP),
                "setup"
            );
            Assert.That(BloomClipboard.FindWebPImagePath(new[] { path }, null), Is.EqualTo(path));
        }

        /// <summary>
        /// A copied PNG or JPEG is not WebP, so it is left for PortableClipboard to paste.
        /// </summary>
        [TestCase("man.png")]
        [TestCase("man.jpg")]
        public void FindWebPImagePath_FileDropOfOtherImage_ReturnsNull(string fileName)
        {
            var path = GetTestImagePath(fileName);
            Assert.That(RobustFile.Exists(path), Is.True, "setup");
            Assert.That(BloomClipboard.FindWebPImagePath(new[] { path }, null), Is.Null);
        }

        /// <summary>
        /// Like PortableClipboard, copied files that no longer exist are skipped.
        /// </summary>
        [Test]
        public void FindWebPImagePath_FileDropSkipsMissingFiles_UsesFirstExistingOne()
        {
            var webPPath = GetTestImagePath("bird-lossless.webp");
            var missingPath = Path.Combine(
                Path.GetDirectoryName(webPPath),
                "no-such-file-anywhere.webp"
            );
            Assert.That(RobustFile.Exists(missingPath), Is.False, "setup");
            Assert.That(
                BloomClipboard.FindWebPImagePath(new[] { missingPath, webPPath }, null),
                Is.EqualTo(webPPath)
            );
        }

        /// <summary>
        /// PortableClipboard would load the first existing copied file (here a PNG), so a WebP
        /// file later in the list must not take over the paste.
        /// </summary>
        [Test]
        public void FindWebPImagePath_FileDropWhoseFirstExistingFileIsNotWebP_ReturnsNull()
        {
            var pngPath = GetTestImagePath("man.png");
            var webPPath = GetTestImagePath("bird-lossless.webp");
            Assert.That(
                BloomClipboard.FindWebPImagePath(new[] { webPPath }, null),
                Is.EqualTo(webPPath),
                "setup: the WebP file alone should be found"
            );
            Assert.That(
                BloomClipboard.FindWebPImagePath(new[] { pngPath, webPPath }, null),
                Is.Null
            );
        }

        /// <summary>
        /// PortableClipboard looks at copied files before text, and so do we.
        /// </summary>
        [Test]
        public void FindWebPImagePath_FileDropIgnoresText()
        {
            var pngPath = GetTestImagePath("man.png");
            var webPPath = GetTestImagePath("bird-lossless.webp");
            Assert.That(BloomClipboard.FindWebPImagePath(new[] { pngPath }, webPPath), Is.Null);
        }

        /// <summary>
        /// If we can't read the file when pasting, leave the paste to PortableClipboard, as before
        /// BloomClipboard existed: the clipboard may also hold bitmap data it can use. That holds
        /// even when the Paste button's polling already remembered the file as WebP, since
        /// locking a file doesn't change its modification time.
        /// </summary>
        [Test]
        public void FindWebPImagePath_WebPFileLockedAfterPolling_ReturnsNull()
        {
            using (var folder = new TemporaryFolder("BloomClipboardTests_Locked"))
            {
                var path = Path.Combine(folder.FolderPath, "locked.webp");
                RobustFile.Copy(GetTestImagePath("bird-lossless.webp"), path);
                Assert.That(
                    BloomClipboard.FindWebPImagePath(
                        new[] { path },
                        null,
                        allowRememberedAnswer: true
                    ),
                    Is.EqualTo(path),
                    "setup: polling should find the unlocked file"
                );
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    Assert.That(BloomClipboard.FindWebPImagePath(new[] { path }, null), Is.Null);
                }
            }
        }

        /// <summary>
        /// Polling remembers the answer for a file, but checks again once the file's
        /// modification time changes.
        /// </summary>
        [Test]
        public void FindWebPImagePath_PollingAfterFileReplaced_ChecksItAgain()
        {
            using (var folder = new TemporaryFolder("BloomClipboardTests_Replaced"))
            {
                var path = Path.Combine(folder.FolderPath, "picture.webp");
                RobustFile.Copy(GetTestImagePath("bird-lossless.webp"), path);
                Assert.That(
                    BloomClipboard.FindWebPImagePath(
                        new[] { path },
                        null,
                        allowRememberedAnswer: true
                    ),
                    Is.EqualTo(path),
                    "setup: the WebP file should be found"
                );

                RobustFile.Copy(GetTestImagePath("man.png"), path, true);
                // Make sure the modification time differs, whatever the file system's resolution.
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

                Assert.That(
                    BloomClipboard.FindWebPImagePath(
                        new[] { path },
                        null,
                        allowRememberedAnswer: true
                    ),
                    Is.Null
                );
            }
        }

        /// <summary>
        /// A paste reads the file afresh, so a WebP file replaced by a PNG that kept the same
        /// modification time is not mistaken for WebP (which would make the paste fail).
        /// </summary>
        [Test]
        public void FindWebPImagePath_PasteAfterReplacementKeepingModificationTime_ReadsFileAgain()
        {
            using (var folder = new TemporaryFolder("BloomClipboardTests_SameTime"))
            {
                var path = Path.Combine(folder.FolderPath, "picture.webp");
                RobustFile.Copy(GetTestImagePath("bird-lossless.webp"), path);
                var originalWriteTimeUtc = RobustFile.GetLastWriteTimeUtc(path);
                Assert.That(
                    BloomClipboard.FindWebPImagePath(
                        new[] { path },
                        null,
                        allowRememberedAnswer: true
                    ),
                    Is.EqualTo(path),
                    "setup: polling should find the WebP file"
                );

                RobustFile.Copy(GetTestImagePath("man.png"), path, true);
                File.SetLastWriteTimeUtc(path, originalWriteTimeUtc);
                Assert.That(
                    BloomClipboard.FindWebPImagePath(
                        new[] { path },
                        null,
                        allowRememberedAnswer: true
                    ),
                    Is.EqualTo(path),
                    "setup: polling should still be using its remembered answer"
                );

                Assert.That(BloomClipboard.FindWebPImagePath(new[] { path }, null), Is.Null);
            }
        }

        /// <summary>
        /// Copied text that is the path to a WebP file is found, when no files were copied.
        /// </summary>
        [Test]
        public void FindWebPImagePath_TextPathToWebP_ReturnsIt()
        {
            var path = GetTestImagePath("bird-lossless.webp");
            Assert.That(BloomClipboard.FindWebPImagePath(null, path), Is.EqualTo(path));
            // An empty file-drop list is treated the same as none.
            Assert.That(BloomClipboard.FindWebPImagePath(new string[0], path), Is.EqualTo(path));
        }

        /// <summary>
        /// Explorer's "Copy as path" wraps the path in quotes; we still find the file.
        /// </summary>
        [Test]
        public void FindWebPImagePath_QuotedTextPathToWebP_ReturnsUnquotedPath()
        {
            var path = GetTestImagePath("bird-lossless.webp");
            Assert.That(
                BloomClipboard.FindWebPImagePath(null, "  \"" + path + "\"\r\n"),
                Is.EqualTo(path)
            );
        }

        /// <summary>
        /// Text that isn't the path of an existing file is not treated as a WebP image.
        /// </summary>
        [TestCase("Just some ordinary text somebody copied")]
        [TestCase(@"C:\no\such\folder\bird.webp")]
        [TestCase("")]
        [TestCase(null)]
        public void FindWebPImagePath_TextNotPathToExistingFile_ReturnsNull(string text)
        {
            Assert.That(BloomClipboard.FindWebPImagePath(null, text), Is.Null);
        }

        /// <summary>
        /// A copied path to a PNG file is left for PortableClipboard to paste.
        /// </summary>
        [Test]
        public void FindWebPImagePath_TextPathToOtherImage_ReturnsNull()
        {
            var path = GetTestImagePath("man.png");
            Assert.That(RobustFile.Exists(path), Is.True, "setup");
            Assert.That(BloomClipboard.FindWebPImagePath(null, path), Is.Null);
        }

        /// <summary>
        /// A copied WebP file is converted to a JPEG (an opaque lossy photo) or a PNG
        /// (lossless or transparent), which stays available in the conversion folder until the
        /// caller disposes it.
        /// </summary>
        [TestCase("man-really-webp.jpg", ".jpg")]
        [TestCase("bird-lossless.webp", ".png")]
        [TestCase("bird-lossy-transparent.webp", ".png")]
        public void GetWebPImage_WebPFile_ReturnsConvertedImageInConversionFolder(
            string fileName,
            string expectedExtension
        )
        {
            var path = GetTestImagePath(fileName);
            TemporaryFolder conversionFolder = null;
            try
            {
                using (
                    var image = BloomClipboard.GetWebPImage(
                        new[] { path },
                        null,
                        out conversionFolder
                    )
                )
                {
                    Assert.That(image, Is.Not.Null);
                    Assert.That(conversionFolder, Is.Not.Null);
                    Assert.That(image.Image.Width, Is.GreaterThan(0));
                    Assert.That(
                        Path.GetDirectoryName(image.OriginalFilePath),
                        Is.EqualTo(conversionFolder.FolderPath)
                    );
                    Assert.That(
                        Path.GetFileName(image.OriginalFilePath),
                        Is.EqualTo(Path.GetFileNameWithoutExtension(fileName) + expectedExtension)
                    );
                    Assert.That(
                        RobustFile.Exists(image.OriginalFilePath),
                        Is.True,
                        "the converted file must still exist for the caller to copy into the book"
                    );
                }
            }
            finally
            {
                conversionFolder?.Dispose();
            }
            Assert.That(
                Directory.Exists(conversionFolder.FolderPath),
                Is.False,
                "disposing the conversion folder should clean up the converted file"
            );
        }

        /// <summary>
        /// A non-WebP file needs no conversion, so no image and no conversion folder result.
        /// </summary>
        [Test]
        public void GetWebPImage_NotWebP_ReturnsNullWithoutConversionFolder()
        {
            var path = GetTestImagePath("man.png");
            var image = BloomClipboard.GetWebPImage(new[] { path }, null, out var conversionFolder);
            Assert.That(image, Is.Null);
            Assert.That(conversionFolder, Is.Null);
        }
    }
}
