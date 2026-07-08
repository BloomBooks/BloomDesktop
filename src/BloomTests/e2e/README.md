# Cloud Team Collections E2E harness

Playwright-over-CDP tests that drive **real Bloom.exe instances** against the local dev stack
(local Supabase + MinIO, see `server/dev/README.md`). This automates the Wave-3 manual
two-instance smoke test and pins the bugs found during it.

(For what the *unit* tests need — much less — see
`Design/CloudTeamCollections/docs/unit-test-setup.md`.)

## Machine setup (fresh dev machine)

Everything below is once-per-machine. Windows 10/11 assumed (the harness launches the real
WinForms/WebView2 Bloom.exe, so Windows is required).

1. **Normal Bloom dev setup** — clone, then `./init.sh` at the repo root (fetches C#
   dependencies, yarn installs, initial front-end build). Provides the .NET SDK usage,
   Volta-managed node + yarn 1.22, and the WebView2 runtime that a working Bloom dev machine
   already has. If `dotnet build src/BloomExe/BloomExe.csproj -c Release` succeeds, this
   layer is done.
2. **Container runtime** — either Docker Desktop or **Podman** (what this harness was built
   against: Podman 5.8.3, Podman Desktop, a *rootful* WSL2 podman machine, with the
   Docker-compatibility named pipe enabled so `docker-compose` works). WSL2 must be enabled
   (needs virtualization; on a VM that means nested virtualization). See
   `server/dev/README.md` for the exact Podman recipe and its gotchas (bind-mount dirs must
   pre-exist; edge-runtime containers must reach MinIO as `bloom-minio:9000`, never
   `host.containers.internal`, which hangs).
3. **Supabase CLI + docker-compose** — `npm i -g supabase` (Volta shim lands on PATH) and
   `winget install Docker.DockerCompose` (or use `podman compose`).
4. **Bring the stack up** (each boot / when containers are down):
   ```powershell
   supabase start                                          # local Postgres/GoTrue/PostgREST/edge runtime
   docker-compose -f server/dev/docker-compose.yml up -d   # MinIO on the supabase network
   ```
   Verify with `server/dev/smoke.ps1`. Dev users (alice@dev.local etc., password
   BloomDev123!) come from `server/dev/seed.sql` automatically.
5. **Interactive, UNLOCKED desktop session.** Hard requirement: a locked session (or a
   service session with no interactive desktop) stalls WebView2 at `about:blank`
   indefinitely and every scenario times out. Check: `Get-Process LogonUI
   -ErrorAction SilentlyContinue` — if it returns a process, the session is locked. Keep the
   machine unlocked for the whole run (set screen lock/sleep policy accordingly).
6. **(Recommended) Defender exclusions** for the repo folder, `C:\BloomE2E\`, and the WSL2
   VHD — real-time scanning of WebView2 profile churn and container disk was measured making
   launches dramatically slower on the original dev machine.

Nothing else is needed: the harness itself builds Bloom (Release) once per run, enables the
`cloud-team-collections` experimental flag in user.config idempotently, and resets the
DB/bucket/scratch folders per scenario. There are no secrets — the committed dev credentials
are local-only constants.

## Prerequisites (per run)

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
- No Bloom.exe from THIS repo tree running (the harness fails loudly if one is; instances
  from other worktrees are tolerated with a warning).

## Running

```powershell
cd src/BloomTests/e2e
yarn install       # once
yarn test          # builds Bloom once (globalSetup), then runs every scenario, workers=1
```

Single file: `yarn playwright test tests/e2e-1-create-share.spec.ts`.

**Confining Bloom windows to one monitor**: set `BLOOM_E2E_SCREEN` to a screen number
(1-based, counting monitors left-to-right by X coordinate) and every launched instance's
windows — including the splash, the post-reopen replacement main window, and WinForms
dialogs — get kept on that screen by a per-instance watcher (`harness/windowPlacement.ts` +
`watchWindowScreen.ps1`). List your screens in that order with:

```powershell
powershell -NoProfile -Command "Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.Screen]::AllScreens | Sort-Object {$_.Bounds.X} | ForEach-Object {$i=1} { \"$i. $($_.DeviceName) $($_.Bounds) primary=$($_.Primary)\"; $i++ }"
```

Caveat: Bloom saves window position to the shared user.config on exit, so your own next
manual Bloom launch may open on the E2E screen once — just drag it back. Test behavior is
unaffected by window position (CDP input is page-relative).

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

## TeamCity build agent — feasibility notes

Running this harness on a TeamCity agent is feasible but has non-standard requirements, all
stemming from the fact that it launches a real GUI app with WebView2:

- **The agent must run as an interactive desktop process, NOT a Windows service.** Install
  the TeamCity agent in "start via Windows logon" mode with automatic logon of a dedicated
  build user, and keep that session unlocked (group policy: no lock screen / no screensaver
  lock; if RDP is ever used to inspect the box, disconnect with `tscon <session> /dest:console`
  rather than plain disconnect, which locks the console session and stalls WebView2 — see
  "Machine setup" item 5).
- **Virtualization**: the agent machine needs WSL2 for Podman/Docker; on a VM host that means
  nested virtualization enabled.
- **Resources**: ≥16 GB RAM recommended. The known flakiness mode of this harness is memory
  pressure (see "Known flakiness" above); an agent that also runs other heavy builds
  concurrently will produce false failures.
- **Runtime**: the full matrix is roughly 35–45 minutes at workers=1 (mandatory — scenarios
  share the DB/bucket and launch multiple Bloom instances). Budget the build configuration
  accordingly; consider a nightly schedule rather than per-commit.
- **State**: per-machine mutable state the harness touches: `%LOCALAPPDATA%\SIL\Bloom\<ver>\
  user.config` (experimental flag, edited idempotently), `%MyDocuments%\Bloom\BloomE2E-*`
  (pull-down destinations, cleaned by prefix), `C:\BloomE2E\` (scratch), `%TEMP%\Bloom WV2-*`
  (WebView2 profiles, cleaned in globalSetup). A dedicated build user keeps all of this away
  from human users' real settings.
- **No secrets**: everything runs against the local stack with committed dev-only constants.
- **Stack bring-up**: the build script must ensure `supabase start` + the MinIO compose file
  are up before `yarn test` (they survive between builds on a persistent agent; a
  bring-up-if-down step plus `server/dev/smoke.ps1` as a health gate is the robust shape).

## Scenario status

See the Progress log in `Design/CloudTeamCollections/tasks/09-e2e.md` for current status per
scenario (green / blocked / deferred + exact next action).
