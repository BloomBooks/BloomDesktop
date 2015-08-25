using System;
using System.IO;
using System.Linq;
using System.Xml;
using Bloom;
using NUnit.Framework;
using Palaso.IO;

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
			Assert.AreEqual("<html><head><title></title></head><body></body></html>",dom.OuterXml.Replace("\r\n", ""));
		}
		[Test]
		public void GetXmlDomFromHtml_HasOpenLinkElement_Closes()
		{
			var dom = XmlHtmlConverter.GetXmlDomFromHtml("<!DOCTYPE html><html><head>    <link rel='stylesheet' href='basePage.css' type='text/css'> </head></html>", false);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//html", 1);//makes sure no namespace was inserted (or does it? what if that assert is too smart))
			Assert.AreEqual("<html><head><link rel=\"stylesheet\" href=\"basePage.css\" type=\"text/css\" /><title></title></head><body></body></html>", dom.OuterXml.Replace("\r\n", ""));
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

		/// <summary>
		/// I put this test in because (while working on BL-1222) I came across some alleged HTML that contained
		/// u elements closed in the XML way (closing slash, instead of separate close tag) which is not allowed
		/// in HTML 5. However, I didn't have to change the code to make it pass; both kinds of empty u/i/b element
		/// are removed entirely. Still, just in case that changes, this makes sure it doesn't change to something
		/// invalid.
		/// </summary>
		[Test]
		public void SaveAsHTML_EmptyUbi_Removed()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<html><body><div data-book='test'/>Text with <u></u> and <b></b> and <i></i> works</body></html>");
			using (var temp = new TempFile())
			{
				XmlHtmlConverter.SaveDOMAsHtml5(dom, temp.Path);
				var text = File.ReadAllText(temp.Path);
				Assert.That(text, Is.Not.StringContaining("<u />"));
				Assert.That(text, Is.Not.StringContaining("<b />"));
				Assert.That(text, Is.Not.StringContaining("<i />"));
				Assert.That(text, Is.Not.StringContaining("<b></b>"));
				Assert.That(text, Is.Not.StringContaining("<u></u>"));
				Assert.That(text, Is.Not.StringContaining("<i></i>"));
			}
		}

		[Test]
		public void SaveAsHTML_MinimalUbiSpan_DoesNotContractOrRemove()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<html><body><div data-book='test'/>Text with <u /><u attr=\"1\" /> and <b/><b attr=\"1\" /><span /><span attr=\"1\" /> and <i /><i attr=\"1\" /> works</body></html>");
			using (var temp = new TempFile())
			{
				XmlHtmlConverter.SaveDOMAsHtml5(dom, temp.Path);
				var text = File.ReadAllText(temp.Path);
				Assert.That(text, Is.Not.StringContaining("<u />"));
				Assert.That(text, Is.Not.StringContaining("<b />"));
				Assert.That(text, Is.Not.StringContaining("<i />"));
				Assert.That(text, Is.Not.StringContaining("<span />"));
				Assert.That(text, Is.Not.StringContaining("<b></b>"));
				Assert.That(text, Is.Not.StringContaining("<u></u>"));
				Assert.That(text, Is.Not.StringContaining("<i></i>"));
				Assert.That(text, Is.Not.StringContaining("<span></span>"));
				Assert.That(text, Is.StringContaining("<b attr=\"1\"></b>"));
				Assert.That(text, Is.StringContaining("<u attr=\"1\"></u>"));
				Assert.That(text, Is.StringContaining("<i attr=\"1\"></i>"));
				Assert.That(text, Is.StringContaining("<span attr=\"1\"></span>"));
			}
		}

		//relates to BL-1242
		[Test]
		public void SaveAsHTML_EmptySpan_DoesNotRemove()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<html><body><p>first line <span class='bloom-linebreak'></span>second line</p></body></html>");
			using(var temp = new TempFile())
			{
				XmlHtmlConverter.SaveDOMAsHtml5(dom, temp.Path);
				var text = File.ReadAllText(temp.Path);
				Assert.That(text, Is.StringContaining("<span class=\"bloom-linebreak\"></span>"));
			}
		}

		[Test]
		public void GetXmlDomFromHtml_HasBrTags_TagsNotDoubled()
		{
			const string html = "<!DOCTYPE html><html><head></head><body><div><br></br></div></body></html>";
			var dom = XmlHtmlConverter.GetXmlDomFromHtml(html, false);
			var found = 0;
			if (dom.DocumentElement != null)
			{
				var xml = dom.DocumentElement.InnerXml;
				found = xml.Select((c, i) => xml.Substring(i)).Count(sub => sub.StartsWith("<br />"));
			}
			Assert.AreEqual(1, found);
		}

		/// <summary>
		/// We don't particularly want it to convert newlines to spaces, as this test demonstrates that it does.
		/// However, that is harmless and unlikely to occur (we're not counting on newlines for formatting)
		/// so I haven't tried to prevent it.
		/// I do want to test that an existing newline is not simply removed, leaving no white space.
		/// </summary>
		[Test]
		public void GetXmlDomFromHtml_HasBandItags_NoExtraNewlinesBefore()
		{
			const string html = @"<!DOCTYPE html><html><head></head><body><div>one<b>two</b>three<i>four</i>five
<b>six</b>seven <i>eight</i>nine</div></body></html>";
			var dom = XmlHtmlConverter.GetXmlDomFromHtml(html, false);
			Assert.That(dom.InnerXml, Is.StringContaining(@"one<b>two</b>three<i>four</i>five <b>six</b>seven <i>eight</i>nine"));
		}

		[Test]
		public void GetXmlDomFromHtml_HasEmptyTags_RemoveTags()
		{
			var html = "<!DOCTYPE html><html><head></head><body><div><u> </u></div></body></html>";
			var dom = XmlHtmlConverter.GetXmlDomFromHtml(html);
			var xml = dom.DocumentElement.GetElementsByTagName("body")[0].InnerXml.Replace("\r\n", "");
			;
			Assert.AreEqual("<div></div>", xml);
		}

		[Test]
		public void GetXmlDomFromHtml_HasNestedEmptyTags_RemoveTags()
		{
			var html = "<!DOCTYPE html><html><head></head><body><div><u><i /></u></div></body></html>";
			var dom = XmlHtmlConverter.GetXmlDomFromHtml(html);
			var xml = dom.DocumentElement.GetElementsByTagName("body")[0].InnerXml.Replace("\r\n", "");
			;
			Assert.AreEqual("<div></div>", xml);
		}

		[Test]
		public void GetXmlDomFromHtml_HasEmptyTagsWithAttributes_NoRemoveTags()
		{
			var html = "<!DOCTYPE html><html><head></head><body><div><u style=\"test\"> </u></div></body></html>";
			var dom = XmlHtmlConverter.GetXmlDomFromHtml(html);
			var xml = dom.DocumentElement.GetElementsByTagName("body")[0].InnerXml.Replace("\r\n","");
			Assert.AreEqual("<div><u style=\"test\"></u></div>", xml);

			html = "<!DOCTYPE html><html><head></head><body><div><u><i style=\"test\" /></u></div></body></html>";
			dom = XmlHtmlConverter.GetXmlDomFromHtml(html);
			xml = dom.DocumentElement.GetElementsByTagName("body")[0].InnerXml.Replace("\r\n", "");
			;
			Assert.AreEqual("<div><u><i style=\"test\"></i></u></div>", xml);
		}
	}
}
