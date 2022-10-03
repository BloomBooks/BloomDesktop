using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Bloom.Publish.Video;
using NUnit.Framework;

namespace BloomTests.Publish.Video
{
	[TestFixture]
	internal class ResolutionTests
    {
		#region GetInverse tests
		[Test]
		public void GetInverse()
		{
			var resolution = new Resolution(800, 600);
			var invertedResolution = resolution.GetInverse();
			Assert.AreEqual(resolution.Height, invertedResolution.Width, "Inverted Resolution width should be original height");
			Assert.AreEqual(resolution.Width, invertedResolution.Height, "Inverted Resolution height should be original width");
		}
		#endregion

		#region GetAspectRatio tests
		// 16:9 Landscape
		[TestCase(1920, 1080, ExpectedResult = "16:9")]
		[TestCase(1280, 720, ExpectedResult = "16:9")]
		[TestCase(854, 480, ExpectedResult = "16:9")]
		[TestCase(640, 360, ExpectedResult = "16:9")]
		[TestCase(426, 240, ExpectedResult = "16:9")]
		[TestCase(256, 144, ExpectedResult = "16:9")]
		// 9:16 Portrait
		[TestCase(1080, 1920, ExpectedResult = "9:16")]
		[TestCase(720, 1280, ExpectedResult = "9:16")]
		[TestCase(480, 854, ExpectedResult = "9:16")]
		[TestCase(360, 640, ExpectedResult = "9:16")]
		[TestCase(240, 426, ExpectedResult = "9:16")]
		[TestCase(144, 256, ExpectedResult = "9:16")]
		// 3:2 or 2:3
		[TestCase(1080, 720, ExpectedResult = "3:2")]
		[TestCase(720, 1080, ExpectedResult = "2:3")]
		// 5:4 or 4:5
		[TestCase(900, 720, ExpectedResult = "5:4")]
		[TestCase(720, 900, ExpectedResult = "4:5")]
		// Square
		[TestCase(720, 720, ExpectedResult = "1:1")]
		public string GetAspectRatio_GivenStandardResolutions_StandardAspectRatioGenerated(int width, int height)
		{
			return new Resolution(width, height).GetAspectRatio();
		}

		[TestCase(2100, 900, ExpectedResult = "2.33:1")]
		public string GetAspectRatio_GivenNonStandardResolution_FallbackAspectRatioGenerated(int width, int height)
		{
			Utilities.DisableDebugListeners();
			try
			{
				// 'ratio' is culture-specific; for test purposes, we need to match a culture invariant version.
				var ratio = new Resolution(width, height).GetAspectRatio();
				var colonIdx = ratio.IndexOf(":", StringComparison.InvariantCulture);
				var first = ratio.Substring(0, colonIdx);
				var second = ratio.Substring(colonIdx);
				return double.Parse(first).ToString(CultureInfo.InvariantCulture) + second ;
			}
			finally
			{
				Utilities.EnableDebugListeners();
			}
		}
		#endregion
	}
}
