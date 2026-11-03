using System;
using System.Drawing;
using System.Drawing.Imaging;
using Bloom;
using Bloom.Utils;
using NUnit.Framework;
using SIL.IO;
using SIL.Windows.Forms.ImageToolbox;

namespace BloomTests.Utils
{
    /// <summary>
    /// Covers the in-memory clipboard that BloomClipboard uses under --e2e. Every test runs in
    /// e2e mode, so none of them touches the Windows clipboard of the machine running them.
    /// </summary>
    [TestFixture]
    public class BloomClipboardTests
    {
        [SetUp]
        public void SetUp()
        {
            Program.ParseStartupPortArguments(new[] { "--e2e" }, out var errorMessage);
            Assert.That(errorMessage, Is.Null, "Sanity check.");
            Assert.That(Program.RunningE2eTests, Is.True, "Sanity check.");
            BloomClipboard.SetE2eContents(null);
        }

        /// <summary>
        /// --e2e and the in-memory contents live in statics that outlast a test; clear them so no
        /// later fixture finds Bloom in e2e mode or something on its clipboard.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            BloomClipboard.SetE2eContents(null);
            Program.ParseStartupPortArguments(Array.Empty<string>(), out _);
        }

        private static TempFile MakePicture(int width, int height)
        {
            var file = TempFile.WithExtension(".png");
            using (var bitmap = new Bitmap(width, height))
                bitmap.Save(file.Path, ImageFormat.Png);
            return file;
        }

        [Test]
        public void SetText_UnderE2e_IsReadBackAndReplacesAPicture()
        {
            using (var picture = MakePicture(3, 2))
            {
                BloomClipboard.SetE2eContents(
                    new BloomClipboard.E2eContents { PicturePath = picture.Path }
                );
                Assert.That(BloomClipboard.CanGetImage(), Is.True, "Sanity check.");

                BloomClipboard.SetText("hello");

                Assert.That(BloomClipboard.ContainsText(), Is.True);
                Assert.That(BloomClipboard.GetText(), Is.EqualTo("hello"));
                Assert.That(
                    BloomClipboard.CanGetImage(),
                    Is.False,
                    "A copy replaces everything the clipboard held."
                );
            }
        }

        [Test]
        public void GetText_EmptyUnderE2e_IsEmptyString()
        {
            Assert.That(BloomClipboard.ContainsText(), Is.False);
            Assert.That(BloomClipboard.GetText(), Is.EqualTo(""));
            Assert.That(BloomClipboard.GetImageFromClipboardWithExceptions(), Is.Null);
            Assert.That(BloomClipboard.ContainsFileDropList(), Is.False);
            Assert.That(BloomClipboard.GetFileDropList(), Is.Empty);
        }

        [Test]
        public void CopyImageToClipboard_PictureFromAFile_PutsThePictureAndItsPath()
        {
            using (var picture = MakePicture(3, 2))
            using (var image = PalasoImage.FromFileRobustly(picture.Path))
            {
                Assert.That(image.OriginalFilePath, Is.EqualTo(picture.Path), "Sanity check.");

                BloomClipboard.CopyImageToClipboard(image);

                var contents = BloomClipboard.GetE2eContents();
                Assert.That(contents.PicturePath, Is.EqualTo(picture.Path));
                Assert.That(
                    contents.Text,
                    Is.EqualTo(picture.Path),
                    "libpalaso puts the file's path beside the picture as text."
                );
                using (var pasted = BloomClipboard.GetImageFromClipboardWithExceptions())
                    Assert.That(pasted.Image.Size, Is.EqualTo(new Size(3, 2)));
            }
        }

        [Test]
        public void CopyImageToClipboard_PictureWithoutAFile_CanBePastedBack()
        {
            using (var bitmap = new Bitmap(5, 4))
            using (var image = PalasoImage.FromImage(bitmap))
            {
                Assert.That(image.OriginalFilePath, Is.Null.Or.Empty, "Sanity check.");

                BloomClipboard.CopyImageToClipboard(image);

                Assert.That(BloomClipboard.CanGetImage(), Is.True);
                Assert.That(BloomClipboard.ContainsText(), Is.False);
                using (var pasted = BloomClipboard.GetImageFromClipboardWithExceptions())
                    Assert.That(pasted.Image.Size, Is.EqualTo(new Size(5, 4)));
            }
        }

        [Test]
        public void CanGetImage_TextOrCopiedFileNamingAPicture_IsTrue()
        {
            using (var picture = MakePicture(2, 2))
            {
                BloomClipboard.SetE2eContents(
                    new BloomClipboard.E2eContents { Text = picture.Path }
                );
                Assert.That(BloomClipboard.CanGetImage(), Is.True, "a text path to a picture");

                BloomClipboard.SetE2eContents(
                    new BloomClipboard.E2eContents { Files = new[] { picture.Path } }
                );
                Assert.That(BloomClipboard.CanGetImage(), Is.True, "a copied picture file");
                Assert.That(BloomClipboard.GetFileDropList(), Is.EqualTo(new[] { picture.Path }));
            }
        }

        [Test]
        public void CanGetImage_TextThatIsNotAPicturePath_IsFalse()
        {
            using (var notAPicture = TempFile.WithExtension(".txt"))
            {
                BloomClipboard.SetE2eContents(
                    new BloomClipboard.E2eContents { Text = notAPicture.Path }
                );
                Assert.That(BloomClipboard.CanGetImage(), Is.False, "an existing non-picture file");

                BloomClipboard.SetE2eContents(
                    new BloomClipboard.E2eContents { Text = "just some words" }
                );
                Assert.That(BloomClipboard.CanGetImage(), Is.False, "ordinary text");
                Assert.That(BloomClipboard.GetImageFromClipboard(), Is.Null);
            }
        }
    }
}
