using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Bloom;
using Bloom.Book;
using Bloom.ImageProcessing;
using Bloom.SafeXml;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;
using SIL.Windows.Forms.ImageToolbox;

namespace BloomTests.ImageProcessing
{
    [TestFixture]
    public class ImageUtilsTests
    {
        private const string _pathToTestImages = "src/BloomTests/ImageProcessing/images";

        [Test]
        public void ShouldChangeFormatToJpeg_Photo_True()
        {
            var path = FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "man.png"
            );
            string jpegPath = Path.Combine(Path.GetTempPath(), Path.GetTempFileName() + ".jpg");
            try
            {
                Assert.IsTrue(
                    ImageUtils.TryChangeFormatToJpegIfHelpful(
                        PalasoImage.FromFileRobustly(path),
                        jpegPath
                    )
                );
                Assert.IsTrue(File.Exists(jpegPath));
            }
            finally
            {
                if (File.Exists(jpegPath))
                    File.Delete(jpegPath);
            }
        }

        [Test]
        public void ShouldChangeFormatToJpeg_OneColor_False()
        {
            var path = FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "bird.png"
            );
            string jpegPath = Path.Combine(Path.GetTempPath(), Path.GetTempFileName() + ".jpg");
            Assert.IsFalse(
                ImageUtils.TryChangeFormatToJpegIfHelpful(
                    PalasoImage.FromFileRobustly(path),
                    jpegPath
                )
            );
            Assert.IsFalse(File.Exists(jpegPath));
        }

        [Test]
        public void ProcessAndSaveImageIntoFolder_PhotoButPNGFile_SavesAsPng()
        {
            // Import no longer converts formats; PNG is preserved as-is.
            ProcessAndSaveImageIntoFolder_AndTestResults("man.png", ImageFormat.Png);
        }

        [Test]
        public void AdjustImageForDisplay_PhotoPngThatAlsoNeedsResizing_StillConvertsToJpeg()
        {
            // The publication paths (BloomPubMaker, EpubMaker) pass maxShortSide/maxLongSide, so a
            // large photo hits resize AND format conversion together. Shrinking it must not cost us
            // the JPEG re-encoding: a photo published as a resized PNG is several times bigger than
            // it needs to be. The sibling test above uses a 118x154 image, which never resizes, so
            // it cannot catch a regression on this path (BL-16645).
            var inputPath = SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "man.png"
            );
            using (var destFolder = new TemporaryFolder("AdjustImageForDisplay_PhotoPngResized"))
            {
                // Ask for a size smaller than the source, which is what makes needsResize true.
                var result = ImageUtils.AdjustImageForDisplay(
                    inputPath,
                    destFolder.Path,
                    maxShortSide: 60,
                    maxLongSide: 80
                );

                Assert.That(result, Is.Not.Null, "Expected a processed version to be created");
                using (var img = Image.FromFile(result))
                {
                    // Sanity: it really did take the resize path, so this is the combined case.
                    Assert.That(
                        img.Width,
                        Is.LessThan(118),
                        "setup: the image should have been shrunk"
                    );
                    Assert.That(
                        img.RawFormat,
                        Is.EqualTo(ImageFormat.Jpeg),
                        "a resized photo must still be re-encoded as a JPEG"
                    );
                }
                Assert.That(Path.GetExtension(result), Is.EqualTo(".jpg"));
            }
        }

        [Test]
        public void AdjustImageForDisplay_PhotoPngWithOneStrayTransparentPixel_StillConvertsToJpeg()
        {
            // The publish path settles for a sampled transparency check deliberately: a photograph
            // carrying a stray non-opaque pixel — a common artifact of editing and AI tools — must
            // still get the JPEG re-encoding that keeps published books small. Nothing pinned that,
            // which is how an exhaustive per-pixel scan reached this call site unnoticed and blocked
            // the conversion for any such photo (BL-16645). The AI image editor keeps the exhaustive
            // scan, because it deletes the original; that is covered by its own tests.
            var inputPath = SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "man.png"
            );
            using (var sourceFolder = new TemporaryFolder("AdjustImageForDisplay_StrayAlphaSource"))
            using (var destFolder = new TemporaryFolder("AdjustImageForDisplay_StrayAlphaDest"))
            {
                var strayPath = Path.Combine(sourceFolder.Path, "stray.png");
                using (var source = new Bitmap(inputPath))
                using (
                    var photo = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb)
                )
                {
                    using (var g = Graphics.FromImage(photo))
                        g.DrawImage(source, 0, 0, source.Width, source.Height);

                    // Find a pixel the sampler doesn't look at rather than assuming which one that
                    // is, so this test doesn't quietly stop testing anything if the sample pattern
                    // changes.
                    var strayX = -1;
                    for (var y = source.Height / 2; y < source.Height && strayX < 0; ++y)
                    {
                        for (var x = source.Width / 2; x < source.Width; ++x)
                        {
                            var wasOpaque = photo.GetPixel(x, y);
                            photo.SetPixel(x, y, Color.FromArgb(254, wasOpaque));
                            if (!ImageUtils.HasTransparency(photo))
                            {
                                strayX = x;
                                break;
                            }
                            photo.SetPixel(x, y, wasOpaque);
                        }
                    }
                    Assert.That(
                        strayX,
                        Is.GreaterThanOrEqualTo(0),
                        "setup: could not find a pixel the sampling misses"
                    );
                    // Sanity: the transparency really is in the image, and only the exhaustive scan
                    // sees it — otherwise this test would pass without exercising the choice at all.
                    Assert.That(
                        ImageUtils.HasTransparency(photo),
                        Is.False,
                        "setup: sampling should miss a single stray pixel"
                    );
                    Assert.That(
                        ImageUtils.HasTransparency(photo, samplePixels: false),
                        Is.True,
                        "setup: the stray pixel should really be there"
                    );
                    photo.Save(strayPath, ImageFormat.Png);
                }

                // Ask for a size smaller than the source, the way the publication code calls in.
                var result = ImageUtils.AdjustImageForDisplay(
                    strayPath,
                    destFolder.Path,
                    maxShortSide: 60,
                    maxLongSide: 80
                );

                Assert.That(result, Is.Not.Null, "Expected a processed version to be created");
                using (var img = Image.FromFile(result))
                {
                    Assert.That(
                        img.RawFormat,
                        Is.EqualTo(ImageFormat.Jpeg),
                        "a stray transparent pixel must not cost a photo its JPEG conversion"
                    );
                }
            }
        }

        [Test]
        public void AdjustImageForDisplay_PhotoButPNGFile_ConvertsToJpeg()
        {
            var inputPath = SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "man.png"
            );
            using (var destFolder = new TemporaryFolder("AdjustImageForDisplay_PhotoPng"))
            {
                var result = ImageUtils.AdjustImageForDisplay(inputPath, destFolder.Path);
                Assert.IsNotNull(result, "Expected a processed version to be created");
                Assert.AreEqual(".jpg", Path.GetExtension(result));
                using (var img = Image.FromFile(result))
                    Assert.AreEqual(ImageFormat.Jpeg, img.RawFormat);
            }
        }

        [Test]
        public void ProcessAndSaveImageIntoFolder_Photo_KeepsJpeg()
        {
            ProcessAndSaveImageIntoFolder_AndTestResults("man.jpg", ImageFormat.Jpeg);
        }

        [Test]
        public void ProcessAndSaveImageIntoFolder_OneColor_SavesAsPng()
        {
            ProcessAndSaveImageIntoFolder_AndTestResults("bird.png", ImageFormat.Png);
        }

        private static void ProcessAndSaveImageIntoFolder_AndTestResults(
            string testImageName,
            ImageFormat expectedOutputFormat
        )
        {
            var inputPath = SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                testImageName
            );
            using (var image = PalasoImage.FromFileRobustly(inputPath))
            {
                using (
                    var folder = new TemporaryFolder(
                        "ImageUtilsTest_ProcessAndSaveImageIntoFolder_AndTestResults"
                    )
                )
                {
                    var fileName = ImageUtils.ProcessAndSaveImageIntoFolder(
                        image,
                        folder.Path,
                        false
                    );
                    Assert.AreEqual(
                        expectedOutputFormat == ImageFormat.Jpeg ? ".jpg" : ".png",
                        Path.GetExtension(fileName)
                    );
                    var outputPath = folder.Combine(fileName);
                    using (var img = Image.FromFile(outputPath))
                    {
                        Assert.AreEqual(expectedOutputFormat, img.RawFormat);
                    }

                    var alternativeThatShouldNotBeThere = Path.Combine(
                        Path.GetDirectoryName(outputPath),
                        Path.GetFileNameWithoutExtension(outputPath)
                            + (expectedOutputFormat.Equals(ImageFormat.Jpeg) ? ".png" : ".jpg")
                    );
                    Assert.IsFalse(
                        File.Exists(alternativeThatShouldNotBeThere),
                        "Did not expect to have the file " + alternativeThatShouldNotBeThere
                    );
                }
            }
        }

        // See BL-3646 which showed we were blacking out the image when converting from png to jpg
        [TestCase("shirt.png")]
        // I think shirt.png still has a transparent background after being fixed, but I'm not absolutely sure,
        // so I'm leaving both tests in place.
        [
            TestCase("shirtWithTransparentBg.png")
#if __MonoCS__
            ,
            Category("SkipOnTeamCity") // only for Linux: TeamCity creates an error from a low-level warning message.
#endif
        ]
        public static void ProcessAndSaveImageIntoFolder_SimpleImageHasTransparentBackground_ImageNotConvertedAndFileSizeNotIncreased(
            string sourceFileName
        )
        {
            var inputPath = SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                sourceFileName
            );
            var originalFileSize = new FileInfo(inputPath).Length;
            using (var image = PalasoImage.FromFileRobustly(inputPath))
            {
                using (
                    var folder = new TemporaryFolder(
                        MethodBase.GetCurrentMethod().Name + sourceFileName
                    )
                )
                {
                    var fileName = ImageUtils.ProcessAndSaveImageIntoFolder(
                        image,
                        folder.Path,
                        false
                    );
                    Assert.AreEqual(".png", Path.GetExtension(fileName));
                    var outputPath = folder.Combine(fileName);
                    using (var result = Image.FromFile(outputPath))
                    {
                        Assert.AreEqual(ImageFormat.Png, result.RawFormat);
                        Assert.That(originalFileSize <= new FileInfo(outputPath).Length);
                    }
                }
            }
        }

        [Test]
        public void AdjustImageForDisplay_LineArtPngWithTransparent_MakesBackgroundTransparent()
        {
            using (var sourceFolder = new TemporaryFolder("AdjustImageForDisplay_LineArt_Source"))
            using (var destFolder = new TemporaryFolder("AdjustImageForDisplay_LineArt_Dest"))
            {
                var sourcePath = sourceFolder.Combine("line-art.png");
                using (var bitmap = new Bitmap(40, 40))
                using (var graphics = Graphics.FromImage(bitmap))
                using (var pen = new Pen(Color.Black, 3))
                {
                    graphics.Clear(Color.White);
                    graphics.DrawLine(pen, 5, 20, 35, 20);
                    bitmap.Save(sourcePath, ImageFormat.Png);
                }

                var result = ImageUtils.AdjustImageForDisplay(
                    sourcePath,
                    destFolder.Path,
                    transparencyMode: ImageTransparencyMode.Auto
                );

                Assert.IsNotNull(result);
                Assert.AreEqual(".png", Path.GetExtension(result));
                using (var resultBitmap = (Bitmap)Image.FromFile(result))
                {
                    Assert.That(resultBitmap.GetPixel(0, 0).A, Is.EqualTo(0));
                    Assert.That(resultBitmap.GetPixel(20, 20).A, Is.EqualTo(255));
                }
            }
        }

        [Test]
        public void AdjustImageForDisplay_LineArtJpegWithTransparent_ConvertsToPngAndMakesTransparent()
        {
            using (
                var sourceFolder = new TemporaryFolder("AdjustImageForDisplay_LineArtJpeg_Source")
            )
            using (var destFolder = new TemporaryFolder("AdjustImageForDisplay_LineArtJpeg_Dest"))
            {
                var sourcePath = sourceFolder.Combine("line-art.jpg");
                using (var bitmap = new Bitmap(40, 40))
                using (var graphics = Graphics.FromImage(bitmap))
                using (var pen = new Pen(Color.Black, 3))
                {
                    graphics.Clear(Color.White);
                    graphics.DrawLine(pen, 5, 20, 35, 20);
                    bitmap.Save(sourcePath, ImageFormat.Jpeg);
                }

                var result = ImageUtils.AdjustImageForDisplay(
                    sourcePath,
                    destFolder.Path,
                    transparencyMode: ImageTransparencyMode.Auto
                );

                Assert.IsNotNull(result);
                Assert.AreEqual(".png", Path.GetExtension(result));
                using (var resultBitmap = (Bitmap)Image.FromFile(result))
                {
                    Assert.AreEqual(ImageFormat.Png, resultBitmap.RawFormat);
                    Assert.That(resultBitmap.GetPixel(0, 0).A, Is.EqualTo(0));
                    Assert.That(resultBitmap.GetPixel(20, 20).A, Is.EqualTo(255));
                }
            }
        }

        // ------------------------------------------------------------------
        // HasTransparency. It samples rather than proving opacity: the top-left corner, plus a
        // handful of pixels scattered over the rest of the image. The scattered part was added for
        // BL-16645, where a "no" makes the AI image editor re-encode the picture as a JPEG and
        // delete the original — so a picture the corner check alone called opaque was flattened
        // for good.
        // ------------------------------------------------------------------

        // A 32-bit bitmap with an opaque border and a fully transparent middle: the shape of a
        // subject knocked out of an otherwise solid canvas. The border is wider than the 15-pixel
        // corner scan, so the corner alone sees nothing but opaque pixels.
        private static Bitmap MakeBitmapTransparentOnlyInTheMiddle(int size, int borderWidth)
        {
            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            for (int y = 0; y < size; ++y)
            for (int x = 0; x < size; ++x)
            {
                var inBorder =
                    x < borderWidth
                    || y < borderWidth
                    || x >= size - borderWidth
                    || y >= size - borderWidth;
                // Vary the border colors so the PNG of this can't compress away to nothing.
                bitmap.SetPixel(
                    x,
                    y,
                    inBorder
                        ? Color.FromArgb(255, (x * 7) % 256, (y * 13) % 256, (x + y) % 256)
                        : Color.FromArgb(0, 0, 0, 0)
                );
            }
            return bitmap;
        }

        [Test]
        public void HasTransparency_TransparentOnlyInTheMiddle_IsDetected()
        {
            using (var bitmap = MakeBitmapTransparentOnlyInTheMiddle(200, 20))
            {
                // Sanity: the corner scan really is blind here, so this test is exercising the
                // scattered sampling and not just re-testing the corner.
                Assert.That(
                    bitmap.GetPixel(0, 0).A,
                    Is.EqualTo(255),
                    "setup: the corner must be opaque"
                );
                Assert.That(
                    bitmap.GetPixel(14, 14).A,
                    Is.EqualTo(255),
                    "setup: the whole 15x15 corner scan must be opaque"
                );
                Assert.That(
                    bitmap.GetPixel(100, 100).A,
                    Is.EqualTo(0),
                    "setup: the middle really is transparent"
                );

                Assert.That(
                    ImageUtils.HasTransparency(bitmap),
                    Is.True,
                    "an interior-only cutout must be found, or the AI editor would flatten it"
                );
            }
        }

        [Test]
        public void HasTransparency_PaletteBasedTransparency_IsDetected()
        {
            // A PNG-8 (or GIF-style) picture keeps its transparency in its palette, and GDI+ loads
            // it as Format8bppIndexed — a pixel format that does NOT carry the Alpha flag. So the
            // "no alpha channel, therefore opaque" shortcut must not be allowed to answer for an
            // indexed image; the palette is the only place its transparency lives.
            using (var bitmap = new Bitmap(50, 50, PixelFormat.Format8bppIndexed))
            {
                var palette = bitmap.Palette;
                palette.Entries[0] = Color.FromArgb(0, 0, 0, 0); // a transparent palette entry
                for (int i = 1; i < palette.Entries.Length; ++i)
                    palette.Entries[i] = Color.FromArgb(255, i % 256, 128, 64);
                bitmap.Palette = palette;

                // Sanity: this really is the shape described above, so the assertion below is
                // testing the indexed path and not something else.
                Assert.That(
                    (bitmap.PixelFormat & PixelFormat.Indexed),
                    Is.EqualTo(PixelFormat.Indexed),
                    "setup: must be an indexed image"
                );
                Assert.That(
                    (bitmap.PixelFormat & PixelFormat.Alpha),
                    Is.Not.EqualTo(PixelFormat.Alpha),
                    "setup: an indexed format carries no Alpha flag — that's the trap"
                );

                Assert.That(
                    ImageUtils.HasTransparency(bitmap),
                    Is.True,
                    "a transparent palette entry means the picture has transparency"
                );
            }
        }

        [Test]
        public void HasTransparency_PaletteWithNoTransparentEntry_IsFalse()
        {
            // The other direction, so the indexed path isn't just answering "true" for everything.
            using (var bitmap = new Bitmap(50, 50, PixelFormat.Format8bppIndexed))
            {
                var palette = bitmap.Palette;
                for (int i = 0; i < palette.Entries.Length; ++i)
                    palette.Entries[i] = Color.FromArgb(255, i % 256, 128, 64);
                bitmap.Palette = palette;

                Assert.That(
                    ImageUtils.HasTransparency(bitmap),
                    Is.False,
                    "an all-opaque palette means no transparency"
                );
            }
        }

        [Test]
        public void HasTransparency_FullyOpaqueRgbaImage_IsFalse()
        {
            // The other direction matters just as much: AI tools routinely emit fully opaque RGBA
            // PNGs, and a false positive here would silently switch off the size optimization
            // BL-16645 added for them.
            using (var bitmap = new Bitmap(200, 200, PixelFormat.Format32bppArgb))
            {
                for (int y = 0; y < 200; ++y)
                for (int x = 0; x < 200; ++x)
                    bitmap.SetPixel(x, y, Color.FromArgb(255, (x * 3) % 256, (y * 5) % 256, 128));

                Assert.That(
                    ImageUtils.HasTransparency(bitmap),
                    Is.False,
                    "an opaque image must not be reported as transparent"
                );
            }
        }

        [Test]
        public void HasTransparency_SameImageTwice_GivesTheSameAnswer()
        {
            // Callers delete the original on a "no", so the answer has to be reproducible rather
            // than varying from run to run. A tiny transparent patch is the case where a sampled
            // answer could differ if the sample positions were not fixed.
            using (var bitmap = new Bitmap(400, 400, PixelFormat.Format32bppArgb))
            {
                for (int y = 0; y < 400; ++y)
                for (int x = 0; x < 400; ++x)
                    bitmap.SetPixel(x, y, Color.FromArgb(255, 10, 20, 30));
                bitmap.SetPixel(390, 390, Color.FromArgb(0, 0, 0, 0));

                var first = ImageUtils.HasTransparency(bitmap);
                for (int i = 0; i < 5; ++i)
                {
                    Assert.That(
                        ImageUtils.HasTransparency(bitmap),
                        Is.EqualTo(first),
                        "the same picture must get the same answer every time"
                    );
                }
            }
        }

        // An opaque bitmap of the given format, with one transparent pixel at (x, y).
        private static Bitmap MakeOpaqueBitmapWithOneClearPixel(
            int width,
            int height,
            int x,
            int y,
            PixelFormat format
        )
        {
            var bitmap = new Bitmap(width, height, format);
            using (var g = Graphics.FromImage(bitmap))
                g.Clear(Color.FromArgb(255, 10, 20, 30));
            bitmap.SetPixel(x, y, Color.FromArgb(0, 0, 0, 0));
            return bitmap;
        }

        [Test]
        public void HasTransparency_TruecolorPngWithTrnsChunk_IsDetected()
        {
            // A PNG can be truecolour with no alpha channel and still be transparent, by naming one
            // colour transparent in a tRNS chunk. If GDI+ handed us that as a plain 24-bit bitmap,
            // the "no alpha channel" shortcut would call it opaque — and the AI image editor deletes
            // the original once told that, so it would be flattened for good. This is the same shape
            // as the palette bug (BL-16645), so it is worth pinning rather than assuming.
            var path = SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "truecolor-trns.png"
            );
            using (var image = Image.FromFile(path))
            {
                Assert.That(
                    ImageUtils.HasTransparency(image, samplePixels: false),
                    Is.True,
                    $"a tRNS truecolour PNG must not be called opaque (GDI+ gave us {image.PixelFormat})"
                );
            }
        }

        [Test]
        public void HasTransparency_Exhaustive_FindsTheOnePixelSamplingMisses()
        {
            // The whole point of samplePixels:false. This is also the test that would catch the
            // exhaustive scan reading the wrong byte or the wrong rows: if it did, it would report
            // this image opaque, and its caller would flatten the transparency and delete the
            // original.
            using (
                var bitmap = MakeOpaqueBitmapWithOneClearPixel(
                    900,
                    700,
                    613,
                    417,
                    PixelFormat.Format32bppArgb
                )
            )
            {
                // Sanity: the sampled answer really does miss it, so the two modes are being
                // distinguished rather than both trivially succeeding.
                Assert.That(
                    ImageUtils.HasTransparency(bitmap, samplePixels: true),
                    Is.False,
                    "setup: one stray pixel is exactly what sampling cannot see"
                );

                Assert.That(
                    ImageUtils.HasTransparency(bitmap, samplePixels: false),
                    Is.True,
                    "reading every pixel must find it"
                );
            }
        }

        [Test]
        // The corners of the buffer are where an off-by-one in the row arithmetic or the alpha
        // offset shows up: first pixel, last pixel of the first row, first of the last row, and
        // the very last pixel. A stride mistake typically loses the last row or the last column.
        [TestCase(0, 0)]
        [TestCase(899, 0)]
        [TestCase(0, 699)]
        [TestCase(899, 699)]
        [TestCase(898, 698)]
        public void HasTransparency_Exhaustive_FindsTransparencyAtTheEdges(int x, int y)
        {
            using (
                var bitmap = MakeOpaqueBitmapWithOneClearPixel(
                    900,
                    700,
                    x,
                    y,
                    PixelFormat.Format32bppArgb
                )
            )
            {
                Assert.That(
                    ImageUtils.HasTransparency(bitmap, samplePixels: false),
                    Is.True,
                    $"a transparent pixel at ({x},{y}) must be found"
                );
            }
        }

        [Test]
        public void HasTransparency_Exhaustive_PremultipliedFormat_IsStillRead()
        {
            // We ask LockBits for Format32bppArgb whatever the bitmap really is, so GDI+ converts
            // a premultiplied image for us. If that ever stopped working we would read nonsense.
            using (
                var bitmap = MakeOpaqueBitmapWithOneClearPixel(
                    500,
                    400,
                    321,
                    222,
                    PixelFormat.Format32bppPArgb
                )
            )
            {
                Assert.That(
                    bitmap.PixelFormat,
                    Is.EqualTo(PixelFormat.Format32bppPArgb),
                    "setup: the bitmap really is premultiplied"
                );

                Assert.That(
                    ImageUtils.HasTransparency(bitmap, samplePixels: false),
                    Is.True,
                    "transparency in a premultiplied image must still be found"
                );
            }
        }

        [Test]
        public void HasTransparency_Exhaustive_FullyOpaque_IsFalse()
        {
            // The other direction, and the one that matters for the size optimization: a false
            // positive here would stop an opaque photo ever being re-encoded.
            using (var bitmap = new Bitmap(900, 700, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bitmap))
                    g.Clear(Color.FromArgb(255, 10, 20, 30));

                Assert.That(
                    ImageUtils.HasTransparency(bitmap, samplePixels: false),
                    Is.False,
                    "an opaque image must not be reported as transparent"
                );
            }
        }

        [Test]
        public void HasTransparency_Exhaustive_IndexedImage_StillUsesThePalette()
        {
            // samplePixels only governs the non-indexed path; an indexed image is exact either way,
            // and must not fall through to the pixel scan.
            using (var bitmap = new Bitmap(50, 50, PixelFormat.Format8bppIndexed))
            {
                var palette = bitmap.Palette;
                palette.Entries[0] = Color.FromArgb(0, 0, 0, 0);
                for (int i = 1; i < palette.Entries.Length; ++i)
                    palette.Entries[i] = Color.FromArgb(255, i % 256, 128, 64);
                bitmap.Palette = palette;

                Assert.That(ImageUtils.HasTransparency(bitmap, samplePixels: false), Is.True);
                Assert.That(ImageUtils.HasTransparency(bitmap, samplePixels: true), Is.True);
            }
        }

        [Test]
        [TestCase("box", "box1")]
        [TestCase("box1", "box2")]
        [TestCase("12311", "12312")]
        [TestCase("12box", "12box1")]
        [TestCase("9", "10")]
        [TestCase("b", "b1")]
        [TestCase("box99", "box100")]
        [TestCase("IMG_20210825_141322", "IMG_20210825_141323")] // Several trailing digits already
        [TestCase("IMG_20210825141322", "IMG_20210825141322-1")] // Too many trailing digits
        [TestCase("IMG_20210825141322-1", "IMG_20210825141322-2")] // Digits all over the place...
        [TestCase("IMG_1629606288606", "IMG_1629606288606-1")] // Too many trailing digits
        public static void GetUnusedFilenameTests(string basename, string expectedResult)
        {
            const string extension = ".txt";
            using (var folder = new TemporaryFolder("UnusedFilenameTest"))
            {
                var basePath = Path.Combine(folder.Path, basename + extension);
                RobustFile.Delete(basePath); // just in case
                RobustFile.WriteAllText(basePath, "test contents");
                var filename = ImageUtils.GetUnusedFilename(
                    Path.GetDirectoryName(basePath),
                    basename,
                    extension
                );
                Assert.That(Path.GetFileNameWithoutExtension(filename), Is.EqualTo(expectedResult));
            }
        }

        [Test]
        [TestCase(3840, 2800, 3840, 2800)] // maximum size landscape
        [TestCase(2800, 3840, 2800, 3840)] // maximum size portrait
        [TestCase(3400, 2500, 3400, 2500)] // smaller than bounds landscape
        [TestCase(2500, 3400, 2500, 3400)] // smaller than bounds portrait
        [TestCase(3000, 3000, 2800, 2800)] // square too large
        [TestCase(4000, 3000, 3733, 2800)] // landscape, both too large squashed
        [TestCase(5376, 3920, 3840, 2800)] // landscape, both too large same aspect ratio
        [TestCase(5000, 3000, 3840, 2304)] // landscape, both too large elongated
        [TestCase(3000, 4000, 2800, 3733)] // portrait, both too large squashed
        [TestCase(3920, 5376, 2800, 3840)] // portrait, both too large same aspect ratio
        [TestCase(3000, 5000, 2304, 3840)] // portrait, both too large elongated
        [TestCase(2500, 5000, 1920, 3840)] // portrait, height too large
        [TestCase(5000, 2500, 3840, 1920)] // landscape, width too large
        [TestCase(3800, 3000, 3546, 2800)] // landscape, height too large
        [TestCase(3000, 3800, 2800, 3546)] // portrait, width too large
        public static void TestGetImageSizes(int width, int height, int newWidth, int newHeight)
        {
            var size = ImageUtils.GetDesiredImageSize(width, height);
            Assert.AreEqual(
                newWidth,
                size.Width,
                $"Computed width for {width},{height} is correct."
            );
            Assert.AreEqual(
                newHeight,
                size.Height,
                $"Computed height for {width},{height} is correct."
            );
        }

        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public void DrawResizedImage_TestForDashedBorder_SmallSquareImage(bool addBorder)
        {
            var path = FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "Othello 199.jpg"
            );
            var image = new Bitmap(path);
            var desiredThumbSize = new Size(200, 200);

            // SUT
            var result = ImageUtils.ResizeImageIfNecessary(desiredThumbSize, image, addBorder);

            // Testing
            TestImageResult(result, addBorder);
        }

        [Test]
        [TestCase(false, false)] // Resize, no border
        [TestCase(false, true)] // Resize, add dashed border
        [TestCase(true, false)] // Center image, no border
        [TestCase(true, true)] // Center image, add dashed border
        public void DrawResizedImage_TestForPresenceOfDashedBorder(bool centerImage, bool addBorder)
        {
            var path = FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "bird.png"
            );
            var image = new Bitmap(path);
            var desiredThumbSize = new Size(70, 70);

            // SUT
            // I don't like conditionals in tests.
            // Unfortunately "DrawResizedImage()" is a private method that is very simply wrapped by two
            // public methods.
            var result = centerImage
                ? ImageUtils.CenterImageIfNecessary(desiredThumbSize, image, addBorder)
                : ImageUtils.ResizeImageIfNecessary(desiredThumbSize, image, addBorder);

            // Test image result
            TestImageResult(result, addBorder);
        }

        private static void TestImageResult(Image result, bool shouldHaveDashedBorder)
        {
            var bitmap = new Bitmap(result);

            var hitMax = false;
            var foundFirstDash = false;
            var foundFirstSpace = false;
            const int maxX = 20;
            for (var i = 0; i < maxX; i++)
            {
                var pixel = bitmap.GetPixel(i, 0);

                if (shouldHaveDashedBorder)
                {
                    if (IsColorOpaqueBlack(pixel))
                    {
                        if (foundFirstDash && foundFirstSpace)
                        {
                            break; // found a second dash
                        }

                        foundFirstDash = true;
                    }
                    else
                    {
                        foundFirstSpace = true;
                    }
                }
                else
                {
                    Assert.That(
                        IsColorOpaqueBlack(pixel),
                        Is.False,
                        $"Point ({i}, 0) should not be black."
                    );
                }

                if (i == maxX - 1)
                    hitMax = true;
            }
            Assert.That(
                shouldHaveDashedBorder,
                Is.Not.EqualTo(hitMax),
                "We should have a dashed border and we didn't find dashes, or we should not have a border and we didn't finish the loop."
            );
        }

        private static bool IsColorOpaqueBlack(Color color)
        {
            return (color.R | color.G | color.B) == 0 && color.A == 255;
        }

        // Test cases come from two sources:
        //   1) Every PNG dropped into images/line-art-tests/yes/ is expected to be detected
        //      as line art; every PNG in images/line-art-tests/no/ is expected to not be.
        //      Drop new test images into those folders and they will be picked up automatically.
        //   2) A few images that are referenced by other tests stay in the parent images/ folder
        //      to avoid duplicating large files; they are listed explicitly below.
        public static IEnumerable<TestCaseData> LineArtTestCases()
        {
            foreach (var item in EnumerateFolderTestCases("line-art-tests/yes", true))
                yield return item;
            foreach (var item in EnumerateFolderTestCases("line-art-tests/no", false))
                yield return item;

            // These images are referenced by other tests (Spreadsheet, BloomPubMaker, etc.)
            // so they stay in the parent images/ folder rather than being moved into the
            // line-art-tests subfolders. The line-art outcome is still pinned here.
            yield return new TestCaseData("aor_Nab037.png", true);
            yield return new TestCaseData("bird.png", false); // has transparency
            yield return new TestCaseData("levels.png", false); // has transparency
            yield return new TestCaseData("bluebird.png", false); // multi-colored
            yield return new TestCaseData("lady24b.png", false); // multi-colored
            yield return new TestCaseData("Mars 2.png", false); // has transparency
        }

        private static IEnumerable<TestCaseData> EnumerateFolderTestCases(
            string subfolder,
            bool expected
        )
        {
            var folder = FileLocationUtilities.GetDirectoryDistributedWithApplication(
                _pathToTestImages,
                subfolder.Replace('/', Path.DirectorySeparatorChar)
            );
            foreach (var path in Directory.EnumerateFiles(folder, "*.png"))
            {
                var rel = subfolder + "/" + Path.GetFileName(path);
                yield return new TestCaseData(rel, expected).SetName(
                    $"TestForNeedingTransparentBackground({rel}, {expected})"
                );
            }
        }

        [Test, TestCaseSource(nameof(LineArtTestCases))]
        public void TestForNeedingTransparentBackground(string relativePath, bool expectedResult)
        {
            var imagePath = FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                relativePath.Replace('/', Path.DirectorySeparatorChar)
            );
            using (var image = PalasoImage.FromFileRobustly(imagePath))
            {
                var isBW = ImageUtils.ShouldMakeBackgroundTransparent(image);
                Assert.That(isBW, Is.EqualTo(expectedResult));
            }
        }

        // To use: uncomment GetDominantColorBucketsForDiagnostics in ImageUtils.cs (remove
        // the surrounding #if false / #endif), then run this test explicitly via the IDE or
        //   dotnet test --filter "FullyQualifiedName~DiagnoseLineArtColorBuckets"
        // It writes LineArtDiagnostics.html to the system temp folder; open it in a browser
        // to see colored swatches, sample counts, and BG/INK labels for each image.
#if false
        [Test, Explicit]
        public void DiagnoseLineArtColorBuckets()
        {
            // Add any images of interest here — relative to _pathToTestImages, or just a filename.
            var imagesToDiagnose = new[]
            {
                "line-art-tests/no/AceByDaisyError.png",
                "line-art-tests/yes/AceByDaisyError Mono antialised.png",
                "lady24b.png",
                "line-art-tests/yes/aor_oce003m.png",
                "line-art-tests/no/LineDrawing-2017.png",
            };

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<!doctype html><html><body style='font-family:sans-serif;padding:16px'>");

            foreach (var relPath in imagesToDiagnose)
            {
                var imagePath = FileLocationUtilities.GetFileDistributedWithApplication(
                    _pathToTestImages,
                    relPath.Replace('/', Path.DirectorySeparatorChar)
                );
                using var palasoImage = PalasoImage.FromFileRobustly(imagePath);
                var bmp = palasoImage.Image as Bitmap;
                if (bmp == null)
                {
                    sb.AppendLine($"<p><b>{Path.GetFileName(relPath)}</b>: not a Bitmap</p>");
                    continue;
                }
                var buckets = ImageUtils.GetDominantColorBucketsForDiagnostics(bmp);
                var result = ImageUtils.ShouldMakeBackgroundTransparent(palasoImage);
                sb.AppendLine(
                    $"<h2>{Path.GetFileName(relPath)} ({bmp.Width}×{bmp.Height}) — "
                    + $"{buckets.Length} buckets — "
                    + $"<span style='color:{(result ? "green" : "red")}'>"
                    + $"{(result ? "LINE ART ✓" : "not line art")}</span></h2>"
                );
                sb.AppendLine("<div style='display:flex;flex-wrap:wrap;gap:8px;margin-bottom:24px'>");
                foreach (var (color, count, isBackground) in buckets)
                {
                    var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                    var brightness = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
                    var textColor = brightness > 128 ? "#000" : "#fff";
                    sb.AppendLine(
                        $"<div style='text-align:center;font-size:11px;width:72px'>"
                        + $"<div style='background:{hex};width:72px;height:72px;border:1px solid #999;"
                        + $"display:flex;align-items:center;justify-content:center;"
                        + $"color:{textColor};font-weight:bold'>{(isBackground ? "BG" : "INK")}</div>"
                        + $"<div>{hex}</div>"
                        + $"<div>{color.R},{color.G},{color.B}</div>"
                        + $"<div>{count} px</div>"
                        + $"</div>"
                    );
                }
                sb.AppendLine("</div>");
            }

            sb.AppendLine("</body></html>");
            var outPath = Path.Combine(Path.GetTempPath(), "LineArtDiagnostics.html");
            File.WriteAllText(outPath, sb.ToString());
            TestContext.Out.WriteLine($"Diagnostic output written to: {outPath}");
        }
#endif

        [Test]
        public void StripMetadataFromImageFile()
        {
            var imagePath = FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "ImageWithProblematicMetadata.jpg"
            );

            using (var image = PalasoImage.FromFileRobustly(imagePath))
            {
                using (var tempFile = TempFile.WithExtension(".jpg"))
                {
                    // Verify setup
                    Assert.Throws<System.InvalidOperationException>(() =>
                        ImageUtils.SaveImageMetadata(image, tempFile.Path)
                    );

                    // SUT
                    ImageUtils.StripMetadataFromImageFile(image);

                    // Verify
                    Assert.DoesNotThrow(() => ImageUtils.SaveImageMetadata(image, tempFile.Path));
                }
            }
        }

        [TestCase("myFolder", "myFile.png")]
        [TestCase("மரியாதை ராமன் கதைகள்", "テスト画像.png")]
        public void TryGetImageSize_NonAsciiFilePath_GetsSize(string folderName, string fileName)
        {
            var originalPath = FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "bird.png"
            );

            string tempFolder = Path.Combine(Path.GetTempPath(), folderName);
            try
            {
                if (!Directory.Exists(tempFolder))
                    Directory.CreateDirectory(tempFolder);

                string newPath = Path.Combine(tempFolder, fileName);
                RobustFile.Copy(originalPath, newPath, true);

                bool result = ImageUtils.TryGetImageSize(newPath, out Size size);

                Assert.IsTrue(result, "TryGetImageSize should return true");

                // Compare with actual dimensions
                using (var img = Image.FromFile(originalPath))
                {
                    Assert.AreEqual(
                        img.Width,
                        size.Width,
                        "Width should match the actual image width"
                    );
                    Assert.AreEqual(
                        img.Height,
                        size.Height,
                        "Height should match the actual image height"
                    );
                }
            }
            finally
            {
                if (Directory.Exists(tempFolder))
                {
                    try
                    {
                        Directory.Delete(tempFolder, true);
                    }
                    catch
                    {
                        // Ignore
                    }
                }
            }
        }

        [TestCase("width", "width: 1433.16px; top: -146.703px; left: -706.162px;", 1433.16)]
        [TestCase("top", "width: 1433.16px; top: -146.703px; left: -706.162px;", -146.703)]
        [TestCase("left", "width: 1433.16px; top: -146.703px; left: -706.162px;", -706.162)]
        [TestCase("left", "some silly nonsence", 0)]
        [TestCase("left", "width: 20", 0)]
        public void GetNumberFromPx(string label, string input, double expected)
        {
            Assert.That(
                Math.Abs(expected - ImageUtils.GetNumberFromPx(label, input)),
                Is.LessThan(0.001)
            );
        }
    }

    [TestFixture]
    public class ReallyCropImagesTests
    {
        private HtmlDom _dom;
        private TemporaryFolder _folder;
        private SafeXmlElement _p1bgi1;
        private SafeXmlElement _p1i1c1;
        private SafeXmlElement _p2i1c1;
        private SafeXmlElement _p2i1c2;
        private SafeXmlElement _p2i1c3;
        private SafeXmlElement _p2i1a;
        private SafeXmlElement _p2i1b;
        private SafeXmlElement _p2i2a;
        private SafeXmlElement _p1i2c4;
        private SafeXmlElement _p1i3c5;
        private SafeXmlElement _p2i3c5;
        private byte[] _manPngBytes;
        private byte[] _manJpgBytes;
        private byte[] _lady24bPngBytes;
        private byte[] _p1i1c1Bytes;
        private byte[] _p2i1c2Bytes;
        private byte[] _p2i1c3Bytes;
        private byte[] _p1i2c4Bytes;
        private byte[] _p1i3c54Bytes;

        public static string MakeImageCanvasElement(
            string imgId,
            string src,
            string ceStyle,
            string imgStyle = null,
            bool background = false
        )
        {
            var backgroundString = background ? " bloom-backgroundImage" : "";
            var imgStyleString = imgStyle == null ? "" : $"style=\"{imgStyle}\"";
            return $"<div class=\"bloom-canvas-element{backgroundString}\" style=\"{ceStyle}\" data-bubble=\"{{`version`:`1.0`,`style`:`none`,`tails`:[],`level`:4,`backgroundColors`:[`transparent`],`shadowOffset`:0}}\">"
                + $"  <div tabindex=\"0\" class=\"bloom-imageContainer bloom-leadingElement\">"
                + $"      <img id=\"{imgId}\" src=\"{src}\" {imgStyleString} />"
                + $"  </div>"
                + $"</div>";
        }

        [OneTimeSetUp]
        public void Setup()
        {
            _folder = new TemporaryFolder("ReallyCropImagesTests");
            // much simplified dom, but enough for this.
            // We want: three uncropped images on two pages with the same name, all uncropped. Should remain the same names and file.
            // Two more images using the same filename, both cropped the same, on two pages, should get cropped once to a new name.
            // More versions of this image on two pages all cropped differently. Should get distinct names.
            // Two images using the same file name, where the first one is cropped, should result in the
            // uncropped one keeping the name, and the cropped one getting a new name.
            // Two images using another file name, all cropped the same, should keep name but change file content.
            _dom = new HtmlDom(
                @"
<html><head></head><body>
    <div class=""bloom-page"" id=""page1"">
        <div class=""marginBox"" id=""image1"">
            <div class=""bloom-canvas"">"
                    + MakeImageCanvasElement(
                        "p1bgi1",
                        "man.png",
                        "height: 378.826px; left: 325px; top: 6px; width: 140px;",
                        null,
                        true
                    )
                    + MakeImageCanvasElement(
                        "p1i1c1",
                        "man.png",
                        "height: 378.826px; left: 325px; top: 6px; width: 140px;",
                        "width: 280px; left: -80px; top: -20px"
                    )
                    + MakeImageCanvasElement(
                        "p1i2c4",
                        "lady24b.png",
                        "height: 378.826px; left: 325px; top: 6px; width: 140px;",
                        "width: 280px; left: -80px; top: -20px"
                    )
                    + MakeImageCanvasElement(
                        "p1i3c5",
                        "man%203.jpg",
                        "height: 378.826px; left: 325px; top: 6px; width: 140px;",
                        "width: 280px; left: -80px; top: -20px"
                    )
                    + @"
            </div>
        </div>
    </div>
    <div class=""bloom-page"" id=""page2"">
        <div class=""marginBox"" id=""image1"">
            <div class=""bloom-canvas"">"
                    + MakeImageCanvasElement(
                        "p2i1a",
                        "man.png",
                        "height: 350px; left: 300px; top: 6px; width: 140px;"
                    )
                    + MakeImageCanvasElement(
                        "p2i1b",
                        "man.png",
                        "height: 350px; left: 20px; top: 6px; width: 140px;"
                    )
                    // same image and crop as p1i1c1, should use same cropped file
                    + MakeImageCanvasElement(
                        "p2i1c1",
                        "man.png",
                        "height: 378.826px; left: 325px; top: 6px; width: 140px;",
                        "width: 280px; left: -80px; top: -20px",
                        true
                    )
                    // same image p1i1c1, but a different img style, so should get a different cropped file
                    + MakeImageCanvasElement(
                        "p2i1c2",
                        "man.png",
                        "height: 378.826px; left: 325px; top: 6px; width: 140px;",
                        "width: 280px; left: -70px; top: -20px",
                        true
                    )
                    // This one needs a different crop because the canvas element width is different
                    + MakeImageCanvasElement(
                        "p2i1c3",
                        "man.png",
                        "height: 378.826px; left: 325px; top: 6px; width: 130px;",
                        "width: 280px; left: -70px; top: -20px",
                        true
                    )
                    + MakeImageCanvasElement(
                        "p2i2a",
                        "lady24b.png",
                        "height: 350px; left: 300px; top: 6px; width: 140px;"
                    )
                    + MakeImageCanvasElement(
                        "p2i3c5",
                        "man%203.jpg",
                        "height: 378.826px; left: 325px; top: 6px; width: 140px;",
                        "width: 280px; left: -80px; top: -20px"
                    )
                    + @"
            </div>
        </div>
    </div>
</body></html>"
            );
            var _pathToTestImages = "src\\BloomTests\\ImageProcessing\\images";
            var path = FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "man.png"
            );
            RobustFile.Copy(path, Path.Combine(_folder.Path, "man.png"));
            _manPngBytes = RobustFile.ReadAllBytes(path);

            path = FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "man.jpg"
            );
            // We are deliberately changing the name slightly to test for a particular problem
            // that was occurring when cropping renamed the output to an 'original' name
            // containing a space. The space needs to be in the name of the image that does
            // NOT occur uncropped.
            RobustFile.Copy(path, Path.Combine(_folder.Path, "man 3.jpg"));
            _manJpgBytes = RobustFile.ReadAllBytes(path);

            path = FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "lady24b.png"
            );
            RobustFile.Copy(path, Path.Combine(_folder.Path, "lady24b.png"));
            _lady24bPngBytes = RobustFile.ReadAllBytes(path);

            // SUT
            ImageUtils.ReallyCropImages(_dom.RawDom, _folder.Path, _folder.Path);

            _p1bgi1 = _dom.SelectSingleNode("//img[@id='p1bgi1']");
            _p1i1c1 = _dom.SelectSingleNode("//img[@id='p1i1c1']");
            _p2i1c1 = _dom.SelectSingleNode("//img[@id='p2i1c1']");
            _p2i1c2 = _dom.SelectSingleNode("//img[@id='p2i1c2']");
            _p2i1c3 = _dom.SelectSingleNode("//img[@id='p2i1c3']");
            _p2i1a = _dom.SelectSingleNode("//img[@id='p2i1a']");
            _p2i1b = _dom.SelectSingleNode("//img[@id='p2i1b']");
            _p2i2a = _dom.SelectSingleNode("//img[@id='p2i2a']");
            _p1i2c4 = _dom.SelectSingleNode("//img[@id='p1i2c4']");
            _p1i3c5 = _dom.SelectSingleNode("//img[@id='p1i3c5']");
            _p2i3c5 = _dom.SelectSingleNode("//img[@id='p2i3c5']");

            _p1i1c1Bytes = RobustFile.ReadAllBytes(
                Path.Combine(_folder.Path, _p1i1c1.GetAttribute("src"))
            );
            _p2i1c2Bytes = RobustFile.ReadAllBytes(
                Path.Combine(_folder.Path, _p2i1c2.GetAttribute("src"))
            );
            _p2i1c3Bytes = RobustFile.ReadAllBytes(
                Path.Combine(_folder.Path, _p2i1c3.GetAttribute("src"))
            );
            _p1i2c4Bytes = RobustFile.ReadAllBytes(
                Path.Combine(_folder.Path, _p1i2c4.GetAttribute("src"))
            );
            _p1i3c54Bytes = RobustFile.ReadAllBytes(
                // The filename is now changing for this because there is more than one img
                // element using this source.  The original file remains, but the cropped one gets
                // a new name.
                Path.Combine(_folder.Path, _p1i3c5.GetAttribute("src"))
            );
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            _folder.Dispose();
        }

        [Test]
        public void UncroppedImages_SrcUnchanged()
        {
            Assert.That(_p1bgi1.GetAttribute("src"), Is.EqualTo("man.png"));
            Assert.That(_p2i1a.GetAttribute("src"), Is.EqualTo("man.png"));
            Assert.That(_p2i1b.GetAttribute("src"), Is.EqualTo("man.png"));
            Assert.That(_p2i2a.GetAttribute("src"), Is.EqualTo("lady24b.png"));
        }

        [Test]
        public void CroppedImage_HaveUncroppedWithSameFile_NewName()
        {
            var newSrc = _p1i1c1.GetAttribute("src");
            Assert.That(newSrc, Is.Not.EqualTo("man.png"));
            Assert.That(Path.GetExtension(newSrc), Is.EqualTo(".png"));
            newSrc = _p1i2c4.GetAttribute("src");
            Assert.That(newSrc, Is.Not.EqualTo("lady24b.png"));
            Assert.That(Path.GetExtension(newSrc), Is.EqualTo(".png"));
        }

        [Test]
        public void CroppedImage_HasDifferentContent()
        {
            Assert.That(
                _p1i1c1Bytes,
                Is.Not.EqualTo(_manPngBytes),
                "Cropped image should have different content from original"
            );
            Assert.That(
                _p2i1c2Bytes,
                Is.Not.EqualTo(_manPngBytes),
                "Cropped image should have different content from original"
            );
            Assert.That(
                _p2i1c3Bytes,
                Is.Not.EqualTo(_manPngBytes),
                "Cropped image should have different content from original"
            );
            Assert.That(
                _p1i2c4Bytes,
                Is.Not.EqualTo(_lady24bPngBytes),
                "Cropped image should have different content from original"
            );
        }

        [Test]
        public void CroppedImages_WithDifferentCrops_ProduceDifferentFiles()
        {
            Assert.That(
                _p1i1c1Bytes,
                Is.Not.EqualTo(_p2i1c2Bytes),
                "Cropped image file contents should be different"
            );
            Assert.That(
                _p1i1c1Bytes,
                Is.Not.EqualTo(_p2i1c3Bytes),
                "Cropped image file contents should be different"
            );
            Assert.That(
                _p2i1c3Bytes,
                Is.Not.EqualTo(_p2i1c2Bytes),
                "Cropped image file contents should be different"
            );
        }

        [Test]
        public void ImageWithSameSrcAndCrop_ShouldUseSameCroppedImgFile()
        {
            Assert.That(_p1i1c1.GetAttribute("src"), Is.EqualTo(_p2i1c1.GetAttribute("src")));
            Assert.That(_p2i3c5.GetAttribute("src"), Is.EqualTo(_p1i3c5.GetAttribute("src")));
        }

        [Test]
        public void ImageWithSameSrcButDifferentCrop_ShouldUseDifferentCroppedImgFile()
        {
            var i1c1Src = _p1i1c1.GetAttribute("src");
            var i2c2Src = _p2i1c2.GetAttribute("src");
            Assert.That(i1c1Src, Is.Not.EqualTo(i2c2Src));
            Assert.That(i2c2Src, Is.Not.EqualTo("man.png"));
        }

        [Test]
        public void CroppedImage_NoUncroppedWithSameName_UsesOriginalName()
        {
            var src = _p1i3c5.GetAttribute("src");
            Assert.That(src, Is.Not.EqualTo("man%203.jpg"));
            // The new name will be something like "f7a07501-4163-409b-9b65-5331423a06ab.jpg"
            Assert.That(
                Regex.IsMatch(
                    src,
                    @"^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}\.jpg"
                ),
                Is.True
            );
        }

        [Test]
        public void CroppedImage_NoUncroppedWithSameName_HasModifiedContent()
        {
            Assert.That(
                _p1i3c54Bytes,
                Is.Not.EqualTo(_manJpgBytes),
                "Cropped image should have different content from original"
            );
        }

        [Test]
        public void ReallyCropImages_SameFolderOrphanedFile_DeletesUnreferencedOriginal()
        {
            // This test verifies that when source and destination are the same,
            // and an image is replaced with a cropped version, the original is deleted
            // if no other img elements reference it.

            using (var folder = new TemporaryFolder("OrphanCleanupTest"))
            {
                // Setup: Create a simple image file
                var imagePath = Path.Combine(folder.Path, "orphan.png");
                var _pathToTestImages = "src\\BloomTests\\ImageProcessing\\images";
                var sourcePath = FileLocationUtilities.GetFileDistributedWithApplication(
                    _pathToTestImages,
                    "bird.png"
                );
                RobustFile.Copy(sourcePath, imagePath);

                // Create a DOM with only one cropped usage of the image
                var dom = new HtmlDom(
                    @"<html><head></head><body>
                <div class=""bloom-page"">
                    <div class=""marginBox"">
                        <div class=""bloom-canvas"">"
                        + MakeImageCanvasElement(
                            "testImg",
                            "orphan.png",
                            "height: 300px; left: 10px; top: 10px; width: 200px;",
                            "width: 400px; left: -50px; top: -50px"
                        )
                        + @"</div>
                    </div>
                </div>
                <div class=""bloom-page"">
                    <div class=""marginBox"">
                        <div class=""bloom-canvas"">"
                        + MakeImageCanvasElement(
                            "testImg2",
                            "orphan.png",
                            "height: 300px; left: 10px; top: 10px; width: 200px;",
                            "width: 400px; left: -50px; top: -50px"
                        )
                        + @"</div>
                    </div>
                </div>
            </body></html>"
                );

                // Verify the original file exists before cropping
                Assert.That(
                    File.Exists(imagePath),
                    Is.True,
                    "Original file should exist before cropping"
                );

                // SUT
                ImageUtils.ReallyCropImages(dom.RawDom, folder.Path, folder.Path);

                // Verify the original file was deleted (orphaned)
                Assert.That(
                    File.Exists(imagePath),
                    Is.False,
                    "Original file should be deleted when replaced and no longer referenced"
                );

                // Verify a new cropped file exists
                var img = dom.SelectSingleNode("//img[@id='testImg']");
                var newSrc = img.GetAttribute("src");
                Assert.That(newSrc, Is.Not.EqualTo("orphan.png"));
                Assert.That(
                    File.Exists(Path.Combine(folder.Path, newSrc)),
                    Is.True,
                    "New cropped file should exist"
                );
            }
        }

        [Test]
        public void ReallyCropImages_SameFolderWithUncropped_KeepsOriginalFile_WithMetadataChanged()
        {
            // This test verifies that when an image appears both cropped and uncropped,
            // the original file is kept (not orphaned).

            using (var folder = new TemporaryFolder("KeepOriginalTest"))
            {
                var imagePath = Path.Combine(folder.Path, "shared.png");
                var _pathToTestImages = "src\\BloomTests\\ImageProcessing\\images";
                var sourcePath = FileLocationUtilities.GetFileDistributedWithApplication(
                    _pathToTestImages,
                    "bird.png"
                );
                RobustFile.Copy(sourcePath, imagePath);

                // Create a DOM with both cropped and uncropped usage
                var dom = new HtmlDom(
                    @"<html><head></head><body>
                <div class=""bloom-page"">
                    <div class=""marginBox"">
                        <div class=""bloom-canvas"">"
                        + MakeImageCanvasElement(
                            "uncroppedImg",
                            "shared.png",
                            "height: 300px; left: 10px; top: 10px; width: 200px;"
                        )
                        + MakeImageCanvasElement(
                            "croppedImg",
                            "shared.png",
                            "height: 300px; left: 10px; top: 10px; width: 200px;",
                            "width: 400px; left: -50px; top: -50px"
                        )
                        + @"</div>
                    </div>
                </div>
            </body></html>"
                );

                var originalBytes = RobustFile.ReadAllBytes(imagePath);

                // SUT
                ImageUtils.ReallyCropImages(dom.RawDom, folder.Path, folder.Path, true);

                // Verify the original file still exists
                Assert.That(
                    File.Exists(imagePath),
                    Is.True,
                    "Original file should be kept when still referenced by uncropped image"
                );

                // Verify the file content has changed due to fixing the metadata.
                var currentBytes = RobustFile.ReadAllBytes(imagePath);
                Assert.That(
                    currentBytes.Length,
                    Is.Not.EqualTo(originalBytes.Length),
                    "Original file content has lost some metadata"
                );

                // Verify uncropped image still references original
                var uncroppedImg = dom.SelectSingleNode("//img[@id='uncroppedImg']");
                Assert.That(uncroppedImg.GetAttribute("src"), Is.EqualTo("shared.png"));

                // Verify cropped image has new name
                var croppedImg = dom.SelectSingleNode("//img[@id='croppedImg']");
                var croppedSrc = croppedImg.GetAttribute("src");
                Assert.That(croppedSrc, Is.Not.EqualTo("shared.png"));
                Assert.That(File.Exists(Path.Combine(folder.Path, croppedSrc)), Is.True);
            }
        }

        [Test]
        public void ReallyCropImages_SameFolderWithUncropped_KeepsOriginalFile()
        {
            // This test verifies that when an image appears both cropped and uncropped,
            // the original file is kept (not orphaned).

            using (var folder = new TemporaryFolder("KeepOriginalTest"))
            {
                var imagePath = Path.Combine(folder.Path, "shared.png");
                var _pathToTestImages = "src\\BloomTests\\ImageProcessing\\images";
                var sourcePath = FileLocationUtilities.GetFileDistributedWithApplication(
                    _pathToTestImages,
                    "bird.png"
                );
                RobustFile.Copy(sourcePath, imagePath);

                // Create a DOM with both cropped and uncropped usage
                var dom = new HtmlDom(
                    @"<html><head></head><body>
                <div class=""bloom-page"">
                    <div class=""marginBox"">
                        <div class=""bloom-canvas"">"
                        + MakeImageCanvasElement(
                            "uncroppedImg",
                            "shared.png",
                            "height: 300px; left: 10px; top: 10px; width: 200px;"
                        )
                        + MakeImageCanvasElement(
                            "croppedImg",
                            "shared.png",
                            "height: 300px; left: 10px; top: 10px; width: 200px;",
                            "width: 400px; left: -50px; top: -50px"
                        )
                        + @"</div>
                    </div>
                </div>
            </body></html>"
                );

                var originalBytes = RobustFile.ReadAllBytes(imagePath);

                // SUT
                ImageUtils.ReallyCropImages(dom.RawDom, folder.Path, folder.Path);

                // Verify the original file still exists
                Assert.That(
                    File.Exists(imagePath),
                    Is.True,
                    "Original file should be kept when still referenced by uncropped image"
                );

                // Verify the original file content is unchanged
                var currentBytes = RobustFile.ReadAllBytes(imagePath);
                Assert.That(
                    currentBytes,
                    Is.EqualTo(originalBytes),
                    "Original file content should be unchanged"
                );

                // Verify uncropped image still references original
                var uncroppedImg = dom.SelectSingleNode("//img[@id='uncroppedImg']");
                Assert.That(uncroppedImg.GetAttribute("src"), Is.EqualTo("shared.png"));

                // Verify cropped image has new name
                var croppedImg = dom.SelectSingleNode("//img[@id='croppedImg']");
                var croppedSrc = croppedImg.GetAttribute("src");
                Assert.That(croppedSrc, Is.Not.EqualTo("shared.png"));
                Assert.That(File.Exists(Path.Combine(folder.Path, croppedSrc)), Is.True);
            }
        }

        [Test]
        public void ReallyCropImages_DifferentFolders_DoesNotDeleteOriginal()
        {
            // This test verifies that when source and destination folders are different,
            // the original file is never deleted.

            using (
                var sourceFolder = new TemporaryFolder("ReallyCropImages_DifferentFolders_Source")
            )
            using (var destFolder = new TemporaryFolder("ReallyCropImages_DifferentFolders_Dest"))
            {
                var imagePath = Path.Combine(sourceFolder.Path, "original.png");
                var _pathToTestImages = "src\\BloomTests\\ImageProcessing\\images";
                var sourcePath = FileLocationUtilities.GetFileDistributedWithApplication(
                    _pathToTestImages,
                    "bird.png"
                );
                RobustFile.Copy(sourcePath, imagePath);

                var dom = new HtmlDom(
                    @"<html><head></head><body>
                <div class=""bloom-page"">
                    <div class=""marginBox"">
                        <div class=""bloom-canvas"">"
                        + MakeImageCanvasElement(
                            "testImg",
                            "original.png",
                            "height: 300px; left: 10px; top: 10px; width: 200px;",
                            "width: 400px; left: -50px; top: -50px"
                        )
                        + @"</div>
                    </div>
                </div>
            </body></html>"
                );

                // SUT
                ImageUtils.ReallyCropImages(dom.RawDom, sourceFolder.Path, destFolder.Path);

                // Verify original file still exists in source folder
                Assert.That(
                    RobustFile.Exists(imagePath),
                    Is.True,
                    "Original file should not be deleted when using different folders"
                );

                // Verify cropped file exists in destination folder
                var img = dom.SelectSingleNode("//img[@id='testImg']");
                var newSrc = img.GetAttribute("src");
                var croppedPath = Path.Combine(destFolder.Path, newSrc);
                Assert.That(
                    RobustFile.Exists(croppedPath),
                    Is.True,
                    "Cropped file should exist in destination folder"
                );

                // Verify cropped file is different than original file
                var originalBytes = RobustFile.ReadAllBytes(imagePath);
                var croppedBytes = RobustFile.ReadAllBytes(croppedPath);
                Assert.That(originalBytes, Is.Not.EqualTo(croppedBytes));
            }
        }

        [Test]
        public void ReallyCropImages_WithNullSrc_DoesNotThrow()
        {
            // Verify robustness with invalid data
            using (var folder = new TemporaryFolder("NullSrcTest"))
            {
                var dom = new HtmlDom(
                    @"<html><head></head><body>
                <div class=""bloom-page"">
                    <div class=""marginBox"">
                        <div class=""bloom-canvas"">
                            <div class=""bloom-canvas-element"" style=""height: 300px; width: 200px;"">
                                <div class=""bloom-imageContainer"">
                                    <img id=""nullImg"" style=""width: 400px;"" />
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </body></html>"
                );

                // SUT - Should not throw
                Assert.DoesNotThrow(() =>
                    ImageUtils.ReallyCropImages(dom.RawDom, folder.Path, folder.Path)
                );
            }
        }

        [Test]
        public void ReallyCropImages_WithEmptySrc_DoesNotThrow()
        {
            // Verify robustness with invalid data
            using (var folder = new TemporaryFolder("EmptySrcTest"))
            {
                var dom = new HtmlDom(
                    @"<html><head></head><body>
                <div class=""bloom-page"">
                    <div class=""marginBox"">
                        <div class=""bloom-canvas"">"
                        + MakeImageCanvasElement(
                            "emptyImg",
                            "",
                            "height: 300px; left: 10px; top: 10px; width: 200px;",
                            "width: 400px; left: -50px; top: -50px"
                        )
                        + @"</div>
                    </div>
                </div>
            </body></html>"
                );

                // SUT - Should not throw
                Assert.DoesNotThrow(() =>
                    ImageUtils.ReallyCropImages(dom.RawDom, folder.Path, folder.Path)
                );
            }
        }

        [Test]
        public void ReallyCropImages_WithUrlEncodedSpace_HandlesCorrectly()
        {
            // Test handling of URL-encoded filenames
            using (var folder = new TemporaryFolder("UrlEncodedTest"))
            {
                var imagePath = Path.Combine(folder.Path, "image with space.png");
                var _pathToTestImages = "src\\BloomTests\\ImageProcessing\\images";
                var sourcePath = FileLocationUtilities.GetFileDistributedWithApplication(
                    _pathToTestImages,
                    "bird.png"
                );
                RobustFile.Copy(sourcePath, imagePath);

                var dom = new HtmlDom(
                    @"<html><head></head><body>
                <div class=""bloom-page"">
                    <div class=""marginBox"">
                        <div class=""bloom-canvas"">"
                        + MakeImageCanvasElement(
                            "encodedImg",
                            "image%20with%20space.png", // URL encoded
                            "height: 300px; left: 10px; top: 10px; width: 200px;",
                            "width: 400px; left: -50px; top: -50px"
                        )
                        + @"</div>
                    </div>
                </div>
            </body></html>"
                );

                // SUT - Should not throw
                Assert.DoesNotThrow(() =>
                    ImageUtils.ReallyCropImages(dom.RawDom, folder.Path, folder.Path)
                );

                var img = dom.SelectSingleNode("//img[@id='encodedImg']");
                var newSrc = img.GetAttribute("src");
                var newPath = UrlPathString.GetFullyDecodedPath(folder.Path, ref newSrc);
                Assert.That(
                    File.Exists(newPath),
                    Is.True,
                    "Cropped file should exist even with URL-encoded source"
                );
            }
        }

        [Test]
        public void ReallyCropImages_CroppedImageWithDataBook_UpdatesDataDiv()
        {
            // When a cropped img element has a data-book attribute and is assigned a new filename,
            // the corresponding bloomDataDiv entry should have its src attribute and InnerText updated.

            using (var folder = new TemporaryFolder("DataDivSyncCroppedTest"))
            {
                var _pathToTestImages = "src\\BloomTests\\ImageProcessing\\images";
                var sourcePath = FileLocationUtilities.GetFileDistributedWithApplication(
                    _pathToTestImages,
                    "man.png"
                );
                RobustFile.Copy(sourcePath, Path.Combine(folder.Path, "man.png"));

                // An uncropped img with the same src forces the cropped img to get a new name.
                var dom = new HtmlDom(
                    @"<html><head></head><body>
                    <div id=""bloomDataDiv"">
                        <div data-book=""coverImage"" lang=""*"" src=""man.png"">man.png</div>
                    </div>
                    <div class=""bloom-page"">
                        <div class=""marginBox"">
                            <div class=""bloom-canvas"">"
                        + MakeImageCanvasElement(
                            "uncroppedImg",
                            "man.png",
                            "height: 300px; left: 10px; top: 10px; width: 200px;"
                        )
                        + @"<div class=""bloom-canvas-element"" style=""height: 300px; left: 10px; top: 10px; width: 200px;"">
                                <div tabindex=""0"" class=""bloom-imageContainer bloom-leadingElement"">
                                    <img id=""croppedImg"" src=""man.png"" data-book=""coverImage""
                                         style=""width: 400px; left: -50px; top: -50px"" />
                                </div>
                            </div>
                        </div>
                        </div>
                    </div>
                </body></html>"
                );

                // Sanity check: data-div entry starts with original src
                var dataDivEntry = dom.SelectSingleNode(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImage']"
                );
                Assert.That(dataDivEntry.GetAttribute("src"), Is.EqualTo("man.png"));
                Assert.That(dataDivEntry.InnerText, Is.EqualTo("man.png"));

                // SUT
                ImageUtils.ReallyCropImages(dom.RawDom, folder.Path, folder.Path);

                var croppedImg = dom.SelectSingleNode("//img[@id='croppedImg']");
                var newSrc = croppedImg.GetAttribute("src");
                Assert.That(newSrc, Is.Not.EqualTo("man.png"), "Cropped img should have a new src");

                // data-div entry should be updated to match the new filename.
                Assert.That(
                    dataDivEntry.GetAttribute("src"),
                    Is.EqualTo(newSrc),
                    "bloomDataDiv src attribute should be updated to the new cropped filename"
                );
                Assert.That(
                    dataDivEntry.InnerText,
                    Is.EqualTo(newSrc),
                    "bloomDataDiv InnerText should be updated to the new cropped filename"
                );
            }
        }

        [Test]
        public void ReallyCropImages_DuplicateCroppedImageWithDataBook_UpdatesDataDiv()
        {
            // When two img elements share an identical crop (the second hits the "duplicate" fast path),
            // and the second has a data-book attribute, the bloomDataDiv should still be updated.

            using (var folder = new TemporaryFolder("DataDivSyncDuplicateTest"))
            {
                var _pathToTestImages = "src\\BloomTests\\ImageProcessing\\images";
                var sourcePath = FileLocationUtilities.GetFileDistributedWithApplication(
                    _pathToTestImages,
                    "man.png"
                );
                RobustFile.Copy(sourcePath, Path.Combine(folder.Path, "man.png"));

                // An uncropped img forces the cropped ones to get new names.
                // Two identical crops: the first processes the key; the second hits the duplicate path.
                // The second has data-book="coverImage".
                var dom = new HtmlDom(
                    @"<html><head></head><body>
                    <div id=""bloomDataDiv"">
                        <div data-book=""coverImage"" lang=""*"" src=""man.png"">man.png</div>
                    </div>
                    <div class=""bloom-page"">
                        <div class=""marginBox"">
                            <div class=""bloom-canvas"">"
                        + MakeImageCanvasElement(
                            "uncroppedImg",
                            "man.png",
                            "height: 300px; left: 10px; top: 10px; width: 200px;"
                        )
                        + MakeImageCanvasElement(
                            "firstCrop",
                            "man.png",
                            "height: 300px; left: 10px; top: 10px; width: 200px;",
                            "width: 400px; left: -50px; top: -50px"
                        )
                        + @"<div class=""bloom-canvas-element"" style=""height: 300px; left: 10px; top: 10px; width: 200px;"">
                                <div tabindex=""0"" class=""bloom-imageContainer bloom-leadingElement"">
                                    <img id=""duplicateCrop"" src=""man.png"" data-book=""coverImage""
                                         style=""width: 400px; left: -50px; top: -50px"" />
                                </div>
                            </div>
                        </div>
                        </div>
                    </div>
                </body></html>"
                );

                // Sanity check
                var dataDivEntry = dom.SelectSingleNode(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImage']"
                );
                Assert.That(dataDivEntry.GetAttribute("src"), Is.EqualTo("man.png"));

                // SUT
                ImageUtils.ReallyCropImages(dom.RawDom, folder.Path, folder.Path);

                var firstCropImg = dom.SelectSingleNode("//img[@id='firstCrop']");
                var duplicateCropImg = dom.SelectSingleNode("//img[@id='duplicateCrop']");
                var firstSrc = firstCropImg.GetAttribute("src");
                var duplicateSrc = duplicateCropImg.GetAttribute("src");

                // Both should use the same cropped file.
                Assert.That(
                    firstSrc,
                    Is.EqualTo(duplicateSrc),
                    "Duplicate crops should reference the same cropped file"
                );
                Assert.That(firstSrc, Is.Not.EqualTo("man.png"));

                // The data-div should be updated even though duplicateCrop hit the fast path.
                Assert.That(
                    dataDivEntry.GetAttribute("src"),
                    Is.EqualTo(duplicateSrc),
                    "bloomDataDiv src should be updated via the duplicate-crop fast path"
                );
                Assert.That(
                    dataDivEntry.InnerText,
                    Is.EqualTo(duplicateSrc),
                    "bloomDataDiv InnerText should be updated via the duplicate-crop fast path"
                );
            }
        }

        [Test]
        public void ReallyCropImages_UncroppedImageWithDataBook_DataDivPreserved()
        {
            // When an img with data-book is uncropped (no rename occurs), the bloomDataDiv entry
            // should be left unchanged.

            using (var folder = new TemporaryFolder("DataDivSyncUncroppedTest"))
            {
                var _pathToTestImages = "src\\BloomTests\\ImageProcessing\\images";
                var sourcePath = FileLocationUtilities.GetFileDistributedWithApplication(
                    _pathToTestImages,
                    "man.png"
                );
                RobustFile.Copy(sourcePath, Path.Combine(folder.Path, "man.png"));

                var dom = new HtmlDom(
                    @"<html><head></head><body>
                    <div id=""bloomDataDiv"">
                        <div data-book=""coverImage"" lang=""*"" src=""man.png"">man.png</div>
                    </div>
                    <div class=""bloom-page"">
                        <div class=""marginBox"">
                            <div class=""bloom-canvas"">
                                <div class=""bloom-canvas-element"" style=""height: 300px; left: 10px; top: 10px; width: 200px;"">
                                    <div tabindex=""0"" class=""bloom-imageContainer bloom-leadingElement"">
                                        <img id=""uncroppedImg"" src=""man.png"" data-book=""coverImage"" />
                                    </div>
                                </div>
                            </div>
                        </div>
                    </div>
                </body></html>"
                );

                // Sanity check
                var dataDivEntry = dom.SelectSingleNode(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImage']"
                );
                Assert.That(dataDivEntry.GetAttribute("src"), Is.EqualTo("man.png"));
                Assert.That(dataDivEntry.InnerText, Is.EqualTo("man.png"));

                // SUT
                ImageUtils.ReallyCropImages(dom.RawDom, folder.Path, folder.Path);

                // Uncropped image keeps its src name.
                var img = dom.SelectSingleNode("//img[@id='uncroppedImg']");
                Assert.That(img.GetAttribute("src"), Is.EqualTo("man.png"));

                // data-div entry should be untouched.
                Assert.That(
                    dataDivEntry.GetAttribute("src"),
                    Is.EqualTo("man.png"),
                    "bloomDataDiv src should be unchanged for uncropped image"
                );
                Assert.That(
                    dataDivEntry.InnerText,
                    Is.EqualTo("man.png"),
                    "bloomDataDiv InnerText should be unchanged for uncropped image"
                );
            }
        }

        [Test]
        public void ReallyCropImages_CroppedImageWithNonCoverDataBook_UpdatesDataDiv()
        {
            // The old implementation only handled "coverImage". The new implementation syncs
            // the data-div for any data-book attribute. This test verifies the generality.

            using (var folder = new TemporaryFolder("DataDivSyncNonCoverTest"))
            {
                var _pathToTestImages = "src\\BloomTests\\ImageProcessing\\images";
                var sourcePath = FileLocationUtilities.GetFileDistributedWithApplication(
                    _pathToTestImages,
                    "man.png"
                );
                RobustFile.Copy(sourcePath, Path.Combine(folder.Path, "man.png"));

                // An uncropped img forces the cropped one to get a new name.
                var dom = new HtmlDom(
                    @"<html><head></head><body>
                    <div id=""bloomDataDiv"">
                        <div data-book=""someOtherImage"" lang=""*"" src=""man.png"">man.png</div>
                    </div>
                    <div class=""bloom-page"">
                        <div class=""marginBox"">
                            <div class=""bloom-canvas"">"
                        + MakeImageCanvasElement(
                            "uncroppedImg",
                            "man.png",
                            "height: 300px; left: 10px; top: 10px; width: 200px;"
                        )
                        + @"<div class=""bloom-canvas-element"" style=""height: 300px; left: 10px; top: 10px; width: 200px;"">
                                <div tabindex=""0"" class=""bloom-imageContainer bloom-leadingElement"">
                                    <img id=""croppedImg"" src=""man.png"" data-book=""someOtherImage""
                                         style=""width: 400px; left: -50px; top: -50px"" />
                                </div>
                            </div>
                        </div>
                        </div>
                    </div>
                </body></html>"
                );

                // Sanity check
                var dataDivEntry = dom.SelectSingleNode(
                    "//div[@id='bloomDataDiv']/div[@data-book='someOtherImage']"
                );
                Assert.That(dataDivEntry.GetAttribute("src"), Is.EqualTo("man.png"));

                // SUT
                ImageUtils.ReallyCropImages(dom.RawDom, folder.Path, folder.Path);

                var croppedImg = dom.SelectSingleNode("//img[@id='croppedImg']");
                var newSrc = croppedImg.GetAttribute("src");
                Assert.That(newSrc, Is.Not.EqualTo("man.png"));

                // data-div entry for a non-coverImage data-book should also be updated.
                Assert.That(
                    dataDivEntry.GetAttribute("src"),
                    Is.EqualTo(newSrc),
                    "bloomDataDiv src for non-coverImage data-book should be updated"
                );
                Assert.That(
                    dataDivEntry.InnerText,
                    Is.EqualTo(newSrc),
                    "bloomDataDiv InnerText for non-coverImage data-book should be updated"
                );
            }
        }

        [Test]
        public void ReallyCropImages_DefaultMode_RemovesCropStyle()
        {
            using (var folder = new TemporaryFolder("DefaultCropStyleRemoval"))
            {
                var imagePath = Path.Combine(folder.Path, "cover.png");
                using (var bitmap = new Bitmap(333, 221))
                {
                    bitmap.Save(imagePath, ImageFormat.Png);
                }

                var dom = new HtmlDom(
                    @"<html><head></head><body>
                    <div class=""bloom-page"">
                        <div class=""marginBox"">
                            <div class=""bloom-canvas"">"
                        + MakeImageCanvasElement(
                            "cropped",
                            "cover.png",
                            "height: 99px; left: 0px; top: 0px; width: 100px;",
                            "width: 230px; left: -55px; top: -35px"
                        )
                        + @"</div>
                        </div>
                    </div>
                </body></html>"
                );

                ImageUtils.ReallyCropImages(dom.RawDom, folder.Path, folder.Path);

                var croppedImg = dom.SelectSingleNode("//img[@id='cropped']");
                Assert.That(
                    croppedImg.HasAttribute("style"),
                    Is.False,
                    "Default crop mode should remove crop styling"
                );
            }
        }

        [Test]
        public void ReallyCropImages_UploadMode_KeepsAdjustedCropStyleToFillContainer()
        {
            using (var folder = new TemporaryFolder("UploadCropStylePreserved"))
            {
                var imagePath = Path.Combine(folder.Path, "cover.png");
                using (var bitmap = new Bitmap(333, 221))
                {
                    bitmap.Save(imagePath, ImageFormat.Png);
                }

                const double canvasWidth = 100;
                const double canvasHeight = 99;

                var dom = new HtmlDom(
                    @"<html><head></head><body>
                    <div class=""bloom-page"">
                        <div class=""marginBox"">
                            <div class=""bloom-canvas"">"
                        + MakeImageCanvasElement(
                            "cropped",
                            "cover.png",
                            "height: 99px; left: 0px; top: 0px; width: 100px;",
                            "width: 230px; left: -55px; top: -35px"
                        )
                        + @"</div>
                        </div>
                    </div>
                </body></html>"
                );

                ImageUtils.ReallyCropImages(dom.RawDom, folder.Path, folder.Path, false, true);

                var croppedImg = dom.SelectSingleNode("//img[@id='cropped']");
                var updatedStyle = croppedImg.GetAttribute("style");

                Assert.That(
                    string.IsNullOrWhiteSpace(updatedStyle),
                    Is.False,
                    "Upload crop mode should preserve style attributes"
                );

                var styledWidth = ImageUtils.GetNumberFromPx("width", updatedStyle);
                var styledLeft = ImageUtils.GetNumberFromPx("left", updatedStyle);
                var styledTop = ImageUtils.GetNumberFromPx("top", updatedStyle);

                Assert.That(styledWidth, Is.GreaterThanOrEqualTo(canvasWidth));
                Assert.That(styledLeft, Is.LessThanOrEqualTo(0.001));
                Assert.That(styledTop, Is.LessThanOrEqualTo(0.001));

                var src = croppedImg.GetAttribute("src");
                var finalImagePath = UrlPathString.GetFullyDecodedPath(folder.Path, ref src);
                Assert.That(
                    ImageUtils.TryGetImageSize(finalImagePath, out var finalImageSize),
                    Is.True
                );

                var displayedHeight = styledWidth * finalImageSize.Height / finalImageSize.Width;
                Assert.That(
                    displayedHeight,
                    Is.GreaterThanOrEqualTo(canvasHeight - 0.01),
                    "Adjusted style should ensure the cropped image still fills the canvas height"
                );
            }
        }
    }
}
