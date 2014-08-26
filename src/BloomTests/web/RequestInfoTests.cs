using System.IO;
using System.Text;
using Bloom.web;
using NUnit.Framework;
using Palaso.IO;

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
					var request = new PretendRequestInfo(ServerBase.PathEndingInSlash);

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

		private TempFile MakeTempFile(byte[] contents)
		{
			var file = TempFile.WithExtension(".tmp");
			File.Delete(file.Path);
			File.WriteAllBytes(file.Path, contents);
			return file;
		}
	}
}
