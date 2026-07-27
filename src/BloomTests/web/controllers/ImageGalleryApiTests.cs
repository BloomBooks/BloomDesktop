using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using Bloom.web.controllers;
using NUnit.Framework;

namespace BloomTests.web.controllers
{
    [TestFixture]
    public class ImageGalleryApiTests
    {
        [TestFixture]
        public class GetInstallerLicenseMetadataTests
        {
            private string _tempFolder;

            [SetUp]
            public void Setup()
            {
                _tempFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                Directory.CreateDirectory(_tempFolder);
            }

            [TearDown]
            public void TearDown()
            {
                Directory.Delete(_tempFolder, recursive: true);
            }

            /// <summary>
            /// Writes a minimal InstallerLicense.rtf that follows the SIL template format.
            /// </summary>
            private void WriteRtf(string grantor, string ccLicensePath)
            {
                // Mimics the structure of the real SIL InstallerLicense.rtf just enough
                // for the regexes to fire: a \langN control word before the grantor and a
                // HYPERLINK directive containing the CC URL.
                var rtf =
                    $@"{{\rtf1\ansi\lang9 {grantor} grants you use of these images "
                    + $@"under the terms of the license. "
                    + $@"\fldinst{{HYPERLINK https://creativecommons.org/{ccLicensePath}legalcode }}"
                    + $@"}}";
                File.WriteAllText(Path.Combine(_tempFolder, "InstallerLicense.rtf"), rtf);
            }

            [Test]
            public void ReturnsLicenseUrlAndCredits_WhenRtfPresent()
            {
                WriteRtf("SIL International", "licenses/by-sa/4.0/");

                var (licenseUrl, credits) = ImageGalleryApi.GetInstallerLicenseMetadata(
                    _tempFolder
                );

                Assert.That(
                    licenseUrl,
                    Is.EqualTo("https://creativecommons.org/licenses/by-sa/4.0/")
                );
                Assert.That(credits, Is.EqualTo("SIL International"));
            }

            [Test]
            public void StripsLegalcodeSuffix_FromLicenseUrl()
            {
                WriteRtf("Test Org", "licenses/by/4.0/");

                var (licenseUrl, _) = ImageGalleryApi.GetInstallerLicenseMetadata(_tempFolder);

                Assert.That(licenseUrl, Is.EqualTo("https://creativecommons.org/licenses/by/4.0/"));
            }

            [Test]
            public void ReturnsEmpty_WhenNoRtfFile()
            {
                var (licenseUrl, credits) = ImageGalleryApi.GetInstallerLicenseMetadata(
                    _tempFolder
                );

                Assert.That(licenseUrl, Is.Empty);
                Assert.That(credits, Is.Empty);
            }

            [Test]
            public void HandlesMultiWordGrantor()
            {
                WriteRtf("Acme Publishing House", "licenses/by-nc-sa/4.0/");

                var (_, credits) = ImageGalleryApi.GetInstallerLicenseMetadata(_tempFolder);

                Assert.That(credits, Is.EqualTo("Acme Publishing House"));
            }
        }

        /// <summary>
        /// Covers the machinery behind BL-16597: reporting the chosen file's real dimensions,
        /// and substituting a downscaled JPEG for an image the browser cannot display.
        /// </summary>
        [TestFixture]
        public class LargeImagePreviewTests
        {
            private string _tempFolder;

            [SetUp]
            public void Setup()
            {
                _tempFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                Directory.CreateDirectory(_tempFolder);
            }

            [TearDown]
            public void TearDown()
            {
                Directory.Delete(_tempFolder, recursive: true);
            }

            /// <summary>
            /// Writes a blank PNG of the given pixel size. 1-bit-per-pixel keeps even a
            /// deliberately enormous test image down to a few megabytes of memory and a
            /// trivially small file, so the over-the-threshold cases are cheap to run.
            /// </summary>
            private string WritePng(string name, int width, int height)
            {
                var path = Path.Combine(_tempFolder, name);
                using (var bitmap = new Bitmap(width, height, PixelFormat.Format1bppIndexed))
                    bitmap.Save(path, ImageFormat.Png);
                return path;
            }

            /// <summary>
            /// Writes a JPEG carrying the given EXIF orientation, by splicing a hand-built APP1
            /// segment in directly after the start-of-image marker. Done by hand because
            /// System.Drawing cannot construct a PropertyItem to attach, and pulling WPF into
            /// this test project just to write a fixture would be a heavier price than 20 bytes
            /// of well-specified header.
            /// </summary>
            private string WriteJpegWithExifOrientation(
                string name,
                int width,
                int height,
                ushort orientation
            )
            {
                var path = Path.Combine(_tempFolder, name);
                byte[] jpeg;
                using (var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb))
                using (var memory = new MemoryStream())
                {
                    using (var g = Graphics.FromImage(bitmap))
                        g.Clear(Color.CornflowerBlue);
                    bitmap.Save(memory, ImageFormat.Jpeg);
                    jpeg = memory.ToArray();
                }
                Assert.That(
                    jpeg[0] == 0xFF && jpeg[1] == 0xD8,
                    Is.True,
                    "Test setup: expected the file to start with a JPEG start-of-image marker"
                );

                // APP1 holding a minimal little-endian TIFF header whose only IFD entry is
                // tag 274 (0x0112, Orientation), type 3 (SHORT), one value.
                var app1 = new byte[]
                {
                    0xFF,
                    0xE1, // APP1 marker
                    0x00,
                    0x22, // segment length (34), including these two bytes
                    0x45,
                    0x78,
                    0x69,
                    0x66,
                    0x00,
                    0x00, // "Exif\0\0"
                    0x49,
                    0x49,
                    0x2A,
                    0x00, // little-endian TIFF header
                    0x08,
                    0x00,
                    0x00,
                    0x00, // offset of IFD0 from the TIFF header
                    0x01,
                    0x00, // one directory entry
                    0x12,
                    0x01, // tag 0x0112 = Orientation
                    0x03,
                    0x00, // type 3 = SHORT
                    0x01,
                    0x00,
                    0x00,
                    0x00, // one value
                    (byte)(orientation & 0xFF),
                    (byte)(orientation >> 8),
                    0x00,
                    0x00, // the value, in the entry itself
                    0x00,
                    0x00,
                    0x00,
                    0x00, // no next IFD
                };

                using (var file = File.Create(path))
                {
                    file.Write(jpeg, 0, 2); // the start-of-image marker
                    file.Write(app1, 0, app1.Length);
                    file.Write(jpeg, 2, jpeg.Length - 2); // the rest of the original
                }
                return path;
            }

            /// <summary>
            /// Writes a TIFF, optionally with see-through areas. TIFF is the format the browser
            /// cannot draw, so these are the fixtures for the stand-in-because-of-format path.
            /// </summary>
            private string WriteTiff(string name, int width, int height, bool transparent)
            {
                var path = Path.Combine(_tempFolder, name);
                using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bitmap))
                        g.Clear(transparent ? Color.Transparent : Color.CornflowerBlue);
                    bitmap.Save(path, ImageFormat.Tiff);
                }
                return path;
            }

            [Test]
            public void GetImageDimensions_ReturnsThePixelDimensions()
            {
                var path = WritePng("small.png", 640, 480);

                var (width, height) = ImageGalleryApi.GetImageDimensions(path);

                Assert.That(width, Is.EqualTo(640));
                Assert.That(height, Is.EqualTo(480));
            }

            [Test]
            public void GetImageDimensions_ReturnsZeros_ForMissingFile()
            {
                var path = Path.Combine(_tempFolder, "nothing-here.png");
                Assert.That(File.Exists(path), Is.False, "Test setup: the file must not exist");

                Assert.That(ImageGalleryApi.GetImageDimensions(path), Is.EqualTo((0, 0)));
            }

            [Test]
            public void GetImageDimensions_ReturnsZeros_ForSomethingThatIsNotAnImage()
            {
                // An SVG is the realistic case: the chooser allows it, but WIC cannot read it.
                var path = Path.Combine(_tempFolder, "drawing.svg");
                File.WriteAllText(path, "<svg xmlns='http://www.w3.org/2000/svg'/>");

                Assert.That(ImageGalleryApi.GetImageDimensions(path), Is.EqualTo((0, 0)));
            }

            [Test]
            public void MakeBrowserSafePreview_ReturnsNull_WhenTheBrowserCanCopeWithTheOriginal()
            {
                var path = WritePng("modest.png", 2000, 1500);
                Assert.That(
                    2000L * 1500,
                    Is.LessThan(ImageGalleryApi.kMaxPreviewPixels),
                    "Test setup: this image is supposed to be under the threshold"
                );

                // Null means "serve the original".
                Assert.That(ImageGalleryApi.MakeBrowserSafePreview(path), Is.Null);
            }

            [Test]
            public void MakeBrowserSafePreview_DownscalesAnImageTooBigForTheBrowser()
            {
                const int originalWidth = 8000;
                const int originalHeight = 6000;
                Assert.That(
                    (long)originalWidth * originalHeight,
                    Is.GreaterThan(ImageGalleryApi.kMaxPreviewPixels),
                    "Test setup: this image is supposed to be over the threshold"
                );
                var path = WritePng("enormous.png", originalWidth, originalHeight);

                var previewPath = ImageGalleryApi.MakeBrowserSafePreview(path);

                Assert.That(previewPath, Is.Not.Null, "Should have made a downscaled stand-in");
                try
                {
                    Assert.That(File.Exists(previewPath), Is.True);
                    using var preview = Image.FromFile(previewPath);
                    // The longer side is constrained; the aspect ratio is preserved.
                    Assert.That(preview.Width, Is.EqualTo(ImageGalleryApi.kPreviewMaxDimension));
                    Assert.That(
                        preview.Height,
                        Is.EqualTo(
                            ImageGalleryApi.kPreviewMaxDimension * originalHeight / originalWidth
                        )
                    );
                }
                finally
                {
                    File.Delete(previewPath);
                }
            }

            // The eight EXIF orientations. 5-8 are the ones that stand the image on its side,
            // so for those the dimensions the viewer ends up seeing are the stored ones swapped.
            [TestCase((ushort)1, 400, 200, TestName = "Orientation1_Upright")]
            [TestCase((ushort)2, 400, 200, TestName = "Orientation2_MirroredHorizontally")]
            [TestCase((ushort)3, 400, 200, TestName = "Orientation3_UpsideDown")]
            [TestCase((ushort)4, 400, 200, TestName = "Orientation4_MirroredVertically")]
            [TestCase((ushort)5, 200, 400, TestName = "Orientation5_Transposed")]
            [TestCase((ushort)6, 200, 400, TestName = "Orientation6_QuarterTurnedClockwise")]
            [TestCase((ushort)7, 200, 400, TestName = "Orientation7_Transverse")]
            [TestCase((ushort)8, 200, 400, TestName = "Orientation8_QuarterTurnedAnticlockwise")]
            public void GetImageDimensions_AllowsForExifRotation(
                ushort orientation,
                int expectedWidth,
                int expectedHeight
            )
            {
                // Stored 400x200 every time; only the orientation tag differs.
                var path = WriteJpegWithExifOrientation(
                    $"rotated{orientation}.jpg",
                    400,
                    200,
                    orientation
                );

                var (width, height) = ImageGalleryApi.GetImageDimensions(path);

                Assert.That(width, Is.EqualTo(expectedWidth));
                Assert.That(height, Is.EqualTo(expectedHeight));
            }

            [Test]
            public void GetImageDimensions_TreatsAnOutOfRangeOrientationAsUpright()
            {
                // 9 is not a real EXIF orientation; the reader should ignore it rather than
                // feed it to the rotation table.
                var path = WriteJpegWithExifOrientation("nonsense.jpg", 400, 200, 9);

                Assert.That(ImageGalleryApi.GetImageDimensions(path), Is.EqualTo((400, 200)));
            }

            [Test]
            public void MakeBrowserSafePreview_MakesAStandInForATiff_EvenASmallOne()
            {
                // No browser will draw a TIFF, so size is not the only reason to substitute one.
                const int width = 300;
                const int height = 200;
                Assert.That(
                    (long)width * height,
                    Is.LessThan(ImageGalleryApi.kMaxPreviewPixels),
                    "Test setup: this one is meant to be under the size threshold, so that only "
                        + "its format can be the reason for a stand-in"
                );
                var path = WriteTiff("scan.tif", width, height, transparent: false);

                var previewPath = ImageGalleryApi.MakeBrowserSafePreview(path);

                Assert.That(previewPath, Is.Not.Null, "A TIFF always needs a stand-in");
                try
                {
                    using var preview = Image.FromFile(previewPath);
                    Assert.That(
                        preview.RawFormat.Guid,
                        Is.EqualTo(ImageFormat.Jpeg.Guid),
                        "The stand-in has to be something the browser can draw"
                    );
                    // Small enough already; there is no reason to enlarge it.
                    Assert.That(preview.Width, Is.EqualTo(width));
                    Assert.That(preview.Height, Is.EqualTo(height));
                }
                finally
                {
                    File.Delete(previewPath);
                }
            }

            [Test]
            public void MakeBrowserSafePreview_ShowsSeeThroughAreasAsWhite_NotBlack()
            {
                // JPEG has no alpha channel, so without compositing first, a fully transparent
                // pixel encodes as whatever is in its colour channels — normally black.
                var path = WriteTiff("transparent.tif", 120, 90, transparent: true);

                var previewPath = ImageGalleryApi.MakeBrowserSafePreview(path);

                Assert.That(previewPath, Is.Not.Null, "Test setup: expected a stand-in for a TIFF");
                try
                {
                    using var preview = new Bitmap(previewPath);
                    var corner = preview.GetPixel(0, 0);
                    var middle = preview.GetPixel(preview.Width / 2, preview.Height / 2);
                    // JPEG is lossy, so allow a little drift rather than demanding pure 255s.
                    Assert.That(
                        corner.R + corner.G + corner.B,
                        Is.GreaterThan(720),
                        $"Transparent areas should come out white; the corner was {corner}"
                    );
                    Assert.That(
                        middle.R + middle.G + middle.B,
                        Is.GreaterThan(720),
                        $"Transparent areas should come out white; the middle was {middle}"
                    );
                }
                finally
                {
                    File.Delete(previewPath);
                }
            }

            [Test]
            public void MakeBrowserSafePreview_KeepsTheColoursOfAnOpaqueImage()
            {
                // Sanity check on the compositing above: it must not wash out a solid image.
                var path = WriteTiff("solid.tif", 120, 90, transparent: false);

                var previewPath = ImageGalleryApi.MakeBrowserSafePreview(path);

                Assert.That(previewPath, Is.Not.Null, "Test setup: expected a stand-in for a TIFF");
                try
                {
                    using var preview = new Bitmap(previewPath);
                    var middle = preview.GetPixel(preview.Width / 2, preview.Height / 2);
                    var expected = Color.CornflowerBlue;
                    Assert.That(
                        Math.Abs(middle.R - expected.R)
                            + Math.Abs(middle.G - expected.G)
                            + Math.Abs(middle.B - expected.B),
                        Is.LessThan(30),
                        $"Expected roughly {expected} but the stand-in had {middle}"
                    );
                }
                finally
                {
                    File.Delete(previewPath);
                }
            }

            [Test]
            public void MakeBrowserSafePreview_WorksOnAnMtaBackgroundThread()
            {
                // This is the only place in Bloom that uses WPF's WIC-backed imaging, and the
                // API server calls it from a thread-pool thread — not the STA thread NUnit
                // runs tests on (see the Apartment attribute in BloomTests.csproj). WPF
                // imaging is fine off the UI thread as long as the bitmaps are frozen, which
                // MakeBrowserSafePreview does; this pins that down where it would otherwise
                // only be true by inspection.
                var path = WritePng("enormous-mta.png", 8000, 6000);
                string previewPath = null;
                Exception failure = null;
                var thread = new Thread(() =>
                {
                    try
                    {
                        previewPath = ImageGalleryApi.MakeBrowserSafePreview(path);
                    }
                    catch (Exception e)
                    {
                        failure = e;
                    }
                });
                thread.SetApartmentState(ApartmentState.MTA);
                thread.Start();

                Assert.That(
                    thread.Join(TimeSpan.FromMinutes(1)),
                    Is.True,
                    "Preview generation did not finish"
                );
                Assert.That(failure, Is.Null, $"Preview generation threw: {failure}");
                Assert.That(previewPath, Is.Not.Null, "Should have made a downscaled stand-in");
                try
                {
                    using var preview = Image.FromFile(previewPath);
                    Assert.That(
                        preview.Width,
                        Is.EqualTo(ImageGalleryApi.kPreviewMaxDimension),
                        "The stand-in made off the UI thread should be scaled like any other"
                    );
                }
                finally
                {
                    File.Delete(previewPath);
                }
            }

            [Test]
            public void MakeBrowserSafePreview_ReturnsNull_ForAFormatItCannotRead()
            {
                var path = Path.Combine(_tempFolder, "drawing.svg");
                File.WriteAllText(path, "<svg xmlns='http://www.w3.org/2000/svg'/>");

                // Falling back to the original is right: the browser handles SVG fine.
                Assert.That(ImageGalleryApi.MakeBrowserSafePreview(path), Is.Null);
            }
        }
    }
}
