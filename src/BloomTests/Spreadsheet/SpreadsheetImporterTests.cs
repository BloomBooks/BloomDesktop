using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Bloom.Book;
using Bloom.Spreadsheet;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.Spreadsheet
{
	/// <summary>
	/// Class tests using spreadsheet IO to change the language of a set of blocks.
	/// Also the content of a couple of other elements is changed.
	/// </summary>
	public class SpreadsheetImporterChangeLanguageTests
	{
		private InternalSpreadsheet _sheet;
		protected HtmlDom _dom;

		[OneTimeSetUp]
		public void OneTimeSetUp()
		{
			_dom = new HtmlDom(SpreadsheetTests.kSimpleTwoPageBook, true);
			AssertThatXmlIn.Dom(_dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en']/p[text()='Riding on elephants can be risky.']", 1); // unchanged

			// The tests in this class all check the results of importing what export produced,
			// but with a couple of changes. Here we locate the cells produced from two particular
			// bloom-editable elements in the kSimpleTwoPageBook DOM and replace them with different text.
			// The import should update those bloom-editables to these changed values.
			var exporter = new SpreadsheetExporter();
			_sheet = exporter.Export(_dom, "fakeImagesFolderpath");
			var indexDe = _sheet.ColumnForLang("de");
			_sheet.Header.SetCell(indexDe, "[tpi]");
			var engColumn = _sheet.ColumnForLang("en");
			_sheet.ContentRows.First(row => row.GetCell(engColumn).Content == "This elephant is running amok. Causing much damage.")
				.SetCell(engColumn, "<p>This elephant is running amok.</p>");
			var frColumn = _sheet.ColumnForLang("fr");
			_sheet.ContentRows.First(row => row.GetCell(frColumn).Content == "Riding on French elephants can be more risky.")
				.SetCell(frColumn, "<p>Riding on French elephants can be very risky.</p>");
			var importer = new SpreadsheetImporter(this._dom, _sheet);
			InitializeImporter(importer);
			importer.Import();
		}

		/// <summary>
		/// Provides a hook where a subclass can change something to apply similar tests with
		/// a slightly different state (e.g., different import params).
		/// </summary>
		protected virtual void InitializeImporter(SpreadsheetImporter importer)
		{ 
		}

		[Test]
		public void TokPisinAdded()
		{
			var assertDom = AssertThatXmlIn.Dom(_dom.RawDom);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-editable') and @lang='tpi']", 2);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='tpi']/p[text()='German elephants are quite orderly.']", 1);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='tpi']/p[text()='Riding on German elephants can be less risky.']", 1);
		}

		[Test]
		public virtual void GermanKept()
		{
			var assertDom = AssertThatXmlIn.Dom(_dom.RawDom);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-editable') and @lang='de']", 2);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='de']/p[text()='German elephants are quite orderly.']", 1);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='de']/p[text()='Riding on German elephants can be less risky.']", 1);
		}

		[Test]
		public void EnglishAndFrenchKeptOrModified()
		{
			var assertDom = AssertThatXmlIn.Dom(_dom.RawDom);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-editable') and @lang='en']", 6);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-editable') and @lang='fr']", 3);
			// Make sure these are in the right places. We put this in the first row, which because of tab index should be imported to the SECOND TG.
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@tabindex='1']/div[@lang='en']/p[text()='This elephant is running amok.']", 1); // modified
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@id='group3']/div[@lang='fr']/p[text()='Riding on French elephants can be very risky.']", 1); // modified
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@id='group3']/div[@lang='en']/p[text()='Riding on elephants can be risky.']", 1); // unchanged
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@tabindex='2']/div[@lang='fr']/p[text()='French elephants should be handled with special care.']", 1); // unchanged
		}
	}
	/// <summary>
	/// Test the alternative behaviour where languages not in the spreadsheet are cleaned out.
	/// </summary>
	public class SpreadsheetImporterChangeLanguageCleanTests : SpreadsheetImporterChangeLanguageTests
	{
		protected override void InitializeImporter(SpreadsheetImporter importer)
		{
			base.InitializeImporter(importer);
			importer.Params = new SpreadsheetImportParams() {RemoveOtherLanguages = true};
		}

		// In this test subclass, which runs the import with RemoveOtherLanguages true, German should be removed.
		// We override the GermanKept test and do NOT mark it as a test so it will not be included in
		// the test cases for this subclass.
		public override void GermanKept()
		{
		}

		[Test]
		public void GermanRemoved()
		{
		var assertDom = AssertThatXmlIn.Dom(_dom.RawDom);
			assertDom.HasNoMatchForXpath("//div[contains(@class, 'bloom-editable') and @lang='de']");
		}
	}

	public class SpreadsheetImportSyncWarnings
	{
		// input DOM has page 1 (two blocks) and 2 (one block) and 5 (two blocks)
		const string inputBook = @"
<html>
	<head>
	</head>

	<body data-l1=""en"" data-l2=""en"" data-l3="""">
	     <div class=""bloom-page numberedPage customPage A5Portrait side-right "" data-page="""" id=""7403192b-f306-4653-b7b1-0acf7163f4b9"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""1"" lang="""">
	        <div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Basic Text &amp; Picture"" lang=""en"">
	            Basic Text &amp; Picture
	        </div>
	        <div class=""pageDescription"" lang=""en""></div>

	        <div class=""marginBox"">
                <div class=""split-pane-component position-bottom"" style=""height: 50%"">
                    <div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
                        <div class=""bloom-editable normal-style bloom-postAudioSplit audio-sentence bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-audiorecordingmode=""TextBox"" id=""i4b150910-b53d-4779-a1fb-8177982c651c"" recordingmd5=""9134cd4f71cf3d6e148a6c9b4afed8dc"" data-duration=""4.245963"" data-languagetipcontent=""English"" data-audiorecordingendtimes=""2.920 4.160"" lang=""en"" contenteditable=""true"">
                            <p><span id=""e4bc05e5-4d65-4016-9bf3-ab44a0df3ea2"" class=""bloom-highlightSegment"" recordingmd5=""undefined"">This elephant is running amok.</span> <span id=""i2ba966b6-4212-4821-9268-04e820e95f50"" class=""bloom-highlightSegment"" recordingmd5=""undefined"">Causing much damage.</span></p>
                        </div>
						<div class=""bloom-editable normal-style bloom-postAudioSplit audio-sentence bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-audiorecordingmode=""TextBox"" id=""i4b150910-b53d-4779-a1fb-8177982c651c"" recordingmd5=""9134cd4f71cf3d6e148a6c9b4afed8dc"" data-duration=""4.245963"" data-languagetipcontent=""French"" data-audiorecordingendtimes=""2.920 4.160"" lang=""fr"" contenteditable=""true"">
                            <p>This French elephant is running amok. Causing much damage.</p>
                        </div>
						<div class=""bloom-editable normal-style bloom-postAudioSplit audio-sentence bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-audiorecordingmode=""TextBox"" id=""i4b150910-b53d-4779-a1fb-8177982c651c"" recordingmd5=""9134cd4f71cf3d6e148a6c9b4afed8dc""  data-languagetipcontent=""Spanish""  lang=""es"" contenteditable=""true"">
                            <p>This Spanish elephant is running amok. Causing much damage.</p>
                        </div>
                        <div class=""bloom-editable normal-style"" style="""" lang=""z"" contenteditable=""true"">
                            <p></p>
                        </div>
                    </div>
					<div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
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
	            </div>
	        </div>
	    </div>
		<div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-right bloom-monolingual"" data-page="""" id=""7403192b-f306-4653-b7b1-0acf7163f4b9"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""2"" lang="""">
	        <div class=""marginBox"">
				<div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
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
		<div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-right bloom-monolingual"" data-page="""" id=""7403192b-f306-4653-b7b1-0acf7163f4b9"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""5"" lang="""">
	        <div class=""marginBox"">
				<div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
					<div class=""bloom-editable normal-style bloom-postAudioSplit audio-sentence bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" lang=""de"" contenteditable=""true"">
                        <p>German elephants triumph!</p>
                    </div>
					<div class=""bloom-editable normal-style bloom-postAudioSplit audio-sentence bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""English"" data-audiorecordingendtimes=""2.920 4.160"" lang=""en"" contenteditable=""true"">
                        <p>English elephants triumph!</p>
                    </div>
					<div class=""bloom-editable normal-style bloom-postAudioSplit audio-sentence bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""French"" data-audiorecordingendtimes=""2.920 4.160"" lang=""fr"" contenteditable=""true"">
                        <p>French elephants triumph!</p>
                    </div>

                    <div class=""bloom-editable normal-style"" style="""" lang=""z"" contenteditable=""true"">
                        <p></p>
                    </div>
                </div>
				<div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
					<div class=""bloom-editable normal-style bloom-postAudioSplit audio-sentence bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" lang=""de"" contenteditable=""true"">
                        <p>German elephants fail.</p>
                    </div>
					<div class=""bloom-editable normal-style bloom-postAudioSplit audio-sentence bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""English"" data-audiorecordingendtimes=""2.920 4.160"" lang=""en"" contenteditable=""true"">
                        <p>English elephants fail.</p>
                    </div>
					<div class=""bloom-editable normal-style bloom-postAudioSplit audio-sentence bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""French"" data-audiorecordingendtimes=""2.920 4.160"" lang=""fr"" contenteditable=""true"">
                        <p>French elephants fail.</p>
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
		private InternalSpreadsheet _sheet;
		private HtmlDom _dom;
		private List<string> _messages;

		[OneTimeSetUp]
		public void OneTimeSetUp()
		{
			// Cases we want to test:
			// - page has too many translation groups
			//		- V1: warn, leave extras unmodified
			//		- eventual: ?
			// - page has too few translation groups (special case: none)
			//		- V1: warn, drop extra inputs
			//		- V2: insert extra copies of page (and something else if it has no TGs)
			//		- eventual: insert extra pages using template specified in sheet
			// - there are input lines for pages that don't exist (pathological, in the middle; likely, after last; test both)
			//		- V1: warn, ignore
			//		- eventual: insert extra pages using template specified in sheet
			// - there are pages that have no input lines (but later pages do)
			// - there are pages after the last input line
			//		- V1: warn, leave unchanged.
			//		- eventual: ?
			// In this test suite, the blocks on the last page and the pages run out before
			// we run out of lines. So we can't test the case of running out of lines first.
			// Another class tests that.
			_dom = new HtmlDom(inputBook, true);
			_sheet = new InternalSpreadsheet();
			_sheet.ColumnForLang("en");
			_sheet.ColumnForLang("fr");
			MakeRow("1","New message about tigers", "New message about French tigers", _sheet);

			// problem 1: input DOM has a second TG on page 1; sheet does not.
			MakeRow("2", "More about tigers", "More about French tigers", _sheet);
			// problem 2: input DOM has no second TG on page 2.
			MakeRow("2", "Still more about tigers", "Still more about French tigers", _sheet);
			MakeRow("2", "More and more about tigers", "More and more about French tigers", _sheet);
			// problem 3: input DOM has no page 3 at all.
			MakeRow("3", "Lost story about tigers", "Lost story about French tigers", _sheet);
			MakeRow("3", "Another lost story about tigers", "Another lost story about French tigers", _sheet);
			// problem 4: input DOM has no page 4 at all.
			MakeRow("4", "Yet another lost story about tigers", "Yet another lost story about French tigers", _sheet);
			MakeRow("5", "A good story about tigers", "A good story about French tigers", _sheet);
			MakeRow("5", "Another good story about tigers", "Another good story about French tigers", _sheet);
			// problem 5: an extra block on page 5
			MakeRow("5", "Page 5 lost story about tigers", "Page 5 lost story about French tigers", _sheet);
			// problem 6: an extra block on after the last page
			MakeRow("6", "Page 6 lost story about tigers", "Page 6 lost story about French tigers", _sheet);
			var importer = new SpreadsheetImporter(this._dom, _sheet);
			_messages=importer.Import();
		}

		public static void MakeRow(string pageNum, string langData1, string langData2, InternalSpreadsheet spreadsheet)
		{
			var newRow = new ContentRow(spreadsheet);
			newRow.SetCell(InternalSpreadsheet.MetadataKeyLabel, InternalSpreadsheet.TextGroupLabel);
			newRow.SetCell(InternalSpreadsheet.PageNumberLabel, pageNum);
			newRow.SetCell(InternalSpreadsheet.TextIndexOnPageLabel, "1");// group index placeholder, not exactly right but near enough for this test
			newRow.SetCell("[en]", "<p>" + langData1 + "</p>");
			newRow.SetCell("[fr]", "<p>" + langData2 + "</p>");
		}

		[Test]
		public void UpdatesExpectedTGs()
		{
			var assertDom = AssertThatXmlIn.Dom(_dom.RawDom);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@data-page-number='1']//div[@lang='en']/p[text()='New message about tigers']", 1);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@data-page-number='1']//div[@lang='fr']/p[text()='New message about French tigers']", 1);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@data-page-number='2']//div[@lang='en']/p[text()='More about tigers']", 1);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@data-page-number='2']//div[@lang='fr']/p[text()='More about French tigers']", 1);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@data-page-number='5']//div[@lang='en']/p[text()='A good story about tigers']", 1);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@data-page-number='5']//div[@lang='fr']/p[text()='A good story about French tigers']", 1);
		}

		[Test]
		public void LeavesOtherTGsAlone()
		{
			var assertDom = AssertThatXmlIn.Dom(_dom.RawDom);
			// No input columns for Spanish
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@data-page-number='1']//div[@lang='es']/p[text()='This Spanish elephant is running amok. Causing much damage.']", 1);
			// No input row for page 1 block 2
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@data-page-number='1']//div[@lang='en']/p[text()='Elephants should be handled with much care.']", 1);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@data-page-number='1']//div[@lang='fr']/p[text()='French elephants should be handled with special care.']", 1);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@data-page-number='1']//div[@lang='de']/p[text()='German elephants are quite orderly.']", 1);
		}

		[Test]
		public void GotMissingInputWarning()
		{
			Assert.That(_messages, Contains.Item("No input row found for block 2 of page 1"));
		}

		[Test]
		public void GotMissingBlockWarning()
		{
			Assert.That(_messages, Contains.Item("Input has 3 row(s) for page 2, but page 2 has only 1 place(s) for text"));
			Assert.That(_messages, Contains.Item("Input has 3 row(s) for page 5, but page 5 has only 2 place(s) for text"));
		}

		[Test]
		public void GotMissingPagesWarning()
		{
			Assert.That(_messages, Contains.Item("Input has rows for page 3, but document has no page 3 that can hold text"));
			Assert.That(_messages, Contains.Item("Input has rows for page 4, but document has no page 4 that can hold text"));
			Assert.That(_messages, Contains.Item("Input has rows for page 6, but document has no page 6 that can hold text"));
		}
	}

	public class SpreadsheetImportSyncWarningsRunOutOfLines
	{
		// input has three content pages. Second has two blocks, the others, one. There's also
		// a couple of non-content pages (one with no TGs, one with no data-page-number)
		// to check that we skip those.
		const string inputBook = @"
<html>
	<head>
	</head>

	<body data-l1=""en"" data-l2=""en"" data-l3="""">
	     <div class=""bloom-page numberedPage customPage A5Portrait side-right "" data-page="""" id=""7403192b-f306-4653-b7b1-0acf7163f4b9"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""1"" lang="""">
	        <div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Basic Text &amp; Picture"" lang=""en"">
	            Basic Text &amp; Picture
	        </div>
	        <div class=""pageDescription"" lang=""en""></div>

	        <div class=""marginBox"">
                <div class=""split-pane-component position-bottom"" style=""height: 50%"">
                    <div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
                        <div class=""bloom-editable normal-style bloom-postAudioSplit audio-sentence bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-audiorecordingmode=""TextBox"" id=""i4b150910-b53d-4779-a1fb-8177982c651c"" recordingmd5=""9134cd4f71cf3d6e148a6c9b4afed8dc"" data-duration=""4.245963"" data-languagetipcontent=""English"" data-audiorecordingendtimes=""2.920 4.160"" lang=""en"" contenteditable=""true"">
                            <p><span id=""e4bc05e5-4d65-4016-9bf3-ab44a0df3ea2"" class=""bloom-highlightSegment"" recordingmd5=""undefined"">This elephant is running amok.</span> <span id=""i2ba966b6-4212-4821-9268-04e820e95f50"" class=""bloom-highlightSegment"" recordingmd5=""undefined"">Causing much damage.</span></p>
                        </div>
						<div class=""bloom-editable normal-style bloom-postAudioSplit audio-sentence bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-audiorecordingmode=""TextBox"" id=""i4b150910-b53d-4779-a1fb-8177982c651c"" recordingmd5=""9134cd4f71cf3d6e148a6c9b4afed8dc"" data-duration=""4.245963"" data-languagetipcontent=""French"" data-audiorecordingendtimes=""2.920 4.160"" lang=""fr"" contenteditable=""true"">
                            <p>This French elephant is running amok. Causing much damage.</p>
                        </div>
						<div class=""bloom-editable normal-style bloom-postAudioSplit audio-sentence bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-audiorecordingmode=""TextBox"" id=""i4b150910-b53d-4779-a1fb-8177982c651c"" recordingmd5=""9134cd4f71cf3d6e148a6c9b4afed8dc""  data-languagetipcontent=""Spanish""  lang=""es"" contenteditable=""true"">
                            <p>This Spanish elephant is running amok. Causing much damage.</p>
                        </div>
                        <div class=""bloom-editable normal-style"" style="""" lang=""z"" contenteditable=""true"">
                            <p></p>
                        </div>
                    </div>
	            </div>
	        </div>
	    </div>
		<div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-right bloom-monolingual"" data-page="""" id=""7403192b-f306-4653-b7b1-0acf7163f4b9"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""2"" lang="""">
	        <div class=""marginBox"">
				<div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
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
				<div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
					<div class=""bloom-editable normal-style bloom-postAudioSplit audio-sentence bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""English"" data-audiorecordingendtimes=""2.920 4.160"" lang=""en"" contenteditable=""true"">
                        <p>There's no input for this block</p>
                    </div>
                    <div class=""bloom-editable normal-style"" style="""" lang=""z"" contenteditable=""true"">
                        <p></p>
                    </div>
                </div>
	        </div>
	    </div>
		<div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-right bloom-monolingual"" data-page="""" id=""7403192b-f306-4653-b7b1-0acf7163f4b9"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""3"" lang="""">
	        <div class=""marginBox"">
				<!-- This page has no input but also no TGs. We should not complain about it -->
	        </div>
	    </div>
		<div class=""bloom-page customPage bloom-combinedPage A5Portrait side-right bloom-monolingual"" data-page="""" id=""7403192b-f306-4653-b7b1-0acf7163f4b9"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" lang="""">
	        <div class=""marginBox"">
				<div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
					<div class=""bloom-editable normal-style bloom-postAudioSplit audio-sentence bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""English"" data-audiorecordingendtimes=""2.920 4.160"" lang=""en"" contenteditable=""true"">
                        <p>There's no input for this whole page, but we should not complain because it's not a numbered page.</p>
                    </div>
                    <div class=""bloom-editable normal-style"" style="""" lang=""z"" contenteditable=""true"">
                        <p></p>
                    </div>
                </div>
	        </div>
	    </div>
		<div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-right bloom-monolingual"" data-page="""" id=""7403192b-f306-4653-b7b1-0acf7163f4b9"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""4"" lang="""">
	        <div class=""marginBox"">
				<div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
					<div class=""bloom-editable normal-style bloom-postAudioSplit audio-sentence bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""English"" data-audiorecordingendtimes=""2.920 4.160"" lang=""en"" contenteditable=""true"">
                        <p>There's no input for this whole page</p>
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
		private InternalSpreadsheet _sheet;
		private HtmlDom _dom;
		private List<string> _messages;

		[OneTimeSetUp]
		public void OneTimeSetUp()
		{
			// Cases we want to test:
			// - there are more blocks on the last page after we run out of input lines
			// - there are pages after the last input line
			//		- V1: warn, leave unchanged.
			//		- eventual: ?
			// In this test suite, lines run out before the blocks.
			_dom = new HtmlDom(inputBook, true);
			_sheet = new InternalSpreadsheet();
			_sheet.ColumnForLang("en");
			_sheet.ColumnForLang("fr");
			SpreadsheetImportSyncWarnings.MakeRow("1", "New message about tigers", "New message about French tigers", _sheet);
			SpreadsheetImportSyncWarnings.MakeRow("2", "More about tigers", "More about French tigers", _sheet);
			// problem: there's no input corresponding to the second block on page 2, nor any of page 4.
			var importer = new SpreadsheetImporter(this._dom, _sheet);
			_messages = importer.Import();
		}

		[Test]
		public void GotMissingInputWarnings()
		{
			Assert.That(_messages, Contains.Item("No input row found for block 2 of page 2"));
			Assert.That(_messages, Contains.Item("No input found for pages from 4 onwards."));
		}

	}

	/// <summary>
	/// Tests the SetContentAsText method
	/// </summary>
	public class SetContentAsTextTests
	{
		private XmlElement _editable;
		XmlDocument _dom;
		[SetUp]
		public void Setup()
		{
			_dom = new XmlDocument();
			_editable = _dom.CreateElement("div");
			_dom.AppendChild(_editable);
		}
		[Test]
		public void SimpleStringMakesPara()
		{
			SpreadsheetImporter.SetContentAsText(_editable, "This is some text");
			AssertThatXmlIn.Dom(_dom).HasSpecifiedNumberOfMatchesForXpath("//p[text()='This is some text']", 1);
		}
		[Test]
		public void StringWithNewlinesMakesParas()
		{
			SpreadsheetImporter.SetContentAsText(_editable, "This is some text\r\n\r\nSome more\r\nand more");
			AssertThatXmlIn.Dom(_dom).HasSpecifiedNumberOfMatchesForXpath("//p", 4);
			AssertThatXmlIn.Dom(_dom).HasSpecifiedNumberOfMatchesForXpath("//p[text()='This is some text']", 1);
			AssertThatXmlIn.Dom(_dom).HasSpecifiedNumberOfMatchesForXpath("//p[text()='']", 1);
			AssertThatXmlIn.Dom(_dom).HasSpecifiedNumberOfMatchesForXpath("//p[text()='Some more']", 1);
			AssertThatXmlIn.Dom(_dom).HasSpecifiedNumberOfMatchesForXpath("//p[text()='and more']", 1);

		}
	}
}
