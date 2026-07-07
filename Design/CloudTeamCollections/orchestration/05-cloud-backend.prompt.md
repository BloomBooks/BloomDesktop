# Agent prompt — task 05: CloudTeamCollection + monitor + join flow (resume-aware)

You are implementing task 05 — the heart of the feature — in the MAIN working tree at
c:\github\BloomDesktop (NOT a worktree; you need the initialized C# build deps).

**Resume check (do this FIRST):** `git status` must be clean (stop and report if not). If
branch `task/05-cloud-backend` exists, check it out and continue from the `## Progress
log` at the bottom of `Design/CloudTeamCollections/tasks/05-cloud-backend.md`. Otherwise
`git checkout -b task/05-cloud-backend cloud-collections`.

**Durability protocol (mandatory):** commit after EVERY completed step — this is the
longest task in the plan and interruptions are CERTAIN. Small coherent commits, messages
ending "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"; same commit ticks the
step's checkbox and updates the `## Progress log` with `date · done · exact next action`.

**Anti-hang rules:** no watch modes or foreground servers; `--max-time` on curl; timeouts
on anything that might block.

**Read first (in this order):** the task file
`Design/CloudTeamCollections/tasks/05-cloud-backend.md`;
`Design/CloudTeamCollections/CONTRACTS.md` (v1.2 — note the NEW `get_book_manifest` RPC,
added for exactly this task's Receive path);
`Design/CloudTeamCollections.md` §Architecture + §Client integration;
`Design/CloudTeamCollections/notes/write-book-status-audit.md` (task 00's caller audit —
drives your WriteBookStatusJsonToRepo diff-dispatch);
the merged Wave-1/2 code you build ON: `src/BloomExe/TeamCollection/Cloud/*.cs`
(CloudEnvironment/CloudAuth/CloudCollectionClient from 03; CloudRepoCache/
BookVersionManifest/CloudBookTransfer from 04) and the abstract members of
`src/BloomExe/TeamCollection/TeamCollection.cs`.

**File discipline (strict):**
- You own NEW files: `Cloud/CloudTeamCollection.cs`, `Cloud/CloudCollectionMonitor.cs`,
  `Cloud/CloudJoinFlow.cs` + tests under `src/BloomTests/TeamCollection/Cloud/`.
- Base classes (`TeamCollection.cs`, `FolderTeamCollection.cs`, etc.) are READ-ONLY. If an
  implementation genuinely needs a base change, STOP that step, record the need in your
  progress log and final report, and work around it if possible — the orchestrator makes
  base changes.
- ONE authorized exception in `TeamCollectionManager.cs`: replace the two
  NotImplementedException placeholders from task 00 (`CreateTeamCollectionFromLink`'s
  cloud branch; `ConnectToCloudCollection`) with real CloudTeamCollection construction.
  Touch NOTHING else in that file.

**Key implementation constraints (from merged-work review notes):**
- Receive: `get_book_manifest` RPC → CloudBookTransfer.DownloadFiles into a TEMP book
  folder → atomic whole-directory swap done BY YOU (the transfer class's per-file move
  loop is not itself a single atomic dir swap — merge log 7 Jul).
- Locks: override TryLockInRepo/UnlockInRepo with checkout_book/unlock_book/force_unlock
  RPCs (server stamps identity; client sends machine name only).
- WriteBookStatusJsonToRepo: diff-dispatch to the NARROWEST RPC per the audit; pure
  bookkeeping writes must never clear a lock; the SyncAtStartup callers flagged in the
  audit are local-only no-ops for cloud.
- RPC wire format: `p_`-prefixed JSON keys + Content-Profile/Accept-Profile: tc (already
  handled inside CloudCollectionClient.CallRpc).
- Monitor: polling only (get_changes, 60s + on-activation); event-id self-echo
  suppression via last-seen cursor; realtime is a later wave.
- Unified recovery (lock-lost/base-superseded): save `.bloomSource` to Lost & Found,
  Receive current, log_event incident (type 100 WorkPreservedLocally), distinct messages
  per sub-case.
- All new user-visible strings: follow `.github/skills/xlf-strings/SKILL.md`, en-only.

**Environment:** full local stack UP (Supabase 127.0.0.1:54321 — anon key via `supabase
status`; MinIO via edge functions; dev users admin/alice/bob@dev.local pw BloomDev123!).
For live integration tests: edge functions must be served — run
`supabase functions serve --env-file server/dev/functions.env` via Start-Process/`&` with
output redirected to a file, NEVER foreground (that stalled a prior agent). [Explicit]
NUnit live tests against the stack are strongly encouraged for the Send/Receive round
trip (see CloudAuthTests' LiveDevProvider test for the pattern).

**Build caution:** Bloom.exe may be RUNNING; an apphost-copy MSB3027 during build is
benign — verify output\Tests\Debug\AnyCPU\Bloom.dll is newer than your sources, run
`dotnet test src/BloomTests/BloomTests.csproj --filter "FullyQualifiedName~Cloud"` and the
`~TeamCollection` regression filter, and report exactly what ran. Never --no-build.

**Final report (raw data):** branch + shas; test commands + verbatim counts (cloud filter,
TC regression, any [Explicit] live runs); base-class changes you needed but could not
make; contract ambiguities; exact next action if unfinished.
