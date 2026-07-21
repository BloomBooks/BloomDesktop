// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Utils;

namespace Bloom.Api
{
    /// <summary>
    /// A small synchronous HTTP helper for talking to our own local BloomServer (and used by tests).
    /// The default HttpClient timeout (100 seconds) is far too long for the local calls we make here,
    /// so this lets the caller specify a short timeout.
    /// This replaces the formerly used System.Net.WebClient subclass (obsolete: SYSLIB0014).
    /// </summary>
    public class WebClientWithTimeout
    {
        // A single shared HttpClient is the recommended pattern; reusing it avoids socket exhaustion
        // and lets all instances (e.g. one per request in test helpers) share one connection pool.
        // Its own timeout is disabled; the per-instance Timeout is applied per request via a
        // CancellationTokenSource, which also keeps Timeout settable at any time.
        private static readonly HttpClient s_client = new HttpClient
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };

        /// <summary>
        /// Time in milliseconds. On timeout, requests throw TaskCanceledException.
        /// </summary>
        public int Timeout { get; set; }

        /// <summary>
        /// The Content-Type header sent with the body of upload (e.g. POST) requests, such as
        /// "text/plain" or "application/json". Not used by DownloadString: a GET has no body,
        /// so no Content-Type header is sent.
        /// </summary>
        public string ContentType { get; set; }

        public WebClientWithTimeout()
            : this(60000) { }

        public WebClientWithTimeout(int timeout)
        {
            Timeout = timeout;
        }

        /// <summary>
        /// Synchronously GET the given url and return the response body as a string.
        /// (RunSync executes on the thread pool so we don't deadlock if called on a thread
        /// with a synchronization context, e.g. the WinForms UI thread.)
        /// </summary>
        public string DownloadString(string url)
        {
            return AsyncUtil.RunSync(async () =>
            {
                // The timeout starts inside the delegate so thread-pool queueing delay doesn't
                // count against it.
                using (var cts = new CancellationTokenSource(Timeout))
                    return await s_client.GetStringAsync(url, cts.Token);
            });
        }

        /// <summary>
        /// Synchronously send the given data to the url using the given HTTP method (e.g. "POST")
        /// and return the response body as a string.
        /// </summary>
        public string UploadString(string url, string method, string data)
        {
            return AsyncUtil.RunSync(async () =>
            {
                using (var cts = new CancellationTokenSource(Timeout))
                using (
                    var request = new HttpRequestMessage(new HttpMethod(method), url)
                    {
                        Content = new StringContent(
                            data,
                            Encoding.UTF8,
                            ContentType ?? "text/plain"
                        ),
                    }
                )
                using (var response = await s_client.SendAsync(request, cts.Token))
                {
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync();
                }
            });
        }
    }
}
