using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.SubscriptionAndFeatures;
using Bloom.TeamCollection;
using Bloom.TeamCollection.Cloud;
using Bloom.Workspace;
using BloomTemp;
using BloomTests.TeamCollection.Cloud;
using Moq;
using NUnit.Framework;

namespace BloomTests.TeamCollection
{
    /// <summary>
    /// Pins the OTHER half of the "tier-timing" fix: WorkspaceModel.HandleTeamStuffBeforeGetBookCollections'
    /// call ordering between TeamCollectionManager.CheckDisablingTeamCollections and
    /// TeamCollection.SynchronizeRepoAndLocal. Folder TCs must keep the original order (check,
    /// then sync); cloud TCs must have the check deferred to run after sync (see WorkspaceModel.cs
    /// and TeamCollectionTierTimingTests for the companion tests of the check's own logic).
    ///
    /// TeamCollectionManager.CheckDisablingTeamCollections and TeamCollection.SynchronizeRepoAndLocal
    /// were both made virtual (from a previously non-virtual "public void") purely so these
    /// recording subclasses could observe call order without ever invoking the real
    /// SynchronizeRepoAndLocal implementation, which pops a real modal progress dialog -- unsafe in
    /// a unit test.
    /// </summary>
    [TestFixture]
    public class WorkspaceModelTierTimingOrderingTests
    {
        private TemporaryFolder _localCollection;
        private string _collectionSettingsPath;
        private RecordingTeamCollectionManager _tcManager;
        private CollectionSettings _collectionSettings;
        private WorkspaceModel _workspaceModel;

        private class RecordingTeamCollectionManager : TeamCollectionManager
        {
            public readonly List<string> CallOrder = new List<string>();

            public RecordingTeamCollectionManager(
                string localCollectionPath,
                BloomWebSocketServer ws
            )
                : base(localCollectionPath, ws, null, null, null, null) { }

            public override void CheckDisablingTeamCollections(CollectionSettings settings)
            {
                CallOrder.Add("check");
                base.CheckDisablingTeamCollections(settings);
            }
        }

        private class RecordingFolderTeamCollection : FolderTeamCollection
        {
            private readonly List<string> _callOrder;

            public RecordingFolderTeamCollection(
                List<string> callOrder,
                ITeamCollectionManager tcManager,
                string localCollectionFolder,
                string repoFolderPath
            )
                : base(tcManager, localCollectionFolder, repoFolderPath)
            {
                _callOrder = callOrder;
            }

            public override void SynchronizeRepoAndLocal(Action whenDone = null)
            {
                _callOrder.Add("sync");
                whenDone?.Invoke();
            }
        }

        private class RecordingCloudTeamCollection : CloudTeamCollection
        {
            private readonly List<string> _callOrder;

            public RecordingCloudTeamCollection(
                List<string> callOrder,
                ITeamCollectionManager tcManager,
                string localCollectionFolder,
                string collectionId,
                CloudEnvironment environment,
                CloudAuth auth,
                CloudCollectionClient client
            )
                : base(
                    tcManager,
                    localCollectionFolder,
                    collectionId,
                    environment: environment,
                    auth: auth,
                    client: client,
                    transfer: new CloudBookTransfer(_ => new Mock<Amazon.S3.IAmazonS3>().Object)
                )
            {
                _callOrder = callOrder;
            }

            public override void SynchronizeRepoAndLocal(Action whenDone = null)
            {
                _callOrder.Add("sync");
                whenDone?.Invoke();
            }
        }

        [SetUp]
        public void Setup()
        {
            _localCollection = new TemporaryFolder("WorkspaceModelTierTimingOrderingTests");
            _collectionSettingsPath = CollectionSettings.GetSettingsFilePath(
                _localCollection.FolderPath
            );
            _tcManager = new RecordingTeamCollectionManager(
                _collectionSettingsPath,
                new BloomWebSocketServer()
            );
            TeamCollectionManager.ForceCurrentUserForTests("test@somewhere.org");
            // Sufficient in every case here -- these tests are about ORDER, not about whether the
            // check disables anything (TeamCollectionTierTimingTests already covers the
            // disable/not-disable logic in isolation). Keeping the collection enabled throughout
            // also matters mechanically: if the check DID disable it, CurrentCollectionEvenIfDisconnected
            // would be replaced by a plain (non-recording) DisconnectedTeamCollection before
            // SynchronizeRepoAndLocal is reached, and that real implementation shows a live dialog.
            _collectionSettings = new CollectionSettings
            {
                Subscription = Subscription.CreateTempSubscriptionForTier(
                    SubscriptionTier.LocalCommunity
                ),
            };
            _tcManager.Settings = _collectionSettings;
            _workspaceModel = new WorkspaceModel(
                new BookSelection(),
                _localCollection.FolderPath,
                _tcManager,
                _collectionSettings,
                new Bloom.SourceCollectionsList()
            );
        }

        [TearDown]
        public void TearDown()
        {
            TeamCollectionManager.ForceCurrentUserForTests(null);
            _localCollection.Dispose();
        }

        private void InstallCollection(Bloom.TeamCollection.TeamCollection collection)
        {
            foreach (
                var propName in new[]
                {
                    nameof(TeamCollectionManager.CurrentCollection),
                    nameof(TeamCollectionManager.CurrentCollectionEvenIfDisconnected),
                }
            )
            {
                typeof(TeamCollectionManager)
                    .GetProperty(propName, BindingFlags.Public | BindingFlags.Instance)
                    .SetValue(_tcManager, collection);
            }
        }

        [Test]
        public void FolderTc_ChecksTierBeforeSync_OrderUnchanged()
        {
            var fake = new RecordingFolderTeamCollection(
                _tcManager.CallOrder,
                _tcManager,
                _localCollection.FolderPath,
                Path.Combine(_localCollection.FolderPath, "repo")
            );
            InstallCollection(fake);
            bool doneCalled = false;

            _workspaceModel.HandleTeamStuffBeforeGetBookCollections(() => doneCalled = true);

            Assert.That(
                _tcManager.CallOrder,
                Is.EqualTo(new[] { "check", "sync" }),
                "folder-TC ordering must be unchanged: the tier check runs BEFORE sync, exactly as before this fix"
            );
            Assert.That(doneCalled, Is.True, "whenDone must still fire");
        }

        [Test]
        public void CloudTc_DefersTierCheckUntilAfterSync()
        {
            var environment = new CloudEnvironment(name =>
                name == "BLOOM_CLOUDTC_ANON_KEY" ? "test-anon-key" : null
            );
            var auth = new CloudAuth(new StubCloudAuthProvider(), new InMemoryCloudTokenStore());
            var client = new CloudCollectionClient(environment, auth);
            client.SetRestClientForTests(new FakeRestExecutor());
            var fake = new RecordingCloudTeamCollection(
                _tcManager.CallOrder,
                _tcManager,
                _localCollection.FolderPath,
                "11111111-1111-1111-1111-111111111111",
                environment,
                auth,
                client
            );
            InstallCollection(fake);
            bool doneCalled = false;

            _workspaceModel.HandleTeamStuffBeforeGetBookCollections(() => doneCalled = true);

            Assert.That(
                _tcManager.CallOrder,
                Is.EqualTo(new[] { "sync", "check" }),
                "cloud-TC ordering must be deferred: sync runs BEFORE the tier check, so the check "
                    + "can see whatever the sync just refreshed on disk instead of racing it"
            );
            Assert.That(doneCalled, Is.True, "whenDone must still fire");
        }
    }
}
