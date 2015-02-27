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
			TranslationGroupManager.UpdateContentLanguageClasses(pageDiv, _collectionSettings.Object, "xyz", "222", "333");
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
			TranslationGroupManager.UpdateContentLanguageClasses(pageDiv, _collectionSettings.Object, "xyz", "222", "333");
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
			TranslationGroupManager.UpdateContentLanguageClasses(pageDiv, _collectionSettings.Object, "xyz", "222", "333");
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
			TranslationGroupManager.UpdateContentLanguageClasses(pageDiv, _collectionSettings.Object, "xyz", "222", null);
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
			TranslationGroupManager.UpdateContentLanguageClasses(pageDiv, _collectionSettings.Object, "xyz", null, null);
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
			TranslationGroupManager.UpdateContentLanguageClasses(pageDiv, _collectionSettings.Object, "xyz", "222", null);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-monolingual')]", 0);//should remove that one
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-bilingual')]", 1);
		}

		/// <summary>
		/// the editmode.css rule that lets us simulate the HTML5 "placeholder" attribute with a "data-placeholder"
		/// cannot reach up to the partent of the div, so it needs to be on the prototype child. But since the prototype these days is usually lang 'x' it is getting
		/// deleted before we can make use of it. So more now, we are putting the data-placeholder on the partent and make sure we copy it to children.
		/// </summary>
		[Test]
		public void UpdateContentLanguageClasses_TranslationGroupHasPlaceHolder_PlaceholderCopiedToNewChildren()
		{
			var contents = @"<div class='bloom-page  bloom-trilingual'>
								<div class='bloom-translationGroup' data-placeholder='copy me' >
								</div>
						</div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);

			TranslationGroupManager.PrepareElementsInPageOrDocument((XmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0], _collectionSettings.Object);

			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div/div[contains(@class, 'bloom-editable') and @contenteditable='true' ]", 3);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='xyz' and @data-placeholder='copy me']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr' and @data-placeholder='copy me']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='es' and @data-placeholder='copy me']", 1);
		}

		[Test]
		public void PrepareElementsOnPage_HasLabelElementInsideTranslationGroup_LeavesUntouched()
		{
			var contents = @"<div class='bloom-page bloom-translationGroup'>
						<label class='bloom-bubble'>something helpful</label>
					</div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);

			TranslationGroupManager.PrepareElementsInPageOrDocument((XmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0], _collectionSettings.Object);

			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//label[@class='bloom-bubble']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//label[@class='bloom-bubble' and text()='something helpful']", 1);
		}

		[Test]
		public void UpdateContentLanguageClasses_PrototypeElementHasImageContainer_ImageContainerCopiedToNewSybling()
		{
			const string contents = @"<div class='bloom-page'>
										<div class='bloom-translationGroup'>
											<div class='bloom-editable' lang='123'>
												Do not copy me.
												<br>Do not copy me.</br>
												<p>Do not copy me.</p>
												<div class='bloom-imageContainer'>
													<img src='foo.png'></img>
													<div contentEditable='true' class='caption'>Do not copy me</div>
												</div>
												Do not copy me.
												<p>Do not copy me.</p>
												<p class='foo bloom-cloneToOtherLanguages bar'>Do copy me.</p>
											</div>
										</div>
									</div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);

			TranslationGroupManager.PrepareElementsInPageOrDocument((XmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0], _collectionSettings.Object);

			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr']/div/img[@src='foo.png']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr']/div/div[@contentEditable='true']", 1);
			//should clear out the caption text (using a raw contentEditable for the caption just becuase bloom-editables inside of bloom-editables is beyond my ambitions at the moment.
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr']/div/div[contains(@class,'caption') and @contentEditable='true' and not(text())]", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr']/*[contains(text(),'Do not')]", 0);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr']//br", 0);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr']//p[not(contains(@class,'bloom-cloneToOtherLanguages'))]", 0); // get rid of all paragraphs, except for...
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr']//p[contains(@class,'bloom-cloneToOtherLanguages')]", 1); // the one with "bloom-cloneToOtherLanguages"
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr']/*[contains(text(),'Do copy me')]", 1);
		}
	}
}
