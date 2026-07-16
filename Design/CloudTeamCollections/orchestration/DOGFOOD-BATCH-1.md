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
Status: **COMPLETE — John's visual check PASSED 13 Jul 2026** ("Confirmed checking progress
is in the right place"). Same session also hand-confirmed the batch's very first fix:
check-in MESSAGES appear in history and are shared to teammates.
- [x] It's the React BloomDialog in TeamCollectionBookStatusPanel (not BrowserProgressDialog):
      now positioned via PaperProps over the #teamCollection div's center, vertically
      clamped so the paper stays on-screen (the panel hugs the window bottom). Falls back
      to default whole-window centering when #teamCollection is absent (unit tests).
      Panel vitest suite 11/11.
- [x] `e2e-2-collaboration-loop` GREEN (9 Jul PM queue).
- [x] [HUMAN, John] Visual check that the checkin-progress dialog appears centered over the
      status panel during a manual checkin — PASSED 13 Jul 2026.

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
0. **RESOLVED 11 Jul 2026 — implemented as option (a), per John's ruling (recorded verbatim in
   the 11 Jul progress entry): editing/takeover of a checkout is only legitimate in the local
   copy of the collection where the book is checked out.** Implementation: migration
   20260711000003 adds `tc.books.locked_seat` (client-computed hash of the local collection
   folder path — the "seat"), recorded by checkout_book/checkout_book_takeover; takeover
   requires machine AND seat match, and a NULL stored seat never matches (fail-safe); a
   trigger clears the seat with every unlock path. Client: CloudTeamCollection.SeatId;
   IsEditableHere/CanTakeOverLockOnThisMachine seat-gated (own pre-seat locks grandfathered;
   other accounts strict). CONTRACTS.md bumped to v1.5. pgTAP 65/65, C# filter 428/428,
   e2e-4 PASSES. FOLLOW-UP flagged for John (not blocking): checkin_start_tx still accepts a
   same-user check-in regardless of seat/machine (pre-existing behavior; the client-side
   editable gate is the enforcement point today) — decide whether the server should also
   refuse cross-seat check-ins by the SAME user.
   **What that question means (plain English, clarified 14 Jul 2026):** A lock is recorded on
   the server as (user, machine, SEAT), where the seat identifies WHICH local copy of the
   collection took the checkout (hash of that copy's folder path). The bug #0 fix makes
   *takeover* of a lock require a matching machine+seat, and enforces it in BOTH places — the
   client (IsEditableHere/CanTakeOverLockOnThisMachine) AND the server (checkout_book_takeover).
   But an ordinary *check-in* (checkin_start_tx) only verifies the lock is held by the same
   USER; it does not check that the check-in comes from the seat that holds the lock. Today the
   only thing stopping a same-user cross-seat check-in is the client: from a second copy the
   book shows as not-editable-here, so the UI won't let you edit/check it in. Concretely: John
   has two copies of the Tetun collection (C:\temp\Tetun Books and the OneDrive copy) under one
   account. If he checks a book out in copy A and then, from copy B, something got a check-in
   through (a client bug, a bypass, or a future/alternate client), the server would accept it
   and could clobber the version copy A is working on. The question is whether to close that gap
   server-side — i.e. also make checkin_start_tx require the caller's machine+seat to match the
   lock's — as defense-in-depth so the seat rule doesn't rely solely on client behavior.
   Trade-off: it needs the client to send its seat/machine on check-in and a server migration +
   pgTAP; risk is low and it mirrors the takeover gate we already trust. (Not urgent — no live
   data-loss seen; the client gate holds in normal use.)
   **DECISION (14 Jul 2026, John): WON'T DO — do not enforce seat on check-in server-side.**
   Two reasons: (1) a client buggy or hacked enough to check in from the wrong source folder
   could just as easily send the wrong seat checksum, so the server gate wouldn't actually
   protect against that threat; and (2) a server-side seat requirement could get in the way of
   recovering a collection whose local folder has legitimately been moved or renamed (which
   changes the seat hash). The client-side editable gate remains the enforcement point. **This
   was the last open piece of bug #0; bug #0 is now fully closed.**
   Original problem statement follows for the record.
   **[Original — NEEDS JOHN] Item 9's same-machine takeover can steal ANY same-machine
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
   before the fixes; 10/14 under heavy load 10 Jul PM). Run it after John decides bug #0 (or
   accept one known e2e-4 failure). Standalone scoreboard as of 10 Jul late evening (after
   the queue-arrival spec fixes): e2e-3 ✅, e2e-5 ✅, e2e-6 ✅, e2e-8 ✅, e2e-9 ✅ (3/3),
   e2e-10 ✅; e2e-4 ❌ blocked solely on bug #0.
4. **UPGRADED from cosmetic (13 Jul human test): cloud TC identity must be the SIGNED-IN
   account, not the registration email.** TeamCollectionManager.CurrentUser (=
   Registration.Default.Email) drives Administrators, checkout attribution, and every
   CurrentUserIdentity comparison — so John's Alice-signed-in instance displayed his books
   as "checked out to john_thomson@sil.org" and every lockedBy comparison crosses
   identities (it limps through only because the same-seat logic tolerates the mismatch).
   For a cloud TC, CurrentUserIdentity should resolve to the signed-in cloud email.
   Dogfood workaround (= what the e2e harness does): impersonate.txt line 1 in the
   collection folder overrides the TC user (alice's copy got one 13 Jul).
5. **Preflight (10 Jul PM, John's request):** light-review pass over the day's diff found 2
   valid adjacent holes, BOTH FIXED + tested (72246c2975): per-account (not per-instance)
   claim_memberships guard; machine-aware lock skip in the requeue pass. The GitHub half of
   preflight (draft PR, Devin, Greptile/CodeRabbit, CI gauntlet) is BLOCKED: `gh` is not
   authenticated in the agent session — John must run `gh auth login`, then re-run
   `/preflight` to create the draft PR and run the bot gauntlet.
6. **Full C# suite: RESOLVED AS FLAKE** — the first run's single failure (1/3131) did not
   reproduce on the identification rerun (3120 passed / 0 failed / 3133 total, merged tree).
7. **[NEW, 13 Jul human test] Create-over-stale-cloud-link reports FAKE SUCCESS.** John
   shared Tetun Books, whose folder still carried Thursday's cloud link (server rows wiped
   by e2e resets): ConnectToCloudCollection correctly threw
   TeamCollectionLinkConflictException, but HandleCreateCloudTeamCollection's catch calls
   request.PostSucceeded() (comment: avoid a double toast) — so CreateTeamCollection.tsx
   advanced to "Your Team Collection is ready. Invite your team from the Sharing panel" with
   NO server row, NO link rewrite, and no Sharing panel (nothing was created). The
   ErrorReport.NotifyUserOfProblem dialog was not seen (may not display in this context).
   FIX NEEDED: the reply must let the dialog distinguish failure (show the conflict message
   + guidance) — and decide the recovery UX for "cloud link points at a collection that no
   longer exists server-side" (same family as the deferred recovery-preconditions decision;
   the create flow could offer re-create when the dead link's id == this collection's id).
   Workaround applied for the human test: moved TeamCollectionLink.txt +
   .bloom-cloud-repo-cache.json + lastCollectionFileSyncData.txt out of Tetun Books (backup:
   Documents/Bloom/Tetun-Books-stale-tc-backup-2026-07-13).
8. **[NEW, 13 Jul — UPSTREAM, not this branch] Debug builds die silently at the collection
   chooser**: UrlLookup.LookupFullUrl's Debug.Assert ("provide an appropriate acceptFinalUrl
   param when looking up a url during startup") fires via CollectionChooserApi.
   HandleGetUnpublishedCount → GetLibraryStatusForBooks → BloomLibraryDetailPageUrlFromBookId
   (all master code; timing/network dependent) and a failed assert TERMINATES the process
   ("Process terminated. Assertion Failed", captured stdout 13 Jul). Release unaffected.
   Candidate for a YouTrack issue against master, not a Cloud TC fix.
9. **[NEW, 13 Jul human test] Client cache survives server-side collection recreation and
   poisons everything after.** C:\temp\Tetun Books kept Thursday's
   .bloom-cloud-repo-cache.json (lastSeenEventId 13, phantom books at seq 4) across the
   server wipe + 13 Jul re-create of the SAME collection id. Consequences observed live:
   the initial share push uploaded NOTHING (cache said the server already had every book at
   the current version — zero checkin transactions ever), and Alice's checkout sent a
   phantom Thursday book id → checkout_book raised book_not_found → "Bloom was not able to
   check out". FIX NEEDED: a FULL get_collection_state snapshot must REPLACE the cache
   (prune cached books absent from the snapshot), and lastSeenEventId > server max_event_id
   must be detected as server regression → drop cache, full resync. Workaround: delete the
   stale cache files before reopening (pending — an instance is still holding them).
10. **[NEW, 13 Jul] `pnpm go` (watchBloomExe.mjs) always passes `--automation`**, so HUMAN
   dogfooding runs under automation semantics: progress-dialog problems auto-close
   (BrowserProgressDialog, 11:19:07 log — this hid the failed initial push), and
   NonFatalProblem reports go to stdout only. The dev launcher needs a way to run WITHOUT
   automation mode (flag through go.mjs → watchBloomExe), or humans keep not-seeing errors.
12. **[NEW, 13 Jul] Cloud identity silently leaks between instances on one Windows account.**
   John signed in "as fred" in a second instance, yet that instance's server calls ran as
   ALICE (proof: collection-file + book downloads succeeded — fred isn't a member and would
   get 403; tc.members/events show zero fred activity). Cause: the DPAPI CloudTokenStore is
   per-Windows-user (shared by every instance), and the shared MRU auto-opened Alice's
   C:\temp copy (which also collided with her running instance — IOException on the
   .bloomCollection). Access control HELD server-side; the failure is client identity UX.
   Design question queued: what is a UI sign-in's scope (instance? machine?), and should
   BLOOM_CLOUDTC_USER-style per-instance pinning be a first-class dev affordance?
   Dogfood rule until then: one PowerShell (env-pinned user) + one collection folder per
   identity.
13. **[NEW, 13 Jul, UX] "Send All" is hard to find**: it lives at the bottom of the TC
   dialog's STATUS tab, but the dialog deliberately opens on the History tab when there are
   no important status messages — John saw only "Sync + Close". The create-success message
   also points at the Sharing panel, not here. Consider surfacing Send All on the status
   panel or making the Status tab default when there are uncommitted local books.
   **DECISION (14 Jul 2026, John): leave "Send All" where it is — no location/UX change
   needed. The Send-All-discoverability half of this item is CLOSED; the admin-role half
   below is still open.** ALSO
   13 Jul: create-time `Settings.Administrators = CurrentUser` stamps the REGISTRATION
   email (bug #4 family) — locked Alice out of her own collection's settings until the
   .bloomCollection was hand-patched; and TeamCollectionApi.isUserAdmin
   (OkToEditCollectionSettings) reports false for the server-side admin — the admin flag
   for cloud should come from the server membership role.
11. **FIXED 13 Jul — join-card dedup is now identity-aware (John's ruling).** Bob (invited,
   claimed member) got NO join card for Tetun because the dedup suppressed by cloud id
   against EVERY local copy in the machine-wide chooser list — including ALICE's C:\temp
   copy. Fix: ComputeJoinCards now takes (id, lastKnownUser) pairs + the signed-in email
   and suppresses only when the copy's TeamCollectionLastKnownUser.txt matches the
   signed-in account (case-insensitive); unknown/missing marker or another account's copy
   → the card SHOWS (CloudJoinFlow's scenario matching handles merge/conflict at join
   time, unchanged). New SharingApi.SignedInEmailForJoinCards;
   GetLocalCloudCollectionIds → GetLocalCloudCopies. 8 chooser tests incl. the exact live
   scenario (other account's copy → card shows); filter 435/435. Also cleaned this
   morning's stale-state workarounds: poisoned Thursday caches purged from C:\temp\Tetun
   Books (bug #9's trigger), both Chodri copies un-teamed (dead collection); backups in
   C:\temp\stale-cloud-tc-backup-2026-07-13.
14. **FIXED 13 Jul — only ONE pending invitation per collection was possible.** Found by
   the display-name test fixture, present since the original schema:
   `members_claimed_user_uq` was `UNIQUE NULLS NOT DISTINCT (collection_id, user_id)`, so
   two unclaimed rows (user_id NULL) collided — inviting a second person before the first
   claimed made members_add die with 23505 (its ON CONFLICT clause targets the email
   constraint, not this one). Contradicted the schema's own documented intent ("claimed
   user unique"); dogfooding never tripped it because invites happened to alternate with
   claims. Fix: 20260713000002 recreates the constraint with default NULLS DISTINCT.
18. **FIXED 13 Jul PM — a teammate's rename arrived as a DUPLICATE book (two local folders,
   one instance id) on the receiving side.** John's live report: after Bob's retitle
   check-in (server row renamed to "Tetun moon and cap", seq 3, unlocked — verified),
   Alice ended up with old-name AND new-name folders, same id, same content, phantom
   checkout displays, "both selected at once". Root cause: an identity-first REGRESSION —
   base `NewBookRenamedFrom`'s heuristic ("a local folder with repo status can't be the
   rename source") assumes name-keyed status; under identity-first the old-name folder
   resolving (by id) to the renamed row is precisely what MARKS it as the rename source,
   so rename detection always failed, the renamed book was treated as NEW and
   auto-downloaded beside the old folder, and each later sync happily refreshed the
   old-name folder's content via its identity binding (explaining "same content incl. the
   new title in both"). FIX: `NewBookRenamedFrom` is now `protected internal virtual`
   (base body byte-identical — folder TCs keep their heuristic);
   CloudTeamCollection overrides it with an exact instance-id comparison (repo name → row
   instance id → local folder carrying that id under a different name). Guards added in
   the two auto-download sweeps (`QueueMissingRepoBooksForBackgroundDownload` + a
   re-check in `DownloadMissingBookInBackground`, both inside CanAutoApplyRemoteChanges-
   gated paths, i.e. cloud-only at runtime): a repo book that is a rename of an existing
   local book is NOT "missing" — the rename is applied by the next sync's
   rename-from-remote pass, which is already identity-driven (GetRepoBooksByIdMap) and
   works for cloud unchanged. CLEANUP: Alice's old-name duplicate backed up to
   C:\temp\stale-cloud-tc-backup-2026-07-13\ and removed; her copy now has one folder per
   id. 3 new CloudIdentityFirstLookupTests.
17. **FIXED 13 Jul PM — new/local-only books in a cloud TC showed "checked out to John1"
   (the REGISTRATION identity).** John's live report from Bob's copy; Alice's copy only
   looked right because C:\temp\Tetun Books\impersonate.txt makes her registration EQUAL
   the account. The base status JSON stamps `who` (plus whoFirstName/whoSurname and
   currentUserName) from TeamCollectionManager.CurrentUser for books whose checkout is
   purely local (new local book / not in repo). FIX in
   TeamCollectionApi.AddCloudBookStatusFields (same method as the earlier currentUser
   override, bug #4 family): when `who` equals the base registration currentUser, rewrite
   it to the signed-in account email and clear the registration first/surname;
   currentUserName now also carries the account identity (avatar dialog). A real repo
   lock's `who` (a member email) is untouched. 2 new TeamCollectionApiCloudTests.
   SECOND ROUND (John: "still showing John1"): the who-rewrite was aimed at the wrong
   field — `who` was ALREADY the account (CurrentUserForStatus resolves to
   CloudTeamCollection.CurrentUserIdentity); the REAL leak is whoFirstName/whoSurname,
   which base WhoHasBookLockedFirstName stamps from the REGISTRATION name for
   new/local-only books, and the UI prefers the name fields over `who`. Fix:
   AddCloudBookStatusFields clears whoFirstName/whoSurname whenever isNewLocalBook — the
   display falls back to `who` (account email). The earlier who-rewrite kept (covers a
   signed-out registration leak). +1 test — NOT yet run (John's two instances hold
   output\Debug); run the filter when they close.
   RESIDUAL of the same family (already in bug #13's tail): create-time
   Settings.Administrators and isUserAdmin still use registration identity.
16. **FIXED 13 Jul PM — `pnpm go` + `--automation` silently killed Bob's startup on any
   sync warning (bug #10's sharp edge, now with a live victim).** John's report: Bob
   exited without any message when switching to the OneDrive Tetun copy, twice. SIL log
   (Log-tmpcgri51/tmp2dcrx1.txt): the startup TC sync reported problems (run 1: the
   perfectly LEGITIMATE "Renaming the local book 'The Moon and the Cap' because there is
   a new one with the same name" — the conflict machinery working as designed on a
   folder whose meta.json id (db07d1f3…, some earlier derivative) differed from the repo
   book's (ca252af0…)) → BrowserProgressDialog's automation gate auto-closed the dialog
   ("problems were reported … auto-closing because Bloom is in automation mode") →
   Application.Run() returned → silent exit. Every relaunch re-tripped it. FIX: new
   `--attended` flag (Program.StartupAttended / UnattendedAutomation = automation &&
   !attended): --automation keeps the ready-handshake, port summary, and single-instance
   bypass, while the four no-human UI policies (BrowserProgressDialog auto-close ×2,
   HtmlErrorReporter notify suppression, NonFatalProblem stdout redirect,
   ProblemReportApi report suppression) now gate on UnattendedAutomation.
   watchBloomExe.mjs (pnpm go) passes --attended by default (opt out with
   BLOOM_GO_UNATTENDED=1 for agent-driven CDP runs that must never block on a dialog);
   the E2E harness's own launch.ts passes plain --automation and keeps full unattended
   behavior. +4 ProgramTests; filter 454/454. RESIDUAL for next E2E pass: agent `pnpm go`
   sessions now show real dialogs unless BLOOM_GO_UNATTENDED=1 is set — update agent
   runbooks. CLEANUP done with it: Bob's OneDrive copy had TWO folders with instance id
   ca252af0 ("The Moon and the Cap" re-downloaded by the sync + "Tetun moon and cap",
   Bob's uncommitted retitle from the shared-folder chaos) — the retitle folder is backed
   up at C:\temp\stale-cloud-tc-backup-2026-07-13\ and removed; "The Moon and the Cap1"
   (db07d1f3, the sync's own conflict rename) left in place as a plain local book. Run
   2's problem message was never logged (blank) — with dialogs visible again it will
   identify itself on screen if it recurs.
15. **FIXED 13 Jul PM — identity-first book resolution (John's ruling: "the status of a
   particular record by instanceID in the database is the source of truth for that book's
   state" — ALWAYS by instance id for a local folder, not merely as a name-miss fallback,
   so an offline-created local book X never wears the status of a teammate's checked-in
   book that happens to share the name X).** CloudTeamCollection.ResolveBookId: a local
   folder resolves ONLY by its meta.json bookInstanceId → _bookIdByInstanceId (unreadable
   id ⇒ null, fail-safe "local-only", never a name guess); the name index applies only
   when no local folder exists (repo-name queries, e.g. GetBookList names). Applied to
   status/lock/seat/version/delete/fetch/casing/history-filter lookups; PutBookInRepo now
   resolves by instance id ONLY (a name match could have checked in over a different
   same-named book — the worst form of this bug); RenameBookInRepo is now a documented
   no-op (_pendingRenameBookId deleted — identity resolution makes the bridge redundant);
   GetRepoBookFile deliberately stays name-based (base contract: repo names, per
   NewBookRenamedFrom/GetRepoBooksByIdMap). Placeholder merge
   (CollectionApi.ComputeNotYetDownloadedBookEntries) also suppresses by local instance
   id, killing the phantom "shadow" card. Per John: FolderTeamCollection untouched — all
   changes are CloudTeamCollection overrides + the cloud-only placeholder path;
   TeamCollection.cs/FolderTeamCollection.cs byte-identical. Tests: new
   CloudIdentityFirstLookupTests (renamed-checked-out book keeps its row+avatar; John's
   offline same-name conflict scenario; repo-name lookup unaffected; no-meta folder
   fail-safe) + placeholder-suppression test; two OkToCheckIn fixtures updated to give
   their local folder its real meta.json identity; PolledChanges fake server made
   tolerant of the auto-apply background download (pre-existing cross-thread flake its
   strict assert caused). Filter (Cloud|TeamCollection|SharingApi|CollectionApi) 442/442.
   ORIGINAL DIAGNOSIS (13 Jul PM, John's live report; server verified CONSISTENT — one row,
   "The Moon and the Cap", still locked by Bob, seq 1, no rename/check-in events).
   Cloud renames are carried only at check-in (RenameBookInRepo + checkin-start
   proposedName, by design), but every client-side repo lookup is by FOLDER NAME:
   `TryGetBookStatusJsonFromRepo` → `TryGetCachedBook(bookFolderName)` → `_bookIdByName`.
   After the local folder rename, the new name misses the index (only the in-memory
   `_pendingRenameBookId` bridges it, and only during PutBook), so the checked-out book
   reads as "not in repo" → treated as a NEW LOCAL BOOK: editable, no checkout avatar
   (Bob's symptom). Meanwhile `GetBookList()` still returns the server (old) name with no
   matching local folder → the progressive-join merge shows a phantom cloud-download
   placeholder of the same book (the "shadow" card). FIX SHAPE (queued, not yet applied):
   resolve by IDENTITY when the name lookup misses — the local folder's meta.json
   bookInstanceId → `_bookIdByInstanceId` (and/or the local status file's `oldName`, which
   HandleBookRename already records) — in TryGetBookStatusJsonFromRepo /
   IsBookPresentInRepo / TryLockInRepo-family lookups; and suppress a placeholder when a
   LOCAL book with the same instance id exists regardless of name. SESSION CONFOUND,
   important: BOTH of John's instances were running against ONE local copy —
   `C:\Users\JohnThomson\OneDrive\Documents\Bloom\Tetun Books` (Bob's pull-down; NOTE
   Documents is OneDrive-redirected). Seat proof: both live locks carry seat
   6d5c0907085da326 = that folder; Alice's C:\temp\Tetun Books is bed69996f9b9d0e1 and
   holds NO current lock. So "Alice got the rename instantly" via the SHARED FILESYSTEM,
   not the server, and her instance likely auto-opened Bob's copy from the MRU after a
   restart (the MRU trap again — and my C# push at ~14:5x likely recycled the dotnet
   watch, killing both test instances). Two Blooms sharing one collection folder is
   unsupported; her doubled cards are partly that artifact, but Bob's symptoms reproduce
   single-instance and are the real bug.

## Progress log
(orchestrator appends: date · what was just completed · EXACT next action)
- 16 Jul 2026 (RESUME from PAUSE #2 — verification pass; WIP is HEALTHIER than the pause note
  feared) · Resumed the /simplify application. Working tree matched the PAUSE #2 doc exactly
  (67 modified + 5 new source files + untracked .claude/settings.json — nothing lost; top
  stash is unrelated Version6.4). **Findings, all now resolved or characterized:**
  - **C# COMPILES** (the pause note's "tree may not compile" worry was unfounded — R1/R2/
    R11-use are unstarted *additional* cleanups, not half-applied breakage). BloomExe builds
    clean. The ONLY real breakage: 4 call sites in CloudBookTransferTests.cs still passed the
    pre-E4 arg list to `UploadChangedFiles` (missing the new `localManifest` param) →
    CS7036. FIXED: inserted `null` for `localManifest` at all 4 sites (behavior-preserving —
    doc says null ⇒ hash as before). BloomTests now compiles.
  - **C# required filter GREEN: 441/441** (non-live: `(~Cloud|~TeamCollection|~SharingApi)
    &!~LiveTests`), 0 failures. (Doc's 460 pre-batch-2 count included ~19 LiveTests, excluded
    here since the stack was not confirmed up; zero failures either way.) CloudBookTransfer
    fixture 10/10.
  - **vitest full suite TRIAGED: 593 passed, 5 failed, 5 skipped.** All 5 failures are in
    `bookEdit/toolbox/talkingBook/talkingBookSpec.ts` ("sentence splitting" audio-checksum
    tests) — a directory this branch NEVER touched (git diff vs origin/master is empty; last
    commit Mar 2026; clean in working tree; none of its deps are in the changed set; global
    test infra untouched). **Conclusion: pre-existing / environmental, NOT caused by the
    refactor.** Every refactored teamCollection/collection test passes.
  - **eslint (changed front-end files): 0 errors.** Fixed 3 warnings in CreateTeamCollection.tsx
    (dropped unused `showDialog`/`closeDialog` from the useSetupBloomDialog destructure — a
    leftover of the CreateCloudTeamCollection extraction; `==`→`===` on the boxesChecked
    gate). CreateTeamCollection + CreateCloudTeamCollection vitest 14/14.
  - e2e: prior agent reported tsc/eslint-level clean; live run still queued (desktop/stack gated).
  **State of REMAINING documented work (unchanged, still open):** batch-2 R1 (DownloadCollection
  FileGroup→S3Extensions.ListAllObjects), R2 (delete CloudTeamCollection.BuildS3Client, reuse
  CloudBookTransfer.BuildDefaultClient), R11-use (reuse hoisted AvailablePath) — all UNSTARTED,
  and R1/R2 touch AWSSDK-v4 S3 client construction (want live MinIO verify); batch-3 file org
  (partials + provider split, large mechanical reorg); full pass + live run.sh/go.sh smoke;
  commit in logical chunks; regen #8052 + re-trigger Greptile (needs `gh auth`, was broken at
  pause). **My 7 line-fixes above are LEFT UNCOMMITTED with the rest of the WIP** (matching this
  batch's established pattern — code WIP uncommitted, state in this doc), fully reproducible from
  this entry. **EXACT next action:** paused to confirm scope with John — how far to push the
  remaining OPTIONAL/quality work (R1/R2/R11-use + batch-3 file org) in this session given the
  token concern that triggered the pause, and whether `gh` is now authenticated for the PR regen.
- 15 Jul 2026 (PAUSED #2 — John: "put this task on hold until the next session; too many
  tokens"). Continuation of the entry below; tree has UNCOMMITTED WIP (~67 modified + 5 new
  files). **What changed since the previous PAUSE entry:**
  - C# batch-1 is COMPLETE and GREEN: fixed the CloudEnvironmentTests compile break, finished
    the whole batch-1 tail (CloudBookTransfer dead `alreadyUploadedThisTransaction` param
    dropped from UploadChangedFiles + both CloudTeamCollection call sites + tests;
    CloudCollectionMonitor dead Stop() deleted + Dispose doc'd terminal; SharingApi
    serializes `emailVerified`, SignedInEmailForJoinCards→CurrentAuth().CurrentEmail,
    RegisterWithApiHandler doc'd; CloudTeamCollection CollectionIdForCloud→CloudCollectionId
    and TryGetBookIdForTests→GetBookIdByNameIndexForTests with callers; TryTakeOverLock doc
    reworded). Required filter ran **460/460 PASSED** (down from 465 because dead-API tests
    were deleted with their APIs).
  - MID-PAUSE INCIDENT (resolved): an old stash mis-applied during the previous pause left
    conflict markers in 9 files foreign to this branch's work + a spurious untracked
    CanvasElementManager.ts; with John's approval all 9 were restored from HEAD and the stray
    file deleted. No stash was dropped; nothing of ours was lost.
  - Three fix agents were then relaunched and STOPPED for THIS pause, each mid-flight:
    (a) React/TS agent — finished auditing/completing the test-render-helper refactor and was
    running the FULL vitest suite, which **exited code 1**; it was stopped while reading the
    failure summary. UNKNOWN whether the failure is refactor-caused or pre-existing — first
    resume job: run `pnpm exec vitest run` in src/BloomBrowserUI and triage.
    (b) e2e agent — verification came back CLEAN (stragglers converted, tsc/eslint-level check
    passed); it was stopped during scratchpad cleanup (it made a HEAD-baseline checkout in the
    session scratchpad with a node_modules junction — harmless, dies with the session).
    Treat e2e work as DONE pending one final look.
    (c) C# batch-2 agent — had applied E1 (GetRepoBooksByIdMap protected virtual + cloud
    override from _cache.GetAllBooks()), E3 (poll early-return), E4 (manifest hashes into
    UploadChangedFiles), E6 (display-name failure TTL), R12 (CloudAuth.CreateInitialized),
    and the R11 hoist (AvailablePath→base); its last words were "Now R11 (use hoisted
    AvailablePath), R1 (ListAllObjects), R2 (delete BuildS3Client)" — so R11's USE side, R1,
    R2 are NOT done, and NOTHING of batch 2 has been compiled or tested. The C# tree may not
    compile.
  **EXACT next action on resume:** (1) `git diff` the C# files to see batch-2 agent's actual
  edits; finish R11-use/R1/R2 (specs in the entry below); run the required filter (baseline
  was 460/460 pre-batch-2). (2) Triage the vitest full-suite failure; finish React/TS
  verification (vitest/eslint/typecheck). (3) C# batch 3 (file org — specs below). (4) Full
  test pass + live run.sh smoke (launchers still unverified live). (5) Commit in logical
  chunks (NOTE: `.claude/settings.json` is untracked session-local config — do NOT commit it;
  add files explicitly, no `git add -A`). (6) Regenerate #8052 via orchestration/regen-*.sh,
  re-trigger Greptile. (7) Final report to John: fixed/skipped summary + REPORT-ONLY decision
  list (below, incl. DpapiCloudTokenStore never wired).
- 15 Jul 2026 (PAUSED MID-REFACTOR — tokens ran out; tree has UNCOMMITTED WIP that DOES NOT
  COMPILE yet) · John asked for a /simplify-style quality review of the whole branch; 5 review
  agents produced ~49 findings; application was in progress when paused. **STATE:**
  - DONE + verified, uncommitted: (a) supabase edge functions — shared
    `_shared/paths.ts` (key layout) + `captureVerifiedUploads` in `_shared/s3.ts` (Map + 8-way
    bounded parallel verify) used by both finish fns; `stubAssumeRole`→test_support;
    `S3_WRITE_ACTIONS` shared — deno 33/33 BEFORE AND AFTER + deno check clean. (b) launcher
    scripts — `terminateChildProcess` (with signalFirst option) in processTree.mjs used by
    go/run/watchBloomExe; new `automationReady.mjs` (prefix + scanner) used by run+watchBloomExe;
    watchBloomExe now uses pipeChildOutput — node --check + eslint clean (needs a live
    go.sh/run.sh smoke later).
  - DONE, uncommitted, MY C# batch-1 so far: BookVersionManifest Diff API deleted (+tests);
    CloudJoinFlow.ListMyCollections+CreateAndJoinCollection deleted; CloudCollectionClient
    UndeleteBookRpc/RenameCheck deleted + UnlockBookRpc/ForceUnlockRpc/DeleteBookRpc renamed
    without Rpc suffix (callers in CloudTeamCollection.cs updated; LockTests test method
    renamed); CloudAuth AccountSwitched/SignedOut events + EventArgs + raiseEvent param deleted
    (+tests rewritten as SignIn_WithDifferentAccount_ReplacesIdentity; stale comments fixed in
    CloudTeamCollection.cs + MemberTests); CloudEnvironment S3Bucket + FirebaseProjectId props +
    env reads deleted. **BROKEN RIGHT NOW: CloudEnvironmentTests still asserts env.S3Bucket
    (~line 18, ~71) and env.FirebaseProjectId (~76, ~88) — delete those assertion lines (and the
    two env entries "BLOOM_CLOUDTC_FIREBASE_PROJECT_ID"/S3_BUCKET in the sandbox test dict) to
    compile.**
  - Two fix agents were STOPPED MID-WORK (partial uncommitted edits, unverified): React/TS agent
    (was doing: extract CreateCloudTeamCollection.tsx from CreateTeamCollection.tsx — likely
    done; shared DevSignInForm.tsx — likely done; useSharingLoginState→useWatchApiData; shared
    test-render helper — was mid-way through the 13 test files, stopped at SharingPanel.test.tsx/
    NewerVersionAvailableMarker.test.tsx) and e2e agent (edits done — waitForSharingReady in
    harness/bloomApi.ts replacing ~14 poll copies + paths.ts header — but verification NOT run).
    On resume: run vitest/eslint/typecheck for BloomBrowserUI and finish/verify both.
  - REMAINING C# work I had queued (batch 1 tail): CloudBookTransfer drop dead
    `alreadyUploadedThisTransaction` param (both CloudTeamCollection call sites pass throwaway
    sets — removes the lock complexity too); CloudCollectionMonitor delete dead Stop() + doc
    Dispose; SharingApi: serialize `emailVerified` in HandleLoginState reply (1 line, makes the
    declared TS field truthful), simplify SignedInEmailForJoinCards→CurrentAuth().CurrentEmail
    (+line ~560 IsSignedIn), doc RegisterWithApiHandler; CloudTeamCollection rename
    CollectionIdForCloud→CloudCollectionId + doc (caller TeamCollectionApi.cs:293), rename
    TryGetBookIdForTests→GetBookIdByNameIndexForTests (caller LiveTests:224); TeamCollection.cs
    reword TryTakeOverLock doc ("true = may proceed as lock holder incl. nothing-to-take-over").
  - BATCH 2 (efficiency/reuse, NOT started): E1 GetRepoBooksByIdMap→protected virtual + cloud
    override from _cache.GetAllBooks() (kills 3 network calls/book at startup); E3
    OnPolledChanges early-return when get_changes empty + cursor unchanged (kills full-cache
    Save every 60s); E4 pass local manifest hashes into UploadChangedFiles (kills double SHA256);
    E6 CurrentUserDisplayName failure retry TTL ~30s; R1 DownloadCollectionFileGroup→
    S3Extensions.ListAllObjects; R2 delete CloudTeamCollection.BuildS3Client, reuse
    CloudBookTransfer.BuildDefaultClient (make internal static); R11 hoist
    FolderTeamCollection.AvailablePath→base TeamCollection, reuse in GetAvailableBloomSourcePath;
    R12 CloudAuth.CreateInitialized factory (3 dup sites: TeamCollectionManager:707,
    CloudTeamCollection:130, SharingApi:144).
  - BATCH 3 (file org, NOT started; John asked explicitly): extract partial
    CloudTeamCollection.CollectionFiles.cs (~310-line banner-delimited cluster ~1462-1770);
    partial TeamCollection.AutoApply.cs (queue machinery ~104-244 + ProcessAutoApplyRemoteChange
    + DownloadMissingBookInBackground + QueueMissingRepoBooks...); split DevCloudAuthProvider +
    FirebaseCloudAuthProvider out of CloudAuth.cs; move HandleReceiveUpdates' loop into
    CloudTeamCollection.ReceiveAllUpdates (NOTE: its predicate skips checked-out-HERE while
    GetUpdatesAvailableCount excludes locked-by-ANYONE — drift to surface to John, don't silently
    unify).
  - REPORT-ONLY for John (decisions): DpapiCloudTokenStore is NEVER WIRED (real sessions won't
    survive restart — wire at the 2 CloudAuth construction sites when AuthMode==Cloud, or delete
    until Firebase ships); E2 cache DownloadStart credentials (~1h TTL, big join speedup); E7
    rename-scan cost per poll; E8 history refetches whole log each open (use since-cursor); E9
    collection-file group re-downloads unchanged files (compare size/ETag from listing); A3
    AddCloudBookStatusFields JSON-surgery could move behind virtual WhoHasBookLocked* seams
    (skipped deliberately: identity logic just live-verified, not worth re-destabilizing); R6
    remaining hooks (useSharingMembers etc.) could share a reload-capable useWatchApiData; R13
    getApiDataOnce dedup (sharingApi/teamCollectionApi cached-promise idiom); R5 shared C# cloud
    test-fixture builder (9 copy-pasted harness blocks). Full agent reports also survive in the
    session tasks dir (a6925…=reuse, a095b…=simplify, af6da…=efficiency, ad850…=altitude,
    a48a9…=naming .output files).
  **EXACT next action on resume:** (1) fix CloudEnvironmentTests compile break; (2) finish C#
  batch-1 tail; (3) run required filter (expect ~465 minus deleted diff/event tests, plus fix
  fallout); (4) verify/finish the React+e2e agents' partial work (vitest/eslint/typecheck);
  (5) batches 2-3; (6) full test pass, live-smoke run.sh, commit in logical chunks, regenerate
  #8052 via regen scripts, re-trigger Greptile; (7) give John the fixed/skipped summary + the
  REPORT-ONLY decision list.
- 15 Jul 2026 (doc cleanup, per John) · Removed the spent orchestration scratch documents from
  the branch: all 11 agent launch prompts (`orchestration/*.prompt.md` — one-time kickoff
  prompts; the durable per-task specs stay in `tasks/*.md`, which code comments reference),
  `BUG0-OPTION-A-SKETCH.md` (bug #0 implemented as option (a) and fully closed — the outcome is
  recorded in this file's OUTSTANDING BUGS #0), and `notes-item7-progressive-join.md` (scouting
  notes with stale line numbers; item 7 shipped). RESUME.md was TRIMMED, not removed: its
  still-operative rules (mandatory C# test filter, review-before-merge, dev-stack bring-up,
  environment quirks) are what this file and IMPLEMENTATION.md point at; its stale Wave-4
  status and prompt-launching machinery are gone. Three e2e harness comments that cited
  09-e2e.prompt.md now cite src/BloomTests/e2e/README.md (same rules, kept doc). KEPT (the
  durable set): CloudTeamCollections.md (design), CONTRACTS.md, GOING-LIVE.md (real
  buckets/DB/deploy), IMPLEMENTATION.md (wave/merge history), tasks/*.md (specs+findings,
  code-referenced), docs/ (walkthrough, unit-test setup), notes/write-book-status-audit.md
  (code-referenced), this file (state), SQUASH-PLAN.md + regen-*.sh (needed until merge),
  server/dev + firebase + e2e READMEs. Mentions of the deleted files in OLD progress-log
  entries here and in tasks/IMPLEMENTATION logs are historical records and were left alone.
  **EXACT next action:** regenerate cloud-tc-for-review + force-push (diff shrinks by the
  deleted docs), re-trigger Greptile; then back to the standing next steps (item 10 [HUMAN],
  promote #8052 when ready).
- 15 Jul 2026 (AM — fresh-eyes review of the 14 Jul work; three real fixes) · John asked for a
  review of yesterday's session. Findings, all fixed + regression-tested (465/465 on the
  required filter):
    1. **Rename-apply guard was unsound (data-loss risk).** The bug B fix guarded the
       SyncAtStartup rename-apply block with LOCAL status (`!GetLocalStatus(..).IsCheckedOut()`),
       but cloud checkouts never stamp local status (TryLockInRepo is RPC+cache only) — so
       "check out → retitle → RESTART before check-in" would rename the folder back to the repo
       name and overwrite the checked-out edits from the repo. Local status can also carry a
       STALE teammate lock (accept-remote-lock sync writes repo status locally), wrongly
       SKIPPING a legitimate rename. Fix: guard on the REPO lock (`IsCheckedOutHereBy(repo
       status)` — same machine-local rule as QueueMissingRepoBooksForBackgroundDownload) plus a
       `NewBookRenamedFrom(newName)==thisFolder` confirmation, which doubles as the
       folder-TC gate (base implementation always answers null when the folder has repo status,
       so folder TCs provably never enter the block). New tests:
       CloudSyncAtStartupTests.SyncAtStartup_TeammateRenamedBook_RenamesLocalFolderInPlace and
       SyncAtStartup_OwnRenameMidCheckin_DoesNotRevertRenameOrClobber (+ per-key S3 test harness).
    2. **Display-name cache ignored account switch.** CurrentUserDisplayName cached on a
       session-wide boolean, so after Bob signs out and Alice signs in (batch item 9), Alice's
       new local books showed BOB's display name. Fix: cache keyed by the signed-in email. New
       test: AddCloudBookStatusFields_AccountSwitch_RefreshesDisplayName.
    3. **run.sh freshness check was effectively dead after any edit.** It compared source mtimes
       against the apphost Bloom.exe, which incremental builds usually don't touch (only
       Bloom.dll changes) — so the skip-build fast path never fired again after the first C#
       edit. Fix: compare against the newest of Bloom.exe/Bloom.dll; also include the repo-root
       Directory.Build.* files in the source scan (master's new Directory.Build.props affects
       builds).
    Also: the review-branch regeneration scripts now live IN THE REPO
    (orchestration/regen-bucket.sh + regen-rebuild.sh, referenced from SQUASH-PLAN.md) instead
    of a session-scratchpad that dies with the session. Reviewed-and-fine: incremental-download
    seeding, upload-race lock, LockedSeat/LocalVersionSeq cache fixes, ExperimentalFeatures
    exact-token match, XLF fix, launcher architecture. **EXACT next action:** regenerate
    cloud-tc-for-review with the new scripts, force-push #8052, re-trigger Greptile.
- 14 Jul 2026 (PM #6 — CLEAN REVIEW CHECKPOINT: Devin + Greptile both satisfied) · Greptile
  re-review of #8052 head 8fbe66e4ea PASSED (9m27s) with NO new findings; the prior XLF P1 is
  fixed in the new head and its thread is resolved/outdated; all checks green (Greptile,
  pr-automation, track); PR MERGEABLE, still draft. **Review status: both bots clear.** Devin
  (via the fork slice-reviews) → 7 actionable, 5 fixed + 1 dismissed + 1 deferred. Greptile
  (full diff) → clean after the XLF P1 fix. **Remaining before human review/merge are the known
  [HUMAN] items only:** item 10 real Bloom Library web upload/download + the deferred
  S3ForcePathStyle production-AWS check (GOING-LIVE.md 4.3); and John's live spot-checks. No
  agent action pending on the bot gauntlet. EXACT next action (future): item-10 production
  validation; when ready, promote #8052 from draft to ready-for-human.
- 14 Jul 2026 (PM #5 — review branch refreshed with all fixes; Greptile re-running) · Re-merged
  origin/master (now 7209ba3bc1 → cloud-collections df580ff6bf); required filter 462/462; pushed.
  cloud-tc-for-review REGENERATED (bucket now also assigns ExperimentalFeaturesTests.cs → g6;
  --no-verify; byte-identical to cloud-collections — empty diff) and force-pushed → **PR #8052
  head 8fbe66e4ea, MERGEABLE**, now carrying all Devin-finding fixes. Greptile re-review triggered
  (@greptile-apps review) and PENDING on the new head. **Fork Devin-probe cleanup DONE:** PRs
  JohnThomson #3/#4/#5 closed, branches devin-probe-{base,client-core,server,backend-api} deleted
  (remote + local). Devin slice-review harvest fully complete (7 actionable → 5 fixed, 1 dismissed,
  1 deferred to item 10). **EXACT next action:** when Greptile finishes on 8fbe66e4ea, gather +
  triage its findings; fix any real ones on cloud-collections and re-regenerate/force-push.
- 14 Jul 2026 (PM #4 — Devin findings triaged + FIXED) · Commit `99bfb1e102` on cloud-collections
  (pushed) fixes 5 of the 7 actionable Devin findings; 462/462 required-filter + new regression
  test; eslint clean. **Fixed:** (1) CloudBookTransfer upload race — alreadyUploadedThisTransaction
  now accessed only under resultGate; (2) CloudRepoCache.RecordCheckinFinish now clears LockedSeat
  on release; (3) ApplyFullSnapshot now carries LocalVersionSeq across the swap (was wiping it →
  needless re-downloads); (6) ExperimentalFeatures now exact-token match (was substring — cloud/
  folder TC flags collided) + regression test CloudAndFolderTeamCollectionTokensDoNotCollide;
  (7) CollectionHistoryTable.tsx now labels numeric-type ≥100 cloud incident events (were blank).
  **DISMISSED (5) BloomS3Client.cs:130** — false positive: the flagged `internal static
  CreateAmazonS3Client(config,credentials)` is a NEW helper, not a visibility downgrade; the methods
  bloom-harvester actually overrides (GetAccessKeyCredentials, the bucketName CreateAmazonS3Client
  overload) are unchanged `protected virtual` — no cross-repo break. **DEFERRED (4)
  CloudEnvironment.cs:144 S3ForcePathStyle** to item 10 (GOING-LIVE real-AWS validation): the logic
  `= !IsNullOrEmpty(S3Endpoint)` is correct for dev/MinIO (DefaultS3Endpoint is the MinIO URL) but
  would force path-style on real AWS; the right production config (leave S3Endpoint empty, or an
  explicit force-path-style override) is exactly what item 10's production validation must settle.
  **EXACT next action:** re-regenerate cloud-tc-for-review from cloud-collections (bucket.sh/
  rebuild.sh, --no-verify), force-push #8052; check/handle Greptile's re-review of the new head;
  delete the throwaway fork probe PRs/branches (JohnThomson #3/#4/#5, branches devin-probe-*).
- 14 Jul 2026 (PM #3 — ALL Devin slice findings gathered; triage/fix next) · Heads unchanged:
  cloud-collections `5c8275bc9a`, cloud-tc-for-review `b45e5e21be` = #8052 (MERGEABLE). Devin
  reviews of all three fork slices are COMPLETE. **Full actionable inventory to triage/fix on
  cloud-collections:**
    - #3 client-core (g5): BUG CloudBookTransfer.cs:241 `alreadyUploadedThisTransaction` ISet.Add
      inside Parallel.ForEach = data race (CONFIRMED real); BUG CloudRepoCache.cs:404 lock seat not
      cleared on check-in release (stale cache); BUG CloudRepoCache.cs:270 full snapshot wipes local
      version tracking → needless re-downloads; INVESTIGATE CloudEnvironment.cs:144 S3ForcePathStyle
      always true (non-empty default endpoint) → breaks real AWS (ties to item 10); INVESTIGATE
      BloomS3Client.cs:130 protected→internal-static breaks bloom-harvester subclass.
    - #4 server (g2+g3): 0 Bugs, 0 Investigate. 6 Informational (3 are "already fixed by a later
      migration"; 1 worth a glance: rpc.ts:65 user values interpolated into PostgREST query strings
      w/o URL-encoding — Devin rated Informational).
    - #5 backend+API (g6+g7): BUG ExperimentalFeatures.cs:20 enabling cloud TC feature silently
      enables the folder one too + disabling folder corrupts the cloud flag; BUG HistoryEvent.cs:36
      a new (cloud) history event type renders BLANK in the history table. Informational (skip):
      FolderTeamCollection PutBook checkinComment not passed; RemoteBookAutoApplyQueue.cs:83-91
      case-sensitivity mismatch between dedup set & priority-move (glance-worthy, rename-adjacent);
      SharingApi static auth/client never disposed; a positive null-ref-fix note; a test-only virtual.
    Net: **7 actionable** (5 from g5, 2 from #5), plus 2 glance-worthy Informationals (rpc.ts URL-encode,
    auto-apply-queue case-sensitivity). Greptile re-review of #8052 new head still not posted — check.
  **EXACT next action:** triage each of the 7 against the code; fix the real ones on cloud-collections
  (begin with the CONFIRMED CloudBookTransfer race). Then Greptile #8052, re-regenerate + force-push,
  delete fork probe branches/PRs (#3/#4/#5, branches devin-probe-*).
- 14 Jul 2026 (PM #2 — SESSION-SAVE for machine sleep; fixes + Devin-slicing review in flight) ·
  **Branch heads:** cloud-collections = `812703d56a` (origin in sync); cloud-tc-for-review =
  `b45e5e21be` (origin in sync) = PR #8052 head, MERGEABLE. Working tree clean.
  **Code shipped this session (all committed + pushed on cloud-collections, ride to #8052 via the
  regen):**
    - Build-once dev launcher `run.sh` (+ `run.mjs`, shared `viteDevServer.mjs`/`childOutput.mjs`;
      `go.mjs` refactored onto them; Program.cs suppresses the DEBUG "Attach debugger" modal under
      --automation; AGENTS.md documents both launchers). Commit c1665fecdc.
    - Bug A (new-local-book avatar shows account display name, not email): CloudTeamCollection
      `CurrentUserDisplayName` cached from MembersList; TeamCollectionApi stamps it. Commit 3d240de7a7.
    - Bug B (teammate-rename round-trip): SyncAtStartup now detects the remote rename by instance id
      even when statusJson is non-null (cloud identity-first) and renames the local folder IN PLACE
      (no duplicate, no stale name). Commit 0b27f6a6f6. LIVE-VERIFIED by John.
    - Incremental Receive: FetchBookFromRepo seeds staging from the local folder so DownloadFiles'
      hash-skip re-downloads only changed files (rename refresh + all Receives). Commit 696958aff8.
    - Greptile P1: launch-crashing `--` in an XLF <note> (CollectionTab.BookNotYetDownloaded) →
      replaced with ';'. Commit acf3ba9272.
    - Decisions recorded (see items #0, #13 above): Send All stays put; server-side cross-seat
      check-in enforcement = WON'T DO (bug #0 fully closed).
  **Preflight / review state:**
    - origin/master (51e467d5de) merged into cloud-collections (clean, 812703d56a); required-filter
      460/460. cloud-tc-for-review regenerated per SQUASH-PLAN (byte-identical, `--no-verify` on the
      9 regen commits — John-approved, since the content already passed hooks on cloud-collections;
      this made the rebuild seconds instead of timing out on the per-commit hook). Force-pushed → #8052.
    - **Devin can't review #8052 (246 files "too large").** So we review the feature in SLICES via
      throwaway PRs ON JOHN'S FORK (JohnThomson/BloomDesktop) to keep the main repo clean. Mechanism:
      base branch = exact master commit `devin-probe-base`; each slice branch checks out that group's
      files from cloud-collections; stacked where dependent. Slices → fork PRs:
        - #3 client-core (g5, 19 files, base devin-probe-base) — **Devin DONE, 3 Bugs + 2 Investigate
          + 3 Info** (below).
        - #4 server (g2+g3, 39 files, base devin-probe-base) — Devin ANALYZING (triggered).
        - #5 backend+API (g6+g7, 54 files, base devin-probe-client-core so g5 types are context) —
          Devin ANALYZING (triggered).
    - **g5 (#3) Devin findings — NOT yet triaged/fixed:** Bugs: (1) CloudRepoCache.cs:404 lock SEAT
      not cleared when a check-in releases the lock (stale cache metadata); (2) CloudRepoCache.cs:270
      full server snapshot wipes local version tracking → needless re-downloads (matches a queued
      follow-up); (3) CloudBookTransfer.cs:241 `alreadyUploadedThisTransaction` (plain ISet) `.Add()`
      inside a Parallel.ForEach = data race — **CONFIRMED real by code read.** Investigate: (a)
      BloomS3Client.cs:130 protected→internal-static on a method bloom-harvester (separate repo)
      extends; (b) CloudEnvironment.cs:144 S3ForcePathStyle always true (non-empty default endpoint)
      → breaks real AWS in production (ties to item 10 GOING-LIVE). Info (skip): CloudSession setters,
      CloudAuth timer race (benign), S3Extensions null-guards OK.
    - Greptile on #8052 NEW head (b45e5e21be): only pr-automation shows so far; Greptile's re-review
      not yet posted — CHECK next session.
  **EXACT next actions (resume here):**
    1. Gather Devin findings from fork PRs #4 (server) and #5 (backend+API) — devin-review skill,
       chrome-devtools isolated context, URLs app.devin.ai/review/JohnThomson/BloomDesktop/pull/{4,5}.
    2. Triage ALL Devin findings (g5 #3 + #4 + #5) against the code; fix the real ones on
       cloud-collections (start with the confirmed CloudBookTransfer.cs:241 race). Also handle
       Greptile's fresh #8052 review.
    3. After fixes: re-run required filter, re-regenerate cloud-tc-for-review (bucket.sh/rebuild.sh
       in scratchpad — rebuild.sh now uses --no-verify), force-push #8052.
    4. Clean up the throwaway fork probe branches + PRs (JohnThomson #3/#4/#5, branches
       devin-probe-base / -client-core / -server / -backend-api) when the review harvest is done.
  **Tooling notes:** regen scripts live in this session's scratchpad (bucket.sh updated so all 253
  diff files bucket into the 9 groups, 0 unmatched; rebuild.sh has --no-verify). Master added
  `build/agent-dotnet.sh|ps1` + Directory.Build.props: builds/tests into a private per-terminal tree
  so a running Bloom no longer blocks builds (`build/agent-dotnet.sh test <proj> --filter ...`).
  Guard: plain `dotnet test` under the new Directory.Build.props left spurious `Bloom.sln` /
  `BloomTests.csproj` edits (dropped BloomExe project+ref) — discard them (`git checkout --`) if they
  reappear; the committed versions are correct.
- 14 Jul 2026 (John pausing — low Fable credits; preflight kicked off) · All instances
  closed; full required filter 459/459 (includes bug #17-round-2's new test, previously
  unrun). origin/master merged into cloud-collections (clean, 17 files, f5a00c1cae) and
  cloud-tc-for-review REGENERATED per SQUASH-PLAN (same 9 review-grained commits, head
  8ac48df0db, byte-identity verified — empty diff vs cloud-collections), force-pushed;
  PR #8052 now MERGEABLE at the new head with all dogfood fixes through bug #18.
  Bot gauntlet triggered: pr-automation (Devin) run 29341744792 in progress;
  @greptile-apps review comment posted (246 files > auto limit). Regeneration gotcha
  recorded: the identity check REQUIRES cloud-collections to be up to date with
  origin/master first (stale master files otherwise show as diff; one BloomExe.csproj
  merge artifact forced a second rebuild — cheap). Also: bash scripting note — $GROUPS is
  a readonly bash builtin; don't use it as a variable name (cost one puzzled retry).
  NEXT (when agent returns): gather Devin/Greptile findings from PR #8052 and
  triage/fix; then remaining [HUMAN] items. FOR JOHN meanwhile (no agent needed):
  (1) restart Bob → verify new/local-only books now say bob@dev.local (bug #17 fix);
  (2) rename round-trip retest: Bob checkout → retitle → check in → Alice should get ONE
  renamed folder (bug #18 fix), watch for the "renamed by a teammate" message;
  (3) display names: set names via the Sharing-panel pencil and verify "checked out to
  <name>" + history show them; (4) item 10 [HUMAN]: real Bloom Library web
  upload/download (AWSSDK v4 validation, GOING-LIVE.md 4.3); (5) decide bug #0 follow-up
  (server-side refusal of same-user cross-seat check-in?) and bug #13 UX (Send All
  discoverability); (6) if Alice's "My first test" blocks editing (lock carries the
  OneDrive seat), admin Force Unlock from the Status panel; (7) housekeeping when done:
  restore sleep timeouts (powercfg /change standby-timeout-ac 120; standby-timeout-dc 3).
- 13 Jul 2026 (Sunday PM #2 — **member display names**, John's request, CODE + SQL TESTS
  DONE; bug #14 found+fixed) · "Show who has a book checked out (and similar) by a
  human-readable name, email as fallback; admins edit the name in the Sharing panel."
  Server (CONTRACTS.md bumped to v1.6, additive): 20260713000001 adds
  tc.members.display_name; members_set_display_name RPC (admin sets anyone's, a claimed
  member their own, blank clears, ≤100 chars); members_list rows carry display_name
  (DROP+CREATE, re-granted); resolve_member_display prefers the durable column over the
  JWT-claim event capture — so locked_by_name in get_collection_state/get_changes/
  get_book_manifest picks it up with no signature changes; get_changes event rows gain
  by_display_name (the CURRENT durable name). 20260713000002 fixes bug #14 (see
  OUTSTANDING BUGS). Both migrations applied to the live local stack via
  `supabase migration up` (NO db reset — John's Tetun server data untouched);
  `supabase test db` 89/89 (24 new in 03_tc_member_display_name_test.sql: auth matrix
  admin/self/other/non-member, trim/clear/too-long, precedence, full pipeline via
  get_collection_state + get_changes). C#: SharingApi.ToApprovedMember maps display_name
  → name; new sharing/setDisplayName endpoint; CloudCollectionClient.MembersSetDisplayName;
  ToBookHistoryEvent UserName preference by_display_name → by_user_name → by_email;
  CloudTeamCollection.StatusFromCachedBook now puts the whole display name in
  lockedByFirstName (surname null — both TS consumers render that cleanly; lockedBy STAYS
  the email because the panel compares it with currentUser for lockedByMe). UI:
  SharingPanel member rows get an admin-only pencil → inline input (Enter/blur commits
  trimmed, Escape cancels, empty clears; all spans/inputs, no new divs, per the CSS
  hazard above); sharingApi.setDisplayName. Vitest 12/12 (5 new); eslint/tsc clean.
  SharingApiTests updated (+2 new C# tests) but **the C# suite has NOT been run**: John's
  Alice instance (pnpm go, dotnet watch PID 67472) is live from THIS worktree, so
  `dotnet test` would fight the locked output\Debug — ALSO NOTE the watcher has likely
  already picked up this session's C# edits (may have rebuilt/restarted Alice) · Next:
  run the C# required filter once Alice closes; John verifies the pencil in the Sharing
  panel + a "checked out to <name>" status; follow-ups queued: member self-service name
  UI, folder-TC parity n/a (folder TCs already use registration first/surname).
- 13 Jul 2026 (Sunday PM — John's UI niggles fixed; **CSS HAZARD recorded**) · Three niggles
  from live testing, all pushed: (1) create-success message now says "Team Collection panel"
  instead of "Sharing panel" (CreateTeamCollection.tsx + XLF, John's call: fix the message,
  don't rename the panel); (2) "(experimental)" dropped from both the Cloud Team Collections
  checkbox label (AdvancedSettingsPanel.tsx — it already sits in an "Experimental Features"
  list) and the "Share this collection on the Bloom sharing server" button
  (TeamCollectionSettingsPanel.tsx); (3) Sharing-panel row alignment fixed (23b636f966).
  **HAZARD for anyone adding UI to the Team Collection settings panel:** the legacy rule
  `#teamCollection-settings div:not(.no-space-below) { margin-bottom: 10px; }`
  (TeamCollectionSettingsPanel.less) uses an ID selector, so it outranks every
  emotion-class rule and silently adds 10px below EVERY div descendant — inside a
  `align-items: center` flex row this lifts div children (avatar, text column, MUI Chip)
  10px above honestly-centered non-div siblings (select, button). Neutralize with
  `> div { margin-bottom: 0 !important; }` on the flex row (see SharingPanel.tsx's
  MemberRow/AddMemberRow comments); nothing weaker wins. Found by John with the inspector
  after two rounds of margin/height guesses failed · Next: member display names (John's
  13 Jul request): display_name column on tc.members + admin editing in the Sharing panel
  + "checked out to <name>" with email fallback.
- 13 Jul 2026 (Sunday AM — weekend recovery + second launch failure diagnosed) · The Podman
  WSL machine stopped over the weekend: whole local stack was down (supabase functions serve
  "failed to run docker"; podman ps connection refused). RECOVERED: podman machine start →
  supabase stop/start → db reset (migrations + seed users) → MinIO compose up → functions
  serve fresh → smoke 3/3 PASS. John's second `pnpm go` failure ("Bloom was started on port
  51040, but no vite server was available") did NOT reproduce on the next run (Bloom came up
  fine, ready line + Vite connected): cold-start race — first post-weekend launch runs
  Vite's dependency optimizer concurrently with a full dotnet-watch rebuild, and Bloom's
  ReactControl.IsViteDevServerRunning allows only 400ms per origin (~1.2s total) at exactly
  that moment. **QUEUED DX FOLLOW-UP: make the startup --vite-port validation patient**
  (retry/longer window, mirroring go.mjs's own two-consecutive-successes poll) — NOT done
  yet because a live dotnet watch was attached to John's running instance (never edit C#
  under a live watcher) · Next: John's human tests in the running instance
  (alice@dev.local / BloomDev123!, fresh collection).
- 11 Jul 2026 (afternoon — human-test launch failure diagnosed + fixed) · John's `pnpm go`
  timed out ("Bloom did not emit BLOOM_AUTOMATION_READY within 120000 ms"). Cause: the E2E
  harness launches write to the SHARED per-machine user.config, so the last matrix left
  `BloomE2E-join-auto-open` (a cloud TC whose server rows the per-scenario DB resets wiped)
  at the top of MruProjects; pnpm go auto-opened it and the connection-refusal MessageBox
  (native, blocking, invisible to the launcher) hung startup. FIX: removed the BloomE2E/
  BobPlaceholder MRU entries (backup: user.config.bak-pre-mru-clean). **HARNESS FOLLOW-UP
  (queued):** the E2E harness must stop polluting the human's MRU — snapshot+restore
  MruProjects around a run (globalSetup/globalTeardown), or launch instances with an
  isolated profile. Same class of hazard as the experimental-flag manipulation, but this
  one actively breaks the next manual launch · Next: John's human tests proceed
  (alice@dev.local / BloomDev123!; fresh collection required — old server rows are gone).
- 11 Jul 2026 (morning — **GOLD STAMP: FULL MATRIX 14/14**, 34.4 min, one run, zero
  failures) · The desktop-unlock watcher fired, environment verified clean, and the
  matrix ran green end to end on the final tree (af8a92a516 = PR #8052's content +
  batch-log commits): e2e-1 through e2e-10 + join-auto-open, including e2e-4's
  seat-gated takeover refusal and e2e-10's legitimate same-seat takeover. This is the
  first fully-green FULL matrix since the post-batch defect hunt began — the batch's
  test pipeline is CLOSED. Everything that remains is John's: [HUMAN] item 3 (centered
  check-in dialog) + item 10 (web up/download, GOING-LIVE.md 4.3), and the OUTSTANDING
  BUGS #0 follow-up decision (server-side same-user cross-seat check-in refusal).
  PR #8052 is ready for review whenever the human checks are done · Next: John.
- 11 Jul 2026 (~04:30 — optional gold-stamp matrix INVALID: desktop locked again mid-run,
  14/14 CDP-connect failures = the locked-session signature; sleep timeouts were already
  disabled, so this was a manual lock or an unseen policy). No leaked processes. NOT a
  loss: the gold run was optional — every scenario has already passed on this exact tree
  (13/14 matrix + standalone exonerations). Unlock watcher re-armed in the orchestrator
  session; if it fires while the session lives, the matrix reruns · Next: John's items
  (see the SQUASH-PLAN-EXECUTED entry above); rerun `yarn test` with the desktop
  UNLOCKED whenever a gold stamp is wanted.
- 11 Jul 2026 (early AM — SQUASH PLAN EXECUTED; PR #8052 open, #8048 closed) · Merged
  origin/master into cloud-collections first (13 commits, RAB/spreadsheet only, zero
  conflicts, filter 428/428). Built `cloud-tc-for-review` per SQUASH-PLAN.md: 9
  review-grained commits, **byte-identical to cloud-collections (empty diff)** after
  converging the prettier drift the packaging build exposed in 14 edge-function files
  (formatting-only; deno 33/33 after; details in SQUASH-PLAN.md's executed-note). Draft
  **PR #8052** opened with review order + caveat + bot history; **#8048 closed** with
  pointer. REMAINING for the batch: (1) John's [HUMAN] tests — item 3 centered check-in
  dialog, item 10 web up/download (GOING-LIVE.md 4.3); (2) John's OUTSTANDING BUGS #0
  follow-up decision (server-side same-user cross-seat check-in refusal); (3) optionally
  one more full matrix as the gold stamp (every scenario HAS passed on this tree; the
  last run was 13/14 with the 14th standalone-exonerated + spec-hardened); (4) regenerate
  cloud-tc-for-review whenever cloud-collections advances.
- 11 Jul 2026 (early AM — POST-SEAT-FIX FULL MATRIX: 13/14, and the 14th is exonerated) ·
  Matrix on the seat-fixed tree (36 min): **e2e-4 PASSED IN THE MATRIX** (bug #0 verified
  under full load); sole failure e2e-5, which passed standalone immediately after (3.0 min)
  = load flake. Root cause found anyway and HARDENED: the spec killed Alice before her
  initial share's asynchronous v1 commit was guaranteed done (nothing between
  createCloudTeamCollection and the kill waits for the book row) — killing her mid-first-
  Send leaves no book row ever. Fix: 90s poll for current_version_seq >= 1 BEFORE
  alice.kill(). Every scenario has now passed on this exact tree; the tight-timeout flake
  class is systematically addressed (all queue/commit polls at the 90s convention:
  e2e-5/6/7/9) · Next: execute SQUASH-PLAN.md (preconditions met: bug #0 fixed+verified,
  matrix verdict in) → cloud-tc-for-review branch + PR, close #8048 with pointer; then
  optionally one more matrix as the gold stamp; John: [HUMAN] tests + OUTSTANDING BUGS #0
  follow-up decision.
- 11 Jul 2026 (early AM — BUG #0 FIXED AND VERIFIED; bot gauntlet fully closed) · John's
  ruling (his words, from the in-session Q&A): "we should only be allowed to edit (either as
  the original user checking the book out, or taking it over) if it is being worked on here,
  in this copy of the collection… as long as the book is checked out here (this local copy)
  and the logged-in user is a member, editing and take-over of the checkout should be
  allowed. (A different user who has a different copy of the collection open, like our bob
  and alice collections, definitely can't do this.)" — i.e. option (a) extended to the
  "checked out here" determination. IMPLEMENTED (details in OUTSTANDING BUGS #0): server
  seat column + gated takeover + auto-clear trigger (migration 20260711000003), client
  SeatId + seat-gated IsEditableHere/CanTakeOverLockOnThisMachine (seams now take bookName),
  CONTRACTS.md v1.5. VERDICTS: pgTAP 65/65 (10 new seat cases incl. e2e-4's
  same-machine-different-seat refusal), C# filter 428/428 (6 new), **e2e-4 PASS** (first
  time since the defect hunt began), **e2e-10 PASS** (same-seat takeover intact). Earlier
  same night: full matrix 12/14 (37 min, desktop unlocked after John returned; sleep
  timeouts disabled via powercfg — the mid-run locks were the 120-min AC idle-sleep timer);
  the two failures were e2e-4 (now fixed) and e2e-7 (standalone 2/2 = load flake; its 20s
  first-commit poll bumped to 90s). GREPTILE RE-REVIEW: "all three findings correctly
  resolved. No new blocking issues." Gauntlet state: Greptile complete+clean, Devin
  size-failed (terminal), CodeRabbit not installed, CI green · Next: (1) final full matrix
  (expect 14/14 — first ever fully-green matrix if it holds), (2) execute SQUASH-PLAN.md →
  cloud-tc-for-review PR, close #8048 with pointer, (3) John: follow-up decision in
  OUTSTANDING BUGS #0 (server-side same-user cross-seat check-in) + [HUMAN] tests (item 3
  centered dialog, item 10 web up/download, GOING-LIVE.md 4.3).
- 10 Jul 2026 (night) · FULL MATRIX ATTEMPT INVALID — 14/14 failed because the Windows
  desktop LOCKED sometime after the standalone runs (LogonUI confirmed running afterward;
  every failure is at connectOverCdp / launch, the locked-session signature the E2E rules
  warn about). NOT a code regression: e2e-3/6/9 had passed standalone within the previous
  hour on the same tree. Orchestrator error to not repeat: re-check LogonUI immediately
  BEFORE every launch, not just at session start. No leaked Bloom processes; stack healthy
  (functions serve re-served itself cleanly after the pgTAP db reset). Greptile thread
  replies posted (all 3 findings fixed in b93d0c9d82) · Next: rerun `yarn test` (full
  matrix) as the FIRST action once the desktop is unlocked — expect 13/14 (e2e-4 = bug #0);
  a desktop-unlock watcher is armed in the orchestrator session to catch the moment.
- 10 Jul 2026 (late evening — runbook step 1 COMPLETE + Greptile findings fixed) ·
  **e2e-3/6/9 ALL GREEN STANDALONE.** e2e-3 passed as-is (pure load flake). e2e-6 FAILED
  standalone and was a REAL spec bug: since item 7 (progressive join), a book new to an
  instance arrives via the background download queue AFTER pollNowViaReceiveUpdates
  returns — the spec read Bob's file immediately (evidence: the book folder existed on
  disk moments after the assertion failed). Fixed: v1-baseline read is now an expect.poll
  (90s, the harness convention); the two 20s ceilings on queue-driven arrivals (e2e-6 v2
  arrival, e2e-9 first test) bumped to 90s. e2e-9 then 3/3 — its one intermediate failure
  (name-race alice: 0-byte stdout at launch) was load I caused myself by running
  lint/vitest during the run; reran truly idle → green. LESSON REINFORCED: "standalone"
  means the AGENT runs nothing else concurrently either. **GREPTILE (bypass) DELIVERED:
  1 P1 + 2 P2, all verified real and FIXED:** (P1/security) checkin-start scoped S3 write
  creds to the CALLER-SUPPLIED bookInstanceId — checkin_start_tx never validates it for
  existing books, so any member could get write creds for any book's prefix in their
  collection; now reads the DB-canonical instance_id back (same selectTcRow pattern as
  checkin-finish) + new deno test pinning that a mismatched client value cannot steer the
  prefix. (P2) reap_expired_checkin_transactions returned only the collection-file count
  (GET DIAGNOSTICS clobbered the loop total) → new migration 20260711000001. (P2)
  checkout_book_takeover raised P0002/42501 bare strings instead of the schema-wide
  PT404/PT403 JSON convention (C# would map both to CloudErrorCode.Unknown) → new
  migration 20260711000002 (logic untouched); pgTAP 4a expectation updated. Also fixed
  Greptile's style note: JoinCloudCollectionDialog.tsx nested ternaries → if/else chains
  (12/12 vitest, lint+prettier clean). Suites: pgTAP 55/55 on the reset stack; deno
  33/33 (NOTE: invariants.test.ts needs --allow-read; without it 2 tests fail on file
  access, not logic). CONTRACTS.md check: the takeover row was ALREADY added in v1.4 —
  the "flagged, not applied" comments in 20260709000007/CloudTeamCollection.cs are stale
  · Next: reply to + resolve the Greptile threads on PR #8048, push, then bug #0 (John),
  squash plan, human tests.
- 10 Jul 2026 (evening — RESUMED after machine sleep; runbook step 1 + bot gauntlet closure) ·
  Environment: containers survived sleep (all healthy), functions serve restarted per the
  zombie rule, smoke.ps1 3/3 PASS, desktop unlocked. e2e-3 STANDALONE: **PASS** (3.1 min,
  idle machine) — its matrix failure confirmed as a load flake; e2e-6/e2e-9 standalone runs
  in progress. BOT GAUNTLET now TERMINAL for PR #8048 (no more waiting): **Devin FAILED —
  "This pull request's diff exceeds the size limit for analysis"** (its review page's Info
  sidebar; no bypass exists, so Devin will also fail on the future squash-plan PR — same
  237-file diff); **Greptile REFUSED — 237 files > its 100-file limit** — but offers a
  bypass, which was TAKEN: `@greptile-apps review` posted on #8048 (bot findings may arrive
  async; check the PR's comments/reviews next visit); **CodeRabbit is NOT INSTALLED on this
  repo** (zero comments ever, repo-wide search; no .coderabbit.yaml) — last session's
  "timed out after 35 min" was waiting on a bot that isn't there; drop it from all future
  waits in this repo; CI 2/2 pass (unchanged) · Next: e2e-6/e2e-9 standalone verdicts, then
  the remaining runbook order (bug #0 = John, squash plan, human tests).
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
