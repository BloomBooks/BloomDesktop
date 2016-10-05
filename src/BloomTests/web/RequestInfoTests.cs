using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Bloom.Api;
using NUnit.Framework;
using SIL.IO;

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
					var request = new PretendRequestInfo(ServerBase.ServerUrlWithBloomPrefixEndingInSlash);

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
		[RequiresSTA]
		public void LocalPathWithoutQuery_SpecialCharactersDecodedCorrectly(string urlEnd, string expectedResult)
		{
			var listener = new HttpListener();
			// Nothing special about 33579. It is a simply a random number we hope is unused.
			// Note: if you get errors indicating the port is in use,
			// it is likely the listener.Stop() line below wasn't called somehow.
			var urlPrefix = "http://localhost:33579/";
			listener.Prefixes.Add(urlPrefix);

			var reqThread = new Thread(() =>
			{
				Thread.Sleep(100);
				WebRequest req = WebRequest.Create(urlPrefix + urlEnd);
				req.GetResponse();
			});
			reqThread.Start();

			try
			{
				listener.Start();
				var context = listener.GetContext(); // This waits for a request.
				var requestInfo = new RequestInfo(context);
				Assert.AreEqual(expectedResult, requestInfo.LocalPathWithoutQuery);
			}
			finally
			{
				listener.Stop();
				reqThread.Join();
			}
		}

		private TempFile MakeTempFile(byte[] contents)
		{
			var file = TempFile.WithExtension(".tmp");
			File.Delete(file.Path);
			File.WriteAllBytes(file.Path, contents);
			return file;
		}
	}
}
