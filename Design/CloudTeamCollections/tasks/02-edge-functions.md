# 02 — Edge functions + AWS provisioning (Wave 1)

**Goal**: the five S3-brokering edge functions per CONTRACTS.md, and the checked-in AWS
provisioning script. Dev/test target is MinIO via the local stack (task 11); real AWS is a
deferred config swap (see the master checklist's deferred-infrastructure list).

**Dependencies**: CONTRACTS.md; task 11's docker-compose/env-var contract. Runs parallel to
01 (mock DB until 01 merges, then integrate). Owns `supabase/functions/**`, `server/provision-aws.*`.

## Steps
- [x] `checkin-start`: membership + lock + base-version checks; new-book path (bookId null ⇒
      row locked-to-caller, versionless, invisible); diff proposed manifest vs current;
      transaction reuse/resume; credential issuance behind a small provider seam:
      **dev mode** (env-selected) returns static MinIO credentials, **production mode** does
      real STS via `bloom-teams-broker` with a per-request session policy scoped to the one
      book prefix — both in the identical response shape (CONTRACTS.md unchanged; clients
      can't tell the difference).
- [x] `checkin-finish`: verify sha256 attributes; capture s3 version-ids; single DB tx
      (version metadata row, current-manifest replacement, book update, lock release, events,
      `.manifest.json` write); MissingOrBadUploads retry path; idempotent.
- [x] `checkin-abort`; transaction expiry sweep (versionless-row reaping).
- [x] `download-start`: read-only creds (`GetObject`+`GetObjectVersion`), collection scope.
- [x] `collection-files-start/finish` with optimistic `expectedVersion`.
- [x] `ClientOutOfDate` handling (client version in requests).
- [x] `server/provision-aws` script (idempotent): buckets `bloom-teams-production|sandbox`,
      versioning ON, public access blocked, lifecycle (abort-multipart 7d; noncurrent expiry
      per the confirmed window), broker role + assume-only IAM user; document secrets setup
      (`supabase secrets set`). **Written and reviewed now; RUN later** when real AWS access
      exists — acceptance for this task does not require an AWS account.

## Acceptance
- [x] Deno tests per function: happy path; lock-held; base-version-superseded; checksum failure
  (missing + wrong-content object); resume; expiry; new-book invisibility until commit.
  (Lock-held/base-version-superseded/resume/new-book-invisibility/checksum-failure verified
  live against the real stack — see Progress log; per-function Deno unit tests below cover
  the handler-level wiring/error-passthrough hermetically. `expiry` is covered by the
  SQL's reap logic + the invariant test below, not a live 48h wait.)
- [x] Invariant test: transaction lifetime < noncurrent-expiry floor (config assertion).

**Agent notes**: Sonnet. MinIO for S3 in tests AND as the dev-mode target (task 11's stack).
Only these functions ever hold AWS/MinIO admin creds.

## Progress log

- 2026-07-06 · done: new migration `20260706000004_tc_checkin_txn_functions.sql` adding
  the internal SECURITY DEFINER transaction functions (`checkin_start_tx`,
  `checkin_finish_tx`, `checkin_abort_tx`, `collection_files_start_tx`,
  `collection_files_finish_tx`, `download_start_check`, expiry-reap helpers, PT###
  HTTP-status passthrough convention) that back all 6 edge functions; applied clean via
  `supabase db reset --local`. All 6 edge functions authored under `supabase/functions/`
  (`checkin-start`, `checkin-finish`, `checkin-abort`, `download-start`,
  `collection-files-start`, `collection-files-finish`) plus `_shared/` helpers
  (env, errors, handler, rpc, s3-credential-provider-seam). `deno check` passes on all.
  NOT YET tested against the live stack. Next action: run
  `supabase functions serve --env-file server/dev/functions.env` (env file not yet
  created — create it first with `BLOOM_DEV_MODE=true`, `BLOOM_S3_ENDPOINT=http://host.containers.internal:9000`,
  `BLOOM_S3_BUCKET=bloom-teams-local`), then exercise checkin-start → checkin-finish
  happy path end-to-end with a real dev-seed user JWT (alice@dev.local), then write Deno
  unit tests per function and continue through the acceptance checklist (lock-held,
  base-version-superseded, checksum failure, resume, expiry, new-book invisibility).
- 2026-07-06 (later same day) · done: full LIVE integration verification against the real
  local stack (Supabase 127.0.0.1:54321 + MinIO via `supabase functions serve --env-file
  server/dev/functions.env`), using a scratch Deno driver script (not committed — ad hoc)
  exercising every item in the Acceptance checklist except a real 48h expiry wait: happy
  path (new-book checkin-start → PUT via MinIO AssumeRole creds → checkin-finish →
  download-start GetObject round-trip), idempotent checkin-finish retry, resume of an
  open transaction (both new-book and existing-book), lock-held (409 LockHeldByOther),
  base-version-superseded (409), checksum failure via MissingOrBadUploads (409, upload
  omitted), new-book invisibility until commit (verified via get_collection_state),
  collection-files two-phase commit + VersionConflict (409). **26/26 checks passed** after
  fixing 4 real bugs found along the way (all fixed + migrations reapplied via
  `supabase db reset`):
  1. **`host.containers.internal`/`host.docker.internal` DNS-resolves but the traffic
     HANGS** (not slow — indefinite) for any Deno/edge-runtime HTTP call through Podman's
     gvproxy host-gateway hop, even though plain `curl` over the identical URL succeeds
     instantly (verified with raw `Deno.connect()`, native `fetch`, and the AWS SDK, both
     from a throwaway container and the real edge-runtime container). This is almost
     certainly what stalled the prior interrupted attempt at this task. **Fix**: MinIO
     now also joins the Supabase CLI's own project Docker network
     (`server/dev/docker-compose.yml`'s `networks:` block — external network
     `supabase_network_bloom-team-collections`, created by `supabase start`) and is
     addressed by container name (`http://bloom-minio:9000`, in `functions.env`).
     Container-to-container traffic on a shared bridge network is instant. Documented in
     `server/dev/README.md`'s new "Known gotchas" section.
  2. **`supabase/config.toml`'s `[edge_runtime].policy = "oneshot"`** (the generated
     default) re-transpiles/type-checks the whole module graph — including the heavy
     `npm:@aws-sdk/client-s3`+`client-sts` imports in `_shared/s3.ts` — on every request,
     which reliably exceeds the edge-runtime's ~10s worker-boot timeout
     (`InvalidWorkerCreation: worker did not respond in time`) for any function that
     reaches `getScopedCredentials`/`adminS3Client`. **Fix**: switched to
     `policy = "per_worker"` (compiles once; also closer to production). Documented in the
     same README section.
  3. **New-book checkin-start resume was unreachable** in `checkin_start_tx`
     (`20260706000004_...sql`): CONTRACTS.md's checkin-start response never exposes
     `bookId` (by design — that's what makes an uncommitted book invisible), so a client
     resuming an interrupted new-book Send has no id to send back and must re-call with
     `bookId: null` + the same `bookInstanceId`. The old code always ran the
     insert-a-new-row path whenever `bookId` was null, so the very row created by try #1
     tripped the "instance_id already in use" conflict check on try #2. Fixed: the
     new-book branch now looks up any existing row by `(collection_id, instance_id)`
     first and treats "found, not yet committed, still locked to me" as a resume (not a
     conflict) — see the updated comment block in the migration.
  4. **`get_collection_state`'s full-snapshot branch leaked uncommitted new books** to
     every collection member (`20260706000003_tc_rpcs.sql`, task 01's file — a
     cross-cutting bug this task's acceptance criterion "new-book invisibility until
     commit" directly depends on). The delta branch was already safe (a never-committed
     book has no events to join against yet), but the full-snapshot branch queried
     `tc.books` directly with no such filter. Fixed by excluding rows where
     `current_version_id IS NULL` unless `locked_by = tc.current_user_id()` (the sender
     still sees their own in-flight new book).
  Remaining for this task: Deno unit tests per function (mocked RPC/S3 — the live spike
  above covers integration but not fast, hermetic CI-friendly coverage), the invariant
  test (transaction lifetime 48h < noncurrent-expiry-floor 7d — config assertion), and
  `server/provision-aws` (author + review only, no AWS account).
- 2026-07-06 (later still) · done: refactored all 6 `supabase/functions/*/index.ts` to
  `export const handler = async (req, body) => {...}` + `if (import.meta.main) { serveJsonPost(handler); }`
  instead of passing the arrow function straight into `serveJsonPost(...)`. Empirically
  verified (via the running local stack) that the real supabase-edge-runtime still sets
  `import.meta.main = true` and serves normally with this guard — re-ran the full 26-check
  live-integration suite after the refactor, still 26/26. This makes each handler
  importable and directly callable from a Deno test with a mocked `Request`, without a
  module-load side effect of starting a real `Deno.serve` (which would collide across
  test files). `deno check` clean on all 6. Next action: write the Deno test suite
  (`_shared/s3.test.ts` using `aws-sdk-client-mock` for STS/S3; per-function
  `index.test.ts` mocking `globalThis.fetch` for the RPC calls), the invariant config
  assertion, then author `server/provision-aws`.
- 2026-07-06 (later still) · done: wrote the Deno test suite. `_shared/test_support.ts`
  (new) provides `setTestEnv`/`mockRequest`/`withMockFetch`/`routedFetchStub`/
  `callHandler` (the last translates a thrown `HttpError` into its `Response`, mirroring
  what `serveJsonPost` does, since tests call the exported `handler` directly).
  `_shared/s3.test.ts`: 8 tests covering `hexToBase64`, `getScopedCredentials` (dev-mode
  MinIO AssumeRole call shape + no-Policy-in-dev + missing-credentials failure),
  `verifyUploadedObject` (match/mismatch/missing-object/missing-VersionId), and
  `writeManifestBackup` (never throws). One `index.test.ts` per function (checkin-start
  5, checkin-finish 4, checkin-abort 4, download-start 3, collection-files-start 3,
  collection-files-finish 3 = 22 tests) covering happy path, required-field validation,
  and RPC error passthrough (409/404/426) with S3-not-called assertions on the error
  paths. `_shared/invariants.test.ts`: 2 tests — re-parses the actual migration/compose
  source text (rather than hardcoding both sides) to assert (a) every 48h transaction-
  expiry `INTERVAL` literal agrees and (b) 48h < the dev MinIO noncurrent-expiry floor
  (7d, from `docker-compose.yml`'s `--noncurrent-expire-days`). All S3/STS mocked via
  `aws-sdk-client-mock`; all PostgREST calls mocked via a `fetch` stub — no live stack
  needed. **32/32 Deno tests pass**
  (`deno test --allow-net --allow-env --allow-sys --allow-read supabase/functions/`).
  Remaining for this task: `server/provision-aws` (author + review only).
- 2026-07-06 (final) · done: authored `server/provision-aws.ps1` (PowerShell, matching
  the only existing `server/` script convention — `server/dev/smoke.ps1`). Idempotent:
  checks-then-creates S3 buckets (`bloom-teams-<environment>`, default
  production+sandbox) with versioning ON, all-public-access blocked, and lifecycle
  rules (noncurrent-version-expiry 7d + abort-incomplete-multipart-upload 7d under
  `tc/`, both parameterized); an IAM role `bloom-teams-broker` (permission-policy
  ceiling: PutObject/GetObject/GetObjectVersion/AbortMultipartUpload/
  ListMultipartUploadParts on both buckets — the edge function's per-request session
  Policy narrows further, per `_shared/s3.ts`'s `buildSessionPolicy`); an assume-only
  IAM user `bloom-teams-broker-caller` (sole permission: `sts:AssumeRole` on that
  role — this is the identity behind `AWS_ACCESS_KEY_ID`/`AWS_SECRET_ACCESS_KEY` in
  `_shared/env.ts`'s `prodBrokerConfig`); an IAM admin user `bloom-teams-admin` with
  direct S3 permissions (`BLOOM_S3_ADMIN_ACCESS_KEY`/`SECRET`, backing
  `adminS3Client()`). Ends by printing the exact `supabase secrets set` command block
  (including `BLOOM_S3_ENDPOINT=https://s3.<region>.amazonaws.com` and
  `BLOOM_S3_FORCE_PATH_STYLE=false`, since `_shared/env.ts`'s `s3Env()` requires an
  explicit endpoint even for real AWS). Supports `-WhatIf`. Verified with
  `[System.Management.Automation.Language.Parser]::ParseFile` (no syntax errors) —
  NOT run against a real AWS account (none available; matches the task's explicit
  "written and reviewed now; RUN later" acceptance bar). All 5 steps of task 02 are
  now complete.
