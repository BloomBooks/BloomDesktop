using System.Diagnostics;
using System.IO;
using Bloom;
using Bloom.Book;
using Bloom.CLI;
using BloomTemp;
using NUnit.Framework;


namespace BloomTests.CLI
{
	[TestFixture]
	public class HydrateBookCommandTests
	{
		private string _originalHtmlPath;
		private string _eventualHtmlPath;
		private TemporaryFolder _testFolder;
		private TemporaryFolder _bookFolder;

		[SetUp]
		public void Setup()
		{
			_testFolder = new TemporaryFolder("hydration test");
			_bookFolder = new TemporaryFolder(_testFolder,"original name");
			_originalHtmlPath = _bookFolder.Combine("original name.html");
			// We need the reference to basic book.css because that's where the list of valid page layouts lives,
			// and Bloom will force the book to A5Portrait if it can't verify that A5Landscape, Device16x9Landscape etc. are valid.
			File.WriteAllText(_originalHtmlPath,
				@"<html><head><link rel='stylesheet' href='Basic Book.css' type='text / css'></link></head><body>
					<div id='bloomDataDiv'>
						<div data-book='bookTitle' lang='en'>
								mudmen
						</div>
						<div data-book='topic' lang='en'>
							Story Book
						</div>

						<div data-book='copyright' lang='*'>
							Copyright © 2016, Joe Author
						</div>

						<div data-book='licenseUrl' lang='*'>
							http://creativecommons.org/licenses/by/4.0/
						</div>

						<div data-book='originalAcknowledgments' lang='en'>
							Some Acknowledgments
						</div>

						<div data-xmatter-page='frontCover' " + HtmlDom.musicAttrName + @"='audio/SoundTrack1.mp3'" + HtmlDom.musicVolumeName + @"='0.17'></div>
					</div>
					<div id ='firstPage' class='bloom-page A5Landscape'>1st page</div>
				</body></html>");

			//NOTE: At the moment, if the bookTitle of the selected vernacular language does not match
			//the name of the file and folder, the hydration process will rename the book's folder and file,
			//just like opening it in Bloom does. At the moment, we set the name of the folder/file to be
			//the same as the title in the requested vernacular, so it isn't an issue. But further tests
			//could make it an issue. For now, these are the same:
			//_eventualHtmlPath = _testFolder.Combine("mudmen", "mudmen.htm");

			//decided that allowing a new name is just going to confuse the programs using this CLI, so
			//let's expect the program to NOT change the names for now.
			_eventualHtmlPath = _testFolder.Combine("original name", "original name.html");
		}

		[TearDown]
		public void TearDown()
		{
			_testFolder.Dispose();
		}

		[Test]
		public void BogusPath_Returns1()
		{
			var code = HydrateBookCommand.Handle(new HydrateParameters()
			{
				Path = "notAnywhere"
			});
			Assert.AreEqual(1, code);
		}

		[Test]
		public void A5LandscapeChangedToDevice16x9Portrait()
		{
			var code = HydrateBookCommand.Handle(new HydrateParameters()
			{
				Path = _bookFolder.FolderPath,
				Preset = "shellbook",
				VernacularIsoCode = "en"
			});
			Assert.AreEqual(0, code, "Should return an exit code of 0, meaning it is happy.");
			var html = File.ReadAllText(_eventualHtmlPath);
			var xhtml = XmlHtmlConverter.GetXmlDomFromHtml(html);
			AssertThatXmlIn.Dom(xhtml).HasAtLeastOneMatchForXpath("//div[contains(@class,'bloom-page') and contains(@class,'Device16x9Portrait')]");
			Assert.That(!html.Contains("A5Landscape"));
		}

		[Test]
		public void XMatterIsFilledIn()
		{
			var code = HydrateBookCommand.Handle(new HydrateParameters()
			{
				Path = _bookFolder.FolderPath,
				Preset = "shellbook",
				VernacularIsoCode = "en"
			});
			Assert.AreEqual(0, code, "Should return an exit code of 0, meaning it is happy.");
			Debug.Write(File.ReadAllText(_eventualHtmlPath));
			var dom = XmlHtmlConverter.GetXmlDomFromHtml(File.ReadAllText(_eventualHtmlPath));

			AssertThatXmlIn.Dom(dom)
				.HasAtLeastOneMatchForXpath("//div[contains(@class,'bookTitle')]/div[contains(@class, 'bloom-editable') and contains(text(), 'mudmen')]");

			AssertThatXmlIn.Dom(dom)
				.HasSpecifiedNumberOfMatchesForXpath("//div[@data-derived='copyright' and contains(text(),'Joe Author')]", 1);

			AssertThatXmlIn.Dom(dom)
				.HasSpecifiedNumberOfMatchesForXpath("//div[@data-book='originalAcknowledgments' and @lang='en' and contains(@class,'bloom-editable') and contains(text(),'Some Acknowledgments')]", 1);

			AssertThatXmlIn.Dom(dom)
				.HasSpecifiedNumberOfMatchesForXpath("//div[@data-xmatter-page='frontCover' and @" + HtmlDom.musicAttrName
					+ "='audio/SoundTrack1.mp3' and @" + HtmlDom.musicVolumeName + "='0.17']", 2);

			AssertThatXmlIn.Dom(dom)
				.HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-xmatter-page='frontCover' and @" + HtmlDom.musicAttrName
					+ "='audio/SoundTrack1.mp3' and @" + HtmlDom.musicVolumeName + "='0.17']", 1);

			AssertThatXmlIn.Dom(dom)
				.HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'frontCover') and contains(@class,'bloom-page') and @data-xmatter-page='frontCover' and @" + HtmlDom.musicAttrName
					+ "='audio/SoundTrack1.mp3' and @" + HtmlDom.musicVolumeName + "='0.17']", 1);
		}

		[Test]
		public void StylesheetAreRelativePaths()
		{
			var code = HydrateBookCommand.Handle(new HydrateParameters()
			{
				Path = _bookFolder.FolderPath,
				Preset = "shellbook",
				VernacularIsoCode = "en"
			});
			Assert.AreEqual(0, code, "Should return an exit code of 0, meaning it is happy.");
			Debug.Write(File.ReadAllText(_eventualHtmlPath));
			var dom = XmlHtmlConverter.GetXmlDomFromHtml(File.ReadAllText(_eventualHtmlPath));

			AssertThatXmlIn.Dom(dom)
				.HasSpecifiedNumberOfMatchesForXpath("//link[@href='basePage.css']",1);

			AssertThatXmlIn.Dom(dom)
				.HasSpecifiedNumberOfMatchesForXpath("//link[@href='langVisibility.css']", 1);
			AssertThatXmlIn.Dom(dom)
				.HasSpecifiedNumberOfMatchesForXpath("//link[@href='Device-XMatter.css']", 1);
		}

		[Test]
		public void CreativeCommonsLicenseImageAdded()
		{
			var code = HydrateBookCommand.Handle(new HydrateParameters
			{
				Path = _bookFolder.FolderPath,
				Preset = "shellbook",
				VernacularIsoCode = "en"
			});
			Assert.AreEqual(0, code, "Should return an exit code of 0, meaning it is happy.");
			Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(_eventualHtmlPath), "license.png")));
		}

		[Test]
		public void SetsCorrectClassesForVernacularLanguage()
		{
			var code = HydrateBookCommand.Handle(new HydrateParameters
			{
				Path = _bookFolder.FolderPath,
				Preset = "shellbook",
				VernacularIsoCode = "en"
			});
			Assert.AreEqual(0, code, "Should return an exit code of 0, meaning it is happy.");
			AssertThatXmlIn.HtmlFile(_eventualHtmlPath)
				.HasSpecifiedNumberOfMatchesForXpath("//div[@data-book='contentLanguage1' and @lang='*' and text()='en']", 1);
			AssertThatXmlIn.HtmlFile(_eventualHtmlPath)
				.HasAtLeastOneMatchForXpath("//div[@data-book='bookTitle' and @contenteditable='true' and @lang='en']");
		}

		[Test]
		public void SetsCorrectClassesForNationalLanguages()
		{
			var code = HydrateBookCommand.Handle(new HydrateParameters
			{
				Path = _bookFolder.FolderPath,
				Preset = "shellbook",
				VernacularIsoCode = "en",
				NationalLanguage1IsoCode = "fr",
				NationalLanguage2IsoCode = "sp"
			});
			Assert.AreEqual(0, code, "Should return an exit code of 0, meaning it is happy.");
			AssertThatXmlIn.HtmlFile(_eventualHtmlPath)
				.HasAtLeastOneMatchForXpath("//div[@data-book='bookTitle' and @contenteditable='true' and @lang='fr' and contains(@class,'bloom-contentNational1')]");
			AssertThatXmlIn.HtmlFile(_eventualHtmlPath)
				.HasAtLeastOneMatchForXpath("//div[@data-book='bookTitle' and @contenteditable='true' and @lang='sp' and contains(@class,'bloom-contentNational2')]");
		}
	}
}
