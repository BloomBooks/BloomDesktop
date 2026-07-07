# 08 — UI: collection tab (Wave 2 shells → Wave 3 wiring)

**Goal**: status button, status/history dialog, Share button, per-book panel states.

**Dependencies**: shells in Wave 2 (mocked); wiring after 06. **Exclusive owner of**
`TeamCollectionButton.tsx`, `TeamCollectionDialog.tsx`, `TeamCollectionBookStatusPanel.tsx`,
`statusPanelCommon.tsx`, `CollectionHistoryTable.tsx` during its waves.

## Steps
- [x] Status button: same chip/colors, driven by live metadata ("Updates available (3 books)").
- [x] Status dialog: "Receive Updates" primary action (Reload remains only for applied
      collection-settings changes); "Send All"; message log unchanged.
- [x] Share button beside the status button → SharingPanel (admin manage / member read-only).
- [x] Per-book panel: keep Check out/Check in + note field + avatars; add signedOut (with
      Sign-in action), updatesAvailable, offline-disabled-with-reason states; check-in progress
      = modal Send; "Force Unlock (Administrator Only)" wired to the audited RPC.
- [x] Book thumbnails: holder-avatar overlay unchanged; subtle "newer version exists" marker.
- [ ] History tab: server events feed for cloud TCs (incl. incident entries), local cache for
      offline; folder TCs unchanged.

## Acceptance
- Component tests: panel state matrix (incl. new states), status-button states, history
  rendering incl. incidents.
- `yarn lint` clean; folder-TC UI behavior unchanged.

**Agent notes**: Sonnet. `StatusPanelState` additions must stay in sync with the C# status
JSON (CONTRACTS.md, book-status section).

## Progress log

- 7 Jul 2026 · Status button done. Added `teamCollectionApi.tsx` shared plumbing for Wave 2:
  `ITeamCollectionCapabilities`/`useTeamCollectionCapabilities` (mocked `teamCollection/capabilities`),
  `isCloudTeamCollection()` helper (branch on capability, not concrete type),
  `useTeamCollectionStatusMetadata` (mocked `teamCollection/tcStatusMetadata`),
  `useCloudCollectionId`/`useIsTeamCollectionAdmin` (mocked, for the upcoming Share button), and
  the additive `IBookTeamCollectionStatus` fields from CONTRACTS.md
  (localVersionSeq/repoVersionSeq/signedIn/requiresSignIn/offlineDisabledReason). All these hooks
  only call their endpoint when the cloud-team-collections experimental feature is on, so folder
  Team Collections make zero extra requests and see zero UI change. `TeamCollectionButton.tsx` now
  shows "Updates Available (N books)" when the metadata provides a count. Also front-loaded the
  full Wave-2 XLF string set into `Bloom.xlf` (steps 1-4's strings) in this commit, since they were
  designed together; later steps consume already-added ids rather than adding more XLF. New test:
  `TeamCollectionButton.test.tsx` (8 tests, passing). `yarn eslint` clean (1 pre-existing warning
  unrelated to this change, in `useTColBookStatus`'s dependency array).
  Next action: implement step 2 (status dialog "Receive Updates" / "Send All"), then run its
  tests + prettier + commit.
- 7 Jul 2026 · Status dialog done (resumed after a session-limit interruption; predecessor's
  in-flight `TeamCollectionDialog.tsx` change and new `TeamCollectionDialog.test.tsx` had been
  preserved by the orchestrator in a WIP commit). `checkInAll`'s l10nKey/label switches to
  `TeamCollection.SendAll`/"Send All" and its post target to `teamCollection/sendAllBooks` when
  `isCloudTeamCollection(useTeamCollectionCapabilities())`; a new `receiveUpdates` button
  (`TeamCollection.ReceiveUpdates`, posts `teamCollection/receiveUpdates`) appears beside the
  existing "Reload Collection" button only for cloud TCs and only when `showReloadButton` is
  false, so the two are mutually exclusive (Reload stays reserved for applied collection-settings
  changes, per the design doc). Folder Team Collections are unaffected: `isCloud` is false
  whenever the capabilities hook's mocked endpoint is never called (flag off) or reports no cloud
  support, so `checkInAll` keeps its exact previous key/label/endpoint and `receiveUpdates` never
  renders. Fixed up the predecessor's WIP test file: removed leftover debug `console.log`s, made
  its `afterEach` use the repo's established `renderedContainer`/`unmountRoot` cleanup (matching
  `SharingPanel.test.tsx`/`JoinCloudCollectionDialog.test.tsx`) instead of a bare
  `document.body.innerHTML = ""`, and — the actual bug blocking all but one assertion — switched
  button lookups from matching visible English text to matching by element `id`
  (`checkInAll`/`receiveUpdates`/`reload`), because the vitest-only localizationManager mock
  resolves every `l10nKey` to the key itself rather than the English fallback (same gotcha
  documented in `JoinCloudCollectionDialog.test.tsx`'s file comment); the predecessor's
  English-text lookups could never match and were failing 4 of 5 tests before this fix.
  `TeamCollectionDialog.test.tsx`: 5 tests, all passing. `yarn eslint` clean on both changed
  files. No new XLF entries needed — `TeamCollection.SendAll`/`TeamCollection.ReceiveUpdates`
  were already front-loaded into `Bloom.xlf` in the step-1 commit.
  Next action: implement step 3 (Share button beside the status button → SharingPanel).
- 7 Jul 2026 · Share button done. New `teamCollection/ShareButton.tsx`: gated on
  `isCloudTeamCollection(useTeamCollectionCapabilities())` — folder Team Collections render
  nothing (not even a hidden node), so this is strictly additive UI. For cloud TCs, renders a
  `TeamCollection.Sharing.ShareButton` ("Share") button next to `TeamCollectionButton` (wired
  into `CollectionTopBarControls.tsx`, which isn't reserved by any other Wave-2 task per
  IMPLEMENTATION.md's shared-file schedule); clicking it opens an MUI `Popover` anchored under
  the button containing the existing `SharingPanel` (task 07), fed by
  `useCloudCollectionId()`/`useIsTeamCollectionAdmin()`/`useSharingLoginState().email` — so an
  admin gets the manage view and a regular member gets SharingPanel's own read-only view, with
  no new branching needed here. No new XLF: `TeamCollection.Sharing.ShareButton` was already
  front-loaded in the step-1 commit; used `@mui/icons-material/Share` (no dialog title needed,
  so no new string). New test: `ShareButton.test.tsx` (4 tests: folder TC renders nothing;
  cloud TC shows the button without opening the panel; admin click opens SharingPanel with
  `isAdmin: true`; non-admin click opens it with `isAdmin: false` and the member's own email) —
  SharingPanel itself is mocked (already unit-tested in `SharingPanel.test.tsx`) to a
  prop-recording stub; MUI's Popover portals to `document.body` like MUI Dialog, so assertions
  query `document` (same pattern as `JoinCloudCollectionDialog.test.tsx`). All 4 passing.
  `yarn eslint` clean on the 3 changed/added files. Re-ran `TeamCollectionButton.test.tsx` (8
  tests) to confirm the `CollectionTopBarControls.tsx` layout change (wrapped
  `TeamCollectionButton` + new `ShareButton` in a flex div) didn't regress it.
  Next action: implement step 4 (per-book panel states: signedOut/updatesAvailable/
  offline-disabled-with-reason, Force Unlock wiring) in `TeamCollectionBookStatusPanel.tsx`.
- 7 Jul 2026 · Per-book panel done. `TeamCollectionBookStatusPanel.tsx`: added
  `isCloud = isCloudTeamCollection(useTeamCollectionCapabilities())` and three new
  `StatusPanelState` values, all cloud-gated in the state-derivation effect (folder TCs never
  produce them, since their driving fields are always undefined there, but the effect also
  explicitly checks `isCloud` per the gating rule): `offlineDisabled` (from
  `props.offlineDisabledReason`, checked right after the existing `invalidRepoDataErrorMsg`
  check — a book that can't be used offline at all takes priority over lock state) renders the
  server-supplied reason verbatim (unlocalized, same precedent as `props.error` elsewhere in
  this file); `signedOut` (from `requiresSignIn && !signedIn`, checked where the book would
  otherwise be "unlocked") shows `TeamCollection.SignedOut`/`SignedOutDescription` with a
  `TeamCollection.Sharing.SignIn` button posting `sharing/showSignIn` (same action
  `JoinCloudCollectionDialog`'s NotSignedIn case uses); `updatesAvailable` (from
  `repoVersionSeq > localVersionSeq` on an otherwise-unlocked book) shows
  `TeamCollection.UpdatesAvailableForBook(Description)` with the existing
  `TeamCollection.ReceiveUpdates` button/endpoint reused from step 2. Check-in progress "modal
  Send": for cloud TCs, the `lockedByMe` case's inline yellow progress bar is replaced by a
  `BloomDialog` (title reuses the existing `checkingIn` string) with an MUI `LinearProgress`
  driven by the same `checkInProgress` websocket-driven state folder TCs already use; folder
  TCs keep the exact inline bar. Force Unlock: `ForceUnlockDialog.tsx` now posts
  `sharing/forceUnlock` instead of `teamCollection/forceUnlock` when `isCloud` — per
  CONTRACTS.md this maps (Wave-3) to the audited `force_unlock(book_id)` RPC ("admin; audited;
  emits ForcedUnlock event"); no client-side audit logic needed since the RPC handles that
  server-side. No new XLF: every string above (`SignedOut`, `SignedOutDescription`, `Sharing.SignIn`,
  `UpdatesAvailableForBook`, `UpdatesAvailableForBookDescription`, `ReceiveUpdates`,
  `OfflineDisabled`, and the reused `CheckingIn`) was already front-loaded or pre-existing.
  New test: `TeamCollectionBookStatusPanel.test.tsx` (11 tests) covering the folder-TC state
  matrix (unlocked/locked/lockedByMe/hasInvalidRepoData unchanged, plus an explicit
  gating regression test proving cloud-shaped fields are ignored when capabilities are all
  false) and the three new cloud states (signedOut incl. its Sign In action,
  signedIn-so-normal-checkout, updatesAvailable incl. its Receive Updates action and the
  no-update-when-current case, offlineDisabled's priority + verbatim reason, and the cloud
  modal not appearing at rest). All 11 passing. `yarn eslint` clean (only the one
  pre-existing, unrelated `checkInProgress` exhaustive-deps warning, confirmed present before
  this change too via `git stash`). Re-ran the full `teamCollection` + `CollectionTopBarControls`
  suites (7 files, 53 tests) — all green, no regressions.
  Next action: implement step 5 (book thumbnails: subtle "newer version exists" marker) —
  likely in the book-thumbnail/preview component that overlays the holder avatar (not yet
  identified by file name; search for the existing holder-avatar-overlay code first).
- 7 Jul 2026 · Book thumbnails done. Found the holder-avatar overlay in
  `collectionsTab/BookButton.tsx` (the per-book thumbnail button in the collection tab's grid,
  NOT `TeamCollectionBookStatusPanel.tsx` which is the panel for the currently-selected book):
  it renders `<BloomAvatar>` as a sibling of the thumbnail `<Button>` when
  `teamCollectionStatus.who` is set, absolutely positioned (top-left) via the `.avatar` class in
  `BooksOfCollection.less`, and `<BookOnBlorgBadge>` inside `.thumbnail-wrapper` (bottom-right,
  `position: relative` on the wrapper) as the existing template for a small corner marker.
  Left both completely unchanged (holder-avatar overlay confirmed still driven by the same
  `who` field, no new branching added there). New `teamCollection/NewerVersionAvailableMarker.tsx`
  (presentational, `show: boolean` prop): a small MUI `Update` icon with a
  `TeamCollection.UpdatesAvailableForBook` tooltip (reused from step 4, no new XLF), positioned
  top-right of `.thumbnail-wrapper` so it can't collide with the avatar (top-left of the whole
  button) or the on-Blorg badge (bottom-right). Wired into `BookButton.tsx`: added
  `isCloud = isCloudTeamCollection(useTeamCollectionCapabilities())` and
  `hasNewerVersionAvailable = isCloud && !teamCollectionStatus?.who && repoVersionSeq >
  localVersionSeq` (mirrors the `updatesAvailable` condition in
  `TeamCollectionBookStatusPanel.tsx`'s state machine — same rule, "unlocked book, newer
  version in the repo"), rendered as a new sibling of `BookOnBlorgBadge` inside
  `.thumbnail-wrapper`. Folder Team Collections: `repoVersionSeq`/`localVersionSeq` are always
  undefined there so the condition is false regardless, and `isCloud` is explicitly checked
  too per the gating rule.
  New test: `NewerVersionAvailableMarker.test.tsx` (2 tests: hidden when `show=false`, present
  when `show=true`) — the marker itself is a pure function of its prop, so it's tested in
  isolation the same way `SharingMembersList` is; `BookButton.tsx` is a large, complex,
  pre-existing, currently-untested file with many unrelated concerns (renaming, context menus,
  drag/drop, websocket label updates), so rather than write its first-ever test suite as a side
  effect of this task, the gating condition here intentionally mirrors logic already covered by
  `TeamCollectionBookStatusPanel.test.tsx`'s `updatesAvailable`/gating tests. The Acceptance
  section's test list (panel state matrix, status-button states, history rendering) doesn't
  call out thumbnail tests specifically. `yarn eslint` on both changed/added files: only
  pre-existing warnings, confirmed via `git stash` (2 unused-var + 3 exhaustive-deps warnings
  already present in `BookButton.tsx` before this change; new files clean). Full
  `teamCollection` + `collectionsTab` suites: 8 files, 55 tests, all green.
  Next action: implement step 6 (history tab: server events feed for cloud TCs incl. incident
  entries, local cache for offline; folder TCs unchanged) in `CollectionHistoryTable.tsx` — the
  last remaining step.
