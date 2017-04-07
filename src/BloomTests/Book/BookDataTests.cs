using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Xml;
using Bloom.Book;
using Bloom.Collection;
using L10NSharp;
using NUnit.Framework;
using SIL.Extensions;
using SIL.IO;
using SIL.Reporting;
using SIL.TestUtilities;
using SIL.Xml;

namespace BloomTests.Book
{
	[TestFixture]
	public sealed class BookDataTests
	{
		private CollectionSettings _collectionSettings;
		private LocalizationManager _localizationManager;
		private LocalizationManager _palasoLocalizationManager;

		[SetUp]
		public void Setup()
		{
			_collectionSettings = new CollectionSettings(new NewCollectionSettings()
			{
				PathToSettingsFile = CollectionSettings.GetPathForNewSettings(new TemporaryFolder("BookDataTests").Path, "test"),
				Language1Iso639Code = "xyz",
				Language2Iso639Code = "en",
				Language3Iso639Code = "fr"
			});
			ErrorReport.IsOkToInteractWithUser = false;

			var localizationDirectory = FileLocator.GetDirectoryDistributedWithApplication("localization");
			_localizationManager = LocalizationManager.Create("fr", "Bloom", "Bloom", "1.0.0", localizationDirectory, "SIL/Bloom",
				null, "");
			_palasoLocalizationManager = LocalizationManager.Create("fr", "Palaso","Palaso", "1.0.0", localizationDirectory, "SIL/Palaso",
				null, "");
		}

		[TearDown]
		public void TearDown()
		{
			_localizationManager.Dispose();
			_palasoLocalizationManager.Dispose();
		}

		[Test]
		public void TextOfInnerHtml_RemovesMarkup()
		{
			var input = "This <em>is</em> the day";
			var output = BookData.TextOfInnerHtml(input);
			Assert.That(output, Is.EqualTo("This is the day"));
		}

		[Test]
		public void TextOfInnerHtml_HandlesXmlEscapesCorrectly()
		{
			var input = "Jack &amp; Jill like xml sequences like &amp;amp; &amp; &amp;lt; &amp; &amp;gt; for characters like &lt;&amp;&gt;";
			var output = BookData.TextOfInnerHtml(input);
			Assert.That(output, Is.EqualTo("Jack & Jill like xml sequences like &amp; & &lt; & &gt; for characters like <&>"));
		}

		[Test]
		public void MakeLanguageUploadData_FindsDefaultInfo()
		{
			var results = _collectionSettings.MakeLanguageUploadData(new[] {"en", "tpi", "xy3"});
			Assert.That(results.Length, Is.EqualTo(3), "should get one result per input");
			VerifyLangData(results[0], "en", "English", "eng");
			VerifyLangData(results[1], "tpi", "Tok Pisin", "tpi");
			VerifyLangData(results[2], "xy3", "xy3", "xy3");
		}

		[Test]
		public void MakeLanguageUploadData_FindsOverriddenNames()
		{
			_collectionSettings.Language1Name = "Cockney";
			// Note: no current way of overriding others; verify they aren't changed.
			var results = _collectionSettings.MakeLanguageUploadData(new[] { "en", "tpi", "xyz" });
			Assert.That(results.Length, Is.EqualTo(3), "should get one result per input");
			VerifyLangData(results[0], "en", "English", "eng");
			VerifyLangData(results[1], "tpi", "Tok Pisin", "tpi");
			VerifyLangData(results[2], "xyz", "Cockney", "xyz");
		}

		void VerifyLangData(LanguageDescriptor lang, string code, string name, string ethCode)
		{
			Assert.That(lang.IsoCode, Is.EqualTo(code));
			Assert.That(lang.Name, Is.EqualTo(name));
			Assert.That(lang.EthnologueCode, Is.EqualTo(ethCode));
		}

	   [Test]
		public void SuckInDataFromEditedDom_NoDataDIvTitleChanged_NewTitleInCache()
	   {
		   HtmlDom bookDom = new HtmlDom(@"<html ><head></head><body>
				<div class='bloom-page' id='guid2'>
					<textarea lang='xyz' data-book='bookTitle'>original</textarea>
				</div>
			 </body></html>");
		   var data = new BookData(bookDom, _collectionSettings, null);
		   Assert.AreEqual("original", data.GetVariableOrNull("bookTitle", "xyz"));


		   HtmlDom editedPageDom = new HtmlDom(@"<html ><head></head><body>
				<div class='bloom-page' id='guid2'>
					<textarea lang='xyz' data-book='bookTitle'>changed</textarea>
				</div>
			 </body></html>");

		   data.SuckInDataFromEditedDom(editedPageDom);

		   Assert.AreEqual("changed", data.GetVariableOrNull("bookTitle", "xyz"));
	   }

		/// <summary>
		/// Regression test: the difference between this situation (had a value before) and the one where this is newly discovered was the source of a bug
		/// </summary>
	   [Test]
	   public void SuckInDataFromEditedDom_HasDataDivWithOldTitleThenTitleChanged_NewTitleInCache()
	   {
		   HtmlDom bookDom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='bookTitle' lang='xyz'>original</div>
				</div>
				<div class='bloom-page' id='guid2'>
					<textarea lang='xyz' data-book='bookTitle'>original</textarea>
				</div>
			 </body></html>");

		   var data = new BookData(bookDom, _collectionSettings, null);
		   Assert.AreEqual("original", data.GetVariableOrNull("bookTitle", "xyz"));

		   HtmlDom editedPageDom = new HtmlDom(@"<html ><head></head><body>
				<div class='bloom-page' id='guid2'>
					<textarea lang='xyz' data-book='bookTitle'>changed</textarea>
				</div>
			 </body></html>");

		   data.SuckInDataFromEditedDom(editedPageDom);

		   Assert.AreEqual("changed", data.GetVariableOrNull("bookTitle", "xyz"));
	   }

		[Test]
		public void UpdateFieldsAndVariables_CustomLibraryVariable_CopiedToOtherElement()
		{
			var dom=new HtmlDom(@"<html ><head></head><body>
				<div class='bloom-page' id='guid3'>
					<p>
						<textarea lang='xyz' id='copyOfVTitle'  data-book='bookTitle'>tree</textarea>
						<textarea lang='xyz' id='1' data-collection='testLibraryVariable'>aa</textarea>
					   <textarea lang='xyz' id='2'  data-collection='testLibraryVariable'>bb</textarea>
					</p>
				</div>
				</body></html>");
			var data = new BookData(dom, _collectionSettings, null);
			data.UpdateVariablesAndDataDivThroughDOM();
			var textarea2 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='2']");
			Assert.AreEqual("aa", textarea2.InnerText);
		}

		[Test]
		public void UpdateFieldsAndVariables_HasBookTitleTemplateWithVernacularPlaceholder_CreatesTitleForVernacular()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='bookTitleTemplate' lang='{V}'>the title</div>
				</div>
				</body></html>");
			var data = new BookData(dom, _collectionSettings, null);
			data.UpdateVariablesAndDataDivThroughDOM();
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@data-book='bookTitle' and @lang='"+_collectionSettings.Language1Iso639Code+"' and text()='the title']",1);
		}

		[Test]
		public void UpdateVariablesAndDataDivThroughDOM_DataBookAttributes_AttributesAddedToDiv()
		{
			var dom = new HtmlDom(@"<html><head></head><body>
				<div id='bloomDataDiv'>
					<div data-book-attributes='frontCover' data-backgroundaudio='audio/SoundTrack1.mp3' data-backgroundaudiovolume='0.17'></div>
				</div>
				<div id='firstPage' class='bloom-page' data-book-attributes='frontCover'>1st page</div>
				</body></html>");
			var data = new BookData(dom, _collectionSettings, null);
			data.UpdateVariablesAndDataDivThroughDOM();
			AssertThatXmlIn.Dom(dom.RawDom)
				.HasSpecifiedNumberOfMatchesForXpath("//div[@id='firstPage' and @data-book-attributes='frontCover' and @data-backgroundaudio='audio/SoundTrack1.mp3' and @data-backgroundaudiovolume='0.17']", 1);
		}

		[Test]
		public void UpdateFieldsAndVariables_VernacularTitleChanged_TitleCopiedToParagraphAnotherPage()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
				<div class='bloom-page' id='guid2'>
						<p>
							<textarea lang='xyz' data-book='bookTitle'>original</textarea>
						</p>
				</div>
				<div class='bloom-page' id='0a99fad3-0a17-4240-a04e-86c2dd1ec3bd'>
						<p class='centered' lang='xyz' data-book='bookTitle' id='P1'>originalButNoExactlyCauseItShouldn'tMatter</p>
				</div>
			 </body></html>");
			var data = new BookData(dom,  _collectionSettings, null);
			var textarea1 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@data-book='bookTitle' and @lang='xyz']");
			textarea1.InnerText = "peace & quiet";
			data.SynchronizeDataItemsThroughoutDOM();
			var paragraph = dom.SelectSingleNodeHonoringDefaultNS("//p[@data-book='bookTitle'  and @lang='xyz']");
			Assert.AreEqual("peace & quiet", paragraph.InnerText);
		}


		[Test]
		public void UpdateFieldsAndVariables_OneDataItemChanges_ItemsWithThatLanguageAlsoUpdated()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
				<div class='bloom-page' id='guid1'>
					<p>
						<textarea lang='en' id='1'  data-book='bookTitle'>EnglishTitle</textarea>
						<textarea lang='xyz' id='2'  data-book='bookTitle'>xyzTitle</textarea>
					</p>
				</div>
				<div class='bloom-page' id='guid3'>
					<p>
						<textarea lang='xyz' id='3'  data-book='bookTitle'>xyzTitle</textarea>
					</p>
				</div>
			 </body></html>");
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='en' and @id='1' and text()='EnglishTitle']", 1);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='xyz'  and @id='2' and text()='xyzTitle']", 1);
			var textarea2 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='2']");
			textarea2.InnerText = "newXyzTitle";
			var data = new BookData(dom, new CollectionSettings() { Language1Iso639Code = "etr" }, null);
			data.SynchronizeDataItemsThroughoutDOM();
			var textarea3 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='3']");
			Assert.AreEqual("newXyzTitle", textarea3.InnerText);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@id='1' and text()='EnglishTitle']", 1);
		}

		[Test]
		public void UpdateFieldsAndVariables_EnglishDataItemChanges_VernItemsUntouched()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
				<div class='bloom-page' id='guid1'>
					<p>
						<textarea lang='en' id='1'  data-book='bookTitle'>EnglishTitle</textarea>
						<textarea lang='xyz' id='2'  data-book='bookTitle'>xyzTitle</textarea>
					</p>
				</div>
				<div class='bloom-page' id='guid3'>
					<p>
						<textarea lang='xyz' id='3'  data-book='bookTitle'>xyzTitle</textarea>
					</p>
				</div>
			 </body></html>");
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='en' and @id='1' and text()='EnglishTitle']", 1);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='xyz'  and @id='2' and text()='xyzTitle']", 1);
			var textarea1 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='1']");
			textarea1.InnerText = "newEnglishTitle";
			var data = new BookData(dom,   new CollectionSettings(){Language1Iso639Code = "etr"}, null);
			data.SynchronizeDataItemsThroughoutDOM();
			var textarea2 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='2']");
			Assert.AreEqual("xyzTitle", textarea2.InnerText);
			var textarea3 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='3']");
			Assert.AreEqual("xyzTitle", textarea3.InnerText);
		}


		[Test]
		public void UpdateFieldsAndVariables_BookTitleInSpanOnSecondPage_UpdatesH2OnFirstWithCurrentNationalLang()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
				<div class='bloom-page titlePage'>
						<div class='pageContent'>
							<h2 data-book='bookTitle' lang='N1'>{national book title}</h2>
						</div>
					</div>
				<div class='bloom-page verso'>
					<div class='pageContent'>
						(<span lang='en' data-book='bookTitle'>Vaccinations</span><span lang='tpi' data-book='bookTitle'>Tambu Sut</span>)
						<br />
					</div>
				</div>
				</body></html>");
			var collectionSettings = new CollectionSettings()
				{
					Language1Iso639Code = "etr"
				};
			var data = new BookData(dom,   collectionSettings, null);
			data.SynchronizeDataItemsThroughoutDOM();
			XmlElement nationalTitle = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//h2[@data-book='bookTitle']");
			Assert.AreEqual("Vaccinations", nationalTitle.InnerText);

			//now switch the national language to Tok Pisin

			collectionSettings.Language2Iso639Code = "tpi";
			data.SynchronizeDataItemsThroughoutDOM();
			nationalTitle = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//h2[@data-book='bookTitle']");
			Assert.AreEqual("Tambu Sut", nationalTitle.InnerText);
		}

        [Test]
        public void UpdateFieldsAndVariables_OneLabelPreserved_DuplicatesRemovedNotAdded()
        {
            var dom = new HtmlDom(@"<html ><head></head><body>
				<div class='bloom-page titlePage'>
						<div id='target' class='bloom-content1' data-book='insideBackCover'>
							<label class='bubble'>Some more space to put things</label><label class='bubble'>Some more space to put things</label>Here is the content
						</div>
				</div>
				</body></html>");
            var collectionSettings = new CollectionSettings()
            {
                Language1Iso639Code = "etr"
            };
            var data = new BookData(dom, collectionSettings, null);
            data.SynchronizeDataItemsThroughoutDOM();
            XmlElement target = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//div[@id='target']");

            // It's expected that the surviving label goes at the end.
            Assert.That(target.InnerText, Is.EqualTo("Here is the contentSome more space to put things"));
            XmlElement label = (XmlElement)target.SelectSingleNodeHonoringDefaultNS("//label");
            Assert.That(label.InnerText, Is.EqualTo("Some more space to put things"));
        }

        [Test]
		public void GetMultilingualContentLanguage_ContentLanguageSpecifiedInHtml_ReadsIt()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='contentLanguage2'>fr</div>
						<div data-book='contentLanguage3'>es</div>
				</div>
				</body></html>");
			var collectionSettings = new CollectionSettings();
			var data = new BookData(dom,   collectionSettings, null);
			Assert.AreEqual("fr", data.MultilingualContentLanguage2);
			Assert.AreEqual("es", data.MultilingualContentLanguage3);
		}
		[Test]
		public void SetMultilingualContentLanguage_ContentLanguageSpecifiedInHtml_ReadsIt()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='contentLanguage2'>fr</div>
				</div>
				</body></html>");
			var collectionSettings = new CollectionSettings();
			var data = new BookData(dom,   collectionSettings, null);
			data.SetMultilingualContentLanguages("en", "de");
			Assert.AreEqual("en", data.MultilingualContentLanguage2);
			Assert.AreEqual("de", data.MultilingualContentLanguage3);
		}
		[Test]
		public void UpdateVariablesAndDataDivThroughDOM_NewLangAdded_AddedToDataDiv()
		{
			var dom = new HtmlDom(@"<html><head></head><body><div data-book='someVariable' lang='en'>hi</div></body></html>");

			var e = dom.RawDom.CreateElement("div");
			e.SetAttribute("data-book", "someVariable");
			e.SetAttribute("lang", "fr");
			e.InnerText = "bonjour";
			dom.RawDom.SelectSingleNode("//body").AppendChild(e);
			var data = new BookData(dom,   new CollectionSettings(), null);
			data.UpdateVariablesAndDataDivThroughDOM();
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//body/div[1][@id='bloomDataDiv']", 1);//NB microsoft uses 1 as the first. W3c uses 0.
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='someVariable' and @lang='en' and text()='hi']", 1);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='someVariable' and @lang='fr' and text()='bonjour']", 1);
		}

		[Test]
		public void UpdateVariablesAndDataDivThroughDOM_HasDataLibraryValues_LibraryValuesNotPutInDataDiv()
		{
			var dom = new HtmlDom(@"<html><head></head><body><div data-book='someVariable' lang='en'>hi</div><div data-collection='user' lang='en'>john</div></body></html>");
			var data = new BookData(dom,   new CollectionSettings(), null);
			data.UpdateVariablesAndDataDivThroughDOM();
			AssertThatXmlIn.Dom(dom.RawDom).HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-book='user']");
			AssertThatXmlIn.Dom(dom.RawDom).HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-collection]");
		}

		[Test]
		public void UpdateVariablesAndDataDivThroughDOM_DoesNotExist_MakesOne()
		{
			var dom = new HtmlDom(@"<html><head></head><body><div data-book='someVariable'>world</div></body></html>");
			var data = new BookData(dom,   new CollectionSettings(), null);
			data.UpdateVariablesAndDataDivThroughDOM();
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//body/div[1][@id='bloomDataDiv']", 1);//NB microsoft uses 1 as the first. W3c uses 0.
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='someVariable' and text()='world']", 1);
		}


		[Test]
		public void SetMultilingualContentLanguages_HasTrilingualLanguages_AddsToDataDiv()
		{
			var dom = new HtmlDom(@"<html><head></head><body></body></html>");
			var data = new BookData(dom,  new CollectionSettings(), null);
			data.SetMultilingualContentLanguages("okm", "kbt");
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='contentLanguage2' and text()='okm']", 1);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='contentLanguage3' and text()='kbt']", 1);
		}
		[Test]
		public void SetMultilingualContentLanguages_ThirdContentLangTurnedOff_RemovedFromDataDiv()
		{
			var dom = new HtmlDom(@"<html><head><div id='bloomDataDiv'><div data-book='contentLanguage2'>xyz</div><div data-book='contentLanguage3'>kbt</div></div></head><body></body></html>");
			var data = new BookData(dom,  new CollectionSettings(), null);
			data.SetMultilingualContentLanguages(null,null);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='contentLanguage3']", 0);
		}


		[TestCase("", "", "", null)]
		[TestCase("the country", "", "", "the country")]
		[TestCase("the country", "the province", "", "the province, the country")]
		[TestCase("the country", "the province", "the district", "the district, the province, the country")]
		[TestCase("", "the province", "the district", "the district, the province")]
		[TestCase("", "", "the district", "the district")]
		[TestCase("", "the province", "", "the province")]
		public void Constructor_CollectionSettingsHasVariousLocationFields_LanguageLocationFilledCorrect(string country, string province, string district, string expected)
		{
			var dom = new HtmlDom();
			var data = new BookData(dom, new CollectionSettings(){Country=country, Province = province, District= district}, null);
			Assert.AreEqual(expected, data.GetVariableOrNull("languageLocation", "*"));
		  }

		/*    data.AddLanguageString("*", "nameOfLanguage", collectionSettings.Language1Name, true);
				data.AddLanguageString("*", "nameOfNationalLanguage1",
									   collectionSettings.GetLanguage2Name(collectionSettings.Language2Iso639Code), true);
				data.AddLanguageString("*", "nameOfNationalLanguage2",
									   collectionSettings.GetLanguage3Name(collectionSettings.Language2Iso639Code), true);
				data.AddGenericLanguageString("iso639Code", collectionSettings.Language1Iso639Code, true);*/

		[Test]
		public void Constructor_CollectionSettingsHasISO639Code_iso639CodeFilledIn()
		{
			var dom = new HtmlDom();
			var data = new BookData(dom, new CollectionSettings() { Language1Iso639Code = "xyz" }, null);
			Assert.AreEqual("xyz", data.GetVariableOrNull("iso639Code", "*"));
		}
		[Test]
		public void Constructor_CollectionSettingsHasISO639Code_DataSetContainsProperV()
		{
			var dom = new HtmlDom();
			var data = new BookData(dom, new CollectionSettings() { Language1Iso639Code = "xyz" }, null);
			Assert.AreEqual("xyz", data.GetWritingSystemCodes()["V"]);
		}
		[Test]
		public void Constructor_CollectionSettingsHasLanguage1Name_LanguagenameOfNationalLanguage1FilledIn()
		{
			var dom = new HtmlDom();
			var data = new BookData(dom, new CollectionSettings() { Language1Name = "foobar" }, null);
			Assert.AreEqual("foobar", data.GetVariableOrNull("nameOfLanguage", "*"));
		}

		//NB: yes, this is confusing, having lang1 = language, lang2 = nationalLang1, lang3 = nationalLang2

		[Test]
		public void Constructor_CollectionSettingsHasLanguage2Iso639Code_nameOfNationalLanguage1FilledIn()
		{
			var dom = new HtmlDom();
			var data = new BookData(dom, new CollectionSettings() { Language2Iso639Code = "tpi" }, null);
			Assert.AreEqual("Tok Pisin", data.GetVariableOrNull("nameOfNationalLanguage1", "*"));
		}
		[Test]
		public void Constructor_CollectionSettingsHasLanguage3Iso639Code_nameOfNationalLanguage2FilledIn()
		{
			var dom = new HtmlDom();
			var data = new BookData(dom, new CollectionSettings() { Language3Iso639Code = "tpi" }, null);
			Assert.AreEqual("Tok Pisin", data.GetVariableOrNull("nameOfNationalLanguage2", "*"));
		}

		[Test]
		public void Set_DidNotHaveForm_Added()
		{
			var htmlDom = new HtmlDom();
			var data = new BookData(htmlDom, new CollectionSettings(), null);
			data.Set("1", "one", "en");
			Assert.AreEqual("one", data.GetVariableOrNull("1", "en"));
			AssertThatXmlIn.Dom(htmlDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en']",1);
			var roundTripData = new BookData(htmlDom, new CollectionSettings(), null);
			var t = roundTripData.GetVariableOrNull("1", "en");
			Assert.AreEqual("one", t);
		}

		[Test]
		public void Set_AddTwoForms_BothAdded()
		{
			var htmlDom = new HtmlDom();
			var data = new BookData(htmlDom, new CollectionSettings(), null);
			data.Set("1", "one", "en");
			data.Set("1", "uno", "es");
			var roundTripData = new BookData(htmlDom, new CollectionSettings(), null);
			Assert.AreEqual("one", roundTripData.GetVariableOrNull("1", "en"));
			Assert.AreEqual("uno", roundTripData.GetVariableOrNull("1", "es"));
		}

		[Test]
		public void Set_DidHaveForm_StillJustOneCopy()
		{
			var htmlDom = new HtmlDom();
			var data = new BookData(htmlDom, new CollectionSettings(), null);
			data.Set("1", "one", "en");
			data.Set("1", "one", "en");
			Assert.AreEqual("one", data.GetVariableOrNull("1", "en"));
			AssertThatXmlIn.Dom(htmlDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en']", 1);
			var roundTripData = new BookData(htmlDom, new CollectionSettings(), null);
			var t = roundTripData.GetVariableOrNull("1", "en");
			Assert.AreEqual("one", t);
		}

		[Test]
		public void Set_EmptyString_Removes()
		{
			var htmlDom = new HtmlDom();
			var data = new BookData(htmlDom, new CollectionSettings(), null);
			data.Set("1", "one", "en");
			data.Set("1", "", "en");
			Assert.AreEqual(null, data.GetVariableOrNull("1", "en"));
			AssertThatXmlIn.Dom(htmlDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en']", 0);
			var roundTripData = new BookData(htmlDom, new CollectionSettings(), null);
			Assert.IsNull(roundTripData.GetVariableOrNull("1", "en"));
		}

		[Test]
		public void Set_Null_Removes()
		{
			var htmlDom = new HtmlDom();
			var data = new BookData(htmlDom, new CollectionSettings(), null);
			data.Set("1", "one", "en");
			data.Set("1", null, "en");
			Assert.AreEqual(null, data.GetVariableOrNull("1", "en"));
			AssertThatXmlIn.Dom(htmlDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en']", 0);
			var roundTripData = new BookData(htmlDom, new CollectionSettings(), null);
			Assert.IsNull(roundTripData.GetVariableOrNull("1", "en"));
		}

		[Test]
		public void RemoveSingleForm_HasForm_Removed()
		{
			var htmlDom = new HtmlDom();
			var data = new BookData(htmlDom, new CollectionSettings(), null);
			data.Set("1","one","en");
			var data2 = new BookData(htmlDom, new CollectionSettings(), null);
			data2.RemoveSingleForm("1","en");
			Assert.IsNull(data2.GetVariableOrNull("1", "en"));
		}

		[Test]
		public void RemoveDataDivVariableForOneLanguage_DoesNotHaveForm_OK()
		{
			var htmlDom = new HtmlDom();
			var data = new BookData(htmlDom, new CollectionSettings(), null);
			data.RemoveSingleForm("1", "en");
			Assert.AreEqual(null, data.GetVariableOrNull("1", "en"));
			AssertThatXmlIn.Dom(htmlDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en']", 0);
			var roundTripData = new BookData(htmlDom, new CollectionSettings(), null);
			Assert.IsNull(roundTripData.GetVariableOrNull("1", "en"));
		}

		[Test]
		public void RemoveDataDivVariableForOneLanguage_WasLastForm_WholeElementRemoved()
		{
			var htmlDom = new HtmlDom();
			var data = new BookData(htmlDom, new CollectionSettings(), null);
			data.Set("1","one","en");
			var roundTripData = new BookData(htmlDom, new CollectionSettings(), null);
			roundTripData.RemoveSingleForm("1", "en");
			Assert.IsNull(roundTripData.GetVariableOrNull("1", "en"));

		}


		[Test]
		public void RemoveDataDivVariableForOneLanguage_WasTwoForms_OtherRemains()
		{
			var htmlDom = new HtmlDom();
			var data = new BookData(htmlDom, new CollectionSettings(), null);
			data.Set("1", "one", "en");
			data.Set("1", "uno", "es");
			var roundTripData = new BookData(htmlDom, new CollectionSettings(), null);
			roundTripData.RemoveSingleForm("1", "en");
			Assert.IsNull(roundTripData.GetVariableOrNull("1", "en"));
			Assert.AreEqual("uno",roundTripData.GetVariableOrNull("1","es"));
		}


		[Test]
		public void Set_CalledTwiceWithDIfferentLangs_HasBoth()
		{
			var htmlDom = new HtmlDom();
			var data = new BookData(htmlDom, new CollectionSettings(), null);
			data.Set("1", "one", "en");
			data.Set("1", "uno", "es");
			Assert.AreEqual(2,data.GetMultiTextVariableOrEmpty("1").Forms.Count());
		}

		[Test]
		public void UpdateVariablesAndDataDivThroughDOM_VariableIsNull_DataDivForItRemoved()
		{
			var htmlDom = new HtmlDom();
			var data = new BookData(htmlDom, new CollectionSettings(), null);
			data.Set("1","one","en");
			data.Set("1", null, "es");
			data.UpdateVariablesAndDataDivThroughDOM();
			AssertThatXmlIn.Dom(htmlDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("html/body/div/div[@lang='en']",1);
			AssertThatXmlIn.Dom(htmlDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("html/body/div/div[@lang='es']", 0);
		}

		[Test]
		public void PrettyPrintLanguage_DoesNotModifyUnknownCodes()
		{
			var htmlDom = new HtmlDom();
			var settingsettings = new CollectionSettings() { Language1Iso639Code = "pdc", Language1Name = "German, Kludged" };
			var data = new BookData(htmlDom, settingsettings, null);
			Assert.That(data.PrettyPrintLanguage("xyz"), Is.EqualTo("xyz"));
		}

		[Test]
		public void PrettyPrintLanguage_AdjustsLang1()
		{
			var htmlDom = new HtmlDom();
			var settingsettings = new CollectionSettings() {Language1Iso639Code = "pdc", Language1Name = "German, Kludged"};
			var data = new BookData(htmlDom, settingsettings, null);
			Assert.That(data.PrettyPrintLanguage("pdc"), Is.EqualTo("German, Kludged"));
		}

		[Test]
		public void PrettyPrintLanguage_AdjustsKnownLanguages()
		{
			var htmlDom = new HtmlDom();
			var settingsettings = new CollectionSettings() { Language1Iso639Code = "pdc", Language1Name = "German, Kludged", Language2Iso639Code = "de", Language3Iso639Code = "fr"};
			var data = new BookData(htmlDom, settingsettings, null);
			Assert.That(data.PrettyPrintLanguage("de"), Is.EqualTo("German"));
			Assert.That(data.PrettyPrintLanguage("fr"), Is.EqualTo("French"));
			Assert.That(data.PrettyPrintLanguage("en"), Is.EqualTo("English"));
			Assert.That(data.PrettyPrintLanguage("es"), Is.EqualTo("Spanish"));
		}

		[Test]
		public void MigrateData_TopicInTokPisinButNotEnglish_ChangesLangeToEnglish()
		{
			var bookDom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
						<div data-book='topic' lang='tpi'>health</div>
				</div>
			 </body></html>");

			var data = new BookData(bookDom, _collectionSettings, null);
			Assert.AreEqual("health", data.GetVariableOrNull("topic", "en"));
			Assert.IsNull(data.GetVariableOrNull("topic", "tpi"));
		}

		[Test]
		public void SynchronizeDataItemsThroughoutDOM_VariousTopicScenarios()
		{
			TestTopicHandling("Health", "fr", "Santé", "fr", "en", null, "Should use lang1");
			TestTopicHandling("Health", "fr", "Santé", "x", "fr", null, "Should use lang2");
			TestTopicHandling("Health", "fr", "Santé", "x", "y", "fr", "Should use lang3");
			TestTopicHandling("Health", "en", "Health", "x", "y", "z", "Should use English");
			TestTopicHandling("Health", "en", "Health", "en", "fr", "es", "Should use lang1");
			TestTopicHandling("NoTopic", "", "", "en", "fr", "es", "'No Topic' should give no @lang and no text");
			TestTopicHandling("Bogus", "en", "Bogus", "z", "fr", "es", "Unrecognized topic should give topic in English");
		}
		private void TestTopicHandling(string topicKey, string expectedLanguage, string expectedTranslation, string lang1, string lang2, string lang3, string description)
		{
			_collectionSettings.Language1Iso639Code = lang1;
			_collectionSettings.Language2Iso639Code = lang2;
			_collectionSettings.Language3Iso639Code = lang3;

			var bookDom = new HtmlDom(@"<html><body>
				<div id='bloomDataDiv'>
						<div data-book='topic' lang='en'>"+topicKey+@"</div>
				</div>
				<div id='somePage'>
                    <div id='test' data-derived='topic'>
					</div>
                </div>
			 </body></html>");
			var data = new BookData(bookDom, _collectionSettings, null);
			data.SynchronizeDataItemsThroughoutDOM();
			try
			{
				if (string.IsNullOrEmpty(expectedLanguage))
				{
					AssertThatXmlIn.Dom(bookDom.RawDom)
						.HasSpecifiedNumberOfMatchesForXpath(
							"//div[@id='test' and @data-derived='topic' and not(@lang) and text()='" + expectedTranslation + "']", 1);
				}
				else
				{
					AssertThatXmlIn.Dom(bookDom.RawDom)
						.HasSpecifiedNumberOfMatchesForXpath(
							"//div[@id='test' and @data-derived='topic' and @lang='" + expectedLanguage + "' and text()='" +
							expectedTranslation + "']", 1);
				}
			}
			catch (Exception)
			{
				Assert.Fail(description);
			}

		}

		/// <summary>
		/// we use English as the one and only "key" language for topics in the datadiv
		/// </summary>
		[Test]
		public void SynchronizeDataItemsThroughoutDOM_HasMultipleTopicItems_RemovesAllButEnglish()
		{
			var bookDom = new HtmlDom(@"<html><body>
				<div id='bloomDataDiv'>
						<div data-book='topic' lang='en'>Health</div>
						<div data-book='topic' lang='fr'>Santé</div>
				</div>
			 </body></html>");
			var data = new BookData(bookDom, _collectionSettings, null);
			data.SynchronizeDataItemsThroughoutDOM();
			AssertThatXmlIn.Dom(bookDom.RawDom).HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-book='topic' and @lang='fr']");
		}

		[Test]
		public void SynchronizeDataItemsThroughoutDOM_TopicHasParagraphElement_Removed()
		{
			var bookDom = new HtmlDom(@"<html><body>
				<div id='bloomDataDiv'>
						<div data-book='topic' lang='en'><p>Health</p></div>
				</div>
			 </body></html>");
			var data = new BookData(bookDom, _collectionSettings, null);
			data.SynchronizeDataItemsThroughoutDOM();
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@data-derived='topic' and @lang='en']/p", 0);
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='topic' and @lang='en' and text()='Health']", 1);
		}
		private bool AndikaNewBasicIsInstalled()
		{
			const string fontToCheck = "andika new basic";
			return FontFamily.Families.FirstOrDefault(f => f.Name.ToLowerInvariant() == fontToCheck) != null;
		}

		[Test]
		[Category("SkipOnTeamCity")]
		public void AndikaNewBasic_MustBeInstalled()
		{
			Assert.That(AndikaNewBasicIsInstalled());
		}

		[Test]
		public void OneTimeCheckVersionNumber_AndikaNewBasicMigration_DoIt()
		{
			// This test needs Andika New Basic installed to work
			// dump out and pass if the font isn't installed
			if (!AndikaNewBasicIsInstalled())
				return; // quietly pass the test if the font isn't installed

			var filepath = _collectionSettings.SettingsFilePath;
			var cssFilePath = Path.GetDirectoryName(filepath).CombineForPath("settingsCollectionStyles.css");
			File.Delete(cssFilePath);
			WriteSettingsFile(filepath, _preAndikaMigrationCollection);

			// SUT
			_collectionSettings.Load();

			// Verify
			var oneTimeCheckVersion = _collectionSettings.OneTimeCheckVersionNumber;
			Assert.That(Convert.ToInt32(oneTimeCheckVersion).Equals(1));
			var font1 = _collectionSettings.DefaultLanguage1FontName;
			Assert.That(font1.Equals("Andika New Basic"));
			var font2 = _collectionSettings.DefaultLanguage1FontName;
			Assert.That(font2.Equals("Andika New Basic"));
			var font3 = _collectionSettings.DefaultLanguage1FontName;
			Assert.That(font3.Equals("Andika New Basic"));
			Assert.That(File.Exists(cssFilePath)); // if this file exists, it means we did the migration
		}

		[Test]
		public void OneTimeCheckVersionNumber_AndikaNewBasicMigration_alreadyDone()
		{
			var filepath = _collectionSettings.SettingsFilePath;
			var cssFilePath = Path.GetDirectoryName(filepath).CombineForPath("settingsCollectionStyles.css");
			File.Delete(cssFilePath);
			WriteSettingsFile(filepath, _postAndikaMigrationCollection);

			// SUT
			_collectionSettings.Load();

			// Verify
			var font1 = _collectionSettings.DefaultLanguage1FontName;
			var oneTimeCheckVersion = _collectionSettings.OneTimeCheckVersionNumber;
			Assert.That(Convert.ToInt32(oneTimeCheckVersion).Equals(1));
			Assert.That(font1.Equals("Andika New Basic"));
			Assert.That(!File.Exists(cssFilePath)); // if this file doesn't exist, it means we didn't do any migration
		}

		[Test]
		public void OneTimeCheckVersionNumber_AndikaNewBasicMigration_doneUserReverted()
		{
			var filepath = _collectionSettings.SettingsFilePath;
			var cssFilePath = Path.GetDirectoryName(filepath).CombineForPath("settingsCollectionStyles.css");
			File.Delete(cssFilePath);
			WriteSettingsFile(filepath, _postAndikaMigrationCollectionNoANB);

			// SUT
			_collectionSettings.Load();

			// Verify
			var font1 = _collectionSettings.DefaultLanguage1FontName;
			var oneTimeCheckVersion = _collectionSettings.OneTimeCheckVersionNumber;
			Assert.That(Convert.ToInt32(oneTimeCheckVersion).Equals(1));
			Assert.That(font1.Equals("Andika"));
			Assert.That(!File.Exists(cssFilePath)); // if this file doesn't exist, it means we didn't do any migration
		}

		private void WriteSettingsFile(string filepath, string xmlString)
		{
			File.WriteAllText(filepath, xmlString);
		}

		#region Collection Settings test data

		private const string _preAndikaMigrationCollection = @"﻿<?xml version='1.0' encoding='utf-8'?>
			<Collection version='0.2'>
				<Language1Name>Tok Pisin</Language1Name>
				<Language1Iso639Code>tpi</Language1Iso639Code>
				<Language2Iso639Code>en</Language2Iso639Code>
				<Language3Iso639Code>ara</Language3Iso639Code>
				<DefaultLanguage1FontName>Andika</DefaultLanguage1FontName>
				<DefaultLanguage2FontName>Andika</DefaultLanguage2FontName>
				<DefaultLanguage3FontName>Andika</DefaultLanguage3FontName>
				<IsLanguage1Rtl>false</IsLanguage1Rtl>
				<IsLanguage2Rtl>false</IsLanguage2Rtl>
				<IsLanguage3Rtl>true</IsLanguage3Rtl>
				<IsSourceCollection>False</IsSourceCollection>
				<XMatterPack>Factory</XMatterPack>
				<Country></Country>
				<Province></Province>
				<District></District>
				<AllowNewBooks>True</AllowNewBooks>
			</Collection>";

		private const string _postAndikaMigrationCollection = @"﻿<?xml version='1.0' encoding='utf-8'?>
			<Collection version='0.2'>
				<Language1Name>Tok Pisin</Language1Name>
				<Language1Iso639Code>tpi</Language1Iso639Code>
				<Language2Iso639Code>en</Language2Iso639Code>
				<Language3Iso639Code>ara</Language3Iso639Code>
				<DefaultLanguage1FontName>Andika New Basic</DefaultLanguage1FontName>
				<DefaultLanguage2FontName>Andika New Basic</DefaultLanguage2FontName>
				<DefaultLanguage3FontName>Andika New Basic</DefaultLanguage3FontName>
				<OneTimeCheckVersionNumber>1</OneTimeCheckVersionNumber>
				<IsLanguage1Rtl>false</IsLanguage1Rtl>
				<IsLanguage2Rtl>false</IsLanguage2Rtl>
				<IsLanguage3Rtl>true</IsLanguage3Rtl>
				<IsSourceCollection>False</IsSourceCollection>
				<XMatterPack>Factory</XMatterPack>
				<Country></Country>
				<Province></Province>
				<District></District>
				<AllowNewBooks>True</AllowNewBooks>
			</Collection>";

		private const string _postAndikaMigrationCollectionNoANB = @"﻿<?xml version='1.0' encoding='utf-8'?>
			<Collection version='0.2'>
				<Language1Name>Tok Pisin</Language1Name>
				<Language1Iso639Code>tpi</Language1Iso639Code>
				<Language2Iso639Code>en</Language2Iso639Code>
				<Language3Iso639Code>ara</Language3Iso639Code>
				<DefaultLanguage1FontName>Andika</DefaultLanguage1FontName>
				<DefaultLanguage2FontName>Andika</DefaultLanguage2FontName>
				<DefaultLanguage3FontName>Andika</DefaultLanguage3FontName>
				<OneTimeCheckVersionNumber>1</OneTimeCheckVersionNumber>
				<IsLanguage1Rtl>false</IsLanguage1Rtl>
				<IsLanguage2Rtl>false</IsLanguage2Rtl>
				<IsLanguage3Rtl>true</IsLanguage3Rtl>
				<IsSourceCollection>False</IsSourceCollection>
				<XMatterPack>Factory</XMatterPack>
				<Country></Country>
				<Province></Province>
				<District></District>
				<AllowNewBooks>True</AllowNewBooks>
			</Collection>";

		#endregion


//		[Test]
//		public void SynchronizeDataItemsThroughoutDOM_EnglishTitleButNoVernacular_DoesNotCopyInEnglish()
//		{
//			var dom = new HtmlDom(@"<html ><head></head><body>
//                <div id='bloomDataDiv'>
//                     <div data-book='bookTitle' lang='en'>the title</div>
//                </div>
//                <div class='bloom-page verso'>
//					 <div id='originalContributions' class='bloom-translationGroup'>
//						<div data-book='originalContributions' lang='etr'></div>
//						<div data-book='originalContributions' lang='en'></div>
//					</div>
//                </div>
//                </body></html>");
//			var collectionSettings = new CollectionSettings()
//			{
//				Language1Iso639Code = "fr"
//			};
//			var data = new BookData(dom, collectionSettings, null);
//			data.SynchronizeDataItemsThroughoutDOM();
//			XmlElement englishContributions = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//*[@data-book='originalContributions' and @lang='en']");
//			Assert.AreEqual("the contributions", englishContributions.InnerText, "Should copy English into body of course, as normal");
//			XmlElement frenchContributions = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//*[@data-book='originalContributions' and @lang='fr']");
//			Assert.AreEqual("the contributions", frenchContributions.InnerText, "Should copy English into French Contributions becuase it's better than just showing nothing");
//		}


		[Test]
		public void SynchronizeDataItemsThroughoutDOM_HasOnlyEnglishContributorsButEnglishIsLang3_CopiesEnglishIntoNationalLanguageSlot()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
					 <div data-book='originalContributions' lang='en'>the contributions</div>
				</div>
				<div class='bloom-page verso'>
					 <div id='originalContributions' class='bloom-translationGroup'>
						<div  class='bloom-copyFromOtherLanguageIfNecessary'  data-book='originalContributions' lang='fr'></div>
						<div  class='bloom-copyFromOtherLanguageIfNecessary'  data-book='originalContributions' lang='en'></div>
					</div>
				</div>
				</body></html>");
			var collectionSettings = new CollectionSettings()
				{
					  Language1Iso639Code = "etr",
					  Language2Iso639Code = "fr"
				};
			var data = new BookData(dom, collectionSettings, null);
			data.SynchronizeDataItemsThroughoutDOM();
			XmlElement englishContributions = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//*[@data-book='originalContributions' and @lang='en']");
			Assert.AreEqual("the contributions", englishContributions.InnerText, "Should copy English into body of course, as normal");
			XmlElement frenchContributions = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//*[@data-book='originalContributions' and @lang='fr']");
			Assert.AreEqual("the contributions", frenchContributions.InnerText, "Should copy English into French Contributions becuase it's better than just showing nothing");
			//Assert.AreEqual("en",frenchContributions.GetAttribute("bloom-languageBloomHadToCopyFrom"),"Should have left a record that we did this dubious 'borrowing' from English");
		}



		[Test]
		public void SynchronizeDataItemsThroughoutDOM_HasOnlyEnglishContributorsInDataDivButFrenchInBody_DoesNotCopyEnglishIntoFrenchSlot()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
					 <div data-book='originalContributions' lang='en'>the contributions</div>
				</div>
				<div class='bloom-page verso'>
					 <div id='originalContributions' class='bloom-translationGroup'>
						<div data-book='originalContributions' lang='fr'>les contributeurs</div>
						<div data-book='originalContributions' lang='xyz'></div>
					</div>
				</div>
				</body></html>");
			var collectionSettings = new CollectionSettings()
			{
				Language1Iso639Code = "etr",
				Language2Iso639Code = "fr"
			};
			var data = new BookData(dom, collectionSettings, null);
			data.SynchronizeDataItemsThroughoutDOM();
			XmlElement frenchContributions = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//*[@data-book='originalContributions' and @lang='fr']");
			Assert.AreEqual("les contributeurs", frenchContributions.InnerText, "Should not touch existing French Contributions");
			//Assert.IsFalse(frenchContributions.HasAttribute("bloom-languageBloomHadToCopyFrom"));
		}


		[Test]
		public void SynchronizeDataItemsThroughoutDOM_HasFrenchAndEnglishContributorsInDataDiv_DoesNotCopyEnglishIntoFrenchSlot()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
					 <div data-book='originalContributions' lang='en'>the contributions</div>
					<div data-book='originalContributions' lang='fr'>les contributeurs</div>
				</div>
				<div class='bloom-page verso'>
					 <div id='originalContributions' class='bloom-translationGroup'>
						<div data-book='originalContributions' lang='fr'></div>
						<div data-book='originalContributions' lang='xyz'></div>
					</div>
				</div>
				</body></html>");
			var collectionSettings = new CollectionSettings()
			{
				Language1Iso639Code = "xyz",
				Language2Iso639Code = "fr"
			};
			var data = new BookData(dom, collectionSettings, null);
			data.SynchronizeDataItemsThroughoutDOM();
			XmlElement frenchContributions = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//*[@data-book='originalContributions' and @lang='fr']");
			Assert.AreEqual("les contributeurs", frenchContributions.InnerText, "Should use the French, not the English even though the French in the body was empty");
			XmlElement vernacularContributions = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//*[@data-book='originalContributions' and @lang='xyz']");
			Assert.AreEqual("", vernacularContributions.InnerText, "Should not copy Edolo into Vernacualr Contributions. Only national language fields get this treatment");
		}

		[Test]
		public void SynchronizeDataItemsThroughoutDOM_HasOnlyEdoloContributors_CopiesItIntoL2ButNotL1()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
					 <div data-book='originalContributions' lang='etr'>the contributions</div>
				</div>
				<div class='bloom-page verso'>
					 <div id='originalContributions' class='bloom-translationGroup'>
						<div class='bloom-copyFromOtherLanguageIfNecessary' data-book='originalContributions' lang='fr'></div>
						<div  class='bloom-copyFromOtherLanguageIfNecessary'  data-book='originalContributions' lang='xyz'></div>
					</div>
				</div>
				</body></html>");
			var collectionSettings = new CollectionSettings()
			{
					  Language1Iso639Code = "xyz",
					  Language2Iso639Code = "fr"
			};
			var data = new BookData(dom, collectionSettings, null);
			data.SynchronizeDataItemsThroughoutDOM();
			XmlElement frenchContributions = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//*[@data-book='originalContributions' and @lang='fr']");
			Assert.AreEqual("the contributions", frenchContributions.InnerText, "Should copy Edolo into French Contributions becuase it's better than just showing nothing");
			XmlElement vernacularContributions = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//*[@data-book='originalContributions' and @lang='xyz']");
			Assert.AreEqual("", vernacularContributions.InnerText, "Should not copy Edolo into Vernacualr Contributions. Only national language fields get this treatment");
		}

		//		[Test]
		//		public void PrepareForEditing_CustomLicenseNotDiscarded()
		//		{
		//			SetDom(@"<div id='bloomDataDiv'>
		//						<div data-book='copyright' lang='*'>
		//							Copyright © 2015, me
		//						</div>
		//						<div data-book='licenseNotes' lang='en'>
		//							Custom license info
		//						</div>
		//					</div>");
		//			var book = CreateBook();
		//			var dom = book.RawDom;
		//			book.PrepareForEditing();
		//			var copyright = dom.SelectSingleNodeHonoringDefaultNS("//div[@class='marginBox']//div[@data-book='copyright']").InnerText;
		//			var licenseBlock = dom.SelectSingleNodeHonoringDefaultNS("//div[@class='licenseBlock']");
		//			var licenseImage = licenseBlock.SelectSingleNode("img");
		//			var licenseUrl = licenseBlock.SelectSingleNode("div[@data-book='licenseUrl']").InnerText;
		//			var licenseDescription = licenseBlock.SelectSingleNode("div[@data-book='licenseDescription']").InnerText;
		//			var licenseNotes = licenseBlock.SelectSingleNode("div[@data-book='licenseNotes']").InnerText;
		//			// Check that updated dom has the right license contents on the Credits page
		//			// Check that data-div hasn't been contaminated with non-custom license stuff
		//			Assert.AreEqual("Copyright © 2015, me", copyright);
		//			Assert.AreEqual("Custom license info", licenseNotes);
		//			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@class='licenseBlock']/div[@data-book='licenseUrl' and text()='']", 1);
		//			Assert.IsEmpty(licenseDescription);
		//			Assert.IsEmpty(licenseImage.Attributes["src"].Value);
		//			Assert.IsNull(licenseImage.Attributes["alt"]);
		//		}


		/// <summary>
		/// BL-3078 where when xmatter was injected and updated, the text stored in data-book overwrote
		/// the innerxml of the div, knocking out the <label></label> in there, which lead to losing
		/// the side bubbles explaining what the field was for.
		/// </summary>
		[Test]
		public void SynchronizeDataItemsThroughoutDOM_EditableHasLabelElement_LabelPreserved()
		{
			var dom = new HtmlDom(@"<html ><head></head><body>
				<div id='bloomDataDiv'>
					 <div data-book='insideBackCover' lang='en'><p/></div>
				</div>
				<div class='bloom-page'>
					 <div id='foo' class='bloom-content1 bloom-editable' data-book='insideBackCover' lang='en'>
						<label>some label</label>
					</div>
				</div>
				</body></html>");
			var data = new BookData(dom, _collectionSettings, null);
			data.SynchronizeDataItemsThroughoutDOM();
			var foo = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//*[@id='foo']");
			Assert.That(foo.InnerXml, Contains.Substring("<label>some label</label>"));
		}
	}
}
