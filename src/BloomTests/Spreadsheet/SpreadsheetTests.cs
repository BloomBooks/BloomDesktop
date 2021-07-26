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
}
