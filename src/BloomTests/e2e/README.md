# Cloud Team Collections E2E harness

Playwright-over-CDP tests that drive **real Bloom.exe instances** against the local dev stack
(local Supabase + MinIO, see `server/dev/README.md`). This automates the Wave-3 manual
two-instance smoke test and pins the bugs found during it.

## Prerequisites

- The local dev stack must already be up: `supabase start`, MinIO (`docker compose -f
  server/dev/docker-compose.yml up -d` — actually run via `podman compose` on this machine,
  see below), and `supabase functions serve` (or the Podman-managed
  `supabase_edge_runtime_*` container, which serves the same role).
- `podman` (or `docker`) on PATH — used to clear the MinIO bucket between scenarios via a
  throwaway `mc` container on the same network as `bloom-minio` (see `harness/reset.ts`; the
  host-gateway route is a known hang, so this never uses `host.containers.internal`).
- `supabase` CLI on PATH (Volta shim on Windows — see the shell-quoting note below).
- `dotnet` SDK (builds `src/BloomExe/BloomExe.csproj`).
- Yarn 1.22 (`yarn install` in this directory once).

## Running

```powershell
cd src/BloomTests/e2e
yarn install       # once
yarn test          # builds Bloom once (globalSetup), then runs every scenario, workers=1
```

Single file: `yarn playwright test tests/e2e-1-create-share.spec.ts`.

## Design

- **Build once, launch many** (`harness/launch.ts`): `globalSetup` runs `dotnet build
  src/BloomExe/BloomExe.csproj -c Release` exactly once for the whole run. Each scenario then
  launches the built exe directly (`output/Release/AnyCPU/Bloom.exe --automation --label
  <name> <collectionFile>`), with per-instance `BLOOM_CLOUDTC_*` env vars for identity. Bloom
  picks its own HTTP/CDP ports and reports them via the `BLOOM_AUTOMATION_READY {...}` stdout
  line (see `.github/skills/bloom-automation/SKILL.md`).
- **RELEASE, not Debug, is mandatory** — not just faster. A Debug build shows a blocking modal
  `MessageBox` ("Attach debugger now", `Program.cs` `#if DEBUG`, fires whenever
  `args.Length > 0`) on every launch that passes a collection-file path, which every harness
  launch does. With no one at the keyboard to click it, the instance hangs forever with no
  HTTP/CDP port ever opening. This cost real time to diagnose (see progress log) — the process
  looked "stuck starting up" with `Responding: True` and no error, until a `PrintWindow`
  screen-capture of its (title-less) window revealed the dialog. Building `-c Release`
  compiles that branch out entirely.
- **Experimental flag** (`harness/experimentalFlag.ts`): Cloud Team Collections is gated by
  the `cloud-team-collections` token in `EnabledExperimentalFeatures`, stored in the *shared,
  per-machine* `user.config` at `%LOCALAPPDATA%\SIL\Bloom\<version>\user.config` (there is no
  env-var override; this is the same manual hack recorded in the Wave-3 merge log). Ensured
  idempotently (never overwrites the developer's real settings file wholesale) once per
  session, in `globalSetup`.
- **Per-scenario reset** (`harness/reset.ts`): `supabase db reset` (replays migrations +
  seed) + clears the MinIO bucket (via a throwaway `mc` container on the `bloom-minio`
  network — NOT `host.containers.internal`, which hangs indefinitely per
  `server/dev/README.md`'s documented gvproxy gotcha) + wipes `C:\BloomE2E\` (this harness's
  scratch-collection root, kept outside the repo so a stray leftover can never be committed).
- **Multi-CDP-target navigation** (`harness/cdp.ts`): the "share on cloud" flow alone spans
  three separate WinForms-hosted WebView2 controls in the same process (Collection tab →
  Settings dialog's Team Collection tab → Create Team Collection dialog) — `waitForPage`
  polls for a new page by URL substring since each is a distinct CDP target that appears only
  once its host WinForms dialog opens.
- **DB verification** (`harness/db.ts`): connects directly to the local Postgres
  (`postgresql://postgres:postgres@localhost:54322/postgres`, stable across `db reset`) via
  `pg`, not the `supabase db query` CLI — that CLI is a Volta `.cmd` shim on Windows, which
  Node can only spawn with `shell: true`, and shell-mode argument concatenation (not real
  escaping) mangled quoted SQL containing spaces in practice.

## Known environment gotchas hit while building this harness

- `execFile("supabase", ...)` fails with `EINVAL` on Windows unless `shell: true` — it's a
  `.cmd` shim, not a real `.exe`. Fine for `db reset` (fixed args); avoided entirely for ad hoc
  SQL (see `harness/db.ts`).
- `podman run --entrypoint /bin/sh ...` needs `MSYS_NO_PATHCONV=1` (or `//bin/sh`) under Git
  Bash, otherwise MSYS rewrites `/bin/sh` into a bogus Windows path before it reaches Podman.
- `BLOOM_AUTOMATION_READY`'s CDP port can be printed a beat before the WebView2 remote-
  debugging listener actually accepts connections — `harness/launch.ts`'s `connectOverCdp`
  retries for up to 15s rather than treating the first `ECONNREFUSED` as fatal.

## Scenario status

See the Progress log in `Design/CloudTeamCollections/tasks/09-e2e.md` for current status per
scenario (green / blocked / deferred + exact next action).
