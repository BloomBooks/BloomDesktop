using System;
using System.IO;
using System.Linq;
using System.Net;
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
    /// Exercises the (unchanged, shared) base-class SyncAtStartup logic against a CloudTeamCollection
    /// with a scripted server, per the task 05 acceptance criteria ("CloudSyncAtStartupTests (ported
    /// matrix; asserts .bloomSource + incident events)"). Scope note: this ports the single highest-
    /// value scenario from the folder-backend SyncAtStartupTests suite -- "checked out and modified
    /// locally, but also modified remotely" (that suite's "Update content and status and warn" case)
    /// -- which is exactly the case that drives CloudTeamCollection's unified-recovery path
    /// (PutBookInRepo's inLostAndFound branch). Porting the full ~15-case matrix from
    /// SyncAtStartupTests.cs would need a fuller cloud server fake (id-conflict scans, rename
    /// detection via GetRepoBookFile, etc.) -- left as follow-up work; see the task 05 final report.
    /// </summary>
    [TestFixture]
    public class CloudSyncAtStartupTests
    {
        private const string kCollectionId = "11111111-1111-1111-1111-111111111111";
        private const string kBookId = "book-id-1";
        private CloudTestHarness _harness;
        private TemporaryFolder _collectionFolder;
        private Mock<ITeamCollectionManager> _mockTcManager;
        private CloudTeamCollection _collection;
        private FakeRestExecutor _executor;
        private string _bookInstanceId;

        [SetUp]
        public void Setup()
        {
            // The scripted manifests carry one real file entry (empty manifests are now rejected by
            // FetchAndCacheManifest's fail-fast guard -- no real book has zero files), so the S3
            // mock serves fixed bytes matching that entry's sha256.
            _harness = CloudTestHarness.Create(
                "CloudSyncAtStartupTests",
                kCollectionId,
                s3Factory: _ => MakeScriptedS3().Object
            );
            _collectionFolder = _harness.CollectionFolder;
            _mockTcManager = _harness.MockTcManager;
            _collection = _harness.Collection;
            _executor = _harness.Executor;
        }

        /// <summary>The fixed content the scripted S3 serves for every key; the scripted
        /// get_book_manifest entries carry its sha256 so pinned-download verification passes.</summary>
        internal const string kRemoteFileContent = "remote content from scripted S3";

        private static Mock<Amazon.S3.IAmazonS3> MakeScriptedS3()
        {
            var mock = new Mock<Amazon.S3.IAmazonS3>();
            mock.Setup(x =>
                    x.GetObjectAsync(
                        It.IsAny<Amazon.S3.Model.GetObjectRequest>(),
                        It.IsAny<System.Threading.CancellationToken>()
                    )
                )
                .Returns<Amazon.S3.Model.GetObjectRequest, System.Threading.CancellationToken>(
                    (req, ct) =>
                        System.Threading.Tasks.Task.FromResult(
                            new Amazon.S3.Model.GetObjectResponse
                            {
                                ResponseStream = new MemoryStream(
                                    System.Text.Encoding.UTF8.GetBytes(kRemoteFileContent)
                                ),
                                HttpStatusCode = HttpStatusCode.OK,
                                VersionId = req.VersionId,
                            }
                        )
                );
            return mock;
        }

        [TearDown]
        public void TearDown()
        {
            _harness.Dispose();
        }

        private IRestResponse HandleServerRequest(IRestRequest req)
        {
            switch (req.Resource)
            {
                case "rest/v1/rpc/get_collection_state":
                {
                    var body = new JObject
                    {
                        ["books"] = new JArray(
                            new JObject
                            {
                                ["id"] = kBookId,
                                ["instance_id"] = _bookInstanceId,
                                ["name"] = "Conflict book",
                                ["current_version_id"] = "v2",
                                ["current_version_seq"] = 2,
                                ["current_checksum"] = "server-side-checksum-after-remote-edit",
                                ["locked_by"] = null,
                                ["locked_by_machine"] = null,
                                ["locked_at"] = null,
                                ["deleted_at"] = null,
                            }
                        ),
                        ["groups"] = new JArray(),
                        ["max_event_id"] = 5,
                    };
                    return FakeResponses.Make(HttpStatusCode.OK, body.ToString());
                }
                case "rest/v1/rpc/get_book_manifest":
                {
                    var body = new JObject
                    {
                        ["bookId"] = kBookId,
                        ["versionId"] = "v2",
                        ["seq"] = 2,
                        ["checksum"] = "server-side-checksum-after-remote-edit",
                        ["files"] = new JArray(
                            new JObject
                            {
                                ["path"] = "New remote book.htm",
                                ["sha256"] =
                                    "3baef9a5f9108b41fff904fdcd328c6cb666712e9779d168ac2f7ffbdfc32372",
                                ["size"] = 31,
                                ["s3VersionId"] = "sv1",
                            }
                        ),
                    };
                    return FakeResponses.Make(HttpStatusCode.OK, body.ToString());
                }
                case "functions/v1/download-start":
                {
                    var body = new JObject
                    {
                        ["s3"] = new JObject
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
                        },
                    };
                    return FakeResponses.Make(HttpStatusCode.OK, body.ToString());
                }
                case "rest/v1/rpc/log_event":
                    return FakeResponses.Make(HttpStatusCode.OK, "{}");
                default:
                    return FakeResponses.Make(HttpStatusCode.OK, "{}");
            }
        }

        [Test]
        public void SyncAtStartup_LocalEditConflictsWithRemoteChange_PreservesLocallyAndReceivesLatest()
        {
            // Arrange a book checked out and edited locally, whose repo checksum has ALSO changed
            // (someone else's Send landed while we were offline) -- SyncAtStartup's "book changed
            // remotely AND edited locally" case, which drives PutBook(..., inLostAndFound: true).
            var folderPath = new BookFolderBuilder()
                .WithRootFolder(_collectionFolder.FolderPath)
                .WithTitle("Conflict book")
                .WithHtm("<html><body>original content</body></html>")
                .Build()
                .BuiltBookFolderPath;
            _bookInstanceId = BookMetaData.FromFolder(folderPath).Id;

            var recordedChecksum = Bloom.TeamCollection.TeamCollection.MakeChecksum(folderPath);
            var localStatus = new BookStatus()
                .WithLockedBy("test@somewhere.org")
                .WithChecksum(recordedChecksum);
            _collection.WriteLocalStatus("Conflict book", localStatus);

            // Simulate a local edit made after that status was recorded: the actual on-disk content
            // (and therefore its checksum) now differs from localStatus.checksum.
            RobustFile.WriteAllText(
                Path.Combine(folderPath, "Conflict book.htm"),
                "<html><body>edited locally, not yet checked in</body></html>"
            );

            _executor.Handler = HandleServerRequest;
            var progressSpy = new ProgressSpy();

            // Act
            _collection.SyncAtStartup(progressSpy, firstTimeJoin: false);

            // Assert: the local edit was preserved as a .bloomSource file in a local Lost and Found...
            var lostAndFoundDir = Path.Combine(_collectionFolder.FolderPath, "Lost and Found");
            Assert.That(
                Directory.Exists(lostAndFoundDir),
                Is.True,
                "Lost and Found folder should have been created"
            );
            var preserved = Directory.EnumerateFiles(lostAndFoundDir, "*.bloomSource").ToList();
            Assert.That(preserved, Is.Not.Empty, "a .bloomSource file should have been saved");

            // ...an incident was posted to the server (log_event, type 100/WorkPreservedLocally)...
            var logEventRequest = FindLogEventRequest();
            Assert.That(logEventRequest, Is.Not.Null, "log_event RPC should have been called");

            // ...and a message was logged locally for the user to see.
            Assert.That(
                _collection.MessageLog.Messages,
                Has.Some.Matches<TeamCollectionMessage>(m =>
                    m.L10NId == "TeamCollection.Cloud.WorkPreservedLocally"
                    && m.Param0 == "Conflict book"
                )
            );
        }

        /// <summary>An S3 mock that serves different bytes per requested key, unlike
        /// <see cref="MakeScriptedS3"/>'s fixed content -- needed by the rename tests, whose
        /// GetRepoBooksByIdMap pass downloads the repo book's real meta.json (it must parse and
        /// carry the right instance id).</summary>
        private static Mock<Amazon.S3.IAmazonS3> MakeScriptedS3PerKey(
            Func<string, byte[]> bytesForKey
        )
        {
            var mock = new Mock<Amazon.S3.IAmazonS3>();
            mock.Setup(x =>
                    x.GetObjectAsync(
                        It.IsAny<Amazon.S3.Model.GetObjectRequest>(),
                        It.IsAny<System.Threading.CancellationToken>()
                    )
                )
                .Returns<Amazon.S3.Model.GetObjectRequest, System.Threading.CancellationToken>(
                    (req, ct) =>
                        System.Threading.Tasks.Task.FromResult(
                            new Amazon.S3.Model.GetObjectResponse
                            {
                                ResponseStream = new MemoryStream(bytesForKey(req.Key)),
                                HttpStatusCode = HttpStatusCode.OK,
                                VersionId = req.VersionId,
                            }
                        )
                );
            return mock;
        }

        /// <summary>Builds a second CloudTeamCollection over the same local folder whose S3 mock
        /// serves per-key content (see <see cref="MakeScriptedS3PerKey"/>). Reassigns _executor so
        /// the test scripts this instance's server.</summary>
        private CloudTeamCollection MakeCollectionWithPerKeyS3(Func<string, byte[]> bytesForKey)
        {
            var environment = new CloudEnvironment(name =>
                name == "BLOOM_CLOUDTC_ANON_KEY" ? "test-anon-key" : null
            );
            var auth = new CloudAuth(new StubCloudAuthProvider(), new InMemoryCloudTokenStore());
            auth.SignIn("test@somewhere.org", "irrelevant");
            var client = new CloudCollectionClient(environment, auth);
            _executor = new FakeRestExecutor();
            client.SetRestClientForTests(_executor);
            return new CloudTeamCollection(
                _mockTcManager.Object,
                _collectionFolder.FolderPath,
                kCollectionId,
                environment: environment,
                auth: auth,
                client: client,
                transfer: new CloudBookTransfer(_ => MakeScriptedS3PerKey(bytesForKey).Object)
            );
        }

        /// <summary>Scripts get_collection_state/get_book_manifest/download-start for ONE repo
        /// book whose manifest exactly matches the given local folder's content (so an incremental
        /// Receive of it downloads nothing except what a test's S3 mock serves).</summary>
        private void ScriptSingleBookServer(
            string repoBookName,
            string instanceId,
            string folderPathForManifest,
            bool lockedByCurrentUserHere
        )
        {
            var manifest = BookVersionManifest.FromLocalFolder(folderPathForManifest);
            // A LOCAL manifest has no s3VersionId (files were never committed), but the server's
            // version manifest always carries one and the pinned-download path fails fast without
            // it -- so stamp one per file.
            var manifestFiles = new JArray(
                manifest.Entries.Select(kvp =>
                    (JToken)
                        new JObject
                        {
                            ["path"] = kvp.Key,
                            ["sha256"] = kvp.Value.Sha256,
                            ["size"] = kvp.Value.Size,
                            ["s3VersionId"] = "sv-" + kvp.Key,
                        }
                )
            );
            var stateBody = new JObject
            {
                ["books"] = new JArray(
                    new JObject
                    {
                        ["id"] = kBookId,
                        ["instance_id"] = instanceId,
                        ["name"] = repoBookName,
                        ["current_version_id"] = "v1",
                        ["current_version_seq"] = 1,
                        ["current_checksum"] = "cs1",
                        // StubCloudAuthProvider signs every session in as user id "user-1", which
                        // ResolveLockedByForDisplay maps back to the signed-in email -- so this
                        // row reads as "checked out by the current user on this machine".
                        ["locked_by"] = lockedByCurrentUserHere ? "user-1" : null,
                        ["locked_by_machine"] = lockedByCurrentUserHere
                            ? TeamCollectionManager.CurrentMachine
                            : null,
                        ["locked_at"] = lockedByCurrentUserHere ? "2026-07-15T00:00:00Z" : null,
                        ["deleted_at"] = null,
                    }
                ),
                ["groups"] = new JArray(),
                ["max_event_id"] = 5,
            };
            var manifestBody = new JObject
            {
                ["bookId"] = kBookId,
                ["versionId"] = "v1",
                ["seq"] = 1,
                ["checksum"] = "cs1",
                ["files"] = manifestFiles,
            };
            var downloadStartBody = new JObject
            {
                ["s3"] = new JObject
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
                },
            };
            _executor.Handler = req =>
            {
                switch (req.Resource)
                {
                    case "rest/v1/rpc/get_collection_state":
                        return FakeResponses.Make(HttpStatusCode.OK, stateBody.ToString());
                    case "rest/v1/rpc/get_book_manifest":
                        return FakeResponses.Make(HttpStatusCode.OK, manifestBody.ToString());
                    case "functions/v1/download-start":
                        return FakeResponses.Make(HttpStatusCode.OK, downloadStartBody.ToString());
                    default:
                        return FakeResponses.Make(HttpStatusCode.OK, "{}");
                }
            };
        }

        // Regression for bug B (14 Jul 2026): a teammate's rename must be applied by renaming the
        // local folder IN PLACE -- not leaving the old-name folder in "newer version available"
        // limbo while a later pass downloads the new name as a duplicate with the same instance id.
        [Test]
        public void SyncAtStartup_TeammateRenamedBook_RenamesLocalFolderInPlace()
        {
            var folderPath = new BookFolderBuilder()
                .WithRootFolder(_collectionFolder.FolderPath)
                .WithTitle("Old book name")
                .WithHtm("<html><body>same content both sides</body></html>")
                .Build()
                .BuiltBookFolderPath;
            var instanceId = BookMetaData.FromFolder(folderPath).Id;
            // Capture the meta.json bytes NOW: the folder gets renamed mid-test, and the repo's
            // meta.json must carry this instance id for the id-map rename detection to bind.
            var metaBytes = RobustFile.ReadAllBytes(Path.Combine(folderPath, "meta.json"));
            // Sanity: the scripted manifest is built from the local folder and MUST include
            // meta.json (rename detection reads the repo book's meta.json via the manifest).
            var localManifest = BookVersionManifest.FromLocalFolder(folderPath);
            Assert.That(
                localManifest.Entries.Keys,
                Does.Contain("meta.json"),
                "setup: manifest lacks meta.json; entries: "
                    + string.Join(", ", localManifest.Entries.Keys)
            );

            var collection = MakeCollectionWithPerKeyS3(key =>
                key.EndsWith("meta.json")
                    ? metaBytes
                    : System.Text.Encoding.UTF8.GetBytes(kRemoteFileContent)
            );
            collection.TestOnly_MakeAutoApplyQueueSynchronous();
            // The repo has the SAME book (by instance id) under a NEW name, not locked; its
            // manifest matches the local content exactly (a pure rename).
            ScriptSingleBookServer(
                "New book name",
                instanceId,
                folderPath,
                lockedByCurrentUserHere: false
            );

            // Sanity: the local folder starts under the old name.
            Assert.That(Directory.Exists(folderPath), Is.True);
            // Sanity: rename detection depends on reading the REPO book's meta.json (for its
            // instance id) through the scripted manifest + S3; prove that path works before
            // blaming the rename logic itself. (Any call hydrates the cache first, as SyncAtStartup
            // itself would.)
            Assert.That(collection.IsBookPresentInRepo("New book name"), Is.True);
            Assert.That(
                collection.GetRepoBookFile("New book name", "meta.json"),
                Does.Contain(instanceId),
                "setup: the repo book's meta.json must be fetchable and carry the instance id"
            );

            collection.SyncAtStartup(new ProgressSpy(), firstTimeJoin: false);

            var newFolderPath = Path.Combine(_collectionFolder.FolderPath, "New book name");
            Assert.That(
                Directory.Exists(newFolderPath),
                Is.True,
                "the local folder should have been renamed to the repo's new name"
            );
            Assert.That(
                Directory.Exists(folderPath),
                Is.False,
                "the old-name folder must be gone (a lingering copy is the duplicate-id bug)"
            );
            var htmPath = Directory.EnumerateFiles(newFolderPath, "*.htm").Single();
            Assert.That(
                RobustFile.ReadAllText(htmPath),
                Does.Contain("same content both sides"),
                "a pure rename must keep the local content"
            );
        }

        // Regression for the guard on the bug B fix (15 Jul 2026 review): the local-rename-mid-
        // checkin edge. When the CURRENT USER has the book checked out ON THIS MACHINE and renamed
        // it locally (not yet checked in), the repo intentionally still shows the OLD name -- the
        // rename-from-remote pass must NOT "correct" the local folder back to the repo name, which
        // would clobber the checked-out work. Cloud checkouts never stamp the LOCAL status
        // (TryLockInRepo is RPC + cache only), so this guard must read the REPO lock.
        [Test]
        public void SyncAtStartup_OwnRenameMidCheckin_DoesNotRevertRenameOrClobber()
        {
            var folderPath = new BookFolderBuilder()
                .WithRootFolder(_collectionFolder.FolderPath)
                .WithTitle("My renamed book")
                .WithHtm("<html><body>my precious checked-out edits</body></html>")
                .Build()
                .BuiltBookFolderPath;
            var instanceId = BookMetaData.FromFolder(folderPath).Id;
            var metaBytes = RobustFile.ReadAllBytes(Path.Combine(folderPath, "meta.json"));

            var collection = MakeCollectionWithPerKeyS3(key =>
                key.EndsWith("meta.json")
                    ? metaBytes
                    : System.Text.Encoding.UTF8.GetBytes(kRemoteFileContent)
            );
            // Synchronous queue: if anything wrongly routes the repo's old-name book to the
            // background download path, it happens inline where the asserts below can see it.
            collection.TestOnly_MakeAutoApplyQueueSynchronous();
            // The repo shows the OLD name, checked out to the current user on this machine --
            // exactly the state after "check out, retitle, restart Bloom before checking in".
            ScriptSingleBookServer(
                "My old book",
                instanceId,
                folderPath,
                lockedByCurrentUserHere: true
            );

            collection.SyncAtStartup(new ProgressSpy(), firstTimeJoin: false);

            Assert.That(
                Directory.Exists(folderPath),
                Is.True,
                "the checked-out, locally-renamed folder must be left alone"
            );
            Assert.That(
                Directory.Exists(Path.Combine(_collectionFolder.FolderPath, "My old book")),
                Is.False,
                "the repo's old name must not be resurrected as a second folder"
            );
            var htmPath = Directory.EnumerateFiles(folderPath, "*.htm").Single();
            Assert.That(
                RobustFile.ReadAllText(htmPath),
                Does.Contain("my precious checked-out edits"),
                "checked-out local edits must not be overwritten from the repo"
            );
        }

        private JObject FindLogEventRequest()
        {
            // The FakeRestExecutor doesn't record request bodies against a specific resource by
            // itself, so re-derive it from the recorded requests list.
            var request = _executor.RequestsSeen.LastOrDefault(r =>
                r.Resource == "rest/v1/rpc/log_event"
            );
            if (request == null)
                return null;
            var bodyParam = request.Parameters.Find(p => p.Type == ParameterType.RequestBody);
            return bodyParam == null ? new JObject() : JObject.Parse((string)bodyParam.Value);
        }

        [Test]
        public void SyncAtStartup_NewBookOnlyInRepo_IsFetchedToLocal()
        {
            // A book that exists in the repo but not locally at all (the ordinary "Add me" case).
            //
            // Batch item 7 (progressive join) changed this scenario's WIRING: cloud SyncAtStartup
            // now reroutes a repo-only book to the background auto-apply queue instead of fetching
            // it synchronously inline (so a half-joined collection's next open stays fast). This
            // test's ASSERTION ("the book ends up on disk") is deliberately kept unchanged --
            // TestOnly_MakeAutoApplyQueueSynchronous makes the queue's worker run inline instead of
            // via a background Task.Run, so the download still completes, deterministically, before
            // SyncAtStartup returns. See TeamCollectionAutoApplyTests for tests that exercise the
            // genuinely-asynchronous, non-blocking behavior this rerouting exists for.
            _collection.TestOnly_MakeAutoApplyQueueSynchronous();
            _bookInstanceId = Guid.NewGuid().ToString();
            _executor.Handler = req =>
            {
                if (req.Resource == "rest/v1/rpc/get_collection_state")
                {
                    var body = new JObject
                    {
                        ["books"] = new JArray(
                            new JObject
                            {
                                ["id"] = kBookId,
                                ["instance_id"] = _bookInstanceId,
                                ["name"] = "New remote book",
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
                }
                return HandleServerRequest(req);
            };

            var progressSpy = new ProgressSpy();
            _collection.SyncAtStartup(progressSpy, firstTimeJoin: false);

            Assert.That(
                Directory.Exists(Path.Combine(_collectionFolder.FolderPath, "New remote book")),
                Is.True,
                "the new remote book's folder should have been fetched locally"
            );
        }
    }
}
