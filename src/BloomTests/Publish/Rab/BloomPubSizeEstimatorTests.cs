using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Bloom.Book;
using Bloom.Publish.Rab;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.Publish.Rab
{
    /// <summary>
    /// Tests for the BloomPUB size estimate shown on the Apps screen before books are built, and the
    /// PNG transparency cache it relies on (BL-17062).
    /// </summary>
    public class BloomPubSizeEstimatorTests
    {
        // The default publish resolution, 1280x720, as passed to GetDesiredImageSize.
        private const int kMaxShortSide = 720;
        private const int kMaxLongSide = 1280;

        private TemporaryFolder _tempFolder;
        private string _bookFolder;
        private long _htmlBytes;

        [SetUp]
        public void SetUp()
        {
            _tempFolder = new TemporaryFolder("BloomPubSizeEstimatorTests");
            _bookFolder = Path.Combine(_tempFolder.Path, "book");
            Directory.CreateDirectory(_bookFolder);
            // The book's pages show the pictures the tests below make (publishing only shrinks
            // pictures that are shown on a page; files need not exist for every test).
            _htmlBytes = WriteBookHtml(
                _bookFolder,
                "big.jpg",
                "small.jpg",
                "rgb.png",
                "opaque.png",
                "transparent.png"
            );
        }

        /// <summary>
        /// Write the book's HTML with one page per picture, each shown in a bloom-canvas the way
        /// Bloom pages are, and return the file's size. A name starting with "keep:" is marked
        /// bloom-preserveResolution.
        /// </summary>
        private static long WriteBookHtml(string bookFolder, params string[] pictures)
        {
            var pages = string.Concat(
                pictures.Select(picture =>
                {
                    var preserve = picture.StartsWith("keep:", StringComparison.Ordinal);
                    var name = preserve ? picture.Substring(5) : picture;
                    var imgClass = preserve ? " class=\"bloom-preserveResolution\"" : "";
                    return "<div class=\"bloom-page\"><div class=\"marginBox\"><div class=\"bloom-canvas\">"
                        + $"<img src=\"{Uri.EscapeDataString(name)}\"{imgClass} />"
                        + "</div></div></div>";
                })
            );
            var htmlPath = Path.Combine(bookFolder, Path.GetFileName(bookFolder) + ".htm");
            RobustFile.WriteAllText(htmlPath, $"<html><head></head><body>{pages}</body></html>");
            return new FileInfo(htmlPath).Length;
        }

        [Test]
        public void EstimateBloomPubSize_PicturesPublishingDoesNotShrink_CountAtFullSize()
        {
            // An unused file, and a picture the author asked to keep at full resolution, both go
            // into the BloomPUB as they are (Devin, PR 8531).
            var htmlBytes = WriteBookHtml(_bookFolder, "keep:kept.jpg");
            var keptPath = Path.Combine(_bookFolder, "kept.jpg");
            var unusedPath = Path.Combine(_bookFolder, "unused.jpg");
            WriteNoisyImage(keptPath, 2560, 1440, PixelFormat.Format24bppRgb, ImageFormat.Jpeg);
            WriteNoisyImage(unusedPath, 2560, 1440, PixelFormat.Format24bppRgb, ImageFormat.Jpeg);
            var keptBytes = new FileInfo(keptPath).Length;
            var unusedBytes = new FileInfo(unusedPath).Length;
            Assert.That(
                Estimate(2560, 1440, keptBytes, false, false),
                Is.LessThan(keptBytes),
                "test setup: shrinking these pictures would have made them smaller"
            );

            var estimate = new BloomPubSizeEstimator().EstimateBloomPubSize(
                _bookFolder,
                new ImagePublishSettings()
            );

            Assert.That(estimate, Is.EqualTo(htmlBytes + keptBytes + unusedBytes));
        }

        [Test]
        public void FindPicturesPublishingProcesses_FindsPagePicturesExceptPreserved()
        {
            WriteBookHtml(_bookFolder, "a picture.png", "keep:full.jpg", "b.jpg");

            var found = BloomPubSizeEstimator.FindPicturesPublishingProcesses(_bookFolder);

            Assert.That(found.Keys, Is.EquivalentTo(new[] { "a picture.png", "b.jpg" }));
            Assert.That(found.Values, Has.All.False, "plain pictures on white pages stay opaque");
        }

        [Test]
        public void FindPicturesPublishingProcesses_FindsBackgroundImageStylePictures()
        {
            // Some older books hold a page picture this way (Devin, PR 8531).
            RobustFile.WriteAllText(
                Path.Combine(_bookFolder, "book.htm"),
                "<html><head></head><body><div class=\"bloom-page\"><div class=\"marginBox\">"
                    + "<div class=\"bloom-imageContainer bloom-background-image-in-style-attr\" "
                    + "style=\"background-image:url('my%20cover.jpg')\"></div>"
                    + "</div></div></body></html>"
            );

            var found = BloomPubSizeEstimator.FindPicturesPublishingProcesses(_bookFolder);

            Assert.That(found.Keys, Is.EquivalentTo(new[] { "my cover.jpg" }));
        }

        [TestCase(
            "class=\"bloom-transparent\"",
            "",
            ExpectedResult = true,
            TestName = "bloom-transparent is made transparent"
        )]
        [TestCase(
            "class=\"bloom-opaque\"",
            "--page-background-color: #ffe0a0",
            ExpectedResult = false,
            TestName = "bloom-opaque never is"
        )]
        [TestCase(
            "",
            "--page-background-color: #ffe0a0",
            ExpectedResult = true,
            TestName = "a colored page may make it transparent"
        )]
        [TestCase("", "", ExpectedResult = false, TestName = "a white page doesn't")]
        public bool FindPicturesPublishingProcesses_KnowsWhichMayBeMadeTransparent(
            string imgAttributes,
            string pageStyle
        )
        {
            RobustFile.WriteAllText(
                Path.Combine(_bookFolder, "book.htm"),
                $"<html><head></head><body><div class=\"bloom-page\" style=\"{pageStyle}\">"
                    + "<div class=\"marginBox\"><div class=\"bloom-canvas\">"
                    + $"<img src=\"art.jpg\" {imgAttributes} /></div></div></div></body></html>"
            );

            return BloomPubSizeEstimator.FindPicturesPublishingProcesses(_bookFolder)["art.jpg"];
        }

        [Test]
        public void EstimatePublishedImageBytes_JpegMadeTransparent_BecomesPng()
        {
            // Publishing saves a picture it makes transparent as a PNG, even a JPEG (Greptile,
            // PR 8531), and the PNG can be bigger than the JPEG was.
            var expected = (long)
                Math.Ceiling(1280 * 720 * BloomPubSizeEstimator.kAssumedResizedPngBytesPerPixel);
            Assert.That(Estimate(2560, 1440, 1_000_000, false, true), Is.EqualTo(expected));
            Assert.That(
                expected,
                Is.GreaterThan(1_000_000),
                "test setup: the PNG should be bigger"
            );
        }

        [TearDown]
        public void TearDown()
        {
            _tempFolder.Dispose();
        }

        // Every non-PNG picture in these tests is a JPEG.
        private static long Estimate(
            int width,
            int height,
            long originalBytes,
            bool isPng,
            bool hasTransparency
        )
        {
            return BloomPubSizeEstimator.EstimatePublishedImageBytes(
                width,
                height,
                originalBytes,
                isPng,
                !isPng,
                hasTransparency,
                kMaxShortSide,
                kMaxLongSide
            );
        }

        private static readonly long kJpegSizeAt1280x720 = (long)
            Math.Ceiling(1280 * 720 * BloomPubSizeEstimator.kAssumedJpegBytesPerPixel);

        [Test]
        public void EstimatePublishedImageBytes_LargeHighQualityJpeg_IsSavedAgainAtPublishQuality()
        {
            // Like Psalm 23-6.jpg in BL-17062: about 0.8 bytes per pixel. Shrinking alone would
            // leave a quarter of its bytes, but publishing saves it again at its own quality.
            Assert.That(
                kJpegSizeAt1280x720,
                Is.LessThan(4_000_000 / 4),
                "test setup: the cap should be smaller than the proportional estimate"
            );
            Assert.That(
                Estimate(2560, 1440, 4_000_000, false, false),
                Is.EqualTo(kJpegSizeAt1280x720)
            );
        }

        [Test]
        public void EstimatePublishedImageBytes_LargePortraitJpeg_ShrinksToPortraitLimits()
        {
            // 1440x2560 shrinks to 720x1280, the same number of pixels as 1280x720.
            Assert.That(
                Estimate(1440, 2560, 4_000_000, false, false),
                Is.EqualTo(kJpegSizeAt1280x720)
            );
        }

        [Test]
        public void EstimatePublishedImageBytes_LargeLowQualityJpeg_ShrinksInProportionToPixels()
        {
            // At 0.2 bytes per pixel the shrunk copy is already below our assumed JPEG size.
            // 2560x1440 has four times the pixels of 1280x720.
            Assert.That(Estimate(2560, 1440, 737_280, false, false), Is.EqualTo(184_320));
        }

        [Test]
        public void EstimatePublishedImageBytes_LargeOpaquePng_IsCappedAtAssumedJpegSize()
        {
            var expectedCap = (long)
                Math.Ceiling(1280 * 720 * BloomPubSizeEstimator.kAssumedJpegBytesPerPixel);
            Assert.That(
                expectedCap,
                Is.LessThan(10_000_000 / 4),
                "test setup: the cap should be smaller than the proportional estimate"
            );
            Assert.That(Estimate(2560, 1440, 10_000_000, true, false), Is.EqualTo(expectedCap));
        }

        [Test]
        public void EstimatePublishedImageBytes_LargeTransparentPng_StaysPngAtAssumedPngSize()
        {
            // Like Feast 1.png in BL-17062: 2550x2550, shrunk to 720x720 and kept a PNG.
            var expected = (long)
                Math.Ceiling(720 * 720 * BloomPubSizeEstimator.kAssumedResizedPngBytesPerPixel);
            Assert.That(Estimate(2550, 2550, 5_900_000, true, true), Is.EqualTo(expected));
            Assert.That(
                Estimate(2550, 2550, 5_900_000, true, false),
                Is.LessThan(expected),
                "the same picture without transparency should count as a smaller JPEG"
            );
        }

        [Test]
        public void EstimatePublishedImageBytes_TransparentPng_NeverMoreThanOriginal()
        {
            // A well-compressed piece of line art is smaller than our per-pixel assumption.
            Assert.That(Estimate(2550, 2550, 300_000, true, true), Is.EqualTo(300_000));
        }

        [Test]
        public void EstimatePublishedImageBytes_SmallJpeg_KeepsItsSize()
        {
            Assert.That(Estimate(800, 600, 123_456, false, false), Is.EqualTo(123_456));
        }

        [Test]
        public void EstimatePublishedImageBytes_SmallPngThatIsAlreadySmall_KeepsItsSize()
        {
            // Line art compresses well as PNG; it should not be counted as bigger than it is,
            // whether or not it turns out to be transparent.
            Assert.That(Estimate(800, 600, 20_000, true, false), Is.EqualTo(20_000));
            Assert.That(Estimate(800, 600, 20_000, true, true), Is.EqualTo(20_000));
        }

        [Test]
        public void EstimateBloomPubSize_RealJpegs_UseTheirDimensions()
        {
            var bigJpegPath = Path.Combine(_bookFolder, "big.jpg");
            WriteNoisyImage(bigJpegPath, 2560, 1440, PixelFormat.Format24bppRgb, ImageFormat.Jpeg);
            var smallJpegPath = Path.Combine(_bookFolder, "small.jpg");
            WriteNoisyImage(smallJpegPath, 400, 300, PixelFormat.Format24bppRgb, ImageFormat.Jpeg);
            var bigJpegBytes = new FileInfo(bigJpegPath).Length;
            var smallJpegBytes = new FileInfo(smallJpegPath).Length;
            Assert.That(bigJpegBytes, Is.GreaterThan(100_000), "test setup: big.jpg should be big");

            var estimate = new BloomPubSizeEstimator().EstimateBloomPubSize(
                _bookFolder,
                new ImagePublishSettings()
            );

            var expected =
                _htmlBytes + Estimate(2560, 1440, bigJpegBytes, false, false) + smallJpegBytes;
            Assert.That(estimate, Is.EqualTo(expected));
            Assert.That(
                estimate,
                Is.LessThan(_htmlBytes + bigJpegBytes + smallJpegBytes),
                "the big picture should count at less than its full size"
            );
        }

        [Test]
        public void EstimateBloomPubSize_PngWithNoAlphaChannel_CountsAsOpaque()
        {
            var pngPath = Path.Combine(_bookFolder, "rgb.png");
            WriteNoisyImage(pngPath, 1440, 1440, PixelFormat.Format24bppRgb, ImageFormat.Png);
            Assert.That(
                BloomPubSizeEstimator.PngCanHaveTransparency(pngPath),
                Is.False,
                "test setup: GDI+ should save a 24-bit picture as a PNG with no alpha channel"
            );

            var estimate = new BloomPubSizeEstimator().EstimateBloomPubSize(
                _bookFolder,
                new ImagePublishSettings()
            );

            Assert.That(
                estimate,
                Is.EqualTo(
                    _htmlBytes + Estimate(1440, 1440, new FileInfo(pngPath).Length, true, false)
                )
            );
        }

        [Test]
        public void EstimateBloomPubSize_OpaquePngWithAlphaChannel_CountsAsTransparent()
        {
            // The picture is opaque, but only decoding it could tell us that, so it counts at the
            // larger, transparent size; "Measure exact sizes" gives the real one (BL-17062).
            var pngPath = Path.Combine(_bookFolder, "opaque.png");
            WriteNoisyImage(pngPath, 1440, 1440, PixelFormat.Format32bppArgb, ImageFormat.Png);
            Assert.That(
                BloomPubSizeEstimator.PngCanHaveTransparency(pngPath),
                Is.True,
                "test setup: GDI+ should save a 32-bit picture as a PNG with an alpha channel"
            );
            var pngBytes = new FileInfo(pngPath).Length;
            var ifTransparent = Estimate(1440, 1440, pngBytes, true, true);
            var ifOpaque = Estimate(1440, 1440, pngBytes, true, false);
            Assert.That(
                ifOpaque,
                Is.LessThan(ifTransparent),
                "test setup: the answer should matter"
            );

            var estimate = new BloomPubSizeEstimator().EstimateBloomPubSize(
                _bookFolder,
                new ImagePublishSettings()
            );

            Assert.That(estimate, Is.EqualTo(_htmlBytes + ifTransparent));
        }

        [TestCase((byte)2, false, ExpectedResult = false, TestName = "RGB is opaque")]
        [TestCase((byte)0, false, ExpectedResult = false, TestName = "Grayscale is opaque")]
        [TestCase((byte)3, false, ExpectedResult = false, TestName = "Palette is opaque")]
        [TestCase((byte)6, false, ExpectedResult = true, TestName = "RGBA might be transparent")]
        [TestCase(
            (byte)4,
            false,
            ExpectedResult = true,
            TestName = "Gray+alpha might be transparent"
        )]
        [TestCase(
            (byte)2,
            true,
            ExpectedResult = true,
            TestName = "RGB with tRNS might be transparent"
        )]
        [TestCase(
            (byte)3,
            true,
            ExpectedResult = true,
            TestName = "Palette with tRNS might be transparent"
        )]
        public bool PngCanHaveTransparency_JudgesFromHeader(byte colorType, bool withTrns)
        {
            var path = Path.Combine(_tempFolder.Path, "test.png");
            RobustFile.WriteAllBytes(path, MakeMinimalPng(colorType, withTrns));
            return BloomPubSizeEstimator.PngCanHaveTransparency(path);
        }

        [Test]
        public void PngCanHaveTransparency_NotReallyAPng_SaysItMight()
        {
            var path = Path.Combine(_tempFolder.Path, "fake.png");
            RobustFile.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            Assert.That(BloomPubSizeEstimator.PngCanHaveTransparency(path), Is.True);
        }

        [Test]
        public void EstimateBloomPubSize_FileChanges_RecomputesEstimate()
        {
            var cssPath = Path.Combine(_bookFolder, "a.css");
            RobustFile.WriteAllBytes(cssPath, new byte[1000]);
            var estimator = new BloomPubSizeEstimator();
            var settings = new ImagePublishSettings();

            var first = estimator.EstimateBloomPubSize(_bookFolder, settings);
            Assert.That(
                estimator.EstimateBloomPubSize(_bookFolder, settings),
                Is.EqualTo(first),
                "an unchanged book should give the same estimate"
            );

            RobustFile.WriteAllBytes(cssPath, new byte[5000]);
            var second = estimator.EstimateBloomPubSize(_bookFolder, settings);

            Assert.That(second - first, Is.EqualTo(4000));
        }

        [Test]
        public void EstimateBloomPubSize_MissingFolder_ReturnsZero()
        {
            var estimate = new BloomPubSizeEstimator().EstimateBloomPubSize(
                Path.Combine(_tempFolder.Path, "no-such-book"),
                new ImagePublishSettings()
            );
            Assert.That(estimate, Is.EqualTo(0));
        }

        /// <summary>
        /// The bytes of a PNG with just the chunks the header check looks at: IHDR (with the given
        /// colour type), an optional tRNS, an empty IDAT and IEND. The CRCs are zeros, since the
        /// check doesn't read them.
        /// </summary>
        private static byte[] MakeMinimalPng(byte colorType, bool withTrns)
        {
            using var stream = new MemoryStream();
            stream.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            void WriteChunk(string type, byte[] data)
            {
                stream.Write(
                    new[]
                    {
                        (byte)(data.Length >> 24),
                        (byte)(data.Length >> 16),
                        (byte)(data.Length >> 8),
                        (byte)data.Length,
                    }
                );
                stream.Write(System.Text.Encoding.ASCII.GetBytes(type));
                stream.Write(data);
                stream.Write(new byte[4]);
            }
            // width 1, height 1, bit depth 8, colour type, compression, filter, interlace
            WriteChunk("IHDR", new byte[] { 0, 0, 0, 1, 0, 0, 0, 1, 8, colorType, 0, 0, 0 });
            if (colorType == 3)
                WriteChunk("PLTE", new byte[] { 255, 0, 0 });
            if (withTrns)
                WriteChunk("tRNS", new byte[] { 0 });
            WriteChunk("IDAT", Array.Empty<byte>());
            WriteChunk("IEND", Array.Empty<byte>());
            return stream.ToArray();
        }

        /// <summary>
        /// Write a picture full of random (opaque) colors, so it doesn't compress to almost
        /// nothing.
        /// </summary>
        private static void WriteNoisyImage(
            string path,
            int width,
            int height,
            PixelFormat pixelFormat,
            ImageFormat format
        )
        {
            const int kBlock = 8;
            var random = new Random(17062);
            using var bitmap = new Bitmap(width, height, pixelFormat);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                for (var y = 0; y < height; y += kBlock)
                {
                    for (var x = 0; x < width; x += kBlock)
                    {
                        using var brush = new SolidBrush(
                            Color.FromArgb(random.Next(256), random.Next(256), random.Next(256))
                        );
                        graphics.FillRectangle(brush, x, y, kBlock, kBlock);
                    }
                }
            }
            bitmap.Save(path, format);
        }
    }
}
