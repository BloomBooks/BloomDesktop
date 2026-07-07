# 08 — UI: collection tab (Wave 2 shells → Wave 3 wiring)

**Goal**: status button, status/history dialog, Share button, per-book panel states.

**Dependencies**: shells in Wave 2 (mocked); wiring after 06. **Exclusive owner of**
`TeamCollectionButton.tsx`, `TeamCollectionDialog.tsx`, `TeamCollectionBookStatusPanel.tsx`,
`statusPanelCommon.tsx`, `CollectionHistoryTable.tsx` during its waves.

## Steps
- [x] Status button: same chip/colors, driven by live metadata ("Updates available (3 books)").
- [x] Status dialog: "Receive Updates" primary action (Reload remains only for applied
      collection-settings changes); "Send All"; message log unchanged.
- [ ] Share button beside the status button → SharingPanel (admin manage / member read-only).
- [ ] Per-book panel: keep Check out/Check in + note field + avatars; add signedOut (with
      Sign-in action), updatesAvailable, offline-disabled-with-reason states; check-in progress
      = modal Send; "Force Unlock (Administrator Only)" wired to the audited RPC.
- [ ] Book thumbnails: holder-avatar overlay unchanged; subtle "newer version exists" marker.
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
