using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Bloom.Book;
using Bloom.Spreadsheet;
using Gtk;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.Spreadsheet
{
	/// <summary>
	/// This class tests a variety of things that can be checked in the process of
	/// exporting a single document to our spreadsheet format.
	/// </summary>
	public class SpreadsheetTests
	{
		public const string kSimpleTwoPageBook = @"
<html>
	<head>
	</head>

	<body data-l1=""en"" data-l2=""en"" data-l3="""">
		<div class=""bloom-page cover coverColor bloom-frontMatter frontCover outsideFrontCover side-right Device16x9Landscape"" data-page=""required singleton"" data-export=""front-matter-cover"" data-xmatter-page=""frontCover"" id=""106c3efd-c64d-4d41-87ad-bf92dbc9fdb1"" lang=""en"" data-page-number="""">
	        <div class=""pageLabel"" lang=""en"" data-i18n=""TemplateBooks.PageLabel.Front Cover"">
	            Front Cover
	        </div>
	        <div class=""pageDescription"" lang=""en""></div>

	        <div class=""marginBox"">
	            <div class=""bloom-translationGroup bookTitle"" data-default-languages=""V,N1"">
	                <label class=""bubble"">Book title in {lang}</label>
	                <div class=""bloom-editable bloom-nodefaultstylerule Title-On-Cover-style bloom-padForOverflow"" lang=""z"" contenteditable=""true"" data-book=""bookTitle""></div>

	                <div class=""bloom-editable bloom-nodefaultstylerule Title-On-Cover-style bloom-padForOverflow audio-sentence bloom-postAudioSplit bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" lang=""en"" contenteditable=""true"" data-book=""bookTitle"" data-audiorecordingmode=""TextBox"" id=""f527323a-4e81-4b45-b6ce-5c05adf37022"" recordingmd5=""e4fcf145d26bc6c786513ce169a2c1a8"" data-duration=""1.82854"" data-audiorecordingendtimes=""1.72"" style=""padding-bottom: 0px;"">
	                    <p><span id=""fecfcdfa-3e6a-4f71-9003-f4a4e84758fe"" class=""bloom-highlightSegment""><strong>Is it the End of the World? - SL</strong></span></p>
	                </div>
	            </div>

	            <div class=""bloom-imageContainer"">
	                <img data-book=""coverImage"" src=""Cover.jpg"" data-copyright="""" data-creator="""" data-license="""" alt=""""></img>

	                <div class=""bloom-translationGroup bloom-imageDescription bloom-trailingElement"" data-default-languages=""auto"">
	                    <div class=""bloom-editable ImageDescriptionEdit-style"" lang=""z"" contenteditable=""true"" data-book=""coverImageDescription""></div>
	                    <div class=""bloom-editable ImageDescriptionEdit-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" lang=""en"" contenteditable=""true"" data-book=""coverImageDescription""></div>
	                </div>
	            </div>

	            <div class=""bottomBlock"">
	                <div data-book=""cover-branding-left-html"" lang=""*""></div>

	                <div class=""bottomTextContent"">
	                    <div class=""creditsRow"" data-hint=""You may use this space for author/illustrator, or anything else."">
	                        <div class=""bloom-translationGroup"" data-default-languages=""V"">
	                            <div class=""bloom-editable smallCoverCredits Cover-Default-style"" lang=""z"" contenteditable=""true"" data-book=""smallCoverCredits""></div>

	                            <div class=""bloom-editable smallCoverCredits Cover-Default-style audio-sentence bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" lang=""en"" contenteditable=""true"" data-book=""smallCoverCredits"" data-audiorecordingmode=""TextBox"" id=""i2e72b4bb-b7b2-4fbd-9b04-42f4b907a032"">
	                                <p>School Journal Junior 1-SL, 2009 - V1.0</p>
	                            </div>
	                        </div>
	                    </div>

	                    <div class=""bottomRow"" data-have-topic=""true"">
	                        <div class=""coverBottomLangName Cover-Default-style"" data-derived=""languagesOfBook"" lang=""en"">
	                            English
	                        </div>

	                        <div class=""coverBottomBookTopic bloom-userCannotModifyStyles bloom-alwaysShowBubble Cover-Default-style"" data-derived=""topic"" data-functiononhintclick=""ShowTopicChooser()"" data-hint=""Click to choose topic"" lang=""en"">
	                            Science
	                        </div>
	                    </div>
	                </div>
	            </div>
	        </div>
	    </div>
	    <div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-right bloom-monolingual"" data-page="""" id=""7403192b-f306-4653-b7b1-0acf7163f4b9"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""1"" lang="""">
	        <div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Basic Text &amp; Picture"" lang=""en"">
	            Basic Text &amp; Picture
	        </div>
	        <div class=""pageDescription"" lang=""en""></div>

	        <div class=""marginBox"">
	            <div class=""split-pane horizontal-percent"" style=""min-height: 42px;"">
	                <div class=""split-pane-component position-top"" style=""bottom: 50%"">
	                    <div class=""split-pane-component-inner"">
	                        <div class=""bloom-imageContainer bloom-leadingElement"" title=""Name: aor_CMB424.png Size: 46.88 kb Dots: 1460 x 1176 For the current paper size: • The image container is 406 x 335 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ✓ This image would print at 345 DPI. • An image with 1269 x 1047 dots would fill this container at 300 DPI.""><img src=""aor_CMB424.png"" alt="""" data-copyright=""Copyright SIL International 2009"" data-creator="""" data-license=""cc-by-sa""></img></div>
	                    </div>
	                </div>
	                <div class=""split-pane-divider horizontal-divider"" style=""bottom: 50%""></div>

	                <div class=""split-pane-component position-bottom"" style=""height: 50%"">
	                    <div class=""split-pane-component-inner"">
							<div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto""  tabindex=""2"">
	                            <div class=""bloom-editable normal-style bloom-postAudioSplit audio-sentence bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""English"" data-audiorecordingendtimes=""2.920 4.160"" lang=""en"" contenteditable=""true"">
	                                <p>Elephants should be handled with much care.</p>
	                            </div>
								<div class=""bloom-editable normal-style bloom-postAudioSplit audio-sentence bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" lang=""de"" contenteditable=""true"">
	                                <p>German elephants are quite orderly.</p>
	                            </div>
								<div class=""bloom-editable normal-style bloom-postAudioSplit audio-sentence bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""French"" data-audiorecordingendtimes=""2.920 4.160"" lang=""fr"" contenteditable=""true"">
	                                <p>French elephants should be handled with special care.</p>
	                            </div>

	                            <div class=""bloom-editable normal-style"" style="""" lang=""z"" contenteditable=""true"">
	                                <p></p>
	                            </div>
	                        </div>
	                        <div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto""  tabindex=""1"">
	                            <div class=""bloom-editable normal-style bloom-postAudioSplit audio-sentence bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-audiorecordingmode=""TextBox"" id=""i4b150910-b53d-4779-a1fb-8177982c651c"" recordingmd5=""9134cd4f71cf3d6e148a6c9b4afed8dc"" data-duration=""4.245963"" data-languagetipcontent=""English"" data-audiorecordingendtimes=""2.920 4.160"" lang=""en"" contenteditable=""true"">
	                                <p><span id=""e4bc05e5-4d65-4016-9bf3-ab44a0df3ea2"" class=""bloom-highlightSegment"" recordingmd5=""undefined"">This elephant is running amok.</span> <span id=""i2ba966b6-4212-4821-9268-04e820e95f50"" class=""bloom-highlightSegment"" recordingmd5=""undefined"">Causing much damage.</span></p>
	                            </div>
								<div class=""bloom-editable normal-style bloom-postAudioSplit audio-sentence bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-audiorecordingmode=""TextBox"" id=""i4b150910-b53d-4779-a1fb-8177982c651c"" recordingmd5=""9134cd4f71cf3d6e148a6c9b4afed8dc"" data-duration=""4.245963"" data-languagetipcontent=""French"" data-audiorecordingendtimes=""2.920 4.160"" lang=""fr"" contenteditable=""true"">
	                                <p>This French elephant is running amok. Causing much damage.</p>
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
		<div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-right bloom-monolingual"" data-page="""" id=""7403192b-f306-4653-b7b1-0acf7163f4b9"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""2"" lang="""">
	        <div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Basic Text &amp; Picture"" lang=""en"">
	            Basic Text &amp; Picture
	        </div>
	        <div class=""pageDescription"" lang=""en""></div>

	        <div class=""marginBox"">
				<div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"" id=""group3"">
					<div class=""bloom-editable normal-style bloom-postAudioSplit audio-sentence bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" lang=""de"" contenteditable=""true"">
                        <p>Riding on German elephants can be less risky.</p>
                    </div>
					<div class=""bloom-editable normal-style bloom-postAudioSplit audio-sentence bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""English"" data-audiorecordingendtimes=""2.920 4.160"" lang=""en"" contenteditable=""true"">
                        <p>Riding on elephants can be risky.</p>
                    </div>
					<div class=""bloom-editable normal-style bloom-postAudioSplit audio-sentence bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""French"" data-audiorecordingendtimes=""2.920 4.160"" lang=""fr"" contenteditable=""true"">
                        <p>Riding on French elephants can be more risky.</p>
                    </div>

                    <div class=""bloom-editable normal-style"" style="""" lang=""z"" contenteditable=""true"">
                        <p></p>
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
			var dom = new HtmlDom(kSimpleTwoPageBook, true);
			_exporter = new SpreadsheetExporter();
			_sheetFromExport = _exporter.Export(dom, "fakeImagesFolderpath");
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
			(_imageRows, _textRows) = splitRows(_rows);
		}

		public static (List<ContentRow>, List<ContentRow>) splitRows(List<ContentRow> allRows)
		{
			var imageRows = new List<ContentRow>();
			var textRows = new List<ContentRow>();
			foreach (ContentRow row in allRows)
			{
				// Does not copy any header present
				if (row.GetCell(InternalSpreadsheet.MetadataKeyLabel).Content.Equals(InternalSpreadsheet.ImageKeyLabel))
				{
					imageRows.Add(row);
				}
				else if (row.GetCell(InternalSpreadsheet.MetadataKeyLabel).Content.Equals(InternalSpreadsheet.TextGroupLabel))
				{
					textRows.Add(row);
				}
			}
			return (imageRows, textRows);
		}

		[TestCase("fromExport")]
		[TestCase("fromFile")]
		public void SavesTextFromBlocks(string source)
		{
			SetupFor(source);
			var offset = _sheet.StandardLeadingColumns.Length;
			// The first row has data from the second element in the document, because of tabindex.
			Assert.That(_textRows[0].GetCell(offset).Text, Is.EqualTo("This elephant is running amok. Causing much damage."));
			Assert.That(_textRows[0].GetCell(offset + 1).Text, Is.EqualTo("This French elephant is running amok. Causing much damage."));
			Assert.That(_textRows[1].GetCell(offset).Text, Is.EqualTo("Elephants should be handled with much care."));
			// French is third in its group, but French is the second language encountered, so it should be in the second cell of its row.
			Assert.That(_textRows[1].GetCell(offset + 1).Text, Is.EqualTo("French elephants should be handled with special care."));
			Assert.That(_textRows[1].GetCell(offset + 2).Text, Is.EqualTo("German elephants are quite orderly."));

			Assert.That(_textRows[2].GetCell(offset).Text, Is.EqualTo("Riding on elephants can be risky."));
			Assert.That(_textRows[2].GetCell(offset + 1).Text, Is.EqualTo("Riding on French elephants can be more risky."));
			Assert.That(_textRows[2].GetCell(offset + 2).Text, Is.EqualTo("Riding on German elephants can be less risky."));

		}

		[TestCase("fromExport")]
		[TestCase("fromFile")]
		public void InsertsGroupNumbers(string source)
		{
			SetupFor(source);
			var groupIndex = _sheet.ColumnForTag(InternalSpreadsheet.TextIndexOnPageLabel);
			Assert.That(_textRows[0].GetCell(groupIndex).Content, Is.EqualTo("1"));
			Assert.That(_textRows[1].GetCell(groupIndex).Content, Is.EqualTo("2"));
			Assert.That(_textRows[2].GetCell(groupIndex).Content, Is.EqualTo("1"));

		}

		[TestCase("fromExport")]
		[TestCase("fromFile")]
		public void SavesLangData(string source)
		{
			SetupFor(source);
			Assert.That(_sheet.LangCount, Is.EqualTo(3));
		}

		[TestCase("fromExport")]
		[TestCase("fromFile")]
		public void AddsRowLabels(string source)
		{
			SetupFor(source);
			var pageNumIndex = _sheet.ColumnForTag(InternalSpreadsheet.PageNumberLabel);

			Assert.That(_textRows[0].GetCell(pageNumIndex).Content, Is.EqualTo("1"));
			Assert.That(_textRows[1].GetCell(pageNumIndex).Content, Is.EqualTo("1"));
			Assert.That(_textRows[2].GetCell(pageNumIndex).Content, Is.EqualTo("2"));
			Assert.That(_textRows[0].GetCell(0).Content, Is.EqualTo(InternalSpreadsheet.TextGroupLabel));
			Assert.That(_textRows[1].GetCell(0).Content, Is.EqualTo(InternalSpreadsheet.TextGroupLabel));
			Assert.That(_textRows[2].GetCell(0).Content, Is.EqualTo(InternalSpreadsheet.TextGroupLabel));
		}

		[Test]
		public void ColumnsNotLanguagesNotCounted()
		{
			var sheet = new InternalSpreadsheet();
			var offset = sheet.StandardLeadingColumns.Length;
			Assert.That(sheet.ColumnForLang("en"), Is.EqualTo(offset));
			Assert.That(sheet.ColumnForLang("es"), Is.EqualTo(offset + 1));
			Assert.That(sheet.Languages, Has.Count.EqualTo(2));
			Assert.That(sheet.Languages, Has.Member("en"));
			Assert.That(sheet.Languages, Has.Member("es"));
		}
	}

	public class SpreadsheetExportRetainMarkupTests
	{
		public const string kVerySimpleBook = @"
<html>
	<head>
	</head>

	<body data-l1=""en"" data-l2=""en"" data-l3="""">
	    <div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-right bloom-monolingual"" data-page="""" id=""7403192b-f306-4653-b7b1-0acf7163f4b9"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""1"" lang="""">
	        <div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Basic Text &amp; Picture"" lang=""en"">
	            Basic Text &amp; Picture
	        </div>
	        <div class=""pageDescription"" lang=""en""></div>

	        <div class=""marginBox"">
	            <div class=""split-pane horizontal-percent"" style=""min-height: 42px;"">
	                <div class=""split-pane-component position-top"" style=""bottom: 50%"">
	                    <div class=""split-pane-component-inner"">
	                        <div class=""bloom-imageContainer bloom-leadingElement"" title=""Name: aor_CMB424.png Size: 46.88 kb Dots: 1460 x 1176 For the current paper size: • The image container is 406 x 335 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ✓ This image would print at 345 DPI. • An image with 1269 x 1047 dots would fill this container at 300 DPI.""><img src=""aor_CMB424.png"" alt="""" data-copyright=""Copyright SIL International 2009"" data-creator="""" data-license=""cc-by-sa""></img></div>
	                    </div>
	                </div>
	                <div class=""split-pane-divider horizontal-divider"" style=""bottom: 50%""></div>

	                <div class=""split-pane-component position-bottom"" style=""height: 50%"">
	                    <div class=""split-pane-component-inner"">
	                        <div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
	                            <div class=""bloom-editable normal-style bloom-postAudioSplit audio-sentence bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-audiorecordingmode=""TextBox"" id=""i4b150910-b53d-4779-a1fb-8177982c651c"" recordingmd5=""9134cd4f71cf3d6e148a6c9b4afed8dc"" data-duration=""4.245963"" data-languagetipcontent=""English"" data-audiorecordingendtimes=""2.920 4.160"" lang=""en"" contenteditable=""true"">
	                                <p><span id=""e4bc05e5-4d65-4016-9bf3-ab44a0df3ea2"" class=""bloom-highlightSegment"" recordingmd5=""undefined"">This elephant is running amok.</span> <span id=""i2ba966b6-4212-4821-9268-04e820e95f50"" class=""bloom-highlightSegment"" recordingmd5=""undefined"">Causing much damage.</span></p>
	                            </div>
								<div class=""bloom-editable normal-style bloom-postAudioSplit audio-sentence bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-audiorecordingmode=""TextBox"" id=""i4b150910-b53d-4779-a1fb-8177982c651c"" recordingmd5=""9134cd4f71cf3d6e148a6c9b4afed8dc"" data-duration=""4.245963"" data-languagetipcontent=""French"" data-audiorecordingendtimes=""2.920 4.160"" lang=""fr"" contenteditable=""true"">
	                                <p>This French elephant is running amok. Causing much damage.</p>
	                            </div>

	                            <div class=""bloom-editable normal-style"" style="""" lang=""z"" contenteditable=""true"">
	                                <p></p>
	                            </div>
	                        </div>
	                    </div>
	                </div>
	            </div>
	        </div>
	    </div>	</body>
</html>
";
		[Test]
		public void RetainMarkup_KeepsIt()
		{
			var dom = new HtmlDom(kVerySimpleBook, true);
			var exporter = new SpreadsheetExporter();
			exporter.Params = new SpreadsheetExportParams() {RetainMarkup = true};
			var sheet = exporter.Export(dom,  "fakeImagesFolderpath");
			var rows = sheet.ContentRows.ToList();
			List<ContentRow> imageRows;
			List<ContentRow> textRows;
			(imageRows, textRows) = SpreadsheetTests.splitRows(rows);
			var offset = sheet.StandardLeadingColumns.Length;
			Assert.That(textRows[0].GetCell(offset).Content.Trim(), Is.EqualTo(
				"<p><span id=\"e4bc05e5-4d65-4016-9bf3-ab44a0df3ea2\" class=\"bloom-highlightSegment\" recordingmd5=\"undefined\">This elephant is running amok.</span> <span id=\"i2ba966b6-4212-4821-9268-04e820e95f50\" class=\"bloom-highlightSegment\" recordingmd5=\"undefined\">Causing much damage.</span></p>"));
		}

		[Test]
		public void NoRetainMarkup_OmitsIt()
		{
			var dom = new HtmlDom(kVerySimpleBook, true);
			var exporter = new SpreadsheetExporter();
			var sheet = exporter.Export(dom,  "fakeImagesFolderpath");
			var rows = sheet.ContentRows.ToList();
			List<ContentRow> imageRows;
			List<ContentRow> textRows;
			(imageRows, textRows) = SpreadsheetTests.splitRows(rows);
			var offset = sheet.StandardLeadingColumns.Length;
			Assert.That(textRows[0].GetCell(offset).Content.Trim(), Is.EqualTo(
				"This elephant is running amok. Causing much damage."));
		}

		[TestCase("<div><p>Some text</p></div>", "Some text")]
		[TestCase("<div>\r\n\t\t<p>Some text</p>\r\n</div>", "Some text")]
		[TestCase("<div>\r\n\t\t<p>Some text.</p><p>Some more.</p>\r\n</div>", "Some text.\r\nSome more.")]
		[TestCase("<div>\r\n\t\t<p>Some text.</p><p></p><p>Some more.</p>\r\n</div>", "Some text.\r\n\r\nSome more.")]
		[TestCase("<div>\r\nSome text</div>", "\r\nSome text")]
		[TestCase("<div>\r\n<span>Some text</span> <span>more text</span>\r\n</div>", "Some text more text")]
		public void GetContent_ReturnsExpected(string input, string expected)
		{
			var doc = new XmlDocument();
			doc.PreserveWhitespace = true;
			doc.LoadXml(input);
			var result = SpreadsheetExporter.GetContent(doc.DocumentElement);
			Assert.That(result, Is.EqualTo(expected));

		}
	}

	public class SpreadsheetImagesTests
	{
		public const string imageBook = @"

<html>
<head>
</head>

<body data-l1=""en"" data-l2="""" data-l3="""">
    <div id=""bloomDataDiv"">
        <div data-book=""styleNumberSequence"" lang=""*"">
            0
        </div>

        <div data-book=""contentLanguage1"" lang=""*"">
            en
        </div>

        <div data-book=""contentLanguage1Rtl"" lang=""*"">
            False
        </div>

        <div data-book=""languagesOfBook"" lang=""*"">
            English
        </div>

        <div data-book=""coverImage"" lang=""*"" src=""multiverse.jpg"" alt=""This picture, multiverse.jpg, is missing or was loading too slowly."" data-copyright="""" data-creator="""" data-license="""">
            multiverse.jpg
        </div>

        <div data-book=""bookTitle"" lang=""en"" class="" bloom-editable bloom-nodefaultstylerule bloom-padForOverflow"" style=""padding-bottom: 4px;"" contenteditable=""true"" data-audiorecordingmode=""Sentence"">
            <p><span id=""a68a7c09-2c4e-4cf7-8f47-1f6d99253c20"" class=""audio-sentence ui-audioCurrent"">Space!</span></p>
        </div>

        <div data-book=""originalTitle"" lang=""*"">
            Space!
        </div>

        <div data-book=""outside-back-cover-branding-bottom-html"" lang=""*""><img class=""branding"" src=""BloomWithTaglineAgainstLight.svg"" alt=""This picture, BloomWithTaglineAgainstLight.svg, is missing or was loading too slowly.""></img></div>

        <div data-book=""licenseImage"" lang=""*"">
            license.png
        </div>
        <div data-xmatter-page=""insideFrontCover"" data-page=""required singleton"" data-export=""front-matter-inside-front-cover"" data-page-number=""""></div>
        <div data-xmatter-page=""titlePage"" data-page=""required singleton"" data-export=""front-matter-title-page"" data-page-number=""""></div>
        <div data-xmatter-page=""credits"" data-page=""required singleton"" data-export=""front-matter-credits"" data-page-number=""""></div>
        <div data-xmatter-page=""frontCover"" data-page=""required singleton"" data-export=""front-matter-cover"" data-page-number=""""></div>
        <div data-xmatter-page=""insideBackCover"" data-page=""required singleton"" data-export=""back-matter-inside-back-cover"" data-page-number=""""></div>
        <div data-xmatter-page=""outsideBackCover"" data-page=""required singleton"" data-export=""back-matter-back-cover"" data-page-number=""""></div>
    </div>

    <div class=""bloom-page cover coverColor bloom-frontMatter frontCover outsideFrontCover A5Portrait side-right"" data-page=""required singleton"" data-export=""front-matter-cover"" data-xmatter-page=""frontCover"" id=""b634d916-e83d-41cd-be18-14e0d123e69e"" lang=""en"" data-page-number="""">
        <div class=""pageLabel"" lang=""en"" data-i18n=""TemplateBooks.PageLabel.Front Cover"">
            Front Cover
        </div>
        <div class=""pageDescription"" lang=""en""></div>

        <div class=""marginBox"">
            <div class=""bloom-translationGroup bookTitle"" data-default-languages=""V,N1"">
                <label class=""bubble"">Book title in {lang}</label>
                <div class=""bloom-editable bloom-nodefaultstylerule Title-On-Cover-style bloom-padForOverflow"" lang=""z"" contenteditable=""true"" data-book=""bookTitle""></div>

                <div class=""bloom-editable bloom-nodefaultstylerule Title-On-Cover-style bloom-padForOverflow bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" lang=""en"" contenteditable=""true"" data-book=""bookTitle"" style=""padding-bottom: 4px;"" data-audiorecordingmode=""Sentence"">
                    <p><span id=""a68a7c09-2c4e-4cf7-8f47-1f6d99253c20"" class=""audio-sentence ui-audioCurrent"">Space!</span></p>
                </div>
            </div>

            <div class=""bloom-imageContainer"">
                <img data-book=""coverImage"" src=""multiverse.jpg"" data-copyright="""" data-creator="""" data-license="""" alt=""""></img>

                <div class=""bloom-translationGroup bloom-imageDescription bloom-trailingElement"" data-default-languages=""auto"">
                    <div class=""bloom-editable ImageDescriptionEdit-style"" lang=""z"" contenteditable=""true"" data-book=""coverImageDescription""></div>
                    <div class=""bloom-editable ImageDescriptionEdit-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" lang=""en"" contenteditable=""true"" data-book=""coverImageDescription""></div>
                </div>
            </div>

            <div class=""bottomBlock"">
                <div data-book=""cover-branding-left-html"" lang=""*""></div>

                <div class=""bottomTextContent"">
                    <div class=""creditsRow"" data-hint=""You may use this space for author/illustrator, or anything else."">
                        <div class=""bloom-translationGroup"" data-default-languages=""V"">
                            <div class=""bloom-editable smallCoverCredits Cover-Default-style"" lang=""z"" contenteditable=""true"" data-book=""smallCoverCredits""></div>
                            <div class=""bloom-editable smallCoverCredits Cover-Default-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" lang=""en"" contenteditable=""true"" data-book=""smallCoverCredits""></div>
                        </div>
                    </div>

                    <div class=""bottomRow"" data-have-topic=""false"">
                        <div class=""coverBottomLangName Cover-Default-style"" data-derived=""languagesOfBook"" lang=""en"">
                            English
                        </div>

                        <div class=""coverBottomBookTopic bloom-userCannotModifyStyles bloom-alwaysShowBubble Cover-Default-style"" data-derived=""topic"" data-functiononhintclick=""ShowTopicChooser()"" data-hint=""Click to choose topic""></div>
                    </div>
                </div>
            </div>
        </div>
    </div>

    <div class=""bloom-page cover coverColor bloom-frontMatter cover coverColor insideFrontCover bloom-frontMatter A5Portrait side-left"" data-page=""required singleton"" data-export=""front-matter-inside-front-cover"" data-xmatter-page=""insideFrontCover"" id=""fca04592-d97f-4f1f-83aa-567a713141da"" lang=""en"" data-page-number="""">
        <div class=""pageLabel"" lang=""en"" data-i18n=""TemplateBooks.PageLabel.Inside Front Cover"">
            Inside Front Cover
        </div>
        <div class=""pageDescription"" lang=""en""></div>

        <div class=""marginBox"">
            <div class=""bloom-translationGroup"" data-default-languages=""N1"">
                <div class=""bloom-editable Inside-Front-Cover-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" lang=""en"" contenteditable=""true"" data-book=""insideFontCover""></div>
            </div><label class=""bubble"">If you need somewhere to put more information about the book, you can use this page, which is the inside of the front cover.</label>
        </div>
    </div>

    <div class=""bloom-page titlePage bloom-frontMatter A5Portrait side-right"" data-page=""required singleton"" data-export=""front-matter-title-page"" data-xmatter-page=""titlePage"" id=""f9ed29ae-f2be-4ffb-adfe-451d7d69f154"" lang=""en"" data-page-number="""">
        <div class=""pageLabel"" lang=""en"" data-i18n=""TemplateBooks.PageLabel.Title Page"">
            Title Page
        </div>
        <div class=""pageDescription"" lang=""en""></div>

        <div class=""marginBox"">
            <div class=""bloom-translationGroup"" data-default-languages=""V,N1"" id=""titlePageTitleBlock"">
                <label class=""bubble"">Book title in {lang}</label>
                <div class=""bloom-editable bloom-nodefaultstylerule Title-On-Title-Page-style bloom-padForOverflow"" lang=""z"" contenteditable=""true"" data-book=""bookTitle""></div>

                <div class=""bloom-editable bloom-nodefaultstylerule Title-On-Title-Page-style bloom-padForOverflow bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" lang=""en"" contenteditable=""true"" data-book=""bookTitle"" style=""padding-bottom: 4px;"" data-audiorecordingmode=""Sentence"">
                    <p><span id=""a68a7c09-2c4e-4cf7-8f47-1f6d99253c20"" class=""audio-sentence ui-audioCurrent"">Space!</span></p>
                </div>
            </div>
            <div class=""largeFlexGap""></div>

            <div class=""bloom-translationGroup"" data-default-languages=""N1"" id=""originalContributions"">
                <label class=""bubble"" data-link-text=""Paste Image Credits"" data-link-target=""PasteImageCredits()"">The contributions made by writers, illustrators, editors, etc., in {lang}</label>
                <div class=""bloom-editable credits bloom-copyFromOtherLanguageIfNecessary Content-On-Title-Page-style"" lang=""z"" contenteditable=""true"" data-book=""originalContributions""></div>
                <div class=""bloom-editable credits bloom-copyFromOtherLanguageIfNecessary Content-On-Title-Page-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" lang=""en"" contenteditable=""true"" data-book=""originalContributions""></div>
            </div>
            <div class=""smallFlexGap""></div>

            <div class=""bloom-translationGroup"" data-default-languages=""N1"" id=""funding"">
                <label class=""bubble"">Use this to acknowledge any funding agencies.</label>
                <div class=""bloom-editable funding Content-On-Title-Page-style bloom-copyFromOtherLanguageIfNecessary"" lang=""z"" contenteditable=""true"" data-book=""funding""></div>
                <div class=""bloom-editable funding Content-On-Title-Page-style bloom-copyFromOtherLanguageIfNecessary bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" lang=""en"" contenteditable=""true"" data-book=""funding""></div>
            </div>
            <div class=""largeFlexGap""></div>
            <div class=""Content-On-Title-Page-style"" id=""languageInformation""></div>

            <div class=""languagesOfBook"" data-derived=""languagesOfBook"" lang=""en"">
                English
            </div>
            <div class=""langName"" data-library=""dialect""></div>

            <div class=""langName bloom-writeOnly"" data-library=""languageLocation"">
                Spain
            </div>
            <div class=""fillPageFlexGap""></div>
            <div data-book=""title-page-branding-bottom-html"" lang=""*""></div>
        </div>
    </div>

    <div class=""bloom-page bloom-frontMatter credits A5Portrait side-left"" data-page=""required singleton"" data-export=""front-matter-credits"" data-xmatter-page=""credits"" id=""da6a6b58-8d3c-4dba-855d-179f42f51431"" lang=""en"" data-page-number="""">
        <div class=""pageLabel"" lang=""en"" data-i18n=""TemplateBooks.PageLabel.Credits Page"">
            Credits Page
        </div>
        <div class=""pageDescription"" lang=""en""></div>

        <div class=""marginBox"">
            <div data-book=""credits-page-branding-top-html"" lang=""*""></div>

            <div class=""bloom-metaData licenseAndCopyrightBlock"" data-functiononhintclick=""bookMetadataEditor"" data-hint=""Click to Edit Copyright &amp; License"" lang=""en"">
                <div class=""copyright Credits-Page-style"" data-derived=""copyright""></div>

                <div class=""licenseBlock"">
                    <img class=""licenseImage"" src=""license.png"" data-derived=""licenseImage"" alt=""""></img>

                    <div class=""licenseNotes Credits-Page-style"" data-derived=""licenseNotes""></div>
                </div>
            </div>

            <div class=""bloom-translationGroup versionAcknowledgments"" data-default-languages=""N1"">
                <label class=""bubble"">Acknowledgments for this version, in {lang}. For example, give credit to the translator for this version.</label>
                <div class=""bloom-editable versionAcknowledgments Credits-Page-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" lang=""en"" contenteditable=""true"" data-book=""versionAcknowledgments""></div>
            </div>

            <div class=""copyright Credits-Page-style"" data-derived=""originalCopyrightAndLicense""></div>

            <div class=""bloom-translationGroup originalAcknowledgments"" data-default-languages=""N1"">
                <label class=""bubble"">Original (or Shell) Acknowledgments in {lang}</label>

                <div class=""bloom-editable bloom-copyFromOtherLanguageIfNecessary Credits-Page-style"" lang=""z"" contenteditable=""true"" data-book=""originalAcknowledgments""></div>

                <div class=""bloom-editable bloom-copyFromOtherLanguageIfNecessary Credits-Page-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" lang=""en"" contenteditable=""true"" data-book=""originalAcknowledgments""></div>
            </div>

            <div class=""ISBNContainer"" data-hint=""International Standard Book Number. Leave blank if you don't have one of these."">
                <span class=""bloom-doNotPublishIfParentOtherwiseEmpty Credits-Page-style"">ISBN</span>

                <div class=""bloom-translationGroup bloom-recording-optional"" data-default-languages=""*"">
                    <div class=""bloom-editable Credits-Page-style bloom-visibility-code-on"" data-book=""ISBN"" lang=""*""></div>
                    <div class=""bloom-editable Credits-Page-style bloom-content1 bloom-contentNational1"" data-book=""ISBN"" lang=""en""></div>
                </div>
            </div>
            <div class=""Credits-Page-style"" data-book=""credits-page-branding-bottom-html"" lang=""*""></div>
        </div>
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
                                <div class=""bloom-imageContainer bloom-leadingElement"" title=""Name: galaxy.jpg Size: 178.00 kb Dots: 1041 x 781 For the current paper size: • The image container is 406 x 231 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ✓ This image would print at 325 DPI. • An image with 1269 x 722 dots would fill this container at 300 DPI.""><img src=""galaxy.jpg"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img></div>
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
                        <div class=""bloom-imageContainer"" title=""Name: mars2.jpg Size: 130.10 kb Dots: 1041 x 447 For the current paper size: • The image container is 406 x 203 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ⚠ This image would print at 246 DPI. • An image with 1269 x 635 dots would fill this container at 300 DPI.""><img src=""mars2.jpg"" alt="""" data-copyright="""" data-creator="""" data-license=""cc-by""></img></div>
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

    <div class=""bloom-page cover coverColor insideBackCover bloom-backMatter A5Portrait side-right"" data-page=""required singleton"" data-export=""back-matter-inside-back-cover"" data-xmatter-page=""insideBackCover"" id=""b21d9f05-b4bf-433a-8356-7e4c16c56729"" lang=""en"" data-page-number="""">
        <div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Inside Back Cover"" data-after-content=""Traditional Front/Back Matter"" lang=""en"">
            Inside Back Cover
        </div>

        <div class=""pageDescription"" lang=""en""></div>

        <div class=""marginBox"">
            <div class=""bloom-translationGroup"" data-default-languages=""N1"">
                <div class=""bloom-editable Inside-Back-Cover-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" data-book=""insideBackCover"" data-languagetipcontent=""English"" data-hint=""If you need somewhere to put more information about the book, you can use this page, which is the inside of the back cover."" data-hasqtip=""true"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" lang=""en"" contenteditable=""true""></div>
            </div>
        </div>
    </div>

    <div class=""bloom-page cover coverColor outsideBackCover bloom-backMatter A5Portrait side-left"" data-page=""required singleton"" data-export=""back-matter-back-cover"" data-xmatter-page=""outsideBackCover"" id=""b471e133-0f64-4ca2-b9df-bdd49a58a012"" lang=""en"" data-page-number="""">
        <div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Outside Back Cover"" data-after-content=""Traditional Front/Back Matter"" lang=""en"">
            Outside Back Cover
        </div>

        <div class=""pageDescription"" lang=""en""></div>

        <div class=""marginBox"">
            <div data-book=""outside-back-cover-branding-top-html"" lang=""*""></div>

            <div class=""bloom-translationGroup"" data-default-languages=""N1"">
                <div class=""bloom-editable Outside-Back-Cover-style bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" data-book=""outsideBackCover"" data-languagetipcontent=""English"" data-hint=""If you need somewhere to put more information about the book, you can use this page, which is the outside of the back cover."" data-hasqtip=""true"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" aria-describedby=""qtip-1"" lang=""en"" contenteditable=""true""></div>
            </div>

            <div data-book=""outside-back-cover-branding-bottom-html"" lang=""*""><img class=""branding"" src=""BloomWithTaglineAgainstLight.svg"" alt=""This picture, BloomWithTaglineAgainstLight.svg, is missing or was loading too slowly.""></img></div>
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
			_sheetFromExport = _exporter.Export(dom,  "fakeImagesFolderpath");
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
			(_imageRows, _textRows) = SpreadsheetTests.splitRows(_rows);
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
			var imageSource = _sheet.ColumnForTag(InternalSpreadsheet.ImageSourceLabel);
			Assert.That(_imageRows[0].GetCell(imageSource).Text.EndsWith("galaxy.jpg"));
			Assert.That(_imageRows[1].GetCell(imageSource).Text.EndsWith("mars2.jpg"));
			Assert.That(_imageRows[2].GetCell(imageSource).Text.EndsWith("bh.jpg"));

			Assert.That(_textRows[0].GetCell(imageSource).Text, Is.EqualTo(""));
		}
	}
}
