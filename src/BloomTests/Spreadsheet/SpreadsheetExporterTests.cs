using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Book;
using Bloom.Spreadsheet;
using BloomTemp;
using BloomTests.TeamCollection;
using Moq;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.Spreadsheet
{
    public class SpreadsheetExporterTests
    {
        public const string bookHtml =
            @"<html>
<head></head>
<body data-l1=""en"" data-l2="""" data-l3="""">
	<div id=""bloomDataDiv"">
        <div data-book=""bookTitle"" lang=""en"" class=""bloom-editable bloom-nodefaultstylerule bloom-padForOverflow bloom-contentFirst"" contenteditable=""true"" style=""padding-bottom: 0px;"">
            <p>My 3rd Basic Book</p>
        </div>
        <div data-book=""bookTitle"" lang=""es-x-ai-google"" class=""bloom-editable bloom-nodefaultstylerule bloom-padForOverflow bloom-contentFirst"" contenteditable=""true"" style=""padding-bottom: 0px;"">
            <p>Mi tercer libro básico</p>
        </div>
        <div data-book=""insideBackCover"" lang=""en"" class=""bloom-editable"" contenteditable=""true"" data-hint=""If you need somewhere to put more information about the book, you can use this page, which is the inside of the back cover."" style="""">
            <p>This is from the inside back cover of the book.</p>
        </div>
        <div data-book=""insideBackCover"" lang=""es-x-ai-google"" class=""bloom-editable"" contenteditable=""true"" data-hint=""If you need somewhere to put more information about the book, you can use this page, which is the inside of the back cover."">
            <p>Esto es del interior de la contraportada del libro.</p>
        </div>
	</div>
    <div class=""bloom-page numberedPage customPage bloom-combinedPage side-right Device16x9Landscape bloom-monolingual"" data-page="""" id=""958a4d5c-053a-456d-996d-5fe779397ed5"" data-tool-id=""signLanguage"" data-pagelineage=""08422e7b-9406-4d11-8c71-02005b1b8095"" data-page-number=""1"" lang="""">
        <div class=""pageLabel"" lang=""en"" data-i18n=""TemplateBooks.PageLabel.Basic Text &amp; Picture"">
            Basic Text &amp; Picture
        </div>
        <div class=""pageDescription"" lang=""en""></div>
        <div class=""marginBox"">
            <div class=""split-pane horizontal-percent"" style=""min-height: 40px;"">
                <div class=""split-pane-component position-top"" style=""bottom: 54%;"">
                    <div class=""split-pane-component-inner"">
                        <div class=""bloom-canvas bloom-leadingElement"" data-title=""Name: aor_CMB712.png Size: 42.95 kb Dots: 1500 x 950 For the current paper size: • The image container is 358 x 286 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ✓ This image would print at 402 DPI. • An image with 1119 x 894 dots would fill this container at 300 DPI."" data-imgsizebasedon=""358,286"" title=""""><img src=""aor_CMB712.png"" alt="""" data-copyright=""Copyright SIL International 2009"" data-creator="""" data-license=""cc-by-sa""></img></div>
                    </div>
                </div>
                <div class=""split-pane-divider horizontal-divider"" style=""bottom: 54%;"" title=""CTRL for precision. Double click to match previous page."" data-splitter-label=""46%""></div>
                <div class=""split-pane-component position-bottom"" style=""height: 54%;"">
                    <div class=""split-pane-component-inner"">
                        <div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"" style=""font-size: 26.6667px;"" data-hasqtip=""true"" aria-describedby=""qtip-0"">
                            <div class=""bloom-editable normal-style"" lang=""es-x-ai-google"" contenteditable=""true"" style="""" tabindex=""0"" spellcheck=""false"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""Spanish AI"">
                                <p>¿Es esto realmente un gato?  La nariz parece demasiado larga para ser un gato.  Pero posado sobre una rama tampoco parece un perro.  Entonces, ¿qué es?  ¡Realmente no puedo decirlo a primera vista!</p>
                            </div>
                            <div class=""bloom-editable normal-style"" lang=""z"" contenteditable=""true"" style="""">
                                <p></p>
                            </div>
                            <div class=""bloom-editable normal-style bloom-visibility-code-on bloom-content1"" lang=""en"" contenteditable=""true"" style="""" tabindex=""0"" spellcheck=""false"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""English"">
                                <p>Is this really a cat?  The nose looks too long for it to be a cat.  But perching on a limb doesn't look like a dog either.  So what is it?  I can't really tell from first glance!</p>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
</body>
</html>";

        private SpreadsheetExporter _exporter;
        private InternalSpreadsheet _sheet;
        private List<ContentRow> _rows;
        private List<ContentRow> _pageContentRows;

        private TemporaryFolder _spreadsheetFolder;
        private TemporaryFolder _bookFolder;
        private ProgressSpy _progressSpy;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var dom = new HtmlDom(bookHtml, true);

            _spreadsheetFolder = new TemporaryFolder("SpreadsheetExporterTests");
            _bookFolder = new TemporaryFolder("SpreadsheetExporterTests_Book");

            var mockLangDisplayNameResolver = new Mock<ILanguageDisplayNameResolver>();
            mockLangDisplayNameResolver
                .Setup(x => x.GetLanguageDisplayName("en"))
                .Returns("English");
            mockLangDisplayNameResolver
                .Setup(x => x.GetLanguageDisplayName("es-x-ai-google"))
                .Returns("Spanish AI");

            _exporter = new SpreadsheetExporter(mockLangDisplayNameResolver.Object);

            // We need this file in place so it can get exported
            RobustFile.WriteAllText(
                Path.Combine(_bookFolder.FolderPath, "aor_CMB712.png"),
                "This is a fake image"
            );

            _progressSpy = new ProgressSpy();
            _sheet = _exporter.ExportToFolder(
                dom,
                _bookFolder.FolderPath,
                _spreadsheetFolder.FolderPath,
                out string outputPath,
                _progressSpy,
                OverwriteOptions.Overwrite
            );
            _rows = _sheet.ContentRows.ToList();
            _pageContentRows = _rows
                .Where(r => r.MetadataKey == InternalSpreadsheet.PageContentRowLabel)
                .ToList();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _spreadsheetFolder?.Dispose();
            _bookFolder?.Dispose();
        }

        [Test]
        public void ExportsTextOnContentPageInAllLanguages()
        {
            Assert.That(
                _pageContentRows[0].GetCell("[en]").Text,
                Is.EqualTo(
                    "Is this really a cat?  The nose looks too long for it to be a cat.  But perching on a limb doesn't look like a dog either.  So what is it?  I can't really tell from first glance!"
                )
            );
            Assert.That(
                _pageContentRows[0].GetCell("[es-x-ai-google]").Text,
                Is.EqualTo(
                    "¿Es esto realmente un gato?  La nariz parece demasiado larga para ser un gato.  Pero posado sobre una rama tampoco parece un perro.  Entonces, ¿qué es?  ¡Realmente no puedo decirlo a primera vista!"
                )
            );
        }

        [Test]
        public void ExportsDataDivTextInAllLanguages()
        {
            var bookTitleRow = _rows.FirstOrDefault(r =>
                r.MetadataKey == InternalSpreadsheet.BookTitleRowLabel
            );
            Assert.That(bookTitleRow, Is.Not.Null);
            Assert.That(bookTitleRow.GetCell("[en]").Text, Is.EqualTo("My 3rd Basic Book"));
            Assert.That(
                bookTitleRow.GetCell("[es-x-ai-google]").Text,
                Is.EqualTo("Mi tercer libro básico")
            );

            var insideBackCoverRow = _rows.FirstOrDefault(r =>
                r.MetadataKey == "[inside back cover]"
            );
            Assert.That(insideBackCoverRow, Is.Not.Null);
            Assert.That(
                insideBackCoverRow.GetCell("[en]").Text,
                Is.EqualTo("This is from the inside back cover of the book.")
            );
            Assert.That(
                insideBackCoverRow.GetCell("[es-x-ai-google]").Text,
                Is.EqualTo("Esto es del interior de la contraportada del libro.")
            );
        }
    }
}
