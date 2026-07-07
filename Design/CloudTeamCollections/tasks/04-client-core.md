# 04 — Client core: cache, manifest, transfer (Wave 2)

**Goal**: the data plumbing `CloudTeamCollection` will sit on.

**Dependencies**: 03 (client/auth), CONTRACTS.md. Owns new files
`src/BloomExe/TeamCollection/Cloud/CloudRepoCache.cs`, `BookVersionManifest.cs`,
`CloudBookTransfer.cs`.

## Steps
- [x] `CloudRepoCache`: thread-safe in-memory book/lock/version map + collection-file record +
      `last_seen_event_id`; persisted snapshot (`.bloom-cloud-repo-cache.json` in the local
      collection folder, excluded from book enumeration); full-snapshot + delta application;
      write-through from mutating RPC results. Never trusted for mutations.
- [x] `BookVersionManifest`: model (path → sha256, size, s3VersionId), NFC path normalization,
      junk/derived-file exclusion list (reuse the publish path's filters), local-folder diff
      (changed/added/removed/unchanged) with hash computation reuse (`MakeChecksum` internals /
      `Book.ComputeHashForAllBookRelatedFiles`).
- [x] `CloudBookTransfer`: hash-skip uploads (parallel PUTs with checksum headers, byte
      progress via IProgress) and downloads **by pinned (path, s3VersionId) only — never
      "latest"**; staged-temp-then-atomic-swap; resume support both directions. Reuse
      `BloomS3Client` session-credential + TransferUtility mechanics (extract shared helper if
      needed — don't disturb the publish path).

## Acceptance
- [ ] `CloudRepoCacheTests` (concurrency, snapshot round-trip, delta, cursor).
- [ ] `BookVersionManifestTests` (diff matrix with data sanity pre-checks; junk exclusion; NFC).
- [ ] `CloudBookTransferTests` (mock S3): skip logic both directions; resume skips done files;
  checksum-mismatch retry; interrupted download leaves the working folder untouched;
  **assert no code path issues an unversioned GET**.

**Agent notes**: Sonnet. This task has no UI and no base-class edits.

## Progress log

- 7 Jul 2026 · Implemented CloudRepoCache.cs, BookVersionManifest.cs, CloudBookTransfer.cs
  (src/BloomExe/TeamCollection/Cloud/); BloomExe builds clean. Extracted
  `BloomS3Client.CreateAmazonS3Client(AmazonS3Config, AmazonS3Credentials)` from `protected`
  instance to `internal static` (pure visibility/staticness change, no behavior change) so
  CloudBookTransfer can reuse session-credential client construction without disturbing the
  publish path. Checksummed uploads use a manually-set `x-amz-checksum-sha256` header (not the
  SDK's native ChecksumAlgorithm/ChecksumSHA256 properties, which don't exist in the
  AWSSDK.S3 3.5.3.10 this project is pinned to) — **live-verified against the local MinIO
  stack**: PUT with the manual header, then read back via a separate probe using a newer
  AWSSDK.S3 (3.7.511.8) GetObjectAttributes(ChecksumMode: ENABLED) — the SHA-256 came back
  correctly, confirming MinIO (and by the same S3 API contract, real AWS) treats the manual
  header identically to the native SDK feature, so `_shared/s3.ts`'s `verifyUploadedObject`
  will accept it once wired up in a later task. Recommend (not done here — BloomExe.csproj is
  orchestrator-owned at merge time per the shared-file schedule): bump AWSSDK.S3 so future
  code can use the native properties instead of a raw header string.
  Next action: write CloudRepoCacheTests.cs, BookVersionManifestTests.cs,
  CloudBookTransferTests.cs in src/BloomTests/TeamCollection/Cloud/ per the Acceptance list
  above (mock IAmazonS3 via Moq, already a test dependency), then run the Cloud + TeamCollection
  regression test filters and report verbatim counts.
