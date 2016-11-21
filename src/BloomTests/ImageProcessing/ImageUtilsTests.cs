using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom;
using Bloom.ImageProcessing;
using NUnit.Framework;
using SIL.TestUtilities;
using SIL.Windows.Forms.ImageToolbox;

namespace BloomTests.ImageProcessing
{
	[TestFixture]
	public class ImageUtilsTests
	{
		private const string _pathToTestImages = "src/BloomTests/ImageProcessing/images";

		[Test]
		public void ShouldChangeFormatToJpeg_Photo_True()
		{
			var path = SIL.IO.FileLocator.GetFileDistributedWithApplication(_pathToTestImages, "man.jpg");
			Assert.IsTrue(ImageUtils.ShouldChangeFormatToJpeg(ImageUtils.GetImageFromFile(path)));
		}

		[Test]
		public void ShouldChangeFormatToJpeg_OneColor_False()
		{
			var path = SIL.IO.FileLocator.GetFileDistributedWithApplication(_pathToTestImages, "bird.png");
			Assert.IsFalse(ImageUtils.ShouldChangeFormatToJpeg(ImageUtils.GetImageFromFile(path)));
		}

		[Test]
		public void ProcessAndSaveImageIntoFolder_PhotoButPNGFile_SavesAsJpeg()
		{
			ProcessAndSaveImageIntoFolder_AndTestResults("man.png", ImageFormat.Jpeg);
		}

		[Test]
		public void ProcessAndSaveImageIntoFolder_Photo_KeepsJpeg()
		{
			ProcessAndSaveImageIntoFolder_AndTestResults("man.jpg", ImageFormat.Jpeg);
		}
		
		[Test]
		public void ProcessAndSaveImageIntoFolder_OneColor_SavesAsPng()
		{
			ProcessAndSaveImageIntoFolder_AndTestResults("bird.png", ImageFormat.Png);
		}

		private static void ProcessAndSaveImageIntoFolder_AndTestResults(string testImageName, ImageFormat expectedOutputFormat)
		{
			var inputPath = SIL.IO.FileLocator.GetFileDistributedWithApplication(_pathToTestImages, testImageName);
			using (var image = PalasoImage.FromFileRobustly(inputPath))
			{
				using (var folder = new TemporaryFolder())
				{
					var fileName = ImageUtils.ProcessAndSaveImageIntoFolder(image, folder.Path, false);
					Assert.AreEqual(expectedOutputFormat == ImageFormat.Jpeg ? ".jpg" : ".png", Path.GetExtension(fileName));
					var outputPath = folder.Combine(fileName);
					using (var img = Image.FromFile(outputPath))
					{
						Assert.AreEqual(expectedOutputFormat, img.RawFormat);
					}
					var alternativeThatShouldNotBeThere = Path.Combine(Path.GetDirectoryName(outputPath),
						Path.GetFileNameWithoutExtension(outputPath) + (expectedOutputFormat.Equals(ImageFormat.Jpeg) ? ".png" : ".jpg"));
					Assert.IsFalse(File.Exists(alternativeThatShouldNotBeThere),
						"Did not expect to have the file " + alternativeThatShouldNotBeThere);
				}
			}
		}

		// See BL-3646 which showed we were blacking out the image when converting from png to jpg
		[Test]
		public static void ProcessAndSaveImageIntoFolder_SimpleImageHasTransparentBackground_ImageNotConvertedAndFileSizeNotIncreased()
		{
			var inputPath = SIL.IO.FileLocator.GetFileDistributedWithApplication(_pathToTestImages, "shirtWithTransparentBg.png");
			var originalFileSize = new FileInfo(inputPath).Length;
			using (var image = PalasoImage.FromFileRobustly(inputPath))
			{
				using (var folder = new TemporaryFolder("TransparentPngTest"))
				{
					var fileName = ImageUtils.ProcessAndSaveImageIntoFolder(image, folder.Path, false);
					Assert.AreEqual(".png", Path.GetExtension(fileName));
					var outputPath = folder.Combine(fileName);
					using (var result = Image.FromFile(outputPath))
					{
						Assert.AreEqual(ImageFormat.Png, result.RawFormat);
						Assert.That(originalFileSize <= new FileInfo(outputPath).Length);
					}
				}
			}
		}
	}
}
