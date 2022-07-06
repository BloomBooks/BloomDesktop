using System;
using System.Collections.Generic;
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
		[TestCase(1920, 1080)]
		[TestCase(1280, 720)]
		[TestCase(854, 480)]
		[TestCase(640, 360)]
		[TestCase(426, 240)]
		[TestCase(256, 144)]
		public void GetAspectRatio_16By9Landscape(int width, int height)
		{
			var aspectRatio = new Resolution(width, height).GetAspectRatio();

			Assert.AreEqual("16:9", aspectRatio);
		}

		[TestCase(1080, 1920)]
		[TestCase(720, 1280)]
		[TestCase(480, 854)]
		[TestCase(360, 640)]
		[TestCase(240, 426)]
		[TestCase(144, 256)]
		public void GetAspectRatio_9By16Portrait(int width, int height)
		{
			var aspectRatio = new Resolution(width, height).GetAspectRatio();

			Assert.AreEqual("9:16", aspectRatio);
		}

		[TestCase(1080, 720)]
		public void GetAspectRatio_3By2Landscape(int width, int height)
		{
			var aspectRatio = new Resolution(width, height).GetAspectRatio();

			Assert.AreEqual("3:2", aspectRatio);
		}

		[TestCase(720, 1080)]
		public void GetAspectRatio_2By3Portrait(int width, int height)
		{
			var aspectRatio = new Resolution(width, height).GetAspectRatio();

			Assert.AreEqual("2:3", aspectRatio);
		}

		[TestCase(900, 720)]
		public void GetAspectRatio_5By4Landscape(int width, int height)
		{
			var aspectRatio = new Resolution(width, height).GetAspectRatio();

			Assert.AreEqual("5:4", aspectRatio);
		}

		[TestCase(720, 900)]
		public void GetAspectRatio_4By5Portrait(int width, int height)
		{
			var aspectRatio = new Resolution(width, height).GetAspectRatio();

			Assert.AreEqual("4:5", aspectRatio);
		}

		[TestCase(720, 720)]
		public void GetAspectRatio_Square(int width, int height)
		{
			var aspectRatio = new Resolution(width, height).GetAspectRatio();

			Assert.AreEqual("1:1", aspectRatio);
		}

		[TestCase(2100, 900)]
		public void GetAspectRatio_UnrecognizedLandscape_FallbackAspectRatioGenerated(int width, int height)
		{
			Utilities.DisableDebugListeners();
			try
			{
				var aspectRatio = new Resolution(width, height).GetAspectRatio();
				Assert.AreEqual("2.33:1", aspectRatio);
			}
			finally
			{
				Utilities.EnableDebugListeners();
			}
		}
		#endregion
	}
}
