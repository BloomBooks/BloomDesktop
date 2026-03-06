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

        private class TestEditingViewApi : EditingViewApi
        {
            public Action<string, UrlPathString, bool> PasteImageAction { get; set; }

            protected override void PasteImage(
                string imageId,
                UrlPathString priorImageSrc,
                bool imageIsGif
            )
            {
                PasteImageAction?.Invoke(imageId, priorImageSrc, imageIsGif);
            }
        }
    }
}