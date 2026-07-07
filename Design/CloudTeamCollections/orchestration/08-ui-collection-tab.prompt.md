# Agent prompt — task 08: UI collection tab (Wave-2 shells) — resume-aware

You are implementing the Wave-2 (shells) scope of task 08 of the Cloud Team Collections
plan in an isolated git worktree of c:\github\BloomDesktop.

**Resume check (do this FIRST):** if branch `task/08-ui-collection-tab` exists, check it
out and continue from the `## Progress log` at the bottom of
`Design/CloudTeamCollections/tasks/08-ui-collection-tab.md`. Otherwise
`git checkout -b task/08-ui-collection-tab`.

**Durability protocol (mandatory, from orchestration/RESUME.md):** commit after EVERY
completed step — small coherent commits, messages ending
"Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>". Same commit: tick the step's
checkbox in the task file AND update its `## Progress log` with
`date · done · exact next action`. Interruptions are EXPECTED; only commits survive. The
pre-commit hook fails in worktrees (husky-run): run `yarn prettier --write` on your changed
files manually, then commit `--no-verify`.

**Anti-hang rules:** vitest in single-run mode ONLY (`yarn vitest run ...`, never watch);
never `yarn build`; no dev servers; timeouts on anything that might block.

**Setup:** front-end lives in src/BloomBrowserUI. First run ` cd src/BloomBrowserUI &&
yarn install` (yarn 1.22, NEVER npm; note the leading space — the terminal drops first
characters). Follow src/BloomBrowserUI/AGENTS.md: arrow-function components, no prop
destructuring, @emotion/react `css` prop, no sx objects.

**Read first:** `Design/CloudTeamCollections/tasks/08-ui-collection-tab.md` (authoritative
steps — Wave-2 scope is SHELLS AGAINST MOCKED ENDPOINTS; real wiring waits for task 06),
CONTRACTS.md §Book-status JSON (StatusPanelState additions must stay in sync with the C#
additive fields: localVersionSeq/repoVersionSeq/signedIn/capability flags), the design
doc §UI changes, and merged task-07 patterns in src/BloomBrowserUI/teamCollection/
(sharingApi.ts hooks; gating style).

**GATING IS NON-NEGOTIABLE (a task-07 review caught an ungated section):** every visible
cloud element added to EXISTING components (TeamCollectionButton, TeamCollectionDialog,
TeamCollectionBookStatusPanel, statusPanelCommon, CollectionHistoryTable) must be behind
the cloud-team-collections experimental feature / backend capability flags so folder-TC
UI stays byte-identical with the flag off. Branch on capability, never concrete type.

**Localization:** every new user-visible string follows
`.github/skills/xlf-strings/SKILL.md` (read it); only edit `DistFiles/localization/en/`.

**Final report (raw data):** branch + shas; component list with test status + verbatim
counts; `yarn lint` result; XLF ids added; gating approach per component; exact next
action if unfinished.
