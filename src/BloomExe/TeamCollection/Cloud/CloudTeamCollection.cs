using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
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
    public class CloudTeamCollection : TeamCollection
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

        // Set by RenameBookInRepo (called by the base PutBook, immediately before PutBookInRepo,
        // whenever a rename is pending) so the following PutBookInRepo call can resolve the book id
        // under the NEW folder name even though the cache/index still only know the OLD name until
        // checkin-finish's subsequent HydrateFromServer catches up.
        private readonly Dictionary<string, string> _pendingRenameBookId = new Dictionary<
            string,
            string
        >(StringComparer.OrdinalIgnoreCase);

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
            {
                auth = new CloudAuth(CloudAuth.CreateProvider(_environment));
                auth.InitializeAtStartup(_environment);
            }
            _auth = auth;
            _client = client ?? new CloudCollectionClient(_environment, _auth);
            _transfer = transfer ?? new CloudBookTransfer();
            _cache = CloudRepoCache.LoadOrCreate(localCollectionFolder);
            RefreshIndexFromCache();
        }

        public string CollectionIdForCloud => _collectionId;

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

        /// <summary>The version seq of what's currently on THIS machine's disk for a book, or null
        /// if never Sent/Received here (book-status JSON's "localVersionSeq").</summary>
        public long? GetLocalVersionSeq(string bookFolderName) =>
            TryGetCachedBook(bookFolderName)?.LocalVersionSeq;

        /// <summary>The latest version seq known to be in the repo for a book, or null if the book
        /// isn't cached at all (book-status JSON's "repoVersionSeq").</summary>
        public long? GetRepoVersionSeq(string bookFolderName) =>
            TryGetCachedBook(bookFolderName)?.CurrentVersionSeq;

        /// <summary>
        /// Count of live, currently-unlocked books whose repo version is newer than what's on this
        /// machine -- drives the status button's "Updates Available (N books)" metadata. A book
        /// this machine has never Received (LocalVersionSeq null) counts too: from this machine's
        /// point of view it equally needs a Receive before it's current.
        /// </summary>
        public int GetUpdatesAvailableCount()
        {
            EnsureCacheHydrated();
            return _cache
                .GetAllBooks()
                .Count(b =>
                    !b.DeletedAt.HasValue
                    && b.CurrentVersionSeq.HasValue
                    && string.IsNullOrEmpty(b.LockedBy)
                    && (b.LocalVersionSeq ?? -1) < b.CurrentVersionSeq.Value
                );
        }

        // ------------------------------------------------------------------
        // Capability flags / simple identity members
        // ------------------------------------------------------------------

        public override string GetBackendType() => "Cloud";

        public override string RepoDescription => $"cloud://sil.bloom/collection/{_collectionId}";

        public override bool SupportsVersionHistory => true;

        public override bool SupportsSharingUi => true;

        public override bool RequiresSignIn => true;

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

        private string TryGetBookId(string bookName)
        {
            lock (_indexGate)
            {
                if (_pendingRenameBookId.TryGetValue(bookName, out var pendingId))
                    return pendingId;
                return _bookIdByName.TryGetValue(bookName, out var id) ? id : null;
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

        private BookStatus StatusFromCachedBook(CloudCachedBook book, string collectionId)
        {
            return new BookStatus
            {
                checksum = book.CurrentChecksum,
                lockedBy = ResolveLockedByForDisplay(book),
                // The server book row has no reliable first/last-name split (task 06's
                // 20260707000006 migration adds a best-effort whole display name -- surfaced via
                // the book-status JSON's separate lockedByEmail/lockedByName fields instead, since
                // stuffing a whole name into "FirstName" with a null Surname would render as
                // "Name null" in the existing TS template `${whoFirstName} ${whoSurname}`).
                // Left null here; see the task 05 final report's contract-ambiguity note.
                lockedByFirstName = null,
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
            var cachedBook = TryGetCachedBook(bookFolderName);
            if (cachedBook == null || !cachedBook.CurrentVersionSeq.HasValue)
            {
                // Either genuinely absent from the repo, or a never-committed new book (invisible to
                // teammates per CONTRACTS.md) -- both are the valid "no repo file" case, not an error.
                status = null;
                return true;
            }
            status = StatusFromCachedBook(cachedBook, CollectionId).ToJson();
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
            var cachedBook = TryGetCachedBook(bookFolderName);
            return cachedBook != null && cachedBook.CurrentVersionSeq.HasValue;
        }

        public override bool KnownToHaveBeenDeleted(string oldName)
        {
            var cachedBook = TryGetCachedBook(oldName);
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
            var bookId = TryGetBookId(bookName);
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
                && newStatus.lockedBy == TeamCollectionManager.CurrentUser
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
        /// exactly what lets AttemptLock's caller show "checked out by X" immediately).</summary>
        protected override bool TryLockInRepo(string bookName, BookStatus newStatus)
        {
            var bookId = TryGetBookId(bookName);
            if (bookId == null)
                return true; // brand-new, never-committed local book; nothing to lock server-side yet.

            var result = _client.CheckoutBook(bookId, TeamCollectionManager.CurrentMachine);
            _cache.RecordCheckoutResult(bookId, result);
            _cache.Save();
            return (bool?)result["success"] ?? false;
        }

        /// <summary>Single RPC unlock/force-unlock, per CONTRACTS.md's unlock_book/force_unlock.</summary>
        protected override void UnlockInRepo(string bookName, bool force)
        {
            var bookId = TryGetBookId(bookName);
            if (bookId == null)
                return;
            if (force)
                _client.ForceUnlockRpc(bookId);
            else
                _client.UnlockBookRpc(bookId);
            _cache.RecordUnlock(bookId);
            _cache.Save();
        }

        // ------------------------------------------------------------------
        // Send (PutBookInRepo) and the unified recovery path
        // ------------------------------------------------------------------

        protected override void PutBookInRepo(
            string sourceBookFolderPath,
            BookStatus newStatus,
            bool inLostAndFound = false,
            Action<float> progressCallback = null
        )
        {
            var bookFolderName = Path.GetFileName(sourceBookFolderPath);

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

            var bookId = TryGetBookId(bookFolderName) ?? TryGetBookIdByInstanceId(bookInstanceId);
            var cachedBook = bookId == null ? null : _cache.TryGetBook(bookId);
            var previousManifest = cachedBook?.Manifest ?? new BookVersionManifest();
            var diff = previousManifest.DiffAgainstLocalFolder(sourceBookFolderPath);
            var changedPaths = diff.Where(d =>
                    d.Kind == ManifestDiffKind.Added || d.Kind == ManifestDiffKind.Changed
                )
                .Select(d => d.Path)
                .ToList();
            var localManifest = BookVersionManifest.FromLocalFolder(sourceBookFolderPath);
            var filesJson = new JArray(
                changedPaths.Select(p =>
                {
                    var entry = localManifest.Entries[p];
                    return (JToken)
                        new JObject
                        {
                            ["path"] = p,
                            ["sha256"] = entry.Sha256,
                            ["size"] = entry.Size,
                        };
                })
            );

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
                        filesJson
                    );
                    lastNameConflict = null;
                    break;
                }
                catch (CloudCollectionClientException e)
                    when (e.Code == CloudErrorCode.NameConflict)
                {
                    lastNameConflict = e;
                    // "name2" resolution, per the task brief and existing FolderTeamCollection
                    // convention for same-name collisions.
                    proposedName = GetBookNameWithoutSuffix(bookFolderName) + (suffix + 1);
                }
            }
            if (startResult == null)
                throw lastNameConflict
                    ?? new ApplicationException(
                        $"Could not check in \"{bookFolderName}\": the cloud Team Collection did not return a transaction."
                    );

            var transactionId = (string)startResult["transactionId"];
            var location = ParseS3Location(startResult);
            var keepCheckedOut = !string.IsNullOrEmpty(newStatus.lockedBy);

            try
            {
                _transfer.UploadChangedFiles(
                    location,
                    sourceBookFolderPath,
                    changedPaths,
                    cachedBook?.Manifest,
                    new HashSet<string>(),
                    4,
                    progressCallback == null
                        ? null
                        : new Progress<CloudTransferProgress>(_ => progressCallback(-1f)),
                    CancellationToken.None
                );
                var finishResult = _client.CheckinFinish(
                    transactionId,
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
                        keepCheckedOut ? TeamCollectionManager.CurrentUser : null,
                        keepCheckedOut ? TeamCollectionManager.CurrentMachine : null
                    );
                    // We just successfully uploaded this exact version, so the local folder IS this
                    // version now (task 06's "localVersionSeq").
                    _cache.RecordLocalVersionSeq(bookId, seq);
                    _cache.Save();
                    RefreshIndexFromCache();
                }
            }
            catch (Exception)
            {
                try
                {
                    _client.CheckinAbort(transactionId);
                }
                catch (Exception abortException)
                {
                    NonFatalProblem.ReportSentryOnly(abortException);
                }
                throw;
            }
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
                Directory.CreateDirectory(lostAndFoundDir);
                var destPath = GetAvailableBloomSourcePath(lostAndFoundDir, bookFolderName);
                var zip = new Bloom.Utils.BloomZipFile(destPath);
                zip.AddDirectory(sourceBookFolderPath, sourceBookFolderPath.Length + 1, null, null);
                zip.Save();

                var bookId = TryGetBookId(bookFolderName);
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

        private static string GetAvailableBloomSourcePath(string folder, string bookFolderName)
        {
            var counter = 0;
            string path;
            do
            {
                counter++;
                path =
                    Path.Combine(folder, bookFolderName + (counter == 1 ? "" : counter.ToString()))
                    + ".bloomSource";
            } while (RobustFile.Exists(path));
            return path;
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
            var bookId = TryGetBookId(bookName);
            if (bookId == null)
                return $"Could not find the book \"{bookName}\" in the cloud Team Collection.";

            string stagingPath = null;
            try
            {
                var manifest = FetchAndCacheManifest(bookId, out var fetchedVersionSeq);
                if (manifest == null)
                    return $"Could not read the file list for \"{bookName}\" from the cloud Team Collection.";

                var download = _client.DownloadStart(_collectionId);
                var collectionLocation = ParseS3Location(download);
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

                var download = _client.DownloadStart(_collectionId);
                var collectionLocation = ParseS3Location(download);
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
            };
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
            var bookId = TryGetBookId(bookFolderName);
            if (bookId == null)
                return; // never made it to the repo; nothing to delete there.
            _client.DeleteBookRpc(bookId);
            HydrateFromServer();
        }

        public override void RenameBookInRepo(string newBookFolderPath, string oldName)
        {
            // Cloud renames are carried implicitly: the caller (base PutBook) calls this and then
            // immediately calls PutBookInRepo, whose checkin-start sends the NEW name as
            // proposedName for the SAME book id -- the server updates the book row's name at
            // checkin-finish (Design/CloudTeamCollections.md: "Folder keyed by instance id -> rename
            // is a DB row update"). All we need to do here is make sure PutBookInRepo can resolve
            // the book id under the new name before the cache/index catch up.
            var newName = Path.GetFileName(newBookFolderPath);
            var bookId = TryGetBookId(oldName);
            if (bookId != null)
            {
                lock (_indexGate)
                    _pendingRenameBookId[newName] = bookId;
            }
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
            var cachedBook = TryGetCachedBook(bookBaseName);
            if (cachedBook == null || string.IsNullOrEmpty(cachedBook.Name))
                return false;
            return !string.Equals(cachedBook.Name, bookBaseName, StringComparison.Ordinal)
                && string.Equals(cachedBook.Name, bookBaseName, StringComparison.OrdinalIgnoreCase);
        }

        public override void EnsureConsistentCasingInLocalName(string bookBaseName)
        {
            var cachedBook = TryGetCachedBook(bookBaseName);
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
        // Collection-level files (bloomCollection/customCollectionStyles.css/configuration.txt/
        // ReaderTools*.json via group "other"; Allowed Words/Sample Texts via their own groups)
        // ------------------------------------------------------------------

        public override void PutCollectionFiles(string[] names)
        {
            UploadCollectionFileGroup("other", names.ToList(), _localCollectionFolder);
        }

        protected override void CopyLocalFolderToRepo(string folderName)
        {
            var sourceDir = Path.Combine(_localCollectionFolder, folderName);
            if (!Directory.Exists(sourceDir))
                return;
            var groupKey = MapFolderNameToGroupKey(folderName);
            var relativeNames = Directory
                .EnumerateFiles(sourceDir)
                .Select(Path.GetFileName)
                .ToList();
            UploadCollectionFileGroup(groupKey, relativeNames, sourceDir);
        }

        private static string MapFolderNameToGroupKey(string folderName) =>
            folderName switch
            {
                "Allowed Words" => "allowed-words",
                "Sample Texts" => "sample-texts",
                _ => "other",
            };

        /// <summary>
        /// collection-files-start/finish, the two-phase protocol CONTRACTS.md describes as "like
        /// check-in" for one collection-file group. The exact response shape isn't spelled out in
        /// CONTRACTS.md beyond "finish bumps the group version atomically"; this assumes a
        /// checkin-start-like `{transactionId, s3}` / `{version}` shape -- flagged as a contract
        /// ambiguity in the task 05 final report.
        /// </summary>
        private void UploadCollectionFileGroup(
            string groupKey,
            List<string> relativeFileNames,
            string sourceFolder
        )
        {
            var files = new List<(string path, string sha256, long size)>();
            foreach (var name in relativeFileNames)
            {
                var fullPath = Path.IsPathRooted(name) ? name : Path.Combine(sourceFolder, name);
                if (!RobustFile.Exists(fullPath))
                    continue;
                var (sha256, size) = BookVersionManifest.ComputeFileHash(fullPath);
                files.Add((Path.GetFileName(fullPath), sha256, size));
            }
            if (files.Count == 0)
                return;

            var filesJson = new JArray(
                files.Select(f =>
                    (JToken)
                        new JObject
                        {
                            ["path"] = f.path,
                            ["sha256"] = f.sha256,
                            ["size"] = f.size,
                        }
                )
            );
            var expectedVersion = _cache.TryGetGroup(groupKey)?.Version ?? 0;
            var startResult = _client.CollectionFilesStart(
                _collectionId,
                groupKey,
                expectedVersion,
                filesJson
            );
            var transactionId = (string)startResult["transactionId"];
            var location = ParseS3Location(startResult);
            _transfer.UploadChangedFiles(
                location,
                sourceFolder,
                files.Select(f => f.path),
                null,
                new HashSet<string>(),
                4,
                null,
                CancellationToken.None
            );
            var finishResult = _client.CollectionFilesFinish(transactionId);
            var newVersion = (long?)(finishResult?["version"]) ?? (expectedVersion + 1);
            _cache.RecordCollectionFilesFinish(groupKey, newVersion);
            _cache.Save();
        }

        protected override void CopyRepoCollectionFilesToLocalImpl(string destFolder)
        {
            _collectionLock.UnlockFor(() => DownloadCollectionFileGroup("other", destFolder));
            DownloadCollectionFileGroup("allowed-words", Path.Combine(destFolder, "Allowed Words"));
            DownloadCollectionFileGroup("sample-texts", Path.Combine(destFolder, "Sample Texts"));
        }

        /// <summary>
        /// Downloads every file currently in one collection-file group directly from S3 (listing the
        /// group's prefix, since CONTRACTS.md defines no manifest RPC for collection-file groups --
        /// only a version-bump counter via get_collection_state; see the task 05 final report's
        /// contract-gap note). Unlike book content, this reads "latest", not a pinned version -- an
        /// acknowledged deviation from the pinned-read invariant, forced by the missing manifest.
        /// </summary>
        private void DownloadCollectionFileGroup(string groupKey, string destFolder)
        {
            try
            {
                var download = _client.DownloadStart(_collectionId);
                var location = ParseS3Location(download);
                var s3Client = BuildS3Client(location);
                var prefix = $"{location.Prefix}collectionFiles/{groupKey}/";

                var keys = new List<string>();
                string continuationToken = null;
                do
                {
                    var response = s3Client
                        .ListObjectsV2Async(
                            new ListObjectsV2Request
                            {
                                BucketName = location.Bucket,
                                Prefix = prefix,
                                ContinuationToken = continuationToken,
                            }
                        )
                        .GetAwaiter()
                        .GetResult();
                    keys.AddRange(response.S3Objects.Select(o => o.Key));
                    continuationToken =
                        response.IsTruncated == true ? response.NextContinuationToken : null;
                } while (continuationToken != null);

                if (keys.Count == 0)
                    return;

                Directory.CreateDirectory(destFolder);
                var keptFileNames = new HashSet<string>();
                foreach (var key in keys)
                {
                    var fileName = key.Substring(prefix.Length);
                    if (string.IsNullOrEmpty(fileName) || fileName.Contains("/"))
                        continue; // collection-file groups are flat per CONTRACTS.md's S3 layout.
                    keptFileNames.Add(fileName);
                    var destPath = Path.Combine(destFolder, fileName);
                    var tempPath = destPath + ".tmp";
                    using (
                        var response = s3Client
                            .GetObjectAsync(
                                new GetObjectRequest { BucketName = location.Bucket, Key = key }
                            )
                            .GetAwaiter()
                            .GetResult()
                    )
                    {
                        response
                            .WriteResponseStreamToFileAsync(tempPath, false, CancellationToken.None)
                            .GetAwaiter()
                            .GetResult();
                    }
                    if (RobustFile.Exists(destPath))
                        RobustFile.Delete(destPath);
                    RobustFile.Move(tempPath, destPath);
                }

                // Mirrors FolderTeamCollection's ExtractFolder "delete extras" behavior.
                foreach (var existing in Directory.EnumerateFiles(destFolder))
                {
                    if (!keptFileNames.Contains(Path.GetFileName(existing)))
                        RobustFile.Delete(existing);
                }
            }
            catch (Exception e)
            {
                NonFatalProblem.Report(
                    ModalIf.None,
                    PassiveIf.All,
                    $"Bloom could not download the '{groupKey}' collection files from the Team Collection.",
                    exception: e
                );
            }
        }

        private static IAmazonS3 BuildS3Client(CloudS3Location location)
        {
            var env = CloudEnvironment.Current;
            var config = new AmazonS3Config
            {
                ServiceURL = env.S3Endpoint,
                ForcePathStyle = env.S3ForcePathStyle,
                AuthenticationRegion = location.Region,
            };
            var credentials = new AmazonS3Credentials
            {
                AccessKey = location.AccessKeyId,
                SecretAccessKey = location.SecretAccessKey,
                SessionToken = location.SessionToken,
            };
            return BloomS3Client.CreateAmazonS3Client(config, credentials);
        }

        protected override DateTime LastRepoCollectionFileModifyTime
        {
            get
            {
                EnsureCacheHydrated();
                var times = new[] { "other", "allowed-words", "sample-texts" }
                    .Select(g => _cache.TryGetGroup(g)?.UpdatedAt)
                    .Where(t => t.HasValue)
                    .Select(t => t.Value)
                    .ToList();
                return times.Count == 0 ? DateTime.MinValue : times.Max();
            }
        }

        /// <summary>
        /// Approximates the repo colorPalettes.json modify time with the "other" group's whole-group
        /// UpdatedAt, since CONTRACTS.md tracks collection-file freshness at group granularity, not
        /// per-file -- see <see cref="SyncColorPaletteFileWithRepo"/>'s note on the same limitation.
        /// </summary>
        protected override DateTime GetRepoColorPaletteTime()
        {
            EnsureCacheHydrated();
            return _cache.TryGetGroup("other")?.UpdatedAt ?? DateTime.MinValue;
        }

        /// <summary>
        /// Pushes local color palette additions up via add_palette_colors' union merge. CONTRACTS.md
        /// defines no RPC to read back a collection's full merged palette state (add_palette_colors
        /// is write-only), so this is push-only for now: local additions reach the repo, but a
        /// teammate's additions only reach us via the ordinary "other" group download (whole-file
        /// replace, not merge) in <see cref="CopyRepoCollectionFilesToLocalImpl"/> -- flagged as a
        /// contract gap (a `get_palette_colors` RPC is needed for a true two-way merge) in the task
        /// 05 final report.
        /// </summary>
        protected override void SyncColorPaletteFileWithRepo(string localFolder)
        {
            try
            {
                var colorPaletteFile = Path.Combine(localFolder, "colorPalettes.json");
                if (!RobustFile.Exists(colorPaletteFile))
                    return;
                var localPalettes = new Dictionary<string, string>();
                CollectionSettings.LoadColorPalettesFromJsonFile(localPalettes, colorPaletteFile);
                foreach (var kvp in localPalettes)
                {
                    var colors = kvp.Value?.Split(
                        new[] { ' ' },
                        StringSplitOptions.RemoveEmptyEntries
                    );
                    if (colors == null || colors.Length == 0)
                        continue;
                    _client.AddPaletteColors(_collectionId, kvp.Key, colors);
                }
            }
            catch (Exception e)
            {
                NonFatalProblem.ReportSentryOnly(e);
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
                    return new TeamCollectionMessage(
                        MessageAndMilestoneType.Error,
                        "TeamCollection.Cloud.NotAMember",
                        "Your account ({0}) is not approved for this Team Collection.",
                        _auth.CurrentEmail
                    );
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

        // ------------------------------------------------------------------
        // Monitoring (polling; see CloudCollectionMonitor)
        // ------------------------------------------------------------------

        protected internal override void StartMonitoring()
        {
            base.StartMonitoring();
            EnsureCacheHydrated();
            _monitor = new CloudCollectionMonitor(
                _client,
                _collectionId,
                _cache.LastSeenEventId,
                OnPolledChanges
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
            var previousBooksById = _cache.GetAllBooks().ToDictionary(b => b.Id);
            _cache.ApplyDelta(changes);
            _cache.Save();
            RefreshIndexFromCache();

            foreach (var book in _cache.GetAllBooks())
            {
                if (string.IsNullOrEmpty(book.Name))
                    continue;
                if (!previousBooksById.TryGetValue(book.Id, out var previous))
                {
                    if (book.CurrentVersionSeq.HasValue)
                        RaiseNewBook(book.Name);
                    continue;
                }
                if (book.DeletedAt.HasValue && !previous.DeletedAt.HasValue)
                {
                    RaiseDeleteRepoBookFile(book.Name);
                    continue;
                }
                if (
                    book.CurrentVersionSeq != previous.CurrentVersionSeq
                    || book.LockedBy != previous.LockedBy
                    || book.Name != previous.Name
                )
                {
                    RaiseBookStateChange(book.Name);
                }
            }

            if (changes["groups"] is JArray groupsArray && groupsArray.Count > 0)
                RaiseRepoCollectionFilesChanged();
        }

        /// <summary>Lets UI code (e.g. a "Receive Updates" button, or Bloom regaining focus) trigger
        /// an immediate poll instead of waiting for the periodic timer.</summary>
        public void PollNow() => _monitor?.PollNow();
    }
}
