# run-bloom reference: mechanisms, rules, and field-verified gotchas

`SKILL.md` is the quick path. This file is the authoritative detail behind it: how the launcher
and helpers work, the rules that keep several agents and the developer from tripping over each
other's Blooms, and the traps hit in real runs. Every command is repo-root-relative bash.

## Facts the helpers rely on

- Bloom project: `src/BloomExe/BloomExe.csproj`. Source-aware launcher: `./go.sh`, which runs
  `src/BloomBrowserUI/scripts/go.mjs` and starts the front-end dev server and the exe together
  through `scripts/watchBloomExe.mjs`.
- In automation mode Bloom prints a machine-readable `BLOOM_AUTOMATION_READY {...}` line with
  its HTTP port, CDP port and process id, and answers `GET /bloom/api/common/instanceInfo` with
  the same facts plus `executablePath`.
- The launcher runs a loopback-only HTTP control server, advertised in
  `<repoRoot>/output/bloom-launcher.json` and by a `BLOOM_LAUNCHER_READY {...}` line.
  `launcherControl.mjs` wraps it. Raw API, for tools: `GET /status`, `POST /restart`,
  `POST /start`, `POST /quit-bloom`, `POST /shutdown` on the `controlUrl` from that file.
- In dev builds the top-level page's `body.className` is `developer`; page URLs look like
  `http://localhost:<port>/bloom/C%3A/...Temp/bloomXXXX.htm`. On the Edit tab the editable page
  is the iframe named `page`; the top document is shell UI plus the root dialog container.

## Launcher control

```bash
node .claude/skills/run-bloom/launcherControl.mjs --status --json
node .claude/skills/run-bloom/launcherControl.mjs --ensure-running --wait-ready --json  # start the stack if nobody's home
node .claude/skills/run-bloom/launcherControl.mjs --restart --wait-ready --json         # rebuild + relaunch, any state
node .claude/skills/run-bloom/launcherControl.mjs --start --wait-ready --json           # relaunch only when parked (awaiting-restart)
node .claude/skills/run-bloom/launcherControl.mjs --quit-bloom --json                   # graceful quit; stops the watch child too
node .claude/skills/run-bloom/launcherControl.mjs --shutdown --json                     # Bloom + dotnet watch + launcher + Vite
```

- **Liveness is HTTP truth, never the file.** A discovery file whose `controlUrl` does not answer
  means nobody is home (a hard-killed launcher); the helper reports
  `launcherFound:false, staleFile:true` and exits 2. A fresh launcher overwrites the stale file.
- **A launch in progress is advertised too.** go.mjs writes an early record (`state:"starting"`,
  a `phase` of `starting`/`init`/`dev-server`/`starting-bloom`, its `goPid`) before the control
  server exists. The helper reports `launcherFound:false, starting:true, phase:...` (exit 2) and
  `--ensure-running` waits for it instead of starting a second stack. `init` means the worktree
  was uninitialized and go.mjs is running `./init.sh` for you (it detects missing node_modules,
  `lib/dotnet` deps and `output/browser` bundles); agents never run init.sh themselves. A CS0246
  such as `PodcastUtilities` on some other build path still means init has not run.
- `--status` states: `building`, `bloom-running`, `awaiting-restart` (only via `--quit-bloom`),
  `restarting`, `launch-failed`. A dotnet-watch hot rebuild passes through `building`
  transiently; poll for `bloom-running` (`--wait-ready` does) rather than sampling once.
- **The human closing Bloom tears the whole stack down**: launcher, dotnet watch and Vite exit so
  an idle stack stops holding memory. The launcher tells that apart from a dotnet-watch rebuild
  by the watcher's file-changed output, so C# edits do not kill the stack. Only `--quit-bloom`
  leaves the launcher parked in `awaiting-restart`.
- `/status` reports `sourceChangedSinceReady`: whether dotnet watch has seen C# changes since the
  current Bloom became ready, so whether `--restart` would incorporate anything. Bloom polls the
  same field (when launched via `--launcher-port`, see `DevLauncher.cs`) for its dev-only restart
  toast.
- `--wait-ready` waits for a **new** launch (`launchNumber` increased) to reach `bloom-running`
  and prints the fresh ports.
- `--ensure-running` starts the stack decoupled from your session: in an Orca terminal tab titled
  "go.sh" when Orca is reachable, else detached with `output/bloom-launcher.log` (reported as
  `logPath`). `output/bloom-launcher.starting.lock` stops two agents double-launching.
- Every action prints a `[control] ... requested` line in the launcher's terminal so the human
  can see why Bloom moved.

## Helpers for a Bloom without a live launcher

```bash
node .claude/skills/run-bloom/bloomProcessStatus.mjs --json                        # processes, repo roots, dotnet watch parents
node .claude/skills/run-bloom/bloomProcessStatus.mjs --running-bloom --json        # HTTP-based: whatever Bloom is serving
node .claude/skills/run-bloom/bloomProcessStatus.mjs --http-port <httpPort> --json # the exact instance on that port
node .claude/skills/run-bloom/killBloomProcess.mjs --http-port <httpPort>          # that instance and its dotnet parent
node .claude/skills/run-bloom/killBloomProcess.mjs --only-mismatched               # only a Bloom from another worktree
node .claude/skills/run-bloom/killBloomProcess.mjs                                 # every detected Bloom-related process
node .claude/skills/run-bloom/killBloomProcess.mjs --pid <n> --watch-pid <n>       # exact PIDs you already know
node .claude/skills/run-bloom/webview2Targets.mjs --http-port <httpPort> --json --wait
node .claude/skills/run-bloom/webview2Targets.mjs --running-bloom --json --wait    # whatever Bloom is serving
node .claude/skills/run-bloom/switchWorkspaceTab.mjs --http-port <httpPort> --tab publish --json
node .claude/skills/run-bloom/screenshotBloom.mjs --http-port <httpPort> --out output/screenshots/bloom.png --json
```

- Use the checked-in helpers, never ad hoc `wmic` or `taskkill` from a bash terminal (the
  shell mangles both; see gotchas). The Node scripts call `wmic`, `taskkill` and `dotnet`
  directly.
- `--running-bloom` scans Bloom's standard port range and asks each instance for
  `instanceInfo`; use it when the user wants the already-running Bloom whatever worktree it is
  from. `--http-port` is the precise form once you know the port, and the only reliable one when
  several Blooms run from one worktree.
- If Bloom was started by `dotnet watch run`, killing only `Bloom.exe` is not enough: the watcher
  restarts it. `killBloomProcess.mjs` stops the chain; the launcher's `--quit-bloom`/`--shutdown`
  avoid the problem entirely when a launcher is live. Exact-target cleanup is strict: with
  `--http-port` it kills only the instance that reports that port, and fails without killing
  anything if it cannot resolve the target.
- `webview2Targets.mjs --wait` blocks until the embedded browser target exists.
  `switchWorkspaceTab.mjs` clicks the real top-bar tab and waits for `workspace/tabs` to report
  it active; it loads Playwright from `src/BloomBrowserUI/react_components/component-tester`.

## Rules

- **Never probe a live Bloom to discover whether something exists.** Bloom's server treats an
  unregistered endpoint as a program error, not a 404: it raises a modal "Bloom had a problem"
  dialog over whatever the developer is doing (`ReportMissingApiEndpoint`; the same for a missing
  file). To learn whether an endpoint exists, grep `src/BloomExe/web` for the string; to identify
  an instance use `common/instanceInfo`, the launcher `/status`, or the helpers, and nothing else.
  A worktree on another branch may not have the endpoint you are looking at.
- **Reuse the current worktree's instance.** Attach over CDP and drive the UI. Restart only when
  the user wants a fresh run or you need new .NET code, and then with
  `launcherControl.mjs --restart`.
- **Do not accumulate instances.** Before any fresh launch, check whether this worktree's Bloom is
  already running; reuse it or stop that exact instance first. Several Blooms from one worktree
  only when the user asked for that.
- **A wrong-worktree Bloom is a blocker.** It produces baffling results. Report the repo root
  `bloomProcessStatus.mjs` shows, stop it with `--only-mismatched`, then start this worktree's.
  That detection is authoritative only when a real `Bloom.exe` child exists or `dotnet watch`
  was started with an absolute `--project` path; otherwise confirm with `instanceInfo`'s
  `executablePath`.
  The exception is when the user says to reuse the running Bloom: then use `--running-bloom`,
  work only against the ports it reports about itself, and never kill or restart it.
- **Drive the UI by clicking and typing.** Do not simulate the user's action through an API call.
  The API is for scripted batch setup (below).
- **Do not launch `Bloom.exe` directly or call `scripts/watchBloomExe.mjs` yourself**, unless
  you are debugging the launcher.
- **Multi-instance work goes by port.** Repo-root matching is not enough when one worktree runs
  two Blooms; take each port from its launch and pass `--http-port` everywhere.
- When reporting, name the helper commands you used so a reviewer can see the run stayed on the
  supported path: whether Bloom was already running and from which repo root, what you stopped
  or started, the ports and PIDs, and what browser-native evidence you collected.

## Driving Bloom HTTP APIs over CDP (host-header and IPv6 gotchas)

Sometimes you need the HTTP API (`editView/topBar/layoutChoiceChange`, `editView/jumpToPage`)
rather than clicks, for example to cycle every page size for screenshots. Two gotchas pull in
opposite directions:

1. **CDP wants IPv4.** On Windows, Node resolves `localhost` to `::1` first, but the WebView2 CDP
   port answers on `127.0.0.1`. `http://localhost:<cdpPort>/json` from Node can return an empty
   or wrong target list (often a lone `about:blank`). Use `http://127.0.0.1:<cdpPort>/json` and
   rewrite the returned `webSocketDebuggerUrl` from `localhost` to `127.0.0.1`.
2. **Bloom's server rejects `Host: 127.0.0.1`** with a 400, because it accepts only `localhost`,
   and `fetch` cannot override the `Host` header.

The fix for both: issue the API call **from inside the page** via `Runtime.evaluate` with a
relative URL, so the page's own `localhost` origin supplies the Host header while your CDP
connection stays on IPv4.

```js
const ts = await (await fetch(`http://127.0.0.1:${cdpPort}/json`)).json();
const target = ts.find(t => /appBundle/.test(t.title||"")) || ts.find(t => t.type==="page");
const ws = new WebSocket(target.webSocketDebuggerUrl.replace("localhost","127.0.0.1"));
// ... Runtime.enable, then:
const evalJs = expr => send("Runtime.evaluate",{expression:expr,returnByValue:true,awaitPromise:true})
                         .then(r => r?.result?.value);
await evalJs(`fetch('/bloom/api/editView/topBar/layoutChoiceChange',
  {method:'POST',headers:{'Content-Type':'application/json'},
   body:JSON.stringify({layoutChoiceId:'LetterPortrait'})}).then(r=>r.status)`);
await evalJs(`fetch('/bloom/api/editView/jumpToPage',{method:'POST',body:'${pageId}'}).then(r=>r.status)`);
```

Poll `document.getElementById('page').contentDocument.querySelector('.bloom-page').className`
to confirm the layout class and page id before you screenshot.

## "Bloom had a problem" dialogs

Bloom surfaces errors, including non-fatal ones in Debug builds, as a modal "Bloom had a problem"
dialog. It lives in its own WinForms window with its own WebView2, so it is a **separate CDP page
target** (in dev, served from the Vite port, not Bloom's), detectable by the `.problem-dialog`
root in any target. Never leave one on screen and never move past it silently.

- `node .claude/skills/run-bloom/dismissProblemDialog.mjs --http-port <httpPort> [--wait] [--json]`
  finds the dialog by DOM (so it never closes a legitimate modal), clicks its own "Learn More" to
  gather the exception and stack, prints them, and closes it with the same action as its Close
  button (`POST /bloom/api/common/closeReactDialog`), which does not submit. It drains a backlog,
  up to a cap.
- **Never click Submit or POST `problemReport/submit`** in automation: that sends a report, with
  a screenshot and the book, to Bloom's servers.
- A problem that reappears after being closed is a real recurring error in the code under test;
  read the gathered detail and fix the cause, do not loop-dismiss. The log at
  `%TEMP%\SIL\Bloom\Log-*.txt` has the same detail but can lag; the dialog's own "Learn More" is
  the live source.

## The exe-backed Playwright suite

From `src/BloomBrowserUI/react_components/component-tester`:
`BLOOM_HTTP_PORT=<httpPort> pnpm exec playwright test --config playwright.bloom-exe.config.ts`
(append a file path for one file). These attach to the real Bloom.exe over CDP and verify tab
switching plus console and network observation. The `playwright` Node library over
`http://127.0.0.1:<cdpPort>` is the confirmed attach path; MCP browser wrappers that own their own
browser cannot attach to an existing CDP endpoint.

## Field-verified gotchas (all hit in real agent runs)

- **Port 8089 is first-come, not per-worktree.** Bloom starts at 8089 and falls forward when it
  is taken, so a second worktree's Bloom lands on 8092/8094 without complaint. Anything that
  hard-codes 8089 (the canvas e2e suite's default `BLOOM_CANVAS_E2E_URL`) drives whichever Bloom
  got there first.
- **`data-toolid` means two things in the toolbox.** A section header's icon carries the tool's
  canonical id (`canvas`); the panel body carries the persisted "Tool"-suffixed name
  (`canvasTool`). A selector matching both finds the 16px icon first, which reads as "the panel
  renders empty"; scope panel queries to `div[data-toolid="...Tool"]`.
- **Reader/audio highlight state lives in `CSS.highlights`, not the DOM.** Since BL-16558 there
  are no marker spans to count; assert via `CSS.highlights.entries()`, counting ranges where
  `!range.collapsed` and `range.getClientRects().length` for "actually painted". No highlights
  is not proof the markup is broken: the Leveled Reader panel's switch gates painting; flip it on.
- **Coordinates differ between frames.** Each iframe has its own coordinate system and page
  scaling (`transform: scale(...)`) changes `getBoundingClientRect()`. Compare a drop point and
  a created element in one coordinate space, and test at more than one zoom level.
- **Ad-hoc driver scripts cannot `import "playwright"` from a scratch directory**: Node resolves
  from the script's own path. Do what the shipped drivers do:
  `createRequire("<repo>/src/BloomBrowserUI/react_components/component-tester/package.json")`
  then `require("playwright")`.
- **WMI can go blind mid-session.** `bloomProcessStatus.mjs` (plain mode) and
  `killBloomProcess.mjs` enumerate processes via `wmic`, which has stopped answering partway
  through a session (zero Bloom processes reported while one served HTTP; `Get-CimInstance`
  hung for minutes). Trust the HTTP-based `--running-bloom` and `instanceInfo` over process
  enumeration.
- **`killBloomProcess.mjs` can under-kill**: `killedProcessIds: []` for a valid target, or the
  `dotnet watch` parents dead and Bloom.exe alive. Verify the port went dark and the PID is gone;
  `Stop-Process -Id <pid> -Force` any survivor. The launcher's `--quit-bloom`/`--shutdown` avoid
  this class of problem.
- **Orphaned `dotnet watch` chains relaunch Bloom.** If Bloom.exe dies but its watcher survives
  (a Task Manager kill), the watcher sits at "Waiting for a file to change" and respawns Bloom on
  the next C# edit. `bloomProcessStatus.mjs --json` lists them under `watchProcesses`.
- **Never type `taskkill /PID ...` in Git Bash**: MSYS rewrites `/PID` to
  `C:/Program Files/Git/PID`. Use the helpers or PowerShell.
- **If you must read a launch log, grep `Bloom ready\. HTTP`, not `Bloom ready\.`**: early in
  the log the launcher prints an instruction that quotes the shorter phrase. Better, poll
  `launcherControl.mjs --status` and never read logs.
- **`dotnet watch` noise**: `⚠ msbuild: [Failure] Package 'X' was restored using .NETFramework...`
  lines are warnings; launch still succeeds. Do not grep the log for bare `Failure` or `error`;
  the launch signals are `Bloom ready. HTTP ...` and, for a failed launch, `Bloom PID ... exited
  shortly after reporting ready` (do not target that port), or better, `--status`.
- **`bloomProcessStatus.mjs` may print `Assertion failed: !(handle->flags & UV_HANDLE_CLOSING)`**
  (libuv, on exit, after the JSON). Ignore it; the JSON on stdout is valid.
- **Agent shells may auto-background long commands.** Read the task's output file; do not assume
  the inline result is the output.
