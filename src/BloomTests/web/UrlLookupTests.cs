using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bloom.web;
using NUnit.Framework;

namespace BloomTests.web
{
    /// <summary>
    /// Tests UrlLookup.TestInternetConnection, which decides whether the internet is reachable.
    /// We drive it through a fake HttpMessageHandler so no real network is needed.
    /// </summary>
    [TestFixture]
    public class UrlLookupTests
    {
        // Returns a configurable response, or throws, for any request.
        private sealed class FakeHttpMessageHandler : HttpMessageHandler
        {
            public HttpStatusCode StatusToReturn = HttpStatusCode.OK;
            public Exception ExceptionToThrow;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                if (ExceptionToThrow != null)
                    throw ExceptionToThrow;
                return Task.FromResult(new HttpResponseMessage(StatusToReturn));
            }
        }

        [TearDown]
        public void TearDown()
        {
            // Restore a real client so we don't affect other tests/fixtures.
            UrlLookup.SetHttpClientForTests(new HttpClient());
        }

        [Test]
        public void TestInternetConnection_SuccessStatus_ReturnsTrue()
        {
            UrlLookup.SetHttpClientForTests(
                new HttpClient(new FakeHttpMessageHandler { StatusToReturn = HttpStatusCode.OK })
            );

            Assert.That(UrlLookup.TestInternetConnection("https://example.com"), Is.True);
        }

        [Test]
        public void TestInternetConnection_ServerError_ReturnsFalse()
        {
            // A captive portal or proxy that answers with an error/interstitial instead of the real
            // site must NOT be treated as "internet available".
            UrlLookup.SetHttpClientForTests(
                new HttpClient(
                    new FakeHttpMessageHandler
                    {
                        StatusToReturn = HttpStatusCode.InternalServerError,
                    }
                )
            );

            Assert.That(UrlLookup.TestInternetConnection("https://example.com"), Is.False);
        }

        [Test]
        public void TestInternetConnection_ClientError_ReturnsFalse()
        {
            UrlLookup.SetHttpClientForTests(
                new HttpClient(
                    new FakeHttpMessageHandler { StatusToReturn = HttpStatusCode.Forbidden }
                )
            );

            Assert.That(UrlLookup.TestInternetConnection("https://example.com"), Is.False);
        }

        [Test]
        public void TestInternetConnection_RequestThrows_ReturnsFalse()
        {
            UrlLookup.SetHttpClientForTests(
                new HttpClient(
                    new FakeHttpMessageHandler
                    {
                        ExceptionToThrow = new HttpRequestException("offline"),
                    }
                )
            );

            Assert.That(UrlLookup.TestInternetConnection("https://example.com"), Is.False);
        }
    }
}
