# 02 — Edge functions + AWS provisioning (Wave 1)

**Goal**: the five S3-brokering edge functions per CONTRACTS.md, and the checked-in AWS
provisioning script. Dev/test target is MinIO via the local stack (task 11); real AWS is a
deferred config swap (see the master checklist's deferred-infrastructure list).

**Dependencies**: CONTRACTS.md; task 11's docker-compose/env-var contract. Runs parallel to
01 (mock DB until 01 merges, then integrate). Owns `supabase/functions/**`, `server/provision-aws.*`.

## Steps
- [ ] `checkin-start`: membership + lock + base-version checks; new-book path (bookId null ⇒
      row locked-to-caller, versionless, invisible); diff proposed manifest vs current;
      transaction reuse/resume; credential issuance behind a small provider seam:
      **dev mode** (env-selected) returns static MinIO credentials, **production mode** does
      real STS via `bloom-teams-broker` with a per-request session policy scoped to the one
      book prefix — both in the identical response shape (CONTRACTS.md unchanged; clients
      can't tell the difference).
- [ ] `checkin-finish`: verify sha256 attributes; capture s3 version-ids; single DB tx
      (version metadata row, current-manifest replacement, book update, lock release, events,
      `.manifest.json` write); MissingOrBadUploads retry path; idempotent.
- [ ] `checkin-abort`; transaction expiry sweep (versionless-row reaping).
- [ ] `download-start`: read-only creds (`GetObject`+`GetObjectVersion`), collection scope.
- [ ] `collection-files-start/finish` with optimistic `expectedVersion`.
- [ ] `ClientOutOfDate` handling (client version in requests).
- [ ] `server/provision-aws` script (idempotent): buckets `bloom-teams-production|sandbox`,
      versioning ON, public access blocked, lifecycle (abort-multipart 7d; noncurrent expiry
      per the confirmed window), broker role + assume-only IAM user; document secrets setup
      (`supabase secrets set`). **Written and reviewed now; RUN later** when real AWS access
      exists — acceptance for this task does not require an AWS account.

## Acceptance
- Deno tests per function: happy path; lock-held; base-version-superseded; checksum failure
  (missing + wrong-content object); resume; expiry; new-book invisibility until commit.
- Invariant test: transaction lifetime < noncurrent-expiry floor (config assertion).

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
