using System.IO;
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
    }
}
