# 04 — Client core: cache, manifest, transfer (Wave 2)

**Goal**: the data plumbing `CloudTeamCollection` will sit on.

**Dependencies**: 03 (client/auth), CONTRACTS.md. Owns new files
`src/BloomExe/TeamCollection/Cloud/CloudRepoCache.cs`, `BookVersionManifest.cs`,
`CloudBookTransfer.cs`.

## Steps
- [ ] `CloudRepoCache`: thread-safe in-memory book/lock/version map + collection-file record +
      `last_seen_event_id`; persisted snapshot (`.bloom-cloud-repo-cache.json` in the local
      collection folder, excluded from book enumeration); full-snapshot + delta application;
      write-through from mutating RPC results. Never trusted for mutations.
- [ ] `BookVersionManifest`: model (path → sha256, size, s3VersionId), NFC path normalization,
      junk/derived-file exclusion list (reuse the publish path's filters), local-folder diff
      (changed/added/removed/unchanged) with hash computation reuse (`MakeChecksum` internals /
      `Book.ComputeHashForAllBookRelatedFiles`).
- [ ] `CloudBookTransfer`: hash-skip uploads (parallel PUTs with checksum headers, byte
      progress via IProgress) and downloads **by pinned (path, s3VersionId) only — never
      "latest"**; staged-temp-then-atomic-swap; resume support both directions. Reuse
      `BloomS3Client` session-credential + TransferUtility mechanics (extract shared helper if
      needed — don't disturb the publish path).

## Acceptance
- `CloudRepoCacheTests` (concurrency, snapshot round-trip, delta, cursor).
- `BookVersionManifestTests` (diff matrix with data sanity pre-checks; junk exclusion; NFC).
- `CloudBookTransferTests` (mock S3): skip logic both directions; resume skips done files;
  checksum-mismatch retry; interrupted download leaves the working folder untouched;
  **assert no code path issues an unversioned GET**.

**Agent notes**: Sonnet. This task has no UI and no base-class edits.
