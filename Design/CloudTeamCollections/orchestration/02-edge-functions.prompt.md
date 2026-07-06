# Agent prompt — task 02: edge functions (resume-aware)

You are implementing task 02 of the Cloud Team Collections plan in an isolated git
worktree of c:\github\BloomDesktop.

**Resume check (do this FIRST):** if branch `task/02-edge-functions` exists, check it out
(`git checkout task/02-edge-functions`), read the `## Progress log` at the bottom of
`Design/CloudTeamCollections/tasks/02-edge-functions.md`, and continue from the recorded
next action. Otherwise `git checkout -b task/02-edge-functions` (you start from the
`cloud-collections` state).

**Durability protocol (mandatory, from orchestration/RESUME.md):** commit after EVERY
completed step — small coherent commits, descriptive messages ending
"Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>". In the same commit: tick that
step's checkbox in the task file AND update its `## Progress log` (create the section if
missing) with `date · done · exact next action`. Never leave >1 step uncommitted.
NOTE: the pre-commit hook may fail in a worktree ("husky-run not found"); if it does,
commit with `--no-verify` (your files are SQL/TS/MD only — the hook is a JS/C# formatter;
the orchestrator re-verifies at merge).

**Read first:** `Design/CloudTeamCollections/tasks/02-edge-functions.md` (authoritative
steps), `Design/CloudTeamCollections/CONTRACTS.md` (v1.1 — frozen shapes),
`server/dev/DEV-CREDENTIALS.md` (CRITICAL correction: MinIO VALIDATES session tokens —
dev credential mode must mint real temporary creds via MinIO's AssumeRole STS API, never
fabricate a token), `server/dev/README.md`, and the schema/RPCs in `supabase/migrations/`.

**Environment (all verified working on this machine):** local Supabase stack is running
(`supabase start`: API http://127.0.0.1:54321, DB 54322; anon/service keys via
`supabase status`). MinIO runs at http://localhost:9000 (bucket `bloom-teams-local`,
versioning ON, creds minioadmin/minioadmin). Deno 2.9 is installed. Serve functions with
`supabase functions serve`. Container networking wrinkle: from INSIDE the edge-runtime
container, MinIO is not `localhost` — try `host.containers.internal` (Podman) or
`host.docker.internal`; make the endpoint an env/secret (`BLOOM_S3_ENDPOINT`) and document
what actually worked in server/dev/README.md.

**Scope:** the five functions per CONTRACTS.md (checkin-start/finish/abort,
download-start, collection-files-start/finish) under `supabase/functions/`, plus the
`server/provision-aws` script (authored + reviewed only — no AWS account available; do
not attempt to run it). Deno tests per function: unit tests with mocked S3/DB are fine
(aws-sdk-client-mock is available on npm if useful for the JS AWS SDK), but where cheap,
run integration checks against the LIVE local stack — it is up. Only these functions ever
hold S3 admin credentials.

**Final report (raw data):** branch + shas; per-function status (authored/tested, which
tests ran vs mocked); the MinIO-endpoint networking answer; any contract ambiguities; the
exact next action if you did not finish.
