---
name: devin-review
description: Kick off a Devin AI code review for a GitHub PR, wait for it, then post unresolved Bugs and Investigate flags as GitHub inline review-thread comments, and resolve the threads for findings Devin now considers fixed. Devin does NOT post to GitHub automatically — this skill bridges that gap.
argument-hint: "PR URL or number, e.g. BloomBooks/BloomDesktop#7949"
user-invocable: true
---
# Devin Review Skill

## When To Use

- When a new PR is created or a new commit pushed, as part of the bot-wait phase before human review.
- When the user explicitly wants a Devin review run and reported.

## URLs

Given `owner/repo` and PR number (e.g. `BloomBooks/BloomDesktop` / `7949`):

- **Results page**: `https://app.devin.ai/review/<owner>/<repo>/pull/<number>`

Navigating to the results page both triggers a review (if none exists yet) and shows results once complete. The `devinreview.com/<owner>/<repo>/pull/<number>` URL is an alias for the same page.

## Page Structure

The right sidebar of the review page has two tabs: **Info** and **Chat**.

The **Info** tab contains:

### Bugs section

Header shows **"N Bug"** where N = count of *unresolved* bugs. Click to expand if collapsed.

Each bug entry:

```
{Title}
Bug  {file}:{line}             ← unresolved
{Title}
Bug  {file}:{line} • Resolved  ← already fixed; skip
```

→ **Post unresolved Bugs to GitHub** as inline review-thread comments. **Resolved** bugs need cross-run reconciliation: if we posted the bug in a prior run, resolve its GitHub thread now; if we never posted it, no action (it was already fixed when Devin first saw it). See step 6.

### Flags section

Header shows **"N Flags"**. Click to expand — it is collapsed by default.

Contains two sub-categories:

**Investigate** items (post these):

```
{Title}
Investigate  {file}:{line-range}
```

**Informational** items (skip these — these are the low-signal "comments"):

```
{Title}
Informational  {file}:{line-range}
```

→ **Post Investigate flags to GitHub. Skip Informational flags.**

### Other sidebar fields

Checks, Reviewers, Assignees, Labels — these are metadata only, no action from this skill.

## Procedure

### 1. Navigate to the Review Page

Use the Chrome DevTools CLI with an **isolated context** (no shared cookies). This is critical — navigating while logged in to Devin consumes on-demand credits. The isolated context is unauthenticated but still shows all findings.

```bash
chrome-devtools new_page "https://app.devin.ai/review/<owner>/<repo>/pull/<number>" --isolatedContext "devin-noauth"
sleep 6
```

Close this tab when done to avoid accumulating isolated-context tabs.

### 2. Check if Review is Complete

```bash
chrome-devtools evaluate_script "() => document.body.innerText" 2>/dev/null | grep -E "Bug|Flags"
```

- If you see lines like `1 Bug` or `6 Flags` → review is complete. Proceed to step 3.
- If the page shows only a loading state or no Bug/Flags section → review is not yet done. Report "Devin review not yet complete" and return. Come back in 5–10 minutes.
- Timeout after 30 minutes total from trigger.

The review typically takes 10–20 minutes from first page navigation.

### 3. Enumerate Findings

Take a snapshot to get accessible UIDs for all finding buttons in the sidebar:

```bash
chrome-devtools take_snapshot 2>/dev/null | grep -E "Bug|Investigate|Informational|Resolved"
```

Each finding appears as a button whose accessible name contains the title, type label (`Bug`, `Investigate`, `Informational`), and file:line. Resolved bugs additionally contain `• Resolved`.

Collect:

- **Unresolved Bugs**: button text contains `Bug` but NOT `• Resolved` — to post (step 5)
- **Investigate flags**: button text contains `Investigate` — to post (step 5)
- **Resolved Bugs**: buttons containing `• Resolved` — record their titles; used to reconcile/resolve GitHub threads (step 6). Do NOT post these.
- **Skip entirely**: `Informational` items (low signal, no post, no reconcile)

Expand the Flags section first if it is collapsed:

```bash
chrome-devtools evaluate_script "() => { const btn = [...document.querySelectorAll('button')].find(el => el.textContent.includes('Flags')); btn?.click(); return btn?.textContent?.trim(); }"
sleep 2
```

### 4. Extract Full Descriptions

Each finding has a **full description** visible only after clicking the finding button. Always extract it — the one-line summary alone is not enough for a useful GitHub comment.

For each finding to post (unresolved Bug or Investigate):

```bash
# Click the finding button by its UID from the snapshot
chrome-devtools click "{uid}"
sleep 2

# Extract: title + body up to the action buttons
# "Ask Devin" is a reliable end-of-description marker for both Bug and Flag panels.
# The dismiss buttons just before it are "Copy bug"/"Copy flag"/"Prompt for agents".
chrome-devtools evaluate_script "() => { var t = document.body.innerText; var askD = t.indexOf('Ask Devin'); var copyIdx = t.lastIndexOf('Copy ', askD); var promptIdx = t.lastIndexOf('Prompt for agents', askD); var end = Math.min(copyIdx > 0 ? copyIdx : askD, promptIdx > 0 ? promptIdx : askD); var start = t.lastIndexOf('TITLE_PREFIX', end); return start >= 0 ? t.slice(start, end).trim() : 'not found'; }"

# Dismiss the panel before clicking the next finding
chrome-devtools press_key "Escape"
sleep 1
```

Where `TITLE_PREFIX` is a distinctive prefix of the finding title (enough to be unambiguous in the page text — avoid words like "the", "a", file names that appear in code diffs).

The extracted text will be: `{Title}\n\n{Full description paragraphs}`

### 5. Post Findings to GitHub as Inline Review Threads

Post each finding as an **inline review comment anchored to its diff line**, so it becomes a *resolvable* GitHub review thread. (Top-level PR comments have no "Resolve" affordance, so we can never close the loop on them — that is why we use review threads.) The "Post to GitHub" button on the Devin page is not functional — always post via `gh`.

First gather the PR head commit and the existing Devin review threads. This one query serves both dedup (step 5) and resolution (step 6): it returns each thread's GraphQL id (to resolve), its first comment's REST id (to reply), the body (to match by title), and whether it is already resolved.

```bash
HEAD_SHA=$(gh pr view <number> --repo <owner>/<repo> --json headRefOid --jq .headRefOid)

gh api graphql -f owner=<owner> -f repo=<repo> -F number=<number> -f query='
query($owner:String!,$repo:String!,$number:Int!){
  repository(owner:$owner,name:$repo){
    pullRequest(number:$number){
      reviewThreads(first:100){ nodes{
        id isResolved
        comments(first:1){ nodes{ databaseId body } }
      }}
    }
  }
}' --jq '.data.repository.pullRequest.reviewThreads.nodes[]
  | select(.comments.nodes[0].body | startswith("[Devin]"))
  | {threadId:.id, isResolved, commentId:.comments.nodes[0].databaseId, body:.comments.nodes[0].body}'
```

A finding is **already posted** if one of those thread bodies contains the same finding title. Skip posting it again.

**Post each unresolved Bug** (and each Investigate flag) that is not already posted. Use `Bug` or `Investigate` in the label:

```bash
# Single line "<file>:<line>":
gh api repos/<owner>/<repo>/pulls/<number>/comments \
  -f commit_id="$HEAD_SHA" \
  -f path="<file>" \
  -F line=<line> \
  -f side="RIGHT" \
  -f body="$(cat <<'EOF'
[Devin] **Bug**: <Title>

<Full description>
EOF
)"
```

For a **line range** `<start>-<end>`, add `-F start_line=<start> -f start_side="RIGHT"` and set `line=<end>`.

**Fallback when the line is not in the diff** (the API returns HTTP 422 — common for Investigate flags on unchanged context lines): post a top-level comment instead so the finding is not lost, and tag it so step 6 can recognize it:

```bash
gh pr comment <number> --repo <owner>/<repo> --body "$(cat <<'EOF'
[Devin] **Bug**: <Title> (`<file>:<line>` — outside diff, not resolvable as a thread)

<Full description>
EOF
)"
```

Record which findings fell back — they cannot be natively resolved later (step 6 edits them instead).

### 6. Reconcile Resolved Findings

For each bug Devin now marks **• Resolved** (collected in step 3), match it by title against the Devin threads gathered in step 5:

- **We posted it before and its thread is still unresolved** → reply to document why, then resolve the thread:

  ```bash
  # Reply on the thread (needs the REST comment id from the query above)
  gh api repos/<owner>/<repo>/pulls/<number>/comments \
    -F in_reply_to=<commentId> \
    -f body="[Devin] ✅ Devin now considers this fixed (as of \`$HEAD_SHA\`). Resolving."

  # Resolve the thread (needs the GraphQL thread id)
  gh api graphql -f threadId=<threadId> -f query='
  mutation($threadId:ID!){ resolveReviewThread(input:{threadId:$threadId}){ thread{ id isResolved } } }'
  ```

- **We posted it before only as a top-level fallback comment** (no thread) → it cannot be natively resolved; edit that comment to prepend a `✅ Resolved (<SHA>)` marker so a human sees it is closed.
- **We never posted it** → no action; it was fixed before we ever flagged it.
- **Its thread is already `isResolved: true`** → no action.

Note: Investigate flags have no "Resolved" state in Devin — when Devin stops flagging one, it simply disappears from the list. Do **not** auto-resolve threads for vanished flags (no reliable signal); leave those for the human reviewer.

### 7. Log that Devin was run — even if it found no issues

Because it is quite expensive to consult Devin, it's important that we can avoid consulting it if we know that we have not committed anything since the last time we consulted it. For this reason, whenever a consultation with Devin is finished, add a comment to the PR: "Consulted Devin on (date time) up to commit (SHA)". Of course it is vital that we actually gave Devin sufficient time to do the check before we decide that it has no new findings.

### 8. Report

Return a summary:

- N unresolved Bugs found — N posted, N skipped (already posted), N fell back to top-level (line not in diff)
- N Resolved Bugs — N threads resolved, N fallback comments marked, N no-action (never posted / already resolved)
- N Investigate flags found — N posted, N skipped
- N Informational items found (not posted — low signal)
- Whether any findings need developer attention before moving to human review

## Notes

- Devin does **not** post its findings to GitHub automatically — that is why this skill exists.
- Findings are posted as **inline review-thread comments** (anchored to the diff line), not top-level PR comments, specifically so they can be resolved later.
- A Resolved bug means Devin confirmed the PR already fixes what it found. If we **never posted** it, no GitHub action is needed. If we **did post** it in a prior run, resolve that thread now (step 6) so the comment we created doesn't linger looking unaddressed.
- Titles are the matching key between a Devin finding and the GitHub thread we posted for it, both for dedup (step 5) and resolution (step 6). Keep the `[Devin] **Bug**: <Title>` / `[Devin] **Investigate**: <Title>` format stable.
- Informational items are observations, not action items. Skip them.
- Use the `chrome-devtools` **CLI** (Bash commands) for all browser automation in this skill — not the MCP plugin (disabled; spawns zombie node processes) and not the Orca browser.
- Always use `--isolatedContext "devin-noauth"` when opening Devin pages. Navigating while logged in consumes on-demand credits; the isolated context is unauthenticated but still shows all findings.
- If Chrome DevTools CLI is unavailable, tell the user: "Please open `https://app.devin.ai/review/<owner>/<repo>/pull/<number>` in Chrome to check Devin's findings."

