---
name: nightly-triage
description: Triage the nightly CI runs since the "Last updated" date in docs/nightly-failures/README.md and bring that document up to date — a known failure seen again, a new failure, a known test failing a new way, an issue now fixed. Use when asked to "investigate the nightly failure", "what was last night's failure?", "look at last night", "triage the nightlies", "investigate recent nightly failures", or "update the nightly failures doc".
---

# Nightly triage

`docs/nightly-failures/README.md` is the high-level place where everyone can see which nightly
failures have already been looked at and where each one stands. This skill brings it up to
date. It is triage, not investigation: find out *what* failed and *whether we already know about
it*, take a first look, and record that. Do not chase root causes, write fixes, or gather
in-depth details here. That work belongs on a YouTrack card and/or a branch, and the doc just
links to it.

## 1. Find the runs to look at

Read the doc's **Last updated** line. Every nightly run after that date is in scope:

```
gh run list --repo BloomBooks/BloomDesktop --workflow nightly.yml --limit 10 --json databaseId,createdAt,conclusion,headSha
```

If there are none, say so and stop.

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

## 3. Classify each failure against the doc

- **Seen again:** the same test failing the same way as an existing entry. Add the date to that
  entry's **Failed** line. If its fix was supposed to cover this, say that it did not.
- **Known test, new symptom:** an existing entry's test failing in a different way. Add a line
  to that entry. Make it a new entry only if the cause is clearly different.
- **New:** not in the doc. Add a short entry: the date, the test and its assertion, and what a
  first look shows, labelled as a first look. Say plainly what is not yet known.
- **Fixed:** an entry whose fix has landed, where the nightly has since passed on it. Remove the
  entry. Its history lives in git and on the card.

Also check each entry's links, and update a PR or card whose state has changed (merged, closed,
new PR opened).

A first look means about one level down, no deeper. Usually that is the error and the trace,
plus the screenshot or the Bloom log from the run's artifacts (`e2e-report`,
`component-tester-traces`):

```
gh run download <id> --repo BloomBooks/BloomDesktop -n <artifact> -D <scratchpad folder>
```

For a Playwright trace, unzip `trace.zip` and read its `test.trace` (test steps), `0-trace.trace`
(console) and `0-trace.network` (API calls). Once a sentence or two describes the failure, stop
there. Delete the downloaded artifacts when you are done; they have deep paths, so on Windows use
`rm -rf` from Git Bash.

## 4. Update the doc

- Set **Last updated** to today, naming the last run triaged.
- Keep each entry brief, in the existing shape: **Failed** (dates, test), **Cause** or **What
  the trace shows**, and **Status** (links to a PR or card, or "no card or PR").
- Do not paste stack traces or log dumps. Short identifiers (a test name, an endpoint, a
  commit) are fine.

## 5. Report

Tell the developer what each run showed, and which entries you added, updated or removed. For
each new or unexplained failure, recommend the next step: a card, a branch to investigate, or
waiting to see whether it recurs. Then let them decide.

## Limits

- Don't create cards, branches or fixes without asking.
- Don't dispatch a CI run.
- Commit and push only as the developer directs.
- Never treat "it passed on a rerun" as an outcome. The no-flaky-tests rule in the root
  `AGENTS.md` applies.
