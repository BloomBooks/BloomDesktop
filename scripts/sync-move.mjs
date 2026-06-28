#!/usr/bin/env node
/**
 * sync-move.mjs — move a work item to a new status in Orca, GitHub board, and/or YouTrack.
 * Zero AI tokens; all mappings are hardcoded.
 *
 * Usage:
 *   node scripts/sync-move.mjs orca <worktree-path> <status>
 *   node scripts/sync-move.mjs gh   <pr-number>     <status>
 *   node scripts/sync-move.mjs yt   <BL-xxxxx>      <status>
 *   node scripts/sync-move.mjs all  <pr-number>      <status>   ← syncs all three
 *
 * Status keys:
 *   waiting-ai    – Waiting for AI review
 *   in-review     – Ready for human review
 *   has-comments  – Reviewer left comments (author needs to act)
 *   completed     – Work done (PR merged/closed)
 *
 * Env vars required:
 *   YOUTRACK  – YouTrack permanent token (perm-...)
 */

import { execSync } from "child_process";

// ── GitHub project constants ──────────────────────────────────────────────────
const GH_OWNER = "BloomBooks";
const GH_PROJECT_NUMBER = 2;
const GH_PROJECT_ID = "PVT_kwDOAFlSFM4Bawkp";
const GH_STATUS_FIELD = "PVTSSF_lADOAFlSFM4BawkpzhVl0_w";

// GitHub board single-select option IDs (from `gh project field-list 2 --owner BloomBooks`)
const GH_OPTIONS = {
    "waiting-ai": "97860183", // Waiting for AI-Review
    "in-review": "05eedb52", // Ready for Human
    "has-comments": "99a3f545", // Has Comments
    completed: null, // auto-hidden when PR closes
};

// YouTrack state names (exact strings from the bundle)
const YT_STATES = {
    "waiting-ai": "In Progress",
    "in-review": "Ready For Code Review",
    "has-comments": "Has Comments",
    completed: null, // leave for human (type-dependent: bug→Closed, feature→Ready For Testing)
};

// Orca workspace-status IDs
const ORCA_STATUSES = {
    "waiting-ai": "status-5",
    "in-review": "in-review",
    "has-comments": "status-6",
    completed: "completed",
};

const YT_BASE = "https://issues.bloomlibrary.org/youtrack";
const YT_TOKEN = process.env.YOUTRACK;

// ── Helpers ───────────────────────────────────────────────────────────────────

function run(cmd) {
    return execSync(cmd, { encoding: "utf8" }).trim();
}

function validateStatus(status) {
    if (!(status in GH_OPTIONS)) {
        console.error(
            `Unknown status "${status}". Valid: ${Object.keys(GH_OPTIONS).join(" | ")}`,
        );
        process.exit(1);
    }
}

// ── Per-system move functions ─────────────────────────────────────────────────

async function moveOrca(worktreePath, status) {
    const s = ORCA_STATUSES[status];
    run(
        `orca worktree set --worktree path:${worktreePath} --workspace-status ${s} --json`,
    );
    console.log(`✓ Orca: ${worktreePath} → ${s}`);
}

async function moveGh(prNumber, status) {
    const optionId = GH_OPTIONS[status];
    if (!optionId) {
        console.log(
            `  GH: no board move for "${status}" (auto-handled by GitHub)`,
        );
        return;
    }

    // Look up the board item ID for this PR number
    const raw = run(
        `gh project item-list ${GH_PROJECT_NUMBER} --owner ${GH_OWNER} --format json --limit 200`,
    );
    const data = JSON.parse(raw);
    const item = data.items.find((i) => i.content?.number === Number(prNumber));
    if (!item) {
        console.warn(`  GH: PR #${prNumber} not found on board — skipping`);
        return;
    }

    run(
        `gh project item-edit --project-id ${GH_PROJECT_ID} --id ${item.id} --field-id ${GH_STATUS_FIELD} --single-select-option-id ${optionId}`,
    );
    console.log(`✓ GH board: PR #${prNumber} (${item.id}) → ${status}`);
}

async function moveYt(issueId, status) {
    const state = YT_STATES[status];
    if (!state) {
        console.log(`  YT: no move for "${status}" — leave for human judgment`);
        return;
    }
    if (!YT_TOKEN) throw new Error("YOUTRACK env var not set");

    const res = await fetch(`${YT_BASE}/api/commands`, {
        method: "POST",
        headers: {
            Authorization: `Bearer ${YT_TOKEN}`,
            "Content-Type": "application/json",
            Accept: "application/json",
        },
        body: JSON.stringify({
            query: `State ${state}`,
            issues: [{ idReadable: issueId }],
        }),
    });

    if (!res.ok) {
        const err = await res.text();
        throw new Error(`YouTrack ${res.status}: ${err}`);
    }
    console.log(`✓ YouTrack: ${issueId} → ${state}`);
}

// ── "all" — move all three systems from a PR number ───────────────────────────

async function moveAll(prNumber, status) {
    // 1. Find the Orca worktree linked to this PR
    const wtRaw = run(`orca worktree list --json`);
    const wtData = JSON.parse(wtRaw);
    const wt = wtData.result.worktrees.find(
        (w) => w.linkedPR === Number(prNumber),
    );

    // 2. Extract BL-xxxxx from branch name, display name, or PR title (in that order)
    let ytIssue = null;
    if (wt) {
        const branch = wt.git?.branch ?? "";
        const name = wt.displayName ?? "";
        const m = (branch + " " + name).match(/BL-\d+/);
        if (m) ytIssue = m[0];
    }
    if (!ytIssue) {
        try {
            // Search PR title then body for BL-xxxxx
            const pr = run(
                `gh pr view ${prNumber} --repo ${GH_OWNER}/BloomDesktop --json title,body`,
            );
            const { title = "", body = "" } = JSON.parse(pr);
            const m = (title + " " + body).match(/BL-\d+/);
            if (m) ytIssue = m[0];
        } catch {
            /* gh not available or PR not found — skip */
        }
    }

    // 3. Move each system
    await moveGh(prNumber, status);

    if (wt) {
        await moveOrca(wt.path, status);
    } else {
        console.warn(
            `  Orca: no worktree with linkedPR=${prNumber} — skipping`,
        );
    }

    if (ytIssue) {
        await moveYt(ytIssue, status);
    } else {
        console.warn(
            `  YT: no BL-xxxxx found for PR #${prNumber} — skipping YouTrack`,
        );
    }
}

// ── Main ──────────────────────────────────────────────────────────────────────

const [, , cmd, target, status] = process.argv;

if (!cmd || !target || !status) {
    console.error(
        [
            "Usage: node scripts/sync-move.mjs <cmd> <target> <status>",
            "  cmd:    orca | gh | yt | all",
            "  status: waiting-ai | in-review | has-comments | completed",
            "Examples:",
            "  node scripts/sync-move.mjs orca D:/bloom.worktrees/BL-16467 has-comments",
            "  node scripts/sync-move.mjs gh   7994            has-comments",
            "  node scripts/sync-move.mjs yt   BL-16467        has-comments",
            "  node scripts/sync-move.mjs all  7994            has-comments",
        ].join("\n"),
    );
    process.exit(1);
}

validateStatus(status);

const handlers = { orca: moveOrca, gh: moveGh, yt: moveYt, all: moveAll };
if (!(cmd in handlers)) {
    console.error(`Unknown command "${cmd}". Use: orca | gh | yt | all`);
    process.exit(1);
}

try {
    await handlers[cmd](target, status);
} catch (e) {
    console.error("Error:", e.message);
    process.exit(1);
}
