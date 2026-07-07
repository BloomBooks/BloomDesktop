# Agent prompt — task 04: client core (cache, manifest, transfer) — resume-aware

You are implementing task 04 of the Cloud Team Collections plan. Work in the MAIN working
tree at c:\github\BloomDesktop (NOT a worktree — you need the initialized C# build deps).

**Resume check (do this FIRST):** `git status` must be clean (stop and report if not). If
branch `task/04-client-core` exists, check it out and continue from the `## Progress log`
at the bottom of `Design/CloudTeamCollections/tasks/04-client-core.md`. Otherwise
`git checkout -b task/04-client-core cloud-collections`.

**Durability protocol (mandatory, from orchestration/RESUME.md):** commit after EVERY
completed step — small coherent commits, messages ending
"Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>". Same commit: tick that step's
checkbox in the task file AND update its `## Progress log` (create if missing) with
`date · done · exact next action`. Never leave >1 step uncommitted — interruptions are
EXPECTED and only commits survive.

**Anti-hang rules:** no watch modes, no foreground servers; `--max-time` on curl; timeouts
on anything that might block.

**Read first:** `Design/CloudTeamCollections/tasks/04-client-core.md` (authoritative
steps), `Design/CloudTeamCollections/CONTRACTS.md` v1.1 (S3 layout; manifest shape;
book-status), the design doc's Architecture section, and the merged task-03 code in
`src/BloomExe/TeamCollection/Cloud/` (build on `CloudCollectionClient.CallRpc` /
`CallEdgeFunction`; config via `CloudEnvironment`).

**Scope (owns new files only):** `src/BloomExe/TeamCollection/Cloud/CloudRepoCache.cs`,
`BookVersionManifest.cs`, `CloudBookTransfer.cs` + tests in
`src/BloomTests/TeamCollection/Cloud/`. Reuse `BloomS3Client` session-credential +
TransferUtility mechanics (extract a shared helper if needed — do NOT disturb the publish
path). Hard invariants: downloads ONLY by pinned (path, s3VersionId) — a test must assert
no code path issues an unversioned GET; NFC path normalization; staged-temp-then-atomic-
swap; junk-file exclusion reusing the publish path's filters. All public methods commented.

**Environment:** the full local stack is UP (Supabase 127.0.0.1:54321; MinIO
localhost:9000, bucket `bloom-teams-local`, minioadmin/minioadmin, versioning ON). Unit
tests use mock S3 per the task file; optionally add [Explicit] live tests against MinIO
(BasicAWSCredentials, ForcePathStyle, ServiceURL override).

**Build caution:** Bloom.exe may be RUNNING. A build may fail ONLY on copying the
Bloom.exe apphost (MSB3027) — compilation still completes. Run
`dotnet test src/BloomTests/BloomTests.csproj --filter "FullyQualifiedName~Cloud"` plus the
TeamCollection regression filter; if the apphost copy fails, verify
output\Tests\Debug\AnyCPU\Bloom.dll is newer than your sources and report exactly what ran.
Never silently use stale binaries.

**Final report (raw data):** branch + shas; test commands + verbatim counts; the
no-unversioned-GET assertion location; any base-class or contract issues found (report,
don't fix); exact next action if unfinished.
