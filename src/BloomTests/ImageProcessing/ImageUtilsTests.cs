using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using Bloom.ImageProcessing;
using NUnit.Framework;
using SIL.IO;
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
			var path = SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(_pathToTestImages, "man.jpg");
			Assert.IsTrue(ImageUtils.ShouldChangeFormatToJpeg(ImageUtils.GetImageFromFile(path)));
		}

		[Test]
		public void ShouldChangeFormatToJpeg_OneColor_False()
		{
			var path = SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(_pathToTestImages, "bird.png");
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

		private static void ProcessAndSaveImageIntoFolder_AndTestResults(string testImageName,
			ImageFormat expectedOutputFormat)
		{
			var inputPath = SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(_pathToTestImages, testImageName);
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
		[TestCase("shirt.png")]
		// I think shirt.png still has a transparent background after being fixed by optipng, but I'm not absolutely sure,
		// so I'm leaving both tests in place.
		[TestCase("shirtWithTransparentBg.png")
#if __MonoCS__
		, Category("SkipOnTeamCity") // only for Linux: TeamCity creates an error from a low-level warning message.
#endif
		]
		public static void ProcessAndSaveImageIntoFolder_SimpleImageHasTransparentBackground_ImageNotConvertedAndFileSizeNotIncreased(string sourceFileName)
		{
			var inputPath = SIL.IO.FileLocationUtilities.GetFileDistributedWithApplication(_pathToTestImages, sourceFileName);
			var originalFileSize = new FileInfo(inputPath).Length;
			using (var image = PalasoImage.FromFileRobustly(inputPath))
			{
				using (var folder = new TemporaryFolder(MethodBase.GetCurrentMethod().Name + sourceFileName))
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

		[Test]
		[TestCase("box", "box1")]
		[TestCase("box1", "box2")]
		[TestCase("12311", "12312")]
		[TestCase("12box", "12box1")]
		[TestCase("9", "10")]
		[TestCase("b", "b1")]
		[TestCase("box99", "box100")]
		public static void GetUnusedFilenameTests(string basename, string expectedResult)
		{
			const string extension = ".txt";
			using (var folder = new TemporaryFolder("UnusedFilenameTest"))
			{
				var basePath = Path.Combine(folder.Path, basename + extension);
				RobustFile.Delete(basePath); // just in case
				RobustFile.WriteAllText(basePath, "test contents");
				var filename = ImageUtils.GetUnusedFilename(Path.GetDirectoryName(basePath), basename, extension);
				Assert.That(Path.GetFileNameWithoutExtension(filename), Is.EqualTo(expectedResult));
			}
		}

		[Test]
		[TestCase(3500, 2550, 3500, 2550)]	// maximum size landscape
		[TestCase(2550, 3500, 2550, 3500)]	// maximum size portrait
		[TestCase(3400, 2500, 3400, 2500)]	// smaller than bounds landscape
		[TestCase(2500, 3400, 2500, 3400)]	// smaller than bounds portrait
		[TestCase(3000, 3000, 2550, 2550)]	// square too large
		[TestCase(4000, 3000, 3400, 2550)]	// landscape, both too large squashed
		[TestCase(5250, 3825, 3500, 2550)]	// landscape, both too large same aspect ratio
		[TestCase(5000, 3000, 3500, 2100)]	// landscape, both too large elongated
		[TestCase(3000, 4000, 2550, 3400)]	// portrait, both too large squashed
		[TestCase(3825, 5250, 2550, 3500)]	// portrait, both too large same aspect ratio
		[TestCase(3000, 5000, 2100, 3500)]	// portrait, both too large elongated
		[TestCase(2500, 5000, 1750, 3500)]	// portrait, height too large
		[TestCase(5000, 2500, 3500, 1750)]	// landscape, width too large
		[TestCase(3400, 2700, 3211, 2550)]	// landscape, height too large
		[TestCase(2700, 3400, 2550, 3211)]	// portrait, width too large
		public static void TestGetImageSizes(int width, int height, int newWidth, int newHeight)
		{
			var size = ImageUtils.GetDesiredImageSize(width, height);
			Assert.AreEqual(size.Width, newWidth, $"Computed width for {width},{height} is correct.");
			Assert.AreEqual(size.Height, newHeight, $"Computed height for {width},{height} is correct.");
		}

		[Test]
		[TestCase(true)]
		[TestCase(false)]
		public void DrawResizedImage_TestForDashedBorder_SmallSquareImage(bool addBorder)
		{
			var path = FileLocationUtilities.GetFileDistributedWithApplication(_pathToTestImages, "Othello 199.jpg");
			var image = new Bitmap(path);
			var desiredThumbSize = new Size(200, 200);

			// SUT
			var result = ImageUtils.ResizeImageIfNecessary(desiredThumbSize, image, addBorder);

			// Testing
			TestImageResult(result, addBorder);
		}

		[Test]
		[TestCase(false, false)] // Resize, no border
		[TestCase(false, true)] // Resize, add dashed border
		[TestCase(true, false)] // Center image, no border
		[TestCase(true, true)] // Center image, add dashed border
		public void DrawResizedImage_TestForPresenceOfDashedBorder(bool centerImage, bool addBorder)
		{
			var path = FileLocationUtilities.GetFileDistributedWithApplication(_pathToTestImages, "bird.png");
			var image = new Bitmap(path);
			var desiredThumbSize = new Size(70, 70);

			// SUT
			// I don't like conditionals in tests.
			// Unfortunately "DrawResizedImage()" is a private method that is very simply wrapped by two
			// public methods.
			var result = centerImage ?
				ImageUtils.CenterImageIfNecessary(desiredThumbSize, image, addBorder) :
				ImageUtils.ResizeImageIfNecessary(desiredThumbSize, image, addBorder);

			// Test image result
			TestImageResult(result, addBorder);
		}

		private static void TestImageResult(Image result, bool shouldHaveDashedBorder)
		{
			var bitmap = new Bitmap(result);

			var hitMax = false;
			var foundFirstDash = false;
			var foundFirstSpace = false;
			const int maxX = 20;
			for (var i = 0; i < maxX; i++)
			{
				var pixel = bitmap.GetPixel(i, 0);

				if (shouldHaveDashedBorder)
				{
					if (IsColorOpaqueBlack(pixel))
					{
						if (foundFirstDash && foundFirstSpace)
						{
							break; // found a second dash
						}

						foundFirstDash = true;
					}
					else
					{
						foundFirstSpace = true;
					}
				}
				else
				{
					Assert.That(IsColorOpaqueBlack(pixel), Is.False, $"Point ({i}, 0) should not be black.");
				}

				if (i == maxX - 1)
					hitMax = true;
			}
			Assert.That(shouldHaveDashedBorder, Is.Not.EqualTo(hitMax),
				"We should have a dashed border and we didn't find dashes, or we should not have a border and we didn't finish the loop.");
		}

		private static bool IsColorOpaqueBlack(Color color)
		{
			return (color.R | color.G | color.B) == 0 && color.A == 255;
		}
	}
}
