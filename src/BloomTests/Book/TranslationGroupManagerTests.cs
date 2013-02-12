using System.Xml;
using Bloom.Book;
using Bloom.Collection;
using Moq;
using NUnit.Framework;
using Palaso.Reporting;
using Palaso.TestUtilities;
using Palaso.Xml;

namespace BloomTests.Book
{
	[TestFixture]
	public class TranslationGroupManagerTests
	{
		private Mock<CollectionSettings> _collectionSettings;

		[SetUp]
		public void Setup()
		{
			_collectionSettings = new Moq.Mock<CollectionSettings>();
			_collectionSettings.SetupGet(x => x.IsSourceCollection).Returns(false);
			_collectionSettings.SetupGet(x => x.Language1Iso639Code).Returns("xyz");
			_collectionSettings.SetupGet(x => x.Language2Iso639Code).Returns("fr");
			_collectionSettings.SetupGet(x => x.Language3Iso639Code).Returns("es");
			_collectionSettings.SetupGet(x => x.XMatterPackName).Returns("Factory");
			ErrorReport.IsOkToInteractWithUser = false;
		}

		[Test]
		public void UpdateContentLanguageClasses_TrilingualBook_AddsBloomTrilingualClassToTranslationGroup()
		{
			var contents = @"<div class='bloom-page  bloom-bilingual'>
						<div class='bloom-translationGroup'>
							<textarea lang='en'></textarea>
							</div>
						</div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);
			var pageDiv = (XmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0];
			TranslationGroupManager.UpdateContentLanguageClasses(pageDiv, "xyz", _collectionSettings.Object.Language2Iso639Code, _collectionSettings.Object.Language3Iso639Code, "222", "333");
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-bilingual')]", 0);//should remove that one
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-trilingual')]", 1);
		}



		[Test]
		public void PrepareElementsOnPage_HasNonEditableDiv_LeavesAlone()
		{
			var contents = @"<div class='bloom-page'>
						<table class='bloom-translationGroup'> <!-- table is used for vertical alignment of the div on some pages -->
						 <td>
							<div lang='en'>This is some English</div>
						</td>
						</table>
					</div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);

			TranslationGroupManager.PrepareElementsInPageOrDocument((XmlElement)dom.SafeSelectNodes("//div[@class='bloom-page']")[0], _collectionSettings.Object);

			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//td/div", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//td/div[@lang='en']", 1);
		}

		[Test]
		public void PrepareElementsOnPage_HasTextAreaInsideTranslationGroup_MakesVernacularAndNational()
		{
			var contents = @"<div class='bloom-page bloom-translationGroup'>
						<textarea lang='en'>This is some English</textarea>
					</div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);

			TranslationGroupManager.PrepareElementsInPageOrDocument((XmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0], _collectionSettings.Object);

			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea", 4);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='en']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='fr']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='es']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='xyz']", 1);
		}

		/// <summary>
		/// this is an abnormal situation, but I did see it (there was another problem with V lang id)
		/// The key challenge for the code is, there's no prototype div to copy.
		/// </summary>
		[Test]
		public void PrepareElementsOnPage_HasEmptyTranslationGroup_MakesVernacularAndNational()
		{
			var contents = @"<div class='bloom-page bloom-translationGroup'>
					</div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);

			TranslationGroupManager.PrepareElementsInPageOrDocument((XmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0], _collectionSettings.Object);

			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div/div[contains(@class, 'bloom-editable') and @contenteditable='true' ]", 3);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='xyz']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='es']", 1);
		}

		[Test]
		public void UpdateContentLanguageClasses_NewPage_AddsContentLanguageClasses()
		{
			var contents = @"<div class='bloom-page'>
						<div class='bloom-translationGroup'>
							<textarea lang='en'></textarea>
							<textarea lang='222'></textarea>
							<textarea lang='333'></textarea>
							</div>
						</div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);
			var pageDiv = (XmlElement)dom.SafeSelectNodes("//div[@class='bloom-page']")[0];
			TranslationGroupManager.UpdateContentLanguageClasses(pageDiv, "xyz", _collectionSettings.Object.Language2Iso639Code, _collectionSettings.Object.Language3Iso639Code, "222", "333");
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='222' and contains(@class, 'bloom-content2')]", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='333' and contains(@class, 'bloom-content3')]", 1);
		}


		[Test]
		public void UpdateContentLanguageClasses_FrontMatterPage_AddsNationalLanguageClasses()
		{
			var contents = @"<div class='bloom-page bloom-frontMatter'>
						<div class='bloom-translationGroup'>
							<textarea lang='en'></textarea>
							<textarea lang='fr'></textarea>
							<textarea lang='es'></textarea>
							</div>
						</div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);
			var pageDiv = (XmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0];
			TranslationGroupManager.UpdateContentLanguageClasses(pageDiv, "xyz", _collectionSettings.Object.Language2Iso639Code, _collectionSettings.Object.Language3Iso639Code, "222", "333");
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='fr' and contains(@class, 'bloom-contentNational1')]", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='es' and contains(@class, 'bloom-contentNational2')]", 1);
		}

		[Test]
		public void UpdateContentLanguageClasses_ExistingPageWith3rdLangRemoved_RemovesContentLanguageClassButLeavesOtherClasses()
		{
			var contents = @"<div class='bloom-page'>
						<div class='bloom-translationGroup'>
							<textarea lang='en'></textarea>
							<textarea class='bloom-content2' lang='222'></textarea>
							<textarea  class='foo bloom-content3 bar' lang='333'></textarea>
							</div>
						</div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);
			var pageDiv = (XmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0];
			TranslationGroupManager.UpdateContentLanguageClasses(pageDiv, "xyz", _collectionSettings.Object.Language2Iso639Code, _collectionSettings.Object.Language3Iso639Code, "222", null);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='222' and contains(@class, 'bloom-content2')]", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='333' and contains(@class, 'bloom-content3')]", 0);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='333' and contains(@class, 'foo')]", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='333' and contains(@class, 'bar')]", 1);
		}


		[Test]
		public void UpdateContentLanguageClasses_MonoLingualBook_AddsBloomMonolingualClassToTranslationGroup()
		{
			var contents = @"<div class='bloom-page bloom-bilingual'>
						<div class='bloom-translationGroup'>
							<textarea lang='en'></textarea>
							</div>
						</div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);
			var pageDiv = (XmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0];
			TranslationGroupManager.UpdateContentLanguageClasses(pageDiv, "xyz", _collectionSettings.Object.Language2Iso639Code, _collectionSettings.Object.Language3Iso639Code, null, null);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-bilingual')]", 0);//should remove that one
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-monolingual')]", 1);
		}

		[Test]
		public void UpdateContentLanguageClasses_BilingualBook_AddsBloomBilingualClassToTranslationGroup()
		{
			var contents = @"<div class='bloom-page  bloom-trilingual'>
						<div class='bloom-translationGroup'>
							<textarea lang='en'></textarea>
							</div>
						</div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);
			var pageDiv = (XmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0];
			TranslationGroupManager.UpdateContentLanguageClasses(pageDiv, "xyz", _collectionSettings.Object.Language2Iso639Code, _collectionSettings.Object.Language3Iso639Code, "222", null);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-monolingual')]", 0);//should remove that one
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-bilingual')]", 1);
		}
	}
}
