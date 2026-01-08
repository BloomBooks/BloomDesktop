using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bloom.Book;
using Bloom.Spreadsheet;
using BloomTemp;
using Moq;
using NUnit.Framework;

namespace BloomTests.Spreadsheet
{
    /// <summary>
    /// This class tests exporting xmatter when exporting to spreadsheet.
    /// </summary>
    public class SpreadsheetXmatterTests
    {
        public const string dataDivBook =
            @"
<html>
<head>
    <meta charset=""UTF-8""></meta>
    <meta name=""Generator"" content=""Bloom Version 5.1.0 (apparent build date: 05-Aug-2021)""></meta>
    <meta name=""BloomFormatVersion"" content=""2.1""></meta>
    <meta name=""pageTemplateSource"" content=""Basic Book""></meta>

    <title>Microwave</title>
    <style type=""text/css"" title=""userModifiedStyles"">
    /*<![CDATA[*/
    .BigWords-style { font-size: 45pt !important; text-align: center !important; }/*]]>*/
    </style>
    <style type=""text/css"">
    DIV.bloom-page.coverColor       {               background-color: #98D0B9 !important;   }
    </style>
    <meta name=""maintenanceLevel"" content=""2""></meta>
    <link rel=""stylesheet"" href=""basePage.css"" type=""text/css""></link>
    <link rel=""stylesheet"" href=""previewMode.css"" type=""text/css""></link>
    <link rel=""stylesheet"" href=""origami.css"" type=""text/css""></link>
    <link rel=""stylesheet"" href=""branding.css"" type=""text/css""></link>
    <link rel=""stylesheet"" href=""Basic Book.css"" type=""text/css""></link>
    <link rel=""stylesheet"" href=""Traditional-XMatter.css"" type=""text/css""></link>
    <link rel=""stylesheet"" href=""defaultLangStyles.css"" type=""text/css""></link>
    <link rel=""stylesheet"" href=""customCollectionStyles.css"" type=""text/css""></link>
</head>

<body data-l1=""es"" data-l2="""" data-l3="""">
    <div id=""bloomDataDiv"">
        <div data-book=""styleNumberSequence"" lang=""*"">
            0
        </div>

        <div data-book=""contentLanguage1"" lang=""*"">
            es
        </div>

        <div data-book=""contentLanguage1Rtl"" lang=""*"">
            False
        </div>

        <div data-book=""languagesOfBook"" lang=""*"">
            Spanish, English
        </div>

        <div data-book=""bookTitle"" lang=""es"" class="" bloom-editable bloom-nodefaultstylerule bloom-padForOverflow"" style=""padding-bottom: 0px;"" contenteditable=""true"">
            <p>Spanish Microwave</p>
        </div>

        <div data-book=""bookTitle"" lang=""en"" class="" bloom-editable bloom-nodefaultstylerule bloom-padForOverflow"" style=""padding-bottom: 0px;"" contenteditable=""true"">
            <p>English Microwave</p>
        </div>

        <div data-book=""coverImage"" lang=""*"" src=""wrongImage.png"" alt=""This picture, microwave1.png, is missing or was loading too slowly."" data-copyright="""" data-creator="""" data-license="""">
            microwave1.png
        </div>

        <div data-book=""originalTitle"" lang=""*"">
            Microwave
        </div>

		<div data-book=""ISBN"" lang=""*"" class="" bloom-editable"" contenteditable=""true"">
            <p>123456</p>
        </div>

        <div data-book=""ISBN"" lang=""en"" class="" bloom-editable"" contenteditable=""true"" style="""">
            <p>123456</p>
        </div>

        <div data-book=""outside-back-cover-branding-bottom-html"" lang=""*""><img class=""branding"" src=""BloomWithTaglineAgainstLight.svg"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img></div>
        <div data-book=""outside-back-cover-bottom-html"" lang=""*""><img class=""branding"" src=""BloomWithTaglineAgainstLight.svg"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img></div>

        <div data-book=""licenseUrl"" lang=""*"">
            http://creativecommons.org/licenses/by/4.0/
        </div>

        <div data-book=""licenseDescription"" lang=""es"">
            http://creativecommons.org/licenses/by/4.0/<br />Se puede hacer uso comercial de esta obra. Se puede adaptar y añadir a esta obra. Se debe mantener el derecho de autor así como los créditos de los autores, ilustradores, etc.
        </div>

        <div data-book=""licenseImage"" lang=""*"">
            license.png
        </div>

		<div data-book=""ztest"" lang=""z"">
			foo
		</div>
        <div data-xmatter-page=""insideFrontCover"" data-page=""required singleton"" data-export=""front-matter-inside-front-cover"" data-page-number=""""></div>
        <div data-xmatter-page=""titlePage"" data-page=""required singleton"" data-export=""front-matter-title-page"" data-page-number=""""></div>
        <div data-xmatter-page=""credits"" data-page=""required singleton"" data-export=""front-matter-credits"" data-page-number=""""></div>
        <div data-xmatter-page=""insideBackCover"" data-page=""required singleton"" data-export=""back-matter-inside-back-cover"" data-page-number=""""></div>
        <div data-xmatter-page=""outsideBackCover"" data-page=""required singleton"" data-export=""back-matter-back-cover"" data-page-number=""""></div>
        <div data-xmatter-page=""frontCover"" data-page=""required singleton"" data-export=""front-matter-cover"" data-page-number=""""></div>
    </div>
</body>
</html>";

        private SpreadsheetExporter _exporter;

        // The tests are all written in terms of _sheet and _rows, the output
        // of an export operation. But we create two sheets, one by export, and
        // one by writing the first to file and reading it back. We want to apply
        // the same tests to each. This is currently achieved by using the test
        // case to select one pair (_sheetFromExport, _rowsFromExport)
        // or (_sheetFromFile, _rowsFromFile) to set as _sheet and _rows.
        private InternalSpreadsheet _sheet;
        private List<ContentRow> _rows;
        private InternalSpreadsheet _sheetFromExport;
        private List<ContentRow> _rowsFromExport;
        private InternalSpreadsheet _sheetFromFile;
        private List<ContentRow> _rowsFromFile;
        private TemporaryFolder _spreadsheetFolder;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var dom = new HtmlDom(dataDivBook, true);

            _spreadsheetFolder = new TemporaryFolder("SpreadsheetXmatterTests");
            var mockLangDisplayNameResolver = new Mock<ILanguageDisplayNameResolver>();
            mockLangDisplayNameResolver
                .Setup(x => x.GetLanguageDisplayName("en"))
                .Returns("English");
            mockLangDisplayNameResolver
                .Setup(x => x.GetLanguageDisplayName("es"))
                .Returns("Español");

            _exporter = new SpreadsheetExporter(mockLangDisplayNameResolver.Object);
            _sheetFromExport = _exporter.ExportToFolder(
                dom,
                "fakeImagesFolderpath",
                _spreadsheetFolder.FolderPath,
                out string outputPath,
                null,
                OverwriteOptions.Overwrite
            );

            _rowsFromExport = _sheetFromExport.ContentRows.ToList();
            _sheetFromFile = InternalSpreadsheet.ReadFromFile(outputPath);
            _rowsFromFile = _sheetFromFile.ContentRows.ToList();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _spreadsheetFolder?.Dispose();
        }

        void SetupFor(string source)
        {
            switch (source)
            {
                case "fromExport":
                    _sheet = _sheetFromExport;
                    _rows = _rowsFromExport;
                    break;
                case "fromFile":
                    _sheet = _sheetFromFile;
                    _rows = _rowsFromFile;
                    break;
                default:
                    // Whatever's going on needs to fail
                    Assert.That(source, Is.Not.EqualTo(source));
                    break;
            }
        }

        [TestCase("fromExport")]
        [TestCase("fromFile")]
        public void BasicXmatterTest(string source)
        {
            SetupFor(source);
            var starLangCol = _sheet.GetRequiredColumnForLang("*");
            var styleNumberSequenceRow = _rows.Find(x =>
                x.MetadataKey.Equals("[style number sequence]")
            );
            Assert.That(styleNumberSequenceRow, Is.Not.Null);
            Assert.That(styleNumberSequenceRow.GetCell(starLangCol).Content, Is.EqualTo("0"));
        }

        [TestCase("fromExport")]
        [TestCase("fromFile")]
        public void MultilingualXmatterTest(string source)
        {
            SetupFor(source);
            var contentLangRow = _rows.Find(x =>
                x.MetadataKey.Equals(InternalSpreadsheet.BookTitleRowLabel)
            );
            Assert.That(contentLangRow, Is.Not.Null);
            Assert.That(
                contentLangRow.GetCell(_sheet.GetRequiredColumnForLang("es")).Content,
                Is.EqualTo("<p>Spanish Microwave</p>")
            );
            Assert.That(
                contentLangRow.GetCell(_sheet.GetRequiredColumnForLang("en")).Content,
                Is.EqualTo("<p>English Microwave</p>")
            );
        }

        [TestCase("fromExport")]
        [TestCase("fromFile")]
        public void ISBN_Correct(string source)
        {
            SetupFor(source);
            var contentLangRow = _rows.Find(x => x.MetadataKey.Equals("[ISBN]"));
            Assert.That(contentLangRow, Is.Not.Null);
            Assert.That(
                contentLangRow.GetCell(_sheet.GetRequiredColumnForLang("en")).Content,
                Is.EqualTo("<p>123456</p>")
            );
            Assert.That(
                contentLangRow.GetCell(_sheet.GetRequiredColumnForLang("*")).Content,
                Is.EqualTo("<p>123456</p>")
            );
        }

        [TestCase("fromExport")]
        [TestCase("fromFile")]
        public void ImageSourceXmatterTest(string source)
        {
            SetupFor(source);
            var imageSourceCol = _sheet.GetColumnForTag(InternalSpreadsheet.ImageSourceColumnLabel);

            var coverImageRow = _rows.Find(x =>
                x.MetadataKey.Equals(InternalSpreadsheet.CoverImageRowLabel)
            );
            Assert.That(coverImageRow, Is.Not.Null);
            Assert.That(
                coverImageRow.GetCell(imageSourceCol).Content,
                Is.EqualTo(Path.Combine("images", "microwave1.png"))
            );

            var licenseImageRow = _rows.Find(x => x.MetadataKey.Equals("[licenseImage]"));
            Assert.That(licenseImageRow, Is.Null);

            var backImageRow = _rows.Find(x =>
                x.MetadataKey.Equals("[outside-back-cover-bottom-html]")
            );
            Assert.That(backImageRow, Is.Not.Null);
            Assert.That(
                backImageRow.GetCell(imageSourceCol).Content,
                Is.EqualTo(Path.Combine("images", "BloomWithTaglineAgainstLight.svg"))
            );
        }

        [TestCase("fromExport")]
        [TestCase("fromFile")]
        public void ZLanguageXmatterNotExported(string source)
        {
            SetupFor(source);
            Assert.That(_sheet.Languages.Contains("z"), Is.False);
            Assert.That(_rows.FirstOrDefault(x => x.MetadataKey.Equals("[ztest]")), Is.Null);
        }

        [Test]
        public void MetadataRowsAreHiddenExceptTitleAndImageCover()
        {
            SetupFor("fromExport");
            foreach (var row in _rows)
            {
                if (
                    new string[]
                    {
                        InternalSpreadsheet.BookTitleRowLabel,
                        InternalSpreadsheet.CoverImageRowLabel,
                    }.Contains(row.MetadataKey)
                )
                    Assert.That(row.Hidden, Is.False);
                else
                    Assert.That(row.Hidden, Is.True);
            }
        }
    }
}
