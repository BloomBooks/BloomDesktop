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
Status: DONE (commit 2d74d280f; e2e-1 GREEN in the 9 Jul PM 4-spec queue)
- [x] Found: ExternalBusyOverlay.tsx's fallback message already had id Common.BloomIsBusy
      but no XLF entry; the useL10n lookup logged the complaint on every collection-tab
      mount. (The specific BloomBridge message the overlay usually shows is intentionally
      unlocalized; only the fallback needed an entry.)
- [x] Added to BloomLowPriority.xlf (John's choice) with translate="no" + context note.
- [x] Verify: `e2e-1-create-share` GREEN (9 Jul PM 4-spec queue, 4/4 in 9.5 min). An
      earlier attempt failed ONLY because the desktop locked mid-run (WebView2 stuck at
      about:blank — the known signature).

### 2. Poll immediately on book selection  `[quick]`
Status: DONE (commit 6f0c4a068; e2e-2 GREEN in the 9 Jul PM queue)
- [x] TeamCollectionManager ctor now subscribes BookSelection.SelectionChanged → if the
      current collection is a CloudTeamCollection, Task.Run(PollNow). Results flow through
      the existing change-event pipeline (same as timer polls).
- [x] Guard: PollNow's own in-flight coalescing covers rapid selection changes; null
      bookSelection guard for unit-test constructions (caught by test run: 10 failures,
      fixed, 363/363 green).
- [x] `e2e-2-collaboration-loop` GREEN (9 Jul PM queue) (note: E2E uses a 5s poll, so the
      speedup itself is mostly invisible there — the run guards against regressions; the
      real check is John's manual test at the default 60s poll).

### 3. Center the checkin-progress dialog in the status panel  `[quick]`
Status: CODE DONE + e2e-2 GREEN — remaining: John's VISUAL check of the centered dialog
during his next manual checkin
- [x] It's the React BloomDialog in TeamCollectionBookStatusPanel (not BrowserProgressDialog):
      now positioned via PaperProps over the #teamCollection div's center, vertically
      clamped so the paper stays on-screen (the panel hugs the window bottom). Falls back
      to default whole-window centering when #teamCollection is absent (unit tests).
      Panel vitest suite 11/11.
- [x] `e2e-2-collaboration-loop` GREEN (9 Jul PM queue).
- [ ] [HUMAN, John] Visual check that the checkin-progress dialog appears centered over the
      status panel during a manual checkin.

### 4+5. Automatic remote-update application + in-place Sync (one work item)  `[medium]`
Status: MERGED + E2E VERIFIED (9 Jul PM queue: e2e-2 + e2e-8 GREEN with auto-apply active;
earlier: orchestrator re-ran C# 375/375 + both vitest files green, reviewed
queue/wiring/panel/XLF line by line). Residual risk (checkout racing the download window)
did not surface in the contention-heavy e2e-8 run; keep an eye on it in the pre-push full
matrix.
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
Status: CODE DONE on branch `task/b1-7-progressive-join` (created from origin/cloud-collections;
not yet merged) — E2E verification QUEUED (orchestrator's job per this task's hard rules)
- [x] On join, fetch collection settings + book list (titles) first, open the collection
      immediately; books not yet downloaded show a placeholder icon suggesting an
      in-progress download. CloudJoinFlow.JoinCollection: removed the blocking
      CopyAllBooksFromRepoToLocalFolder call; every repo book is now queued via the new
      TeamCollection.QueueBookForBackgroundDownload right after settings download.
      CollectionApi.HandleBooksRequest merges CloudTeamCollection's repo book list against local
      folders (new pure ComputeNotYetDownloadedBookEntries + BookListEntry DTO) so repo-only
      books appear with `notYetDownloaded: true`; BookButton.tsx renders these as a simple
      dashed-border placeholder (cloud-download icon + title, no thumbnail request) instead of
      the normal interactive button.
- [x] Background-download books; swap each placeholder for the real icon as its download
      completes. New TeamCollection.DownloadMissingBookInBackground (the RemoteBookAutoApplyQueue
      worker's new branch for books with no local folder at all) downloads, updates status, then
      invalidates the cached book list and sends the existing "editableCollectionList"/"reload:"
      websocket event so BooksOfCollection.tsx's collections/books re-fetch swaps the placeholder
      for the real button automatically.
- [x] Selecting a not-yet-downloaded book bumps it to the front of the download queue; status
      panel shows a "downloading" message until it arrives. RemoteBookAutoApplyQueue gained
      EnqueueFront (priority, dedupe-preserving, never interrupts an in-flight download);
      CollectionApi's selected-book POST handler gracefully detects a placeholder click
      (TryPrioritizeNotYetDownloadedBook, via new CloudTeamCollection.TryGetBookNameForInstanceId
      + PrioritizeDownload) and bumps it to the front instead of crashing on the missing
      BookInfo. DEVIATION (flagged for John/orchestrator): the "downloading" indicator is shown
      as a persistent placeholder icon on the book button itself (visible for every
      not-yet-downloaded book) rather than routing through the real BookSelection/preview-pane
      and TeamCollectionBookStatusPanel.tsx's StatusPanelState union, per the scout notes' exact
      seam — faking a "selected" placeholder book (no real local folder/Book object exists yet)
      risked destabilizing the preview iframe and the lock/checkout endpoints that also key off
      BookSelection.CurrentSelection. The functionally important, tested part (priority bump) IS
      implemented; only the panel-specific visual treatment was simplified.
- [x] Same SAFETY rule as item 4+5: a book appears in the collection only as placeholder
      (no dangerous actions possible) or fully downloaded — never as a half-populated folder the
      user can act on. Temp-folder staging + atomic swap. The placeholder branch in BookButton.tsx
      is a completely separate render path with no context menu, no rename, no double-click-edit;
      the only action is a click that posts to collections/selected-book (priority bump, graceful,
      never a real selection). CopyBookFromRepoToLocal's existing stage-then-atomic-swap (unchanged)
      still guarantees a book is placeholder-only or fully downloaded, never half-populated.
- [x] Handle interruption: Bloom closed mid-join resumes/completes downloads on next open
      (SyncAtStartup should already fetch missing books — verify). Verified AND changed: cloud
      SyncAtStartup's "brand new book!" branch now reroutes to the same background queue
      (QueueBookForBackgroundDownload) instead of fetching synchronously inline, when
      CanAutoApplyRemoteChanges is true (cloud only) — so a half-joined collection's next open
      stays fast and downloads keep resuming in the background, instead of blocking the startup
      sync dialog on every still-missing book. Folder TCs are completely unaffected (unchanged
      synchronous fetch, pinned by a new regression test).
- [ ] Verify: `join-auto-open` + `e2e-9-new-book-lifecycle`; consider a new spec for the
      placeholder/priority behavior if cheap. NOT run — this task's hard rules forbid launching
      Bloom/e2e (orchestrator's job after merge, serialized with other E2E runs).

Implementation notes (9 Jul, agent report):
- Files changed: RemoteBookAutoApplyQueue.cs (EnqueueFront + LinkedList-based priority queue,
  dedupe-preserving); TeamCollection.cs (QueueBookForBackgroundDownload/PrioritizeBackgroundDownload,
  DownloadMissingBookInBackground, ProcessAutoApplyRemoteChange's new missing-folder branch,
  SyncAtStartup's cloud rerouting); CloudJoinFlow.cs (blocking call removed, enqueue loop added);
  CloudTeamCollection.cs (TryGetBookInstanceIdForName/TryGetBookNameForInstanceId/PrioritizeDownload);
  CollectionApi.cs (BookListEntry DTO, ComputeNotYetDownloadedBookEntries pure merge function +
  GetNotYetDownloadedBookEntries wiring via TeamCollectionApi.TheOneInstance -- no new constructor
  dependency needed, following SharingApi's existing precedent -- and TryPrioritizeNotYetDownloadedBook
  for the graceful selected-book handling); BookButton.tsx (placeholder render branch + new
  CollectionTab.BookNotYetDownloaded tooltip string); BooksOfCollection.tsx (IBookInfo.notYetDownloaded).
- New XLF string CollectionTab.BookNotYetDownloaded added to Bloom.xlf, translate="no", flagged as
  provisional placement pending John's priority confirmation (note in the entry itself suggests
  BloomMediumPriority.xlf as a likely alternative). The pre-existing sibling progress-message ids in
  this same code path (JoiningCloudCollection, FetchedNewBook, and this task's new
  FetchingNewBookInBackground) have NO XLF entries at all -- an established (if arguably
  incomplete) precedent for TeamCollection sync-dialog progress text in this codebase; the new one
  was left unlocalized to match, flagged here rather than silently deviating.
- Tests: C# required filter 393/393 green (15 new: 4 EnqueueFront tests + 1 real-async EnqueueFront
  sanity test in RemoteBookAutoApplyQueueTests.cs; 2 missing-folder ProcessAutoApplyRemoteChange
  tests + 3 SyncAtStartup rerouting tests in TeamCollectionAutoApplyTests.cs, using
  TestFolderTeamCollection's existing AutoApplyRemoteChangesForTests toggle so the shared
  base-class logic is exercised without needing a full CloudTeamCollection; 6 new
  CollectionApiTests.cs tests for the pure ComputeNotYetDownloadedBookEntries merge function).
  CloudSyncAtStartupTests.SyncAtStartup_NewBookOnlyInRepo_IsFetchedToLocal updated per this item's
  own instruction (TestOnly_MakeAutoApplyQueueSynchronous added, assertion kept, reasoning
  documented in the test). BookButton.test.tsx (new, 5/5 green) covers the placeholder
  render/label/click-priority-bump behavior and the unaffected normal-button paths. yarn typecheck
  and eslint show no NEW errors/warnings introduced (compared before/after via git stash) beyond
  this codebase's large pre-existing unrelated baseline of typecheck errors.
- Deliberate omissions/risks for the orchestrator to re-verify live: (1) the status-panel
  simplification noted above; (2) no dedicated CloudJoinFlow test file was added (no existing
  FakeRestExecutor-based harness for it, and the diff there is a small, low-risk 3-line change
  covered indirectly by the queue's own tests) -- the orchestrator's join-auto-open E2E run is the
  real coverage for this path; (3) CollectionApi.HandleBooksRequest now calls
  CloudTeamCollection.GetBookList()/EnsureCacheHydrated() on every collections/books request for a
  cloud TC, which may trigger a network hydrate call the first time (idempotent afterwards) --
  minor latency risk, not previously present in this endpoint; (4) the placeholder's "id" is the
  book's stable InstanceId (matches BookInfo.Id once downloaded) so the React key doesn't change
  across the download -- worth an E2E spot-check that the placeholder truly swaps in place rather
  than flickering/remounting; (5) no dedicated E2E spec for the placeholder/priority behavior was
  added (existing join-auto-open + e2e-9-new-book-lifecycle only) -- consider one if the live
  verification surfaces gaps.

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
Status: CODE DONE on branch `task/b1-9-account-switch` (created from origin/cloud-collections;
not yet merged) — E2E verification QUEUED (orchestrator's job; hard rules forbade
launching Bloom/e2e for this task)
John's spec: local machine access is unrestricted; only shared-data operations are gated by
the CURRENT logon's server permissions. Collection was joined under account A, Bloom now
signed in as B:
- [x] B NOT a member of the TC → REFUSE to open the collection. Message must name the current
      logon, say it is not a member, give the admin email(s) to ask for membership, and name
      the last team member who edited this collection on this machine.
      TeamCollectionManager.CheckConnection gained an `allowHardRefusal` parameter (default
      false, preserving every existing mid-session caller); only the constructor's initial
      open-time call passes true. CloudTeamCollection.CheckConnection's non-member branch now
      sets a new TeamCollectionMessage.IsAccessRefusal flag and composes the full detail text
      (admins + last-known-user, see ComposeNotAMemberRefusalDetail) instead of the old
      one-line message; a hard-refusal message throws the new
      TeamCollectionAccessRefusedException, which propagates up through Autofac/ProjectContext
      to Program.HandleErrorOpeningProjectWindow (new early special-case: plain message box,
      no "Report this crash" flow, then falls through to the existing chooser-reopen path
      exactly like any other failed project open).
- [x] B IS a member → open CONNECTED. Books locally checked out by A show as checked out by A
      but may be edited as if checked out by B — ONLY if the server state would have let A edit
      here (i.e. A's lock is for THIS machine; not if A holds it elsewhere). On first edit of
      such a book, atomically switch the checkout everywhere to B. If B checks it in (even
      without editing), history records the checkin by B.
      New virtual seams on TeamCollection (IsEditableHere, CanTakeOverLockOnThisMachine,
      TryTakeOverLock) default to today's strict behavior for folder TCs; CloudTeamCollection
      overrides them for the same-machine-different-account case. New RPC
      tc.checkout_book_takeover (migration 20260709000007) atomically reassigns a lock from a
      different account to the caller ONLY when the existing lock's machine matches — purely
      additive, does NOT modify checkin_start_tx/checkin_finish_tx. The takeover call happens
      in PutBookInRepo just before check-in (there is no per-keystroke "edit happened" hook
      anywhere in this codebase — confirmed by research — so "on first edit" is implemented as
      "on first check-in of that edit", the earliest point a takeover has any observable
      effect) and in AttemptLock (for an explicit "check out" click, though the UI is unlikely
      to show that affordance here since IsEditableHere already reports the book as usable).
      Checkin attribution already falls out for free (checkin_finish_tx uses the caller's JWT).

### 10. AWSSDK.S3 version bump (John decision, 9 Jul: take it on this branch)  `[quick-medium]`
Status: CODE DONE + SUITES GREEN on branch `task/b1-10-awssdk-bump` (bump 9b81c6040; not yet
merged) — remaining: orchestrator's e2e-1 + e2e-2 through MinIO, then John's [HUMAN] web
up/download check
- [x] Bump AWSSDK.S3 (and its AWSSDK.Core pair) to current stable in the csproj(s); check
      whether BloomHarvester/other projects pin the same package family and must move in
      lockstep. DONE: BloomExe.csproj Core 3.5.1.32 -> 4.0.100.3, S3 3.5.3.10 -> 4.0.100.3
      (major v4 jump); server/dev/parity-check floats 3.* -> 4.*. No other project in this
      repo pins the family (BloomHarvester is a separate repo; there is no central
      Directory.Packages.props — per-csproj pins are the convention). AWSSDK.SecurityToken
      is not referenced anywhere (per-book session creds arrive as plain strings from the
      edge functions), so only S3+Core move. project.assets.json confirms no SIL package
      transitively pins AWSSDK.Core. v4 adjustments (details in commit 9b81c6040): checksum
      config RequestChecksumCalculation/ResponseChecksumValidation=WHEN_REQUIRED on the two
      MinIO-facing client builders (CloudBookTransfer.BuildDefaultClient,
      CloudTeamCollection.BuildS3Client) because v4's WHEN_SUPPORTED default sends CRC32/
      CRC64 trailing checksums S3-compatible endpoints may reject; BloomS3Client (real AWS
      only) deliberately keeps the v4 defaults. Null-collection/bool? compile+runtime fixes
      in S3Extensions.ListAllObjects and BloomS3ClientTests.DeleteFromUnitTestBucketAsync;
      removed two orphaned usings that broke the v4 compile (ThirdParty.Json.LitJson was
      embedded in AWSSDK.Core v3 and is gone in v4).
- [x] Suites: cloud filter + ONE full BloomTests run (AWSSDK is used by the BloomLibrary
      web-upload code — WebLibraryIntegration — so cloud-only filters are NOT sufficient).
      DONE: baseline full run on UNMODIFIED cloud-collections FIRST (so pre-existing
      failures can't be blamed on the bump): 3036 passed / 0 failed / 13 skipped / 3049
      total. Post-bump: cloud filter 387/387; full run 3036 passed / 0 failed / 13 skipped
      / 3049 total — identical to baseline, zero regressions. S3-specific fixtures called
      out explicitly: CloudBookTransferTests 11/11, BloomS3ClientTests +
      CloudEnvironmentTests' S3ForcePathStyle test, 44/44 in the combined ~S3/~
      CloudBookTransfer/~BloomS3Client filter, including the LIVE
      DownloadBook_DoesNotExist_Throws which hit the real BloomLibraryBooks-UnitTests
      bucket with the v4 client (validating the null-S3Objects fix against real AWS).
- [ ] E2E: at least e2e-1 + e2e-2 (S3 up/down through MinIO exercises the new SDK's
      path-style + AssumeRole handling — the risky surface for a bump).
- [ ] [HUMAN, John] Manual check that web book upload (publish to bloomlibrary.org) and
      download (into Bloom) still work — recorded in GOING-LIVE.md 4.3.

## Also queued from dogfooding (not in John's list, orchestrator-flagged)
- Administrators field shows the REGISTRATION email (john_thomson@sil.org) instead of the
  signed-in account email for cloud TCs (`ConnectToCloudCollection` sets
  `Settings.Administrators = new[] { CurrentUser }`) — cosmetic identity-model
  inconsistency, fix opportunistically with item 4+5 or 6.
- Tier-timing fix (GOING-LIVE.md Phase 5, `task/b1-tier-timing`): `CheckDisablingTeamCollections`
  gated solely on `CurrentCollection == null`, which for a cloud TC doesn't mean
  "Settings.Subscription is trustworthy" (CurrentCollection is set before the connect-and-sync
  sequence completes, and that sequence's success depends on cloud sign-in timing plus an S3
  download that silently swallows exceptions) — so a healthy cloud TC could be permanently
  disabled for the session on a stale/blank subscription snapshot. Fixed by deferring the cloud
  check (WorkspaceModel) until after the collection-file sync, and re-reading the SubscriptionCode
  fresh from disk at that point instead of trusting the in-memory snapshot. Folder-TC behavior/
  timing unchanged. See branch for full diagnosis + tests.

## OUTSTANDING BUGS (10 Jul 2026 PM — the current work list)
0. **[NEEDS JOHN — product decision] Item 9's same-machine takeover can steal ANY same-machine
   lock, even across separate collection folders (found by e2e-4 after its download bugs were
   fixed).** Scenario: Bob (admin) force-unlocks Alice's checkout and takes the lock himself;
   Alice's later attemptLockOfCurrentBook RETURNS FALSE — but the server lock silently ends up
   reassigned to ALICE, because item 9's takeover path (AttemptLock → TryTakeOverLock →
   checkout_book_takeover) fires whenever the existing lock's MACHINE matches, and in E2E (and
   any genuinely shared computer) every user is on the same machine. The machine-match gate
   cannot distinguish John's intended scenario ("collection was joined under account A, B opens
   the SAME local folder") from two users with SEPARATE local folders on one computer (two
   'seats', which is what e2e-4 simulates and what a shared lab machine would really be).
   Design options sketched for John:
   (a) Server-side 'seat': checkout_book/checkout_book_takeover store+compare a per-local-folder
       id (e.g. hash of folder path) alongside machine — cleanest semantics, needs a migration +
       pgTAP + client change;
   (b) Client-side gate on the LOCAL folder's own state: only allow takeover if THIS folder
       shows evidence the lock holder was using THIS folder. TeamCollectionLastKnownUser.txt
       does NOT work for this as-is (CheckConnection overwrites it with the NEW user at open
       time, before any takeover); writing a minimal local status record at cloud checkout would
       work but John earlier decided cloud checkouts deliberately DON'T write local status;
   (c) Accept the behavior (any same-machine user can take over any same-machine lock) and fix
       e2e-4's expectation — probably wrong: it makes force-unlock semantics unreliable on
       shared machines, and the takeover is SILENT (attemptLock even reported false while the
       server lock changed hands — at minimum that inconsistency is a bug in any option).
   Suggested: (a). Until decided, e2e-4 fails at its 'server lock is exactly Bob's' assertion
   (spec line ~166). The e2e-4 DOWNLOAD failures that motivated the original bug #1 are FIXED
   (see below).
1. **e2e-4 background download fails + retry skipped (FIXED 10 Jul PM, verified by rerun —
   the book now downloads in ~5s; kept for the record).** Evidence
   (bob-joined SIL log, 14:51, preserved by the new durable logging): the one real download
   attempt failed with `Could not find file 'C:\Users\<user>\AppData\Local\Temp\
   BloomCloudTCDownload\A5 Portrait.htm'` — the cloud download STAGING FOLDER IS A FIXED
   SHARED TEMP PATH (`Temp\BloomCloudTCDownload`), so concurrent instances (two Blooms run in
   every two-instance spec, plus any leftover files from earlier runs/specs) can clobber or
   half-empty each other's staging area mid-copy. FIX: make the staging dir unique per
   download (e.g. `BloomCloudTCDownload-<pid>-<random>`), clean up after. Secondly: after
   that failure, Alice's checkout made QueueMissingRepoBooksForBackgroundDownload skip every
   retry (books locked by ANYONE are skipped). The skip is TOO BROAD — a book locked by
   someone ELSE is still safely downloadable (that is exactly what Receive does); the skip
   only needs to cover books locked BY THE CURRENT USER (the local-rename-mid-checkin edge,
   where the old repo name intentionally has no local folder). FIX: change the guard in
   TeamCollection.QueueMissingRepoBooksForBackgroundDownload from "locked by anyone" to
   "locked by me", and add a unit test mirroring QueueMissingRepoBooks_BookLockedInRepo_SkipsIt
   but with a foreign lock expecting download. Then rerun e2e-4.
2. **e2e-5 + e2e-8 singles: BOTH PASSED (10 Jul PM, post-merge tree)** — confirms their 10 Jul
   AM matrix failures were transient infra as suspected. Current single-spec scoreboard on the
   merged + defect-fixed tree: e2e-3 ✅, e2e-5 ✅, e2e-8 ✅, e2e-10 ✅; e2e-4 ❌ blocked solely
   on bug #0 (its download failures are fixed; it now fails at the takeover-semantics
   assertion, spec line ~166).
3. **Full E2E matrix** not yet run on the post-defect-fix, master-merged state (was 8/14
   before the fixes). Run it after John decides bug #0 (or accept one known e2e-4 failure).
4. Cosmetic (tracked): Administrators field shows registration email, not signed-in email
   (see "Also queued from dogfooding").
5. **Preflight (10 Jul PM, John's request):** light-review pass over the day's diff found 2
   valid adjacent holes, BOTH FIXED + tested (72246c2975): per-account (not per-instance)
   claim_memberships guard; machine-aware lock skip in the requeue pass. The GitHub half of
   preflight (draft PR, Devin, Greptile/CodeRabbit, CI gauntlet) is BLOCKED: `gh` is not
   authenticated in the agent session — John must run `gh auth login`, then re-run
   `/preflight` to create the draft PR and run the bot gauntlet.
6. **Full C# suite: RESOLVED AS FLAKE** — the first run's single failure (1/3131) did not
   reproduce on the identification rerun (3120 passed / 0 failed / 3133 total, merged tree).

## Progress log
(orchestrator appends: date · what was just completed · EXACT next action)
- 10 Jul 2026 (EOD — SHUTDOWN STATE; machine going to sleep; next session may be a different
  agent: read this entry + OUTSTANDING BUGS + SQUASH-PLAN.md and you have everything) ·
  FULL MATRIX under HEAVY LOAD: 10/14 (40 min, ran concurrently with the 16-min full C#
  suite + review agents — the known load-correlated-flake regime). Failures: e2e-4
  (EXPECTED — bug #0, John's pending decision), e2e-3 / e2e-6 / e2e-9 (all three are
  suspected LOAD FLAKES: e2e-3 passed standalone TWICE earlier today on this exact tree;
  e2e-6/e2e-9 were green in the last pre-batch matrix; artifacts in
  src/BloomTests/e2e/test-results/). BOT GAUNTLET at cutoff: CI 2/2 pass (pr-automation +
  track; heavy CI doesn't run on this draft); CodeRabbit TIMED OUT after 35 min (no
  review/comment via API); Devin TIMED OUT this session (huge PR — only the diff tree
  renders on its page, no findings pass yet for HEAD; it keeps analyzing server-side).
  Devin/CodeRabbit results will simply be waiting on PR #8048 whenever checked next.
  SQUASH-PLAN.md committed (d8ff5c830e): review-grained packaging branch design, 9 grouped
  commits, regenerable, byte-identical-verified · NEXT SESSION, in order: (1) rerun e2e-3,
  e2e-6, e2e-9 STANDALONE on an idle machine (expect green; investigate for real if any
  fails again), (2) John: bug #0 decision (options in OUTSTANDING BUGS #0; ready-to-implement
  option-(a) sketch in BUG0-OPTION-A-SKETCH.md, same folder), implement + rerun e2e-4,
  (3) gather bots: run the devin-review skill against PR 8048 (it mirrors findings to the
  PR) + read CodeRabbit's review if posted; fix/reply per preflight rules, (4) execute
  SQUASH-PLAN.md once 1–3 are done, open the new PR from cloud-tc-for-review, close 8048
  with a pointer, (5) John's [HUMAN] tests: item 3 centered dialog, item 10 web
  up/download (GOING-LIVE.md 4.3). Environment reminders for the resumer: functions-serve
  zombie rule (server/dev/README.md) after any sleep/restart of the stack; E2E needs
  BLOOM_E2E_SCREEN=1 and an unlocked desktop; front-end is pnpm now (e2e harness stays
  yarn).
- 10 Jul 2026 (PM, gauntlet running) · John authenticated gh. DRAFT PR CREATED:
  https://github.com/BloomBooks/BloomDesktop/pull/8048 (cloud-collections → master, draft).
  Devin triggered for HEAD 24b0f5c740 via the pr-automation workflow (completed = trigger
  loaded); CodeRabbit + CI self-triggered on the PR. FULL E2E MATRIX running concurrently
  (expected: 13/14, e2e-4's takeover assertion the only known failure — bug #0 pending
  John's decision). If this session is cut off mid-gauntlet: re-run `/preflight` in a fresh
  session — it re-enters wherever the PR/bots currently are (the devin-review skill gathers
  + mirrors any finished Devin findings; matrix results land in the next entry) · Next:
  poll bots (~30 min cap) → mirror Devin findings → fix/reply → matrix verdict → John's
  decisions (bug #0, human tests).
- 10 Jul 2026 (PM, preflight — END-OF-SESSION STATE) · Preflight (John's request) ran to the
  limit of what the session could do: LOCAL HALF COMPLETE — light-review sub-agent over the
  day's diff found 2 valid adjacent holes, both FIXED + unit-tested + pushed (72246c2975:
  per-account claim_memberships guard — an in-session account switch would have resurrected
  defect 2; machine-aware lock skip — your own other-machine checkout no longer blocks the
  self-heal download). Gate results: cloud filter 422/422; FULL C# suite 3120/0/13 of 3133
  (first run's single failure did NOT reproduce → flake); pnpm lint 0 errors; targeted vitest
  14/14; mergeability with origin/master clean (0 behind, 0 conflicts). E2E singles on the
  final tree: e2e-3/5/8/10 ALL PASS; e2e-4 fails ONLY at the takeover-semantics assertion
  (OUTSTANDING BUGS #0, John's decision — options a/b/c documented there, recommend (a)
  server-side seat). GITHUB HALF BLOCKED: `gh` unauthenticated in the agent session, so no
  draft PR / Devin / CodeRabbit / CI ran — after `gh auth login`, re-run `/preflight`.
  Preflight report artifact (decisions + copy-back form) published for John · NEXT, in
  order: (1) John: gh auth login + answer the report's decisions (esp. bug #0), (2) implement
  bug #0 as decided + rerun e2e-4, (3) full E2E matrix, (4) re-run /preflight for the bot
  gauntlet, (5) John's [HUMAN] tests: item 3 centered dialog, item 10 web up/download
  (GOING-LIVE.md 4.3).
- 10 Jul 2026 (PM, master integration) · cloud-collections is now UP TO DATE with
  origin/master (c41fcfd2bd) — as a MERGE, not the planned rebase, deliberately: a true
  rebase meant replaying 189 commits over a master that already contains cherry-picked
  batch commits (e.g. the Common.BloomIsBusy l10n fix is master's tip), and it started
  conflicting at commit 4/189 (add/add on files master partially has); cloud-collections
  also already has merge-style history (task merges), so linearizing + force-pushing a
  shared branch was worse than integrating. The merge itself completed with ZERO conflicts
  (the feared overlap files — 2 XLF, CollectionApi.cs, ExternalApi.cs — all auto-merged;
  the cherry-picked l10n fix was byte-identical on both sides). A safety branch
  `cloud-collections-pre-rebase-2026-07-10` marks the pre-merge state. PNPM: the front-end
  (src/BloomBrowserUI, BloomVisualRegressionTests, src/content) is now pnpm 11.5.2 — NEVER
  yarn/npm there anymore (root AGENTS.md updated by master); the E2E harness
  (src/BloomTests/e2e) deliberately KEEPS its own yarn.lock (unaffected by the migration)
  · Next: pnpm install + C# cloud filter on the merged tree (running), push
  cloud-collections, then OUTSTANDING BUGS #1 (e2e-4) and the remaining test pipeline.
- 10 Jul 2026 (PM, later) · e2e-4 rerun FAILED with a NEW, fully-diagnosed signature (see
  OUTSTANDING BUGS #1 above — fixed shared download staging folder + over-broad locked-book
  skip in the new retry pass). Per John's live instruction: pausing the test loop here,
  merging the defect-fix branch, then REBASING cloud-collections onto current origin/master
  (~62 commits incl. the pnpm migration) before returning to test fixes · Next: merge
  task/b1-postbatch-defects (fast-forward) + push, rebase, post-rebase build sanity, then
  fix OUTSTANDING BUGS #1 and rerun e2e-4/5/8 + full matrix, then John's [HUMAN] checks.
- 10 Jul 2026 (PM — HANDOFF ENTRY; possibly the last session with this agent for a while;
  written for human/agent resumers weeks later) · ALL THREE post-batch defects DIAGNOSED,
  FIXED, COMMITTED on `task/b1-postbatch-defects`, unit suites green (cloud filter 418/418),
  and E2E-verified per the fail-fast protocol (one failing spec at a time, no full-suite
  reruns until each passed). Root causes, for the record:
  · DEFECT 1 (books never arrived after join-relaunch; e2e-3/e2e-4): the pullDown→kill→
    relaunch pattern guarantees the in-memory RemoteBookAutoApplyQueue dies with the process;
    the relaunch's SyncAtStartup rerouting was the only redelivery path and every failure in
    that pipeline was SILENT, while the poll only raises events for books whose repo state
    CHANGED — so one miss = book missing forever. Fix (4339e02d60): new
    TeamCollection.QueueMissingRepoBooksForBackgroundDownload (queues every unlocked repo
    book with no local folder), called from CloudTeamCollection.StartMonitoring (post-sync)
    and after every OnPolledChanges — any drop now self-heals within one poll interval; plus
    durable SIL logging on all previously-silent paths (incl. ReportProgressAndLog, whose
    startup-sync record previously vanished with the collection folder). 4 new unit tests.
    VERIFIED: e2e-3 PASSED (was the failing waitForBookFile signature).
  · DEFECT 2 (e2e-10 bob-takeover: alive 90s, empty stdout, no window/server): dotnet-stack
    dump of the live hung process showed the UI thread blocked in MessageBox.Show inside
    TeamCollectionManager's ctor. Chain: an APPROVED-but-never-CLAIMED membership (bob opens
    ALICE's local folder — item 9's shared-computer scenario — so bob never ran the join flow,
    the only place claim_memberships was called) passes CheckConnection's EMAIL-based
    my_collections check, then get_collection_state throws not_a_member (RLS gates are
    user_id-based) during the ctor's first sync; the generic catch shows a MODAL MessageBox
    no automation can dismiss. Fix (ae35b87c34): CheckConnection now calls ClaimMemberships
    (idempotent, once per session) on membership confirm; NonFatalProblem.Report in
    --automation mode writes BLOOM_AUTOMATION_NONFATAL_PROBLEM + stack to stdout and returns
    instead of blocking (mirrors the RunningInConsoleMode guard) — any future startup report
    is a readable harness-log line, never a silent hang. New unit test pins the claim call.
    VERIFIED: e2e-10 PASSED end-to-end (refusal + takeover-checkin attribution).
  · DEFECT 3 (Cannot Find API Endpoint teamCollection/capabilities toast): the endpoint was
    project-level but is legitimately probed with no project open — the E2E harness readiness
    poll (proven: a probe landed BEFORE any WebView2 existed in bob's 10:11 log) and late
    calls from a closing collection tab while the chooser is up (John's sighting; the item-6
    "chooser bundle hook" hypothesis was DISPROVEN — no chooser component calls it). Fix
    (4819eda881): registration moved to the app-level SharingApi (TheOneInstance precedent),
    all-false when no project/TC is current. Fallout fix (c24af86042): two harness call
    sites that single-shot-asserted supportsSharingUi===true right after a relaunch now use
    the same 20s expect.poll as every other site (the app-level endpoint answers
    truthfully-false while the project is still opening; the old one-shots only ever passed
    because a project-level registration race hid the timing). VERIFIED by the e2e-3/e2e-10
    passes above (both exercise the polled path).
  Also noteworthy: the window-placement watcher (7029006d5) is CONFIRMED working now
  (windowPlacement.log files written, windows moved to the spare screen); the missing SIL
  Log-tmp files for hard-killed instances are expected (SIL Logger doesn't flush on kill) —
  stdout via the NonFatalProblem automation line is now the reliable channel · NEXT ACTIONS,
  in order: (1) e2e-4 + e2e-5 + e2e-8 singles (running/queued at handoff time — see the next
  entry if one was added, else run them first), (2) merge `task/b1-postbatch-defects` into
  `cloud-collections` (fast-forward; branch is strictly ahead) + push, (3) FULL E2E MATRIX
  (cd src/BloomTests/e2e && yarn test; ~30 min, desktop unlocked, stack up — remember the
  functions-serve zombie rule in server/dev/README.md), (4) rebase cloud-collections onto
  origin/master — now ~62 commits behind incl. the pnpm migration; only ~4 overlapping files
  expected (2 XLF, CollectionApi.cs, ExternalApi.cs); after rebasing, remember the front-end
  package manager may switch from yarn to pnpm on the rebased branch — re-read the rebased
  AGENTS.md before running front-end commands, (5) post-rebase full matrix, (6) John's
  [HUMAN] checks: item 3 centered-dialog visual, item 10 web upload/download
  (GOING-LIVE.md 4.3), John's dogfood-plan decision. Open cosmetic item: Administrators
  shows registration email (see "Also queued from dogfooding").
- 10 Jul 2026 (resumed again after VS Code restart) · Verified state: main tree clean on
  `task/b1-postbatch-defects` at b0941db62, no worktree WIP. Dev stack was BROKEN at resume:
  edge-runtime container missing entirely and no `supabase functions serve` process (several
  supabase containers had restarted ~45 min prior) — restarted functions serve with
  server/dev/functions.env, endpoint now answering (401 on bare probe = healthy), edge
  container up. Relaunched the three-defect diagnosis/fix agent (defects 1–3 from the 10 Jul
  AM entry) on the existing branch in the main tree · Next: review + merge that branch, then
  rerun e2e-3/4/5/8/10, then full matrix → rebase onto origin/master → post-rebase matrix →
  John's visual checks.
- 10 Jul 2026 (resumed after VS Code restart) · Verified resume state: working tree clean
  at ff6c5a6f8, no uncommitted worktree work, dev stack healthy (edge-runtime container
  restarted ~15 min prior but has its BLOOM_* env — NOT a functions-serve zombie).
  Relaunched the three-defect diagnosis/fix agent (defects 1–3 from the 10 Jul AM pause
  note) on branch `task/b1-postbatch-defects` in the main tree · Next: review + merge that
  branch, then rerun e2e-3/4/5/8/10, then full matrix → rebase onto origin/master →
  post-rebase matrix → John's visual checks.
- 10 Jul 2026 (PAUSED again for another restart, John's request) · The three-defect agent
  was stopped while still in its read-only diagnosis phase — NO code work or commits lost
  (branch `task/b1-postbatch-defects` contains only the two orchestrator log commits; main
  tree is checked out on it, clean). The three defect descriptions in the 10 Jul AM entry
  remain the full open work list; stack was verified healthy at resume time · Next action:
  relaunch the three-defect diagnosis/fix agent on the existing
  `task/b1-postbatch-defects` branch (defect descriptions above are self-sufficient), then
  the unchanged pipeline: review/merge → e2e-3/4/5/8/10 → full matrix → rebase onto
  origin/master → post-rebase matrix → John's visual checks.
- 10 Jul 2026 (AM, PAUSED for VS Code restart) · State: all batch items 1–10 + tier-timing
  fix MERGED and pushed (through commit 7029006d5). Post-batch E2E stabilization in
  progress — full matrix run 1 was 8/14. Fixed + pushed since: AWSSDK-v4 null S3Objects
  second site (DownloadCollectionFileGroup); item-9 sidecar idle-loop that starved the UI
  thread (checkin timeouts; watcher-file feedback loop — see commit f451aa865); harness
  waitForBookFile for progressive-join; e2e-10 refusal line to stdout
  (BLOOM_AUTOMATION_REFUSED_COLLECTION); window watcher NEVER RAN (node detached spawn
  kills powershell instantly — fixed non-detached + DPI-aware + spawn-time + placement
  logs, commits 80b333c4c/7029006d5). THREE OPEN DEFECTS, diagnosis agent was killed
  before starting (no work lost):
  (1) background book download silently dropped after join-relaunch — hypothesis:
  DownloadMissingBookInBackground's IsBookPresentInRepo pre-check on an unhydrated cache
  returns false → silent return → dedupe means never re-queued (e2e-3/e2e-4 failures; no
  RemoteBookAutoApplyQueue error lines in SIL logs = silent drop confirmed);
  (2) e2e-10 'bob-takeover' relaunch never reaches BLOOM_AUTOMATION_READY (empty stdout;
  check SIL Log-tmp*.txt ~10:0x AM Jul 10);
  (3) collection chooser triggers 'Cannot Find API Endpoint teamCollection/capabilities'
  toast (project-level endpoint called at app level; John saw it on screen; suspect a
  hook item 6 pulled into the chooser bundle).
  ALSO PENDING: full matrix re-run → rebase onto origin/master (47 commits incl. pnpm
  migration; only 4 overlapping files: 2 XLF, CollectionApi.cs, ExternalApi.cs) →
  post-rebase matrix → John's visual checks. e2e-5/e2e-8 retest failures were TRANSIENT
  infra (podman/db-reset under load; verified clean after). Stack is up; remember the
  functions-serve zombie rule (server/dev/README.md) after any supabase stop/start ·
  Next action: relaunch the three-defect diagnosis/fix agent (its full brief is in the
  orchestrator conversation; the three defect descriptions above are self-sufficient),
  then rerun e2e-3/4/5/8/10, then the full pipeline above.
- 9 Jul 2026 · Batch plan created; full-matrix baseline run in progress (validates
  checkin-comment fix + 5s poll live) · Next: item 1 ("Bloom is busy" l10n) code work
  while the matrix runs.
- 9 Jul 2026 (PM) · 4-spec verification queue GREEN 4/4 in 9.5 min (e2e-1, join-auto-open,
  e2e-2, e2e-8) on the merged state incl. items 1–6 and 8 — items 1/2 fully DONE, 4+5 E2E
  verified, 6 join-flow regression clear. John decisions recorded: safety window 7d;
  subscription tier same as folder TCs; AWSSDK bump on this branch (item 10) with [HUMAN]
  web up/download check; account-switch spec (item 9); recovery spec (item 8, implemented,
  382/382). Remaining: item 7 (agent next), items 9/10, John's dogfood-plan decision +
  visual dialog check · Next: launch item 7 implementation agent.
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
- 9 Jul 2026 (agent) · Item 7 (progressive join) CODE DONE on branch
  `task/b1-7-progressive-join` (created from origin/cloud-collections; not yet merged):
  CloudJoinFlow no longer blocks on CopyAllBooksFromRepoToLocalFolder -- every repo book is
  queued via the new TeamCollection.QueueBookForBackgroundDownload right after settings download,
  so the collection opens immediately. CollectionApi.HandleBooksRequest merges the cloud repo book
  list into the collections/books JSON (new BookListEntry DTO + pure
  ComputeNotYetDownloadedBookEntries, unit-tested) so repo-only books show `notYetDownloaded:
  true`; BookButton.tsx renders those as a simple placeholder (dashed border, cloud-download icon,
  title, no thumbnail request, no context menu -- SAFETY: no dangerous action reachable).
  RemoteBookAutoApplyQueue gained EnqueueFront (priority, never interrupts an in-flight download);
  selecting a placeholder (CollectionApi's selected-book handler) gracefully bumps its download to
  the queue front instead of crashing on the missing BookInfo. Each background download
  (TeamCollection.DownloadMissingBookInBackground, the queue worker's new no-local-folder branch)
  invalidates the cached book list and re-sends the existing editableCollectionList/reload
  websocket event so the placeholder swaps for the real button automatically. SyncAtStartup's
  "brand new book!" branch now reroutes to the same background queue for cloud
  (CanAutoApplyRemoteChanges) instead of fetching synchronously, so a half-joined collection's
  next open stays fast (folder TCs completely unaffected, pinned by a new regression test).
  DEVIATION flagged for John/orchestrator: the "downloading" status indicator is a persistent
  placeholder icon on the book button itself, not routed through the real BookSelection/preview
  pane and TeamCollectionBookStatusPanel.tsx's StatusPanelState union as the scout notes'
  exact seam suggested -- judged too risky (no real Book/local folder exists yet to fake a
  selection with) for the value added; the functionally important part (priority bump) IS
  implemented and tested. New XLF string CollectionTab.BookNotYetDownloaded added to Bloom.xlf,
  provisional/translate="no", flagged for John's priority-file confirmation. Tests: C# required
  filter 393/393 green (15 new across RemoteBookAutoApplyQueueTests, TeamCollectionAutoApplyTests,
  and new CollectionApiTests.cs); CloudSyncAtStartupTests.SyncAtStartup_NewBookOnlyInRepo_IsFetchedToLocal
  updated per this item's own instruction (queue now made synchronous for the test, assertion
  unchanged, reasoning documented inline); new BookButton.test.tsx 5/5 green. yarn typecheck/eslint
  show no NEW issues (verified via git-stash before/after diff against this codebase's large
  pre-existing unrelated typecheck-error baseline). E2E NOT run (this task's hard rules forbid
  launching Bloom/e2e) — `join-auto-open` + `e2e-9-new-book-lifecycle` queued for the orchestrator
  · Next: orchestrator review + merge of task/b1-7-progressive-join into cloud-collections, then
  the queued E2E pass, then items 9/10.
- 9 Jul 2026 (agent) · Item 10 (AWSSDK bump) CODE DONE + SUITES GREEN on branch
  `task/b1-10-awssdk-bump` (created from origin/cloud-collections; not yet merged): AWSSDK.Core
  3.5.1.32 -> 4.0.100.3 and AWSSDK.S3 3.5.3.10 -> 4.0.100.3 in BloomExe.csproj (major v4);
  parity-check tool floats 3.* -> 4.*; no other project pins the family, AWSSDK.SecurityToken is
  not referenced anywhere, no transitive SIL pin conflicts. v4 adjustments:
  RequestChecksumCalculation/ResponseChecksumValidation=WHEN_REQUIRED on the two MinIO-facing
  client builders (CloudBookTransfer, CloudTeamCollection) — v4's WHEN_SUPPORTED default sends
  CRC32/CRC64 trailing checksums S3-compatible endpoints may reject; BloomS3Client (real AWS)
  keeps v4 defaults. Null-collection/bool? fixes in S3Extensions.ListAllObjects +
  BloomS3ClientTests; removed 2 orphaned usings (LitJson embedded in v3 Core, gone in v4).
  Baseline full BloomTests on UNMODIFIED cloud-collections FIRST: 3036/0/13 (3049 total);
  post-bump: cloud filter 387/387, full suite 3036/0/13 — identical, zero regressions;
  S3-specific fixtures 44/44 incl. the LIVE DownloadBook_DoesNotExist_Throws against real AWS.
  E2E NOT run (orchestrator's job): e2e-1 + e2e-2 through MinIO queued — watch for checksum
  (should be silent now), path-style, and AuthenticationRegion behavior; then John's [HUMAN]
  web up/download check (GOING-LIVE.md 4.3) · Next: orchestrator review + merge of
  task/b1-10-awssdk-bump, then the queued E2E pass.
- 9 Jul 2026 (agent) · Item 9 (account-switch behavior) CODE DONE on branch
  `task/b1-9-account-switch` (created from origin/cloud-collections; not yet merged): refusal
  path — TeamCollectionManager.CheckConnection(allowHardRefusal) (default false, only the
  constructor's initial open-time call passes true) throws the new
  TeamCollectionAccessRefusedException when CloudTeamCollection.CheckConnection's non-member
  branch sets the new TeamCollectionMessage.IsAccessRefusal flag; Program.HandleErrorOpeningProjectWindow
  special-cases that exception (plain message box, no crash-report flow) before falling through
  to the existing chooser-reopen path. The refusal message composes admin email(s) (read from
  the local .bloomCollection's Administrators field — flagged risk: this inherits the
  already-tracked "Administrators shows registration email not signed-in email" bug from the
  "Also queued from dogfooding" list, since that fix was out of this item's scope) and "last
  known team member on this machine" from a NEW durable local record,
  TeamCollectionLastKnownUser.txt (sidecar file next to TeamCollectionLink.txt; chosen over
  extending TeamCollectionLink.txt's tightly-scoped tested format; written at join time
  (CloudJoinFlow) and refreshed on every successful membership confirmation
  (CloudTeamCollection.CheckConnection), so it doubles as "who joined" and "last confirmed
  local user" — documented as an approximation, not literally "last edited"). Takeover path —
  new virtual seams on TeamCollection (IsEditableHere/CanTakeOverLockOnThisMachine/
  TryTakeOverLock, all no-op/strict by default so folder TCs are unaffected) let
  CloudTeamCollection treat a book locked to a DIFFERENT account on THIS machine as editable
  and checkin-able; new additive RPC tc.checkout_book_takeover (migration
  20260709000007_tc_checkout_takeover.sql) atomically reassigns the lock, called from
  PutBookInRepo just before check-in (no per-keystroke "edit happened" hook exists anywhere in
  this codebase, confirmed by research, so "on first edit" == "on first check-in of that edit")
  and from AttemptLock (explicit checkout click, likely unreachable in the UI here but kept for
  symmetry). checkin_start_tx/checkin_finish_tx are UNTOUCHED — purely additive, so no existing
  RPC's contract changed. CONTRACTS.md addition flagged, NOT applied (orchestrator decision per
  this task's rules): a `checkout_book_takeover(book_id, machine) -> {success, locked_by,
  locked_by_machine, locked_at}` row alongside checkout_book/unlock_book/force_unlock. Tests: 55
  pgTAP (42 existing + 13 new in 02_tc_checkout_takeover_test.sql, actually run against the
  local dev stack — same-machine takeover, cross-machine rejection, no-op re-takeover,
  non-member rejection all green); C# required filter (Cloud|TeamCollection|SharingApi) 406/406
  green (17 new: 5 ComposeNotAMemberRefusalDetail + 2 CheckConnection refusal/last-known-user in
  CloudTeamCollectionMemberTests.cs, 9 in new CloudAccountSwitchTakeoverTests.cs, 3 in new
  TeamCollectionAccountSwitchRefusalTests.cs). One new XLF string,
  TeamCollection.Cloud.NotAMemberRefusal, added to Bloom.xlf (translate="no"), FLAGGED
  PROVISIONAL for John's priority-file confirmation — it's shown in a plain MessageBox, arguably
  more user-facing than most existing unlocalized TC internal strings, so may deserve a
  different priority file or eventual real translation. New (non-run) E2E spec
  `e2e-10-account-switch.spec.ts` written for the orchestrator's next pass, replacing the old
  blocked task-09 scenario of the same number (different shape now — open-time refuse/takeover,
  not in-session block-with-choices); flags that the refusal MessageBox is a native Win32 dialog
  invisible to CDP entirely, so the spec verifies it via the instance's own log file instead.
  Known omissions/risks for the orchestrator: (1) the Administrators-email identity bug noted
  above; (2) no automated test exercises PutBookInRepo's pre-checkin takeover call end-to-end
  (would need a full book-folder + checkin-start/finish edge-function mock harness) — covered
  indirectly by direct unit tests of the virtual seams plus the new E2E spec; (3) TestFolderTeamCollection's
  own takeover behavior was not separately tested since CanTakeOverLockOnThisMachine's folder
  default is `false` (unchanged behavior, no new folder-TC surface to test) · Next: orchestrator
  review + merge of task/b1-9-account-switch, then the queued E2E pass including e2e-10.
- 10 Jul 2026 (agent) · Tier-timing fix ("Also queued from dogfooding") CODE + TESTS DONE on
  branch `task/b1-tier-timing` (created from origin/cloud-collections; not yet merged): diagnosis
  — `TeamCollectionManager.CheckDisablingTeamCollections` (TeamCollectionManager.cs ~782) gates
  solely on `CurrentCollection == null`; for a cloud TC that's set (TeamCollectionManager.cs ~364)
  BEFORE the connect-and-sync sequence (~374-391) that is the only thing able to deliver a fresh,
  repo-authoritative SubscriptionCode into `Settings.Subscription` — an in-memory CollectionSettings
  snapshot captured once at ProjectContext startup and never reloaded mid-session. That sequence's
  success depends on cloud sign-in readiness (`CloudTeamCollection.CheckConnection` short-circuits
  on `!_auth.IsSignedIn`) and an S3 download that silently swallows exceptions
  (`CloudTeamCollection.DownloadCollectionFileGroup`'s catch-and-report-only handler) rather than
  propagating failure — so a cloud TC's subscription snapshot can still be stale/blank when the
  check runs, permanently disconnecting a healthy collection for the session (matches the E2E-9
  harness's observed ~1-in-40 misfire, tasks/09-e2e.md). Fix: `WorkspaceModel.HandleTeamStuffBeforeGetBookCollections`
  now defers the check for cloud TCs to run inside `SynchronizeRepoAndLocal`'s `whenDone` callback
  (after sync), and `TeamCollectionManager.GetSubscriptionForDisablingCheck` (new) re-reads the
  SubscriptionCode fresh from the on-disk `.bloomCollection` file for a cloud TC instead of
  trusting the in-memory snapshot; folder TCs (and the no-TC case) keep the original immediate
  check, byte-identical. `TeamCollectionManager.CheckDisablingTeamCollections` and
  `TeamCollection.SynchronizeRepoAndLocal` marked `virtual` (previously plain `public void`) purely
  so test subclasses can observe call order/behavior without invoking a real progress dialog. New
  tests: `TeamCollectionTierTimingTests` (misfire no longer disables; genuinely insufficient tier
  still disables for cloud via fresh disk read; non-cloud path unaffected, in both directions) and
  `WorkspaceModelTierTimingOrderingTests` (folder TC still checks-then-syncs; cloud TC now
  syncs-then-checks) — 7 new tests, all green. Full required filter
  `(~Cloud|~TeamCollection|~SharingApi)&!~LiveTests`: 413/413 (406 baseline + 7 new), zero
  regressions. Risk for the orchestrator's E2E pass: the harness's `createScratchCollection`
  (collectionFixture.ts) stamps a fake valid subscription code onto every scratch collection as a
  workaround for this exact bug — with the fix merged, that workaround is likely safe to REMOVE
  (or at least no longer load-bearing), but flagged for the orchestrator to verify live before
  touching the harness, since removing it now means every E2E cloud-TC scenario exercises the real
  timing path for the first time · Next: orchestrator review + merge of task/b1-tier-timing.
