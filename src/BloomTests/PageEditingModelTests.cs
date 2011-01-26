using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Bloom.Edit;
using NUnit.Framework;
using Palaso.TestUtilities;
using Palaso.UI.WindowsForms.ImageToolbox;

namespace BloomTests
{
	public class PageEditingModelTests
	{
		private int kSampleImageDimension = 5;

		[Test]
		public void ChangePicture_PictureIsFromOutsideProject_PictureCopiedAndAttributeChanged()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<html><body><div/><div><img id='one'/><img id='two' src='old.png'/></div></body></html>");
			var model = new PageEditingModel();
			using (var src = new TemporaryFolder("bloom pictures test source"))
			using (var dest = new TemporaryFolder("bloom picture tests dest"))
			{
				var newImagePath = src.Combine("new.png");
				model.ChangePicture(dest.Path, dom, "two", MakeSamplePngImage(newImagePath));
				Assert.IsTrue(File.Exists(dest.Combine("new.png")));
				AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath(@"//img[@id='two' and @src='new.png']", 1);
		  }

		}

		/// <summary>
		/// With this, we test the secenario where someone grabs, say "untitled.png", then does
		/// so again in a different place. At this time, we will just throw away the first one
		/// and use the new one, in both places in document. Alternatively, we could take the
		/// trouble to rename the second one to a safe name so that there are two files.
		/// </summary>
		[Test]
		public void ChangePicture_AlreadyHaveACopyInPublicationFolder_PictureUpdated()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<html><body><div/><div><img id='one'/><img id='two' src='old.png'/></div></body></html>");
			var model = new PageEditingModel();
			using (var src = new TemporaryFolder("bloom pictures test source"))
			using (var dest = new TemporaryFolder("bloom picture tests dest"))
			{
				var dogImagePath = src.Combine("dog.png");
				var destDogImagePath = dest.Combine("dog.png");
				File.WriteAllText(destDogImagePath, "old dog");
				model.ChangePicture(dest.Path, dom, "two", MakeSamplePngImage(dogImagePath));
				Assert.IsTrue(Image.FromFile(destDogImagePath).Width == kSampleImageDimension);
			}
		}

		private PalasoImage MakeSamplePngImage(string path)
		{
			var x = new Bitmap(kSampleImageDimension, kSampleImageDimension);
			x.Save(path,ImageFormat.Png);
			return new PalasoImage() {Image = x, FileName = Path.GetFileName(path)};
		}

		private PalasoImage MakeSampleTifImage(string path)
		{
			var x = new Bitmap(kSampleImageDimension, kSampleImageDimension);
			x.Save(path, ImageFormat.Tiff);
			return new PalasoImage() { Image = x, FileName = Path.GetFileName(path) };
		}

		private PalasoImage MakeSampleJpegImage(string path)
		{
			var x = new Bitmap(kSampleImageDimension, kSampleImageDimension);
			x.Save(path, ImageFormat.Jpeg);
			//nb: even if we reload the image from the file, the rawformat will be memory bitmap, not jpg as we'd wish
			return new PalasoImage() { Image = Image.FromFile(path), FileName = Path.GetFileName(path) };
		}

		/// <summary>
		/// Some (or maybe all?) browsers can't show tiff, so we might as well convert it
		/// </summary>
		[Test]
		public void ChangePicture_PictureIsTiff_ConvertedToPng()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<html><body><div/><div><img id='one'/><img id='two' src='old.png'/></div></body></html>");
			var model = new PageEditingModel();
			using (var src = new TemporaryFolder("bloom pictures test source"))
			using (var dest = new TemporaryFolder("bloom picture tests dest"))
			{
				model.ChangePicture(dest.Path, dom, "two", MakeSampleTifImage(src.Combine("new.tif")));
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
			var model = new PageEditingModel();
			using (var src = new TemporaryFolder("bloom pictures test source"))
			using (var dest = new TemporaryFolder("bloom picture tests dest"))
			{
				model.ChangePicture(dest.Path, dom, "two", MakeSampleJpegImage(src.Combine("new.jpg")));
				Assert.IsTrue(File.Exists(dest.Combine("new.jpg")));
				AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath(@"//img[@id='two' and @src='new.jpg']", 1);
				using (var converted = Image.FromFile(dest.Combine("new.jpg")))
				{
					Assert.AreEqual(ImageFormat.Jpeg.Guid, converted.RawFormat.Guid);
				}
			}

		}
	}
}
