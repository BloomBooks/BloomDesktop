# 07 — UI: setup, settings, sharing panel, chooser (Wave 1 shells → Wave 3 wiring)

**Goal**: the create/share/join surfaces, Notion-simple.

**Dependencies**: shells against mocked endpoints in Wave 1; real wiring after 06.
Owns new `src/BloomBrowserUI/teamCollection/SharingPanel.tsx`,
`JoinCloudCollectionDialog.tsx`; **exclusive owner of** `CreateTeamCollection.tsx`,
`TeamCollectionSettingsPanel.tsx`, `CollectionChooserDialog` during its waves.

## Steps
- [x] Settings (not shared): keep folder-TC button; add "Share this collection on the Bloom
      sharing server (experimental)" behind the experimental flag + feature gate, disabled
      state explains gating.
- [ ] Cloud create dialog: sign-in step (inline; in dev auth mode this is a plain
      email/password form driven by `sharing/loginState`'s reported mode — the real
      BloomLibrary browser flow slots in later), immutable-name acknowledgement, initial Send
      progress; no folder chooser, no Dropbox checkboxes, no restart.
- [x] SharingPanel (cloud TCs): approved-emails list (avatar, name-when-claimed, email, role
      chip, claimed/pending), add-with-role, remove (warns: force-unlocks their checkouts),
      change role; last-admin protections; member read-only view. Folder TCs keep old panel.
- [ ] Collection chooser: "Get my Team Collections" (signed-out state included); pull-down join
      via the six-scenario dialog (new states: NotSignedIn, ApprovalRemoved).
- [ ] Registration dialog: email unlock for cloud TCs (identity = account).
- [ ] All strings via XLF (DistFiles/localization/en only), Send/Receive terminology.

## Acceptance
- vitest browser-mode component tests: SharingPanel CRUD/pending/last-admin/read-only;
  chooser listing + signed-out; create dialog gating and flow.
- `yarn lint` clean. (Never run `yarn build`.)

**Agent notes**: Sonnet. Emotion `css` prop styling; arrow-function components; no prop
destructuring — follow src/BloomBrowserUI/AGENTS.md.

## Progress log
- 2026-07-06 · done: `sharingApi.ts` (shells for the Wave-3 SharingApi endpoints) and
  `SharingPanel.tsx`/`SharingMembersList` (approved-accounts CRUD, claimed/pending, last-admin
  protection, member read-only view) with `SharingPanel.test.tsx` (7 tests, all green); also
  fixed a pre-existing vitest gap (`processSimpleMarkdown` missing from the
  `localizationManager` mock, which crashed any test rendering `Div`/`BloomButton`/etc.) ·
  next: wire the "Share this collection..." experimental-gated entry point into
  TeamCollectionSettingsPanel.tsx (step 1).
- 2026-07-06 · done: resumed session; re-verified WIP files are prettier-clean (no changes)
  and `SharingPanel.test.tsx` (7 tests) still passes — `yarn vitest run` in default `forks`
  pool hit "Timeout starting forks runner" on this machine, so re-ran with `--pool=threads`,
  which passed cleanly in 37.8s; will use `--pool=threads` for the rest of this session.
- 2026-07-06 · done: step 1 (Settings). Added
  `useIsCloudTeamCollectionsExperimentalFeatureEnabled()` to `sharingApi.ts` (reads
  `app/enabledExperimentalFeatures`, checks for the `cloud-team-collections` token — matches
  `ExperimentalFeatures.kCloudTeamCollections` in C#) and a new "Share this collection on the
  Bloom sharing server (experimental)" `BloomButton` in `TeamCollectionSettingsPanel.tsx`'s
  not-yet-a-TC branch, disabled until the experimental flag is on, with
  `l10nTipEnglishDisabled` explaining the gate; posts
  `teamCollection/showCreateCloudTeamCollectionDialog` (new Wave-3 endpoint) when enabled.
  Also added `createCloudTeamCollection()` to `sharingApi.ts` for the next step · next: cloud
  create dialog (sign-in → immutable-name ack → initial Send progress) in
  `CreateTeamCollection.tsx` (step 2).
