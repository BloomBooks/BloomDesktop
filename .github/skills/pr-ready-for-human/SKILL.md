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

## Board & Sync Reference
All Orca / GitHub board / YouTrack moves use the scripts documented in the `pr-kanban-sync` skill. Status keys: `waiting-ai` | `in-review` | `has-comments` | `completed`.

```bash
node scripts/sync-move.mjs all <pr-number> <status-key>
```

See `.github/skills/pr-kanban-sync/SKILL.md` for IDs, limitations, and individual-system fallbacks.

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

If `$YOUTRACK` is not set, tell the user: "I need a YouTrack API token to post the PR link. Set `YOUTRACK` in your environment or post the comment manually: PR: <PR URL>"

## Stage 4: GitHub Project Board — Add to Project & Set "Waiting for AI-Review"

### Ensure the PR is on the board
Check if it's already there:
```bash
gh project item-list 2 --owner BloomBooks --format json --limit 200 | \
  node -e "const d=JSON.parse(require('fs').readFileSync('/dev/stdin','utf8')); console.log(d.items.find(i=>i.content?.number===<PR-number>)?.id ?? 'not found')"
```

If not on the board, add it:
```bash
PR_NODE_ID=$(gh pr view <number> --repo BloomBooks/BloomDesktop --json id --jq '.id')
gh api graphql -f query="mutation {
  addProjectV2ItemById(input: {
    projectId: \"PVT_kwDOAFlSFM4Bawkp\"
    contentId: \"$PR_NODE_ID\"
  }) { item { id } }
}"
```

### Set all three systems to "waiting-ai"
```bash
node scripts/sync-move.mjs all <pr-number> waiting-ai
```

This sets GitHub board → "Waiting for AI-Review", Orca → "status-5" (Waiting on AI Review), YouTrack → "In Progress". If `all` warns about a missing Orca link, fall back to individual commands (see `pr-kanban-sync` skill).

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

```bash
node scripts/sync-move.mjs all <pr-number> in-review
```

This sets GitHub board → "Ready for Human", Orca → "in-review", YouTrack → "Ready For Code Review".

### Report
"PR #<number> is now in **Ready for Human** review. PR: <URL>"

## Re-entry After a New Commit
When a commit is pushed to a PR already in "Ready for Human" or "Has Comments":
1. Re-run Stage 4 (`node scripts/sync-move.mjs all <pr> waiting-ai` — resets all three systems).
2. Re-run Stages 5–6 (new bot-wait cycle, then back to human when quiet).

The whole cycle must restart because bots re-run on every commit.

## Rules
- Never move to "Ready for Human" while CI is failing.
- Never move to "Ready for Human" without Devin having run.
- Always check for duplicate YouTrack comments before posting.
- Always post Devin findings as GitHub PR comments before moving to any review state.
- If `YOUTRACK` is unavailable, note it and continue — the YouTrack comment is the lowest-stakes step.
- If the Orca browser is unavailable for Devin, tell the user what URL to open manually.
