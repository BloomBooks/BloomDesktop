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

        // man-really-webp.jpg is a WebP file with a misleading name, as web sites often serve.
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

        [TestCase("man.png")]
        [TestCase("man.jpg")]
        public void FindWebPImagePath_FileDropOfOtherImage_ReturnsNull(string fileName)
        {
            var path = GetTestImagePath(fileName);
            Assert.That(RobustFile.Exists(path), Is.True, "setup");
            Assert.That(BloomClipboard.FindWebPImagePath(new[] { path }, null), Is.Null);
        }

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

        [Test]
        public void FindWebPImagePath_FileDropWhoseFirstExistingFileIsNotWebP_ReturnsNull()
        {
            // PortableClipboard would load the PNG, so we must leave this paste to it.
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

        [Test]
        public void FindWebPImagePath_FileDropIgnoresText()
        {
            // PortableClipboard looks at copied files before text, and so do we.
            var pngPath = GetTestImagePath("man.png");
            var webPPath = GetTestImagePath("bird-lossless.webp");
            Assert.That(BloomClipboard.FindWebPImagePath(new[] { pngPath }, webPPath), Is.Null);
        }

        [Test]
        public void FindWebPImagePath_UnreadableWebPFile_ReturnsNull()
        {
            // If we can't read the file, leave the paste to PortableClipboard, as before
            // BloomClipboard existed: the clipboard may also hold bitmap data it can use.
            using (var folder = new TemporaryFolder("BloomClipboardTests_Locked"))
            {
                var path = Path.Combine(folder.FolderPath, "locked.webp");
                RobustFile.Copy(GetTestImagePath("bird-lossless.webp"), path);
                Assert.That(
                    BloomClipboard.FindWebPImagePath(new[] { path }, null),
                    Is.EqualTo(path),
                    "setup: the unlocked copy should be found"
                );
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    Assert.That(BloomClipboard.FindWebPImagePath(new[] { path }, null), Is.Null);
                }
            }
        }

        [Test]
        public void FindWebPImagePath_TextPathToWebP_ReturnsIt()
        {
            var path = GetTestImagePath("bird-lossless.webp");
            Assert.That(BloomClipboard.FindWebPImagePath(null, path), Is.EqualTo(path));
            // An empty file-drop list is treated the same as none.
            Assert.That(BloomClipboard.FindWebPImagePath(new string[0], path), Is.EqualTo(path));
        }

        [Test]
        public void FindWebPImagePath_QuotedTextPathToWebP_ReturnsUnquotedPath()
        {
            // Explorer's "Copy as path" wraps the path in quotes.
            var path = GetTestImagePath("bird-lossless.webp");
            Assert.That(
                BloomClipboard.FindWebPImagePath(null, "  \"" + path + "\"\r\n"),
                Is.EqualTo(path)
            );
        }

        [TestCase("Just some ordinary text somebody copied")]
        [TestCase(@"C:\no\such\folder\bird.webp")]
        [TestCase("")]
        [TestCase(null)]
        public void FindWebPImagePath_TextNotPathToExistingFile_ReturnsNull(string text)
        {
            Assert.That(BloomClipboard.FindWebPImagePath(null, text), Is.Null);
        }

        [Test]
        public void FindWebPImagePath_TextPathToOtherImage_ReturnsNull()
        {
            var path = GetTestImagePath("man.png");
            Assert.That(RobustFile.Exists(path), Is.True, "setup");
            Assert.That(BloomClipboard.FindWebPImagePath(null, path), Is.Null);
        }

        [TestCase("man-really-webp.jpg", ".jpg")] // opaque lossy: a photo, best as JPEG
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
