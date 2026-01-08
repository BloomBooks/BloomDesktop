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
        public void CreateHtmlString_CreatesDocStructure()
        {
            var bodyContent = "<div>foo</div>";
            var bodyContentWithNoWhitespace = Regex.Replace(bodyContent, @"\s+", "");
            string doc1 = XmlHtmlConverter.CreateHtmlString(bodyContent);
            var doc1WithNoWhitespace = Regex.Replace(doc1, @"\s+", "");
            Assert.That(
                doc1WithNoWhitespace,
                Is.EqualTo(
                    $"<html><head><title></title></head><body>{bodyContentWithNoWhitespace}</body></html>"
                )
            );

            var headContent = "<meta charset='utf-8'>";
            var headContentWithNoWhitespace = Regex.Replace(headContent, @"\s+", "");
            string doc2 = XmlHtmlConverter.CreateHtmlString(bodyContent, headContent);
            var doc2WithNoWhitespace = Regex.Replace(doc2, @"\s+", "");

            Assert.That(
                doc2WithNoWhitespace,
                Is.EqualTo(
                    $"<html><head>{headContentWithNoWhitespace}</head><body>{bodyContentWithNoWhitespace}</body></html>"
                )
            );
        }

        [Test]
        public void GetXmlDomFromHtml_HasOpenLinkElement_Closes()
        {
            var doc = XmlHtmlConverter.CreateHtmlString(
                "<link rel='stylesheet' href='basePage.css' type='text/css'>"
            );
            var dom = XmlHtmlConverter.GetXmlDomFromHtml(doc, false);
            AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//html", 1); //makes sure no namespace was inserted (or does it? what if that assert is too smart))
            var xml = dom.OuterXml;
            Assert.That(
                xml,
                Does.Contain("<link rel=\"stylesheet\" href=\"basePage.css\" type=\"text/css\" />")
            );
            Assert.That(
                xml,
                Does.Not.Contain(
                    "<link rel=\"stylesheet\" href=\"basePage.css\" type=\"text/css\">"
                )
            );
        }

        [Test]
        public void SaveAsHTML_HasXHTMLSelfClosingDiv_ChangesToHTMLStandard()
        {
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(XmlHtmlConverter.CreateHtmlString("<div data-book='test'/>"));
            using (var temp = new TempFile())
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
        public void SaveAsHTML_EmptyUbi_DoNotResultInSelfClosing()
        {
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(
                XmlHtmlConverter.CreateHtmlString(
                    "<div data-book='test'/>Text with <u></u> and <b></b> and <i></i> works"
                )
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
            }
        }

        [Test]
        public void SaveAsHTML_MinimalUbiSpan_DoesNotContractOrRemove()
        {
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(
                XmlHtmlConverter.CreateHtmlString(
                    "<div data-book='test'/>Text with <u /><u attr=\"1\" /> and <b/><b attr=\"1\" /><span /><span attr=\"1\" /> and <i /><i attr=\"1\" />  <em /><em attr=\"1\" />  <strong /><strong attr=\"1\" /> works"
                )
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
            dom.LoadXml(XmlHtmlConverter.CreateHtmlString("<div data-book='test'/>" + original));
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
                XmlHtmlConverter.CreateHtmlString(
                    "<p>first line <span class='bloom-linebreak'></span>second line</p>"
                )
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
            dom.LoadXml(XmlHtmlConverter.CreateHtmlString(pattern));
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
            dom.LoadXml(XmlHtmlConverter.CreateHtmlString(pattern));
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

        [TestCase("area")]
        [TestCase("img")]
        [TestCase("br")]
        [TestCase("hr")]
        [TestCase("link")]
        [TestCase("input")]
        [TestCase("source")]
        [TestCase("wbr")]
        public void SaveAsHtml_VoidElements_ConvertsToSelfClosing(string tag)
        {
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(XmlHtmlConverter.CreateHtmlString($"<{tag} data-foo='bar'></{tag}>"));
            using (var temp = new TempFile())
            {
                XmlHtmlConverter.SaveDOMAsHtml5(dom, temp.Path);
                var text = File.ReadAllText(temp.Path);
                Assert.That(text, Does.Contain($"<{tag} data-foo=\"bar\" />"));
            }
        }

        [Test]
        public void SaveAsHtml_NonVoidElement_DoesNotConvert()
        {
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(XmlHtmlConverter.CreateHtmlString($"<span data-foo='bar'></span>"));
            using (var temp = new TempFile())
            {
                XmlHtmlConverter.SaveDOMAsHtml5(dom, temp.Path);
                var text = File.ReadAllText(temp.Path);
                Assert.That(text, Does.Contain($"<span data-foo=\"bar\"></span>"));
                Assert.That(text, Does.Not.Contain($"<span data-foo=\"bar\" />"));
            }
        }

        [Test]
        public void SaveAsHtml_HasSvgs_DoesNotChoke()
        {
            string svg =
                @"<svg xmlns=""http://www.w3.org/2000/svg""><path d=""M 0 0 L 10 10"" /></svg>";
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(XmlHtmlConverter.CreateHtmlString($"{svg}<div>foobar</div>"));
            using (var temp = new TempFile())
            {
                XmlHtmlConverter.SaveDOMAsHtml5(dom, temp.Path);
                var text = File.ReadAllText(temp.Path);
                Assert.That(text, Does.Contain("<svg"));
                Assert.That(text, Does.Contain("foobar"));
            }
        }

        [Test]
        public void GetXmlDomFromHtml_HasSvgs_DoesNotChoke()
        {
            string svg =
                @"<svg xmlns=""http://www.w3.org/2000/svg""><path d=""M 0 0 L 10 10"" /></svg>";
            string html = XmlHtmlConverter.CreateHtmlString($"{svg}<div>foobar</div>");
            var dom = XmlHtmlConverter.GetXmlDomFromHtml(html, false);
            Assert.That(dom.InnerXml, Does.Contain("<svg"));
            Assert.That(dom.InnerXml, Does.Contain("foobar"));
        }

        [Test]
        public void GetXmlDomFromHtml_HasBrTags_TagsNotDoubled()
        {
            var html = XmlHtmlConverter.CreateHtmlString("<div><br></br></div>");
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
            dom.LoadXml(XmlHtmlConverter.CreateHtmlString("<br />"));
            using (var temp = new TempFile())
            {
                XmlHtmlConverter.SaveDOMAsHtml5(dom, temp.Path);
                var text = File.ReadAllText(temp.Path);
                Assert.That(text, Does.Contain("<br"));
                Assert.That(text, Does.Not.Contain("</br>"));
            }
        }

        /// <summary>
        /// Existing newlines are not simply removed leaving no white space, though they may be replaced with a space.
        /// We're not relying on them for formatting.
        /// </summary>
        [Test]
        public void GetXmlDomFromHtml_HasBandItags_WhitespaceNotDisappearing()
        {
            var html = XmlHtmlConverter.CreateHtmlString(
                @"<div>one<b>two</b>three<i>four</i>five
<b>six</b>seven <i>eight</i>nine</div>"
            );
            var dom = XmlHtmlConverter.GetXmlDomFromHtml(html, false);
            Assert.That(
                dom.InnerXml,
                Does.Match(@"one<b>two</b>three<i>four</i>five\s+<b>six</b>seven <i>eight</i>nine")
            );
        }

        [Test]
        public void GetXmlDomFromHtml_HasDirectFormatting_PreserveSpacesBetween()
        {
            var html = XmlHtmlConverter.CreateHtmlString(
                @"<div><p>one <b>two</b> <i>three</i> <strong>four</strong> <em>five</em> <u>six</u> seven</p></div>"
            );
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
            var html = XmlHtmlConverter.CreateHtmlString(
                "<div><p>one <span class=\"x\">two</span> <span class=\"y\">three</span> <span class =\"z\">four</span> five</p></div>"
            );
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
            var html = XmlHtmlConverter.CreateHtmlString(
                "<div><p>This is a<strong> bold </strong>sentence with a<span style=\"color:red\"> red </span>word</p></div>"
            );
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
            var html = XmlHtmlConverter.CreateHtmlString(pattern);
            var dom = XmlHtmlConverter.GetXmlDomFromHtml(html, false);
            AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//p", 5);
        }

        [Test]
        public void GetXmlDomFromHtml_HasSpaceOnlyTags_KeepTags()
        {
            var html = XmlHtmlConverter.CreateHtmlString("<div><u> </u></div>");
            var dom = XmlHtmlConverter.GetXmlDomFromHtml(html);
            var xml = dom.DocumentElement.GetElementsByTagName("body")[0].InnerXml;
            Assert.AreEqual("<div><u> </u></div>", xml.Trim());
        }

        [Test]
        public void GetXmlDomFromHtml_HasEmptyTagsWithAttributes_NoRemoveTags()
        {
            var html = XmlHtmlConverter.CreateHtmlString("<div><u style=\"test\"> </u></div>");
            var dom = XmlHtmlConverter.GetXmlDomFromHtml(html);
            var xml = dom.DocumentElement.GetElementsByTagName("body")[0].InnerXml;
            Assert.AreEqual("<div><u style=\"test\"> </u></div>", xml.Trim());

            html = XmlHtmlConverter.CreateHtmlString("<div><u><i style=\"test\" /></u></div>");
            dom = XmlHtmlConverter.GetXmlDomFromHtml(html);
            xml = dom.DocumentElement.GetElementsByTagName("body")[0].InnerXml;
            Assert.AreEqual("<div><u><i style=\"test\"></i></u></div>", xml.Trim());
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
        public void GetXmlDomFromHtml_RemovesBlankLinesAtEndOfStyleBlocks()
        {
            var styleContent =
                @".BigWords-style { font-size: 45pt ! important; text-align: center ! important; }
.normal-style { text-align: initial ! important; }




";
            var html =
                @"<!DOCTYPE html><html><head><style>"
                + styleContent
                + "</style></head><body></body></html>";
            var dom = XmlHtmlConverter.GetXmlDomFromHtml(html);
            var xml = dom.DocumentElement.GetElementsByTagName("style")[0].InnerXml;
            Assert.That(xml, Is.EqualTo(styleContent.Trim()));
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

        [Test]
        public void GetXmlDomFromHtml_HasXmlDeclaration_RespectsIncludeXmlDeclaration()
        {
            var html = XmlHtmlConverter.CreateHtmlString("<div data-book='test'/>");
            var htmlDomWithXmlDecl = XmlHtmlConverter.GetXmlDomFromHtml(html, true);
            var htmlDomWithoutXmlDecl = XmlHtmlConverter.GetXmlDomFromHtml(html);
            Assert.That(
                htmlDomWithXmlDecl.OuterXml,
                Does.StartWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>"),
                "The XML declaration should be included."
            );
            Assert.That(
                htmlDomWithoutXmlDecl.OuterXml,
                Does.Not.StartWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>"),
                "The XML declaration should not be included."
            );
        }

        [Test]
        public void GetXmlDomFromHtml_Deduplicates_attributes()
        {
            // Some unit tests, at least, depend on the converter fixing duplicate attributes. We don't know whether it matters which one of a pair of duplicates it keeps, but the current code keeps the second, so this test is written to make sure we at least notice if this changes.
            var html = XmlHtmlConverter.CreateHtmlString(
                "<div data-foo=\"bar\" data-baz=\"qux\" data-foo=\"bar2\"></div>"
            );
            var htmlDom = XmlHtmlConverter.GetXmlDomFromHtml(html, true);
            var xml = htmlDom.DocumentElement.InnerXml;
            Assert.That(
                xml,
                Does.Not.Contain("data-foo=\"bar\""),
                "The first of the duplicate attributes should not be present."
            );
            Assert.That(
                xml,
                Does.Contain("data-foo=\"bar2\""),
                "The second of the duplicate attributes should be present."
            );
            Assert.That(
                xml,
                Does.Contain("data-baz=\"qux\""),
                "The remaining attributes should remain present."
            );
        }

        [Test]
        public void GetXmlDomFromHtml_nbsp_handled()
        {
            var html = XmlHtmlConverter.CreateHtmlString(
                "<div>Some text with a&nbsp;non-breaking space.</div>"
            );
            var htmlDom = XmlHtmlConverter.GetXmlDomFromHtml(html, true);
            var xml = htmlDom.DocumentElement.InnerXml;
            var nbspIndex = xml.IndexOf("Some text with a") + "Some text with a".Length;
            Assert.That(
                xml[nbspIndex],
                Is.EqualTo('\u00A0'),
                "The non-breaking space should be preserved"
            );
        }

        [Test]
        public void GetXmlDomFromHtml_PreservesCommentsInMiddle()
        {
            var html =
                @"
<html>
<!-- This is a comment 1-->
<head>In the head<style>
<!-- This is a comment 2-->
</style></head>
<body>in the body</body>
<!-- This is a comment 3-->
</html>";
            var htmlDom = XmlHtmlConverter.GetXmlDomFromHtml(html);
            var xml = htmlDom.DocumentElement.OuterXml;
            Assert.That(xml, Does.Contain("<!-- This is a comment 1-->"));
            Assert.That(xml, Does.Contain("<!-- This is a comment 2-->"));
            Assert.That(xml, Does.Contain("<!-- This is a comment 3-->"));
        }

        [Test]
        public void GetXmlDomFromHtml_doesNotDoubleEncodeEntities()
        {
            var html = XmlHtmlConverter.CreateHtmlString(
                "<div>Ben &amp; Jerry</div><div>Buggy says &quot;hi&quot;</div>"
            );
            var htmlDom = XmlHtmlConverter.GetXmlDomFromHtml(html, true);
            var xml = htmlDom.DocumentElement.InnerXml;
            Assert.That(xml, Does.Contain("Ben &amp; Jerry"));
            Assert.That(xml, Does.Contain("Buggy says \"hi\"")); // decoded by this point for some reason
            Assert.That(
                xml,
                Does.Not.Contain("&amp;amp;"),
                "The &amp; entity should not be double-encoded."
            );
            Assert.That(
                xml,
                Does.Not.Contain("&amp;quot;"),
                "The &quot; entity should not be double-encoded."
            );
        }

        [Test]
        public void GetXmlDomFromHtml_ParsesStructureRegardlessOfComments()
        {
            var mainHtml =
                @"
<html>
<head>In the head</head>
<body>in the body</body>
</html>";
            var withDoctype =
                @"<!DOCTYPE html>
" + mainHtml;
            var withComment =
                @"
<!-- This is a comment -->
<!-- Another comment -->
" + mainHtml;
            var withMultilineComment =
                @"
<!-- This is a comment 
comment continues onto the next line
 and the next -->
" + mainHtml;
            var withDoctypeAndComment =
                @"<!DOCTYPE html>
<!-- This is a comment -->
" + mainHtml;
            var withCommentAndDoctype =
                @"<!-- This is a comment -->
<!DOCTYPE html>
" + mainHtml;
            var withBlankLines =
                @" <!DOCTYPE html>

<!-- This is a comment -->




" + mainHtml;
            foreach (
                var html in new[]
                {
                    mainHtml,
                    withDoctype,
                    withComment,
                    withMultilineComment,
                    withDoctypeAndComment,
                    withCommentAndDoctype,
                    withBlankLines,
                }
            )
            {
                var doc = XmlHtmlConverter.GetXmlDomFromHtml(html);
                Assert.That(
                    doc.Head.InnerXml.Trim(),
                    Is.EqualTo("In the head"),
                    $"failed to properly parse html: ${html}"
                );
                Assert.That(
                    doc.Body.InnerXml,
                    Is.EqualTo("in the body"),
                    $"failed to properly parse html: ${html}"
                );
            }
        }
    }
}
