---
name: devin-review
description: Kick off a Devin AI code review for a GitHub PR, wait for it, then post unresolved Bugs and Investigate flags as GitHub PR comments. Devin does NOT post to GitHub automatically — this skill bridges that gap.
argument-hint: "PR URL or number, e.g. BloomBooks/BloomDesktop#7949"
user-invocable: true
---

# Devin Review Skill

## When To Use
- When a new PR is created or a new commit pushed, as part of the bot-wait phase before human review.
- Invoked by `pr-ready-for-human` during its bot-wait stage.
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

→ **Post unresolved Bugs to GitHub** (Resolved ones are confirmed-fixed; no action needed.)

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

**Pin to the right tab and verify the PR before reading anything.** Multiple PR reviews
opened in the same `devin-noauth` context are separate tabs that all share that context, and
`evaluate_script`/`take_snapshot` operate on whatever page is currently *selected* — which can
drift to another PR's tab. So always select your tab explicitly and confirm the page header
shows the PR you expect:

```bash
chrome-devtools list_pages                      # find the index of YOUR pull/<number> tab
chrome-devtools select_page <id>                # pin future calls to it
chrome-devtools evaluate_script "() => (document.body.innerText.match(/PR #\d+/)||['?'])[0]"
# → must print "PR #<number>". If not, you are about to read the wrong review.
```

Close stray tabs when done to avoid accumulating isolated-context tabs.

### 2. Check if Review is Complete — beware STALE results

Reviews take a while (**10–20+ minutes**), and a **new push invalidates the prior review**:
Devin keeps showing the *old* commit's findings, prefixed with an **"Outdated"** badge, until
the re-review of the new HEAD finishes. So "I see `1 Bug` / `6 Flags`" does **NOT** mean the
results are current — you must check they are not marked Outdated.

Do **not** grep `document.body.innerText` for status. The page embeds the entire diff inline,
so keywords like `pending`, `complete`, `reviewing`, and even commit SHAs match diff/source
text and give false positives. Read the finding **buttons** in the sidebar instead:

```bash
chrome-devtools evaluate_script "() => { const b=[...document.querySelectorAll('button')]; const headers=b.filter(x=>/\d+\s+(Bug|Flag)s?/.test(x.textContent)).map(x=>x.textContent.trim().replace(/\s+/g,' ')); return JSON.stringify(headers); }"
```

- Headers like `"1 Bug"`, `"6 Flags"` with **no** `Outdated` prefix → review is current for the
  selected commit. Proceed to step 3.
- Headers like "Outdated 1 Bug", "Outdated 6 Flags" → these are the *previous* commit's
  results; the re-review is still running. Report "Devin re-review still in progress" and come
  back in 5–10 minutes. Re-checking requires a reload (`chrome-devtools navigate_page --type reload`).
- No Bug/Flags buttons at all → review hasn't produced results yet; come back later.
- Timeout after 30 minutes total from trigger.

**There is no GitHub-side signal.** Devin does not post a commit status/check, so `gh pr checks`
will not tell you whether the review is done — the web UI is the only source. (A "pending"
entry in Devin's commit-status popup is some *other* GitHub check, e.g. `code-review/reviewable`,
not Devin.)

**Do not pre-judge the findings.** Even when a push looks purely mechanical (e.g. a master
merge), wait for the non-Outdated results and enumerate them — don't assume the prior set
carries over.

### 3. Enumerate Findings

Take a snapshot to get accessible UIDs for all finding buttons in the sidebar:

```bash
chrome-devtools take_snapshot 2>/dev/null | grep -E "Bug|Investigate|Informational|Resolved"
```

Each finding appears as a button whose accessible name contains the title, type label (`Bug`, `Investigate`, `Informational`), and file:line. Resolved bugs additionally contain `• Resolved`.

Collect:
- **Unresolved Bugs**: button text contains `Bug` but NOT `• Resolved`
- **Investigate flags**: button text contains `Investigate`
- **Skip**: buttons containing `• Resolved` (resolved bugs) or `Informational`

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

### 5. Post Findings to GitHub

The "Post to GitHub" button on the Devin page is not functional — always post via `gh` instead.

First check for existing Devin comments to avoid duplicates:
```bash
gh api repos/<owner>/<repo>/issues/<number>/comments --paginate --jq '.[].body' | grep "\[Devin\]"
```

For each unresolved **Bug**:
```bash
gh pr comment <number> --repo <owner>/<repo> --body "$(cat <<'EOF'
[Devin] **Bug**: <Title>

`<file>:<line>`

<Full description from the bug report if available via "See bug report" link>
EOF
)"
```

For each **Investigate** flag:
```bash
gh pr comment <number> --repo <owner>/<repo> --body "$(cat <<'EOF'
[Devin] **Investigate**: <Title>

`<file>:<line-range>`
EOF
)"
```

Skip if a comment starting with `[Devin]` and containing the same title already exists.

### 6. Report

Return a summary:
- N unresolved Bugs found — N posted, N skipped (already posted), N resolved (skipped)
- N Investigate flags found — N posted, N skipped
- N Informational items found (not posted — low signal)
- Whether any findings need developer attention before moving to human review

## Real Example (PR #7949)

**Bugs (3 total, 1 unresolved):**
- ✅ Post: "Legacy ebook layout name normalization fails for mixed-case input" — `SizeAndOrientation.cs:80`
- ⏭ Skip: "buildSavePageContentString calls removeEditingDebris..." — `bloomEditing.ts:1322` — Resolved
- ⏭ Skip: "Overlay can get permanently stuck if exception occurs..." — `ExternalApi.cs:240` — Resolved

**Flags (6 Investigate, several Informational):**
- ✅ Post: "Scale inconsistency in computeImageFitTopPercent..." — `autoFitImageOverTextSplits.ts:284`
- ✅ Post: "BringBookUpToDate may write to disk before per-page processing..." — `BookProcessor.cs:36`
- ✅ Post: "HandleProcessBookByPath could hit a stale FolderPath..." — `ExternalApi.cs:505`
- ✅ Post: "Tab change from ReloadCurrentBookDiscardingEdits may be asynchronous..." — `ExternalApi.cs:849`
- ✅ Post: "Device16x9 layouts get renamed to 'Ebook' in the user-facing display name" — `Layout.cs:169`
- ✅ Post: "Replace(\"ebook\", \"Ebook\") in ExtractPageSizeName is dead code" — `SizeAndOrientation.cs:76`
- ⏭ Skip: All Informational items (buildSavePageContentString DOM mutation, doWhenWorkspaceBundleLoaded, etc.)

## Notes
- Devin does **not** post its findings to GitHub automatically — that is why this skill exists.
- A Resolved bug means Devin confirmed the PR already fixes what it found. No GitHub comment needed.
- Informational items are observations, not action items. Skip them.
- Use the `chrome-devtools` **CLI** (Bash commands) for all browser automation in this skill — not the MCP plugin (disabled; spawns zombie node processes) and not the Orca browser.
- Always use `--isolatedContext "devin-noauth"` when opening Devin pages. Navigating while logged in consumes on-demand credits; the isolated context is unauthenticated but still shows all findings.
- If Chrome DevTools CLI is unavailable, tell the user: "Please open `https://app.devin.ai/review/<owner>/<repo>/pull/<number>` in Chrome to check Devin's findings."
