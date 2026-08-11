// Copyright (c) 2026 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using Bloom.Api;
using Bloom.Book;
using NUnit.Framework;
using TemporaryFolder = SIL.TestUtilities.TemporaryFolder;

namespace BloomTests.web
{
    /// <summary>
    /// Fetches a file from a real, listening BloomServer over real HTTP, and then shuts that server
    /// down. The rest of the server tests use PretendRequestInfo, which never touches an
    /// HttpListener, so nothing else covers what happens between "the worker thread has finished
    /// with this request" and "the bytes have reached the client" — which is exactly where BL-16667
    /// lived: the response body was still going out when Dispose closed the listener, the framework
    /// callback finishing the send blew up, and being on a thread nobody owned it took the whole
    /// process with it.
    /// </summary>
    [TestFixture]
    public class ServeFileAndShutDownTests
    {
        private TemporaryFolder _folder;

        [SetUp]
        public void Setup()
        {
            // These tests listen on the one fixed port, so they must not overlap with the other
            // fixtures that do. (Same reasoning as EndpointHandlerTests, whose lock this is.)
            Monitor.Enter(EndpointHandlerTests._portMonitor);
            _folder = new TemporaryFolder("ServeFileAndShutDownTests");
        }

        [TearDown]
        public void TearDown()
        {
            _folder.Dispose();
            Monitor.Exit(EndpointHandlerTests._portMonitor);
        }

        /// <summary>
        /// A file small enough to go down the "send it all in one go" path, which is the one that
        /// finishes after the worker thread has moved on.
        /// </summary>
        private string MakeFileToServe(string name, int size)
        {
            var path = Path.Combine(_folder.Path, name);
            var content = new byte[size];
            for (var i = 0; i < size; i++)
                content[i] = (byte)(i % 251); // a pattern, so truncation or padding shows up
            File.WriteAllBytes(path, content);
            return path;
        }

        /// <summary>
        /// The server serves a file named by its full path after the /bloom/ prefix.
        /// </summary>
        private static string UrlFor(string filePath)
        {
            return BloomServer.ServerUrlWithBloomPrefixEndingInSlash + filePath.Replace('\\', '/');
        }

        [Test]
        public void FileRequest_OverRealHttp_ReturnsTheWholeFile()
        {
            var path = MakeFileToServe("served.txt", 40000);
            var expected = File.ReadAllBytes(path);
            Assert.That(
                expected.Length,
                Is.EqualTo(40000),
                "sanity check on the file we are about to ask for"
            );

            byte[] received;
            using (var server = new BloomServer(new BookSelection()))
            {
                server.EnsureListening();
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                {
                    received = client.GetByteArrayAsync(UrlFor(path)).Result;
                }
            }

            // Not just the length: the body is finished off by our own code now (see
            // PendingResponseWrites), so it is worth checking that the client got every byte and
            // the right ones.
            Assert.That(received, Is.EqualTo(expected));
        }

        [Test]
        public void MissingFileRequest_OverRealHttp_Returns404WithTheMessageAsTheBody()
        {
            // The error path sends its body the same way the success paths do, and it is the one
            // most likely to be in flight when Bloom shuts down, because the browser goes on asking
            // for things while the server is going away. Worth its own test because it is also the
            // one path where nothing else had set ContentLength64 first.
            var missing = Path.Combine(_folder.Path, "notThere.txt");
            Assert.That(
                File.Exists(missing),
                Is.False,
                "sanity check: this test is about a file that is not there"
            );

            using (var server = new BloomServer(new BookSelection()))
            {
                server.EnsureListening();
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                using (var response = client.GetAsync(UrlFor(missing)).Result)
                {
                    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
                    // Reading the body to the end is the point: if the declared length and the bytes
                    // sent disagreed, this would hang or come up short rather than fail loudly.
                    var body = response.Content.ReadAsStringAsync().Result;
                    Assert.That(
                        body,
                        Is.Not.Empty,
                        "the 404 should carry its message as the body, which is what client code reads"
                    );
                    Assert.That(
                        response.Content.Headers.ContentLength,
                        Is.EqualTo(Encoding.UTF8.GetByteCount(body)),
                        "the length the server declared and the body it actually sent must agree"
                    );
                }
            }
        }

        [Test]
        public void DisposingWhileAResponseIsStillGoingOut_DoesNotKillTheProcess()
        {
            // This reproduces BL-16667, so it needs the send to be genuinely unfinished when Dispose
            // runs -- which an ordinary GET cannot arrange, because by the time the client has the
            // bytes the send is over. So: ask for a file far bigger than the socket will buffer, take
            // the headers, and then never read the body. The server's worker thread is finished and
            // has handed the rest of the send on, but the data is stuck half way out. Disposing now
            // is what used to fail: closing the listener made the stalled send blow up on a framework
            // callback that catches only Win32Exception, on a thread none of our try/catch blocks
            // cover, so it reached the runtime and killed the process. In a test run that looked like
            // the run stopping part way through and calling itself passed.
            var path = MakeFileToServe("stalled.txt", 1900000); // still under the 2MB one-go limit

            // Kept alive past the server's Dispose: letting the client go would close the connection
            // and unstick the very send we need stuck.
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            HttpResponseMessage response;
            try
            {
                using (var server = new BloomServer(new BookSelection()))
                {
                    server.EnsureListening();
                    response = client
                        .GetAsync(UrlFor(path), HttpCompletionOption.ResponseHeadersRead)
                        .Result;
                    Assert.That(
                        (int)response.StatusCode,
                        Is.EqualTo(200),
                        "sanity check: the server should have accepted the request and started replying"
                    );
                    // Deliberately no read of response.Content here.
                }
                // Reaching this line at all is most of the point of the test: with the old code the
                // process was gone before it.
                Assert.That(
                    PendingResponseWrites.WaitUntilAllHaveFinished(TimeSpan.FromSeconds(10)),
                    Is.True,
                    "the abandoned send should have been accounted for, not left counted forever"
                );
            }
            finally
            {
                client.Dispose();
            }
        }

        [Test]
        public void DisposingImmediatelyAfterServingFiles_LeavesNothingInFlight()
        {
            var paths = Enumerable
                .Range(0, 5)
                .Select(i => MakeFileToServe($"served{i}.txt", 200000))
                .ToArray();

            using (var server = new BloomServer(new BookSelection()))
            {
                server.EnsureListening();
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                {
                    foreach (var path in paths)
                    {
                        var received = client.GetByteArrayAsync(UrlFor(path)).Result;
                        Assert.That(
                            received.Length,
                            Is.EqualTo(200000),
                            "the server did not return the whole file"
                        );
                    }
                }
                // Dispose happens here, with the last response only just answered.
            }

            Assert.That(
                PendingResponseWrites.WaitUntilAllHaveFinished(TimeSpan.FromSeconds(10)),
                Is.True,
                "every send should have been waited for by the Dispose that followed it"
            );
        }
    }
}
