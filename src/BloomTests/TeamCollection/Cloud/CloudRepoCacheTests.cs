using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bloom.TeamCollection.Cloud;
using BloomTemp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace BloomTests.TeamCollection.Cloud
{
    [TestFixture]
    public class CloudRepoCacheTests
    {
        private TemporaryFolder _collectionFolder;
        private string _collectionFolderPath;

        [SetUp]
        public void SetUp()
        {
            _collectionFolder = new TemporaryFolder("CloudRepoCacheTests");
            _collectionFolderPath = _collectionFolder.FolderPath;
        }

        [TearDown]
        public void TearDown()
        {
            _collectionFolder.Dispose();
        }

        private static JObject MakeBookRow(
            string id,
            string instanceId = "inst-1",
            string name = "Book",
            long? versionSeq = 1,
            string checksum = "chk-1",
            string lockedBy = null,
            string lockedByMachine = null,
            DateTime? deletedAt = null
        ) =>
            new JObject
            {
                ["id"] = id,
                ["instance_id"] = instanceId,
                ["name"] = name,
                ["current_version_id"] = versionSeq.HasValue ? "v-" + versionSeq : null,
                ["current_version_seq"] = versionSeq,
                ["current_checksum"] = checksum,
                ["locked_by"] = lockedBy,
                ["locked_by_machine"] = lockedByMachine,
                ["locked_at"] = null,
                ["deleted_at"] = deletedAt,
            };

        // ------------------------------------------------------------------
        // Full snapshot
        // ------------------------------------------------------------------

        [Test]
        public void ApplyFullSnapshot_PopulatesBooksGroupsAndCursor()
        {
            var cache = new CloudRepoCache(_collectionFolderPath);
            var state = new JObject
            {
                ["books"] = new JArray(MakeBookRow("book-1"), MakeBookRow("book-2")),
                ["groups"] = new JArray(
                    new JObject
                    {
                        ["group_key"] = "other",
                        ["version"] = 3,
                        ["updated_at"] = DateTime.UtcNow,
                    }
                ),
                ["max_event_id"] = 42,
            };

            cache.ApplyFullSnapshot(state);

            Assert.That(cache.GetAllBooks(), Has.Count.EqualTo(2), "sanity check on the fixture");
            Assert.That(cache.TryGetBook("book-1"), Is.Not.Null);
            Assert.That(cache.TryGetBook("book-1").Name, Is.EqualTo("Book"));
            Assert.That(cache.TryGetGroup("other").Version, Is.EqualTo(3));
            Assert.That(cache.LastSeenEventId, Is.EqualTo(42));
        }

        [Test]
        public void ApplyFullSnapshot_RemovesBooksNotInTheNewSnapshot()
        {
            var cache = new CloudRepoCache(_collectionFolderPath);
            cache.ApplyFullSnapshot(
                new JObject { ["books"] = new JArray(MakeBookRow("stale-book")) }
            );
            Assert.That(
                cache.TryGetBook("stale-book"),
                Is.Not.Null,
                "sanity check: it's there before the second snapshot"
            );

            cache.ApplyFullSnapshot(
                new JObject { ["books"] = new JArray(MakeBookRow("fresh-book")) }
            );

            Assert.That(
                cache.TryGetBook("stale-book"),
                Is.Null,
                "a full snapshot is authoritative for what exists"
            );
            Assert.That(cache.TryGetBook("fresh-book"), Is.Not.Null);
        }

        [Test]
        public void ApplyFullSnapshot_PreservesManifestForBooksStillPresent()
        {
            var cache = new CloudRepoCache(_collectionFolderPath);
            cache.ApplyFullSnapshot(new JObject { ["books"] = new JArray(MakeBookRow("book-1")) });
            var manifest = new BookVersionManifest(
                new Dictionary<string, BookVersionManifestEntry>
                {
                    ["book.htm"] = new BookVersionManifestEntry("sha", 1, "v1"),
                }
            );
            cache.RecordManifest("book-1", manifest);
            Assert.That(
                cache.TryGetBook("book-1").Manifest,
                Is.Not.Null,
                "sanity check before re-snapshotting"
            );

            cache.ApplyFullSnapshot(
                new JObject { ["books"] = new JArray(MakeBookRow("book-1", versionSeq: 2)) }
            );

            var afterBook = cache.TryGetBook("book-1");
            Assert.That(
                afterBook.CurrentVersionSeq,
                Is.EqualTo(2),
                "the server row's fields must still update"
            );
            Assert.That(
                afterBook.Manifest,
                Is.Not.Null,
                "manifest data (not carried on the row) must survive a re-snapshot"
            );
            Assert.That(afterBook.Manifest.Entries.Keys, Has.Member("book.htm"));
        }

        // ------------------------------------------------------------------
        // Delta application
        // ------------------------------------------------------------------

        [Test]
        public void ApplyDelta_UpsertsTouchedBooksAndAdvancesCursor_WithoutRemovingUntouchedOnes()
        {
            var cache = new CloudRepoCache(_collectionFolderPath);
            cache.ApplyFullSnapshot(
                new JObject
                {
                    ["books"] = new JArray(MakeBookRow("book-1", versionSeq: 1)),
                    ["max_event_id"] = 10,
                }
            );

            cache.ApplyDelta(
                new JObject
                {
                    ["books"] = new JArray(
                        MakeBookRow("book-1", versionSeq: 2),
                        MakeBookRow("book-2", versionSeq: 1)
                    ),
                    ["max_event_id"] = 15,
                }
            );

            Assert.That(
                cache.TryGetBook("book-1").CurrentVersionSeq,
                Is.EqualTo(2),
                "existing book updated"
            );
            Assert.That(
                cache.TryGetBook("book-2"),
                Is.Not.Null,
                "a book new to a delta (never seen before) is added"
            );
            Assert.That(cache.LastSeenEventId, Is.EqualTo(15));
        }

        [Test]
        public void ApplyDelta_TombstonedBook_KeepsRowButRecordsDeletedAt()
        {
            var cache = new CloudRepoCache(_collectionFolderPath);
            cache.ApplyFullSnapshot(new JObject { ["books"] = new JArray(MakeBookRow("book-1")) });

            var deletedAt = DateTime.UtcNow;
            cache.ApplyDelta(
                new JObject { ["books"] = new JArray(MakeBookRow("book-1", deletedAt: deletedAt)) }
            );

            var book = cache.TryGetBook("book-1");
            Assert.That(
                book,
                Is.Not.Null,
                "delta never removes a row; tombstoning is via deleted_at"
            );
            Assert.That(book.DeletedAt, Is.Not.Null);
        }

        // ------------------------------------------------------------------
        // Cursor monotonicity
        // ------------------------------------------------------------------

        [Test]
        public void Cursor_NeverGoesBackward()
        {
            var cache = new CloudRepoCache(_collectionFolderPath);
            cache.ApplyDelta(new JObject { ["max_event_id"] = 20 });
            Assert.That(cache.LastSeenEventId, Is.EqualTo(20), "sanity check");

            cache.ApplyDelta(new JObject { ["max_event_id"] = 5 }); // stale/out-of-order response

            Assert.That(
                cache.LastSeenEventId,
                Is.EqualTo(20),
                "a smaller cursor value must be ignored"
            );
        }

        [Test]
        public void Cursor_NullMaxEventId_LeavesCursorUnchanged()
        {
            var cache = new CloudRepoCache(_collectionFolderPath);
            cache.ApplyDelta(new JObject { ["max_event_id"] = 7 });

            cache.ApplyDelta(new JObject { ["max_event_id"] = null }); // e.g. get_changes with no new events

            Assert.That(cache.LastSeenEventId, Is.EqualTo(7));
        }

        // ------------------------------------------------------------------
        // Write-through
        // ------------------------------------------------------------------

        [Test]
        public void RecordCheckoutResult_UpdatesLockFieldsRegardlessOfSuccess()
        {
            var cache = new CloudRepoCache(_collectionFolderPath);
            cache.ApplyFullSnapshot(new JObject { ["books"] = new JArray(MakeBookRow("book-1")) });

            cache.RecordCheckoutResult(
                "book-1",
                new JObject
                {
                    ["success"] = false,
                    ["locked_by"] = "other@example.com",
                    ["locked_by_machine"] = "OtherMachine",
                    ["locked_at"] = DateTime.UtcNow,
                }
            );

            var book = cache.TryGetBook("book-1");
            Assert.That(
                book.LockedBy,
                Is.EqualTo("other@example.com"),
                "even a failed checkout tells us who holds the lock"
            );
            Assert.That(book.LockedByMachine, Is.EqualTo("OtherMachine"));
        }

        [Test]
        public void RecordUnlock_ClearsLockFields()
        {
            var cache = new CloudRepoCache(_collectionFolderPath);
            cache.ApplyFullSnapshot(
                new JObject
                {
                    ["books"] = new JArray(
                        MakeBookRow(
                            "book-1",
                            lockedBy: "me@example.com",
                            lockedByMachine: "MyMachine"
                        )
                    ),
                }
            );
            Assert.That(
                cache.TryGetBook("book-1").LockedBy,
                Is.EqualTo("me@example.com"),
                "sanity check"
            );

            cache.RecordUnlock("book-1");

            var book = cache.TryGetBook("book-1");
            Assert.That(book.LockedBy, Is.Null);
            Assert.That(book.LockedByMachine, Is.Null);
        }

        [Test]
        public void RecordCheckinFinish_NewBook_AddsRowAndStoresManifest()
        {
            var cache = new CloudRepoCache(_collectionFolderPath);
            var manifest = new BookVersionManifest(
                new Dictionary<string, BookVersionManifestEntry>
                {
                    ["book.htm"] = new BookVersionManifestEntry("sha", 1, "v1"),
                }
            );

            cache.RecordCheckinFinish(
                "new-book-id",
                "inst-9",
                "New Book",
                "version-9",
                1,
                "checksum-9",
                manifest,
                keptCheckedOut: false,
                lockedByEmail: null,
                lockedByMachine: null
            );

            var book = cache.TryGetBook("new-book-id");
            Assert.That(
                book,
                Is.Not.Null,
                "checkin-finish for a bookId:null Send must create the row"
            );
            Assert.That(book.InstanceId, Is.EqualTo("inst-9"));
            Assert.That(book.CurrentVersionSeq, Is.EqualTo(1));
            Assert.That(book.Manifest.Entries.Keys, Has.Member("book.htm"));
            Assert.That(book.LockedBy, Is.Null, "keptCheckedOut=false must release the lock");
        }

        [Test]
        public void RecordCheckinFinish_KeepCheckedOut_KeepsLock()
        {
            var cache = new CloudRepoCache(_collectionFolderPath);

            cache.RecordCheckinFinish(
                "book-1",
                "inst-1",
                "Book",
                "version-1",
                1,
                "checksum-1",
                new BookVersionManifest(),
                keptCheckedOut: true,
                lockedByEmail: "me@example.com",
                lockedByMachine: "MyMachine"
            );

            var book = cache.TryGetBook("book-1");
            Assert.That(book.LockedBy, Is.EqualTo("me@example.com"));
            Assert.That(book.LockedByMachine, Is.EqualTo("MyMachine"));
        }

        // ------------------------------------------------------------------
        // Snapshot persistence round-trip
        // ------------------------------------------------------------------

        [Test]
        public void SaveThenLoadOrCreate_RoundTripsBooksGroupsAndCursor()
        {
            var cache = new CloudRepoCache(_collectionFolderPath);
            var manifest = new BookVersionManifest(
                new Dictionary<string, BookVersionManifestEntry>
                {
                    ["book.htm"] = new BookVersionManifestEntry("sha-1", 100, "v-1"),
                }
            );
            cache.ApplyFullSnapshot(
                new JObject
                {
                    ["books"] = new JArray(MakeBookRow("book-1", versionSeq: 3)),
                    ["groups"] = new JArray(
                        new JObject
                        {
                            ["group_key"] = "other",
                            ["version"] = 2,
                            ["updated_at"] = DateTime.UtcNow,
                        }
                    ),
                    ["max_event_id"] = 99,
                }
            );
            cache.RecordManifest("book-1", manifest);

            cache.Save();
            Assert.That(
                File.Exists(cache.SnapshotPath),
                Is.True,
                "sanity check: Save actually wrote the file"
            );

            var reloaded = CloudRepoCache.LoadOrCreate(_collectionFolderPath);

            Assert.That(reloaded.LastSeenEventId, Is.EqualTo(99));
            var book = reloaded.TryGetBook("book-1");
            Assert.That(book, Is.Not.Null);
            Assert.That(book.CurrentVersionSeq, Is.EqualTo(3));
            Assert.That(book.Manifest, Is.Not.Null);
            Assert.That(book.Manifest.Entries["book.htm"].S3VersionId, Is.EqualTo("v-1"));
            Assert.That(reloaded.TryGetGroup("other").Version, Is.EqualTo(2));
        }

        [Test]
        public void LoadOrCreate_NoSnapshotFile_ReturnsEmptyCache()
        {
            var cache = CloudRepoCache.LoadOrCreate(_collectionFolderPath);

            Assert.That(cache.LastSeenEventId, Is.EqualTo(0));
            Assert.That(cache.GetAllBooks(), Is.Empty);
        }

        [Test]
        public void LoadOrCreate_CorruptSnapshotFile_ReturnsEmptyCacheRatherThanThrowing()
        {
            Directory.CreateDirectory(_collectionFolderPath);
            File.WriteAllText(
                Path.Combine(_collectionFolderPath, CloudRepoCache.SnapshotFileName),
                "{ not valid json"
            );

            CloudRepoCache cache = null;
            Assert.DoesNotThrow(() => cache = CloudRepoCache.LoadOrCreate(_collectionFolderPath));
            Assert.That(cache.GetAllBooks(), Is.Empty);
        }

        [Test]
        public void SnapshotFileName_IsNotMatchedByRootLevelCollectionFileWhitelist()
        {
            // TeamCollection.RootLevelCollectionFilesIn only picks up specific named files
            // (the .bloomCollection file, customCollectionStyles.css, configuration.txt, and
            // ReaderTools*.json). The cache file must not accidentally match that last glob.
            Assert.That(
                CloudRepoCache.SnapshotFileName,
                Does.Not.StartWith("ReaderTools"),
                "must not be swept up as a collection-settings file to sync to the repo"
            );
        }

        // ------------------------------------------------------------------
        // Concurrency
        // ------------------------------------------------------------------

        [Test]
        public void ConcurrentReadsAndWrites_DoNotCorruptOrThrow()
        {
            var cache = new CloudRepoCache(_collectionFolderPath);
            const int bookCount = 20;
            const int iterationsPerBook = 25;

            var writers = Enumerable
                .Range(0, bookCount)
                .Select(i =>
                    Task.Run(() =>
                    {
                        var bookId = "book-" + i;
                        for (var iteration = 0; iteration < iterationsPerBook; iteration++)
                        {
                            cache.ApplyDelta(
                                new JObject
                                {
                                    ["books"] = new JArray(
                                        MakeBookRow(bookId, versionSeq: iteration)
                                    ),
                                    ["max_event_id"] = i * iterationsPerBook + iteration,
                                }
                            );
                            cache.RecordCheckoutResult(
                                bookId,
                                new JObject
                                {
                                    ["locked_by"] = "user@example.com",
                                    ["locked_by_machine"] = "M",
                                    ["locked_at"] = DateTime.UtcNow,
                                }
                            );
                            cache.RecordUnlock(bookId);
                        }
                    })
                )
                .ToArray();

            var readers = Enumerable
                .Range(0, 10)
                .Select(_ =>
                    Task.Run(() =>
                    {
                        for (var iteration = 0; iteration < iterationsPerBook; iteration++)
                        {
                            // Must never throw (e.g. a torn read of the dictionary) and must never
                            // return a partially-constructed row.
                            foreach (var book in cache.GetAllBooks())
                            {
                                Assert.That(book.Id, Is.Not.Null);
                            }
                            var _ = cache.LastSeenEventId;
                        }
                    })
                )
                .ToArray();

            Assert.DoesNotThrow(() => Task.WaitAll(writers.Concat(readers).ToArray()));

            Assert.That(
                cache.GetAllBooks(),
                Has.Count.EqualTo(bookCount),
                "every book's writer thread must have registered its row exactly once"
            );
            foreach (var book in cache.GetAllBooks())
            {
                Assert.That(
                    book.CurrentVersionSeq,
                    Is.EqualTo(iterationsPerBook - 1),
                    "last write for each book should win"
                );
                Assert.That(book.LockedBy, Is.Null, "each writer's loop ends with an unlock");
            }
        }
    }
}
