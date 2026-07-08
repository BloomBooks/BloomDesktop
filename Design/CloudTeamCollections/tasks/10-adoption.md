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
