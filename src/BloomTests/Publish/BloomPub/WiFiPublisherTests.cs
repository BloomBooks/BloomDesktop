using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Publish.BloomPub.wifi;
using Bloom.web;
using NUnit.Framework;

namespace BloomTests.Publish.BloomPub
{
    /// <summary>
    /// Tests the HttpClient-based upload that WiFiPublisher uses to send a book to an Android device.
    /// The "device" is just an HTTP endpoint, so we exercise the real upload + completion/cancellation
    /// logic against a fake HttpMessageHandler, with no real network and no device required.
    /// </summary>
    [TestFixture]
    public class WiFiPublisherTests
    {
        // Captures the outgoing request and returns (or throws) whatever the test configures.
        private sealed class FakeHttpMessageHandler : HttpMessageHandler
        {
            public HttpRequestMessage CapturedRequest;
            public byte[] CapturedBody;
            public string CapturedContentType;
            public HttpStatusCode StatusToReturn = HttpStatusCode.OK;
            public Exception ExceptionToThrow;

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                CapturedRequest = request;
                if (request.Content != null)
                {
                    CapturedBody = await request.Content.ReadAsByteArrayAsync();
                    // Captured here because the content is disposed when the send completes.
                    CapturedContentType = request.Content.Headers.ContentType?.ToString();
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (ExceptionToThrow != null)
                    throw ExceptionToThrow;
                return new HttpResponseMessage(StatusToReturn);
            }
        }

        // A WiFiPublisher that records reported exceptions instead of routing them to the (UI) progress
        // reporter, so tests can assert on the error-handling behavior. Setting ThrowFromReport
        // simulates the reporting channel itself failing (e.g. progress websocket torn down).
        private sealed class TestableWiFiPublisher : WiFiPublisher
        {
            public Exception ReportedException;
            public Exception ThrowFromReport;

            public TestableWiFiPublisher()
                : base(new NullWebSocketProgress(), null) { }

            protected override void ReportException(Exception e)
            {
                ReportedException = e;
                if (ThrowFromReport != null)
                    throw ThrowFromReport;
            }
        }

        private static TestableWiFiPublisher MakePublisher(FakeHttpMessageHandler handler)
        {
            var publisher = new TestableWiFiPublisher();
            publisher.SetHttpClientForTests(new HttpClient(handler));
            // A real send sets this to indicate a transfer is in progress; we set it directly so we can
            // verify that completion clears it.
            publisher.WifiSendCancellationForTests = new CancellationTokenSource();
            return publisher;
        }

        [Test]
        public void UploadToDevice_PostsBookBytesToPutfileEndpoint()
        {
            var handler = new FakeHttpMessageHandler();
            var publisher = MakePublisher(handler);
            var bytes = Encoding.UTF8.GetBytes("pretend bloompub contents");
            Assert.That(bytes.Length, Is.GreaterThan(0), "sanity: test data should be non-empty");

            publisher
                .UploadToDevice(
                    "192.168.1.50",
                    "my book.bloompub",
                    bytes,
                    publisher.WifiSendCancellationForTests
                )
                .Wait();

            Assert.That(
                handler.CapturedRequest,
                Is.Not.Null,
                "the device never received a request"
            );
            Assert.That(handler.CapturedRequest.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(
                handler.CapturedRequest.RequestUri.AbsoluteUri,
                Is.EqualTo("http://192.168.1.50:5914/putfile?path=my%20book.bloompub"),
                "the filename should be URL-escaped in the path query of the putfile endpoint"
            );
            Assert.That(
                handler.CapturedBody,
                Is.EqualTo(bytes),
                "the exact book bytes should be sent as the request body"
            );
            Assert.That(
                handler.CapturedContentType,
                Is.EqualTo("application/octet-stream"),
                "the book upload should declare the same Content-Type the old WebClient sent"
            );
        }

        [Test]
        public void UploadToDevice_OnSuccess_ClearsInProgressStateAndReportsNoError()
        {
            var handler = new FakeHttpMessageHandler { StatusToReturn = HttpStatusCode.OK };
            var publisher = MakePublisher(handler);
            Assert.That(
                publisher.WifiSendCancellationForTests,
                Is.Not.Null,
                "sanity: a send should be marked in progress before completion"
            );

            publisher
                .UploadToDevice(
                    "10.0.0.5",
                    "book.bloompub",
                    new byte[] { 1, 2, 3 },
                    publisher.WifiSendCancellationForTests
                )
                .Wait();

            Assert.That(
                publisher.WifiSendCancellationForTests,
                Is.Null,
                "the in-progress state should be cleared once the send completes, allowing the next send"
            );
            Assert.That(
                publisher.ReportedException,
                Is.Null,
                "no error should be reported on success"
            );
        }

        [Test]
        public void UploadToDevice_OnErrorResponse_ReportsException()
        {
            var handler = new FakeHttpMessageHandler
            {
                StatusToReturn = HttpStatusCode.InternalServerError,
            };
            var publisher = MakePublisher(handler);

            publisher
                .UploadToDevice(
                    "10.0.0.5",
                    "book.bloompub",
                    new byte[] { 1 },
                    publisher.WifiSendCancellationForTests
                )
                .Wait();

            Assert.That(
                publisher.ReportedException,
                Is.TypeOf<HttpRequestException>(),
                "a non-success response from the device should be reported as an error"
            );
            Assert.That(
                publisher.WifiSendCancellationForTests,
                Is.Null,
                "the in-progress state should be cleared even when the send fails"
            );
        }

        [Test]
        public void UploadToDevice_WhenSendThrows_ReportsTheException()
        {
            var thrown = new HttpRequestException("simulated network failure");
            var handler = new FakeHttpMessageHandler { ExceptionToThrow = thrown };
            var publisher = MakePublisher(handler);

            publisher
                .UploadToDevice(
                    "10.0.0.5",
                    "book.bloompub",
                    new byte[] { 1 },
                    publisher.WifiSendCancellationForTests
                )
                .Wait();

            Assert.That(
                publisher.ReportedException,
                Is.SameAs(thrown),
                "an exception thrown during the send should be reported"
            );
            Assert.That(publisher.WifiSendCancellationForTests, Is.Null);
        }

        [Test]
        public void UploadToDevice_WhenCanceled_DoesNotReportAndClearsState()
        {
            var handler = new FakeHttpMessageHandler();
            var publisher = MakePublisher(handler);
            // Simulate Stop() canceling an in-progress send (e.g. during shutdown).
            publisher.WifiSendCancellationForTests.Cancel();

            publisher
                .UploadToDevice(
                    "10.0.0.5",
                    "book.bloompub",
                    new byte[] { 1 },
                    publisher.WifiSendCancellationForTests
                )
                .Wait();

            Assert.That(
                publisher.ReportedException,
                Is.Null,
                "cancellation (typically during shutdown) should not be reported as an error"
            );
            Assert.That(publisher.WifiSendCancellationForTests, Is.Null);
        }

        [Test]
        public void UploadToDevice_StaleCompletion_DoesNotClearNewerSend()
        {
            // Models the race where Stop() abandons send A and a newer send B becomes the current one
            // BEFORE A's (slow) completion continuation finally runs. A's completion must not clear or
            // resume B's transfer.
            var handler = new FakeHttpMessageHandler();
            var publisher = MakePublisher(handler);

            var sendA = publisher.WifiSendCancellationForTests;
            var sendB = new CancellationTokenSource();
            // By the time A completes, B is the send the publisher is tracking.
            publisher.WifiSendCancellationForTests = sendB;

            // Run A's upload to completion (still using A's own token source).
            publisher.UploadToDevice("10.0.0.5", "a.bloompub", new byte[] { 1 }, sendA).Wait();

            Assert.That(
                publisher.WifiSendCancellationForTests,
                Is.SameAs(sendB),
                "a stale send's completion must not clear the newer send's in-progress state"
            );
            Assert.That(
                publisher.ReportedException,
                Is.Null,
                "the stale completion succeeded, so nothing should be reported"
            );

            sendB.Dispose();
        }

        [Test]
        public void HandleSendSetupFailure_UploadNotStarted_ReportsAndClearsState()
        {
            // A failure before the upload started (e.g. packaging the book threw): no completion
            // continuation will ever run, so the in-progress state must be cleared here.
            var publisher = MakePublisher(new FakeHttpMessageHandler());
            var cancellation = publisher.WifiSendCancellationForTests;
            var error = new InvalidOperationException("packaging failed");

            publisher.HandleSendSetupFailure(error, cancellation, uploadStarted: false);

            Assert.That(publisher.ReportedException, Is.SameAs(error));
            Assert.That(
                publisher.WifiSendCancellationForTests,
                Is.Null,
                "a setup failure with no upload started must clear the in-progress state so future sends work"
            );
        }

        [Test]
        public void HandleSendSetupFailure_UploadAlreadyStarted_DoesNotClearState()
        {
            // A failure AFTER the upload started (e.g. updating the advertiser threw): the upload's
            // completion continuation owns the in-progress state and its CancellationTokenSource, so
            // this path must NOT clear/dispose it (doing so would clobber the in-flight transfer and
            // could let a second send start).
            var publisher = MakePublisher(new FakeHttpMessageHandler());
            var cancellation = publisher.WifiSendCancellationForTests;
            var error = new InvalidOperationException(
                "advertiser update failed after upload started"
            );

            publisher.HandleSendSetupFailure(error, cancellation, uploadStarted: true);

            Assert.That(
                publisher.ReportedException,
                Is.SameAs(error),
                "the error should still be reported"
            );
            Assert.That(
                publisher.WifiSendCancellationForTests,
                Is.SameAs(cancellation),
                "an in-flight upload's in-progress state must be left for its completion to clear"
            );

            cancellation.Dispose();
        }

        [Test]
        public void HandleSendSetupFailure_StaleSend_DoesNotClearNewerSendOrResumeAdvertising()
        {
            // Models the race where Stop() abandoned send A while its setup was still running, and a
            // newer send B is now in progress (advertiser paused). When A's setup finally throws, it
            // must not clear B's in-progress state NOR resume advertising mid-B-transfer.
            var publisher = MakePublisher(new FakeHttpMessageHandler());
            var advertiser = new WiFiAdvertiser(new NullWebSocketProgress()) { Paused = true };
            publisher.WifiAdvertiserForTests = advertiser;
            var staleA = publisher.WifiSendCancellationForTests;
            var sendB = new CancellationTokenSource();
            publisher.WifiSendCancellationForTests = sendB;

            publisher.HandleSendSetupFailure(
                new InvalidOperationException("stale setup failed"),
                staleA,
                uploadStarted: false
            );

            Assert.That(
                publisher.WifiSendCancellationForTests,
                Is.SameAs(sendB),
                "a stale setup failure must not clear the newer send's in-progress state"
            );
            Assert.That(
                advertiser.Paused,
                Is.True,
                "a stale setup failure must not resume advertising while the newer send is transferring"
            );

            sendB.Dispose();
        }

        [Test]
        public void HandleSendSetupFailure_CurrentSend_ResumesAdvertising()
        {
            // The normal setup-failure case: the failing send is still the current one, so the state
            // is cleared and advertising resumes so devices can request again.
            var publisher = MakePublisher(new FakeHttpMessageHandler());
            var advertiser = new WiFiAdvertiser(new NullWebSocketProgress()) { Paused = true };
            publisher.WifiAdvertiserForTests = advertiser;
            var cancellation = publisher.WifiSendCancellationForTests;

            publisher.HandleSendSetupFailure(
                new InvalidOperationException("setup failed"),
                cancellation,
                uploadStarted: false
            );

            Assert.That(publisher.WifiSendCancellationForTests, Is.Null);
            Assert.That(
                advertiser.Paused,
                Is.False,
                "after a current send's setup failure, advertising must resume"
            );
        }

        [Test]
        public void UploadToDevice_WhenReportingThrows_StillClearsInProgressState()
        {
            // If reporting the outcome itself throws (e.g. the progress channel is being torn down),
            // the completion must still clear the in-progress state and resume advertising; otherwise
            // WiFi publishing is silently wedged for the rest of the session.
            var handler = new FakeHttpMessageHandler
            {
                StatusToReturn = HttpStatusCode.InternalServerError,
            };
            var publisher = MakePublisher(handler);
            publisher.ThrowFromReport = new InvalidOperationException("progress channel is gone");
            var advertiser = new WiFiAdvertiser(new NullWebSocketProgress()) { Paused = true };
            publisher.WifiAdvertiserForTests = advertiser;

            var task = publisher.UploadToDevice(
                "10.0.0.5",
                "book.bloompub",
                new byte[] { 1 },
                publisher.WifiSendCancellationForTests
            );
            // The reporting exception propagates out of the continuation; we only care that
            // cleanup ran anyway.
            var aggregate = Assert.Throws<AggregateException>(() => task.Wait());
            Assert.That(
                aggregate.GetBaseException(),
                Is.SameAs(publisher.ThrowFromReport),
                "sanity: the failure should be the simulated reporting exception"
            );

            Assert.That(
                publisher.ReportedException,
                Is.Not.Null,
                "sanity: the device error should have reached ReportException"
            );
            Assert.That(
                publisher.WifiSendCancellationForTests,
                Is.Null,
                "the in-progress state must be cleared even when reporting throws"
            );
            Assert.That(
                advertiser.Paused,
                Is.False,
                "advertising must resume even when reporting throws"
            );
        }
    }
}
