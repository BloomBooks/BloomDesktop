using System;
using System.IO;
using System.Xml;
using Bloom;
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
		private LocalizationManager _localizationManager;
		private LocalizationManager _palasoLocalizationManager;
		private static TemporaryFolder _brandingFolder;
		private string _pathToBrandingSettingJson;

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
				null, "", new string[] {});
			_palasoLocalizationManager = LocalizationManager.Create("fr", "Palaso","Palaso", "1.0.0", localizationDirectory, "SIL/Bloom",
				null, "", new string[] { });

			_brandingFolder = new TemporaryFolder("unitTestBrandingFolder");
			_pathToBrandingSettingJson = _brandingFolder.Combine("settings.json");
		}

		[TearDown]
		public void TearDown()
		{
			_localizationManager.Dispose();
			_palasoLocalizationManager.Dispose();
			_brandingFolder.Dispose();
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
			string dataDivContent= @"<div lang='en' data-book='licenseNotes'>my custom</div>
					<div data-book='copyright' class='bloom-content1'>Copyright © 2012, test</div>";
			Assert.AreEqual("my custom", GetMetadata(dataDivContent).License.RightsStatement);
		}

		[Test]
		public void GetLicenseMetadata_HasOnlyCopyrightAndLicenseNotes_IsCustomLicense()
		{
			string dataDivContent = @"<div lang='en' data-book='licenseNotes'>my custom</div>
					<div data-book='copyright' class='bloom-content1'>Copyright © 2012, test</div>";
			Assert.IsTrue(GetMetadata(dataDivContent).License is CustomLicense);
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
		public void GetLicenseMetadata_HasCCLicenseURLWithIGOQualifier_ConvertedToFulCCLicenseObject()
		{
			//nb: the real testing is done on the palaso class that does the reading, this is just a quick sanity check
			string dataDivContent = @"<div lang='en' data-book='licenseUrl'>http://creativecommons.org/licenses/by/3.0/igo</div>";
			var creativeCommonsLicense = (CreativeCommonsLicense)(GetMetadata(dataDivContent).License);
			Assert.IsTrue(creativeCommonsLicense.AttributionRequired);
			Assert.IsTrue(creativeCommonsLicense.CommercialUseAllowed);
			Assert.IsTrue(creativeCommonsLicense.IntergovernmentalOriganizationQualifier);
		}
		[Test]
		public void GetLicenseMetadata_HasOnlyCopyrightAndDescription_IsNullLicense()
		{
			//nb: the real testing is done on the palaso class that does the reading, this is just a quick sanity check
			var dataDivContent = @"<div lang='en' data-book='licenseDescription'>This could say anything</div>
			<div data-book='copyright' class='bloom-content1'>Copyright © 2012, test</div>";
			Assert.IsTrue(GetMetadata(dataDivContent).License is NullLicense);
		}

		[Test]
		public void GetLicenseMetadata_HasSymbolInCopyright_FullCopyrightStatmentAcquired()
		{
			string dataDivContent = @"<div data-book='copyright' class='bloom-content1'>Copyright © 2012, test</div>";
			Assert.AreEqual("Copyright © 2012, test", GetMetadata(dataDivContent).CopyrightNotice);
		}

		[TestCase("http://creativecommons.org/licenses/by-nc-sa/", "http://creativecommons.org/licenses/by-nc-sa/4.0/")]
		[TestCase("http://creativecommons.org/licenses/by-nc-sa", "http://creativecommons.org/licenses/by-nc-sa/4.0/")]
		[TestCase("http://creativecommons.org/licenses/by/", "http://creativecommons.org/licenses/by/4.0/")]
		[TestCase("http://creativecommons.org/licenses/by", "http://creativecommons.org/licenses/by/4.0/")]
		[TestCase("http://creativecommons.org/licenses/by-nc-sa/4.0/", "http://creativecommons.org/licenses/by-nc-sa/4.0/")]
		[TestCase("http://creativecommons.org/licenses/by-nc-sa/4.0", "http://creativecommons.org/licenses/by-nc-sa/4.0/")]
		//Regression tests:
		[TestCase("http://creativecommons.org/licenses/by/4.0/", "http://creativecommons.org/licenses/by/4.0/")]
		[TestCase("http://creativecommons.org/licenses/by/4.0", "http://creativecommons.org/licenses/by/4.0/")]
		[TestCase("http://creativecommons.org/licenses/by-nc-sa/3.0/", "http://creativecommons.org/licenses/by-nc-sa/3.0/")]
		[TestCase("http://creativecommons.org/licenses/by-nc-sa/3.0", "http://creativecommons.org/licenses/by-nc-sa/3.0/")]
		[TestCase("http://creativecommons.org/licenses/by/3.0/", "http://creativecommons.org/licenses/by/3.0/")]
		[TestCase("http://creativecommons.org/licenses/by/3.0", "http://creativecommons.org/licenses/by/3.0/")]
		public void GetMetadataLicenseUrl_MissingCCVersion_CorrectsToDefaultVersion(string input, string result)
		{
			string dataDivContent = @"<div lang='*' data-book='licenseUrl'>" + input + "</div>";
			Assert.AreEqual(result, GetMetadata(dataDivContent).License.Url);
		}

		[Test]
		public void SetLicenseMetadata_ToNoLicenseUrl_OriginalHasLicenseUrlInEn_ClearsEn()
		{
			string dataDivContent = @"<div lang='en' data-book='licenseUrl'>http://creativecommons.org/licenses/by-nc-sa/3.0/</div>";
			var dom = MakeDom(dataDivContent);
			var creativeCommonsLicense = (CreativeCommonsLicense)(BookCopyrightAndLicense.GetMetadata(dom).License);
			Assert.IsTrue(creativeCommonsLicense.AttributionRequired); // yes, we got a CC license from the 'en' licenseUrl
			var newLicense = new CustomLicense();
			var newMetaData = new Metadata();
			newMetaData.License = newLicense;
			var settings = new CollectionSettings();
			BookCopyrightAndLicense.SetMetadata(newMetaData, dom,  null, settings);
			AssertThatXmlIn.Dom(dom.RawDom).HasNoMatchForXpath("//div[@data-book='licenseUrl']");
		}

		// BRANDING-RELATED TESTS

		[Test]
		public void GetLicenseMetadata_SettingsExistsButIsEmpty_MetadataMatches()
		{
			File.WriteAllText(_pathToBrandingSettingJson,@"{}");
			var dataDivContent = @"";
			var metadata = GetMetadata(dataDivContent);
			Assert.AreEqual("http://creativecommons.org/licenses/by/4.0/", metadata.License.Url, "Expected default CC license");
			Assert.That(metadata.License.RightsStatement, Is.Null.Or.Empty);
			Assert.That(metadata.CopyrightNotice, Is.Null.Or.Empty);
		}
		[Test]
		public void GetLicenseMetadata_SettingsExistsButHasBogusJson_MetadataMatches()
		{
			File.WriteAllText(_pathToBrandingSettingJson, @"");
			var dataDivContent = @"{'foo':'bar'}";
			var metadata = GetMetadata(dataDivContent);
			Assert.AreEqual("http://creativecommons.org/licenses/by/4.0/", metadata.License.Url, "Expected default CC license");
			Assert.That(metadata.License.RightsStatement, Is.Null.Or.Empty);
			Assert.That(metadata.CopyrightNotice, Is.Null.Or.Empty);
		}
		[Test]
		public void GetLicenseMetadata_BrandingHasLicenseAndNotesButNotCopyright_MetadataMatches()
		{
			File.WriteAllText(_pathToBrandingSettingJson,
				@"{
					'LicenseUrl':'http://creativecommons.org/licenses/by/3.0/igo/',
					'LicenseRightsStatement': 'These are custom notes.'
				}");
			var dataDivContent = @"";
			var metadata = GetMetadata(dataDivContent);
			Assert.AreEqual("http://creativecommons.org/licenses/by/3.0/igo/", metadata.License.Url);
			Assert.AreEqual("These are custom notes.", metadata.License.RightsStatement);
			Assert.That(metadata.CopyrightNotice, Is.Null.Or.Empty);
		}

		[Test]
		public void GetLicenseMetadata_HasCopyrightAndLicenseAndLicenseNotes_MetadataMatches()
		{
			File.WriteAllText(_pathToBrandingSettingJson,
				@"{
					'CopyrightNotice':'Copyright © 2016',
					'LicenseUrl':'http://creativecommons.org/licenses/by/3.0/igo/',
					'LicenseRightsStatement': 'These are custom notes.'
				}");
			var dataDivContent = @"";
			var metadata = GetMetadata(dataDivContent);
			Assert.AreEqual("http://creativecommons.org/licenses/by/3.0/igo/", metadata.License.Url);
			Assert.AreEqual("These are custom notes.", metadata.License.RightsStatement);
			Assert.AreEqual("Copyright © 2016", metadata.CopyrightNotice);
		}

		// we don't want shell books to get this notice
		[Test]
		public void GetLicenseMetadata_HasCopyrightAlready_CustomBrandingStuffIgnored()
		{
			File.WriteAllText(_pathToBrandingSettingJson,
				@"{
					'CopyrightNotice':'Copyright © 2016',
					'LicenseUrl':'http://creativecommons.org/licenses/by/3.0/igo/',
					'LicenseRightsStatement': 'These are custom notes.'
				}");
			var dataDivContent = @"<div data-book='copyright' class='bloom-content1'>Copyright © 2012, test</div>";
			var metadata = GetMetadata(dataDivContent);
			Assert.IsTrue(metadata.CopyrightNotice.Contains("2012"));
			Assert.IsTrue(metadata.License is NullLicense);
			Assert.That(metadata.License.RightsStatement, Is.Null.Or.Empty);
		}


		[Test, Ignore("Enable once we have French CC License Localization") /*meanwhile, I have tested on my machine*/]
		public void SetLicenseMetadata_CCLicenseWithFrenchNationalLanguage_DataDivHasFrenchDescription()
		{
			_collectionSettings.Language1Iso639Code = "fr";
			_collectionSettings.Language2Iso639Code = "en";

			TestSetLicenseMetdataEffectOnDataDiv(new Metadata()
				{
					CopyrightNotice = "foo",
					License = new CreativeCommonsLicense(true, true, CreativeCommonsLicense.DerivativeRules.Derivatives)
				},
				startingDataDivContent: "",
				xpath: "//*[@data-book='licenseDescription' and @lang='fr' and contains(text(),'Vous')]", expectedCount: 1);
		}

		[Test]
		public void SetLicenseMetadata_CCLicense_LicenseImageAddedToDataDiv()
		{
			TestSetLicenseMetdataEffectOnDataDiv(new Metadata()
			{
				CopyrightNotice = "foo",
				License = new CreativeCommonsLicense(true,true, CreativeCommonsLicense.DerivativeRules.Derivatives)
			},
			startingDataDivContent: "",
			xpath: "//*[@data-book='licenseImage' and text()='license.png']",
			expectedCount: 1);
		}
		[Test]
		public void SetLicenseMetadata_CustomLicense_LicenseImageRemovedFromDataDiv()
		{
			TestSetLicenseMetdataEffectOnDataDiv(new Metadata()
			{
				CopyrightNotice = "foo",
				License = new CustomLicense()
			},
			startingDataDivContent: "<div data-book='licenseImage' lang='*'>license.png</div>",
			xpath: "//*[@data-book='licenseImage']",
			expectedCount: 0);
		}
		[Test]
		public void SetLicenseMetadata_NullLicense_LicenseImageRemovedFromDataDiv()
		{
			TestSetLicenseMetdataEffectOnDataDiv(new Metadata()
			{
				CopyrightNotice = "foo",
				License = new NullLicense()
			},
			startingDataDivContent: "<div data-book='licenseImage' lang='*'>license.png</div>",
			xpath: "//*[@data-book='licenseImage']",
			expectedCount: 0);
		}
		[Test]
		public void SetLicenseMetadata_PreviouslyHadCCLicenseInFrenchThenChangedToCustom_OnlyShowsCustomRightsStatement()
		{
			_collectionSettings.Language1Iso639Code = "fr";
			// This will probably improve in the future, but for now, the custom rights statement does not have a language.
			// This test makes sure that we don't leave obsolete descriptions around in a preferred language.
			var dom = TestSetLicenseMetdataEffectOnDataDiv(new Metadata()
			{
				CopyrightNotice = "foo",
				License = new CustomLicense() { RightsStatement = "custom rights"}
			},
			startingDataDivContent: "<div data-book='licenseDescription' lang='fr'>Some old French</div>",
			xpath: "//*[@data-book='licenseDescription']",
			expectedCount: 1);

			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//*[@data-book='licenseDescription' and @lang='fr']",0);
		}
		[Test]
		public void SetMetadata_CustomLicense_LicenseImageSrcAndAltAreEmpty()
		{
			TestSetLicenseMetdataEffectOnDataDiv(new Metadata()
			{
				CopyrightNotice = "foo",
				License = new CustomLicense()
			},
			startingPageContent: "<img data-derived='licenseImage' lang='*' alt='This picture, license.png, is missing or was loading too slowly.'>license.png</img>",
			xpath: "//img[@data-derived='licenseImage' and (not(@alt) or @alt='') and @src='']",
			expectedCount: 1);
		}
		private  HtmlDom TestSetLicenseMetdataEffectOnDataDiv(Metadata metadata = null, string startingDataDivContent = "", string startingPageContent = "", string xpath = "", int expectedCount = 1)
		{
			var dom = new HtmlDom(@"<html><head><div id='bloomDataDiv'>" + startingDataDivContent + "</div><div id='credits'>" + startingPageContent + "</div></head><body></body></html>");
			Bloom.Book.BookCopyrightAndLicense.SetMetadata(metadata, dom, null, _collectionSettings);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath(xpath, expectedCount);
			return dom;
		}

		private static Metadata GetMetadata(string dataDivContent)
		{
			var dom = MakeDom(dataDivContent);
			//normally, the branding is just a name, which we look up in the official branding folder
			//but in order to allow unit tests to test particular contents of it, we also allow
			//it to be a path to a constructed branding folder.
			//These unit tests can then write to a temp file and point to that.
			return BookCopyrightAndLicense.GetMetadata(dom, _brandingFolder.Path);
		}

		private static HtmlDom MakeDom(string dataDivContent)
		{
			return new HtmlDom(@"<html><head><div id='bloomDataDiv'>" + dataDivContent + "</div></head><body></body></html>");
		}

		[Test]
		public void CheckDataDivToPagePropagation_Copyright()
		{
			CheckUpdateDomFromDataDiv("copyright", null,description:"if copyright is not in datadiv, on page the corresponding element should be empty");
			CheckUpdateDomFromDataDiv("copyright", "", description: "if copyright is empty datadiv, on page the corresponding element should be empty");
			CheckUpdateDomFromDataDiv("copyright", "copyright correct, 1996", description: "if copyright is in datadiv, on page the corresponding element should be a copy");
		}
		[Test]
		public void CheckDataDivToPagePropagation_LicenseUrl()
		{
			CheckUpdateDomFromDataDiv("licenseUrl", null,description: "if licenseUrl is not in datadiv, on page the corresponding element should be empty");
			CheckUpdateDomFromDataDiv("licenseUrl", "", description: "if licenseUrl is empty datadiv, on page the corresponding element should be empty");
			CheckUpdateDomFromDataDiv("licenseUrl", "example.com", description: "if licenseUrl is in datadiv, on page the corresponding element should be a copy");
		}
		[Test]
		public void CheckDataDivToPagePropagation_LicenseNotes()
		{
			CheckUpdateDomFromDataDiv("licenseNotes", null, description: "if licenseNotes is not in datadiv, on page the corresponding element should be empty");
			CheckUpdateDomFromDataDiv("licenseNotes", "", description: "if licenseNotes is empty datadiv, on page the corresponding element should be empty");
			CheckUpdateDomFromDataDiv("licenseNotes", "some notes", description: "if licenseNotes is in datadiv, on page the corresponding element should be a copy");
			CheckUpdateDomFromDataDiv("licenseNotes", "line 1<br />line 2", description: "can include br in license notes", customXPath:"//div[@id='test']/div/br");
		}
		[Test]
		public void CheckDataDivToPagePropagation_LicenseDescription()
		{
			CheckUpdateDomFromDataDiv("licenseDescription", null, description: "if licenseDescription is not in datadiv, on page the corresponding element should be empty");
			CheckUpdateDomFromDataDiv("licenseDescription", "", description: "if licenseDescription is empty datadiv, on page the corresponding element should be empty");
			CheckUpdateDomFromDataDiv("licenseDescription", "some Description", description: "if licenseDescription is in datadiv, on page the corresponding element should be a copy");
			CheckUpdateDomFromDataDiv("licenseDescription", "line 1<br />line 2", description: "can include br in description", customXPath: "//div[@id='test']/div/br");
		}
		[Test]
		public void CheckDataDivToPagePropagation_LicenseImage()
		{
			CheckUpdateDomFromDataDiv("licenseImage", null, description: "if licenseImage is not in datadiv, on page the img element should have an empty @src and empty @alt");
			CheckUpdateDomFromDataDiv("licenseImage", "", description: "if licenseImage has empty @src in datadiv, on page the img element should have an empty @src and empty @alt");
			CheckUpdateDomFromDataDiv("licenseImage", "something.png", description: "if licenseImage is in datadiv, on page the img element should have the @src filled with the url");
		}


		[Test]
		public void UpdateDomFromDataDiv_CCLicense_OnPageTheLicenseHasFrench()
		{
			_collectionSettings.Language1Iso639Code = "fr";
			_collectionSettings.Language2Iso639Code = "en";

			//NB: ideally, this test would just set the licenseUrl and then test the resulting description.
			//That is, the description would not even be in the datadiv, since all we need is the licenseURl
			//(at least for creative commons licenses). We would then just generate the description when we
			//update the page.
			//However, for backwards compatibility, we still (as of 3.6) determine the description when doing
			//a SetMetadata, put the description in the bloomdatadiv, and then it just flows down
			//to the page.
			var html = @"<html><body>
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

			BookCopyrightAndLicense.UpdateDomFromDataDiv(bookDom, "", _collectionSettings);
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='test']/*[@data-derived='licenseDescription' and @lang='fr' and contains(text(),'French')]", 1);
		}

		[Test]
		public void UpdateDomFromDataDiv_CopiesCopyrightAndOriginalCopyrightToMultipleDestinations()
		{
			// We could test other fields too, but these are enough to cover the two main methods that do the copying.
			var html = @"<html><head><meta name='lockedDownAsShell' content='true'></meta></head><body>
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

			BookCopyrightAndLicense.UpdateDomFromDataDiv(bookDom, "", _collectionSettings);
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@class='test']/*[@data-derived='originalCopyrightAndLicense' and @lang='*' and contains(text(),'Adapted from original, Copyright © 2007, Foo Publishers. Licensed under CC-BY-NC 4.0. You can do anything you want if your name is Fred.')]", 2);
			AssertThatXmlIn.Dom(bookDom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@class='test']/*[@data-derived='copyright' and @lang='*' and contains(text(),'Copyright © 2008, Bar Publishers')]", 2);
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
		private void CheckUpdateDomFromDataDiv(string key, string dataDivValue,  string lang1="en", string lang2="", string lang3="", string description=null, string customXPath=null)
		{
			if (description == null)
				description = string.Format("{0} should be '{1}'", key, dataDivValue);

			_collectionSettings.Language1Iso639Code = lang1;
			_collectionSettings.Language2Iso639Code = lang2;
			_collectionSettings.Language3Iso639Code = lang3;

			var existingLicenseBlockOnPage = @"<div id='test'>
						<div data-derived = 'copyright' lang='en'>Some Copyright</div>
						<img src='license.png' alt='blah blah' data-derived='licenseImage'/>
						<div data-derived = 'licenseUrl' lang='en'>Boilerplate.com</div>
						<div data-derived='licenseDescription' lang='en'>BoilerPlateDescription</div>
						<div data-derived='licenseNotes' lang='en'>BoilerPlateNotes</div>
					</div>";

			string html= "<html><body><div id='bloomDataDiv'>";
			if (dataDivValue != null) //we want this even if it is empty, just not null
			{
					html += string.Format("<{0} data-book='{1}' lang='en'>{2}</{0}>","div",key,dataDivValue);
			}
			html += "</div>";//end of datadiv
			html += existingLicenseBlockOnPage;
			html += "</body></html>";
			var bookDom = new HtmlDom(html);

			BookCopyrightAndLicense.UpdateDomFromDataDiv(bookDom,"", _collectionSettings);
			string valuePredicate;
			if (key == "licenseImage")
			{
				valuePredicate = string.IsNullOrEmpty(dataDivValue) ? "@src=''" : "@src='" + dataDivValue + "'";
			}
			else
			{
				valuePredicate = string.IsNullOrEmpty(dataDivValue) ? "(text()='' or not(text()))" : "text()='" + dataDivValue + "'";
			}
			var xpath = "//div[@id='test']/*[@data-derived='" + key + "' and " + valuePredicate + "]";
			if(!string.IsNullOrEmpty(customXPath))
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

		void AssertOriginalCopyrightAndLicense(HtmlDom dom, string copyright, string license, string licenseNotes = "")
		{
			Assert.That(dom.GetBookSetting("originalCopyright")["*"], Is.EqualTo(copyright));
			if (string.IsNullOrEmpty(copyright))
				AssertThatXmlIn.Dom(dom.RawDom).HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-book='originalCopyright']");
			Assert.That(dom.GetBookSetting("originalLicenseUrl")["*"], Is.EqualTo(license));
			if (string.IsNullOrEmpty(license))
				AssertThatXmlIn.Dom(dom.RawDom).HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-book='originalLicenseUrl']");
			Assert.That(dom.GetBookSetting("originalLicenseNotes")["*"], Is.EqualTo(licenseNotes));
			if (string.IsNullOrEmpty(licenseNotes))
				AssertThatXmlIn.Dom(dom.RawDom).HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-book='originalLicenseNotes']");
		}

		[Test]
		public void SetOriginalCopyrightAndLicense_HasCopyrightAndLicense_MovesToOriginalCopyrightAndLicense()
		{
			var dom = SetOriginalCopyrightAndLicense(
				@" <div id='bloomDataDiv'>
					<div data-book='copyright' lang='*'> Copyright © 2007, Foo Publishing </div>
					<div data-book='licenseUrl' lang='*'> http://creativecommons.org/licenses/by-nc/3.0/ </div>
				</div>");
			Assert.AreEqual("Adapted from original, Copyright © 2007, Foo Publishing. Licensed under CC-BY-NC 3.0.", GetEnglishOriginalCopyrightAndLicense(dom));
			AssertOriginalCopyrightAndLicense(dom, "Copyright © 2007, Foo Publishing", "http://creativecommons.org/licenses/by-nc/3.0/");
		}

		[Test]
		public void SetOriginalCopyrightAndLicense_HasCopyrightAndLicenseAndExtra_CopyrightIsNowEmpty()
		{
			var dom = SetOriginalCopyrightAndLicense(
				@" <div id='bloomDataDiv'>
					<div data-book='bookTitle' lang='en'>A really really empty book</div>
						<div data-book='copyright' lang='*'> Copyright © 2007, Foo Publishing </div>
						<div data-book='licenseUrl' lang='*'>
						http://creativecommons.org/licenses/by/4.0/
						</div>
					</div>");
			AssertThatXmlIn.Dom(dom.RawDom).HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-book='copyright']");
		}

		[Test]
		public void SetOriginalCopyrightAndLicense_HasCreativeCommonsLicenseAndNotes_NotesRetainedInOriginal()
		{
			var dom = SetOriginalCopyrightAndLicense(
				@" <div id='bloomDataDiv'>
					<div data-book='bookTitle' lang='en'>A really really empty book</div>
						<div data-book='licenseUrl' lang='*'>
						http://creativecommons.org/licenses/by/4.0/
						</div>
					<div data-derived='licenseNotes' lang='*'>
						You can do anything you want if your name is Fred.
					</div>
				</div>");
			Assert.AreEqual("Adapted from original without a copyright notice. Licensed under CC-BY 4.0. You can do anything you want if your name is Fred.", GetEnglishOriginalCopyrightAndLicense(dom));
			AssertOriginalCopyrightAndLicense(dom, "", "http://creativecommons.org/licenses/by/4.0/", "You can do anything you want if your name is Fred.");
		}
		[Test]
		public void SetOriginalCopyrightAndLicense_HasCustomLicenseAndNotes_NotesRetainedInOriginal()
		{
			var dom = SetOriginalCopyrightAndLicense(
				@" <div id='bloomDataDiv'>
					<div data-book='bookTitle' lang='en'>A really really empty book</div>
					<div data-derived='licenseNotes' lang='*'>
						You can do anything you want if your name is Fred.
					</div>
				</div>");
			Assert.AreEqual("Adapted from original without a copyright notice. You can do anything you want if your name is Fred.", GetEnglishOriginalCopyrightAndLicense(dom));
			AssertOriginalCopyrightAndLicense(dom, "", "", "You can do anything you want if your name is Fred.");
		}

		// It's rather arbitrary what happens in this case, since we don't think it's possible for the original
		// to have no license (short of hand-editing the HTML).
		[Test]
		public void SetOriginalCopyrightAndLicense_HasNoCopyrightOrLicense_GetsWithoutCopyrightNotice()
		{
			var dom = SetOriginalCopyrightAndLicense(@" <div id='bloomDataDiv'>
					  <div data-book='bookTitle' lang='en'>A really really empty book</div>
					</div>");
			Assert.AreEqual("Adapted from original without a copyright notice.", GetEnglishOriginalCopyrightAndLicense(dom));
			AssertOriginalCopyrightAndLicense(dom, "", "", "");
		}

		[Test]
		public void SetOriginalCopyrightAndLicense_HasNoCopyrightOrLicense_SourceCollection_GetsNoOriginalNotice()
		{
			try
			{
				_collectionSettings.IsSourceCollection = true;
				var dom = SetOriginalCopyrightAndLicense(@" <div id='bloomDataDiv'>
					  <div data-book='bookTitle' lang='en'>A really really empty book</div>
					</div>");
				Assert.AreEqual(null, GetEnglishOriginalCopyrightAndLicense(dom));
				AssertOriginalCopyrightAndLicense(dom, "", "", "");
			}
			finally
			{
				_collectionSettings.IsSourceCollection = false;
			}
		}

		[Test]
		public void SetOriginalCopyrightAndLicense_HasLicenseButNoCopyright_GetsExpectedOriginalCopyrightAndLicense()
		{
			var dom = SetOriginalCopyrightAndLicense(
				@" <div id='bloomDataDiv'>
					<div data-book='bookTitle' lang='en'>A really really empty book</div>
						<div data-book='licenseUrl' lang='*'>
						http://creativecommons.org/licenses/by/4.0/
						</div>
					</div>");
			Assert.AreEqual("Adapted from original without a copyright notice. Licensed under CC-BY 4.0.", GetEnglishOriginalCopyrightAndLicense(dom));
			AssertOriginalCopyrightAndLicense(dom, "", "http://creativecommons.org/licenses/by/4.0/");
		}

		[Test]
		public void SetOriginalCopyrightAndLicense_NoLicense_GetsExpectedOriginalCopyrightAndLicense()
		{
			var dom = SetOriginalCopyrightAndLicense(
				@" <div id='bloomDataDiv'>
					<div data-book='bookTitle' lang='en'>A really really empty book</div>
						<div data-book='copyright' lang='*'> Copyright © 2007, Some Old Publisher </div>
					</div>");

			Assert.AreEqual("Adapted from original, Copyright © 2007, Some Old Publisher.", GetEnglishOriginalCopyrightAndLicense(dom));
			AssertOriginalCopyrightAndLicense(dom, "Copyright © 2007, Some Old Publisher", "", "");
		}

		// when we use a translation, it may have its own copyright. However that doesn't mean that we ever replace
		// the "original" copyright and license. Those stick with the book through all adaptations.
		[Test]
		public void SetOriginalCopyrightAndLicense_SourceIsAlsoAnAdaptation_OriginalCopyrightAndLicensePreserved()
		{
			var dom = SetOriginalCopyrightAndLicense(
				@" <div id='bloomDataDiv'>
					<div data-book='bookTitle' lang='en'>A really really empty book</div>
						<div data-book='copyright' lang='*'>
						Copyright © 2007, Foo Publishers
						</div>
					<div data-book='licenseUrl' lang='*'>
						http://creativecommons.org/licenses/by/4.0/
						</div>
						<div data-derived='licenseNotes' lang='*'>
							You can do anything you want if your name is Fred.
						</div>
					</div>");
			// now do it again, simulating adaptation from the translation with different copyright etc.
			var dataDiv = dom.SelectSingleNode("//div[@id='bloomDataDiv']");
			AppendDataDivElement(dataDiv, "copyright", "*", "Copyright © 2008, Bar Translators");
			AppendDataDivElement(dataDiv, "licenseUrl", "*", "http://creativecommons.org/licenses/by-nc/4.0/");
			AppendDataDivElement(dataDiv, "licenseNotes", "*", "You can do almost anything if your name is John");
			var bookData = new BookData(dom, _collectionSettings, null);
			BookCopyrightAndLicense.SetOriginalCopyrightAndLicense(dom, bookData, _collectionSettings);
			Assert.AreEqual("Adapted from original, Copyright © 2007, Foo Publishers. Licensed under CC-BY 4.0. You can do anything you want if your name is Fred.", GetEnglishOriginalCopyrightAndLicense(dom));
			AssertOriginalCopyrightAndLicense(dom, "Copyright © 2007, Foo Publishers", "http://creativecommons.org/licenses/by/4.0/", "You can do anything you want if your name is Fred.");
		}

		[Test]
		public void GetOriginalCopyrightAndLicense_NotDerivativeBook_Empty()
		{
			var dom = new HtmlDom(
				@" <div id='bloomDataDiv'>
					<div data-book='bookTitle' lang='en'>A really really empty book</div>
						<div data-book='copyright' lang='*'> Copyright © 2007, Some Old Publisher </div>
					</div>");

			Assert.That(GetEnglishOriginalCopyrightAndLicense(dom), Is.Null);
		}

		void AppendDataDivElement(XmlElement dataDiv, string dataBook, string lang, string val)
		{
			var newDiv = dataDiv.OwnerDocument.CreateElement("div");
			newDiv.SetAttribute("data-book", dataBook);
			newDiv.SetAttribute("lang", lang);
			newDiv.InnerText = val;
			dataDiv.AppendChild(newDiv);
		}

		[Test]
		public void AmpersandInOriginalCopyrightHandledProperly()
		{
			// See http://issues.bloomlibrary.org/youtrack/issue/BL-4585.
			var dom = new HtmlDom(
				@"<html>
				  <head><meta name='lockedDownAsShell' content='true'></meta></head>
				  <body>
				    <div id='bloomDataDiv'>
				      <div data-book='copyright' lang='*'>
				        Copyright © 2011, LASI &amp; SILA
				      </div>
				      <div data-book='licenseUrl' lang='en'>
				        http://creativecommons.org/licenses/by-nc-sa/4.0/
				      </div>
				    </div>
				    <div class='bloom-page cover frontCover outsideFrontCover coverColor bloom-frontMatter A4Landscape layout-style-Default' data-page='required singleton' data-export='front-matter-cover' id='2c97f5ad-24a1-47f0-8b5c-fa2181e1b129'>
				      <div class='bloom-page cover frontCover outsideFrontCover coverColor bloom-frontMatter verso A4Landscape layout-style-Default' data-page='required singleton' data-export='front-matter-credits' id='7a220c97-07e4-47c5-835a-e37dc921f98f'>
				        <div class='marginBox'>
				          <div data-functiononhintclick='bookMetadataEditor' data-hint='Click to Edit Copyright &amp; License' id='versoLicenseAndCopyright' class='bloom-metaData'>
				            <div data-derived='copyright' lang='*' class='copyright'></div>
				            <div data-derived='originalCopyrightAndLicense' lang='en' class='copyright'></div>
				          </div>
				        </div>
				      </div>
				    </div>
				  </body>
				</html>");
			var metadata = BookCopyrightAndLicense.GetMetadata(dom);
			var initialCopyright = metadata.CopyrightNotice;
			Assert.AreEqual("Copyright © 2011, LASI & SILA", initialCopyright);

			var bookData = new BookData(dom, _collectionSettings, null);
			BookCopyrightAndLicense.SetOriginalCopyrightAndLicense(dom, bookData, _collectionSettings);
			var originalCopyright = GetEnglishOriginalCopyrightAndLicense(dom);
			Assert.AreEqual("Adapted from original, Copyright © 2011, LASI & SILA. Licensed under CC-BY-NC-SA 4.0.", originalCopyright);

			BookCopyrightAndLicense.UpdateDomFromDataDiv(dom, null, _collectionSettings);
			var nodes1 = dom.RawDom.SelectNodes("/html/body//div[@data-derived='originalCopyrightAndLicense']");
			Assert.AreEqual(1, nodes1.Count);
			Assert.AreEqual("Adapted from original, Copyright © 2011, LASI & SILA. Licensed under CC-BY-NC-SA 4.0.", nodes1.Item(0).InnerText);
			Assert.AreEqual("Adapted from original, Copyright © 2011, LASI &amp; SILA. Licensed under CC-BY-NC-SA 4.0.", nodes1.Item(0).InnerXml);
			var nodes2 = dom.RawDom.SelectNodes("/html/body//div[@data-derived='copyright']");
			Assert.AreEqual(1, nodes2.Count);
			Assert.AreEqual("", nodes2.Item(0).InnerText);
			Assert.AreEqual("", nodes2.Item(0).InnerXml);
			AssertOriginalCopyrightAndLicense(dom, "Copyright © 2011, LASI &amp; SILA", "http://creativecommons.org/licenses/by-nc-sa/4.0/");
		}

		private HtmlDom SetOriginalCopyrightAndLicense(string dataDivString)
		{
			// All of the tests using this method require that the book is locked down (that is, a derivative that
			// is expected to have original copyright and license information).
			var html = "<html><head><meta name='lockedDownAsShell' content='true'></meta></head><body>" + dataDivString + "</body></html>";
			var dom = new HtmlDom(html);
			var bookData = new BookData(dom, _collectionSettings, null);
			BookCopyrightAndLicense.SetOriginalCopyrightAndLicense(dom, bookData, _collectionSettings);
			return dom;
		}

		private string GetEnglishOriginalCopyrightAndLicense(HtmlDom dom)
		{
			return BookCopyrightAndLicense.GetOriginalCopyrightAndLicenseNotice(_collectionSettings, dom);
		}

		private static void OriginalCopyrightAndLicenseMustMatchXpath(HtmlDom dom, string xpath)
		{
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath(xpath, 1);
		}
	}
}
