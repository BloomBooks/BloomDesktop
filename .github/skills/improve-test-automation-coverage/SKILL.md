---
name: improve-test-automation-coverage
description: '"improve-test-automation-coverage N" — take N test cases whose Notion Automation property is Planned, claim each one (set it to Building at once), and automate each in its own Orca worktree with a supervised Claude Fable 5.1 worker that follows add-e2e-test. The controller reviews every worker''s test, sends fixes, then lets the worker run preflight and open a draft PR that links the Notion card; the card ends as "PR Pending". Use when the developer says "improve test automation coverage", "burn tokens on test automation", or "automate N planned tests".'
argument-hint: "N — how many Planned test cases to automate in parallel (default 3); or 'case 349,350' to name the cards"
user-invocable: true
---

# Improve test automation coverage

Turn N manual test cases into edge-to-edge tests, in parallel, with one Orca worktree and one
Claude Fable 5.1 worker per case. You are the **controller**: you pick and claim the cases, start
the workers, answer their review requests, and report. The workers write the tests and open the
PRs. The developer answers worker questions in Orca.

Files beside this skill:

- `notion_automation.py` — list, show, claim, and update cards. Run it with `py`.
- `e2e-lock.mjs` — the machine-wide lock every Playwright run goes through. Workers use it; you
  use it too if you ever run a test yourself.
- `worker-brief.md` — the task brief template for a worker.

Related skills: `.github/skills/add-e2e-test/SKILL.md` (what a worker follows), `orchestration`
and `orca-cli` (Orca mechanics; run `orca skills get orchestration` first, the guide is
version-matched), `preflight` (the worker runs it to open the PR), `personal-board`.

## Authorization

Invoking this skill authorizes, for this run only: setting the `Automation` and
`Automation Notes` properties of Notion test cards; creating Orca worktrees and workers; and,
inside each worker, everything `preflight` authorizes (commit, push, draft PR, bot replies). Still
forbidden: marking a PR ready for review, moving any Orca card to Peer Review, setting a card to
`Automated` or `Keep manual` (a human does both, after merge or after judging the case).

## Constants

- Notion database "Test Case Runs", suite run `6.5` (the current release; change `--suite` when
  the team moves on). Token: `BLOOM_TESTCASE_NOTION`, Windows User scope. In PowerShell:
  `$env:BLOOM_TESTCASE_NOTION = [Environment]::GetEnvironmentVariable('BLOOM_TESTCASE_NOTION','User')`
- Base branch for every worktree and PR: `master`. The BloomE2E system lives on master; pass
  `--base-branch master` explicitly, do not rely on the Orca repo default. This is the
  exception `AGENTS.md` allows to its "new work targets Version6.5" rule: an e2e test cannot
  target a branch that has no `src/BloomE2E`.
- Worker agent: `claude`, model `claude-fable-5-1`.
- Default N: 3. N is a maximum; if fewer Planned cards exist, take what there is and say so.
- Worktree name: `tc<TestCaseID>-<one to three words from the title>`, for example
  `tc349-duplicate-page`. There is no YouTrack card for this work, so the `BL-` prefix rule does
  not apply; `preflight` finding no ticket id is the expected outcome.

## The Automation lifecycle this skill drives

`Planned` → `Building` (you, at claim time) → `PR Pending` (worker, when the draft PR exists,
with the PR URL in `Automation Notes`) → `Automated` (a human, after merge; a separate sweep of
`PR Pending` cards is planned). A worker whose test covers only part of a card's steps splits the
card when it sets `PR Pending`, as `add-e2e-test` describes: the original becomes the
`[Automated portion]` and keeps its id, the uncovered steps move to a new `[Manual portion]` row.
A card is never left half automated. A worker that finds a case not feasible as
written sets it to `Has automation problems` with a dated note that says what the card, or
Bloom, needs; that is the queue for the developer who wrote the card. The developer telling the worker
that the card is not ready counts as such a finding, as much as a technical block does. Such a card is out of
the candidate list until that developer fixes it and sets `Planned` again. A worker blocked by the
environment (broken build, no PR possible) sets the card back to `Planned` with a `Blocked:`
note instead, because the card itself is fine.

## Unattended mode

The developer is away and nobody answers questions. Two things change; everything else stays the same.

- **Workers never ask the developer.** The brief (built with `brief ... --unattended`) tells the worker
  to decide for itself. A card that leaves a question of intent open, or that needs a human
  judgment such as "is this too hard", goes to `Has automation problems` with the unanswered
  questions in the note, so the developer can answer them on the card later. A small ambiguity that a
  careful tester would resolve the same way gets the conservative reading, written down in the
  PR description and the `PR Pending` note.
- **You keep N workers busy until no `Planned` card is left to take.** When a worker settles without a
  PR (`HAS AUTOMATION PROBLEMS`, or a blocked card), release it, claim the next `Planned` card
  by the ordering rule in Step 1, and start a new worker for it, so that N workers run at any
  time. Stop claiming when `list-planned` returns nothing you may take, or when this run has
  started 3 × N workers, whichever comes first; the cap keeps a broken environment from
  burning through every card. A worker that opens a PR is a success and is not replaced beyond
  that cap either. Report at the end as usual, with one row per card tried.

In unattended mode the worker still asks you for review; you reply `ship` or `fixes` yourself.
`preflight` is autonomous already and batches its decisions into the PR report.

## Step 1 — Pick and claim the cases

**Claim first, spawn later.** Other developers run this skill on other machines. The window
between reading `Planned` and writing `Building` is the only race, so keep it short: claim each
card the moment you decide to take it, before you create anything else for it.

1. Resolve the argument. A bare number is N (default 3). `case <id>[,<id>...]` names the
   cards to take instead, for example `case 349` or `case 349,350`; then N is the count of ids
   and the ordering rule below does not apply, but the `Planned` check and the claim do. The
   word `unattended` anywhere in the argument selects unattended mode (see below); it
   combines with either form, for example `3 unattended` or `case 349 unattended`.
2. List candidates:
   ```powershell
   py <skill dir>\notion_automation.py list-planned
   ```
3. Skip a card whose `automationNotes` contains `[improve-test-automation-coverage` and
   `Blocked:` (an earlier run hit an environment problem). Show the developer those skips in the final
   report; do not retry them without being asked. Cards with automation problems are not in
   the `Planned` list at all.
4. Take the first N remaining cards, lowest `Test Case ID` first, and claim each one at once:
   ```powershell
   py <skill dir>\notion_automation.py claim <testCaseId>
   ```
   Exit code 3 means someone else got there first; take the next candidate instead. Keep going
   until you hold N claims or run out of candidates.
5. Only now read each claimed card (`show <testCaseId>`) so the brief can carry its title.

## Step 2 — Start one worker per claimed card

Confirm Orca is up (`orca status --json`) and read the version-matched guide
(`orca skills get orchestration`). Then:

```powershell
orca orchestration run-create --objective "improve-test-automation-coverage: automate Notion test cases <ids>" --json
```

For each claimed card:

1. Build the brief with the script, never with sed or a regex (a regex eats the backslashes in
   the skill path; the first run produced a brief with bell characters in it):
   ```powershell
   py <skill dir>\notion_automation.py brief <testCaseId> --out %LOCALAPPDATA%\Bloom\e2e-briefs\brief-<testCaseId>.md
   ```
   Add `--unattended` in unattended mode; it swaps the "ask the developer" rules for "decide yourself".
   Write the brief where the worker can read it and where it outlives your scratchpad: the folder
   above (the script creates it) or another durable folder such as
   `<orca repo path>\..\e2e-briefs\`. Do not paste the brief into the task spec: Orca types the spec into
   the worker's terminal, and a 140-line paste has arrived truncated and unsubmitted. The spec is
   two sentences:
   `Read the file <absolute brief path> and follow it exactly; it is your whole task. Report with worker_done as it says.`
2. Find the Orca repo that owns this checkout: `orca repo list --json`. For BloomDesktop on this
   machine that is `path:D:/bloom`. A worktree path such as `D:/automate-notion-test` is not a
   repo and `worker-start` answers `repo_not_found`.
3. Create the task and start the worker in a fresh top-level worktree off master:
   ```powershell
   orca orchestration task-create --spec "Read the file <absolute brief path> and follow it exactly; it is your whole task. Report with worker_done as it says." --json
   orca orchestration worker-start --task <task_id> --worktree new-top-level --name tc<id>-<words> --repo path:<orca repo path> --base-branch master --agent claude --model claude-fable-5-1 --setup run --json
   ```
   Read the receipt: `ready` with setup `running` is normal. A failed start exits nonzero; read
   its `stage` and `effects`, fix the cause, and start again with `--retry-of <dispatch_id>`.
   If the PowerShell tool refuses the command, run the same command through the Bash tool; the
   first run saw the auto-mode classifier deny it once in PowerShell and accept it in Bash.
   About a minute after the start, read the worker's terminal (`orca terminal read`) and confirm
   the agent is working on the brief, not sitting at an empty prompt or on an unsubmitted paste.
   If it sits, the spec never arrived: send it again with
   `orca terminal send --terminal <handle> --text "<the two-sentence spec>" --enter --json`.
   Do not leave a claimed card without a worker: if you cannot start one, set the card back to
   `Planned` with a `[improve-test-automation-coverage <date>] Blocked: <reason>` note.
4. Expect a `status` message `Setup failed for worker <dispatch>` soon after the start. The
   repo setup hook (`./init.sh`) runs under `cmd.exe` with `NoDefaultCurrentDirectoryInExePath=1`
   inherited from Claude Code and dies with `'.' is not recognized`. That is an agent-session
   artifact, not a repo fault (see the team rules). The brief tells the worker to run
   `./init.sh` itself, so you do nothing about this message except acknowledge it.
5. Record `task_id`, `dispatch_id`, worktree path, and Test Case ID in a table in your
   scratchpad. You will need all four for every later message.

Start all N workers before you wait on any of them.

## Step 3 — Supervise: answer review requests, relay nothing else

Workers ask the developer their own questions with `AskUserQuestion` in their terminals. You do not
relay those. You handle three message types:

```powershell
orca orchestration check --wait --types worker_done,escalation,question --timeout-ms 900000 --json
```

Loop until every dispatch has settled. After each batch, acknowledge with
`check --ack <delivery_id> --wait ...` and keep waiting; for the last acknowledgement, when no
more messages can come, `check --ack <delivery_id> --json` without `--wait` returns at once. A
wait that times out with nothing is normal; a worker can spend an hour in preflight. Do not stop
a worker for being slow. While it waits, `check --wait` prints one `_keepalive` line every 15
seconds; pipe the output through `Select-String -NotMatch '_keepalive'` or it floods your
context. `status` and `heartbeat` messages need no action beyond the acknowledgement.

### A `question` whose text starts with `READY FOR REVIEW <id>`

The worker has asked you to review its test, and is blocked until you reply.

1. Review the worktree at the path you recorded. Read the whole diff against `origin/master`,
   the new spec file, and any Bloom change (`data-testid`, `E2eTestingApi`). Check it against
   `add-e2e-test`: the title carries `[Test Case ID <id>]`; the test builds its own collection
   unless a fixture is justified; the behavior under test goes through the real UI, setup may use
   the API; waits are state-based; no native dialogs; helpers reused rather than re-implemented;
   the covered and uncovered Test Steps match what the worker says, and any uncovered step means
   the card was split into an automated and a manual portion; `AUTOMATION-DEBT.md` records
   anything the worker could not automate cleanly. Run the `code-review` skill on the worktree
   for a second opinion when the diff touches C# or the shared helpers.
2. If you want to see it run, run it yourself through the lock, from that worktree's
   `src/BloomE2E`:
   `node <skill dir>\e2e-lock.mjs -- pnpm exec playwright test tests/<file>.spec.ts`
3. Reply once:
   - Nothing to fix: `orca orchestration reply --id <message_id> --body "ship" --json`
   - Otherwise: `orca orchestration reply --id <message_id> --body "fixes: 1. ... 2. ..." --json`.
     Number the fixes; say what and why, not how. Expect a second `READY FOR REVIEW` afterwards.

Cap the loop at three review rounds per worker. If the third round still needs fixes, reply
`ship` with the remaining points listed for the PR description, and put them in your report.

### Any other `question`

A worker should ask the developer, not you, about the product; if one asks you anyway, answer from what
you know, or tell it to use `AskUserQuestion`. Never invent an answer about intent.

### `escalation` or `worker_done`

Read the body. An `escalation` is a report, not an ending: answer it or act on it, and leave the
worker running. Only a `worker_done` settles the dispatch.

For each dispatch a `worker_done` settled, release the worker:
`orca orchestration worker-release --dispatch <dispatch_id> --json`. A result of `retained`
with reason `user_takeover` means the developer typed in that worker's terminal; Orca keeps the terminal
open and there is nothing more to do. Confirm the Notion card is
in the state the outcome implies (`PR Pending` with a PR URL; `Has automation problems` with a
dated note; or `Planned` with a `Blocked:` note) and fix it with `notion_automation.py set` if the worker forgot. Do not delete the worktree: the
PR lives on that branch.

## Resuming a stalled run

A run stalls when the controller or a worker stops for a reason outside the work: a Claude
usage limit, a machine sleep, an Orca restart. Symptoms: `check` shows an `escalation`
"Agent exited unexpectedly", or heartbeats "rejected ... capability is revoked", and the cards
stay `Building`. A new controller can take the run over:

1. `orca orchestration worker-list --json` filtered on the run id gives every dispatch, its
   task, and its worktree. `git -C <worktree> status --short` shows what the dead worker left.
   Nothing is lost: the work is uncommitted in the worktree.
2. For each task whose dispatch is `failed` or `abandoned`, start a replacement in the SAME
   worktree: `worker-start --task <task_id> --retry-of <old dispatch> --worktree id:<worktree id>
   --agent claude --model claude-fable-5-1`. Then send the new dispatch a follow-up that says
   the predecessor died, that its work is in the worktree, to read `git status` and `git diff`
   first and continue from it, and where the brief file is. Restate any review fixes you had
   already sent the dead worker.
3. Acknowledge the stale inbox messages, then continue Step 3 as usual.

Do not reset a card to `Planned` because its worker died; the claim and the worktree are
still good.

## Step 4 — Report

One message to the developer, in this order:

1. A table: Test Case ID, title, outcome (PR URL / has automation problems / blocked), Orca
   worktree name.
2. For each PR, one or two sentences on what the test covers and what it does not, and any
   decision preflight left for the developer.
3. The cards this run skipped because of an earlier `[improve-test-automation-coverage` note.
4. Anything that went wrong in the run itself (a worker restart, a lock wait over 30 minutes, a
   Notion write that failed) and any papercut you logged.

Each worker that opened a PR leaves its worktree in Personal Review for the developer to look
at. Do not move anything to Peer Review.
