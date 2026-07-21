# Cloud TC — orchestration rules & resume protocol

> **Current state lives in [DOGFOOD-BATCH-1.md](DOGFOOD-BATCH-1.md)** (its progress log's
> newest entry is the resume point). This file keeps the general working rules that batch
> doc and IMPLEMENTATION.md refer to.
>
> History note (15 Jul 2026): this folder originally also held the per-task agent launch
> prompts (`<NN>-<name>.prompt.md`) used to build the feature in waves. All tasks are long
> merged, so those scratch prompts were removed; the durable per-task specs and findings
> remain in `../tasks/*.md`, and the wave-by-wave merge log is in `../IMPLEMENTATION.md`.

## The durable-state rule

All work state lives in **git**, never only in a conversation:

- Commit after EVERY completed step — small, coherent commits; tick the step's checkbox /
  update the item's `Status:` line in the same commit.
- The state doc (currently DOGFOOD-BATCH-1.md) ends with a `## Progress log`; every work
  session appends: `date · what was just completed · EXACT next action`. A resumer starts
  by reading the newest entry.
- A step half-done at interruption is redone from its last commit; uncommitted work found
  in a leftover worktree is secured as a WIP commit first.

## Working rules (still operative)

- Review before merge is MANDATORY (independently re-run the tests; see the merge log in
  IMPLEMENTATION.md for the kinds of bugs review has caught: SQL type bug, bad bcrypt
  hash, fake-session-token spec error, ungated UI section, JSON-null claimed bug).
- C# test filter for cloud work MUST be
  `"FullyQualifiedName~Cloud|FullyQualifiedName~TeamCollection|FullyQualifiedName~SharingApi"`
  (exclude `~LiveTests` unless the stack is up): SharingApiTests live under
  web.controllers and match NEITHER ~Cloud NOR ~TeamCollection — that gap let a real bug
  merge with "all green" claims (7 Jul).
- The local dev stack must be up for server/C#-integration verification: `supabase start`
  + `docker-compose -f server/dev/docker-compose.yml up -d` (see server/dev/README.md;
  MinIO must be on the supabase network — the compose file handles this).
- Known environment quirks: pre-commit hook fails in worktrees (prettier manually +
  `--no-verify`, orchestrator re-verifies); Bloom.exe often running → apphost copy error
  MSB3027 is benign if test DLLs are fresh (or use `build/agent-dotnet.sh`, which builds
  into a private tree and avoids the lock entirely); edge-runtime containers must reach
  MinIO as `bloom-minio:9000`, never `host.containers.internal` (hangs under Podman).

## How to resume (human instructions)

Start a fresh Claude Code session in this repo and say: **"Resume the dogfood batch per
Design/CloudTeamCollections/orchestration/DOGFOOD-BATCH-1.md."** The resumer reads that
file's newest progress-log entry and item Status lines, secures any uncommitted work, and
continues with the stated next action.
