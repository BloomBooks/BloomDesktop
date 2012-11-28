using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using Bloom.Collection;
using NUnit.Framework;
using Bloom.Book;
using Palaso.TestUtilities;
using Palaso.UI.WindowsForms.ClearShare;

namespace BloomTests.Book
{
	[TestFixture]
	public sealed class BookDataTests
	{
		private CollectionSettings _collectionSettings;

		[SetUp]
		public void Setup()
		{
			_collectionSettings = new CollectionSettings(new NewCollectionSettings() {
				PathToSettingsFile = CollectionSettings.GetPathForNewSettings(new TemporaryFolder("BookDataTests").Path, "test"),
				Language1Iso639Code = "xyz", Language2Iso639Code = "en", Language3Iso639Code = "fr" });
		}

		[Test]
		public void UpdateFieldsAndVariables_CustomLibraryVariable_CopiedToOtherElement()
		{
			var dom=new HtmlDom(@"<html ><head></head><body>
				<div class='bloom-page' id='guid3'>
					<p>
						<textarea lang='xyz' id='copyOfVTitle'  data-book='bookTitle'>tree</textarea>
						<textarea lang='xyz' id='1' data-library='testLibraryVariable'>aa</textarea>
					   <textarea lang='xyz' id='2'  data-library='testLibraryVariable'>bb</textarea>
					</p>
				</div>
				</body></html>");
			var data = new BookData(dom, _collectionSettings, null);
			data.UpdateVariablesAndDataDivThroughDOM();
			var textarea2 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='2']");
			Assert.AreEqual("aa", textarea2.InnerText);
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
			var textarea1 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@data-book='bookTitle' and @lang='xyz']");
			textarea1.InnerText = "peace";
			var data = new BookData(dom,  _collectionSettings, null);
			data.SynchronizeDataItemsThroughoutDOM();
			var paragraph = dom.SelectSingleNodeHonoringDefaultNS("//p[@data-book='bookTitle'  and @lang='xyz']");
			Assert.AreEqual("peace", paragraph.InnerText);
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
			var data = new BookData(dom,   new CollectionSettings(), null);
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
			var data = new BookData(dom,   new CollectionSettings(), null);
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
			var collectionSettings = new CollectionSettings();
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
			var dom = new HtmlDom(@"<html><head></head><body><div data-book='someVariable' lang='en'>hi</div><div data-library='user' lang='en'>john</div></body></html>");
			var data = new BookData(dom,   new CollectionSettings(), null);
			data.UpdateVariablesAndDataDivThroughDOM();
			AssertThatXmlIn.Dom(dom.RawDom).HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-book='user']");
			AssertThatXmlIn.Dom(dom.RawDom).HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-library]");
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


		[Test]
		public void Constructor_CollectionSettingsHasCountrProvinceDistrict_LanguageLocationFilledIn()
		{
//            var dom = new HtmlDom(@"<html><head><div id='bloomDataDiv'>
//                    <div data-book='country'>the country</div>
//                    <div data-book='province'>the province</div>
//                    <div data-book='district'>the district</div>
//            </div></head><body></body></html>");
			var dom = new HtmlDom();
			var data = new BookData(dom, new CollectionSettings(){Country="the country", Province = "the province", District= "the district"}, null);
			Assert.AreEqual("the district, the province<br/>the country", data.GetVariableOrNull("languageLocation", "*"));
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

		#region Metadata
		[Test]
		public void GetLicenseMetadata_HasCustomLicense_RightsStatementContainsCustom()
		{
			string dataDivContent= @"<div lang='en' data-book='licenseNotes'>my custom</div>
					<div data-book='copyright' class='bloom-content1'>Copyright © 2012, test</div>";
			Assert.AreEqual("my custom", GetMetadata(dataDivContent).License.RightsStatement);
		}
		[Test]
		public void GetLicenseMetadata_HasCCLicenseURL_ConvertedToFulCCLicenseObject()
		{
			//nb: the real testing is done on the palaso class that does the reading, this is just a quick sanity check
			string dataDivContent = @"<div lang='en' data-book='licenseUrl'>http://creativecommons.org/licenses/by-nc-sa/3.0/</div>";
			var creativeCommonsLicense = (CreativeCommonsLicense) (GetMetadata(dataDivContent).License);
			Assert.IsTrue(creativeCommonsLicense.AttributionRequired);
			Assert.IsFalse(creativeCommonsLicense.CommercialUseAllowed);
			Assert.IsTrue(creativeCommonsLicense.DerivativeRule== CreativeCommonsLicense.DerivativeRules.DerivativesWithShareAndShareAlike);
		}
		[Test]
		public void GetLicenseMetadata_NullLicense_()
		{
			//nb: the real testing is done on the palaso class that does the reading, this is just a quick sanity check
			string dataDivContent = @"<div lang='en' data-book='licenseDescription'>This could say anthing</div>";
			Assert.IsTrue(GetMetadata(dataDivContent).License is NullLicense);
		}

		[Test]
		public void GetLicenseMetadata_HasSymbolInCopyright_FullCopyrightStatmentAcquired()
		{
			string dataDivContent = @"<div data-book='copyright' class='bloom-content1'>Copyright © 2012, test</div>";
			Assert.AreEqual("Copyright © 2012, test", GetMetadata(dataDivContent).CopyrightNotice);
		}

		private static Metadata GetMetadata(string dataDivContent)
		{
			var dom = new HtmlDom(@"<html><head><div id='bloomDataDiv'>" + dataDivContent + "</div></head><body></body></html>");
			var data = new BookData(dom, new CollectionSettings(), null);
			return data.GetLicenseMetadata();
		}

		#endregion
	}
}
