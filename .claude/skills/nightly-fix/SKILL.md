---
name: nightly-fix
description: Fix an open nightly CI failure, recorded on a YouTrack card tagged "Nightly Failure", as one branch ready to become a draft PR, with the tests that prove the fix named. Use when asked to "fix the nightly failures", "fix last night's failure", or "fix <a Nightly Failure card>". The Bloom bot runs it every morning after nightly-triage.
---

# Fix a nightly failure

Each open nightly failure has a YouTrack card tagged **Nightly Failure**, which the
`nightly-triage` skill keeps: what failed, in which nightlies, and what a first look found. Find the
open ones with `/issues?query=tag:%20%7BNightly%20Failure%7D%20%23Unresolved` (see the
`youtrack-api` skill). This skill takes one or more of those cards and fixes them: one change that
one PR can carry.

## 1. Read the rules first

Read the root `AGENTS.md`, and the `AGENTS.md` or `README.md` of each folder that you change:
`src/BloomBrowserUI/AGENTS.md`, `src/BloomTests/AGENTS.md`, `src/BloomE2E/README.md`. Two of
their rules shape this whole skill:

- **Never accept a flaky test.** A fix for a flaky failure removes the race, in Bloom or in the
  test. Retries, a longer timeout, a fixed delay, looser assertions, skipping the test, or a test
  hook added to Bloom are not fixes: they need a developer's permission, which you do not have
  here. If those are the only fixes you can find, write up what you found instead (step 5).
- **Changing what an existing test checks needs a developer's approval.** You may make such a
  change, because the draft PR is where a developer approves or refuses it, but you must say so
  plainly (step 5).

## 2. Choose what to fix

Leave out a card that someone has already started on: it has an Assignee, its State is past
Ready For Work, or its description or comments link an open PR or name a branch or commit with a
possible fix. That work belongs to whoever
started it, even when it is unreviewed or out of date. Also leave out any failure that an open PR
on a `bot/` branch already covers.

From the rest, choose what one PR should fix. Use judgement: one PR unless there is a reason for
more. When a feature changed some wording and three tests broke on it, that is one PR updating the
three tests. A separate PR is right only for a failure whose cause and fix have nothing to do with
the first, so that each can merge without waiting on the other. Take the failure most likely to
recur first: a known failure seen again over a new one seen once.

## 3. Find the cause

The card's description and comments are a first look. Go further: the run's log and artifacts, the trace, the code the
test drives, and `git log` for what changed around when it started. Decide which of these it is,
because each is fixed and proved differently:

- **Bloom changed on purpose** (new wording, a moved control, a new step) and the test still
  expects the old behaviour. The fix is in the test. Proof: the updated tests pass once.
- **A race or other flakiness**, in Bloom or in the test. Proof: the test fails on master within
  10 runs, and passes 10 runs out of 10 on the fix.
- **A real Bloom bug** that the test caught. The fix is in Bloom. Proof as for a race when the bug
  only sometimes shows, otherwise one passing run.

## 4. Make the change and check it

Keep the change to what fixes the failure. Follow the code style in `AGENTS.md`.

Check what you can before handing over:
- Changed C#: it must compile.
- Changed TypeScript with vitest tests near it: run them.
- E2E, visual regression, C# unit and React component tests need Windows; name them in your
  result so that they run there (step 5).

## 5. Report

Say:
- which cards the change fixes, by their ids;
- what the cause was and what the change does;
- which kind from step 3 it is;
- which tests prove it (spec files, C# test filter, component test filters);
- whether it changes what an existing test checks. If it does, this comes first: what the test
  checked before and after, and why the test was wrong and not Bloom.

If you could not fix it, say what you found, what you ruled out, and what a developer should look
at next.
