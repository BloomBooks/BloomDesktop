using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.SafeXml;
using Bloom.Spreadsheet;
using BloomTemp;
using Moq;
using NUnit.Framework;
using OfficeOpenXml;
using SIL.IO;

namespace BloomTests.Spreadsheet
{
    /// <summary>
    /// The production import of a derivative book's spreadsheet into a real, blank book, including
    /// the step that brings the book up to date afterwards. The credits page must then show the
    /// user's own wording of the sentence about the original book, and the book must keep the
    /// original title (BL-17070).
    /// </summary>
    public class SpreadsheetOriginalCopyrightIntoBookTests
    {
        static SpreadsheetOriginalCopyrightIntoBookTests()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        private const string kUsersNotice =
            "<p>Adapté de l'original, <em>Zebras and other cool animals</em>, Copyright © 1910, First Copyright Owner. Licence sous CC BY 4.0.</p>";

        private const string kSourceBook =
            @"<html>
<head></head>
<body data-l1=""fr"" data-l2="""" data-l3="""">
	<div id=""bloomDataDiv"">
		<div data-book=""bookTitle"" lang=""en"">Horses</div>
		<div data-book=""bookTitle"" lang=""fr"">zèbres</div>
		<div data-book=""originalTitle"" lang=""*"">Horses</div>
		<div data-book=""originalLicenseUrl"" lang=""*"">http://creativecommons.org/licenses/by/4.0/</div>
		<div data-book=""originalCopyright"" lang=""*"">Copyright © 2010, First Copyright Owner</div>
		<div data-book=""userOriginalCopyrightAndLicense"" lang=""*"">"
            + kUsersNotice
            + @"</div>
		<div data-book=""copyright"" lang=""*"">Copyright © 2022, Second Copyright Holder</div>
		<div data-book=""licenseUrl"" lang=""*"">http://creativecommons.org/licenses/by/4.0/</div>
	</div>
</body>
</html>";

        private const string kBlankBook =
            @"<html>
<head></head>
<body data-l1=""fr"" data-l2="""" data-l3="""">
	<div id=""bloomDataDiv"">
		<div data-book=""bookTitle"" lang=""fr""></div>
	</div>
	{0}
</body>
</html>";

        private TemporaryFolder _testFolder;
        private HtmlDom _resultDom;

        /// <summary>
        /// Export the source book to a real .xlsx file, read it back, and import it into a blank
        /// book the way the Collections tab does, keeping the book's DOM for the tests to examine.
        /// </summary>
        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            _testFolder = SpreadsheetTestFolders.MakeFolderFor(this);
            var bookFolder = new TemporaryFolder(_testFolder, "Book");
            var settings = new NewCollectionSettings();
            settings.Language1.Tag = "fr";
            settings.Language1.SetName("French", false);
            settings.SettingsFilePath = Path.Combine(bookFolder.FolderPath, "dummy");
            var fileLocator = new BloomFileLocator(
                settings,
                new XMatterPackFinder(new[] { BloomFileLocator.GetFactoryXMatterDirectory() }),
                ProjectContext.GetFactoryFileLocations(),
                ProjectContext.GetFoundFileLocations(),
                ProjectContext.GetAfterXMatterFileLocations()
            );
            File.WriteAllText(
                Path.Combine(bookFolder.FolderPath, "blank.htm"),
                kBlankBook.Replace("{0}", SpreadsheetImporterWithBookTests.pageWithJustText)
            );
            var storage = new BookStorage(
                bookFolder.FolderPath,
                fileLocator,
                new BookRenamedEvent(),
                settings
            );
            var book = new Bloom.Book.Book(
                new BookInfo(bookFolder.FolderPath, true),
                storage,
                null,
                settings,
                new PageListChangedEvent(),
                new BookRefreshEvent()
            );

            var resolver = new Mock<ILanguageDisplayNameResolver>();
            resolver.Setup(x => x.GetLanguageDisplayName(It.IsAny<string>())).Returns("French");
            var exporter = new SpreadsheetExporter(resolver.Object);
            exporter.Params = new SpreadsheetExportParams();
            var sheet = exporter.Export(new HtmlDom(kSourceBook, true), "fakeImagesFolderpath");
            using (var tempFile = TempFile.WithExtension("xlsx"))
            {
                sheet.WriteToFile(tempFile.Path);
                var sheetFromFile = InternalSpreadsheet.ReadFromFile(tempFile.Path);
                var importer = new SpreadsheetImporter(null, book, bookFolder.FolderPath);
                await importer.ImportAsync(sheetFromFile);
            }
            Assert.That(
                book.IsSaveable,
                Is.True,
                "the import only brings a saveable book up to date"
            );
            _resultDom = book.OurHtmlDom;
        }

        /// <summary>Remove the book the import made.</summary>
        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _testFolder?.Dispose();
        }

        /// <summary>The imported book keeps the original title and the user's wording.</summary>
        [TestCase("originalTitle", "Horses")]
        [TestCase("userOriginalCopyrightAndLicense", kUsersNotice)]
        public void DataDivValueSurvives(string key, string expected)
        {
            Assert.That(
                _resultDom.GetBookSetting(key).GetExactAlternative("*"),
                Is.EqualTo(expected)
            );
        }

        /// <summary>
        /// The credits page shows the user's wording, not the sentence Bloom would generate.
        /// </summary>
        [Test]
        public void CreditsPageShowsUsersWording()
        {
            var spots = _resultDom
                .RawDom.SafeSelectNodes(
                    "//div[contains(@class,'bloom-page')]//*[@data-derived='originalCopyrightAndLicense']"
                )
                .Cast<SafeXmlElement>()
                .ToList();
            Assert.That(
                spots,
                Is.Not.Empty,
                "the credits page should have the original-copyright spot"
            );
            foreach (var spot in spots)
                Assert.That(spot.InnerXml, Does.Contain("Copyright © 1910"));
        }
    }
}
