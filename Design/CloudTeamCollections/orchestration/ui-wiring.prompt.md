# Agent prompt — Wave-3 UI wiring (resume-aware)

You are implementing the final Wave-3 step: connecting the Wave-1/2 UI shells to the real
C# endpoints task 06 just delivered. Work in an isolated git worktree of
c:\github\BloomDesktop.

**Resume check (do this FIRST):** if branch `task/ui-wiring` exists, check it out and
continue from the `## Progress log` at the bottom of this prompt's companion notes in the
task files below. Otherwise `git checkout -b task/ui-wiring`.

**Durability protocol (mandatory):** commit after EVERY completed item; messages ending
"Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"; keep a running `## Progress log`
(one line per item: `date · done · exact next action`) at the bottom of
`Design/CloudTeamCollections/tasks/07-ui-setup.md` (the wiring completes that task's
deferred items). Pre-commit hook fails in worktrees: `yarn prettier --write` your files,
then `--no-verify`.

**Anti-hang rules:** vitest single-run only; never `yarn build`; no dev servers.

**Setup:** ` cd src/BloomBrowserUI && yarn install` (leading space; yarn only). Follow
src/BloomBrowserUI/AGENTS.md conventions and the gating rule: all cloud UI stays behind
the experimental feature / capability flags; folder-TC UI byte-identical.

**Read first:** the "Still needed for UI wiring" section of task 06's final progress-log
entry in `Design/CloudTeamCollections/tasks/06-api-endpoints.md`; the Wave-3 deferrals in
07's and 08's progress logs; `src/BloomBrowserUI/teamCollection/sharingApi.ts` and
`teamCollectionApi.tsx` (the hooks now have REAL endpoints behind them).

**Work items:**
1. Fix the `WireUpForWinforms` double-registration in `CreateTeamCollection.tsx` (task 06
   found the cloud dialog's registration unconditionally overwrites the folder one, so the
   folder create dialog can no longer open). Both dialogs must coexist — e.g. select the
   component from a URL param/window flag the C# side already passes, or split bundles.
   This is the one item that BREAKS FOLDER TC today: fix it first and prove it with a test.
2. Dedicated sign-in dialog: a small email/password dialog for dev auth mode (per
   `sharing/loginState`'s mode field), opened by `sharing/showSignIn` instead of the
   create-dialog placeholder. Real-mode shows a "not yet available" message. Wire the C#
   side's existing endpoint; component test for both modes.
3. Wire `SharingPanel` into `TeamCollectionSettingsPanel`'s isTeamCollection branch for
   cloud TCs (07's deferred item) — folder TCs keep the old admin panel.
4. Wire `JoinCloudCollectionDialog`'s state matching into `CollectionChooser`'s
   `onPullDown` (07's deferred item) using `collections/pullDown`'s real responses.
5. Sweep `sharingApi.ts`/`teamCollectionApi.tsx` for any lingering mock-only defaults that
   would mask real endpoint failures now that the endpoints exist (fail fast per
   AGENTS.md).
6. Run the full component sweep (`yarn vitest run teamCollection collectionsTab
   collection react_components/TopBar`) + `yarn lint`; all green, no new warnings.

**Localization:** new strings per `.github/skills/xlf-strings/SKILL.md`, en-only.

**Final report (raw data):** branch + shas; per-item status; test sweep verbatim counts;
lint result; anything still blocking the two-instance manual smoke (the Wave-3 gate).
