using System.Collections.Generic;
using System.Linq;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Properties;
using Bloom.SubscriptionAndFeatures;
using Bloom.web.controllers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.Collection
{
    /// <summary>
    /// Covers the save half of the React Collection Settings dialog: the values it posts, applied
    /// to a collection by CollectionSettingsUpdater.
    /// </summary>
    [TestFixture]
    public class CollectionSettingsUpdaterTests
    {
        private TemporaryFolder _folder;
        private XMatterPackFinder _xmatterPackFinder;
        private string _originalEnabledFeatures;
        private bool _originalAutoUpdate;
        private List<string> _renameRequests;

        [OneTimeSetUp]
        public void FixtureSetup()
        {
            SIL.Reporting.ErrorReport.IsOkToInteractWithUser = false;
            _folder = new TemporaryFolder("CollectionSettingsUpdaterTests");
            _xmatterPackFinder = new XMatterPackFinder(
                new[] { BloomFileLocator.GetFactoryXMatterDirectory() }
            );
        }

        [OneTimeTearDown]
        public void FixtureTeardown()
        {
            _folder.Dispose();
        }

        /// <summary>
        /// Apply writes user-level settings (the experimental features and auto-update) as well as
        /// the collection, and those outlive the test, so put back whatever we found.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _originalEnabledFeatures = Settings.Default.EnabledExperimentalFeatures;
            _originalAutoUpdate = Settings.Default.AutoUpdate;
            _renameRequests = new List<string>();
        }

        [TearDown]
        public void TearDown()
        {
            Settings.Default.EnabledExperimentalFeatures = _originalEnabledFeatures;
            Settings.Default.AutoUpdate = _originalAutoUpdate;
            Settings.Default.Save();
        }

        /// <summary>
        /// A collection of its own for each test, so that one test's Save cannot affect another.
        /// </summary>
        private CollectionSettings CreateCollectionSettings(string collectionName)
        {
            return new CollectionSettings(
                CollectionSettings.GetPathForNewSettings(_folder.Path, collectionName)
            );
        }

        /// <summary>
        /// The values as the GET sends them, which is also what the dialog posts back when the
        /// user changed nothing.
        /// </summary>
        private static JObject CurrentValuesJson(CollectionSettings settings)
        {
            return JObject.FromObject(
                new CollectionSettingsValues(settings),
                JsonSerializer.Create(CollectionSettingsApi.kCamelCaseSettings)
            );
        }

        private void Apply(JObject postedValues, CollectionSettings settings)
        {
            CollectionSettingsUpdater.Apply(
                postedValues,
                settings,
                currentCollectionIsTeamCollection: false,
                _xmatterPackFinder,
                newName => _renameRequests.Add(newName)
            );
        }

        [Test]
        public void Apply_ChangedValues_WrittenToSettingsAndSaved()
        {
            var settings = CreateCollectionSettings("ApplyWritesEverything");
            // Sanity check: the values we are about to set must really be new, or the test proves nothing.
            Assert.That(
                settings.Country,
                Is.Not.EqualTo("Papua New Guinea"),
                "Sanity check: the country should not already be the one we are setting"
            );
            Assert.That(
                settings.PageNumberStyle,
                Is.EqualTo("Decimal"),
                "Sanity check: a new collection numbers its pages with Decimal"
            );
            Assert.That(
                settings.Language1.Tag,
                Is.Not.EqualTo("fr"),
                "Sanity check: Language1 should not already be French"
            );

            var values = CurrentValuesJson(settings);
            values["frontBackMatter"]["country"] = "Papua New Guinea";
            values["frontBackMatter"]["district"] = "Lae";
            values["frontBackMatter"]["pageNumberStyle"] = "Devanagari";
            values["frontBackMatter"]["xmatter"] = "SuperPaperSaver";
            values["languages"]["language1"]["tag"] = "fr";
            values["languages"]["language1"]["name"] = "Français";
            values["languages"]["language1"]["isCustomName"] = true;
            values["languages"]["language1"]["fontName"] = "Andika";

            Apply(values, settings);

            Assert.That(settings.Country, Is.EqualTo("Papua New Guinea"));
            Assert.That(settings.District, Is.EqualTo("Lae"));
            Assert.That(settings.PageNumberStyle, Is.EqualTo("Devanagari"));
            Assert.That(settings.XMatterPackName, Is.EqualTo("SuperPaperSaver"));
            Assert.That(settings.Language1.Tag, Is.EqualTo("fr"));
            Assert.That(settings.Language1.Name, Is.EqualTo("Français"));
            Assert.That(settings.Language1.FontName, Is.EqualTo("Andika"));

            var reloaded = new CollectionSettings(settings.SettingsFilePath);
            Assert.That(
                reloaded.Country,
                Is.EqualTo("Papua New Guinea"),
                "Apply should have saved the collection to disk"
            );
            Assert.That(reloaded.Language1.Tag, Is.EqualTo("fr"));
        }

        /// <summary>
        /// The React dialog posts every value back on OK, changed or not. Applying them must leave
        /// the collection as it was, and must not ask for a rename.
        /// </summary>
        [Test]
        public void Apply_UnchangedValues_NothingChanges()
        {
            var settings = CreateCollectionSettings("ApplyUnchanged");
            var before = CurrentValuesJson(settings);

            Apply((JObject)before.DeepClone(), settings);

            var after = CurrentValuesJson(settings);
            Assert.That(
                JToken.DeepEquals(before, after),
                Is.True,
                $"The values should not have moved.\nBefore: {before}\nAfter: {after}"
            );
            Assert.That(_renameRequests, Is.Empty);
        }

        [Test]
        public void Apply_NewCollectionName_QueuesSanitizedRename()
        {
            var settings = CreateCollectionSettings("ApplyRename");
            var values = CurrentValuesJson(settings);
            values["advanced"]["collectionName"] = "New: Name";

            Apply(values, settings);

            Assert.That(_renameRequests, Is.EqualTo(new[] { "New- Name" }));
            Assert.That(
                settings.CollectionName,
                Is.EqualTo("ApplyRename"),
                "the rename moves the folder later; Apply only queues it"
            );
        }

        /// <summary>
        /// The GET reports a missing third language as null, so posting null back means the
        /// collection should have none.
        /// </summary>
        [Test]
        public void Apply_Language3Null_RemovesThirdLanguage()
        {
            var settings = CreateCollectionSettings("ApplyRemovesLanguage3");
            var setup = CurrentValuesJson(settings);
            setup["languages"]["language3"] = JObject.FromObject(
                new LanguageValues { Tag = "fr", Name = "French" },
                JsonSerializer.Create(CollectionSettingsApi.kCamelCaseSettings)
            );
            Apply(setup, settings);
            Assert.That(
                settings.Language3.Tag,
                Is.EqualTo("fr"),
                "Sanity check: the collection should start with a third language"
            );

            var values = CurrentValuesJson(settings);
            Assert.That(
                values["languages"]["language3"].Type,
                Is.EqualTo(JTokenType.Object),
                "Sanity check: the GET should report the third language"
            );
            values["languages"]["language3"] = JValue.CreateNull();
            Apply(values, settings);

            Assert.That(settings.Language3.Tag, Is.Empty);
            Assert.That(
                CurrentValuesJson(settings)["languages"]["language3"].Type,
                Is.EqualTo(JTokenType.Null)
            );
        }

        /// <summary>
        /// The GET reports a missing sign language as null, so posting null back means the
        /// collection should have none.
        /// </summary>
        [Test]
        public void Apply_SignLanguageNull_RemovesSignLanguage()
        {
            var settings = CreateCollectionSettings("ApplyRemovesSignLanguage");
            var setup = CurrentValuesJson(settings);
            setup["languages"]["signLanguage"] = JObject.FromObject(
                new SignLanguageValues { Tag = "ase", Name = "American Sign Language" },
                JsonSerializer.Create(CollectionSettingsApi.kCamelCaseSettings)
            );
            Apply(setup, settings);
            var values = CurrentValuesJson(settings);
            Assert.That(
                (string)values["languages"]["signLanguage"]?["tag"],
                Is.EqualTo("ase"),
                "Sanity check: the GET should report the sign language"
            );

            values["languages"]["signLanguage"] = JValue.CreateNull();
            Apply(values, settings);

            Assert.That(settings.SignLanguage.Tag, Is.Empty);
            Assert.That(
                CurrentValuesJson(settings)["languages"]["signLanguage"].Type,
                Is.EqualTo(JTokenType.Null)
            );
        }

        /// <summary>
        /// No point in letting them have the Nat lang 2 be the same as 1.
        /// </summary>
        [Test]
        public void Apply_Language3SameAsLanguage2_ClearsLanguage3()
        {
            var settings = CreateCollectionSettings("ApplyClearsDuplicateLanguage3");
            var values = CurrentValuesJson(settings);
            values["languages"]["language2"]["tag"] = "fr";
            values["languages"]["language3"] = JObject.FromObject(
                new LanguageValues { Tag = "fr", Name = "French" },
                JsonSerializer.Create(CollectionSettingsApi.kCamelCaseSettings)
            );

            Apply(values, settings);

            Assert.That(settings.Language2.Tag, Is.EqualTo("fr"));
            Assert.That(settings.Language3.Tag, Is.Empty);
            Assert.That(settings.Language3.Name, Is.Empty);
        }

        /// <summary>
        /// A pack that is no longer available gets replaced while saving.
        /// </summary>
        [Test]
        public void Apply_UnavailableXmatter_IsReplaced()
        {
            var settings = CreateCollectionSettings("ApplyXmatterCorrected");
            var values = CurrentValuesJson(settings);
            values["frontBackMatter"]["xmatter"] = "NoSuchXmatterPack";

            Apply(values, settings);

            Assert.That(settings.XMatterPackName, Is.Not.EqualTo("NoSuchXmatterPack"));
        }

        /// <summary>
        /// CollectionSettings leaves the places null for a collection whose file never carried
        /// them; the dialog gets empty strings instead, and posting those back saves them.
        /// </summary>
        [Test]
        public void Apply_PlacesAreNull_SavesThemAsEmptyStrings()
        {
            var settings = CreateCollectionSettings("ApplyNullPlaces");
            settings.Country = null;
            settings.Province = null;
            settings.District = null;
            var values = CurrentValuesJson(settings);
            Assert.That(
                (string)values["frontBackMatter"]["country"],
                Is.Empty,
                "Sanity check: the GET should send a string, not a null"
            );

            Apply(values, settings);

            Assert.That(settings.Country, Is.Empty);
            Assert.That(settings.Province, Is.Empty);
            Assert.That(settings.District, Is.Empty);
        }

        /// <summary>
        /// With an expired subscription the collection remembers its bookshelf only in memory
        /// (ExpiredBookshelf); saving any setting must still write it to the file, or the
        /// bookshelf is gone when the subscription is renewed (BL-15056).
        /// </summary>
        [Test]
        public void Apply_ExpiredSubscriptionBookshelf_StaysInTheFile()
        {
            var settings = CreateCollectionSettings("ApplyKeepsExpiredBookshelf");
            // What loading a collection with an expired subscription and a bookshelf leaves.
            settings.DefaultBookshelf = "";
            settings.ExpiredBookshelf = "rememberedShelf";

            Apply(CurrentValuesJson(settings), settings);

            Assert.That(
                RobustFile.ReadAllText(settings.SettingsFilePath),
                Does.Contain("bookshelf:rememberedShelf"),
                "the saved file should still carry the bookshelf"
            );
            Assert.That(
                settings.DefaultBookshelf,
                Is.Empty,
                "in memory it should stay hidden while the subscription is expired"
            );
            Assert.That(settings.ExpiredBookshelf, Is.EqualTo("rememberedShelf"));
        }

        /// <summary>
        /// The file is checked rather than a reloaded collection, because loading keeps the
        /// bookshelf only for an Enterprise subscription, which a test collection has not got.
        /// </summary>
        [Test]
        public void Apply_ChangedBookshelf_SavedToTheFile()
        {
            var settings = CreateCollectionSettings("ApplySavesBookshelf");
            var values = CurrentValuesJson(settings);
            Assert.That(
                (string)values["bloomLibrary"]["defaultBookshelf"],
                Is.Empty,
                "Sanity check: a new collection should have no bookshelf"
            );
            values["bloomLibrary"]["defaultBookshelf"] = "chosenShelf";

            Apply(values, settings);

            Assert.That(settings.DefaultBookshelf, Is.EqualTo("chosenShelf"));
            Assert.That(
                RobustFile.ReadAllText(settings.SettingsFilePath),
                Does.Contain("bookshelf:chosenShelf")
            );
        }

        [Test]
        public void Apply_BookshelfSetToNone_RemovedFromTheFile()
        {
            var settings = CreateCollectionSettings("ApplyRemovesBookshelf");
            settings.DefaultBookshelf = "oldShelf";
            settings.Save();
            Assert.That(
                RobustFile.ReadAllText(settings.SettingsFilePath),
                Does.Contain("bookshelf:oldShelf"),
                "Sanity check: the file should start with a bookshelf"
            );
            var values = CurrentValuesJson(settings);
            values["bloomLibrary"]["defaultBookshelf"] = "";

            Apply(values, settings);

            Assert.That(settings.DefaultBookshelf, Is.Empty);
            Assert.That(
                RobustFile.ReadAllText(settings.SettingsFilePath),
                Does.Not.Contain("bookshelf:")
            );
        }

        [Test]
        public void ApplyAdministrators_OnlyInATeamCollection()
        {
            var settings = CreateCollectionSettings("ApplyAdministrators");
            Assert.That(
                settings.AdministratorsDisplayString,
                Is.Empty,
                "Sanity check: a new collection has no administrators"
            );

            CollectionSettingsUpdater.ApplyAdministrators(
                "someone@example.com",
                settings,
                currentCollectionIsTeamCollection: false
            );
            Assert.That(settings.AdministratorsDisplayString, Is.Empty);

            CollectionSettingsUpdater.ApplyAdministrators(
                "someone@example.com, another@example.com",
                settings,
                currentCollectionIsTeamCollection: true
            );
            Assert.That(
                settings.AdministratorsDisplayString,
                Is.EqualTo("someone@example.com, another@example.com")
            );
        }

        /// <summary>
        /// Pro tier is not allowed in a Team Collection; elsewhere it is applied.
        /// </summary>
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void ApplySubscriptionAndBookshelf_ProSubscription_RefusedOnlyInATeamCollection(
            bool isTeamCollection,
            bool expectApplied
        )
        {
            var settings = CreateCollectionSettings("ApplyProSubscription" + isTeamCollection);
            var pro = Subscription.ForUnitTestWithOverrideTierOrDescriptor(
                SubscriptionTier.Pro,
                "Test-Pro-Subscription"
            );
            Assert.That(
                settings.Subscription.Descriptor,
                Is.Not.EqualTo(pro.Descriptor),
                "Sanity check: the collection should not already have this subscription"
            );

            CollectionSettingsUpdater.ApplySubscriptionAndBookshelf(
                pro,
                null,
                settings,
                isTeamCollection
            );

            Assert.That(
                settings.Subscription.Descriptor == pro.Descriptor,
                Is.EqualTo(expectApplied)
            );
        }

        /// <summary>
        /// The default QR caption follows the UI language, so posting it back unchanged must not
        /// store it, or it would stay in that language.
        /// </summary>
        [Test]
        public void Apply_UnchangedQrCaption_IsNotStored()
        {
            var settings = CreateCollectionSettings("ApplyQrCaption");
            Assert.That(
                settings.BadgeQrCodeLabel,
                Is.Null.Or.Empty,
                "Sanity check: a new collection has no caption of its own"
            );

            Apply(CurrentValuesJson(settings), settings);

            Assert.That(settings.BadgeQrCodeLabel, Is.Null.Or.Empty);
        }
    }
}
