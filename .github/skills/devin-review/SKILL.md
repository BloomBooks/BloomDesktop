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

Close this tab when done to avoid accumulating isolated-context tabs.

### 2. Check if Review is Complete

```bash
chrome-devtools evaluate_script "() => document.body.innerText" 2>/dev/null | grep -E "Bug|Flags"
```

- If you see lines like `1 Bug` or `6 Flags` → review is complete. Proceed to step 3.
- If the page shows only a loading state or no Bug/Flags section → review is not yet done. Report "Devin review not yet complete" and return. Come back in 5–10 minutes.
- Timeout after 30 minutes total from trigger.

The review typically takes 10–20 minutes from first page navigation.

### 3. Extract Findings

Run this to get the full sidebar text:

```bash
chrome-devtools evaluate_script "() => { const t = document.body.innerText; const idx = t.indexOf('1 Bug') !== -1 ? t.indexOf('1 Bug') : t.indexOf(' Bug'); return idx >= 0 ? t.slice(idx, idx+4000) : 'not found'; }"
```

Then expand the Flags section by clicking it:

```bash
chrome-devtools evaluate_script "() => { const btn = [...document.querySelectorAll('button')].find(el => el.textContent.includes('Flags')); btn?.click(); return btn?.textContent?.trim(); }"
sleep 2
chrome-devtools evaluate_script "() => { const t = document.body.innerText; const idx = t.indexOf('Flags'); return t.slice(idx, idx+3000); }"
```

Parse the extracted text to build a findings list:

**Unresolved Bugs**: lines where `Bug  file:line` appears WITHOUT `• Resolved`

**Investigate flags**: lines with label `Investigate` (stop before `Informational` items)

**Skip**: Resolved bugs, and all Informational items.

### 4. Post Findings to GitHub

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

### 5. Report

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
