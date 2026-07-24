using System;
using System.Net;
using System.Net.Http;
using System.Text;
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
        public void PasteImage_WhenPasteFailsWithInvalidOperation_ReturnsBadRequestWithMessageBody()
        {
            // The front end shows this message to the user (falling back to a generic one if
            // it doesn't arrive), so the exception message must survive the round trip in the
            // response body. See handlePasteImageApiError in bloomImages.ts.
            const string message =
                "Bloom failed to interpret the clipboard contents as an image. Possibly it was a damaged file, or too large. Try copying something else.";
            _api.PasteImageAction = (_, __, ___) => throw new InvalidOperationException(message);

            _server.EnsureListening();
            using var client = new HttpClient();
            using var response = client
                .PostAsync(
                    BloomServer.ServerUrlWithBloomPrefixEndingInSlash + "api/editView/pasteImage",
                    new StringContent(
                        "{\"imageId\":\"image-123\",\"imageSrc\":\"\",\"imageIsGif\":false}",
                        Encoding.UTF8,
                        "application/json"
                    )
                )
                .Result;

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            var body = response.Content.ReadAsStringAsync().Result;
            Assert.That(body, Is.EqualTo(message));
        }

        [Test]
        public void HeadRequest_ToMissingEndpoint_Returns404WithoutHanging()
        {
            // WriteError sends the message as the response body, but a HEAD response must not
            // have one: http.sys throws if we try, and the client would hang with no response
            // at all instead of getting its 404.
            _server.EnsureListening();
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var request = new HttpRequestMessage(
                HttpMethod.Head,
                BloomServer.ServerUrlWithBloomPrefixEndingInSlash + "api/no/such/endpoint"
            );
            using var response = client.SendAsync(request).Result;

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
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
