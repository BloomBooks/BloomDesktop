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
