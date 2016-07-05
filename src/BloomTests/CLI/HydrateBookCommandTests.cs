using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Bloom.Book;
using Bloom.CLI;
using BloomTemp;
using NUnit.Framework;


namespace BloomTests.CLI
{
	[TestFixture]
	public class HydrateBookCommandTests
	{
		private HtmlDom _dom;
		private DataSet _dataSet;
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
			File.WriteAllText(_originalHtmlPath,
				@"<html><head></head><body>
					<div data-book='bookTitle' lang='en'>
							mudmen
						</div>
					</div>
					<div id ='firstPage' class='bloom-page A5Landscape'>1st page</div>
				</body></html>");

			//NOTE: At the moment, if the bookTitle of the selected vernacular language does not match
			//the name of the file and folder, the hydration process will rename the book's folder and file, 
			//just like opening it in Bloom does. At the moment, we set the name of the folder/file to be
			//the same as the title in the requested vernacular, so it isn't an issue. But further tests
			//could make it and issue. For now, these are the same:
			_eventualHtmlPath = _testFolder.Combine("mudmen", "mudmen.htm");
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
		public void PresetIsApp_A5LandscapeChangedToDevice16x9Landscape()
		{
			var code = HydrateBookCommand.Handle(new HydrateParameters()
			{
				Path = _bookFolder.FolderPath,
				Preset = "app",
				VernacularIsoCode = "en"
			});
			Assert.AreEqual(0, code, "Should return an exit code of 0, meaning it is happy.");
			var html = File.ReadAllText(_eventualHtmlPath);
			AssertThatXmlIn.File(_eventualHtmlPath).HasAtLeastOneMatchForXpath("//div[contains(@class,'bloom-page') and contains(@class,'Device16x9Landscape')]");
			Assert.That(!html.Contains("A5Landscape"));
		}

		/// <summary>
		/// Eventually we need to have the app preset investigate whether to use the video xmatter or
		/// the (at this time, non-existant) book-app xmatter. For now, we expect it to assume we have
		/// a multimedia book.
		/// </summary>
		[Test]
		public void PresetIsApp_XmatterSetToVideo()
		{
			var code = HydrateBookCommand.Handle(new HydrateParameters()
			{
				Path = _bookFolder.FolderPath,
				Preset = "app",
				VernacularIsoCode = "en"
			});
			Assert.AreEqual(0, code, "Should return an exit code of 0, meaning it is happy.");
			var html = File.ReadAllText(_eventualHtmlPath);
			Assert.That(html.Contains("Opening Screen"));
		}

		[Test]
		public void PresetIsApp_XMatterIsFilledIn()
		{
			var code = HydrateBookCommand.Handle(new HydrateParameters()
			{
				Path = _bookFolder.FolderPath,
				Preset = "app",
				VernacularIsoCode = "en"
			});
			Assert.AreEqual(0, code, "Should return an exit code of 0, meaning it is happy.");
			Debug.Write(File.ReadAllText(_eventualHtmlPath));
			AssertThatXmlIn.HtmlFile(_eventualHtmlPath)
				.HasAtLeastOneMatchForXpath("//div[contains(@class,'bookTitle')]/div[contains(@class, 'bloom-editable') and contains(text(), 'mudmen')]");
		}


		[Test]
		public void SetsCorrectClassesForVernacularLanguage()
		{
		}

		[Test]
		public void SetsCorrectClassesForNationalLanguages()
		{
		}
	}
}
