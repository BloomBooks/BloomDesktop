# 02 — Edge functions + AWS provisioning (Wave 1)

**Goal**: the five S3-brokering edge functions per CONTRACTS.md, and the checked-in AWS
provisioning script.

**Dependencies**: CONTRACTS.md. Runs parallel to 01 (mock DB until 01 merges, then integrate).
Owns `supabase/functions/**`, `server/provision-aws.*`.

## Steps
- [ ] `checkin-start`: membership + lock + base-version checks; new-book path (bookId null ⇒
      row locked-to-caller, versionless, invisible); diff proposed manifest vs current;
      transaction reuse/resume; STS creds via `bloom-teams-broker` with per-request session
      policy scoped to the one book prefix.
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
      (`supabase secrets set`).

## Acceptance
- Deno tests per function: happy path; lock-held; base-version-superseded; checksum failure
  (missing + wrong-content object); resume; expiry; new-book invisibility until commit.
- Invariant test: transaction lifetime < noncurrent-expiry floor (config assertion).

**Agent notes**: Sonnet. MinIO for S3 in tests. Only these functions ever hold AWS creds.
