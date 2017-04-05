﻿using System.Collections.Generic;
using System.Xml;
using Bloom.Book;
using Bloom.Collection;
using Moq;
using NUnit.Framework;
using SIL.Reporting;
using SIL.TestUtilities;
using SIL.Xml;

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

		/// <summary>
		/// This is the normal shell book case. PrepareElementsOnPage should create 3 empty divs, one for
		/// each of the languages in the test settings. The 2 existing divs should still contain their text,
		/// but the new ones should remain empty.
		/// </summary>
		[Test]
		public void PrepareElementsOnPage_FromShell_MakesVernacularAndNationalEmpty()
		{
			var contents = @"<div class='bloom-page numberedPage A5Portrait bloom-monolingual'
							id='f4a22289-1755-4b79-afc1-5d20eaa892fe'>
<div class='marginBox'>
  <div class='bloom-imageContainer'>
	<img alt='' src='test.png' height='20' width='20'/>
  </div>
  <div class='bloom-translationGroup normal-style'>
	<div style='' class='bloom-editable' contenteditable='true'
		lang='en'>The Mother said, Nurse!
			The Nurse answered.</div>
	<div style='' class='bloom-editable' contenteditable='true'
		lang='tpi'>Mama i tok: Sista.
			Sista i tok: Bai mi stori long yu.</div>
  </div>
</div></div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);

			TranslationGroupManager.PrepareElementsInPageOrDocument((XmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0], _collectionSettings.Object);

			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div/div[contains(@class, 'bloom-editable') and @contenteditable='true' ]", 5);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='xyz']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='es']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and contains(., 'The Mother')]", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='tpi' and contains(., 'Mama i tok')]", 1);
			AssertThatXmlIn.Dom(dom).HasNoMatchForXpath("//div[@lang='xyz' and contains(., 'The Mother')]");
			AssertThatXmlIn.Dom(dom).HasNoMatchForXpath("//div[@lang='xyz' and contains(., 'Mama i tok')]");
			AssertThatXmlIn.Dom(dom).HasNoMatchForXpath("//div[@lang='fr' and contains(., 'The Mother')]");
			AssertThatXmlIn.Dom(dom).HasNoMatchForXpath("//div[@lang='fr' and contains(., 'Mama i tok')]");
		}

		/// <summary>
		/// The logic is tested below, directly on ShouldNormallyShowEditable().
		/// Here we just need to test the mechanics of adding/removing attributes.
		/// </summary>
		[Test]
		public void UpdateContentLanguageClasses_Typical_MetadataPage_TurnsOnCorrectLanguages()
		{
			var contents = @"<div class='bloom-page' >
						<div class='bloom-translationGroup' data-default-languages='N1,N2'>
							<div class='bloom-editable' lang='xyz'></div>
							<div class='bloom-editable' lang='fr'></div>
							<div class='bloom-editable' lang='es'></div>
							</div>
						</div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);
			var pageDiv = (XmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0];
			TranslationGroupManager.UpdateContentLanguageClasses(pageDiv, _collectionSettings.Object, "xyz", "222", "333");
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-visibility-code-on')]", 2);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr' and contains(@class, 'bloom-visibility-code-on')]", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='es' and contains(@class, 'bloom-visibility-code-on')]", 1);
		}

		[Test]
		public void UpdateContentLanguageClasses_TrilingualContentPage_TurnsOnCorrectLanguages()
		{
			var contents = @"<div class='bloom-page' >
						<div class='bloom-translationGroup'>
							<div class='bloom-editable' lang='xyz'></div>
							<div class='bloom-editable' lang='en'></div>
							<div class='bloom-editable' lang='fr'></div>
							<div class='bloom-editable' lang='es'></div>
							</div>
						</div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);
			var pageDiv = (XmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0];
			TranslationGroupManager.UpdateContentLanguageClasses(pageDiv, _collectionSettings.Object, "xyz", /* make trilingual --> */ "fr", "es");
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-visibility-code-on')]", 3);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr' and contains(@class, 'bloom-visibility-code-on')]", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='es' and contains(@class, 'bloom-visibility-code-on')]", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='xyz' and contains(@class, 'bloom-visibility-code-on')]", 1);
		}

		[Test]
		public void UpdateContentLanguageClasses_BilingualContentPage_TurnsOnCorrectLanguages()
		{
			var contents = @"<div class='bloom-page' >
						<div class='bloom-translationGroup'>
							<div class='bloom-editable' lang='xyz'></div>
							<div class='bloom-editable' lang='en'></div>
							<div class='bloom-editable' lang='fr'></div>
							<div class='bloom-editable' lang='es'></div>
							</div>
						</div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);
			var pageDiv = (XmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0];
			TranslationGroupManager.UpdateContentLanguageClasses(pageDiv, _collectionSettings.Object, "xyz", /* makes bilingual --> */ "fr", "");
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-visibility-code-on')]", 2);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr' and contains(@class, 'bloom-visibility-code-on')]", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='xyz' and contains(@class, 'bloom-visibility-code-on')]", 1);
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

		[Test]
		public void UpdateContentLanguageClasses_TrilingualBook_DataDefaultLanguageIsAuto_BloomContent2AndBloomContent3ClassesPresent()
		{
			var contents = @"<div class='bloom-page'>
						<div class='bloom-translationGroup' data-default-languages='auto'>
							<div class='bloom-editable' lang='xyz'></div>
							<div class='bloom-editable' lang='fr'></div>
							<div class='bloom-editable' lang='es'></div>
							</div>
						</div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);
			var pageDiv = (XmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0];
			TranslationGroupManager.UpdateContentLanguageClasses(pageDiv, _collectionSettings.Object, "xyz", "fr", "es");
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-content2')]", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-content3')]", 1);
		}

		[Test]
		public void UpdateContentLanguageClasses_BilingualBook_DataDefaultLanguageIsNational1_BloomContent2ClassNotPresent()
		{
			var contents = @"<div class='bloom-page'>
						<div class='bloom-translationGroup' data-default-languages='N1'>
							<div class='bloom-editable' lang='xyz'></div>
							<div class='bloom-editable' lang='fr'></div>
							<div class='bloom-editable' lang='es'></div>
							</div>
						</div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);
			var pageDiv = (XmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0];
			TranslationGroupManager.UpdateContentLanguageClasses(pageDiv, _collectionSettings.Object, "xyz", "fr", null);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-content2')]", 0);
		}

		[Test]
		public void UpdateContentLanguageClasses_TrilingualBook_DataDefaultLanguageIsNational1_BloomContent2ClassNotPresent()
		{
			var contents = @"<div class='bloom-page'>
						<div class='bloom-translationGroup' data-default-languages='N1'>
							<div class='bloom-editable' lang='xyz'></div>
							<div class='bloom-editable' lang='fr'></div>
							<div class='bloom-editable' lang='es'></div>
							</div>
						</div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);
			var pageDiv = (XmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0];
			TranslationGroupManager.UpdateContentLanguageClasses(pageDiv, _collectionSettings.Object, "xyz", "fr", "es");
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-content2')]", 0);
		}

		[Test]
		public void UpdateContentLanguageClasses_TrilingualBook_DataDefaultLanguageIsNational2_BloomContent3ClassNotPresent()
		{
			var contents = @"<div class='bloom-page'>
						<div class='bloom-translationGroup' data-default-languages='N2'>
							<div class='bloom-editable' lang='xyz'></div>
							<div class='bloom-editable' lang='fr'></div>
							<div class='bloom-editable' lang='es'></div>
							</div>
						</div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);
			var pageDiv = (XmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0];
			TranslationGroupManager.UpdateContentLanguageClasses(pageDiv, _collectionSettings.Object, "xyz", "fr", "es");
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-content3')]", 0);
		}

		/// <summary>
		/// the editmode.css rule that lets us simulate the HTML5 "placeholder" attribute with a "data-placeholder"
		/// cannot reach up to the partent of the div, so it needs to be on the prototype child. But since the prototype these days is usually lang 'x' it is getting
		/// deleted before we can make use of it. So more now, we are putting the data-placeholder on the partent and make sure we copy it to children.
		/// </summary>
		[Test]
		public void PrepareElementsInPageOrDocument_TranslationGroupHasPlaceHolder_PlaceholderCopiedToNewChildren()
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
		public void PrepareElementsInPageOrDocument_HasLabelElementInsideTranslationGroup_LeavesUntouched()
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
		public void PrepareElementsInPageOrDocument_PrototypeHasUnderlinedText_CopyHasNone()
		{
			var contents = @"<div class='bloom-page numberedPage A5Portrait bloom-monolingual'
							id='f4a22289-1755-4b79-afc1-5d20eaa892fe'>
							<div class='marginBox'>
							  <div class='bloom-translationGroup normal-style'>
								<div style='' class='bloom-editable' contenteditable='true'
									lang='en'>The <i>Mother</i> said, <u>Nurse!</u>
										The Nurse <b>answered</b>.</div>
							</div></div></div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);

			TranslationGroupManager.PrepareElementsInPageOrDocument((XmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0], _collectionSettings.Object);

			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='xyz']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='es']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and contains(., 'The Mother')]", 1);
			AssertThatXmlIn.Dom(dom).HasNoMatchForXpath("//div[@lang='fr']/u");
			AssertThatXmlIn.Dom(dom).HasNoMatchForXpath("//div[@lang='fr']/b");
			AssertThatXmlIn.Dom(dom).HasNoMatchForXpath("//div[@lang='fr']/i");
		}

		[Test]
		public void PrepareElementsInPageOrDocument_PrototypeElementHasImageContainer_ImageContainerCopiedToNewSibling()
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

			//the added french should have all the structure including a copy of the image container div, but none of the text except from the bloom-cloneToOtherLanguages paragraph
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

		[Test]
		public void PrepareDataBookTranslationGroups_PlaceholdersCreatedAsNeeded()
		{
			var contents = @"<div class='bloom-page'>
								<div class='bloom-translationGroup'>
										<div class='bloom-editable' data-book='bookTitle' lang='en'>Some English</div>
								</div>
						</div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);

			var languages = new string[] {"en","es","fr"};
			TranslationGroupManager.PrepareDataBookTranslationGroups((XmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0], languages);

			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div/div[contains(@class, 'bloom-editable')]", 3);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='es']", 1);
			//should touch the existing one
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='en' and text()='Some English']", 1);
		}

		[Test]
		public void ShouldNormallyShowEditable_SituationsWhereVernacularShouldBeShown()
		{
			Assert.IsTrue(TranslationGroupManager.ShouldNormallyShowEditable("xyz", new[] {"V"}, "", "", _collectionSettings.Object),
				"The data-default-languages calls for the vernacular ");
			Assert.IsTrue(TranslationGroupManager.ShouldNormallyShowEditable("xyz", new[] { "N1","V" }, "", "", _collectionSettings.Object),
				"The data-default-languages calls for the vernacular ");
			Assert.IsTrue(TranslationGroupManager.ShouldNormallyShowEditable("xyz", new string[] {}, "", "", _collectionSettings.Object),
				"The data-default-languages is empty, so should default to 'auto', which always includes vernacular ");

			Assert.IsTrue(TranslationGroupManager.ShouldNormallyShowEditable("xyz", new[] { "" }, "", "", _collectionSettings.Object),
				"The data-default-languages is empty, so should default to 'auto', which always includes vernacular ");
			Assert.IsTrue(TranslationGroupManager.ShouldNormallyShowEditable("xyz", new[] { "auto" }, "", "", _collectionSettings.Object),
				"The data-default-languages is auto, which always includes vernacular ");
			Assert.IsTrue(TranslationGroupManager.ShouldNormallyShowEditable("xyz", new[] { "AUTO" }, "", "", _collectionSettings.Object),
				"The data-default-languages is AUTO, which always includes vernacular ");
		}

		[Test]
		public void ShouldNormallyShowEditable_SituationsWhereVernacularShouldNotBeShown()
		{
			Assert.IsFalse(TranslationGroupManager.ShouldNormallyShowEditable("xyz", new[] { "N1" }, "", "", _collectionSettings.Object),
				"The data-default-languages calls for the vernacular ");
			Assert.IsFalse(TranslationGroupManager.ShouldNormallyShowEditable("xyz", new[] { "N2" }, "", "", _collectionSettings.Object),
				"The data-default-languages calls for the vernacular ");
		}

		[Test]
		public void ShouldNormallyShowEditable_SituationsWhereFirstNationalLanguageShouldNotBeShown()
		{
			Assert.IsFalse(TranslationGroupManager.ShouldNormallyShowEditable("fr", new[] { "V" }, "fr", "", _collectionSettings.Object),
				"The data-default-languages calls only for the vernacular ");
			Assert.IsFalse(TranslationGroupManager.ShouldNormallyShowEditable("fr", new[] { "V" }, "fr", "", _collectionSettings.Object),
				"The data-default-languages calls only for the vernacular ");
			Assert.IsFalse(TranslationGroupManager.ShouldNormallyShowEditable("fr", new[] { "N2" }, "", "", _collectionSettings.Object),
				"The data-default-languages calls for the second national language only ");
			Assert.IsFalse(TranslationGroupManager.ShouldNormallyShowEditable("fr", new[] { "auto" }, "es", "", _collectionSettings.Object),
				"Bilingual, the second language is set to the second national language.");
		}

		[Test]
		public void ShouldNormallyShowEditable_SituationsWhereNationalLanguagesShouldBeShown()
		{
			Assert.IsTrue(TranslationGroupManager.ShouldNormallyShowEditable("fr", new[] { "V","N1" }, "", "", _collectionSettings.Object),
				"The data-default-languages calls for the vernacular and n1");
			Assert.IsTrue(TranslationGroupManager.ShouldNormallyShowEditable("fr", new[] { "N1" }, "", "", _collectionSettings.Object),
				"The data-default-languages calls for the n1; it should show.");
			Assert.IsTrue(TranslationGroupManager.ShouldNormallyShowEditable("fr", new[] { "N1","N2" }, "", "", _collectionSettings.Object),
				"The data-default-languages calls for both national languages");
			Assert.IsTrue(TranslationGroupManager.ShouldNormallyShowEditable("es", new[] { "N1", "N2" }, "", "", _collectionSettings.Object),
				"The data-default-languages calls for both national languages");
			Assert.IsTrue(TranslationGroupManager.ShouldNormallyShowEditable("fr", new[] { "auto" }, "fr", "es", _collectionSettings.Object),
				"Auto and Trilingual, so should show all three languages.");
			Assert.IsTrue(TranslationGroupManager.ShouldNormallyShowEditable("es", new[] { "auto" }, "fr", "es", _collectionSettings.Object),
				"Auto and Trilingual, so should show all three languages.");
		}
	}
}
