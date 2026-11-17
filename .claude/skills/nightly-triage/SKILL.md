---
name: nightly-triage
description: Triage a nightly CI run of BloomDesktop and record each failure on its YouTrack card, tagged "Nightly Failure" — a comment on the card that already covers that test, reopening it if it was closed, or a new card for a failure nobody has seen — then write a brief summary for the team. Use when asked to "investigate the nightly failure", "what was last night's failure?", "look at last night", "triage the nightlies", or "investigate recent nightly failures".
---

# Nightly triage

Every nightly failure has one YouTrack card, tagged **Nightly Failure**. The card is where
everyone sees which nightly failures have been looked at and where each one stands, and each
night the test fails again adds a comment to it. This skill keeps those cards up to date. It is
triage, not investigation: find out *what* failed and *whether a card already covers it*, take a
first look, and record that. Do not chase root causes, write fixes, or gather in-depth details
here. That work goes on the card and on a branch.

Talk to YouTrack as the `youtrack-api` skill says. Every comment and description starts with its
attribution line.

## 1. Find the runs to look at

Triage the run the developer names. When they name none, take the newest scheduled run:

```
gh run list --repo BloomBooks/BloomDesktop --workflow nightly.yml --branch master --event schedule --limit 5 --json databaseId,createdAt,conclusion,headSha
```

If it passed, say so and stop.

## 2. For each failed run, name what failed

```
gh run view <id> --repo BloomBooks/BloomDesktop --json jobs        # failed jobs and steps
gh run view <id> --repo BloomBooks/BloomDesktop --log-failed       # the failing step's log
```

The log is large, so filter it:
- The run's summary lines (`* ✅` / `* ❌`) give each suite's counts.
- Drop `[WebServer]` lines, which are noise from the component tests.
- Playwright failures start at `  1) <spec>:<line> › <title>`. The 40 lines after that give
  the error and the source line.
- C# failures show as `Failed <TestName>`.

Ignore errors from passing suites, such as `AggregateError` stacks in the vitest output.

## 3. Find each failure's card

Every card's description has a `**Test:**` line naming each test it covers, as the spec file (or
C# class) and the test's title. Search the tagged cards, closed ones included, for the test's
title, then for its spec file or class:

```
/issues?query=tag:%20%7BNightly%20Failure%7D%20%22<words from the test title>%22&fields=idReadable,summary,description,customFields(name,value(name))
```

A failure that is not tied to one test (a whole suite crashed, a step's teardown failed) has a
card whose `**Test:**` line names the suite or step; search for that.

Then decide which of these it is:

- **Seen again:** a card covers this test and it failed the same way. Comment on the card.
- **Same test, new symptom:** a card covers this test but it failed in a different way. Comment
  on the card with the new symptom. Make a new card only if the cause is clearly different.
- **The fix did not hold:** the card is Closed. Comment that it failed again after the fix, and
  set its State to **Incoming** so it is looked at again.
- **New:** no card covers it. Make one (step 4).

One cause can break several tests at once; give them one card and list each test on its
`**Test:**` line.

A first look means about one level down, no deeper. Usually that is the error and the trace,
plus the screenshot or the Bloom log from the run's artifacts (`e2e-report`,
`component-tester-traces`, `bloom-diagnostics`). `bloom-diagnostics` is uploaded on every run
that reached the visual-regression suite, green ones included. It holds Bloom's own logs
(`Log*.txt` from `%TEMP%\SIL\Bloom`, one per Bloom the e2e and visual-regression suites
launched), the Windows application event log, and the hung-Bloom watchdog's samples. It does not
hold anything from the C# tests: they point `TEMP` at a folder of their own, which the nightly
does not upload.

```
gh run download <id> --repo BloomBooks/BloomDesktop -n <artifact> -D <scratchpad folder>
```

For a Playwright trace, unzip `trace.zip` and read its `test.trace` (test steps), `0-trace.trace`
(console) and `0-trace.network` (API calls). Once a sentence or two describes the failure, stop
there. Delete the downloaded artifacts when you are done; they have deep paths, so on Windows use
`rm -rf` from Git Bash.

## 4. Write it on the card

**A comment on an existing card** says which nightly failed, as a link to the run, and how:

```
Failed again in the 2026-11-14 nightly ([run 43210987654](https://github.com/BloomBooks/BloomDesktop/actions/runs/43210987654)), the same way as before.
```

Add a sentence when the symptom is new, or when the first look found something that the card does
not say yet. Don't repeat what the card already says.

**A new card:**
- Summary: `Nightly: ` and then what fails, in a few plain words (`Nightly: the Leveled Reader
  comes back on level 1 instead of the level the book was left on`).
- Type **Bug**, State **Incoming**, Reporter Name **Nightly Test Failure**, and Kanban Board the
  current sprint of the **Bloom Kanban** board, read from `/agiles?fields=name,currentSprint(name)`.
  Set them with a POST to the issue's `customFields`; Kanban Board is a
  `SingleVersionIssueCustomField`, and Reporter Name is a `SimpleIssueCustomField` whose value is
  the plain string. A command (`/commands`) can't
  set Kanban Board.
- The tag **Nightly Failure**: POST `{"id":"104-289"}` to `/issues/<id>/tags`. Don't tag through a
  command: `tag {Nightly Failure}` makes a new private tag named with the braces.
- Description, after the attribution line:

  ```
  **Test:** `reader-tool-stage-and-level.spec.ts` › "each book remembers its own level [Test Case ID 442]"

  **Failed:** 2026-11-11 ([run 43013004427](https://github.com/BloomBooks/BloomDesktop/actions/runs/43013004427))

  <the assertion that failed, and what the first look shows, labelled as a first look>

  <what is not yet known>
  ```

Keep both brief. Do not paste stack traces or log dumps. Short identifiers (a test name, an
endpoint, a commit) are fine.

Also look at any open PR that a card links, and say on the card when its state has changed
(merged, closed).

## 5. Write a brief summary for the team

A triage that found something ends with a very short summary, which goes in the thread of the
nightly's own report in Slack's #bloom-team-developers, so the team sees that the night was
looked at. When the Bloom bot runs this skill, its job posts the summary there. When a developer
runs it, put the summary in your report so the developer can post it. If the run passed, write no
summary: the nightly's own report already says it passed.

Keep it to a few lines: `[Claude triage]` on a line of its own, then which run was covered, then
one line per failure saying whether it is new, seen again, or failed again after its fix, with a
short name and its card. For example:

```
[Claude triage]
Last night's run failed in one e2e test, new: the Leveled Reader came back on level 1 instead of 2. [BL-17099](https://issues.bloomlibrary.org/youtrack/issue/BL-17099)
```

No stack traces, no log excerpts. The repo is public, but the post goes to people, so keep it
plain.

## 6. Report

Tell the developer what each run showed, and which cards you commented on, reopened or made.
That high-level picture is the deliverable. For each new or unexplained failure, say plainly that
its cause is still open. Whether and when to investigate it further is a separate decision, and
it is the developer's.

Never present waiting to see whether a failure recurs as an option. A test that failed without a
code change to explain it is already flaky, and the no-flaky-tests rule in the root `AGENTS.md`
applies from its first failure.

## Limits

- Make or reopen cards only for nightly failures, as above. Don't make branches or fixes without
  asking.
- Don't close a card. A card closes through the usual review and testing of its fix.
- Don't dispatch a CI run.
- Commit and push only as the developer directs.
- Never treat "it passed on a rerun", or "it passed the next night", as an outcome, and never
  recommend waiting for a failure to recur. The no-flaky-tests rule in the root `AGENTS.md`
  applies.
