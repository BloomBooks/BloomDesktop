---
name: pr-ready-for-human
description: Full "ready for human review" workflow. Triggered when the user says "this is ready for human review." Ensures committed+pushed, PR exists, YouTrack linked, GitHub project board updated, bot reviews run and quiet, then moves to human review columns. Also handles re-entry after a new commit pushes a PR back into the bot-wait stage.
argument-hint: "optional: PR number or branch name — defaults to current worktree"
user-invocable: true
---

# PR Ready For Human Review

## Trigger Phrase
When the user says **"This is ready for human review"** (or equivalent), execute this skill from start to finish.

When the user pushes a new commit to a PR that is already in a review state, re-enter at **Stage 4** (reset to bot-wait).

## GitHub Project Board Reference
Project: **PR Review Tracker** — https://github.com/orgs/BloomBooks/projects/2

GraphQL IDs (hardcoded — do not re-query unless these stop working):
- Project ID: `PVT_kwDOAFlSFM4Bawkp`
- Status field ID: `PVTSSF_lADOAFlSFM4BawkpzhVl0_w`
- Status option IDs:
  - `97860183` → "Waiting for AI-Review"
  - `99a3f545` → "Has Comments"
  - `05eedb52` → "Ready for Human"

## Stage 1: Committed & Pushed

```bash
git status
git log --branches --not --remotes --oneline
```

- If there are uncommitted changes: ask the user whether to commit them, then commit.
- If there are unpushed commits: push them.
- Confirm the branch is tracking a remote (`git branch -vv`).

## Stage 2: PR Exists

```bash
gh pr list --head <branch> --repo BloomBooks/BloomDesktop --json number,title,url,state
```

If no open PR:
```bash
gh pr create --title "<BL-XXXXX short summary from YouTrack>" \
  --body "Fixes https://issues.bloomlibrary.org/youtrack/issue/<BL-XXXXX>"
```

Record the PR number and URL.

## Stage 3: YouTrack Comment with PR Link

Find the YouTrack issue ID from the branch name, PR title, or recent commit messages (look for `BL-XXXXX`).

Check for an existing PR link comment to avoid duplicates:
```bash
curl -s "https://issues.bloomlibrary.org/youtrack/api/issues/<issue-id>/comments?fields=text" \
  -H "Authorization: Bearer $YOUTRACK" | grep -i "github.com.*pull"
```

If no PR link comment exists, post one:
```bash
curl -s -X POST "https://issues.bloomlibrary.org/youtrack/api/issues/<issue-id>/comments" \
  -H "Authorization: Bearer $YOUTRACK" \
  -H "Content-Type: application/json" \
  -d "{\"text\": \"PR: <PR URL>\"}"
```

If `$YOUTRACK_TOKEN` is not set, tell the user: "I need a YouTrack API token to post the PR link. Set `YOUTRACK_TOKEN` in your environment or post the comment manually: PR: <PR URL>"

## Stage 4: GitHub Project Board — Add to Project & Set "Waiting for AI-Review"

### Find or add the PR's project item
```bash
gh api graphql -f query='{
  repository(owner:"BloomBooks", name:"BloomDesktop") {
    pullRequest(number:<PR-number>) {
      projectItems(first:10) {
        nodes { id project { number } }
      }
    }
  }
}'
```

If the PR is not in project 2, add it:
```bash
gh api graphql -f query='mutation {
  addProjectV2ItemById(input: {
    projectId: "PVT_kwDOAFlSFM4Bawkp"
    contentId: "<PR node id>"
  }) { item { id } }
}'
```

Get the PR node ID with:
```bash
gh pr view <number> --repo BloomBooks/BloomDesktop --json id --jq '.id'
```

### Set Status to "Waiting for AI-Review"
```bash
gh api graphql -f query='mutation {
  updateProjectV2ItemFieldValue(input: {
    projectId: "PVT_kwDOAFlSFM4Bawkp"
    itemId: "<itemId>"
    fieldId: "PVTSSF_lADOAFlSFM4BawkpzhVl0_w"
    value: { singleSelectOptionId: "97860183" }
  }) { projectV2Item { id } }
}'
```

### Also sync the local worktree board if re-entering after a commit
If you maintain a personal local board tracker (e.g. an `orca-board` user skill), sync it
back to the "in progress / waiting on AI review" state. Skip this step if no such skill is
available — the GitHub project board above is the shared source of truth.

## Stage 5: Bot-Review Wait Cycle

This stage runs after a new PR is created OR after any new commit is pushed to an existing PR.

### 5a. Kick off Devin
Invoke the `devin-review` skill for this PR. Use the Chrome DevTools CLI (not Orca browser) to navigate to `https://devinreview.com/<owner>/<repo>/pull/<number>` and trigger the review. It will **not** block — it returns immediately after triggering, with a note that results will be ready in ~20 minutes.

### 5b. Tell the User and Wait
Report: "Bot reviews kicked off (including Devin). Come back in ~20 minutes and say 'check bot reviews for PR #<number>' to proceed."

Do **not** actively sleep or block for 20 minutes. Return control to the user.

### 5c. On Re-check ("check bot reviews")
When the user returns to check:

**Check Devin findings:**
Run the `devin-review` skill again (in read-only check mode — navigate to `app.devin.ai` and extract findings). It will post any unresolved "Potential Bug" and "Potential Issue" findings as GitHub comments if not already posted. Report what was found.

**Check GitHub CI:**
```bash
gh pr checks <number> --repo BloomBooks/BloomDesktop
```

**Check for new review comments (last 20 min):**
```bash
gh api repos/BloomBooks/BloomDesktop/pulls/<number>/comments \
  --jq '[.[] | select(.created_at > "<20-minutes-ago-ISO>")]'
```

**Decision**:
- If CI is **failing** → update project card to "Has Comments", report which checks failed. Stop.
- If there are **new unresolved comments** (from Devin posts or other bots) → update project card to "Has Comments", list what needs attention. Stop.
- If CI is **passing** AND no new bot activity in the last 20 minutes → proceed to Stage 6.

If the user says "skip bots, just move it to human review" → honor explicitly and proceed to Stage 6, but note in your response that the bot check was skipped at user request.

## Stage 6: Move to "Ready for Human"

### GitHub project board
```bash
gh api graphql -f query='mutation {
  updateProjectV2ItemFieldValue(input: {
    projectId: "PVT_kwDOAFlSFM4Bawkp"
    itemId: "<itemId>"
    fieldId: "PVTSSF_lADOAFlSFM4BawkpzhVl0_w"
    value: { singleSelectOptionId: "05eedb52" }
  }) { projectV2Item { id } }
}'
```

### Local worktree board
If you maintain a personal local board tracker (e.g. an `orca-board` user skill), sync it
to the "in human review" state. Skip if unavailable.

### Report
"PR #<number> is now in **Ready for Human** review. PR: <URL>"

## Re-entry After a New Commit
When a commit is pushed to a PR already in "Ready for Human" or "Has Comments":
1. Re-run Stage 4 (set project card back to "Waiting for AI-Review", and sync any local board tracker).
2. Re-run Stages 5–6 (new bot-wait cycle, then back to human when quiet).

The whole cycle must restart because bots re-run on every commit.

## Rules
- Never move to "Ready for Human" while CI is failing.
- Never move to "Ready for Human" without Devin having run.
- Always check for duplicate YouTrack comments before posting.
- Always post Devin findings as GitHub PR comments before moving to any review state.
- If `YOUTRACK_TOKEN` is unavailable, note it and continue — the YouTrack comment is the lowest-stakes step.
- If the browser automation is unavailable for Devin, tell the user what URL to open manually.
