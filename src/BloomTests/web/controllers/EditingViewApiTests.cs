using System;
using System.IO;
using System.Net;
using Bloom;
using Bloom.Api;
using Bloom.Book;
using Bloom.web.controllers;
using NUnit.Framework;

namespace BloomTests.web.controllers
{
    [TestFixture]
    public class EditingViewApiTests
    {
        private BloomServer _server;
        private TestEditingViewApi _api;

        [SetUp]
        public void Setup()
        {
            var bookSelection = new BookSelection();
            bookSelection.SelectBook(new Bloom.Book.Book());
            _server = new BloomServer(bookSelection);

            _api = new TestEditingViewApi();
            _api.RegisterWithApiHandler(_server.ApiHandler);
        }

        [TearDown]
        public void TearDown()
        {
            _server?.Dispose();
            _server = null;
            _api = null;
        }

        [Test]
        public void PasteImage_WhenPasteSucceeds_ReturnsOKAndForwardsRequestData()
        {
            string capturedImageId = null;
            UrlPathString capturedImageSrc = null;
            bool capturedImageIsGif = true;

            _api.PasteImageAction = (imageId, imageSrc, imageIsGif) =>
            {
                capturedImageId = imageId;
                capturedImageSrc = imageSrc;
                capturedImageIsGif = imageIsGif;
            };

            var result = ApiTest.PostString(
                _server,
                "editView/pasteImage",
                "{\"imageId\":\"image-123\",\"imageSrc\":\"images%2Fmy%20image.png\",\"imageIsGif\":false}",
                ApiTest.ContentType.JSON
            );

            Assert.That(result, Is.EqualTo("OK"));
            Assert.That(capturedImageId, Is.EqualTo("image-123"));
            Assert.That(capturedImageSrc.NotEncoded, Is.EqualTo("images/my image.png"));
            Assert.That(capturedImageIsGif, Is.False);
        }

        [Test]
        public void PasteImage_WhenPasteFailsWithInvalidOperation_ReturnsBadRequest()
        {
            _api.PasteImageAction = (_, __, ___) =>
                throw new InvalidOperationException("No image on clipboard for paste image.");

            var exception = Assert.Throws<WebException>(() =>
                ApiTest.PostString(
                    _server,
                    "editView/pasteImage",
                    "{\"imageId\":\"image-123\",\"imageSrc\":\"\",\"imageIsGif\":false}",
                    ApiTest.ContentType.JSON
                )
            );

            var response = exception.Response as HttpWebResponse;
            Assert.That(response, Is.Not.Null);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

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

                var (licenseUrl, credits) = EditingViewApi.GetInstallerLicenseMetadata(_tempFolder);

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

                var (licenseUrl, _) = EditingViewApi.GetInstallerLicenseMetadata(_tempFolder);

                Assert.That(licenseUrl, Is.EqualTo("https://creativecommons.org/licenses/by/4.0/"));
            }

            [Test]
            public void ReturnsEmpty_WhenNoRtfFile()
            {
                var (licenseUrl, credits) = EditingViewApi.GetInstallerLicenseMetadata(_tempFolder);

                Assert.That(licenseUrl, Is.Empty);
                Assert.That(credits, Is.Empty);
            }

            [Test]
            public void HandlesMultiWordGrantor()
            {
                WriteRtf("Acme Publishing House", "licenses/by-nc-sa/4.0/");

                var (_, credits) = EditingViewApi.GetInstallerLicenseMetadata(_tempFolder);

                Assert.That(credits, Is.EqualTo("Acme Publishing House"));
            }
        }

        private class TestEditingViewApi : EditingViewApi
        {
            public Action<string, UrlPathString, bool> PasteImageAction { get; set; }

            protected override void PasteImage(
                string imageId,
                UrlPathString priorImageSrc,
                bool imageIsGif,
                string backgroundColor
            )
            {
                PasteImageAction?.Invoke(imageId, priorImageSrc, imageIsGif);
            }
        }
    }
}
