using System;
using Bloom.TeamCollection;
using Bloom.TeamCollection.Cloud;
using BloomTemp;
using Moq;

namespace BloomTests.TeamCollection.Cloud
{
    /// <summary>
    /// Builds the CloudTeamCollection unit-test fixture that nearly every cloud test used to set up
    /// by hand: a temp collection folder, a mock <see cref="ITeamCollectionManager"/>, a <see
    /// cref="CloudEnvironment"/> stubbed with a test anon key, a <see cref="CloudAuth"/> over a
    /// StubCloudAuthProvider + in-memory token store, and a <see cref="CloudCollectionClient"/>
    /// whose REST calls are intercepted by a <see cref="FakeRestExecutor"/>. The pieces are handed
    /// back individually so each fixture stays explicit about what it drives (script the Executor,
    /// inspect the Auth, seed the folder...) and can inject its own S3 behaviour. <see
    /// cref="Dispose"/> undoes the two process-global test seams (the temp folder and the forced
    /// current user), matching the identical TearDown every one of these fixtures had.
    ///
    /// Deliberately NOT used by CloudCollectionClientTests / CloudCollectionMonitorTests: those
    /// test the client and monitor directly (the former with a custom auth provider) and never
    /// need a whole CloudTeamCollection, so sharing this would couple them to a fixture they don't
    /// exercise.
    /// </summary>
    internal sealed class CloudTestHarness : IDisposable
    {
        public TemporaryFolder CollectionFolder { get; private set; }
        public string CollectionFolderPath => CollectionFolder.FolderPath;
        public Mock<ITeamCollectionManager> MockTcManager { get; private set; }
        public CloudEnvironment Environment { get; private set; }
        public CloudAuth Auth { get; private set; }
        public CloudCollectionClient Client { get; private set; }
        public FakeRestExecutor Executor { get; private set; }
        public CloudTeamCollection Collection { get; private set; }

        /// <param name="folderName">Temp-folder label (conventionally the fixture's own name).</param>
        /// <param name="collectionId">The cloud collection id the CloudTeamCollection binds to.</param>
        /// <param name="currentUser">Value for TeamCollectionManager.ForceCurrentUserForTests -- the
        /// Bloom "current user"; reset to null on <see cref="Dispose"/>.</param>
        /// <param name="signIn">When true (the default) also signs CloudAuth in as <paramref
        /// name="currentUser"/>; pass false for tests that exercise the signed-out state.</param>
        /// <param name="s3Factory">Factory the CloudBookTransfer uses to make its S3 client;
        /// defaults to a bare Moq IAmazonS3 (so no real transfer happens). Sync tests pass a
        /// scripted one that serves fixed bytes.</param>
        public static CloudTestHarness Create(
            string folderName,
            string collectionId,
            string currentUser = "test@somewhere.org",
            bool signIn = true,
            Func<CloudS3Location, Amazon.S3.IAmazonS3> s3Factory = null
        )
        {
            var harness = new CloudTestHarness
            {
                CollectionFolder = new TemporaryFolder(folderName),
                MockTcManager = new Mock<ITeamCollectionManager>(),
            };
            TeamCollectionManager.ForceCurrentUserForTests(currentUser);

            harness.Environment = new CloudEnvironment(name =>
                name == "BLOOM_CLOUDTC_ANON_KEY" ? "test-anon-key" : null
            );
            harness.Auth = new CloudAuth(
                new StubCloudAuthProvider(),
                new InMemoryCloudTokenStore()
            );
            if (signIn)
                harness.Auth.SignIn(currentUser, "irrelevant");

            harness.Client = new CloudCollectionClient(harness.Environment, harness.Auth);
            harness.Executor = new FakeRestExecutor();
            harness.Client.SetRestClientForTests(harness.Executor);

            harness.Collection = new CloudTeamCollection(
                harness.MockTcManager.Object,
                harness.CollectionFolderPath,
                collectionId,
                environment: harness.Environment,
                auth: harness.Auth,
                client: harness.Client,
                transfer: new CloudBookTransfer(
                    s3Factory ?? (_ => new Mock<Amazon.S3.IAmazonS3>().Object)
                )
            );
            return harness;
        }

        public void Dispose()
        {
            CollectionFolder.Dispose();
            TeamCollectionManager.ForceCurrentUserForTests(null);
        }
    }
}
