# Agent prompt — task 10: adoption path + polish (resume-aware)

You are implementing task 10 plus the Wave-3 polish list in an isolated git worktree of
c:\github\BloomDesktop.

**Resume check (do this FIRST):** if branch `task/10-adoption` exists, check it out and
continue from the `## Progress log` at the bottom of
`Design/CloudTeamCollections/tasks/10-adoption.md`. Otherwise
`git checkout -b task/10-adoption`.

**Durability protocol (mandatory):** commit after EVERY completed item, messages ending
"Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"; tick checkboxes + progress log
in the same commit. Worktree hook fails: `yarn prettier --write` your tsx/ts files, then
`--no-verify`. For front-end tests: ` cd src/BloomBrowserUI && yarn install` first
(leading space; yarn only; NEVER yarn build; vitest single-run only). C# can be AUTHORED
here but not fully test-run (no build deps in worktrees) — write it carefully, note
"authored, orchestrator verifies at merge" in the progress log, and keep C# changes in
small isolated commits.

**Read first:** `Design/CloudTeamCollections/tasks/10-adoption.md` (authoritative);
IMPLEMENTATION.md's Wave-3 merge-log entry (the polish items below come from it);
`.github/skills/xlf-strings/SKILL.md` (all strings en-only).

**Work items, in order:**
1. **Proper experimental-feature checkbox** (owed since the smoke test's user.config
   hack): add "Cloud Team Collections (experimental)" to Settings → Advanced, wired like
   the existing allowTeamCollection option end to end — GetAdvancedSettingsData /
   StoreAdvancedSettingsData / CollectionSettingsDialog pending-change + restart plumbing
   in `CollectionSettingsApi.cs`, checkbox in `AdvancedSettingsPanel.tsx`, token
   `ExperimentalFeatures.kCloudTeamCollections`. Component test for the tsx side.
2. **Pull-down auto-open**: `collections/pullDown` (SharingApi.cs) returns the local
   collection folder path from CloudJoinFlow; the chooser/join dialog uses it to invoke
   the same open-collection action the chooser's cards use, instead of making the user
   hunt for the new collection. Component test.
3. **Un-team cleanup** (task file step 1): enabling cloud on a formerly-folder-TC
   collection cleans per-book `TeamCollection.status`, `lastCollectionFileSyncData.txt`,
   `log.txt`; simultaneous folder-link + cloud-link in TeamCollectionLink.txt territory =
   clear error. Unit-testable C# — author tests alongside.
4. **First-Receive reconcile** (task file step 2): verify-by-reading that members' existing
   local copies reconcile by checksum on first Receive (CloudJoinFlow's scenario logic);
   document findings in the progress log; fix only if trivially wrong.
5. **User documentation**: author `Design/CloudTeamCollections/docs/user-walkthrough.md` —
   the un-team → enable cloud → invite team walkthrough incl. "everyone check in first",
   written for end users (the docs-site source of truth until it moves).
6. **Localization sweep**: audit ALL new user-visible strings across the cloud UI work
   (07/08/wiring/your items) per the xlf-strings skill; fix gaps; en-only.
7. **Analytics audit**: verify create/join/send/receive/force-unlock/incident events carry
   Backend="Cloud" and sensible params (read TeamCollectionApi/SharingApi Analytics.Track
   calls); add missing ones; note bytes-uploaded-vs-skipped as future enhancement if not
   cheaply available.

NOT in scope: the preview-pane refresh nit (needs base-code selection plumbing — the
orchestrator will assess separately); dogfood (humans).

**Final report (raw data):** branch + shas; per-item status; test commands + verbatim
counts (front-end); which C# items need orchestrator build-verification; XLF ids
added/fixed; exact next action if unfinished.
