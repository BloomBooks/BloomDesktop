# Cloud TC — agent orchestration & resume protocol

This folder holds the launch prompts for in-flight implementation tasks and the protocol
that makes them resumable across work sessions (including AI-session token limits).

## The durable-state rule

All task state lives in **git branches**, never in a conversation:

- One branch per task: `task/02-edge-functions`, `task/03-auth`, `task/07-ui-setup`
  (based on `cloud-collections`).
- Agents commit after EVERY completed checklist step — small, coherent commits; never one
  big commit at the end. Tick the step's checkbox in the task file in the same commit.
- Each task file ends with a `## Progress log` section; every commit appends/updates one
  line: `date · what was just completed · EXACT next action`. A resumer starts by reading
  this line.
- A step that is half-done at interruption is simply redone from its last commit.

## How to resume (human instructions)

1. Wait for usage limits to reset (session window), then start a **fresh** Claude Code
   session in this repo (cheaper than resuming the old conversation; everything needed is
   on disk and in Claude's project memory).
2. Say: **"Resume the Cloud Team Collections wave-1 tasks per
   Design/CloudTeamCollections/orchestration/RESUME.md."**
3. The orchestrator will: check each `task/*` branch and its progress log; relaunch any
   unfinished agent with its prompt file from this folder (the prompts are
   resume-aware — they check for an existing branch first); review and merge finished
   branches into `cloud-collections` per IMPLEMENTATION.md rules.

## Orchestrator notes

- Launch prompts: `02-edge-functions.prompt.md`, `03-auth.prompt.md`,
  `07-ui-setup.prompt.md` (this folder). Sonnet agents; 02 and 07 in isolated worktrees,
  03 in the main tree (needs initialized C# build deps).
- Review before merge is MANDATORY (run the tests yourself; see the merge log in
  IMPLEMENTATION.md for the kinds of bugs review has caught).
- The local dev stack must be up for 02/03 verification: `supabase start` +
  `docker-compose -f server/dev/docker-compose.yml up -d` (see server/dev/README.md).
