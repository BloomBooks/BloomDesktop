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
