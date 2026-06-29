/**
 * sync-constants.mjs — shared status mappings for the PR-sync scripts.
 *
 * Imported by both sync-move.mjs and sync-pr-status.mjs so the two can never
 * drift out of sync (e.g. a status rename fixed in one file but not the other).
 * Zero AI tokens; all mappings are hardcoded.
 */

// ── GitHub project constants ──────────────────────────────────────────────────
export const GH_OWNER = "BloomBooks";
export const GH_PROJECT_NUMBER = 2;

// ── Internal key → Orca workspaceStatus value ─────────────────────────────────
export const ORCA_STATUSES = {
    "waiting-ai": "status-5",
    "in-review": "in-review",
    "has-comments": "status-6",
    completed: "completed",
};

// ── Internal key → YouTrack state name (null = leave for human judgment) ───────
export const YT_STATES = {
    "waiting-ai": "In Progress",
    "in-review": "Ready For Code Review",
    "has-comments": "Has Comments",
    completed: null, // type-dependent: bug→Closed, feature→Ready For Testing
};

export const YT_BASE = "https://issues.bloomlibrary.org/youtrack";
