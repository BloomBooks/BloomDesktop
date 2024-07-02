using Bloom.Book;
using Bloom.SafeXml;
using Bloom.Spreadsheet;
using Moq;
using NUnit.Framework;
using OfficeOpenXml;
using SIL.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;

namespace BloomTests.Spreadsheet
{
    /// <summary>
    /// This class tests roundtripping a book with formatted text to and from a spreadsheet.
    /// An earlier version of this class (pre-Dec 16, 2021) tested round tripping both
    /// with retainMarkup and without it, but as of BL-10764 we deliberately don't ever
    /// interpret HTML markup in the spreadsheet...if there is any we escape it so it
    /// ends up as literal text. Therefore round trips with retainMarkup true are no longer
    /// possible. This means that export with retainMarkup is not currently tested;
    /// I'm not worried about this as we will probably discard the capability, and there
    /// is no UI way to do it anyway, nor likely to be.
    /// </summary>
    public class SpreadsheetRoundtripTests
    {
        static SpreadsheetRoundtripTests()
        {
            // The package requires us to do this as a way of acknowledging that we
            // accept the terms of the NonCommercial license.
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        private const string roundtripTestBook =
            @"
<!DOCTYPE html>

<html>
<head>
</head>

<body data-l1=""es"" data-l2="""" data-l3="""">
	<div id=""bloomDataDiv"">
		<div data-book=""bookTitle"" lang=""en"" id=""idShouldGetKept"">
			<p><em>Pineapple</em></p>

            <p>Farm</p>

		</div>
        <div data-book=""topic"" lang=""en"">
            Health
		</div>
		<div data-book=""coverImage"" lang=""*"" src=""cover.png"" alt=""This picture, cover.png, is missing or was loading too slowly."">
			cover.png
		</div>
		<div data-book=""licenseImage"" lang= ""*"" >
			license.png
		</div>
		<div data-book=""outside-back-cover-branding-bottom-html"" lang=""*""><img class=""branding"" src=""BloomWithTaglineAgainstLight.svg"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img></div>
	</div>
    <div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-right bloom-monolingual"" data-page="""" id=""dc90dbe0-7584-4d9f-bc06-0e0326060054"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""1"" lang="""">
        <div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Basic Text &amp; Picture"" lang=""en"">
            Basic Text &amp; Picture
        </div>

        <div class=""pageDescription"" lang=""en""></div>

        <div class=""split-pane-component marginBox"" style="""">
            <div class=""split-pane horizontal-percent"" style=""min-height: 42px;"">
                <div class=""split-pane-component position-top"">
                    <div class=""split-pane-component-inner"" min-width=""60px 150px 250px"" min-height=""60px 150px 250px"">
                        <div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
                            <div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" id=""simpleFormattingTest"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""español"" lang=""es"" contenteditable=""true"">
                                <p>Once upon a time there was a very <strong>bold</strong> dog. This dog was <em>itchy</em>. It went <u>under</u> a tree. The tree had 10<sup>4</sup> leaves.</p>
                            </div>

                            <div class=""bloom-editable normal-style"" style="""" lang=""z"" contenteditable=""true"">
                                <p></p>
                            </div>

                            <div class=""bloom-editable normal-style bloom-contentNational1"" style="""" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""English"" lang=""en"" contenteditable=""true"">
                                <p></p>
                            </div>
                        </div>
                    </div>
                </div>

                <div class=""split-pane-divider horizontal-divider""></div>

                <div class=""split-pane-component position-bottom"">
                    <div class=""split-pane-component-inner"" min-width=""60px 150px 250px"" min-height=""60px 150px 250px"">
                        <div class=""bloom-translationGroup bloom-trailingElement"">
                            <div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" id=""nestedFormattingTest"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""español"" lang=""es"" contenteditable=""true"">
                                <p><strong>One day the dog went for a walk. She had a very pleasant walk. After her <u>walk,</u> <em><u>she</u> decided to take a nap.</em></strong></p>

                                <p><strong><em>The next day,</em> the dog decided to go for a swim. She swam in a lake.</strong></p>
                            </div>

                            <div class=""bloom-editable normal-style bloom-contentNational1"" style="""" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""English"" lang=""en"" contenteditable=""true"">
                                <p></p>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>

    <div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-left bloom-monolingual"" data-page="""" id=""703ed5fc-ef1e-4699-b151-a6a46c1059ef"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""2"" lang="""">
        <div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Basic Text &amp; Picture"" lang=""en"">
            Basic Text &amp; Picture
        </div>

        <div class=""pageDescription"" lang=""en""></div>

        <div class=""split-pane-component marginBox"" style="""">
            <div class=""split-pane horizontal-percent"" style=""min-height: 42px;"">
                <div class=""split-pane-component position-top"">
                    <div class=""split-pane-component-inner"" min-width=""60px 150px 250px"" min-height=""60px 150px 250px"">
                        <div class=""bloom-imageContainer bloom-leadingElement"" title=""Name: brain1.jpg Size: 68.62 kb Dots: 1100 x 880 For the current paper size: • The image container is 406 x 335 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ⚠ This image would print at 260 DPI. • An image with 1269 x 1047 dots would fill this container at 300 DPI.""><img src=""brain1.jpg"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img></div>
                    </div>
                </div>

                <div class=""split-pane-divider horizontal-divider""></div>

                <div class=""split-pane-component position-bottom"">
                    <div class=""split-pane-component-inner"" min-width=""60px 150px 250px"" min-height=""60px 150px 250px"">
                        <div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
                            <div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" id=""whitespaceTest"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""español"" lang=""es"" contenteditable=""true"">
                                <p></p>

                                <p>An empty paragraph comes before this one.</p>

                                <p></p>

                                <p></p>

                                <p>This sentence follows two empty paragraphs. It will be followed by a new line.<span class=""bloom-linebreak""></span>﻿This sentence is trailed by an empty paragraph.</p>

                                <p></p>
                            </div>

                            <div class=""bloom-editable normal-style"" style="""" lang=""z"" contenteditable=""true"">
                                <p></p>
                            </div>

                            <div class=""bloom-editable normal-style bloom-contentNational1"" style="""" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""English"" lang=""en"" contenteditable=""true"">
                                <p></p>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
	<div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-left bloom-monolingual"" data-page="""" id=""703ed5fc-ef1e-4699-b151-a6a46c1059ef"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""3"" lang="""">
        <div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Basic Text &amp; Picture"" lang=""en"">
            Basic Text &amp; Picture
        </div>

        <div class=""pageDescription"" lang=""en""></div>

        <div class=""split-pane-component marginBox"" style="""">

            <div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"" data-test-id=""tg1"">
                <div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""español"" lang=""es"" contenteditable=""true"">
                    <p></p>
                </div>

                <div class=""bloom-editable normal-style"" style="""" lang=""z"" contenteditable=""true"">
                    <p></p>
                </div>
            </div>
			<div class=""bloom-imageContainer""><img src=""placeHolder.png""></img></div>
			<div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"" data-test-id=""tg2"">
                <div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""español"" lang=""es"" contenteditable=""true"">
                    <p>This should round trip to the second group.</p>
                </div>

                <div class=""bloom-editable normal-style"" style="""" lang=""z"" contenteditable=""true"">
                    <p></p>
                </div>
            </div>
         </div>
    </div>
	    <div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-right bloom-monolingual"" data-page="""" id=""dc90dbe0-7584-4d9f-bc06-0e0326060054"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""1"" lang="""">
        <div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Basic Text &amp; Picture"" lang=""en"">
            Basic Text &amp; Picture
        </div>

        <div class=""pageDescription"" lang=""en""></div>

        <div class=""split-pane-component marginBox"" style="""">
            <div class=""split-pane horizontal-percent"" style=""min-height: 42px;"">
                <div class=""split-pane-component position-top"">
                    <div class=""split-pane-component-inner"" min-width=""60px 150px 250px"" min-height=""60px 150px 250px"">
                        <div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
                            <div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" id=""colorTest"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""español"" lang=""es"" contenteditable=""true"">
                                <p>I have a<span style=""color:#ff0000;""> red</span> house and a <span style=""color:#ff00ff"">purple</span> pig.</p>
                            </div>

                            <div class=""bloom-editable normal-style"" style="""" lang=""z"" contenteditable=""true"">
                                <p></p>
                            </div>

                            <div class=""bloom-editable normal-style bloom-contentNational1"" style="""" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""English"" lang=""en"" contenteditable=""true"">
                                <p></p>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
</body>
</html>
";

        private HtmlDom _roundtrippedDom;
        private InternalSpreadsheet _sheetFromExport;

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            var origDom = new HtmlDom(roundtripTestBook, true);
            _roundtrippedDom = new HtmlDom(roundtripTestBook, true); //Will get imported into

            // We want to test that exporting a book with branding in the data-dive and importing into a book with no branding
            // does not reinstate the branding. So we need to remove it from the pre-import DOM, which is otherwise
            // (for this test) the same as what we originally exported.
            var branding = _roundtrippedDom.SelectSingleNode(
                "//div[@data-book='outside-back-cover-branding-bottom-html']"
            );
            branding.ParentNode.RemoveChild(branding);
            AssertThatXmlIn
                .Dom(origDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@id='simpleFormattingTest']", 1);
            AssertThatXmlIn
                .Dom(origDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@id='nestedFormattingTest']", 1);
            var mockLangDisplayNameResolver = new Mock<ILanguageDisplayNameResolver>();
            mockLangDisplayNameResolver
                .Setup(x => x.GetLanguageDisplayName("en"))
                .Returns("English");
            var exporter = new SpreadsheetExporter(mockLangDisplayNameResolver.Object);
            exporter.Params = new SpreadsheetExportParams();
            _sheetFromExport = exporter.Export(origDom, "fakeImagesFolderpath");
            using (var tempFile = TempFile.WithExtension("xslx"))
            {
                _sheetFromExport.WriteToFile(tempFile.Path);
                var sheet = InternalSpreadsheet.ReadFromFile(tempFile.Path);
                var importer = new TestSpreadsheetImporter(null, _roundtrippedDom);
                await importer.ImportAsync(sheet);
            }
        }

        [OneTimeTearDown]
        public void OneTimeTearDown() { }

        [Test]
        public void RoundTripSimpleFormatting()
        {
            var nodeList = _roundtrippedDom.SafeSelectNodes("//div[@id='simpleFormattingTest']");
            Assert.That(nodeList.Count, Is.EqualTo(1));
            var node = nodeList[0];
            RemoveTopLevelWhitespace(node);
            Assert.That(
                node.InnerText,
                Is.EqualTo(
                    "Once upon a time there was a very bold dog. This dog was itchy. It went under a tree. The tree had 104 leaves."
                )
            );

            Assert.That(
                FormatNodeContainsText("//div[@id='simpleFormattingTest']//strong", "bold")
            );
            Assert.That(FormatNodeContainsText("//div[@id='simpleFormattingTest']//em", "itchy"));
            Assert.That(FormatNodeContainsText("//div[@id='simpleFormattingTest']//u", "under"));
            Assert.That(FormatNodeContainsText("//div[@id='simpleFormattingTest']//sup", "4"));

            Assert.That(
                FormatNodeContainsText("//div[@id='simpleFormattingTest']//strong", "dog"),
                Is.False
            );
            Assert.That(
                FormatNodeContainsText("//div[@id='simpleFormattingTest']//em", "dog"),
                Is.False
            );
            Assert.That(
                FormatNodeContainsText("//div[@id='simpleFormattingTest']//u", "dog"),
                Is.False
            );
            Assert.That(
                FormatNodeContainsText("//div[@id='simpleFormattingTest']//sup", "dog"),
                Is.False
            );
        }

        [Test]
        public void RoundtripNestedFormatting()
        {
            var nodeList = _roundtrippedDom.SafeSelectNodes("//div[@id='nestedFormattingTest']");
            Assert.That(nodeList.Count, Is.EqualTo(1));
            var node = nodeList[0];
            RemoveTopLevelWhitespace(node);
            Assert.That(
                node.InnerText,
                Is.EqualTo(
                    "One day the dog went for a walk. She had a very pleasant walk. After her walk, she decided to take a nap.The next day, the dog decided to go for a swim. She swam in a lake."
                )
            );
            Assert.That(
                HasTextWithFormatting(
                    "//div[@id='nestedFormattingTest']//",
                    "One day the dog went for a walk. She had a very pleasant walk. After her ",
                    bold: true,
                    italic: false,
                    underlined: false,
                    superscript: false
                )
            );
            Assert.That(
                HasTextWithFormatting(
                    "//div[@id='nestedFormattingTest']//",
                    "walk,",
                    bold: true,
                    italic: false,
                    underlined: true,
                    superscript: false
                )
            );
            Assert.That(
                HasTextWithFormatting(
                    "//div[@id='nestedFormattingTest']//",
                    "she",
                    bold: true,
                    italic: true,
                    underlined: true,
                    superscript: false
                )
            );
            Assert.That(
                HasTextWithFormatting(
                    "//div[@id='nestedFormattingTest']//",
                    " decided to take a nap.",
                    bold: true,
                    italic: true,
                    underlined: false,
                    superscript: false
                )
            );
            Assert.That(
                HasTextWithFormatting(
                    "//div[@id='nestedFormattingTest']//",
                    "The next day,",
                    bold: true,
                    italic: true,
                    underlined: false,
                    superscript: false
                )
            );
            Assert.That(
                HasTextWithFormatting(
                    "//div[@id='nestedFormattingTest']//",
                    " the dog decided to go for a swim. She swam in a lake.",
                    bold: true,
                    italic: false,
                    underlined: false,
                    superscript: false
                )
            );
        }

        [Test]
        public void RoundtripColor()
        {
            var nodeList = _roundtrippedDom.SafeSelectNodes("//div[@id='colorTest']");
            Assert.That(nodeList.Count, Is.EqualTo(1));
            var node = nodeList[0];
            RemoveTopLevelWhitespace(node);
            Assert.That(node.InnerText, Is.EqualTo("I have a red house and a purple pig."));
            Assert.That(
                HasTextWithFormatting(
                    "//div[@id='colorTest']//",
                    "I have a",
                    bold: false,
                    italic: false,
                    underlined: false,
                    superscript: false,
                    colorString: null
                )
            );
            Assert.That(
                HasTextWithFormatting(
                    "//div[@id='colorTest']//",
                    " red",
                    bold: false,
                    italic: false,
                    underlined: false,
                    superscript: false,
                    colorString: "#FF0000"
                )
            );
            Assert.That(
                HasTextWithFormatting(
                    "//div[@id='colorTest']//",
                    "purple",
                    bold: false,
                    italic: false,
                    underlined: false,
                    superscript: false,
                    colorString: "#FF00FF"
                )
            );
        }

        //Remove the whitespace between <p> tags that was originally there just for readability
        private void RemoveTopLevelWhitespace(SafeXmlNode node)
        {
            foreach (var childNode in node.ChildNodes)
            {
                if (childNode.Name.Equals("#whitespace"))
                {
                    node.RemoveChild(childNode);
                }
            }
        }

        [Test]
        public void WhitespaceTestKeepLineBreak()
        {
            var nodeList = _roundtrippedDom.SafeSelectNodes("//div[@id='whitespaceTest']");
            Assert.That(nodeList.Count, Is.EqualTo(1));
            var node = nodeList[0];
            RemoveTopLevelWhitespace(node);
            var children = node.ChildNodes;
            Assert.That(children.Count, Is.EqualTo(6));
            foreach (var child in children)
            {
                Assert.That(child.Name, Is.EqualTo("p"));
            }

            Assert.That(children[0].InnerText, Is.EqualTo(""));
            Assert.That(
                children[1].InnerText,
                Is.EqualTo("An empty paragraph comes before this one.")
            );
            Assert.That(children[2].InnerText, Is.EqualTo(""));
            Assert.That(children[3].InnerText, Is.EqualTo(""));
            Assert.That(
                children[4].InnerXml,
                Is.EqualTo(
                    "This sentence follows two empty paragraphs. It will be followed by a new line.<span class=\"bloom-linebreak\"></span>\xfeffThis sentence is trailed by an empty paragraph."
                )
            );
            Assert.That(children[5].InnerText, Is.EqualTo(""));
        }

        [Test]
        public void DatDivUnchanged()
        {
            Assert.That(
                HasTextWithFormatting(
                    "//div[@id='bloomDataDiv']/div[@data-book='bookTitle' and @lang='en' and @id='idShouldGetKept']//",
                    "Pineapple",
                    bold: false,
                    italic: true,
                    underlined: false,
                    superscript: false
                )
            );
            Assert.That(
                HasTextWithFormatting(
                    "//div[@id='bloomDataDiv']/div[@data-book='bookTitle' and @lang='en' and @id='idShouldGetKept']//",
                    "Farm",
                    bold: false,
                    italic: false,
                    underlined: false,
                    superscript: false
                )
            );
        }

        [Test]
        public void BrandingRemoved()
        {
            AssertThatXmlIn
                .Dom(_roundtrippedDom.RawDom)
                .HasNoMatchForXpath(
                    "//div[@id='bloomDataDiv']/div[contains(@data-book, 'branding')]"
                );
        }

        [Test]
        public void BrandingNotExported()
        {
            foreach (var row in _sheetFromExport.ContentRows)
            {
                Assert.That(row.GetCell(0).Content, Does.Not.Contain("branding"));
            }
        }

        [Test]
        public void DatDivImagesUnchanged()
        {
            Assert.That(
                FormatNodeContainsText(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImage' and @src='cover.png']",
                    "cover.png"
                )
            );
            Assert.That(
                FormatNodeContainsText(
                    "//div[@id='bloomDataDiv']/div[@data-book='licenseImage' and not(@src)]",
                    "license.png"
                )
            );
        }

        private bool HasTextWithFormatting(
            string baseXPath,
            string text,
            bool bold,
            bool italic,
            bool underlined,
            bool superscript,
            string colorString = null
        )
        {
            return FormatNodeContainsText(baseXPath + "strong", text) == bold
                && FormatNodeContainsText(baseXPath + "em", text) == italic
                && FormatNodeContainsText(baseXPath + "u", text) == underlined
                && FormatNodeContainsText(baseXPath + "sup", text) == superscript
                // if color = null, check that text exists with no color. Otherwise, check that text exists with the specified color.
                && (
                    colorString == null
                        && (
                            FormatNodeContainsText(
                                baseXPath + "span[not(contains(@style, 'color'))]",
                                text
                            )
                            || FormatNodeContainsText(baseXPath + "span[not(@style)]", text)
                            || FormatNodeContainsText(baseXPath + "*", text)
                        )
                    || (
                        colorString != null
                        && FormatNodeContainsText(
                            baseXPath + String.Format("span[contains(@style, '{0}')]", colorString),
                            text
                        )
                    )
                );
        }

        private bool FormatNodeContainsText(string xPath, string text)
        {
            var nodeList = _roundtrippedDom.SafeSelectNodes(xPath);
            return Enumerable.Any(nodeList, x => x.InnerText.Contains(text));
        }

        [TestCase("", "")]
        [TestCase(
            "Nothing to escape here.\r\n\t Here neither.",
            "Nothing to escape here.\r\n\t Here neither."
        )]
        [TestCase("foo_x000D_\nbar", "foo\r\nbar")]
        [TestCase("foo_x000D_bar\r\n_x000D_\r\nbaz_x000D_\n\n", "foo\rbar\r\n\r\r\nbaz\r\n\n")]
        [TestCase("foo_x000D_bar_x005F_baz_x000D_", "foo\rbar_baz\r")]
        public void UndoExcelEscapedChars(string input, string expected)
        {
            Assert.That(
                SpreadsheetIO.ReplaceExcelEscapedCharsAndEscapeXmlOnes(input),
                Is.EqualTo(expected)
            );
        }

        // This test verifies the effect of inserting [blank] for empty cells. Without it,
        // a row made of a picture and an empty cell would look like a row for just a picture,
        // and the NEXT text row would go into the one that should be empty.
        [Test]
        public void EmptyElementBeforeNonEmptyWithPictureRoundTrips()
        {
            AssertThatXmlIn
                .Dom(_roundtrippedDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@data-test-id='tg2']/div/p[contains(text(),'This should round trip to the second group.')]",
                    1
                );
            AssertThatXmlIn
                .Dom(_roundtrippedDom.RawDom)
                .HasNoMatchForXpath(
                    "//div[@data-test-id='tg1']/div/p[contains(text(),'This should round trip to the second group.')]"
                );
        }
    }
}
