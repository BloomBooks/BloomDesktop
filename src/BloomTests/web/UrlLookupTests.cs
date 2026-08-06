using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bloom.web;
using NUnit.Framework;
using Sentry;

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

    /// <summary>
    /// Tests the labelling that lets us tell the two kinds of URL-lookup failure apart in Sentry
    /// (BL-16575). The point of the change is that a patient background failure - which is
    /// surprising and worth investigating - must not be grouped with the routine quick blocking
    /// timeouts, nor with the pre-BL-16575 events. Since it is the fingerprint that decides
    /// grouping, that is what these tests check.
    /// </summary>
    [TestFixture]
    public class UrlLookupSentryLabellingTests
    {
        // A real Sentry Scope, which is all DescribeFailureForSentry touches. This needs no
        // SentrySdk.Init and sends nothing anywhere.
        private static Scope MakeScope() => new Scope(new SentryOptions());

        private static Scope Describe(bool inBackground, double budgetSeconds, Exception e)
        {
            var scope = MakeScope();
            // Sanity check: an untouched scope must not already carry what we're about to assert.
            Assert.That(
                scope.Tags.ContainsKey("urlLookupMode"),
                Is.False,
                "test setup problem: the fresh scope already had the tag we are testing for"
            );
            UrlLookup.DescribeFailureForSentry(
                scope,
                UrlLookup.ModeName(inBackground),
                TimeSpan.FromSeconds(budgetSeconds),
                e
            );
            return scope;
        }

        [Test]
        public void DescribeFailureForSentry_BackgroundAndBlocking_GetDifferentFingerprints()
        {
            var e = new TimeoutException("timed out");
            var background = Describe(inBackground: true, budgetSeconds: 40, e);
            var blocking = Describe(inBackground: false, budgetSeconds: 3, e);

            Assert.That(
                background.Fingerprint,
                Is.Not.EqualTo(blocking.Fingerprint),
                "the two failure modes must land in separate Sentry issues"
            );
        }

        [Test]
        public void DescribeFailureForSentry_Background_TagsAndFingerprintDescribeTheBudget()
        {
            var scope = Describe(
                inBackground: true,
                budgetSeconds: 40,
                new TimeoutException("timed out")
            );

            Assert.That(scope.Tags["urlLookupMode"], Is.EqualTo("background"));
            Assert.That(scope.Tags["urlLookupBudgetSeconds"], Is.EqualTo("40"));
            Assert.That(
                scope.Fingerprint,
                Is.EqualTo(
                    new[]
                    {
                        "UrlLookup.TryGetUrlDataFromServer",
                        "background",
                        "System.TimeoutException",
                    }
                )
            );
        }

        [Test]
        public void DescribeFailureForSentry_Blocking_TagsAndFingerprintDescribeTheBudget()
        {
            var scope = Describe(
                inBackground: false,
                budgetSeconds: 3,
                new TimeoutException("timed out")
            );

            Assert.That(scope.Tags["urlLookupMode"], Is.EqualTo("blocking"));
            Assert.That(scope.Tags["urlLookupBudgetSeconds"], Is.EqualTo("3"));
            Assert.That(
                scope.Fingerprint,
                Is.EqualTo(
                    new[]
                    {
                        "UrlLookup.TryGetUrlDataFromServer",
                        "blocking",
                        "System.TimeoutException",
                    }
                )
            );
        }

        [Test]
        public void DescribeFailureForSentry_DifferentExceptionType_GetsItsOwnFingerprint()
        {
            // An unexpected failure (e.g. the JSON not parsing) is a real bug, and must not be
            // hidden among the timeouts.
            var timeout = Describe(inBackground: true, budgetSeconds: 40, new TimeoutException());
            var parseFailure = Describe(
                inBackground: true,
                budgetSeconds: 40,
                new FormatException()
            );

            Assert.That(timeout.Fingerprint, Is.Not.EqualTo(parseFailure.Fingerprint));
        }
    }
}
