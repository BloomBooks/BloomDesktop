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
- [x] Cloud create dialog: sign-in step (inline; in dev auth mode this is a plain
      email/password form driven by `sharing/loginState`'s reported mode — the real
      BloomLibrary browser flow slots in later), immutable-name acknowledgement, initial Send
      progress; no folder chooser, no Dropbox checkboxes, no restart.
- [x] SharingPanel (cloud TCs): approved-emails list (avatar, name-when-claimed, email, role
      chip, claimed/pending), add-with-role, remove (warns: force-unlocks their checkouts),
      change role; last-admin protections; member read-only view. Folder TCs keep old panel.
- [x] Collection chooser: "Get my Team Collections" (signed-out state included); pull-down join
      via the six-scenario dialog (new states: NotSignedIn, ApprovalRemoved).
- [x] Registration dialog: email unlock for cloud TCs (identity = account).
- [x] All strings via XLF (DistFiles/localization/en only), Send/Receive terminology.

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
- 2026-07-06 · done: step 2 (Cloud create dialog). Added `CreateCloudTeamCollectionBody`
  (presentational, four states derived from props: sign-in [dev-mode email/password form or a
  cloud-mode "Sign in with your Bloom account" placeholder button per `loginState.mode`] →
  immutable-name-acknowledgement checkbox gating the Share button → sending [LinearProgress] →
  done/error) and `CreateCloudTeamCollectionDialog` (container wiring it to
  `useSharingLoginState`/`signIn`/`createCloudTeamCollection` from `sharingApi.ts` and the
  BloomDialog frame; no folder chooser, no Dropbox checkboxes; bottom button is Cancel until
  done, then Close — no restart) in `CreateTeamCollection.tsx`. Fixed `createCloudTeamCollection`
  to use `postJson` instead of `post` (the latter is fire-and-forget and returns no promise —
  would have crashed the `.then()` chain). Added `CreateCloudTeamCollection.test.tsx` (10
  tests, all green, same raw-DOM-render pattern as `SharingPanel.test.tsx`) covering sign-in
  gating (dev vs cloud mode), email/password field wiring, name-ack gating of the Share button,
  sending/error/done states. `yarn eslint` on all touched files: 0 errors, pre-existing warnings
  only (unrelated lines in the original `CreateTeamCollectionDialog`) · next: Collection chooser
  "Get my Team Collections" (signed-in listing + signed-out state) in `CollectionChooser.tsx`
  (step 3), then `JoinCloudCollectionDialog.tsx` (new, six-scenario + NotSignedIn +
  ApprovalRemoved) for pull-down join.
- 2026-07-06 · done (partial step 3 — listing half; pull-down-join dialog still to do): added
  `MyCloudCollectionsSection.tsx` (new; presentational "Get my Team Collections" sidebar of the
  collection chooser: signed-out sign-in prompt, loading, empty state, and a listing with a
  per-row pull-down button) and wired it into `CollectionChooser.tsx` alongside the existing
  recent-collections grid, backed by `useSharingLoginState`/`useMyCloudCollections`/
  `pullDownCollection` from `sharingApi.ts`. Added `MyCloudCollectionsSection.test.tsx` (4
  tests, all green) covering signed-out/loading/empty/listing+pull-down-click. Found and fixed
  the same "l10n `Div`/`P`/`Span` don't forward `data-testid`" trap as elsewhere in this task —
  wrapped in a plain `<div data-testid=...>` for the loading/empty states. `yarn eslint` clean
  on all touched files · next: `JoinCloudCollectionDialog.tsx` (new) — the pull-down-join dialog
  with the six folder-TC scenarios adapted to cloud collections plus NotSignedIn and
  ApprovalRemoved (8 states total), to complete step 3; `onPullDown` in `CollectionChooser.tsx`
  will eventually need to open it once it exists (Wave-1 shell can leave the direct
  `pullDownCollection` call as the placeholder action for now).
- 2026-07-06 · done (step 3 complete): added `JoinCloudCollectionDialog.tsx` (new) — the
  pull-down-join dialog, structurally mirroring the folder-TC `JoinTeamCollectionDialog.tsx`'s
  six scenarios (CreateNewCollection, MatchesExistingNonTeamCollection,
  MatchesExistingTeamCollection[Elsewhere], MatchesDifferentTeamCollection,
  IncompleteLocalCopy — renamed from IncompleteTeamCollection since the cloud failure mode is a
  corrupt local pull-down cache, not a missing ".txt" file) plus two new states unique to cloud
  collections: NotSignedIn (action button becomes "Sign In", posts `sharing/showSignIn`) and
  ApprovalRemoved (action button disabled, explains the user isn't on the approved list). The
  join/pull-down action calls `pullDownCollection` from `sharingApi.ts`. Added
  `JoinCloudCollectionDialog.test.tsx` (8 tests, all green) covering all eight states; had to
  query `document.body` rather than the local render container since MUI's `Dialog` portals its
  content, and to assert on l10n *keys* rather than English text because the test-only
  `localizationManager` mock resolves every key to itself (same trap noted in
  `SharingPanel.test.tsx`). Not wired into `CollectionChooser.tsx`'s `onPullDown` yet — knowing
  which of the eight states applies requires the six-scenario matching logic that task
  05-cloud-backend.md says lives server/backend-side and doesn't exist until later; the chooser
  keeps calling `pullDownCollection` directly for now (Wave-1 shell). Similarly, `SharingPanel`
  is not wired into `TeamCollectionSettingsPanel.tsx`'s `isTeamCollection` branch yet — that
  needs a "is this a cloud TC" signal that isn't available until the backend capability flags
  land; both are complete, tested, standalone shells ready for that wiring. `yarn eslint`
  clean on all touched files · next: Registration dialog email unlock for cloud TCs (step 4),
  then XLF strings for everything added in this task (step 5, follow
  `.github/skills/xlf-strings/SKILL.md`, only `DistFiles/localization/en/`).
- 2026-07-06 · done: step 4 (Registration dialog). Added `cloudAccountEmail?: string` to
  `IRegistrationContentsProps` (registrationTypes.ts) and `IRegistrationDialogProps`
  (registrationDialogLauncher.tsx), threaded through `RegistrationDialog` and both launchers
  (`RegistrationDialogLauncher`, `RegistrationDialogEventLauncher`). In
  `registrationContents.tsx`, when `cloudAccountEmail` is set: the email field is force-synced
  to it and always locked (`mayChangeEmail` becomes `false` regardless of the prop — identity is
  the signed-in account, not a free-text field), with a distinct label
  ("RegisterDialog.CloudAccountEmail") and an explanatory note
  ("RegisterDialog.CloudAccountEmailNote") instead of the folder-TC "Check in to change email"
  message (which means something different — "already registered" rather than
  "tied to your account"). Wired this into `CreateCloudTeamCollectionDialog` in
  `CreateTeamCollection.tsx`: `startSend` now checks `registration/userInfo` first (same pattern
  as the folder dialog's `tryToCreate`) and shows the registration dialog with
  `cloudAccountEmail: loginState.email` if not yet registered, only calling the actual
  `createCloudTeamCollection()` (renamed the old body to `doSend`) once registered. No new test
  file added (no pre-existing vitest coverage for the registration components in this repo —
  only Playwright `.uitest.ts` files, which are excluded from the vitest run and out of scope
  here); re-ran the full task test suite (29 tests across
  SharingPanel/CreateCloudTeamCollection/MyCloudCollectionsSection/JoinCloudCollectionDialog) to
  confirm no regressions. `yarn eslint` clean on all touched files · next: step 5 — XLF strings
  for every new user-visible string added across this whole task (follow
  `.github/skills/xlf-strings/SKILL.md`; only `DistFiles/localization/en/`). This is the last
  step before final report.
- 2026-07-07 · done: step 5 (XLF strings) was already completed in the prior WIP commit
  (`8c82b4b37`) but not yet logged/verified — closing out the task. Confirmed every new
  user-facing string across `CollectionChooser`/`MyCloudCollectionsSection`, `SharingPanel`,
  `CreateTeamCollection`'s cloud dialog, `JoinCloudCollectionDialog`, and the registration
  cloud-account additions goes through `l10nKey`/`Div`/`P`/`Span`/`BloomButton`/
  `AttentionTextField` (no hardcoded UI text), and that every one of those keys has a matching
  `translate="no"` entry with an `ID:` note in `DistFiles/localization/en/Bloom.xlf` or
  `BloomMediumPriority.xlf` (priority split follows the SKILL.md table: primary actions/labels
  in `Bloom.xlf`, secondary/help/error text in `BloomMediumPriority.xlf`), each with a second
  translator-context note wherever the string is short/generic, a fragment, or contains a
  placeholder or product name — including the auto-derived
  `TeamCollection.Sharing.ShareOnCloudServer.ToolTipWhenDisabled` key consumed via
  `l10nTipEnglishDisabled` on the Settings button. No existing entries were modified. Only
  `DistFiles/localization/en/` was touched. Terminology check: the initial collection upload
  uses "Send"/"Sending" (matching folder-TC Send/Receive language), and the join/pull-down flow
  uses "Get"/"download" — consistent with the existing folder-TC `JoinTeamCollectionDialog`'s
  own wording, so no new/conflicting terminology was introduced.
- 2026-07-07 · done: final wrap-up for task 07. Ran `yarn prettier --write` on every file this
  branch touched (`git diff --name-only 087e9a725..HEAD`); all 16 already matched (the two WIP
  commits that skipped the pre-commit hook turned out to be prettier-clean already, so no
  reformatting was needed). Re-ran the full component-test suite in single-run mode:
  `yarn vitest run --pool=threads teamCollection/SharingPanel.test.tsx
  teamCollection/CreateCloudTeamCollection.test.tsx
  collection/MyCloudCollectionsSection.test.tsx teamCollection/JoinCloudCollectionDialog.test.tsx`
  → 4 test files, 29 tests, all passing. Ran `yarn lint` across the whole project: 0 errors,
  777 warnings, none of them on lines this branch added or in files newly created by this
  branch (checked `CreateTeamCollection.tsx`'s two reported warnings and `vitest.setup.ts`'s
  one — both sit on pre-existing lines outside this branch's diff hunks). All five task steps
  are now checked off. Task 07 (Wave-1 shells) is complete and ready for Wave-3 wiring per the
  "next" notes left in each shell (SharingPanel into `TeamCollectionSettingsPanel`'s
  `isTeamCollection` branch; `JoinCloudCollectionDialog`'s eight-state matching logic into
  `CollectionChooser`'s `onPullDown`) once the relevant backend capability flags/matching logic
  from tasks 05/06 land — see the note at the end of the 2026-07-06 step-3 entry above.
