using System;
using Bloom.Book;
using Bloom.Collection;
using Bloom.SafeXml;
using L10NSharp;
using L10NSharp.Windows.Forms;
using NUnit.Framework;
using SIL.Core.ClearShare;
using SIL.Core.Desktop.i18n;
using SIL.IO;
using SIL.Reporting;
using SIL.TestUtilities;
using SIL.Windows.Forms.ClearShare;

namespace BloomTests.Book
{
    [TestFixture]
    public sealed class BookCopyrightAndLicenseTests
    {
        private CollectionSettings _collectionSettings;
        private ILocalizationManager _localizationManager;
        private ILocalizationManager _palasoLocalizationManager;

        [SetUp]
        public void Setup()
        {
            // Normally set in Program.cs, but not for unit tests
            SIL.Localizer.Default = new L10NSharpLocalizer();

            _collectionSettings = new CollectionSettings(
                new NewCollectionSettings()
                {
                    PathToSettingsFile = CollectionSettings.GetPathForNewSettings(
                        new TemporaryFolder("BookDataTests").Path,
                        "test"
                    ),
                    Language1Tag = "xyz",
                    Language2Tag = "en",
                    Language3Tag = "fr",
                }
            );
            ErrorReport.IsOkToInteractWithUser = false;

            LocalizationManager.UseLanguageCodeFolders = true;
            var localizationDirectory =
                FileLocationUtilities.GetDirectoryDistributedWithApplication("localization");
            _localizationManager = LocalizationManagerWinforms.Create(
                "fr",
                "Bloom",
                "Bloom",
                "1.0.0",
                localizationDirectory,
                "SIL/Bloom",
                null,
                new string[] { }
            );
            _palasoLocalizationManager = LocalizationManagerWinforms.Create(
                "fr",
                "Palaso",
                "Palaso",
                "1.0.0",
                localizationDirectory,
                "SIL/Bloom",
                null,
                new string[] { }
            );
        }

        [TearDown]
        public void TearDown()
        {
            _localizationManager.Dispose();
            _palasoLocalizationManager.Dispose();
            LocalizationManager.ForgetDisposedManagers();
        }

        [Test]
        public void GetLicenseMetadata_HasNoCopyrightOrLicense_ReturnCcByForDefault()
        {
            string dataDivContent = @"";
            Assert.True(GetMetadata(dataDivContent).License is CreativeCommonsLicenseInfo);
        }

        [Test]
        public void GetLicenseMetadata_HasCustomLicense_RightsStatementContainsCustom()
        {
            string dataDivContent =
                @"<div lang='en' data-book='licenseNotes'>my custom</div>
					<div data-book='copyright' lang='*' class='bloom-content1'>Copyright © 2012, test</div>";
            Assert.AreEqual("my custom", GetMetadata(dataDivContent).License.RightsStatement);
        }

        [Test]
        public void GetLicenseMetadata_HasOnlyCopyrightAndLicenseNotes_IsCustomLicense()
        {
            string dataDivContent =
                @"<div lang='en' data-book='licenseNotes'>my custom</div>
					<div data-book='copyright' lang='*' class='bloom-content1'>Copyright © 2012, test</div>";
            Assert.IsTrue(GetMetadata(dataDivContent).License is CustomLicenseInfo);
        }

        [Test]
        public void GetLicenseMetadata_HasCCLicenseURL_ConvertedToFulCCLicenseObject()
        {
            //nb: the real testing is done on the palaso class that does the reading, this is just a quick sanity check
            string dataDivContent =
                @"<div lang='en' data-book='licenseUrl'>http://creativecommons.org/licenses/by-nc-sa/3.0/</div>";
            var creativeCommonsLicense = (CreativeCommonsLicense)(
                GetMetadata(dataDivContent).License
            );
            Assert.IsTrue(creativeCommonsLicense.AttributionRequired);
            Assert.IsFalse(creativeCommonsLicense.CommercialUseAllowed);
            Assert.IsTrue(
                creativeCommonsLicense.DerivativeRule
                    == CreativeCommonsLicenseInfo.DerivativeRules.DerivativesWithShareAndShareAlike
            );
        }

        [Test]
        public void GetLicenseMetadata_HasCCLicenseURLWithIGOQualifier_ConvertedToFulCCLicenseObject()
        {
            //nb: the real testing is done on the palaso class that does the reading, this is just a quick sanity check
            string dataDivContent =
                @"<div lang='en' data-book='licenseUrl'>http://creativecommons.org/licenses/by/3.0/igo</div>";
            var creativeCommonsLicense = (CreativeCommonsLicense)(
                GetMetadata(dataDivContent).License
            );
            Assert.IsTrue(creativeCommonsLicense.AttributionRequired);
            Assert.IsTrue(creativeCommonsLicense.CommercialUseAllowed);
            Assert.IsTrue(creativeCommonsLicense.IntergovernmentalOrganizationQualifier);
        }

        [Test]
        public void GetLicenseMetadata_HasOnlyCopyrightAndDescription_IsNullLicense()
        {
            //nb: the real testing is done on the palaso class that does the reading, this is just a quick sanity check
            var dataDivContent =
                @"<div lang='en' data-book='licenseDescription'>This could say anything</div>
			<div data-book='copyright' lang='*' class='bloom-content1'>Copyright © 2012, test</div>";
            Assert.IsTrue(GetMetadata(dataDivContent).License is NullLicense);
        }

        [Test]
        public void GetLicenseMetadata_HasSymbolInCopyright_FullCopyrightStatmentAcquired()
        {
            string dataDivContent =
                @"<div data-book='copyright' lang='*' class='bloom-content1'>Copyright © 2012, test</div>";
            Assert.AreEqual("Copyright © 2012, test", GetMetadata(dataDivContent).CopyrightNotice);
        }

        [TestCase(
            "http://creativecommons.org/licenses/by-nc-sa/",
            "http://creativecommons.org/licenses/by-nc-sa/4.0/"
        )]
        [TestCase(
            "http://creativecommons.org/licenses/by-nc-sa",
            "http://creativecommons.org/licenses/by-nc-sa/4.0/"
        )]
        [TestCase(
            "http://creativecommons.org/licenses/by/",
            "http://creativecommons.org/licenses/by/4.0/"
        )]
        [TestCase(
            "http://creativecommons.org/licenses/by",
            "http://creativecommons.org/licenses/by/4.0/"
        )]
        [TestCase(
            "http://creativecommons.org/licenses/by-nc-sa/4.0/",
            "http://creativecommons.org/licenses/by-nc-sa/4.0/"
        )]
        [TestCase(
            "http://creativecommons.org/licenses/by-nc-sa/4.0",
            "http://creativecommons.org/licenses/by-nc-sa/4.0/"
        )]
        //Regression tests:
        [TestCase(
            "http://creativecommons.org/licenses/by/4.0/",
            "http://creativecommons.org/licenses/by/4.0/"
        )]
        [TestCase(
            "http://creativecommons.org/licenses/by/4.0",
            "http://creativecommons.org/licenses/by/4.0/"
        )]
        [TestCase(
            "http://creativecommons.org/licenses/by-nc-sa/3.0/",
            "http://creativecommons.org/licenses/by-nc-sa/3.0/"
        )]
        [TestCase(
            "http://creativecommons.org/licenses/by-nc-sa/3.0",
            "http://creativecommons.org/licenses/by-nc-sa/3.0/"
        )]
        [TestCase(
            "http://creativecommons.org/licenses/by/3.0/",
            "http://creativecommons.org/licenses/by/3.0/"
        )]
        [TestCase(
            "http://creativecommons.org/licenses/by/3.0",
            "http://creativecommons.org/licenses/by/3.0/"
        )]
        public void GetMetadataLicenseUrl_MissingCCVersion_CorrectsToDefaultVersion(
            string input,
            string result
        )
        {
            string dataDivContent = @"<div lang='*' data-book='licenseUrl'>" + input + "</div>";
            Assert.AreEqual(result, GetMetadata(dataDivContent).License.Url);
        }

        // Previously, it was assumed there would only be one value for each of copyright and license,
        // so the code simply got the "first" one. With branding, we can have a single branding pack
        // supply copyright and license for multiple languages (see Afghan-Children-Read branding).
        [Test]
        public void GetMetadata_DataProvidedByBranding_GetsCorrectValuesForLanguage1()
        {
            CollectionSettings collectionSettings = new CollectionSettings { Language1Tag = "yyy" };
            string dataDivContent =
                @"
<div lang='aaa' data-book='licenseNotes'>My aaa license notes</div>
<div lang='en' data-book='licenseNotes'>My en license notes</div>
<div lang='*' data-book='licenseNotes'>My * license notes</div>
<div lang='yyy' data-book='licenseNotes'>My yyy license notes</div>
<div lang='zzz' data-book='licenseNotes'>My zzz license notes</div>
<div lang='en' data-book='copyright'>My en copyright</div>
<div lang='yyy' data-book='copyright'>My yyy copyright</div>
<div lang='*' data-book='copyright'>My * copyright</div>";

            Metadata metadata = GetMetadata(dataDivContent, collectionSettings);
            Assert.AreEqual("My yyy license notes", metadata.License.RightsStatement);
            Assert.AreEqual("My yyy copyright", metadata.CopyrightNotice);
        }

        [Test]
        public void SetLicenseMetadata_ToNoLicenseUrl_OriginalHasLicenseUrlInEn_ClearsEn()
        {
            string dataDivContent =
                @"<div lang='en' data-book='licenseUrl'>http://creativecommons.org/licenses/by-nc-sa/3.0/</div>";
            var dom = MakeDom(dataDivContent);
            var collectionSettings = new CollectionSettings();
            var bookData = new BookData(dom, collectionSettings, null);
            var creativeCommonsLicense = (CreativeCommonsLicense)(
                BookCopyrightAndLicense.GetMetadata(dom, bookData).License
            );
            Assert.IsTrue(creativeCommonsLicense.AttributionRequired); // yes, we got a CC license from the 'en' licenseUrl
            var newLicense = new CustomLicense();
            var newMetaData = new Metadata();
            newMetaData.License = newLicense;
            BookCopyrightAndLicense.SetMetadata(newMetaData, dom, null, bookData, false);
            AssertThatXmlIn.Dom(dom.RawDom).HasNoMatchForXpath("//div[@data-book='licenseUrl']");
        }

        [Test]
        public void SetLicenseMetadata_CCLicenseWithFrenchNationalLanguage_DataDivHasFrenchDescription()
        {
            _collectionSettings.Language1Tag = "fr";
            _collectionSettings.Language2Tag = "en";

            TestSetLicenseMetdataEffectOnDataDiv(
                new Metadata()
                {
                    CopyrightNotice = "foo",
                    License = new CreativeCommonsLicense(
                        true,
                        true,
                        CreativeCommonsLicenseInfo.DerivativeRules.Derivatives
                    ),
                },
                startingDataDivContent: "",
                xpath: "//*[@data-book='licenseDescription' and @lang='fr' and contains(., 'création')]",
                expectedCount: 1
            );
        }

        [Test]
        public void SetLicenseMetadata_CCLicense_LicenseImageAddedToDataDiv()
        {
            TestSetLicenseMetdataEffectOnDataDiv(
                new Metadata()
                {
                    CopyrightNotice = "foo",
                    License = new CreativeCommonsLicense(
                        true,
                        true,
                        CreativeCommonsLicenseInfo.DerivativeRules.Derivatives
                    ),
                },
                startingDataDivContent: "",
                xpath: "//*[@data-book='licenseImage' and text()='license.png']",
                expectedCount: 1
            );
        }

        [Test]
        public void SetLicenseMetadata_CustomLicense_LicenseImageRemovedFromDataDiv()
        {
            TestSetLicenseMetdataEffectOnDataDiv(
                new Metadata() { CopyrightNotice = "foo", License = new CustomLicense() },
                startingDataDivContent: "<div data-book='licenseImage' lang='*'>license.png</div>",
                xpath: "//*[@data-book='licenseImage']",
                expectedCount: 0
            );
        }

        [Test]
        public void SetLicenseMetadata_NullLicense_LicenseImageRemovedFromDataDiv()
        {
            TestSetLicenseMetdataEffectOnDataDiv(
                new Metadata() { CopyrightNotice = "foo", License = new NullLicense() },
                startingDataDivContent: "<div data-book='licenseImage' lang='*'>license.png</div>",
                xpath: "//*[@data-book='licenseImage']",
                expectedCount: 0
            );
        }

        [Test]
        public void SetLicenseMetadata_PreviouslyHadCCLicenseInFrenchThenChangedToCustom_OnlyShowsCustomRightsStatement()
        {
            _collectionSettings.Language1Tag = "fr";
            // This will probably improve in the future, but for now, the custom rights statement does not have a language.
            // This test makes sure that we don't leave obsolete descriptions around in a preferred language.
            var dom = TestSetLicenseMetdataEffectOnDataDiv(
                new Metadata()
                {
                    CopyrightNotice = "foo",
                    License = new CustomLicense() { RightsStatement = "custom rights" },
                },
                startingDataDivContent: "<div data-book='licenseDescription' lang='fr'>Some old French</div>",
                xpath: "//*[@data-book='licenseDescription']",
                expectedCount: 1
            );

            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//*[@data-book='licenseDescription' and @lang='fr']",
                    0
                );
        }

        [Test]
        public void SetMetadata_CustomLicense_LicenseImageSrcAndAltAreEmpty()
        {
            TestSetLicenseMetdataEffectOnDataDiv(
                new Metadata() { CopyrightNotice = "foo", License = new CustomLicense() },
                startingPageContent: "<img data-derived='licenseImage' lang='*' alt='This image, license.png, is missing or was loading too slowly.'>license.png</img>",
                xpath: "//img[@data-derived='licenseImage' and (not(@alt) or @alt='') and @src='']",
                expectedCount: 1
            );
        }

        private HtmlDom TestSetLicenseMetdataEffectOnDataDiv(
            Metadata metadata = null,
            string startingDataDivContent = "",
            string startingPageContent = "",
            string xpath = "",
            int expectedCount = 1
        )
        {
            var dom = new HtmlDom(
                @"<html><head><div id='bloomDataDiv'>"
                    + startingDataDivContent
                    + "</div><div id='credits'>"
                    + startingPageContent
                    + "</div></head><body></body></html>"
            );
            var bookData = new BookData(dom, _collectionSettings, null);
            BookCopyrightAndLicense.SetMetadata(metadata, dom, null, bookData, false);
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(xpath, expectedCount);
            return dom;
        }

        private Metadata GetMetadata(
            string dataDivContent,
            CollectionSettings collectionSettings = null
        )
        {
            var dom = MakeDom(dataDivContent);
            var bookData = new BookData(dom, collectionSettings ?? _collectionSettings, null);
            return BookCopyrightAndLicense.GetMetadata(dom, bookData);
        }

        private static HtmlDom MakeDom(string dataDivContent)
        {
            return new HtmlDom(
                @"<html><head><div id='bloomDataDiv'>"
                    + dataDivContent
                    + "</div></head><body></body></html>"
            );
        }

        [Test]
        public void CheckDataDivToPagePropagation_Copyright()
        {
            CheckUpdateDomFromDataDiv(
                "copyright",
                null,
                description: "if copyright is not in datadiv, on page the corresponding element should be empty"
            );
            CheckUpdateDomFromDataDiv(
                "copyright",
                "",
                description: "if copyright is empty datadiv, on page the corresponding element should be empty"
            );
            CheckUpdateDomFromDataDiv(
                "copyright",
                "copyright correct, 1996",
                description: "if copyright is in datadiv, on page the corresponding element should be a copy"
            );
        }

        [Test]
        public void CheckDataDivToPagePropagation_LicenseUrl()
        {
            CheckUpdateDomFromDataDiv(
                "licenseUrl",
                null,
                description: "if licenseUrl is not in datadiv, on page the corresponding element should be empty"
            );
            CheckUpdateDomFromDataDiv(
                "licenseUrl",
                "",
                description: "if licenseUrl is empty datadiv, on page the corresponding element should be empty"
            );
            CheckUpdateDomFromDataDiv(
                "licenseUrl",
                "example.com",
                description: "if licenseUrl is in datadiv, on page the corresponding element should be a copy"
            );
        }

        [Test]
        public void CheckDataDivToPagePropagation_LicenseNotes()
        {
            CheckUpdateDomFromDataDiv(
                "licenseNotes",
                null,
                description: "if licenseNotes is not in datadiv, on page the corresponding element should be empty"
            );
            CheckUpdateDomFromDataDiv(
                "licenseNotes",
                "",
                description: "if licenseNotes is empty datadiv, on page the corresponding element should be empty"
            );
            CheckUpdateDomFromDataDiv(
                "licenseNotes",
                "some notes",
                description: "if licenseNotes is in datadiv, on page the corresponding element should be a copy"
            );
            CheckUpdateDomFromDataDiv(
                "licenseNotes",
                "line 1<br />line 2",
                description: "can include br in license notes",
                customXPath: "//div[@id='test']/div/br"
            );
        }

        [Test]
        public void CheckDataDivToPagePropagation_LicenseDescription()
        {
            CheckUpdateDomFromDataDiv(
                "licenseDescription",
                null,
                description: "if licenseDescription is not in datadiv, on page the corresponding element should be empty"
            );
            CheckUpdateDomFromDataDiv(
                "licenseDescription",
                "",
                description: "if licenseDescription is empty datadiv, on page the corresponding element should be empty"
            );
            CheckUpdateDomFromDataDiv(
                "licenseDescription",
                "some Description",
                description: "if licenseDescription is in datadiv, on page the corresponding element should be a copy"
            );
            CheckUpdateDomFromDataDiv(
                "licenseDescription",
                "line 1<br />line 2",
                description: "can include br in description",
                customXPath: "//div[@id='test']/div/br"
            );
        }

        [Test]
        public void CheckDataDivToPagePropagation_LicenseImage()
        {
            CheckUpdateDomFromDataDiv(
                "licenseImage",
                null,
                description: "if licenseImage is not in datadiv, on page the img element should have an empty @src and empty @alt"
            );
            CheckUpdateDomFromDataDiv(
                "licenseImage",
                "",
                description: "if licenseImage has empty @src in datadiv, on page the img element should have an empty @src and empty @alt"
            );
            CheckUpdateDomFromDataDiv(
                "licenseImage",
                "something.png",
                description: "if licenseImage is in datadiv, on page the img element should have the @src filled with the url"
            );
        }

        [Test]
        public void UpdateDomFromDataDiv_CCLicense_OnPageTheLicenseHasFrench()
        {
            _collectionSettings.Language1Tag = "fr";
            _collectionSettings.Language2Tag = "en";

            //NB: ideally, this test would just set the licenseUrl and then test the resulting description.
            //That is, the description would not even be in the datadiv, since all we need is the licenseURl
            //(at least for creative commons licenses). We would then just generate the description when we
            //update the page.
            //However, for backwards compatibility, we still (as of 3.6) determine the description when doing
            //a SetMetadata, put the description in the bloomdatadiv, and then it just flows down
            //to the page.
            var html =
                @"<html><body>
							<div id='bloomDataDiv'>
								<div data-book='licenseDescription' lang='es'>Spanish Description</div>
								<div data-book='licenseDescription' lang='fr'>French Description</div>
								<div data-book='licenseDescription' lang='en'>English Description</div>
							</div>
							<div id='test'>
								<div data-derived='licenseDescription' lang='en'>BoilerPlateDescription</div>
							</div>
						</body></html>";
            var bookDom = new HtmlDom(html);
            var bookData = new BookData(bookDom, _collectionSettings, null);

            BookCopyrightAndLicense.UpdateDomFromDataDiv(bookDom, "", bookData, false);
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='test']/*[@data-derived='licenseDescription' and @lang='fr' and contains(text(),'French')]",
                    1
                );
        }

        [Test]
        public void UpdateDomFromDataDiv_CopiesCopyrightAndOriginalCopyrightToMultipleDestinations()
        {
            // We could test other fields too, but these are enough to cover the two main methods that do the copying.
            var html =
                @"<html><head></head><body>
							<div id='bloomDataDiv'>
								<div data-book='copyright' lang='*'>Copyright © 2008, Bar Publishers</div>
								<div data-book='originalLicenseUrl' lang='*'>http://creativecommons.org/licenses/by-nc/4.0/</div>
								<div data-book='originalLicenseNotes' lang='*'>You can do anything you want if your name is Fred.</div>
								<div data-book='originalCopyright' lang='*'>Copyright © 2007, Foo Publishers</div>
							</div>
							<div id='test' class='test'>
								<div data-derived='copyright' lang='*'>something obsolete</div>
								<div data-derived='originalCopyrightAndLicense' lang='en'>BoilerPlateDescription</div>
							</div>
							<div id='test2' class='test'>
								<div data-derived='copyright' lang='*'>something else obsolete to be overwritten</div>
								<div data-derived='originalCopyrightAndLicense' lang='en'>Some other place we show original copyright</div>
							</div>
						</body></html>";
            var bookDom = new HtmlDom(html);
            var bookData = new BookData(bookDom, _collectionSettings, null);

            BookCopyrightAndLicense.UpdateDomFromDataDiv(bookDom, "", bookData, false);
            // This is an abbreviated version of the text we expect in originalCopyrightAndLicense. Now that we have an embedded <cite> element, matching the whole thing
            // is difficult. We have other tests that deal with exactly what goes in this field; here we're just concerned with getting the right number of copies.
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='test']/*[@data-derived='originalCopyrightAndLicense' and @lang='*' and contains(text(),'This book is an adaptation of the original')]",
                    2
                );
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='test']/*[@data-derived='copyright' and @lang='*' and contains(text(),'Copyright © 2008, Bar Publishers')]",
                    2
                );

            // Changing the useOriginalCopyright flag should empty out the data-derived='originalCopyrightAndLicense' divs.
            // The #bloomDataDiv would have to change to change the data-derived='copyright' divs.
            BookCopyrightAndLicense.UpdateDomFromDataDiv(bookDom, "", bookData, true);
            Console.WriteLine("DEBUG bookDom =\n{0}", bookDom.RawDom.OuterXml);
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasNoMatchForXpath(
                    "//div[@class='test']/*[@data-derived='originalCopyrightAndLicense' and @lang='*']"
                );
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='test']/*[@data-derived='originalCopyrightAndLicense' and .='']",
                    2
                );
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='test']/*[@data-derived='copyright' and @lang='*' and contains(text(),'Copyright © 2008, Bar Publishers')]",
                    2
                );
        }

        /// <summary>
        /// Once the user has asked to edit the generated original-copyright sentence, Bloom
        /// stops generating it and shows their wording instead. The book's own copy of the page
        /// still holds it locked: only the copy sent to the editor is opened up, which is what
        /// makes leaving the page or refreshing it lock the sentence again.
        /// </summary>
        [Test]
        public void UpdateDomFromDataDiv_UserEditsOriginalCopyrightNotice_ShowsTheirWordingStillLocked()
        {
            var html =
                @"<html><head></head><body>
							<div id='bloomDataDiv'>
								<div data-book='copyright' lang='*'>Copyright © 2008, Bar Publishers</div>
								<div data-book='originalLicenseUrl' lang='*'>http://creativecommons.org/licenses/by-nc/4.0/</div>
								<div data-book='originalCopyright' lang='*'>Copyright © 2007, Foo Publishers</div>
							</div>
							<div id='test' class='test'>
								<div class='copyright Credits-Page-style' data-derived='originalCopyrightAndLicense' lang='en'>BoilerPlateDescription</div>
							</div>
						</body></html>";
            var bookDom = new HtmlDom(html);
            var bookData = new BookData(bookDom, _collectionSettings, null);

            // Sanity check: with the flag off we get Bloom's sentence, with the padlock that
            // offers to hand it over. Without this, the assertions below could pass on a DOM
            // where the notice never appeared in the first place.
            BookCopyrightAndLicense.UpdateDomFromDataDiv(bookDom, "", bookData, false, false);
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='test']/*[@data-derived='originalCopyrightAndLicense' and contains(text(),'This book is an adaptation of the original')]",
                    1
                );
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//*[@data-derived='originalCopyrightAndLicense' and @data-link-icon='lock'"
                        + " and @data-link-target='UnlockOriginalCredits()'"
                        + " and @data-link-icon-tooltip='Unlock to edit']",
                    1
                );

            // This is what the API does when the user clicks the padlock.
            BookCopyrightAndLicense.SeedUserEditableOriginalCopyrightNotice(bookDom, bookData);
            BookCopyrightAndLicense.UpdateDomFromDataDiv(bookDom, "", bookData, false, true);

            // The user's wording is what shows now, and it is still locked.
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='test']/*[@data-derived='originalCopyrightAndLicense'"
                        + " and @data-link-icon='lock'"
                        + " and contains(., 'This book is an adaptation of the original')]",
                    1
                );
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasNoMatchForXpath("//div[@class='test']//*[@data-book]");
            // The wording is markup, and must go back onto the page as markup rather than as
            // text that shows the reader its own tags.
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='test']/*[@data-derived='originalCopyrightAndLicense']/p",
                    1
                );
            // The wording is in the data div, which is what makes the user's edits stick.
            Assert.That(
                bookData.GetVariableOrNull("originalCopyrightAndLicense", "*").Xml,
                Does.Contain("This book is an adaptation of the original")
            );
        }

        /// <summary>
        /// When the book carries the original copyright as its own, saying it again in a
        /// sentence about the original book prints it twice. That holds for the user's own
        /// wording as much as for Bloom's, and their wording waits in the data div in case
        /// they turn the option off again. See BL-7381.
        /// </summary>
        [Test]
        public void UpdateDomFromDataDiv_UsingOriginalCopyright_HidesTheUsersOwnNoticeToo()
        {
            var html =
                @"<html><head></head><body>
							<div id='bloomDataDiv'>
								<div data-book='originalCopyright' lang='*'>Copyright © 2007, Foo Publishers</div>
								<div data-book='originalCopyrightAndLicense' lang='*'><p>My own words about the original.</p></div>
							</div>
							<div id='test' class='test'>
								<div class='copyright Credits-Page-style' data-derived='originalCopyrightAndLicense' lang='*'><p>My own words about the original.</p></div>
							</div>
						</body></html>";
            var bookDom = new HtmlDom(html);
            var bookData = new BookData(bookDom, _collectionSettings, null);

            // Sanity check: without the option, the user's wording is what shows.
            BookCopyrightAndLicense.UpdateDomFromDataDiv(bookDom, "", bookData, false, true);
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='test']/*[contains(., 'My own words about the original')]",
                    1
                );

            BookCopyrightAndLicense.UpdateDomFromDataDiv(bookDom, "", bookData, true, true);

            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasNoMatchForXpath(
                    "//div[@class='test']//*[contains(., 'My own words about the original')]"
                );
            AssertThatXmlIn.Dom(bookDom.RawDom).HasNoMatchForXpath("//*[@data-link-icon]");
            // Their wording is still there for when they turn the option off again.
            Assert.That(
                bookData.GetVariableOrNull("originalCopyrightAndLicense", "*").Xml,
                Does.Contain("My own words about the original")
            );
        }

        /// <summary>
        /// The copy of the page sent to the editor turns the locked sentence into an ordinary
        /// editable field, with an open padlock in the bubble and the caret waiting in it.
        /// </summary>
        [Test]
        public void MakeOriginalCopyrightNoticeEditable_ReplacesTheLockedTextWithAnEditableField()
        {
            var html =
                @"<html><head></head><body>
							<div id='test' class='test'>
								<div class='copyright Credits-Page-style' data-derived='originalCopyrightAndLicense' lang='*'
								     data-hint='Bloom wrote this' data-link-icon='lock' data-link-target='UnlockOriginalCredits()'><p>Some sentence about the original.</p></div>
							</div>
						</body></html>";
            var pageDom = new HtmlDom(html);

            BookCopyrightAndLicense.MakeOriginalCopyrightNoticeEditable(pageDom);

            AssertThatXmlIn
                .Dom(pageDom.RawDom)
                .HasNoMatchForXpath("//*[@data-derived='originalCopyrightAndLicense']");
            AssertThatXmlIn
                .Dom(pageDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='test']/div[contains(@class,'bloom-translationGroup')"
                        + " and contains(@class,'copyright') and @data-default-languages='*'"
                        + " and @data-link-icon='unlock'"
                        + " and @data-link-target='RelockOriginalCredits()'"
                        + " and not(@data-link-icon-tooltip)]"
                        + "/div[@data-book='originalCopyrightAndLicense' and @lang='*'"
                        + " and contains(@class,'bloom-editable')"
                        + " and contains(@class,'Credits-Page-style')"
                        + " and not(@data-hint) and not(@data-link-icon)"
                        + " and @data-bloom-focus-when-shown='true'"
                        + " and contains(., 'Some sentence about the original')]",
                    1
                );
        }

        /// <summary>
        /// The unlock is good for one look at the page, so both the copy that goes back into the
        /// book and the next copy sent to the editor have to be locked again, keeping whatever
        /// the user typed.
        /// </summary>
        [Test]
        public void LockOriginalCopyrightNotice_PutsTheEditedWordingBackAsPlainText()
        {
            var pageDom = new HtmlDom(
                @"<html><head></head><body><div id='test' class='test'>
					<div class='copyright Credits-Page-style' data-derived='originalCopyrightAndLicense' lang='*'><p>Some sentence about the original.</p></div>
				</div></body></html>"
            );
            BookCopyrightAndLicense.MakeOriginalCopyrightNoticeEditable(pageDom);
            // Sanity check: we can only test the locking if the unlocking happened.
            AssertThatXmlIn
                .Dom(pageDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-translationGroup')]",
                    1
                );
            // Stand in for the user's editing, and for the empty fields Bloom adds for the
            // book's other languages.
            var editable =
                pageDom.SelectSingleNode("//div[@data-book='originalCopyrightAndLicense']")
                as SafeXmlElement;
            editable.InnerXml = "<p>My own <em>wording</em>.</p>";
            var otherLanguage =
                editable.ParentNode.AppendChild(pageDom.RawDom.CreateElement("div"))
                as SafeXmlElement;
            otherLanguage.SetAttribute("class", "bloom-editable");
            otherLanguage.SetAttribute("data-book", "originalCopyrightAndLicense");
            otherLanguage.SetAttribute("lang", "fr");

            BookCopyrightAndLicense.LockOriginalCopyrightNotice(pageDom.RawDom);

            AssertThatXmlIn.Dom(pageDom.RawDom).HasNoMatchForXpath("//*[@data-book]");
            AssertThatXmlIn
                .Dom(pageDom.RawDom)
                .HasNoMatchForXpath("//*[contains(@class,'bloom-translationGroup')]");
            AssertThatXmlIn
                .Dom(pageDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='test']/div[@data-derived='originalCopyrightAndLicense'"
                        + " and contains(@class,'Credits-Page-style')"
                        + " and @data-link-icon='lock']/p/em[text()='wording']",
                    1
                );
        }

        /// <summary>
        /// A book that is not a derivative has nothing to hand over, so the empty spot must not
        /// turn into an editable field if some other page's unlock passes through.
        /// </summary>
        [Test]
        public void MakeOriginalCopyrightNoticeEditable_NothingThere_LeavesItAlone()
        {
            var pageDom = new HtmlDom(
                @"<html><head></head><body><div id='test' class='test'>
					<div class='copyright Credits-Page-style' data-derived='originalCopyrightAndLicense'></div>
				</div></body></html>"
            );

            BookCopyrightAndLicense.MakeOriginalCopyrightNoticeEditable(pageDom);

            AssertThatXmlIn
                .Dom(pageDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//*[@data-derived='originalCopyrightAndLicense']",
                    1
                );
            AssertThatXmlIn.Dom(pageDom.RawDom).HasNoMatchForXpath("//*[@data-book]");
        }

        /// <summary>
        /// Bloom names the original title in a &lt;cite data-book="originalTitle"&gt; so it can keep
        /// it in step with the book's settings. Once the sentence belongs to the user it is just
        /// words they can type over, and a data-book inside a data-book field would be harvested
        /// as book data, so the citation is reduced to plain italics.
        /// </summary>
        [Test]
        public void SeedUserEditableOriginalCopyrightNotice_ReplacesOriginalTitleCitationWithItalics()
        {
            var html =
                @"<html><head></head><body>
							<div id='bloomDataDiv'>
								<div data-book='originalLicenseUrl' lang='*'>http://creativecommons.org/licenses/by/4.0/</div>
								<div data-book='originalCopyright' lang='*'>Copyright © 2007, Foo Publishers</div>
								<div data-book='originalTitle' lang='*'>Aat ni Tata</div>
							</div>
						</body></html>";
            var bookDom = new HtmlDom(html);
            var bookData = new BookData(bookDom, _collectionSettings, null);
            Assert.That(
                BookCopyrightAndLicense.GetOriginalCopyrightAndLicenseNotice(bookData, bookDom),
                Does.Contain("<cite"),
                "Test setup problem: the generated sentence was supposed to contain a citation."
            );

            BookCopyrightAndLicense.SeedUserEditableOriginalCopyrightNotice(bookDom, bookData);

            var stored = bookData.GetVariableOrNull("originalCopyrightAndLicense", "*").Xml;
            Assert.That(stored, Does.Not.Contain("cite"));
            Assert.That(stored, Does.Not.Contain("data-book"));
            Assert.That(stored, Does.Contain("<em>Aat ni Tata</em>"));
            // A bare run of text and markup would leave the italicized title on a line of its
            // own once the editing code wrapped the text nodes in paragraphs.
            Assert.That(stored, Does.StartWith("<p>"));
            Assert.That(stored, Does.EndWith("</p>"));
        }

        /// <summary>
        /// Bloom builds the sentence by pasting the original title and copyright holder straight
        /// into it, and those are whatever the publisher typed: "SIL &amp; LASI" is ordinary.
        /// The sentence therefore is not valid XML, and handing it over to the user has to escape
        /// it rather than parse it.
        /// </summary>
        [Test]
        public void SeedUserEditableOriginalCopyrightNotice_AmpersandInTitleAndCopyright_IsEscaped()
        {
            var html =
                @"<html><head></head><body>
							<div id='bloomDataDiv'>
								<div data-book='originalLicenseUrl' lang='*'>http://creativecommons.org/licenses/by/4.0/</div>
								<div data-book='originalCopyright' lang='*'>Copyright © 2007, SIL &amp; LASI</div>
								<div data-book='originalTitle' lang='*'>Tom &amp; Jerry</div>
							</div>
						</body></html>";
            var bookDom = new HtmlDom(html);
            var bookData = new BookData(bookDom, _collectionSettings, null);
            Assert.That(
                BookCopyrightAndLicense.GetOriginalCopyrightAndLicenseNotice(bookData, bookDom),
                Does.Contain("SIL & LASI"),
                "Test setup problem: the generated sentence was supposed to hold a bare ampersand."
            );

            BookCopyrightAndLicense.SeedUserEditableOriginalCopyrightNotice(bookDom, bookData);

            var stored = bookData.GetVariableOrNull("originalCopyrightAndLicense", "*").Xml;
            Assert.That(stored, Does.Contain("SIL &amp; LASI"));
            Assert.That(stored, Does.Contain("<em>Tom &amp; Jerry</em>"));
            // The stored wording goes back onto the page with InnerXml, so it has to parse.
            Assert.That(() => SafeXmlDocument.Create().LoadXml(stored), Throws.Nothing);
        }

        /// <summary>
        /// A book that was never a derivative has the same empty data-derived div, and there is
        /// nothing there to hand over, so it must not get the bubble either.
        /// </summary>
        [Test]
        public void UpdateDomFromDataDiv_NotADerivative_NoNoticeAndNoHint()
        {
            var html =
                @"<html><head></head><body>
							<div id='bloomDataDiv'>
								<div data-book='copyright' lang='*'>Copyright © 2008, Bar Publishers</div>
								<div data-book='licenseUrl' lang='*'>http://creativecommons.org/licenses/by/4.0/</div>
							</div>
							<div id='test' class='test'>
								<div data-derived='originalCopyrightAndLicense' lang='en'>BoilerPlateDescription</div>
							</div>
						</body></html>";
            var bookDom = new HtmlDom(html);
            var bookData = new BookData(bookDom, _collectionSettings, null);
            Assert.That(
                bookData.BookIsDerivative(),
                Is.False,
                "Test setup problem: this book was supposed to not be a derivative."
            );

            BookCopyrightAndLicense.UpdateDomFromDataDiv(bookDom, "", bookData, false, false);

            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='test']/*[@data-derived='originalCopyrightAndLicense' and .='']",
                    1
                );
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasNoMatchForXpath(
                    "//*[@data-derived='originalCopyrightAndLicense' and @data-hint]"
                );
            AssertThatXmlIn.Dom(bookDom.RawDom).HasNoMatchForXpath("//*[@data-link-icon]");
        }

        [Test]
        public void SetMetadata_CopiesCopyrightAndOriginalCopyrightToMultipleDestinations()
        {
            // We could test other fields too, but these are enough to cover the two main methods that do the copying.
            var html =
                @"<html><head></head><body>
							<div id='bloomDataDiv'>
								<div data-book='copyright' lang='*'>Copyright © 2008, Bar Publishers</div>
								<div data-book='originalLicenseUrl' lang='*'>http://creativecommons.org/licenses/by-nc/4.0/</div>
								<div data-book='originalLicenseNotes' lang='*'>You can do anything you want if your name is Fred.</div>
								<div data-book='originalCopyright' lang='*'>Copyright © 2007, Foo Publishers</div>
							</div>
							<div id='test' class='test'>
								<div data-derived='copyright' lang='*'>something obsolete</div>
								<div data-derived='originalCopyrightAndLicense' lang='en'>BoilerPlateDescription</div>
							</div>
							<div id='test2' class='test'>
								<div data-derived='copyright' lang='*'>something else obsolete to be overwritten</div>
								<div data-derived='originalCopyrightAndLicense' lang='en'>Some other place we show original copyright</div>
							</div>
						</body></html>";
            var bookDom = new HtmlDom(html);
            var bookData = new BookData(bookDom, _collectionSettings, null);
            var metadata = BookCopyrightAndLicense.GetMetadata(bookDom, bookData);
            metadata.CopyrightNotice = "Copyright © 2019, Foo-Bar Publishers";
            BookCopyrightAndLicense.SetMetadata(metadata, bookDom, "", bookData, false);
            // This is an abbreviated version of the text we expect in originalCopyrightAndLicense. Now that we have an embedded <cite> element, matching the whole thing
            // is difficult. We have other tests that deal with exactly what goes in this field; here we're just concerned with generating it or not.
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='test']/*[@data-derived='originalCopyrightAndLicense' and @lang='*' and contains(text(),'This book is an adaptation of the original')]",
                    2
                );
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='test']/*[@data-derived='copyright' and @lang='*' and contains(text(),'Copyright © 2019, Foo-Bar Publishers')]",
                    2
                );

            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='copyright' and contains(text(), 'Copyright © 2019, Foo-Bar Publishers')]",
                    1
                );
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='originalCopyright' and contains(text(), 'Copyright © 2007, Foo Publishers')]",
                    1
                );
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-book='licenseUrl']");
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='originalLicenseUrl' and contains(text(), 'http://creativecommons.org/licenses/by-nc/4.0/')]",
                    1
                );

            // Change to use the original copyright and license.
            var originalMetadata = BookCopyrightAndLicense.GetOriginalMetadata(bookDom, bookData);
            BookCopyrightAndLicense.SetMetadata(originalMetadata, bookDom, "", bookData, true);
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasNoMatchForXpath(
                    "//div[@class='test']/*[@data-derived='originalCopyrightAndLicense' and @lang='*']"
                );
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='test']/*[@data-derived='originalCopyrightAndLicense' and .='']",
                    2
                );
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@class='test']/*[@data-derived='copyright' and @lang='*' and contains(text(),'Copyright © 2007, Foo Publishers')]",
                    2
                );

            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='copyright' and contains(text(), 'Copyright © 2007, Foo Publishers')]",
                    1
                );
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='originalCopyright' and contains(text(), 'Copyright © 2007, Foo Publishers')]",
                    1
                );
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='licenseUrl' and contains(text(), 'http://creativecommons.org/licenses/by-nc/4.0/')]",
                    1
                );
            AssertThatXmlIn
                .Dom(bookDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='originalLicenseUrl' and contains(text(), 'http://creativecommons.org/licenses/by-nc/4.0/')]",
                    1
                );
        }

        /// <summary>
        /// Start out with an html with a bloomDataDiv describe by the parameters, then run it through the derivation of
        /// elements, then check to see that we got the expected result
        /// </summary>
        /// <param name="key">the data key. E.g. 'licenseDescription'</param>
        /// <param name="dataDivValue">if null, then the element should not be present at all in the bloomDataDiv of the incoming html</param>
        /// <param name="tag">defaults to div, for img we pass in "img"</param>
        /// <param name="valueAttribute"></param>
        /// <param name="lang1"></param>
        /// <param name="lang2"></param>
        /// <param name="lang3"></param>
        /// <param name="description"></param>
        private void CheckUpdateDomFromDataDiv(
            string key,
            string dataDivValue,
            string lang1 = "en",
            string lang2 = "",
            string lang3 = "",
            string description = null,
            string customXPath = null
        )
        {
            if (description == null)
                description = string.Format("{0} should be '{1}'", key, dataDivValue);

            _collectionSettings.Language1Tag = lang1;
            _collectionSettings.Language2Tag = lang2;
            _collectionSettings.Language3Tag = lang3;

            var existingLicenseBlockOnPage =
                @"<div id='test'>
						<div data-derived = 'copyright' lang='en'>Some Copyright</div>
						<img src='license.png' alt='blah blah' data-derived='licenseImage'/>
						<div data-derived = 'licenseUrl' lang='en'>Boilerplate.com</div>
						<div data-derived='licenseDescription' lang='en'>BoilerPlateDescription</div>
						<div data-derived='licenseNotes' lang='en'>BoilerPlateNotes</div>
					</div>";

            string html = "<html><body><div id='bloomDataDiv'>";
            if (dataDivValue != null) //we want this even if it is empty, just not null
            {
                html += string.Format(
                    "<{0} data-book='{1}' lang='en'>{2}</{0}>",
                    "div",
                    key,
                    dataDivValue
                );
            }
            html += "</div>"; //end of datadiv
            html += existingLicenseBlockOnPage;
            html += "</body></html>";
            var bookDom = new HtmlDom(html);
            var bookData = new BookData(bookDom, _collectionSettings, null);

            BookCopyrightAndLicense.UpdateDomFromDataDiv(bookDom, "", bookData, false);
            string valuePredicate;
            if (key == "licenseImage")
            {
                valuePredicate = string.IsNullOrEmpty(dataDivValue)
                    ? "@src=''"
                    : "@src='" + dataDivValue + "'";
            }
            else
            {
                valuePredicate = string.IsNullOrEmpty(dataDivValue)
                    ? "(text()='' or not(text()))"
                    : "text()='" + dataDivValue + "'";
            }
            var xpath =
                "//div[@id='test']/*[@data-derived='" + key + "' and " + valuePredicate + "]";
            if (!string.IsNullOrEmpty(customXPath))
            {
                xpath = customXPath;
            }
            try
            {
                AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath(xpath, 1);
            }
            catch (AssertionException)
            {
                Console.WriteLine("xpath was:" + xpath);
                Assert.Fail(description);
            }
        }

        [Test]
        public void GetOriginalCopyrightAndLicense_NotDerivativeBook_Empty()
        {
            var dom = new HtmlDom(
                @" <div id='bloomDataDiv'>
					<div data-book='bookTitle' lang='en'>A really really empty book</div>
						<div data-book='copyright' lang='*'> Copyright © 2007, Some Old Publisher </div>
						<div data-book='originalTitle' lang='*'> How to manage titles </div>
					</div>"
            );

            Assert.That(GetEnglishOriginalCopyrightAndLicense(dom), Is.Null);
        }

        // Many other cases with original title are tested as part of the test of SetOriginalCopyrightAndLicense.
        [Test]
        public void GetOriginalCopyrightAndLicense_HasOriginalCopyrightAndLicense_NoOriginalTitle_InsertsCiteElementWithMissingClass()
        {
            var dom = new HtmlDom(
                @" <div id='bloomDataDiv'>
					<div data-book='copyright' lang='*'> Copyright © 2007, Foo Publishing </div>
					<div data-book='licenseUrl' lang='*'> http://creativecommons.org/licenses/by-nc/3.0/ </div>
					<div data-book='originalCopyright' lang='*'> Copyright © 2007, Foo Publishing </div>
					<div data-book='originalLicenseUrl' lang='*'> http://creativecommons.org/licenses/by-nc/3.0/ </div>
				</div>"
            );
            Assert.AreEqual(
                "This book is an adaptation of the original, <cite data-book=\"originalTitle\" class=\"missingOriginalTitle\"></cite>, Copyright © 2007, Foo Publishing. Licensed under CC BY-NC 3.0.",
                GetEnglishOriginalCopyrightAndLicense(dom)
            );
        }

        [Test]
        public void GetOriginalCopyrightAndLicense_HasOriginalLicense_NoOriginalCopyrightOrTitle_InsertsCiteElementWithMissingClass()
        {
            var dom = new HtmlDom(
                @" <div id='bloomDataDiv'>
					<div data-book='copyright' lang='*'> Copyright © 2007, Foo Publishing </div>
					<div data-book='licenseUrl' lang='*'> http://creativecommons.org/licenses/by-nc/3.0/ </div>
					<div data-book='originalLicenseUrl' lang='*'> http://creativecommons.org/licenses/by-nc/3.0/ </div>
				</div>"
            );
            Assert.AreEqual(
                "This book is an adaptation of the original without a copyright notice, <cite data-book=\"originalTitle\" class=\"missingOriginalTitle\"></cite>. Licensed under CC BY-NC 3.0.",
                GetEnglishOriginalCopyrightAndLicense(dom)
            );
        }

        [Test]
        public void GetOriginalCopyrightAndLicense_HasAllParts_InFrench()
        {
            var dom = new HtmlDom(
                @" <div id='bloomDataDiv'>
					<div data-book='bookTitle' lang='en'>An interesting book</div>
					<div data-book='originalTitle' lang='*'> How to manage titles </div>
					<div data-book='copyright' lang='*'> Copyright © 2017, Foo-bar Publishing </div>
					<div data-book='licenseUrl' lang='*'> http://creativecommons.org/licenses/by/4.0/ </div>
					<div data-book='originalCopyright' lang='*'> Copyright © 2007, Foo Publishing </div>
					<div data-book='originalLicenseUrl' lang='*'> http://creativecommons.org/licenses/by/4.0/ </div>
				</div>"
            );
            var result = GetFrenchOriginalCopyrightAndLicense(dom);
            // We could try to mock what L10NSharp returns for this one test..., or we could just test that it's not using English.
            Assert.That(result.StartsWith("This book is an adaptation of the original"), Is.False);
            Assert.That(result.Contains("Licensed under CC BY 4.0"), Is.False);
        }

        private string GetFrenchOriginalCopyrightAndLicense(HtmlDom dom)
        {
            // Set these before making the bookData as it will cache during constructor.
            _collectionSettings.Language1.Tag = "en";
            _collectionSettings.Language2.Tag = "fr";
            _collectionSettings.Language1.SetName("English", false);
            _collectionSettings.Language2.SetName("French", false);
            var bookData = new BookData(dom, _collectionSettings, null);
            return BookCopyrightAndLicense.GetOriginalCopyrightAndLicenseNotice(bookData, dom);
        }

        private string GetEnglishOriginalCopyrightAndLicense(HtmlDom dom)
        {
            var bookData = new BookData(dom, _collectionSettings, null);
            return BookCopyrightAndLicense.GetOriginalCopyrightAndLicenseNotice(bookData, dom);
        }
    }
}
