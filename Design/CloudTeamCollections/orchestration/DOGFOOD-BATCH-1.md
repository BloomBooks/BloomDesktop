# Dogfood batch 1 (9 Jul 2026) — restartable work plan

John's bug/improvement list from first real dogfooding, plus decisions already made.
This file is the durable state for the batch: the orchestrator ticks checkboxes and
updates each item's `Status:` line as work proceeds (same protocol as RESUME.md — commit
after every completed step, progress state lives in git, never only in a conversation).

**To restart after any interruption:** start a fresh Claude Code session in this repo and
say: **"Resume the dogfood batch per
Design/CloudTeamCollections/orchestration/DOGFOOD-BATCH-1.md."** The resumer reads each
item's Status line, secures any uncommitted `task/*` or worktree work as WIP commits, and
continues with the next unchecked step. General orchestration rules (review-before-merge,
C# test filter, dev-stack bring-up, environment quirks) are in RESUME.md and apply here.

**Testing protocol for this batch (agreed with John 9 Jul):** per item, run only the 1–2
E2E specs covering the touched area (each spec independently wipes + reseeds the dev DB,
so subsets are trustworthy); add `e2e-1-create-share` whenever a change touches startup or
localizable strings. One full-matrix run before pushing the finished batch. E2E runs are
serialized (shared dev DB + real Bloom launches); code work may proceed during a run.
Bloom windows go to John's spare screen via machine-level `BLOOM_E2E_SCREEN=1`.

**Decision already made (9 Jul, John):** remote-checkin application is FULLY AUTOMATIC —
when the poll notices a checkin for a book not checked out here, apply it to the local
book folder immediately and refresh the preview if that book is selected. The Sync button
(renamed from Reload) forces an immediate pass; no updates-available nag for the common
case.

## Work order

Chosen order: quick wins first, then the propagation cluster (items 4+5 are one work
item), then the two larger UI features. Items 1–3 are independent of everything else.

### 1. "Bloom is busy" missing localization  `[quick]`
Status: CODE DONE (commit 2d74d280f) — e2e-1 launch gate QUEUED (needs unlocked screen)
- [x] Found: ExternalBusyOverlay.tsx's fallback message already had id Common.BloomIsBusy
      but no XLF entry; the useL10n lookup logged the complaint on every collection-tab
      mount. (The specific BloomBridge message the overlay usually shows is intentionally
      unlocalized; only the fallback needed an entry.)
- [x] Added to BloomLowPriority.xlf (John's choice) with translate="no" + context note.
- [ ] Verify: `e2e-1-create-share` (launch gate for XLF changes). First attempt failed
      ONLY because the desktop locked mid-run (WebView2 stuck at about:blank — the known
      signature); re-run when unlocked.

### 2. Poll immediately on book selection  `[quick]`
Status: CODE DONE (commit 6f0c4a068) — e2e-2 verification QUEUED (needs unlocked screen)
- [x] TeamCollectionManager ctor now subscribes BookSelection.SelectionChanged → if the
      current collection is a CloudTeamCollection, Task.Run(PollNow). Results flow through
      the existing change-event pipeline (same as timer polls).
- [x] Guard: PollNow's own in-flight coalescing covers rapid selection changes; null
      bookSelection guard for unit-test constructions (caught by test run: 10 failures,
      fixed, 363/363 green).
- [ ] `e2e-2-collaboration-loop` re-run when screen unlocked (note: E2E uses a 5s poll, so
      the speedup itself is mostly invisible there — the run guards against regressions;
      the real check is John's manual test at the default 60s poll).

### 3. Center the checkin-progress dialog in the status panel  `[quick]`
Status: CODE DONE (commit 207cc1d0) — visual verification QUEUED (needs unlocked screen)
- [x] It's the React BloomDialog in TeamCollectionBookStatusPanel (not BrowserProgressDialog):
      now positioned via PaperProps over the #teamCollection div's center, vertically
      clamped so the paper stays on-screen (the panel hugs the window bottom). Falls back
      to default whole-window centering when #teamCollection is absent (unit tests).
      Panel vitest suite 11/11.
- [ ] Visual check + `e2e-2-collaboration-loop` when screen unlocked.

### 4+5. Automatic remote-update application + in-place Sync (one work item)  `[medium]`
Status: MERGED into cloud-collections (9 Jul; orchestrator re-ran C# 375/375 + both vitest
files green, reviewed queue/wiring/panel/XLF line by line) — E2E verification QUEUED
(desktop locked). Residual risk noted at review: a checkout racing the download window
between re-verify and swap is possible but tiny (swap is two directory renames; E2E-2/3
exercise contention) — watch for it in the E2E pass.
Observed bug: after a remote checkin, the other instance updated lock state (avatar +
status panel) but the TC button showed no "updates available" and the preview did not
refresh; book folder content update unverified.
- [x] Diagnose: CloudCollectionMonitor.OnPolledChanges DOES already notify the base
      TeamCollection change pipeline correctly (RaiseBookStateChange → HandleModifiedFile at
      Application.Idle), and it DID reach the HasBeenChangedRemotely branch — but that branch
      only ever wrote a "TeamCollection.BookModifiedRemotely" NewStuff log message; it never
      copied anything to the local folder (confirmed: folder TC has the exact same message-only
      behavior for this branch, so this wasn't a cloud-specific regression, just a gap neither
      backend had filled in). Root cause of "TC button showed no updates available": the
      top-bar TeamCollectionButton's color/label state comes from `teamCollection/tcStatus`
      (CollectionTabView.cs sends this from TeamCollectionMessageLog.TeamCollectionStatus, which
      is driven ONLY by whether a NewStuff message log entry exists) — it is a SEPARATE signal
      from `teamCollection/tcStatusMetadata`'s `updatesAvailableCount` (which reads the
      CloudRepoCache version numbers directly and is pushed via the `statusMetadataChanged`
      socket event on every poll). So `updatesAvailableCount` was almost certainly already
      correct and live; what was missing was confirming a NewStuff message actually got written
      for John's test run (a corrupted/stuck session, a race, or simply that this was the very
      poll that revealed the gap) — this needs re-verification once the desktop is available,
      but the code-level cause (message-only branch, no auto-apply) is confirmed and is exactly
      what this item's implementation now fixes.
- [x] Implement fully-automatic application: TeamCollection.CanAutoApplyRemoteChanges (false by
      default; true only for CloudTeamCollection) + a new single-consumer RemoteBookAutoApplyQueue
      (dedupes by book name, one book at a time, runs on Task.Run so downloads never block the UI
      thread). HandleModifiedFile's HasBeenChangedRemotely branch now queues the book instead of
      just logging when CanAutoApplyRemoteChanges is true; the worker re-verifies eligibility
      (still changed remotely, not checked out here, no clobber/checkout conflict) at the moment
      it actually runs, then reuses CopyBookFromRepoToLocal, updates book status, and refreshes
      the preview (SendBookContentReload) only if the applied book is the one currently selected.
      Falls back to exactly the old message-only behavior on failure or ineligibility. Folder TCs
      are unaffected (CanAutoApplyRemoteChanges stays false there).
- [x] SAFETY (John, 9 Jul): CopyBookFromRepoToLocal already staged-then-atomically-swapped before
      this task (two directory renames; not reimplemented). The auto-apply worker's
      re-verification (checked-out-here / clobber / checkout-conflict, re-read fresh on the
      worker thread right before copying) is the mechanism that keeps a book "busy-safe": if the
      user checks it out, starts editing, or otherwise changes its state between queueing and the
      worker actually running, the worker backs off silently rather than clobbering anything.
      NOT separately implemented: an explicit UI-level "busy" lock during the download window
      itself (publish/delete/rename aren't specially blocked while a download is in flight) —
      the re-verification step covers the has-this-changed-since-queueing race but a reviewer
      should double check there's no narrow window between the re-verification check and the
      actual folder swap where a concurrent user action could interleave badly. Given the swap
      itself is two fast directory renames (not file-by-file writes), this window is believed
      negligible but wasn't independently stress-tested.
- [x] Rename "Reload" to "Sync" for cloud TCs. The existing `teamCollection/receiveUpdates`
      backend endpoint already did PollNow() before its receive loop (no server-side change
      needed) — only the label changed, in both the dialog and the per-book status panel.
      Discovered and fixed a related bug while in there: the per-book panel's generic
      "needsReload" state (isChangedRemotely, a content-update signal identical for both
      backends) was checked ahead of the cloud-specific "updatesAvailable" state, so a cloud
      book with a pending remote change showed "Reload Collection" instead of "Sync" — cloud now
      renders needsReload with the same copy/button as updatesAvailable; folder TCs (and genuine
      hasConflictingChange for either backend) are unchanged.
- [x] Keep the reload-requiring paths (settings changes etc.) working — HandleCollectionSettingsChange
      and the dialog's showReloadButton/"Reload Collection" path were not touched at all.
- [x] XLF for the new "Sync" label: renamed TeamCollection.ReceiveUpdates → TeamCollection.Sync
      (was translate="no", so free to rename per the xlf skill) and updated
      UpdatesAvailableForBookDescription's text/note to match.
- [ ] Verify: `e2e-2-collaboration-loop` + `e2e-8-receive-during-send`; e2e-1 for the XLF. NOT
      run — this task's hard rules forbid launching Bloom / running e2e (desktop session
      locked); queued for the next E2E pass alongside items 1–3.
- [x] Update tests/specs that assert on the old Reload wording/behavior: TeamCollectionDialog.test.tsx
      and TeamCollectionBookStatusPanel.test.tsx updated for the rename, plus new tests for the
      auto-apply eligibility logic (RemoteBookAutoApplyQueueTests, TeamCollectionAutoApplyTests)
      and the needsReload cloud/folder split. C# required filter 375/375 green; both touched
      vitest files green (5/5, 13/13); yarn typecheck and eslint clean.

### 6. Join-card integration in the collection chooser  `[medium]`
Status: CODE DONE on branch `task/b1-6-join-cards` (288e4057e, 9fe1c4de4, 8a3a01130,
f59dc6ea3, 5e572a84d; not yet merged into cloud-collections) — E2E verification QUEUED
(desktop locked; forbidden by this task's rules)
- [x] In the collection chooser dialog, remove the separate "team collections to join"
      list; instead add extra cards to the MAIN collection list for collections the user
      is invited to (server membership exists) but has no local copy of.
      MyCloudCollectionsSection.tsx + its test deleted; CollectionChooser now fetches the
      new `collections/getJoinCards` endpoint and passes results to CollectionCardList.
- [x] NO join card when the user already joined + has a local copy. DO show a join card
      when a same-named local collection exists that is NOT a TC (existing join-conflict
      code handles the actual join). CollectionChooserApi.ComputeJoinCards matches by
      cloud collection id ONLY (via TeamCollectionLink.txt scan of MRU + discovered local
      folders, GetLocalCloudCollectionIds) -- a same-named non-cloud-linked local folder
      still gets a join card; CloudJoinFlow's own scenario matching resolves merge-or-
      conflict once the user actually tries to join, unchanged.
- [x] Join cards do not count against the MRU-list card limit. CollectionCardList slices
      `collections` at maxCardCount=10 first, then appends `joinCollections` (unsliced).
- [x] Omit any card info not available for an unjoined TC (thumbnail, languages, …).
      CollectionCard's isJoinCard variant shows only title + TeamCollectionIcon + a "Get"
      join cue (reusing CollectionChooser.PullDown's wording); no per-card fetch (the
      unpublished-count effect is skipped) and the "..." Show-in-Explorer menu is hidden
      (no local folder exists yet).
- [ ] Verify: `join-auto-open` + `e2e-1-create-share`; vitest for the card-list logic. The
      vitest half is DONE (CollectionCardList.test.tsx: 4/4; CollectionChooser.test.tsx
      rewritten: 3/3) -- only the E2E launches remain queued (desktop locked; this task's
      hard rules forbid launching Bloom/e2e). `join-auto-open.spec.ts` exists under
      src/BloomTests/e2e/tests; checked its content -- it drives collections/pullDown and
      workspace/openCollection directly via HTTP (not through any chooser UI selector), so
      it does not touch MyCloudCollectionsSection/join cards at all and should be
      unaffected by this change. Still queued to actually run (desktop locked) as a
      regression check, per this task's hard rules.

Implementation notes (scouted 9 Jul, read-only — verified paths/lines):
- Chooser: `collection/CollectionChooserDialog.tsx` wraps `CollectionChooser.tsx` (MRU via
  `collections/getMostRecentlyUsedCollections`; cloud list via `useMyCloudCollections` →
  GET `collections/mine`; `joinTarget` state opens `JoinCloudCollectionDialog`).
- The separate list to REMOVE: `collection/MyCloudCollectionsSection.tsx` (+ its test);
  its "Get" button → `pullDownCollection` → POST `collections/pullDown` →
  `SharingApi.HandlePullDown` → `CloudJoinFlow.JoinCollection` (keep all of that; join
  cards reuse `JoinCloudCollectionDialog` and the pull-down + auto-open flow unchanged).
- MRU card cap: `CollectionCardList.tsx` `maxCardCount = 10` slice — join cards must be
  appended AFTER the slice. Card shape: `ICollectionInfo` in `CollectionCard.tsx`
  (path/title/bookCount/checkedOutCount/unpublishedCount/isTeamCollection) — join-card
  variant needs `collectionId`, a join flag, minimal info, no per-card unpublished fetch.
- "Has local copy of cloud collection X": for each MRU/local folder,
  `TeamCollectionManager.GetTcLinkPathFromLcPath` + `TeamCollectionLink.FromFile`
  (`IsCloud`, `CloudCollectionId`); a summary from `collections/mine` gets a join card iff
  no local cloud link matches its id. `CloudJoinFlow.DetermineScenario` (CloudJoinFlow.cs
  ~128) already does this matching — reference/reuse. Same-name non-TC local collections
  STILL get a join card (CloudJoinFlow's PlainCollectionSameGuid/DifferentGuid handles the
  merge-or-error; the card test keys off cloud links ONLY, not names).
- Server-side merge preferred: extend `CollectionChooserApi` (application-level, like
  SharingApi) with an `internal static` pure matching helper (SharingApiTests' pattern —
  no CollectionChooserApiTests file exists yet).
- Tests to rewrite: `CollectionChooser.test.tsx`; delete `MyCloudCollectionsSection.test.tsx`
  with its component; `JoinCloudCollectionDialog.test.tsx` stays valid.

### 7. Progressive join: open the collection before all books download  `[large]`
Status: NOT STARTED
- [ ] On join, fetch collection settings + book list (titles) first, open the collection
      immediately; books not yet downloaded show a placeholder icon suggesting an
      in-progress download.
- [ ] Background-download books; swap each placeholder for the real icon as its download
      completes.
- [ ] Selecting a not-yet-downloaded book bumps it to the front of the download queue;
      status panel shows a "downloading" message until it arrives.
- [ ] Same SAFETY rule as item 4+5: a book appears in the collection only as placeholder
      (no dangerous actions possible) or fully downloaded — never as a half-populated
      folder the user can act on. Temp-folder staging + atomic swap.
- [ ] Handle interruption: Bloom closed mid-join resumes/completes downloads on next open
      (SyncAtStartup should already fetch missing books — verify).
- [ ] Verify: `join-auto-open` + `e2e-9-new-book-lifecycle`; consider a new spec for the
      placeholder/priority behavior if cheap.

### 8. Recovery safety net (John decision, 9 Jul — replaces the old "recovery preconditions"
question)  `[quick-medium]`
Status: NOT STARTED
John's spec: when a sync operation brings a remote version of a book to local but the local
copy has somehow changed (rare — e.g. force-steal while edited, or any unexplained local
drift), GO AHEAD and make local consistent with the TC, but FIRST save the previous local
version as a .bloomSource so nothing is lost. SaveLocalCopyForRecovery (CloudTeamCollection,
~line 668: zips to <collection>/Lost and Found/<name>.bloomSource + logs an incident)
already does exactly this and the STARTUP sync path already uses it (pinned by
CloudSyncAtStartupTests.SyncAtStartup_LocalEditConflictsWithRemoteChange_...). The gap is
the two RUNTIME overwrite paths, made urgent by item 4+5's auto-apply (whose eligibility
gates use IsCheckedOutHereBy(GetLocalStatus) — dead-false for cloud, since cloud checkout
never writes the local status file; see tasks/09-e2e.md E2E-4 finding):
- [ ] In ProcessAutoApplyRemoteChange (TeamCollection.cs): before CopyBookFromRepoToLocal,
      if the local folder's current checksum differs from the local status checksum (local
      changed since last sync), preserve via a new virtual seam (base no-op; cloud override
      → SaveLocalCopyForRecovery) — then apply as normal.
- [ ] Same guard in TeamCollectionApi.HandleReceiveUpdates (the Sync button loop).
- [ ] Unit tests through TestFolderTeamCollection (seam already has the synchronous-queue
      test hooks); assert preserve-called-iff-locally-modified.
- [ ] NOT needed (per John): persisting cloud checkout state to the local status file —
      that was only required to reproduce folder-TC *blocking* semantics; John chose
      apply-and-preserve instead.
- [ ] E2E: this unblocks E2E-4's blocked .bloomSource sub-requirement — extend that spec
      when convenient.

### 9. Account-switch behavior (John decision, 9 Jul — unblocks E2E-10)  `[medium-large]`
Status: NOT STARTED (decision recorded verbatim; implement after items 7/8)
John's spec: local machine access is unrestricted; only shared-data operations are gated by
the CURRENT logon's server permissions. Collection was joined under account A, Bloom now
signed in as B:
- B NOT a member of the TC → REFUSE to open the collection. Message must name the current
  logon, say it is not a member, give the admin email(s) to ask for membership, and name
  the last team member who edited this collection on this machine.
- B IS a member → open CONNECTED. Books locally checked out by A show as checked out by A
  but may be edited as if checked out by B — ONLY if the server state would have let A edit
  here (i.e. A's lock is for THIS machine; not if A holds it elsewhere). On first edit of
  such a book, atomically switch the checkout everywhere to B. If B checks it in (even
  without editing), history records the checkin by B.

## Also queued from dogfooding (not in John's list, orchestrator-flagged)
- Administrators field shows the REGISTRATION email (john_thomson@sil.org) instead of the
  signed-in account email for cloud TCs (`ConnectToCloudCollection` sets
  `Settings.Administrators = new[] { CurrentUser }`) — cosmetic identity-model
  inconsistency, fix opportunistically with item 4+5 or 6.

## Progress log
(orchestrator appends: date · what was just completed · EXACT next action)
- 9 Jul 2026 · Batch plan created; full-matrix baseline run in progress (validates
  checkin-comment fix + 5s poll live) · Next: item 1 ("Bloom is busy" l10n) code work
  while the matrix runs.
- 9 Jul 2026 (later) · Baseline matrix 13/13 GREEN (31 min). Items 1–3 code done +
  committed (2d74d280f, 6f0c4a068, 207cc1d0); unit suites green (C# 363/363, panel vitest
  11/11). Screen NOW LOCKED (John away): all Bloom-launching verification queued — e2e-1
  (item 1 gate), e2e-2 (items 2+3), plus item 3 visual check. A Debug Bloom (PID 48012,
  origin unknown, possibly John's) is running and locks output/Debug — build/test with
  `-c Release` until it's gone; do NOT kill it without John · Next: item 4+5 design read
  (CloudTeamCollection change-application path), code-only work.
- 9 Jul 2026 (later still) · Item 4+5 CODE DONE on branch `task/b1-45-auto-sync` (created
  from cloud-collections; not yet merged): TeamCollection.CanAutoApplyRemoteChanges +
  RemoteBookAutoApplyQueue (auto-apply for cloud TCs, folder TCs unchanged); "Receive
  Updates" renamed to "Sync" everywhere (dialog + per-book panel) plus a fixed
  needsReload/updatesAvailable priority bug for cloud found along the way; XLF renamed
  TeamCollection.ReceiveUpdates → TeamCollection.Sync. Diagnosis of the missing
  "updates available" badge: it's driven solely by the message log's NewStuff milestone
  (TeamCollectionStatus.NewStuff → teamCollection/tcStatus), a SEPARATE signal from
  tcStatusMetadata's updatesAvailableCount (which was likely already correct/live) — see
  the item's own diagnosis bullet above for detail. Tests: C# required filter 375/375
  green (incl. new RemoteBookAutoApplyQueueTests + TeamCollectionAutoApplyTests); both
  touched vitest files green (5/5, 13/13); yarn typecheck/eslint clean. E2E NOT run
  (desktop locked; forbidden by this task's rules) — `e2e-2-collaboration-loop` +
  `e2e-8-receive-during-send` + e2e-1 (XLF gate) queued alongside items 1–3's pending
  runs · Next: orchestrator review + merge of task/b1-45-auto-sync into cloud-collections,
  then the queued full E2E pass covering items 1–5, then item 6 (join-card integration).
- 9 Jul 2026 (even later) · Item 6 CODE DONE on branch `task/b1-6-join-cards` (created from
  cloud-collections; not yet merged): removed MyCloudCollectionsSection.tsx (+ test);
  CollectionChooserApi gains `collections/getJoinCards` (SharingApi.GetMyCollectionsForJoinCards
  for the signed-in check + cloud list, no network call when signed out; ComputeJoinCards is the
  pure id-matching helper, internal static, unit-tested in new CollectionChooserApiTests.cs;
  GetLocalCloudCollectionIds scans MRU + discovered local folders' TeamCollectionLink.txt files
  the same way CloudJoinFlow.DetermineScenario does, but across ALL known folders rather than one
  expected name, since a join card is about "has ANY local copy", not "would this name collide").
  CollectionCard grows an isJoinCard variant (title + TC icon + reused "Get" cue only, no per-card
  fetch, no Show-in-Explorer menu); CollectionCardList appends joinCollections AFTER its
  maxCardCount(10) slice so they're never capped. CollectionChooser.test.tsx rewritten for the
  card-based flow; new CollectionCardList.test.tsx covers the append-after-slice logic; new
  CollectionCardList.stories.tsx "WithJoinCards" story. Removed 4 now-orphaned untranslated XLF
  entries from the deleted sidebar (kept + repurposed CollectionChooser.PullDown, "Get", as the
  join cue). C# required filter 380/380 green; CollectionCardList.test.tsx 4/4 and
  CollectionChooser.test.tsx 3/3 green; yarn typecheck and eslint (changed files) clean. E2E NOT
  run (desktop locked; forbidden by this task's rules) — `join-auto-open` (checked: drives
  pullDown/openCollection directly via HTTP, doesn't touch the chooser UI, should be unaffected)
  + `e2e-1-create-share` (XLF gate) queued · Next: orchestrator review + merge of
  task/b1-6-join-cards into cloud-collections, then item 7 (progressive join) once the queued
  E2E pass covering items 1–6 runs.
