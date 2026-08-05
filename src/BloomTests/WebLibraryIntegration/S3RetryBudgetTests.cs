using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using NUnit.Framework;

namespace BloomTests.WebLibraryIntegration
{
    /// <summary>
    /// Pins down how the AWS SDK behaves when a request times out, because UrlLookup's time budget
    /// arithmetic depends on it (BL-16575). UrlLookup gives the background retrieval a short
    /// per-attempt timeout plus retries, inside a longer overall cancellation token, on the
    /// assumption that an attempt-level timeout is a *retryable* error and so the later attempts
    /// actually run. If instead the SDK surfaced it as a plain cancellation, the whole operation
    /// would abort on the first timeout and the background path would be worth only one attempt -
    /// a fraction of the patience we think we are giving it.
    ///
    /// We test it rather than reason about it because it is third-party behavior that an SDK
    /// upgrade could quietly change, and because the symptom - "we still give up too early on a
    /// slow connection" - would otherwise be invisible until someone re-read the Sentry buckets
    /// months later.
    ///
    /// The stand-in for a slow server is a socket that accepts connections and never answers, so
    /// every attempt can only end in a timeout. Counting accepted connections counts attempts.
    /// </summary>
    [TestFixture]
    public class S3RetryBudgetTests
    {
        // A server that accepts TCP connections, holds them open, and never sends a response.
        // (A raw socket rather than HttpListener, which on Windows can need a URL ACL.)
        private sealed class StalledServer : IDisposable
        {
            private readonly TcpListener _listener;
            private readonly CancellationTokenSource _stop = new CancellationTokenSource();
            private int _connectionCount;

            public StalledServer()
            {
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                Task.Run(() => AcceptLoop());
            }

            public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

            public int ConnectionCount => Volatile.Read(ref _connectionCount);

            private async Task AcceptLoop()
            {
                var held = new System.Collections.Generic.List<TcpClient>();
                try
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        var client = await _listener.AcceptTcpClientAsync();
                        Interlocked.Increment(ref _connectionCount);
                        // Hold the connection open without replying, so the client can only time out.
                        held.Add(client);
                    }
                }
                catch (ObjectDisposedException) { } // expected when we stop the listener
                catch (InvalidOperationException) { } // ditto
                finally
                {
                    foreach (var c in held)
                        c.Close();
                }
            }

            public void Dispose()
            {
                _stop.Cancel();
                _listener.Stop();
            }
        }

        /// <summary>
        /// The behavior UrlLookup's background budget relies on: a per-attempt timeout is retried
        /// (up to MaxErrorRetry) rather than aborting the whole operation, so long as the caller's
        /// overall cancellation token still has time left.
        /// </summary>
        [Test]
        public void AttemptTimeout_IsRetried_WithinTheOverallCancellationToken()
        {
            using (var server = new StalledServer())
            {
                const int maxErrorRetry = 2; // so we expect 1 initial attempt + 2 retries = 3
                var perAttempt = TimeSpan.FromMilliseconds(500);
                // Generous enough that the retries are limited by maxErrorRetry, not by this.
                var overall = TimeSpan.FromSeconds(15);

                var config = new AmazonS3Config
                {
                    ServiceURL = $"http://127.0.0.1:{server.Port}",
                    ForcePathStyle = true,
                    UseHttp = true,
                    Timeout = perAttempt,
                    MaxErrorRetry = maxErrorRetry,
                };
                var s3 = new AmazonS3Client("fake-access-key", "fake-secret-key", config);

                Assert.That(
                    server.ConnectionCount,
                    Is.EqualTo(0),
                    "test setup problem: something connected before we made the request"
                );

                var stopwatch = Stopwatch.StartNew();
                using (var cts = new CancellationTokenSource(overall))
                {
                    Assert.That(
                        () =>
                            s3.GetObjectAsync(
                                    new GetObjectRequest { BucketName = "b", Key = "k" },
                                    cts.Token
                                )
                                .GetAwaiter()
                                .GetResult(),
                        Throws.Exception,
                        "a server that never answers must not somehow produce a result"
                    );
                    stopwatch.Stop();

                    Assert.That(
                        cts.IsCancellationRequested,
                        Is.False,
                        $"the overall token should still have had time left, but the attempts took {stopwatch.Elapsed}"
                    );
                }

                Assert.That(
                    server.ConnectionCount,
                    Is.EqualTo(maxErrorRetry + 1),
                    "an attempt-level timeout should be retried; if this is 1, the SDK aborted the "
                        + "whole operation on the first timeout and UrlLookup's background budget "
                        + "buys only one attempt, not MaxErrorRetry+1"
                );
            }
        }

        /// <summary>
        /// The other half of the contract: the caller's overall token really is the hard ceiling,
        /// so a stalled connection cannot keep retrying past it. This is what stops UrlLookup's
        /// blocking path from ever exceeding its short budget.
        /// </summary>
        [Test]
        public void OverallCancellationToken_CutsRetriesOff()
        {
            using (var server = new StalledServer())
            {
                var config = new AmazonS3Config
                {
                    ServiceURL = $"http://127.0.0.1:{server.Port}",
                    ForcePathStyle = true,
                    UseHttp = true,
                    Timeout = TimeSpan.FromMilliseconds(500),
                    // Far more retries than the overall budget can accommodate.
                    MaxErrorRetry = 50,
                };
                var s3 = new AmazonS3Client("fake-access-key", "fake-secret-key", config);

                var overall = TimeSpan.FromSeconds(3);
                var stopwatch = Stopwatch.StartNew();
                using (var cts = new CancellationTokenSource(overall))
                {
                    Assert.That(
                        () =>
                            s3.GetObjectAsync(
                                    new GetObjectRequest { BucketName = "b", Key = "k" },
                                    cts.Token
                                )
                                .GetAwaiter()
                                .GetResult(),
                        Throws.Exception
                    );
                }
                stopwatch.Stop();

                Assert.That(
                    stopwatch.Elapsed,
                    Is.LessThan(overall + TimeSpan.FromSeconds(5)),
                    "the overall cancellation token must bound the whole operation, retries included"
                );
                Assert.That(
                    server.ConnectionCount,
                    Is.LessThan(51),
                    "test setup problem: more attempts than MaxErrorRetry allows"
                );
            }
        }
    }
}
