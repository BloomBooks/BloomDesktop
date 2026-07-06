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
