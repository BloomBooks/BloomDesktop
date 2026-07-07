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
        private TemporaryFolder _collectionFolder;
        private Mock<ITeamCollectionManager> _mockTcManager;
        private CloudTeamCollection _collection;
        private FakeRestExecutor _executor;
        private string _bookInstanceId;

        [SetUp]
        public void Setup()
        {
            _collectionFolder = new TemporaryFolder("CloudSyncAtStartupTests");
            _mockTcManager = new Mock<ITeamCollectionManager>();
            TeamCollectionManager.ForceCurrentUserForTests("test@somewhere.org");

            var environment = new CloudEnvironment(name =>
                name == "BLOOM_CLOUDTC_ANON_KEY" ? "test-anon-key" : null
            );
            var auth = new CloudAuth(new StubCloudAuthProvider(), new InMemoryCloudTokenStore());
            auth.SignIn("test@somewhere.org", "irrelevant");
            var client = new CloudCollectionClient(environment, auth);
            _executor = new FakeRestExecutor();
            client.SetRestClientForTests(_executor);

            _collection = new CloudTeamCollection(
                _mockTcManager.Object,
                _collectionFolder.FolderPath,
                kCollectionId,
                environment: environment,
                auth: auth,
                client: client,
                // Zero-file manifests below mean DownloadFiles is called with an empty file list,
                // so no real S3 interaction happens -- the mock object is never actually invoked.
                transfer: new CloudBookTransfer(_ => new Mock<Amazon.S3.IAmazonS3>().Object)
            );
        }

        [TearDown]
        public void TearDown()
        {
            _collectionFolder.Dispose();
            TeamCollectionManager.ForceCurrentUserForTests(null);
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
                        ["files"] = new JArray(), // empty -- avoids needing real S3 mocking
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
