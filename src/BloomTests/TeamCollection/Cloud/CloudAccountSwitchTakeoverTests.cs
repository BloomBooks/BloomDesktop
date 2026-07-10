using System;
using System.Linq;
using System.Net;
using Bloom.TeamCollection;
using Bloom.TeamCollection.Cloud;
using BloomTemp;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace BloomTests.TeamCollection.Cloud
{
    /// <summary>
    /// Tests for the checkout-takeover half of batch item 9 (account-switch behavior,
    /// Design/CloudTeamCollections/orchestration/DOGFOOD-BATCH-1.md): once a signed-in account
    /// is confirmed as a Team Collection member (see CloudTeamCollectionMemberTests for the
    /// refusal-vs-member CheckConnection matrix), a book left checked out HERE (this machine) by
    /// a DIFFERENT member must be editable, and the server lock must atomically move to the
    /// current user the moment that matters (check-in). Uses the same FakeRestExecutor/
    /// StubCloudAuthProvider pattern as CloudTeamCollectionMemberTests/CloudSyncAtStartupTests.
    /// </summary>
    [TestFixture]
    public class CloudAccountSwitchTakeoverTests
    {
        private const string kCollectionId = "22222222-2222-2222-2222-222222222222";
        private const string kOtherMachine = "SomeoneElsesMachine";
        private const string kCurrentUserEmail = "bob@dev.local";

        // TeamCollectionManager.CurrentMachine is Environment.MachineName unless overridden via
        // impersonate.txt (read by a real TeamCollectionManager's constructor, which these tests
        // never construct) -- so "this machine" for test purposes must be whatever that static
        // property actually resolves to right now, not an arbitrary literal.
        private static string ThisMachine => TeamCollectionManager.CurrentMachine;

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
            _collectionFolder = new TemporaryFolder("CloudAccountSwitchTakeoverTests");
            _mockTcManager = new Mock<ITeamCollectionManager>();
            // "CurrentMachine" (TeamCollectionManager.CurrentMachine) defaults to
            // Environment.MachineName; override it via impersonate.txt-free static test seam so
            // the test is deterministic regardless of the actual machine it runs on.
            TeamCollectionManager.ForceCurrentUserForTests(kCurrentUserEmail);

            _auth = new CloudAuth(new StubCloudAuthProvider(), new InMemoryCloudTokenStore());
            _auth.SignIn(kCurrentUserEmail, "irrelevant");
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

        private void ScriptCollectionState(
            string bookId,
            string bookName,
            string lockedBy,
            string lockedByMachine
        )
        {
            _executor.Handler = req =>
            {
                Assert.That(req.Resource, Is.EqualTo("rest/v1/rpc/get_collection_state"));
                var body = new JObject
                {
                    ["books"] = new JArray(
                        new JObject
                        {
                            ["id"] = bookId,
                            ["instance_id"] = "instance-" + bookId,
                            ["name"] = bookName,
                            ["current_version_id"] = "v1",
                            ["current_version_seq"] = 1,
                            ["current_checksum"] = "checksum-" + bookId,
                            ["locked_by"] = lockedBy,
                            ["locked_by_machine"] = lockedByMachine,
                            ["locked_at"] =
                                lockedBy == null ? null : (JToken)DateTime.UtcNow.ToString("o"),
                            ["deleted_at"] = null,
                        }
                    ),
                    ["groups"] = new JArray(),
                    ["max_event_id"] = 1,
                };
                return FakeResponses.Make(HttpStatusCode.OK, body.ToString());
            };
        }

        // ------------------------------------------------------------------
        // IsEditableHere / NeedCheckoutToEdit
        // ------------------------------------------------------------------

        [Test]
        public void NeedCheckoutToEdit_LockedByOtherAccount_SameMachine_ReturnsFalse_IsEditable()
        {
            ScriptCollectionState("book-1", "My Book", "some-other-user-id", ThisMachine);

            var bookFolderPath = _collectionFolder.Combine("My Book");
            Assert.That(
                _collection.NeedCheckoutToEdit(bookFolderPath),
                Is.False,
                "a book locked to a different account on THIS machine must be editable without an explicit checkout"
            );
        }

        [Test]
        public void NeedCheckoutToEdit_LockedByOtherAccount_DifferentMachine_ReturnsTrue()
        {
            ScriptCollectionState("book-1", "My Book", "some-other-user-id", kOtherMachine);

            var bookFolderPath = _collectionFolder.Combine("My Book");
            Assert.That(
                _collection.NeedCheckoutToEdit(bookFolderPath),
                Is.True,
                "a lock held on a DIFFERENT machine must remain a genuine conflict, not editable"
            );
        }

        [Test]
        public void NeedCheckoutToEdit_Unlocked_ReturnsTrue_StillNeedsCheckout()
        {
            ScriptCollectionState("book-1", "My Book", null, null);

            var bookFolderPath = _collectionFolder.Combine("My Book");
            Assert.That(_collection.NeedCheckoutToEdit(bookFolderPath), Is.True);
        }

        // ------------------------------------------------------------------
        // OkToCheckIn: same-machine takeover is allowed; cross-machine is not
        // ------------------------------------------------------------------

        [Test]
        public void OkToCheckIn_LockedByOtherAccount_SameMachine_ReturnsTrue()
        {
            ScriptCollectionState("book-1", "My Book", "some-other-user-id", ThisMachine);
            // OkToCheckIn compares repo checksum to LOCAL status checksum; make them match so
            // that check doesn't independently fail this test. (Local status's own lockedBy is
            // irrelevant to OkToCheckIn -- it only reads its checksum -- so it's left at
            // whatever GetStatus/WithChecksum produces, matching the DifferentMachine test
            // below.) WriteLocalStatus needs the book's own folder to already exist.
            System.IO.Directory.CreateDirectory(_collectionFolder.Combine("My Book"));
            var localStatus = _collection.GetStatus("My Book").WithChecksum("checksum-book-1");
            _collection.WriteLocalStatus("My Book", localStatus);

            Assert.That(_collection.OkToCheckIn("My Book"), Is.True);
        }

        [Test]
        public void OkToCheckIn_LockedByOtherAccount_DifferentMachine_ReturnsFalse()
        {
            ScriptCollectionState("book-1", "My Book", "some-other-user-id", kOtherMachine);
            System.IO.Directory.CreateDirectory(_collectionFolder.Combine("My Book"));
            var localStatus = _collection.GetStatus("My Book").WithChecksum("checksum-book-1");
            _collection.WriteLocalStatus("My Book", localStatus);

            Assert.That(_collection.OkToCheckIn("My Book"), Is.False);
        }

        // ------------------------------------------------------------------
        // TryTakeOverLock / the new RPC wiring
        // ------------------------------------------------------------------

        [Test]
        public void TryTakeOverLock_ServerAccepts_UpdatesStatusToCurrentUser()
        {
            ScriptCollectionState("book-1", "My Book", "some-other-user-id", ThisMachine);
            // Force hydration/index so TryGetBookId can resolve "My Book" -> "book-1".
            _collection.IsBookPresentInRepo("My Book");

            _executor.Handler = req =>
            {
                Assert.That(req.Resource, Is.EqualTo("rest/v1/rpc/checkout_book_takeover"));
                var body = new JObject
                {
                    ["success"] = true,
                    ["locked_by"] = kCurrentUserEmail,
                    ["locked_by_machine"] = ThisMachine,
                    ["locked_at"] = DateTime.UtcNow.ToString("o"),
                };
                return FakeResponses.Make(HttpStatusCode.OK, body.ToString());
            };

            var result = _collection.TryTakeOverLock("My Book");

            Assert.That(result, Is.True);
            Assert.That(_collection.GetStatus("My Book").lockedBy, Is.EqualTo(kCurrentUserEmail));
        }

        [Test]
        public void TryTakeOverLock_ServerRefuses_ReturnsFalse_StatusUnchanged()
        {
            ScriptCollectionState("book-1", "My Book", "some-other-user-id", kOtherMachine);
            _collection.IsBookPresentInRepo("My Book");

            _executor.Handler = req =>
            {
                Assert.That(req.Resource, Is.EqualTo("rest/v1/rpc/checkout_book_takeover"));
                var body = new JObject
                {
                    ["success"] = false,
                    ["locked_by"] = "some-other-user-id",
                    ["locked_by_machine"] = kOtherMachine,
                    ["locked_at"] = DateTime.UtcNow.ToString("o"),
                };
                return FakeResponses.Make(HttpStatusCode.OK, body.ToString());
            };

            var result = _collection.TryTakeOverLock("My Book");

            Assert.That(result, Is.False);
            Assert.That(
                _collection.GetStatus("My Book").lockedBy,
                Is.EqualTo("some-other-user-id")
            );
        }

        // ------------------------------------------------------------------
        // AttemptLock: an explicit "check out" click on a takeover-eligible book performs the
        // handover instead of silently failing.
        // ------------------------------------------------------------------

        [Test]
        public void AttemptLock_LockedByOtherAccount_SameMachine_TakesOverAndSucceeds()
        {
            ScriptCollectionState("book-1", "My Book", "some-other-user-id", ThisMachine);
            _collection.IsBookPresentInRepo("My Book");

            _executor.Handler = req =>
            {
                if (req.Resource == "rest/v1/rpc/checkout_book_takeover")
                {
                    var body = new JObject
                    {
                        ["success"] = true,
                        ["locked_by"] = kCurrentUserEmail,
                        ["locked_by_machine"] = ThisMachine,
                        ["locked_at"] = DateTime.UtcNow.ToString("o"),
                    };
                    return FakeResponses.Make(HttpStatusCode.OK, body.ToString());
                }
                Assert.Fail($"Unexpected request: {req.Resource}");
                return null;
            };

            var success = _collection.AttemptLock("My Book");

            Assert.That(success, Is.True);
            Assert.That(_collection.GetStatus("My Book").lockedBy, Is.EqualTo(kCurrentUserEmail));
        }

        [Test]
        public void AttemptLock_LockedByOtherAccount_DifferentMachine_DoesNotAttemptTakeover()
        {
            ScriptCollectionState("book-1", "My Book", "some-other-user-id", kOtherMachine);
            _collection.IsBookPresentInRepo("My Book");

            _executor.Handler = req =>
            {
                Assert.Fail(
                    $"Should not have called any RPC for a cross-machine lock; got {req.Resource}"
                );
                return null;
            };

            var success = _collection.AttemptLock("My Book");

            Assert.That(success, Is.False);
        }

        [Test]
        public void AttemptLock_AlreadyLockedByMe_DoesNotAttemptTakeover()
        {
            ScriptCollectionState("book-1", "My Book", null, null);
            _collection.IsBookPresentInRepo("My Book");

            _executor.Handler = req =>
            {
                Assert.That(req.Resource, Is.EqualTo("rest/v1/rpc/checkout_book"));
                var body = new JObject
                {
                    ["success"] = true,
                    ["locked_by"] = kCurrentUserEmail,
                    ["locked_by_machine"] = ThisMachine,
                    ["locked_at"] = DateTime.UtcNow.ToString("o"),
                };
                return FakeResponses.Make(HttpStatusCode.OK, body.ToString());
            };

            var success = _collection.AttemptLock("My Book");

            Assert.That(success, Is.True);
        }
    }
}
