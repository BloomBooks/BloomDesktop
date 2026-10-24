using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.web.controllers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SIL.TestUtilities;

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

        /// <summary>
        /// The Front &amp; Back Matter page shows this as an image in its preview of the badge.
        /// </summary>
        [Test]
        public void MakeLanguageQrCodePng_GivesAPng()
        {
            var png = CollectionSettingsApi.MakeLanguageQrCodePng("cak");

            using (var stream = new MemoryStream(png))
            using (var image = Image.FromStream(stream))
            {
                Assert.That(image.RawFormat, Is.EqualTo(ImageFormat.Png));
                Assert.That(image.Width, Is.GreaterThan(0));
            }
        }

        /// <summary>
        /// The dialog's Style list offers only installed packs, so the value it is given must be
        /// one of them: the pack the books really get, not one that has since been uninstalled.
        /// </summary>
        [Test]
        public void FrontBackMatterXmatter_SavedPackNotInstalled_IsTheFactoryDefault()
        {
            using var folder = new TemporaryFolder("FrontBackMatterXmatter_NotInstalled");
            var settings = new CollectionSettings(
                CollectionSettings.GetPathForNewSettings(folder.Path, "Uninstalled")
            );
            var finder = new XMatterPackFinder(
                new[] { BloomFileLocator.GetFactoryXMatterDirectory() }
            );
            settings.XMatterPackName = "NoSuchPack";
            Assert.That(
                finder.GetXMattersToOfferInSettings(null).Select(p => p.Key),
                Has.No.Member("NoSuchPack"),
                "Sanity check: the saved pack should not be installed"
            );

            var values = new CollectionSettingsValues(settings, finder);

            Assert.That(values.FrontBackMatter.Xmatter, Is.EqualTo(finder.FactoryDefault.Key));
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
        /// The dialog looks each restart path up in the values it got, so a path that named
        /// nothing would silently stop that setting ever being noticed as needing a restart.
        /// </summary>
        [Test]
        public void EveryRestartPath_ResolvesInFullyPopulatedValues()
        {
            using var folder = new TemporaryFolder("EveryRestartPath_Resolves");
            var settings = new CollectionSettings(
                CollectionSettings.GetPathForNewSettings(folder.Path, "RestartPaths")
            );
            // A new collection has no third or sign language, and those are null in the values.
            settings.Language3.ChangeTag("fr");
            settings.SignLanguage.ChangeTag("ase");
            var json = JObject.Parse(
                JsonConvert.SerializeObject(
                    new CollectionSettingsValues(
                        settings,
                        new XMatterPackFinder(
                            new[] { BloomFileLocator.GetFactoryXMatterDirectory() }
                        )
                    ),
                    CollectionSettingsApi.kCamelCaseSettings
                )
            );
            var restartPaths = CollectionSettingsValues.GetRestartPaths();
            Assert.That(
                restartPaths,
                Has.Member("frontBackMatter.xmatter")
                    .And.Member("subscription.code")
                    .And.Member("languages.language3.fontName")
                    .And.Member("languages.signLanguage.tag")
                    .And.Member("languages.pageNumberStyle")
                    .And.Member("readerTools.letters")
                    .And.Member("experimental." + ExperimentalFeatures.kTeamCollections)
                    .And.Member("experimental." + ExperimentalFeatures.kTables),
                "Sanity check: the paths should cover every group"
            );

            foreach (var path in restartPaths)
            {
                Assert.That(
                    json.SelectToken(path),
                    Is.Not.Null,
                    $"restart path '{path}' names nothing in the settings values"
                );
            }
        }
    }
}
