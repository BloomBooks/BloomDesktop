using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Bloom.Api;
using Bloom.web;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.web
{
    [TestFixture]
    class RequestInfoTests
    {
        [Test]
        public void RetrieveFileWithSpecialCharacters()
        {
            const string fileContents = @"\&<'@?>/" + "\r\n\"";
            using (var asciiFile = MakeTempFile(Encoding.ASCII.GetBytes(fileContents)))
            {
                using (var utf8File = MakeTempFile(Encoding.UTF8.GetBytes(fileContents)))
                {
                    var request = new PretendRequestInfo(
                        BloomServer.ServerUrlWithBloomPrefixEndingInSlash
                    );

                    request.WriteCompleteOutput(File.ReadAllText(asciiFile.Path));
                    var asciiString = request.ReplyContents;

                    Assert.AreEqual(asciiString.Length, 11);
                    Assert.AreEqual(asciiString[0], '\\');
                    Assert.AreEqual(asciiString[1], '&');
                    Assert.AreEqual(asciiString[2], '<');
                    Assert.AreEqual(asciiString[3], '\'');
                    Assert.AreEqual(asciiString[4], '@');
                    Assert.AreEqual(asciiString[5], '?');
                    Assert.AreEqual(asciiString[6], '>');
                    Assert.AreEqual(asciiString[7], '/');
                    Assert.AreEqual(asciiString[8], '\r');
                    Assert.AreEqual(asciiString[9], '\n');
                    Assert.AreEqual(asciiString[10], '"');

                    request.WriteCompleteOutput(File.ReadAllText(utf8File.Path));
                    var utf8String = request.ReplyContents;
                    Assert.AreEqual(utf8String.Length, 11);
                    Assert.AreEqual(utf8String[0], '\\');
                    Assert.AreEqual(utf8String[1], '&');
                    Assert.AreEqual(utf8String[2], '<');
                    Assert.AreEqual(utf8String[3], '\'');
                    Assert.AreEqual(utf8String[4], '@');
                    Assert.AreEqual(utf8String[5], '?');
                    Assert.AreEqual(utf8String[6], '>');
                    Assert.AreEqual(utf8String[7], '/');
                    Assert.AreEqual(utf8String[8], '\r');
                    Assert.AreEqual(utf8String[9], '\n');
                    Assert.AreEqual(utf8String[10], '"');
                }
            }
        }

        [TestCase("blah", "/blah")]
        [TestCase("bl%23ah", "/bl#ah")]
        [TestCase("bl?ah", "/bl")]
        [TestCase("bl%F4%80%80%8Aah", "/bl􀀊ah")] // private use character
        [TestCase("one + one", "/one + one")] // BL-3814. See http://stackoverflow.com/a/1006074/723299
        [TestCase("//networkUrl", "///networkUrl")] // BL-3808 Error using Bloom through network share
        // BL-16669: a file name may itself contain a '%' followed by two hex digits. Encoded for
        // the url that '%' becomes "%25", and we must decode exactly once to get back to the real
        // name; a second decode would send us looking for "photoA.jpg", which isn't there.
        [TestCase("photo%2541.jpg", "/photo%41.jpg")]
        public void LocalPathWithoutQuery_SpecialCharactersDecodedCorrectly(
            string urlEnd,
            string expectedResult
        )
        {
            var context = new TestHttpListenerContext();
            var request = new TestHttpListenerRequest();
            request.SetRawUrl("/" + urlEnd);
            context.SetRequest(request);
            var requestInfo = new RequestInfo(context);
            Assert.AreEqual(expectedResult, requestInfo.LocalPathWithoutQuery);
        }

        [Test]
        public void GetPostJson_PreservesPlusSigns()
        {
            // https://issues.bloomlibrary.org/youtrack/issue/BL-15384
            const string body = "{\"email\":\"joe+extra@example.com\"}";
            var context = new TestHttpListenerContext();
            var request = new TestHttpListenerRequest();
            request.SetRawUrl("/registration/userInfo");
            request.SetJsonBody(body);
            context.SetRequest(request);

            var requestInfo = new RequestInfo(context);

            Assert.AreEqual(body, requestInfo.GetPostJson());
        }

        public enum LargeFileChange
        {
            FileDeleted,
            FolderDeleted,
            FileTruncated,
            FileGrown,
            FileLocked,
        }

        /// <summary>
        /// BL-16931: files of 2MB or more are sent in pieces, reopening the file between pieces so
        /// it is never held locked, after promising the original length in Content-Length. Deleting,
        /// truncating, growing or locking the file part way through used to make an exception escape from
        /// ReplyWithFileContent (e.g. "Cannot close stream until all bytes are written"). This
        /// serves such a file through a real HttpListener, since HttpListenerResponse can't be faked.
        /// </summary>
        [TestCase(LargeFileChange.FileDeleted)]
        [TestCase(LargeFileChange.FolderDeleted)]
        [TestCase(LargeFileChange.FileTruncated)]
        [TestCase(LargeFileChange.FileGrown)]
        [TestCase(LargeFileChange.FileLocked)]
        public void ReplyWithFileContent_LargeFileChangesWhileSending_NoExceptionEscapes(
            LargeFileChange change
        )
        {
            // Big enough that the server cannot have handed it all to the socket before the client
            // has read its first bytes, so the change always lands between two pieces. Not a multiple
            // of the 512KB piece size: once exactly Content-Length bytes are written, HttpResponseStream
            // ignores further writes, so a grown file only causes trouble when a piece crosses that limit.
            const int fileLength = 32 * 1024 * 1024 + 1000;
            var contents = new byte[fileLength];
            for (var i = 0; i < fileLength; i++)
                contents[i] = (byte)(i % 251);

            using (var folder = new TemporaryFolder("RequestInfoTests_LargeFile"))
            {
                var path = Path.Combine(folder.Path, "large.jpg");
                File.WriteAllBytes(path, contents);
                Assert.That(
                    new FileInfo(path).Length,
                    Is.GreaterThanOrEqualTo(2 * 1024 * 1024),
                    "the file must be big enough to be sent in pieces"
                );

                var (listener, port) = StartListenerOnFreePort();
                try
                {
                    Exception serverException = null;
                    var serverTask = Task.Run(() =>
                    {
                        var context = listener.GetContext();
                        try
                        {
                            new RequestInfo(
                                new BloomHttpListenerContext(context)
                            ).ReplyWithFileContent(path);
                        }
                        catch (Exception e)
                        {
                            serverException = e;
                        }
                    });

                    // Task.Run keeps the client's awaits off NUnit's test synchronization context. If
                    // an assertion below fails while the client is still reading, a continuation
                    // posted to that context after the test ends crashes the whole test host.
                    var clientTask = Task.Run(() =>
                        ReadWhileChangingFileAsync(
                            $"http://localhost:{port}/large.jpg",
                            path,
                            folder.Path,
                            change,
                            serverTask
                        )
                    );

                    Assert.That(
                        serverTask.Wait(TimeSpan.FromSeconds(60)),
                        Is.True,
                        "the server should finish its reply"
                    );
                    Assert.That(
                        serverException,
                        Is.Null,
                        "ReplyWithFileContent should not throw: " + serverException
                    );

                    Assert.That(
                        clientTask.Wait(TimeSpan.FromSeconds(60)),
                        Is.True,
                        "the client should finish reading"
                    );
                    var (promisedLength, received, clientException, serverWasStillSending) =
                        clientTask.Result;
                    Assert.That(
                        serverWasStillSending,
                        Is.True,
                        "the file must change while the reply is still being sent, or this test proves nothing"
                    );
                    Assert.That(
                        promisedLength,
                        Is.EqualTo(fileLength),
                        "Content-Length should be the length when the reply started"
                    );
                    if (change == LargeFileChange.FileGrown)
                    {
                        Assert.That(
                            clientException,
                            Is.Null,
                            "a grown file should still give a complete reply"
                        );
                        Assert.That(received, Is.EqualTo(fileLength));
                    }
                    else
                    {
                        // The server dropped the connection, so the client gets fewer bytes than
                        // it was promised, and HttpClient reports that as an error.
                        Assert.That(received, Is.LessThan(fileLength));
                        Assert.That(
                            clientException,
                            Is.Not.Null,
                            "the client should see the reply fail, not end quietly short"
                        );
                    }
                }
                finally
                {
                    listener.Close();
                }
            }
        }

        /// <summary>
        /// Request the file, read its first bytes, make the requested change to the file on disk,
        /// then read the rest. Returns the Content-Length the server sent, the number of body bytes
        /// received, the exception (if any) that ended the read, and whether the server was still
        /// sending when the file was changed.
        /// </summary>
        private static async Task<(
            long? promisedLength,
            long received,
            Exception exception,
            bool serverWasStillSending
        )> ReadWhileChangingFileAsync(
            string url,
            string path,
            string folderPath,
            LargeFileChange change,
            Task serverTask
        )
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
            using (
                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead)
            )
            using (var body = await response.Content.ReadAsStreamAsync())
            {
                var promisedLength = response.Content.Headers.ContentLength;
                var buffer = new byte[64 * 1024];
                long received = await body.ReadAsync(buffer, 0, buffer.Length);
                Assert.That(received, Is.GreaterThan(0), "the reply should have started");

                var serverWasStillSending = !serverTask.IsCompleted;
                FileStream lockStream = null;
                switch (change)
                {
                    case LargeFileChange.FileDeleted:
                        File.Delete(path);
                        break;
                    case LargeFileChange.FolderDeleted:
                        Directory.Delete(folderPath, true);
                        break;
                    case LargeFileChange.FileTruncated:
                        using (
                            var fs = new FileStream(
                                path,
                                FileMode.Open,
                                FileAccess.Write,
                                FileShare.ReadWrite | FileShare.Delete
                            )
                        )
                            fs.SetLength(0);
                        break;
                    case LargeFileChange.FileGrown:
                        using (
                            var fs = new FileStream(
                                path,
                                FileMode.Append,
                                FileAccess.Write,
                                FileShare.ReadWrite | FileShare.Delete
                            )
                        )
                            fs.Write(new byte[1024 * 1024], 0, 1024 * 1024);
                        break;
                    case LargeFileChange.FileLocked:
                        // Held until the client has read all it will get, which is longer than the
                        // server keeps retrying to reopen the file.
                        lockStream = OpenExclusivelyWithRetry(path);
                        break;
                }

                try
                {
                    int read;
                    while ((read = await body.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        received += read;
                    return (promisedLength, received, null, serverWasStillSending);
                }
                catch (Exception e)
                {
                    return (promisedLength, received, e, serverWasStillSending);
                }
                finally
                {
                    lockStream?.Dispose();
                }
            }
        }

        /// <summary>
        /// Open the file so that no other process can open it. The server has it open for a moment
        /// between pieces, and opening it exclusively fails during that moment, so try again.
        /// </summary>
        private static FileStream OpenExclusivelyWithRetry(string path)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                }
                catch (IOException) when (attempt < 100)
                {
                    System.Threading.Thread.Sleep(10);
                }
            }
        }

        /// <summary>
        /// Start an HttpListener on a loopback port nothing else is using. Another process can take
        /// the port between our finding it and the listener starting, so try a few ports.
        /// </summary>
        private static (HttpListener listener, int port) StartListenerOnFreePort()
        {
            for (var attempt = 1; ; attempt++)
            {
                var port = GetFreeLoopbackPort();
                var listener = new HttpListener();
                listener.Prefixes.Add($"http://localhost:{port}/");
                try
                {
                    listener.Start();
                    return (listener, port);
                }
                catch (HttpListenerException) when (attempt < 5)
                {
                    listener.Close();
                }
            }
        }

        private static int GetFreeLoopbackPort()
        {
            var tcpListener = new TcpListener(IPAddress.Loopback, 0);
            tcpListener.Start();
            var port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
            tcpListener.Stop();
            return port;
        }

        private TempFile MakeTempFile(byte[] contents)
        {
            var file = TempFile.WithExtension(".tmp");
            File.Delete(file.Path);
            File.WriteAllBytes(file.Path, contents);
            return file;
        }

        private class TestHttpListenerContext : IHttpListenerContext
        {
            public IHttpListenerRequest Request { get; private set; }
            public HttpListenerResponse Response { get; private set; }

            public void SetRequest(IHttpListenerRequest request)
            {
                Request = request;
            }
        }

        private class TestHttpListenerRequest : IHttpListenerRequest
        {
            public Encoding ContentEncoding { get; private set; }
            public string ContentType { get; private set; }
            public bool HasEntityBody { get; private set; }
            public string HttpMethod { get; private set; }
            public Stream InputStream { get; private set; }
            public string RawUrl { get; private set; }
            public Uri Url { get; private set; }

            public void SetRawUrl(string rawUrl)
            {
                RawUrl = rawUrl;
            }

            public void SetJsonBody(string json)
            {
                ContentEncoding = Encoding.UTF8;
                ContentType = "application/json";
                HasEntityBody = true;
                HttpMethod = "POST";
                InputStream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            }
        }
    }
}
