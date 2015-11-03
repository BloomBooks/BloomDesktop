using Bloom.Workspace;
using NUnit.Framework;
using SIL.IO;
using SIL.Windows.Forms.ImageToolbox;

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
			using (var image = PalasoImage.FromFile(imagePath))
			{
				BloomClipboard.CopyImageToClipboard(image);
				using (var resultingImage = BloomClipboard.GetImageFromClipboard())
				{
					// There is no working PalasoImage.Equals(), so just try a few properties
					Assert.AreEqual(image.FileName, resultingImage.FileName);
					Assert.AreEqual(image.Image.Size, resultingImage.Image.Size);
					Assert.AreEqual(image.Image.Flags, resultingImage.Image.Flags);
				}
			}
		}

		[Test]
		[Platform(Exclude = "Linux", Reason = "Linux code not yet available.")]
		public void ClipboardRoundTripWorks_Bmp()
		{
			var imagePath = GetPathToImage("PasteHS.bmp");
			using (var image = PalasoImage.FromFile(imagePath))
			{
				BloomClipboard.CopyImageToClipboard(image);
				using (var resultingImage = BloomClipboard.GetImageFromClipboard())
				{
					// There is no working PalasoImage.Equals(), so just try a few properties
					Assert.AreEqual(image.FileName, resultingImage.FileName);
					Assert.AreEqual(image.Image.Size, resultingImage.Image.Size);
					Assert.AreEqual(image.Image.Flags, resultingImage.Image.Flags);
				}
			}
		}

		[Test]
		[Platform(Exclude = "Linux", Reason = "Linux code not yet available.")]
		public void ClipboardRoundTripWorks_GetsExistingMetadata()
		{
			var imagePath = GetPathToImage("AOR_EAG00864.png");
			using (var image = PalasoImage.FromFile(imagePath))
			{
				var preCopyLicense = image.Metadata.License.Token;
				var preCopyCollectionUri = image.Metadata.CollectionUri;
				BloomClipboard.CopyImageToClipboard(image);
				using (var resultingImage = BloomClipboard.GetImageFromClipboard())
				{
					// Test that the same metadata came through
					Assert.IsTrue(resultingImage.Metadata.IsMinimallyComplete);
					Assert.AreEqual(preCopyLicense, resultingImage.Metadata.License.Token);
					Assert.AreEqual(preCopyCollectionUri, resultingImage.Metadata.CollectionUri);
					Assert.AreEqual(image.Image.Flags, resultingImage.Image.Flags);
				}
			}
		}
	}
}
