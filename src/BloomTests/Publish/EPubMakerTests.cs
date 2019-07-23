using Bloom.Book;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using SIL.Xml;
using Assert = NUnit.Framework.Assert;

namespace BloomTests.Publish
{
	[TestFixture]
	public class EPubMakerTests
	{
		[Test]
		public void HandleImageDescriptions_LicenseAlt_SetToDefault()
		{
			string inputImageHtml = "<img class=\"licenseImage\" src=\"a.png\" alt=\"This picture, a.png, is missing or was loading too slowly.\" />";
			var htmlDom = new HtmlDom($"<html><body><div data-book=\"outside-back-cover-branding-bottom-html\">{inputImageHtml}</div></body></html>");

			HandleImageDescriptions(htmlDom);

			string expectedImageHtml = "<img class=\"licenseImage\" src=\"a.png\" alt=\"Image representing the license of this book\" role=\"presentation\" />";
			Assert.That(htmlDom.InnerXml, Is.EqualTo($"<html><body><div data-book=\"outside-back-cover-branding-bottom-html\">{expectedImageHtml}</div></body></html>"));
		}

		[Test]
		public void HandleImageDescriptions_BrandingAlt_Custom_Unchanged()
		{
			string inputImageHtml = "<img src=\"a.png\" alt=\"Logo of CLB\" />";
			var htmlDom = new HtmlDom($"<html><body><div data-book=\"outside-back-cover-branding-bottom-html\">{inputImageHtml}</div></body></html>");

			HandleImageDescriptions(htmlDom);

			string expectedImageHtml = "<img src=\"a.png\" alt=\"Logo of CLB\" role=\"presentation\" />";
			Assert.That(htmlDom.InnerXml, Is.EqualTo($"<html><body><div data-book=\"outside-back-cover-branding-bottom-html\">{expectedImageHtml}</div></body></html>"));
		}

		[Test]
		public void HandleImageDescriptions_BrandingAlt_Placeholder_SetToDefault()
		{
			string inputImageHtml = "<img src=\"a.png\" alt=\"This picture, a.png, is missing or was loading too slowly.\" />";
			var htmlDom = new HtmlDom($"<html><body><div data-book=\"outside-back-cover-branding-bottom-html\">{inputImageHtml}</div></body></html>");

			HandleImageDescriptions(htmlDom);

			string expectedImageHtml = "<img src=\"a.png\" alt=\"Logo of the book sponsors\" role=\"presentation\" />";
			Assert.That(htmlDom.InnerXml, Is.EqualTo($"<html><body><div data-book=\"outside-back-cover-branding-bottom-html\">{expectedImageHtml}</div></body></html>"));
		}

		[Test]
		public void HandleImageDescriptions_BrandingAlt_NotSet_SetToDefault()
		{
			string inputImageHtml = "<img src=\"a.png\" />";
			var htmlDom = new HtmlDom($"<html><body><div data-book=\"outside-back-cover-branding-bottom-html\">{inputImageHtml}</div></body></html>");

			HandleImageDescriptions(htmlDom);

			string expectedImageHtml = "<img src=\"a.png\" alt=\"Logo of the book sponsors\" role=\"presentation\" />";
			Assert.That(htmlDom.InnerXml, Is.EqualTo($"<html><body><div data-book=\"outside-back-cover-branding-bottom-html\">{expectedImageHtml}</div></body></html>"));
		}

		[Test]
		public void HandleImageDescriptions_AriaHidden()
		{
			string inputImageHtml = "<img src='a.png'/>";
			var htmlDom = new HtmlDom($@"<html><body>
	<div class='bloom-page' data-page-number='5'>
		<div class='marginBox'>
			<div class='bloom-imageContainer' aria-hidden='true'>
				{inputImageHtml}
				<div class='bloom-translationGroup bloom-imageDescription bloom-content1'>
					<div class='bloom-editable bloom-visibility-code-on' lang='en'></div>
				</div>
			</div>
		</div>
	</div>
</body></html>");

			HandleImageDescriptions(htmlDom);

			var imgNodes = htmlDom.SafeSelectNodes("//img[@src='a.png']");
			Assert.That(imgNodes, Is.Not.Null);
			Assert.That(imgNodes.Count, Is.EqualTo(1));
			var img = imgNodes[0] as System.Xml.XmlElement;
			Assert.That(img, Is.Not.Null);
			Assert.That(img.Attributes.Count, Is.EqualTo(2));
			Assert.That(img.GetAttribute("role"), Is.EqualTo("presentation"));
		}

		[Test]
		public void HandleImageDescriptions_AriaNotHidden()
		{
			string inputImageHtml = "<img src='a.png'/>";
			var htmlDom = new HtmlDom($@"<html><body>
	<div class='bloom-page' data-page-number='5'>
		<div class='marginBox'>
			<div class='bloom-imageContainer'>
				{inputImageHtml}
				<div class='bloom-translationGroup bloom-imageDescription'>
					<div class='bloom-editable bloom-visibility-code-on bloom-content1' lang='en'>
						<p>This is a test.</p>
					</div>
				</div>
			</div>
		</div>
	</div>
</body></html>");

			HandleImageDescriptions(htmlDom);

			var imgNodes = htmlDom.SafeSelectNodes("//img[@src='a.png']");
			Assert.That(imgNodes, Is.Not.Null);
			Assert.That(imgNodes.Count, Is.EqualTo(1));
			var img = imgNodes[0] as System.Xml.XmlElement;
			Assert.That(img, Is.Not.Null);
			Assert.That(img.Attributes.Count, Is.EqualTo(2));
			Assert.That(img.GetAttribute("role"), Is.EqualTo(""));
			Assert.That(img.GetAttribute("alt"), Is.EqualTo("This is a test."));
		}

		[Test]
		public void HandleImageDescriptions_HowTo_OnPage_MovesDescriptionToAside()
		{
			string inputImageHtml = "<img src='a.png'/>";
			var htmlDom = new HtmlDom($@"<html><body>
	<div class='bloom-page' data-page-number='5'>
		<div class='marginBox'>
			<div class='bloom-imageContainer'>
				{inputImageHtml}
				<div class='bloom-translationGroup bloom-imageDescription'>
					<div class='bloom-editable bloom-visibility-code-on bloom-content1' lang='en'>
						<p>This is a test.</p>
					</div>
				</div>
			</div>
		</div>
	</div>
</body></html>");

			HandleImageDescriptions(htmlDom, BookInfo.HowToPublishImageDescriptions.OnPage);

			var divList = htmlDom.SafeSelectNodes("//div[@class='marginBox']/div");
			Assert.That(divList, Is.Not.Null);
			Assert.That(divList.Count, Is.EqualTo(2));

			var divImage = divList[0] as System.Xml.XmlElement;
			Assert.That(divImage.GetAttribute("class"), Is.EqualTo("bloom-imageContainer"));
			var imgNodes = divImage.SafeSelectNodes("./*");
			Assert.That(imgNodes, Is.Not.Null);
			Assert.That(imgNodes.Count, Is.EqualTo(1));
			var img = imgNodes[0] as System.Xml.XmlElement;
			Assert.That(img.LocalName, Is.EqualTo("img"));
			Assert.That(img.GetAttribute("alt"), Is.EqualTo("This is a test."));

			var divAside = divList[1] as System.Xml.XmlElement;
			Assert.That(divAside.GetAttribute("class"), Is.EqualTo("asideContainer"));
			var asideNodes = divAside.SafeSelectNodes("./*");
			Assert.That(asideNodes, Is.Not.Null);
			Assert.That(asideNodes.Count, Is.EqualTo(1));
			var aside = asideNodes[0] as System.Xml.XmlElement;
			Assert.That(aside.LocalName, Is.EqualTo("aside"));
			var paraNodes = aside.SafeSelectNodes("./*");
			Assert.That(paraNodes, Is.Not.Null);
			Assert.That(paraNodes.Count, Is.EqualTo(1));
			var para = paraNodes[0] as System.Xml.XmlElement;
			Assert.That(para.LocalName, Is.EqualTo("p"));
			Assert.That(para.InnerText, Is.EqualTo("This is a test."));
			Assert.That(para.InnerXml, Is.EqualTo("This is a test."));
		}

		[Test]
		public void HandleImageDescriptions_HowTo_None_LeavesDescriptionAlone()
		{
			string inputImageHtml = "<img src='a.png'/>";
			var htmlDom = new HtmlDom($@"<html><body>
	<div class='bloom-page' data-page-number='5'>
		<div class='marginBox'>
			<div class='bloom-imageContainer'>
				{inputImageHtml}
				<div class='bloom-translationGroup bloom-imageDescription'>
					<div class='bloom-editable bloom-visibility-code-on bloom-content1' lang='en'>
						<p>This is a test.</p>
					</div>
				</div>
			</div>
		</div>
	</div>
</body></html>");

			HandleImageDescriptions(htmlDom);

			var divList = htmlDom.SafeSelectNodes("//div[@class='marginBox']/div");
			Assert.That(divList, Is.Not.Null);
			Assert.That(divList.Count, Is.EqualTo(1));
			var divImage = divList[0] as System.Xml.XmlElement;
			Assert.That(divImage.GetAttribute("class"), Is.EqualTo("bloom-imageContainer"));
			var imgNodes = divImage.SafeSelectNodes("./*");
			Assert.That(imgNodes, Is.Not.Null);
			Assert.That(imgNodes.Count, Is.EqualTo(2));

			var img = imgNodes[0] as System.Xml.XmlElement;
			Assert.That(img.LocalName, Is.EqualTo("img"));
			Assert.That(img.GetAttribute("alt"), Is.EqualTo("This is a test."));

			var div = imgNodes[1] as System.Xml.XmlElement;
			Assert.That(div.LocalName, Is.EqualTo("div"));
			Assert.That(div.GetAttribute("class"), Is.EqualTo("bloom-translationGroup bloom-imageDescription"));
			var divDescList = div.SafeSelectNodes("./*");
			Assert.That(divDescList, Is.Not.Null);
			Assert.That(divDescList.Count, Is.EqualTo(1));
			var divDesc = divDescList[0] as System.Xml.XmlElement;
			Assert.That(divDesc.LocalName, Is.EqualTo("div"));
			Assert.That(divDesc.GetAttribute("lang"), Is.EqualTo("en"));
			var paraNodes = divDesc.SafeSelectNodes("./*");
			Assert.That(paraNodes, Is.Not.Null);
			Assert.That(paraNodes.Count, Is.EqualTo(1));
			var para = paraNodes[0] as System.Xml.XmlElement;
			Assert.That(para.LocalName, Is.EqualTo("p"));
			Assert.That(para.InnerText, Is.EqualTo("This is a test."));
			Assert.That(para.InnerXml, Is.EqualTo("This is a test."));
		}

		private void HandleImageDescriptions(HtmlDom htmlDom, BookInfo.HowToPublishImageDescriptions howTo = BookInfo.HowToPublishImageDescriptions.None)
		{
			var obj = new PrivateObject(new Bloom.Publish.Epub.EpubMaker(null, null));
			obj.SetFieldOrProperty("PublishImageDescriptions", howTo);
			obj.Invoke("HandleImageDescriptions", htmlDom);
		}
	}
}
