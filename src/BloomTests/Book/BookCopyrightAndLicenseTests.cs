using System;
using Bloom.Book;
using Bloom.Collection;
using L10NSharp;
using NUnit.Framework;
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
            _collectionSettings = new CollectionSettings(
                new NewCollectionSettings()
                {
                    PathToSettingsFile = CollectionSettings.GetPathForNewSettings(
                        new TemporaryFolder("BookDataTests").Path,
                        "test"
                    ),
                    Language1Tag = "xyz",
                    Language2Tag = "en",
                    Language3Tag = "fr"
                }
            );
            ErrorReport.IsOkToInteractWithUser = false;

            LocalizationManager.UseLanguageCodeFolders = true;
            var localizationDirectory =
                FileLocationUtilities.GetDirectoryDistributedWithApplication("localization");
            _localizationManager = LocalizationManager.Create(
                TranslationMemory.XLiff,
                "fr",
                "Bloom",
                "Bloom",
                "1.0.0",
                localizationDirectory,
                "SIL/Bloom",
                null,
                "",
                new string[] { }
            );
            _palasoLocalizationManager = LocalizationManager.Create(
                TranslationMemory.XLiff,
                "fr",
                "Palaso",
                "Palaso",
                "1.0.0",
                localizationDirectory,
                "SIL/Bloom",
                null,
                "",
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
            Assert.True(GetMetadata(dataDivContent).License is CreativeCommonsLicense);
        }

        [Test]
        public void GetLicenseMetadata_HasCustomLicense_RightsStatementContainsCustom()
        {
            string dataDivContent =
                @"<div lang='en' data-book='licenseNotes'>my custom</div>
					<div data-book='copyright' class='bloom-content1'>Copyright © 2012, test</div>";
            Assert.AreEqual("my custom", GetMetadata(dataDivContent).License.RightsStatement);
        }

        [Test]
        public void GetLicenseMetadata_HasOnlyCopyrightAndLicenseNotes_IsCustomLicense()
        {
            string dataDivContent =
                @"<div lang='en' data-book='licenseNotes'>my custom</div>
					<div data-book='copyright' class='bloom-content1'>Copyright © 2012, test</div>";
            Assert.IsTrue(GetMetadata(dataDivContent).License is CustomLicense);
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
                    == CreativeCommonsLicense.DerivativeRules.DerivativesWithShareAndShareAlike
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
			<div data-book='copyright' class='bloom-content1'>Copyright © 2012, test</div>";
            Assert.IsTrue(GetMetadata(dataDivContent).License is NullLicense);
        }

        [Test]
        public void GetLicenseMetadata_HasSymbolInCopyright_FullCopyrightStatmentAcquired()
        {
            string dataDivContent =
                @"<div data-book='copyright' class='bloom-content1'>Copyright © 2012, test</div>";
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

        [
            Test,
            Ignore("Enable once we have French CC License Localization") /*meanwhile, I have tested on my machine*/
        ]
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
                        CreativeCommonsLicense.DerivativeRules.Derivatives
                    )
                },
                startingDataDivContent: "",
                xpath: "//*[@data-book='licenseDescription' and @lang='fr' and contains(text(),'Vous')]",
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
                        CreativeCommonsLicense.DerivativeRules.Derivatives
                    )
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
                    License = new CustomLicense() { RightsStatement = "custom rights" }
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
                startingPageContent: "<img data-derived='licenseImage' lang='*' alt='This picture, license.png, is missing or was loading too slowly.'>license.png</img>",
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
            Bloom.Book.BookCopyrightAndLicense.SetMetadata(metadata, dom, null, bookData, false);
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
