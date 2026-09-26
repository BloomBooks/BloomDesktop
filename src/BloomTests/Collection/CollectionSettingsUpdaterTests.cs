using System.Collections.Generic;
using System.Linq;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Properties;
using Bloom.SubscriptionAndFeatures;
using Bloom.web.controllers;
using NUnit.Framework;
using SIL.TestUtilities;

namespace BloomTests.Collection
{
    /// <summary>
    /// Covers the save half of the Collection Settings dialogs: both the WinForms one and the
    /// React one hand their pending edits to CollectionSettingsUpdater.
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

        private bool Apply(
            PendingCollectionSettings pending,
            CollectionSettings settings,
            bool currentCollectionIsTeamCollection = false
        )
        {
            return CollectionSettingsUpdater.Apply(
                pending,
                settings,
                currentCollectionIsTeamCollection,
                _xmatterPackFinder,
                newName => _renameRequests.Add(newName)
            );
        }

        /// <summary>
        /// The subscription tab posts every change to the code field. Typing a new code and then
        /// putting the saved one back must leave nothing pending, or OK saves the undone code.
        /// </summary>
        [Test]
        public void RecordPendingSubscription_CodeChangedThenReverted_NothingPending()
        {
            var settings = CreateCollectionSettings("SubscriptionRevert");
            var pending = new PendingCollectionSettings(settings);
            var savedCode = settings.Subscription.Code;
            Assert.That(
                settings.Subscription.IsDifferent("Other-Code-123456-7890"),
                Is.True,
                "Sanity check: the new code has to differ from the saved one"
            );

            SubscriptionSettingsEditorApi.RecordPendingSubscription(
                pending,
                settings,
                new Subscription("Other-Code-123456-7890")
            );
            Assert.That(
                pending.Subscription?.Code,
                Is.EqualTo("Other-Code-123456-7890"),
                "Sanity check: a different code should be pending"
            );
            Assert.That(
                pending.RestartRequired,
                Is.True,
                "Sanity check: a changed subscription should need a restart"
            );

            SubscriptionSettingsEditorApi.RecordPendingSubscription(
                pending,
                settings,
                new Subscription(savedCode)
            );

            Assert.That(pending.Subscription, Is.Null);
            Assert.That(
                pending.RestartRequired,
                Is.False,
                "Undoing the only change should take back the restart too"
            );
        }

        /// <summary>
        /// Undoing a subscription edit must not take back a restart that some other change needs.
        /// </summary>
        [Test]
        public void RecordPendingSubscription_RevertedWithAnotherRestartChange_StillRestarts()
        {
            var settings = CreateCollectionSettings("SubscriptionRevertOtherChange");
            var pending = new PendingCollectionSettings(settings);
            pending.ChangeThatRequiresRestart();

            SubscriptionSettingsEditorApi.RecordPendingSubscription(
                pending,
                settings,
                new Subscription("Other-Code-123456-7890")
            );
            SubscriptionSettingsEditorApi.RecordPendingSubscription(
                pending,
                settings,
                new Subscription(settings.Subscription.Code)
            );

            Assert.That(pending.Subscription, Is.Null);
            Assert.That(pending.RestartRequired, Is.True);
        }

        [Test]
        public void Apply_ChangedValues_WrittenToSettingsAndSaved()
        {
            var settings = CreateCollectionSettings("ApplyWritesEverything");
            var pending = new PendingCollectionSettings(settings);
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

            pending.Country = "Papua New Guinea";
            pending.Province = " Morobe "; // Apply is expected to trim
            pending.District = "Lae";
            pending.NumberingStyle = "Devanagari";
            pending.Xmatter = "SuperPaperSaver";
            pending.Language1.ChangeTag("fr");
            pending.Language1.SetName("Français", true);
            pending.FontSelections[0] = "Andika";

            Apply(pending, settings);

            Assert.That(settings.Country, Is.EqualTo("Papua New Guinea"));
            Assert.That(settings.Province, Is.EqualTo("Morobe"), "should have been trimmed");
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
        /// No point in letting them have the Nat lang 2 be the same as 1.
        /// </summary>
        [Test]
        public void Apply_Language3SameAsLanguage2_ClearsLanguage3()
        {
            var settings = CreateCollectionSettings("ApplyClearsDuplicateLanguage3");
            var pending = new PendingCollectionSettings(settings);
            pending.Language2.ChangeTag("fr");
            pending.Language3.ChangeTag("fr");
            Assert.That(
                pending.Language3.Tag,
                Is.EqualTo("fr"),
                "Sanity check: the two languages should start out the same"
            );

            Apply(pending, settings);

            Assert.That(settings.Language2.Tag, Is.EqualTo("fr"));
            Assert.That(settings.Language3.Tag, Is.Empty);
            Assert.That(settings.Language3.Name, Is.Empty);
        }

        [Test]
        public void Apply_XmatterChangedByUser_ReportsRestartNeeded()
        {
            var settings = CreateCollectionSettings("ApplyXmatterRestart");
            var pending = new PendingCollectionSettings(settings);
            Assert.That(
                settings.XMatterPackName,
                Is.EqualTo("Traditional"),
                "Sanity check: a new collection uses the Traditional xmatter"
            );
            Assert.That(
                _xmatterPackFinder.GetXMattersToOfferInSettings(null).Select(x => x.Key),
                Contains.Item("SuperPaperSaver"),
                "Sanity check: the xmatter we switch to has to be one the user could choose"
            );

            // Both dialogs record a user's pack change this way.
            pending.Xmatter = "SuperPaperSaver";
            pending.ChangeThatRequiresRestart();

            Assert.That(Apply(pending, settings), Is.True);
            Assert.That(settings.XMatterPackName, Is.EqualTo("SuperPaperSaver"));
        }

        /// <summary>
        /// A pack that is no longer available gets corrected while saving; the user changed
        /// nothing, so Bloom must not restart on them.
        /// </summary>
        [Test]
        public void Apply_XmatterOnlyCorrectedWhileSaving_ReportsNoRestartNeeded()
        {
            var settings = CreateCollectionSettings("ApplyXmatterCorrected");
            var pending = new PendingCollectionSettings(settings);
            pending.Xmatter = "NoSuchXmatterPack";

            Assert.That(Apply(pending, settings), Is.False);
            Assert.That(
                settings.XMatterPackName,
                Is.Not.EqualTo("NoSuchXmatterPack"),
                "Sanity check: saving is expected to replace an unavailable pack"
            );
        }

        /// <summary>
        /// CollectionSettings leaves the places null for a collection whose file never carried
        /// them, and both the session and the values the API hands the dialog have to turn that
        /// into a string, because Apply trims what comes back.
        /// </summary>
        [Test]
        public void Apply_PlacesAreNull_SavesThemAsEmptyStrings()
        {
            var settings = CreateCollectionSettings("ApplyNullPlaces");
            settings.Country = null;
            settings.Province = null;
            settings.District = null;
            var pending = new PendingCollectionSettings(settings);
            Assert.That(
                pending.Country,
                Is.Empty,
                "Sanity check: the session should start with a string, not a null"
            );

            Apply(pending, settings);

            Assert.That(settings.Country, Is.Empty);
            Assert.That(settings.Province, Is.Empty);
            Assert.That(settings.District, Is.Empty);
        }

        [Test]
        public void Apply_NothingChanged_ReportsNoRestartNeeded()
        {
            var settings = CreateCollectionSettings("ApplyNoChangeNoRestart");
            var pending = new PendingCollectionSettings(settings);

            Assert.That(Apply(pending, settings), Is.False);
        }

        [Test]
        public void Validate_TeamCollectionWithBadAdministratorEmail_ReturnsMessage()
        {
            var settings = CreateCollectionSettings("ValidateBadAdmins");
            var pending = new PendingCollectionSettings(settings);
            pending.Administrators = "not-an-email-address";

            Assert.That(
                CollectionSettingsUpdater.Validate(
                    pending,
                    currentCollectionIsTeamCollection: false
                ),
                Is.Null,
                "Sanity check: administrators are only validated in a Team Collection"
            );
            Assert.That(
                CollectionSettingsUpdater.Validate(
                    pending,
                    currentCollectionIsTeamCollection: true
                ),
                Is.Not.Null.And.Not.Empty,
                "an invalid administrator email should be reported"
            );
        }

        [Test]
        public void Validate_TeamCollectionWithGoodAdministratorEmails_ReturnsNull()
        {
            var settings = CreateCollectionSettings("ValidateGoodAdmins");
            var pending = new PendingCollectionSettings(settings);
            pending.Administrators = "someone@example.com, another@example.com";

            Assert.That(
                CollectionSettingsUpdater.Validate(
                    pending,
                    currentCollectionIsTeamCollection: true
                ),
                Is.Null
            );
        }
    }
}
