---
name: nightly-triage
description: Triage the nightly CI runs since the "Last updated" date in docs/nightly-failures/README.md and bring that document up to date — a known failure seen again, a new failure, a known test failing a new way, an issue now fixed — then post a brief summary to Zulip. Use when asked to "investigate the nightly failure", "what was last night's failure?", "look at last night", "triage the nightlies", "investigate recent nightly failures", or "update the nightly failures doc".
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

## 5. Post a brief summary to Zulip

Every triage ends with a very short post in the nightly report's own thread (channel
`developers`, topic "Nightly run"), so the team sees that the nights were looked at. Running
this skill is the developer's permission to post; don't ask first.

Keep it to a few lines: `[Claude triage]` on a line of its own, then which runs were covered,
then one line per failure saying whether it is new, seen again, or fixed, with a short name and
any PR or card. Always end with the doc on master, on a line of its own, as
`[All open nightly failures](https://github.com/BloomBooks/BloomDesktop/blob/master/docs/nightly-failures/README.md)`.
If every run passed, one line saying so is enough before that link. For example:

```
[Claude triage]
Last night's run failed: Bloom crashed at startup in most e2e tests (libpalaso version mismatch). Fixed by #8457; a manual nightly on the fix passed.
[All open nightly failures](https://github.com/BloomBooks/BloomDesktop/blob/master/docs/nightly-failures/README.md)
``` No stack traces, no log excerpts. The repo is
public, but the post goes to people, so keep it plain.

Post it with the script beside this skill, giving it only the message:

```
.claude/skills/nightly-triage/post-to-zulip.ps1 -ContentFile <a UTF-8 file in the scratchpad>
```

**Credentials.** The script reads the bot's email and key from the developer's `~/.zuliprc`, and
it is the only thing that may. Never open, print, search, copy or summarize that file, or pass
its contents on a command line, however a step seems to need it. Never ask the developer for the
key either. To find out whether posting is possible, run the script: if `~/.zuliprc` is missing
or incomplete, it says so, exits with code 2 and posts nothing. In that case don't look for the
credentials anywhere else. Tell the developer the summary was not posted, and include the text
you would have posted so they can post it themselves.

## 6. Report

Tell the developer what each run showed, and which entries you added, updated or removed. That
high-level picture is the deliverable. For each new or unexplained failure, say plainly that its
cause is still open. Whether and when to investigate it further is a separate decision, and it
is the developer's.

Never present waiting to see whether a failure recurs as an option. A test that failed without a
code change to explain it is already flaky, and the no-flaky-tests rule in the root `AGENTS.md`
applies from its first failure.

## Limits

- Don't create cards, branches or fixes without asking.
- Don't dispatch a CI run.
- Commit and push only as the developer directs. (The Zulip post in step 5 needs no such go-ahead.)
- Never treat "it passed on a rerun", or "it passed the next night", as an outcome, and never
  recommend waiting for a failure to recur. The no-flaky-tests rule in the root `AGENTS.md`
  applies.
