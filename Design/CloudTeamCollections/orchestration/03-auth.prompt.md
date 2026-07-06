# Agent prompt — task 03: auth + client skeleton (resume-aware)

You are implementing task 03 of the Cloud Team Collections plan. Work in the MAIN working
tree at c:\github\BloomDesktop (NOT a worktree — you need the initialized C# build deps).

**Resume check (do this FIRST):** `git status` must be clean (stop and report if not). If
branch `task/03-auth` exists, check it out and continue from the `## Progress log` at the
bottom of `Design/CloudTeamCollections/tasks/03-auth.md`. Otherwise
`git checkout -b task/03-auth cloud-collections`.

**Durability protocol (mandatory, from orchestration/RESUME.md):** commit after EVERY
completed step — small coherent commits, descriptive messages ending
"Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>". In the same commit: tick that
step's checkbox in the task file AND update its `## Progress log` (create if missing)
with `date · done · exact next action`. Never leave >1 step uncommitted.

**Read first:** `Design/CloudTeamCollections/tasks/03-auth.md` (authoritative steps —
note the dev-provider-first design), `Design/CloudTeamCollections/CONTRACTS.md` v1.1
(wire format: RPC JSON keys use the `p_` prefix; `tc`-schema calls need
`Content-Profile: tc` — both verified live), `server/dev/README.md` §Environment
variables (the `BLOOM_CLOUDTC_*` contract `CloudEnvironment` must implement).

**Environment:** local stack is running (Supabase API http://127.0.0.1:54321; anon key
via `supabase status`). Seeded dev users: admin@dev.local / alice@dev.local /
bob@dev.local, password BloomDev123! — sign-in verified working via
POST /auth/v1/token?grant_type=password. Use these for any live verification of the dev
auth provider.

**Build caution:** Bloom.exe may be RUNNING on this machine. Building then fails copying
the Bloom.exe apphost (MSB3027) — compilation still completes. Run tests with
`dotnet test src/BloomTests/BloomTests.csproj --filter "FullyQualifiedName~Cloud"` (and
the TeamCollection filter for regression); if the build errors ONLY on the apphost copy,
verify output\Tests\Debug\AnyCPU\Bloom.dll is newer than your source changes and proceed
with `dotnet test` on the built DLL, reporting exactly what you did. Never use stale
binaries silently.

**Scope (owns new files only):** `src/BloomExe/TeamCollection/Cloud/CloudEnvironment.cs`,
`CloudAuth.cs`, `CloudCollectionClient.cs`, plus `src/BloomTests/TeamCollection/Cloud/`
tests. If BloomExe.csproj needs explicit file includes, add ONLY your new files. All
public methods commented. Editing a checked-out book must never block on auth.

**Final report (raw data):** branch + shas; test commands + verbatim result counts; what
was verified live vs unit-tested; the exact next action if you did not finish.
