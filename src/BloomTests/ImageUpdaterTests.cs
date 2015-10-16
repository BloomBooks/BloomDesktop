using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Remoting;
using System.Xml;
using Bloom;
using Bloom.Book;
using BloomTemp;
using NUnit.Framework;
using Palaso.Progress;
using Palaso.UI.WindowsForms.ClearShare;
using Palaso.UI.WindowsForms.ImageToolbox;

namespace BloomTests
{
	[TestFixture]
	public class ImageUpdaterTests
	{
		[Test]
		public void UpdateImgMetdataAttributesToMatchImage_GivenImgElement_AddsMetadata()
		{
			TestUpdateImgMetadataAttributesToMatchImage("<html><body><div/><div><img id='one'/><img id='two' src='test.png' data-copyright='old'/></div></body></html>");
		}
		[Test]
		public void UpdateImgMetdataAttributesToMatchImage_GivenBackgroundImageElement_AddsMetadata()
		{
			TestUpdateImgMetadataAttributesToMatchImage("<html><body><img id='one'/><div id='two'  data-copyright='old' style='color:orange; background-image=url(\"test.png\")'/></body></html>");
		}
		private void TestUpdateImgMetadataAttributesToMatchImage(string contents)
		{
			var dom = new XmlDocument();
			dom.LoadXml(contents);

			using (var folder = new TemporaryFolder("bloom pictures test source"))
			{
				MakeSamplePngImageWithMetadata(folder.Combine("test.png"));
				ImageUpdater.UpdateImgMetdataAttributesToMatchImage(folder.FolderPath,
					dom.SelectSingleNode("//*[@id='two']") as XmlElement, new NullProgress());
			}

			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//*[@data-copyright='Copyright 1999 by me']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//*[@data-creator='joe']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//*[@data-license='cc-by-nd']", 1);
		}

		[Test]
		public void UpdateAllHtmlDataAttributesForAllImgElements_HasBothImgAndBackgroundImageElements_UpdatesBoth()
		{
			var dom = new HtmlDom("<html><body><img src='test.png'/><div style='color:orange; background-image=url(\"test.png\")'/></body></html>");

			using (var folder = new TemporaryFolder("bloom pictures test source"))
			{
				MakeSamplePngImageWithMetadata(folder.Combine("test.png"));
				ImageUpdater.UpdateAllHtmlDataAttributesForAllImgElements(folder.FolderPath, dom, new NullProgress());
			}

			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//*[@data-copyright='Copyright 1999 by me']", 2);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//*[@data-creator='joe']", 2);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//*[@data-license='cc-by-nd']", 2);
		}

		protected void MakeSamplePngImageWithMetadata(string path)
		{
			var x = new Bitmap(10, 10);
			x.Save(path, ImageFormat.Png);
			x.Dispose();
			using (var img = PalasoImage.FromFile(path))
			{
				img.Metadata.Creator = "joe";
				img.Metadata.CopyrightNotice = "Copyright 1999 by me";
				img.Metadata.License = new CreativeCommonsLicense(true,true,CreativeCommonsLicense.DerivativeRules.NoDerivatives);
				img.SaveUpdatedMetadataIfItMakesSense();
			}
		}
	}
}
