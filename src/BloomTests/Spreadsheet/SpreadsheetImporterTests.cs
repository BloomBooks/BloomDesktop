using Bloom.Book;
using Bloom.Spreadsheet;
using BloomTemp;
using NUnit.Framework;
using SIL.IO;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.IO;
using BloomTests.Spreadsheet;

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
		private TemporaryFolder _tempFolder;
		public const string TestImagesDir = "src/BloomTests/ImageProcessing/images";
		private string _pathToTestImages;

		[OneTimeSetUp]
		public void OneTimeSetUp()
		{
			_pathToTestImages = SIL.IO.FileLocationUtilities.GetDirectoryDistributedWithApplication(TestImagesDir);
			_dom = new HtmlDom(SpreadsheetTests.kSimpleTwoPageBook, true);
			AssertThatXmlIn.Dom(_dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en']/p[text()='Riding on elephants can be risky.']", 1); // unchanged
			_tempFolder = new TemporaryFolder("SpreadsheetImportTestImageFolder");
			string bookFolderPath = _tempFolder.FolderPath;

			RobustFile.Copy(Path.Combine(_pathToTestImages, "man.png"), Path.Combine(_tempFolder.FolderPath, "man.png"));

			// The tests in this class all check the results of importing what export produced,
			// but with some changes. 
			var exporter = new SpreadsheetExporter();
			_sheet = exporter.Export(_dom, bookFolderPath); 
			// Changing this header will cause all the data that was originally tagged as German to be imported as Tok Pisin.
			var indexDe = _sheet.ColumnForLang("de");
			_sheet.Header.SetCell(indexDe, "[tpi]");
			// Here we locate the cells produced from two particular
			// bloom-editable elements in the kSimpleTwoPageBook DOM and replace them with different text.
			// The import should update those bloom-editables to these changed values.
			var engColumn = _sheet.ColumnForLang("en");
			var firstRowToModify = _sheet.ContentRows.FirstOrDefault(row => row.GetCell(engColumn).Content.Contains("This elephant is running amok."));
			Assert.IsNotNull(firstRowToModify, "Did not find the first text row that OneTimeSetup was expecting to modify");
			firstRowToModify.SetCell(engColumn, "<p>This elephant is running amok.</p>");
			var frColumn = _sheet.ColumnForLang("fr");
			var secondRowToModify = _sheet.ContentRows.FirstOrDefault(row => row.GetCell(frColumn).Content.Contains("Riding on French elephants can be more risky."));
			Assert.IsNotNull(secondRowToModify, "Did not find the second text row that OneTimeSetup was expecting to modify");
			secondRowToModify.SetCell(frColumn, "<p>Riding on French elephants can be very risky.</p>");

			var asteriskColumn = _sheet.ColumnForLang("*");
			var imageSrcColumn = _sheet.ColumnForTag(InternalSpreadsheet.ImageSourceLabel);

			var firstImageRowToModify = _sheet.ContentRows.FirstOrDefault(row => row.GetCell(imageSrcColumn).Content.Contains("aor_CMB424.png"));
			Assert.IsNotNull(firstImageRowToModify, "Did not find the first image row that OneTimeSetup was expecting to modify");
			firstImageRowToModify.SetCell(imageSrcColumn, Path.Combine(_pathToTestImages, "LakePendOreille.jpg"));

			var firstXmatterRowToModify = _sheet.ContentRows.FirstOrDefault(row => row.MetadataKey.Contains("styleNumberSequence"));
			Assert.IsNotNull(firstXmatterRowToModify, "Did not find the first xmatter row that OneTimeSetup was expecting to modify");
			firstXmatterRowToModify.SetCell(asteriskColumn, "7");

			var secondXmatterRowToModify = _sheet.ContentRows.FirstOrDefault(row => row.MetadataKey.Contains("coverImage"));
			Assert.IsNotNull(secondXmatterRowToModify, "Did not find the second xmatter row that OneTimeSetup was expecting to modify");
			secondXmatterRowToModify.SetCell(imageSrcColumn, Path.Combine(_pathToTestImages, "bird.png"));

			var thirdXmatterRowToModify = _sheet.ContentRows.FirstOrDefault(row => row.MetadataKey.Contains("bookTitle"));
			Assert.IsNotNull(thirdXmatterRowToModify, "Did not find the third xmatter row that OneTimeSetup was expecting to modify");
			thirdXmatterRowToModify.SetCell(_sheet.ColumnForLang("fr"), "");
			thirdXmatterRowToModify.SetCell(_sheet.ColumnForLang("tpi"), "");
			thirdXmatterRowToModify.SetCell(_sheet.ColumnForLang("en"), "<p>This is Not the End of the English World</p>");

			var fourthXmatterRowToModify = _sheet.ContentRows.FirstOrDefault(row => row.MetadataKey.Contains("contentLanguage1"));
			fourthXmatterRowToModify.SetCell(_sheet.ColumnForTag(InternalSpreadsheet.MetadataKeyLabel), "[newDataBookLabel]");
			fourthXmatterRowToModify.SetCell(asteriskColumn, "newContent");

			var fifthXmatterRowToModify = _sheet.ContentRows.FirstOrDefault(row => row.MetadataKey.Contains("licenseImage"));
			Assert.IsNotNull(fifthXmatterRowToModify, "Did not find the fifth xmatter row that OneTimeSetup was expecting to modify");
			fifthXmatterRowToModify.SetCell(imageSrcColumn, Path.Combine(_pathToTestImages, "man.png"));

			var importer = new SpreadsheetImporter(this._dom, _sheet, bookFolderPath);
			InitializeImporter(importer);
			importer.Import();
		}

		[OneTimeTearDown]
		public void OneTimeTearDown()
		{
			_tempFolder.Dispose();
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
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-editable') and @lang='de' and not(@data-book)]", 2);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='de']/p[text()='German elephants are quite orderly.']", 1);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@lang='de']/p[text()='Riding on German elephants can be less risky.']", 1);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='bookTitle' and @lang='de']", 1);
		}

		[Test]
		public void EnglishAndFrenchKeptOrModified()
		{
			var assertDom = AssertThatXmlIn.Dom(_dom.RawDom);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-editable') and @lang='en' and not(@data-book='bookTitle')]", 5);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-editable') and @lang='fr' and not(@data-book='bookTitle')]", 3);
			// Make sure these are in the right places. We put this in the first row, which because of tab index should be imported to the SECOND TG.
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@tabindex='1']/div[@lang='en']/p[text()='This elephant is running amok.']", 1); // modified
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@id='group3']/div[@lang='fr']/p[text()='Riding on French elephants can be very risky.']", 1); // modified
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@id='group3']/div[@lang='en']/p[text()='Riding on elephants can be risky.']", 1); // unchanged
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@tabindex='2']/div[@lang='fr']/p[text()='French elephants should be handled with special care.']", 1); // unchanged
		}

		[Test]
		public void XmatterChanged()
		{
			var assertDom = AssertThatXmlIn.Dom(_dom.RawDom);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='styleNumberSequence' and @lang='*' and text()='7']", 1);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='styleNumberSequence' and text()='1']", 0);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='coverImage' and @src='bird.png' and text()='bird.png']", 1);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='bookTitle' and @lang='fr']", 0);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='bookTitle' and @lang='tpi']", 0);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='bookTitle' and @lang='en']/p[text()='This is Not the End of the English World']", 1);

			//We changed contentLanguage1 to newDataBookLabel. The importer should keep its old contentLanguage1 element, since there is no input for it,
			//as well as adding a new element for newDataBookLabel
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='contentLanguage1' and @lang='*']", 1);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='newDataBookLabel' and @lang='*' and text()='newContent']", 1);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='licenseImage' and @lang='*' and not(@src) and text()='man.png']", 1);

			//make sure z language node is not removed
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@data-book='ztest' and @lang='z']", 1);
		}

		[Test]
		public void XmatterAttributesModified()
		{
			var assertDom = AssertThatXmlIn.Dom(_dom.RawDom);
			//image-specific attributes are removed
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='coverImage' and alt='oldAlt']", 0);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='coverImage' and data-license='oldLicense']", 0);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='coverImage' and  data-copyright='oldCopyright']", 0);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='coverImage' and  data-license='cc-by-sa']", 0);

			//other attributes are kept
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='styleNumberSequence' and id='idForTestingAttributeHandling']", 1);

		}

		[Test]
		public void ImageContainerUpdated()
		{
			var assertDom = AssertThatXmlIn.Dom(_dom.RawDom);
			//make sure all attributes get kept
			//except the old data-copyright, data-creator, and data-license get removed if image is changed
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-imageContainer') and @title='theOldTitle']", 1);
			assertDom.HasNoMatchForXpath("//div[contains(@class, 'bloom-imageContainer')]/img[@data-copyright='Copyright SIL International 2009']");
			assertDom.HasNoMatchForXpath("//div[contains(@class, 'bloom-imageContainer')]/img[@data-creator='oldDataCreator']");
			assertDom.HasNoMatchForXpath("//div[contains(@class, 'bloom-imageContainer')]/img[@data-license='cc-by-sa']");
			assertDom.HasNoMatchForXpath("//div[contains(@class, 'bloom-imageContainer')]/img[@alt='oldAlt']");

			assertDom.HasNoMatchForXpath("//div[contains(@class, 'bloom-imageContainer')]/img[@src='aor_CMB424.png']");
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-imageContainer')]/img[@src='LakePendOreille.jpg']", 1);
		}

		//TODO test case sensitivities in file names
		[Test]
		public void ImageFilesGotCopiedIn()
		{
			Assert.That(RobustFile.Exists(Path.Combine(_tempFolder.FolderPath, "LakePendOreille.jpg")));
			Assert.That(RobustFile.Exists(Path.Combine(_tempFolder.FolderPath, "man.png")));
			Assert.That(RobustFile.Exists(Path.Combine(_tempFolder.FolderPath, "bird.png")));

			//branding svg element was not changed, so the file should not have gotten copied in
			Assert.That(RobustFile.Exists(Path.Combine(_tempFolder.FolderPath, "BloomWithTaglineAgainstLight.svg")), Is.False);
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
			importer.Params = new SpreadsheetImportParams() { RemoveOtherLanguages = true };
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
			assertDom.HasNoMatchForXpath("//div[@data-book and @lang='de']");
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
		<div id=""bloomDataDiv"">
			<div data-book=""topic"" lang=""*"">
				FakeTopic
			</div>
	</div>
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

			MakeXmatterRows(_sheet);

			MakeRow("1", "New message about tigers", "New message about French tigers", _sheet);

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
			var importer = new SpreadsheetImporter(this._dom, _sheet, "fakeBookFolderPath");
			_messages = importer.Import();
		}

		public static void MakeRow(string pageNum, string langData1, string langData2, InternalSpreadsheet spreadsheet, string indexOnPage = "1")
		{
			var newRow = new ContentRow(spreadsheet);
			newRow.SetCell(InternalSpreadsheet.MetadataKeyLabel, InternalSpreadsheet.TextGroupLabel);
			newRow.SetCell(InternalSpreadsheet.PageNumberLabel, pageNum);
			newRow.SetCell(InternalSpreadsheet.TextIndexOnPageLabel, indexOnPage);// group index placeholder, not exactly right but near enough for this test
			newRow.SetCell("[en]", "<p>" + langData1 + "</p>");
			newRow.SetCell("[fr]", "<p>" + langData2 + "</p>");
		}

		public static void MakeXmatterRows(InternalSpreadsheet spreadsheet)
		{
			var topicRow = new ContentRow(spreadsheet);
			topicRow.SetCell(InternalSpreadsheet.MetadataKeyLabel, "[topic]");
			topicRow.SetCell("[en]", "Agriculture");
			topicRow.SetCell("[*]", "Agricultura");

			var coverImageRow = new ContentRow(spreadsheet);
			coverImageRow.SetCell(InternalSpreadsheet.MetadataKeyLabel, "[coverImage]");
			//No content, to check that we get a warning
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
		public void GotAsteriskAndOtherLanguageXmatterWarning()
		{
			Assert.That(_messages, Contains.Item("topic information found in both * language column and other language column(s)"));
		}


		[Test]
		public void GotNoCoverImageWarning()
		{
			Assert.That(_messages, Contains.Item("No cover image found"));
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
		<div id=""bloomDataDiv"">
		</div>
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
			var importer = new SpreadsheetImporter(this._dom, _sheet, "fakeBookFolderPath");
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
	/// Test that importing of both text and images work properly and do not interfere with each other.
	/// This class tests having image rows come before text rows for each page, child classes will test other orderings
	/// </summary>
	public class SpreadsheetImportTextAndImages
	{
		//Input has four pages. First has 2 translation groups and 2 images, second has 2 images,
		//third is blank, fourth has 1 translation group
		const string textImagesBook = @"
	<html>
		<head>
		</head>
		<body data-l1=""en"" data-l2=""en"" data-l3="""">
			<div id=""bloomDataDiv"">
			</div>
			<div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-right bloom-bilingual"" data-page="""" id=""79a50bfd-6fb9-4aa6-87d1-6c2f114d1df3"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398383"" data-page-number=""1"" lang="""">
				<div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Picture in Middle"" lang=""en"">
					Picture in Middle
				</div>

				<div class=""pageDescription"" lang=""en""></div>

				<div class=""marginBox"">
					<div class=""split-pane horizontal-percent"" style=""min-height: 42px;"">
						<div class=""split-pane-component position-top"" style=""bottom: 76%"">
							<div class=""split-pane-component-inner"" min-width=""60px 150px 250px"" min-height=""60px 150px"">
								<div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
									<div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" id=""es1"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""español"" lang=""es"" contenteditable=""true"">
										<p>The Spanish monkey went biking.</p>
									</div>

									<div class=""bloom-editable normal-style"" style="""" lang=""z"" contenteditable=""true"">
										<p></p>
									</div>

									<div class=""bloom-editable normal-style bloom-content2 bloom-contentNational1 bloom-visibility-code-on"" id=""en1"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""English"" lang=""en"" contenteditable=""true"">
										<p>The English monkey went swimming.</p>
									</div>
								</div>
							</div>
						</div>

						<div class=""split-pane-divider horizontal-divider"" style=""bottom: 76%""></div>

						<div class=""split-pane-component position-bottom"" style=""height: 76%"">
							<div class=""split-pane horizontal-percent"" style=""min-height: 42px;"">
								<div class=""split-pane-component position-top"">
									<div class=""split-pane-component-inner"" min-width=""60px 150px 250px"" min-height=""60px 150px"">
										<div class=""bloom-imageContainer bloom-leadingElement"" title=""Name: 1.jpg Size: 2.07 mb Dots: 3500 x 2100 For the current paper size: • The image container is 406 x 254 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ✓ This image would print at 828 DPI. • An image with 1269 x 794 dots would fill this container at 300 DPI.""><img src = ""1.jpg"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img></div>
									</div>
								</div>

								<div class=""split-pane-divider horizontal-divider""></div>

								<div class=""split-pane-component position-bottom"">
									<div class=""split-pane horizontal-percent"" style=""min-height: 42px;"">
										<div class=""split-pane-component position-top"">
											<div class=""split-pane-component-inner"" min-width=""60px 150px 250px"" min-height=""60px"">
												<div class=""bloom-translationGroup bloom-trailingElement"">
													<div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" id=""es2"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""español"" lang=""es"" contenteditable=""true"">
														<p>The Spanish monkey had a lovely bike ride.</p>
													</div>

													<div class=""bloom-editable normal-style bloom-content2 bloom-contentNational1 bloom-visibility-code-on"" id=""en2"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""English"" lang=""en"" contenteditable=""true"">
														<p>The English monkey had a lovely swim.</p>
													</div>
												</div>
											</div>
										</div>

										<div class=""split-pane-divider horizontal-divider""></div>

										<div class=""split-pane-component position-bottom"">
											<div class=""split-pane-component-inner"" min-width=""60px 150px 250px"" min-height=""60px"">
												<div class=""bloom-imageContainer bloom-leadingElement"" title=""Name: 2.jpg Size: 2.73 mb Dots: 3500 x 2100 For the current paper size: • The image container is 406 x 127 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ✓ This image would print at 1587 DPI. • An image with 1269 x 397 dots would fill this container at 300 DPI.""><img src = ""2.jpg"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img></div>
											</div>
										</div>
									</div>
								</div>
							</div>
						</div>
					</div>
				</div>
			</div>

			<div class=""bloom-page numberedPage customPage A5Portrait bloom-bilingual side-left"" data-page="""" id=""a3654885-c360-4723-a1cd-5acf7dab77cf"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398385"" data-page-number=""2"" lang="""">
				<div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Just a Picture"" lang=""en"">
					Just a Picture
				</div>

				<div class=""pageDescription"" lang=""en""></div>

				<div class=""marginBox"">
					<div class=""split-pane horizontal-percent"" style=""min-height: 42px;"">
						<div class=""split-pane-component position-top"">
							<div class=""split-pane-component-inner"" min-width=""60px 150px 250px"" min-height=""60px 150px 250px"">
								<div class=""bloom-imageContainer"" title=""Name: brain.jpg Size: 68.62 kb Dots: 1100 x 880 For the current paper size: • The image container is 406 x 335 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ⚠ This image would print at 260 DPI. • An image with 1269 x 1047 dots would fill this container at 300 DPI.""><img src = ""brain.jpg"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img></div>
							</div>
						</div>

						<div class=""split-pane-divider horizontal-divider""></div>

						<div class=""split-pane-component position-bottom"">
							<div class=""split-pane-component-inner"" min-width=""60px 150px 250px"" min-height=""60px 150px 250px"">
								<div class=""bloom-imageContainer bloom-leadingElement"" title=""Name: man.jpg Size: 184.65 kb Dots: 2500 x 1406 For the current paper size: • The image container is 406 x 338 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ✓ This image would print at 591 DPI. • An image with 1269 x 1057 dots would fill this container at 300 DPI.""><img src = ""man.jpg"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img></div>
							</div>
						</div>
					</div>
				</div>
			</div>

			<div class=""bloom-page numberedPage customPage A5Portrait bloom-bilingual side-right"" data-page="""" id=""95a6d6be-95a9-474b-9899-4d0938419626"" data-pagelineage=""5dcd48df-e9ab-4a07-afd4-6a24d0398386"" data-page-number=""3"" lang="""">
				<div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Custom"" lang=""en"">
					Custom
				</div>

				<div class=""pageDescription"" lang=""en""></div>

				<div class=""marginBox"">
					<div class=""split-pane-component-inner""></div>
				</div>
			</div>

			<div class=""bloom-page numberedPage customPage A5Portrait side-left bloom-bilingual"" data-page="""" id=""5f3ad94c-cf93-4b23-b9e3-c177ea8da1bd"" data-pagelineage=""a31c38d8-c1cb-4eb9-951b-d2840f6a8bdb"" data-page-number=""4"" lang="""">
				<div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Just Text"" lang=""en"">
					Just Text
				</div>

				<div class=""pageDescription"" lang=""en""></div>

				<div class=""marginBox"">
					<div class=""split-pane-component-inner"">
						<div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
							<div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""español"" lang=""es"" contenteditable=""true"">
								<p>The Spanish monkey was a purple monkey.</p>
							</div>

							<div class=""bloom-editable normal-style"" style="""" lang=""z"" contenteditable=""true"">
								<p></p>
							</div>

							<div class=""bloom-editable normal-style bloom-content2 bloom-contentNational1 bloom-visibility-code-on"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""English"" lang=""en"" contenteditable=""true"">
								<p>The English monkey was an orange monkey.</p>
							</div>
						</div>
					</div>
				</div>
			</div>
		</body>
	</html>";

		private InternalSpreadsheet _sheet;
		private HtmlDom _dom;
		private List<string> _messages;
		private string _pathToTestImages;
		private TemporaryFolder _tempFolder;
		private bool _syncWarningsTesting; //For use making child classes that test extra/missing rows warning messages

		[OneTimeSetUp]
		public void OneTimeSetUp()
		{
			_pathToTestImages = SIL.IO.FileLocationUtilities.GetDirectoryDistributedWithApplication(SpreadsheetImporterChangeLanguageTests.TestImagesDir);
			_tempFolder = new TemporaryFolder("SpreadsheetImportTestImageFolder");
			_dom = new HtmlDom(textImagesBook, true);
			_sheet = new InternalSpreadsheet();
			_sheet.ColumnForLang("es");
			_sheet.ColumnForLang("en");
			MakeFirstPageRows();
			if (!_syncWarningsTesting)
			{
				MakeImageRow("2", Path.Combine(_pathToTestImages, "bird.png"), _sheet, indexOnPage: "1");
				MakeImageRow("2", Path.Combine(_pathToTestImages, "man.png"), _sheet, indexOnPage: "2");
				MakeTextRow("4", "The new Spanish monkey was very purple.", "The new English monkey was very orange.", _sheet);
			}
			var importer = new SpreadsheetImporter(this._dom, _sheet, _tempFolder.FolderPath);
			_messages = importer.Import();
		}

		[OneTimeTearDown]
		public void OneTimeTearDown()
		{
			_tempFolder.Dispose();
		}

		//Importing should be robust to the relative ordering of text with respect to image rows for each page
		//child classes will override this method to test other orderings
		protected virtual void MakeFirstPageRows() 
		{
			MakeFirstPageImageRow1();
			MakeFirstPageImageRow2();
			MakeFirstPageTextRow1();
			MakeFirstPageTextRow2();
		}

		protected void MakeFirstPageImageRow1()
		{
			MakeImageRow("1", Path.Combine(_pathToTestImages, "shirt.png"), _sheet, indexOnPage: "1");
		}
		protected void MakeFirstPageImageRow2()
		{
			MakeImageRow("1", Path.Combine(_pathToTestImages, "lady24b.png"), _sheet, indexOnPage: "1");
		}
		protected void MakeFirstPageTextRow1()
		{
			MakeTextRow("1", "The new Spanish monkey went biking.", "The new English monkey went swimming.", _sheet, indexOnPage: "1");
		}
		protected void MakeFirstPageTextRow2()
		{
			MakeTextRow("1", "The new Spanish monkey had a lovely bike ride.", "The new English monkey had a lovely swim.", _sheet, indexOnPage: "2");
		}

		public static void MakeTextRow(string pageNum, string langData1, string langData2, InternalSpreadsheet spreadsheet, string indexOnPage = "1")
		{
			var newRow = new ContentRow(spreadsheet);
			newRow.SetCell(InternalSpreadsheet.MetadataKeyLabel, InternalSpreadsheet.TextGroupLabel);
			newRow.SetCell(InternalSpreadsheet.PageNumberLabel, pageNum);
			newRow.SetCell(InternalSpreadsheet.TextIndexOnPageLabel, indexOnPage);
			newRow.SetCell("[es]", "<p>" + langData1 + "</p>");
			newRow.SetCell("[en]", "<p>" + langData2 + "</p>");
		}

		public static void MakeImageRow(string pageNum, string imageSource, InternalSpreadsheet spreadsheet, string indexOnPage = "1")
		{
			var newRow = new ContentRow(spreadsheet);
			newRow.SetCell(InternalSpreadsheet.MetadataKeyLabel, InternalSpreadsheet.ImageKeyLabel);
			newRow.SetCell(InternalSpreadsheet.PageNumberLabel, pageNum);
			newRow.SetCell(InternalSpreadsheet.TextIndexOnPageLabel, indexOnPage);
			newRow.SetCell(InternalSpreadsheet.ImageSourceLabel, imageSource);
		}

		[Test]
		public void TextAndImagesPageUpdated() 
		{
			var assertDom = AssertThatXmlIn.Dom(_dom.RawDom);
			assertDom.HasSpecifiedNumberOfMatchesForXpath(
				"//div[contains(@class, 'bloom-translationGroup')]/div[contains(@class, 'bloom-editable') and @id='es1']" +
				"/p[text()='The new Spanish monkey went biking.']", 1);
			assertDom.HasSpecifiedNumberOfMatchesForXpath(
				"//div[contains(@class, 'bloom-translationGroup')]/div[contains(@class, 'bloom-editable') and @id='en1']" +
				"/p[text()='The new English monkey went swimming.']", 1);
			assertDom.HasSpecifiedNumberOfMatchesForXpath(
				"//div[contains(@class, 'bloom-translationGroup')]/div[contains(@class, 'bloom-editable') and @id='es2']" +
				"/p[text()='The new Spanish monkey had a lovely bike ride.']", 1);
			assertDom.HasSpecifiedNumberOfMatchesForXpath(
				"//div[contains(@class, 'bloom-translationGroup')]/div[contains(@class, 'bloom-editable') and @id='en2']" +
				"/p[text()='The new English monkey had a lovely swim.']", 1);

			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-imageContainer')]/img[@src='shirt.png']", 1);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-imageContainer')]/img[@src='lady24b.png']", 1);
		}

		[Test]
		public void BlankPageLeftBlank()
		{
			var assertDom = AssertThatXmlIn.Dom(_dom.RawDom);
			assertDom.HasNoMatchForXpath("//div[contains(@class, 'bloom-page') and @data-page-number='3']" +
				"//div[contains(@class, 'bloom-translationGroup') or contains(@class, 'bloom-imageContainer')]"); //TODO should fail with page 2 or 4
		}

		[Test]
		public void ImageOnlyPageUpdated()
		{
			var assertDom = AssertThatXmlIn.Dom(_dom.RawDom);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-imageContainer')]/img[@src='bird.png']", 1);
			assertDom.HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-imageContainer')]/img[@src='man.png']", 1);
		}

		[Test]
		public void TextOnlyPageUpdated()
		{
			var assertDom = AssertThatXmlIn.Dom(_dom.RawDom);
			assertDom.HasSpecifiedNumberOfMatchesForXpath(
				"//div[contains(@class, 'bloom-translationGroup')]/div[contains(@class, 'bloom-editable') and @lang='es']" +
				"/p[text()='The new Spanish monkey was very purple.']", 1);
			assertDom.HasSpecifiedNumberOfMatchesForXpath(
				"//div[contains(@class, 'bloom-translationGroup')]/div[contains(@class, 'bloom-editable') and @lang='en']" +
				"/p[text()='The new English monkey was very orange.']", 1);
		}

	}


	//Importing should be robust to the relative ordering of text vs image rows for each page
	// though the text rows need to be in order with respect to each other, and same with the image rows.

	/// <summary>
	/// Make sure importing still works when rows with text content come before rows with images for the same page
	/// </summary>
	public class SpreadsheetImportTextAndImages_TextRowsComeFirst : SpreadsheetImportTextAndImages
	{
		protected override void MakeFirstPageRows()
		{
			MakeFirstPageTextRow1();
			MakeFirstPageTextRow2();
			MakeFirstPageImageRow1();
			MakeFirstPageImageRow2();
		}
	}

	/// <summary>
	/// Make sure importing still works when rows with text content and rows with images for the same page are interspersed
	/// </summary>
	public class SpreadsheetImportTextAndImages_TextAndImageRowsInterspersed : SpreadsheetImportTextAndImages
	{
		protected override void MakeFirstPageRows()
		{
			MakeFirstPageImageRow1();
			MakeFirstPageTextRow1();
			MakeFirstPageImageRow2();
			MakeFirstPageTextRow2();
		}
	}

	//TODO write more child classes that test that _warnings contains the right messages
	//in error cases such as:
		//An extraneous image row in spreadsheet before good text rows and vice versa
		//a missing image row before good text rows and vice versa
		//spreadsheet is missing all text and/or all images after a certain point
		//there are extra/missing rows of both types (image and text) on the same page
}



//TODO test
//That an untouched image keeps its attributes and is untouched
//Importing xmatter images
//all the error situations with just images


