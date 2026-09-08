using System.IO;
using Bloom.FontProcessing;
using Bloom.Publish.Epub;
using NUnit.Framework;

namespace BloomTests.Publish.Epub
{
    /// <summary>
    /// Tests that a font the user embedded by dropping a .woff2 file into the book folder is
    /// embedded in the ePUB, rather than reported as missing and replaced by the default font.
    /// </summary>
    [TestFixture]
    public class ExportEpubWithEmbeddedFontTests : ExportEpubTestsBaseClass
    {
        public override void Setup()
        {
            base.Setup();
            _ns = ExportEpubTestsBaseClass.GetNamespaceManager();
            _bookServer = CreateBookServer();
        }

        public override void TearDown()
        {
            // EmbedFonts adds the book's fonts to the FontFileFinder, which is a reused singleton
            // in a test run, so another test would otherwise still resolve MyEmbedded.
            FontFileFinder.ResetInstanceForTests();
            base.TearDown();
        }

        [Test]
        public void FontInBookFolder_IsEmbeddedInEpub()
        {
            var userStyleSheet =
                @"/*<![CDATA[*/
    .Embedded-style[lang='xyz'] { font-family: MyEmbedded ! important; font-size: 12pt ! important; }
	/*]]>*/";
            var book = SetupBookLong(
                "This is some text",
                "xyz",
                extraEditDivClasses: "Embedded-style",
                extraHeadContent: "<style type='text/css' title='userModifiedStyles'>"
                    + userStyleSheet
                    + "</style>"
            );
            // Sanity check: the style asks for the font before we export.
            Assert.That(
                book.OurHtmlDom.RawDom.InnerXml,
                Does.Contain("font-family: MyEmbedded"),
                "test setup should have put the font reference in userModifiedStyles"
            );

            // The font the user dropped into the book folder. Content is irrelevant; the family name
            // comes from the file name.
            var fontFileName = "MyEmbedded.woff2";
            File.WriteAllText(Path.Combine(book.FolderPath, fontFileName), "phony woff2");

            MakeEpub("output", "FontInBookFolder_IsEmbeddedInEpub", book);

            // The font file is in the ePUB, declared with the woff2 media type.
            VerifyEpubItemExists("content/" + EpubMaker.kFontsFolder + "/" + fontFileName);
            Assert.That(
                FixContentForXPathValueSlash(_manifestContent),
                Does.Contain(kFontsSlash + fontFileName)
            );
            Assert.That(_manifestContent, Does.Contain("application/font-woff2"));

            // fonts.css declares the family, pointing from css/ back out to fonts/.
            var fontCssData = ExportEpubTestsBaseClass.GetZipContent(
                _epub,
                "content/" + EpubMaker.kCssFolder + "/fonts.css"
            );
            Assert.That(
                fontCssData,
                Does.Contain(
                    "@font-face {font-family:'MyEmbedded'; font-weight:normal; font-style:normal; src:url('../"
                        + EpubMaker.kFontsFolder
                        + "/"
                        + fontFileName
                        + "') format('woff2');}"
                )
            );

            // The style still asks for the embedded font; it was not rewritten to the default font.
            var pageData = GetPageNData(2);
            Assert.That(pageData, Does.Contain("font-family: MyEmbedded"));
            Assert.That(pageData, Does.Not.Contain("font-family: Andika ! important"));
        }
    }
}
