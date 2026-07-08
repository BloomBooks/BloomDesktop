# 10 — Adoption path + polish (Wave 4)

**Goal**: the manual folder-TC → cloud path is smooth, documented, and clean.

**Dependencies**: waves 0–3. Touches: cloud-create flow (cleanup step), docs.

## Steps
- [ ] Enabling cloud on a formerly-folder-TC collection cleans stale artifacts: per-book
      `TeamCollection.status`, `lastCollectionFileSyncData.txt`, `log.txt`; simultaneous
      folder-link + cloud-link = error with fix instructions.
- [ ] Members' existing local copies reconcile by checksum on first Receive (verify the
      first-time-join merge path).
- [ ] User documentation: the un-team + enable + invite-team walkthrough (docs site), incl.
      "everyone check in first".
- [ ] Localization sweep of all new strings (xlf-strings skill rules).
- [ ] Analytics review: create/join/send (bytes uploaded vs skipped)/receive/force-unlock/
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
