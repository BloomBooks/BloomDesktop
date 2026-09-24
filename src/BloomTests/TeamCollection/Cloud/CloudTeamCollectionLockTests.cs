using System.IO;
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
    /// Tests for CloudTeamCollection's lock overrides (TryLockInRepo/UnlockInRepo, exercised via
    /// the base class's public AttemptLock/UnlockBook/ForceUnlock), verifying they dispatch to
    /// checkout_book/unlock_book/force_unlock, write-through the result into the cache
    /// immediately (so a second call in the same session sees the update without a network call),
    /// and keep the book folder's `.checkout` record (CONTRACTS.md v1.9) in step.
    /// </summary>
    [TestFixture]
    public class CloudTeamCollectionLockTests
    {
        private const string kCollectionId = "11111111-1111-1111-1111-111111111111";
        private const string kBookId = "book-id-1";
        private const string kGuid = "5d3c2b1a-0f9e-4d8c-b7a6-958473625140";
        private CloudTestHarness _harness;
        private CloudTeamCollection _collection;
        private FakeRestExecutor _executor;
        private string _bookFolderPath;

        [SetUp]
        public void Setup()
        {
            _harness = CloudTestHarness.Create("CloudTeamCollectionLockTests", kCollectionId);
            _collection = _harness.Collection;
            _executor = _harness.Executor;

            // The local book, bound to its repo row by its meta.json instance id (bug #15).
            _bookFolderPath = _harness.CollectionFolder.Combine("My book");
            Directory.CreateDirectory(_bookFolderPath);
            File.WriteAllText(
                Path.Combine(_bookFolderPath, "meta.json"),
                "{\"bookInstanceId\":\"instance-1\"}"
            );

            // Hydrate the cache with one committed, unlocked book so the name/id index knows it.
            _executor.Handler = req =>
            {
                var body = new JObject
                {
                    ["books"] = new JArray(
                        new JObject
                        {
                            ["id"] = kBookId,
                            ["instance_id"] = "instance-1",
                            ["name"] = "My book",
                            ["current_version_id"] = "v1",
                            ["current_version_seq"] = 1,
                            ["current_checksum"] = "cs1",
                            ["locked_by"] = null,
                            ["locked_by_machine"] = null,
                            ["locked_at"] = null,
                            ["deleted_at"] = null,
                        }
                    ),
                    ["groups"] = new JArray(),
                    ["max_event_id"] = 1,
                };
                return FakeResponses.Make(HttpStatusCode.OK, body.ToString());
            };
            _collection.IsBookPresentInRepo("My book"); // forces hydration
        }

        [TearDown]
        public void TearDown()
        {
            _harness.Dispose();
        }

        private static JObject RequestBody(IRestRequest request) =>
            JObject.Parse(
                (string)request.Parameters.First(p => p.Type == ParameterType.RequestBody).Value
            );

        private static JObject GrantedCheckout() =>
            new JObject
            {
                ["success"] = true,
                ["checkoutGuid"] = kGuid,
                ["locked_by"] = "test@somewhere.org",
                ["locked_by_machine"] = TeamCollectionManager.CurrentMachine,
                ["locked_at"] = System.DateTime.UtcNow.ToString("o"),
            };

        [Test]
        public void AttemptLock_ServerGrants_ReturnsTrueUpdatesStatusAndWritesCheckoutRecord()
        {
            Assert.That(
                File.Exists(CloudCheckoutFile.GetPath(_bookFolderPath)),
                Is.False,
                "sanity check: no record before checking out"
            );
            _executor.Handler = req =>
            {
                Assert.That(req.Resource, Is.EqualTo("rest/v1/rpc/checkout_book"));
                return FakeResponses.Make(HttpStatusCode.OK, GrantedCheckout().ToString());
            };

            var result = _collection.AttemptLock("My book");

            Assert.That(result, Is.True);
            Assert.That(_collection.WhoHasBookLocked("My book"), Is.EqualTo("test@somewhere.org"));
            var record = CloudCheckoutFile.Read(_bookFolderPath);
            Assert.That(record.CheckoutGuid, Is.EqualTo(kGuid));
            Assert.That(record.BookId, Is.EqualTo(kBookId));
            Assert.That(record.CollectionId, Is.EqualTo(kCollectionId));
            Assert.That(record.UserEmail, Is.EqualTo("test@somewhere.org"));
            Assert.That(_collection.IsCheckedOutInThisCopy("My book"), Is.True);
        }

        [Test]
        public void AttemptLock_LockedByMeElsewhere_ReturnsFalse_WritesNoRecord()
        {
            // The server says I already hold it -- necessarily in another copy -- and issues no
            // GUID: this copy stays read-only (no "check out here instead").
            _executor.Handler = req =>
                FakeResponses.Make(
                    HttpStatusCode.OK,
                    new JObject { ["success"] = false, ["locked_by_me"] = true }.ToString()
                );

            var result = _collection.AttemptLock("My book");

            Assert.That(result, Is.False);
            Assert.That(File.Exists(CloudCheckoutFile.GetPath(_bookFolderPath)), Is.False);
            Assert.That(_collection.IsCheckedOutInThisCopy("My book"), Is.False);
            Assert.That(_collection.WhoHasBookLocked("My book"), Is.EqualTo("test@somewhere.org"));
        }

        /// <summary>
        /// TryLockInRepo itself correctly reports the server's denial (see
        /// CloudTeamCollectionMemberTests and TryLockInRepo's own logic), and AttemptLock re-reads
        /// the status when it gets that denial, so WhoHasBookLocked reflects the real winner.
        /// </summary>
        [Test]
        public void AttemptLock_ServerDenies_CacheAndWhoHasBookLockedReflectTheActualWinner()
        {
            _executor.Handler = req =>
            {
                var body = new JObject
                {
                    ["success"] = false,
                    ["locked_by"] = "someoneelse@somewhere.org",
                    ["locked_by_machine"] = "THEIR-MACHINE",
                    ["locked_at"] = System.DateTime.UtcNow.ToString("o"),
                };
                return FakeResponses.Make(HttpStatusCode.OK, body.ToString());
            };

            _collection.AttemptLock("My book");

            Assert.That(
                _collection.WhoHasBookLocked("My book"),
                Is.EqualTo("someoneelse@somewhere.org")
            );
            Assert.That(File.Exists(CloudCheckoutFile.GetPath(_bookFolderPath)), Is.False);
        }

        [Test]
        public void UnlockBook_SendsGuid_ClearsLock_AndDeletesCheckoutRecord()
        {
            // First grant a lock so there's something to release.
            _executor.Handler = req =>
                FakeResponses.Make(HttpStatusCode.OK, GrantedCheckout().ToString());
            _collection.AttemptLock("My book");
            Assert.That(
                CloudCheckoutFile.ReadGuid(_bookFolderPath),
                Is.EqualTo(kGuid),
                "sanity check: checked out here"
            );

            JObject unlockBody = null;
            _executor.Handler = req =>
            {
                if (req.Resource == "rest/v1/rpc/unlock_book")
                    unlockBody = RequestBody(req);
                return FakeResponses.Make(HttpStatusCode.OK, "{}");
            };

            _collection.UnlockBook("My book");

            Assert.That(unlockBody, Is.Not.Null, "unlock_book should have been called");
            Assert.That((string)unlockBody["p_checkout_guid"], Is.EqualTo(kGuid));
            Assert.That(_collection.WhoHasBookLocked("My book"), Is.Null.Or.Empty);
            Assert.That(File.Exists(CloudCheckoutFile.GetPath(_bookFolderPath)), Is.False);
        }

        [Test]
        public void ForceUnlock_CallsServerForceUnlock_WithoutAnyGuid()
        {
            // An administrator can always cancel someone's checkout without the secret GUID, and
            // without this copy having a `.checkout` record at all.
            Assert.That(File.Exists(CloudCheckoutFile.GetPath(_bookFolderPath)), Is.False);
            JObject forceUnlockBody = null;
            _executor.Handler = req =>
            {
                if (req.Resource == "rest/v1/rpc/force_unlock")
                    forceUnlockBody = RequestBody(req);
                return FakeResponses.Make(HttpStatusCode.OK, "{}");
            };

            _collection.ForceUnlock("My book");

            Assert.That(forceUnlockBody, Is.Not.Null, "force_unlock should have been called");
            Assert.That(
                forceUnlockBody.Properties().Select(p => p.Name),
                Is.EquivalentTo(new[] { "p_book_id" }),
                "force_unlock takes only the book id"
            );
            Assert.That(_collection.WhoHasBookLocked("My book"), Is.Null.Or.Empty);
        }
    }
}
