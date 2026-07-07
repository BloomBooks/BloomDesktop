using System;
using System.Linq;
using System.Net;
using Bloom.TeamCollection;
using Bloom.TeamCollection.Cloud;
using BloomTemp;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RestSharp;

namespace BloomTests.TeamCollection.Cloud
{
    /// <summary>
    /// Tests for CloudTeamCollection's simple identity/capability members and its cache-backed
    /// "member" reads (status/presence/list), using the FakeRestExecutor/StubCloudAuthProvider
    /// pattern from CloudCollectionClientTests to script get_collection_state/my_collections
    /// responses instead of a live server.
    /// </summary>
    [TestFixture]
    public class CloudTeamCollectionMemberTests
    {
        private const string kCollectionId = "11111111-1111-1111-1111-111111111111";
        private TemporaryFolder _collectionFolder;
        private Mock<ITeamCollectionManager> _mockTcManager;
        private CloudTeamCollection _collection;
        private FakeRestExecutor _executor;
        private CloudAuth _auth;

        private static CloudEnvironment MakeEnvironment() =>
            new CloudEnvironment(name => name == "BLOOM_CLOUDTC_ANON_KEY" ? "test-anon-key" : null);

        [SetUp]
        public void Setup()
        {
            _collectionFolder = new TemporaryFolder("CloudTeamCollectionMemberTests");
            _mockTcManager = new Mock<ITeamCollectionManager>();
            TeamCollectionManager.ForceCurrentUserForTests("test@somewhere.org");

            _auth = new CloudAuth(new StubCloudAuthProvider(), new InMemoryCloudTokenStore());
            var environment = MakeEnvironment();
            var client = new CloudCollectionClient(environment, _auth);
            _executor = new FakeRestExecutor();
            client.SetRestClientForTests(_executor);

            _collection = new CloudTeamCollection(
                _mockTcManager.Object,
                _collectionFolder.FolderPath,
                kCollectionId,
                environment: environment,
                auth: _auth,
                client: client,
                transfer: new CloudBookTransfer(_ => new Mock<Amazon.S3.IAmazonS3>().Object)
            );
        }

        [TearDown]
        public void TearDown()
        {
            _collectionFolder.Dispose();
            TeamCollectionManager.ForceCurrentUserForTests(null);
        }

        [Test]
        public void GetBackendType_IsCloud()
        {
            Assert.That(_collection.GetBackendType(), Is.EqualTo("Cloud"));
        }

        [Test]
        public void RepoDescription_ContainsCollectionId()
        {
            Assert.That(_collection.RepoDescription, Does.Contain(kCollectionId));
        }

        [Test]
        public void CapabilityFlags_AllTrueForCloud()
        {
            Assert.That(_collection.SupportsVersionHistory, Is.True);
            Assert.That(_collection.SupportsSharingUi, Is.True);
            Assert.That(_collection.RequiresSignIn, Is.True);
        }

        private void ScriptCollectionState(params (string id, string name, bool deleted)[] books)
        {
            _executor.Handler = req =>
            {
                Assert.That(req.Resource, Is.EqualTo("rest/v1/rpc/get_collection_state"));
                var booksArray = new JArray(
                    books.Select(b =>
                        (JToken)
                            new JObject
                            {
                                ["id"] = b.id,
                                ["instance_id"] = "instance-" + b.id,
                                ["name"] = b.name,
                                ["current_version_id"] = "v1",
                                ["current_version_seq"] = 1,
                                ["current_checksum"] = "checksum-" + b.id,
                                ["locked_by"] = null,
                                ["locked_by_machine"] = null,
                                ["locked_at"] = null,
                                ["deleted_at"] = b.deleted
                                    ? (JToken)DateTime.UtcNow.ToString("o")
                                    : null,
                            }
                    )
                );
                var body = new JObject
                {
                    ["books"] = booksArray,
                    ["groups"] = new JArray(),
                    ["max_event_id"] = 1,
                };
                return FakeResponses.Make(HttpStatusCode.OK, body.ToString());
            };
        }

        [Test]
        public void GetBookList_ReturnsOnlyNonDeletedCommittedBooks()
        {
            ScriptCollectionState(("id-1", "Alive book", false), ("id-2", "Deleted book", true));

            var list = _collection.GetBookList();

            Assert.That(list, Contains.Item("Alive book"));
            Assert.That(list, Does.Not.Contain("Deleted book"));
        }

        [Test]
        public void IsBookPresentInRepo_TrueForCommittedBook_FalseForUnknown()
        {
            ScriptCollectionState(("id-1", "Alive book", false));

            Assert.That(_collection.IsBookPresentInRepo("Alive book"), Is.True);
            Assert.That(_collection.IsBookPresentInRepo("No such book"), Is.False);
        }

        [Test]
        public void KnownToHaveBeenDeleted_TrueOnlyForTombstonedBook()
        {
            ScriptCollectionState(("id-1", "Alive book", false), ("id-2", "Deleted book", true));
            // Force hydration so the name index knows about both books.
            _collection.IsBookPresentInRepo("Alive book");

            Assert.That(_collection.KnownToHaveBeenDeleted("Deleted book"), Is.True);
            Assert.That(_collection.KnownToHaveBeenDeleted("Alive book"), Is.False);
        }

        [Test]
        public void GetStatus_ForCommittedBook_ReflectsRepoChecksum()
        {
            ScriptCollectionState(("id-1", "Alive book", false));

            var status = _collection.GetStatus("Alive book");

            Assert.That(status.checksum, Is.EqualTo("checksum-id-1"));
            Assert.That(status.collectionId, Is.EqualTo(kCollectionId));
        }

        [Test]
        public void CheckConnection_NotSignedIn_ReturnsMessage()
        {
            var message = _collection.CheckConnection();

            Assert.That(message, Is.Not.Null);
            Assert.That(message.L10NId, Is.EqualTo("TeamCollection.Cloud.NotSignedIn"));
        }

        [Test]
        public void CheckConnection_SignedInButNotAMember_ReturnsMessage()
        {
            _auth.SignIn("test@somewhere.org", "irrelevant");
            _executor.Handler = req => FakeResponses.Make(HttpStatusCode.OK, "[]"); // my_collections: empty

            var message = _collection.CheckConnection();

            Assert.That(message, Is.Not.Null);
            Assert.That(message.L10NId, Is.EqualTo("TeamCollection.Cloud.NotAMember"));
        }

        [Test]
        public void CheckConnection_SignedInAndAMember_ReturnsNull()
        {
            _auth.SignIn("test@somewhere.org", "irrelevant");
            _executor.Handler = req =>
                FakeResponses.Make(
                    HttpStatusCode.OK,
                    new JArray(
                        new JObject { ["id"] = kCollectionId, ["name"] = "Some Collection" }
                    ).ToString()
                );

            var message = _collection.CheckConnection();

            Assert.That(message, Is.Null);
        }
    }
}
