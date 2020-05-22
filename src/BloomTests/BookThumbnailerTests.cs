using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Bloom;
using BloomTests.Book;
using NUnit.Framework;
using SIL.Extensions;
using SIL.IO;
using SIL.Progress;

namespace BloomTests
{
	[TestFixture]
	class BookThumbnailerTests : BookTestsBase
	{
		protected override string GetTestFolderName() => "BookThumbnailerTests";
		
		private Bloom.Book.Book SetupBook(string coverImageFilename, int coverImageWidth, int coverImageHeight)
		{
			var imgTag = $"<img style='width: {coverImageWidth};' src='{coverImageFilename}' height='{coverImageHeight}' alt='missing'></img>";
			SetDom(@"<div id='bloomDataDiv'>
						<div data-book='coverImage' lang='*'>
							" + imgTag + @"
						</div>
					</div>
					<div class='bloom-page bloom-frontMatter'>
						<div class='marginBox'>
							<div class='bloom-imageContainer' data-book='coverImage'>
							</div>
						</div>
					</div>");
			var book = CreateBook();
			var imagePath = book.FolderPath.CombineForPath(coverImageFilename);
			MakeSamplePngImageWithMetadata(imagePath, coverImageWidth, coverImageHeight);
			book.BringBookUpToDate(new NullProgress());

			return book;
		}

		// Bigger images
		[TestCase(70, 1024, 768, 70, 52)]
		[TestCase(256, 1024, 768, 256, 192)]
		[TestCase(70, 768, 1024, 52, 70)]
		[TestCase(256, 768, 1024, 192, 256)]
		[TestCase(100, 400, 400, 100, 100)]
		// Same size - no adjustment
		[TestCase(256, 256, 100, 256, 100)]
		[TestCase(256, 100, 256, 100, 256)]
		[TestCase(100, 100, 100, 100, 100)]
		// Images smaller than requested size - NOT upscaled
		[TestCase(256, 100, 50, 100, 50)]
		[TestCase(256, 50, 100, 50, 100)]
		[TestCase(100, 50, 50, 50, 50)]
		public void GenerateCoverImageOfRequestedMaxSize_GivenVariousThmbnailSizes_ThumbnailIsCorrectSize(int requestedSize, int originalWidth, int originalHeight, int expectedWidth, int expectedHeight)
		{
			// Setup
			var book = SetupBook("coverImage.png", originalWidth, originalHeight);

			// System under test
			BookThumbNailer.GenerateCoverImageOfRequestedMaxSize(book, requestedSize);

			// Verification
			string expectedThumbnailFilename = book.FolderPath.CombineForPath($"thumbnail-{requestedSize}.png");
			Assert.That(RobustFile.Exists(expectedThumbnailFilename), Is.True, "Thumbnail does not exist at expected path");

			using (var image = System.Drawing.Image.FromFile(expectedThumbnailFilename))
			{
				Assert.That(image.Width, Is.EqualTo(expectedWidth), "image width");
				Assert.That(image.Height, Is.EqualTo(expectedHeight), "image height");
			}
		}

		[TestCase(600, 600)]		
		public void GenerateSocialMediaSharingThumbnail_LargerSquareImage_AllOpaque(int originalWidth, int originalHeight)
		{
			int thumbnailSize = 300;
			int thumbnailCenter = thumbnailSize / 2;

			// Define expectations
			var opaquePoints = new List<(int, int)>()
			{
				(0, 0),
				(0, thumbnailSize - 1),
				(thumbnailSize - 1, 0),
				(thumbnailSize -1, thumbnailSize - 1),

				(thumbnailCenter, thumbnailCenter)
			};

			var transparentPoints = Enumerable.Empty<(int, int)>();

			// Run the test helper
			RunSocialMediaSharingThumbnailTest(thumbnailSize, originalWidth, originalHeight, opaquePoints, transparentPoints);
		}

		[Test]
		public void GenerateSocialMediaSharingThumbnail_LargerLandscapeImage_TransparentTopBottomAdded()
		{
			int originalWidth = 900;
			int originalHeight = 600;

			int thumbnailSize = 300;
			int thumbnailCenter = thumbnailSize / 2;

			// Define expectations
			var opaquePoints = new List<(int, int)>()
			{
				(0, thumbnailCenter),	// Left
				(thumbnailSize - 1, thumbnailCenter),	// Right
				(thumbnailCenter, 50),	// Top
				(thumbnailCenter, 249),	// Bottom
				(thumbnailCenter, thumbnailCenter)	// Center
			};

			var transparentPoints = new List<(int, int)>()
			{
				(thumbnailCenter, 49),	// Top
				(thumbnailCenter, 250),	// Bottom
			};

			// Run the test helper
			RunSocialMediaSharingThumbnailTest(thumbnailSize, originalWidth, originalHeight, opaquePoints, transparentPoints);
		}

		[Test]
		public void GenerateSocialMediaSharingThumbnail_LargerPortraitImage_TransparentLeftRightAdded()
		{
			int originalWidth = 600;
			int originalHeight = 900;

			int thumbnailSize = 300;
			int thumbnailCenter = thumbnailSize / 2;

			// Define expectations
			var opaquePoints = new List<(int, int)>()
			{
				(50, thumbnailCenter),	// Left
				(249, thumbnailCenter),	// Right
				(thumbnailCenter, 0),	// Top
				(thumbnailCenter, thumbnailSize-1),	// Bottom
				(thumbnailCenter, thumbnailCenter)	// Center
			};

			var transparentPoints = new List<(int, int)>()
			{
				(49, thumbnailCenter),	// Left
				(250, thumbnailCenter),	// Right
			};

			// Run the test helper
			RunSocialMediaSharingThumbnailTest(thumbnailSize, originalWidth, originalHeight, opaquePoints, transparentPoints);
		}

		[Test]
		public void GenerateSocialMediaSharingThumbnail_SmallerRectangularImage_TransparentOutsideOpaqueInside()
		{
			int originalWidth = 150;
			int originalHeight = 100;

			int thumbnailSize = 300;
			int thumbnailCenter = thumbnailSize / 2;

			// Define expectations
			var opaquePoints = new List<(int, int)>()
			{
				// Test for left
				(75, thumbnailCenter),
				// Test for right
				(224, thumbnailCenter),
				// Test for top
				(thumbnailCenter, 100),
				// Test for bottom
				(thumbnailCenter, 199)
			};

			var transparentPoints = new List<(int, int)>()
			{
				// Test for left
				(75-1, thumbnailCenter),
				// Test for right
				(224+1, thumbnailCenter),
				// Test for top
				(thumbnailCenter, 100-1),
				// Test for bottom
				(thumbnailCenter, 199+1)
			};

			// Run the test helper
			RunSocialMediaSharingThumbnailTest(thumbnailSize, originalWidth, originalHeight, opaquePoints, transparentPoints);
		}

		[Test]
		public void GenerateSocialMediaSharingThumbnail_PlaceholderImageOnCover_ThumbnailStillGenerated()
		{
			int originalWidth = 341;
			int originalHeight = 335;
			var book = SetupBook("placeHolder.png", originalWidth, originalHeight);

			int thumbnailSize = 300;
			int thumbnailCenter = thumbnailSize / 2;

			// Define some basic expectations
			// The main test is that the filename exists at all.
			var opaquePoints = new List<(int, int)>() { (thumbnailCenter, thumbnailCenter) };

			var transparentPoints = new List<(int, int)>()
			{
				// Test for left
				(0, 0),
				// Test for right
				(0, thumbnailSize - 1),
				// Test for top
				(thumbnailSize - 1, 0),
				// Test for bottom
				(thumbnailSize - 1, thumbnailSize-1)
			};

			// Run the test helper
			RunSocialMediaSharingThumbnailTest(thumbnailSize, originalWidth, originalHeight, opaquePoints, transparentPoints, book);
		}

		private void RunSocialMediaSharingThumbnailTest(int thumbnailSize, int inputWidth, int inputHeight, IEnumerable<(int, int)> expectedOpaquePoints, IEnumerable<(int, int)> expectedTransparentPoints, Bloom.Book.Book book = null)
		{
			// Setup
			if (book == null)
				book = SetupBook("coverImage.png", inputWidth, inputHeight);

			// System under test
			string thumbnailFilename = BookThumbNailer.GenerateSocialMediaSharingThumbnail(book);

			// Verification
			Assert.That(RobustFile.Exists(thumbnailFilename), Is.True, "Thumbnail does not exist at expected path");
			using (var image = System.Drawing.Image.FromFile(thumbnailFilename))
			{
				Assert.That(image.Width, Is.EqualTo(thumbnailSize), "image width");
				Assert.That(image.Height, Is.EqualTo(thumbnailSize), "image height");

				var bitmap = new Bitmap(image);

				foreach (var (x, y) in expectedOpaquePoints)
				{
					var pixel = bitmap.GetPixel(x, y);
					var alphaOpacity = pixel.A;

					Assert.That(alphaOpacity, Is.EqualTo(255), $"Alpha at point ({x}, {y}) should be opaque.");
				}

				foreach (var (x, y) in expectedTransparentPoints)
				{
					var pixel = bitmap.GetPixel(x, y);
					var alphaOpacity = pixel.A;

					Assert.That(alphaOpacity, Is.EqualTo(0), $"Alpha at point ({x}, {y}) should be transparent.");
				}
			}
		}

		[TestCase("thumbnail.png")]
		[TestCase("thumbnail-70.png")]
		[TestCase("thumbnail-256.png")]
		[TestCase("thumbnail-75x75.png")]
		[TestCase("thumbnail-1027x768.png")]
		public void IsCoverImageSrcValid_PlaceholderImageButScenarioAllowsIt_Valid(string newThumbnailFileName)
		{
			var options = new HtmlThumbNailer.ThumbnailOptions { FileName = newThumbnailFileName };

			bool isValid = BookThumbNailer.IsCoverImageSrcValid("placeHolder.png", options);

			Assert.That(isValid, Is.True);
		}

		[TestCase("coverImage200.jpg")]
		[TestCase("coverImage-200.png")]
		[TestCase("coverImage-300x300.png")]
		[TestCase("thumbnail.jpg")]
		[TestCase("thumbnailAsdf.png")]
		[TestCase("thumbnail-70Asdf.png")]
		[TestCase("thumbnail-300x300Asdf.png")]
		public void IsCoverImageSrcValid_PlaceholderImageButScenarioDisallowsIt_NotValid(string newThumbnailFileName)
		{
			var options = new HtmlThumbNailer.ThumbnailOptions { FileName = newThumbnailFileName };

			bool isValid = BookThumbNailer.IsCoverImageSrcValid("placeHolder.png", options);

			Assert.That(isValid, Is.False);
		}
	}
}
