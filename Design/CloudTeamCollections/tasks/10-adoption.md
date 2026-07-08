# 10 — Adoption path + polish (Wave 4)

**Goal**: the manual folder-TC → cloud path is smooth, documented, and clean.

**Dependencies**: waves 0–3. Touches: cloud-create flow (cleanup step), docs.

## Steps
- [x] Enabling cloud on a formerly-folder-TC collection cleans stale artifacts: per-book
      `TeamCollection.status`, `lastCollectionFileSyncData.txt`, `log.txt`; simultaneous
      folder-link + cloud-link = error with fix instructions.
- [x] Members' existing local copies reconcile by checksum on first Receive (verify the
      first-time-join merge path). -- verified by reading; see progress log (no checksum-based
      reconciliation actually happens; documented as a known, pre-existing-pattern limitation,
      mitigated procedurally by "everyone check in first" in the item-5 docs).
- [ ] User documentation: the un-team + enable + invite-team walkthrough (docs site), incl.
      "everyone check in first".
- [ ] Localization sweep of all new strings (xlf-strings skill rules).
- [x] Analytics review: create/join/send (bytes uploaded vs skipped)/receive/force-unlock/
      incident events flowing with Backend="Cloud".
- [ ] Dogfood with a real team; triage findings.

## Acceptance
- E2E-7 green; a real Dropbox-TC collection migrated by hand following only the docs.

**Agent notes**: Sonnet + Haiku (strings/docs). Nothing here changes protocol or schema.

## Progress log

- 8 Jul 2026 · in progress · Prompt item 1 (proper experimental-feature checkbox, owed since the
  Wave-3 smoke's user.config hack): added `ExperimentalFeatures.kCloudTeamCollections` end-to-end
  wiring mirroring `allowTeamCollection` exactly — `CollectionSettingsDialog`
  (PendingAllowCloudTeamCollection / AllowCloudTeamCollectionOptionEnabled /
  UpdateCloudTeamCollectionAllowed, gated off `CurrentCollectionEvenIfDisconnected is
  CloudTeamCollection` the same way the folder flag gates off "currently in a TC"),
  `CollectionSettingsApi.GetAdvancedSettingsData`/`StoreAdvancedSettingsData`, and a new
  "Cloud Team Collections (experimental)" `ConfigrBoolean` in `AdvancedSettingsPanel.tsx` (gated
  by `useGetFeatureStatus("CloudTeamCollection")`, which `FeatureRegistry.cs` already wired to
  this same token). New XLF entry
  `CollectionSettingsDialog.AdvancedTab.Experimental.CloudTeamCollections` in
  BloomMediumPriority.xlf (same file as the analogous AppBuilder checkbox), `translate="no"`,
  with a translator context note. Component test
  `collection/AdvancedSettingsPanel.test.tsx` (5 tests: renders/labels, enabled/disabled by
  feature status, enabled/disabled by host-dialog flag, posts the toggle) — green:
  `yarn vitest run collection/AdvancedSettingsPanel.test.tsx --pool=threads` → 5/5 passed.
  C# side (CollectionSettingsDialog.cs, CollectionSettingsApi.cs, ExperimentalFeatures.cs doc
  comment) is authored but not build-verified in this worktree — orchestrator verifies at merge;
  it's a narrow, mechanical mirror of the already-shipped `allowTeamCollection` code path so risk
  is low, but please double check `AllowCloudTeamCollectionOptionEnabled`'s `is CloudTeamCollection`
  check compiles (needed `using Bloom.TeamCollection.Cloud;` added to CollectionSettingsDialog.cs).

- 8 Jul 2026 · done · Prompt item 2 (pull-down auto-open): `SharingApi.HandlePullDown` now
  replies with `{ collectionFolder }` (the joined `CloudTeamCollection.LocalCollectionFolder`,
  `internal` and already same-assembly-visible) instead of a bare `PostSucceeded()`.
  `sharingApi.ts` gains `IPullDownResult`; `JoinCloudCollectionDialog.handleJoinClick` now calls
  `postString("workspace/openCollection", result.collectionFolder)` on success — the exact same
  action `CollectionCard`'s `onClick` uses for the chooser's own cards — before closing the
  dialog. Updated `JoinCloudCollectionDialog.test.tsx` to mock `postString` and added two tests
  (auto-opens the returned folder; no-op when the response carries no `collectionFolder`, e.g. in
  tests that only `mockResolvedValue(undefined)`). `yarn vitest run
  collection/AdvancedSettingsPanel.test.tsx teamCollection/JoinCloudCollectionDialog.test.tsx
  collection/CollectionChooser.test.tsx --pool=threads` → 19/19 passed (CollectionChooser's own
  test stubs JoinCloudCollectionDialog entirely so it's unaffected by this change; included here
  as a regression check since it's the dialog's embedding parent).
  Note for whoever runs E2E-9/E2E-7 live: `--pool=threads` was needed to work around a
  "[vitest-pool]: Timeout starting forks runner" error with the default fork pool in this
  worktree/session; not investigated further since `--pool=threads` reliably worked, but flag it
  if CI or another dev also hits it. C# side (SharingApi.cs) authored but not build-verified here
  — orchestrator verifies at merge. Bundled into the same `HandlePullDown` edit (item 7's
  analytics audit, done early since it's the same method): added the missing
  `Analytics.Track("TeamCollectionJoin", ...)` call for the cloud pull-down path — previously
  ZERO analytics fired for cloud join, unlike the folder-TC join path
  (`TeamCollectionApi.HandleJoinTeamCollection`'s own "TeamCollectionJoin" event). Uses
  `CurrentAuth().GetLoginState(CloudEnvironment.Current).Email` for `User` since SharingApi is
  app-level (no per-project `_settings`/`CurrentUser` available there).

- 8 Jul 2026 · done · Task-file step 1 / prompt item 3 (un-team cleanup):
  `TeamCollectionManager.ConnectToCloudCollection` (the "enable cloud" entry point, called from
  `TeamCollectionApi.HandleCreateCloudTeamCollection`) now: (1) calls new static
  `ThrowIfConflictingTeamCollectionLink(localCollectionFolder)` FIRST — throws the new
  `TeamCollectionLinkConflictException` (in TeamCollectionLink.cs) with concrete fix instructions
  if TeamCollectionLink.txt still describes a folder TC ("delete TeamCollectionLink.txt from this
  collection's folder, then try again") or a different/existing cloud TC. This is the
  "simultaneous folder-link + cloud-link" conflict the task file calls out — a sign the user
  started "un-teaming" (their term for disconnecting from a Dropbox-style shared TC folder, which
  has no dedicated Bloom UI today — confirmed by grep, it's a manual/DIY step the user-docs in
  item 5 need to spell out) but didn't finish removing the old link file. (2) Otherwise calls new
  `TeamCollection.CleanStaleTeamCollectionArtifacts(localCollectionFolder)`, which deletes every
  per-book `TeamCollection.status` file plus collection-level `lastCollectionFileSyncData.txt`
  and `log.txt` (deliberately leaves TeamCollectionLink.txt alone -- ConnectToCloudCollection
  overwrites it with the fresh cloud link right after). This matters because
  `TeamCollection.PutBook`/`GetStatus` fall back to LOCAL status whenever the repo has no record
  for a book yet — true for every book on a brand-new cloud collection's first upload — so a
  stale leftover status file (wrong checksum, or `lockedBy` some old folder-TC teammate) would
  otherwise leak into that book's very first cloud version.
  Both new methods are pure/file-system-only (no network calls), refactored out specifically so
  they're unit-testable without standing up a full TeamCollectionManager/CloudCollectionClient.
  Tests authored in `TeamCollectionManagerTests.cs` (was an empty stub): 3 tests for
  `ThrowIfConflictingTeamCollectionLink` (no link → no-op; folder link → throws with the folder
  path and "TeamCollectionLink.txt" in the message; cloud link → throws with the collection id),
  5 tests for `CleanStaleTeamCollectionArtifacts` (deletes per-book status files; deletes the two
  collection-level files; leaves TeamCollectionLink.txt alone; no-op on a plain never-was-a-TC
  folder). C# authored but NOT build/test-verified in this worktree (no build deps here) —
  **orchestrator: please run** `dotnet test --filter FullyQualifiedName~TeamCollectionManagerTests`
  (full build, not `--no-build`) to confirm before merging.

- 8 Jul 2026 · done · Prompt item 7 (analytics audit), concluding the piece started opportunistically
  in the item-2 commit. Read every `Analytics.Track` call reachable from cloud code paths
  (`TeamCollectionApi.cs`, `TeamCollection.cs`, `SharingApi.cs`, `CloudJoinFlow.cs`,
  `CloudTeamCollection.cs`) plus their call sites. Findings:
  - **create** ("TeamCollectionCreate", both `HandleCreateTeamCollection` folder path and
    `HandleCreateCloudTeamCollection` cloud path) — OK, `Backend` already reads
    `_tcManager.CurrentCollection.GetBackendType()` dynamically (`"Cloud"` for
    `CloudTeamCollection.GetBackendType()`), so it was already correct for cloud with no change
    needed.
  - **join** — folder path ("TeamCollectionJoin" from `HandleJoinTeamCollection`) OK, same dynamic
    `Backend`. **Cloud path (pull-down) had ZERO analytics** — fixed in the item-2 commit
    (`SharingApi.HandlePullDown` now tracks "TeamCollectionJoin" with `Backend="Cloud"`,
    `JoinType="pullDown"`).
  - **send** (per-book check-in, "TeamCollectionCheckinBook" in `HandleCompleteCheckinOfCurrentBook`
    or similar) — OK, dynamic `Backend`, fires for both backends identically since `PutBook` is
    backend-agnostic at the `TeamCollectionApi` layer.
  - **receive** — per-book checkout ("TeamCollectionCheckoutBook") OK, dynamic `Backend`. But the
    cloud-only bulk **"Receive Updates" button (`HandleReceiveUpdates`) had ZERO analytics** —
    fixed here: added `Analytics.Track("TeamCollectionReceiveUpdates", ...)` with `BooksReceived`
    and `BooksSkippedCheckedOutHere` counts. Byte-level uploaded-vs-skipped counts (explicitly
    flagged as a nice-to-have in the task prompt) are NOT cheaply available on this path today:
    `CopyBookFromRepoToLocal`/S3 download don't expose per-book byte counts without extra
    HEAD/GetObjectAttributes calls per book — **flagging as a future enhancement**, not done here.
  - **force-unlock** ("TeamCollectionRevertOtherCheckout" in `HandleForceUnlock`, shared by both
    `teamCollection/forceUnlock` and `sharing/forceUnlock`) — OK, dynamic `Backend`, already
    correct for cloud, no change needed.
  - **incident** ("TeamCollectionConflictingEditOrCheckout" in the check-in conflict branch) — OK,
    dynamic `Backend`, no change needed.
  - Also noted but explicitly NOT in this prompt's event list, so left alone: `SharingApi.cs`'s
    `addApproval`/`removeApproval`/`setRole`/members endpoints (the "invite team" workflow) have
    zero analytics today. Flagging as a gap for a future task if invite-funnel metrics become
    wanted; the docs (item 5) still work fine without it.
  Net: two real gaps found and fixed (cloud join, Receive Updates); everything else was already
  correctly wired via the existing `GetBackendType()`-based dynamic `Backend` pattern. No new unit
  tests added for the two new `Analytics.Track` calls — consistent with this codebase's existing
  convention (grep confirms no test anywhere asserts on an `Analytics.Track` call; `Analytics` is
  evidently safe/no-op-tolerant to call from code paths that ARE covered by tests, e.g.
  `HandleForceUnlock`/`HandleAttemptLockOfCurrentBook`, which have existing green tests in
  `TeamCollectionApiTests.cs` despite their own preexisting `Analytics.Track` calls).

- 8 Jul 2026 · done (verify-by-reading, no code change) · Task-file step 2 / prompt item 4
  (First-Receive reconcile). Traced `CloudJoinFlow.JoinCollection`'s full first-join path for
  every scenario with a pre-existing local copy (`AlreadyJoinedSameCollection`,
  `PlainCollectionSameGuid`, and — via `JoinCloudCollectionDialog`'s
  `MatchesExistingNonTeamCollection` merge copy — the general "you already have a local
  collection with this name" case): all of them fall through to the same
  `cloudTc.CopyRepoCollectionFilesToLocal(...)` + `cloudTc.CopyAllBooksFromRepoToLocalFolder(...)`
  call. `CopyAllBooksFromRepoToLocalFolder` (TeamCollection.cs, shared base class) calls
  `CopyBookFromRepoToLocal` -> `FetchBookFromRepo` for EVERY book in the repo's list
  UNCONDITIONALLY — there is no checksum comparison anywhere in this path. Concretely:
  `CloudTeamCollection.FetchBookFromRepo` downloads to a staging folder, verified per-file, then
  does a two-rename atomic-ish swap of the whole book folder, and **deletes** whatever was
  previously at that path (no backup/Lost-and-Found capture of pre-existing local content, no
  checksum-match short-circuit to skip an unnecessary download).
  This is NOT a cloud-specific regression: `FolderTeamCollection.FetchBookFromRepo`
  (`RobustZip.UnzipDirectory(destFolder, bookPath)`) has the exact same "always overwrite, never
  compare checksums" behavior for its own first-join path — this is long-standing, symmetric
  behavior across both backends, not something introduced by this project.
  Net effect for a member with a genuinely pre-existing local copy of the same collection: (a) if
  their local content already matches the repo (the expected case when the whole team checked in
  before the admin ran "enable cloud" — see item 5's docs, "everyone check in first" is written
  as a load-bearing instruction precisely because of this), every book still gets wastefully
  re-downloaded and re-written byte-for-byte identical — inefficient but harmless; (b) if their
  local content had DIVERGED (they kept editing after un-teaming, or another un-synced local
  change), that divergent content is silently discarded with no salvage step. (b) is a real gap,
  but fixing it properly needs new merge/conflict UX (detect divergence, offer Lost-and-Found
  preservation like the checked-in-conflict path already does elsewhere in TeamCollection.cs) —
  not a "trivial" fix per this item's own instructions, and risky to author here without build
  verification given it would touch the shared `CopyAllBooksFromRepoToLocalFolder`/
  `FetchBookFromRepo` contract used by both backends' join flows. Recommending this as a
  follow-up task if dogfooding (item 7 of the Steps list) surfaces it as a real-world problem;
  documented as a known limitation in the user walkthrough (item 5) instead, which is why "make
  sure everyone checks in all their work to the OLD Team Collection before enabling cloud" is
  called out as a MUST, not a nice-to-have, in that doc.
