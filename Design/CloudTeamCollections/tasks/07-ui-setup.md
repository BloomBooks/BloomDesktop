# 07 — UI: setup, settings, sharing panel, chooser (Wave 1 shells → Wave 3 wiring)

**Goal**: the create/share/join surfaces, Notion-simple.

**Dependencies**: shells against mocked endpoints in Wave 1; real wiring after 06.
Owns new `src/BloomBrowserUI/teamCollection/SharingPanel.tsx`,
`JoinCloudCollectionDialog.tsx`; **exclusive owner of** `CreateTeamCollection.tsx`,
`TeamCollectionSettingsPanel.tsx`, `CollectionChooserDialog` during its waves.

## Steps
- [ ] Settings (not shared): keep folder-TC button; add "Share this collection on the Bloom
      sharing server (experimental)" behind the experimental flag + feature gate, disabled
      state explains gating.
- [ ] Cloud create dialog: sign-in step (inline), immutable-name acknowledgement, initial Send
      progress; no folder chooser, no Dropbox checkboxes, no restart.
- [ ] SharingPanel (cloud TCs): approved-emails list (avatar, name-when-claimed, email, role
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
