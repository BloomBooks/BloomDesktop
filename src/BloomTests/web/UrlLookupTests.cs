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

        // The best-effort startup URL lookup must NOT report expected transient network/timeout
        // failures to Sentry (BLOOM-DESKTOP-ERZ, -2H2); it must still report genuinely unexpected
        // failures. These tests pin the classifier that makes that distinction.

        [Test]
        public void IsExpectedTransientLookupFailure_Timeout_ReturnsTrue()
        {
            Assert.That(
                UrlLookup.IsExpectedTransientLookupFailure(
                    new TimeoutException("A task was canceled.")
                ),
                Is.True
            );
        }

        [Test]
        public void IsExpectedTransientLookupFailure_Cancellation_ReturnsTrue()
        {
            Assert.That(
                UrlLookup.IsExpectedTransientLookupFailure(new TaskCanceledException()),
                Is.True
            );
        }

        [Test]
        public void IsExpectedTransientLookupFailure_WrappedWebException_ReturnsTrue()
        {
            // The AWS SDK surfaces a timeout as an AmazonServiceException wrapping a WebException,
            // so the classifier must look down the InnerException chain, not just at the top.
            var wrapped = new Exception(
                "A WebException with status Timeout was thrown.",
                new WebException("timed out", WebExceptionStatus.Timeout)
            );
            Assert.That(UrlLookup.IsExpectedTransientLookupFailure(wrapped), Is.True);
        }

        [Test]
        public void IsExpectedTransientLookupFailure_UnexpectedError_ReturnsFalse()
        {
            // A parse/logic error is a real bug and must still reach Sentry.
            Assert.That(
                UrlLookup.IsExpectedTransientLookupFailure(
                    new InvalidOperationException("bad json")
                ),
                Is.False
            );
        }
    }
}
