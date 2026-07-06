# Cloud Team Collections — implementation master checklist

Design: [../CloudTeamCollections.md](../CloudTeamCollections.md) · Contracts: [CONTRACTS.md](CONTRACTS.md)
Rules: agents tick checkboxes **only in their own task file**; this master file is updated
**only by the orchestrator**. Every task PR must build, pass its acceptance tests, and pass
the entire existing folder-TC test suite.

## Local-first development strategy

All development and testing through Wave 4 runs against a **fully local stack**: no real S3
bucket, no hosted Supabase project, and no Firebase/BloomLibrary auth changes are needed to
start. Setup and details live in [tasks/11-local-dev-stack.md](tasks/11-local-dev-stack.md).

- **Database + API**: local Supabase (`supabase start`, Docker) — the identical Postgres
  schema, RLS, RPCs, edge functions (`supabase functions serve`), and realtime that production
  will use. Migrations written now ARE the production migrations. (SQLite rejected: it has no
  PostgREST/RLS/edge-function equivalents, so we would build a parallel backend and then throw
  it away; local Supabase is the product stack itself.)
- **S3 substitute**: MinIO in Docker — an S3-compatible server that stores objects on the
  local file system, with object versioning and checksum support. `BloomS3Client` /
  TransferUtility talk to it through a service-URL override; nothing else in the client
  changes. In dev mode the edge functions return static MinIO credentials **in the same JSON
  shape as the production STS response**, so CONTRACTS.md is unchanged (per-book credential
  scoping is a production security measure, not a functional dependency).
- **Auth substitute**: local GoTrue (bundled with local Supabase) email/password with
  auto-confirm — any email + password signs up/in as a valid, verified user ("a backend that
  accepts any login"), yielding real JWTs so RLS and every RPC run unchanged. Real
  BloomLibrary/Firebase sign-in (Option A/B/C) plugs in later behind the existing `CloudAuth`
  seam; the server side isolates the token-shape difference (Firebase `email_verified` claim
  vs GoTrue confirmation) in one SQL helper, `tc.jwt_email_verified()`.
- **Two instances on one machine**: each Bloom instance gets its own collection folder plus
  `BLOOM_CLOUDTC_USER` / `BLOOM_CLOUDTC_PASSWORD` env-var overrides so it runs as a distinct
  dev identity (bypassing the shared stored-token settings). This is the Wave 3 manual smoke
  and the mechanism the Wave 4 E2E harness scales up.
- **Environment switching**: every external endpoint (Supabase URL, anon key, S3
  endpoint/bucket/path-style, auth mode) resolves through one `CloudEnvironment` config
  (env vars over compiled defaults; owned by task 03). Cutover to the real bucket, hosted
  Supabase, and real sign-in is configuration plus the deferred-infrastructure list below —
  zero protocol or schema change.

## Branching

- Integration branch: `cloud-collections` (base branch: **confirm with John** — master vs the
  active Version6.x branch). Base merged into integration weekly.
- One branch + one git worktree per task; PRs into the integration branch, merged one at a
  time by the orchestrator after code review.

## Waves

| Wave | Tasks | Parallel? | Gate |
|------|-------|-----------|------|
| 0 | [00-enablers](tasks/00-enablers.md) | No — orchestrator-led (shared hot files) | Existing TC suite green, zero behavior change |
| 1 | [11-local-dev-stack](tasks/11-local-dev-stack.md) · [01-server-schema](tasks/01-server-schema.md) · [02-edge-functions](tasks/02-edge-functions.md) · [03-auth](tasks/03-auth.md) · [07-ui-setup](tasks/07-ui-setup.md) | Yes — zero file overlap (contracts frozen first) | Each task's acceptance tests; 11's stack-smoke script green |
| 2 | [04-client-core](tasks/04-client-core.md) · [08-ui-collection-tab](tasks/08-ui-collection-tab.md) | Yes | Unit suites green |
| 3 | [05-cloud-backend](tasks/05-cloud-backend.md) → [06-api-endpoints](tasks/06-api-endpoints.md) → UI wiring | **Sequenced** (shared files) | Two-instance smoke on ONE machine against the local stack |
| 4 | [09-e2e](tasks/09-e2e.md) · [10-adoption](tasks/10-adoption.md) | Yes | Full E2E matrix green against the local stack; dogfood |

## Shared-file schedule (no two concurrent tasks may touch the same one)

| File | Owner |
|------|-------|
| TeamCollection.cs, TeamCollectionManager.cs | Wave 0 only (orchestrator) |
| TeamCollectionApi.cs | 06 only |
| CollectionChooserDialog | 07 only |
| FeatureRegistry.cs, BloomExe.csproj | Orchestrator at merge time |
| supabase/** | 01/02 (01 owns migrations; 02 owns functions/); 11 owns config.toml auth/dev settings + seed |
| server/dev/** (docker-compose, seeds, smoke script, docs) | 11 only |
| Cloud/CloudEnvironment.cs | 03 only |

## Deferred until real infrastructure is available (tracked, NOT blocking)

Each of these is a config/provisioning swap, not a code change, thanks to the seams above.

- [ ] Auth Option A/B/C decision (colleague review) and, for Option A: the BloomLibrary2
      `src/editor.ts` token-forwarding change + the Firebase Admin claim function (other repos).
      Then: implement the real `CloudAuth` provider behind the existing interface.
- [ ] Run `server/provision-aws` (script is written and reviewed in task 02) against real AWS:
      buckets, versioning, lifecycle, `bloom-teams-broker` role, assume-only IAM user.
- [ ] Create hosted Supabase projects (production + sandbox); `supabase db push` the same
      migrations; deploy the same edge functions; `supabase secrets set` the AWS credentials.
- [ ] Flip edge functions from static-MinIO-credential dev mode to real STS (env switch).
- [ ] Re-verify MinIO/AWS parity assumptions against the real bucket (sha256 checksum headers,
      s3 version-id capture, lifecycle behavior) and re-run the E2E matrix against sandbox.

## Status

- [x] Wave 0 complete (folder backend provably unchanged — 208/208 TC tests green)
- [ ] Wave 1 complete (incl. local dev stack up: `supabase start` + MinIO + dev logins)
  - [x] 01-server-schema authored + reviewed (pgTAP unrun — awaiting Docker)
  - [x] 11-local-dev-stack authored + reviewed (smoke/parity unrun — awaiting Docker)
  - [ ] 02-edge-functions
  - [ ] 03-auth
  - [ ] 07-ui-setup (shells)
  - [ ] Docker Desktop installed → run pgTAP, seed sign-in check, smoke, parity spike
- [ ] Wave 2 complete
- [ ] Wave 3 complete (two-instance same-machine smoke against local stack)
- [ ] Wave 4 complete
- [ ] Real-infrastructure cutover complete (deferred list above)
- [ ] Auth option decided (colleague review — see design doc Open items; **not blocking** —
      dev auth provider ships first)
- [ ] Safety-window duration confirmed (7 days vs 1 day)

## Merge log

(orchestrator appends: date · task · PR · notes)

- 6 Jul 2026 · 00-enablers · merged locally · Reviewed diff line-by-line; seams preserve
  folder behavior; 208/208 TeamCollection tests (24 new) verified against fresh binaries.
  Note: TryLockInRepo gained a BookStatus param vs the task file (avoids redundant GetStatus).
- 6 Jul 2026 · 01-server-schema · merged locally · Orchestrator fixes: checkout_book
  ROW_COUNT type bug; pgTAP errcode; realtime pg_notify→realtime.send TODO (wave 4);
  seed wiring in config.toml. CONTRACTS.md bumped to v1.1 (p_ arg prefix; Content-Profile
  header). pgTAP suite authored but UNRUN (no Docker yet).
- 6 Jul 2026 · 11-local-dev-stack · merged locally · Orchestrator fix: seed bcrypt hash was
  invalid (verified with bcryptjs); replaced with a self-verified hash. Smoke script and
  parity spike authored but UNRUN (no Docker yet); parity-check compiles clean.
