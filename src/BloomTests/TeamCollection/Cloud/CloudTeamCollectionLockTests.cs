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
    /// Tests for CloudTeamCollection's lock overrides (TryLockInRepo/UnlockInRepo, exercised via
    /// the base class's public AttemptLock/UnlockBook/ForceUnlock), verifying they dispatch to
    /// checkout_book/unlock_book/force_unlock and write-through the result into the cache
    /// immediately (so a second call in the same session sees the update without a network call).
    /// </summary>
    [TestFixture]
    public class CloudTeamCollectionLockTests
    {
        private const string kCollectionId = "11111111-1111-1111-1111-111111111111";
        private const string kBookId = "book-id-1";
        private CloudTestHarness _harness;
        private CloudTeamCollection _collection;
        private FakeRestExecutor _executor;

        [SetUp]
        public void Setup()
        {
            _harness = CloudTestHarness.Create("CloudTeamCollectionLockTests", kCollectionId);
            _collection = _harness.Collection;
            _executor = _harness.Executor;

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

        [Test]
        public void AttemptLock_ServerGrants_ReturnsTrueAndUpdatesStatus()
        {
            _executor.Handler = req =>
            {
                Assert.That(req.Resource, Is.EqualTo("rest/v1/rpc/checkout_book"));
                var body = new JObject
                {
                    ["success"] = true,
                    ["locked_by"] = "test@somewhere.org",
                    ["locked_by_machine"] = TeamCollectionManager.CurrentMachine,
                    ["locked_at"] = System.DateTime.UtcNow.ToString("o"),
                };
                return FakeResponses.Make(HttpStatusCode.OK, body.ToString());
            };

            var result = _collection.AttemptLock("My book");

            Assert.That(result, Is.True);
            Assert.That(_collection.WhoHasBookLocked("My book"), Is.EqualTo("test@somewhere.org"));
        }

        /// <summary>
        /// TryLockInRepo itself correctly reports the server's denial (see
        /// CloudTeamCollectionMemberTests and TryLockInRepo's own logic), but note what this test
        /// does NOT assert: TeamCollection.AttemptLock (base class, unchanged) discards
        /// TryLockInRepo's bool return value and returns based on the LOCAL status object it
        /// optimistically set to "locked by me" before calling TryLockInRepo -- it never re-reads
        /// status afterward. So AttemptLock's own return value is only reliable when the race was
        /// already visible in cache/repo BEFORE this call (the normal case for FolderTeamCollection,
        /// whose TryLockInRepo can't fail); for a genuinely-raced cloud lock like this one,
        /// AttemptLock can return true even though the server denied it. The base class's own doc
        /// comment on TryLockInRepo anticipates this ("the caller should re-read status to find out
        /// who won") -- WhoHasBookLocked below is that re-read, and IS reliable. Flagged in the task
        /// 05 final report as a base-class behavior worth revisiting (AttemptLock could use
        /// TryLockInRepo's return value instead of discarding it), not fixed here since
        /// TeamCollection.cs is read-only for this task.
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
        }

        [Test]
        public void UnlockBook_CallsUnlockRpcAndClearsLock()
        {
            // First grant a lock so there's something to release.
            _executor.Handler = req =>
                FakeResponses.Make(
                    HttpStatusCode.OK,
                    new JObject
                    {
                        ["success"] = true,
                        ["locked_by"] = "test@somewhere.org",
                        ["locked_by_machine"] = TeamCollectionManager.CurrentMachine,
                        ["locked_at"] = System.DateTime.UtcNow.ToString("o"),
                    }.ToString()
                );
            _collection.AttemptLock("My book");

            var unlockCalled = false;
            _executor.Handler = req =>
            {
                if (req.Resource == "rest/v1/rpc/unlock_book")
                    unlockCalled = true;
                return FakeResponses.Make(HttpStatusCode.OK, "{}");
            };

            _collection.UnlockBook("My book");

            Assert.That(unlockCalled, Is.True);
            Assert.That(_collection.WhoHasBookLocked("My book"), Is.Null.Or.Empty);
        }

        [Test]
        public void ForceUnlock_CallsServerForceUnlock()
        {
            var forceUnlockCalled = false;
            _executor.Handler = req =>
            {
                if (req.Resource == "rest/v1/rpc/force_unlock")
                    forceUnlockCalled = true;
                return FakeResponses.Make(HttpStatusCode.OK, "{}");
            };

            _collection.ForceUnlock("My book");

            Assert.That(forceUnlockCalled, Is.True);
            Assert.That(_collection.WhoHasBookLocked("My book"), Is.Null.Or.Empty);
        }
    }
}
