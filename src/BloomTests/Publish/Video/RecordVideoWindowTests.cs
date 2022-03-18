using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bloom.Publish.Video;
using NUnit.Framework;

namespace BloomTests.Publish.Video
{
	[TestFixture]
	internal class RecordVideoWindowTests
	{
		[TestCase(2560, 1440)]
		[TestCase(1920, 1080)]
		[TestCase(1280, 720)]
		[TestCase(854, 480)]
		public void RecordVideoWindowGetBestYouTubeSize_LandscapeBookOnExactStandardSizeScreen_ReturnsThatSize(int maxWidth, int maxHeight)
		{
			var result = RecordVideoWindow.GetBestYouTubeSize(maxWidth, maxHeight, true);

			var expectedResult = new RecordVideoWindow.Resolution(maxWidth, maxHeight);
			Assert.AreEqual(expectedResult.ToString(), result.ToString());
		}

		[TestCase(2560 - 1, 1440, 1920, 1080)]
		[TestCase(1920, 1080 - 1, 1280, 720)]
		[TestCase(1280 - 1, 720, 854, 480)]
		[TestCase(854, 480 - 1, 640, 360)]
		public void RecordVideoWindowGetBestYouTubeSize_LandscapeBookOnSlightlySmallerThanStandardSizeScreen_ReturnsOneStandardSizeDown(int maxWidth, int maxHeight, int expectedWidth, int expectedHeight)
		{
			var result = RecordVideoWindow.GetBestYouTubeSize(maxWidth, maxHeight, true);

			var expectedResult = new RecordVideoWindow.Resolution(expectedWidth, expectedHeight);
			Assert.AreEqual(expectedResult.ToString(), result.ToString());
		}

		[Test]
		public void RecordVideoWindowGetBestYouTubeSize_PortraitBookOnLandscapeScreen_ReturnsStandardHeightAndProportionalWidth()
		{
			var result = RecordVideoWindow.GetBestYouTubeSize(1920, 1080, false);

			var expectedResult = new RecordVideoWindow.Resolution(480, 854);
			Assert.AreEqual(expectedResult.ToString(), result.ToString());
		}

		[TestCase(2160, 3840)]
		[TestCase(1440, 2560)]
		[TestCase(1080, 1920)]
		[TestCase(720, 1280)]
		[TestCase(480, 854)]
		[TestCase(360, 640)]
		[TestCase(240, 426)]
		[TestCase(144, 256)]
		public void RecordVideoWindowGetBestYouTubeSize_PortraitBookOnPortraitScreen_ReturnsStandardHeightAndProportionalWidth(int maxWidth, int expectedHeight)
		{
			int maxHeight = maxWidth * 2;	// A 9x16 video will fit in this screen, with plenty of buffer
			var result = RecordVideoWindow.GetBestYouTubeSize(maxWidth, maxHeight, false);

			int expectedWidth = maxWidth;
			var expectedResult = new RecordVideoWindow.Resolution(expectedWidth, expectedHeight);
			Assert.AreEqual(expectedResult.ToString(), result.ToString());
		}
	}
}
