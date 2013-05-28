using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Bloom;
using Bloom.Book;
using NUnit.Framework;
using Palaso.IO;
using Palaso.TestUtilities;

namespace BloomTests
{
	[TestFixture]
	public class XmlHtmlConverterTests
	{
		[Test]
		public void GetXmlDomFromHtml_MinimalWellFormedHtml5()
		{

			var dom = XmlHtmlConverter.GetXmlDomFromHtml("<!DOCTYPE html><html></html>", false);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//html", 1);//makes sure no namespace was inserted (or does it? what if that assert is too smart))
			Assert.AreEqual("<html><head><title></title></head><body></body></html>",dom.OuterXml);
		}
		[Test]
		public void GetXmlDomFromHtml_HasOpenLinkElement_Closes()
		{
			var dom = XmlHtmlConverter.GetXmlDomFromHtml("<!DOCTYPE html><html><head>    <link rel='stylesheet' href='basePage.css' type='text/css'> </head></html>", false);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//html", 1);//makes sure no namespace was inserted (or does it? what if that assert is too smart))
			Assert.AreEqual("<html><head><link rel=\"stylesheet\" href=\"basePage.css\" type=\"text/css\" /><title></title></head><body></body></html>", dom.OuterXml);
		}
		[Test]
		public void GetXmlDomFromHtml_HasErrors_ReportsError()
		{
			Assert.Throws<ApplicationException>(() => XmlHtmlConverter.GetXmlDomFromHtml("<!DOCTYPE html><html><head>    <blahblah> </head></html>", false));
		}

		[Test]
		public void SaveAsHTML_HasXHTMLSelfClosingDiv_ChangesToHTMLStandard()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<html><body><div data-book='test'/></body></html>");
			using(var temp = new TempFile())
			{
				XmlHtmlConverter.SaveDOMAsHtml5(dom, temp.Path);
				var text = File.ReadAllText(temp.Path);
				Assert.IsTrue(text.Contains("</div>"), text);
			}
		}
	}
}
