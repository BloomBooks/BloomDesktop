using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Bloom.Book;
using Bloom.TeamCollection;
using Bloom.TeamCollection.Cloud;
using BloomTemp;
using BloomTests.DataBuilders;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RestSharp;
using SIL.IO;

namespace BloomTests.TeamCollection.Cloud
{
    /// <summary>
    /// Scenario tests for the checkout GUID (CONTRACTS.md v1.9): a checkout belongs to the copy of
    /// the collection whose book folder holds the current GUID in its `.checkout` record. Covers
    /// a moved/renamed collection folder (keeps its checkout and checks in), the duplicated-folder
    /// case (the copy whose twin checked in is cancelled -- Lost and Found only if it had edits),
    /// obsolescence through another account's checkout or an administrator's force-unlock
    /// (detected at open and by polling), polling raising a book-state change when only the hash
    /// changes, and check-in refusals from checkin-finish (not retried; the caller recovers).
    /// v1.10: a check-in never writes a record; a checkin-finish whose answer is lost is retried
    /// and, if still unanswered, leaves the book read-only until a poll settles it either way
    /// (existing book or first check-in); and the crash-right-after-commit cases reconcile at
    /// open without a spurious Lost and Found copy.
    /// Drives a real CloudTeamCollection against a scripted server + S3, like
    /// CloudSyncAtStartupTests.
    /// </summary>
    [TestFixture]
    public class CloudCheckoutGuidTests
    {
        private const string kCollectionId = "33333333-3333-3333-3333-333333333333";
        private const string kBookId = "book-id-1";
        private const string kBookTitle = "Guid book";
        private const string kCurrentUser = "test@somewhere.org";

        // StubCloudAuthProvider signs every session in as this user id.
        private const string kCurrentUserId = "user-1";
        private const string kGuid = "7a6b5c4d-3e2f-4a1b-9c8d-7e6f5a4b3c2d";
        private const string kLocalContent = "<html><body>content as last synced</body></html>";
        private const string kLocalEdit = "<html><body>edited here, not checked in</body></html>";
        private const string kRemoteContent =
            "<html><body>the team's current version</body></html>";

        private TemporaryFolder _parentFolder;
        private string _collectionFolderPath;
        private string _bookFolderPath;
        private byte[] _metaBytes;
        private string _instanceId;
        private FakeRestExecutor _executor;
        private CloudAuth _auth;
        private CloudTeamCollection _collection;

        // The scripted server's current row for the one book, and what it was asked.
        private JObject _bookRow;
        private long _maxEventId = 5;
        private readonly List<JObject> _checkinStartBodies = new List<JObject>();
        private readonly List<JObject> _checkinFinishBodies = new List<JObject>();
        private Func<IRestResponse> _checkinFinishResponse;
        private bool _checkinAbortCalled;

        [SetUp]
        public void SetUp()
        {
            _parentFolder = new TemporaryFolder("CloudCheckoutGuidTests");
            _collectionFolderPath = _parentFolder.Combine("My Collection");
            Directory.CreateDirectory(_collectionFolderPath);
            TeamCollectionManager.ForceCurrentUserForTests(kCurrentUser);
            _checkinStartBodies.Clear();
            _checkinFinishBodies.Clear();
            _checkinAbortCalled = false;
            _checkinFinishResponse = () =>
                FakeResponses.Make(
                    HttpStatusCode.OK,
                    new JObject { ["versionId"] = "v2", ["seq"] = 2 }.ToString()
                );
            BuildLocalBook();
        }

        [TearDown]
        public void TearDown()
        {
            _collection?.StopMonitoring();
            _parentFolder.Dispose();
            TeamCollectionManager.ForceCurrentUserForTests(null);
        }

        // ------------------------------------------------------------------
        // Fixture helpers
        // ------------------------------------------------------------------

        private void BuildLocalBook()
        {
            _bookFolderPath = new BookFolderBuilder()
                .WithRootFolder(_collectionFolderPath)
                .WithTitle(kBookTitle)
                .WithHtm(kLocalContent)
                .Build()
                .BuiltBookFolderPath;
            _metaBytes = RobustFile.ReadAllBytes(Path.Combine(_bookFolderPath, "meta.json"));
            _instanceId = BookMetaData.FromFolder(_bookFolderPath).Id;
        }

        private string HtmPath => Path.Combine(_bookFolderPath, kBookTitle + ".htm");

        private static string Sha256Hex(byte[] bytes) =>
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        /// <summary>The server row for the book: at version <paramref name="seq"/>, locked by
        /// <paramref name="lockedBy"/> (null = unlocked) under <paramref name="guidHash"/>.</summary>
        private JObject MakeRow(long seq, string checksum, string lockedBy, string guidHash) =>
            new JObject
            {
                ["id"] = kBookId,
                ["instance_id"] = _instanceId,
                ["name"] = kBookTitle,
                ["current_version_id"] = "v" + seq,
                ["current_version_seq"] = seq,
                ["current_checksum"] = checksum,
                ["locked_by"] = lockedBy,
                ["locked_by_machine"] = lockedBy == null ? null : "TheMachineItWasCheckedOutOn",
                ["locked_at"] = lockedBy == null ? null : "2026-09-20T00:00:00Z",
                ["checkoutGuidHash"] = guidHash,
                ["deleted_at"] = null,
            };

        /// <summary>The server's book rows: none while <see cref="_bookRow"/> is null (the
        /// server has never committed the book).</summary>
        private JArray BookRows() => _bookRow == null ? new JArray() : new JArray(_bookRow);

        private static JObject S3Block() =>
            new JObject
            {
                ["bucket"] = "test-bucket",
                ["region"] = "us-east-1",
                ["prefix"] = "tc/x/",
                ["credentials"] = new JObject
                {
                    ["accessKeyId"] = "a",
                    ["secretAccessKey"] = "b",
                    ["sessionToken"] = "c",
                },
            };

        private IRestResponse HandleServerRequest(IRestRequest req)
        {
            JObject Body() =>
                JObject.Parse(
                    (string)req.Parameters.First(p => p.Type == ParameterType.RequestBody).Value
                );
            switch (req.Resource)
            {
                case "rest/v1/rpc/get_collection_state":
                    return FakeResponses.Make(
                        HttpStatusCode.OK,
                        new JObject
                        {
                            ["books"] = BookRows(),
                            ["groups"] = new JArray(),
                            ["max_event_id"] = _maxEventId,
                        }.ToString()
                    );
                case "rest/v1/rpc/get_changes":
                    return FakeResponses.Make(
                        HttpStatusCode.OK,
                        new JObject
                        {
                            ["events"] = new JArray(),
                            ["books"] = BookRows(),
                            ["max_event_id"] = _maxEventId,
                        }.ToString()
                    );
                case "rest/v1/rpc/get_book_manifest":
                    return FakeResponses.Make(
                        HttpStatusCode.OK,
                        new JObject
                        {
                            ["bookId"] = kBookId,
                            ["versionId"] = _bookRow["current_version_id"],
                            ["seq"] = _bookRow["current_version_seq"],
                            ["files"] = new JArray(
                                new JObject
                                {
                                    ["path"] = "meta.json",
                                    ["sha256"] = Sha256Hex(_metaBytes),
                                    ["size"] = _metaBytes.Length,
                                    ["s3VersionId"] = "sv-meta",
                                },
                                new JObject
                                {
                                    ["path"] = kBookTitle + ".htm",
                                    ["sha256"] = Sha256Hex(Encoding.UTF8.GetBytes(kRemoteContent)),
                                    ["size"] = Encoding.UTF8.GetByteCount(kRemoteContent),
                                    ["s3VersionId"] = "sv-htm",
                                }
                            ),
                        }.ToString()
                    );
                case "functions/v1/download-start":
                    return FakeResponses.Make(
                        HttpStatusCode.OK,
                        new JObject { ["s3"] = S3Block() }.ToString()
                    );
                case "functions/v1/checkin-start":
                    _checkinStartBodies.Add(Body());
                    return FakeResponses.Make(
                        HttpStatusCode.OK,
                        new JObject
                        {
                            ["transactionId"] = "tx-1",
                            ["changedPaths"] = new JArray(kBookTitle + ".htm"),
                            ["s3"] = S3Block(),
                        }.ToString()
                    );
                case "functions/v1/checkin-finish":
                    _checkinFinishBodies.Add(Body());
                    return _checkinFinishResponse();
                case "functions/v1/checkin-abort":
                    _checkinAbortCalled = true;
                    return FakeResponses.Make(HttpStatusCode.OK, "{}");
                default:
                    // log_event, my_collections, etc.
                    return FakeResponses.Make(HttpStatusCode.OK, "{}");
            }
        }

        private Mock<IAmazonS3> MakeS3()
        {
            var mock = new Mock<IAmazonS3>();
            mock.Setup(x =>
                    x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>())
                )
                .Returns<GetObjectRequest, CancellationToken>(
                    (req, ct) =>
                        Task.FromResult(
                            new GetObjectResponse
                            {
                                ResponseStream = new MemoryStream(
                                    req.Key.EndsWith("meta.json")
                                        ? _metaBytes
                                        : Encoding.UTF8.GetBytes(kRemoteContent)
                                ),
                                HttpStatusCode = HttpStatusCode.OK,
                                VersionId = req.VersionId,
                            }
                        )
                );
            mock.Setup(x =>
                    x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>())
                )
                .ReturnsAsync(
                    new PutObjectResponse
                    {
                        HttpStatusCode = HttpStatusCode.OK,
                        VersionId = "sv-new",
                    }
                );
            return mock;
        }

        /// <summary>Opens the collection at the current <see cref="_collectionFolderPath"/>.</summary>
        private CloudTeamCollection OpenCollection()
        {
            var environment = new CloudEnvironment(name =>
                name == "BLOOM_CLOUDTC_ANON_KEY" ? "test-anon-key" : null
            );
            _auth = new CloudAuth(new StubCloudAuthProvider(), new InMemoryCloudTokenStore());
            _auth.SignIn(kCurrentUser, "irrelevant");
            var client = new CloudCollectionClient(environment, _auth);
            _executor = new FakeRestExecutor { Handler = HandleServerRequest };
            client.SetRestClientForTests(_executor);
            var s3 = MakeS3();
            _collection = new CloudTeamCollection(
                new Mock<ITeamCollectionManager>().Object,
                _collectionFolderPath,
                kCollectionId,
                environment: environment,
                auth: _auth,
                client: client,
                transfer: new CloudBookTransfer(_ => s3.Object)
            );
            _collection.TestOnly_MakeAutoApplyQueueSynchronous();
            return _collection;
        }

        private void WriteCheckoutRecord(string guid) =>
            new CloudCheckoutFile
            {
                CheckoutGuid = guid,
                BookId = kBookId,
                CollectionId = kCollectionId,
                UserEmail = kCurrentUser,
                CheckedOutAtUtc = DateTime.UtcNow,
            }.Write(_bookFolderPath);

        /// <summary>Records "last synced at the current local content" in the local status file,
        /// the reference point for "changed since the last sync".</summary>
        private string RecordLastSync(CloudTeamCollection collection)
        {
            var checksum = Bloom.TeamCollection.TeamCollection.MakeChecksum(_bookFolderPath);
            collection.WriteLocalStatus(kBookTitle, new BookStatus().WithChecksum(checksum));
            return checksum;
        }

        private string CheckoutRecordPath => CloudCheckoutFile.GetPath(_bookFolderPath);

        private string LostAndFoundFolder => Path.Combine(_collectionFolderPath, "Lost and Found");

        private string[] LostAndFoundFiles() =>
            Directory.Exists(LostAndFoundFolder)
                ? Directory.GetFiles(LostAndFoundFolder, "*.bloomSource")
                : new string[0];

        private JObject LastLogEventBody()
        {
            var request = _executor.RequestsSeen.LastOrDefault(r =>
                r.Resource == "rest/v1/rpc/log_event"
            );
            return request == null
                ? null
                : JObject.Parse(
                    (string)request.Parameters.First(p => p.Type == ParameterType.RequestBody).Value
                );
        }

        private bool HasWorkPreservedMessage() =>
            _collection.MessageLog.Messages.Any(m =>
                m.L10NId == "TeamCollection.Cloud.WorkPreservedLocally"
            );

        // ------------------------------------------------------------------
        // A moved/renamed collection folder keeps its checkout
        // ------------------------------------------------------------------

        [Test]
        public void MovedAndRenamedCollectionFolder_WithCheckoutRecord_StaysEditable_AndChecksIn()
        {
            var lastSyncChecksum = Bloom.TeamCollection.TeamCollection.MakeChecksum(
                _bookFolderPath
            );
            _bookRow = MakeRow(
                1,
                lastSyncChecksum,
                kCurrentUserId,
                CloudCheckoutFile.HashGuid(kGuid)
            );
            WriteCheckoutRecord(kGuid);
            RobustFile.WriteAllText(HtmPath, kLocalEdit);

            // Move and rename the whole collection folder (the record travels with the book).
            var movedTo = _parentFolder.Combine("Somewhere else", "Renamed Collection");
            Directory.CreateDirectory(Path.GetDirectoryName(movedTo));
            Directory.Move(_collectionFolderPath, movedTo);
            _collectionFolderPath = movedTo;
            _bookFolderPath = Path.Combine(movedTo, kBookTitle);
            var collection = OpenCollection();
            collection.WriteLocalStatus(
                kBookTitle,
                new BookStatus().WithChecksum(lastSyncChecksum)
            );

            collection.SyncAtStartup(new ProgressSpy(), firstTimeJoin: false);

            Assert.That(
                File.Exists(CheckoutRecordPath),
                Is.True,
                "the checkout must survive the move"
            );
            Assert.That(LostAndFoundFiles(), Is.Empty, "nothing was cancelled");
            Assert.That(RobustFile.ReadAllText(HtmPath), Is.EqualTo(kLocalEdit));
            Assert.That(collection.NeedCheckoutToEdit(_bookFolderPath), Is.False);
            Assert.That(collection.OkToCheckIn(kBookTitle), Is.True);

            collection.PutBook(_bookFolderPath, checkin: true);

            Assert.That(_checkinStartBodies, Has.Count.EqualTo(1));
            Assert.That((string)_checkinStartBodies[0]["checkoutGuid"], Is.EqualTo(kGuid));
            Assert.That(
                File.Exists(CheckoutRecordPath),
                Is.False,
                "a completed check-in ends the checkout"
            );
            Assert.That(collection.GetStatus(kBookTitle).lockedBy, Is.Null.Or.Empty);
        }

        // ------------------------------------------------------------------
        // Obsolete records are cancelled at open
        // ------------------------------------------------------------------

        [Test]
        public void DuplicatedFolder_TwinCheckedIn_ThisCopyWithEdits_IsCancelledIntoLostAndFound()
        {
            // Copy 1 of a duplicated collection folder checked the book in (unlocked, new version);
            // this is copy 2, which still has the old record and local edits.
            _bookRow = MakeRow(2, "cs-after-twin-checkin", null, null);
            WriteCheckoutRecord(kGuid);
            var collection = OpenCollection();
            RecordLastSync(collection);
            RobustFile.WriteAllText(HtmPath, kLocalEdit);

            var hadProblems = collection.SyncAtStartup(new ProgressSpy(), firstTimeJoin: false);

            Assert.That(hadProblems, Is.True, "preserving work should keep the dialog open");
            var preserved = LostAndFoundFiles();
            Assert.That(preserved, Has.Length.EqualTo(1), "the local edits go to Lost and Found");
            using (var zip = ZipFile.OpenRead(preserved[0]))
            {
                var names = zip.Entries.Select(e => e.Name).ToList();
                Assert.That(names, Does.Contain(kBookTitle + ".htm"), "sanity check");
                Assert.That(names, Does.Not.Contain(CloudCheckoutFile.FileName));
            }
            Assert.That((string)LastLogEventBody()?["p_message"], Is.EqualTo("ObsoleteCheckout"));
            Assert.That(HasWorkPreservedMessage(), Is.True);
            Assert.That(File.Exists(CheckoutRecordPath), Is.False);
            Assert.That(RobustFile.ReadAllText(HtmPath), Is.EqualTo(kRemoteContent));
            Assert.That(collection.NeedCheckoutToEdit(_bookFolderPath), Is.True);
        }

        [Test]
        public void DuplicatedFolder_TwinCheckedIn_ThisCopyUnedited_IsCancelledSilently()
        {
            _bookRow = MakeRow(2, "cs-after-twin-checkin", null, null);
            WriteCheckoutRecord(kGuid);
            var collection = OpenCollection();
            RecordLastSync(collection);

            collection.SyncAtStartup(new ProgressSpy(), firstTimeJoin: false);

            Assert.That(LostAndFoundFiles(), Is.Empty);
            Assert.That(LastLogEventBody(), Is.Null, "no incident without lost work");
            Assert.That(HasWorkPreservedMessage(), Is.False);
            Assert.That(File.Exists(CheckoutRecordPath), Is.False);
            Assert.That(RobustFile.ReadAllText(HtmPath), Is.EqualTo(kRemoteContent));
        }

        [Test]
        public void CheckedOutByAnotherAccountSince_RecordIsObsolete_CancelledAtOpen()
        {
            _bookRow = MakeRow(
                1,
                "cs-1",
                "some-other-user-id",
                CloudCheckoutFile.HashGuid(Guid.NewGuid().ToString())
            );
            WriteCheckoutRecord(kGuid);
            var collection = OpenCollection();
            RecordLastSync(collection);

            collection.SyncAtStartup(new ProgressSpy(), firstTimeJoin: false);

            Assert.That(File.Exists(CheckoutRecordPath), Is.False);
            Assert.That(LostAndFoundFiles(), Is.Empty);
            Assert.That(collection.NeedCheckoutToEdit(_bookFolderPath), Is.True);
        }

        [Test]
        public void SameAccountCheckedOutAgainElsewhere_RecordIsObsolete_CancelledAtOpen()
        {
            // Locked by ME, but under a GUID this copy doesn't have (my other copy re-checked it
            // out): LockedBy alone can't tell; the hash does.
            _bookRow = MakeRow(
                1,
                "cs-1",
                kCurrentUserId,
                CloudCheckoutFile.HashGuid(Guid.NewGuid().ToString())
            );
            WriteCheckoutRecord(kGuid);
            var collection = OpenCollection();
            RecordLastSync(collection);
            RobustFile.WriteAllText(HtmPath, kLocalEdit);

            collection.SyncAtStartup(new ProgressSpy(), firstTimeJoin: false);

            Assert.That(File.Exists(CheckoutRecordPath), Is.False);
            Assert.That(LostAndFoundFiles(), Has.Length.EqualTo(1));
            Assert.That(collection.NeedCheckoutToEdit(_bookFolderPath), Is.True);
        }

        // ------------------------------------------------------------------
        // Obsolete records are cancelled by polling (force-unlock)
        // ------------------------------------------------------------------

        [Test]
        public void AdministratorForceUnlocks_FormerHoldersCopy_CancelsAtNextPoll_PreservingEdits()
        {
            // The holder's copy: checked out here, with edits.
            _bookRow = MakeRow(1, "cs-1", kCurrentUserId, CloudCheckoutFile.HashGuid(kGuid));
            WriteCheckoutRecord(kGuid);
            var collection = OpenCollection();
            RecordLastSync(collection);
            RobustFile.WriteAllText(HtmPath, kLocalEdit);
            collection.HydrateFromServer();
            Assert.That(
                collection.NeedCheckoutToEdit(_bookFolderPath),
                Is.False,
                "sanity check: checked out in this copy before the force-unlock"
            );

            // An administrator force-unlocks it from another copy (force_unlock takes no GUID;
            // the server clears the lock and its hash).
            _bookRow = MakeRow(1, "cs-1", null, null);
            _maxEventId = 6;
            try
            {
                collection.StartMonitoring();
                collection.PollNow();
            }
            finally
            {
                collection.StopMonitoring();
            }

            Assert.That(File.Exists(CheckoutRecordPath), Is.False);
            Assert.That(
                LostAndFoundFiles(),
                Has.Length.EqualTo(1),
                "the edits go to Lost and Found"
            );
            Assert.That((string)LastLogEventBody()?["p_message"], Is.EqualTo("ObsoleteCheckout"));
            Assert.That(RobustFile.ReadAllText(HtmPath), Is.EqualTo(kRemoteContent));
            Assert.That(collection.NeedCheckoutToEdit(_bookFolderPath), Is.True);
        }

        [Test]
        public void Poll_OnlyTheGuidHashChanged_RaisesBookStateChange()
        {
            // A new checkout by the SAME account from another copy changes neither LockedBy nor
            // the version, only the hash -- which changes whether the book is checked out HERE.
            _bookRow = MakeRow(1, "cs-1", kCurrentUserId, CloudCheckoutFile.HashGuid(kGuid));
            var collection = OpenCollection();
            collection.HydrateFromServer();
            string raisedFileName = null;
            collection.BookRepoChange += (sender, args) => raisedFileName = args.BookFileName;

            _bookRow = MakeRow(
                1,
                "cs-1",
                kCurrentUserId,
                CloudCheckoutFile.HashGuid(Guid.NewGuid().ToString())
            );
            _maxEventId = 6;
            try
            {
                collection.StartMonitoring();
                collection.PollNow();
            }
            finally
            {
                collection.StopMonitoring();
            }

            Assert.That(raisedFileName, Is.EqualTo(kBookTitle + ".bloom"));
        }

        // ------------------------------------------------------------------
        // Check-in refusals from checkin-finish (v1.8/v1.9) are not retried
        // ------------------------------------------------------------------

        private CloudTeamCollection OpenCheckedOutHere()
        {
            var lastSyncChecksum = Bloom.TeamCollection.TeamCollection.MakeChecksum(
                _bookFolderPath
            );
            _bookRow = MakeRow(
                1,
                lastSyncChecksum,
                kCurrentUserId,
                CloudCheckoutFile.HashGuid(kGuid)
            );
            WriteCheckoutRecord(kGuid);
            var collection = OpenCollection();
            collection.WriteLocalStatus(
                kBookTitle,
                new BookStatus().WithChecksum(lastSyncChecksum)
            );
            RobustFile.WriteAllText(HtmPath, kLocalEdit);
            collection.HydrateFromServer();
            return collection;
        }

        [Test]
        public void CheckinFinish_CheckoutElsewhere_AbortsAndThrowsRefusal_DropsTheRecord()
        {
            var collection = OpenCheckedOutHere();
            var finishCalls = 0;
            _checkinFinishResponse = () =>
            {
                finishCalls++;
                return FakeResponses.Make(
                    HttpStatusCode.Conflict,
                    "{\"error\":\"CheckoutElsewhere\"}"
                );
            };

            var refused = Assert.Throws<CloudCheckinRefusedException>(() =>
                collection.PutBook(_bookFolderPath, checkin: true)
            );

            Assert.That(refused.Code, Is.EqualTo(CloudErrorCode.CheckoutElsewhere));
            Assert.That(refused.Message, Does.Contain("another copy of this collection"));
            Assert.That(finishCalls, Is.EqualTo(1), "a refusal must not be retried");
            Assert.That(_checkinAbortCalled, Is.True);
            Assert.That(File.Exists(CheckoutRecordPath), Is.False, "this copy no longer holds it");
            Assert.That(
                RobustFile.ReadAllText(HtmPath),
                Is.EqualTo(kLocalEdit),
                "local work untouched; the caller preserves it"
            );
        }

        [Test]
        public void CheckinFinish_LockHeldByOther_NullHolder_ExplainsTheLockWasReleased()
        {
            var collection = OpenCheckedOutHere();
            _checkinFinishResponse = () =>
                FakeResponses.Make(
                    HttpStatusCode.Conflict,
                    "{\"error\":\"LockHeldByOther\",\"holder\":null}"
                );

            var refused = Assert.Throws<CloudCheckinRefusedException>(() =>
                collection.PutBook(_bookFolderPath, checkin: true)
            );

            Assert.That(refused.Code, Is.EqualTo(CloudErrorCode.LockHeldByOther));
            Assert.That(refused.Message, Does.Contain("no longer checked out to you"));
            Assert.That(_checkinAbortCalled, Is.True);
            Assert.That(File.Exists(CheckoutRecordPath), Is.False);
        }

        [Test]
        public void CheckinFinish_BaseVersionSuperseded_ThrowsRefusal_KeepsTheCheckout()
        {
            var collection = OpenCheckedOutHere();
            _checkinFinishResponse = () =>
                FakeResponses.Make(
                    HttpStatusCode.Conflict,
                    "{\"error\":\"BaseVersionSuperseded\",\"currentVersionSeq\":3}"
                );

            var refused = Assert.Throws<CloudCheckinRefusedException>(() =>
                collection.PutBook(_bookFolderPath, checkin: true)
            );

            Assert.That(refused.Code, Is.EqualTo(CloudErrorCode.BaseVersionSuperseded));
            Assert.That(_checkinAbortCalled, Is.True);
            Assert.That(
                CloudCheckoutFile.ReadGuid(_bookFolderPath),
                Is.EqualTo(kGuid),
                "the checkout is still ours; only the content is out of date"
            );
        }

        [Test]
        public void CheckinFinish_TransactionChanged_StopsWithoutAborting()
        {
            // A concurrent checkin-start resumed the same transaction; it now belongs to that
            // newer attempt, so this one must stop WITHOUT calling checkin-abort.
            var collection = OpenCheckedOutHere();
            _checkinFinishResponse = () =>
                FakeResponses.Make(HttpStatusCode.Conflict, "{\"error\":\"TransactionChanged\"}");

            var ex = Assert.Throws<ApplicationException>(() =>
                collection.PutBook(_bookFolderPath, checkin: true)
            );

            Assert.That(ex.Message, Does.Contain("Please try again"));
            Assert.That(
                (ex.InnerException as CloudCollectionClientException)?.Code,
                Is.EqualTo(CloudErrorCode.TransactionChanged)
            );
            Assert.That(
                _checkinAbortCalled,
                Is.False,
                "must not abort the newer attempt's transaction"
            );
            Assert.That(
                CloudCheckoutFile.ReadGuid(_bookFolderPath),
                Is.EqualTo(kGuid),
                "nothing was committed; the checkout is untouched"
            );
            Assert.That(RobustFile.ReadAllText(HtmPath), Is.EqualTo(kLocalEdit));
        }

        [Test]
        public void CollectionFilesFinish_TransactionChanged_StopsWithoutAborting()
        {
            _bookRow = MakeRow(1, "cs-1", null, null);
            var collection = OpenCollection();
            collection.HydrateFromServer();
            RobustFile.WriteAllText(
                Path.Combine(_collectionFolderPath, "customCollectionStyles.css"),
                "body {}"
            );
            var finishCalls = 0;
            _executor.Handler = req =>
            {
                switch (req.Resource)
                {
                    case "functions/v1/collection-files-start":
                        return FakeResponses.Make(
                            HttpStatusCode.OK,
                            new JObject
                            {
                                ["transactionId"] = "ctx-1",
                                ["changedPaths"] = new JArray("customCollectionStyles.css"),
                                ["s3"] = S3Block(),
                            }.ToString()
                        );
                    case "functions/v1/collection-files-finish":
                        finishCalls++;
                        return FakeResponses.Make(
                            HttpStatusCode.Conflict,
                            "{\"error\":\"TransactionChanged\"}"
                        );
                    case "functions/v1/collection-files-abort":
                    case "functions/v1/checkin-abort":
                        Assert.Fail("must not abort the newer attempt's transaction");
                        return null;
                    default:
                        return HandleServerRequest(req);
                }
            };

            var ex = Assert.Throws<ApplicationException>(() =>
                collection.PutCollectionFiles(new[] { "customCollectionStyles.css" })
            );

            Assert.That(finishCalls, Is.EqualTo(1), "not retried");
            Assert.That(ex.Message, Does.Contain("Please try again"));
            Assert.That(
                (ex.InnerException as CloudCollectionClientException)?.Code,
                Is.EqualTo(CloudErrorCode.TransactionChanged)
            );
        }

        [Test]
        public void TakeIfFreeCheckin_WritesNoRecord_AndDoesNotAskToKeepTheBookCheckedOut()
        {
            // v1.10: checkin-start never issues a GUID, so a send that isn't from a checkout in
            // this copy only holds the lock while sending; it can't be "kept" (that would need a
            // GUID of our own, as for checkout_book, which is not a feature yet).
            _bookRow = MakeRow(1, "cs-1", null, null);
            var collection = OpenCollection();
            RecordLastSync(collection);
            collection.HydrateFromServer();

            collection.PutBookInRepoForTests(
                _bookFolderPath,
                new BookStatus { lockedBy = kCurrentUser, checksum = "cs-new" }
            );

            Assert.That(
                (string)_checkinStartBodies.Single()["checkoutGuid"],
                Is.Null,
                "sanity check: this copy had no GUID to send"
            );
            Assert.That((bool)_checkinFinishBodies.Single()["keepCheckedOut"], Is.False);
            Assert.That(File.Exists(CheckoutRecordPath), Is.False, "a check-in never writes one");
            Assert.That(collection.GetStatus(kBookTitle).lockedBy, Is.Null.Or.Empty);
        }

        [Test]
        public void KeepCheckedOutCheckin_OfABookHeldHere_LeavesTheCheckoutAsItWas()
        {
            var collection = OpenCheckedOutHere();

            collection.PutBookInRepoForTests(
                _bookFolderPath,
                new BookStatus { lockedBy = kCurrentUser, checksum = "cs-new" }
            );

            Assert.That((string)_checkinStartBodies.Single()["checkoutGuid"], Is.EqualTo(kGuid));
            Assert.That((bool)_checkinFinishBodies.Single()["keepCheckedOut"], Is.True);
            Assert.That(CloudCheckoutFile.ReadGuid(_bookFolderPath), Is.EqualTo(kGuid));
            Assert.That(collection.IsCheckedOutInThisCopy(kBookTitle), Is.True);
        }

        // ------------------------------------------------------------------
        // A checkin-finish whose answer is lost
        // ------------------------------------------------------------------

        private static IRestResponse NoResponse() =>
            new RestResponse
            {
                ResponseStatus = ResponseStatus.TimedOut,
                ErrorMessage = "timed out",
            };

        private static IRestResponse Committed(long seq) =>
            FakeResponses.Make(
                HttpStatusCode.OK,
                new JObject { ["versionId"] = "v" + seq, ["seq"] = seq }.ToString()
            );

        private void Poll(CloudTeamCollection collection)
        {
            _maxEventId++;
            try
            {
                collection.StartMonitoring();
                collection.PollNow();
            }
            finally
            {
                collection.StopMonitoring();
            }
        }

        [Test]
        public void CheckinFinish_ResponseLostOnce_IsRetried_AndCompletes()
        {
            var collection = OpenCheckedOutHere();
            collection.LostResponseRetryDelays = new[] { TimeSpan.Zero, TimeSpan.Zero };
            var finishCalls = 0;
            _checkinFinishResponse = () => ++finishCalls == 1 ? NoResponse() : Committed(2);

            collection.PutBook(_bookFolderPath, checkin: true);

            Assert.That(finishCalls, Is.EqualTo(2));
            Assert.That(
                _checkinFinishBodies.Select(b => (string)b["transactionId"]).Distinct(),
                Is.EquivalentTo(new[] { "tx-1" }),
                "the retry finishes the same transaction"
            );
            Assert.That(_checkinAbortCalled, Is.False);
            Assert.That(File.Exists(CheckoutRecordPath), Is.False);
            Assert.That(collection.GetLocalVersionSeq(kBookTitle), Is.EqualTo(2));
        }

        /// <summary>Checks in the (edited) book held in this copy while every checkin-finish
        /// goes unanswered, so the outcome is unknown.</summary>
        private CloudTeamCollection CheckInWithoutAnAnswer(CloudTeamCollection collection)
        {
            collection.LostResponseRetryDelays = new[] { TimeSpan.Zero };
            _checkinFinishResponse = NoResponse;
            var editableBefore = !collection.NeedCheckoutToEdit(_bookFolderPath);

            var unconfirmed = Assert.Throws<CloudCheckinUnconfirmedException>(() =>
                collection.PutBook(_bookFolderPath, checkin: true)
            );

            Assert.That(editableBefore, Is.True, "sanity check: editable before the check-in");
            Assert.That(unconfirmed.Message, Does.Contain("did not hear back"));
            Assert.That(_checkinFinishBodies, Has.Count.EqualTo(2), "one call plus one retry");
            Assert.That(_checkinAbortCalled, Is.False, "it may have committed: never abort");
            Assert.That(collection.IsCheckinUnconfirmed(kBookTitle), Is.True);
            Assert.That(
                collection.NeedCheckoutToEdit(_bookFolderPath),
                Is.True,
                "read-only until the outcome is known"
            );
            Assert.That(RobustFile.ReadAllText(HtmPath), Is.EqualTo(kLocalEdit));
            return collection;
        }

        [Test]
        public void CheckinFinish_NoAnswer_BookReadOnly_ThenThePollFindsItCommitted()
        {
            var collection = CheckInWithoutAnAnswer(OpenCheckedOutHere());
            Assert.That(
                collection.GetStatus(kBookTitle).lockedBy,
                Is.Null.Or.Empty,
                "shown as checked in meanwhile"
            );
            Assert.That(File.Exists(CheckoutRecordPath), Is.True, "kept until we know");
            var committedChecksum = Bloom.TeamCollection.TeamCollection.MakeChecksum(
                _bookFolderPath
            );

            // It had committed: the retry gets the (idempotent) result.
            _checkinFinishResponse = () => Committed(2);
            _bookRow = MakeRow(2, committedChecksum, null, null);
            Poll(collection);

            Assert.That(collection.IsCheckinUnconfirmed(kBookTitle), Is.False);
            Assert.That(File.Exists(CheckoutRecordPath), Is.False);
            Assert.That(collection.NeedCheckoutToEdit(_bookFolderPath), Is.True);
            Assert.That(
                collection.GetLocalStatus(kBookTitle).checksum,
                Is.EqualTo(committedChecksum)
            );
            Assert.That(collection.GetLocalVersionSeq(kBookTitle), Is.EqualTo(2));
            Assert.That(LostAndFoundFiles(), Is.Empty);
            Assert.That(RobustFile.ReadAllText(HtmPath), Is.EqualTo(kLocalEdit));
        }

        [Test]
        public void CheckinFinish_NoAnswer_BookReadOnly_ThenThePollFindsItStillCheckedOutHere()
        {
            var collection = CheckInWithoutAnAnswer(OpenCheckedOutHere());

            // It never committed (the transaction has since expired); the server still has the
            // book checked out in this copy.
            _checkinFinishResponse = () =>
                FakeResponses.Make(HttpStatusCode.Gone, "{\"error\":\"TransactionExpired\"}");
            Poll(collection);

            Assert.That(collection.IsCheckinUnconfirmed(kBookTitle), Is.False);
            Assert.That(CloudCheckoutFile.ReadGuid(_bookFolderPath), Is.EqualTo(kGuid));
            Assert.That(
                collection.NeedCheckoutToEdit(_bookFolderPath),
                Is.False,
                "editable again: still checked out here"
            );
            Assert.That(RobustFile.ReadAllText(HtmPath), Is.EqualTo(kLocalEdit));
            Assert.That(LostAndFoundFiles(), Is.Empty);
        }

        [Test]
        public void CheckinFinish_NoAnswer_TryingToCheckInAgainBeforeKnowing_IsRefused()
        {
            var collection = CheckInWithoutAnAnswer(OpenCheckedOutHere());
            var startsBefore = _checkinStartBodies.Count;

            var ex = Assert.Throws<ApplicationException>(() =>
                collection.PutBook(_bookFolderPath, checkin: true)
            );

            Assert.That(ex.Message, Does.Contain("still finding out"));
            Assert.That(_checkinStartBodies, Has.Count.EqualTo(startsBefore));
        }

        /// <summary>A new local book (the server has no row for it), first check-in sent, no
        /// answer to its finish.</summary>
        private CloudTeamCollection FirstCheckInWithoutAnAnswer()
        {
            _bookRow = null;
            var collection = OpenCollection();
            collection.HydrateFromServer();
            RobustFile.WriteAllText(HtmPath, kLocalEdit);
            Assert.That(
                collection.GetStatus(kBookTitle).lockedBy,
                Is.EqualTo(Bloom.TeamCollection.TeamCollection.FakeUserIndicatingNewBook),
                "sanity check: a new book"
            );
            return CheckInWithoutAnAnswer(collection);
        }

        [Test]
        public void FirstCheckin_NoAnswer_ReadOnly_ThenThePollFindsItCommitted()
        {
            var collection = FirstCheckInWithoutAnAnswer();
            Assert.That(
                (string)_checkinStartBodies.Last()["checkoutGuid"],
                Is.Null,
                "sanity check: a first check-in has no GUID"
            );

            _checkinFinishResponse = () => Committed(1);
            _bookRow = MakeRow(
                1,
                Bloom.TeamCollection.TeamCollection.MakeChecksum(_bookFolderPath),
                null,
                null
            );
            Poll(collection);

            Assert.That(collection.IsCheckinUnconfirmed(kBookTitle), Is.False);
            Assert.That(collection.IsCheckedOutInThisCopy(kBookTitle), Is.False, "checked in");
            Assert.That(collection.NeedCheckoutToEdit(_bookFolderPath), Is.True);
            Assert.That(collection.GetLocalVersionSeq(kBookTitle), Is.EqualTo(1));
            Assert.That(File.Exists(CheckoutRecordPath), Is.False);
        }

        [Test]
        public void FirstCheckin_NoAnswer_ReadOnly_ThenThePollFindsItNeverCommitted()
        {
            var collection = FirstCheckInWithoutAnAnswer();

            _checkinFinishResponse = () =>
                FakeResponses.Make(HttpStatusCode.Gone, "{\"error\":\"TransactionExpired\"}");
            Poll(collection);

            Assert.That(collection.IsCheckinUnconfirmed(kBookTitle), Is.False);
            Assert.That(
                collection.GetStatus(kBookTitle).lockedBy,
                Is.EqualTo(Bloom.TeamCollection.TeamCollection.FakeUserIndicatingNewBook)
            );
            Assert.That(
                collection.NeedCheckoutToEdit(_bookFolderPath),
                Is.False,
                "a new book again, editable"
            );
            Assert.That(RobustFile.ReadAllText(HtmPath), Is.EqualTo(kLocalEdit));
        }

        // ------------------------------------------------------------------
        // Startup reconciliation after a crash right after the server committed
        // ------------------------------------------------------------------

        [Test]
        public void CheckoutWrittenAheadThenCrashBeforeTheCall_RecordRemovedSilentlyAtOpen()
        {
            // The record was saved, then Bloom died before checkout_book reached the server:
            // the server never locked it.
            var checksum = Bloom.TeamCollection.TeamCollection.MakeChecksum(_bookFolderPath);
            _bookRow = MakeRow(1, checksum, null, null);
            WriteCheckoutRecord(kGuid);
            var collection = OpenCollection();
            collection.WriteLocalStatus(kBookTitle, new BookStatus().WithChecksum(checksum));

            var hadProblems = collection.SyncAtStartup(new ProgressSpy(), firstTimeJoin: false);

            Assert.That(hadProblems, Is.False);
            Assert.That(File.Exists(CheckoutRecordPath), Is.False);
            Assert.That(LostAndFoundFiles(), Is.Empty);
            Assert.That(LastLogEventBody(), Is.Null, "no incident");
            Assert.That(RobustFile.ReadAllText(HtmPath), Is.EqualTo(kLocalContent));
            Assert.That(collection.NeedCheckoutToEdit(_bookFolderPath), Is.True);
        }

        [Test]
        public void ExistingBookCheckinCommittedJustBeforeACrash_ReconciledQuietly_NoLostAndFound()
        {
            // Checked out here, edited, checked in; the server committed (unlocked, new version
            // with exactly this content), but Bloom died before hearing so: the record is still
            // there and the local status still has the pre-edit checksum.
            var lastSyncChecksum = Bloom.TeamCollection.TeamCollection.MakeChecksum(
                _bookFolderPath
            );
            RobustFile.WriteAllText(HtmPath, kLocalEdit);
            var committedChecksum = Bloom.TeamCollection.TeamCollection.MakeChecksum(
                _bookFolderPath
            );
            Assert.That(committedChecksum, Is.Not.EqualTo(lastSyncChecksum), "sanity check");
            _bookRow = MakeRow(2, committedChecksum, null, null);
            WriteCheckoutRecord(kGuid);
            var collection = OpenCollection();
            collection.WriteLocalStatus(
                kBookTitle,
                new BookStatus().WithChecksum(lastSyncChecksum)
            );

            var hadProblems = collection.SyncAtStartup(new ProgressSpy(), firstTimeJoin: false);

            Assert.That(hadProblems, Is.False);
            Assert.That(LostAndFoundFiles(), Is.Empty, "the local content IS the committed one");
            Assert.That(HasWorkPreservedMessage(), Is.False);
            Assert.That(File.Exists(CheckoutRecordPath), Is.False);
            Assert.That(RobustFile.ReadAllText(HtmPath), Is.EqualTo(kLocalEdit));
            Assert.That(collection.NeedCheckoutToEdit(_bookFolderPath), Is.True, "checked in");
            Assert.That(
                collection.GetLocalStatus(kBookTitle).checksum,
                Is.EqualTo(committedChecksum)
            );
            Assert.That(collection.GetLocalVersionSeq(kBookTitle), Is.EqualTo(2));
            Assert.That(collection.GetUpdatesAvailableCount(), Is.EqualTo(0));
        }

        /// <summary>
        /// The shared part of this (no local status, same checksum in the repo: take the repo's
        /// status) is covered for every backend by
        /// SyncAtStartupTests.SyncAtStartup_SameBookLocallyAndShared_NoLocalStatus_KeepsBookAddsStatus;
        /// this adds what is cloud-specific: the copy is recorded as holding the committed
        /// version (no "update available", nothing received) and is not editable.
        /// </summary>
        [Test]
        public void NewBookFirstCheckinCommittedJustBeforeACrash_ShownCheckedIn_AndCurrent()
        {
            RobustFile.WriteAllText(HtmPath, kLocalEdit);
            _bookRow = MakeRow(
                1,
                Bloom.TeamCollection.TeamCollection.MakeChecksum(_bookFolderPath),
                null,
                null
            );
            var collection = OpenCollection();
            Assert.That(
                File.Exists(
                    Bloom.TeamCollection.TeamCollection.GetStatusFilePath(
                        kBookTitle,
                        _collectionFolderPath
                    )
                ),
                Is.False,
                "sanity check: locally it still looks new"
            );

            var hadProblems = collection.SyncAtStartup(new ProgressSpy(), firstTimeJoin: false);

            Assert.That(hadProblems, Is.False);
            Assert.That(LostAndFoundFiles(), Is.Empty);
            Assert.That(RobustFile.ReadAllText(HtmPath), Is.EqualTo(kLocalEdit));
            Assert.That(collection.GetLocalVersionSeq(kBookTitle), Is.EqualTo(1));
            Assert.That(collection.GetUpdatesAvailableCount(), Is.EqualTo(0));
            Assert.That(collection.NeedCheckoutToEdit(_bookFolderPath), Is.True, "checked in");
        }
    }
}
