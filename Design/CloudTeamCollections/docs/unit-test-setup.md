# Cloud Team Collections — unit-test setup

What a dev machine (or CI agent) needs to run the Cloud Team Collections *unit* tests, as
opposed to the E2E harness (whose much longer requirements live in
`src/BloomTests/e2e/README.md`).

## The short version

The default suites need **nothing beyond normal Bloom dev setup** (`./init.sh` once). They run
mocked, with no containers, no local Supabase, no MinIO, no network, and no interactive
desktop — safe for any ordinary CI agent, including one running as a service.

| Suite | Command | Extra setup |
|---|---|---|
| C# cloud/TC unit tests (~334) | `dotnet test src/BloomTests/BloomTests.csproj --filter "(FullyQualifiedName~Cloud\|FullyQualifiedName~TeamCollection\|FullyQualifiedName~SharingApi)&FullyQualifiedName!~LiveTests"` | none |
| Front-end component tests | ` cd src/BloomBrowserUI` then `yarn vitest run <files> --pool=threads` | `yarn install` once |

Notes that matter:
- **Never pass `--no-build` to `dotnet test`** — a stale DLL can hide real regressions
  (AGENTS.md rule). Building first is the point.
- The C# filter above is the mandatory *widened* filter: `SharingApiTests` live under
  `web.controllers` and match neither `~Cloud` nor `~TeamCollection`; a narrower filter once
  let a real bug merge behind an "all green" claim.
- vitest on Windows wants `--pool=threads` and single-run mode (`vitest run`, never watch);
  the default fork pool has been seen timing out ("Timeout starting forks runner").

## Suites that DO need the local stack

Three opt-in suites talk to real services. They all need the local stack from
`server/dev/README.md` (Supabase CLI + Podman/Docker + MinIO — the same "Machine setup"
steps 2–4 in the E2E README, but NOT its unlocked-desktop requirement, since no Bloom.exe
is launched):

1. **C# live tests** — `CloudTeamCollectionLiveTests`, marked `[Explicit]`, excluded by the
   default filter's `!~LiveTests`. Run deliberately with the stack up:
   `dotnet test src/BloomTests/BloomTests.csproj --filter "FullyQualifiedName~CloudTeamCollectionLiveTests"`.
2. **pgTAP schema/RLS tests** — `supabase test db` (42 tests; they `SET LOCAL ROLE
   authenticated` because superuser bypasses RLS — a vacuously-green trap if ever rewritten).
3. **Deno edge-function tests** — `deno test` under `supabase/functions/` (32 tests; these
   mock S3/STS so they only need `deno`, not the stack — `npm i -g deno` or the standard
   installer).

A CI shape that works: the mocked suites in the ordinary per-commit build; the stack-backed
suites (1–2) plus the E2E matrix on the dedicated interactive agent described in the E2E
README's TeamCity section.
