using System;
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
    /// Tests for the checkout-takeover half of batch item 9 (account-switch behavior,
    /// Design/CloudTeamCollections/orchestration/DOGFOOD-BATCH-1.md), in terms of the checkout
    /// GUID (CONTRACTS.md v1.9): a book checked out IN THIS COPY of the collection (its folder's
    /// `.checkout` record holds the GUID whose hash the server reports) is editable by whichever
    /// member is signed in, whatever machine the lock was recorded on, and the server lock moves
    /// to the current account (presenting the GUID) the moment that matters. A copy without the
    /// current GUID -- another account's lock or the current user's own -- is never editable and
    /// never attempts a takeover. Uses the same FakeRestExecutor/StubCloudAuthProvider pattern as
    /// CloudTeamCollectionMemberTests/CloudSyncAtStartupTests.
    /// </summary>
    [TestFixture]
    public class CloudAccountSwitchTakeoverTests
    {
        private const string kCollectionId = "22222222-2222-2222-2222-222222222222";
        private const string kOtherMachine = "SomeoneElsesMachine";
        private const string kCurrentUserEmail = "bob@dev.local";
        private const string kGuid = "0b8f5a3e-9c1d-4e2f-8a7b-6c5d4e3f2a1b";
        private const string kBookName = "My Book";
        private const string kBookId = "book-1";

        // TeamCollectionManager.CurrentMachine is Environment.MachineName unless overridden via
        // impersonate.txt (read by a real TeamCollectionManager's constructor, which these tests
        // never construct) -- so "this machine" for test purposes must be whatever that static
        // property actually resolves to right now, not an arbitrary literal.
        private static string ThisMachine => TeamCollectionManager.CurrentMachine;

        private static string GuidHash => CloudCheckoutFile.HashGuid(kGuid);

        private CloudTestHarness _harness;
        private TemporaryFolder _collectionFolder;
        private CloudTeamCollection _collection;
        private FakeRestExecutor _executor;

        [SetUp]
        public void Setup()
        {
            // Signed in as the machine-local current user (kCurrentUserEmail).
            _harness = CloudTestHarness.Create(
                "CloudAccountSwitchTakeoverTests",
                kCollectionId,
                currentUser: kCurrentUserEmail
            );
            _collectionFolder = _harness.CollectionFolder;
            _collection = _harness.Collection;
            _executor = _harness.Executor;
        }

        [TearDown]
        public void TearDown()
        {
            _harness.Dispose();
        }

        private string BookFolderPath => _collectionFolder.Combine(kBookName);

        private void ScriptCollectionState(
            string lockedBy,
            string lockedByMachine,
            string checkoutGuidHash
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
                            ["id"] = kBookId,
                            ["instance_id"] = "instance-" + kBookId,
                            ["name"] = kBookName,
                            ["current_version_id"] = "v1",
                            ["current_version_seq"] = 1,
                            ["current_checksum"] = "checksum-" + kBookId,
                            ["locked_by"] = lockedBy,
                            ["locked_by_machine"] = lockedByMachine,
                            ["checkoutGuidHash"] = checkoutGuidHash,
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

        /// <summary>Creates the local folder for the scripted repo book, carrying the meta.json
        /// instance id ScriptCollectionState gives it (a local folder binds to its repo row by
        /// that id -- bug #15), and optionally this copy's `.checkout` record.</summary>
        private void CreateLocalBookFolder(string checkoutGuid, string recordedEmail = null)
        {
            Directory.CreateDirectory(BookFolderPath);
            File.WriteAllText(
                Path.Combine(BookFolderPath, "meta.json"),
                $"{{\"bookInstanceId\":\"instance-{kBookId}\"}}"
            );
            if (checkoutGuid != null)
                new CloudCheckoutFile
                {
                    CheckoutGuid = checkoutGuid,
                    BookId = kBookId,
                    CollectionId = kCollectionId,
                    UserEmail = recordedEmail ?? "alice@dev.local",
                    CheckedOutAtUtc = DateTime.UtcNow,
                }.Write(BookFolderPath);
        }

        private static JObject RequestBody(IRestRequest request) =>
            JObject.Parse(
                (string)request.Parameters.First(p => p.Type == ParameterType.RequestBody).Value
            );

        // ------------------------------------------------------------------
        // IsEditableHere / NeedCheckoutToEdit
        // ------------------------------------------------------------------

        [Test]
        public void NeedCheckoutToEdit_LockedByOtherAccount_InThisCopy_IsEditable()
        {
            ScriptCollectionState("some-other-user-id", ThisMachine, GuidHash);
            CreateLocalBookFolder(kGuid);

            Assert.That(
                _collection.NeedCheckoutToEdit(BookFolderPath),
                Is.False,
                "a book another account checked out in THIS copy must be editable without an explicit checkout"
            );
        }

        [Test]
        public void NeedCheckoutToEdit_LockedByOtherAccount_InThisCopy_RecordedOnAnotherMachine_IsEditable()
        {
            // The collection folder was copied here from the computer the checkout was made on:
            // the `.checkout` record came with it, and the machine no longer matters.
            ScriptCollectionState("some-other-user-id", kOtherMachine, GuidHash);
            CreateLocalBookFolder(kGuid);

            Assert.That(_collection.NeedCheckoutToEdit(BookFolderPath), Is.False);
        }

        [Test]
        public void NeedCheckoutToEdit_LockedByOtherAccount_NoCheckoutRecordHere_NeedsCheckout()
        {
            // Same machine, but the checkout is in a DIFFERENT copy of the collection (bug #0):
            // this copy has no `.checkout` record, so editing here would risk conflicting changes.
            ScriptCollectionState("some-other-user-id", ThisMachine, GuidHash);
            CreateLocalBookFolder(checkoutGuid: null);

            Assert.That(_collection.NeedCheckoutToEdit(BookFolderPath), Is.True);
        }

        [Test]
        public void NeedCheckoutToEdit_StaleCheckoutRecord_NeedsCheckout()
        {
            // This copy's record is from an earlier checkout; the server has moved on to a new
            // GUID (e.g. the twin of a duplicated collection folder checked in and out again).
            ScriptCollectionState(
                kCurrentUserEmail,
                ThisMachine,
                CloudCheckoutFile.HashGuid(Guid.NewGuid().ToString())
            );
            CreateLocalBookFolder(kGuid);

            Assert.That(_collection.NeedCheckoutToEdit(BookFolderPath), Is.True);
        }

        [Test]
        public void NeedCheckoutToEdit_OwnLock_InThisCopy_RecordedOnAnotherMachine_IsEditable()
        {
            ScriptCollectionState(kCurrentUserEmail, kOtherMachine, GuidHash);
            CreateLocalBookFolder(kGuid, kCurrentUserEmail);

            Assert.That(_collection.NeedCheckoutToEdit(BookFolderPath), Is.False);
            Assert.That(
                _collection.IsCheckedOutHereBy(_collection.GetStatus(kBookName)),
                Is.True,
                "checked out HERE now means in this copy, whatever machine the lock names"
            );
        }

        [Test]
        public void NeedCheckoutToEdit_OwnLock_InAnotherCopyOnThisMachine_NeedsCheckout()
        {
            // John's ruling covers the same user's OTHER copy too: the book is being worked on
            // in the copy that holds the GUID, not this one.
            ScriptCollectionState(kCurrentUserEmail, ThisMachine, GuidHash);
            CreateLocalBookFolder(checkoutGuid: null);

            Assert.That(_collection.NeedCheckoutToEdit(BookFolderPath), Is.True);
            Assert.That(_collection.IsCheckedOutHereBy(_collection.GetStatus(kBookName)), Is.False);
        }

        [Test]
        public void NeedCheckoutToEdit_Unlocked_ReturnsTrue_StillNeedsCheckout()
        {
            ScriptCollectionState(null, null, null);
            CreateLocalBookFolder(checkoutGuid: null);

            Assert.That(_collection.NeedCheckoutToEdit(BookFolderPath), Is.True);
        }

        // ------------------------------------------------------------------
        // OkToCheckIn
        // ------------------------------------------------------------------

        private void WriteMatchingLocalStatus()
        {
            // OkToCheckIn compares repo checksum to LOCAL status checksum; make them match so
            // that check doesn't independently decide these tests.
            var localStatus = _collection.GetStatus(kBookName).WithChecksum("checksum-" + kBookId);
            _collection.WriteLocalStatus(kBookName, localStatus);
        }

        [Test]
        public void OkToCheckIn_LockedByOtherAccount_InThisCopy_ReturnsTrue()
        {
            ScriptCollectionState("some-other-user-id", kOtherMachine, GuidHash);
            CreateLocalBookFolder(kGuid);
            WriteMatchingLocalStatus();

            Assert.That(_collection.OkToCheckIn(kBookName), Is.True);
        }

        [Test]
        public void OkToCheckIn_LockedByOtherAccount_NotInThisCopy_ReturnsFalse()
        {
            ScriptCollectionState("some-other-user-id", ThisMachine, GuidHash);
            CreateLocalBookFolder(checkoutGuid: null);
            WriteMatchingLocalStatus();

            Assert.That(_collection.OkToCheckIn(kBookName), Is.False);
        }

        [Test]
        public void OkToCheckIn_OwnLock_InThisCopy_OnAnotherMachine_ReturnsTrue()
        {
            ScriptCollectionState(kCurrentUserEmail, kOtherMachine, GuidHash);
            CreateLocalBookFolder(kGuid, kCurrentUserEmail);
            WriteMatchingLocalStatus();

            Assert.That(_collection.OkToCheckIn(kBookName), Is.True);
        }

        [Test]
        public void OkToCheckIn_OwnLock_InAnotherCopy_ReturnsFalse()
        {
            ScriptCollectionState(kCurrentUserEmail, ThisMachine, GuidHash);
            CreateLocalBookFolder(checkoutGuid: null);
            WriteMatchingLocalStatus();

            Assert.That(_collection.OkToCheckIn(kBookName), Is.False);
        }

        // ------------------------------------------------------------------
        // TryTakeOverLock / the RPC wiring
        // ------------------------------------------------------------------

        [Test]
        public void TryTakeOverLock_ServerAccepts_SendsGuid_UpdatesStatus_AndRestampsCheckoutRecord()
        {
            ScriptCollectionState("some-other-user-id", ThisMachine, GuidHash);
            CreateLocalBookFolder(kGuid, "alice@dev.local");
            Assert.That(_collection.IsBookPresentInRepo(kBookName), Is.True, "hydrate");

            JObject sentBody = null;
            _executor.Handler = req =>
            {
                Assert.That(req.Resource, Is.EqualTo("rest/v1/rpc/checkout_book_takeover"));
                sentBody = RequestBody(req);
                var body = new JObject
                {
                    ["success"] = true,
                    ["locked_by"] = kCurrentUserEmail,
                    ["locked_by_machine"] = ThisMachine,
                    ["locked_at"] = DateTime.UtcNow.ToString("o"),
                };
                return FakeResponses.Make(HttpStatusCode.OK, body.ToString());
            };

            var result = _collection.TryTakeOverLock(kBookName);

            Assert.That(result, Is.True);
            Assert.That((string)sentBody["p_checkout_guid"], Is.EqualTo(kGuid));
            Assert.That((string)sentBody["p_book_id"], Is.EqualTo(kBookId));
            Assert.That(_collection.GetStatus(kBookName).lockedBy, Is.EqualTo(kCurrentUserEmail));
            var record = CloudCheckoutFile.Read(BookFolderPath);
            Assert.That(record.CheckoutGuid, Is.EqualTo(kGuid), "a takeover keeps the GUID");
            Assert.That(record.UserEmail, Is.EqualTo(kCurrentUserEmail));
            Assert.That(
                _collection.NeedCheckoutToEdit(BookFolderPath),
                Is.False,
                "still checked out in this copy, now to the current account"
            );
        }

        [Test]
        public void TryTakeOverLock_ServerRefuses_ReturnsFalse_StatusUnchanged()
        {
            ScriptCollectionState("some-other-user-id", kOtherMachine, GuidHash);
            CreateLocalBookFolder(kGuid, "alice@dev.local");
            _collection.IsBookPresentInRepo(kBookName);

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

            var result = _collection.TryTakeOverLock(kBookName);

            Assert.That(result, Is.False);
            Assert.That(
                _collection.GetStatus(kBookName).lockedBy,
                Is.EqualTo("some-other-user-id")
            );
            Assert.That(
                CloudCheckoutFile.Read(BookFolderPath).UserEmail,
                Is.EqualTo("alice@dev.local"),
                "a refused takeover must not re-stamp the record"
            );
        }

        [Test]
        public void TryTakeOverLock_NoCheckoutRecord_DoesNotCallServer()
        {
            ScriptCollectionState("some-other-user-id", ThisMachine, GuidHash);
            CreateLocalBookFolder(checkoutGuid: null);
            _collection.IsBookPresentInRepo(kBookName);
            _executor.Handler = req =>
            {
                Assert.Fail($"No GUID, so no takeover RPC; got {req.Resource}");
                return null;
            };

            Assert.That(_collection.TryTakeOverLock(kBookName), Is.False);
        }

        // ------------------------------------------------------------------
        // AttemptLock: an explicit "check out" click on a takeover-eligible book performs the
        // handover instead of silently failing.
        // ------------------------------------------------------------------

        [Test]
        public void AttemptLock_LockedByOtherAccount_InThisCopy_TakesOverAndSucceeds()
        {
            ScriptCollectionState("some-other-user-id", ThisMachine, GuidHash);
            CreateLocalBookFolder(kGuid);
            _collection.IsBookPresentInRepo(kBookName);

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

            var success = _collection.AttemptLock(kBookName);

            Assert.That(success, Is.True);
            Assert.That(_collection.GetStatus(kBookName).lockedBy, Is.EqualTo(kCurrentUserEmail));
        }

        [Test]
        public void AttemptLock_LockedByOtherAccount_NotInThisCopy_DoesNotAttemptTakeover()
        {
            // Bug #0 (e2e-4's exact scenario): an explicit checkout attempt on a book locked in
            // a DIFFERENT copy on this same machine must not fire the takeover RPC at all.
            ScriptCollectionState("some-other-user-id", ThisMachine, GuidHash);
            CreateLocalBookFolder(checkoutGuid: null);
            _collection.IsBookPresentInRepo(kBookName);

            _executor.Handler = req =>
            {
                Assert.Fail(
                    $"Should not have called any RPC for a lock held in another copy; got {req.Resource}"
                );
                return null;
            };

            Assert.That(_collection.AttemptLock(kBookName), Is.False);
        }

        [Test]
        public void AttemptLock_OwnLockInAnotherCopy_DoesNotReclaim()
        {
            // No "check out here instead": the copy without the GUID stays read-only.
            ScriptCollectionState(kCurrentUserEmail, ThisMachine, GuidHash);
            CreateLocalBookFolder(checkoutGuid: null);
            _collection.IsBookPresentInRepo(kBookName);

            _executor.Handler = req =>
            {
                Assert.Fail($"Must not try to move my own checkout here; got {req.Resource}");
                return null;
            };

            Assert.That(_collection.AttemptLock(kBookName), Is.False);
            Assert.That(File.Exists(CloudCheckoutFile.GetPath(BookFolderPath)), Is.False);
        }

        [Test]
        public void AttemptLock_Unlocked_ChecksOut_WritesCheckoutRecord()
        {
            ScriptCollectionState(null, null, null);
            CreateLocalBookFolder(checkoutGuid: null);
            _collection.IsBookPresentInRepo(kBookName);

            string sentGuid = null;
            _executor.Handler = req =>
            {
                Assert.That(req.Resource, Is.EqualTo("rest/v1/rpc/checkout_book"));
                sentGuid = (string)
                    JObject.Parse(
                        (string)req.Parameters.First(p => p.Type == ParameterType.RequestBody).Value
                    )["p_checkout_guid"];
                var body = new JObject
                {
                    ["success"] = true,
                    ["locked_by"] = kCurrentUserEmail,
                    ["locked_by_machine"] = ThisMachine,
                    ["locked_at"] = DateTime.UtcNow.ToString("o"),
                };
                return FakeResponses.Make(HttpStatusCode.OK, body.ToString());
            };

            var success = _collection.AttemptLock(kBookName);

            Assert.That(success, Is.True);
            Assert.That(sentGuid, Is.Not.Null);
            Assert.That(CloudCheckoutFile.ReadGuid(BookFolderPath), Is.EqualTo(sentGuid));
            Assert.That(
                _collection.NeedCheckoutToEdit(BookFolderPath),
                Is.False,
                "the write-through hash plus the new record make it editable at once"
            );
        }
    }
}
