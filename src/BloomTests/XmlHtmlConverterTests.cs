using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Bloom;
using Bloom.SafeXml;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests
{
    [TestFixture]
    public class XmlHtmlConverterTests
    {
        [Test]
        public void GetXmlDomFromHtml_MinimalWellFormedHtml5()
        {
            var dom = XmlHtmlConverter.GetXmlDomFromHtml("<!DOCTYPE html><html></html>", false);
            AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//html", 1); //makes sure no namespace was inserted (or does it? what if that assert is too smart))
            // The PreserveWhitespace setting appears to insert newlines that we don't care about.
            var xml = dom.OuterXml;
            xml = xml.Replace(Environment.NewLine, "");
            Assert.AreEqual("<html><head><title></title></head><body></body></html>", xml);
        }

        [Test]
        public void GetXmlDomFromHtml_HasOpenLinkElement_Closes()
        {
            var dom = XmlHtmlConverter.GetXmlDomFromHtml(
                "<!DOCTYPE html><html><head>    <link rel='stylesheet' href='basePage.css' type='text/css'> </head></html>",
                false
            );
            AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//html", 1); //makes sure no namespace was inserted (or does it? what if that assert is too smart))
            // The PreserveWhitespace setting appears to insert newlines that we don't care about.
            var xml = dom.OuterXml;
            xml = xml.Replace(Environment.NewLine, "");
            Assert.AreEqual(
                "<html><head><link rel=\"stylesheet\" href=\"basePage.css\" type=\"text/css\" /><title></title></head><body></body></html>",
                xml
            );
        }

        [Test]
        public void GetXmlDomFromHtml_HasErrors_ReportsError()
        {
            Assert.Throws<ApplicationException>(
                () =>
                    XmlHtmlConverter.GetXmlDomFromHtml(
                        "<!DOCTYPE html><html><head>    <blahblah> </head></html>",
                        false
                    )
            );
        }

        [Test]
        public void SaveAsHTML_HasXHTMLSelfClosingDiv_ChangesToHTMLStandard()
        {
            var dom = SafeXmlDocument.Create();
            dom.LoadXml("<html><body><div data-book='test'/></body></html>");
            using (var temp = new TempFile())
            {
                XmlHtmlConverter.SaveDOMAsHtml5(dom, temp.Path);
                var text = File.ReadAllText(temp.Path);
                Assert.IsTrue(text.Contains("</div>"), text);
            }
        }

        [TestCase("abc<svg whatever> some content</svg> something $$ else")]
        [TestCase("abc<svg whatever> some content</svg> something $$ <svg 2>second svg</svg>else")]
        public void RemoveRestoreSvgs(string input)
        {
            var svgs = new List<string>();
            var intermediate = XmlHtmlConverter.RemoveSvgs(input, svgs);
            var adapted = intermediate.Replace("$$", "Something changed");
            Assert.That(intermediate, Does.Not.Contain("<svg"));
            Assert.That(intermediate, Does.Not.Contain("</svg"));
            Assert.That(intermediate, Does.Not.Contain("some content")); // checks at least once that the body of the svg is gone
            var output = XmlHtmlConverter.RestoreSvgs(adapted, svgs);
            var expectedOutput = input.Replace("$$", "Something changed");
            Assert.That(output, Is.EqualTo(expectedOutput));
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
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(
                "<html><body><div data-book='test'/>Text with <u></u> and <b></b> and <i></i> works</body></html>"
            );
            using (var temp = new TempFile())
            {
                XmlHtmlConverter.SaveDOMAsHtml5(dom, temp.Path);
                var text = File.ReadAllText(temp.Path);
                Assert.That(text, Does.Not.Contain("<u />"));
                Assert.That(text, Does.Not.Contain("<b />"));
                Assert.That(text, Does.Not.Contain("<i />"));
                Assert.That(text, Does.Not.Contain("<em />"));
                Assert.That(text, Does.Not.Contain("<strong />"));
                Assert.That(text, Does.Not.Contain("<i></i>"));
                Assert.That(text, Does.Not.Contain("<b></b>"));
                Assert.That(text, Does.Not.Contain("<u></u>"));
                Assert.That(text, Does.Not.Contain("<em></em>"));
                Assert.That(text, Does.Not.Contain("<strong></strong>"));
            }
        }

        [Test]
        public void SaveAsHTML_MinimalUbiSpan_DoesNotContractOrRemove()
        {
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(
                "<html><body><div data-book='test'/>Text with <u /><u attr=\"1\" /> and <b/><b attr=\"1\" /><span /><span attr=\"1\" /> and <i /><i attr=\"1\" />  <em /><em attr=\"1\" />  <strong /><strong attr=\"1\" /> works</body></html>"
            );
            using (var temp = new TempFile())
            {
                XmlHtmlConverter.SaveDOMAsHtml5(dom, temp.Path);
                var text = File.ReadAllText(temp.Path);
                Assert.That(text, Does.Not.Contain("<u />"));
                Assert.That(text, Does.Not.Contain("<b />"));
                Assert.That(text, Does.Not.Contain("<i />"));
                Assert.That(text, Does.Not.Contain("<em />"));
                Assert.That(text, Does.Not.Contain("<strong />"));
                Assert.That(text, Does.Not.Contain("<span />"));
                Assert.That(text, Does.Not.Contain("<b></b>"));
                Assert.That(text, Does.Not.Contain("<u></u>"));
                Assert.That(text, Does.Not.Contain("<i></i>"));
                Assert.That(text, Does.Not.Contain("<em></em>"));
                Assert.That(text, Does.Not.Contain("<strong></strong>"));
                Assert.That(text, Does.Not.Contain("<span></span>"));
                Assert.That(text, Does.Contain("<b attr=\"1\"></b>"));
                Assert.That(text, Does.Contain("<u attr=\"1\"></u>"));
                Assert.That(text, Does.Contain("<i attr=\"1\"></i>"));
                Assert.That(text, Does.Contain("<strong attr=\"1\"></strong>"));
                Assert.That(text, Does.Contain("<em attr=\"1\"></em>"));
                Assert.That(text, Does.Contain("<span attr=\"1\"></span>"));
            }
        }

        [Test, Ignore("Will fix in BL-2558")]
        public void SaveAsHTML_HasEmUpAgainstStrong_DoesNotInsertSpace()
        {
            var dom = SafeXmlDocument.Create();
            var original = "<p><em>one</em><strong>two</strong></p>";
            dom.LoadXml("<html><body><div data-book='test'/>" + original + "</body></html>");
            using (var temp = new TempFile())
            {
                XmlHtmlConverter.SaveDOMAsHtml5(dom, temp.Path);
                var text = File.ReadAllText(temp.Path);
                Assert.That(text, Does.Contain(original));
            }
        }

        //relates to BL-1242
        [Test]
        public void SaveAsHTML_EmptySpan_DoesNotRemove()
        {
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(
                "<html><body><p>first line <span class='bloom-linebreak'></span>second line</p></body></html>"
            );
            using (var temp = new TempFile())
            {
                XmlHtmlConverter.SaveDOMAsHtml5(dom, temp.Path);
                var text = File.ReadAllText(temp.Path);
                Assert.That(text, Does.Contain("<span class=\"bloom-linebreak\"></span>"));
            }
        }

        [Test]
        public void SaveAsHTM_HasEmptyParagraphsAndCites_RetainsThem()
        {
            var pattern =
                "<p></p><p></p><p>a</p><p></p><p>b</p><p/><cite></cite><cite /><cite data-book='originalTitle'></cite><cite data-book='originalTitle'/>";
            var dom = SafeXmlDocument.Create();
            dom.LoadXml("<!DOCTYPE html><html><body>" + pattern + "</body></html>");
            using (var temp = new TempFile())
            {
                XmlHtmlConverter.SaveDOMAsHtml5(dom, temp.Path);
                var r = new Regex("<p");
                var text = File.ReadAllText(temp.Path);
                var matches = r.Matches(text);
                Assert.AreEqual(6, matches.Count, text);
                Assert.AreEqual(4, new Regex("<cite").Matches(text).Count, text);
                //this one also exercises XmlHtmlConverter.GetXmlDomFromHtmlFile, so we're not really testing anymore
                var assertHtmlFile = AssertThatXmlIn.HtmlFile(temp.Path);
                assertHtmlFile.HasSpecifiedNumberOfMatchesForXpath("//p", 6);
                assertHtmlFile.HasSpecifiedNumberOfMatchesForXpath("//cite", 4);
            }
        }

        [Test]
        public void SaveDOMAsHtml5_DoesNotMessUpPaths()
        {
            var pattern = "<svg whatever='whatever'><path something='rubbish' /></svg>";
            var dom = SafeXmlDocument.Create();
            dom.LoadXml("<!DOCTYPE html><html><body>" + pattern + "</body></html>");
            using (var temp = new TempFile())
            {
                XmlHtmlConverter.SaveDOMAsHtml5(dom, temp.Path);
                // This is a regression test guarding against a problem where something we were trying
                // to do to <p whatever /> that converted it to <p whatever ></p> was also converting
                // <path whatever /> to <path whatever></p> (note the non-matching closing tag!)
                // That causes the parser to throw on invalid XML, so the main point here is just that we ended up
                // with valid XML. The further test that the <path> element survived is a bonus.
                AssertThatXmlIn
                    .HtmlFile(temp.Path)
                    .HasSpecifiedNumberOfMatchesForXpath("//path", 1);
            }
        }

        [Test]
        public void GetXmlDomFromHtml_HasBrTags_TagsNotDoubled()
        {
            const string html =
                "<!DOCTYPE html><html><head></head><body><div><br></br></div></body></html>";
            var dom = XmlHtmlConverter.GetXmlDomFromHtml(html, false);
            var found = 0;
            if (dom.DocumentElement != null)
            {
                var xml = dom.DocumentElement.InnerXml;
                found = xml.Select((c, i) => xml.Substring(i))
                    .Count(sub => sub.StartsWith("<br />"));
            }
            Assert.AreEqual(1, found);
        }

        [Test]
        public void SaveDOMAsHtml5_SavesBrCorrectly()
        {
            var dom = SafeXmlDocument.Create();
            dom.LoadXml("<html><body><br /></body></html>");
            using (var temp = new TempFile())
            {
                XmlHtmlConverter.SaveDOMAsHtml5(dom, temp.Path);
                var text = File.ReadAllText(temp.Path);
                Assert.That(text, Does.Contain("<br />"));
                Assert.That(text, Does.Not.Contain("</br>"));
            }
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
            const string html =
                @"<!DOCTYPE html><html><head></head><body><div>one<b>two</b>three<i>four</i>five
<b>six</b>seven <i>eight</i>nine</div></body></html>";
            var dom = XmlHtmlConverter.GetXmlDomFromHtml(html, false);
            Assert.That(
                dom.InnerXml,
                Does.Contain(@"one<b>two</b>three<i>four</i>five <b>six</b>seven <i>eight</i>nine")
            );
        }

        [Test]
        public void GetXmlDomFromHtml_HasDirectFormatting_PreserveSpacesBetween()
        {
            const string html =
                @"<!DOCTYPE html><html><head></head><body><div><p>one <b>two</b> <i>three</i> <strong>four</strong> <em>five</em> <u>six</u> seven</p></div></body></html>";
            var dom = XmlHtmlConverter.GetXmlDomFromHtml(html, false);
            Assert.That(
                dom.InnerXml,
                Does.Contain(
                    @"<p>one <b>two</b> <i>three</i> <strong>four</strong> <em>five</em> <u>six</u> seven</p>"
                )
            );
        }

        [Test]
        public void GetXmlDomFromHtml_HasSpans_PreserveSpacesBetween()
        {
            const string html =
                "<!DOCTYPE html><html><head></head><body><div><p>one <span class=\"x\">two</span> <span class=\"y\">three</span> <span class =\"z\">four</span> five</p></div></body></html>";
            var dom = XmlHtmlConverter.GetXmlDomFromHtml(html, false);
            Assert.That(
                dom.InnerXml,
                Does.Contain(
                    "<p>one <span class=\"x\">two</span> <span class=\"y\">three</span> <span class=\"z\">four</span> five</p>"
                )
            );
        }

        [Test]
        public void GetXmlDomFromHtml_PreservesSpaceAtElementBoundaries()
        {
            const string html =
                "<!DOCTYPE html><html><head></head><body><div><p>This is a<strong> bold </strong>sentence with a<span style=\"color:red\"> red </span>word</p></div></body></html>";
            var dom = XmlHtmlConverter.GetXmlDomFromHtml(html, false);
            Assert.That(
                dom.InnerXml,
                Does.Contain(
                    "<p>This is a<strong> bold </strong>sentence with a<span style=\"color:red\"> red </span>word</p>"
                )
            );
        }

        [Test]
        public void GetXmlDomFromHtml_HasEmptyParagraphs_RetainsEmptyParagraphs()
        {
            var pattern = "<p></p><p></p><p>a</p><p></p><p>b</p>";
            var html = "<!DOCTYPE html><html><body>" + pattern + "</body></html>";
            var dom = XmlHtmlConverter.GetXmlDomFromHtml(html, false);
            AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//p", 5);
        }

        [Test]
        public void GetXmlDomFromHtml_HasEmptyTags_RemoveTags()
        {
            var html = "<!DOCTYPE html><html><head></head><body><div><u></u></div></body></html>";
            var dom = XmlHtmlConverter.GetXmlDomFromHtml(html);
            var xml = dom.DocumentElement.GetElementsByTagName("body")[0].InnerXml;
            // The PreserveWhitespace setting appears to insert newlines that we don't care about.
            xml = xml.Replace(Environment.NewLine, "");
            Assert.AreEqual("<div></div>", xml);
        }

        [Test]
        public void GetXmlDomFromHtml_HasSpaceOnlyTags_KeepTags()
        {
            var html = "<!DOCTYPE html><html><head></head><body><div><u> </u></div></body></html>";
            var dom = XmlHtmlConverter.GetXmlDomFromHtml(html);
            var xml = dom.DocumentElement.GetElementsByTagName("body")[0].InnerXml;
            // The PreserveWhitespace setting appears to insert newlines that we don't care about.
            xml = xml.Replace(Environment.NewLine, "");
            Assert.AreEqual("<div><u> </u></div>", xml);
        }

        [Test]
        public void GetXmlDomFromHtml_HasNestedEmptyTags_RemoveTags()
        {
            var html =
                "<!DOCTYPE html><html><head></head><body><div><u><i /></u></div></body></html>";
            var dom = XmlHtmlConverter.GetXmlDomFromHtml(html);
            var xml = dom.DocumentElement.GetElementsByTagName("body")[0].InnerXml;
            // The PreserveWhitespace setting appears to insert newlines that we don't care about.
            xml = xml.Replace(Environment.NewLine, "");
            Assert.AreEqual("<div></div>", xml);
        }

        [Test]
        public void GetXmlDomFromHtml_HasEmptyTagsWithAttributes_NoRemoveTags()
        {
            var html =
                "<!DOCTYPE html><html><head></head><body><div><u style=\"test\"> </u></div></body></html>";
            var dom = XmlHtmlConverter.GetXmlDomFromHtml(html);
            var xml = dom.DocumentElement.GetElementsByTagName("body")[0].InnerXml;
            // The PreserveWhitespace setting appears to insert newlines that we don't care about.
            xml = xml.Replace(Environment.NewLine, "");
            Assert.AreEqual("<div><u style=\"test\"> </u></div>", xml);

            html =
                "<!DOCTYPE html><html><head></head><body><div><u><i style=\"test\" /></u></div></body></html>";
            dom = XmlHtmlConverter.GetXmlDomFromHtml(html);
            xml = dom.DocumentElement.GetElementsByTagName("body")[0].InnerXml;
            xml = xml.Replace(Environment.NewLine, "");
            // The PreserveWhitespace setting appears to insert newlines that we don't care about.
            Assert.AreEqual("<div><u><i style=\"test\"></i></u></div>", xml);
        }

        [Test]
        public void GetXmlDomFromHtml_HasProtectedGtInStylesheet_DoesNotConvert()
        {
            var styleContent =
                @"
/*<![CDATA[*/
.BigWords-style { font-size: 45pt ! important; text-align: center ! important; }
.normal-style { text-align: initial ! important; }
.normal-style > p { text-indent: -20pt ! important; margin-left: 20pt ! important; margin-bottom: 1em ! important; }
/*]]>*/
";
            var html =
                @"<!DOCTYPE html><html><head><style>"
                + styleContent
                + "</style></head><body></body></html>";
            var dom = XmlHtmlConverter.GetXmlDomFromHtml(html);
            var xml = dom.DocumentElement.GetElementsByTagName("style")[0].InnerXml;
            // Trim because it may mess with leading or trailing white space in ways we don't care about.
            Assert.That(xml.Trim(), Is.EqualTo(styleContent.Trim()));
        }

        // I don't know of a use case where we want tidy to convert an unprotected > into an entity.
        // There are very few places where these characters can occur unprotected in HTML.
        // However, trying to parse something as XHTML that has them will fail, making the document
        // unusable. So it seems best to let Tidy make its best effort to fix anything that
        // isn't specifically marked up as being in a block of CDATA.
        // This test confirms that we aren't interfering with Tidy's behavior except in
        // the one special case we care about.
        [Test]
        public void GetXmlDomFromHtml_HasUnProtectedGtInStylesheet_Converts()
        {
            var styleContent =
                @"
.BigWords-style { font-size: 45pt ! important; text-align: center ! important; }
.normal-style { text-align: initial ! important; }
.normal-style > p { text-indent: -20pt ! important; margin-left: 20pt ! important; margin-bottom: 1em ! important; }
";
            var html =
                @"<!DOCTYPE html><html><head><style>"
                + styleContent
                + "</style></head><body></body></html>";
            var dom = XmlHtmlConverter.GetXmlDomFromHtml(html);
            var xml = dom.DocumentElement.GetElementsByTagName("style")[0].InnerXml;
            // Trim because it may mess with leading or trailing white space in ways we don't care about.
            Assert.That(xml.Trim(), Is.EqualTo(styleContent.Replace(">", "&gt;").Trim()));
        }

        [Test]
        public void GetXmlDomFromHtml_RemovesBOMs()
        {
            var bookHtml =
                "\uFEFF"
                + @"<html><head>
						<link rel='stylesheet' href='Basic Book.css' type='text/css'></link>
						<link rel='stylesheet' href='Traditional-XMatter.css' type='text/css'></link>
					</head><body>"
                + "\uFEFF"
                + @"
					<div class='bloom-page' id='guid1'></div>"
                + "\uFEFF"
                + @"
			</body></html>";
            var htmlDom = XmlHtmlConverter.GetXmlDomFromHtml(bookHtml);
            var outerXml = htmlDom.OuterXml;
            var idxBOM = outerXml.IndexOf('\uFEFF');
            Assert.That(idxBOM, Is.EqualTo(-1), "All BOMs are removed.");
        }
    }
}
