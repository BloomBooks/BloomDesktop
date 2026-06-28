---
name: pr-kanban-sync
description: State machine and scripts for keeping Orca board, GitHub Projects board, and YouTrack in sync as PRs move through review stages. Use these scripts instead of raw GraphQL or direct CLI calls whenever moving a PR between stages.
argument-hint: "status to move to: waiting-ai | in-review | has-comments | completed"
user-invocable: true
---

# PR Kanban Sync

## Overview

Three systems track the same PRs. They must stay aligned:

| State key | Orca column | GitHub board | YouTrack |
|---|---|---|---|
| `waiting-ai` | Waiting on AI Review | Waiting for AI-Review | In Progress |
| `in-review` | In human review | Ready for Human | Ready For Code Review |
| `has-comments` | Has Comments | Has Comments | Has Comments |
| `completed` | completed (archived) | auto-hidden on merge | leave for human |

## Scripts

Both scripts live at `scripts/` in the repo root and require Node.js 18+.

### `sync-move.mjs` — move one item manually

```bash
# Move all three systems at once (preferred):
node scripts/sync-move.mjs all  <pr-number>   <status-key>

# Individual systems:
node scripts/sync-move.mjs gh   <pr-number>   <status-key>
node scripts/sync-move.mjs orca <worktree-path> <status-key>
node scripts/sync-move.mjs yt   <BL-xxxxx>    <status-key>
```

`all` resolves the Orca worktree via `linkedPR` and the YouTrack issue via BL# from branch → display name → PR title → PR body. If either lookup fails it warns and skips that system; use the individual subcommands as fallback.

Requires env var `YOUTRACK` (YouTrack permanent token).

### `sync-pr-status.mjs` — polling reconciler (zero tokens)

```bash
node scripts/sync-pr-status.mjs
```

Reads all Orca worktrees with `linkedPR` set, fetches GitHub board status in one batch call, and updates any Orca worktrees + YouTrack issues that are out of sync. Intended to run every 15 minutes via Orca automation (see **Automation** below). Also safe to run manually to force a re-sync.

## State Machine

```
[PR opened / first push]
  → node scripts/sync-move.mjs all <pr> waiting-ai

[Devin and CI finish cleanly → ready for human]
  → node scripts/sync-move.mjs all <pr> in-review

[Human reviewer: CHANGES_REQUESTED]
  → node scripts/sync-move.mjs all <pr> has-comments

[Author pushes new commits while in has-comments or in-review]
  → node scripts/sync-move.mjs all <pr> waiting-ai  (restart bot-wait)

[PR merged or closed]
  → node scripts/sync-move.mjs all <pr> completed
  (YouTrack is skipped for completed — type-dependent, leave for human)
```

## GitHub Board Reference

Project: **PR Review Tracker** — https://github.com/orgs/BloomBooks/projects/2

GraphQL IDs (hardcoded in `sync-move.mjs` — do not re-query unless they stop working):
- Project ID: `PVT_kwDOAFlSFM4Bawkp`
- Status field ID: `PVTSSF_lADOAFlSFM4BawkpzhVl0_w`
- Option IDs: `97860183` (Waiting for AI-Review), `05eedb52` (Ready for Human), `99a3f545` (Has Comments)

## Adding a PR to the Board

`sync-move.mjs` only updates items already on the board. To add a new PR:

```bash
PR_NODE_ID=$(gh pr view <number> --repo BloomBooks/BloomDesktop --json id --jq '.id')
gh api graphql -f query="mutation {
  addProjectV2ItemById(input: {
    projectId: \"PVT_kwDOAFlSFM4Bawkp\"
    contentId: \"$PR_NODE_ID\"
  }) { item { id } }
}"
# Then immediately set its status:
node scripts/sync-move.mjs gh <pr-number> waiting-ai
```

## Automation (15-minute polling)

Register once per machine (the automation targets the `master-2` worktree as a stable shell host):

```powershell
orca automations create `
  --name "Sync PR Status" `
  --trigger "*/15 * * * *" `
  --workspace "path:D:/orca-worktrees/bloom/master-2" `
  --reuse-session `
  --prompt "Run this command and report its output: node scripts/sync-pr-status.mjs" `
  --provider claude `
  --json
```

To list or remove:
```bash
orca automations list --json
orca automations remove <automationId> --json
```

## Known Limitations

- `all` and the polling script only update Orca if `linkedPR` is set on the worktree. Orca sets this automatically when a PR is opened through its UI. If you created the PR via `gh pr create`, link it manually:
  ```bash
  # Not yet supported by orca CLI — use individual subcommands as fallback
  node scripts/sync-move.mjs gh <pr> <status>
  orca worktree set --worktree active --workspace-status <orca-status>
  node scripts/sync-move.mjs yt <BL-xxxxx> <status>
  ```
- YouTrack is skipped if no `BL-xxxxx` appears anywhere in the branch name, display name, PR title, or PR body.
- The `completed` transition leaves YouTrack alone (bug vs feature determines the final state there).
