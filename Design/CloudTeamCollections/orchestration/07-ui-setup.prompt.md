# Agent prompt — task 07: UI setup/sharing shells (resume-aware)

You are implementing the Wave-1 (shells) scope of task 07 of the Cloud Team Collections
plan in an isolated git worktree of c:\github\BloomDesktop.

**Resume check (do this FIRST):** if branch `task/07-ui-setup` exists, check it out and
continue from the `## Progress log` at the bottom of
`Design/CloudTeamCollections/tasks/07-ui-setup.md`. Otherwise
`git checkout -b task/07-ui-setup`.

**Durability protocol (mandatory, from orchestration/RESUME.md):** commit after EVERY
completed step — small coherent commits, descriptive messages ending
"Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>". In the same commit: tick that
step's checkbox in the task file AND update its `## Progress log` (create if missing)
with `date · done · exact next action`. Never leave >1 step uncommitted.
NOTE: the pre-commit hook may fail in a worktree ("husky-run not found"); if so, run
`yarn prettier --write` on your changed files manually, then commit `--no-verify`
(orchestrator re-verifies at merge).

**Setup:** front-end lives in src/BloomBrowserUI. First run ` cd src/BloomBrowserUI &&
yarn install` (yarn 1.22, NEVER npm; note the leading space guard for the terminal's
lost-first-character quirk). NEVER run `yarn build`. Verify with `yarn lint` and vitest
(component tests). Follow src/BloomBrowserUI/AGENTS.md: arrow-function components, no
prop destructuring, @emotion/react `css` prop styling, no sx objects.

**Read first:** `Design/CloudTeamCollections/tasks/07-ui-setup.md` (authoritative steps —
Wave-1 scope is SHELLS AGAINST MOCKED ENDPOINTS; real wiring waits for task 06),
`Design/CloudTeamCollections.md` §UI changes, CONTRACTS.md §Book-status JSON. In dev auth
mode the sign-in step is a plain email/password form driven by `sharing/loginState`'s
reported mode (mock that endpoint's both modes).

**Localization:** every new user-visible string follows
`.github/skills/xlf-strings/SKILL.md` (READ IT before adding strings). Only ever edit
under `DistFiles/localization/en/` — never other language dirs.

**Scope:** SharingPanel.tsx, JoinCloudCollectionDialog.tsx (new); CreateTeamCollection.tsx,
TeamCollectionSettingsPanel.tsx, CollectionChooserDialog (exclusive owner during this
task). Keep folder-TC behavior byte-identical; cloud paths behind the experimental flag.

**Final report (raw data):** branch + shas; component list with test status; `yarn lint`
result; strings added (XLF ids); the exact next action if you did not finish.
