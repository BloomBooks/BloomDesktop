using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Bloom;
using Bloom.Book;
using Bloom.Spreadsheet;
using BloomTemp;
using BloomTests.TeamCollection;
using Moq;
using NUnit.Framework;
using OfficeOpenXml;
using OfficeOpenXml.Drawing;
using SIL.IO;

namespace BloomTests.Spreadsheet
{
    /// <summary>
    /// This class tests exporting a book with images to a spreadsheet and
    /// verifying that image related information appears properly.
    /// </summary>
    public class SpreadsheetImagesTests
    {
        // re-use the images from another test (added empty file empty-file.jpg for these tests)
        private const string _pathToTestImages = "src/BloomTests/ImageProcessing/images";
        public const string imageBook =
            @"

<html>
<head>
</head>

<body data-l1=""en"" data-l2="""" data-l3="""">
	<div id=""bloomDataDiv"">
        <div data-book=""outside-back-cover-bottom-html"" lang=""*""><img class=""branding"" src=""BloomWithTaglineAgainstLight.svg"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img></div>
	</div>
    <div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-right bloom-monolingual"" data-page="""" id=""bdf2acc2-1ea1-4f70-9e36-6bcee3613752"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398384"" data-page-number=""1"" lang="""">
        <div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Image on Bottom"" lang=""en"">
            Image on Bottom
        </div>

        <div class=""pageDescription"" lang=""en""></div>

        <div class=""marginBox"">
            <div class=""split-pane horizontal-percent"" style=""min-height: 42px;"">
                <div class=""split-pane-component position-top"" style=""bottom: 30.1471%;"">
                    <div class=""split-pane horizontal-percent"" style=""min-height: 42px;"">
                        <div class=""split-pane-component position-top"">
                            <div class=""split-pane-component-inner"" min-width=""60px 150px 250px"" min-height=""60px 150px"">
                                <div class=""bloom-canvas bloom-leadingElement bloom-has-canvas-element"" title=""Name: man.jpg Size: 178.00 kb Dots: 1041 x 781 For the current paper size: • The image container is 406 x 231 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ✓ This image would print at 325 DPI. • An image with 1269 x 722 dots would fill this container at 300 DPI."">
									<div class=""bloom-canvas-element bloom-backgroundImage"" style=""width: 100px; height: 100px"">
										<div class=""bloom-imageContainer"">
											<img src=""man.jpg"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img>
										</div>
									</div>
									<div class=""bloom-translationGroup bloom-imageDescription bloom-trailingElement"" data-default-languages=""auto"">
					                    <div class=""bloom-editable ImageDescriptionEdit-style"" lang=""z"" contenteditable=""true"" data-book=""coverImageDescription""></div>
					                    <div class=""bloom-editable ImageDescriptionEdit-style bloom-content1 bloom-visibility-code-on"" lang=""en"" contenteditable=""true"" >A picture of a man</div>
					                </div>
								</div>
                            </div>
                        </div>

                        <div class=""split-pane-divider horizontal-divider""></div>

                        <div class=""split-pane-component position-bottom"">
                            <div class=""split-pane-component-inner"" min-width=""60px 150px 250px"" min-height=""60px 150px"">
                                <div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
                                    <div class=""bloom-editable normal-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""English"" lang=""en"" contenteditable=""true"">
                                        <p>I am going to outer space.</p>
                                    </div>

                                    <div class=""bloom-editable normal-style"" style="""" lang=""z"" contenteditable=""true"">
                                        <p></p>
                                    </div>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>

                <div class=""split-pane-divider horizontal-divider"" style=""bottom: 30.1471%;"" title=""69.9%""></div>

                <div class=""split-pane-component position-bottom"" style=""height: 30.1471%;"">
                    <div class=""split-pane-component-inner"" min-width=""60px 150px 250px"" min-height=""60px 150px"">
                        <div class=""bloom-canvas bloom-has-canvas-element"" title=""Name: Mars 2.png Size: 130.10 kb Dots: 1041 x 447 For the current paper size: • The image container is 406 x 203 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ⚠ This image would print at 246 DPI. • An image with 1269 x 635 dots would fill this container at 300 DPI."">
							<div class=""bloom-canvas-element bloom-backgroundImage"" style=""width: 100px; height: 100px"">
								<div class=""bloom-imageContainer"">
									<img src=""Mars%202.png"" alt="""" data-copyright="""" data-creator="""" data-license=""cc-by""></img>
								</div>
							</div>
						</div>
						<div class=""bloom-canvas bloom-has-canvas-element"" title=""Name:missing file.jpg Size: 130.10 kb Dots: 1041 x 447 For the current paper size: • The image container is 406 x 203 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ⚠ This image would print at 246 DPI. • An image with 1269 x 635 dots would fill this container at 300 DPI."">
							<div class=""bloom-canvas-element bloom-backgroundImage"" style=""width: 100px; height: 100px"">
								<div class=""bloom-imageContainer"">
									<img src=""missing%20file.jpg"" alt="""" data-copyright="""" data-creator="""" data-license=""cc-by""></img>
								</div>
							</div>
						</div>
                    </div>
                </div>
            </div>
        </div>
    </div>
<div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-left bloom-monolingual"" data-page="""" id=""f3262bcc-ccea-458c-857c-24ddc15462f7"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""2"" lang="""">
        <div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Basic Text &amp; Image"" lang=""en"">
            Basic Text &amp; Image
        </div>

        <div class=""pageDescription"" lang=""en""></div>

        <div class=""marginBox"">
			<div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
                <div class=""bloom-editable normal-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""English"" lang=""en"" contenteditable=""true"">
                    <p>Outer space is fascinating.</p>
                </div>

                <div class=""bloom-editable normal-style"" style="""" lang=""z"" contenteditable=""true"">
                    <p></p>
                </div>
            </div>
            <div class=""split-pane horizontal-percent"" style=""min-height: 42px;"">
                <div class=""split-pane-component position-top"" style=""bottom: 50%"">
                    <div class=""split-pane-component-inner"">
                        <div class=""bloom-canvas bloom-leadingElement bloom-has-canvas-element"" title=""Name: empty-file.jpg Size: 3.86 kb Dots: 225 x 225 For the current paper size: • The image container is 406 x 335 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ⚠ This image would print at 64 DPI. • An image with 1269 x 1047 dots would fill this container at 300 DPI."">
							<div class=""bloom-canvas-element bloom-backgroundImage"" style=""width: 100px; height: 100px"">
								<div class=""bloom-imageContainer"">
									<img src=""empty-file.jpg"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img>
								</div>
							</div>
						</div>
                    </div>
                </div>

                <div class=""split-pane-divider horizontal-divider"" style=""bottom: 50%""></div>

                <div class=""split-pane-component position-bottom"" style=""height: 50%"">
                    <div class=""split-pane-component-inner"">
                        <div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
                            <div class=""bloom-editable normal-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""English"" lang=""en"" contenteditable=""true"">
                                <p>Outer space is very scary.</p>
                            </div>

                            <div class=""bloom-editable normal-style"" style="""" lang=""z"" contenteditable=""true"">
                                <p></p>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
	<div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-left bloom-monolingual"" data-page="""" id=""f3262bcc-ccea-458c-857c-24ddc15462f7"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""2"" lang="""">
        <div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Basic Text &amp; Image"" lang=""en"">
            Basic Text &amp; Image
        </div>

        <div class=""pageDescription"" lang=""en""></div>

        <div class=""marginBox"">
			<div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
                <div class=""bloom-editable normal-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""English"" lang=""en"" contenteditable=""true"">
                    <p>This page has only text</p>
                </div>

                <div class=""bloom-editable normal-style"" style="""" lang=""z"" contenteditable=""true"">
                    <p></p>
                </div>
            </div>
        </div>
    </div>
	<div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-left bloom-monolingual"" data-page="""" id=""f3262bcc-ccea-458c-857c-24ddc15462f7"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""2"" lang="""">
        <div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Basic Text &amp; Image"" lang=""en"">
            Basic Text &amp; Image
        </div>
        <div class=""pageDescription"" lang=""en""></div>
        <div class=""marginBox"">
			<div class=""bloom-canvas bloom-leadingElement bloom-has-canvas-element"" title=""Name: lady24b.png Size: 3.86 kb Dots: 225 x 225 For the current paper size: • The image container is 406 x 335 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ⚠ This image would print at 64 DPI. • An image with 1269 x 1047 dots would fill this container at 300 DPI."">
				<div class=""bloom-canvas-element bloom-backgroundImage"" style=""width: 100px; height: 100px"">
					<div class=""bloom-imageContainer"">
						<img src=""lady24b.png"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img>
					</div>
				</div>
			</div>
			<div class=""bloom-canvas bloom-leadingElement bloom-has-canvas-element"" title=""Name: placeHolder.png Size: 3.86 kb Dots: 225 x 225 For the current paper size: • The image container is 406 x 335 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ⚠ This image would print at 64 DPI. • An image with 1269 x 1047 dots would fill this container at 300 DPI."">
				<div class=""bloom-canvas-element bloom-backgroundImage"" style=""width: 100px; height: 100px"">
					<div class=""bloom-imageContainer"">
						<img src=""placeHolder.png""></img>
					</div>
				</div>
			</div>
        </div>
    </div>
    <div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-left bloom-monolingual"" data-page="""" id=""b6dcaece-11a0-49d3-80c7-a83b7a8d9b6f"" data-pagelineage=""eabed994-4cdf-4dd5-aa40-196528c2bc55"" data-page-number=""3"" lang="""">
        <div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Basic Text &amp; Image"" lang=""en"">
            Basic Text &amp; Image
        </div>
        <div class=""pageDescription"" lang=""en""></div>
        <div class=""marginBox"">
            <div class=""bloom-canvas bloom-leadingElement"" data-test-id=""ic{4}""><img src=""Othello 199.jpg"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img></div>
        </div>
    </div>
</body>
</html>
";
        private SpreadsheetExporter _exporter;

        // The tests are all written in terms of _sheet and _rows, the output
        // of an export operation. But we create two sheets, one by export, and
        // one by writing the first to file and reading it back. We want to apply
        // the same tests to each. This is currently achieved by using the test
        // case to select one pair (_sheetFromExport, _rowsFromExport)
        // or (_sheetFromFile, _rowsFromFile) to set as _sheet and _rows.
        private InternalSpreadsheet _sheet;
        private List<ContentRow> _rows;
        private List<ContentRow> _pageContentRows;
        private InternalSpreadsheet _sheetFromExport;
        private List<ContentRow> _rowsFromExport;
        private InternalSpreadsheet _sheetFromFile;
        private List<ContentRow> _rowsFromFile;
        private TemporaryFolder _testFolder;
        private TemporaryFolder _spreadsheetFolder;
        private TemporaryFolder _bookFolder;
        private ProgressSpy _progressSpy;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var dom = new HtmlDom(imageBook, true);

            _testFolder = SpreadsheetTestFolders.MakeFolderFor(this);
            _spreadsheetFolder = new TemporaryFolder(_testFolder, "Spreadsheet");
            _bookFolder = new TemporaryFolder(_testFolder, "Book");

            var mockLangDisplayNameResolver = new Mock<ILanguageDisplayNameResolver>();
            mockLangDisplayNameResolver
                .Setup(x => x.GetLanguageDisplayName("en"))
                .Returns("English");

            _exporter = new SpreadsheetExporter(mockLangDisplayNameResolver.Object);
            var path = SIL.IO.FileLocationUtilities.GetDirectoryDistributedWithApplication(
                _pathToTestImages
            );

            // We need all these files in one place so we can verify that all of them get copied except placeHolder.png
            foreach (
                var name in new[]
                {
                    "BloomWithTaglineAgainstLight.svg",
                    "man.jpg",
                    "Mars 2.png",
                    "lady24b.png",
                    "empty-file.jpg",
                    "Othello 199.jpg",
                }
            )
                RobustFile.Copy(
                    Path.Combine(path, name),
                    Path.Combine(_bookFolder.FolderPath, name)
                );

            _progressSpy = new ProgressSpy();
            _sheetFromExport = _exporter.ExportToFolder(
                dom,
                _bookFolder.FolderPath,
                _spreadsheetFolder.FolderPath,
                out string outputPath,
                _progressSpy,
                OverwriteOptions.Overwrite
            );
            _rowsFromExport = _sheetFromExport.ContentRows.ToList();
            _sheetFromFile = InternalSpreadsheet.ReadFromFile(outputPath);
            _rowsFromFile = _sheetFromFile.ContentRows.ToList();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            // This also removes the folders nested inside it.
            _testFolder?.Dispose();
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

            _pageContentRows = _rows
                .Where(r => r.MetadataKey == InternalSpreadsheet.PageContentRowLabel)
                .ToList();
        }

        [TestCase("fromExport")]
        [TestCase("fromFile")]
        public void AddsImagePageNumbers(string source)
        {
            SetupFor(source);
            var pageNumberIndex = _sheet.GetColumnForTag(InternalSpreadsheet.PageNumberColumnLabel);
            Assert.That(_pageContentRows[0].GetCell(pageNumberIndex).Content, Is.EqualTo("1"));
            Assert.That(_pageContentRows[1].GetCell(pageNumberIndex).Content, Is.EqualTo("1"));
            Assert.That(_pageContentRows[2].GetCell(pageNumberIndex).Content, Is.EqualTo("1"));
            Assert.That(_pageContentRows[3].GetCell(pageNumberIndex).Content, Is.EqualTo("2"));
        }

        // This case doesn't really 'belong' here but it's a convenient place to check that
        // we don't create audio columns unless the document has audio, without creating another whole
        // DOM to test in the audio tests.
        [TestCase("fromExport")]
        public void HasNoAudioColumns(string source)
        {
            SetupFor(source);
            Assert.That(_sheet.Header.GetRow(0).CellContents, Has.None.Match(".*audio.*"));
        }

        [TestCase("fromExport")]
        [TestCase("fromFile")]
        public void AddsRowLabels(string source)
        {
            SetupFor(source);
            Assert.That(
                _pageContentRows[0].GetCell(0).Content,
                Is.EqualTo(InternalSpreadsheet.PageContentRowLabel)
            );
            Assert.That(
                _pageContentRows[1].GetCell(0).Content,
                Is.EqualTo(InternalSpreadsheet.PageContentRowLabel)
            );
            Assert.That(
                _pageContentRows[2].GetCell(0).Content,
                Is.EqualTo(InternalSpreadsheet.PageContentRowLabel)
            );
        }

        [Test]
        public void CopiesImagesToDestFolder()
        {
            var destImageFolder = Path.Combine(_spreadsheetFolder.FolderPath, "images");
            Assert.That(Directory.Exists(destImageFolder));
            Assert.That(
                File.Exists(Path.Combine(destImageFolder, "BloomWithTaglineAgainstLight.svg"))
            );
            Assert.That(File.Exists(Path.Combine(destImageFolder, "man.jpg")));
            Assert.That(File.Exists(Path.Combine(destImageFolder, "Mars 2.png")));
            Assert.That(File.Exists(Path.Combine(destImageFolder, "lady24b.png")));
            Assert.That(File.Exists(Path.Combine(destImageFolder, "Othello 199.jpg")));
            Assert.That(File.Exists(Path.Combine(destImageFolder, "placeHolder.png")), Is.False);
        }

        [TestCase("fromExport")]
        [TestCase("fromFile")]
        public void ExportsLegacyImageStructure(string source)
        {
            SetupFor(source);
            var legacyImagePath = Path.Combine("images", "Othello 199.jpg");
            var legacyImageRow = _pageContentRows.FirstOrDefault(x =>
                x.GetCell(InternalSpreadsheet.ImageSourceColumnLabel).Text == legacyImagePath
            );
            Assert.That(legacyImageRow, Is.Not.Null);
            Assert.That(legacyImageRow.GetCell("[en]").Text, Is.EqualTo(""));
        }

        [TestCase("fromExport")]
        [TestCase("fromFile")]
        public void SavesImageSources(string source)
        {
            SetupFor(source);
            var imageSourceColumn = _sheet.GetColumnForTag(
                InternalSpreadsheet.ImageSourceColumnLabel
            );
            var path = SIL.IO.FileLocationUtilities.GetDirectoryDistributedWithApplication(
                _pathToTestImages
            );
            var manImagePath = Path.Combine("images", "man.jpg");
            Assert.That(
                _pageContentRows[0].GetCell(imageSourceColumn).Text,
                Is.EqualTo(manImagePath)
            );
            var marsImagePath = Path.Combine("images", "Mars 2.png");
            Assert.That(
                _pageContentRows[1].GetCell(imageSourceColumn).Text,
                Is.EqualTo(marsImagePath)
            );
            var missingFileImagePath = Path.Combine("images", "missing file.jpg");
            Assert.That(
                _pageContentRows[2].GetCell(imageSourceColumn).Text,
                Is.EqualTo(missingFileImagePath)
            );
            var emptyFileImagePath = Path.Combine("images", "empty-file.jpg");
            Assert.That(
                _pageContentRows[3].GetCell(imageSourceColumn).Text,
                Is.EqualTo(emptyFileImagePath)
            );
            Assert.That(_pageContentRows[4].GetCell(imageSourceColumn).Text, Is.EqualTo("")); // no more images, but a second text group on P2
            Assert.That(_pageContentRows[5].GetCell(imageSourceColumn).Text, Is.EqualTo("")); // no images, but there is text on P3
            var ladyImagePath = Path.Combine("images", "lady24b.png");
            Assert.That(
                _pageContentRows[6].GetCell(imageSourceColumn).Text,
                Is.EqualTo(ladyImagePath)
            );
            Assert.That(
                _pageContentRows[7].GetCell(imageSourceColumn).Text,
                Is.EqualTo(InternalSpreadsheet.BlankContentIndicator)
            );
        }

        [TestCase("fromExport")]
        [TestCase("fromFile")]
        public void PutsTextWithImages(string source)
        {
            SetupFor(source);
            Assert.That(
                _pageContentRows[0].GetCell("[en]").Text,
                Is.EqualTo("I am going to outer space.")
            );
            Assert.That(_pageContentRows[1].GetCell("[en]").Text, Is.EqualTo("")); // two images on P1, but no more text
            Assert.That(_pageContentRows[2].GetCell("[en]").Text, Is.EqualTo("")); // two images on P1, but no more text
            Assert.That(
                _pageContentRows[3].GetCell("[en]").Text,
                Is.EqualTo("Outer space is fascinating.")
            );
            Assert.That(
                _pageContentRows[4].GetCell("[en]").Text,
                Is.EqualTo("Outer space is very scary.")
            );
            Assert.That(
                _pageContentRows[5].GetCell("[en]").Text,
                Is.EqualTo("This page has only text")
            );
            Assert.That(_pageContentRows[6].GetCell("[en]").Text, Is.EqualTo("")); // no text on P4
        }

        [TestCase("fromFile")] //Images are embedded during writing of .xlsx file
        public void displayThumbnail_imageFilePresent_noErrorMessage(string source)
        {
            SetupFor(source);
            var thumbnailColumn = _sheet.GetColumnForTag(
                InternalSpreadsheet.ImageThumbnailColumnLabel
            );
            var goodImageFileRow = _pageContentRows.First(x =>
                x.GetCell(InternalSpreadsheet.ImageSourceColumnLabel).Text.Contains("man.jpg")
            );
            Assert.That(goodImageFileRow.GetCell(thumbnailColumn).Text, Is.EqualTo(""));
        }

        [TestCase("fromFile")]
        public void displayThumbnail_imageMissing_ErrorMessageForMissingFile(string source)
        {
            SetupFor(source);
            var thumbnailColumn = _sheet.GetColumnForTag(
                InternalSpreadsheet.ImageThumbnailColumnLabel
            );
            var missingFileRow = _pageContentRows.First(x =>
                x.GetCell(InternalSpreadsheet.ImageSourceColumnLabel)
                    .Text.Contains("missing file.jpg")
            );
            Assert.That(missingFileRow.GetCell(thumbnailColumn).Text, Is.EqualTo("Missing"));
        }

        [TestCase("fromFile")]
        public void displayThumbnail_imageEmpty_ErrorMessage(string source)
        {
            SetupFor(source);
            var thumbnailColumn = _sheet.GetColumnForTag(
                InternalSpreadsheet.ImageThumbnailColumnLabel
            );
            Assert.That(
                _pageContentRows[3].GetCell(thumbnailColumn).Text,
                Is.EqualTo("Bad image file")
            );
        }

        [TestCase("fromFile")]
        public void displayThumbnail_svg_svgErrorMessage(string source)
        {
            SetupFor(source);
            var thumbnailColumn = _sheet.GetColumnForTag(
                InternalSpreadsheet.ImageThumbnailColumnLabel
            );
            var svgRow = _rows.First(x =>
                x.GetCell(InternalSpreadsheet.RowTypeColumnLabel)
                    .Text.Equals("[outside-back-cover-bottom-html]")
            );
            Assert.That(svgRow.GetCell(thumbnailColumn).Text, Is.EqualTo("Can't display SVG"));
        }

        // A minimal book whose three images all reduce to the same EPPlus drawing name once the
        // extension is dropped: "conflict.png" and "conflict.gif" match exactly, and "Conflict.jpg"
        // matches them case-insensitively. EPPlus treats drawing names as case-insensitive and
        // rejects duplicates, so this is the situation that used to throw during export (BL-16498).
        private const string conflictingImageNamesBook =
            @"
<html>
<head>
</head>
<body data-l1=""en"" data-l2="""" data-l3="""">
    <div id=""bloomDataDiv""></div>
    <div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-right bloom-monolingual"" data-page="""" id=""aaaaaaaa-1111-1111-1111-111111111111"" data-page-number=""1"" lang="""">
        <div class=""pageLabel"" lang=""en"">Image</div>
        <div class=""pageDescription"" lang=""en""></div>
        <div class=""marginBox"">
            <div class=""bloom-canvas bloom-leadingElement bloom-has-canvas-element"">
                <div class=""bloom-canvas-element bloom-backgroundImage"" style=""width: 100px; height: 100px"">
                    <div class=""bloom-imageContainer"">
                        <img src=""conflict.png"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img>
                    </div>
                </div>
            </div>
        </div>
    </div>
    <div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-left bloom-monolingual"" data-page="""" id=""bbbbbbbb-2222-2222-2222-222222222222"" data-page-number=""2"" lang="""">
        <div class=""pageLabel"" lang=""en"">Image</div>
        <div class=""pageDescription"" lang=""en""></div>
        <div class=""marginBox"">
            <div class=""bloom-canvas bloom-leadingElement bloom-has-canvas-element"">
                <div class=""bloom-canvas-element bloom-backgroundImage"" style=""width: 100px; height: 100px"">
                    <div class=""bloom-imageContainer"">
                        <img src=""Conflict.jpg"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img>
                    </div>
                </div>
            </div>
        </div>
    </div>
    <div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-right bloom-monolingual"" data-page="""" id=""cccccccc-3333-3333-3333-333333333333"" data-page-number=""3"" lang="""">
        <div class=""pageLabel"" lang=""en"">Image</div>
        <div class=""pageDescription"" lang=""en""></div>
        <div class=""marginBox"">
            <div class=""bloom-canvas bloom-leadingElement bloom-has-canvas-element"">
                <div class=""bloom-canvas-element bloom-backgroundImage"" style=""width: 100px; height: 100px"">
                    <div class=""bloom-imageContainer"">
                        <img src=""conflict.gif"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img>
                    </div>
                </div>
            </div>
        </div>
    </div>
</body>
</html>
";

        /// <summary>
        /// Regression test for BL-16498: exporting a book whose image file names collide once the
        /// extension is dropped (including names that differ only in case) must embed every image
        /// without error. Before the fix, the second colliding image threw inside EPPlus's
        /// AddPicture (which treats drawing names as case-insensitive and rejects duplicates); the
        /// exporter caught the throw and reported the image as a "Bad image file".
        /// </summary>
        [Test]
        public void Export_ImagesWithConflictingNames_AllEmbeddedWithoutError()
        {
            using (var bookFolder = new TemporaryFolder(_testFolder, "ImageConflict_Book"))
            using (var outputFolder = new TemporaryFolder(_testFolder, "ImageConflict_Out"))
            {
                // Copy one known-good image to three names that collide once the extension is dropped.
                var sourceImage = Path.Combine(
                    SIL.IO.FileLocationUtilities.GetDirectoryDistributedWithApplication(
                        _pathToTestImages
                    ),
                    "man.jpg"
                );
                var conflictingNames = new[] { "conflict.png", "Conflict.jpg", "conflict.gif" };
                foreach (var name in conflictingNames)
                    RobustFile.Copy(sourceImage, Path.Combine(bookFolder.FolderPath, name));

                // Sanity check: the setup really did create three distinct, non-empty image files
                // whose names collide case-insensitively once the extension is removed.
                Assert.That(
                    conflictingNames
                        .Select(n => Path.GetFileNameWithoutExtension(n).ToLowerInvariant())
                        .Distinct()
                        .Count(),
                    Is.EqualTo(1),
                    "Setup sanity check: all test image names should reduce to the same case-insensitive base name."
                );
                foreach (var name in conflictingNames)
                {
                    var path = Path.Combine(bookFolder.FolderPath, name);
                    Assert.That(
                        RobustFile.Exists(path),
                        $"Setup sanity check: {name} should exist."
                    );
                    Assert.That(
                        new FileInfo(path).Length,
                        Is.GreaterThan(0),
                        $"Setup sanity check: {name} should not be empty."
                    );
                }

                var mockLangDisplayNameResolver = new Mock<ILanguageDisplayNameResolver>();
                mockLangDisplayNameResolver
                    .Setup(x => x.GetLanguageDisplayName("en"))
                    .Returns("English");
                var exporter = new SpreadsheetExporter(mockLangDisplayNameResolver.Object);
                var progressSpy = new ProgressSpy();

                var sheet = exporter.ExportToFolder(
                    new HtmlDom(conflictingImageNamesBook, true),
                    bookFolder.FolderPath,
                    outputFolder.FolderPath,
                    out string outputPath,
                    progressSpy,
                    OverwriteOptions.Overwrite
                );

                // Sanity check: all three images made it into the export as image content rows.
                var imageSourceColumn = sheet.GetColumnForTag(
                    InternalSpreadsheet.ImageSourceColumnLabel
                );
                var exportedImageRows = sheet
                    .ContentRows.Where(r =>
                        r.GetCell(imageSourceColumn).Text.ToLowerInvariant().Contains("conflict")
                    )
                    .ToList();
                Assert.That(
                    exportedImageRows.Count,
                    Is.EqualTo(3),
                    "Setup sanity check: expected all three conflicting images to be exported as content rows."
                );

                // The images are actually embedded (and any errors recorded) while the .xlsx file is
                // written, so read the file back to check the results.
                var sheetFromFile = InternalSpreadsheet.ReadFromFile(outputPath);
                var thumbnailColumn = sheetFromFile.GetColumnForTag(
                    InternalSpreadsheet.ImageThumbnailColumnLabel
                );
                foreach (
                    var row in sheetFromFile.ContentRows.Where(r =>
                        r.GetCell(imageSourceColumn).Text.ToLowerInvariant().Contains("conflict")
                    )
                )
                {
                    Assert.That(
                        row.GetCell(thumbnailColumn).Text,
                        Is.EqualTo(""),
                        $"Image '{row.GetCell(imageSourceColumn).Text}' should have embedded without an error, "
                            + "but its thumbnail cell holds an error message (the duplicate-name collision was not resolved)."
                    );
                }

                // And no warning should have been reported about embedding these images.
                Assert.That(
                    progressSpy.Warnings,
                    Is.Empty,
                    "Exporting images with conflicting names should not report any warnings."
                );
            }
        }

        /// <summary>
        /// A minimal book holding a single image, so a test can inspect exactly one thumbnail.
        /// </summary>
        private static string MinimalImageBook(string imageFileName) =>
            $@"
<html><head></head>
<body data-l1=""en"" data-l2="""" data-l3="""">
    <div id=""bloomDataDiv""></div>
    <div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-right bloom-monolingual"" data-page="""" id=""aaaaaaaa-1111-1111-1111-111111111111"" data-page-number=""1"" lang="""">
        <div class=""pageLabel"" lang=""en"">Image</div>
        <div class=""pageDescription"" lang=""en""></div>
        <div class=""marginBox"">
            <div class=""bloom-canvas bloom-leadingElement bloom-has-canvas-element"">
                <div class=""bloom-canvas-element bloom-backgroundImage"" style=""width: 100px; height: 100px"">
                    <div class=""bloom-imageContainer"">
                        <img src=""{imageFileName}"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img>
                    </div>
                </div>
            </div>
        </div>
    </div>
</body></html>";

        /// <summary>
        /// Export a book containing just the one named test image, and return the path of the
        /// spreadsheet written. The caller owns the two temporary folders.
        /// </summary>
        private string ExportBookWithOneImage(
            string imageFileName,
            TemporaryFolder bookFolder,
            TemporaryFolder outputFolder
        )
        {
            RobustFile.Copy(
                Path.Combine(
                    SIL.IO.FileLocationUtilities.GetDirectoryDistributedWithApplication(
                        _pathToTestImages
                    ),
                    imageFileName
                ),
                Path.Combine(bookFolder.FolderPath, imageFileName)
            );

            var mockLangDisplayNameResolver = new Mock<ILanguageDisplayNameResolver>();
            mockLangDisplayNameResolver
                .Setup(x => x.GetLanguageDisplayName("en"))
                .Returns("English");
            var exporter = new SpreadsheetExporter(mockLangDisplayNameResolver.Object);

            exporter.ExportToFolder(
                new HtmlDom(MinimalImageBook(imageFileName), true),
                bookFolder.FolderPath,
                outputFolder.FolderPath,
                out string outputPath,
                new ProgressSpy(),
                OverwriteOptions.Overwrite
            );
            return outputPath;
        }

        /// <summary>
        /// Regression test for BL-16529 (image cells too high). ResizeImageIfNecessary never
        /// enlarges an image, so a source smaller than the thumbnail target is embedded at its
        /// original size. The row must be sized from the thumbnail we actually embed, not from the
        /// larger target; otherwise small images get a row that is much too tall, leaving dead space
        /// below the image.
        ///
        /// The row height no longer depends on the exporting machine's display scaling (the exporter
        /// stamps every thumbnail with 96dpi instead of compensating for the display -- see
        /// Export_Thumbnail_IsStamped96Dpi_SoLayoutDoesNotDependOnTheDisplay), so this can assert
        /// both bounds: the row hugs the image.
        /// </summary>
        [Test]
        public void Export_SmallImage_RowNotSizedFromOversizedTarget()
        {
            using (var bookFolder = new TemporaryFolder(_testFolder, "SmallImageRowHeight_Book"))
            using (var outputFolder = new TemporaryFolder(_testFolder, "SmallImageRowHeight_Out"))
            {
                var sourceImage = Path.Combine(
                    SIL.IO.FileLocationUtilities.GetDirectoryDistributedWithApplication(
                        _pathToTestImages
                    ),
                    "man.jpg"
                );

                int sourceImageHeightPx;
                using (var img = Image.FromFile(sourceImage))
                {
                    // Sanity check: this test only exercises the bug if the source is small enough
                    // that the exporter won't enlarge it (source width < the 150px thumbnail target).
                    Assert.That(
                        img.Width,
                        Is.LessThan(150),
                        "Setup sanity check: the test image must be narrower than the thumbnail target "
                            + "so that it is embedded at its original (un-enlarged) size."
                    );
                    sourceImageHeightPx = img.Height;
                }

                var outputPath = ExportBookWithOneImage("man.jpg", bookFolder, outputFolder);

                using (var package = new ExcelPackage(new FileInfo(outputPath)))
                {
                    var worksheet = package.Workbook.Worksheets[0];
                    var picture = worksheet.Drawings.OfType<ExcelPicture>().Single();
                    // The image is anchored in the row where the exporter placed it (0-based From.Row).
                    var imageRow = worksheet.Row(picture.From.Row + 1);

                    // Row height is in points; convert to its nominal pixels (72 points/inch,
                    // 96 px/inch). The exporter adds a few pixels so the image isn't flush against the
                    // row edge.
                    const double pointsToPixels = 96.0 / 72.0;
                    var rowHeightPx = imageRow.Height * pointsToPixels;

                    // Sanity check: the row is a real, non-trivial height (it was actually set).
                    Assert.That(
                        rowHeightPx,
                        Is.GreaterThan(30),
                        "The image row should have been given a real height."
                    );
                    // The regression check: the row must be sized from the embedded thumbnail, not the
                    // oversized target. The un-enlarged thumbnail is the source image's height, so the
                    // row's nominal height must not exceed that (plus a few px of padding). Before the
                    // fix the row was sized from finalWidth*aspect (e.g. 150*154/118 = 195px for this
                    // 118x154 image), far taller than the 154px image actually embedded.
                    Assert.That(
                        rowHeightPx,
                        Is.LessThanOrEqualTo(sourceImageHeightPx + 15),
                        $"Row height ({rowHeightPx:0}px) is taller than the embedded image "
                            + $"({sourceImageHeightPx}px), so it was sized from the oversized target "
                            + "rather than the image actually embedded (BL-16529)."
                    );
                    // ...and the row must still be tall enough to show the whole image.
                    Assert.That(
                        rowHeightPx,
                        Is.GreaterThanOrEqualTo(sourceImageHeightPx),
                        $"Row height ({rowHeightPx:0}px) is shorter than the embedded image "
                            + $"({sourceImageHeightPx}px), so the image is clipped by its row (BL-16529)."
                    );
                }
            }
        }

        /// <summary>
        /// Regression test for BL-16529 ("images in spreadsheet export display smaller"). Bloom is
        /// PerMonitorV2 DPI aware (see Program.cs), so the GDI+ bitmaps ImageUtils produces carry the
        /// screen's DPI -- 192 on a 200%-scaled display rather than 96. EPPlus turns a picture's pixel
        /// size into its physical extent in the sheet using the image's own resolution, so a 192dpi
        /// thumbnail was laid out at half its intended size: exactly the reported symptom. The
        /// exporter must therefore stamp 96dpi on every thumbnail, so that a given book always
        /// exports to the same spreadsheet whatever display the exporting machine has, and the sheet
        /// looks the same wherever it is opened.
        ///
        /// Note this assertion can only *fail* on a machine whose display scaling is above 100%; at
        /// 100% the screen DPI is 96 and the old code happened to be right. It is worth asserting
        /// anyway: it pins the intent, and it fails on precisely the high-DPI machines where the bug
        /// was reported.
        /// </summary>
        [Test]
        public void Export_Thumbnail_IsStamped96Dpi_SoLayoutDoesNotDependOnTheDisplay()
        {
            const string imageFileName = "bird.png";
            using (var bookFolder = new TemporaryFolder(_testFolder, "ThumbnailDpi_Book"))
            using (var outputFolder = new TemporaryFolder(_testFolder, "ThumbnailDpi_Out"))
            {
                using (
                    var img = Image.FromFile(
                        Path.Combine(
                            SIL.IO.FileLocationUtilities.GetDirectoryDistributedWithApplication(
                                _pathToTestImages
                            ),
                            imageFileName
                        )
                    )
                )
                {
                    // Sanity check: the source must be wider than the thumbnail target, so that it is
                    // really scaled down to that target and we are asserting on the exporter's own
                    // sizing rather than on a source image passed through untouched.
                    Assert.That(
                        img.Width,
                        Is.GreaterThan(150),
                        "Setup sanity check: the test image must be wider than the 150px thumbnail "
                            + "target so that the exporter resizes it."
                    );
                }

                var outputPath = ExportBookWithOneImage(imageFileName, bookFolder, outputFolder);

                using (var package = new ExcelPackage(new FileInfo(outputPath)))
                {
                    var worksheet = package.Workbook.Worksheets[0];
                    var picture = worksheet.Drawings.OfType<ExcelPicture>().Single();
                    using (var stream = new MemoryStream(picture.Image.ImageBytes))
                    using (var thumbnail = Image.FromStream(stream))
                    {
                        // Sanity check: we are looking at the thumbnail the exporter made, scaled to
                        // the 150px target, not at the original image.
                        Assert.That(
                            thumbnail.Width,
                            Is.EqualTo(150),
                            "The embedded thumbnail should have been scaled to the 150px target width."
                        );
                        Assert.That(
                            thumbnail.HorizontalResolution,
                            Is.EqualTo(96f).Within(0.01f),
                            "The embedded thumbnail must be stamped 96dpi. At any other resolution "
                                + "EPPlus computes a different physical extent from the same pixels, so "
                                + "the image is laid out at the wrong size -- at 192dpi (a 200%-scaled "
                                + "display) it comes out half size (BL-16529)."
                        );
                        Assert.That(
                            thumbnail.VerticalResolution,
                            Is.EqualTo(96f).Within(0.01f),
                            "The embedded thumbnail must be stamped 96dpi vertically too (BL-16529)."
                        );
                    }
                }

                // And the payoff: the extent the picture actually occupies in the sheet must match the
                // thumbnail's pixels one for one. This is the number the reporter saw go wrong -- in the
                // reported 6.5 export a 150px thumbnail was laid out over just 75px, which is the
                // "images only take up half the column width" symptom.
                Assert.That(
                    GetFirstDrawingWidthInPixels(outputPath),
                    Is.EqualTo(150).Within(1),
                    "The picture should occupy the full 150px thumbnail width in the sheet. A smaller "
                        + "extent for the same pixels means the thumbnail's resolution made the "
                        + "spreadsheet library lay it out too small (BL-16529)."
                );
            }
        }

        /// <summary>
        /// Regression test for BL-16529, and the one that actually protects the fix on our build
        /// machines. The export-level tests above cannot: on a display at 100% scaling a GDI+ bitmap
        /// already carries 96dpi, so they pass whether or not the exporter stamps the resolution, and
        /// every build machine is at 100%. Here we hand the save step a bitmap deliberately marked
        /// 192dpi -- what it would really receive on a 200%-scaled display -- so the assertion has the
        /// same force everywhere, and deleting the stamping would fail this test.
        /// </summary>
        [Test]
        public void SaveThumbnailForEmbedding_StampsStandard96Dpi_WhateverResolutionItWasGiven()
        {
            using (var thumbnail = new Bitmap(150, 100))
            {
                thumbnail.SetResolution(192f, 192f);
                // Sanity check: the bitmap really starts at the wrong resolution, so a pass below
                // means the save step changed it rather than it having been right all along.
                Assert.That(
                    thumbnail.HorizontalResolution,
                    Is.EqualTo(192f).Within(0.01f),
                    "Setup sanity check: the test bitmap should start at 192dpi."
                );

                using (var stream = new MemoryStream())
                {
                    SpreadsheetIO.SaveThumbnailForEmbedding(thumbnail, stream);
                    stream.Seek(0, SeekOrigin.Begin);
                    using (var saved = Image.FromStream(stream))
                    {
                        Assert.That(
                            saved.HorizontalResolution,
                            Is.EqualTo(96f).Within(0.01f),
                            "The embedded thumbnail must be stamped 96dpi regardless of the resolution "
                                + "the bitmap arrived with. At 192dpi the spreadsheet library lays the "
                                + "picture out at half its intended size (BL-16529)."
                        );
                        Assert.That(
                            saved.VerticalResolution,
                            Is.EqualTo(96f).Within(0.01f),
                            "The embedded thumbnail must be stamped 96dpi vertically too (BL-16529)."
                        );
                        // The stamping must not have resampled the picture; only its claimed
                        // resolution changes, never its pixels.
                        Assert.That(
                            new Size(saved.Width, saved.Height),
                            Is.EqualTo(new Size(150, 100)),
                            "Stamping the resolution should not change the thumbnail's pixel size."
                        );
                    }
                }
            }
        }

        /// <summary>
        /// The width, in pixels at the standard 96dpi, that the first picture in the spreadsheet is
        /// laid out over. We read this from the raw drawing XML because that is what a spreadsheet
        /// viewer reads: the extent is stored in EMUs (914400 per inch, so 9525 per pixel at 96dpi),
        /// and the library derives it from the embedded image's pixel size and its resolution.
        /// </summary>
        private static double GetFirstDrawingWidthInPixels(string xlsxPath)
        {
            using (var xlsx = System.IO.Compression.ZipFile.OpenRead(xlsxPath))
            {
                var drawingEntry = xlsx
                    .Entries.Where(e =>
                        e.FullName.StartsWith("xl/drawings/drawing") && e.FullName.EndsWith(".xml")
                    )
                    .OrderBy(e => e.FullName)
                    .First();
                string drawingXml;
                using (var reader = new StreamReader(drawingEntry.Open()))
                    drawingXml = reader.ReadToEnd();

                var match = System.Text.RegularExpressions.Regex.Match(
                    drawingXml,
                    @"<xdr:ext\s+cx=""(\d+)""\s+cy=""(\d+)"""
                );
                Assert.That(
                    match.Success,
                    Is.True,
                    "Setup sanity check: could not find a picture extent (xdr:ext) in the exported "
                        + "spreadsheet's drawing XML, so there is nothing to measure."
                );
                const double emusPerPixelAt96Dpi = 914400.0 / 96.0;
                return int.Parse(match.Groups[1].Value) / emusPerPixelAt96Dpi;
            }
        }
    }
}
