using System;
using System.Collections.Generic;
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
        public void CheckConnection_SignedInButNotAMember_ReturnsRefusalMessage()
        {
            // Batch item 9 (account-switch behavior): non-membership is now a hard refusal
            // (IsAccessRefusal), not just an ordinary Disconnected-mode message. The renamed
            // L10NId ("NotAMemberRefusal") reflects that this message text changed shape (it now
            // composes admin/last-known-user detail, see ComposeNotAMemberRefusalDetail's own
            // tests below) -- there was no existing XLF entry for the old id to migrate.
            _auth.SignIn("test@somewhere.org", "irrelevant");
            _executor.Handler = req => FakeResponses.Make(HttpStatusCode.OK, "[]"); // my_collections: empty

            var message = _collection.CheckConnection();

            Assert.That(message, Is.Not.Null);
            Assert.That(message.L10NId, Is.EqualTo("TeamCollection.Cloud.NotAMemberRefusal"));
            Assert.That(message.IsAccessRefusal, Is.True);
            Assert.That(message.Param0, Is.EqualTo("test@somewhere.org"));
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

        [Test]
        public void CheckConnection_SignedInAndAMember_RecordsLastKnownUser()
        {
            // Batch item 9 (account-switch behavior): every successful membership confirmation
            // records the current user as the last known local user of this collection, so a
            // FUTURE non-member's refusal message can name them.
            Assert.That(
                TeamCollectionLastKnownUser.Read(_collectionFolder.FolderPath),
                Is.Null,
                "sanity check: nothing recorded before any successful connection check"
            );

            _auth.SignIn("test@somewhere.org", "irrelevant");
            _executor.Handler = req =>
                FakeResponses.Make(
                    HttpStatusCode.OK,
                    new JArray(
                        new JObject { ["id"] = kCollectionId, ["name"] = "Some Collection" }
                    ).ToString()
                );

            _collection.CheckConnection();

            Assert.That(
                TeamCollectionLastKnownUser.Read(_collectionFolder.FolderPath),
                Is.EqualTo("test@somewhere.org")
            );
        }

        [Test]
        public void CheckConnection_SignedInAndAMember_ClaimsMembershipsOncePerSession()
        {
            // Post-batch defect fix (10 Jul 2026): my_collections matches by EMAIL
            // (approved-or-claimed) but every data RPC's RLS gate matches by user_id, so an
            // approved-but-never-claimed account (batch item 9's shared-computer reopen, which
            // never runs the join flow that otherwise calls ClaimMemberships) passed
            // CheckConnection's member check and then threw not_a_member on the very first
            // sync's get_collection_state call -- inside TeamCollectionManager's constructor.
            _auth.SignIn("test@somewhere.org", "irrelevant");
            var requestedResources = new List<string>();
            _executor.Handler = req =>
            {
                requestedResources.Add(req.Resource);
                return FakeResponses.Make(
                    HttpStatusCode.OK,
                    req.Resource.EndsWith("claim_memberships")
                        ? "{}"
                        : new JArray(
                            new JObject { ["id"] = kCollectionId, ["name"] = "Some Collection" }
                        ).ToString()
                );
            };

            _collection.CheckConnection();
            _collection.CheckConnection();

            Assert.That(
                requestedResources.Count(r => r.EndsWith("claim_memberships")),
                Is.EqualTo(1),
                "claim_memberships should be called on the first successful member check and "
                    + "not repeated within the session (it is idempotent server-side)"
            );
        }

        [Test]
        public void CheckConnection_AccountSwitchMidSession_ClaimsAgainForTheNewAccount()
        {
            // Preflight review finding (10 Jul 2026): the claimed-flag must be per ACCOUNT, not
            // per instance -- this CloudTeamCollection survives an in-session sign-out +
            // sign-in as a different approved member (nothing disposes it on
            // CloudAuth.AccountSwitched), and skipping the second account's claim resurrects
            // the not_a_member startup failure the claim call exists to prevent.
            var requestedResources = new List<string>();
            _executor.Handler = req =>
            {
                requestedResources.Add(req.Resource);
                return FakeResponses.Make(
                    HttpStatusCode.OK,
                    req.Resource.EndsWith("claim_memberships")
                        ? "{}"
                        : new JArray(
                            new JObject { ["id"] = kCollectionId, ["name"] = "Some Collection" }
                        ).ToString()
                );
            };

            _auth.SignIn("first@somewhere.org", "irrelevant");
            _collection.CheckConnection();
            _auth.SignOut();
            _auth.SignIn("second@somewhere.org", "irrelevant");
            _collection.CheckConnection();

            Assert.That(
                requestedResources.Count(r => r.EndsWith("claim_memberships")),
                Is.EqualTo(2),
                "each distinct signed-in account must claim its own memberships"
            );
        }

        // Regression for the first two-instance smoke test (7 Jul 2026): poll-detected changes
        // must be raised with the folder backend's repo FILE name (".bloom" suffix) — the base
        // HandleModifiedFile starts with EndsWith(".bloom") and silently DISCARDS anything else,
        // which left teammates' UIs permanently stale even though the cache had fresh data.
        [Test]
        public void PolledChanges_RaiseBookStateChange_WithBloomSuffixedFileName()
        {
            ScriptCollectionState(("book-1", "My Book", false));
            _collection.HydrateFromServer();

            string raisedFileName = null;
            _collection.BookRepoChange += (sender, args) => raisedFileName = args.BookFileName;

            // Script the next get_changes poll: same book, now at seq 2 (someone checked in).
            _executor.Handler = req =>
            {
                Assert.That(req.Resource, Is.EqualTo("rest/v1/rpc/get_changes"));
                var body = new JObject
                {
                    ["events"] = new JArray(
                        new JObject
                        {
                            ["id"] = 2,
                            ["type"] = 1,
                            ["book_id"] = "book-1",
                        }
                    ),
                    ["books"] = new JArray(
                        new JObject
                        {
                            ["id"] = "book-1",
                            ["instance_id"] = "instance-book-1",
                            ["name"] = "My Book",
                            ["current_version_id"] = "v2",
                            ["current_version_seq"] = 2,
                            ["current_checksum"] = "checksum-2",
                            ["locked_by"] = null,
                            ["locked_by_machine"] = null,
                            ["locked_at"] = null,
                            ["deleted_at"] = null,
                        }
                    ),
                    ["max_event_id"] = 2,
                };
                return FakeResponses.Make(HttpStatusCode.OK, body.ToString());
            };

            try
            {
                _collection.StartMonitoring();
                _collection.PollNow();
            }
            finally
            {
                _collection.StopMonitoring();
            }

            Assert.That(
                raisedFileName,
                Is.EqualTo("My Book.bloom"),
                "the event contract wants the repo file name incl. suffix; the bare name is ignored by HandleModifiedFile"
            );
        }

        // Regression for the first two-instance smoke test (7 Jul 2026): the "other"
        // collection-file group downloads into the COLLECTION ROOT, and its naive
        // mirror-delete removed TeamCollectionLink.txt (never uploaded, by design),
        // silently un-teaming the collection for the next Bloom session.
        [Test]
        public void FilesEligibleForDeleteExtras_OtherGroup_NeverTouchesUnsharedRootFiles()
        {
            using (var folder = new TemporaryFolder("DeleteExtrasPolicy"))
            {
                System.IO.File.WriteAllText(
                    folder.Combine("My Collection.bloomCollection"),
                    "<Collection/>"
                );
                System.IO.File.WriteAllText(folder.Combine("customCollectionStyles.css"), "x");
                System.IO.File.WriteAllText(folder.Combine("TeamCollectionLink.txt"), "cloud://x");
                System.IO.File.WriteAllText(folder.Combine(".bloom-cloud-repo-cache.json"), "{}");
                System.IO.File.WriteAllText(folder.Combine("lastCollectionFileSyncData.txt"), "x");
                System.IO.File.WriteAllText(folder.Combine("log.txt"), "x");

                // Server group contains only the .bloomCollection: the css was deleted on
                // the server, so it (and ONLY it) is eligible for delete-extras.
                var kept = new System.Collections.Generic.HashSet<string>
                {
                    "My Collection.bloomCollection",
                };

                var doomed = CloudTeamCollection
                    .FilesEligibleForDeleteExtras("other", folder.FolderPath, kept)
                    .Select(System.IO.Path.GetFileName)
                    .ToList();

                Assert.That(doomed, Is.EquivalentTo(new[] { "customCollectionStyles.css" }));
            }
        }

        [Test]
        public void FilesEligibleForDeleteExtras_DedicatedGroupFolder_MirrorsExactly()
        {
            using (var folder = new TemporaryFolder("DeleteExtrasAllowedWords"))
            {
                System.IO.File.WriteAllText(folder.Combine("words1.txt"), "x");
                System.IO.File.WriteAllText(folder.Combine("words2.txt"), "x");
                var kept = new System.Collections.Generic.HashSet<string> { "words1.txt" };

                var doomed = CloudTeamCollection
                    .FilesEligibleForDeleteExtras("allowed-words", folder.FolderPath, kept)
                    .Select(System.IO.Path.GetFileName)
                    .ToList();

                // Allowed Words/Sample Texts folders belong wholly to their group: a file the
                // server no longer has really should be removed locally.
                Assert.That(doomed, Is.EquivalentTo(new[] { "words2.txt" }));
            }
        }

        // ------------------------------------------------------------------
        // Batch item 9 (account-switch behavior): ComposeNotAMemberRefusalDetail matrix.
        // Pure function -- no fake server needed.
        // ------------------------------------------------------------------

        [Test]
        public void ComposeNotAMemberRefusalDetail_BothKnown_NamesBothAdminsAndLastKnownUser()
        {
            var detail = CloudTeamCollection.ComposeNotAMemberRefusalDetail(
                new[] { "admin1@example.com", "admin2@example.com" },
                "alice@example.com"
            );

            Assert.That(detail, Does.Contain("admin1@example.com"));
            Assert.That(detail, Does.Contain("admin2@example.com"));
            Assert.That(detail, Does.Contain("alice@example.com"));
        }

        [Test]
        public void ComposeNotAMemberRefusalDetail_OnlyAdminsKnown_NamesOnlyAdmins()
        {
            var detail = CloudTeamCollection.ComposeNotAMemberRefusalDetail(
                new[] { "admin1@example.com" },
                null
            );

            Assert.That(detail, Does.Contain("admin1@example.com"));
        }

        [Test]
        public void ComposeNotAMemberRefusalDetail_OnlyLastKnownUserKnown_NamesThem()
        {
            var detail = CloudTeamCollection.ComposeNotAMemberRefusalDetail(
                null,
                "alice@example.com"
            );

            Assert.That(detail, Does.Contain("alice@example.com"));
        }

        [Test]
        public void ComposeNotAMemberRefusalDetail_NeitherKnown_StillProducesUsableSentence()
        {
            var detail = CloudTeamCollection.ComposeNotAMemberRefusalDetail(null, null);

            Assert.That(detail, Is.Not.Null.And.Not.Empty);
            Assert.That(detail, Does.Contain("administrator"));
        }

        [Test]
        public void ComposeNotAMemberRefusalDetail_EmptyAdministratorsArray_TreatedAsUnknown()
        {
            // A legacy/empty Administrators array (not null, but no entries) should behave the
            // same as "not known" rather than producing an empty "()" list in the message.
            var detail = CloudTeamCollection.ComposeNotAMemberRefusalDetail(
                new string[0],
                "alice@example.com"
            );

            Assert.That(detail, Does.Contain("alice@example.com"));
            Assert.That(detail, Does.Not.Contain("()"));
        }
    }
}
