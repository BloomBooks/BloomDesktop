using System;
using System.IO;
using System.Net;
using System.Text;
using Bloom.Api;
using Bloom.web;
using NUnit.Framework;
using SIL.IO;
using TemporaryFolder = SIL.TestUtilities.TemporaryFolder;

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

        // A little more than one 512KB piece, so WriteFileInPieces must reopen the file once.
        private const int kLengthOfPiecedFile = 512 * 1024 + 1000;

        [Test]
        public void WriteFileInPieces_FileStaysPut_WritesWholeFile()
        {
            using (var folder = new TemporaryFolder("WriteFileInPieces"))
            {
                var path = MakePiecedFile(folder.Path);
                var output = new StreamWithWriteCallback(null);

                var wroteWholeFile = RequestInfo.WriteFileInPieces(path, OpenShared(path), output);

                Assert.That(wroteWholeFile, Is.True);
                Assert.That(output.ToArray(), Is.EqualTo(File.ReadAllBytes(path)));
            }
        }

        // BL-16935: the runtime image cache deletes its files when the selected book changes,
        // possibly while we are part way through sending one of them.
        [Test]
        public void WriteFileInPieces_FileDeletedAfterFirstPiece_ReturnsFalse()
        {
            using (var folder = new TemporaryFolder("WriteFileInPieces"))
            {
                var path = MakePiecedFile(folder.Path);
                var output = new StreamWithWriteCallback(() => File.Delete(path));

                var wroteWholeFile = RequestInfo.WriteFileInPieces(path, OpenShared(path), output);

                Assert.That(File.Exists(path), Is.False, "the test should have deleted the file");
                Assert.That(wroteWholeFile, Is.False);
                Assert.That(output.Length, Is.EqualTo(512 * 1024));
            }
        }

        // The cache's whole folder may go, which makes the reopen throw DirectoryNotFoundException.
        [Test]
        public void WriteFileInPieces_FolderDeletedAfterFirstPiece_ReturnsFalse()
        {
            using (var folder = new TemporaryFolder("WriteFileInPieces"))
            {
                var subfolder = Path.Combine(folder.Path, "cache");
                Directory.CreateDirectory(subfolder);
                var path = MakePiecedFile(subfolder);
                var output = new StreamWithWriteCallback(() => Directory.Delete(subfolder, true));

                var wroteWholeFile = RequestInfo.WriteFileInPieces(path, OpenShared(path), output);

                Assert.That(
                    Directory.Exists(subfolder),
                    Is.False,
                    "the test should have deleted the folder"
                );
                Assert.That(wroteWholeFile, Is.False);
                Assert.That(output.Length, Is.EqualTo(512 * 1024));
            }
        }

        private static string MakePiecedFile(string folderPath)
        {
            var path = Path.Combine(folderPath, "big.jpg");
            var contents = new byte[kLengthOfPiecedFile];
            new Random(16935).NextBytes(contents);
            File.WriteAllBytes(path, contents);
            return path;
        }

        private static FileStream OpenShared(string path)
        {
            return new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
        }

        /// <summary>
        /// A MemoryStream that runs an action (once) after its first Write, standing in for
        /// something that happens while a piece of the file is being sent.
        /// </summary>
        private class StreamWithWriteCallback : MemoryStream
        {
            private Action _afterFirstWrite;

            public StreamWithWriteCallback(Action afterFirstWrite)
            {
                _afterFirstWrite = afterFirstWrite;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                base.Write(buffer, offset, count);
                var action = _afterFirstWrite;
                _afterFirstWrite = null;
                action?.Invoke();
            }
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
            public string Referer { get; set; }
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
