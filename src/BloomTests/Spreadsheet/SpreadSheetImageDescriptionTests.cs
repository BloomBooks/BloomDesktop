using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Bloom.Book;
using Bloom.Collection;
using Bloom.SafeXml;
using Bloom.Spreadsheet;
using Moq;
using NUnit.Framework;
using OfficeOpenXml;
using SIL.IO;
using SIL.Xml;

namespace BloomTests.Spreadsheet
{
    internal class SpreadSheetImageDescriptionTests
    {
        static SpreadSheetImageDescriptionTests()
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
		
		<div data-book=""coverImage"" lang=""*"" src=""cover.png"" alt=""This picture, cover.png, is missing or was loading too slowly."">
			cover.png
		</div>
		<div data-book=""coverImageDescription"" lang=""akl"" class="" bloom-editable"" contenteditable=""true"" data-audiorecordingmode=""Sentence"">
            <p><span id=""i1e02b639-7b63-4d1c-92c0-7fe6fdc93532"" class=""audio-sentence"" recordingmd5=""1c1ff00da14960f5470f2cec5dd7700a"" data-duration=""3.709388"">An airplane on a lake, with trees in the background</span></p>
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
			<div class=""bloom-imageContainer"">
				<img src=""placeHolder.png""></img>
				<div class=""bloom-translationGroup bloom-imageDescription bloom-trailingElement"">
                    <div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" contenteditable=""true"" lang=""akl"" data-languagetipcontent=""Aklanon"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-audiorecordingmode=""Sentence"">
                        <p><span id=""i46a016d9-9032-4c1c-98e5-ae48bded567d"" class=""audio-sentence"" recordingmd5=""c829ae37804119adee364bbcdbddae5a"" data-duration=""3.709388"">A fish jumping above a lake with mountains behind</span></p>
                    </div>

                    <div class=""bloom-editable normal-style bloom-contentNational1"" contenteditable=""true"" lang=""en"" data-languagetipcontent=""English"" style="""" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"">
                        <p></p>
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
			<div class=""bloom-imageContainer"">
				<img src=""placeHolder.png""></img>
				<div class=""bloom-translationGroup bloom-imageDescription bloom-trailingElement"">
                    <div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" contenteditable=""true"" lang=""akl"" data-languagetipcontent=""Aklanon"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-audiorecordingmode=""Sentence"">
                        <p><span id=""i46a016d9-9032-4c1c-98e5-ae48bded567d"" class=""audio-sentence"" recordingmd5=""c829ae37804119adee364bbcdbddae5a"" data-duration=""3.709388"">Another image description</span></p>
                    </div>

                    <div class=""bloom-editable normal-style bloom-contentNational1"" contenteditable=""true"" lang=""en"" data-languagetipcontent=""English"" style="""" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"">
                        <p></p>
                    </div>
                </div>
			</div>
         </div>
    </div>
</body>
</html>
";

        private HtmlDom _destDom;
        private InternalSpreadsheet _sheetFromExport;
        private List<SafeXmlElement> _contentPages;

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            var origDom = new HtmlDom(roundtripTestBook, true);
            // An empty book to import into.
            var target = string.Format(
                SpreadsheetImageAndTextImportTests.templateDom,
                SpreadsheetImageAndTextImportTests.coverPage
                    + SpreadsheetImageAndTextImportTests.PageWithImageAndText(
                        1,
                        1,
                        1,
                        imgDescription: @"<div class=""bloom-translationGroup bloom-imageDescription""><div class =""bloom-editable"" lang=""z""></div></div>"
                    )
            );

            _destDom = new HtmlDom(target, true);

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
                var importer = new TestSpreadsheetImporter(null, _destDom);
                await importer.ImportAsync(sheet);
            }
            _contentPages = _destDom
                .SafeSelectNodes("//div[contains(@class, 'numberedPage')]")
                .Cast<SafeXmlElement>()
                .ToList();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown() { }

        [Test]
        public void CoverImageDescriptionSurvived()
        {
            AssertThatNodeContainsText(
                _destDom.Body,
                "//div[@id='bloomDataDiv']/div[@data-book='coverImageDescription']",
                "An airplane on a lake"
            );
        }

        private void AssertThatNodeContainsText(SafeXmlElement source, string xPath, string text)
        {
            var nodeList = source.SafeSelectNodes(xPath);
            Assert.That(
                nodeList,
                Has.Length.GreaterThan(0),
                "Should have found matches for " + xPath
            );
            Assert.That(
                Enumerable.Any(nodeList, x => x.InnerText.Contains(text)),
                Is.True,
                "Some matching node should have contained " + text
            );
        }

        // I would like to do this, but it requires a lot of extra setup to make a real Book object
        // so the import will run BringBookUpToDate. I think we have adequate testing elsewhere that if
        // coverImageDescription makes it into the data div it will also make it into the cover itself.
        //[Test]
        //public void CoverImageDescription_PutOnCover()
        //{
        //	AssertThatNodeContainsText(_coverPage, ".//div[@class='bloom-imageContainer']/div[contains(@class, 'bloom-imageDescription')]", "An airplane on a lake");
        //}

        [Test]
        public void ImageDescription_PresentOnContentPage()
        {
            AssertThatNodeContainsText(
                _contentPages[0],
                ".//div[contains(@class, 'bloom-imageContainer')]/div[contains(@class, 'bloom-imageDescription')]",
                "A fish jumping above a lake"
            );
        }

        [Test]
        public void ShouldNotDuplicateExistingImageDescription()
        {
            // The 'existing' page that we import onto already has an image description.
            // The importer should reuse it, not add another.
            AssertThatXmlIn
                .Element(_contentPages[0])
                .HasSpecifiedNumberOfMatchesForXpath(
                    ".//div[contains(@class, 'bloom-imageContainer')]/div[contains(@class, 'bloom-imageDescription')]",
                    1
                );
        }

        [Test]
        public void ImageDescription_PresentOnNewPage()
        {
            // Since the second exported page just has an image, and the one page in the original target book
            // is an image-and-picture, the importer will have to insert a page, and create the image description div.
            AssertThatNodeContainsText(
                _contentPages[1],
                ".//div[contains(@class, 'bloom-imageContainer')]/div[contains(@class, 'bloom-imageDescription')]",
                "Another image description"
            );
        }

        // Review: what else is worth checking?
        // We could possibly make a test case for audio in image descriptions, but the exact same code is used to output the contents
        // of an image description as any other translation group that might have audio.
        // We could do more detailed tests of the structure of the TG that is made. But, again, the same code is used and is tested elsewhere.
    }
}
