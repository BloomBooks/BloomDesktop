using System.Threading.Tasks;
using Bloom.Book;
using Bloom.SafeXml;
using Bloom.Spreadsheet;
using Moq;
using NUnit.Framework;
using OfficeOpenXml;
using SIL.IO;

namespace BloomTests.Spreadsheet
{
    /// <summary>
    /// A derivative book keeps the original book's title, copyright and license in its data div,
    /// and the user may have reworded the sentence about the original book, which puts their
    /// wording (markup and all) into the data div as userOriginalCopyrightAndLicense.
    /// All of that must survive export to a spreadsheet file and import into a new blank book
    /// (BL-17070).
    /// </summary>
    public class SpreadsheetOriginalCopyrightRoundTripTests
    {
        static SpreadsheetOriginalCopyrightRoundTripTests()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        private const string kUsersNotice =
            "<p>Adapté de l'original, <em>Zebras &amp; other cool animals</em>, Copyright © 1910, First Copyright Owner. Licence sous CC BY 4.0.</p>";

        private const string kSourceBook =
            @"<html>
<head></head>
<body data-l1=""fr"" data-l2="""" data-l3="""">
	<div id=""bloomDataDiv"">
		<div data-book=""bookTitle"" lang=""fr"">zèbres</div>
		<div data-book=""copyright"" lang=""*"">Copyright © 2026, Colin</div>
		<div data-book=""licenseUrl"" lang=""*"">http://creativecommons.org/licenses/by/4.0/</div>
		<div data-book=""originalTitle"" lang=""*"">Zebras &amp; other cool animals</div>
		<div data-book=""originalCopyright"" lang=""*"">Copyright © 2010, First Copyright Owner</div>
		<div data-book=""originalLicenseUrl"" lang=""*"">http://creativecommons.org/licenses/by/4.0/</div>
		<div data-book=""userOriginalCopyrightAndLicense"" lang=""*"">"
            + kUsersNotice
            + @"</div>
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
</body>
</html>";

        private HtmlDom _importedDom;

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            var sourceDom = new HtmlDom(kSourceBook, true);
            // Sanity check: the source book carries the user's wording as markup.
            Assert.That(
                sourceDom
                    .GetBookSetting("userOriginalCopyrightAndLicense")
                    .GetExactAlternative("*"),
                Is.EqualTo(kUsersNotice)
            );
            _importedDom = new HtmlDom(kBlankBook, true);
            Assert.That(
                _importedDom.GetBookSetting("userOriginalCopyrightAndLicense").Empty,
                Is.True,
                "the blank book should start without the user's wording"
            );

            var mockLangDisplayNameResolver = new Mock<ILanguageDisplayNameResolver>();
            mockLangDisplayNameResolver
                .Setup(x => x.GetLanguageDisplayName(It.IsAny<string>()))
                .Returns("French");
            var exporter = new SpreadsheetExporter(mockLangDisplayNameResolver.Object);
            exporter.Params = new SpreadsheetExportParams();
            var sheet = exporter.Export(sourceDom, "fakeImagesFolderpath");
            using (var tempFile = TempFile.WithExtension("xlsx"))
            {
                sheet.WriteToFile(tempFile.Path);
                var sheetFromFile = InternalSpreadsheet.ReadFromFile(tempFile.Path);
                var importer = new TestSpreadsheetImporter(null, _importedDom);
                await importer.ImportAsync(sheetFromFile);
            }
        }

        [TestCase("originalTitle", "Zebras &amp; other cool animals")]
        [TestCase("originalCopyright", "Copyright © 2010, First Copyright Owner")]
        [TestCase("originalLicenseUrl", "http://creativecommons.org/licenses/by/4.0/")]
        [TestCase("copyright", "Copyright © 2026, Colin")]
        [TestCase("licenseUrl", "http://creativecommons.org/licenses/by/4.0/")]
        [TestCase("userOriginalCopyrightAndLicense", kUsersNotice)]
        public void DataDivValueSurvives(string key, string expected)
        {
            Assert.That(
                _importedDom.GetBookSetting(key).GetExactAlternative("*"),
                Is.EqualTo(expected)
            );
        }
    }
}
