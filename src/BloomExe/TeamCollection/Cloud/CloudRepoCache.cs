using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.TeamCollection.Cloud
{
    /// <summary>
    /// One book row as reported by the server (tc.books, per CONTRACTS.md's get_collection_state /
    /// get_changes shapes: id/instance_id/name/current_version_id/current_version_seq/
    /// current_checksum/locked_by/locked_by_machine/locked_at/deleted_at[/created_at/created_by]).
    /// This is a read-through CACHE of server state for cheap synchronous status queries (e.g.
    /// rendering the book list's lock icons) — it is never consulted to decide whether a lock or
    /// mutation is allowed; every RPC that changes state re-validates against the server's live row
    /// regardless of what this says.
    /// </summary>
    public class CloudCachedBook
    {
        public string Id; // tc.books.id (uuid)
        public string InstanceId;
        public string Name;
        public string CurrentVersionId; // uuid; null until the book's first checkin-finish
        public long? CurrentVersionSeq;
        public string CurrentChecksum;
        public string LockedBy; // email; null when not checked out
        public string LockedByMachine;
        public DateTime? LockedAt;
        public DateTime? DeletedAt;
        public DateTime? CreatedAt;
        public string CreatedBy;

        /// <summary>Display-friendly resolution of <see cref="LockedBy"/> (task 06's
        /// 20260707000006 migration: tc.resolve_member_display, joined server-side since
        /// LockedBy is the raw auth user id, not an email). Null when not locked or when the
        /// server doesn't know a display name (common in dev-auth mode).</summary>
        public string LockedByEmail;
        public string LockedByDisplayName;

        /// <summary>
        /// The version seq of what is CURRENTLY on this machine's disk for this book, as of the
        /// last successful Send or Receive (task 06, CONTRACTS.md's book-status JSON
        /// "localVersionSeq"). Null if this book has never been fetched/sent from this machine
        /// this cache has known about -- e.g. a book a teammate created that we haven't Received
        /// yet. Deliberately NOT touched by <see cref="ApplyServerRow"/> (which only carries
        /// repo-side truth); only <see cref="CloudRepoCache.RecordLocalVersionSeq"/> writes it, so
        /// it survives repeated polling/hydration untouched until we actually move bytes.
        /// </summary>
        public long? LocalVersionSeq;

        /// <summary>
        /// The book's current per-file manifest (path → sha256/size/s3VersionId), once known.
        /// CONTRACTS.md v1.1 does not yet define an RPC that returns a book's per-file manifest (only
        /// the aggregate current_checksum/current_version_seq come back from get_collection_state /
        /// get_changes) — see the "contract gap" note in task 04's final report. Callers populate this
        /// via <see cref="CloudRepoCache.RecordManifest"/> whenever they obtain it by some other means
        /// (e.g. right after their own checkin-finish, since they already built the manifest to drive
        /// the upload). May be null.
        /// </summary>
        public BookVersionManifest Manifest;

        /// <summary>Deep-enough copy for handing out of the cache (the manifest itself is treated as
        /// immutable once built — see <see cref="BookVersionManifest"/> — so sharing the reference is
        /// safe).</summary>
        internal CloudCachedBook Clone() =>
            new CloudCachedBook
            {
                Id = Id,
                InstanceId = InstanceId,
                Name = Name,
                CurrentVersionId = CurrentVersionId,
                CurrentVersionSeq = CurrentVersionSeq,
                CurrentChecksum = CurrentChecksum,
                LockedBy = LockedBy,
                LockedByMachine = LockedByMachine,
                LockedAt = LockedAt,
                DeletedAt = DeletedAt,
                CreatedAt = CreatedAt,
                CreatedBy = CreatedBy,
                LockedByEmail = LockedByEmail,
                LockedByDisplayName = LockedByDisplayName,
                LocalVersionSeq = LocalVersionSeq,
                Manifest = Manifest,
            };

        /// <summary>Applies the fields present in a server book row (get_collection_state /
        /// get_changes shape). Leaves <see cref="Manifest"/> untouched — the server row never carries
        /// it (see the field's own doc comment).</summary>
        internal void ApplyServerRow(JObject row)
        {
            Id = (string)row["id"];
            InstanceId = (string)row["instance_id"];
            Name = (string)row["name"];
            CurrentVersionId = (string)row["current_version_id"];
            CurrentVersionSeq = (long?)row["current_version_seq"];
            CurrentChecksum = (string)row["current_checksum"];
            LockedBy = (string)row["locked_by"];
            LockedByMachine = (string)row["locked_by_machine"];
            LockedAt = (DateTime?)row["locked_at"];
            DeletedAt = (DateTime?)row["deleted_at"];
            if (row["created_at"] != null)
                CreatedAt = (DateTime?)row["created_at"];
            if (row["created_by"] != null)
                CreatedBy = (string)row["created_by"];
            // Present since the 20260707000006 migration; older cached snapshots / server
            // versions simply leave these null, which is exactly "no display name available".
            LockedByEmail = (string)row["locked_by_email"];
            LockedByDisplayName = (string)row["locked_by_name"];
        }

        internal JObject ToSnapshotJson() =>
            new JObject
            {
                ["id"] = Id,
                ["instanceId"] = InstanceId,
                ["name"] = Name,
                ["currentVersionId"] = CurrentVersionId,
                ["currentVersionSeq"] = CurrentVersionSeq,
                ["currentChecksum"] = CurrentChecksum,
                ["lockedBy"] = LockedBy,
                ["lockedByMachine"] = LockedByMachine,
                ["lockedAt"] = LockedAt,
                ["deletedAt"] = DeletedAt,
                ["createdAt"] = CreatedAt,
                ["createdBy"] = CreatedBy,
                ["lockedByEmail"] = LockedByEmail,
                ["lockedByDisplayName"] = LockedByDisplayName,
                ["localVersionSeq"] = LocalVersionSeq,
                ["manifest"] = Manifest?.ToJson(),
            };

        internal static CloudCachedBook FromSnapshotJson(JObject json) =>
            new CloudCachedBook
            {
                Id = (string)json["id"],
                InstanceId = (string)json["instanceId"],
                Name = (string)json["name"],
                CurrentVersionId = (string)json["currentVersionId"],
                CurrentVersionSeq = (long?)json["currentVersionSeq"],
                CurrentChecksum = (string)json["currentChecksum"],
                LockedBy = (string)json["lockedBy"],
                LockedByMachine = (string)json["lockedByMachine"],
                LockedAt = (DateTime?)json["lockedAt"],
                DeletedAt = (DateTime?)json["deletedAt"],
                CreatedAt = (DateTime?)json["createdAt"],
                CreatedBy = (string)json["createdBy"],
                LockedByEmail = (string)json["lockedByEmail"],
                LockedByDisplayName = (string)json["lockedByDisplayName"],
                LocalVersionSeq = (long?)json["localVersionSeq"],
                Manifest = json["manifest"] is JArray filesArray
                    ? BookVersionManifest.FromJson(filesArray)
                    : null,
            };
    }

    /// <summary>One collection-file group's version (tc.collection_file_groups), per CONTRACTS.md's
    /// collection-files-start/finish two-phase protocol.</summary>
    public class CloudCachedCollectionFileGroup
    {
        public string GroupKey; // 'other' | 'allowed-words' | 'sample-texts'
        public long Version;
        public DateTime? UpdatedAt;

        internal void ApplyServerRow(JObject row)
        {
            GroupKey = (string)row["group_key"];
            Version = (long)row["version"];
            UpdatedAt = (DateTime?)row["updated_at"];
        }

        internal JObject ToSnapshotJson() =>
            new JObject
            {
                ["groupKey"] = GroupKey,
                ["version"] = Version,
                ["updatedAt"] = UpdatedAt,
            };

        internal static CloudCachedCollectionFileGroup FromSnapshotJson(JObject json) =>
            new CloudCachedCollectionFileGroup
            {
                GroupKey = (string)json["groupKey"],
                Version = (long)json["version"],
                UpdatedAt = (DateTime?)json["updatedAt"],
            };
    }

    /// <summary>
    /// Thread-safe, persisted cache of one cloud collection's server-reported state: the book/lock/
    /// version map, collection-file group versions, and the `last_seen_event_id` cursor
    /// (CONTRACTS.md Realtime section). Makes the synchronous status calls the rest of TeamCollection
    /// needs (e.g. "is this book locked, by whom") cheap, and hydrates Disconnected mode from the
    /// last snapshot when offline (design doc: "CloudRepoCache ... hydrates Disconnected mode").
    /// Populated ONLY by (a) applying a full/delta snapshot from get_collection_state/get_changes, or
    /// (b) write-through from this client's own successful mutating RPC calls. Never trusted to
    /// authorize a mutation — every state-changing RPC re-validates against the server's live row.
    /// </summary>
    public class CloudRepoCache
    {
        /// <summary>
        /// Filename of the persisted snapshot in the local collection folder. Deliberately not
        /// matched by TeamCollection.RootLevelCollectionFilesIn's whitelist (so it's not swept up as
        /// a collection-settings file), and — being a plain file, not a folder — automatically
        /// invisible to whatever enumerates book subfolders.
        /// </summary>
        public const string SnapshotFileName = ".bloom-cloud-repo-cache.json";

        private const int CurrentSnapshotVersion = 1;

        private readonly object _gate = new object();
        private readonly Dictionary<string, CloudCachedBook> _booksById = new Dictionary<
            string,
            CloudCachedBook
        >(StringComparer.Ordinal);
        private readonly Dictionary<string, CloudCachedCollectionFileGroup> _groupsByKey =
            new Dictionary<string, CloudCachedCollectionFileGroup>(StringComparer.Ordinal);
        private long _lastSeenEventId;

        /// <summary>Path to this cache's persisted snapshot file (local collection folder +
        /// <see cref="SnapshotFileName"/>).</summary>
        public string SnapshotPath { get; }

        public CloudRepoCache(string localCollectionFolder)
        {
            SnapshotPath = Path.Combine(localCollectionFolder, SnapshotFileName);
        }

        /// <summary>
        /// The polling/reconnect cursor (CONTRACTS.md Realtime section: "Clients persist
        /// last_seen_event_id"): the highest event id incorporated so far. Advances monotonically —
        /// a smaller or equal incoming value (e.g. a duplicate or out-of-order response) is ignored.
        /// </summary>
        public long LastSeenEventId
        {
            get
            {
                lock (_gate)
                    return _lastSeenEventId;
            }
        }

        /// <summary>
        /// Replaces the ENTIRE book/group map from a full get_collection_state(since_event_id: null)
        /// response (`{books:[...], groups:[...], max_event_id}`, all snake_case row fields per the
        /// SQL function). Books/groups not present in the snapshot are dropped — a full snapshot is
        /// authoritative for "what currently exists" (deleted-but-tombstoned books still appear, with
        /// deleted_at set; a book that's gone from the snapshot entirely no longer exists or is no
        /// longer visible to this member). Any already-cached <see cref="CloudCachedBook.Manifest"/>
        /// is preserved across the swap for books that are still present, since the snapshot itself
        /// carries no manifest data (see that field's doc comment).
        /// </summary>
        public void ApplyFullSnapshot(JObject state)
        {
            lock (_gate)
            {
                var oldManifests = _booksById.ToDictionary(kv => kv.Key, kv => kv.Value.Manifest);
                _booksById.Clear();
                foreach (var row in AsObjectArray(state["books"]))
                {
                    var book = new CloudCachedBook();
                    book.ApplyServerRow(row);
                    if (oldManifests.TryGetValue(book.Id, out var manifest))
                        book.Manifest = manifest;
                    _booksById[book.Id] = book;
                }

                _groupsByKey.Clear();
                foreach (var row in AsObjectArray(state["groups"]))
                {
                    var group = new CloudCachedCollectionFileGroup();
                    group.ApplyServerRow(row);
                    _groupsByKey[group.GroupKey] = group;
                }

                AdvanceCursorLocked(state["max_event_id"]);
            }
        }

        /// <summary>
        /// Merges a delta response — either get_collection_state(since_event_id: N)'s `books` array
        /// (only books touched since the cursor; everything else is untouched) or get_changes'
        /// `books` (the touched-book rows accompanying its `events` list — the event log itself is
        /// History-tab material, out of this cache's scope per the task file). Upserts each row (a
        /// book id not already cached is added — e.g. a brand-new book just Sent by a teammate);
        /// never removes a book, since unlike a full snapshot a delta doesn't enumerate "everything
        /// that still exists" — tombstoning is via `deleted_at`, present on the row itself.
        /// </summary>
        public void ApplyDelta(JObject changes)
        {
            lock (_gate)
            {
                foreach (var row in AsObjectArray(changes["books"]))
                {
                    var id = (string)row["id"];
                    if (!_booksById.TryGetValue(id, out var book))
                    {
                        book = new CloudCachedBook();
                        _booksById[id] = book;
                    }
                    book.ApplyServerRow(row);
                }

                AdvanceCursorLocked(changes["max_event_id"]);
            }
        }

        private static IEnumerable<JObject> AsObjectArray(JToken token) =>
            (token as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>();

        // Caller must already hold _gate.
        private void AdvanceCursorLocked(JToken maxEventIdToken)
        {
            if (maxEventIdToken == null || maxEventIdToken.Type == JTokenType.Null)
                return;
            var maxEventId = (long)maxEventIdToken;
            if (maxEventId > _lastSeenEventId)
                _lastSeenEventId = maxEventId;
        }

        /// <summary>
        /// Write-through for a checkout_book RPC result (CONTRACTS.md: `{success, locked_by,
        /// locked_by_machine, locked_at}` — present whether or not `success` is true, since a failed
        /// checkout still reports who currently holds the lock). Updates the cached lock fields
        /// immediately rather than waiting for the next poll/snapshot; never itself decides whether a
        /// checkout is allowed (the RPC already did that).
        /// </summary>
        public void RecordCheckoutResult(string bookId, JObject checkoutResult)
        {
            lock (_gate)
            {
                var book = GetOrAddLocked(bookId);
                book.LockedBy = (string)checkoutResult["locked_by"];
                book.LockedByMachine = (string)checkoutResult["locked_by_machine"];
                book.LockedAt = (DateTime?)checkoutResult["locked_at"];
            }
        }

        /// <summary>Write-through for a successful unlock_book/force_unlock RPC: both simply clear
        /// the lock (the difference between them is server-side auditing only).</summary>
        public void RecordUnlock(string bookId)
        {
            lock (_gate)
            {
                if (_booksById.TryGetValue(bookId, out var book))
                {
                    book.LockedBy = null;
                    book.LockedByMachine = null;
                    book.LockedAt = null;
                }
            }
        }

        /// <summary>
        /// Write-through for a successful checkin-finish (CONTRACTS.md: `{versionId, seq}`), plus the
        /// manifest the client just committed (it already has this in hand — it built it to drive the
        /// upload). Adds the book if this was its first-ever commit (the checkin-start `bookId:null`
        /// path) — <paramref name="instanceId"/> and <paramref name="name"/> are only needed then.
        /// </summary>
        public void RecordCheckinFinish(
            string bookId,
            string instanceId,
            string name,
            string versionId,
            long versionSeq,
            string checksum,
            BookVersionManifest manifest,
            bool keptCheckedOut,
            string lockedByEmail,
            string lockedByMachine
        )
        {
            lock (_gate)
            {
                var book = GetOrAddLocked(bookId);
                if (book.InstanceId == null)
                    book.InstanceId = instanceId;
                if (book.Name == null)
                    book.Name = name;
                book.CurrentVersionId = versionId;
                book.CurrentVersionSeq = versionSeq;
                book.CurrentChecksum = checksum;
                book.Manifest = manifest;
                if (keptCheckedOut)
                {
                    book.LockedBy = lockedByEmail;
                    book.LockedByMachine = lockedByMachine;
                }
                else
                {
                    book.LockedBy = null;
                    book.LockedByMachine = null;
                    book.LockedAt = null;
                }
            }
        }

        /// <summary>
        /// Write-through: records the version seq now actually present on THIS machine's disk for
        /// a book, immediately after a successful Send or Receive (task 06, book-status JSON's
        /// "localVersionSeq"). Adds the book row if it isn't cached yet (shouldn't normally happen,
        /// since a Send/Receive implies we already know the book id, but mirrors the other
        /// write-through methods' defensiveness).
        /// </summary>
        public void RecordLocalVersionSeq(string bookId, long versionSeq)
        {
            lock (_gate)
            {
                var book = GetOrAddLocked(bookId);
                book.LocalVersionSeq = versionSeq;
            }
        }

        /// <summary>Write-through: record a book's current manifest once obtained by whatever means
        /// (see <see cref="CloudCachedBook.Manifest"/>'s doc comment on the contract gap this covers
        /// for). No-op if the book id isn't cached yet.</summary>
        public void RecordManifest(string bookId, BookVersionManifest manifest)
        {
            lock (_gate)
            {
                if (_booksById.TryGetValue(bookId, out var book))
                    book.Manifest = manifest;
            }
        }

        /// <summary>Write-through for a successful collection-files-finish: bumps one group's cached
        /// version (CONTRACTS.md's two-phase collection-files protocol).</summary>
        public void RecordCollectionFilesFinish(string groupKey, long newVersion)
        {
            lock (_gate)
            {
                if (!_groupsByKey.TryGetValue(groupKey, out var group))
                {
                    group = new CloudCachedCollectionFileGroup { GroupKey = groupKey };
                    _groupsByKey[groupKey] = group;
                }
                group.Version = newVersion;
                group.UpdatedAt = DateTime.UtcNow;
            }
        }

        // Caller must already hold _gate.
        private CloudCachedBook GetOrAddLocked(string bookId)
        {
            if (!_booksById.TryGetValue(bookId, out var book))
            {
                book = new CloudCachedBook { Id = bookId };
                _booksById[bookId] = book;
            }
            return book;
        }

        /// <summary>Returns a snapshot copy of the cached row for <paramref name="bookId"/>, or null
        /// if not cached. A copy, not a live reference, so callers can't mutate cache state by
        /// accident and don't need to hold any lock while reading it.</summary>
        public CloudCachedBook TryGetBook(string bookId)
        {
            lock (_gate)
            {
                return _booksById.TryGetValue(bookId, out var book) ? book.Clone() : null;
            }
        }

        /// <summary>Returns a snapshot copy of every cached book row.</summary>
        public IReadOnlyList<CloudCachedBook> GetAllBooks()
        {
            lock (_gate)
            {
                return _booksById.Values.Select(b => b.Clone()).ToList();
            }
        }

        /// <summary>Returns a snapshot copy of the cached version for a collection-file group, or
        /// null if not cached.</summary>
        public CloudCachedCollectionFileGroup TryGetGroup(string groupKey)
        {
            lock (_gate)
            {
                return _groupsByKey.TryGetValue(groupKey, out var group)
                    ? new CloudCachedCollectionFileGroup
                    {
                        GroupKey = group.GroupKey,
                        Version = group.Version,
                        UpdatedAt = group.UpdatedAt,
                    }
                    : null;
            }
        }

        /// <summary>
        /// Serializes the full cache to <see cref="SnapshotPath"/> via a staged-temp-then-atomic-swap
        /// write, so a crash mid-write never corrupts the previous snapshot (a reader always sees
        /// either the complete old file or the complete new one, never a partial one).
        /// </summary>
        public void Save()
        {
            JObject json;
            lock (_gate)
            {
                json = new JObject
                {
                    ["version"] = CurrentSnapshotVersion,
                    ["lastSeenEventId"] = _lastSeenEventId,
                    ["books"] = new JArray(_booksById.Values.Select(b => b.ToSnapshotJson())),
                    ["groups"] = new JArray(_groupsByKey.Values.Select(g => g.ToSnapshotJson())),
                };
            }

            var tempPath = SnapshotPath + ".tmp";
            RobustFile.WriteAllText(tempPath, json.ToString(), Encoding.UTF8);
            if (RobustFile.Exists(SnapshotPath))
                RobustFile.Delete(SnapshotPath);
            RobustFile.Move(tempPath, SnapshotPath);
        }

        /// <summary>
        /// Loads a previously-saved snapshot from <paramref name="localCollectionFolder"/>, or
        /// returns an empty cache (cursor 0, no books) if none exists yet or it can't be parsed — a
        /// missing/corrupt cache file is not fatal, it just means the next full get_collection_state
        /// call rebuilds it from scratch.
        /// </summary>
        public static CloudRepoCache LoadOrCreate(string localCollectionFolder)
        {
            var cache = new CloudRepoCache(localCollectionFolder);
            if (!RobustFile.Exists(cache.SnapshotPath))
                return cache;

            try
            {
                var json = JObject.Parse(RobustFile.ReadAllText(cache.SnapshotPath, Encoding.UTF8));
                cache._lastSeenEventId = (long?)json["lastSeenEventId"] ?? 0;
                foreach (var row in AsObjectArray(json["books"]))
                {
                    var book = CloudCachedBook.FromSnapshotJson(row);
                    cache._booksById[book.Id] = book;
                }
                foreach (var row in AsObjectArray(json["groups"]))
                {
                    var group = CloudCachedCollectionFileGroup.FromSnapshotJson(row);
                    cache._groupsByKey[group.GroupKey] = group;
                }
            }
            catch (Exception e)
            {
                Logger.WriteEvent(
                    "CloudRepoCache: failed to load snapshot at "
                        + cache.SnapshotPath
                        + ": "
                        + e.Message
                );
            }

            return cache;
        }
    }
}
