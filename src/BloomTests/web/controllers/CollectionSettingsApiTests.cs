using System.Collections.Generic;
using Bloom;
using Bloom.web.controllers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace BloomTests.web.controllers
{
    [TestFixture]
    public class CollectionSettingsApiTests
    {
        // A language display name is arbitrary user text. Apostrophes are common in real language
        // names, and BL-16209 showed that a user can put anything at all in there, including the
        // characters that break a hand-built JSON string.
        private const string kNastyDisplayName = "Unsafe ~!@#$%^&*()_-+={}[]\\|:;\"'<>.?/";

        /// <summary>
        /// BL-16209: the languageData reply used to be assembled by string interpolation, so a
        /// display name containing a double quote or a backslash produced invalid JSON. The
        /// collection tab then read undefined for languageName, threw, and left the user with a
        /// blank screen they could not get past.
        /// </summary>
        [Test]
        public void MakeLanguageDataJson_NameHasJsonBreakingCharacters_ProducesValidParseableJson()
        {
            // Sanity check the test data: this only proves anything if the name really does
            // contain the characters that JSON has to escape.
            Assert.That(
                kNastyDisplayName,
                Does.Contain("\"").And.Contain("\\"),
                "Test data should contain a double quote and a backslash"
            );

            var json = CollectionSettingsApi.MakeLanguageDataJson(
                kNastyDisplayName,
                "qaa-BA-x-Unsafeam"
            );

            dynamic result = JsonConvert.DeserializeObject(json);
            Assert.That((string)result.languageName, Is.EqualTo(kNastyDisplayName));
            Assert.That((string)result.languageCode, Is.EqualTo("qaa-BA-x-Unsafeam"));
        }

        /// <summary>
        /// The clients treat languageName as a string (BooksOnBlorgProgressBar asks it for its
        /// .length), and the hand-built string this replaced turned a null name into "". Keep
        /// doing that, so a null can't reintroduce the BL-16209 crash by another route.
        /// </summary>
        [Test]
        public void MakeLanguageDataJson_NullName_ProducesEmptyStringNotNull()
        {
            var json = CollectionSettingsApi.MakeLanguageDataJson(null, null);

            dynamic result = JsonConvert.DeserializeObject(json);
            Assert.That((string)result.languageName, Is.EqualTo(""));
            Assert.That((string)result.languageCode, Is.EqualTo(""));
        }

        [Test]
        public void MakeLanguageDataJson_OrdinaryName_ProducesExpectedFields()
        {
            var json = CollectionSettingsApi.MakeLanguageDataJson("Kaqchikel", "cak");

            dynamic result = JsonConvert.DeserializeObject(json);
            Assert.That((string)result.languageName, Is.EqualTo("Kaqchikel"));
            Assert.That((string)result.languageCode, Is.EqualTo("cak"));
        }

        /// <summary>
        /// Values with a third language and nothing unusual about them, so each test can change
        /// just the one thing it is about.
        /// </summary>
        private static CollectionSettingsValues MakeValues()
        {
            return new CollectionSettingsValues
            {
                Languages = new LanguagesValues
                {
                    Language1 = new LanguageValues
                    {
                        Tag = "cak",
                        Name = "Kaqchikel",
                        FontName = "Andika",
                        LineHeight = 1.5m,
                    },
                    Language2 = new LanguageValues
                    {
                        Tag = "en",
                        Name = "English",
                        FontName = "Andika",
                    },
                    Language3 = new LanguageValues
                    {
                        Tag = "fr",
                        Name = "French",
                        FontName = "Andika",
                    },
                    SignLanguage = new SignLanguageValues { Tag = "ase", Name = "ASL" },
                },
                FrontBackMatter = new FrontBackMatterValues
                {
                    Xmatter = "Traditional",
                    PageNumberStyle = "Decimal",
                    ShowQrCode = true,
                    QrcodeCaption = "Find this book",
                    Country = "Guatemala",
                },
                Advanced = new AdvancedValues { AutoUpdate = true, CollectionName = "Test" },
                Experimental = new Dictionary<string, bool>
                {
                    { ExperimentalFeatures.kTeamCollections, false },
                },
            };
        }

        [Test]
        public void GetRestartPaths_NamesTheSettingsThatNeedARestart()
        {
            var paths = CollectionSettingsValues.GetRestartPaths();

            Assert.That(paths, Contains.Item("languages.language1.tag"));
            Assert.That(paths, Contains.Item("languages.language3.fontName"));
            Assert.That(paths, Contains.Item("languages.signLanguage.name"));
            Assert.That(paths, Contains.Item("frontBackMatter.xmatter"));
            Assert.That(paths, Contains.Item("advanced.collectionName"));
            Assert.That(
                paths,
                Contains.Item("experimental." + ExperimentalFeatures.kTeamCollections)
            );
            // A sign language has no font, and the country is not worth a restart.
            Assert.That(paths, Does.Not.Contain("languages.signLanguage.fontName"));
            Assert.That(paths, Does.Not.Contain("frontBackMatter.country"));
        }

        [Test]
        public void AnyRestartPathChanged_NothingChanged_False()
        {
            Assert.That(
                CollectionSettingsValues.AnyRestartPathChanged(MakeValues(), MakeValues()),
                Is.False
            );
        }

        [TestCase("xmatter")]
        [TestCase("pageNumberStyle")]
        public void AnyRestartPathChanged_FrontBackMatterSettingChanged_True(string field)
        {
            var before = MakeValues();
            var after = MakeValues();
            if (field == "xmatter")
                after.FrontBackMatter.Xmatter = "SuperPaperSaver";
            else
                after.FrontBackMatter.PageNumberStyle = "Devanagari";

            Assert.That(CollectionSettingsValues.AnyRestartPathChanged(before, after), Is.True);
        }

        [Test]
        public void AnyRestartPathChanged_LanguageFontChanged_True()
        {
            var before = MakeValues();
            var after = MakeValues();
            after.Languages.Language2.FontName = "Charis SIL";

            Assert.That(CollectionSettingsValues.AnyRestartPathChanged(before, after), Is.True);
        }

        /// <summary>
        /// The experimental feature tokens contain hyphens, which the path lookup has to cope with.
        /// </summary>
        [Test]
        public void AnyRestartPathChanged_TeamCollectionsToggled_True()
        {
            var before = MakeValues();
            var after = MakeValues();
            after.Experimental[ExperimentalFeatures.kTeamCollections] = true;
            Assert.That(
                before.Experimental[ExperimentalFeatures.kTeamCollections],
                Is.False,
                "Sanity check: the toggle has to start out off for this to be a change"
            );

            Assert.That(CollectionSettingsValues.AnyRestartPathChanged(before, after), Is.True);
        }

        [Test]
        public void AnyRestartPathChanged_ThirdLanguageRemoved_True()
        {
            var before = MakeValues();
            var after = MakeValues();
            Assert.That(
                before.Languages.Language3.Tag,
                Is.EqualTo("fr"),
                "Sanity check: there should be a third language to remove"
            );
            after.Languages.Language3 = null;

            Assert.That(CollectionSettingsValues.AnyRestartPathChanged(before, after), Is.True);
        }

        /// <summary>
        /// The settings that take effect without a restart must not make the dialog demand one.
        /// </summary>
        [Test]
        public void AnyRestartPathChanged_OnlyNonRestartSettingsChanged_False()
        {
            var before = MakeValues();
            var after = MakeValues();
            after.FrontBackMatter.Country = "Peru";
            after.Languages.Language1.LineHeight = 2.0m;
            after.Advanced.AutoUpdate = false;

            Assert.That(CollectionSettingsValues.AnyRestartPathChanged(before, after), Is.False);
        }

        /// <summary>
        /// A path that names nothing resolves to null, so a typo in the list would silently stop
        /// that setting ever being noticed as needing a restart.
        /// </summary>
        [Test]
        public void EveryRestartPath_ResolvesInFullyPopulatedValues()
        {
            var json = JObject.Parse(
                JsonConvert.SerializeObject(MakeValues(), CollectionSettingsApi.kCamelCaseSettings)
            );

            foreach (var path in CollectionSettingsValues.GetRestartPaths())
            {
                Assert.That(
                    json.SelectToken(path),
                    Is.Not.Null,
                    $"restart path '{path}' names nothing in the settings values"
                );
            }
        }

        /// <summary>
        /// The TypeScript interface ICollectionSettingsResponse is written against camelCase
        /// names, so the serialization the API uses has to produce them.
        /// </summary>
        [Test]
        public void SerializedValues_UseTheCamelCaseNamesTheContractPromises()
        {
            var json = JObject.Parse(
                JsonConvert.SerializeObject(MakeValues(), CollectionSettingsApi.kCamelCaseSettings)
            );

            Assert.That((string)json["languages"]["language1"]["fontName"], Is.EqualTo("Andika"));
            Assert.That(
                (int)json["languages"]["language1"]["baseUIFontSizeInPoints"],
                Is.EqualTo(0)
            );
            Assert.That((string)json["languages"]["signLanguage"]["tag"], Is.EqualTo("ase"));
            Assert.That(
                (string)json["frontBackMatter"]["qrcodeCaption"],
                Is.EqualTo("Find this book")
            );
            Assert.That((string)json["advanced"]["collectionName"], Is.EqualTo("Test"));
            // Dictionary keys are feature tokens and must not be camel-cased or otherwise rewritten.
            Assert.That(
                (bool)json["experimental"][ExperimentalFeatures.kTeamCollections],
                Is.False
            );
        }
    }
}
