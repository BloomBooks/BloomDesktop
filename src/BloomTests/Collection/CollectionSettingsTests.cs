using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using Bloom.Book;
using Bloom.Collection;
using FFMpegCore.Arguments;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.Collection
{
    [TestFixture]
    public class CollectionSettingsTests
    {
        private TemporaryFolder _folder;

        [OneTimeSetUp]
        public void FixtureSetup()
        {
            SIL.Reporting.ErrorReport.IsOkToInteractWithUser = false;
            _folder = new TemporaryFolder("CollectionSettingsTests");
        }

        [OneTimeTearDown]
        public void Cleanup()
        {
            _folder.Dispose(); // fixture teardown
        }

        /// <summary>
        /// Creates a new CollectionSettings object. May or may not load a .bloomCollection file depending on
        /// whether one exists in the collection or not.
        /// </summary>
        /// <returns></returns>
        private CollectionSettings CreateCollectionSettings(
            string parentFolderPath,
            string collectionName
        )
        {
            return new CollectionSettings(
                CollectionSettings.GetPathForNewSettings(parentFolderPath, collectionName)
            );
        }

        /// <summary>
        /// This is a regression test related to https://jira.sil.org/browse/BL-685.
        /// Apparently calculating the name is expensive, so it is cached. This
        /// test ensures that the cache doesn't keep the name from tracking the language tag.
        /// </summary>
        [Test]
        public void Language1TagChanged_NameChangedToo()
        {
            const string collectionName = "test";
            var settings = CreateCollectionSettings(_folder.Path, collectionName);
            settings.Language1Tag = "fr";
            Assert.AreEqual("French", settings.Language1.GetNameInLanguage("en"));
            settings.Language1Tag = "en";
            Assert.AreEqual("English", settings.Language1.GetNameInLanguage("en"));
        }

        [Test]
        public void PageNumberStyle_BadNameInFile()
        {
            var bloomCollectionFileContents =
                @"<?xml version=""1.0"" encoding=""utf-8""?>
<Collection version=""0.2"">
	<PageNumberStyle>xyz</PageNumberStyle>
</Collection>";
            const string collectionName = "test";
            var collectionPath = CollectionSettings.GetPathForNewSettings(
                _folder.Path,
                collectionName
            );
            Directory.CreateDirectory(Path.GetDirectoryName(collectionPath));
            RobustFile.WriteAllText(collectionPath, bloomCollectionFileContents);
            var settings = CreateCollectionSettings(_folder.Path, collectionName);
            Assert.AreEqual(
                "Decimal",
                settings.PageNumberStyle,
                "'xyz' is not in the approved list of numbering styles, should default to 'Decimal'"
            );
        }

        [Test]
        public void PageNumberStyle_NotInFile()
        {
            var bloomCollectionFileContents =
                @"<?xml version=""1.0"" encoding=""utf-8""?>
<Collection version=""0.2"">
</Collection>";
            const string collectionName = "test";
            var collectionPath = CollectionSettings.GetPathForNewSettings(
                _folder.Path,
                collectionName
            );
            Directory.CreateDirectory(Path.GetDirectoryName(collectionPath));
            RobustFile.WriteAllText(collectionPath, bloomCollectionFileContents);
            var settings = CreateCollectionSettings(_folder.Path, collectionName);
            Assert.AreEqual(
                "Decimal",
                settings.PageNumberStyle,
                "If the bloomCollection has no value for numbering style, assume 'decimal'"
            );
        }

        [Test]
        public void PageNumberStyle_CanRoundTrip()
        {
            const string style = "Gurmukhi";
            const string collectionName = "test";
            var settings = CreateCollectionSettings(_folder.Path, collectionName);
            settings.Language1Tag = "en";
            settings.PageNumberStyle = style;
            settings.Save();
            var newSettings = CreateCollectionSettings(_folder.Path, collectionName);
            Assert.AreEqual(
                style,
                newSettings.PageNumberStyle,
                "Numbering style 'Gurmukhi' should round trip"
            );
        }

        [Test]
        public void RTL_CanRoundTrip()
        {
            const string collectionName = "test";
            var settings = CreateCollectionSettings(_folder.Path, collectionName);
            settings.Language1Tag = "en";
            settings.Language1.IsRightToLeft = true;
            settings.Save();
            var newSettings = CreateCollectionSettings(_folder.Path, collectionName);
            Assert.That(newSettings.Language1.IsRightToLeft, Is.True);
        }

        [Test]
        public void LegacyRTL_Loads()
        {
            // A real collection settings file from 5.6.7.  The RTL setting is in the old place.
            // This is also a continuing test that an old file can at least be read.
            var input =
                @"<Collection version=""0.2"">
  <CollectionId>70a3889b-2ef9-46fb-948b-3da17b0351c9</CollectionId>
  <Language1Name>Arta</Language1Name>
  <Language1IsCustomName>false</Language1IsCustomName>
  <Language1Iso639Code>atz</Language1Iso639Code>
  <DefaultLanguage1FontName>Andika</DefaultLanguage1FontName>
  <IsLanguage1Rtl>true</IsLanguage1Rtl>
  <Language1LineHeight>0</Language1LineHeight>
  <Language1BreaksLinesOnlyAtSpaces>false</Language1BreaksLinesOnlyAtSpaces>
  <Language1BaseUIFontSizeInPoints>0</Language1BaseUIFontSizeInPoints>
  <Language2Name>English</Language2Name>
  <Language2IsCustomName>false</Language2IsCustomName>
  <Language2Iso639Code>en</Language2Iso639Code>
  <DefaultLanguage2FontName>Andika</DefaultLanguage2FontName>
  <IsLanguage2Rtl>false</IsLanguage2Rtl>
  <Language2LineHeight>0</Language2LineHeight>
  <Language2BreaksLinesOnlyAtSpaces>false</Language2BreaksLinesOnlyAtSpaces>
  <Language2BaseUIFontSizeInPoints>0</Language2BaseUIFontSizeInPoints>
  <Language3Name></Language3Name>
  <Language3IsCustomName>false</Language3IsCustomName>
  <Language3Iso639Code></Language3Iso639Code>
  <DefaultLanguage3FontName>Andika</DefaultLanguage3FontName>
  <IsLanguage3Rtl>false</IsLanguage3Rtl>
  <Language3LineHeight>0</Language3LineHeight>
  <Language3BreaksLinesOnlyAtSpaces>false</Language3BreaksLinesOnlyAtSpaces>
  <Language3BaseUIFontSizeInPoints>0</Language3BaseUIFontSizeInPoints>
  <SignLanguageName></SignLanguageName>
  <SignLanguageIsCustomName>false</SignLanguageIsCustomName>
  <SignLanguageIso639Code></SignLanguageIso639Code>
  <OneTimeCheckVersionNumber>1</OneTimeCheckVersionNumber>
  <IsSourceCollection>False</IsSourceCollection>
  <XMatterPack>Traditional</XMatterPack>
  <PageNumberStyle>Decimal</PageNumberStyle>
  <BrandingProjectName>Default</BrandingProjectName>
  <SubscriptionCode></SubscriptionCode>
  <Country>Philippines</Country>
  <Province></Province>
  <District></District>
  <AllowNewBooks>True</AllowNewBooks>
  <AudioRecordingMode>Sentence</AudioRecordingMode>
  <AudioRecordingTrimEndMilliseconds>40</AudioRecordingTrimEndMilliseconds>
  <BooksOnWebGoal>200</BooksOnWebGoal>
  <BulkPublishBloomPubSettings>
    <MakeBookshelfFile>True</MakeBookshelfFile>
    <MakeBloomBundle>True</MakeBloomBundle>
    <BookshelfColor>#B0DEE4</BookshelfColor>
    <DistributionTag></DistributionTag>
    <BookshelfLabel></BookshelfLabel>
  </BulkPublishBloomPubSettings>
</Collection>";
            var collectionName = "testRtl";
            var collectionPath = CollectionSettings.GetPathForNewSettings(
                _folder.Path,
                collectionName
            );
            Directory.CreateDirectory(Path.GetDirectoryName(collectionPath));
            RobustFile.WriteAllText(collectionPath, input);
            var settings = CreateCollectionSettings(_folder.Path, collectionName);
            Assert.That(settings.Language1.IsRightToLeft, Is.True);
        }

        [Test]
        public void DefaultBookshelf_ReadWrite()
        {
            var bloomCollectionFileContents =
                @"<?xml version=""1.0"" encoding=""utf-8""?>
<Collection version=""0.2"">
	<BrandingProjectName>Kygyzstan2020</BrandingProjectName>
	<SubscriptionCode>FakeCode</SubscriptionCode>
	<DefaultBookTags>bookshelf:kygyzstan2020-ky-grade1-term1</DefaultBookTags>
</Collection>";
            const string collectionName = "test";
            var collectionPath = CollectionSettings.GetPathForNewSettings(
                _folder.Path,
                collectionName
            );
            Directory.CreateDirectory(Path.GetDirectoryName(collectionPath));
            RobustFile.WriteAllText(collectionPath, bloomCollectionFileContents);
            var settings = CreateCollectionSettings(_folder.Path, collectionName);
            // If there isn't a valid project name/code pair, then the project goes to Default and the bookshelf to none.
            // We don't want to expose a valid name/code pair in the source code so this test is all we have.
            Assert.That(settings.DefaultBookshelf, Is.EqualTo(""));
            Assert.That(settings.BrandingProjectKey, Is.EqualTo("Default"));
            Assert.That(settings.SubscriptionCode, Is.EqualTo("FakeCode"));
            // We don't protect writing the same way as reading, since users aren't able to select a bookshelf unless
            // they've established a valid project which has one or more bookshelves.
            settings.DefaultBookshelf = "some-other-shelf";
            settings.Save();
            var newContents = RobustFile.ReadAllText(collectionPath);
            AssertThatXmlIn
                .String(newContents)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//DefaultBookTags[text()='bookshelf:some-other-shelf']",
                    1
                );
            var settings2 = CreateCollectionSettings(
                Path.GetDirectoryName(collectionPath),
                collectionName
            );
            // And the assigned bookshelf will disappear on loading into the CollectionSettings object.
            // The fake SubscriptionCode has disappeared because the Default project doesn't need a subscription code so it isn't saved.
            Assert.That(settings2.DefaultBookshelf, Is.EqualTo(""));
            Assert.That(settings2.BrandingProjectKey, Is.EqualTo("Default"));
            Assert.That(settings2.SubscriptionCode, Is.Null);
        }

        [TestCase(null, null, null, new[] { "en" })]
        [TestCase("", "", "", new[] { "en" })]
        [TestCase("pt", "pt", null, new[] { "pt", "en" })] // don't duplicate "pt"
        [TestCase("en", "es", "de", new[] { "en", "es", "de" })] // don't duplicate "en"
        [TestCase("id", "es", "de", new[] { "id", "es", "de", "en" })] // more typical case where adding English is important
        [TestCase("zh-CN", "es", "de", new[] { "zh-CN", "es", "de", "en" })] // zh-CN does not require an insertion
        [TestCase("zh-Hans", "es", "de", new[] { "zh-Hans", "zh-CN", "es", "de", "en" })] // any other zh-X requires zh-CN to be inserted following it.
        [TestCase("es", "zh-Hans", "de", new[] { "es", "zh-Hans", "zh-CN", "de", "en" })] // try in all 3 positions.
        [TestCase("es", "id", "zh-Hant", new[] { "es", "id", "zh-Hant", "zh-CN", "en" })]
        [TestCase("es", "zh-Hans", "zh-Hant", new[] { "es", "zh-Hans", "zh-Hant", "zh-CN", "en" })] // if we have two locale-specific ones, the insertion should be after both.
        [TestCase("fr-CA", "es", "de", new[] { "fr-CA", "fr", "es", "de", "en" })]
        [TestCase("es", "fr-CA", "de", new[] { "es", "fr-CA", "fr", "de", "en" })]
        [TestCase("es", "id", "fr-LU", new[] { "es", "id", "fr-LU", "fr", "en" })]
        [TestCase("rub", "", "en", new[] { "rub", "en" })] // don't stick in Russian as an alternative to an unrelated 3 letter tag, and don't duplicate "en"
        // given two fr-X tags, insert fr after the last of them. The main point here is that fr should be tried after fr-FR and fr-LU.
        // But the result here is actually debatable: should es be preferred to fr in this case?
        // Maybe the right result is fr-FR, fr-LU, fr, es, en in this case, since the original order indicates that French is better than Spanish?
        // But it's a very obscure and unlikely case; I think we can live with what the current algorithm does.
        [TestCase("fr-FR", "es", "fr-LU", new[] { "fr-FR", "es", "fr-LU", "fr", "en" })]
        [TestCase(
            "zh-Hans",
            "fr-CA",
            "es-SV",
            new[] { "zh-Hans", "zh-CN", "fr-CA", "fr", "es-SV", "es", "en" }
        )] // all three!!
        [TestCase("fr", "fr-CA", "de", new[] { "fr", "fr-CA", "de", "en" })] // already have the fall-back, don't add again.
        // The following test cases are special cases for Pashto languages.
        // See comments in LicenseDescriptionLanguagePriorities.
        [TestCase("pbt", "pbt", null, new[] { "pbt", "pbu", "ps", "pus", "en" })] // don't duplicate "pbt"
        [TestCase("pst", "pst", null, new[] { "pst", "pbu", "ps", "pus", "en" })] // don't duplicate "pst"
        [TestCase("ps", "ps", null, new[] { "ps", "pbu", "pus", "en" })] // don't duplicate "ps"
        [TestCase("pus", "pus", null, new[] { "pus", "pbu", "ps", "en" })] // don't duplicate "pus"
        [TestCase("pbu", "pbu", null, new[] { "pbu", "ps", "pus", "en" })] // don't duplicate "pbu"
        [TestCase("xyz", "pbu", "abc", new[] { "xyz", "pbu", "ps", "pus", "abc", "en" })]
        public void GetLanguagePrioritiesForLocalizedTextOnPage_GetsCorrectListOfLanguages(
            string lang1,
            string lang2,
            string lang3,
            string[] results
        )
        {
            var settings = CreateCollectionSettings(_folder.Path, "test");
            settings.Language1Tag = lang1;
            settings.Language2Tag = lang2;
            settings.Language3Tag = lang3;
            var bookData = new BookData(new HtmlDom("<html><body></body></html>"), settings, null);
            bookData.SetMultilingualContentLanguages(lang1, lang2, lang3);
            Assert.That(
                bookData.GetLanguagePrioritiesForLocalizedTextOnPage(),
                Is.EqualTo(results)
            );
        }

        [TestCase("pt", "pt", null, new[] { "pt", "en" })] // don't duplicate "pt"
        [TestCase("en", "es", "de", new[] { "es", "de", "en" })] // don't duplicate "en", and last because not selected
        [TestCase("id", "es", "de", new[] { "es", "de", "id", "en" })] // more typical case where adding English is important. 'id' comes later because not selected.
        [TestCase("zh-CN", "es", "de", new[] { "es", "de", "zh-CN", "en" })] // zh-CN does not require an insertion
        [TestCase("zh-Hans", "es", "de", new[] { "es", "de", "zh-Hans", "zh-CN", "en" })] // any other zh-X requires zh-CN to be inserted following it.
        [TestCase("es", "zh-Hans", "zh-Hant", new[] { "zh-Hans", "zh-Hant", "zh-CN", "es", "en" })] // if we have two locale-specific ones, the insertion should be after both.
        [TestCase("fr-CA", "es", "de", new[] { "es", "de", "fr-CA", "fr", "en" })]
        [TestCase("rub", "", "en", new[] { "en", "rub" })] // don't stick in Russian as an alternative to an unrelated 3 letter tag, and don't duplicate "en" or move to end
        public void GetLanguagePrioritiesForLocalizedTextOnPage_L1Unchecked_GetsCorrectListOfLanguages(
            string lang1,
            string lang2,
            string lang3,
            string[] results
        )
        {
            var settings = CreateCollectionSettings(_folder.Path, "test");
            settings.Language1Tag = lang1;
            settings.Language2Tag = lang2;
            settings.Language3Tag = lang3;
            var bookData = new BookData(new HtmlDom("<html><body></body></html>"), settings, null);
            bookData.SetMultilingualContentLanguages(lang2, lang3);
            Assert.That(
                bookData.GetLanguagePrioritiesForLocalizedTextOnPage(),
                Is.EqualTo(results)
            );
        }

        [TestCase("xyz", "abc", null, new[] { "abc", "en" })]
        public void GetLanguagePrioritiesForLocalizedTextOnPage_DoNotIncludeLang1_GetsCorrectListOfLanguages(
            string lang1,
            string lang2,
            string lang3,
            string[] results
        )
        {
            var settings = CreateCollectionSettings(_folder.Path, "test");
            settings.Language1Tag = lang1;
            settings.Language2Tag = lang2;
            settings.Language3Tag = lang3;
            var bookData = new BookData(new HtmlDom("<html><body></body></html>"), settings, null);
            bookData.SetMultilingualContentLanguages(lang1, lang2, lang3);
            Assert.That(
                bookData.GetLanguagePrioritiesForLocalizedTextOnPage(false),
                Is.EqualTo(results)
            );
        }

        [TestCase("xyz", "abc", "tpi", new[] { "abc", "tpi", "en" })]
        [TestCase("xyz", "abc", null, new[] { "abc", "en" })]
        public void GetLanguagePrioritiesForLocalizedTextOnPage_DoNotIncludeLang1_Lang1NotChecked_GetsCorrectListOfLanguages(
            string lang1,
            string lang2,
            string lang3,
            string[] results
        )
        {
            var settings = CreateCollectionSettings(_folder.Path, "test");
            settings.Language1Tag = lang1;
            settings.Language2Tag = lang2;
            settings.Language3Tag = lang3;
            var bookData = new BookData(new HtmlDom("<html><body></body></html>"), settings, null);
            bookData.SetMultilingualContentLanguages(lang2, lang3);
            Assert.That(
                bookData.GetLanguagePrioritiesForLocalizedTextOnPage(false),
                Is.EqualTo(results)
            );
        }

        [TestCase("xyz", "abc", "tpi", new[] { "abc", "tpi", "en" })]
        [TestCase("xyz", "abc", null, new[] { "abc", "en" })]
        public void GetLanguagePrioritiesForLocalizedTextOnPage_DoNotIncludeLang1_OnlyLang1Checked_GetsCorrectListOfLanguages(
            string lang1,
            string lang2,
            string lang3,
            string[] results
        )
        {
            var settings = CreateCollectionSettings(_folder.Path, "test");
            settings.Language1Tag = lang1;
            settings.Language2Tag = lang2;
            settings.Language3Tag = lang3;
            var bookData = new BookData(new HtmlDom("<html><body></body></html>"), settings, null);
            bookData.SetMultilingualContentLanguages(lang1);
            Assert.That(
                bookData.GetLanguagePrioritiesForLocalizedTextOnPage(false),
                Is.EqualTo(results)
            );
        }

        [TestCase("", "2")] // default
        [TestCase("Decimal", "2")]
        [TestCase("Devanagari", "२")]
        [TestCase("Khmer", "២")]
        [TestCase("Cjk-Decimal", "二")]
        public void CharactersForDigitsForPageNumbers_Tests(
            string numberStyleName,
            string digitForNumber2
        )
        {
            var settings = CreateCollectionSettings(_folder.Path, "test");
            settings.PageNumberStyle = numberStyleName;
            Assert.AreEqual(
                digitForNumber2,
                settings.CharactersForDigitsForPageNumbers.Substring(2, 1)
            );
        }

        [Test]
        public void BulkPublishBloomPubSettings_GivenBulkPublishSettings_SavesToXmlProperly()
        {
            var settings = CreateCollectionSettings(_folder.Path, "test");
            settings.BulkPublishBloomPubSettings =
                new Bloom.Publish.BloomPub.BulkBloomPubPublishSettings()
                {
                    makeBookshelfFile = false,
                    makeBloomBundle = false,
                    bookshelfColor = "#FF0000",
                    distributionTag = "distTag",
                    bookshelfLabel = "bookshelfLabel"
                };

            // System under test
            settings.Save();

            // Verification
            var text = RobustFile.ReadAllText(settings.SettingsFilePath);
            string expected =
                @"<BulkPublishBloomPubSettings>
    <MakeBookshelfFile>False</MakeBookshelfFile>
    <MakeBloomBundle>False</MakeBloomBundle>
    <BookshelfColor>#FF0000</BookshelfColor>
    <DistributionTag>distTag</DistributionTag>
    <BookshelfLabel>bookshelfLabel</BookshelfLabel>
  </BulkPublishBloomPubSettings>";
            StringAssert.Contains(expected, text);
        }

        [Test]
        public void BulkPublishBloomPubSettings_GivenBulkPublishSettingsInXml_LoadsProperly()
        {
            var collectionName = "loadBulkPublishSettingsTest";
            var collectionSettingsPath = Path.Combine(
                _folder.Path,
                $"{collectionName}.bloomCollection"
            );
            if (RobustFile.Exists(collectionSettingsPath))
            {
                RobustFile.Delete(collectionSettingsPath);
            }

            string fileContents =
                @"<?xml version=""1.0"" encoding=""utf-8""?>
<Collection version=""0.2"">
  <BulkPublishBloomPubSettings>
    <MakeBookshelfFile>False</MakeBookshelfFile>
    <MakeBloomBundle>False</MakeBloomBundle>
    <BookshelfColor>#FF0000</BookshelfColor>
    <DistributionTag>distTag</DistributionTag>
    <BookshelfLabel>bookshelfLabel</BookshelfLabel>
  </BulkPublishBloomPubSettings>
</Collection>";
            RobustFile.WriteAllText(collectionSettingsPath, fileContents);

            // System under test
            var collectionSettings = new CollectionSettings(collectionSettingsPath);

            // Verification
            var bulkPublishSettings = collectionSettings.BulkPublishBloomPubSettings;
            Assert.That(
                bulkPublishSettings.makeBookshelfFile,
                Is.EqualTo(false),
                "makeBookshelfFile"
            );
            Assert.That(bulkPublishSettings.makeBloomBundle, Is.EqualTo(false), "makeBloomBundle");
            Assert.That(bulkPublishSettings.bookshelfColor, Is.EqualTo("#FF0000"));
            Assert.That(bulkPublishSettings.distributionTag, Is.EqualTo("distTag"));
            Assert.That(bulkPublishSettings.bookshelfLabel, Is.EqualTo("bookshelfLabel"));
        }

        [Test]
        public void AddColorToPalette_GetColorPaletteAsJson_WorkTogether()
        {
            var collectionName = "PaletteTesting";
            var collectionSettingsPath = Path.Combine(
                _folder.Path,
                collectionName,
                $"{collectionName}.bloomCollection"
            );
            if (RobustFile.Exists(collectionSettingsPath))
                RobustFile.Delete(collectionSettingsPath);
            var settings = CreateCollectionSettings(_folder.Path, collectionName);

            const string jsonColor1 = "{\"colors\":[\"#012345\"],\"opacity\":1}";
            const string jsonColor2 = "{\"colors\":[\"#012345\",\"#987654\"],\"opacity\":1}";
            const string jsonColor3 = "{\"colors\":[\"#012345\"],\"opacity\":0.75}";
            const string jsonColor4 = "{\"colors\":[\"#987643\"],\"opacity\":0.5}";

            var jsonResult = settings.GetColorPaletteAsJson("test-text");
            // initial palette is empty
            Assert.That(jsonResult, Is.EqualTo("[]"));
            // Adding a color adds it properly.
            settings.AddColorToPalette("test-text", jsonColor1);
            jsonResult = settings.GetColorPaletteAsJson("test-text");
            Assert.That(jsonResult, Is.EqualTo("[" + jsonColor1 + "]"));
            // Adding same color doesn't change anything.
            settings.AddColorToPalette("test-text", jsonColor1);
            jsonResult = settings.GetColorPaletteAsJson("test-text");
            Assert.That(jsonResult, Is.EqualTo("[" + jsonColor1 + "]"));
            // Adding a different color adds it at the end.
            settings.AddColorToPalette("test-text", jsonColor2);
            jsonResult = settings.GetColorPaletteAsJson("test-text");
            Assert.That(jsonResult, Is.EqualTo("[" + jsonColor1 + "," + jsonColor2 + "]"));
            // Add a third color works
            settings.AddColorToPalette("test-text", jsonColor3);
            jsonResult = settings.GetColorPaletteAsJson("test-text");
            Assert.That(
                jsonResult,
                Is.EqualTo("[" + jsonColor1 + "," + jsonColor2 + "," + jsonColor3 + "]")
            );
            // Adding to a different palette works.  and doesn't change the wrong palette.
            settings.AddColorToPalette("test-background", jsonColor4);
            jsonResult = settings.GetColorPaletteAsJson("test-background");
            Assert.That(jsonResult, Is.EqualTo("[" + jsonColor4 + "]"));
            jsonResult = settings.GetColorPaletteAsJson("test-text");
            Assert.That(
                jsonResult,
                Is.EqualTo("[" + jsonColor1 + "," + jsonColor2 + "," + jsonColor3 + "]")
            );

            // The file is supposed to be saved on every addition.  Check its contents.
            var settingsContent = RobustFile.ReadAllText(
                collectionSettingsPath,
                System.Text.Encoding.UTF8
            );
            var xml = XElement.Parse(settingsContent);
            var elements = xml.Descendants("Palette");
            Assert.That(elements, Is.Not.Null);
            int count = 0;
            foreach (XElement element in elements)
            {
                ++count; // why elements doesn't have Count or Count() is beyond me!
                if (element.Attribute("id").Value == "test-text")
                {
                    Assert.That(element.Value, Is.EqualTo("#012345 #012345-#987654 #012345/0.75"));
                }
                else if (element.Attribute("id").Value == "test-background")
                {
                    Assert.That(element.Value, Is.EqualTo("#987643/0.5"));
                }
                else
                {
                    Assert.That(false, "unexpected element id value");
                }
            }
            Assert.That(count, Is.EqualTo(2));
        }

        // Though currently Bloom does not save things in this state,
        // I wanted to ensure that an empty <Palette> tag was not a problem
        // since it did cause a problem when I was testing.
        [Test]
        public void GetColorPaletteAsJson_HasNoColors_ReturnEmptyArray()
        {
            var collectionName = "EmptyPaletteTesting";
            var collectionSettingsPath = Path.Combine(
                _folder.Path,
                collectionName,
                $"{collectionName}.bloomCollection"
            );
            if (RobustFile.Exists(collectionSettingsPath))
                RobustFile.Delete(collectionSettingsPath);
            var settings = CreateCollectionSettings(_folder.Path, collectionName);

            FieldInfo colorPalletesFi = typeof(CollectionSettings).GetField(
                "ColorPalettes",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

            colorPalletesFi.SetValue(
                settings,
                new Dictionary<string, string> { { "test-empty-text", null } }
            );
            var jsonResult = settings.GetColorPaletteAsJson("test-empty-text");
            Assert.That(jsonResult, Is.EqualTo("[]"));

            colorPalletesFi.SetValue(
                settings,
                new Dictionary<string, string> { { "test-empty-text", "" } }
            );
            jsonResult = settings.GetColorPaletteAsJson("test-empty-text");
            Assert.That(jsonResult, Is.EqualTo("[]"));
        }

        [TestCase("")]
        [TestCase("abc")]
        [TestCase("test>@example.com")]
        [TestCase("test @example.com")]
        [TestCase("test@example.com,notAnEmailAddress")]
        public void ValidateAdministrators_InvalidEmails_ReturnsFalse(string input)
        {
            Assert.That(CollectionSettings.ValidateAdministrators(input), Is.False);
        }

        [TestCase("test@example.com")]
        [TestCase("test@example.com test2@example.com")]
        [TestCase("test@example.com,test2@example.com")]
        [TestCase("test@example.com, test2@example.com")]
        [TestCase("  test@example.org, ,,,     test2@example.com, test3@example.com  ")]
        public void ValidateAdministrators_ValidEmails_ReturnsTrue(string input)
        {
            Assert.That(CollectionSettings.ValidateAdministrators(input), Is.True);
        }
    }
}
