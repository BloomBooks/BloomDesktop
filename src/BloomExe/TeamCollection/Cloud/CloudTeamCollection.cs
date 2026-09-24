using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Bloom.Book;
using Bloom.Collection;
using Bloom.MiscUI;
using Bloom.WebLibraryIntegration;
using Newtonsoft.Json.Linq;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.TeamCollection.Cloud
{
    /// <summary>
    /// TeamCollection backend implementation for cloud-hosted (S3 + Supabase) Team Collections.
    /// See Design/CloudTeamCollections.md (architecture) and CONTRACTS.md (wire contracts).
    ///
    /// Design summary: every abstract member that reads repo state reads from a locally-persisted
    /// <see cref="CloudRepoCache"/> (populated by an initial <see cref="HydrateFromServer"/> call
    /// and kept current by <see cref="CloudCollectionMonitor"/>'s polling); every member that
    /// changes repo state calls a <see cref="CloudCollectionClient"/> RPC/edge function directly and
    /// then write-throughs the result into the cache so subsequent reads are immediately consistent
    /// without waiting for the next poll. Most abstract members are keyed by book folder *name*,
    /// while the cache (and the server) key everything by the immutable server book id, so this
    /// class maintains a name/instanceId -&gt; id index alongside the cache.
    /// </summary>
    public partial class CloudTeamCollection : TeamCollection
    {
        private readonly string _collectionId;
        private readonly CloudEnvironment _environment;
        private readonly CloudAuth _auth;
        private readonly CloudCollectionClient _client;
        private readonly CloudBookTransfer _transfer;
        private readonly CloudRepoCache _cache;
        private readonly CollectionLock _collectionLock;
        private CloudCollectionMonitor _monitor;
        private bool _hydrated;

        // The account (email) claim_memberships was last called for (see CheckConnection):
        // claiming converts an approved-by-email membership row into a claimed (user_id-filled)
        // one that every data RPC's RLS gate accepts. Keyed by EMAIL, not a once-per-session
        // bool (preflight review finding, 10 Jul 2026): this instance survives an in-session
        // sign-out + sign-in as a DIFFERENT approved member (nothing resets it on an
        // account switch), and a stale "already claimed" bool would skip claiming for
        // the new account -- resurrecting the not_a_member startup failure this field exists to
        // prevent.
        private string _membershipsClaimedForEmail;

        // Most TeamCollection abstract members are keyed by book folder *name*; the cache (and the
        // server) key everything by the immutable server "books.id". These two indexes translate.
        // Guarded by _indexGate rather than being rebuilt from the (already thread-safe) cache on
        // every lookup, since several base-class code paths call GetStatus/IsBookPresentInRepo/etc.
        // in tight loops (e.g. SyncAtStartup).
        private readonly object _indexGate = new object();
        private readonly Dictionary<string, string> _bookIdByName = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, string> _bookIdByInstanceId = new Dictionary<
            string,
            string
        >(StringComparer.Ordinal);

        // Cache of local book folders' meta.json instance ids, keyed by folder name and
        // revalidated against meta.json's write time + size (bug #15): ResolveBookId consults
        // the local identity on every repo lookup, and status lookups run in tight per-book
        // loops (SyncAtStartup, book-button rendering), so an uncached read would hammer disk.
        private readonly Dictionary<
            string,
            (string instanceId, long writeTimeTicks, long fileLength)
        > _localInstanceIdCache = new Dictionary<string, (string, long, long)>(
            StringComparer.OrdinalIgnoreCase
        );

        // Per-(book,file) cache of single-file repo fetches (GetRepoBookFile), valid for this
        // process's lifetime -- avoids re-downloading e.g. meta.json for the same book repeatedly
        // when SyncAtStartup's rename/id-conflict scan visits every book once per pass.
        private readonly ConcurrentDictionary<string, string> _repoFileCache =
            new ConcurrentDictionary<string, string>();

        private const int kMaxNameConflictRetries = 10;

        /// <summary>Server-side event-type numeric for the "work preserved locally" incident.
        /// Sourced from the shared enum so client and server stay in sync via one definition.</summary>
        private const int kWorkPreservedLocallyEventType = (int)
            Bloom.History.BookHistoryEventType.WorkPreservedLocally;

        /// <summary>This empty constructor allows the class to be mocked (matches
        /// <see cref="FolderTeamCollection"/>'s own pattern).</summary>
        public CloudTeamCollection()
        {
            System.Diagnostics.Debug.Assert(Program.RunningUnitTests);
        }

        public CloudTeamCollection(
            ITeamCollectionManager manager,
            string localCollectionFolder,
            string collectionId,
            TeamCollectionMessageLog tcLog = null,
            BookCollectionHolder bookCollectionHolder = null,
            CollectionLock collectionLock = null,
            CloudEnvironment environment = null,
            CloudAuth auth = null,
            CloudCollectionClient client = null,
            CloudBookTransfer transfer = null
        )
            : base(manager, localCollectionFolder, tcLog, bookCollectionHolder)
        {
            _collectionId = collectionId;
            CollectionId = collectionId;
            _collectionLock = collectionLock ?? new CollectionLock();
            _environment = environment ?? CloudEnvironment.Current;
            // Only when WE create the default auth (no auth was injected) do we also initialize
            // it at startup. This fixes a real gap: TeamCollectionManager.CreateCloudTeamCollection
            // (the "open an already-joined cloud collection" path, e.g. on ordinary Bloom startup)
            // passes no auth, so without this the session would never pick up
            // BLOOM_CLOUDTC_USER/PASSWORD or a stored token, and the collection would always open
            // signed out. Callers that inject their own CloudAuth (ConnectToCloudCollection, which
            // already calls InitializeAtStartup itself before constructing us; every unit test)
            // are unaffected -- they own their auth's lifecycle already.
            if (auth == null)
                auth = CloudAuth.CreateInitialized(_environment);
            _auth = auth;
            _client = client ?? new CloudCollectionClient(_environment, _auth);
            _transfer = transfer ?? new CloudBookTransfer();
            _cache = CloudRepoCache.LoadOrCreate(localCollectionFolder);
            RefreshIndexFromCache();
        }

        /// <summary>The server-side collections.id this instance talks to (same value the UI's
        /// useCloudCollectionId hook fetches; the base CollectionId field carries it too).</summary>
        public string CloudCollectionId => _collectionId;

        /// <summary>In a cloud TC the identity that owns checkouts is the signed-in ACCOUNT
        /// (the server stamps locks from the auth token), not Bloom's registration email.
        /// Falls back to the base (registration) identity when signed out, where nothing can
        /// be checked out here anyway. See the base property's doc for the smoke-test failure
        /// this fixes (own checkout uneditable).</summary>
        protected internal override string CurrentUserIdentity =>
            _auth?.CurrentEmail ?? base.CurrentUserIdentity;

        private string _currentUserDisplayName;

        // The account email _currentUserDisplayName was resolved for. Keying the cache by email
        // (rather than a boolean) makes an ACCOUNT SWITCH (batch item 9: sign out, different
        // member signs in, same instance) invalidate it automatically -- a boolean flag kept
        // serving the PREVIOUS member's display name for the new member's new local books.
        private string _currentUserDisplayNameForEmail;

        // Failure back-off for the display-name lookup (also keyed by email, for the same
        // account-switch reason as above): when MembersList throws (offline, server down), don't
        // re-attempt the network call for this TTL. Without it, every status render retried the
        // lookup immediately -- a timeout storm while offline. 30s keeps the avatar self-healing
        // soon after connectivity returns while being far longer than any render burst.
        internal static readonly TimeSpan DisplayNameFailureRetryTtl = TimeSpan.FromSeconds(30);
        private string _displayNameLookupFailedForEmail;
        private DateTime _displayNameLookupFailedAtUtc;

        /// <summary>
        /// The signed-in account's admin-editable display name (tc.members.display_name), e.g.
        /// "Alice Admin", or null when signed out / not yet resolvable. Used so a brand-new
        /// local book's avatar shows the same initials + full name as a real checkout by this
        /// user (dogfood bug: new local books showed a single-letter, email-tooltip avatar while
        /// real checkouts showed "AA"/"Alice Admin"). Resolved lazily from MembersList and cached
        /// per signed-in email; a failed resolve is retried, but only after
        /// <see cref="DisplayNameFailureRetryTtl"/> (a per-call retry made every status render
        /// pay a fresh network timeout while offline). Not refreshed after a successful
        /// resolve while the same account stays signed in -- display-name changes are rare and
        /// the avatar is cosmetic. Callers fall back to the email when this is null, matching
        /// prior behavior.
        /// </summary>
        protected internal virtual string CurrentUserDisplayName
        {
            get
            {
                var email = _auth?.CurrentEmail;
                if (string.IsNullOrEmpty(email))
                    return null;
                if (
                    string.Equals(
                        _currentUserDisplayNameForEmail,
                        email,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    return _currentUserDisplayName;
                if (
                    string.Equals(
                        _displayNameLookupFailedForEmail,
                        email,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && DateTime.UtcNow - _displayNameLookupFailedAtUtc < DisplayNameFailureRetryTtl
                )
                    return null; // recent failure; don't hammer the server (see the TTL field's doc)
                try
                {
                    string resolved = null;
                    foreach (var member in _client.MembersList(CollectionId).OfType<JObject>())
                    {
                        if (
                            string.Equals(
                                (string)member["email"],
                                email,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            resolved = (string)member["display_name"];
                            break;
                        }
                    }
                    _currentUserDisplayName = resolved;
                    _currentUserDisplayNameForEmail = email;
                    _displayNameLookupFailedForEmail = null;
                }
                catch
                {
                    // Network/permission hiccup; leave unresolved so a later call can retry --
                    // but not before the TTL elapses (timeout-storm guard).
                    _displayNameLookupFailedForEmail = email;
                    _displayNameLookupFailedAtUtc = DateTime.UtcNow;
                    return null;
                }
                return _currentUserDisplayName;
            }
        }

        // ------------------------------------------------------------------
        // Accessors for task 06 (SharingApi / TeamCollectionApi): thin, read-only exposure of
        // this collection's own auth/client/cache state, so those API classes can be simple
        // pass-throughs instead of duplicating Cloud-backend business logic. Mirrors the existing
        // pattern of TeamCollectionApi downcasting to FolderTeamCollection for folder-specific
        // members (e.g. GetPathToBookFileInRepo).
        // ------------------------------------------------------------------

        /// <summary>The auth session actually driving this collection's own RPC/edge-function
        /// calls -- the single source of truth for "am I signed in" while this collection is the
        /// open one (see SharingApi's CurrentAuth/CurrentClient helpers).</summary>
        public CloudAuth Auth => _auth;

        /// <summary>The RPC/edge-function client this collection uses for everything -- exposed so
        /// SharingApi can call collection-scoped RPCs (members list/add/remove/setRole, force
        /// unlock, history) without this class needing to grow business logic for them.</summary>
        public CloudCollectionClient Client => _client;

        /// <summary>Test-only: resolve a book folder name to its server book id (null if unknown).</summary>
        internal string GetBookIdByNameIndexForTests(string bookFolderName) =>
            TryGetBookId(bookFolderName);

        /// <summary>The version seq of what's currently on THIS machine's disk for a book, or null
        /// if never Sent/Received here (book-status JSON's "localVersionSeq").</summary>
        public long? GetLocalVersionSeq(string bookFolderName) =>
            ResolveCachedBook(bookFolderName)?.LocalVersionSeq;

        /// <summary>The latest version seq known to be in the repo for a book, or null if the book
        /// isn't cached at all (book-status JSON's "repoVersionSeq").</summary>
        public long? GetRepoVersionSeq(string bookFolderName) =>
            ResolveCachedBook(bookFolderName)?.CurrentVersionSeq;

        /// <summary>The server book id for a book folder name, or null if unknown -- lets
        /// SharingApi's history endpoint match a "current book only" filter (Bloom's BookInfo has
        /// no notion of the server's tc.books.id) without duplicating this class's private
        /// name/instanceId -&gt; id index.</summary>
        public string TryGetBookIdForHistoryFilter(string bookFolderName) =>
            ResolveBookId(bookFolderName);

        /// <summary>
        /// Count of live books whose repo version is newer than what's on this machine -- drives
        /// the status button's "Updates Available (N books)" metadata. A book this machine has
        /// never Received (LocalVersionSeq null) counts too: from this machine's point of view it
        /// equally needs a Receive before it's current.
        ///
        /// This must count EXACTLY the books <see cref="ReceiveAllUpdates"/> would receive, so the
        /// two never disagree: a book checked out by ANOTHER user still counts (John, 16 Jul 2026)
        /// -- a reviewer legitimately wants the latest checked-in version even though they can't
        /// edit it, and it would be wrong for the badge to switch off (without the update ever
        /// being received) merely because someone else took the book out. Only a book checked out
        /// HERE is excluded -- the same book ReceiveAllUpdates skips, because receiving would
        /// clobber local edits. "Checked out here" is computed via the identical
        /// StatusFromCachedBook + IsCheckedOutHereBy path ReceiveAllUpdates uses, so they can't drift.
        /// </summary>
        public int GetUpdatesAvailableCount()
        {
            EnsureCacheHydrated();
            return _cache
                .GetAllBooks()
                .Count(b =>
                    !b.DeletedAt.HasValue
                    && b.CurrentVersionSeq.HasValue
                    && !IsCheckedOutHereBy(StatusFromCachedBook(b, _collectionId, b.Name))
                    && (b.LocalVersionSeq ?? -1) < b.CurrentVersionSeq.Value
                );
        }

        /// <summary>
        /// The body of the "Sync" / Receive-Updates button (see
        /// TeamCollectionApi.HandleReceiveUpdates): polls once, then downloads every book whose
        /// repo version is newer than the local copy EXCEPT books checked out here (receiving
        /// would clobber local edits). Any locally-modified copy is preserved before it is
        /// overwritten (batch item 8). Returns (booksReceived, booksSkippedBecauseCheckedOutHere)
        /// for the caller's progress/analytics summary.
        ///
        /// NOTE (flagged for John, 16 Jul 2026 file-org pass): the "which books" test here skips
        /// only books checked out HERE, whereas <see cref="GetUpdatesAvailableCount"/> above
        /// excludes books locked by ANYONE -- so a book locked by another user counts as "no
        /// update available" in the badge yet is still received by this loop. The two predicates
        /// are deliberately NOT unified in this pure code-move; whether they SHOULD agree is a
        /// separate behavior decision.
        /// </summary>
        public (int received, int skippedCheckedOutHere) ReceiveAllUpdates(
            Bloom.web.IWebSocketProgress progress
        )
        {
            PollNow();
            var receivedCount = 0;
            var skippedCheckedOutCount = 0;
            foreach (var bookName in GetBookList())
            {
                var status = GetStatus(bookName);
                if (IsCheckedOutHereBy(status))
                {
                    skippedCheckedOutCount++;
                    continue; // Checked out here; Receive would conflict with local edits.
                }
                var repoSeq = GetRepoVersionSeq(bookName);
                var localSeq = GetLocalVersionSeq(bookName);
                if (!repoSeq.HasValue || (localSeq ?? -1) >= repoSeq.Value)
                    continue; // Already current.
                progress.MessageWithoutLocalizing($"Receiving updates for '{bookName}'...");
                // Batch item 8: same preserve-before-overwrite guard as the auto-apply worker --
                // Sync must never silently discard local content that changed since the last sync.
                PreserveLocalCopyIfModifiedSinceLastSync(bookName);
                CopyBookFromRepoToLocal(bookName, dialogOnError: false);
                receivedCount++;
            }
            return (receivedCount, skippedCheckedOutCount);
        }

        // ------------------------------------------------------------------
        // Capability flags / simple identity members
        // ------------------------------------------------------------------

        public override string GetBackendType() => "Cloud";

        public override string RepoDescription => $"cloud://sil.bloom/collection/{_collectionId}";

        public override bool SupportsVersionHistory => true;

        public override bool SupportsSharingUi => true;

        public override bool RequiresSignIn => true;

        /// <summary>
        /// Cloud Team Collections apply safe remote book changes automatically (batch item 4+5,
        /// decision 9 Jul 2026): a poll that notices a checkin for a book that isn't checked out
        /// here downloads and swaps it in without the user having to click anything, instead of
        /// just showing a "click to get updates" message. See TeamCollection.HandleModifiedFile and
        /// ProcessAutoApplyRemoteChange for the actual logic; this flag is the only thing that
        /// differs from a folder Team Collection.
        /// </summary>
        protected override bool CanAutoApplyRemoteChanges => true;

        /// <summary>
        /// Batch item 8 (John's recovery decision, 9 Jul 2026): before a sync overwrites a local
        /// book copy that changed since the last sync, zip it to Lost and Found as a .bloomSource
        /// and log the WorkPreservedLocally incident -- then the overwrite proceeds, making local
        /// consistent with the Team Collection without silently losing anything.
        /// </summary>
        protected override void PreserveLocalCopyForRecoveryBeforeOverwrite(string bookFolderName)
        {
            SaveLocalCopyForRecovery(
                Path.Combine(_localCollectionFolder, bookFolderName),
                bookFolderName,
                "LocalChangesOverwrittenBySync"
            );
        }

        // ------------------------------------------------------------------
        // Cache hydration (get_collection_state) and the name/instanceId <-> id index
        // ------------------------------------------------------------------

        /// <summary>
        /// Ensures the cache has been populated at least once this session. Cheap after the first
        /// call (a plain bool check); the actual refresh is a synchronous network call, matching how
        /// the base class's own repo-reading abstract members are documented as needing to be
        /// synchronous and thread-safe.
        /// </summary>
        private void EnsureCacheHydrated()
        {
            if (_hydrated)
                return;
            HydrateFromServer();
        }

        /// <summary>
        /// Fetches the full snapshot (first call / cursor 0) or a delta (subsequent calls) from
        /// get_collection_state and applies it to the cache. Called at startup, after every
        /// mutating call whose response doesn't carry enough information to write-through directly
        /// (see PutBookInRepo's "checkin-start/finish don't return the server book id" note), and
        /// can be called by <see cref="Cloud.CloudJoinFlow"/> after creating/joining a collection.
        /// </summary>
        internal void HydrateFromServer()
        {
            var sinceEventId = _cache.LastSeenEventId;
            var state =
                sinceEventId > 0
                    ? _client.GetCollectionState(_collectionId, sinceEventId)
                    : _client.GetCollectionState(_collectionId);
            if (state == null)
            {
                // A 2xx response with an empty body -- not an error the client maps (those throw),
                // but not the contract shape either. Don't fail here (callers run on background
                // threads and the poll will re-try), but never be SILENT about it: an empty cache
                // wrongly marked hydrated makes every IsBookPresentInRepo answer false, which
                // silently drops queued background downloads.
                Logger.WriteEvent(
                    $"CloudTeamCollection: get_collection_state returned no data (since_event_id={sinceEventId}); cache left as-is with {_cache.GetAllBooks().Count()} book(s)."
                );
                _hydrated = true;
                return;
            }
            if (sinceEventId > 0)
                _cache.ApplyDelta(state);
            else
                _cache.ApplyFullSnapshot(state);
            _cache.Save();
            RefreshIndexFromCache();
            _hydrated = true;
        }

        private void RefreshIndexFromCache()
        {
            lock (_indexGate)
            {
                _bookIdByName.Clear();
                _bookIdByInstanceId.Clear();
                foreach (var book in _cache.GetAllBooks())
                {
                    if (!string.IsNullOrEmpty(book.Name))
                        _bookIdByName[book.Name] = book.Id;
                    if (!string.IsNullOrEmpty(book.InstanceId))
                        _bookIdByInstanceId[book.InstanceId] = book.Id;
                }
            }
        }

        /// <summary>Resolve a REPO book name (e.g. one returned by GetBookList) to its server book
        /// id. This is pure name-index lookup; for anything that denotes a LOCAL book folder, use
        /// <see cref="ResolveBookId"/>, which resolves by the folder's own instance id.</summary>
        private string TryGetBookId(string bookName)
        {
            lock (_indexGate)
            {
                return _bookIdByName.TryGetValue(bookName, out var id) ? id : null;
            }
        }

        /// <summary>
        /// Resolve a book folder name to its server book id, IDENTITY FIRST (bug #15; John's
        /// ruling, 13 Jul 2026: "the status of a particular record by instanceID in the database
        /// is the source of truth for that book's state"). When a local folder exists, its
        /// meta.json bookInstanceId -- never its folder name -- decides which server row (if any)
        /// is this book. That keeps (a) a checked-out book that was renamed locally bound to its
        /// own row until check-in carries the rename to the server, and (b) a local book that
        /// merely shares a NAME with some other checked-in book (e.g. created offline while a
        /// teammate checked in an unrelated book of the same name) from wearing that book's
        /// status. A local folder whose id cannot be read resolves to null ("not in the repo")
        /// rather than guessing by name -- fail-safe, degrading to "local-only book". The name
        /// index is consulted only when there is NO local folder, i.e. the caller is asking about
        /// a repo book (typically a name from GetBookList).
        /// </summary>
        private string ResolveBookId(string bookFolderName)
        {
            var folderPath = Path.Combine(_localCollectionFolder, bookFolderName);
            if (Directory.Exists(folderPath))
            {
                var instanceId = TryGetLocalBookInstanceId(bookFolderName);
                return instanceId == null ? null : TryGetBookIdByInstanceId(instanceId);
            }
            return TryGetBookId(bookFolderName);
        }

        private CloudCachedBook ResolveCachedBook(string bookFolderName)
        {
            var id = ResolveBookId(bookFolderName);
            return id == null ? null : _cache.TryGetBook(id);
        }

        /// <summary>Test-only: a Send with exactly the given new status (PutBook derives it from
        /// the repo status, which makes some check-in shapes awkward to set up).</summary>
        internal void PutBookInRepoForTests(string sourceBookFolderPath, BookStatus newStatus) =>
            PutBookInRepo(sourceBookFolderPath, newStatus);

        /// <summary>Test-only: exposes <see cref="ResolveBookId"/>'s identity-first semantics.</summary>
        internal string ResolveBookIdForTests(string bookFolderName) =>
            ResolveBookId(bookFolderName);

        /// <summary>
        /// Identity-exact remote-rename detection (bug #18). The base heuristic ("a local folder
        /// with repo status can't be the rename source") assumes name-keyed status and is
        /// INVERTED under this class's identity-first resolution: after a teammate's rename, the
        /// old-name local folder resolves (by instance id) to the renamed repo row -- having
        /// status is precisely what marks it as the rename source. Instead compare instance ids
        /// directly: the repo book named <paramref name="newBookName"/> is a rename of whichever
        /// local folder carries the same meta.json id under a different name. Without this,
        /// the receiving side downloaded the renamed book as a NEW book next to the old-name
        /// folder -- two local folders with one instance id (both "selected" at once, phantom
        /// checkout displays -- John's live report, 13 Jul 2026).
        /// </summary>
        protected internal override string NewBookRenamedFrom(string newBookName)
        {
            // A one-off call builds the index and throws it away; the per-poll bulk path uses the
            // ref-scanState override below so it builds the index only once for the whole scan.
            object scanState = null;
            return NewBookRenamedFrom(newBookName, ref scanState);
        }

        /// <summary>
        /// Bulk-scan override for QueueMissingRepoBooksForBackgroundDownload. Finding the local
        /// folder that shares a repo book's instance id means enumerating every local folder; the
        /// per-book method does that once per missing book, which is O(missing x local) disk stats
        /// per poll -- wasteful during a progressive join of a large collection (E7). Here we build
        /// the instanceId -> folder index ONCE (the first missing book that needs it) and reuse it
        /// for the rest of the pass, collapsing it to O(missing + local). scanState is a
        /// caller-owned local (see the base pass), so it is rebuilt fresh each poll -- never stale
        /// across polls -- and touched only by the polling thread.
        /// </summary>
        protected internal override string NewBookRenamedFrom(
            string newBookName,
            ref object scanState
        )
        {
            EnsureCacheHydrated();
            // Repo-name lookup on purpose: the caller is asking about a repo book's name.
            var instanceId = TryGetBookInstanceIdForName(newBookName);
            if (string.IsNullOrEmpty(instanceId))
                return null;
            var index =
                scanState as Dictionary<string, string>
                ?? (Dictionary<string, string>)(scanState = BuildLocalInstanceIdToFolder());
            return
                index.TryGetValue(instanceId, out var folder)
                && !string.Equals(folder, newBookName, StringComparison.OrdinalIgnoreCase)
                ? folder
                : null;
        }

        /// <summary>
        /// Maps each local book folder's meta.json instance id to its folder name, by enumerating
        /// the collection folder once. Per-folder id reads go through <see
        /// cref="TryGetLocalBookInstanceId"/>, so they hit the timestamp/size-validated cache. On
        /// the (pathological, bug #18) case of two local folders sharing one id, first-enumerated
        /// wins -- matching the per-book method's original first-match-returns loop.
        /// </summary>
        private Dictionary<string, string> BuildLocalInstanceIdToFolder()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in Directory.EnumerateDirectories(_localCollectionFolder))
            {
                var folderName = Path.GetFileName(path);
                var instanceId = TryGetLocalBookInstanceId(folderName);
                if (!string.IsNullOrEmpty(instanceId) && !map.ContainsKey(instanceId))
                    map[instanceId] = folderName;
            }
            return map;
        }

        /// <summary>The meta.json bookInstanceId of the LOCAL folder, or null when the folder has
        /// no readable id. Cached per folder name, revalidated by meta.json timestamp+size (the
        /// id itself never legitimately changes in place, but the folder a name points at can --
        /// delete + recreate, rename shuffles).</summary>
        private string TryGetLocalBookInstanceId(string bookFolderName)
        {
            try
            {
                var metaInfo = new FileInfo(
                    Path.Combine(_localCollectionFolder, bookFolderName, "meta.json")
                );
                if (!metaInfo.Exists)
                    return null;
                var writeTimeTicks = metaInfo.LastWriteTimeUtc.Ticks;
                var fileLength = metaInfo.Length;
                lock (_indexGate)
                {
                    if (
                        _localInstanceIdCache.TryGetValue(bookFolderName, out var cached)
                        && cached.writeTimeTicks == writeTimeTicks
                        && cached.fileLength == fileLength
                    )
                        return cached.instanceId;
                }
                var instanceId = GetBookId(bookFolderName); // base helper: meta.json's Id
                lock (_indexGate)
                    _localInstanceIdCache[bookFolderName] = (
                        instanceId,
                        writeTimeTicks,
                        fileLength
                    );
                return instanceId;
            }
            catch (Exception)
            {
                // Unreadable folder/meta.json = unknown identity; callers treat that as
                // "not a repo book" rather than misbinding by name.
                return null;
            }
        }

        private string TryGetBookIdByInstanceId(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId))
                return null;
            lock (_indexGate)
            {
                return _bookIdByInstanceId.TryGetValue(instanceId, out var id) ? id : null;
            }
        }

        private CloudCachedBook TryGetCachedBook(string bookName)
        {
            var id = TryGetBookId(bookName);
            return id == null ? null : _cache.TryGetBook(id);
        }

        /// <summary>
        /// Batch item 7 (progressive join): the stable per-book instance id (matches BookInfo.Id
        /// once the book is actually downloaded) for a repo book, keyed by its folder name. Used
        /// by CollectionApi's HandleBooksRequest merge to give a not-yet-downloaded placeholder
        /// entry a client-visible id that stays the same across the download, so React doesn't
        /// remount the book button when the placeholder swaps for the real one.
        /// </summary>
        public string TryGetBookInstanceIdForName(string bookName)
        {
            EnsureCacheHydrated();
            return TryGetCachedBook(bookName)?.InstanceId;
        }

        /// <summary>
        /// Batch item 7 (progressive join): the reverse of <see cref="TryGetBookInstanceIdForName"/>
        /// -- resolves a not-yet-downloaded placeholder's client-visible id back to its repo book
        /// folder name. Used by CollectionApi's selected-book handler to find which book to
        /// prioritize when the user clicks a placeholder (there is no local BookInfo to look it up
        /// by folder name the normal way).
        /// </summary>
        public string TryGetBookNameForInstanceId(string instanceId)
        {
            EnsureCacheHydrated();
            var bookId = TryGetBookIdByInstanceId(instanceId);
            return bookId == null ? null : _cache.TryGetBook(bookId)?.Name;
        }

        /// <summary>
        /// Batch item 7 (progressive join): bumps a not-yet-downloaded book's background download
        /// to the front of the queue -- called when the user selects its placeholder, so the book
        /// they're waiting for arrives before others that were merely queued in the background. A
        /// no-op if the book already has a local folder (nothing left to prioritize).
        /// </summary>
        public void PrioritizeDownload(string bookName)
        {
            if (Directory.Exists(Path.Combine(LocalCollectionFolder, bookName)))
                return;
            PrioritizeBackgroundDownload(bookName);
        }

        /// <summary>
        /// The BookStatus a cached server row stands for, as seen from the local book folder
        /// <paramref name="localFolderName"/> (usually the book's name; a locally-renamed
        /// checked-out book's folder differs from its repo name). Sets
        /// <see cref="BookStatus.checkedOutInThisCopy"/>, which is what makes "checked out here"
        /// mean "checked out in this copy of the collection" for a cloud book.
        /// </summary>
        private BookStatus StatusFromCachedBook(
            CloudCachedBook book,
            string collectionId,
            string localFolderName
        )
        {
            return new BookStatus
            {
                checkedOutInThisCopy = IsCheckedOutInThisCopy(book, localFolderName),
                checksum = book.CurrentChecksum,
                lockedBy = ResolveLockedByForDisplay(book),
                // The server book row has no first/last-name split, only a whole display name
                // (locked_by_name: since 20260713000001 the durable, admin-editable
                // tc.members.display_name, with the older JWT-claim capture as fallback). The
                // WHOLE name goes in the FirstName slot with Surname left null: both consumers
                // (TeamCollectionBookStatusPanel's `${whoFirstName ?? ""} ${whoSurname ?? ""}`
                // .trim() and BookButton's `whoFirstName || who`) render that cleanly, and they
                // fall back to the email (lockedBy) when it is null. lockedBy itself must STAY
                // the email -- the panel compares it with currentUser to decide lockedByMe.
                lockedByFirstName = book.LockedByDisplayName,
                lockedBySurname = null,
                lockedWhen = book.LockedAt.HasValue
                    ? $"{book.LockedAt.Value.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.fffZ}"
                    : null,
                lockedWhere = book.LockedByMachine,
                collectionId = collectionId,
            };
        }

        /// <summary>
        /// Live-testing discovery (see the task 05 final report): the server stamps
        /// `locked_by`/`created_by` with the raw auth user id (JWT `sub`), not the email
        /// CONTRACTS.md's identity model describes ("account email is the identity in cloud TCs").
        /// Task 06's 20260707000006 migration fixes this server-side for every member (not just the
        /// caller) by joining tc.members in get_collection_state/get_changes and reporting the
        /// result as <see cref="CloudCachedBook.LockedByEmail"/> -- preferred here whenever present.
        /// The original client-side workaround (resolve only OUR OWN id, since that's all a plain
        /// JWT comparison can do) is kept as a fallback for a cache snapshot saved before that
        /// migration landed (an old <c>.bloom-cloud-repo-cache.json</c> with no LockedByEmail yet).
        /// </summary>
        private string ResolveLockedByForDisplay(CloudCachedBook book)
        {
            var lockedBy = book.LockedBy;
            if (string.IsNullOrEmpty(lockedBy))
                return lockedBy;
            if (!string.IsNullOrEmpty(book.LockedByEmail))
                return book.LockedByEmail;
            if (lockedBy == _auth.CurrentUserId)
                return _auth.CurrentEmail;
            return lockedBy;
        }

        // ------------------------------------------------------------------
        // Book status / list / presence (read from cache)
        // ------------------------------------------------------------------

        protected override bool TryGetBookStatusJsonFromRepo(
            string bookFolderName,
            out string status,
            bool reportFailure = true
        )
        {
            EnsureCacheHydrated();
            // Identity-first (bug #15): a local folder resolves by its own instance id, so a
            // locally-renamed checked-out book still reports ITS row's status, and a local book
            // that merely shares a name with someone else's checked-in book reports none.
            var cachedBook = ResolveCachedBook(bookFolderName);
            if (cachedBook == null || !cachedBook.CurrentVersionSeq.HasValue)
            {
                // Either genuinely absent from the repo, or a never-committed new book (invisible to
                // teammates per CONTRACTS.md) -- both are the valid "no repo file" case, not an error.
                status = null;
                return true;
            }
            status = StatusFromCachedBook(cachedBook, CollectionId, bookFolderName).ToJson();
            return true;
        }

        protected override string GetBookStatusJsonFromRepo(string bookFolderName)
        {
            TryGetBookStatusJsonFromRepo(bookFolderName, out var status);
            return status;
        }

        public override bool IsBookPresentInRepo(string bookFolderName)
        {
            EnsureCacheHydrated();
            var cachedBook = ResolveCachedBook(bookFolderName);
            return cachedBook != null && cachedBook.CurrentVersionSeq.HasValue;
        }

        public override bool KnownToHaveBeenDeleted(string oldName)
        {
            var cachedBook = ResolveCachedBook(oldName);
            return cachedBook != null && cachedBook.DeletedAt.HasValue;
        }

        public override string[] GetBookList()
        {
            EnsureCacheHydrated();
            return _cache
                .GetAllBooks()
                .Where(b => !b.DeletedAt.HasValue && b.CurrentVersionSeq.HasValue)
                .Select(b => b.Name)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToArray();
        }

        /// <summary>
        /// Cloud override: answered entirely from the repo cache, which already knows every
        /// book's instance id (the server's books.instance_id IS meta.json's bookInstanceId).
        /// The base implementation fetches each repo book's meta.json via GetRepoBookFile just to
        /// read that same id -- for this backend, several network round trips PER BOOK at every
        /// startup sync. Matches the base contract exactly: only live, committed repo books
        /// (the same filter as <see cref="GetBookList"/>) with a readable id appear; value is
        /// (repo book name, whether a local folder of that name exists). Nothing is ever added to
        /// <paramref name="unreadableBooks"/> -- there are no repo zip files here to be unreadable,
        /// and the base only adds names on zip/IO read failures.
        /// </summary>
        protected override Dictionary<string, Tuple<string, bool>> GetRepoBooksByIdMap(
            ICollection<string> unreadableBooks = null
        )
        {
            EnsureCacheHydrated();
            var booksById = new Dictionary<string, Tuple<string, bool>>();
            foreach (var book in _cache.GetAllBooks())
            {
                if (book.DeletedAt.HasValue || !book.CurrentVersionSeq.HasValue)
                    continue; // not a live, committed repo book (mirrors GetBookList)
                if (string.IsNullOrEmpty(book.Name) || string.IsNullOrEmpty(book.InstanceId))
                    continue; // no usable name/id -- the base skips id-less books too
                booksById[book.InstanceId] = Tuple.Create(
                    book.Name,
                    Directory.Exists(Path.Combine(_localCollectionFolder, book.Name))
                );
            }
            return booksById;
        }

        /// <summary>
        /// Diff-dispatches to the narrowest RPC per Design/CloudTeamCollections/notes/
        /// write-book-status-audit.md. TryLockInRepo/UnlockInRepo (overridden below) already handle
        /// the lock-changing callers (AttemptLock/UnlockBook/ForceUnlock); this method only needs to
        /// handle the remaining base-class callers that call WriteBookStatus directly:
        /// ForgetChangesCheckin (clears a lock -> unlock_book), and SyncAtStartup's three cases
        /// (restore-our-checkout -> checkout_book; accept-remote-lock and update-checksum-only ->
        /// local-only, no RPC, since the cloud repo is already authoritative for both).
        /// </summary>
        protected override void WriteBookStatusJsonToRepo(string bookName, string status)
        {
            var newStatus = BookStatus.FromJson(status);
            var bookId = ResolveBookId(bookName);
            if (bookId == null)
                return; // never-committed book; checkin-start/finish already established repo state.

            var cachedBook = _cache.TryGetBook(bookId);
            if (cachedBook == null)
                return;

            if (string.IsNullOrEmpty(newStatus.lockedBy))
            {
                // ForgetChangesCheckin: abandon-and-unlock.
                if (!string.IsNullOrEmpty(cachedBook.LockedBy))
                    UnlockInRepo(bookName, force: false);
                return;
            }

            if (
                string.IsNullOrEmpty(cachedBook.LockedBy)
                && newStatus.lockedBy == CurrentUserIdentity
                && newStatus.checksum == cachedBook.CurrentChecksum
            )
            {
                // SyncAtStartup restoring our own abandoned-remotely checkout, content unchanged.
                TryLockInRepo(bookName, newStatus);
                return;
            }

            // SyncAtStartup's "accept remote lock" / "update checksum only" cases: the cloud repo is
            // already authoritative for both lock and checksum, so there is nothing to write back.
        }

        // ------------------------------------------------------------------
        // Locking
        // ------------------------------------------------------------------

        /// <summary>Conditional lock via a single RPC, per CONTRACTS.md's checkout_book (race-free:
        /// "conditional UPDATE"). The RPC always returns 200 with `{success, locked_by,
        /// locked_by_machine, locked_at}` -- present whether or not `success` is true, so we can
        /// write-through the winner's identity into the cache even on a failed attempt (that's
        /// exactly what lets AttemptLock's caller show "checked out by X" immediately).
        /// v1.9: a successful checkout returns the checkout GUID, which is saved in the book
        /// folder's `.checkout` record -- that is what makes this copy the one the book is checked
        /// out in. `locked_by_me` (the caller already holds it, in another copy) is a refusal:
        /// there is no "check out here instead"; the book stays read-only in this copy.</summary>
        protected override bool TryLockInRepo(string bookName, BookStatus newStatus)
        {
            var bookId = ResolveBookId(bookName);
            if (bookId == null)
                return true; // brand-new, never-committed local book; nothing to lock server-side yet.

            var result = _client.CheckoutBook(bookId, TeamCollectionManager.CurrentMachine);
            var success = (bool?)result["success"] ?? false;
            if (success)
            {
                var guid =
                    (string)result["checkoutGuid"]
                    ?? throw new ApplicationException(
                        $"The Team Collection server checked out \"{bookName}\" but did not return its checkout GUID."
                    );
                var bookFolderPath = Path.Combine(_localCollectionFolder, bookName);
                try
                {
                    WriteCheckoutFile(bookFolderPath, bookId, guid);
                }
                catch (Exception)
                {
                    // Without the record no copy could ever use this checkout; give it back
                    // rather than leave the book locked to nobody's copy.
                    _client.UnlockBook(bookId, guid);
                    throw;
                }
            }
            _cache.RecordCheckoutResult(bookId, result, _auth.CurrentUserId, _auth.CurrentEmail);
            _cache.Save();
            return success;
        }

        /// <summary>Single RPC unlock/force-unlock, per CONTRACTS.md's unlock_book/force_unlock.
        /// An ordinary unlock presents this copy's checkout GUID and then removes the `.checkout`
        /// record. A forced unlock (administrator only) needs no GUID and never depends on this
        /// copy having one: it leaves any local record alone, and the copy that held the checkout
        /// (possibly this one) finds its record obsolete at its next poll or open and cancels it
        /// there (see <see cref="ReconcileCheckoutFiles"/>), preserving any edits.</summary>
        protected override void UnlockInRepo(string bookName, bool force)
        {
            var bookId = ResolveBookId(bookName);
            if (bookId == null)
                return;
            var bookFolderPath = Path.Combine(_localCollectionFolder, bookName);
            if (force)
                _client.ForceUnlock(bookId);
            else
            {
                _client.UnlockBook(bookId, CloudCheckoutFile.ReadGuid(bookFolderPath));
                CloudCheckoutFile.Delete(bookFolderPath);
            }
            _cache.RecordUnlock(bookId);
            _cache.Save();
        }

        /// <summary>Saves the `.checkout` record for a checkout this copy was just given.</summary>
        private void WriteCheckoutFile(string bookFolderPath, string bookId, string checkoutGuid)
        {
            new CloudCheckoutFile
            {
                CheckoutGuid = checkoutGuid,
                BookId = bookId,
                CollectionId = _collectionId,
                UserEmail = _auth.CurrentEmail,
                CheckedOutAtUtc = DateTime.UtcNow,
            }.Write(bookFolderPath);
        }

        // ------------------------------------------------------------------
        // "Here" means "in this copy": the checkout GUID, and account-switch takeover
        // ------------------------------------------------------------------

        /// <summary>
        /// True when the book is checked out (by anyone) IN THIS COPY of the collection: the
        /// server row is locked, and <paramref name="localFolderName"/>'s `.checkout` record holds
        /// the GUID whose hash the server reports (CONTRACTS.md v1.9). The machine is irrelevant:
        /// the record travels with the book folder, so a collection folder that was moved,
        /// renamed, or copied to another computer keeps its checkout, while any other copy
        /// (including a duplicate whose twin already checked in or out again) does not.
        /// </summary>
        private bool IsCheckedOutInThisCopy(CloudCachedBook book, string localFolderName)
        {
            if (string.IsNullOrEmpty(book.LockedBy) || string.IsNullOrEmpty(localFolderName))
                return false;
            return CloudCheckoutFile.MatchesServerHash(
                Path.Combine(_localCollectionFolder, localFolderName),
                book.CheckoutGuidHash
            );
        }

        /// <summary>
        /// Whether the book in local folder <paramref name="bookFolderName"/> is checked out in
        /// this copy of the collection (by any account), or null when the repo knows no such
        /// book (a new local-only book, or one never committed). See the private overload.
        /// </summary>
        public bool? IsCheckedOutInThisCopy(string bookFolderName)
        {
            EnsureCacheHydrated();
            var book = ResolveCachedBook(bookFolderName);
            if (book == null || !book.CurrentVersionSeq.HasValue)
                return null;
            return IsCheckedOutInThisCopy(book, bookFolderName);
        }

        /// <summary>
        /// Editable here = checked out in THIS copy, by any account (John's decisions: local
        /// access is unrestricted -- batch item 9 -- but only where the book is actually checked
        /// out). A book another account checked out in this copy is therefore editable, and its
        /// server lock moves to the current account through <see cref="TryTakeOverLock"/> the
        /// first time that matters. The current user's own checkout seen from any OTHER copy is
        /// not editable, and nothing here offers to move it ("check out here instead" was
        /// deliberately left out); going back to the copy that has it, or an administrator's
        /// force-unlock, is the way out.
        /// </summary>
        protected internal override bool IsEditableHere(string bookName, BookStatus status)
        {
            if (status.lockedBy == FakeUserIndicatingNewBook)
                return true; // a new local-only book is always editable
            if (!status.checkedOutInThisCopy.HasValue)
                return base.IsEditableHere(bookName, status); // not a repo-cache status
            return status.IsCheckedOut() && status.checkedOutInThisCopy.Value;
        }

        /// <summary>
        /// A lock held by a DIFFERENT account can be taken over only from the copy it is checked
        /// out in (the one with the current GUID), which is also the only copy able to prove it
        /// to the server. Machine no longer matters.
        /// </summary>
        protected internal override bool CanTakeOverLockOnThisMachine(
            string bookName,
            BookStatus repoStatus
        ) =>
            repoStatus.IsCheckedOut()
            && repoStatus.lockedBy != CurrentUserIdentity
            && repoStatus.checkedOutInThisCopy == true;

        /// <summary>
        /// Calls the tc.checkout_book_takeover RPC (CONTRACTS.md v1.9) to atomically reassign the
        /// book's server lock to the current account, presenting this copy's checkout GUID (the
        /// server's only test). The GUID does not change; the `.checkout` record is re-stamped
        /// with the new account. Purely additive server-side: checkin_start_tx itself is
        /// untouched, so calling this before check-in is what lets its "LockHeldByOther" gate
        /// pass cleanly for an account-switched check-in.
        /// </summary>
        protected internal override bool TryTakeOverLock(string bookName)
        {
            var bookId = ResolveBookId(bookName);
            if (bookId == null)
                return true; // brand-new, never-committed local book; nothing to take over.

            var bookFolderPath = Path.Combine(_localCollectionFolder, bookName);
            var record = CloudCheckoutFile.Read(bookFolderPath);
            if (record == null)
                return false; // only the copy holding the GUID can take the lock over
            var result = _client.CheckoutBookTakeover(
                bookId,
                record.CheckoutGuid,
                TeamCollectionManager.CurrentMachine
            );
            var success = (bool?)result["success"] ?? false;
            if (success)
            {
                // Same checkout, same GUID; only the account holding it changed.
                record.UserEmail = _auth.CurrentEmail;
                record.Write(bookFolderPath);
            }
            _cache.RecordCheckoutResult(bookId, result, _auth.CurrentUserId, _auth.CurrentEmail);
            _cache.Save();
            return success;
        }

        // ------------------------------------------------------------------
        // Send (PutBookInRepo) and the unified recovery path
        // ------------------------------------------------------------------

        protected override void PutBookInRepo(
            string sourceBookFolderPath,
            BookStatus newStatus,
            bool inLostAndFound = false,
            Action<float> progressCallback = null,
            string checkinComment = null
        )
        {
            var bookFolderName = Path.GetFileName(sourceBookFolderPath);

            // Account-switch takeover (batch item 9): if the repo still shows this book locked
            // to a DIFFERENT account but checked out in THIS copy, take the server lock over BEFORE
            // checking in, so checkin_start_tx's existing (unmodified) "LockHeldByOther" gate
            // sees OUR lock. This is the primary place the takeover actually happens in
            // practice: there is no per-keystroke "book was just edited" hook anywhere in this
            // codebase today (saves are local-only until check-in), so "on first edit" is
            // implemented as "on first check-in of that edit" -- the earliest point a
            // takeover has any observable effect on the shared system anyway. Also covers "B
            // checks in without editing first": OkToCheckIn already allows it (via the same
            // CanTakeOverLockOnThisMachine seam) and this call makes sure the server lock -- not
            // just history attribution -- ends up correctly on B.
            var repoStatusBeforeCheckin = GetStatus(bookFolderName);
            if (CanTakeOverLockOnThisMachine(bookFolderName, repoStatusBeforeCheckin))
                TryTakeOverLock(bookFolderName);

            if (inLostAndFound)
            {
                // The base class uses inLostAndFound to mean "don't overwrite the repo, this content
                // conflicts with what's there" (SyncAtStartup's ConflictingCheckout/ConflictingEdit
                // cases). A cloud repo has no folder-based Lost & Found location to write to (unlike
                // FolderTeamCollection's repo-side "Lost and Found" folder); per the design doc's
                // unified recovery, we instead preserve the content locally and log an incident.
                SaveLocalCopyForRecovery(
                    sourceBookFolderPath,
                    bookFolderName,
                    "ConflictingContent"
                );
                return;
            }

            var meta = BookMetaData.FromFolder(sourceBookFolderPath);
            var bookInstanceId = meta?.Id;
            if (string.IsNullOrEmpty(bookInstanceId))
                throw new ApplicationException(
                    $"Could not read the book id of \"{bookFolderName}\" from its meta.json; cannot send it to the cloud Team Collection."
                );

            // Identity ONLY -- never the folder name (bug #15, John's ruling). Resolving by name
            // here could bind this check-in to a DIFFERENT book that happens to hold the name
            // (e.g. a teammate checked one in while we were offline) and silently overwrite it;
            // an unknown instance id correctly means "first-ever Send of a new book" instead,
            // and the server's name-conflict retry below gives it a distinct name if needed.
            var bookId = TryGetBookIdByInstanceId(bookInstanceId);
            var cachedBook = bookId == null ? null : _cache.TryGetBook(bookId);
            // CONTRACTS.md: checkin-start's `files` is the FULL proposed manifest; the SERVER
            // diffs it against the current version and its response's changedPaths[] tells us
            // what to upload. An earlier version of this method sent only a locally-computed
            // changed-file list, which the server (correctly) treated as the complete manifest —
            // committing versions whose manifests were missing every unchanged file (empty, in
            // the first two-instance smoke test). The local diff is not a substitute: the server
            // is authoritative about what its current version contains.
            var localManifest = BookVersionManifest.FromLocalFolder(sourceBookFolderPath);
            var filesJson = new JArray(
                localManifest.Entries.Select(kvp =>
                    (JToken)
                        new JObject
                        {
                            ["path"] = kvp.Key,
                            ["sha256"] = kvp.Value.Sha256,
                            ["size"] = kvp.Value.Size,
                        }
                )
            );

            // This copy's checkout GUID (null for a new book, or when this copy has none -- then
            // the server either takes a free lock for us or refuses with CheckoutElsewhere /
            // LockHeldByOther).
            var checkoutGuid = CloudCheckoutFile.ReadGuid(sourceBookFolderPath);
            var proposedName = GetBookNameWithoutSuffix(bookFolderName);
            JObject startResult = null;
            CloudCollectionClientException lastNameConflict = null;
            for (var suffix = 1; suffix <= kMaxNameConflictRetries; suffix++)
            {
                try
                {
                    startResult = _client.CheckinStart(
                        _collectionId,
                        bookId,
                        bookInstanceId,
                        proposedName,
                        cachedBook?.CurrentVersionId,
                        newStatus.checksum,
                        Application.ProductVersion,
                        filesJson,
                        checkoutGuid
                    );
                    lastNameConflict = null;
                    break;
                }
                catch (CloudCollectionClientException e)
                    when (e.Code == CloudErrorCode.NameConflict)
                {
                    // Since v1.8 this also comes back when an EXISTING book is renamed (locally,
                    // while checked out) to a name another live book already has. Either way the
                    // "name2" resolution applies (the existing FolderTeamCollection convention for
                    // same-name collisions): the server commits the suffixed name for this book's
                    // row, and the next sync's rename-from-remote pass brings the local folder
                    // name into line, exactly as for a new book.
                    lastNameConflict = e;
                    proposedName = GetBookNameWithoutSuffix(bookFolderName) + (suffix + 1);
                    Logger.WriteEvent(
                        $"CloudTeamCollection: checkin-start reported a name conflict for \"{bookFolderName}\"; retrying as \"{proposedName}\"."
                    );
                }
                catch (CloudCollectionClientException e) when (IsCheckinRefusal(e.Code))
                {
                    throw MakeCheckinRefusedException(sourceBookFolderPath, e);
                }
                catch (CloudCollectionClientException e)
                    when (e.Code == CloudErrorCode.InvalidManifest)
                {
                    throw new ApplicationException(
                        $"The Team Collection server would not accept the list of files in \"{bookFolderName}\": {e.Message}",
                        e
                    );
                }
            }
            if (startResult == null)
                throw lastNameConflict
                    ?? new ApplicationException(
                        $"Could not check in \"{bookFolderName}\": the cloud Team Collection did not return a transaction."
                    );

            // checkin-start issues a new GUID when it takes a free lock for us or creates a new
            // book. The server now holds the lock under it whether or not this transaction goes
            // on to commit, so record it right away.
            var issuedGuid = (string)startResult["checkoutGuid"];
            if (issuedGuid != null)
            {
                checkoutGuid = issuedGuid;
                WriteCheckoutFile(sourceBookFolderPath, bookId, checkoutGuid);
            }

            var transactionId = (string)startResult["transactionId"];
            var location = ParseS3Location(startResult);
            var keepCheckedOut = !string.IsNullOrEmpty(newStatus.lockedBy);
            // Upload what the SERVER says changed relative to its current version (see the
            // manifest comment above) — not a local guess.
            var changedPaths =
                ((JArray)startResult["changedPaths"])?.Select(t => (string)t).ToList()
                ?? new List<string>();

            try
            {
                _transfer.UploadChangedFiles(
                    location,
                    sourceBookFolderPath,
                    changedPaths,
                    cachedBook?.Manifest,
                    // We just hashed the whole folder to build checkin-start's proposed manifest;
                    // passing it here keeps UploadChangedFiles from hashing each file again.
                    localManifest,
                    4,
                    progressCallback == null
                        ? null
                        : new Progress<CloudTransferProgress>(_ => progressCallback(-1f)),
                    CancellationToken.None
                );
                // The comment must reach the server: unlike folder TCs (where the message rides
                // inside history.db within the .bloom file), cloud history is displayed from the
                // server's event log, so a comment we don't send here is invisible to everyone.
                var finishResult = _client.CheckinFinish(
                    transactionId,
                    comment: string.IsNullOrEmpty(checkinComment) ? null : checkinComment,
                    keepCheckedOut: keepCheckedOut
                );
                var versionId = (string)finishResult["versionId"];
                var seq = (long)finishResult["seq"];

                // checkin-start/finish don't return the server-assigned book id for a first-ever
                // Send (CONTRACTS.md gap -- see the task 05 final report); resolve it via a
                // targeted state refresh matched on the stable, client-generated bookInstanceId.
                if (bookId == null)
                {
                    HydrateFromServer();
                    bookId = TryGetBookIdByInstanceId(bookInstanceId);
                }

                // The checkout (and its GUID) ends with the check-in unless it was kept.
                if (!keepCheckedOut)
                    CloudCheckoutFile.Delete(sourceBookFolderPath);
                else if (issuedGuid != null && bookId != null)
                    WriteCheckoutFile(sourceBookFolderPath, bookId, checkoutGuid); // now with the book id

                if (bookId != null)
                {
                    _cache.RecordCheckinFinish(
                        bookId,
                        bookInstanceId,
                        proposedName,
                        versionId,
                        seq,
                        newStatus.checksum,
                        localManifest,
                        keepCheckedOut,
                        // LockedBy is the raw auth user id (matches the server row + checkout_book),
                        // NOT the email CurrentUserIdentity returns — see CloudCachedBook.LockedBy.
                        keepCheckedOut ? _auth.CurrentUserId : null,
                        keepCheckedOut ? TeamCollectionManager.CurrentMachine : null,
                        keepCheckedOut ? _auth.CurrentEmail : null,
                        keepCheckedOut && checkoutGuid != null
                            ? CloudCheckoutFile.HashGuid(checkoutGuid)
                            : null
                    );
                    // We just successfully uploaded this exact version, so the local folder IS this
                    // version now (task 06's "localVersionSeq").
                    _cache.RecordLocalVersionSeq(bookId, seq);
                    _cache.Save();
                    RefreshIndexFromCache();
                }
            }
            catch (CloudCollectionClientException e)
                when (e.Code == CloudErrorCode.TransactionChanged)
            {
                // A concurrent checkin-start resumed this same transaction while this finish was
                // verifying uploads: nothing was committed, and the still-open transaction now
                // belongs to that newer attempt. Aborting it would kill the newer attempt (and, for
                // a new book, delete its uncommitted row), so just stop this one.
                throw new ApplicationException(
                    $"\"{bookFolderName}\" changed while it was being sent to the Team Collection, so Bloom stopped sending it. Please try again.",
                    e
                );
            }
            catch (Exception e)
            {
                try
                {
                    _client.CheckinAbort(transactionId);
                }
                catch (Exception abortException)
                {
                    NonFatalProblem.ReportSentryOnly(abortException);
                }
                // v1.8/v1.9: checkin-finish re-checks the lock, the checkout GUID and the base
                // version. Those refusals are final for this transaction (retrying cannot
                // succeed); the caller must preserve the local work and Receive instead.
                if (
                    e is CloudCollectionClientException clientException
                    && IsCheckinRefusal(clientException.Code)
                )
                    throw MakeCheckinRefusedException(sourceBookFolderPath, clientException);
                throw;
            }
        }

        /// <summary>The check-in outcomes that mean "the repo will not take this content from
        /// this copy" (as opposed to transient failures worth retrying).</summary>
        private static bool IsCheckinRefusal(CloudErrorCode code) =>
            code == CloudErrorCode.LockHeldByOther
            || code == CloudErrorCode.CheckoutElsewhere
            || code == CloudErrorCode.BaseVersionSuperseded;

        /// <summary>
        /// Builds the user-facing <see cref="CloudCheckinRefusedException"/> for a refused
        /// check-in, and removes this copy's `.checkout` record when the refusal means this copy
        /// no longer holds the checkout (LockHeldByOther, CheckoutElsewhere). A
        /// BaseVersionSuperseded refusal leaves the checkout alone: it is still ours, the local
        /// content is merely based on an out-of-date version.
        /// </summary>
        private CloudCheckinRefusedException MakeCheckinRefusedException(
            string sourceBookFolderPath,
            CloudCollectionClientException e
        )
        {
            var bookName = Path.GetFileName(sourceBookFolderPath);
            string reason;
            switch (e.Code)
            {
                case CloudErrorCode.CheckoutElsewhere:
                    CloudCheckoutFile.Delete(sourceBookFolderPath);
                    reason =
                        $"Bloom could not check in \"{bookName}\" from here, because it is checked out to you in another copy of this collection.";
                    break;
                case CloudErrorCode.LockHeldByOther:
                    CloudCheckoutFile.Delete(sourceBookFolderPath);
                    var holder = DescribeLockHolder(e.Details?["holder"]);
                    reason =
                        holder == null
                            ? $"Bloom could not check in \"{bookName}\", because it is no longer checked out to you (for example, an administrator may have unlocked it)."
                            : $"Bloom could not check in \"{bookName}\", because it is now checked out to {holder}.";
                    break;
                default: // BaseVersionSuperseded
                    reason =
                        $"Bloom could not check in \"{bookName}\", because someone checked in a newer version of it since you checked it out.";
                    break;
            }
            return new CloudCheckinRefusedException(e.Code, reason, e);
        }

        /// <summary>Best-effort human-readable form of a LockHeldByOther `holder` (null when the
        /// lock was released, e.g. force-unlocked).</summary>
        private static string DescribeLockHolder(JToken holder)
        {
            if (holder is JObject holderObject)
                return (string)(
                    holderObject["name"]
                    ?? holderObject["locked_by_name"]
                    ?? holderObject["email"]
                    ?? holderObject["locked_by_email"]
                );
            if (holder is JValue holderValue && holderValue.Type == JTokenType.String)
                return (string)holderValue;
            return null;
        }

        /// <summary>
        /// Unified recovery for the inLostAndFound branch of PutBookInRepo: saves the user's local
        /// copy as a `.bloomSource` zip under a local "Lost and Found" folder (there is no repo-side
        /// Lost &amp; Found for a cloud collection, unlike FolderTeamCollection's), and posts a
        /// `log_event` incident. The caller (SyncAtStartup, via PutBook(..., inLostAndFound: true))
        /// is responsible for then Receiving the current repo version into the local folder.
        ///
        /// Note on "distinct messages per sub-case" (task brief): SyncAtStartup itself already logs
        /// a sub-case-specific message via ReportProblemSyncingBook before/around calling PutBook
        /// with inLostAndFound (e.g. "ConflictingCheckout" vs "ConflictingEdit" -- see
        /// TeamCollection.cs, ~line 2415/2539) -- that pre-existing, unchanged base-class logic
        /// already provides the sub-case-specific text. PutBookInRepo's own `inLostAndFound` bool
        /// parameter does NOT itself carry which sub-case triggered it (that would need a base-class
        /// signature change we didn't make -- see the task 05 final report), so the ONE additional
        /// message this method logs is deliberately sub-case-agnostic: it only adds the ".bloomSource
        /// preserved" fact, which none of the existing base messages mention.
        /// </summary>
        private void SaveLocalCopyForRecovery(
            string sourceBookFolderPath,
            string bookFolderName,
            string subCase
        )
        {
            try
            {
                var lostAndFoundDir = Path.Combine(_localCollectionFolder, "Lost and Found");
                // AvailablePath (hoisted to the base TeamCollection) creates the folder and finds
                // a non-colliding "<name>[.N].bloomSource" path.
                var destPath = AvailablePath(bookFolderName, lostAndFoundDir, ".bloomSource");
                var zip = new Bloom.Utils.BloomZipFile(destPath);
                // Never the checkout record: restoring the copy must not resurrect a checkout.
                zip.AddDirectory(
                    sourceBookFolderPath,
                    sourceBookFolderPath.Length + 1,
                    new[] { CloudCheckoutFile.FileName },
                    null
                );
                zip.Save();

                var bookId = ResolveBookId(bookFolderName);
                try
                {
                    _client.LogEvent(
                        _collectionId,
                        bookId,
                        kWorkPreservedLocallyEventType,
                        subCase
                    );
                }
                catch (Exception e)
                {
                    NonFatalProblem.ReportSentryOnly(e);
                }

                MessageLog.WriteMessage(
                    MessageAndMilestoneType.NewStuff,
                    "TeamCollection.Cloud.WorkPreservedLocally",
                    "Your changes to \"{0}\" have been saved to the \"Lost and Found\" folder in your collection, and you now have the latest version from the team.",
                    bookFolderName,
                    null
                );
            }
            catch (Exception e)
            {
                NonFatalProblem.Report(
                    ModalIf.All,
                    PassiveIf.All,
                    $"Bloom could not preserve your local changes to \"{bookFolderName}\" before receiving the version from the Team Collection.",
                    exception: e
                );
            }
        }

        // ------------------------------------------------------------------
        // Receive (FetchBookFromRepo) and single-file reads (GetRepoBookFile)
        // ------------------------------------------------------------------

        protected override string FetchBookFromRepo(
            string destinationCollectionFolder,
            string bookName
        )
        {
            EnsureCacheHydrated();
            var bookId = ResolveBookId(bookName);
            if (bookId == null)
                return $"Could not find the book \"{bookName}\" in the cloud Team Collection.";

            string stagingPath = null;
            try
            {
                var manifest = FetchAndCacheManifest(bookId, out var fetchedVersionSeq);
                if (manifest == null)
                    return $"Could not read the file list for \"{bookName}\" from the cloud Team Collection.";

                var collectionLocation = GetCollectionDownloadLocation();
                var instanceId = _cache.TryGetBook(bookId)?.InstanceId;
                var location = BuildBookS3Location(collectionLocation, instanceId);
                var pinnedFiles = manifest
                    .Entries.Select(kvp => new PinnedFileDownload
                    {
                        RelativePath = kvp.Key,
                        S3VersionId = kvp.Value.S3VersionId,
                        ExpectedSha256Hex = kvp.Value.Sha256,
                        ExpectedSize = kvp.Value.Size,
                    })
                    .ToList();

                var finalPath = Path.Combine(
                    destinationCollectionFolder,
                    GetBookNameWithoutSuffix(bookName)
                );
                stagingPath = finalPath + ".cloudReceive-" + Guid.NewGuid().ToString("N");
                Directory.CreateDirectory(stagingPath);

                // Incremental Receive: seed the staging folder with the LOCAL copies of files that
                // are still part of the target version, so DownloadFiles' hash-skip re-downloads
                // ONLY the files that actually changed. A teammate's rename (or any Receive of a
                // book we already have) then transfers a handful of changed files instead of the
                // whole book. We seed only files that appear in the target manifest (by relative
                // path), so files removed in the new version are simply never carried into staging,
                // and local-only junk (never in the manifest) is left behind -- the swap below then
                // makes finalPath exactly the target version. Seeding is a local copy (cheap next to
                // a network download) and preserves the existing stage-then-atomic-swap guarantee.
                if (Directory.Exists(finalPath))
                {
                    foreach (var relativePath in manifest.Entries.Keys)
                    {
                        var platformRelative = relativePath.Replace(
                            '/',
                            Path.DirectorySeparatorChar
                        );
                        var sourceFile = Path.Combine(finalPath, platformRelative);
                        if (!RobustFile.Exists(sourceFile))
                            continue;
                        var seededFile = Path.Combine(stagingPath, platformRelative);
                        Directory.CreateDirectory(Path.GetDirectoryName(seededFile));
                        RobustFile.Copy(sourceFile, seededFile);
                    }
                    // The checkout record is never part of a version, but receiving a book must
                    // not end its checkout (e.g. Forget Changes receives first and then unlocks,
                    // which needs the GUID). Code that means to end the checkout deletes the
                    // record itself before receiving.
                    var checkoutFile = CloudCheckoutFile.GetPath(finalPath);
                    if (RobustFile.Exists(checkoutFile))
                        RobustFile.Copy(checkoutFile, CloudCheckoutFile.GetPath(stagingPath));
                }

                _transfer.DownloadFiles(
                    location,
                    pinnedFiles,
                    stagingPath,
                    4,
                    null,
                    CancellationToken.None
                );

                // Atomic-as-possible whole-directory swap. CloudBookTransfer.DownloadFiles stages
                // and verifies every file before touching stagingPath at all, but its own final
                // step is a per-file delete+move loop, not a single directory rename (merge log,
                // 7 Jul). We do the actual swap of the BOOK folder here with two directory-rename
                // moves (each atomic on the same volume), so a crash mid-swap leaves either the old
                // folder or the new one intact under finalPath -- never a mix of old and new files.
                string backupPath = null;
                if (Directory.Exists(finalPath))
                {
                    backupPath = finalPath + ".cloudReceiveOld-" + Guid.NewGuid().ToString("N");
                    RobustIO.MoveDirectory(finalPath, backupPath);
                }
                RobustIO.MoveDirectory(stagingPath, finalPath);
                stagingPath = null; // successfully moved; nothing left to clean up
                if (backupPath != null)
                    RobustIO.DeleteDirectoryAndContents(backupPath, true);

                // The whole-book folder now matches the version whose manifest we just fetched and
                // downloaded from -- record it as this machine's local version (book-status JSON's
                // "localVersionSeq"; task 06). Deliberately only done here (a completed whole-book
                // swap), never in GetRepoBookFile's single-file peek, which doesn't update the local
                // folder at all.
                if (fetchedVersionSeq.HasValue)
                {
                    _cache.RecordLocalVersionSeq(bookId, fetchedVersionSeq.Value);
                    _cache.Save();
                }

                return null;
            }
            catch (Exception e)
            {
                return $"Bloom could not download the book \"{bookName}\" from the Team Collection: {e.Message}";
            }
            finally
            {
                if (stagingPath != null && Directory.Exists(stagingPath))
                    RobustIO.DeleteDirectoryAndContents(stagingPath, true);
            }
        }

        public override string GetRepoBookFile(string bookName, string fileName)
        {
            var cacheKey = bookName + "|" + fileName;
            if (_repoFileCache.TryGetValue(cacheKey, out var cachedContent))
                return cachedContent;

            var bookId = TryGetBookId(bookName);
            if (bookId == null)
                return null;

            string tempFolder = null;
            try
            {
                var manifest =
                    _cache.TryGetBook(bookId)?.Manifest ?? FetchAndCacheManifest(bookId, out _);
                if (
                    manifest == null
                    || !manifest.Entries.TryGetValue(
                        BookVersionManifest.NormalizePath(fileName),
                        out var entry
                    )
                )
                    return null;

                var collectionLocation = GetCollectionDownloadLocation();
                var instanceId = _cache.TryGetBook(bookId)?.InstanceId;
                var location = BuildBookS3Location(collectionLocation, instanceId);
                tempFolder = Path.Combine(
                    Path.GetTempPath(),
                    "BloomCloudFile-" + Guid.NewGuid().ToString("N")
                );
                _transfer.DownloadFiles(
                    location,
                    new[]
                    {
                        new PinnedFileDownload
                        {
                            RelativePath = fileName,
                            S3VersionId = entry.S3VersionId,
                            ExpectedSha256Hex = entry.Sha256,
                            ExpectedSize = entry.Size,
                        },
                    },
                    tempFolder,
                    1,
                    null,
                    CancellationToken.None
                );
                var content = RobustFile.ReadAllText(
                    Path.Combine(tempFolder, fileName),
                    System.Text.Encoding.UTF8
                );
                _repoFileCache[cacheKey] = content;
                return content;
            }
            catch (Exception e)
            {
                NonFatalProblem.ReportSentryOnly(
                    e,
                    $"CloudTeamCollection.GetRepoBookFile({bookName}, {fileName})"
                );
                return null;
            }
            finally
            {
                if (tempFolder != null && Directory.Exists(tempFolder))
                    RobustIO.DeleteDirectoryAndContents(tempFolder, true);
            }
        }

        /// <summary>Fetches and caches a book's current manifest, out-parameter also reporting the
        /// version seq it belongs to (get_book_manifest's "seq", v1.2) so Receive can record what
        /// ended up on disk (task 06's "localVersionSeq") without a second round trip.</summary>
        private BookVersionManifest FetchAndCacheManifest(string bookId, out long? versionSeq)
        {
            var manifestResponse = _client.GetBookManifest(bookId);
            if (manifestResponse == null)
            {
                versionSeq = null;
                return null;
            }
            var manifest = BookVersionManifest.FromJson((JArray)manifestResponse["files"]);
            versionSeq = (long?)manifestResponse["seq"];
            // Fail fast on a committed-but-empty manifest rather than "receiving" it, which
            // would atomically swap an EMPTY folder over the local book. No real book has zero
            // files; the only known way to get one is the fixed send-only-changed-paths client
            // bug (7 Jul 2026), but any future cause deserves a loud stop, not silent data loss.
            if (
                manifest.Entries.Count == 0
                && (long?)manifestResponse["seq"] != null
                && manifestResponse["versionId"]?.Type != JTokenType.Null
            )
                throw new ApplicationException(
                    $"The cloud Team Collection's current version of this book (seq {versionSeq}) has an empty file manifest. "
                        + "Refusing to Receive it over the local copy. Someone should check this book in again from a good copy."
                );
            _cache.RecordManifest(bookId, manifest);
            return manifest;
        }

        private static CloudS3Location ParseS3Location(JObject response)
        {
            var s3 = (JObject)response["s3"];
            var creds = (JObject)s3["credentials"];
            return new CloudS3Location
            {
                Bucket = (string)s3["bucket"],
                Region = (string)s3["region"],
                Prefix = (string)s3["prefix"],
                AccessKeyId = (string)creds["accessKeyId"],
                SecretAccessKey = (string)creds["secretAccessKey"],
                SessionToken = (string)creds["sessionToken"],
                ExpiresAtUtc = ParseCredentialExpiration((string)creds["expiration"]),
            };
        }

        /// <summary>Parses the edge function's ISO-8601 `credentials.expiration` to UTC, or
        /// DateTime.MinValue when absent/unparseable (callers then treat the creds as
        /// non-cacheable and simply fetch fresh ones per use).</summary>
        private static DateTime ParseCredentialExpiration(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return DateTime.MinValue;
            return DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var parsed
            )
                ? parsed.UtcDateTime
                : DateTime.MinValue;
        }

        private readonly object _downloadLocationLock = new object();
        private CloudS3Location _cachedDownloadLocation;
        private string _cachedDownloadLocationForUser;

        // Refetch once the cached download credentials are within this margin of their stated
        // expiration, leaving ample headroom for any in-flight download to finish on them.
        private static readonly TimeSpan kDownloadCredsExpiryMargin = TimeSpan.FromMinutes(5);

        /// <summary>
        /// The collection-scoped, read-only S3 credentials from download-start (CONTRACTS.md:
        /// GetObject/GetObjectVersion over the whole `tc/{cid}/` prefix, ~1h TTL), CACHED and
        /// reused across every book and collection-file-group download instead of re-fetched per
        /// download. Because the creds cover the entire collection prefix, one fetch serves a whole
        /// join/Receive; without this a join did one edge-function call plus a server-side STS
        /// AssumeRole for EACH book. Refetched when the cached creds approach their stated
        /// expiration (so long joins refresh mid-way rather than assuming a fixed TTL), or when the
        /// signed-in account changes (the creds were issued for the prior identity). Internal so a
        /// test can drive it directly.
        /// </summary>
        internal CloudS3Location GetCollectionDownloadLocation()
        {
            lock (_downloadLocationLock)
            {
                if (
                    _cachedDownloadLocation != null
                    && _cachedDownloadLocationForUser == CurrentUserIdentity
                    // Add the margin to "now" rather than subtracting it from the expiry, so an
                    // unknown expiry (DateTime.MinValue -> treated as always-stale) can't underflow.
                    && _cachedDownloadLocation.ExpiresAtUtc
                        > DateTime.UtcNow + kDownloadCredsExpiryMargin
                )
                    return _cachedDownloadLocation;

                var location = ParseS3Location(_client.DownloadStart(_collectionId));
                _cachedDownloadLocation = location;
                _cachedDownloadLocationForUser = CurrentUserIdentity;
                return location;
            }
        }

        /// <summary>
        /// download-start's credentials are scoped to the WHOLE collection prefix (`tc/{cid}/`,
        /// covering every book, per CONTRACTS.md: "read: the collection prefix incl.
        /// GetObjectVersion") -- unlike checkin-start's creds, which are already scoped to one
        /// book's own `tc/{cid}/books/{bookInstanceId}/` prefix. A book's manifest entries are bare
        /// relative paths within that book's own folder (e.g. "meta.json"), so any Receive-path
        /// download must insert the `books/{bookInstanceId}/` segment itself before combining with
        /// the collection-level prefix -- CloudBookTransfer builds each S3 key as exactly
        /// `location.Prefix + file.RelativePath`, with no other place that segment could come from.
        /// Discovered via the live round-trip test (a mocked/unit test can't catch this, since it
        /// would have to fake real S3 key-not-found semantics to notice the missing segment).
        /// </summary>
        private static CloudS3Location BuildBookS3Location(
            CloudS3Location collectionLocation,
            string bookInstanceId
        )
        {
            if (string.IsNullOrEmpty(bookInstanceId))
                throw new ApplicationException(
                    "Cannot build a book-scoped S3 location without a bookInstanceId (the book is not yet known to the local cache)."
                );
            return new CloudS3Location
            {
                Bucket = collectionLocation.Bucket,
                Region = collectionLocation.Region,
                Prefix = $"{collectionLocation.Prefix}books/{bookInstanceId}/",
                AccessKeyId = collectionLocation.AccessKeyId,
                SecretAccessKey = collectionLocation.SecretAccessKey,
                SessionToken = collectionLocation.SessionToken,
            };
        }

        // ------------------------------------------------------------------
        // Delete / rename
        // ------------------------------------------------------------------

        public override void DeleteBookFromRepo(string bookFolderPath, bool makeTombstone = true)
        {
            // The cloud delete_book RPC always tombstones (sets deleted_at); there is no
            // "delete without tombstone" mode server-side. The base class's own doc comment on
            // makeTombstone notes we currently never pass false, so this is not a live gap today.
            var bookFolderName = Path.GetFileName(bookFolderPath);
            var bookId = ResolveBookId(bookFolderName);
            if (bookId == null)
                return; // never made it to the repo; nothing to delete there.
            _client.DeleteBook(bookId, CloudCheckoutFile.ReadGuid(bookFolderPath));
            CloudCheckoutFile.Delete(bookFolderPath);
            HydrateFromServer();
        }

        public override void RenameBookInRepo(string newBookFolderPath, string oldName)
        {
            // Deliberately a no-op for the cloud backend. Cloud renames are carried implicitly:
            // the caller (base PutBook) calls this and then immediately calls PutBookInRepo,
            // whose checkin-start sends the NEW name as proposedName for the SAME book id -- the
            // server updates the book row's name at checkin-finish
            // (Design/CloudTeamCollections.md: "Folder keyed by instance id -> rename is a DB row
            // update"). And since every repo lookup for a local folder resolves by the folder's
            // meta.json instance id (bug #15, ResolveBookId), the renamed folder already binds to
            // its row without any bridging state here (an earlier version kept a pending
            // new-name -> id map for exactly that gap).
        }

        protected override void MoveRepoBookToLostAndFound(string bookName)
        {
            // The only caller (SyncAtStartup) uses this for Dropbox-style conflict-marker file
            // names, which cannot occur in a cloud-backed repo -- there is no filesystem-level
            // conflict-copy mechanism to produce them (Design/CloudTeamCollections.md: "most
            // Dropbox-era cases ... become structurally impossible"). Kept as a safety net that
            // reports rather than crashing, since SyncAtStartup is shared, unchanged base logic.
            NonFatalProblem.ReportSentryOnly(
                new InvalidOperationException(
                    $"CloudTeamCollection.MoveRepoBookToLostAndFound('{bookName}') was called; this should be structurally impossible for a cloud-backed repo."
                )
            );
        }

        // ------------------------------------------------------------------
        // Casing
        // ------------------------------------------------------------------

        public override bool DoLocalAndRemoteNamesDifferOnlyByCase(string bookBaseName)
        {
            var cachedBook = ResolveCachedBook(bookBaseName);
            if (cachedBook == null || string.IsNullOrEmpty(cachedBook.Name))
                return false;
            return !string.Equals(cachedBook.Name, bookBaseName, StringComparison.Ordinal)
                && string.Equals(cachedBook.Name, bookBaseName, StringComparison.OrdinalIgnoreCase);
        }

        public override void EnsureConsistentCasingInLocalName(string bookBaseName)
        {
            var cachedBook = ResolveCachedBook(bookBaseName);
            if (cachedBook == null || !DoLocalAndRemoteNamesDifferOnlyByCase(bookBaseName))
                return;
            var localFolderPath = Path.Combine(_localCollectionFolder, bookBaseName);
            if (!Directory.Exists(localFolderPath))
                return;

            var tempName = Guid.NewGuid().ToString("N");
            var tempPath = Path.Combine(_localCollectionFolder, tempName);
            RobustIO.MoveDirectory(localFolderPath, tempPath);
            var finalPath = Path.Combine(_localCollectionFolder, cachedBook.Name);
            RobustIO.MoveDirectory(tempPath, finalPath);

            var htmFileName = Path.Combine(finalPath, bookBaseName + ".htm");
            if (RobustFile.Exists(htmFileName))
            {
                var newHtmFileName = Path.Combine(finalPath, cachedBook.Name + ".htm");
                var tempBookPath = Path.Combine(finalPath, tempName + ".htm");
                RobustFile.Move(htmFileName, tempBookPath);
                RobustFile.Move(tempBookPath, newHtmFileName);
            }
        }

        // ------------------------------------------------------------------
        // Converting the current local collection into a fresh cloud Team Collection
        // (TeamCollectionManager.ConnectToCloudCollection's counterpart to
        // FolderTeamCollection.SetupTeamCollection/SetupTeamCollectionWithProgressDialog).
        // ------------------------------------------------------------------

        /// <summary>
        /// Pushes every existing local book and collection-level file up to this (freshly-linked,
        /// still-empty) cloud collection, then starts monitoring. Called once, right after
        /// TeamCollectionManager.ConnectToCloudCollection creates the server-side row and links the
        /// current local collection to it.
        /// </summary>
        public void SetupCloudTeamCollection(Bloom.web.IWebSocketProgress progress)
        {
            progress.Message(
                "StartingCopy",
                "",
                "Starting to set up the Team Collection",
                Bloom.web.ProgressKind.Progress
            );
            CopyRepoCollectionFilesFromLocal(_localCollectionFolder);
            SynchronizeBooksFromLocalToRepo(progress);
            StartMonitoring();
        }

        /// <summary>Wraps <see cref="SetupCloudTeamCollection"/> with a progress dialog, mirroring
        /// <see cref="FolderTeamCollection.SetupTeamCollectionWithProgressDialog"/>.</summary>
        public void SetupCloudTeamCollectionWithProgressDialog()
        {
            var title = "Setting Up Team Collection"; // matches FolderTeamCollection's own (un-l10n'd) title.
            ShowProgressDialog(
                title,
                (progress, worker) =>
                {
                    try
                    {
                        SetupCloudTeamCollection(progress);
                    }
                    catch (Exception ex)
                    {
                        // this will ensure that progress.HaveProblemsBeenReported is true.
                        progress.MessageWithoutLocalizing(
                            "Something went wrong: " + ex.Message,
                            Bloom.web.ProgressKind.Error
                        );
                    }
                    progress.Message("Done", "Done");
                    return progress.HaveProblemsBeenReported;
                }
            );
        }

        // ------------------------------------------------------------------
        // Connection check
        // ------------------------------------------------------------------

        public override TeamCollectionMessage CheckConnection()
        {
            if (!_auth.IsSignedIn)
                return new TeamCollectionMessage(
                    MessageAndMilestoneType.Error,
                    "TeamCollection.Cloud.NotSignedIn",
                    "Please sign in to your Bloom account to use this Team Collection."
                );
            try
            {
                var collections = _client.MyCollections();
                var isMember = collections.Any(c => (string)c["id"] == _collectionId);
                if (!isMember)
                {
                    // Account-switch behavior (batch item 9): the current logon is not a server
                    // member of this Team Collection -- e.g. it was joined under a different
                    // account. TeamCollectionManager.CheckConnection(allowHardRefusal: true), the
                    // ONLY caller that sets IsAccessRefusal-aware behavior, turns this into a
                    // hard "refuse to open" rather than the ordinary Disconnected fallback; a
                    // membership loss discovered later in the session (this same code path,
                    // called with allowHardRefusal defaulting to false) still just disconnects.
                    return new TeamCollectionMessage(
                        MessageAndMilestoneType.Error,
                        "TeamCollection.Cloud.NotAMemberRefusal",
                        "Bloom cannot open this Team Collection here because {0} is not a member of it. {1}",
                        _auth.CurrentEmail,
                        ComposeNotAMemberRefusalDetail(
                            ReadLocalAdministrators(),
                            TeamCollectionLastKnownUser.Read(_localCollectionFolder)
                        )
                    )
                    {
                        IsAccessRefusal = true,
                    };
                }
                // Confirmed as a member: make sure the membership is CLAIMED (user_id filled on
                // the membership row). my_collections above matches by EMAIL, approved-or-claimed,
                // but every data RPC's RLS gate (get_collection_state etc.) matches by user_id --
                // an approved-but-never-claimed account passes this check and then throws
                // not_a_member on the very first sync. That is exactly the batch item 9
                // shared-computer scenario: the account opening the collection never ran the join
                // flow (which is where ClaimMemberships was otherwise called -- CloudJoinFlow),
                // because a different account joined this folder. Found live: e2e-10's member
                // reopen hit the not_a_member throw inside TeamCollectionManager's constructor.
                // Idempotent and cheap; once per ACCOUNT (not per session -- an in-session
                // sign-out + sign-in as a different approved member must claim again; see the
                // field's own comment).
                if (
                    !string.Equals(
                        _membershipsClaimedForEmail,
                        _auth.CurrentEmail,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    _client.ClaimMemberships();
                    _membershipsClaimedForEmail = _auth.CurrentEmail;
                }
                // Record ourselves as the last known local user of this
                // collection on this machine, so a FUTURE non-member's refusal message (above)
                // can name us. Doubles as "who joined" for a collection nobody has reopened
                // since (see TeamCollectionLastKnownUser's own doc comment).
                TeamCollectionLastKnownUser.Record(_localCollectionFolder, _auth.CurrentEmail);
            }
            catch (CloudCollectionClientException e) when (e.Code == CloudErrorCode.NotSignedIn)
            {
                return new TeamCollectionMessage(
                    MessageAndMilestoneType.Error,
                    "TeamCollection.Cloud.NotSignedIn",
                    "Please sign in to your Bloom account to use this Team Collection."
                );
            }
            catch (Exception e)
            {
                return new TeamCollectionMessage(
                    MessageAndMilestoneType.Error,
                    "TeamCollection.Cloud.NoConnection",
                    "Bloom could not reach the Team Collection server. Please check your internet connection. ({0})",
                    e.Message
                );
            }
            return null;
        }

        /// <summary>
        /// Best-effort read of the locally-known Administrators list (from the last-synced
        /// .bloomCollection file), for use in the non-member refusal message: a non-member
        /// cannot query the server's members/admin list (tc.members_list's RLS gate filters out
        /// all rows for a non-member -- verified in the members_list migration), so this is
        /// "whatever is locally known" as the batch item's spec anticipates. NOTE (documented
        /// limitation, tracked separately -- see the batch file's "Also queued from dogfooding"
        /// item): ConnectToCloudCollection currently stamps Administrators with the CREATOR's
        /// Bloom REGISTRATION email rather than their signed-in cloud email, so this list may not
        /// exactly match the admin's cloud logon; fixing that identity mismatch is out of scope
        /// here and is tracked as a separate opportunistic fix.
        /// </summary>
        private string[] ReadLocalAdministrators()
        {
            try
            {
                var settingsPath = Bloom.Collection.CollectionSettings.GetSettingsFilePath(
                    _localCollectionFolder
                );
                var settings = Bloom.ProjectContext.GetCollectionSettings(settingsPath);
                return settings?.Administrators;
            }
            catch (Exception)
            {
                // No usable local .bloomCollection file yet (e.g. this machine has never
                // synced collection files at all) -- ComposeNotAMemberRefusalDetail already
                // handles "administrators unknown" gracefully, falling back to naming only
                // the last known local user (if that's known) or a generic "an administrator".
                return null;
            }
        }

        /// <summary>
        /// Pure, unit-testable composition of the second sentence of the non-member refusal
        /// message: names admin(s) to ask, and/or the last known local team member, depending on
        /// what's actually known locally (both may be unavailable -- e.g. a legacy collection
        /// with no recorded Administrators and no TeamCollectionLastKnownUser.txt yet).
        /// </summary>
        internal static string ComposeNotAMemberRefusalDetail(
            IReadOnlyCollection<string> administrators,
            string lastKnownUser
        )
        {
            var adminList =
                administrators == null
                    ? null
                    : string.Join(", ", administrators.Where(a => !string.IsNullOrWhiteSpace(a)));
            var haveAdmins = !string.IsNullOrEmpty(adminList);
            var haveLastKnownUser = !string.IsNullOrEmpty(lastKnownUser);

            if (haveAdmins && haveLastKnownUser)
                return string.Format(
                    "Ask an administrator of this Team Collection ({0}) to add you as a member, or ask {1}, the last team member known to have used this collection on this computer.",
                    adminList,
                    lastKnownUser
                );
            if (haveAdmins)
                return string.Format(
                    "Ask an administrator of this Team Collection ({0}) to add you as a member.",
                    adminList
                );
            if (haveLastKnownUser)
                return string.Format(
                    "Ask {0}, the last team member known to have used this collection on this computer, or another administrator of this Team Collection, to add you as a member.",
                    lastKnownUser
                );
            return "Ask an administrator of this Team Collection to add you as a member.";
        }

        // ------------------------------------------------------------------
        // Obsolete `.checkout` records (CONTRACTS.md v1.9)
        // ------------------------------------------------------------------

        // Folder names of books whose obsolete checkout could not be cancelled yet because the
        // book might be open for editing (see ReconcileCheckoutFiles); retried on every poll.
        private readonly HashSet<string> _deferredObsoleteCheckouts = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly object _reconcileGate = new object();

        /// <summary>
        /// Collection open: cancel obsolete checkouts before SyncAtStartup's shared passes look at
        /// any book (they would otherwise see a book this copy no longer holds as checked out
        /// here, or as a conflict).
        /// </summary>
        protected override bool PrepareLocalBooksForSyncAtStartup(
            Bloom.web.IWebSocketProgress progress
        )
        {
            EnsureCacheHydrated();
            return ReconcileCheckoutFiles(null, progress, deferIfPossiblyBeingEdited: false);
        }

        /// <summary>
        /// Finds book folders whose `.checkout` record is obsolete and cancels those checkouts in
        /// this copy. A record is obsolete when the server row (as last hydrated or polled) is
        /// unlocked, or its checkoutGuidHash is not the hash of the record's GUID: someone checked
        /// the book in or out from another copy (e.g. the other half of a duplicated collection
        /// folder), an administrator force-unlocked it, or another account now holds it (every
        /// way the lock can change hands issues a new GUID, except an account-switch takeover,
        /// which only the copy holding the GUID can do). Cancelling means: if the local book
        /// changed since the last sync, save it to Lost and Found (WorkPreservedLocally incident,
        /// sub-case "ObsoleteCheckout"); remove the record; receive the repo version.
        ///
        /// Runs only right after the cache was refreshed from the server (collection open, and
        /// each poll that touched books), so while disconnected a record is trusted as it stands.
        /// A book the user might be editing (it is the selected book) is not received under
        /// them: it already reads as not checked out here, so it has become read-only; its
        /// cancellation is retried on every later poll -- a change of selection triggers one --
        /// and at the next open.
        /// </summary>
        /// <param name="onlyBookIds">When not null, look only at these server book ids (plus
        /// any previously deferred books).</param>
        /// <param name="progress">Startup progress to report to, or null (polling).</param>
        /// <returns>true if any local work was saved to Lost and Found.</returns>
        internal bool ReconcileCheckoutFiles(
            ICollection<string> onlyBookIds,
            Bloom.web.IWebSocketProgress progress,
            bool deferIfPossiblyBeingEdited
        )
        {
            var preservedAny = false;
            lock (_reconcileGate)
            {
                foreach (var folderPath in Directory.EnumerateDirectories(_localCollectionFolder))
                {
                    // A missing or unreadable record means this copy has no checkout to cancel.
                    if (CloudCheckoutFile.Read(folderPath) == null)
                        continue;
                    var folderName = Path.GetFileName(folderPath);
                    var book = ResolveCachedBook(folderName);
                    // Nothing to compare against or receive for a book the repo doesn't know, has
                    // never committed, or has deleted (remote deletes have their own handling).
                    if (book == null || !book.CurrentVersionSeq.HasValue || book.DeletedAt.HasValue)
                        continue;
                    if (
                        onlyBookIds != null
                        && !onlyBookIds.Contains(book.Id)
                        && !_deferredObsoleteCheckouts.Contains(folderName)
                    )
                        continue;
                    if (IsCheckedOutInThisCopy(book, folderName))
                    {
                        _deferredObsoleteCheckouts.Remove(folderName);
                        continue;
                    }
                    if (deferIfPossiblyBeingEdited && IsPossiblyBeingEdited(folderPath))
                    {
                        _deferredObsoleteCheckouts.Add(folderName);
                        continue;
                    }
                    _deferredObsoleteCheckouts.Remove(folderName);
                    preservedAny |= CancelObsoleteCheckout(folderName, progress);
                }
            }
            return preservedAny;
        }

        /// <summary>The Edit tab only ever edits the selected book, so any other book is
        /// certainly not being edited.</summary>
        private bool IsPossiblyBeingEdited(string bookFolderPath) =>
            string.Equals(
                _tcManager?.BookSelection?.CurrentSelection?.FolderPath,
                bookFolderPath,
                StringComparison.OrdinalIgnoreCase
            );

        /// <summary>
        /// The "cancel" half of <see cref="ReconcileCheckoutFiles"/> for one book: preserve local
        /// changes (if any) in Lost and Found, remove the `.checkout` record, receive the repo
        /// version. Returns true if local work was preserved.
        /// </summary>
        private bool CancelObsoleteCheckout(
            string bookFolderName,
            Bloom.web.IWebSocketProgress progress
        )
        {
            var bookFolderPath = Path.Combine(_localCollectionFolder, bookFolderName);
            var preserved = IsLocalCopyModifiedSinceLastSync(bookFolderName);
            if (preserved)
                SaveLocalCopyForRecovery(bookFolderPath, bookFolderName, "ObsoleteCheckout");
            CloudCheckoutFile.Delete(bookFolderPath);
            var message =
                $"\"{bookFolderName}\" is no longer checked out in this copy of the collection (it was checked in or out from somewhere else, or unlocked by an administrator), so Bloom has switched it to the Team Collection's version.";
            Logger.WriteEvent("CloudTeamCollection: " + message);
            progress?.MessageWithoutLocalizing(
                message,
                preserved ? Bloom.web.ProgressKind.Warning : Bloom.web.ProgressKind.Progress
            );
            var error = CopyBookFromRepoToLocal(bookFolderName);
            if (error != null)
            {
                Logger.WriteEvent(
                    $"CloudTeamCollection: receiving \"{bookFolderName}\" after cancelling its obsolete checkout failed: {error}"
                );
                progress?.MessageWithoutLocalizing(error, Bloom.web.ProgressKind.Error);
            }
            if (progress == null)
            {
                // Polling: refresh the book's status display, and its preview if it is showing.
                UpdateBookStatus(bookFolderName, true);
                if (IsPossiblyBeingEdited(bookFolderPath))
                    _tcManager?.SendBookContentReload();
            }
            return preserved;
        }

        // ------------------------------------------------------------------
        // Monitoring (polling; see CloudCollectionMonitor)
        // ------------------------------------------------------------------

        protected internal override void StartMonitoring()
        {
            base.StartMonitoring();
            EnsureCacheHydrated();
            // Self-healing pass (see the method's own doc): monitoring starts right after the
            // startup sync, so this catches any repo book the sync failed to queue for download
            // (e.g. the in-memory queue lost to a kill/crash between a progressive join's pullDown
            // and the relaunch).
            QueueMissingRepoBooksForBackgroundDownload();
            _monitor = new CloudCollectionMonitor(
                _client,
                _collectionId,
                _cache.LastSeenEventId,
                OnPolledChanges,
                pollInterval: _environment.PollInterval
            );
            _monitor.Start();
        }

        protected internal override void StopMonitoring()
        {
            _monitor?.Dispose();
            _monitor = null;
            base.StopMonitoring();
        }

        /// <summary>
        /// Applies one batch of get_changes results to the cache and raises the same low-level
        /// events FolderTeamCollection's FileSystemWatcher callbacks raise, so all the shared
        /// base-class idle-time handling (HandleNewBook/HandleModifiedFile/HandleDeletedRepoFile/
        /// message log entries) works unchanged for the cloud backend too. Because
        /// CloudCollectionMonitor's polling cursor is the same last_seen_event_id this class
        /// persists, an event we caused ourselves (e.g. our own checkin) is already reflected in the
        /// cache by the time the next poll's delta arrives, so comparing before/after cache state
        /// here naturally suppresses raising a change notification for our own writes.
        /// </summary>
        private void OnPolledChanges(JObject changes)
        {
            // An idle poll (no touched books, cursor unchanged) makes ApplyDelta a no-op: the cache
            // content and its index are already current and no Raise*/socket event below could
            // fire. Skip the full-cache Save + index rebuild + book-event pass in that case (E3) --
            // otherwise every 60s poll rewrites the whole repo-cache file to disk while nothing has
            // changed. The book snapshot is only needed to diff for those events, so only take it
            // when the poll actually carried book rows. The group-file check and the self-healing
            // download pass below run on EVERY poll regardless (see their own notes).
            var hasBookRows = changes["books"] is JArray booksArray && booksArray.Count > 0;
            var previousBooksById = hasBookRows
                ? _cache.GetAllBooks().ToDictionary(b => b.Id)
                : null;

            var cacheChanged = _cache.ApplyDelta(changes);
            if (cacheChanged)
            {
                _cache.Save();
                RefreshIndexFromCache();
            }

            // v1.9: a poll that touched a book may have made this copy's checkout of it obsolete
            // (checked in or out from another copy, force-unlocked...). Cancel those before
            // raising the change events, so their handlers see the book as it now is here. Books
            // deferred earlier because they might have been open for editing are retried on
            // every poll, idle or not (a selection change triggers one).
            var touchedBookIds = hasBookRows
                ? new HashSet<string>(
                    ((JArray)changes["books"]).OfType<JObject>().Select(row => (string)row["id"])
                )
                : new HashSet<string>();
            bool anyDeferred;
            lock (_reconcileGate)
                anyDeferred = _deferredObsoleteCheckouts.Count > 0;
            if (touchedBookIds.Count > 0 || anyDeferred)
                ReconcileCheckoutFiles(touchedBookIds, null, deferIfPossiblyBeingEdited: true);

            if (cacheChanged && hasBookRows)
                RaiseBookEventsForPolledChanges(previousBooksById);

            if (changes["groups"] is JArray groupsArray && groupsArray.Count > 0)
                RaiseRepoCollectionFilesChanged();

            // Self-healing pass: a repo book missing locally whose repo state did NOT change this
            // poll raises none of the events above, so without this it would never be retried
            // (found the hard way -- see QueueMissingRepoBooksForBackgroundDownload's doc).
            QueueMissingRepoBooksForBackgroundDownload();
        }

        /// <summary>
        /// Raises the per-book Raise*/socket notifications for a poll that actually touched books,
        /// by diffing the current cache against <paramref name="previousBooksById"/> (the snapshot
        /// taken before ApplyDelta). Split out of OnPolledChanges so the idle-poll fast path (E3)
        /// stays readable.
        /// </summary>
        private void RaiseBookEventsForPolledChanges(
            Dictionary<string, CloudCachedBook> previousBooksById
        )
        {
            foreach (var book in _cache.GetAllBooks())
            {
                if (string.IsNullOrEmpty(book.Name))
                    continue;
                // The Raise* event contract (and the base handlers behind it) use the folder
                // backend's repo FILE name, i.e. WITH the ".bloom" suffix — HandleModifiedFile
                // literally starts with EndsWith(".bloom") and silently ignores anything else.
                // Passing the bare book name here meant every cloud change notification was
                // discarded before reaching the UI (found by the first two-instance smoke test:
                // teammates' screens never updated even though the cache/API had fresh data).
                var bookFileName = book.Name + ".bloom";
                if (!previousBooksById.TryGetValue(book.Id, out var previous))
                {
                    if (book.CurrentVersionSeq.HasValue)
                        RaiseNewBook(bookFileName);
                    continue;
                }
                if (book.DeletedAt.HasValue && !previous.DeletedAt.HasValue)
                {
                    RaiseDeleteRepoBookFile(bookFileName);
                    continue;
                }
                // CheckoutGuidHash too: a new checkout of the book by the SAME account (from
                // another copy) changes only the GUID, yet it changes whether the book is checked
                // out HERE.
                if (
                    book.CurrentVersionSeq != previous.CurrentVersionSeq
                    || book.LockedBy != previous.LockedBy
                    || book.CheckoutGuidHash != previous.CheckoutGuidHash
                    || book.Name != previous.Name
                )
                {
                    RaiseBookStateChange(bookFileName);
                }
            }

            // Task 06: the status button's "Updates Available (N books)" metadata
            // (teamCollection/tcStatusMetadata) can only have changed if this poll actually touched
            // any book -- and this method only runs when it did -- so push the same "reuse existing
            // contexts" websocket plumbing the rest of this class already uses (RaiseBookStateChange
            // etc. above) so the UI refreshes without waiting for its own next poll.
            SocketServer?.SendEvent("teamCollection", "statusMetadataChanged");
        }

        /// <summary>Lets UI code (e.g. a "Receive Updates" button, or Bloom regaining focus) trigger
        /// an immediate poll instead of waiting for the periodic timer.</summary>
        public void PollNow() => _monitor?.PollNow();
    }
}
