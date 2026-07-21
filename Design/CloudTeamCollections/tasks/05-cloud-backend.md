# 05 — CloudTeamCollection + monitor (Wave 3, first)

**Goal**: the backend subclass and change monitoring — the heart of the feature.

**Dependencies**: 00, 01, 02, 03, 04. Owns new files `Cloud/CloudTeamCollection.cs`,
`Cloud/CloudCollectionMonitor.cs`, `Cloud/CloudJoinFlow.cs`. Touches (exclusive this task):
nothing shared — the manager factory seam from 00 is wired by config.

## Steps
- [ ] Implement every abstract member per the design doc's mapping table (list/status members
      from cache; PutBookInRepo = Send pipeline via checkin-start/finish; FetchBookFromRepo =
      pinned-version staged fetch + swap; delete/rename/tombstone via RPCs; collection-file
      members on the group contracts; casing members against the book row; CheckConnection =
      network + session + membership with precise messages; GetBackendType = "Cloud").
- [ ] `WriteBookStatusJsonToRepo` diff-dispatch (per 00's caller audit): lock changes →
      lock RPCs; bookkeeping-only writes never clear a lock; server stamps identity.
- [ ] New-book first-Send path incl. NameConflict → "name2" resolution and id-conflict flow.
- [ ] Unified recovery: on lock-lost/base-superseded, save `.bloomSource` to local Lost & Found,
      Receive current, post incident event + message (distinct texts per sub-case).
- [ ] Account rules: same-account sign-out/in safe; account switch with unsent changes blocked
      with Send-or-preserve choices.
- [ ] `CloudCollectionMonitor`: polling first (get_changes, 60s; on-activation), event→base-queue
      mapping, event-id self-echo suppression, catch-up-then-trust on reconnect.
- [ ] `CloudJoinFlow`: my_collections listing → local collection creation → first Receive
      (six-scenario matching logic moved from FolderTeamCollection).
- [ ] Modal Send/Receive orchestration on the existing BrowserProgressDialog harness.

## Acceptance
- `CloudTeamCollectionMemberTests`, `CloudTeamCollectionLockTests`, `CloudSyncAtStartupTests`
  (ported matrix; asserts `.bloomSource` + incident events), `CloudCollectionMonitorTests`.
- Folder-TC suite still green.
- Manual: two Bloom instances on ONE machine (distinct collection folders + dev identities
  via `BLOOM_CLOUDTC_USER`) against the local stack (task 11) — checkout/Send/Receive loop
  works, lock state visible across instances.

**Agent notes**: Sonnet, orchestrator reviews closely. Base-class code is read-only here;
anything needing a base change goes back to the orchestrator.

## Progress log

- 7 Jul 2026 · done · Read task brief, CONTRACTS.md v1.2, architecture doc, write-book-status
  audit, and surveyed (via sub-agent) the full abstract-member surface of TeamCollection.cs,
  TeamCollectionManager.cs's two NotImplementedException seams, and the Wave 3/4 support
  classes (CloudCollectionClient/CloudRepoCache/BookVersionManifest/CloudBookTransfer/CloudAuth/
  CloudEnvironment). Added typed RPC/edge-function wrapper methods to CloudCollectionClient.cs
  (create_collection, my_collections, claim_memberships, get_collection_state, get_changes,
  get_book_manifest, checkout_book, unlock_book, force_unlock, delete_book, undelete_book,
  rename_check, members list/add/remove/set_role, add_palette_colors, log_event,
  checkin-start/finish/abort, download-start, collection-files-start/finish) — this file's own
  doc comment said later tasks build these on top of it. Builds clean.
  Next action: write `Cloud/CloudTeamCollection.cs` implementing every abstract member per the
  mapping table, using `CloudRepoCache` + the new client methods + `CloudBookTransfer`.
- 7 Jul 2026 · done · Wrote `Cloud/CloudTeamCollection.cs` (every abstract/virtual member of
  TeamCollection implemented: status/list/presence from cache; TryLockInRepo/UnlockInRepo via
  checkout_book/unlock_book/force_unlock; WriteBookStatusJsonToRepo diff-dispatch per the task 00
  audit; PutBookInRepo = Send via checkin-start/upload/checkin-finish with "name2" NameConflict
  retry and an inLostAndFound -> unified-recovery branch (.bloomSource to local Lost and Found +
  log_event type 100 WorkPreservedLocally + distinct message per sub-case); FetchBookFromRepo =
  Receive via get_book_manifest + pinned download into a staging folder + a two-rename atomic
  directory swap done by this class (per the merge-log note that CloudBookTransfer's own move loop
  isn't itself atomic); GetRepoBookFile = single pinned-file fetch, cached per sync pass; casing
  methods against the cached book's canonical Name; collection files via collection-files-start/
  finish (push) and a direct S3 prefix-list+download (pull, since no group manifest RPC exists);
  color palette sync is push-only via add_palette_colors (no read-back RPC exists); CheckConnection
  = signed-in + membership check; StartMonitoring/StopMonitoring wire a new CloudCollectionMonitor
  instance whose polled deltas raise the same low-level events FolderTeamCollection's file watcher
  raises. Also wrote `Cloud/CloudCollectionMonitor.cs` (60s Timer polling get_changes, PollNow() for
  on-activation/manual triggering, self-echo suppression falls out of the shared last_seen_event_id
  cursor). Builds clean (`dotnet build src/BloomExe/BloomExe.csproj`).
  Contract gaps/ambiguities found while implementing (see final report for full list): (1)
  checkin-start/finish never return the server-assigned book id for a brand-new book -- worked
  around with a post-commit get_collection_state refresh matched on bookInstanceId; (2) no manifest
  RPC exists for collection-file groups (only a version-bump counter) -- worked around with a direct
  S3 ListObjectsV2, reading latest rather than a pinned version; (3) add_palette_colors has no
  matching read-back RPC -- palette sync is currently push-only; (4) member first/last name isn't in
  the book row shape -- lock display fields lockedByFirstName/Surname are null for cloud TCs; (5)
  collection-files-start/finish's exact response shape isn't spelled out -- assumed
  checkin-start-like `{transactionId, s3}` / `{version}`; (6) the `members` RPC names
  (members_list/add/remove/set_role) are a guess at CONTRACTS.md's "members: list/add/remove/
  set_role" shorthand.
  Base-class change identified but NOT made (file discission): `Bloom.History.BookHistoryEventType`
  has no `WorkPreservedLocally` member; used the literal `100` per the task brief instead of adding
  it to that shared enum (out of this task's owned-file scope) -- recommend the orchestrator add
  `WorkPreservedLocally = 100` there.
  Next action: write `Cloud/CloudJoinFlow.cs`, then the one authorized
  TeamCollectionManager.cs edit, then tests.
- 7 Jul 2026 · done · Wrote `Cloud/CloudJoinFlow.cs`: `ListMyCollections()` (my_collections),
  `DetermineScenario()` (the six-scenario local-vs-remote matrix ported from
  `FolderTeamCollection.ShowJoinCollectionTeamDialog`'s boolean logic: FreshJoin/
  AlreadyJoinedSameCollection/LinkedToDifferentCloudCollection/LinkedToFolderTeamCollection/
  PlainCollectionSameGuid/PlainCollectionDifferentGuid), `JoinCollection()` (creates the local
  folder + link file + CloudTeamCollection + first Receive of collection files and every book;
  throws `CloudJoinConflictException` for the three human-decision scenarios), and
  `CreateAndJoinCollection()` (create_collection + join). Key design point: a cloud collection's
  `.bloomCollection` file travels with it in the "other" collection-file group (same as a folder
  TC's zipped settings), so joining does not need to fabricate a fresh CollectionSettings --
  downloading collection files brings the real settings file down. Builds clean.
  Scope note: the interactive resolution dialog for the three conflict scenarios (the cloud
  equivalent of ShowJoinCollectionTeamDialog's React dialog) is UI-layer work outside this task's
  file ownership; CloudJoinConflictException.Message is ready for a future dialog to display.
  Next action: the one authorized TeamCollectionManager.cs edit (CreateTeamCollectionFromLink's
  cloud branch + ConnectToCloudCollection), then the test suite (CloudTeamCollectionMemberTests,
  CloudTeamCollectionLockTests, CloudSyncAtStartupTests, CloudCollectionMonitorTests), then
  build/test verification and the final report.
- 7 Jul 2026 · done · Made the one authorized TeamCollectionManager.cs edit: replaced both
  NotImplementedException placeholders (`CreateTeamCollectionFromLink`'s cloud branch now calls a
  new private `CreateCloudTeamCollection` helper mirroring `CreateFolderTeamCollection`;
  `ConnectToCloudCollection(collectionId)` now creates the server-side row via create_collection
  -- collectionId doubles as both this Bloom collection's own CollectionId GUID and the server's
  `collections.id`, per CONTRACTS.md's link-format note -- links the local collection, and pushes
  existing local content up via a new `CloudTeamCollection.SetupCloudTeamCollectionWithProgressDialog`
  mirroring FolderTeamCollection's own Setup*WithProgressDialog). Touched nothing else in the file
  (verified via `git diff --stat` + full diff review). Builds clean.
  Next action: write the test suite (CloudTeamCollectionMemberTests, CloudTeamCollectionLockTests,
  CloudSyncAtStartupTests, CloudCollectionMonitorTests) under src/BloomTests/TeamCollection/Cloud/,
  run the cloud + TC-regression filters, then write the final report.
- 7 Jul 2026 · done · Wrote the four acceptance-criteria test files under
  src/BloomTests/TeamCollection/Cloud/ (CloudTeamCollectionMemberTests, CloudTeamCollectionLockTests,
  CloudSyncAtStartupTests, CloudCollectionMonitorTests), reusing the FakeRestExecutor/
  StubCloudAuthProvider/FakeResponses harness already established in CloudCollectionClientTests.cs
  (same namespace, no duplication). `dotnet test src/BloomTests/BloomTests.csproj --filter
  "FullyQualifiedName~Cloud"` → 102 passed, 0 failed. `--filter "FullyQualifiedName~TeamCollection"`
  (folder-TC regression + all cloud) → 300 passed, 0 failed.
  CloudSyncAtStartupTests ports the single highest-value scenario from the folder suite (checked-out
  + locally-edited book whose repo checksum ALSO changed remotely -> SyncAtStartup's
  PutBook(inLostAndFound: true) path) and asserts all three unified-recovery effects: a `.bloomSource`
  file appears in a local "Lost and Found" folder, a `log_event` RPC fires, and a
  "TeamCollection.Cloud.WorkPreservedLocally" message lands in the log; plus a simpler "new book
  fetched from repo" case. Porting the full ~15-case SyncAtStartupTests.cs matrix is flagged as
  follow-up work in the final report (needs a fuller scripted-server fake for rename/id-conflict
  detection via GetRepoBookFile).
  Real bug/behavior found while writing CloudTeamCollectionLockTests: `TeamCollection.AttemptLock`
  (base class, unchanged) discards `TryLockInRepo`'s bool return value and returns based on the LOCAL
  status it optimistically set to "locked by me" BEFORE calling TryLockInRepo -- it never re-reads
  status afterward. So AttemptLock's own return value can be `true` even when the cloud server denied
  the lock; only a separate `WhoHasBookLocked`/`GetStatus` call afterward reliably reflects who won
  (which TryLockInRepo's own doc comment already anticipates: "the caller should re-read status to
  find out who won"). Not fixed (TeamCollection.cs is read-only for this task) -- flagged in the
  final report as a base-class change worth considering.
  Next action: build/test verification is done; write the final report.
- 7 Jul 2026 · done · Added `Cloud/CloudTeamCollectionLiveTests.cs` ([Explicit] Send-then-Receive
  round trip against the REAL local dev stack: `supabase start` + `supabase functions serve
  --env-file server/dev/functions.env`, started in the background per the anti-hang rules) and ran
  it to green. It found and fixed TWO real bugs that no mocked test could have caught:
  (1) **JSON serialization bug** in `CloudCollectionClient.CallRpc`/`CallEdgeFunction`: RestSharp's
  own default JSON serializer doesn't know how to serialize a `Newtonsoft.Json.Linq.JArray`/
  `JObject` embedded in the anonymous body object (it reflects over JToken's own CLR properties
  instead of writing a native array/object), which silently produced a malformed `files` array and
  a cryptic Postgres "jsonb_to_recordset must be an array of objects" error on the very first live
  checkin-start call. Fixed by serializing the body with Newtonsoft ourselves and attaching the
  resulting JSON text as a raw request-body parameter (`CloudCollectionClient.AddJsonBody` private
  helper), rather than handing RestSharp a raw object to serialize itself.
  (2) **Missing book-instance path segment on Receive**: `download-start`'s credentials are scoped
  to the whole COLLECTION prefix (`tc/{cid}/`), not the book-specific one (`tc/{cid}/books/
  {bookInstanceId}/`) that `checkin-start` returns for uploads -- `FetchBookFromRepo`/
  `GetRepoBookFile` were building S3 keys directly under the collection prefix, producing
  `NoSuchVersion` (confirmed via a manual Node/aws-sdk repro against the live MinIO). Fixed with a
  new `CloudTeamCollection.BuildBookS3Location` helper that inserts `books/{bookInstanceId}/`
  (looked up from the cache) before combining with `download-start`'s prefix.
  Also confirmed a THIRD issue that is NOT fixable in this task's files: the live server currently
  stamps `locked_by`/`created_by` with the raw auth user id (JWT `sub`, confirmed by decoding a
  real session token) rather than the email CONTRACTS.md's identity model specifies ("account email
  is the identity in cloud TCs") -- a task-01 SQL-function issue, out of scope here. Added a
  client-side `ResolveLockedByForDisplay` workaround that resolves OUR OWN id back to our own email
  (fixes every IsCheckedOutHereBy/"is this checked out to me" check, the case that matters most),
  leaving a teammate's id unresolved to a friendly name pending the server-side fix.
  Re-ran `--filter "FullyQualifiedName~TeamCollection&FullyQualifiedName!~LiveTests"` after all
  three fixes: still 300 passed, 0 failed. Live test: 1 passed (run manually with
  `BLOOM_CLOUDTC_ANON_KEY` exported, `supabase functions serve --env-file server/dev/functions.env`
  running in the background).
  Next action: none for this task; final report follows. Recommend the orchestrator route the
  locked_by-is-a-uuid finding to whoever owns task 01's SQL functions.
