using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Spreadsheet;
using SIL.IO;
using NUnit.Framework;
using System.IO;
using BloomTemp;
using System.Xml;
using System;
using System.Threading.Tasks;

namespace BloomTests.Spreadsheet
{
    public class SpreadsheetImporterWithBookTests
    {
        private TemporaryFolder _testFolder;
        private TemporaryFolder _bookFolder;
        private SpreadsheetImporter _importer;
        private HtmlDom _dom;
        private XmlElement _resultElement;

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _dom = null;
            _importer = null;
            _testFolder?.Dispose();
        }

        [OneTimeSetUp]
        public async Task OneTimeSetup()
        {
            _testFolder = new TemporaryFolder("SpreadsheetImporterWithBookTests");
            // We need 2 layers of temp folder because BringBookUpToDate will change the name of the book
            // folder to match an imported title.
            _bookFolder = new TemporaryFolder(_testFolder, "Book");
            var settings = new NewCollectionSettings();
            settings.Language1.Tag = "en";
            settings.Language1.SetName("English", false);
            settings.SettingsFilePath = Path.Combine(_bookFolder.FolderPath, "dummy");

            var fileLocator = new BloomFileLocator(
                settings,
                new XMatterPackFinder(new string[] { }),
                ProjectContext.GetFactoryFileLocations(),
                ProjectContext.GetFoundFileLocations(),
                ProjectContext.GetAfterXMatterFileLocations()
            );
            var bookFilePath = Path.Combine(_bookFolder.FolderPath, "testBook.htm");
            if (File.Exists(bookFilePath)) // Shouldn't ever happen, but... just being careful.
            {
                RobustFile.Delete(bookFilePath);
            }
            _dom = SetupTestDom();
            // Write out our test book
            File.WriteAllText(bookFilePath, _dom.RawDom.OuterXml.ToString());
            var storage = new BookStorage(
                _bookFolder.FolderPath,
                fileLocator,
                new BookRenamedEvent(),
                settings
            );

            var book = new Bloom.Book.Book(
                new BookInfo(_bookFolder.FolderPath, true),
                storage,
                null,
                settings,
                new PageListChangedEvent(),
                new BookRefreshEvent()
            );

            // Create the regular production importer
            _importer = new SpreadsheetImporter(null, book, _bookFolder.FolderPath);

            // Set up the internal spreadsheet rows directly.
            var ss = new InternalSpreadsheet();
            var columnForEn = ss.AddColumnForLang("en", "English");
            var columnForImage = ss.GetColumnForTag(InternalSpreadsheet.ImageSourceColumnLabel);

            var newTitle = "My new book title";
            var titleRow = new ContentRow(ss);
            titleRow.AddCell(InternalSpreadsheet.BookTitleRowLabel);
            titleRow.SetCell(columnForEn, newTitle);

            var coverImageRow = new ContentRow(ss);
            coverImageRow.AddCell(InternalSpreadsheet.CoverImageRowLabel);
            coverImageRow.SetCell(columnForImage, Path.Combine("images", "Othello 199.jpg"));

            var justTextRow = new ContentRow(ss);
            justTextRow.AddCell(InternalSpreadsheet.PageContentRowLabel);
            justTextRow.SetCell(columnForEn, "this is page 1");

            await _importer.ImportAsync(ss);

            _resultElement = ReadResultingBookToXml(newTitle);
        }

        private HtmlDom SetupTestDom()
        {
            // Create an HtmlDom for a template to import into
            var xml = string.Format(templateDom, coverPage + pageWithJustText);
            return new HtmlDom(xml, true);
        }

        public static string templateDom =
            @"
<!DOCTYPE html>

<html>
<head>
</head>

<body data-l1=""es"" data-l2="""" data-l3="""">
	<div id=""bloomDataDiv"">
		<div data-book=""bookTitle"" lang=""en"">
			<p><em>Pineapple</em></p>

            <p>Farm</p>

		</div>
        <div data-book=""topic"" lang=""en"">
            Health
		</div>
		<div data-book=""coverImage"" lang=""*"" src=""cover.png"" alt=""This picture, placeHolder.png, is missing or was loading too slowly."">
			cover.png
		</div>
		<div data-book=""licenseImage"" lang= ""*"" >
			license.png
		</div>
		<div data-book=""outside-back-cover-branding-bottom-html"" lang=""*""><img class=""branding"" src=""BloomWithTaglineAgainstLight.svg"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img></div>
	</div>
	{0}
</body>
</html>
";

        public static string coverPage =
            @"<div class=""bloom-page cover coverColor bloom-frontMatter frontCover outsideFrontCover A5Portrait side-right"" data-page=""required singleton"" data-export=""front-matter-cover"" id=""bc85989e-9503-4f4e-b12f-f83a4002937f"">
        <div class=""pageLabel"" lang=""en"">
            Front Cover
        </div>
        <div class=""pageDescription"" lang=""en""></div>

        <div class=""marginBox"">
            <div class=""bloom-translationGroup bookTitle"" data-default-languages=""V,N1"">
                <label class=""bubble"">Book title in {lang}</label>
                <div class=""bloom-editable bloom-nodefaultstylerule Title-On-Cover-style"" lang=""z"" contenteditable=""true"" data-book=""bookTitle""></div>
                <div class=""bloom-editable bloom-nodefaultstylerule Title-On-Cover-style bloom-contentNational2"" lang=""de"" contenteditable=""true"" data-book=""bookTitle""></div>
                <div class=""bloom-editable bloom-nodefaultstylerule Title-On-Cover-style bloom-contentNational1 bloom-visibility-code-on"" lang=""ar"" contenteditable=""true"" data-book=""bookTitle"" dir=""rtl""></div>

                <div class=""bloom-editable bloom-nodefaultstylerule Title-On-Cover-style bloom-content1 bloom-visibility-code-on"" lang=""ksf"" contenteditable=""true"" data-book=""bookTitle"">
                    <p>a lion book</p>
                </div>

                <div class=""bloom-editable bloom-nodefaultstylerule Title-On-Cover-style"" lang=""en"" contenteditable=""true"" data-book=""bookTitle"">
                    <p>Another lion book</p>
                </div>
                <div class=""bloom-editable bloom-nodefaultstylerule Title-On-Cover-style"" lang=""*"" contenteditable=""true"" data-book=""bookTitle""></div>
            </div>
            <div class=""bloom-imageContainer bloom-backgroundImage"" data-book=""coverImage"" style=""background-image:url('some image.jpg')"" data-copyright=""Copyright, SIL International 2009."" data-creator="""" data-license=""cc-by-nd""></div>

            <div class=""bottomBlock"">
                <img class=""branding"" src=""/bloom/api/branding/image?id=cover-bottom-left.svg"" type=""image/svg"" onerror=""this.style.display='none'""></img> 

                <div class=""bottomTextContent"">
                    <div class=""creditsRow"" data-hint=""You may use this space for author/illustrator, or anything else."">
                        <div class=""bloom-translationGroup"" data-default-languages=""V"">
                            <div class=""bloom-editable Cover-Default-style"" lang=""z"" contenteditable=""true"" data-book=""smallCoverCredits""></div>
                            <div class=""bloom-editable Cover-Default-style bloom-contentNational2"" lang=""de"" contenteditable=""true"" data-book=""smallCoverCredits""></div>
                            <div class=""bloom-editable Cover-Default-style bloom-contentNational1"" lang=""ar"" contenteditable=""true"" data-book=""smallCoverCredits"" dir=""rtl""></div>
                            <div class=""bloom-editable Cover-Default-style bloom-content1 bloom-visibility-code-on"" lang=""ksf"" contenteditable=""true"" data-book=""smallCoverCredits""></div>
                        </div>
                    </div>

                    <div class=""bottomRow"" data-have-topic=""false"">
                        <div class=""coverBottomLangName Cover-Default-style"" data-book=""languagesOfBook"">
                            Bafia
                        </div>

                        <div class=""coverBottomBookTopic bloom-userCannotModifyStyles bloom-alwaysShowBubble Cover-Default-style"" data-derived=""topic"" data-functiononhintclick=""ShowTopicChooser()"" data-hint=""Click to choose topic""></div>
                    </div>
                </div>
            </div>
        </div>
    </div>";

        public static string pageWithJustText =
            @"<div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-left bloom-monolingual"" data-page="""" id=""dc90dbe0-7584-4d9f-bc06-0e0326060054"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""1"" lang="""">
			<div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Basic Text &amp; Picture"" lang=""en"">
				Basic Text &amp; Picture
			</div>

			<div class=""pageDescription"" lang=""en""></div>

			<div class=""split-pane-component marginBox"" style="""">
				<div class=""split-pane horizontal-percent"" style=""min-height: 42px;"">
					<div class=""split-pane-component position-top"">
						<div class=""split-pane-component-inner"" min-width=""60px 150px 250px"" min-height=""60px 150px 250px"">
							<div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
							   <div class=""bloom-editable normal-style"" style="""" lang=""z"" contenteditable=""true"">
									<p></p>
								</div>
							</div>
						</div>
					</div>
				</div>
			</div>
		</div>";

        private XmlElement ReadResultingBookToXml(string newTitle)
        {
            // _bookFolder is no longer accurate here, since BringBookUpToDate has renamed the folder
            // to match the new title.
            var bookPath = Path.Combine(_testFolder.FolderPath, newTitle, newTitle + ".htm");
            if (!File.Exists(bookPath))
                return null;
            var dom = new XmlDocument();
            try
            {
                dom.LoadXml(RobustFile.ReadAllText(bookPath));
            }
            catch (Exception)
            {
                return null;
            }
            return dom.DocumentElement;
        }

        [Test]
        public void BookTitleGetsPersisted()
        {
            var datadivdom = AssertThatXmlIn.Element(
                _resultElement.SelectSingleNode("//div[@id='bloomDataDiv']") as XmlElement
            );
            var frontCoverDom = AssertThatXmlIn.Element(
                _resultElement.SelectSingleNode("//div[contains(@class, 'frontCover')]")
                    as XmlElement
            );

            // Check DataDiv title
            datadivdom.HasSpecifiedNumberOfMatchesForXpath(
                "./div[@data-book='bookTitle' and @lang='en' and contains(text(), 'My new book title')]",
                1
            );
            datadivdom.HasNoMatchForXpath(
                "./div[@data-book='bookTitle' and @lang='en' and contains(text(), 'Another lion book')]"
            );

            // Check Front Cover title
            frontCoverDom.HasNoMatchForXpath(
                ".//div[@data-book='bookTitle' and @lang='en' and contains(text(), 'Another lion book')]"
            );
            frontCoverDom.HasSpecifiedNumberOfMatchesForXpath(
                ".//div[@data-book='bookTitle' and @lang='en' and contains(text(), 'My new book title')]",
                1
            );
        }

        [Test]
        public void FrontCoverImageGetsPersisted()
        {
            var datadivdom = AssertThatXmlIn.Element(
                _resultElement.SelectSingleNode("//div[@id='bloomDataDiv']") as XmlElement
            );
            var frontCoverDom = AssertThatXmlIn.Element(
                _resultElement.SelectSingleNode("//div[contains(@class, 'frontCover')]")
                    as XmlElement
            );

            // Check DataDiv front cover image
            datadivdom.HasSpecifiedNumberOfMatchesForXpath(
                "./div[@data-book='coverImage' and @lang='*' and contains(text(), 'Othello 199.jpg')]",
                1
            );
            datadivdom.HasNoMatchForXpath(
                "./div[@data-book='coverImage' and @lang='*' and contains(text(), 'some image.jpg')]"
            );

            // Check Front Cover image
            frontCoverDom.HasNoMatchForXpath(
                "//img[@data-book='coverImage' and @src='some image.jpg']"
            );
            frontCoverDom.HasSpecifiedNumberOfMatchesForXpath(
                "//img[@data-book='coverImage' and @src='Othello%20199.jpg']",
                1
            );
        }

        // At one point, if we had a real CollectionSettings object, the importer just updated the
        // side-classes (.side-right/.side-left). For that change, we had a fairly elaborate test to see if
        // the side-classes for each page were updated correctly.
        // Since then we've updated the importer to just call BringBookUpToDate() after the import process.
        // That does an already well-tested set of things that includes setting the side-classes,
        // so I think this simple test is enough to see that the update process happened correctly.
        [Test]
        public void PageHasCorrectSideClass()
        {
            var pageWithJustTextDom = AssertThatXmlIn.Element(
                _resultElement.SelectSingleNode("//div[@id='dc90dbe0-7584-4d9f-bc06-0e0326060054']")
                    as XmlElement
            );
            // This page we added started out as 'side-left'.
            // The xmatter update process changes our frontCover xmatter to frontCoverPage +
            // insideFrontCoverPage + titlePage + creditsPage. This switches the side-left to side-right.
            var expectedClass = "side-right";
            var unexpectedClass = "side-left";
            pageWithJustTextDom.HasNoMatchForXpath(
                $"self::div[contains(@class, '" + unexpectedClass + "')]"
            );
            pageWithJustTextDom.HasSpecifiedNumberOfMatchesForXpath(
                $"self::div[contains(@class, '" + expectedClass + "')]",
                1
            );
        }
    }
}
