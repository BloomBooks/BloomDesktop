using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using Bloom.Collection;
using NUnit.Framework;
using Bloom.Book;
using Palaso.TestUtilities;

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
			var data = new BookData(dom, "pretendPath", "one", _collectionSettings);
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
			var data = new BookData(dom, "pretendPath", "one",  _collectionSettings);
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
			var data = new BookData(dom, "pretendPath", "one",  new CollectionSettings());
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
			var data = new BookData(dom, "pretendPath", "one",  new CollectionSettings());
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
			var data = new BookData(dom, "pretendPath", "one",  collectionSettings);
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
			var data = new BookData(dom, "pretendPath", "one",  collectionSettings);
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
			var data = new BookData(dom, "pretendPath", "one",  collectionSettings);
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
			var data = new BookData(dom, "pretendPath", "one",  new CollectionSettings());
			data.UpdateVariablesAndDataDivThroughDOM();
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//body/div[1][@id='bloomDataDiv']", 1);//NB microsoft uses 1 as the first. W3c uses 0.
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='someVariable' and @lang='en' and text()='hi']", 1);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='someVariable' and @lang='fr' and text()='bonjour']", 1);
		}

		[Test]
		public void UpdateVariablesAndDataDivThroughDOM_HasDataLibraryValues_LibraryValuesNotPutInDataDiv()
		{
			var dom = new HtmlDom(@"<html><head></head><body><div data-book='someVariable' lang='en'>hi</div><div data-library='user' lang='en'>john</div></body></html>");
			var data = new BookData(dom, "pretendPath", "one",  new CollectionSettings());
			data.UpdateVariablesAndDataDivThroughDOM();
			AssertThatXmlIn.Dom(dom.RawDom).HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-book='user']");
			AssertThatXmlIn.Dom(dom.RawDom).HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-library]");
		}

		[Test]
		public void UpdateVariablesAndDataDivThroughDOM_DoesNotExist_MakesOne()
		{
			var dom = new HtmlDom(@"<html><head></head><body><div data-book='someVariable'>world</div></body></html>");
			var data = new BookData(dom, "pretendPath", "one",  new CollectionSettings());
			data.UpdateVariablesAndDataDivThroughDOM();
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//body/div[1][@id='bloomDataDiv']", 1);//NB microsoft uses 1 as the first. W3c uses 0.
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='someVariable' and text()='world']", 1);
		}


		[Test]
		public void SetMultilingualContentLanguages_HasTrilingualLanguages_AddsToDataDiv()
		{
			var dom = new HtmlDom(@"<html><head></head><body></body></html>");
			var data = new BookData(dom, "pretendPath", "one", new CollectionSettings());
			data.SetMultilingualContentLanguages("okm", "kbt");
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='contentLanguage2' and text()='okm']", 1);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='contentLanguage3' and text()='kbt']", 1);
		}
		[Test]
		public void SetMultilingualContentLanguages_ThirdContentLangTurnedOff_RemovedFromDataDiv()
		{
			var dom = new HtmlDom(@"<html><head><div id='bloomDataDiv'><div data-book='contentLanguage2'>xyz</div><div data-book='contentLanguage3'>kbt</div></div></head><body></body></html>");
			var data = new BookData(dom, "pretendPath", "one", new CollectionSettings());
			data.SetMultilingualContentLanguages(null,null);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='contentLanguage3']", 0);
		}
	}
}
