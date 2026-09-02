# Worker brief: automate Notion test case {{TEST_CASE_ID}}

You are one worker in an Orca orchestration run started by the
`improve-test-automation-coverage` skill. Your one job: turn the manual test case below into an
edge-to-edge test in `src/BloomE2E/`, get it reviewed by the controller, and open a draft PR.

- Notion card: {{CARD_URL}}
- Title: {{CARD_TITLE}}
- Test Case ID: {{TEST_CASE_ID}}
- Skill folder with the helper scripts (absolute path, in the controller's checkout):
  `{{SKILL_DIR}}`
- Your worktree branch was created from `master`. PRs target `master`. `AGENTS.md` says new
  work targets `Version6.5`; this task is the exception, because `src/BloomE2E` exists only on
  master. Do not retarget the branch or the PR.

The controller has already set the card's `Automation` property to `Building`. Do not set it
again.

## Ground rules

- **Follow `.github/skills/add-e2e-test/SKILL.md` in this worktree** as the authoritative
  procedure. Read `src/BloomE2E/README.md`, `src/BloomE2E/AUTOMATION-DEBT.md`, and the existing
  tests in `src/BloomE2E/tests/` before you write anything.
{{QUESTIONS_RULE}}
- **Never run the e2e suite directly.** Other worktrees run Bloom e2e tests on this machine at the
  same time, and concurrent Bloom instances collide. Every Playwright run goes through the lock:

  ```powershell
  Set-Location <this worktree>\src\BloomE2E
  node {{SKILL_DIR}}/e2e-lock.mjs -- pnpm exec playwright test tests/<your-file>.spec.ts
  ```

  The lock waits for the other run to finish, then runs yours. Do not kill a Bloom.exe you did
  not start.
- Keep the Orca card current: `orca worktree set --worktree active --comment "<state>" --json`
  after each checkpoint (feasibility decided, test written, test green, review round, PR open).
- Build Bloom whenever it helps, as the add-e2e-test skill's build section says: nobody runs
  Bloom from your worktree, so nothing collides. Never commit or push except inside the
  `preflight` skill in step 6. That step, not the skill's own "Ship" section, is how your PR
  gets opened; the controller owns the PR's state, so leave it a draft.

## Steps

### 0. Finish the worktree setup yourself

Orca's setup hook for this worktree has failed or will fail: it runs `./init.sh` under
`cmd.exe` with `NoDefaultCurrentDirectoryInExePath=1` inherited from Claude Code, which
prints `'.' is not recognized`. The repo is fine. If your branch is behind `origin/master`,
fast-forward it first. Then do the setup yourself, in this order:

1. From the Bash tool, in this worktree, run `./init.sh`. It fetches the C# dependencies and
   installs and builds the front-end. It does **not** touch the e2e package or the test inputs,
   so steps 2 to 4 are yours as well.
2. `pnpm install` in `src/BloomE2E` (its own pnpm package), then confirm that
   `src/BloomE2E/node_modules` exists.
3. `node build/get-testing-inputs.mjs`, then confirm that `output/testing-inputs` exists.
4. Build `Bloom.exe`, which the e2e suite launches from `output/Debug/`, with
   `dotnet build src/BloomExe/BloomExe.csproj`. Use plain `dotnet` here, not the
   `build/agent-dotnet.ps1` wrapper, because the wrapper builds no `Bloom.exe`. No Bloom runs
   from this worktree, so nothing locks the output.

### 1. Read the card

```powershell
$env:BLOOM_TESTCASE_NOTION = [Environment]::GetEnvironmentVariable('BLOOM_TESTCASE_NOTION','User')
py {{SKILL_DIR}}/notion_automation.py show {{TEST_CASE_ID}}
```

The `testSteps` array is the card body, block by block: headings, bullets, and `to_do` items.
Together with `summary` it is the behavior contract. Some cards write every step as a bullet and
have no `to_do` items; treat those bullets as the steps. Read `automationNotes` too.

### 2. Decide feasibility

Decide whether the whole test, or a meaningful part, can be automated under the add-e2e-test
rules: no native OS dialogs, no WinForms surfaces without an `E2eTestingApi` hook, real UI for
the behavior under test, state-based waits. Look for the UI in the React source and confirm
selectors or the `data-testid` you will add. If a small Bloom change (a `data-testid`, an
`E2eTestingApi` hook) makes it feasible, that change is part of your PR.

{{UNDECIDED_RULE}}

If the case is **not feasible as written**, or the developer tells you the card is not ready, flag it for
the developer who wrote the card and stop. The note must tell that developer what to change in
the card, or what Bloom lacks, and list every question you would have asked:

```powershell
py {{SKILL_DIR}}/notion_automation.py set {{TEST_CASE_ID}} "Has automation problems" --note "[improve-test-automation-coverage {{TODAY}}] <which step blocks automation and why; what the card or Bloom needs before an agent can try again>"
orca orchestration send --type worker_done --subject "HAS AUTOMATION PROBLEMS: <reason in a few words>" --body "<the same reason, plus what card or Bloom change would make it feasible>" --task-id <task_id> --dispatch-id <dispatch_id> --outcome succeeded --json
```

Use the task id and dispatch id from the dispatch preamble at the top of your prompt.

### 3. Implement the test

Follow the add-e2e-test skill. Put `[Test Case ID {{TEST_CASE_ID}}]` in the test title. Prefer
`collectionSpec` (the test builds its own collection). If you cannot implement each step, that is a problem. If a substantial portion can be automated and there is a clean split, then the notion test must be split into manual vs. automated. Otherwise,  you can just fail the implementation of this test and set the Automation property to "Has automation problems".

### 4. Run it through the lock, three times

Run the file three times in a row through `e2e-lock.mjs`. Investigate any failure; a test that
passes 2 of 3 is not done. Confirm no `Bloom.exe` you started survives the run.

Also run `pnpm typecheck` in `src/BloomE2E`.

### 5. Ask the controller for review

Send a blocking question to the coordinator and wait for the answer:

```powershell
orca orchestration ask --question "READY FOR REVIEW {{TEST_CASE_ID}}: <files changed>; <steps covered / not covered>; <3 lock runs green>" --options "ship,fixes" --timeout-ms 3600000 --json
```

The controller reads your worktree, then replies either `ship` or a list of fixes. Apply the
fixes, re-run step 4, and ask again. Repeat until the answer is `ship`. If the ask times out,
resume it with `orca orchestration ask --resume <message_id> --timeout-ms 3600000 --json`; do not
proceed without a `ship`.

### 6. Ship (this replaces the "Ship" section of add-e2e-test)

1. Run the `preflight` skill (`Skill` tool, name `preflight`). It commits, pushes, opens a draft
   PR against `master`, and waits for the bots. Preflight looks for a `BL-` ticket in the branch
   name; there is none, which is a normal outcome for this work. When it writes the PR
   description, make sure the body contains this line, so the PR points at the Notion card:

   ```
   Automates Notion test case {{TEST_CASE_ID}} ({{CARD_TITLE}}): {{CARD_URL}}
   ```

   Also say which Test Steps the test covers and which it does not, and give any change
   outside `src/BloomE2E` its own **Bloom production code changes** heading, as the add-e2e-test
   summary does.
2. Set the card to `PR Pending` with the PR URL in the note. If the test covers only part of the
   steps, say which part in the same note:

   ```powershell
   py {{SKILL_DIR}}/notion_automation.py set {{TEST_CASE_ID}} "PR Pending" --note "[improve-test-automation-coverage {{TODAY}}] PR: <pr url>. Covers steps: <which>. Not covered: <which, or 'none'>."
   ```

3. Move the Orca card to Personal Review, never Peer Review:
   `orca worktree set --worktree active --workspace-status status-7 --json`
4. Report once and stop:

   ```powershell
   orca orchestration send --type worker_done --subject "PR open: <pr url>" --body "<what the test covers, what it does not, any AUTOMATION-DEBT entry, preflight decisions that need the developer>" --task-id <task_id> --dispatch-id <dispatch_id> --outcome succeeded --files-modified "<comma-separated paths>" --json
   ```

If the environment blocks you for good (build broken, Bloom will not launch, preflight cannot
open a PR), the card itself is fine: set it back to `Planned` with a
`[improve-test-automation-coverage {{TODAY}}] Blocked:` note, then send `worker_done` with
`--outcome failed` and the reason. Use `Has automation problems` only when the card or Bloom is
what stands in the way.
