using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bloom.Book;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
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

			var obj = new PrivateObject(new Bloom.Publish.Epub.EpubMaker(null, null));
			obj.Invoke("HandleImageDescriptions", htmlDom);

			string expectedImageHtml = "<img class=\"licenseImage\" src=\"a.png\" alt=\"Image representing the license of this book\" role=\"presentation\" />";
			Assert.That(htmlDom.InnerXml, Is.EqualTo($"<html><body><div data-book=\"outside-back-cover-branding-bottom-html\">{expectedImageHtml}</div></body></html>"));
		}

		[Test]
		public void HandleImageDescriptions_BrandingAlt_Custom_Unchanged()
		{
			string inputImageHtml = "<img src=\"a.png\" alt=\"Logo of CLB\" />";
			var htmlDom = new HtmlDom($"<html><body><div data-book=\"outside-back-cover-branding-bottom-html\">{inputImageHtml}</div></body></html>");

			var obj = new PrivateObject(new Bloom.Publish.Epub.EpubMaker(null, null));
			obj.Invoke("HandleImageDescriptions", htmlDom);

			string expectedImageHtml = "<img src=\"a.png\" alt=\"Logo of CLB\" role=\"presentation\" />";
			Assert.That(htmlDom.InnerXml, Is.EqualTo($"<html><body><div data-book=\"outside-back-cover-branding-bottom-html\">{expectedImageHtml}</div></body></html>"));
		}

		[Test]
		public void HandleImageDescriptions_BrandingAlt_Placeholder_SetToDefault()
		{
			string inputImageHtml = "<img src=\"a.png\" alt=\"This picture, a.png, is missing or was loading too slowly.\" />";
			var htmlDom = new HtmlDom($"<html><body><div data-book=\"outside-back-cover-branding-bottom-html\">{inputImageHtml}</div></body></html>");

			var obj = new PrivateObject(new Bloom.Publish.Epub.EpubMaker(null, null));
			obj.Invoke("HandleImageDescriptions", htmlDom);

			string expectedImageHtml = "<img src=\"a.png\" alt=\"Logo of the book sponsors\" role=\"presentation\" />";
			Assert.That(htmlDom.InnerXml, Is.EqualTo($"<html><body><div data-book=\"outside-back-cover-branding-bottom-html\">{expectedImageHtml}</div></body></html>"));
		}

		[Test]
		public void HandleImageDescriptions_BrandingAlt_NotSet_SetToDefault()
		{
			string inputImageHtml = "<img src=\"a.png\" />";
			var htmlDom = new HtmlDom($"<html><body><div data-book=\"outside-back-cover-branding-bottom-html\">{inputImageHtml}</div></body></html>");

			var obj = new PrivateObject(new Bloom.Publish.Epub.EpubMaker(null, null));
			obj.Invoke("HandleImageDescriptions", htmlDom);

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

			var obj = new PrivateObject(new Bloom.Publish.Epub.EpubMaker(null, null));
			obj.Invoke("HandleImageDescriptions", htmlDom);

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

			var obj = new PrivateObject(new Bloom.Publish.Epub.EpubMaker(null, null));
			obj.Invoke("HandleImageDescriptions", htmlDom);

			var imgNodes = htmlDom.SafeSelectNodes("//img[@src='a.png']");
			Assert.That(imgNodes, Is.Not.Null);
			Assert.That(imgNodes.Count, Is.EqualTo(1));
			var img = imgNodes[0] as System.Xml.XmlElement;
			Assert.That(img, Is.Not.Null);
			Assert.That(img.Attributes.Count, Is.EqualTo(2));
			Assert.That(img.GetAttribute("role"), Is.EqualTo(""));
			Assert.That(img.GetAttribute("alt"), Is.EqualTo("This is a test."));
		}
	}
}
