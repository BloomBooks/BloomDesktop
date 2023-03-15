using Bloom.Book;
using Bloom.Spreadsheet;
using Moq;
using NUnit.Framework;
using OfficeOpenXml;
using SIL.IO;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BloomTests.Spreadsheet
{
	/// <summary>
	/// This class tests a variety of things that can be checked in the process of
	/// exporting a single document to our spreadsheet format.
	/// </summary>
	public class SpreadsheetTests
	{
		//Note the BloomDataDiv here is for testing and doesn't completely match the rest of the book
		public const string kSimpleTwoPageBook = @"
<html>
	<head>
	</head>

	<body data-l1=""en"" data-l2=""en"" data-l3="""">
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
				English, French, German
			</div>

			<div data-book=""bookTitle"" lang=""fr"" class="" bloom-editable bloom-nodefaultstylerule bloom-padForOverflow"" style=""padding-bottom: 0px;"" contenteditable=""true"">
				<p>Is it the End of the French World?</p>
			</div>

			<div data-book=""bookTitle"" lang=""de"" class="" bloom-editable bloom-nodefaultstylerule bloom-padForOverflow"" style=""padding-bottom: 0px;"" contenteditable=""true"">
				<p>Is it the End of the German World?</p>
			</div>

			<div data-book=""coverImage"" style=""background-image:url('New%20Template%203.jpg')"" lang=""*"" src=""Cover.jpg"" alt=""This picture,Cover.jpg, is missing or was loading too slowly."" data-copyright="""" data-creator="""" data-license="""">
				Cover.jpg
			</div>

			<div data-book=""originalTitle"" lang=""*"">
				Is it the End of the World?
			</div>

			<div data-book=""outside-back-cover-branding-bottom-html"" lang=""*""><img class=""branding"" src=""BloomWithTaglineAgainstLight.svg"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img></div>

			<div data-book=""copyright"" lang=""*"">Copyright C 2022 Somone</div>
			<div data-book=""licenseUrl"" lang=""*"">
				http://creativecommons.org/licenses/by/4.0/
			</div>
			<div data-book=""licenseNotes"" lang=""*"">
				Be nice to the author
			</div>

			<div data-book=""licenseDescription"" lang=""fr"">
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
							<div class=""box-header-off bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
			                   <div class=""bloom-editable normal-style"" style="""" lang=""z"" contenteditable=""true"">
			                        <p></p>
			                    </div>
								<div class=""bloom-editable normal-style"" style="""" lang=""en"" contenteditable=""true"">
			                        <p></p>
			                    </div>
			                </div>
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
		<div class=""bloom-page numberedPage customPage bloom-combinedPage side-right Device16x9Landscape bloom-monolingual"" data-page="""" id=""958a4d5c-053a-456d-996d-5fe779397ed5"" data-tool-id=""signLanguage"" data-pagelineage=""08422e7b-9406-4d11-8c71-02005b1b8095"" data-page-number=""1"" lang="""">
	        <div class=""pageLabel"" lang=""en"" data-i18n=""TemplateBooks.PageLabel.Big Text Diglot"">
	            Big Text Diglot
	        </div>
	        <div class=""pageDescription"" lang=""en""></div>

	        <div class=""marginBox"">
	            <div class=""split-pane horizontal-percent"" style=""min-height: 42px;"">
	                <div class=""split-pane-component position-top"" style=""bottom: 33.9339%"">
	                    <div class=""split-pane-component-inner"">
	                        <div class=""split-pane vertical-percent"" style=""min-width: 0px;"">
	                            <div class=""split-pane-component position-left"">
	                                <div class=""split-pane-component-inner"" min-height=""60px 150px 250px"" min-width=""60px 150px"" style=""position: relative;"">
										<div class=""bloom-widgetContainer bloom-leadingElement"">
						                    <iframe src=""activities/balldragTouch/index.html"">Must have a closing tag in HTML</iframe>
						                </div>
									</div>
	                            </div>
	                            <div class=""split-pane-divider vertical-divider""></div>

	                            <div class=""split-pane-component position-right"">
	                                <div class=""split-pane-component-inner"" min-height=""60px 150px 250px"" min-width=""60px 150px"" style=""position: relative;"">
	                                    <div class=""bloom-videoContainer bloom-leadingElement bloom-selected"">
	                                        <video>
	                                        <source src=""video/ac2e237a-140f-45e4-b3e8-257dd12f4793.mp4""></source></video>
	                                    </div>
	                                </div>
	                            </div>
	                        </div>
	                    </div>
	                </div>
	                <div class=""split-pane-divider horizontal-divider"" style=""bottom: 33.9339%"" title=""CTRL for precision. Double click to match previous page."" data-splitter-label=""66%""></div>

	                <div class=""split-pane-component position-bottom"" style=""height: 33.9339%"">
	                    <div class=""split-pane-component-inner"">
	                        <div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
	                            <div class=""bloom-editable normal-style"" lang=""z"" contenteditable=""true"" style="""">
	                                <p></p>
	                            </div>

	                            <div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" lang=""en"" contenteditable=""true"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""Aklanon"">
	                                <p>A castle with a very big flag</p>
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
		[OneTimeSetUp]
		public void OneTimeSetUp()
		{
			var dom = new HtmlDom(kSimpleTwoPageBook, true);
			var langCodesToLangNames = new Dictionary<string, string>();
			langCodesToLangNames.Add("en", "English");
			langCodesToLangNames.Add("fr", "French");
			langCodesToLangNames.Add("de", "German");
			var mockLangDisplayNameResolver = new Mock<ILanguageDisplayNameResolver>();
			mockLangDisplayNameResolver.Setup(x => x.GetLanguageDisplayName("en")).Returns("English");
			mockLangDisplayNameResolver.Setup(x => x.GetLanguageDisplayName("fr")).Returns("French");
			mockLangDisplayNameResolver.Setup(x => x.GetLanguageDisplayName("de")).Returns("German");
			_exporter = new SpreadsheetExporter(mockLangDisplayNameResolver.Object);
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

			_pageContentRows = _rows.Where(r => r.MetadataKey == InternalSpreadsheet.PageContentRowLabel).ToList();
		}

		[TestCase("fromExport")]
		[TestCase("fromFile")]
		public void HeaderHasExpectedNumRows(string source)
		{
			SetupFor(source);
			Assert.That(_sheet.Header.RowCount, Is.EqualTo(2));
		}

		[TestCase("fromExport")]
		[TestCase("fromFile")]
		public void HeaderHasExpectedNumCols(string source)
		{
			SetupFor(source);
			// was originally 8, but our test data has some audio,
			// which we're not really testing here, but it adds some columns.
			// We also now add a video and widget column and page labels
			Assert.That(_sheet.Header.ColumnCount, Is.EqualTo(17));
		}

		[TestCase("fromExport")]
		[TestCase("fromFile")]
		public void HeaderHiddenRowsStillAtTop(string source)
		{
			SetupFor(source);
			var firstRow = _sheet.AllRows().First();

			Assert.That(firstRow.Hidden, Is.True);
		}

		[TestCase("fromExport")]
		[TestCase("fromFile")]
		public void HeaderFirstRowContainsColumnIds(string source)
		{
			SetupFor(source);
			var firstRow = _sheet.AllRows().First();

			Assert.That(firstRow.GetCell(0).Content, Is.EqualTo(InternalSpreadsheet.RowTypeColumnLabel));
			Assert.That(firstRow.GetCell(5).Content, Is.EqualTo("[de]"));
		}

		[TestCase("fromExport")]
		[TestCase("fromFile")]
		public void HeaderSecondRowContainsColumnFriendlyNames(string source)
		{
			// Note: This test depends on the Mocked ILanguageDisplayNameResolver
			SetupFor(source);
			var secondRow = _sheet.AllRows().ToList()[1];

			Assert.That(secondRow.GetCell(0).Content, Is.EqualTo(InternalSpreadsheet.RowTypeColumnFriendlyName));
			Assert.That(secondRow.GetCell(5).Content, Is.EqualTo("German"));
		}

		[TestCase("fromExport")]
		[TestCase("fromFile")]
		public void SavesTextFromBlocks(string source)
		{
			SetupFor(source);
			var enCol = _sheet.GetRequiredColumnForLang("en");
			var frCol = _sheet.GetRequiredColumnForLang("fr");
			var deCol = _sheet.GetRequiredColumnForLang("de");
			// The first row has data from the second element in the document, because of tabindex.
			Assert.That(_pageContentRows[0].GetCell(enCol).Text, Is.EqualTo("This elephant is running amok. Causing much damage."));
			Assert.That(_pageContentRows[0].GetCell(frCol).Text, Is.EqualTo("This French elephant is running amok. Causing much damage."));
			Assert.That(_pageContentRows[1].GetCell(enCol).Text, Is.EqualTo("Elephants should be handled with much care."));

			Assert.That(_pageContentRows[1].GetCell(frCol).Text, Is.EqualTo("French elephants should be handled with special care."));
			Assert.That(_pageContentRows[1].GetCell(deCol).Text, Is.EqualTo("German elephants are quite orderly."));

			Assert.That(_pageContentRows[2].GetCell(enCol).Text, Is.EqualTo("Riding on elephants can be risky."));
			Assert.That(_pageContentRows[2].GetCell(frCol).Text, Is.EqualTo("Riding on French elephants can be more risky."));
			Assert.That(_pageContentRows[2].GetCell(deCol).Text, Is.EqualTo("Riding on German elephants can be less risky."));

		}

		[TestCase("fromExport")]
		[TestCase("fromFile")]
		public void SavesLangData(string source)
		{
			SetupFor(source);
			Assert.That(_sheet.LangCount, Is.EqualTo(4)); //en, fr, de, and *
		}

		[TestCase("fromExport")]
		[TestCase("fromFile")]
		public void AddsRowLabels(string source)
		{
			SetupFor(source);
			var pageNumIndex = _sheet.GetColumnForTag(InternalSpreadsheet.PageNumberColumnLabel);

			Assert.That(_pageContentRows[0].GetCell(pageNumIndex).Content, Is.EqualTo("1"));
			Assert.That(_pageContentRows[1].GetCell(pageNumIndex).Content, Is.EqualTo("1"));
			Assert.That(_pageContentRows[2].GetCell(pageNumIndex).Content, Is.EqualTo("2"));
			Assert.That(_pageContentRows[0].GetCell(0).Content, Is.EqualTo(InternalSpreadsheet.PageContentRowLabel));
			Assert.That(_pageContentRows[1].GetCell(0).Content, Is.EqualTo(InternalSpreadsheet.PageContentRowLabel));
			Assert.That(_pageContentRows[2].GetCell(0).Content, Is.EqualTo(InternalSpreadsheet.PageContentRowLabel));
		}

		[TestCase("fromExport")]
		[TestCase("fromFile")]
		public void AddsPageTypes(string source)
		{
			SetupFor(source);
			var pageTypeIndex = _sheet.GetColumnForTag(InternalSpreadsheet.PageTypeColumnLabel);

			Assert.That(_pageContentRows[0].GetCell(pageTypeIndex).Content, Is.EqualTo("Basic Text & Picture"));
			Assert.That(_pageContentRows[1].GetCell(pageTypeIndex).Content, Is.EqualTo(""));
			Assert.That(_pageContentRows[2].GetCell(pageTypeIndex).Content, Is.EqualTo("Basic Text & Picture"));
			Assert.That(_pageContentRows[3].GetCell(pageTypeIndex).Content, Is.EqualTo("Big Text Diglot"));
		}

		[TestCase("fromExport")]
		[TestCase("fromFile")]
		public void MakesVideoCell(string source)
		{
			SetupFor(source);
			var videoIndex = _sheet.GetColumnForTag(InternalSpreadsheet.VideoSourceColumnLabel);
			Assert.That(videoIndex, Is.GreaterThan(0));

			Assert.That(_pageContentRows[3].GetCell(videoIndex).Content, Is.EqualTo("video/ac2e237a-140f-45e4-b3e8-257dd12f4793.mp4"));
		}

		[TestCase("fromExport")]
		[TestCase("fromFile")]
		public void MakesWidgetCell(string source)
		{
			SetupFor(source);
			var widgetIndex = _sheet.GetColumnForTag(InternalSpreadsheet.WidgetSourceColumnLabel);
			Assert.That(widgetIndex, Is.GreaterThan(0));

			Assert.That(_pageContentRows[3].GetCell(widgetIndex).Content, Is.EqualTo("activities/balldragTouch/index.html"));
		}

		[Test]
		public void ColumnsNotLanguagesNotCounted()
		{
			// Setup
			var sheet = new InternalSpreadsheet();
			var offset = sheet.StandardLeadingColumns.Length;
			sheet.AddColumnForLang("en", "English");
			sheet.AddColumnForLang("es", "Español");

			// System under test
			var langs = sheet.Languages;

			// Verification
			Assert.That(langs, Has.Count.EqualTo(2));
			Assert.That(langs, Has.Member("en"));
			Assert.That(langs, Has.Member("es"));
		}

		[Test]
		public void RichtextNotUsedWhenNotNeeded()
		{
			using (var tempFile = TempFile.WithExtension("xslx"))
			{
				_sheetFromExport.WriteToFile(tempFile.Path);
				var info = new FileInfo(tempFile.Path);
				using (var package = new ExcelPackage(info))
				{
					var worksheet = package.Workbook.Worksheets[0];
					int c = _sheetFromExport.StandardLeadingColumns.Length;
					for (int r = 0; r < 4; r++)
					{
						ExcelRange currentCell = worksheet.Cells[r + 1, c + 1];
						Assert.That(!currentCell.IsRichText);
					}
				}
			}
		}

		[Test]
		public void SpreadsheetIOWriteSpreadsheet_RetainMarkupOptionRetainsMarkup()
		{
			using (var tempFile = TempFile.WithExtension("xslx"))
			{
				SpreadsheetIO.WriteSpreadsheet(_sheetFromExport, tempFile.Path, retainMarkup:true);
				var info = new FileInfo(tempFile.Path);
				using (var package = new ExcelPackage(info))
				{
					var worksheet = package.Workbook.Worksheets[0];
					int c = _sheetFromExport.GetRequiredColumnForLang("en");
					var rowCount = worksheet.Dimension.Rows;
					//The first TextGroup row should contain the marked-up string we are looking for
					for (int r = 0; true; r++)
					{
						Assert.That(r, Is.LessThan(rowCount), "did not find expected TextGroup row");
						ExcelRange rowTypeCell = worksheet.Cells[r + 1, 1];
						if (rowTypeCell.Value.ToString().Equals(InternalSpreadsheet.PageContentRowLabel))
						{
							string markedUp = @"<p><span id=""e4bc05e5-4d65-4016-9bf3-ab44a0df3ea2"" class=""bloom-highlightSegment"" recordingmd5=""undefined"">This elephant is running amok.</span> <span id=""i2ba966b6-4212-4821-9268-04e820e95f50"" class=""bloom-highlightSegment"" recordingmd5=""undefined"">Causing much damage.</span></p>";
							ExcelRange textCell = worksheet.Cells[r + 1, c + 1];
							Assert.That(textCell.Value.ToString().Contains(markedUp));
							break;
						}
					}
				}
			}
		}

		[Test]
		public void SpreadsheetIOWriteSpreadsheet_HeaderFriendlyNameForImageNotOverwritten()
		{
			using (var tempFile = TempFile.WithExtension("xslx"))
			{
				SpreadsheetIO.WriteSpreadsheet(_sheetFromExport, tempFile.Path, retainMarkup:false);
				var info = new FileInfo(tempFile.Path);
				using (var package = new ExcelPackage(info))
				{
					var worksheet = package.Workbook.Worksheets[0];
					var cell = worksheet.Cells[2, 4];	// Note: This uses 1-based index
					Assert.That(cell.Value.ToString(), Is.EqualTo("Image"));
				}
			}
		}
	}
}
