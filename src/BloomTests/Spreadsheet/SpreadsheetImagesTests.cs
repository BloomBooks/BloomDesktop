using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Bloom.Book;
using Bloom.Spreadsheet;
using BloomTests.Book;
using Gtk;
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
		// re-use the images from another test (added an svg and an empty file bh.jpg for these tests)
		private const string _pathToTestImages = "src/BloomTests/ImageProcessing/images";
		public const string imageBook = @"

<html>
<head>
</head>

<body data-l1=""en"" data-l2="""" data-l3="""">
	<div id=""bloomDataDiv"">
        <div data-book=""outside-back-cover-branding-bottom-html"" lang=""*""><img class=""branding"" src=""BloomWithTaglineAgainstLight.svg"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img></div>
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
                                <div class=""bloom-imageContainer bloom-leadingElement"" title=""Name: man.jpg Size: 178.00 kb Dots: 1041 x 781 For the current paper size: • The image container is 406 x 231 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ✓ This image would print at 325 DPI. • An image with 1269 x 722 dots would fill this container at 300 DPI.""><img src=""man.jpg"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img></div>
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
                        <div class=""bloom-imageContainer"" title=""Name: mars 2.jpg Size: 130.10 kb Dots: 1041 x 447 For the current paper size: • The image container is 406 x 203 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ⚠ This image would print at 246 DPI. • An image with 1269 x 635 dots would fill this container at 300 DPI.""><img src=""mars%202.jpg"" alt="""" data-copyright="""" data-creator="""" data-license=""cc-by""></img></div>
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
            <div class=""split-pane horizontal-percent"" style=""min-height: 42px;"">
                <div class=""split-pane-component position-top"" style=""bottom: 50%"">
                    <div class=""split-pane-component-inner"">
                        <div class=""bloom-imageContainer bloom-leadingElement"" title=""Name: bh.jpg Size: 3.86 kb Dots: 225 x 225 For the current paper size: • The image container is 406 x 335 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ⚠ This image would print at 64 DPI. • An image with 1269 x 1047 dots would fill this container at 300 DPI.""><img src=""bh.jpg"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img></div>
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
</body>
</html>
";
		private HtmlDom _dom;
		private SpreadsheetExporter _exporter;
		// The tests are all written in terms of _sheet and _rows, the output
		// of an export operation. But we create two sheets, one by export, and
		// one by writing the first to file and reading it back. We want to apply
		// the same tests to each. This is currently achieved by using the test
		// case to select one pair (_sheetFromExport, _rowsFromExport)
		// or (_sheetFromFile, _rowsFromFile) to set as _sheet and _rows.
		private InternalSpreadsheet _sheet;
		private List<ContentRow> _rows;
		private List<ContentRow> _imageRows;
		private List<ContentRow> _textRows;
		private InternalSpreadsheet _sheetFromExport;
		private List<ContentRow> _rowsFromExport;
		private InternalSpreadsheet _sheetFromFile;
		private List<ContentRow> _rowsFromFile;

		[OneTimeSetUp]
		public void OneTimeSetUp()
		{
			var dom = new HtmlDom(imageBook, true);
			_exporter = new SpreadsheetExporter();
			var path = SIL.IO.FileLocationUtilities.GetDirectoryDistributedWithApplication(_pathToTestImages);
			_sheetFromExport = _exporter.Export(dom, path);
			_rowsFromExport = _sheetFromExport.ContentRows.ToList();
			using (var tempFile = TempFile.WithExtension("xslx"))
			{
				_sheetFromExport.WriteToFile(tempFile.Path);
				_sheetFromFile = InternalSpreadsheet.ReadFromFile(tempFile.Path);
				_rowsFromFile = _sheetFromFile.ContentRows.ToList();
			}
		}

		[OneTimeTearDown]
		public void OneTimeTearDown()
		{

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
			(_imageRows, _textRows) = SpreadsheetTests.SplitRows(_rows);
		}

		[TestCase("fromExport")]
		[TestCase("fromFile")]
		public void AddsImageIndices(string source)
		{
			SetupFor(source);
			var imageIndex = _sheet.ColumnForTag(InternalSpreadsheet.ImageIndexOnPageLabel);
			Assert.That(_imageRows[0].GetCell(imageIndex).Content, Is.EqualTo("1"));
			Assert.That(_imageRows[1].GetCell(imageIndex).Content, Is.EqualTo("2"));
			Assert.That(_imageRows[2].GetCell(imageIndex).Content, Is.EqualTo("1"));

			Assert.That(_textRows[0].GetCell(imageIndex).Content, Is.EqualTo(""));

		}

		[TestCase("fromExport")]
		[TestCase("fromFile")]
		public void AddsImagePageNumbers(string source)
		{
			SetupFor(source);
			var pageNumberIndex = _sheet.ColumnForTag(InternalSpreadsheet.PageNumberLabel);
			Assert.That(_imageRows[0].GetCell(pageNumberIndex).Content, Is.EqualTo("1"));
			Assert.That(_imageRows[1].GetCell(pageNumberIndex).Content, Is.EqualTo("1"));
			Assert.That(_imageRows[2].GetCell(pageNumberIndex).Content, Is.EqualTo("2"));
		}

		[TestCase("fromExport")]
		[TestCase("fromFile")]
		public void AddsImageRowLabels(string source)
		{
			SetupFor(source);
			Assert.That(_imageRows[0].GetCell(0).Content, Is.EqualTo(InternalSpreadsheet.ImageKeyLabel));
			Assert.That(_imageRows[1].GetCell(0).Content, Is.EqualTo(InternalSpreadsheet.ImageKeyLabel));
			Assert.That(_imageRows[2].GetCell(0).Content, Is.EqualTo(InternalSpreadsheet.ImageKeyLabel));

			Assert.That(_textRows[0].GetCell(0).Content, Is.EqualTo(InternalSpreadsheet.TextGroupLabel));
		}

		[TestCase("fromExport")]
		[TestCase("fromFile")]
		public void SavesImageSources(string source)
		{
			SetupFor(source);
			var imageSourceColumn = _sheet.ColumnForTag(InternalSpreadsheet.ImageSourceLabel);
			var path = SIL.IO.FileLocationUtilities.GetDirectoryDistributedWithApplication(_pathToTestImages);
			var manImagePath = Path.Combine(path, "man.jpg");
			Assert.That(_imageRows[0].GetCell(imageSourceColumn).Text, Is.EqualTo(manImagePath));
			var marsImagePath = Path.Combine(path, "mars 2.jpg");
			Assert.That(_imageRows[1].GetCell(imageSourceColumn).Text, Is.EqualTo(marsImagePath));
			var bhImagePath = Path.Combine(path, "bh.jpg");
			Assert.That(_imageRows[2].GetCell(imageSourceColumn).Text, Is.EqualTo(bhImagePath));

			Assert.That(_textRows[0].GetCell(imageSourceColumn).Text, Is.EqualTo(""));
		}

		[TestCase("fromFile")] //Images are embedded during writing of .xlsx file
		public void displayThumbnail_imageFilePresent_noErrorMessage(string source)
		{
			SetupFor(source);
			var thumbnailColumn = _sheet.ColumnForTag(InternalSpreadsheet.ImageThumbnailLabel);
			Assert.That(_imageRows[0].GetCell(thumbnailColumn).Text, Is.EqualTo(""));
		}

		[TestCase("fromFile")] 
		public void displayThumbnail_imageMissing_ErrorMessageForMissingFile(string source)
		{
			SetupFor(source);
			var thumbnailColumn = _sheet.ColumnForTag(InternalSpreadsheet.ImageThumbnailLabel);
			Assert.That(_imageRows[1].GetCell(thumbnailColumn).Text, Is.EqualTo("Missing"));
		}

		[TestCase("fromFile")]
		public void displayThumbnail_imageEmpty_ErrorMessage(string source)
		{
			SetupFor(source);
			var thumbnailColumn = _sheet.ColumnForTag(InternalSpreadsheet.ImageThumbnailLabel);
			Assert.That(_imageRows[2].GetCell(thumbnailColumn).Text, Is.EqualTo("Bad image file"));
		}

		[TestCase("fromFile")]
		public void displayThumbnail_svg_svgErrorMessage(string source)
		{
			SetupFor(source);
			var thumbnailColumn = _sheet.ColumnForTag(InternalSpreadsheet.ImageThumbnailLabel);
			var svgRow = _rows.First(x => x.GetCell(InternalSpreadsheet.MetadataKeyLabel).Text.Equals("[outside-back-cover-branding-bottom-html]"));
			Assert.That(svgRow.GetCell(thumbnailColumn).Text, Is.EqualTo("Can't display SVG"));
		}
	}
}
