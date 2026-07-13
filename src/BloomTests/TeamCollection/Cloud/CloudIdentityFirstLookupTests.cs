using System.IO;
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
    /// Tests for CloudTeamCollection's identity-first book resolution (bug #15; John's ruling,
    /// 13 Jul 2026: "the status of a particular record by instanceID in the database is the
    /// source of truth for that book's state"). When a local folder exists, its meta.json
    /// bookInstanceId -- never its folder name -- decides which server row is this book:
    /// a checked-out book renamed locally stays bound to its own row, and a local book that
    /// merely shares a NAME with someone else's checked-in book must not wear that book's
    /// status. Name lookup applies only when there is no local folder (repo-name queries,
    /// e.g. names from GetBookList).
    /// </summary>
    [TestFixture]
    public class CloudIdentityFirstLookupTests
    {
        private const string kCollectionId = "22222222-2222-2222-2222-222222222222";
        private TemporaryFolder _collectionFolder;
        private Mock<ITeamCollectionManager> _mockTcManager;
        private CloudTeamCollection _collection;
        private FakeRestExecutor _executor;

        [SetUp]
        public void Setup()
        {
            _collectionFolder = new TemporaryFolder("CloudIdentityFirstLookupTests");
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
                transfer: new CloudBookTransfer(_ => new Mock<Amazon.S3.IAmazonS3>().Object)
            );

            // Hydrate the cache with one committed book, checked out by a TEAMMATE on another
            // machine: "The Moon and the Cap" / instance-moon (the live bug-#15 book).
            _executor.Handler = req =>
            {
                var body = new JObject
                {
                    ["books"] = new JArray(
                        new JObject
                        {
                            ["id"] = "book-moon",
                            ["instance_id"] = "instance-moon",
                            ["name"] = "The Moon and the Cap",
                            ["current_version_id"] = "v1",
                            ["current_version_seq"] = 1,
                            ["current_checksum"] = "cs1",
                            ["locked_by"] = "user-bob",
                            ["locked_by_email"] = "bob@dev.local",
                            ["locked_by_machine"] = "BOBS-MACHINE",
                            ["locked_at"] = "2026-07-13T20:15:38Z",
                            ["deleted_at"] = null,
                        }
                    ),
                    ["groups"] = new JArray(),
                    ["max_event_id"] = 1,
                };
                return FakeResponses.Make(HttpStatusCode.OK, body.ToString());
            };
            _collection.IsBookPresentInRepo("The Moon and the Cap"); // forces hydration
        }

        [TearDown]
        public void TearDown()
        {
            _collectionFolder.Dispose();
            TeamCollectionManager.ForceCurrentUserForTests(null);
        }

        /// <summary>Creates a local book folder with a meta.json carrying the given instance id
        /// (the identity every lookup should resolve by), plus the .htm that makes it look like a
        /// real Bloom book folder.</summary>
        private void MakeLocalBook(string folderName, string instanceId)
        {
            var folderPath = Path.Combine(_collectionFolder.FolderPath, folderName);
            Directory.CreateDirectory(folderPath);
            File.WriteAllText(
                Path.Combine(folderPath, "meta.json"),
                $"{{\"bookInstanceId\":\"{instanceId}\"}}"
            );
            File.WriteAllText(Path.Combine(folderPath, folderName + ".htm"), "<html/>");
        }

        [Test]
        public void RenamedCheckedOutBook_StillBindsToItsOwnRepoRow()
        {
            // Bob checked out "The Moon and the Cap" and retitled it; the local folder renamed
            // ahead of check-in. Identity (instance-moon) must keep it bound to its row.
            MakeLocalBook("Tetun moon and cap", "instance-moon");

            Assert.That(
                _collection.ResolveBookIdForTests("Tetun moon and cap"),
                Is.EqualTo("book-moon")
            );
            Assert.That(
                _collection.IsBookPresentInRepo("Tetun moon and cap"),
                Is.True,
                "the renamed folder is the same book, not a new local one"
            );
            Assert.That(
                _collection.WhoHasBookLocked("Tetun moon and cap"),
                Is.EqualTo("bob@dev.local"),
                "the checkout must survive the local rename (bug #15's lost avatar)"
            );
        }

        [Test]
        public void LocalBookSharingNameWithSomeoneElsesBook_DoesNotWearItsStatus()
        {
            // John's conflict scenario: while Alice was offline she created a book whose name
            // happens to match a book Bob checked in. Her book's own instance id is different,
            // so it must NOT report Bob's book's status as its own.
            MakeLocalBook("The Moon and the Cap", "instance-alices-own");

            Assert.That(
                _collection.ResolveBookIdForTests("The Moon and the Cap"),
                Is.Null,
                "a local book resolves by ITS identity; a name match with someone else's book is not a binding"
            );
            Assert.That(_collection.IsBookPresentInRepo("The Moon and the Cap"), Is.False);
            // Sanity: the repo row itself is untouched and still Bob's -- only the local folder's
            // binding changed. (A name query with no local folder still finds it; see next test.)
            var status = _collection.GetStatus("The Moon and the Cap");
            Assert.That(
                status.lockedBy,
                Is.Not.EqualTo("bob@dev.local"),
                "Alice's local book must not show as checked out to Bob"
            );
        }

        [Test]
        public void RepoBookWithNoLocalFolder_ResolvesByName()
        {
            // No local folder at all (e.g. a name straight from GetBookList during copy-down):
            // name lookup is the correct and only possible binding.
            Assert.That(
                _collection.ResolveBookIdForTests("The Moon and the Cap"),
                Is.EqualTo("book-moon")
            );
            Assert.That(_collection.IsBookPresentInRepo("The Moon and the Cap"), Is.True);
            Assert.That(
                _collection.WhoHasBookLocked("The Moon and the Cap"),
                Is.EqualTo("bob@dev.local")
            );
        }

        [Test]
        public void LocalFolderWithNoReadableId_DoesNotBindByName()
        {
            // Fail-safe: a local folder whose identity cannot be read must not guess by name --
            // it degrades to "local-only book" rather than wearing a repo book's status.
            var folderPath = Path.Combine(_collectionFolder.FolderPath, "The Moon and the Cap");
            Directory.CreateDirectory(folderPath); // no meta.json at all

            Assert.That(_collection.ResolveBookIdForTests("The Moon and the Cap"), Is.Null);
            Assert.That(_collection.IsBookPresentInRepo("The Moon and the Cap"), Is.False);
        }

        [Test]
        public void RenamedCheckedOutBook_KnownToHaveBeenDeletedConsultsItsOwnRow()
        {
            // KnownToHaveBeenDeleted also resolves by identity: the renamed local book asks about
            // ITS row (not deleted), not about a name that no longer matches anything.
            MakeLocalBook("Tetun moon and cap", "instance-moon");
            Assert.That(_collection.KnownToHaveBeenDeleted("Tetun moon and cap"), Is.False);
        }

        // Bug #18 (13 Jul 2026, John's live report): a teammate's rename arrives as a repo book
        // name with no local folder. The base rename-source heuristic ("has repo status => can't
        // be the rename source") is inverted under identity-first resolution, so the receiving
        // side downloaded the renamed book as a NEW book next to its old-name folder -- two local
        // folders with one instance id. The cloud override compares instance ids exactly.
        [Test]
        public void NewBookRenamedFrom_LocalFolderWithSameIdUnderOldName_IsDetected()
        {
            // The repo book is named "The Moon and the Cap" (instance-moon); locally it still
            // sits under the name the teammate renamed it FROM.
            MakeLocalBook("Old Local Name", "instance-moon");

            Assert.That(
                _collection.NewBookRenamedFrom("The Moon and the Cap"),
                Is.EqualTo("Old Local Name")
            );
        }

        [Test]
        public void NewBookRenamedFrom_NoLocalFolderWithThatId_ReturnsNull()
        {
            MakeLocalBook("Unrelated Book", "instance-unrelated");

            Assert.That(_collection.NewBookRenamedFrom("The Moon and the Cap"), Is.Null);
        }

        [Test]
        public void NewBookRenamedFrom_LocalFolderUnderTheSameName_IsNotARenameSource()
        {
            // The book already exists locally under the repo's own name: nothing was renamed.
            MakeLocalBook("The Moon and the Cap", "instance-moon");

            Assert.That(_collection.NewBookRenamedFrom("The Moon and the Cap"), Is.Null);
        }
    }
}
