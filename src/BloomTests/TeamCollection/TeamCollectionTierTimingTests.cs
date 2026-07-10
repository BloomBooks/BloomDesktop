using System.Reflection;
using Bloom.Api;
using Bloom.Collection;
using Bloom.SubscriptionAndFeatures;
using Bloom.TeamCollection;
using Bloom.TeamCollection.Cloud;
using BloomTemp;
using BloomTests.TeamCollection.Cloud;
using Moq;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.TeamCollection
{
    /// <summary>
    /// Pins down the fix for the "subscription-tier check timing" bug (GOING-LIVE.md Phase 5):
    /// TeamCollectionManager.CheckDisablingTeamCollections' only readiness gate is
    /// CurrentCollection == null, which for a cloud Team Collection does not reliably mean
    /// "Settings.Subscription reflects the repo's authoritative value" (see
    /// GetSubscriptionForDisablingCheck's own doc comment for the full mechanism). These tests
    /// exercise CheckDisablingTeamCollections directly against a real TeamCollectionManager
    /// (constructed with no TeamCollectionLink.txt present, so its constructor's own TC-loading
    /// logic is a no-op and CurrentCollection starts null -- the same pattern used by
    /// TeamCollectionAccountSwitchRefusalTests) with a fake TeamCollection installed via
    /// reflection (CurrentCollection has a private setter by design).
    /// </summary>
    [TestFixture]
    public class TeamCollectionTierTimingTests
    {
        private const string kSufficientCode = "Fake-LC-006273-1463"; // parses to LocalCommunity tier
        private TemporaryFolder _localCollection;
        private string _collectionSettingsPath;
        private TeamCollectionManager _tcManager;

        [SetUp]
        public void Setup()
        {
            _localCollection = new TemporaryFolder("TeamCollectionTierTimingTests");
            _collectionSettingsPath = CollectionSettings.GetSettingsFilePath(
                _localCollection.FolderPath
            );
            // No TeamCollectionLink.txt in this folder, so the constructor's own TC-loading
            // logic is a no-op; CurrentCollection starts null and each test installs whatever
            // fake it needs via reflection (see InstallCurrentCollection).
            _tcManager = new TeamCollectionManager(
                _collectionSettingsPath,
                new BloomWebSocketServer(),
                null,
                null,
                null,
                null
            );
            TeamCollectionManager.ForceCurrentUserForTests("test@somewhere.org");
        }

        [TearDown]
        public void TearDown()
        {
            TeamCollectionManager.ForceCurrentUserForTests(null);
            _localCollection.Dispose();
        }

        private void InstallCurrentCollection(Bloom.TeamCollection.TeamCollection collection)
        {
            // CurrentCollection has a private setter (by design -- nothing outside
            // TeamCollectionManager itself should replace it); TeamCollectionAccountSwitchRefusalTests
            // already established this reflection pattern for exactly this reason.
            typeof(TeamCollectionManager)
                .GetProperty(
                    nameof(TeamCollectionManager.CurrentCollection),
                    BindingFlags.Public | BindingFlags.Instance
                )
                .SetValue(_tcManager, collection);
        }

        /// <summary>
        /// A real CloudTeamCollection wired with the same StubCloudAuthProvider/FakeRestExecutor/
        /// InMemoryCloudTokenStore fakes CloudSyncAtStartupTests and CloudTeamCollectionMemberTests
        /// use, so its constructor never attempts real network access. No network method is ever
        /// called on it here -- the point is only that CurrentCollection is genuinely a
        /// Cloud.CloudTeamCollection, matching what GetSubscriptionForDisablingCheck type-checks
        /// for.
        /// </summary>
        private CloudTeamCollection MakeFakeCloudCollection()
        {
            var environment = new CloudEnvironment(name =>
                name == "BLOOM_CLOUDTC_ANON_KEY" ? "test-anon-key" : null
            );
            var auth = new CloudAuth(new StubCloudAuthProvider(), new InMemoryCloudTokenStore());
            var client = new CloudCollectionClient(environment, auth);
            client.SetRestClientForTests(new FakeRestExecutor());
            return new CloudTeamCollection(
                new Mock<ITeamCollectionManager>().Object,
                _localCollection.FolderPath,
                "11111111-1111-1111-1111-111111111111",
                environment: environment,
                auth: auth,
                client: client,
                transfer: new CloudBookTransfer(_ => new Mock<Amazon.S3.IAmazonS3>().Object)
            );
        }

        private Mock<Bloom.TeamCollection.TeamCollection> MakeFakeNonCloudCollection()
        {
            // Any non-CloudTeamCollection TeamCollection exercises the "else" branch of
            // GetSubscriptionForDisablingCheck; a bare mock of the abstract base class is enough
            // since folder-specific behavior isn't in play for this check.
            var fake = new Mock<Bloom.TeamCollection.TeamCollection>();
            fake.Setup(c => c.RepoDescription).Returns("fake-folder-repo");
            return fake;
        }

        private void WriteOnDiskSubscriptionCode(string code)
        {
            // Minimal .bloomCollection XML -- CollectionSettings.Load only needs SubscriptionCode
            // for what GetSubscriptionForDisablingCheck's cloud path re-reads.
            var xml =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n"
                + "<Collection version=\"0.2\">\r\n"
                + $"\t<SubscriptionCode>{code}</SubscriptionCode>\r\n"
                + "</Collection>";
            RobustFile.WriteAllText(_collectionSettingsPath, xml);
        }

        private static CollectionSettings SettingsWithSubscription(SubscriptionTier tier)
        {
            return new CollectionSettings
            {
                Subscription = Subscription.CreateTempSubscriptionForTier(tier),
            };
        }

        [Test]
        public void CheckDisablingTeamCollections_CloudTc_StaleInMemorySettingsButFreshDiskSufficient_DoesNotDisable()
        {
            // The diagnosed misfire: Settings.Subscription (captured once, at ProjectContext
            // startup, and never reloaded mid-session) is stale/insufficient, but the on-disk
            // .bloomCollection file -- what the cloud collection-file sync actually refreshes --
            // already carries a sufficient, valid code by the time this check runs. Before the
            // fix, this scenario intermittently and permanently disabled a healthy cloud TC.
            WriteOnDiskSubscriptionCode(kSufficientCode);
            _tcManager.Settings = SettingsWithSubscription(SubscriptionTier.Basic);
            InstallCurrentCollection(MakeFakeCloudCollection());

            _tcManager.CheckDisablingTeamCollections(_tcManager.Settings);

            Assert.That(
                _tcManager.CurrentCollection,
                Is.Not.Null,
                "the cloud TC should not have been disabled: the fresh on-disk subscription is "
                    + "sufficient, even though the stale in-memory snapshot was not"
            );
        }

        [Test]
        public void CheckDisablingTeamCollections_CloudTc_GenuinelyInsufficientOnDisk_StillDisables()
        {
            // Control case: a cloud TC whose freshly-synced on-disk subscription really IS
            // insufficient must still be disabled -- the fix must not turn into "never disable a
            // cloud TC for subscription reasons." Note the in-memory snapshot is (implausibly)
            // sufficient here, to prove the cloud path really does trust the disk read over it.
            WriteOnDiskSubscriptionCode("");
            _tcManager.Settings = SettingsWithSubscription(SubscriptionTier.Enterprise);
            InstallCurrentCollection(MakeFakeCloudCollection());

            _tcManager.CheckDisablingTeamCollections(_tcManager.Settings);

            Assert.That(
                _tcManager.CurrentCollection,
                Is.Null,
                "a genuinely insufficient cloud subscription must still disable the TC"
            );
            Assert.That(
                _tcManager.CurrentCollectionEvenIfDisconnected,
                Is.InstanceOf<DisconnectedTeamCollection>()
            );
            Assert.That(
                (
                    (DisconnectedTeamCollection)_tcManager.CurrentCollectionEvenIfDisconnected
                ).DisconnectedBecauseOfSubscriptionTier,
                Is.True
            );
        }

        [Test]
        public void CheckDisablingTeamCollections_NonCloudTc_UsesInMemorySettings_IgnoringDisk()
        {
            // Folder TCs (and anything else that isn't a CloudTeamCollection) must be
            // byte-identical to the pre-fix behavior: only Settings.Subscription (in-memory) is
            // consulted; the on-disk file is never re-read for this check.
            WriteOnDiskSubscriptionCode(kSufficientCode); // sufficient on disk...
            _tcManager.Settings = SettingsWithSubscription(SubscriptionTier.Basic); // ...but not in memory
            InstallCurrentCollection(MakeFakeNonCloudCollection().Object);

            _tcManager.CheckDisablingTeamCollections(_tcManager.Settings);

            Assert.That(
                _tcManager.CurrentCollection,
                Is.Null,
                "the in-memory (insufficient) Settings.Subscription should have disabled it, "
                    + "exactly as before this fix -- the sufficient on-disk file must be ignored "
                    + "for a non-cloud collection"
            );
            Assert.That(
                (
                    (DisconnectedTeamCollection)_tcManager.CurrentCollectionEvenIfDisconnected
                ).DisconnectedBecauseOfSubscriptionTier,
                Is.True
            );
        }

        [Test]
        public void CheckDisablingTeamCollections_NonCloudTc_SufficientInMemorySettings_NotDisabled_EvenIfDiskInsufficient()
        {
            WriteOnDiskSubscriptionCode(""); // insufficient on disk -- must be ignored for non-cloud
            _tcManager.Settings = SettingsWithSubscription(SubscriptionTier.LocalCommunity);
            InstallCurrentCollection(MakeFakeNonCloudCollection().Object);

            _tcManager.CheckDisablingTeamCollections(_tcManager.Settings);

            Assert.That(
                _tcManager.CurrentCollection,
                Is.Not.Null,
                "a sufficient in-memory subscription must not be overridden by an insufficient "
                    + "on-disk file for a non-cloud collection"
            );
        }

        [Test]
        public void CheckDisablingTeamCollections_CurrentCollectionNull_NoOp()
        {
            // Sanity check matching the pre-existing early-return: with no TC at all (or already
            // disabled), the check must do nothing, regardless of Settings.
            _tcManager.Settings = SettingsWithSubscription(SubscriptionTier.Basic);

            Assert.That(_tcManager.CurrentCollection, Is.Null, "sanity check: no TC installed");
            Assert.DoesNotThrow(() =>
                _tcManager.CheckDisablingTeamCollections(_tcManager.Settings)
            );
            Assert.That(_tcManager.CurrentCollectionEvenIfDisconnected, Is.Null);
        }
    }
}
