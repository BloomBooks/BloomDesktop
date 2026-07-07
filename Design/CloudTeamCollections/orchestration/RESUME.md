# Cloud TC — agent orchestration & resume protocol

This folder holds the launch prompts for in-flight implementation tasks and the protocol
that makes them resumable across work sessions (including AI-session token limits).

## The durable-state rule

All task state lives in **git branches**, never in a conversation:

- One branch per task, named `task/<NN>-<name>`, based on `cloud-collections`. The
  currently in-flight set = whatever `git branch --list "task/*"` shows unmerged into
  `cloud-collections`. As of the Wave-2 launch that is `task/04-client-core` and
  `task/08-ui-collection-tab` (all Wave-0/1 branches are merged; see IMPLEMENTATION.md's
  Status + Merge log).
- Agents commit after EVERY completed checklist step — small, coherent commits; never one
  big commit at the end. Tick the step's checkbox in the task file in the same commit.
- Each task file ends with a `## Progress log` section; every commit appends/updates one
  line: `date · what was just completed · EXACT next action`. A resumer starts by reading
  this line.
- A step that is half-done at interruption is simply redone from its last commit — or, if
  the orchestrator finds uncommitted work in a leftover `.claude/worktrees/agent-*`
  worktree, it secures that as a WIP commit on the branch first (proven pattern).

## How to resume (human instructions)

1. Wait for usage limits to reset (session window), then start a **fresh** Claude Code
   session in this repo (cheaper than resuming the old conversation; everything needed is
   on disk and in Claude's project memory).
2. Say: **"Resume the Cloud Team Collections tasks per
   Design/CloudTeamCollections/orchestration/RESUME.md."**
3. The orchestrator will: find unmerged `task/*` branches and read their progress logs;
   secure any uncommitted worktree work as WIP commits; relaunch unfinished agents with
   their prompt files from this folder (prompts are resume-aware — they check for an
   existing branch first); review and merge finished branches into `cloud-collections`
   per IMPLEMENTATION.md rules; rebase onto origin/master at least daily.

## Orchestrator notes

- Launch prompts live in this folder, one per task (`<NN>-<name>.prompt.md`). Sonnet
  agents. Front-end/server-file tasks run in isolated worktrees; C#-building tasks run in
  the MAIN tree (worktrees lack initialized build deps) — one C# task at a time.
- Review before merge is MANDATORY (independently re-run the tests; see the merge log in
  IMPLEMENTATION.md for the kinds of bugs review has caught: SQL type bug, bad bcrypt
  hash, fake-session-token spec error, ungated UI section).
- The local dev stack must be up for server/C#-integration verification: `supabase start`
  + `docker-compose -f server/dev/docker-compose.yml up -d` (see server/dev/README.md;
  MinIO must be on the supabase network — the compose file handles this).
- Known environment quirks: pre-commit hook fails in worktrees (prettier manually +
  `--no-verify`, orchestrator re-verifies); Bloom.exe often running → apphost copy error
  MSB3027 is benign if test DLLs are fresh; edge-runtime containers must reach MinIO as
  `bloom-minio:9000`, never `host.containers.internal` (hangs under Podman).
