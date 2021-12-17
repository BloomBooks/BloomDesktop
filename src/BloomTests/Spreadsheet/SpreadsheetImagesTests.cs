using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Bloom;
using Bloom.Book;
using Bloom.Spreadsheet;
using BloomTemp;
using BloomTests.Book;
using BloomTests.TeamCollection;
using Gtk;
using Moq;
using NUnit.Framework;
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
		public const string imageBook = @"

<html>
<head>
</head>

<body data-l1=""en"" data-l2="""" data-l3="""">
	<div id=""bloomDataDiv"">
        <div data-book=""outside-back-cover-bottom-html"" lang=""*""><img class=""branding"" src=""BloomWithTaglineAgainstLight.svg"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img></div>
	</div>
    <div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-right bloom-monolingual"" data-page="""" id=""bdf2acc2-1ea1-4f70-9e36-6bcee3613752"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398384"" data-page-number=""1"" lang="""">
        <div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Picture on Bottom"" lang=""en"">
            Picture on Bottom
        </div>

        <div class=""pageDescription"" lang=""en""></div>

        <div class=""marginBox"">
            <div class=""split-pane horizontal-percent"" style=""min-height: 42px;"">
                <div class=""split-pane-component position-top"" style=""bottom: 30.1471%;"">
                    <div class=""split-pane horizontal-percent"" style=""min-height: 42px;"">
                        <div class=""split-pane-component position-top"">
                            <div class=""split-pane-component-inner"" min-width=""60px 150px 250px"" min-height=""60px 150px"">
                                <div class=""bloom-imageContainer bloom-leadingElement"" title=""Name: man.jpg Size: 178.00 kb Dots: 1041 x 781 For the current paper size: • The image container is 406 x 231 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ✓ This image would print at 325 DPI. • An image with 1269 x 722 dots would fill this container at 300 DPI."">
									<img src=""man.jpg"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img>
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
                        <div class=""bloom-imageContainer"" title=""Name: mars 2.png Size: 130.10 kb Dots: 1041 x 447 For the current paper size: • The image container is 406 x 203 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ⚠ This image would print at 246 DPI. • An image with 1269 x 635 dots would fill this container at 300 DPI.""><img src=""mars%202.png"" alt="""" data-copyright="""" data-creator="""" data-license=""cc-by""></img></div>
						<div class=""bloom-imageContainer"" title=""Name:missing file.jpg Size: 130.10 kb Dots: 1041 x 447 For the current paper size: • The image container is 406 x 203 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ⚠ This image would print at 246 DPI. • An image with 1269 x 635 dots would fill this container at 300 DPI.""><img src=""missing%20file.jpg"" alt="""" data-copyright="""" data-creator="""" data-license=""cc-by""></img></div>
                    </div>
                </div>
            </div>
        </div>
    </div>
<div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-left bloom-monolingual"" data-page="""" id=""f3262bcc-ccea-458c-857c-24ddc15462f7"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""2"" lang="""">
        <div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Basic Text &amp; Picture"" lang=""en"">
            Basic Text &amp; Picture
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
                        <div class=""bloom-imageContainer bloom-leadingElement"" title=""Name: empty-file.jpg Size: 3.86 kb Dots: 225 x 225 For the current paper size: • The image container is 406 x 335 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ⚠ This image would print at 64 DPI. • An image with 1269 x 1047 dots would fill this container at 300 DPI.""><img src=""empty-file.jpg"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img></div>
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
        <div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Basic Text &amp; Picture"" lang=""en"">
            Basic Text &amp; Picture
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
        <div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Basic Text &amp; Picture"" lang=""en"">
            Basic Text &amp; Picture
        </div>
        <div class=""pageDescription"" lang=""en""></div>
        <div class=""marginBox"">
			<div class=""bloom-imageContainer bloom-leadingElement"" title=""Name: lady24b.png Size: 3.86 kb Dots: 225 x 225 For the current paper size: • The image container is 406 x 335 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ⚠ This image would print at 64 DPI. • An image with 1269 x 1047 dots would fill this container at 300 DPI.""><img src=""lady24b.png"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img></div>
			<div class=""bloom-imageContainer bloom-leadingElement"" title=""Name: placeHolder.png Size: 3.86 kb Dots: 225 x 225 For the current paper size: • The image container is 406 x 335 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ⚠ This image would print at 64 DPI. • An image with 1269 x 1047 dots would fill this container at 300 DPI.""><img src=""placeHolder.png""></img></div>
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
		private TemporaryFolder _spreadsheetFolder;
		private TemporaryFolder _bookFolder;
		private ProgressSpy _progressSpy;

		[OneTimeSetUp]
		public void OneTimeSetUp()
		{
			var dom = new HtmlDom(imageBook, true);

			_spreadsheetFolder = new TemporaryFolder("SpreadsheetImagesTests");
			_bookFolder = new TemporaryFolder("SpreadsheetImagesTests_Book");

			var mockLangDisplayNameResolver = new Mock<ILanguageDisplayNameResolver>();
			mockLangDisplayNameResolver.Setup(x => x.GetLanguageDisplayName("en")).Returns("English");

			_exporter = new SpreadsheetExporter(mockLangDisplayNameResolver.Object);
			var path = SIL.IO.FileLocationUtilities.GetDirectoryDistributedWithApplication(_pathToTestImages);

			// We need all these files in one place so we can verify that all of them get copied except placeHolder.png
			foreach (var name in new []{ "BloomWithTaglineAgainstLight.svg", "man.jpg", "mars 2.png", "lady24b.png", "empty-file.jpg" })
				RobustFile.Copy(Path.Combine(path, name), Path.Combine(_bookFolder.FolderPath, name));
			var placeHolderSource = Path.Combine(BloomFileLocator.FactoryCollectionsDirectory, "template books", "Basic Book", "placeHolder.png");
			RobustFile.Copy(placeHolderSource, Path.Combine(_bookFolder.FolderPath, "placeHolder.png"));

			_progressSpy = new ProgressSpy();
			_sheetFromExport =
				_exporter.ExportToFolder(dom, _bookFolder.FolderPath, _spreadsheetFolder.FolderPath, out string outputPath, _progressSpy, OverwriteOptions.Overwrite);
			_rowsFromExport = _sheetFromExport.ContentRows.ToList();
			_sheetFromFile = InternalSpreadsheet.ReadFromFile(outputPath);
			_rowsFromFile = _sheetFromFile.ContentRows.ToList();
		}

		[OneTimeTearDown]
		public void OneTimeTearDown()
		{
			_spreadsheetFolder?.Dispose();
			_bookFolder?.Dispose();
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

			_pageContentRows = _rows.Where(r => r.MetadataKey == InternalSpreadsheet.PageContentRowLabel).ToList();			
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

		[TestCase("fromExport")]
		[TestCase("fromFile")]
		public void AddsRowLabels(string source)
		{
			SetupFor(source);
			Assert.That(_pageContentRows[0].GetCell(0).Content, Is.EqualTo(InternalSpreadsheet.PageContentRowLabel));
			Assert.That(_pageContentRows[1].GetCell(0).Content, Is.EqualTo(InternalSpreadsheet.PageContentRowLabel));
			Assert.That(_pageContentRows[2].GetCell(0).Content, Is.EqualTo(InternalSpreadsheet.PageContentRowLabel));
		}

		[Test]
		public void CopiesImagesToDestFolder()
		{
			var destImageFolder = Path.Combine(_spreadsheetFolder.FolderPath, "images");
			Assert.That(Directory.Exists(destImageFolder));
			Assert.That(File.Exists(Path.Combine(destImageFolder, "BloomWithTaglineAgainstLight.svg")));
			Assert.That(File.Exists(Path.Combine(destImageFolder, "man.jpg")));
			Assert.That(File.Exists(Path.Combine(destImageFolder, "mars 2.png")));
			Assert.That(File.Exists(Path.Combine(destImageFolder, "lady24b.png")));
			Assert.That(File.Exists(Path.Combine(destImageFolder, "placeHolder.png")), Is.False);
		}

		[TestCase("fromExport")]
		[TestCase("fromFile")]
		public void SavesImageSources(string source)
		{
			SetupFor(source);
			var imageSourceColumn = _sheet.GetColumnForTag(InternalSpreadsheet.ImageSourceColumnLabel);
			var path = SIL.IO.FileLocationUtilities.GetDirectoryDistributedWithApplication(_pathToTestImages);
			var manImagePath = Path.Combine("images", "man.jpg");
			Assert.That(_pageContentRows[0].GetCell(imageSourceColumn).Text, Is.EqualTo(manImagePath));
			var marsImagePath = Path.Combine("images", "mars 2.png");
			Assert.That(_pageContentRows[1].GetCell(imageSourceColumn).Text, Is.EqualTo(marsImagePath));
			var missingFileImagePath = Path.Combine("images", "missing file.jpg");
			Assert.That(_pageContentRows[2].GetCell(imageSourceColumn).Text, Is.EqualTo(missingFileImagePath));
			var emptyFileImagePath = Path.Combine("images", "empty-file.jpg");
			Assert.That(_pageContentRows[3].GetCell(imageSourceColumn).Text, Is.EqualTo(emptyFileImagePath));
			Assert.That(_pageContentRows[4].GetCell(imageSourceColumn).Text, Is.EqualTo("")); // no more images, but a second text group on P2
			Assert.That(_pageContentRows[5].GetCell(imageSourceColumn).Text, Is.EqualTo("")); // no images, but there is text on P3
			var ladyImagePath = Path.Combine("images", "lady24b.png");
			Assert.That(_pageContentRows[6].GetCell(imageSourceColumn).Text, Is.EqualTo(ladyImagePath));
			Assert.That(_pageContentRows[7].GetCell(imageSourceColumn).Text, Is.EqualTo(InternalSpreadsheet.BlankContentIndicator));
		}

		[TestCase("fromExport")]
		[TestCase("fromFile")]
		public void PutsTextWithImages(string source)
		{
			SetupFor(source);
			Assert.That(_pageContentRows[0].GetCell("[en]").Text, Is.EqualTo("I am going to outer space."));
			Assert.That(_pageContentRows[1].GetCell("[en]").Text, Is.EqualTo("")); // two images on P1, but no more text
			Assert.That(_pageContentRows[2].GetCell("[en]").Text, Is.EqualTo("")); // two images on P1, but no more text
			Assert.That(_pageContentRows[3].GetCell("[en]").Text, Is.EqualTo("Outer space is fascinating."));
			Assert.That(_pageContentRows[4].GetCell("[en]").Text, Is.EqualTo("Outer space is very scary."));
			Assert.That(_pageContentRows[5].GetCell("[en]").Text, Is.EqualTo("This page has only text"));
			Assert.That(_pageContentRows[6].GetCell("[en]").Text, Is.EqualTo("")); // no text on P4
		}

		[TestCase("fromFile")] //Images are embedded during writing of .xlsx file
		public void displayThumbnail_imageFilePresent_noErrorMessage(string source)
		{
			SetupFor(source);
			var thumbnailColumn = _sheet.GetColumnForTag(InternalSpreadsheet.ImageThumbnailColumnLabel);
			var goodImageFileRow = _pageContentRows.First(x => x.GetCell(InternalSpreadsheet.ImageSourceColumnLabel).Text.Contains("man.jpg"));
			Assert.That(goodImageFileRow.GetCell(thumbnailColumn).Text, Is.EqualTo(""));
		}

		[TestCase("fromFile")] 
		public void displayThumbnail_imageMissing_ErrorMessageForMissingFile(string source)
		{
			SetupFor(source);
			var thumbnailColumn = _sheet.GetColumnForTag(InternalSpreadsheet.ImageThumbnailColumnLabel);
			var missingFileRow = _pageContentRows.First(x => x.GetCell(InternalSpreadsheet.ImageSourceColumnLabel).Text.Contains("missing file.jpg"));
			Assert.That(missingFileRow.GetCell(thumbnailColumn).Text, Is.EqualTo("Missing"));
		}

		[TestCase("fromFile")]
		public void displayThumbnail_imageEmpty_ErrorMessage(string source)
		{
			SetupFor(source);
			var thumbnailColumn = _sheet.GetColumnForTag(InternalSpreadsheet.ImageThumbnailColumnLabel);
			Assert.That(_pageContentRows[3].GetCell(thumbnailColumn).Text, Is.EqualTo("Bad image file"));
		}

		[TestCase("fromFile")]
		public void displayThumbnail_svg_svgErrorMessage(string source)
		{
			SetupFor(source);
			var thumbnailColumn = _sheet.GetColumnForTag(InternalSpreadsheet.ImageThumbnailColumnLabel);
			var svgRow = _rows.First(x => x.GetCell(InternalSpreadsheet.RowTypeColumnLabel).Text.Equals("[outside-back-cover-bottom-html]"));
			Assert.That(svgRow.GetCell(thumbnailColumn).Text, Is.EqualTo("Can't display SVG"));
		}

		[TestCase("fromFile")]
		public void GotWarningForExportingImageDescription(string source)
		{
			SetupFor(source);
			// A better test would be to have multiple image descriptions and verify that we only get one
			// warning. However, this is throw-away code: eventually we most likely WILL handle image
			// descriptions. I think it's enough.
			Assert.That(_progressSpy.Messages,
				Has.Some.Property("Item1")
					.EqualTo(
						"Image description are not currently supported by spreadsheet import/export. They will be ignored."));
		}
	}
}
