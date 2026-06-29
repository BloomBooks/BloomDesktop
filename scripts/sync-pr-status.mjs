#!/usr/bin/env node
/**
 * sync-pr-status.mjs — reconcile Orca worktree statuses with the GitHub board.
 * Zero AI tokens; all mappings are hardcoded.
 *
 * Run automatically every 15 minutes via Orca automation, or manually.
 *
 * Env vars required:
 *   YOUTRACK  – YouTrack permanent token (perm-...)
 */

import { execSync } from "child_process";

const GH_OWNER = "BloomBooks";
const GH_PROJECT_NUMBER = 2;

// GitHub board status label → internal key
const GH_STATUS_TO_KEY = {
    "Waiting for AI-Review": "waiting-ai",
    "Ready for Human": "in-review",
    "Has Comments": "has-comments",
};

// Internal key → Orca workspaceStatus value
const ORCA_STATUSES = {
    "waiting-ai": "status-5",
    "in-review": "in-review",
    "has-comments": "status-6",
    completed: "completed",
};

// Internal key → YouTrack state name (null = leave for human)
const YT_STATES = {
    "waiting-ai": "In Progress",
    "in-review": "Ready For Code Review",
    "has-comments": "Has Comments",
    completed: null,
};

const YT_BASE = "https://issues.bloomlibrary.org/youtrack";
const YT_TOKEN = process.env.YOUTRACK;

function run(cmd) {
    return execSync(cmd, { encoding: "utf8" }).trim();
}

/** Return the first BL-xxxxx found in a string, or null. */
function extractBl(text) {
    const m = (text ?? "").match(/BL-\d+/);
    return m ? m[0] : null;
}

async function setOrcaStatus(worktreePath, statusKey) {
    run(
        `orca worktree set --worktree "path:${worktreePath}" --workspace-status ${ORCA_STATUSES[statusKey]}`,
    );
}

/** Read an issue's current State field value (name), or null if unset/unknown. */
async function getYtState(issueId) {
    const res = await fetch(
        `${YT_BASE}/api/issues/${issueId}?fields=customFields(name,value(name))`,
        {
            headers: {
                Authorization: `Bearer ${YT_TOKEN}`,
                Accept: "application/json",
            },
        },
    );
    if (!res.ok) {
        throw new Error(`YouTrack GET ${res.status}: ${await res.text()}`);
    }
    const data = await res.json();
    const stateField = data.customFields?.find((f) => f.name === "State");
    return stateField?.value?.name ?? null;
}

async function setYtState(issueId, statusKey) {
    const state = YT_STATES[statusKey];
    if (!state) return false;
    if (!YT_TOKEN) {
        console.warn("  YT: YOUTRACK env var not set — skipping");
        return false;
    }

    // Idempotent: only issue the command if the issue isn't already in the
    // target state. This lets a later reconciliation cycle self-heal a
    // YouTrack update that failed transiently on an earlier cycle, without
    // re-applying (and re-logging activity for) state that's already correct.
    if ((await getYtState(issueId)) === state) return false;

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
    return true;
}

async function main() {
    // 1. Orca worktrees that have a linked PR
    const wtRaw = run("orca worktree list --json");
    const wtData = JSON.parse(wtRaw);
    const linked = wtData.result.worktrees.filter((w) => w.linkedPR != null);

    if (!linked.length) {
        console.log("No Orca worktrees with linkedPR — nothing to sync");
        return;
    }
    console.log(`Checking ${linked.length} Orca worktree(s) with linked PRs\n`);

    // 2. GitHub board status by PR number (single batch call)
    const boardRaw = run(
        `gh project item-list ${GH_PROJECT_NUMBER} --owner ${GH_OWNER} --format json --limit 500`,
    );
    const boardData = JSON.parse(boardRaw);
    const boardByPr = {};
    for (const item of boardData.items) {
        if (item.content?.number) {
            boardByPr[item.content.number] = item.status;
        }
    }

    // 3. Process each linked worktree
    let changed = 0;
    for (const wt of linked) {
        const prNum = wt.linkedPR;
        const currentOrcaStatus = wt.workspaceStatus;
        const label = `PR #${prNum} (${wt.displayName})`;

        let targetKey;
        const boardStatus = boardByPr[prNum];

        if (!boardStatus) {
            // Not on board — may be merged/closed; check GitHub directly
            try {
                const prJson = run(
                    `gh pr view ${prNum} --repo ${GH_OWNER}/BloomDesktop --json state`,
                );
                const { state } = JSON.parse(prJson);
                if (state === "MERGED" || state === "CLOSED") {
                    targetKey = "completed";
                } else {
                    // Open but not yet on the board — skip
                    console.log(`  ${label}: open, not on board — skipping`);
                    continue;
                }
            } catch {
                console.warn(`  ${label}: gh pr view failed — skipping`);
                continue;
            }
        } else {
            targetKey = GH_STATUS_TO_KEY[boardStatus];
            if (!targetKey) {
                console.log(
                    `  ${label}: unrecognized board status "${boardStatus}" — skipping`,
                );
                continue;
            }
        }

        const targetOrcaStatus = ORCA_STATUSES[targetKey];
        const orcaInSync = currentOrcaStatus === targetOrcaStatus;

        // Resolve BL# for YouTrack: branch → display name → PR title/body.
        // We resolve it even when Orca is already in sync, because YouTrack is
        // reconciled independently below (see "Apply YouTrack update").
        let ytIssue = extractBl(wt.git?.branch) ?? extractBl(wt.displayName);
        if (!ytIssue) {
            try {
                const pr = run(
                    `gh pr view ${prNum} --repo ${GH_OWNER}/BloomDesktop --json title,body`,
                );
                const { title = "", body = "" } = JSON.parse(pr);
                ytIssue = extractBl(title + " " + body);
            } catch {
                /* skip */
            }
        }

        // Apply Orca update (skip the write when it already matches, but still
        // fall through to reconcile YouTrack — a matching Orca status does NOT
        // imply YouTrack succeeded on a previous cycle).
        if (orcaInSync) {
            console.log(`  ${label}: Orca already ${currentOrcaStatus} ✓`);
        } else {
            try {
                await setOrcaStatus(wt.path, targetKey);
                console.log(
                    `✓ ${label}: Orca ${currentOrcaStatus} → ${targetOrcaStatus}`,
                );
                changed++;
            } catch (e) {
                console.error(`  ${label}: Orca update failed: ${e.message}`);
                continue;
            }
        }

        // Apply YouTrack update
        if (ytIssue) {
            try {
                const moved = await setYtState(ytIssue, targetKey);
                if (moved)
                    console.log(
                        `  ✓ YouTrack ${ytIssue} → ${YT_STATES[targetKey]}`,
                    );
            } catch (e) {
                console.error(`  YouTrack ${ytIssue}: ${e.message}`);
            }
        } else {
            console.log(
                `  YT: no BL-xxxxx for PR #${prNum} — skipping YouTrack`,
            );
        }
    }

    console.log(`\nDone. ${changed} worktree(s) updated.`);
}

main().catch((e) => {
    console.error("Fatal:", e.message);
    process.exit(1);
});
