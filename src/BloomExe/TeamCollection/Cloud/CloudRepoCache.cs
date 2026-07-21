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

        // --- Version tracking: these three answer "which version is in the repo, and is my
        // on-disk copy behind it?". CurrentVersionId is IDENTITY (which exact version),
        // CurrentVersionSeq is the repo's ORDER (how new), LocalVersionSeq is this machine's ORDER
        // (how new a copy I have on disk). ---

        /// <summary>UUID of the book's current committed version (tc.versions.id; null until the
        /// first checkin-finish). Version IDENTITY, not order: it names one exact version, so the
        /// client sends it to checkin-start as the base version and the server rejects an edit made
        /// from a stale base (409 conflict). UUIDs aren't orderable — for "newer/older" use
        /// <see cref="CurrentVersionSeq"/>. Always set together with it.</summary>
        public string CurrentVersionId;

        /// <summary>The current committed version's per-book ORDINAL (tc.versions.seq: 1, 2, 3…;
        /// null until the first checkin-finish). Version ORDER, not identity: a plain integer for
        /// cheap "is the repo ahead of my disk?" comparisons against <see cref="LocalVersionSeq"/>
        /// and for cross-poll change detection. HasValue also doubles as "this book exists in the
        /// repo" (a never-committed local-only book is null). Unique only per book, so it is no
        /// substitute for the UUID <see cref="CurrentVersionId"/> as an identity/FK. Set from the
        /// server row on every poll, and from the returned seq right after our own
        /// checkin-finish.</summary>
        public long? CurrentVersionSeq;

        /// <summary>
        /// The version seq (SAME per-book numbering as <see cref="CurrentVersionSeq"/>) of what is
        /// CURRENTLY on THIS machine's disk for this book, as of its last successful Send or Receive
        /// (book-status JSON's "localVersionSeq"). Compared directly with CurrentVersionSeq: equal
        /// ⇒ up to date; local &lt; current ⇒ the repo has a newer version to Receive; it cannot
        /// legitimately exceed current (committing makes our version the current one, so a Send sets
        /// both equal). Null if this machine has never fetched/sent this book — e.g. a teammate's
        /// book we haven't Received yet. There is deliberately no "LocalVersionId": the local side
        /// only needs the ordinal for the behind/ahead comparison, never a version identity.
        ///
        /// This is THIS-MACHINE-ONLY state: deliberately NOT touched by <see cref="ApplyServerRow"/>
        /// (which carries only repo-side truth); only
        /// <see cref="CloudRepoCache.RecordLocalVersionSeq"/> writes it, and it is preserved across
        /// polls/full snapshots so a resync doesn't make every already-downloaded book look
        /// out-of-date.
        ///
        /// A word on "Local": here it is the THIS-MACHINE'S-WORKING-COPY sense (the bytes in the
        /// book folder on disk), contrasted with the repo/server version — NOT the "local backend"
        /// sense of <see cref="CloudAuthMode.Local"/> (a local Supabase + MinIO stack). Both senses
        /// of "local" live in this feature; every "Local"/"local copy"/"localVersionSeq" on the
        /// cache and collection side is this collection-copy one.
        /// </summary>
        public long? LocalVersionSeq;

        public string CurrentChecksum;

        /// <summary>The lock owner's RAW AUTH USER ID (JWT `sub`, = tc.books.locked_by), not an
        /// email; null when not checked out. This is the authoritative, always-present-when-locked
        /// identity — it is what "is this checked out?" tests, cross-poll change detection, and the
        /// display fallback rely on. For a human-readable form use <see cref="LockedByEmail"/> /
        /// <see cref="LockedByDisplayName"/> (which are only best-effort and may be null even while
        /// this is set). Set from the server row (get_collection_state/get_changes), the
        /// checkout_book RPC result, and <see cref="RecordCheckinFinish"/> — all the user id.</summary>
        public string LockedBy;
        public string LockedByMachine;

        /// <summary>Which local copy of the collection ("seat", 20260711000003: a stable hash of
        /// the local collection folder path) holds the lock. Null = unknown (legacy lock, or one
        /// acquired via checkin_start_tx's take-if-free path); a null seat can never be taken
        /// over (fail-safe) — see CloudTeamCollection.SeatId and bug #0 in the batch doc.</summary>
        public string LockedSeat;
        public DateTime? LockedAt;
        public DateTime? DeletedAt;
        public DateTime? CreatedAt;
        public string CreatedBy;

        /// <summary>Best-effort human-readable resolution of <see cref="LockedBy"/> (task 06's
        /// 20260707000006 migration: tc.resolve_member_display, joined server-side since LockedBy
        /// is the raw auth user id, not an email). DISPLAY ONLY — null when not locked OR when the
        /// server can't resolve the user (common in local-auth mode), so it is never a substitute
        /// for <see cref="LockedBy"/> in "is it locked?"/change-detection logic. Carried on the
        /// server row (get_collection_state/get_changes) but NOT written by the checkout /
        /// checkin-finish write-throughs, so it can briefly lag LockedBy until the next poll.</summary>
        public string LockedByEmail;
        public string LockedByDisplayName;

        /// <summary>
        /// The per-file manifest (path → sha256/size/s3VersionId) of a COMMITTED REPO VERSION of
        /// this book — the database/server side, NOT the local working copy on disk. It never
        /// reflects uncommitted local edits: the tell is the per-entry s3VersionId, a pinned,
        /// already-uploaded S3 version that a purely-local manifest does not have (contrast
        /// <see cref="BookVersionManifest.FromLocalFolder"/>, which leaves s3VersionId null).
        ///
        /// It is populated only with a repo version's manifest, from one of two sources: the
        /// <c>get_book_manifest</c> RPC (CloudTeamCollection.FetchAndCacheManifest), or — right
        /// after THIS client's own checkin-finish — the manifest it just uploaded, which is now the
        /// repo's current version (<see cref="RecordCheckinFinish"/> / <see cref="RecordManifest"/>).
        ///
        /// Which version it describes: normally the current one, but note the ordinary poll
        /// responses (get_collection_state / get_changes) carry no manifest, so
        /// <see cref="ApplyFullSnapshot"/> PRESERVES whatever manifest was last cached even when the
        /// snapshot reports a newer <see cref="CurrentVersionSeq"/>. So it can lag the current repo
        /// version until something re-fetches it (e.g. a Receive). It therefore matches what is on
        /// disk only when the local copy is up to date AND the manifest is current
        /// (LocalVersionSeq == CurrentVersionSeq == the version this manifest is for). May be null
        /// when never obtained.
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
                LockedSeat = LockedSeat,
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
            // Present since the 20260711000003 migration; older rows leave it null (= unknown seat).
            LockedSeat = (string)row["locked_seat"];
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
                ["lockedSeat"] = LockedSeat,
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
                LockedSeat = (string)json["lockedSeat"],
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

        // Cached version of each collection-file GROUP (the collection-file analogue of _booksById).
        // Collection-level files are split into three independently-versioned groups — 'other' (the
        // .bloomCollection settings, styles, branding, etc.), 'allowed-words', and 'sample-texts' —
        // so each syncs on its own version (large, rarely-changed word/sample-text sets aren't
        // re-checked when settings change, and unrelated edits to different groups don't collide on
        // the per-group optimistic-concurrency check). So expect at most three entries, not one.
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
                // LocalVersionSeq is this-machine-only state (never carried in a server row), so it
                // must survive the full-snapshot swap for books that still exist -- otherwise a
                // resync wipes it, and every already-received book then looks like it has an
                // un-downloaded newer version (repoVersionSeq > null), triggering a needless
                // re-download. (Mirrors the Manifest carry-over just above.)
                var oldLocalVersionSeqs = _booksById.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.LocalVersionSeq
                );
                _booksById.Clear();
                foreach (var row in AsObjectArray(state["books"]))
                {
                    var book = new CloudCachedBook();
                    book.ApplyServerRow(row);
                    if (oldManifests.TryGetValue(book.Id, out var manifest))
                        book.Manifest = manifest;
                    if (oldLocalVersionSeqs.TryGetValue(book.Id, out var localSeq))
                        book.LocalVersionSeq = localSeq;
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
        /// <returns>true if the delta actually mutated cached state — at least one book row was
        /// upserted or the event cursor advanced. false means an idle poll that changed nothing, so
        /// the caller can skip persisting/re-indexing (E3).</returns>
        public bool ApplyDelta(JObject changes)
        {
            lock (_gate)
            {
                var changed = false;
                foreach (var row in AsObjectArray(changes["books"]))
                {
                    changed = true;
                    var id = (string)row["id"];
                    if (!_booksById.TryGetValue(id, out var book))
                    {
                        book = new CloudCachedBook();
                        _booksById[id] = book;
                    }
                    book.ApplyServerRow(row);
                }

                changed |= AdvanceCursorLocked(changes["max_event_id"]);
                return changed;
            }
        }

        private static IEnumerable<JObject> AsObjectArray(JToken token) =>
            (token as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>();

        // Caller must already hold _gate. Returns true if the cursor actually advanced.
        private bool AdvanceCursorLocked(JToken maxEventIdToken)
        {
            if (maxEventIdToken == null || maxEventIdToken.Type == JTokenType.Null)
                return false;
            var maxEventId = (long)maxEventIdToken;
            if (maxEventId > _lastSeenEventId)
            {
                _lastSeenEventId = maxEventId;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Keeps the DISPLAY-ONLY lock fields (<see cref="CloudCachedBook.LockedByEmail"/> /
        /// <see cref="CloudCachedBook.LockedByDisplayName"/>) consistent after a client-side
        /// write-through (re)assigns <see cref="CloudCachedBook.LockedBy"/>. The checkout/checkin
        /// RPC results carry the raw user id but NOT the resolved email/name, and
        /// ResolveLockedByForDisplay prefers LockedByEmail — so a value left over from a previous
        /// owner (e.g. after a takeover) would show the WRONG person until the next poll. Rules,
        /// given the value LockedBy had before (<paramref name="previousLockedBy"/>):
        /// nobody holds it now → drop both; the current user now holds it → use the email we know,
        /// and keep our own display name only if it was already ours (else drop it for the next
        /// poll to resolve); it (newly) changed to someone else the client can't resolve → drop
        /// both so we never show a stale name. Must be called under <c>_gate</c>, after LockedBy is
        /// set.
        /// </summary>
        private static void SyncLockDisplayFieldsLocked(
            CloudCachedBook book,
            string previousLockedBy,
            string currentUserId,
            string currentUserEmail
        )
        {
            if (string.IsNullOrEmpty(book.LockedBy))
            {
                book.LockedByEmail = null;
                book.LockedByDisplayName = null;
            }
            else if (book.LockedBy == currentUserId)
            {
                book.LockedByEmail = currentUserEmail;
                if (previousLockedBy != currentUserId)
                    book.LockedByDisplayName = null;
            }
            else if (book.LockedBy != previousLockedBy)
            {
                book.LockedByEmail = null;
                book.LockedByDisplayName = null;
            }
        }

        /// <summary>
        /// Write-through for a checkout_book RPC result (CONTRACTS.md: `{success, locked_by,
        /// locked_by_machine, locked_at}` — present whether or not `success` is true, since a failed
        /// checkout still reports who currently holds the lock). Updates the cached lock fields
        /// immediately rather than waiting for the next poll/snapshot; never itself decides whether a
        /// checkout is allowed (the RPC already did that). <paramref name="currentUserId"/>/
        /// <paramref name="currentUserEmail"/> let it keep the display fields consistent when the
        /// winner is the current user (the RPC does not return the resolved email/name).
        /// </summary>
        public void RecordCheckoutResult(
            string bookId,
            JObject checkoutResult,
            string currentUserId,
            string currentUserEmail
        )
        {
            lock (_gate)
            {
                var book = GetOrAddLocked(bookId);
                var previousLockedBy = book.LockedBy;
                book.LockedBy = (string)checkoutResult["locked_by"];
                book.LockedByMachine = (string)checkoutResult["locked_by_machine"];
                book.LockedSeat = (string)checkoutResult["locked_seat"];
                book.LockedAt = (DateTime?)checkoutResult["locked_at"];
                SyncLockDisplayFieldsLocked(
                    book,
                    previousLockedBy,
                    currentUserId,
                    currentUserEmail
                );
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
                    book.LockedSeat = null;
                    book.LockedAt = null;
                    book.LockedByEmail = null;
                    book.LockedByDisplayName = null;
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
            string lockedByUserId,
            string lockedByMachine,
            string lockedByEmail
        )
        {
            lock (_gate)
            {
                var book = GetOrAddLocked(bookId);
                var previousLockedBy = book.LockedBy;
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
                    // The raw auth user id (matching the server row and checkout_book), NOT an
                    // email — see LockedBy's doc. Keeping this the user id avoids a spurious
                    // book-state-change event when the next poll refreshes LockedBy from the server.
                    book.LockedBy = lockedByUserId;
                    book.LockedByMachine = lockedByMachine;
                }
                else
                {
                    book.LockedBy = null;
                    book.LockedByMachine = null;
                    book.LockedSeat = null;
                    book.LockedAt = null;
                }
                // keptCheckedOut always retains the current user's own lock, so lockedByUserId is
                // the current user here — keep the display fields consistent with the new owner.
                SyncLockDisplayFieldsLocked(book, previousLockedBy, lockedByUserId, lockedByEmail);
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

        // Upsert by id: returns the cached row, or adds a bare (id-only, all other fields null) one
        // if absent. Caller must already hold _gate.
        //
        // A bare row is safe to leave partially populated: each write-through sets the fields its
        // operation owns (RecordCheckinFinish fully populates a first-ever-committed book; the
        // others normally find the book already present and the add is just a defensive fallback).
        // Readers never treat a bare row as a real repo book — book enumerations and
        // IsBookPresentInRepo gate on CurrentVersionSeq.HasValue (null on a bare row), and the
        // name/instanceId index skips rows with a null Name/InstanceId — and a full snapshot later
        // either fully populates the row (if its id is present) or drops it, so partial rows never
        // accumulate or surface as phantom books.
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
