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
	}
}
