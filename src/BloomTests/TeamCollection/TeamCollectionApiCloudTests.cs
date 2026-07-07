using System.Net;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.TeamCollection;
using Bloom.TeamCollection.Cloud;
using Bloom.web;
using BloomTemp;
using BloomTests.DataBuilders;
using BloomTests.TeamCollection.Cloud;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace BloomTests.TeamCollection
{
    /// <summary>
    /// Task 06: tests for TeamCollectionApi's additive cloud-only endpoints/fields
    /// (capabilities, tcStatusMetadata, cloudCollectionId, isUserAdmin, and the book-status JSON's
    /// localVersionSeq/repoVersionSeq/signedIn/requiresSignIn) and the byte-identical guarantee for
    /// no collection at all.
    ///
    /// Uses a REAL CloudTeamCollection (fake REST executor, no live network -- same pattern as
    /// CloudTeamCollectionLockTests) rather than a Moq mock, because the new task-06 accessors
    /// (GetLocalVersionSeq/GetRepoVersionSeq/GetUpdatesAvailableCount/Auth/Client) are plain
    /// members Moq cannot intercept, and their whole point is to read real cache state.
    ///
    /// The mock ITeamCollectionManager's CurrentCollection getter reads a lazy field
    /// (_cloudCollection) that starts, and STAYS, null until a test explicitly calls
    /// EnsureCloudCollection()/HydrateWith() -- so "no collection" tests never construct one at
    /// all (avoiding both TeamCollectionApi's constructor-time SetupMonitoringBehavior side effect
    /// and any lazy-hydration attempt against an unconfigured fake executor).
    /// </summary>
    [TestFixture]
    public class TeamCollectionApiCloudTests
    {
        private const string kCollectionId = "22222222-2222-2222-2222-222222222222";
        private TemporaryFolder _collectionFolder;
        private CloudTeamCollection _cloudCollection; // null until EnsureCloudCollection() is called
        private CloudEnvironment _environment;
        private CloudAuth _auth;
        private CloudCollectionClient _client;
        private FakeRestExecutor _executor;
        private BloomServer _server;
        private TeamCollectionApi _api;

        // One shared BloomServer for the whole fixture (matching TeamCollectionApiServerTests'
        // established pattern below in TeamCollectionApiTests.cs) -- BloomServer binds to a
        // process-wide port, so creating/disposing a fresh one per test is unreliable; instead
        // each [SetUp] clears and re-registers handlers on the same server.
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _server = new BloomServer(new BookSelection());
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _server?.Dispose();
            _server = null;
        }

        [SetUp]
        public void Setup()
        {
            // NUnit's default fixture lifecycle is ONE SHARED instance across every [Test] in this
            // class (SetUp/TearDown run per-test, but instance fields persist between them) -- so
            // this reset matters even though it looks redundant with the field initializer.
            _cloudCollection = null;
            _server.ApiHandler.ClearEndpointHandlers();
            _collectionFolder = new TemporaryFolder("TeamCollectionApiCloudTests");
            TeamCollectionManager.ForceCurrentUserForTests("test@somewhere.org");

            var mockTcManager = new Mock<ITeamCollectionManager>();
            // Lazy: re-evaluated on every access, so it reflects _cloudCollection's value AT CALL
            // TIME -- null for every "no collection" test (which never calls
            // EnsureCloudCollection()), and the real object for every other test.
            mockTcManager.SetupGet(m => m.CurrentCollection).Returns(() => _cloudCollection);
            mockTcManager
                .SetupGet(m => m.CurrentCollectionEvenIfDisconnected)
                .Returns(() => _cloudCollection);
            mockTcManager.SetupGet(m => m.OkToEditCollectionSettings).Returns(true);

            var apiBuilder = new TeamCollectionApiBuilder()
                .WithTeamCollectionManager(mockTcManager.Object)
                .WithCollectionSettings(new CollectionSettings())
                .WithBookSelection(new BookSelection());
            _api = apiBuilder.Build(); // _cloudCollection is null here regardless -- see above.
            _api.RegisterWithApiHandler(_server.ApiHandler);

            _environment = new CloudEnvironment(name =>
                name == "BLOOM_CLOUDTC_ANON_KEY" ? "test-anon-key" : null
            );
            _auth = new CloudAuth(new StubCloudAuthProvider(), new InMemoryCloudTokenStore());
            _client = new CloudCollectionClient(_environment, _auth);
            _executor = new FakeRestExecutor();
            _client.SetRestClientForTests(_executor);

            _mockTcManagerForTests = mockTcManager;
        }

        // Kept only so EnsureCloudCollection() (called from within a [Test] body, well after
        // Setup returns) can pass the SAME manager object CloudTeamCollection's constructor wants.
        private Mock<ITeamCollectionManager> _mockTcManagerForTests;

        [TearDown]
        public void TearDown()
        {
            _collectionFolder.Dispose();
            TeamCollectionManager.ForceCurrentUserForTests(null);
        }

        /// <summary>Constructs the real CloudTeamCollection on first use (idempotent). Until a test
        /// calls this (directly or via HydrateWith), CurrentCollection stays null throughout the
        /// ENTIRE test, including its Setup -- required for the "no collection" tests below.</summary>
        private CloudTeamCollection EnsureCloudCollection()
        {
            if (_cloudCollection == null)
            {
                _cloudCollection = new CloudTeamCollection(
                    _mockTcManagerForTests.Object,
                    _collectionFolder.FolderPath,
                    kCollectionId,
                    environment: _environment,
                    auth: _auth,
                    client: _client
                );
            }
            return _cloudCollection;
        }

        private void HydrateWith(params JObject[] books)
        {
            _executor.Handler = req =>
                FakeResponses.Make(
                    HttpStatusCode.OK,
                    new JObject
                    {
                        ["books"] = new JArray(books),
                        ["groups"] = new JArray(),
                        ["max_event_id"] = 1,
                    }.ToString()
                );
            EnsureCloudCollection().IsBookPresentInRepo("force hydration"); // any call triggers it
        }

        private static JObject Book(
            string id,
            string name,
            long? currentVersionSeq,
            string lockedBy = null
        ) =>
            new JObject
            {
                ["id"] = id,
                ["instance_id"] = id + "-instance",
                ["name"] = name,
                ["current_version_id"] = currentVersionSeq.HasValue
                    ? "v-" + currentVersionSeq
                    : null,
                ["current_version_seq"] = currentVersionSeq,
                ["current_checksum"] = "cs",
                ["locked_by"] = lockedBy,
                ["locked_by_machine"] = lockedBy == null ? null : "SomeMachine",
                ["locked_at"] = null,
                ["deleted_at"] = null,
            };

        [Test]
        public void Capabilities_NoCollection_AllFalse()
        {
            var result = ApiTest.GetString(_server, endPoint: "teamCollection/capabilities");
            Assert.That(result, Does.Contain("\"supportsVersionHistory\":false"));
            Assert.That(result, Does.Contain("\"supportsSharingUi\":false"));
            Assert.That(result, Does.Contain("\"requiresSignIn\":false"));
        }

        [Test]
        public void Capabilities_CloudCollection_AllTrue()
        {
            HydrateWith();
            var result = ApiTest.GetString(_server, endPoint: "teamCollection/capabilities");
            Assert.That(result, Does.Contain("\"supportsVersionHistory\":true"));
            Assert.That(result, Does.Contain("\"supportsSharingUi\":true"));
            Assert.That(result, Does.Contain("\"requiresSignIn\":true"));
        }

        [Test]
        public void CloudCollectionId_CloudCollection_ReturnsId()
        {
            HydrateWith();
            var result = ApiTest.GetString(_server, endPoint: "teamCollection/cloudCollectionId");
            Assert.That(result, Is.EqualTo(kCollectionId));
        }

        [Test]
        public void CloudCollectionId_NoCollection_ReturnsEmptyString()
        {
            var result = ApiTest.GetString(_server, endPoint: "teamCollection/cloudCollectionId");
            Assert.That(result, Is.EqualTo(""));
        }

        [Test]
        public void IsUserAdmin_ReflectsOkToEditCollectionSettings()
        {
            var result = ApiTest.GetString(_server, endPoint: "teamCollection/isUserAdmin");
            Assert.That(result, Is.EqualTo("true"));
        }

        [Test]
        public void TcStatusMetadata_UnlockedNewerBookInRepo_CountsAsUpdateAvailable()
        {
            HydrateWith(
                Book("book-1", "Book One", currentVersionSeq: 3), // never locally received
                Book("book-2", "Book Two", currentVersionSeq: 1, lockedBy: "someone-else") // locked: excluded
            );

            var result = ApiTest.GetString(_server, endPoint: "teamCollection/tcStatusMetadata");

            Assert.That(
                result,
                Does.Contain("\"updatesAvailableCount\":1"),
                "only the unlocked book with a repo version and no local version should count"
            );
        }

        [Test]
        public void TcStatusMetadata_NoCollection_NullCount()
        {
            var result = ApiTest.GetString(_server, endPoint: "teamCollection/tcStatusMetadata");
            Assert.That(result, Does.Contain("\"updatesAvailableCount\":null"));
        }

        [Test]
        public void AddCloudBookStatusFields_NoCollection_ReturnsInputByteIdentical()
        {
            const string original = "{\"who\":\"someone\",\"isUserAdmin\":true}";

            var result = _api.AddCloudBookStatusFields(original, "SomeBook");

            Assert.That(
                result,
                Is.SameAs(original),
                "must return the exact same string instance, not even a re-parsed copy, "
                    + "when the current collection isn't a CloudTeamCollection"
            );
        }

        [Test]
        public void AddCloudBookStatusFields_CloudCollection_AddsVersionSeqAndSignedInFields()
        {
            HydrateWith(Book("book-1", "My Book", currentVersionSeq: 5));
            const string original = "{\"who\":null,\"isUserAdmin\":true}";

            var result = _api.AddCloudBookStatusFields(original, "My Book");

            var json = JObject.Parse(result);
            Assert.That((long)json["repoVersionSeq"], Is.EqualTo(5));
            Assert.That(
                json["localVersionSeq"].Type,
                Is.EqualTo(JTokenType.Null),
                "book-1 has never been Sent/Received on this machine"
            );
            Assert.That((bool)json["requiresSignIn"], Is.True);
            Assert.That(
                (bool)json["signedIn"],
                Is.False,
                "the test's CloudAuth was constructed but SignIn() was never called"
            );
            // Pre-existing fields must survive untouched.
            Assert.That((bool)json["isUserAdmin"], Is.True);
        }

        // Regression for the first two-instance smoke test (7 Jul 2026): the base status JSON's
        // currentUser is Bloom's REGISTRATION email, but cloud locks are stamped with the
        // signed-in account -- so the panel's who === currentUser check called the user's own
        // checkout "someone else". For cloud TCs, currentUser must be the account email.
        [Test]
        public void AddCloudBookStatusFields_SignedIn_OverridesCurrentUserWithAccountEmail()
        {
            HydrateWith(Book("book-1", "My Book", currentVersionSeq: 5));
            _auth.SignIn("alice@dev.local", "irrelevant");
            const string original =
                "{\"who\":\"alice@dev.local\",\"currentUser\":\"registration@example.com\"}";

            var result = _api.AddCloudBookStatusFields(original, "My Book");

            var json = JObject.Parse(result);
            Assert.That((string)json["currentUser"], Is.EqualTo("alice@dev.local"));
        }

        [Test]
        public void AddCloudBookStatusFields_SignedOut_LeavesCurrentUserAlone()
        {
            HydrateWith(Book("book-1", "My Book", currentVersionSeq: 5));
            const string original = "{\"currentUser\":\"registration@example.com\"}";

            var result = _api.AddCloudBookStatusFields(original, "My Book");

            var json = JObject.Parse(result);
            Assert.That(
                (string)json["currentUser"],
                Is.EqualTo("registration@example.com"),
                "with no account email available there is nothing better to report"
            );
        }

        [Test]
        public void AddCloudBookStatusFields_NoBookFolderName_OnlyAddsCollectionWideFlags()
        {
            HydrateWith();
            const string original = "{\"who\":\"\"}";

            var result = _api.AddCloudBookStatusFields(original, null);

            var json = JObject.Parse(result);
            Assert.That(json["requiresSignIn"], Is.Not.Null);
            Assert.That(
                json["repoVersionSeq"],
                Is.Null,
                "there's no specific book to report a version for"
            );
        }
    }
}
