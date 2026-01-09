using System;
using System.IO;
using System.Xml.Linq;
using Bloom.Book;
using BloomTemp;
using NUnit.Framework;

namespace BloomTests.Book
{
    [TestFixture]
    public class CollectionSettingsReconstructorTests
    {
        private CollectionSettingsReconstructor _trilingualAnalyzer;
        private CollectionSettingsReconstructor _bilingualAnalyzer;
        private CollectionSettingsReconstructor _monolingualBookInBilingualCollectionAnalyzer;
        private CollectionSettingsReconstructor _monolingualBookInTrilingualCollectionAnalyzer;
        private CollectionSettingsReconstructor _bilingualBookInTrilingualCollectionAnalyzer;
        private CollectionSettingsReconstructor _emptyBrandingAnalyzer;
        private CollectionSettingsReconstructor _silleadBrandingAnalyzer;
        private XElement _twoLanguageCollection;
        private XElement _threeLanguageCollection;
        private XElement _threeLanguageCollectionUsingTwo;
        private XElement _silleadCollection;

        private const string kHtml =
            @"
<html>
	<head>
	</head>

	<body>
		<div id='bloomDataDiv'>
			{0}
		</div>

	    <div class='bloom-page cover coverColor bloom-frontMatter frontCover outsideFrontCover side-right A5Portrait' data-page='required singleton' data-export='front-matter-cover' data-xmatter-page='frontCover' id='923f450f-87dc-4fe8-829a-bf9cfe98ac6f' data-page-number=''>
			<div class='pageLabel' lang='en' data-i18n='TemplateBooks.PageLabel.Front Cover'>
				Front Cover
			</div>
			<div class='pageDescription' lang='en'></div>

            <div class='bloom-translationGroup bookTitle' data-default-languages='V,N1'>
				<div class='bloom-editable bloom-nodefaultstylerule Title-On-Cover-style bloom-padForOverflow' lang='z' contenteditable='true' data-book='bookTitle'></div>

				<div class='bloom-editable bloom-nodefaultstylerule Title-On-Cover-style bloom-padForOverflow bloom-content1 bloom-visibility-code-on' lang='xk' contenteditable='true' data-book='bookTitle'>
					<p>My Title</p>
				</div>

				<div class='bloom-editable bloom-nodefaultstylerule Title-On-Cover-style bloom-padForOverflow bloom-content2 bloom-contentNational1 bloom-visibility-code-on' lang='fr' contenteditable='true' data-book='bookTitle'>
					<p>My Title in the National Language</p>
				</div>
				<div class='bloom-editable bloom-nodefaultstylerule Title-On-Cover-style bloom-padForOverflow {1}' lang='de' contenteditable='true' data-book='bookTitle'></div>
			</div>
	    </div>
	</body>
</html>";

        private string kHtml2 =
            @"
<html>
	<head></head>
	<body>
		<div id='bloomDataDiv'>
			<div data-book=""contentLanguage1"" lang=""*"">cmo-KH</div>
			<div data-book=""contentLanguage2"" lang=""*"">km</div>
		</div>
		<div class=""bloom-page cover coverColor bloom-frontMatter frontCover outsideFrontCover side-right Device16x9Portrait"" data-page=""required singleton"" data-export=""front-matter-cover"" data-xmatter-page=""frontCover"" id=""43b20336-73be-4d49-8dac-e15ac7f74f10"" lang=""en"" data-page-number="""">
			<div class=""pageLabel"" lang=""en"" data-i18n=""TemplateBooks.PageLabel.Front Cover"">
				Front Cover
			</div>
			<div class=""pageDescription"" lang=""en""></div>
			<div class=""marginBox"">
				<div data-book=""cover-branding-top-html"" lang=""*""></div>
				<div class=""bloom-translationGroup bookTitle"" data-default-languages=""V,N1"">
					<label class=""bubble"">Book title in {lang}</label>
					<div class=""bloom-editable bloom-nodefaultstylerule Title-On-Cover-style bloom-padForOverflow"" lang=""z"" contenteditable=""true"" data-book=""bookTitle""></div>
					<div class=""bloom-editable bloom-nodefaultstylerule Title-On-Cover-style bloom-padForOverflow bloom-content1 bloom-visibility-code-on"" lang=""cmo-KH"" contenteditable=""true"" data-book=""bookTitle"" style=""padding-bottom: 0px;"">
						<p>០៥ ងក៝ច​នាវ វាក្រាក់</p>
					</div>
					<div class=""bloom-editable bloom-nodefaultstylerule Title-On-Cover-style bloom-padForOverflow bloom-contentNational1 bloom-visibility-code-on"" lang=""en"" contenteditable=""true"" data-book=""bookTitle"" style=""padding-bottom: 0px;"">
						<p>The story of Uncle Krak</p>
					</div>
				</div>
			</div>
		</div>
		<div class=""bloom-page numberedPage customPage bloom-combinedPage side-right Device16x9Portrait bloom-bilingual"" data-page="""" id=""57d6a153-9a1b-4ce7-bd89-e4ef26a58336"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""1"" lang="""">
			<div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Basic Text"" lang=""en"">
				Basic Text
			</div>
			<div class=""pageDescription"" lang=""en""></div>
			<div class=""marginBox"">
				<div class=""bloom-translationGroup bloom-trailingElement bloom-vertical-align-center"" data-default-languages=""auto"" data-hasqtip=""true"">
					<div class=""bloom-editable BigText-style bloom-content1 bloom-visibility-code-on"" style=""min-height: 43.2px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""Bunong"" lang=""cmo-KH"" contenteditable=""true"">
						<p>ពាង់​រាញា​វាក្រាក់។</p>
					</div>
					<div class=""bloom-editable normal-style bloom-content2 bloom-contentNational2 bloom-visibility-code-on"" style=""min-height: 33.6px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""Khmer"" lang=""km"" contenteditable=""true"">
						<p>គាត់ឈ្មោះ​អ៊ុំក្រាក់។​</p>
					</div>
				</div>
			</div>
		</div>
	</body>
</html>";

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // Insert the appropriate contentLanguageX/bloom-content*X information for the different types of books
            _trilingualAnalyzer = new CollectionSettingsReconstructor(
                GetTriLingualHtml(),
                GetMetaData()
            );

            _bilingualAnalyzer = new CollectionSettingsReconstructor(
                GetBiLingualHtml(),
                GetMetaData()
            );

            var monoLingualHtml = GetMonoLingualHtml();
            _emptyBrandingAnalyzer = new CollectionSettingsReconstructor(
                monoLingualHtml,
                GetMetaData(@"""brandingProjectName"":"""",")
            );
            _silleadBrandingAnalyzer = new CollectionSettingsReconstructor(
                monoLingualHtml,
                GetMetaData(@"""brandingProjectName"":""SIL-LEAD"",")
            );
            _monolingualBookInTrilingualCollectionAnalyzer = new CollectionSettingsReconstructor(
                monoLingualHtml,
                GetMetaData()
            );

            var monoLingualBookInBilingualCollectionHtml = String
                .Format(kHtml, kContentLanguage1Xml, "")
                .Replace("bloom-content2 ", "");
            _monolingualBookInBilingualCollectionAnalyzer = new CollectionSettingsReconstructor(
                monoLingualBookInBilingualCollectionHtml,
                GetMetaData()
            );

            _bilingualBookInTrilingualCollectionAnalyzer = new CollectionSettingsReconstructor(
                kHtml2,
                GetMetaData2()
            );

            _twoLanguageCollection = XElement.Parse(
                _monolingualBookInBilingualCollectionAnalyzer.BloomCollection
            );
            _threeLanguageCollection = XElement.Parse(
                _monolingualBookInTrilingualCollectionAnalyzer.BloomCollection
            );
            _threeLanguageCollectionUsingTwo = XElement.Parse(
                _bilingualBookInTrilingualCollectionAnalyzer.BloomCollection
            );
            _silleadCollection = XElement.Parse(_silleadBrandingAnalyzer.BloomCollection);
        }

        private const string kContentClassForLanguage3 = "bloom-contentNational2";
        private const string kContentLanguage1Xml =
            "<div data-book='contentLanguage1' lang='*'>xk</div>";
        private const string kContentLanguage2Xml =
            "<div data-book='contentLanguage2' lang='*'>fr</div>";
        private const string kContentLanguage3Xml =
            "<div data-book='contentLanguage3' lang='*'>de</div>";

        public static string GetTriLingualHtml()
        {
            return String.Format(
                kHtml,
                kContentLanguage1Xml + kContentLanguage2Xml + kContentLanguage3Xml,
                kContentClassForLanguage3
            );
        }

        private string GetBiLingualHtml()
        {
            return String.Format(
                kHtml,
                kContentLanguage1Xml + kContentLanguage2Xml,
                kContentClassForLanguage3
            );
        }

        private string GetMonoLingualHtml()
        {
            return String
                .Format(kHtml, kContentLanguage1Xml, kContentClassForLanguage3)
                .Replace("bloom-content2 ", "");
        }

        private string GetMetaData(string brandingJson = "")
        {
            const string meta =
                @"{""a11y_NoEssentialInfoByColor"":false,""a11y_NoTextIncludedInAnyImages"":false,""epub_HowToPublishImageDescriptions"":0,""epub_RemoveFontStyles"":false,""bookInstanceId"":""11c2c600-35af-488b-a8d6-3479edcb9217"",""suitableForMakingShells"":false,""suitableForMakingTemplates"":false,""suitableForVernacularLibrary"":true,""bloomdVersion"":0,""experimental"":false,{0}""folio"":false,""isRtl"":false,""title"":""Aeneas"",""allTitles"":""{\""en\"":\""Aeneas\"",\""es\"":\""Spanish title\""}"",""baseUrl"":null,""bookOrder"":null,""isbn"":"""",""bookLineage"":""056B6F11-4A6C-4942-B2BC-8861E62B03B3"",""downloadSource"":""ken@example.com/11c2c600-35af-488b-a8d6-3479edcb9217"",""license"":""cc-by"",""formatVersion"":""2.0"",""licenseNotes"":""Please be nice to John"",""copyright"":""Copyright © 2018, JohnT"",""credits"":"""",""tags"":[],""pageCount"":10,""languages"":[],""langPointers"":[{""__type"":""Pointer"",""className"":""language"",""objectId"":""2cy807OQoe""},{""__type"":""Pointer"",""className"":""language"",""objectId"":""VUiYTJhOyJ""},{""__type"":""Pointer"",""className"":""language"",""objectId"":""jwP3nu7XGY""}],""summary"":null,""allowUploadingToBloomLibrary"":true,""bookletMakingIsAppropriate"":true,""LeveledReaderTool"":null,""LeveledReaderLevel"":0,""country"":"""",""province"":"""",""district"":"""",""xmatterName"":null,""uploader"":{""__type"":""Pointer"",""className"":""_User"",""objectId"":""TWGrqk7NaR""},""tools"":[],""currentTool"":""talkingBookTool"",""toolboxIsOpen"":true,""author"":null,""subjects"":null,""hazards"":null,""a11yFeatures"":null,""a11yLevel"":null,""a11yCertifier"":null,""readingLevelDescription"":null,""typicalAgeRange"":null,""features"":[""blind"",""signLanguage"", ""signLanguage:aen""],""language-display-names"":{""xk"":""Vernacular"",""fr"":""French"",""pt"":""Portuguese"",""de"":""German"",""aen"":""Custom SL Name""}}";
            // can't use string.format here, because the metadata has braces as part of the json.
            return meta.Replace("{0}", brandingJson);
        }

        private string GetMetaData2(string brandingJson = "")
        {
            const string meta =
                @"{""a11y_NoEssentialInfoByColor"":false,""a11y_NoTextIncludedInAnyImages"":false,""epub_HowToPublishImageDescriptions"":0,""epub_RemoveFontStyles"":false,
	""bookInstanceId"":""11c2c600-35af-488b-a8d6-3479edcb9217"",""suitableForMakingShells"":false,""suitableForMakingTemplates"":false,""suitableForVernacularLibrary"":true,
	""bloomdVersion"":0,""experimental"":false,{0}""folio"":false,""isRtl"":false,""title"":""Aeneas"",
	""allTitles"":""{\""en\"":\""Aeneas\"",\""es\"":\""Spanish title\""}"",""baseUrl"":null,""bookOrder"":null,""isbn"":"""",""bookLineage"":""056B6F11-4A6C-4942-B2BC-8861E62B03B3"",
	""downloadSource"":""ken@example.com/11c2c600-35af-488b-a8d6-3479edcb9217"",""license"":""cc-by"",""formatVersion"":""2.0"",""licenseNotes"":""Please be nice to John"",
	""copyright"":""Copyright © 2018, JohnT"",""credits"":"""",""tags"":[],""pageCount"":10,
	""languages"":[],""langPointers"":[{""__type"":""Pointer"",""className"":""language"",""objectId"":""2cy807OQoe""},{""__type"":""Pointer"",""className"":""language"",""objectId"":
	""VUiYTJhOyJ""},{""__type"":""Pointer"",""className"":""language"",""objectId"":""jwP3nu7XGY""}],
	""summary"":null,""allowUploadingToBloomLibrary"":true,""bookletMakingIsAppropriate"":true,""LeveledReaderTool"":null,""LeveledReaderLevel"":0,
	""country"":"""",""province"":"""",""district"":"""",""xmatterName"":null,""uploader"":{""__type"":""Pointer"",""className"":""_User"",""objectId"":""TWGrqk7NaR""},""tools"":[],
	""currentTool"":""talkingBookTool"",""toolboxIsOpen"":true,""author"":null,""subjects"":null,""hazards"":null,""a11yFeatures"":null,""a11yLevel"":null,""a11yCertifier"":null,
	""readingLevelDescription"":null,""typicalAgeRange"":null,""features"":[],
	""language-display-names"":{""cmo-KH"":""Bunong"",""km"":""Khmer"",""en"":""English"",""de"":""German"",}}";
            // can't use string.format here, because the metadata has braces as part of the json.
            return meta.Replace("{0}", brandingJson);
        }

        [Test]
        public void Language1Code_InTrilingualBook()
        {
            Assert.That(_trilingualAnalyzer.Language1Code, Is.EqualTo("xk"));
        }

        [Test]
        public void Language1Code_InBilingualBook()
        {
            Assert.That(_bilingualAnalyzer.Language1Code, Is.EqualTo("xk"));
            Assert.That(
                _bilingualBookInTrilingualCollectionAnalyzer.Language1Code,
                Is.EqualTo("cmo-KH")
            );
        }

        [Test]
        public void Language1Code_InMonolingualBook()
        {
            Assert.That(
                _monolingualBookInBilingualCollectionAnalyzer.Language1Code,
                Is.EqualTo("xk")
            );
            Assert.That(
                _monolingualBookInTrilingualCollectionAnalyzer.Language1Code,
                Is.EqualTo("xk")
            );
        }

        [Test]
        public void Language2Code_InTrilingualBook()
        {
            Assert.That(_trilingualAnalyzer.Language2Code, Is.EqualTo("fr"));
        }

        [Test]
        public void Language2Code_InBilingualBook()
        {
            Assert.That(_bilingualAnalyzer.Language2Code, Is.EqualTo("fr"));
            Assert.That(
                _bilingualBookInTrilingualCollectionAnalyzer.Language2Code,
                Is.EqualTo("en")
            );
        }

        [Test]
        public void Language2Code_InMonolingualBook()
        {
            Assert.That(
                _monolingualBookInBilingualCollectionAnalyzer.Language2Code,
                Is.EqualTo("fr")
            );
            Assert.That(
                _monolingualBookInTrilingualCollectionAnalyzer.Language2Code,
                Is.EqualTo("fr")
            );
        }

        [Test]
        public void Language3Code_InTrilingualBook()
        {
            Assert.That(_trilingualAnalyzer.Language3Code, Is.EqualTo("de"));
        }

        [Test]
        public void Language3Code_InBilingualBook()
        {
            Assert.That(_bilingualAnalyzer.Language3Code, Is.EqualTo("de"));
            Assert.That(
                _bilingualBookInTrilingualCollectionAnalyzer.Language3Code,
                Is.EqualTo("km")
            );
        }

        [Test]
        public void Language3Code_InMonolingualBook()
        {
            Assert.That(_monolingualBookInBilingualCollectionAnalyzer.Language3Code, Is.Empty);
            Assert.That(
                _monolingualBookInTrilingualCollectionAnalyzer.Language3Code,
                Is.EqualTo("de")
            );
        }

        [Test]
        public void SignLanguageCode_InMonolingualBook()
        {
            Assert.That(
                _monolingualBookInTrilingualCollectionAnalyzer.SignLanguageCode,
                Is.EqualTo("aen")
            );
        }

        [Test]
        public void SignLanguageCode_InBilingualBook()
        {
            Assert.That(_bilingualAnalyzer.SignLanguageCode, Is.EqualTo("aen"));
            Assert.That(
                _bilingualBookInTrilingualCollectionAnalyzer.SignLanguageCode,
                Is.EqualTo("")
            );
        }

        [TestCase("Kyrgyzstan2020-XMatter.css", "Kyrgyzstan2020")]
        [TestCase("Device-XMatter.css", "Device")]
        public void GetBestXMatter_DirectoryHasXMatterCSS_PopulatesCollectionXMatterSetting(
            string filename,
            string expectedXMatterName
        )
        {
            using (var tempFolder = new TemporaryFolder("BookAnalyzerTests"))
            {
                var filePath = Path.Combine(tempFolder.FolderPath, filename);
                using (File.Create(filePath)) // TemporaryFolder takes a long time to dispose if you don't dispose this first.
                {
                    // System under test
                    var analyzer = new CollectionSettingsReconstructor(
                        GetMonoLingualHtml(),
                        GetMetaData(),
                        tempFolder.FolderPath
                    );

                    // Verification
                    string expected = $"<XMatterPack>{expectedXMatterName}</XMatterPack>";
                    Assert.That(
                        analyzer.BloomCollection.Contains(expected),
                        Is.True,
                        "BloomCollection did not contain expected XMatter Pack. XML: "
                            + analyzer.BloomCollection
                    );
                }
            }
        }

        [TestCase]
        public void GetBestXMatter_DirectoryMissingXMatterCSS_PopulatesWithDefault()
        {
            using (var tempFolder = new TemporaryFolder("BookAnalyzerTests"))
            {
                // System under test
                var analyzer = new CollectionSettingsReconstructor(
                    GetMonoLingualHtml(),
                    GetMetaData(),
                    tempFolder.FolderPath
                );

                // Verification
                string expectedDefault = $"<XMatterPack>Device</XMatterPack>";
                Assert.That(
                    analyzer.BloomCollection.Contains(expectedDefault),
                    Is.True,
                    "BloomCollection did not contain default XMatter Pack. XML: "
                        + analyzer.BloomCollection
                );
            }
        }

        [Test]
        public void Branding_Specified()
        {
            Assert.That(_silleadBrandingAnalyzer.SubscriptionCode, Is.EqualTo("SIL-LEAD-***-***"));
        }

        [Test]
        public void Branding_Empty()
        {
            Assert.That(_emptyBrandingAnalyzer.SubscriptionCode, Is.Null);
        }

        [Test]
        public void Branding_Missing_SetToDefault()
        {
            Assert.That(_monolingualBookInBilingualCollectionAnalyzer.SubscriptionCode, Is.Null);
        }

        [Test]
        public void BookCollection_HasLanguage1Code()
        {
            Assert.That(
                _twoLanguageCollection.Element("Language1Iso639Code")?.Value,
                Is.EqualTo("xk")
            );
            Assert.That(
                _threeLanguageCollection.Element("Language1Iso639Code")?.Value,
                Is.EqualTo("xk")
            );
            Assert.That(
                _threeLanguageCollectionUsingTwo.Element("Language1Iso639Code")?.Value,
                Is.EqualTo("cmo-KH")
            );
        }

        [Test]
        public void BookCollection_HasLanguage2Code()
        {
            Assert.That(
                _twoLanguageCollection.Element("Language2Iso639Code")?.Value,
                Is.EqualTo("fr")
            );
            Assert.That(
                _threeLanguageCollection.Element("Language2Iso639Code")?.Value,
                Is.EqualTo("fr")
            );
            Assert.That(
                _threeLanguageCollectionUsingTwo.Element("Language2Iso639Code")?.Value,
                Is.EqualTo("en")
            );
        }

        [Test]
        public void BookCollection_HasLanguage3Code()
        {
            Assert.That(
                _threeLanguageCollection.Element("Language3Iso639Code")?.Value,
                Is.EqualTo("de")
            );
            Assert.That(
                _threeLanguageCollectionUsingTwo.Element("Language3Iso639Code")?.Value,
                Is.EqualTo("km")
            );
        }

        [Test]
        public void BookCollection_HasSignLanguageCode()
        {
            Assert.That(
                _threeLanguageCollection.Element("SignLanguageIso639Code")?.Value,
                Is.EqualTo("aen")
            );
        }

        [Test]
        public void BookCollection_HasSignLanguageName()
        {
            Assert.That(
                _threeLanguageCollection.Element("SignLanguageName")?.Value,
                Is.EqualTo("Custom SL Name")
            );
        }

        [Test]
        public void BookCollection_HasBranding()
        {
            Assert.That(
                _silleadCollection.Element("SubscriptionCode")?.Value,
                Is.EqualTo("SIL-LEAD-***-***")
            );
        }

        [Test]
        public void BookCollection_HasCorrectLanguageNames()
        {
            Assert.That(
                _threeLanguageCollection.Element("Language1Name")?.Value,
                Is.EqualTo("Vernacular")
            );
            Assert.That(
                _threeLanguageCollection.Element("Language2Name")?.Value,
                Is.EqualTo("French")
            );
            Assert.That(
                _threeLanguageCollection.Element("Language3Name")?.Value,
                Is.EqualTo("German")
            );
            Assert.That(
                _threeLanguageCollectionUsingTwo.Element("Language1Name")?.Value,
                Is.EqualTo("Bunong")
            );
            Assert.That(
                _threeLanguageCollectionUsingTwo.Element("Language2Name")?.Value,
                Is.EqualTo("English")
            );
            Assert.That(
                _threeLanguageCollectionUsingTwo.Element("Language3Name")?.Value,
                Is.EqualTo("Khmer")
            );
        }

        private const string kHtmlUnmodifiedPages =
            @"<html>
  <head>
    <meta charset='UTF-8' />
  </head>
  <body>
    <div id='bloomDataDiv'>
      <div data-book='contentLanguage1' lang='*'>en</div>
      <div data-book='contentLanguage1Rtl' lang='*'>False</div>
      <div data-book='languagesOfBook' lang='*'>English</div>
    </div>
    <div class='bloom-page cover coverColor bloom-frontMatter frontCover outsideFrontCover side-right A5Portrait' data-page='required singleton' data-export='front-matter-cover' data-xmatter-page='frontCover' id='89a32796-8cf9-4b0f-a694-43a3d705f620' data-page-number=''>
      <div class='pageLabel' lang='en' data-i18n='TemplateBooks.PageLabel.Front Cover'>Front Cover</div>
    </div>
    <div class='bloom-page numberedPage customPage bloom-combinedPage side-right A5Portrait bloom-monolingual' data-page='' id='002627bd-4853-487d-986a-88ea67e0f31c' data-pagelineage='adcd48df-e9ab-4a07-afd4-6a24d0398382' data-page-number='1' lang=''>
      <div class='pageLabel' data-i18n='TemplateBooks.PageLabel.Basic Text &amp; Picture' lang='en'>Basic Text &amp; Picture</div>
      <div class='marginBox'>
        <div style='min-height: 42px;' class='split-pane horizontal-percent'>
          <div class='split-pane-component position-top' style='bottom: 50%'>
            <div class='split-pane-component-inner'>
              <div title='image-placeholder.png 6.58 KB 341 x 335 81 DPI (should be 300-600) Bit Depth: 32' class='bloom-canvas bloom-leadingElement'>
                <img src='image-placeholder.png' alt='place holder'></img>
                <div class='bloom-translationGroup bloom-imageDescription bloom-trailingElement'></div>
              </div>
            </div>
          </div>
          <div class='split-pane-divider horizontal-divider' style='bottom: 50%' />
          <div class='split-pane-component position-bottom' style='height: 50%'>
            <div class='split-pane-component-inner'>
              <div class='bloom-translationGroup bloom-trailingElement' data-default-languages='auto'>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
    <div class='bloom-page numberedPage customPage bloom-combinedPage side-left A5Portrait bloom-monolingual' data-page='' id='1e8377b3-f8b3-4fc6-976b-dc3262463880' data-pagelineage='adcd48df-e9ab-4a07-afd4-6a24d0398382' data-page-number='2' lang=''>
      <div class='pageLabel' data-i18n='TemplateBooks.PageLabel.Basic Text &amp; Picture' lang='en'>Basic Text &amp; Picture</div>
      <div class='marginBox'>
        <div style='min-height: 42px;' class='split-pane horizontal-percent'>
          <div class='split-pane-component position-top' style='bottom: 50%'>
            <div class='split-pane-component-inner'>
              <div title='aor_ACC029M.png 35.14 KB 1500 x 806 355 DPI (should be 300-600) Bit Depth: 1' class='bloom-canvas bloom-leadingElement'>
                <img src='aor_ACC029M.png' alt=''></img>
                <div class='bloom-translationGroup bloom-imageDescription bloom-trailingElement'></div>
              </div>
            </div>
          </div>
          <div class='split-pane-divider horizontal-divider' style='bottom: 50%' />
          <div class='split-pane-component position-bottom' style='height: 50%'>
            <div class='split-pane-component-inner'>
			  <!-- This vestigial box-header-off group needs to be ignored when counting groups on a page -->
              <div class='box-header-off bloom-translationGroup'>
              </div>
              <div class='bloom-translationGroup bloom-trailingElement' data-default-languages='auto'>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
    <div class='bloom-page cover coverColor outsideBackCover bloom-backMatter side-left A5Portrait' data-page='required singleton' data-export='back-matter-back-cover' data-xmatter-page='outsideBackCover' id='f6afe49f-a2fc-480e-80fe-b3262e87868d' data-page-number=''>
      <div class='pageLabel' lang='en' data-i18n='TemplateBooks.PageLabel.Outside Back Cover'>Outside Back Cover</div>
    </div>
  </body>
</html>
";

        private const string kHtmlModifiedPage =
            @"<html>
  <head>
    <meta charset='UTF-8' />
  </head>
  <body>
    <div class='bloom-page numberedPage customPage bloom-combinedPage A5Portrait bloom-monolingual side-left' data-page='' id='5a72f533-cd59-4e8d-9da5-2fa052144621' data-pagelineage='adcd48df-e9ab-4a07-afd4-6a24d0398382' data-page-number='1' lang=''>
      <div class='pageLabel' data-i18n='TemplateBooks.PageLabel.Basic Text &amp; Picture' lang='en'>Basic Text &amp; Picture</div>
      <div class='marginBox'>
        <div style='min-height: 42px;' class='split-pane horizontal-percent'>
          <div class='split-pane-component position-top' style='bottom: 50%'>
            <div min-height='60px 150px 250px' min-width='60px 150px 250px' style='position: relative;' class='split-pane-component-inner'>
              <div title='PT-eclipse1.jpg 526.06 KB 4010 x 2684 1915 DPI (should be 300-600) Bit Depth: 24' class='bloom-canvas bloom-leadingElement'>
                <img src='PT-eclipse1.jpg' alt='sun hidden by moon with corona shining all around the moon&apos;s obscuring disk' height='217' width='324'></img>
                <div class='bloom-translationGroup bloom-imageDescription bloom-trailingElement'>
                  <div data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 24px;' class='bloom-editable ImageDescriptionEdit-style bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='en'>
                    <p>sun hidden by moon with corona shining all around the moon's obscuring disk</p>
                  </div>
                </div>
              </div>
            </div>
          </div>
          <div class='split-pane-divider horizontal-divider' style='bottom: 50%'></div>
          <div class='split-pane-component position-bottom' style='height: 50%'>
            <div style='min-height: 42px;' class='split-pane horizontal-percent'>
              <div class='split-pane-component position-top'>
                <div min-height='60px 150px' min-width='60px 150px 250px' style='position: relative;' class='split-pane-component-inner'>
                  <div class='bloom-translationGroup bloom-trailingElement' data-default-languages='auto'>
                    <div data-languagetipcontent='English' data-audiorecordingmode='Sentence' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 24px;' class='bloom-editable normal-style bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='en'>
                      <p>Solar Eclipse photographed by Paul Thordarson</p>
                    </div>
                  </div>
                </div>
              </div>
              <div class='split-pane-divider horizontal-divider'></div>
              <div class='split-pane-component position-bottom'>
                <div min-height='60px 150px' min-width='60px 150px 250px' style='position: relative;' class='split-pane-component-inner adding'>
                  <div class='box-header-off bloom-translationGroup'>
                  </div>
                  <div class='bloom-translationGroup bloom-trailingElement'>
                    <div data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' style='min-height: 24px;' class='bloom-editable normal-style bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='en'>
                      <p>This is a test.</p>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </body>
</html>
";
    }
}
