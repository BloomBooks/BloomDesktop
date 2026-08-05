using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Bloom.ImageProcessing;
using NUnit.Framework;
using SIL.Core.ClearShare;
using SIL.Progress;
using SIL.TestUtilities;
using SIL.Windows.Forms.ClearShare;

namespace BloomTests.ImageProcessing
{
    /// <summary>
    /// Tests for the folder-wide image shrinking that protects Bloom from oversized images:
    /// ImageUtils.NeedToShrinkImages (the cheap TagLib header scan) and
    /// ImageUtils.FixSizeAndTransparencyOfImagesInFolder (the in-place GraphicsMagick resize).
    /// This path runs both from the old-book migration (BookStorage.MigrateToMediaLevel1ShrinkLargeImages)
    /// and, since BL-16627, from BookProcessor.ProcessBook on every book with oversized images.
    /// </summary>
    /// <remarks>
    /// These tests exercise the real GraphicsMagick executable that ships in the "gm" folder
    /// beside Bloom, just as other tests in this folder (e.g. the format-conversion tests) do.
    /// The images are generated programmatically rather than committed as binaries, both to keep
    /// the repository small and because a multi-megapixel image is what makes the test meaningful.
    /// </remarks>
    [TestFixture]
    public class ImageFolderShrinkTests
    {
        // Comfortably over ImageUtils.MaxLength x MaxBreadth (3840x2800) in both dimensions, but
        // not so large that generating and resizing the images makes the tests slow.
        private const int kOversizedLongSide = 3900;
        private const int kOversizedShortSide = 2850;

        #region NeedToShrinkImages

        [Test]
        public void NeedToShrinkImages_AllImagesWithinLimits_ReturnsFalse()
        {
            using (var folder = new TemporaryFolder("NeedToShrinkImages_AllImagesWithinLimits"))
            {
                CreateImageFile(folder.Combine("small.png"), 400, 300, ImageFormat.Png);
                CreateImageFile(folder.Combine("small.jpg"), 400, 300, ImageFormat.Jpeg);
                // Exactly at the maximum is not too big.
                CreateImageFile(
                    folder.Combine("exactly-max.png"),
                    ImageUtils.MaxLength,
                    ImageUtils.MaxBreadth,
                    ImageFormat.Png
                );

                Assert.That(ImageUtils.NeedToShrinkImages(folder.Path), Is.False);
            }
        }

        [Test]
        public void NeedToShrinkImages_OversizedPng_ReturnsTrue()
        {
            using (var folder = new TemporaryFolder("NeedToShrinkImages_OversizedPng"))
            {
                CreateImageFile(folder.Combine("small.jpg"), 400, 300, ImageFormat.Jpeg);
                CreateImageFile(
                    folder.Combine("big.png"),
                    kOversizedLongSide,
                    kOversizedShortSide,
                    ImageFormat.Png
                );

                Assert.That(ImageUtils.NeedToShrinkImages(folder.Path), Is.True);
            }
        }

        [Test]
        public void NeedToShrinkImages_OversizedJpeg_ReturnsTrue()
        {
            using (var folder = new TemporaryFolder("NeedToShrinkImages_OversizedJpeg"))
            {
                CreateImageFile(folder.Combine("small.png"), 400, 300, ImageFormat.Png);
                // Portrait orientation, and the ".jpeg" spelling, which the scan also has to catch.
                CreateImageFile(
                    folder.Combine("big.jpeg"),
                    kOversizedShortSide,
                    kOversizedLongSide,
                    ImageFormat.Jpeg
                );

                Assert.That(ImageUtils.NeedToShrinkImages(folder.Path), Is.True);
            }
        }

        [Test]
        public void NeedToShrinkImages_OnlyOversizedImageIsPlaceholder_ReturnsFalse()
        {
            using (var folder = new TemporaryFolder("NeedToShrinkImages_OnlyPlaceholderIsBig"))
            {
                CreateImageFile(folder.Combine("small.png"), 400, 300, ImageFormat.Png);
                CreateImageFile(
                    folder.Combine("placeholder.png"),
                    kOversizedLongSide,
                    kOversizedShortSide,
                    ImageFormat.Png
                );

                Assert.That(
                    ImageUtils.NeedToShrinkImages(folder.Path),
                    Is.False,
                    "The placeholder image is exempt, so nothing here needs shrinking."
                );
            }
        }

        [Test]
        public void NeedToShrinkImages_NonImageFilesInFolder_ReturnsFalse()
        {
            using (var folder = new TemporaryFolder("NeedToShrinkImages_NonImageFiles"))
            {
                // Nothing here is a PNG or JPEG, so the scan should not be upset by any of it.
                File.WriteAllText(folder.Combine("book.htm"), "<html><body>hello</body></html>");
                File.WriteAllText(folder.Combine("meta.json"), "{}");
                File.WriteAllText(folder.Combine("not-really-an-image.png"), "this is not a PNG");

                Assert.That(ImageUtils.NeedToShrinkImages(folder.Path), Is.False);
            }
        }

        #endregion

        #region FixSizeAndTransparencyOfImagesInFolder

        [Test]
        public void FixSizeAndTransparencyOfImagesInFolder_ShrinksOversizedImagesAndLeavesOthersAlone()
        {
            using (var folder = new TemporaryFolder("FixSizeOfImagesInFolder_Shrinks"))
            {
                var landscapePngPath = folder.Combine("big-landscape.png");
                var portraitJpgPath = folder.Combine("big-portrait.jpg");
                var landscapeJpegPath = folder.Combine("big-landscape.jpeg");
                var smallPngPath = folder.Combine("small.png");
                var smallJpgPath = folder.Combine("small.jpg");
                var placeholderPath = folder.Combine("placeholder.png");

                CreateImageFile(
                    landscapePngPath,
                    kOversizedLongSide,
                    kOversizedShortSide,
                    ImageFormat.Png
                );
                CreateImageFile(
                    portraitJpgPath,
                    kOversizedShortSide,
                    kOversizedLongSide,
                    ImageFormat.Jpeg
                );
                CreateImageFile(
                    landscapeJpegPath,
                    kOversizedLongSide,
                    kOversizedShortSide,
                    ImageFormat.Jpeg
                );
                CreateImageFile(smallPngPath, 400, 300, ImageFormat.Png);
                CreateImageFile(smallJpgPath, 400, 300, ImageFormat.Jpeg);
                CreateImageFile(
                    placeholderPath,
                    kOversizedLongSide,
                    kOversizedShortSide,
                    ImageFormat.Png
                );

                // Sanity checks: the setup really does have oversized images, and the files we
                // expect to be left alone really are on disk before we start.
                Assert.That(
                    ImageUtils.NeedToShrinkImages(folder.Path),
                    Is.True,
                    "Test setup problem: nothing in the folder was seen as oversized."
                );
                var originalLandscapePngSize = GetImageDimensions(landscapePngPath);
                var originalPortraitJpgSize = GetImageDimensions(portraitJpgPath);
                var originalLandscapeJpegSize = GetImageDimensions(landscapeJpegPath);
                var smallPngBytes = File.ReadAllBytes(smallPngPath);
                var smallJpgBytes = File.ReadAllBytes(smallJpgPath);
                var placeholderBytes = File.ReadAllBytes(placeholderPath);

                ImageUtils.FixSizeAndTransparencyOfImagesInFolder(
                    folder.Path,
                    new List<string>(),
                    new NullProgress()
                );

                Assert.That(
                    ImageUtils.NeedToShrinkImages(folder.Path),
                    Is.False,
                    "After the fix-up, nothing in the folder should still be oversized."
                );
                AssertShrunkToFitWithAspectRatioPreserved(
                    landscapePngPath,
                    originalLandscapePngSize
                );
                AssertShrunkToFitWithAspectRatioPreserved(portraitJpgPath, originalPortraitJpgSize);
                AssertShrunkToFitWithAspectRatioPreserved(
                    landscapeJpegPath,
                    originalLandscapeJpegSize
                );

                Assert.That(
                    File.ReadAllBytes(smallPngPath),
                    Is.EqualTo(smallPngBytes),
                    "A PNG that was already small enough should not have been rewritten."
                );
                Assert.That(
                    File.ReadAllBytes(smallJpgPath),
                    Is.EqualTo(smallJpgBytes),
                    "A JPEG that was already small enough should not have been rewritten."
                );
                Assert.That(
                    File.ReadAllBytes(placeholderPath),
                    Is.EqualTo(placeholderBytes),
                    "The placeholder image is exempt and should not have been rewritten, "
                        + "even though it is oversized."
                );
            }
        }

        [Test]
        public void FixSizeAndTransparencyOfImagesInFolder_NothingOversized_ChangesNothing()
        {
            using (var folder = new TemporaryFolder("FixSizeOfImagesInFolder_NothingOversized"))
            {
                var pngPath = folder.Combine("small.png");
                var jpgPath = folder.Combine("small.jpg");
                CreateImageFile(pngPath, 400, 300, ImageFormat.Png);
                CreateImageFile(jpgPath, 400, 300, ImageFormat.Jpeg);
                var pngBytes = File.ReadAllBytes(pngPath);
                var jpgBytes = File.ReadAllBytes(jpgPath);

                ImageUtils.FixSizeAndTransparencyOfImagesInFolder(
                    folder.Path,
                    new List<string>(),
                    new NullProgress()
                );

                Assert.That(File.ReadAllBytes(pngPath), Is.EqualTo(pngBytes));
                Assert.That(File.ReadAllBytes(jpgPath), Is.EqualTo(jpgBytes));
            }
        }

        [Test]
        public void FixSizeAndTransparencyOfImagesInFolder_PreservesIntellectualPropertyMetadata()
        {
            using (var folder = new TemporaryFolder("FixSizeOfImagesInFolder_Metadata"))
            {
                var pngPath = folder.Combine("big.png");
                var jpgPath = folder.Combine("big.jpg");
                CreateImageFile(pngPath, kOversizedLongSide, kOversizedShortSide, ImageFormat.Png);
                CreateImageFile(jpgPath, kOversizedLongSide, kOversizedShortSide, ImageFormat.Jpeg);
                WriteIntellectualPropertyMetadata(pngPath, "Bilha Amos");
                WriteIntellectualPropertyMetadata(jpgPath, "Susanna Krüger");

                // Sanity check: the metadata really is in the files before we shrink them.
                // (GraphicsMagick loses TagLib metadata, so the resize code has to copy it back;
                // if it were never there to begin with, the real assertions below would pass
                // for the wrong reason.)
                AssertHasIntellectualPropertyMetadata(pngPath, "Bilha Amos", "before shrinking");
                AssertHasIntellectualPropertyMetadata(
                    jpgPath,
                    "Susanna Krüger",
                    "before shrinking"
                );

                ImageUtils.FixSizeAndTransparencyOfImagesInFolder(
                    folder.Path,
                    new List<string>(),
                    new NullProgress()
                );

                // Sanity check: the images really were rewritten, so the metadata checks below
                // are testing the copy-back and not merely an untouched file.
                Assert.That(
                    GetImageDimensions(pngPath).Width,
                    Is.LessThan(kOversizedLongSide),
                    "Test problem: the PNG was not actually shrunk."
                );
                Assert.That(
                    GetImageDimensions(jpgPath).Width,
                    Is.LessThan(kOversizedLongSide),
                    "Test problem: the JPEG was not actually shrunk."
                );

                // Report on both files even if the first one has lost something.
                Assert.Multiple(() =>
                {
                    AssertHasIntellectualPropertyMetadata(pngPath, "Bilha Amos", "after shrinking");
                    AssertHasIntellectualPropertyMetadata(
                        jpgPath,
                        "Susanna Krüger",
                        "after shrinking"
                    );
                });
            }
        }

        #endregion

        #region helpers

        /// <summary>
        /// Create an image file of the given size with enough variation in it to be a plausible
        /// stand-in for a real picture (a single flat color would compress to almost nothing and
        /// would not tell us much about resizing).
        /// </summary>
        private static void CreateImageFile(string path, int width, int height, ImageFormat format)
        {
            using (var bitmap = new Bitmap(width, height))
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.White);
                    const int shapeCount = 24;
                    for (var i = 0; i < shapeCount; ++i)
                    {
                        var color = Color.FromArgb(
                            255,
                            (i * 37) % 256,
                            (i * 91) % 256,
                            (i * 53) % 256
                        );
                        using (var brush = new SolidBrush(color))
                        {
                            graphics.FillEllipse(
                                brush,
                                i * width / shapeCount,
                                i * height / shapeCount,
                                width / 3,
                                height / 3
                            );
                        }
                    }
                }
                bitmap.Save(path, format);
            }
        }

        /// <summary>
        /// Read the pixel dimensions of an image file without holding a lock on the file
        /// (Image.FromFile would keep the file open for the life of the Image).
        /// </summary>
        private static Size GetImageDimensions(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var image = Image.FromStream(stream, false, false))
                return image.Size;
        }

        /// <summary>
        /// Assert that the image at the given path now fits inside Bloom's maximum image size,
        /// is as large as that limit allows, and has kept the aspect ratio it started with.
        /// </summary>
        private static void AssertShrunkToFitWithAspectRatioPreserved(
            string path,
            Size originalSize
        )
        {
            var label = Path.GetFileName(path);
            var expectedSize = ImageUtils.GetDesiredImageSize(
                originalSize.Width,
                originalSize.Height
            );
            Assert.That(
                expectedSize,
                Is.Not.EqualTo(originalSize),
                $"Test setup problem: {label} was not oversized to begin with."
            );

            var newSize = GetImageDimensions(path);
            // An untouched file is the signature of GraphicsMagick being missing or failing:
            // ResizeImageFileWithOptionalTransparency bails out and leaves the image oversized.
            // Say so, rather than leaving a bare dimension mismatch for someone to decode.
            Assert.That(
                newSize,
                Is.Not.EqualTo(originalSize),
                $"{label} was left at its original size. The usual cause is that GraphicsMagick "
                    + "(the 'gm' folder beside the test assembly) is missing or failed to run."
            );
            Assert.That(
                Math.Max(newSize.Width, newSize.Height),
                Is.LessThanOrEqualTo(ImageUtils.MaxLength),
                $"{label}: long dimension should now fit within the maximum."
            );
            Assert.That(
                Math.Min(newSize.Width, newSize.Height),
                Is.LessThanOrEqualTo(ImageUtils.MaxBreadth),
                $"{label}: short dimension should now fit within the maximum."
            );
            // GraphicsMagick's -scale fits the image inside the box we ask for, so it can land a
            // pixel short of the requested size; what matters is that it did not shrink further.
            Assert.That(
                newSize.Width,
                Is.EqualTo(expectedSize.Width).Within(2),
                $"{label}: unexpected width."
            );
            Assert.That(
                newSize.Height,
                Is.EqualTo(expectedSize.Height).Within(2),
                $"{label}: unexpected height."
            );

            var originalAspect = (double)originalSize.Height / originalSize.Width;
            var newAspect = (double)newSize.Height / newSize.Width;
            Assert.That(
                newAspect,
                Is.EqualTo(originalAspect).Within(0.005),
                $"{label}: aspect ratio should have been preserved."
            );
        }

        private static void WriteIntellectualPropertyMetadata(string path, string creator)
        {
            var metadata = new Metadata
            {
                Creator = creator,
                CopyrightNotice = "Copyright © 2026 " + creator,
                License = new CreativeCommonsLicense(
                    true,
                    true,
                    CreativeCommonsLicenseInfo.DerivativeRules.Derivatives
                ),
            };
            metadata.Write(path);
        }

        private static void AssertHasIntellectualPropertyMetadata(
            string path,
            string expectedCreator,
            string when
        )
        {
            var label = $"{Path.GetFileName(path)} ({when})";
            var metadata = Metadata.FromFile(path);
            Assert.That(metadata.Creator, Is.EqualTo(expectedCreator), $"{label}: creator");
            Assert.That(
                metadata.CopyrightNotice,
                Is.EqualTo("Copyright © 2026 " + expectedCreator),
                $"{label}: copyright"
            );
            Assert.That(metadata.License, Is.Not.Null, $"{label}: license");
            Assert.That(
                metadata.License.Url,
                Is.EqualTo(
                    new CreativeCommonsLicense(
                        true,
                        true,
                        CreativeCommonsLicenseInfo.DerivativeRules.Derivatives
                    ).Url
                ),
                $"{label}: license url"
            );
        }

        #endregion
    }
}
