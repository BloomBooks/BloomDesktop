using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Bloom;
using Bloom.ImageProcessing;
using Bloom.Api;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.web
{
	/// <summary>
	/// Test BloomServer with image serving tests
	/// </summary>
	[TestFixture]
	public class ImageServerTests
	{
		private TemporaryFolder _folder;

		[SetUp]
		public void Setup()
		{
			_folder = new TemporaryFolder("ImageServerTests");
		}

		[TearDown]
		public void TearDown()
		{
			_folder.Dispose();
		}

		[Test]
		public void GetMissingImage_ReturnsError()
		{
			using (var server = CreateBloomServer())
			using (var file = MakeTempImage())
			{
				var transaction = new PretendRequestInfo(BloomServer.ServerUrlWithBloomPrefixEndingInSlash + "abc.png");
				server.MakeReply(transaction);
				Assert.AreEqual(404, transaction.StatusCode);
			}
		}

		[Test]
		public void GetSmallImage_ReturnsSameSizeImage()
		{
			using (var server = CreateBloomServer())
			using (var file = MakeTempImage())
			{
				var transaction = new PretendRequestInfo(BloomServer.ServerUrlWithBloomPrefixEndingInSlash + file.Path);
				server.MakeReply(transaction);
				Assert.IsTrue(transaction.ReplyImagePath.Contains(".png"));
			}
		}

		/// <summary>
		/// Regression for BL-2720
		/// </summary>
		[Test]
		public void GetImageWithEscapedSpaces_ReturnsImage()
		{
			using (var server = CreateBloomServer())
			using (var file = MakeTempImage("my cat.png"))
			{
				var transaction = new PretendRequestInfo(BloomServer.ServerUrlWithBloomPrefixEndingInSlash + file.Path.Replace(" ","%20"));
				server.MakeReply(transaction);
				Assert.IsTrue(transaction.ReplyImagePath.Contains(".png"));
			}
		}

		[Test]
		public void GetFileName_FileNotExist_ReturnsCorrectName()
		{
			var test = "c:/asdfg/test1.css";
			var fileName = Path.GetFileName(test);
			Assert.AreEqual("test1.css", fileName);

			test = "/one/two/test2.css";
			fileName = Path.GetFileName(test);
			Assert.AreEqual("test2.css", fileName);

			test = "test3.css";
			fileName = Path.GetFileName(test);
			Assert.AreEqual("test3.css", fileName);

			test = "test4";
			fileName = Path.GetFileName(test);
			Assert.AreEqual("test4", fileName);
		}

		private BloomServer CreateBloomServer()
		{
			return new BloomServer(new RuntimeImageProcessor(new BookRenamedEvent()), null, null);
		}
		private TempFile MakeTempImage(string fileName=null)
		{
			TempFile file;
			if (fileName == null)
			{
				file = TempFile.WithExtension(".png");
			}
			else
			{
				file = TempFile.WithFilename(fileName);
			}
			File.Delete(file.Path);
			using(var x = new Bitmap(100,100))
			{
				x.Save(file.Path, ImageFormat.Png);
			}
			return file;
		}

	}

}
