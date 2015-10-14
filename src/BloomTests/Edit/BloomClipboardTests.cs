using System.IO;
using Bloom.Workspace;
using NUnit.Framework;
using Palaso.IO;
using Palaso.UI.WindowsForms.ImageToolbox;

namespace BloomTests.Edit
{
	[TestFixture]
	[RequiresSTA] // or you get a ThreadStateException
	class BloomClipboardTests
	{
		private const string TestImageDir = "src/BloomTests/Edit/BloomClipboardTestImages";

		[SetUp]
		public void Setup()
		{
		}

		private static string GetPathToImage(string requestedImage)
		{
			return FileLocator.GetFileDistributedWithApplication(TestImageDir, requestedImage);
		}

		[Test]
		[Platform(Exclude = "Linux", Reason = "Linux code not yet available.")]
		public void ClipboardRoundTripWorks_Png()
		{
			var imagePath = GetPathToImage("LineSpacing.png");
			var image = PalasoImage.FromFile(imagePath);
			BloomClipboard.CopyImageToClipboard(image);
			var resultingImage = BloomClipboard.GetImageFromClipboard();
			// There is no working PalasoImage.Equals(), so just try a few properties
			Assert.AreEqual(image.FileName, resultingImage.FileName);
			Assert.AreEqual(image.Image.Size, resultingImage.Image.Size);
			Assert.AreEqual(image.Image.Flags, resultingImage.Image.Flags);
		}

		[Test]
		[Platform(Exclude = "Linux", Reason = "Linux code not yet available.")]
		public void ClipboardRoundTripWorks_Bmp()
		{
			var imagePath = GetPathToImage("PasteHS.bmp");
			var image = PalasoImage.FromFile(imagePath);
			BloomClipboard.CopyImageToClipboard(image);
			var resultingImage = BloomClipboard.GetImageFromClipboard();
			// There is no working PalasoImage.Equals(), so just try a few properties
			Assert.AreEqual(image.FileName, resultingImage.FileName);
			Assert.AreEqual(image.Image.Size, resultingImage.Image.Size);
			Assert.AreEqual(image.Image.Flags, resultingImage.Image.Flags);
		}
	}
}
