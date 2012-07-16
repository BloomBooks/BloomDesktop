using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Bloom.ImageProcessing;
using Bloom.web;
using NUnit.Framework;
using Palaso.IO;
using Palaso.TestUtilities;

namespace BloomTests.LowResImageProcessing
{
	[TestFixture]
	public class ImageServerTests
	{
		private TemporaryFolder _folder;

		[SetUp]
		public void Setup()
		{
			_folder = new TemporaryFolder("ImageServerTests");
		}

		[Test]
		public void GetMissingImage_ReturnsError()
		{
			using (var server = CreateImageServer())
			using (var file = MakeTempImage())
			{
				var transaction = new PretendRequestInfo("http://localhost:8089/bloom/abc.png");
				server.MakeReply(transaction);
				Assert.AreEqual(404, transaction.StatusCode);
			}
		}

		[Test]
		public void GetSmallImage_ReturnsSameSizeImage()
		{
			using (var server = CreateImageServer())
			using (var file = MakeTempImage())
			{
				var transaction = new PretendRequestInfo("http://localhost:8089/bloom/"+file.Path);
				server.MakeReply(transaction);
				Assert.IsTrue(transaction.ReplyImagePath.Contains(".png"));
			}
		}

		private ImageServer CreateImageServer()
		{
			return new ImageServer();
		}
		private TempFile MakeTempImage()
		{
			var file = TempFile.WithExtension(".png");
			File.Delete(file.Path);
			using(var x = new Bitmap(100,100))
			{
				x.Save(file.Path, ImageFormat.Png);
			}
			return file;
		}
	}

}
