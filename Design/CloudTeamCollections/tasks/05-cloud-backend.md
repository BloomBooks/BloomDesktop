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
