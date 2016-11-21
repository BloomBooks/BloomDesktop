using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Xml;
using Bloom;
using Bloom.Edit;
using Bloom.ImageProcessing;
using NUnit.Framework;
using SIL.TestUtilities;
using SIL.Windows.Forms.ImageToolbox;

namespace BloomTests
{
	[TestFixture]
	public class PageEditingModelTests
	{
		private int kSampleImageDimension = 5;

		[Test]
		[Category("RequiresUI")]
		public void ChangePicture_PictureIsFromOutsideProject_PictureCopiedAndAttributeChanged()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<html><body><div/><div><img id='one'/><img id='two' src='old.png'/></div></body></html>");
			
			using (var src = new TemporaryFolder("bloom pictures test source"))
			using (var dest = new TemporaryFolder("bloom picture tests dest"))
			{
				var newImagePath = src.Combine("new.png");
				using (var original = MakeSamplePngImage(newImagePath))
				{
					ChangePicture(dest.Path, dom, "two", original);
					Assert.IsTrue(File.Exists(dest.Combine("new.png")));
					AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath(@"//img[@id='two' and @src='new.png']", 1);
				}
			}

		}

		/// <summary>
		/// With this, we test the secenario where someone grabs, say "untitled.png", then does
		/// so again in a different place. At this time, we will just throw away the first one
		/// and use the new one, in both places in document. Alternatively, we could take the
		/// trouble to rename the second one to a safe name so that there are two files.
		/// </summary>
		[Test,Ignore("Test needs work")]
		public void ChangePicture_AlreadyHaveACopyInPublicationFolder_PictureUpdated()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<html><body><div/><div><img id='one'/><img id='two' src='old.png'/></div></body></html>");
			
			using (var src = new TemporaryFolder("bloom pictures test source"))
			using (var dest = new TemporaryFolder("bloom picture tests dest"))
			{
				var dogImagePath = src.Combine("dog.png");
				using (var original = MakeSamplePngImage(dogImagePath))
				{
					var destDogImagePath = dest.Combine("dog.png");
					File.WriteAllText(destDogImagePath, "old dog");
					ChangePicture(dest.Path, dom, "two", original);
					Assert.IsTrue(ImageUtils.GetImageFromFile(destDogImagePath).Width == kSampleImageDimension);
				}
			}
		}

		private PalasoImage MakeSamplePngImage(string path)
		{
			var x = new Bitmap(kSampleImageDimension, kSampleImageDimension);
			x.Save(path,ImageFormat.Png);
			x.Dispose();
			return  PalasoImage.FromFileRobustly(path);
		}

		private PalasoImage MakeSampleTifImage(string path)
		{
			var x = new Bitmap(kSampleImageDimension, kSampleImageDimension);
			x.Save(path, ImageFormat.Tiff);
			return PalasoImage.FromFileRobustly(path);
		}

		private PalasoImage MakeSampleJpegImage(string path)
		{
			var x = new Bitmap(kSampleImageDimension, kSampleImageDimension);
			x.Save(path, ImageFormat.Jpeg);
			//nb: even if we reload the image from the file, the rawformat will be memory bitmap, not jpg as we'd wish
			return PalasoImage.FromFileRobustly(path);
		}

		/// <summary>
		/// Some (or maybe all?) browsers can't show tiff, so we might as well convert it
		/// </summary>
		[Test]
		[Category("RequiresUI")]
		public void ChangePicture_PictureIsTiff_ConvertedToPng()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<html><body><div/><div><img id='one'/><img id='two' src='old.png'/></div></body></html>");
			
			using (var src = new TemporaryFolder("bloom pictures test source"))
			using (var dest = new TemporaryFolder("bloom picture tests dest"))
			using (var original = MakeSampleTifImage(src.Combine("new.tif")))
			{
				ChangePicture(dest.Path, dom, "two", original);
				Assert.IsTrue(File.Exists(dest.Combine("new.png")));
				AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath(@"//img[@id='two' and @src='new.png']", 1);
				using (var converted = Image.FromFile(dest.Combine("new.png")))
				{
					Assert.AreEqual(ImageFormat.Png.Guid, converted.RawFormat.Guid);
				}
			}
		}

		[Test]
		public void ChangePicture_PictureIsJpg_StaysJpg()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<html><body><div/><div><img id='one'/><img id='two' src='old.png'/></div></body></html>");
			
			using (var src = new TemporaryFolder("bloom pictures test source"))
			using (var dest = new TemporaryFolder("bloom picture tests dest"))
			using (var original = MakeSampleJpegImage(src.Combine("new.jpg")))
			{
				ChangePicture(dest.Path, dom, "two", original);
				Assert.IsTrue(File.Exists(dest.Combine("new.jpg")));
				AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath(@"//img[@id='two' and @src='new.jpg']", 1);
				using (var converted = Image.FromFile(dest.Combine("new.jpg")))
				{
					Assert.AreEqual(ImageFormat.Jpeg.Guid, converted.RawFormat.Guid);
				}
			}

		}
		[Test]
		public void ChangePicture_ElementIsDivWithBackgroundImage_Changes()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<html><body><div id='one' style='background-image:url(\"old.png\")'></div></body></html>");
			
			using (var src = new TemporaryFolder("bloom pictures test source"))
			using (var dest = new TemporaryFolder("bloom picture tests dest"))
			using (var original = MakeSampleJpegImage(src.Combine("new.jpg")))
			{
				ChangePicture(dest.Path, dom, "one", original);
				Assert.IsTrue(File.Exists(dest.Combine("new.jpg")));
				AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='one' and @style=\"background-image:url(\'new.jpg\')\"]", 1);
			}
		}
		/* abandoned this feature for now, as I realized we don't need it. But maybe some day.
		[Test]
		public void PreserveClassAttributeOfElement_ElementFound_HtmlChanged()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<html><body><div id='parent'><div id='foo' class='old'></div></div></body></html>");
			var model = new PageEditingModel();
			model.PreserveClassAttributeOfElement(dom.DocumentElement, "<div id='foo' class='new'></div>");
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath(@"//div[@id='foo' and @class='new']", 1);
		}*/

		public void ChangePicture(string bookFolderPath, XmlDocument dom, string imageId, PalasoImage imageInfo)
		{
			var model = new PageEditingModel();
			var node = (XmlElement) dom.SelectSingleNode("//*[@id='" + imageId + "']");
			model.ChangePicture(bookFolderPath, new ElementProxy(node), imageInfo, null);
		}

	}
}
