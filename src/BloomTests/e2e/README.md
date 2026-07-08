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
- **ReactDialog-hosted WebView2 controls are not reliably CDP-reachable** (`harness/rawCdp.ts`
  documents the investigation; `harness/cdp.ts`'s `waitForPage` was an earlier, now-unused
  attempt at the same problem via Playwright). The "share on cloud" flow alone spans three
  separate WinForms-hosted WebView2 controls in the same process (Collection tab → Settings
  dialog's Team Collection tab → Create Team Collection dialog), each its own environment with
  a distinct `UserDataFolder` (confirmed via Bloom's log) but ALL requesting the identical
  `--remote-debugging-port`. Only one such browser process can ever bind that port, so at most
  one control's content is CDP-visible at a time, and empirically it isn't reliably the one
  you just opened — confirmed both through Playwright's `browser.contexts()` and the raw CDP
  `/json/list` endpoint. **Every scenario in this harness therefore drives state-changing
  actions whose UI lives in one of these secondary dialogs via the same backend API endpoint
  the dialog's button would call**, rather than automating the dialog UI — see E2E-1 and E2E-2's
  header comments for the specific endpoints. CDP clicks are used only against the main
  Collection-tab page, which does NOT have this problem (it's the one control alive from
  startup) — except see the checkout/check-in button finding below, which turned out to have a
  similar unreliability for a different, undiagnosed reason.
- **Reconnect timing around `createCloudTeamCollection`**: that call's reopen-collection
  callback reloads the main window's WebView2 control in place. If a Playwright connection is
  already attached and watching before the reopen, it follows the same CDP target through the
  reload correctly (confirmed: body content grows as the reload happens, same target id
  throughout). A **fresh** `connectOverCDP` call made *after* the reopen has already happened
  intermittently finds only an `about:blank` target (confirmed via raw `/json/list`, not just
  Playwright's page cache) even after retrying the page-lookup for 60+ seconds. The fix used
  throughout: connect to a page *before* triggering any action that might cause a reopen, and
  keep reusing that same `Page` object afterward instead of reconnecting.
- **Checkout/check-in buttons don't respond to CDP clicks.** Playwright reports a successful
  `.click()` on the visible, enabled "CHECK OUT BOOK"/"CHECK IN BOOK" button (a
  `getByRole("button", {name: ...})` locator resolving to exactly one element), but
  before/after screenshots show no state change and `teamCollection/bookStatus` still reports
  `who: null` afterward. Calling the same endpoint the button posts to
  (`teamCollection/attemptLockOfCurrentBook` / `checkInCurrentBook`) directly succeeds
  immediately. Root cause undiagnosed (a ripple/overlay intercepting the hit-test is one
  candidate) — E2E-2 uses the direct API call for checkout/check-in themselves, but still uses
  a real CDP click to *select* the book first (that part works fine).
- **`CloudCollectionMonitor.DefaultPollInterval` is 60 seconds.** A second instance sitting
  idle takes up to a minute to notice a remote checkout/check-in organically.
  `teamCollection/receiveUpdates` calls `CloudTeamCollection.PollNow()` internally before doing
  anything else, so scenarios that need to observe another instance's change promptly should
  call `receiveUpdates` to force an immediate poll rather than waiting out the timer or padding
  `expect.poll` timeouts past 60s.
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

## Known flakiness on a loaded machine

E2E-2 (two-instance) intermittently times out at varying steps (Bloom launch, `connectOverCdp`,
or an individual API call) when the machine is under heavy concurrent load — observed with only
~18-20% free RAM while multiple other VS Code + language-server + browser processes were
running (e.g. other agents working in parallel `.claude/worktrees/`). Every individual step has
been independently verified correct in isolation; see the task's progress log for detail. If
this harness is flaky in CI, check available memory/CPU before assuming a logic regression.

`globalSetup` also clears leaked `%TEMP%\Bloom WV2-*` profile folders (Bloom itself never
cleans these up — see `harness/reset.ts`'s `resetLeakedWebView2Profiles` doc comment) since 117
of them had accumulated by the end of this harness's development session and were measurably
slow to even enumerate. This is a real, worth-keeping fix, but on its own it did not resolve
the memory-pressure-driven flakiness above.

## Scenario status

See the Progress log in `Design/CloudTeamCollections/tasks/09-e2e.md` for current status per
scenario (green / blocked / deferred + exact next action).
