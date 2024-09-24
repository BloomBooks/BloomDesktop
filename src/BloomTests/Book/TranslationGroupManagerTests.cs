using System.Collections.Generic;
using System.Xml;
using Bloom.Book;
using Bloom.Collection;
using Bloom.SafeXml;
using L10NSharp;
using NUnit.Framework;
using SIL.IO;
using SIL.Reporting;

namespace BloomTests.Book
{
    [TestFixture]
    public class TranslationGroupManagerTests
    {
        private CollectionSettings _collectionSettings;
        private ILocalizationManager _localizationManager;

        [SetUp]
        public void Setup()
        {
            _collectionSettings = new CollectionSettings
            {
                Language1Tag = "xyz",
                Language2Tag = "fr",
                Language3Tag = "es",
                XMatterPackName = "Factory"
            };
            ErrorReport.IsOkToInteractWithUser = false;

            LocalizationManager.UseLanguageCodeFolders = true;
            var localizationDirectory =
                FileLocationUtilities.GetDirectoryDistributedWithApplication(
                    "src/BloomTests/TestLocalization"
                );
            _localizationManager = LocalizationManager.Create(
                TranslationMemory.XLiff,
                "en",
                "Bloom",
                "Bloom",
                "1.0.0",
                localizationDirectory,
                "SIL/BloomTests",
                null,
                ""
            );
        }

        [TearDown]
        public void TearDown()
        {
            _localizationManager.Dispose();
            LocalizationManager.ForgetDisposedManagers();
        }

        [Test]
        public void UpdateContentLanguageClasses_TrilingualBook_AddsBloomTrilingualClassToTranslationGroup()
        {
            var contents =
                @"<html><body><div class='bloom-page  bloom-bilingual'>
						<div class='bloom-translationGroup'>
							<textarea lang='en'></textarea>
							</div>
						</div></body></html>";
            var dom = new HtmlDom(contents);
            var bookData = new BookData(dom, _collectionSettings, null);

            var pageDiv = (SafeXmlElement)
                dom.RawDom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0];
            TranslationGroupManager.UpdateContentLanguageClasses(
                pageDiv,
                bookData,
                LegacyAppearanceSettings,
                "xyz",
                "222",
                "333"
            );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'bloom-bilingual')]",
                    0
                ); //should remove that one
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'bloom-trilingual')]",
                    1
                );
        }

        [Test]
        public void PrepareElementsOnPage_HasNonEditableDiv_LeavesAlone()
        {
            var contents =
                @"<html><body><div class='bloom-page'>
						<table class='bloom-translationGroup'> <!-- table is used for vertical alignment of the div on some pages -->
						 <td>
							<div lang='en'>This is some English</div>
						</td>
						</table>
					</div></body></html>";
            var dom = new HtmlDom(contents);
            var bookData = new BookData(dom, _collectionSettings, null);

            TranslationGroupManager.PrepareElementsInPageOrDocument(
                (SafeXmlElement)dom.RawDom.SafeSelectNodes("//div[@class='bloom-page']")[0],
                bookData
            );

            AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//td/div", 1);
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//td/div[@lang='en']", 1);
        }

        [Test]
        public void PrepareElementsOnPage_HasTextAreaInsideTranslationGroup_MakesVernacularAndNationalAndL3()
        {
            var contents =
                @"<html><body><div class='bloom-page bloom-translationGroup'>
						<textarea lang='en'>This is some English</textarea>
					</div></body></html>";
            var dom = new HtmlDom(contents);
            var bookData = new BookData(dom, _collectionSettings, null);

            TranslationGroupManager.PrepareElementsInPageOrDocument(
                (SafeXmlElement)
                    dom.RawDom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0],
                bookData
            );

            AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//textarea", 4);
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='en']", 1);
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='fr']", 1);
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='es']", 1);
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='xyz']", 1);
        }

        /// <summary>
        /// this is an abnormal situation, but I did see it (there was another problem with V lang id)
        /// The key challenge for the code is, there's no prototype div to copy.
        /// </summary>
        [Test]
        public void PrepareElementsOnPage_HasEmptyTranslationGroup_MakesVernacularAndNationalAndL3()
        {
            var contents =
                @"<html><body><div class='bloom-page bloom-translationGroup'>
					</div></body></html>";
            var dom = new HtmlDom(contents);
            var bookData = new BookData(dom, _collectionSettings, null);

            TranslationGroupManager.PrepareElementsInPageOrDocument(
                (SafeXmlElement)
                    dom.RawDom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0],
                bookData
            );

            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div/div[contains(@class, 'bloom-editable') and @contenteditable='true' ]",
                    3
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div/div[contains(@class, 'normal-style') and contains(@class, 'bloom-editable')]",
                    3
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'normal-style') and contains(@class, 'bloom-translationGroup')]",
                    0
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@lang='xyz']", 1);
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr']", 1);
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@lang='es']", 1);
        }

        /// <summary>
        /// This is the normal shell book case. PrepareElementsOnPage should create 2 empty divs, one for
        /// each of the active languages in the test settings (we're not setting a third in the BookData here).
        /// The 2 existing divs should still contain their text,
        /// but the new ones should remain empty.
        /// </summary>
        [Test]
        public void PrepareElementsOnPage_FromShell_MakesVernacularAndNationalAndL3Empty()
        {
            var contents =
                @"<html><body><div class='bloom-page numberedPage A5Portrait bloom-monolingual'
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
</div></div></body></html>";
            var dom = new HtmlDom(contents);
            var bookData = new BookData(dom, _collectionSettings, null);

            TranslationGroupManager.PrepareElementsInPageOrDocument(
                (SafeXmlElement)
                    dom.RawDom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0],
                bookData
            );

            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div/div[contains(@class, 'bloom-editable') and @contenteditable='true' ]",
                    5
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div/div[contains(@class, 'normal-style') and contains(@class, 'bloom-editable')]",
                    5
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'normal-style') and contains(@class, 'bloom-translationGroup')]",
                    0
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@lang='xyz']", 1);
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr']", 1);
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@lang='es']", 1);
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='en' and contains(., 'The Mother')]",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='tpi' and contains(., 'Mama i tok')]",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasNoMatchForXpath("//div[@lang='xyz' and contains(., 'The Mother')]");
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasNoMatchForXpath("//div[@lang='xyz' and contains(., 'Mama i tok')]");
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasNoMatchForXpath("//div[@lang='fr' and contains(., 'The Mother')]");
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasNoMatchForXpath("//div[@lang='fr' and contains(., 'Mama i tok')]");
        }

        // Currently a brand new AppearanceSettiings object is in legacy mode, and the code we're testing
        // won't ask it anything once it determines we are in that mode.
        private AppearanceSettings LegacyAppearanceSettings => new AppearanceSettings();

        /// <summary>
        /// The logic is tested below, directly on ShouldNormallyShowEditable().
        /// Here we just need to test the mechanics of adding/removing attributes.
        /// </summary>
        [Test]
        public void UpdateContentLanguageClasses_Typical_MetadataPage_TurnsOnCorrectLanguages()
        {
            var contents =
                @"<html><body><div class='bloom-page' >
						<div class='bloom-translationGroup' data-default-languages='N1,N2'>
							<div class='bloom-editable' lang='xyz'></div>
							<div class='bloom-editable' lang='fr'></div>
							<div class='bloom-editable' lang='es'></div>
							</div>
						</div></body></html>";
            var dom = new HtmlDom(contents);
            var bookData = new BookData(dom, _collectionSettings, null);

            var pageDiv = (SafeXmlElement)
                dom.RawDom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0];

            // Here the arguments don't matter much. data-default-languages specifies N1, which means the Metadata1Language should
            // be visible, and N2, which currently turns on L3.
            TranslationGroupManager.UpdateContentLanguageClasses(
                pageDiv,
                bookData,
                LegacyAppearanceSettings,
                "xyz",
                "222",
                "333"
            );

            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'bloom-visibility-code-on')]",
                    2
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='fr' and contains(@class, 'bloom-visibility-code-on')]",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='es' and contains(@class, 'bloom-visibility-code-on')]",
                    1
                );
        }

        [Test]
        public void UpdateContentLanguageClasses_Legacy_TurnsOnCorrectLanguages()
        {
            var contents =
                @"<html><body><div class='bloom-page' >
						<div class='bloom-translationGroup' data-default-languages='N1,N2' data-visibility-variable='cover-title-LN-show'>
							<div class='bloom-editable' lang='xyz'></div>
							<div class='bloom-editable' lang='fr'></div>
							<div class='bloom-editable' lang='es'></div>
							</div>
						</div></body></html>";
            var dom = new HtmlDom(contents);
            var bookData = new BookData(dom, _collectionSettings, null);

            var pageDiv = (SafeXmlElement)
                dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0];

            // Here the arguments don't matter much. data-default-languages specifies N1, which means the Metadata1Language should
            // be visible, and N2, which currently turns on L3.
            TranslationGroupManager.UpdateContentLanguageClasses(
                pageDiv,
                bookData,
                LegacyAppearanceSettings,
                "xyz",
                "222",
                "333"
            );

            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'bloom-visibility-code-on')]",
                    2
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='fr' and contains(@class, 'bloom-visibility-code-on')]",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='es' and contains(@class, 'bloom-visibility-code-on')]",
                    1
                );
        }

        [Test]
        public void UpdateContentLanguageClasses_LegacyDataDefault_NotLegacy_SetsVisibilityCodeFromSettings()
        {
            var contentsPattern =
                @"<html><body><div class='bloom-page' >
						<div class='bloom-translationGroup' data-default-languages='{0}' data-visibility-variable='cover-title-LN-show'>
							<div class='bloom-editable' lang='xyz'></div>
							<div class='bloom-editable' lang='fr'></div>
							<div class='bloom-editable' lang='es'></div>
                            <div class='bloom-editable' lang='z'></div>
                            <div class='bloom-editable' lang='qed'></div>
							</div>
						</div></body></html>";
            var contents = string.Format(contentsPattern, "V,N1");
            var dom = new HtmlDom(contents);
            var bookData = new BookData(dom, _collectionSettings, null);
            // Note that we deliberately don't do this: we leave bookData.Language2Tag and Language3Tag null.
            // The appearance system variables called L2 and L3 refer to Metadata1Language and Metadata2Language.
            //bookData.SetMultilingualContentLanguages("xyz", "fr", "es");

            var pageDiv = (SafeXmlElement)
                dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0];

            var settings = new AppearanceSettings();
            settings.UpdateFromJson(
                @"{
  ""cssThemeName"": ""default"",
  ""cover-title-L2-show"": false,
  ""cover-title-L3-show"": true,
  ""cover-topic-show"": true,
  ""cover-languageName-show"": true,
  ""pageNumber-show"": true
}"
            );
            settings._areSettingsConsistentWithFiles = true; // needs to be set to allow the default theme to be used

            // Here the arguments don't matter much. data-default-languages specifies N1, which means the Metadata1Language should
            // be visible, and N2, which currently turns on L3.
            TranslationGroupManager.UpdateContentLanguageClasses(
                pageDiv,
                bookData,
                settings,
                "xyz",
                "fr",
                "es"
            );

            // The settings say that L3 should be visible, so it should be on
            // The settings have nothing to say about L1, so it should be on based on V being in data-default-languages
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'bloom-visibility-code-on')]",
                    2
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='es' and contains(@class, 'bloom-visibility-code-on')]",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='xyz' and contains(@class, 'bloom-visibility-code-on')]",
                    1
                );

            // Try it again with V turned off. This was originally a way to test
            // that the data-default-languages is used as a fall-back when the settings don't specify.
            // Now we have settings for all three languages so that fall-back is never used.
            // I just updated the test to the new behavior.
            contents = string.Format(contentsPattern, "N1,N2");
            dom.RawDom.LoadXml(contents);
            pageDiv = (SafeXmlElement)
                dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0];
            settings.UpdateFromJson(
                @"{
  ""cssThemeName"": ""default"",
  ""cover-title-L2-show"": true,
  ""cover-title-L3-show"": false,
  ""cover-topic-show"": true,
  ""cover-languageName-show"": true,
  ""pageNumber-show"": true
}"
            );

            // Here the arguments don't matter much. data-default-languages specifies N1, which means the Metadata1Language should
            // be visible, and N2, which currently turns on L3.
            TranslationGroupManager.UpdateContentLanguageClasses(
                pageDiv,
                bookData,
                settings,
                "xyz",
                "fr",
                "es"
            );

            // The settings say that L2 should be visible, so it should be on.
            // By default they also say L1 should be.
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'bloom-visibility-code-on')]",
                    2
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='fr' and contains(@class, 'bloom-visibility-code-on')]",
                    1
                );
        }

        [Test]
        public void UpdateContentLanguageClasses_TrilingualContentPage_TurnsOnCorrectLanguages()
        {
            var contents =
                @"<html><body><div class='bloom-page' >
						<div class='bloom-translationGroup'>
							<div class='bloom-editable' lang='xyz'></div>
							<div class='bloom-editable' lang='en'></div>
							<div class='bloom-editable' lang='fr'></div>
							<div class='bloom-editable' lang='es'></div>
							</div>
						</div></body></html>";
            var dom = new HtmlDom(contents);
            var bookData = new BookData(dom, _collectionSettings, null);
            var pageDiv = (SafeXmlElement)
                dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0];
            TranslationGroupManager.UpdateContentLanguageClasses(
                pageDiv,
                bookData,
                LegacyAppearanceSettings,
                "xyz", /* make trilingual --> */
                "fr",
                "es"
            );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'bloom-visibility-code-on')]",
                    3
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='fr' and contains(@class, 'bloom-visibility-code-on')]",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='es' and contains(@class, 'bloom-visibility-code-on')]",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='xyz' and contains(@class, 'bloom-visibility-code-on')]",
                    1
                );
        }

        [Test]
        public void UpdateContentLanguageClasses_BilingualContentPage_TurnsOnCorrectLanguages()
        {
            var contents =
                @"<html><body><div class='bloom-page' >
						<div class='bloom-translationGroup'>
							<div class='bloom-editable' lang='xyz'></div>
							<div class='bloom-editable' lang='en'></div>
							<div class='bloom-editable' lang='fr'></div>
							<div class='bloom-editable' lang='es'></div>
							</div>
						</div></body></html>";
            var dom = new HtmlDom(contents);
            var bookData = new BookData(dom, _collectionSettings, null);
            var pageDiv = (SafeXmlElement)
                dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0];
            TranslationGroupManager.UpdateContentLanguageClasses(
                pageDiv,
                bookData,
                LegacyAppearanceSettings,
                "xyz", /* makes bilingual --> */
                "fr",
                ""
            );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'bloom-visibility-code-on')]",
                    2
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='fr' and contains(@class, 'bloom-visibility-code-on')]",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='xyz' and contains(@class, 'bloom-visibility-code-on')]",
                    1
                );
        }

        [Test]
        public void UpdateContentLanguageClasses_MonoLingualBook_AddsBloomMonolingualClassToTranslationGroup()
        {
            var contents =
                @"<html><body><div class='bloom-page bloom-bilingual'>
						<div class='bloom-translationGroup'>
							<textarea lang='en'></textarea>
							</div>
						</div></body></html>";
            var dom = new HtmlDom(contents);
            var bookData = new BookData(dom, _collectionSettings, null);
            var pageDiv = (SafeXmlElement)
                dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0];
            TranslationGroupManager.UpdateContentLanguageClasses(
                pageDiv,
                bookData,
                LegacyAppearanceSettings,
                "xyz",
                null,
                null
            );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'bloom-bilingual')]",
                    0
                ); //should remove that one
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'bloom-monolingual')]",
                    1
                );
        }

        [Test]
        public void UpdateContentLanguageClasses_BilingualBook_AddsBloomBilingualClassToTranslationGroup()
        {
            var contents =
                @"<html><body><div class='bloom-page  bloom-trilingual'>
						<div class='bloom-translationGroup'>
							<textarea lang='en'></textarea>
							</div>
						</div></body></html>";
            var dom = new HtmlDom(contents);
            var bookData = new BookData(dom, _collectionSettings, null);
            var pageDiv = (SafeXmlElement)
                dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0];
            TranslationGroupManager.UpdateContentLanguageClasses(
                pageDiv,
                bookData,
                LegacyAppearanceSettings,
                "xyz",
                "222",
                null
            );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'bloom-monolingual')]",
                    0
                ); //should remove that one
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'bloom-bilingual')]",
                    1
                );
        }

        /// <summary>
        /// Checks that elements in bloomDataDiv are updated too
        /// </summary>

        [Test]
        public void UpdateContentLanguageClasses_BloomDataDiv_ElementsAreUpdatedToo()
        {
            // Setup
            var contents =
                @"<body>
	<div id='bloomDataDiv'>
		<div data-book='styleNumberSequence' lang='*'>
			0
		</div>
		<div data-book='coverImageDescription' lang='*'>
			<div data-languagetipcontent='Portuguese' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable ImageDescriptionEdit-style bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='pt'>
				<p>An Image Description in a language that is not one of the languages of the book</p>
			</div>

			<div data-languagetipcontent='XYZ' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable ImageDescriptionEdit-style bloom-visibility-code-on' contenteditable='true' lang='xyz'>
				<p>An Image Description in Language 1</p>
			</div>

			<div style='' class='bloom-editable ImageDescriptionEdit-style' contenteditable='true' lang='z'>
				<p></p>
			</div>

			<div data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='' class='bloom-editable ImageDescriptionEdit-style bloom-contentNational1' contenteditable='true' lang='en'>
				<p></p>
			</div>
		</div>
		<!-- End of bloomDataDiv -->

		<div class='bloom-page  bloom-trilingual'>
			<div class='bloom-translationGroup'>
				<div data-languagetipcontent='Portuguese' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable ImageDescriptionEdit-style bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='pt'>
					<p>An Image Description in a language that is not one of the languages of the book</p>
				</div>
			</div>
			<div class='bloom-translationGroup'>
				<div data-languagetipcontent='XYZ' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable ImageDescriptionEdit-style bloom-visibility-code-on' contenteditable='true' lang='xyz'>
					<p>An Image Description in Language 1</p>
				</div>
			</div>
		</div>
	</div>
</body>";

            var dom = new HtmlDom(contents);
            var bookData = new BookData(dom, _collectionSettings, null);
            var bodyDiv = (SafeXmlElement)dom.SafeSelectNodes("//body")[0];

            // System under test
            TranslationGroupManager.UpdateContentLanguageClasses(
                bodyDiv,
                bookData,
                LegacyAppearanceSettings,
                "xyz",
                "222",
                null
            );

            // Verification
            var bloomDataDivImageDescription = (SafeXmlElement)
                dom.SafeSelectNodes(
                    "//div[@id='bloomDataDiv']/div[@data-book='coverImageDescription']/div[@lang='pt']"
                )[0];
            var classString = bloomDataDivImageDescription.GetAttribute("class");
            Assert.IsFalse(
                classString.Contains("bloom-content"),
                "Div should have out-of-date bloom-content1 class removed"
            );

            var pageNonL1ImageDescription = (SafeXmlElement)
                dom.SafeSelectNodes("//div[contains(@class, 'bloom-page')]/div/div[@lang='pt']")[0];
            classString = pageNonL1ImageDescription.GetAttribute("class");
            Assert.IsFalse(
                classString.Contains("bloom-content"),
                "Div should have out-of-date bloom-content1 class removed"
            );

            var pageL1ImageDescription = (SafeXmlElement)
                dom.SafeSelectNodes("//div[contains(@class, 'bloom-page')]/div/div[@lang='xyz']")[
                    0
                ];
            classString = pageL1ImageDescription.GetAttribute("class");
            Assert.IsTrue(
                classString.Contains("bloom-content1"),
                "L1 Div should have bloom-content1 class added"
            );

            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'bloom-content1')]",
                    2
                ); // Should remove content1 off the "pt" versions in the dataDiv and the page, but keep the "xyz" (Lang1) ones in both the dataDiv and page
        }

        /// <summary>
        /// the editmode.css rule that lets us simulate the HTML5 "placeholder" attribute with a "data-placeholder"
        /// cannot reach up to the partent of the div, so it needs to be on the prototype child. But since the prototype these days is usually lang 'x' it is getting
        /// deleted before we can make use of it. So more now, we are putting the data-placeholder on the partent and make sure we copy it to children.
        /// </summary>
        [Test]
        public void PrepareElementsInPageOrDocument_TranslationGroupHasPlaceHolder_PlaceholderCopiedToNewChildren()
        {
            var contents =
                @"<html><body><div class='bloom-page  bloom-trilingual'>
								<div class='bloom-translationGroup normal-style' data-placeholder='copy me' >
								</div>
						</div></body></html>";
            var dom = new HtmlDom(contents);
            var bookData = new BookData(dom, _collectionSettings, null);
            bookData.SetMultilingualContentLanguages("xyz", "fr", "es");

            TranslationGroupManager.PrepareElementsInPageOrDocument(
                (SafeXmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0],
                bookData
            );

            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div/div[contains(@class, 'bloom-editable') and @contenteditable='true' ]",
                    3
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div/div[contains(@class, 'normal-style') and contains(@class, 'bloom-editable')]",
                    3
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'normal-style') and contains(@class, 'bloom-translationGroup')]",
                    0
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='xyz' and @data-placeholder='copy me']",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='fr' and @data-placeholder='copy me']",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='es' and @data-placeholder='copy me']",
                    1
                );
        }

        [Test]
        public void PrepareElementsOnPage_HasLabelElementInsideTranslationGroup_LeavesUntouched()
        {
            var contents =
                @"<html><body><div class='bloom-page bloom-translationGroup'>
						<label class='bloom-bubble'>something helpful</label>
					</div></body></html>";
            var dom = new HtmlDom(contents);
            var bookData = new BookData(dom, _collectionSettings, null);

            TranslationGroupManager.PrepareElementsInPageOrDocument(
                (SafeXmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0],
                bookData
            );

            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//label[@class='bloom-bubble']", 1);
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//label[@class='bloom-bubble' and text()='something helpful']",
                    1
                );
        }

        [Test]
        public void UpdateContentLanguageClasses_PrototypeHasUnderlinedText_CopyHasNone()
        {
            var contents =
                @"<html><body><div class='bloom-page numberedPage A5Portrait bloom-monolingual'
							id='f4a22289-1755-4b79-afc1-5d20eaa892fe'>
							<div class='marginBox'>
							  <div class='bloom-translationGroup normal-style'>
								<div style='' class='bloom-editable plain-style' contenteditable='true'
									lang='en'>The <i>Mother</i> said, <u>Nurse!</u>
										The Nurse <b>answered</b>.</div>
							</div></div></div></body></html>";
            var dom = new HtmlDom(contents);
            var bookData = new BookData(dom, _collectionSettings, null);

            TranslationGroupManager.PrepareElementsInPageOrDocument(
                (SafeXmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0],
                bookData
            );

            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div/div[contains(@class, 'plain-style') and contains(@class, 'bloom-editable')]",
                    4
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'normal-style') and contains(@class, 'bloom-translationGroup')]",
                    0
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@lang='xyz']", 1);
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr']", 1);
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@lang='es']", 1);
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='en' and contains(., 'The Mother')]",
                    1
                );
            AssertThatXmlIn.Dom(dom.RawDom).HasNoMatchForXpath("//div[@lang='fr']/u");
            AssertThatXmlIn.Dom(dom.RawDom).HasNoMatchForXpath("//div[@lang='fr']/b");
            AssertThatXmlIn.Dom(dom.RawDom).HasNoMatchForXpath("//div[@lang='fr']/i");
        }

        [Test]
        public void UpdateContentLanguageClasses_PrototypeElementHasImageContainer_ImageContainerCopiedToNewSibling()
        {
            const string contents =
                @"<html><body><div class='bloom-page'>
										<div class='bloom-translationGroup normal-style'>
											<div class='bloom-editable plain-style' lang='123'>
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
									</div></body></html>";
            var dom = new HtmlDom(contents);
            var bookData = new BookData(dom, _collectionSettings, null);

            TranslationGroupManager.PrepareElementsInPageOrDocument(
                (SafeXmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0],
                bookData
            );

            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div/div[contains(@class, 'plain-style') and contains(@class, 'bloom-editable')]",
                    4
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'normal-style') and contains(@class, 'bloom-translationGroup')]",
                    0
                );
            //the added french should have all the structure including a copy of the image container div, but none of the text except from the bloom-cloneToOtherLanguages paragraph
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='fr']/div/img[@src='foo.png']",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='fr']/div/div[@contentEditable='true']",
                    1
                );
            //should clear out the caption text (using a raw contentEditable for the caption just becuase bloom-editables inside of bloom-editables is beyond my ambitions at the moment.
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='fr']/div/div[contains(@class,'caption') and @contentEditable='true' and not(text())]",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='fr']/*[contains(text(),'Do not')]",
                    0
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr']//br", 0);
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='fr']//p[not(contains(@class,'bloom-cloneToOtherLanguages'))]",
                    0
                ); // get rid of all paragraphs, except for...
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='fr']//p[contains(@class,'bloom-cloneToOtherLanguages')]",
                    1
                ); // the one with "bloom-cloneToOtherLanguages"
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='fr']/*[contains(text(),'Do copy me')]",
                    1
                );
        }

        [Test]
        public void PrepareDataBookTranslationGroups_PlaceholdersCreatedAsNeeded()
        {
            var contents =
                @"<html><body><div class='bloom-page'>
								<div class='bloom-translationGroup'>
										<div class='bloom-editable' data-book='bookTitle' lang='en'>Some English</div>
								</div>
						</div></body></html>";
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(contents);

            var languages = new string[] { "en", "es", "fr" };
            TranslationGroupManager.PrepareDataBookTranslationGroups(
                (SafeXmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0],
                languages
            );

            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div/div[contains(@class, 'bloom-editable')]",
                    3
                );
            AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='fr']", 1);
            AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@lang='es']", 1);
            //should touch the existing one
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@lang='en' and text()='Some English']",
                    1
                );
        }

        [Test]
        public void ShouldNormallyShowEditable_SituationsWhereVernacularShouldBeShown()
        {
            var bookData = new BookData(
                new HtmlDom("<html><body></body></html>"),
                _collectionSettings,
                null
            );
            Assert.IsTrue(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "xyz",
                    new[] { "V" },
                    "",
                    "",
                    bookData
                ),
                "The data-default-languages calls for the vernacular "
            );
            Assert.IsTrue(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "xyz",
                    new[] { "N1", "V" },
                    "",
                    "",
                    bookData
                ),
                "The data-default-languages calls for the vernacular "
            );
            Assert.IsTrue(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "xyz",
                    new string[] { },
                    "",
                    "",
                    bookData
                ),
                "The data-default-languages is empty, so should default to 'auto', which always includes vernacular "
            );

            Assert.IsTrue(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "xyz",
                    new[] { "" },
                    "",
                    "",
                    bookData
                ),
                "The data-default-languages is empty, so should default to 'auto', which always includes vernacular "
            );
            Assert.IsTrue(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "xyz",
                    new[] { "auto" },
                    "",
                    "",
                    bookData
                ),
                "The data-default-languages is auto, which always includes vernacular "
            );
            Assert.IsTrue(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "xyz",
                    new[] { "AUTO" },
                    "",
                    "",
                    bookData
                ),
                "The data-default-languages is AUTO, which always includes vernacular "
            );
        }

        [Test]
        public void ShouldNormallyShowEditable_SituationsWhereVernacularShouldNotBeShown()
        {
            var bookData = new BookData(
                new HtmlDom("<html><body></body></html>"),
                _collectionSettings,
                null
            );
            Assert.IsFalse(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "xyz",
                    new[] { "N1" },
                    "",
                    "",
                    bookData
                ),
                "The data-default-languages calls for the vernacular "
            );
            Assert.IsFalse(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "xyz",
                    new[] { "N2" },
                    "",
                    "",
                    bookData
                ),
                "The data-default-languages calls for the vernacular "
            );
        }

        [Test]
        public void ShouldNormallyShowEditable_SituationsWhereFirstNationalLanguageShouldNotBeShown()
        {
            var bookData = new BookData(
                new HtmlDom("<html><body></body></html>"),
                _collectionSettings,
                null
            );
            Assert.IsFalse(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "fr",
                    new[] { "V" },
                    "fr",
                    "",
                    bookData
                ),
                "The data-default-languages calls only for the vernacular "
            );
            Assert.IsFalse(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "fr",
                    new[] { "V" },
                    "fr",
                    "",
                    bookData
                ),
                "The data-default-languages calls only for the vernacular "
            );
            Assert.IsFalse(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "fr",
                    new[] { "N2" },
                    "",
                    "",
                    bookData
                ),
                "The data-default-languages calls for the second national language only "
            );
            Assert.IsFalse(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "fr",
                    new[] { "auto" },
                    "es",
                    "",
                    bookData
                ),
                "Bilingual, the second language is set to the second national language."
            );
        }

        [Test]
        public void ShouldNormallyShowEditable_SituationsWhereNationalLanguagesShouldBeShown()
        {
            var bookData = new BookData(
                new HtmlDom("<html><body></body></html>"),
                _collectionSettings,
                null
            );
            bookData.SetMultilingualContentLanguages("xyz", "fr", "es");
            Assert.IsTrue(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "fr",
                    new[] { "V", "N1" },
                    "",
                    "",
                    bookData
                ),
                "The data-default-languages calls for the vernacular and n1"
            );
            Assert.IsTrue(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "fr",
                    new[] { "N1" },
                    "",
                    "",
                    bookData
                ),
                "The data-default-languages calls for the n1; it should show."
            );
            Assert.IsTrue(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "fr",
                    new[] { "N1", "N2" },
                    "",
                    "",
                    bookData
                ),
                "The data-default-languages calls for both national languages"
            );
            Assert.IsTrue(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "es",
                    new[] { "N1", "N2" },
                    "",
                    "",
                    bookData
                ),
                "The data-default-languages calls for both national languages"
            );
            Assert.IsTrue(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "fr",
                    new[] { "auto" },
                    "fr",
                    "es",
                    bookData
                ),
                "Auto and Trilingual, so should show all three languages."
            );
            Assert.IsTrue(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "es",
                    new[] { "auto" },
                    "fr",
                    "es",
                    bookData
                ),
                "Auto and Trilingual, so should show all three languages."
            );
            bookData.SetMultilingualContentLanguages("xyz");
            Assert.IsTrue(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "fr",
                    new[] { "N1" },
                    "",
                    "",
                    bookData
                ),
                "The data-default-languages calls for the N1; it should show, even when not in the auto list."
            );
            Assert.IsFalse(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "es",
                    new[] { "N1" },
                    "",
                    "",
                    bookData
                ),
                "The data-default-languages calls for the N1; it should show, even when not in the auto list."
            );
            Assert.IsFalse(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "fr",
                    new[] { "N2" },
                    "",
                    "",
                    bookData
                ),
                "The data-default-languages calls for the N1; it should show, even when not in the auto list."
            );
            Assert.IsTrue(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "es",
                    new[] { "N2" },
                    "",
                    "",
                    bookData
                ),
                "The data-default-languages calls for the N1; it should show, even when not in the auto list."
            );
        }

        [Test]
        public void ShouldNormallyShowEditable_SituationsWhereNumberedLanguagesShouldBeShown()
        {
            var bookData = new BookData(
                new HtmlDom("<html><body></body></html>"),
                _collectionSettings,
                null
            );
            bookData.SetMultilingualContentLanguages("xyz", "fr", "es");
            Assert.IsTrue(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "xyz",
                    new[] { "L1" },
                    "",
                    "",
                    bookData
                ),
                "The data-default-languages calls for the L1"
            );
            Assert.IsTrue(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "fr",
                    new[] { "L2" },
                    "",
                    "",
                    bookData
                ),
                "The data-default-languages calls for the L2."
            );
            Assert.IsTrue(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "es",
                    new[] { "L3" },
                    "",
                    "",
                    bookData
                ),
                "The data-default-languages calls for the L3."
            );
            bookData.SetMultilingualContentLanguages("fr");
            Assert.IsFalse(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "xyz",
                    new[] { "L1" },
                    "",
                    "",
                    bookData
                ),
                "xyz is not L1"
            );
            Assert.IsTrue(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "fr",
                    new[] { "L1" },
                    "",
                    "",
                    bookData
                ),
                "French is L1, should show"
            );
            Assert.IsFalse(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "fr",
                    new[] { "L2" },
                    "",
                    "",
                    bookData
                ),
                "No language is currently L2."
            );
            Assert.IsFalse(
                TranslationGroupManager.ShouldNormallyShowEditable(
                    "es",
                    new[] { "L3" },
                    "",
                    "",
                    bookData
                ),
                "No language is currently L3."
            );
        }

        [Test]
        public void PrepareElementsInPageOrDocument_TransfersNormalStyleCorrectly()
        {
            var contents =
                @"<html><body><div class='bloom-page' >
						<div class='bloom-translationGroup normal-style'>
							<div class='bloom-editable plain-style' lang='xyz'></div>
							<div class='bloom-editable' lang='en'></div>
							<div class='bloom-editable' lang='fr'></div>
							<div class='bloom-editable fancy-style' lang='es'></div>
							<div lang='pt'>not editable</div>
						</div>
					</div></body></html>";
            var dom = new HtmlDom(contents);
            var bookData = new BookData(dom, _collectionSettings, null);
            TranslationGroupManager.PrepareElementsInPageOrDocument(
                (SafeXmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0],
                bookData
            );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div/div[contains(@class, 'normal-style') and contains(@class, 'bloom-editable')]",
                    2
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div/div[contains(@class, 'plain-style') and contains(@class, 'bloom-editable')]",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div/div[contains(@class, 'fancy-style') and contains(@class, 'bloom-editable')]",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='bloom-translationGroup']/div",
                    5
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='bloom-translationGroup']/div[not(contains(@class, 'bloom-editable') or contains(@class, 'normal-style'))]",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'normal-style') and contains(@class, 'bloom-translationGroup')]",
                    0
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@class='bloom-translationGroup']", 1);
        }

        [Test]
        public void PrepareElementsInPageOrDocument_HasGenerateTranslationsDiv_GeneratesTranslations()
        {
            var contents =
                @"<html><body><div class='bloom-page'>
						<div class='bloom-translationGroup'>
							<div class='bloom-editable' lang='en' data-generate-translations='true' data-i18n='Test.L10N.ID'>English Text</div>
						</div>
					</div></body></html>";
            var dom = new HtmlDom(contents);
            var bookData = new BookData(dom, _collectionSettings, null);

            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@data-generate-translations]", 1);
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@data-i18n]", 1);

            //SUT
            TranslationGroupManager.PrepareElementsInPageOrDocument(
                (SafeXmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0],
                bookData
            );

            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@class='bloom-translationGroup']", 1);
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasNoMatchForXpath("//div[@data-generate-translations]");
            AssertThatXmlIn.Dom(dom.RawDom).HasNoMatchForXpath("//div[@data-i18n]");
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='bloom-translationGroup']/div",
                    5
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div/div[contains(@class, 'bloom-editable')]",
                    5
                );
            // We start with an English div
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div/div[contains(@class, 'bloom-editable') and @lang='en' and text()='English Text']",
                    1
                );
            // These three are included because they are languages of the collection. (We also have a translation for French.)
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div/div[contains(@class, 'bloom-editable') and @lang='fr' and not(text())]",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div/div[contains(@class, 'bloom-editable') and @lang='xyz' and not(text())]",
                    1
                );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div/div[contains(@class, 'bloom-editable') and @lang='es' and text()='Spanish Text']",
                    1
                );
            // This one is included because we have a translation available
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div/div[contains(@class, 'bloom-editable') and @lang='zh-CN' and text()='Chinese Text']",
                    1
                );
        }

        [Test]
        public void FixDuplicateLanguageDivs_HandlesEmptyDivs()
        {
            var contents =
                @"<div class='bloom-page' >
			<div class='bloom-translationGroup normal-style'>
				<div class='bloom-editable' data-languagetipcontent='First' lang='xyz'></div>
				<div class='bloom-editable' lang='en'></div>
				<div class='bloom-editable' lang='fr'></div>
				<div class='bloom-editable' data-languagetipcontent='Second' lang='xyz'></div>
			</div>
		</div>";
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(contents);
            TranslationGroupManager.FixDuplicateLanguageDivs(
                (SafeXmlElement)
                    dom.SafeSelectNodes("//div[contains(@class,'bloom-translationGroup')]")[0],
                "xyz"
            );
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'bloom-translationGroup')]",
                    1
                );
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@class='bloom-editable']", 3);
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='bloom-editable' and @lang='xyz']",
                    1
                );
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='bloom-editable' and @lang='en']",
                    1
                );
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='bloom-editable' and @lang='fr']",
                    1
                );
        }

        [Test]
        public void FixDuplicateLanguageDivs_HandlesEmptySecondDiv()
        {
            var contents =
                @"<div class='bloom-page' >
			<div class='bloom-translationGroup normal-style'>
				<div class='bloom-editable' data-languagetipcontent='First' lang='xyz'>Xyz text</div>
				<div class='bloom-editable' lang='en'>English text</div>
				<div class='bloom-editable' lang='fr'>French text</div>
				<div class='bloom-editable' data-languagetipcontent='Second' lang='xyz'></div>
			</div>
		</div>";
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(contents);
            TranslationGroupManager.FixDuplicateLanguageDivs(
                (SafeXmlElement)
                    dom.SafeSelectNodes("//div[contains(@class,'bloom-translationGroup')]")[0],
                "xyz"
            );
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'bloom-translationGroup')]",
                    1
                );
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@class='bloom-editable']", 3);
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='bloom-editable' and @lang='xyz' and contains(., 'Xyz text')]",
                    1
                );
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='bloom-editable' and @lang='en']",
                    1
                );
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='bloom-editable' and @lang='fr']",
                    1
                );
        }

        [Test]
        public void FixDuplicateLanguageDivs_HandlesEmptyFirstDiv()
        {
            var contents =
                @"<div class='bloom-page' >
			<div class='bloom-translationGroup normal-style'>
				<div class='bloom-editable' data-languagetipcontent='First' lang='xyz'></div>
				<div class='bloom-editable' lang='en'>English text</div>
				<div class='bloom-editable' lang='fr'>French text</div>
				<div class='bloom-editable' data-languagetipcontent='Second' lang='xyz'>Xyz text</div>
			</div>
		</div>";
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(contents);
            TranslationGroupManager.FixDuplicateLanguageDivs(
                (SafeXmlElement)
                    dom.SafeSelectNodes("//div[contains(@class,'bloom-translationGroup')]")[0],
                "xyz"
            );
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'bloom-translationGroup')]",
                    1
                );
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@class='bloom-editable']", 3);
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='bloom-editable' and @lang='xyz' and contains(., 'Xyz text')]",
                    1
                );
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='bloom-editable' and @lang='en']",
                    1
                );
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='bloom-editable' and @lang='fr']",
                    1
                );
        }

        [Test]
        public void FixDuplicateLanguageDivs_HandlesNonemptyDivs()
        {
            var contents =
                @"<div class='bloom-page' >
			<div class='bloom-translationGroup normal-style'>
				<div class='bloom-editable' data-languagetipcontent='First' lang='xyz'>First Xyz text</div>
				<div class='bloom-editable' lang='en'>English text</div>
				<div class='bloom-editable' lang='fr'>French text</div>
				<div class='bloom-editable' data-languagetipcontent='Second' lang='xyz'>Second Xyz text</div>
			</div>
		</div>";
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(contents);
            TranslationGroupManager.FixDuplicateLanguageDivs(
                (SafeXmlElement)
                    dom.SafeSelectNodes("//div[contains(@class,'bloom-translationGroup')]")[0],
                "xyz"
            );
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'bloom-translationGroup')]",
                    1
                );
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@class='bloom-editable']", 3);
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='bloom-editable' and @lang='xyz' and contains(., 'First Xyz text"
                        + System.Environment.NewLine
                        + "Second Xyz text')]",
                    1
                );
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='bloom-editable' and @lang='en']",
                    1
                );
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='bloom-editable' and @lang='fr']",
                    1
                );
        }

        [Test]
        public void SortTranslationGroups_YieldsExpectedOrder()
        {
            var doc = SafeXmlDocument.Create();
            var input = new List<SafeXmlElement>();
            for (int i = 0; i < 7; i++)
            {
                var div = doc.CreateElement("div");
                input.Add(div);
                div.SetAttribute("data-which", i.ToString());
            }

            input[1].SetAttribute("tabindex", "4");
            input[3].SetAttribute("tabindex", "2");
            input[5].SetAttribute("tabindex", "3");
            input[6].SetAttribute("tabindex", "1");
            var output = TranslationGroupManager.SortTranslationGroups(input);
            // Bafflingly, this assert seems to work for any pair of items from the list.
            //Assert.That(output[0], Is.EqualTo(input[6]));
            Assert.That(output[0].Equals(input[6]));
            Assert.That(output[1].Equals(input[3]));
            Assert.That(output[2].Equals(input[5]));
            Assert.That(output[3].Equals(input[1]));
            Assert.That(output[4].Equals(input[0]));
            Assert.That(output[5].Equals(input[2]));
            Assert.That(output[6].Equals(input[4]));
        }

        [Test]
        public void AddThemeVisibleOrderClass_NoSiblings_AddsContentFirst()
        {
            var doc = SafeXmlDocument.Create();
            var tg = doc.CreateElement("div");
            tg.SetAttribute("class", "bloom-translationGroup");
            doc.AppendChild(tg);
            var editableIrrelevant = doc.CreateElement("div");
            editableIrrelevant.SetAttribute("class", "bloom-editable");
            tg.AppendChild(editableIrrelevant);
            editableIrrelevant.SetAttribute("lang", "qaa");
            tg.SetAttribute("data-visibility-variable", "cover-title-LN-show");
            var editable = doc.CreateElement("div");
            editable.SetAttribute("class", "bloom-editable bloom-visibility-code-on");
            tg.AppendChild(editable);

            TranslationGroupManager.AddThemeVisibleOrderClass(
                editable,
                false,
                "fr",
                "fr",
                "en",
                "tpi"
            );
            Assert.That(editable.GetAttribute("class"), Does.Contain("bloom-contentFirst"));

            editable.SetAttribute("class", "bloom-editable bloom-visibility-code-on");
            TranslationGroupManager.AddThemeVisibleOrderClass(
                editable,
                false,
                "en",
                "fr",
                "en",
                "tpi"
            );
            Assert.That(editable.GetAttribute("class"), Does.Contain("bloom-contentFirst"));

            editable.SetAttribute("class", "bloom-editable bloom-visibility-code-on");
            TranslationGroupManager.AddThemeVisibleOrderClass(
                editable,
                false,
                "tpi",
                "fr",
                "en",
                "tpi"
            );
            Assert.That(editable.GetAttribute("class"), Does.Contain("bloom-contentFirst"));
        }

        [Test]
        public void AddThemeVisibleOrderClass_SiblingL1_AddsContentSecond()
        {
            var doc = SafeXmlDocument.Create();
            var tg = doc.CreateElement("div");
            tg.SetAttribute("class", "bloom-translationGroup");
            doc.AppendChild(tg);
            tg.SetAttribute("data-visibility-variable", "cover-title-LN-show");
            var editable = doc.CreateElement("div");
            editable.SetAttribute("class", "bloom-editable bloom-visibility-code-on");
            tg.AppendChild(editable);
            tg.AppendChild(doc.CreateWhitespace("  "));
            var editableFirst = doc.CreateElement("div");
            editableFirst.SetAttribute("class", "bloom-editable bloom-visibility-code-on");
            tg.AppendChild(editableFirst);
            editableFirst.SetAttribute("lang", "fr");
            // We don't want to have to implement SafeXmlDocument.CreateWhitespace, so we'll just
            // wrap the editable element instead of creating a SafeXmlDocument to start with.
            // No fair trying "fr" since that occurs on sibling
            TranslationGroupManager.AddThemeVisibleOrderClass(
                editable,
                false,
                "en",
                "fr",
                "en",
                "tpi"
            );
            Assert.That(editable.GetAttribute("class"), Does.Contain("bloom-contentSecond"));
            Assert.That(editable.GetAttribute("class"), Does.Not.Contain("bloom-contentFirst"));

            editable.SetAttribute("class", "bloom-editable bloom-visibility-code-on");
            TranslationGroupManager.AddThemeVisibleOrderClass(
                editable,
                false,
                "tpi",
                "fr",
                "en",
                "tpi"
            );
            Assert.That(editable.GetAttribute("class"), Does.Contain("bloom-contentSecond"));
            Assert.That(editable.GetAttribute("class"), Does.Not.Contain("bloom-contentFirst"));
        }

        /// <summary>
        /// This started life as a test of the AddThemeVisibleOrderClass method, but we needed at least
        /// one test that tested it along with setting visibility classes, since there was a problem
        /// when we didn't finish setting all the visibility classes before working on contentFirst etc.
        /// </summary>
        [Test]
        public void UpdateContentLanguageClasses_SiblingL1AndL2_AddsContentThird()
        {
            var doc = SafeXmlDocument.Create();
            var body = doc.CreateElement("body");
            doc.AppendChild(body);
            var tg = doc.CreateElement("div");
            tg.SetAttribute("class", "bloom-translationGroup");
            body.AppendChild(tg);
            tg.SetAttribute("data-visibility-variable", "cover-title-LN-show");
            var editableThird = doc.CreateElement("div");
            editableThird.SetAttribute("class", "bloom-editable");
            editableThird.SetAttribute("lang", "es");
            tg.AppendChild(editableThird);
            tg.AppendChild(doc.CreateWhitespace("  "));
            var editableFirst = doc.CreateElement("div");
            editableFirst.SetAttribute("class", "bloom-editable");
            tg.AppendChild(editableFirst);
            editableFirst.SetAttribute("lang", "xyz");
            var editableSecond = doc.CreateElement("div");
            editableSecond.SetAttribute("class", "bloom-editable");
            tg.AppendChild(editableSecond);
            editableSecond.SetAttribute("lang", "fr");
            var editableNone = doc.CreateElement("div");
            editableNone.SetAttribute("class", "bloom-editable");
            tg.AppendChild(editableNone);
            editableNone.SetAttribute("lang", "tpi");

            editableThird.SetAttribute("class", "bloom-editable bloom-visibility-code-on");
            var bookData = new BookData(new HtmlDom(doc), _collectionSettings, null);
            bookData.SetMultilingualContentLanguages("xyz");
            var settings = new AppearanceSettings();
            settings.UpdateFromJson(
                @"{
  ""cssThemeName"": ""default"",
""cover-title-L1-show"": true,
  ""cover-title-L2-show"": true,
  ""cover-title-L3-show"": true
}"
            );
            settings.SetConsistentWithFiles();
            // We don't want to have to implement SafeXmlDocument.CreateWhitespace, so we'll just
            // wrap the body element instead of creating a SafeXmlDocument to start with.
            TranslationGroupManager.UpdateContentLanguageClasses(
                body,
                bookData,
                settings,
                "xyz",
                "fr",
                "es"
            );
            Assert.That(editableThird.GetAttribute("class"), Does.Contain("bloom-contentThird"));
            Assert.That(
                editableThird.GetAttribute("class"),
                Does.Not.Contain("bloom-contentFirst")
            );
            Assert.That(
                editableThird.GetAttribute("class"),
                Does.Not.Contain("bloom-contentSecond")
            );
            Assert.That(
                editableThird.GetAttribute("class"),
                Does.Contain("bloom-visibility-code-on")
            );

            Assert.That(
                editableFirst.GetAttribute("class"),
                Does.Contain("bloom-visibility-code-on")
            );
            Assert.That(editableFirst.GetAttribute("class"), Does.Contain("bloom-contentFirst"));
            Assert.That(
                editableFirst.GetAttribute("class"),
                Does.Not.Contain("bloom-contentSecond")
            );
            Assert.That(
                editableFirst.GetAttribute("class"),
                Does.Not.Contain("bloom-contentThird")
            );

            Assert.That(
                editableSecond.GetAttribute("class"),
                Does.Contain("bloom-visibility-code-on")
            );
            Assert.That(
                editableSecond.GetAttribute("class"),
                Does.Not.Contain("bloom-contentFirst")
            );
            Assert.That(editableSecond.GetAttribute("class"), Does.Contain("bloom-contentSecond"));
            Assert.That(
                editableSecond.GetAttribute("class"),
                Does.Not.Contain("bloom-contentThird")
            );

            Assert.That(
                editableNone.GetAttribute("class"),
                Does.Not.Contain("bloom-visibility-code-on")
            );
            Assert.That(editableNone.GetAttribute("class"), Does.Not.Contain("bloom-contentFirst"));
            Assert.That(
                editableNone.GetAttribute("class"),
                Does.Not.Contain("bloom-contentSecond")
            );
            Assert.That(editableNone.GetAttribute("class"), Does.Not.Contain("bloom-contentThird"));
        }

        [Test]
        public void AddThemeVisibleOrderClass_NotVisible_Or_Irrelevant_AddsNothing()
        {
            var doc = SafeXmlDocument.Create();
            var tg = doc.CreateElement("div");
            tg.SetAttribute("class", "bloom-translationGroup");
            doc.AppendChild(tg);
            tg.SetAttribute("data-visibility-variable", "cover-title-LN-show");
            var editable = doc.CreateElement("div");
            editable.SetAttribute("class", "bloom-editable");
            tg.AppendChild(editable);
            TranslationGroupManager.AddThemeVisibleOrderClass(
                editable,
                false,
                "fr",
                "fr",
                "en",
                "tpi"
            );
            Assert.That(editable.GetAttribute("class"), Is.EqualTo("bloom-editable"));

            TranslationGroupManager.AddThemeVisibleOrderClass(
                editable,
                false,
                "en",
                "fr",
                "en",
                "tpi"
            );

            Assert.That(editable.GetAttribute("class"), Is.EqualTo("bloom-editable"));
            TranslationGroupManager.AddThemeVisibleOrderClass(
                editable,
                false,
                "tpi",
                "fr",
                "en",
                "tpi"
            );
            Assert.That(editable.GetAttribute("class"), Is.EqualTo("bloom-editable"));

            editable.SetAttribute("class", "bloom-editable bloom-visibility-code-on");
            TranslationGroupManager.AddThemeVisibleOrderClass(
                editable,
                true, // will do nothing in legacy mode
                "fr",
                "fr",
                "en",
                "tpi"
            );
            Assert.That(
                editable.GetAttribute("class"),
                Is.EqualTo("bloom-editable bloom-visibility-code-on")
            );

            tg.RemoveAttribute("data-visibility-variable"); // will do nothing without this (not in appearance system)
            TranslationGroupManager.AddThemeVisibleOrderClass(
                editable,
                false,
                "fr",
                "fr",
                "en",
                "tpi"
            );
            Assert.That(
                editable.GetAttribute("class"),
                Is.EqualTo("bloom-editable bloom-visibility-code-on")
            );
        }

        [Test]
        public void IsPageAffectedByLanguageMenu_HasNoTextBlock_False()
        {
            var doc = SafeXmlDocument.Create();
            doc.LoadXml("<html><body><div class='bloom-page'></div></body></html>");
            var page = (SafeXmlElement)doc.SelectSingleNode("//div[contains(@class,'bloom-page')]");
            Assert.That(
                TranslationGroupManager.IsPageAffectedByLanguageMenu(page, false),
                Is.False
            );
        }

        [Test]
        public void IsPageAffectedByLanguageMenu_HasTextBlockWithNoDataDefaultLangs_True()
        {
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(
                @"
<html><body><div class='bloom-page'>
   <div class='bloom-translationGroup'>
       <div class='bloom-editable' lang='xyz'></div>
   </div>
</div></body></html>"
            );
            var page = (SafeXmlElement)doc.SelectSingleNode("//div[contains(@class,'bloom-page')]");
            Assert.That(TranslationGroupManager.IsPageAffectedByLanguageMenu(page, false), Is.True);
        }

        [Test]
        public void IsPageAffectedByLanguageMenu_HasTextBlockWithN1_False()
        {
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(
                @"
<html><body><div class='bloom-page'>
   <div class='bloom-translationGroup' data-default-languages='N1'>
       <div class='bloom-editable' lang='xyz'></div>
   </div>
</div></body></html>"
            );
            var page = (SafeXmlElement)doc.SelectSingleNode("//div[contains(@class,'bloom-page')]");
            Assert.That(
                TranslationGroupManager.IsPageAffectedByLanguageMenu(page, false),
                Is.False
            );
        }

        [Test]
        public void IsPageAffectedByLanguageMenu_HasTextBlockWithVN1_True()
        {
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(
                @"
<html><body><div class='bloom-page'>
   <div class='bloom-translationGroup' data-default-languages='V,N1'>
       <div class='bloom-editable' lang='xyz'></div>
   </div>
</div></body></html>"
            );
            var page = (SafeXmlElement)doc.SelectSingleNode("//div[contains(@class,'bloom-page')]");
            Assert.That(TranslationGroupManager.IsPageAffectedByLanguageMenu(page, false), Is.True);
        }

        [Test]
        public void IsPageAffectedByLanguageMenu_HasTextBlockWithVN1_AndDataVisibilityVariable_False()
        {
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(
                @"
<html><body><div class='bloom-page'>
   <div class='bloom-translationGroup' data-default-languages='V,N1' data-visibility-variable='cover-title-LN-show'>
       <div class='bloom-editable' lang='xyz'></div>
   </div>
</div></body></html>"
            );
            var page = (SafeXmlElement)doc.SelectSingleNode("//div[contains(@class,'bloom-page')]");
            Assert.That(
                TranslationGroupManager.IsPageAffectedByLanguageMenu(page, false),
                Is.False
            );
        }

        [Test]
        public void IsPageAffectedByLanguageMenu_HasTextBlockWithVN1_AndDataVisibilityVariable_Legacy_True()
        {
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(
                @"
<html><body><div class='bloom-page'>
   <div class='bloom-translationGroup' data-default-languages='V,N1' data-visibility-variable='cover-title-LN-show'>
       <div class='bloom-editable' lang='xyz'></div>
   </div>
</div></body></html>"
            );
            var page = (SafeXmlElement)doc.SelectSingleNode("//div[contains(@class,'bloom-page')]");
            Assert.That(TranslationGroupManager.IsPageAffectedByLanguageMenu(page, true), Is.True);
        }

        [Test]
        public void IsPageAffectedByLanguageMenu_HasTextBlockWithN1_AndOneWithAuto_True()
        {
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(
                @"
<html><body><div class='bloom-page'>
   <div class='bloom-translationGroup' data-default-languages='N1'>
       <div class='bloom-editable' lang='xyz'></div>
   </div>
   <div class='bloom-translationGroup' data-default-languages='auto'>
       <div class='bloom-editable' lang='xyz'></div>
   </div>
</div></body></html>"
            );
            var page = (SafeXmlElement)doc.SelectSingleNode("//div[contains(@class,'bloom-page')]");
            Assert.That(TranslationGroupManager.IsPageAffectedByLanguageMenu(page, false), Is.True);
        }
    }
}
