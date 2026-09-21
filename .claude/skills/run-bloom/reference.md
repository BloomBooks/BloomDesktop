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

## Driving WinForms and OS dialogs (UI Automation, no pointer)

CDP reaches only the web content inside Bloom's WebView2s. Everything around it — the
collection Settings dialog's tabs and OK/Cancel/Help buttons, any other WinForms `Form`, and
the **native OS dialogs** Bloom opens (the file picker, a raw `MessageBox`) — is driven over
Windows UI Automation instead, with `.claude/skills/run-bloom/winformsUia.ps1` (stock
PowerShell 5.1, nothing to install). It uses `InvokePattern`, `SelectionItemPattern`,
`ValuePattern` and `WindowPattern`, so it never moves the pointer, sends a keystroke or takes
focus; it works on an off-screen (`headless`) window and while the developer is typing in
another app. Never fall back to synthesizing mouse or keyboard input for these.

```bash
P=.claude/skills/run-bloom/winformsUia.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File $P windows  -ProcessId <bloomPid>
powershell -NoProfile -ExecutionPolicy Bypass -File $P tree     -ProcessId <bloomPid> -Window CollectionSettingsDialog -Depth 2
powershell -NoProfile -ExecutionPolicy Bypass -File $P select   -ProcessId <bloomPid> -Window CollectionSettingsDialog -Control "Book Making"
powershell -NoProfile -ExecutionPolicy Bypass -File $P invoke   -ProcessId <bloomPid> -Window CollectionSettingsDialog -Control _cancelButton
powershell -NoProfile -ExecutionPolicy Bypass -File $P setvalue -ProcessId <bloomPid> -Window Open -Control "File name:" -Value "C:\full\path\image.png"
powershell -NoProfile -ExecutionPolicy Bypass -File $P invoke   -ProcessId <bloomPid> -Window Open -Control Open
```

Each of these works against a real `./go.sh` Bloom:

- **A WinForms dialog.** Settings opened from the top bar (a CDP click on the web "Settings"
  button); `select` switched to the Book Making tab; `invoke` on `_cancelButton` closed it.
- **Bloom's own OS file picker.** The image gallery's picker (`ImageGalleryApi`, a
  `BloomOpenFileDialog`) came up as `[Window] name='Open'`; `setvalue` on "File name:" plus
  `invoke` on "Open" closed it, and Bloom's API returned the chosen path.
- **A message box inside a real process.** Given a relative path, that picker raised its own
  "file not found" box, owned by the picker; `tree` read its text and `invoke ... -Control OK`
  dismissed it.

What to know:

- **Addressing.** A WinForms control's UIA `AutomationId` is its designer `Name`, so the
  dialog is `CollectionSettingsDialog`, its buttons `_okButton`/`_cancelButton`/`_helpButton`,
  its tab strip `_tab` with tab items whose `Name` is the visible caption ("Languages", "Book
  Making", ...). OS dialogs have no useful ids; use the visible names ("Open", "File name:",
  "Cancel", "OK"). `-Window` and `-Control` accept either. `tree` shows both, so dump first
  when unsure. When several controls share a name (the picker's "File name:" is a label, a
  ComboBox and an Edit), the script takes the outermost one that supports the command.
- **A modal dialog is not a top-level window in UIA.** A WinForms dialog sits under its owner
  (the `Shell` window) as a descendant, and a message box raised by a dialog sits under that
  dialog; an OS file picker is top-level. `list` shows only top-level windows, `windows` shows
  all of them, and every command searches both places.
- **Win32 controls need the client-side proxies, and PowerShell does not load them.** Without
  them every control in a file picker or message box is an inert `[Pane]` with no patterns.
  The script registers them itself, through a compiled C# shim, because doing it from
  PowerShell throws. If a `tree` of an OS dialog shows only panes, that registration failed
  (the script says so on stderr). The proxies can also attach a beat late: the first dump
  right after a dialog appears once showed its "Open" button as a pane, and the next query
  showed it as a `[Button]`; re-query before concluding a control is not drivable.
- **Give the file picker an absolute path.** A relative one makes the dialog raise a
  "file not found" box and stay open; dismiss that with `invoke -Window <dialog> -Control OK`
  and set the value again.
- **In an e2e test, still prefer arming the answer.** `e2e/nextFileToChoose` makes
  `BloomOpenFileDialog`/`BloomFolderChooser` answer without showing anything, which is
  faster and cannot be upset by whatever else is on the screen. UIA is for the cases that
  hook does not cover and for interactive/diagnostic driving.
- **Not reachable this way:** WinForms `LinkLabel`s (the "Change..." language links on the
  Languages tab) expose no pattern at all, and the web content on the other tabs is a WebView2
  — drive that over CDP. `tree` prunes WebView2 subtrees on purpose.
- **Reading a raw `MessageBox`.** It appears as a `[Window]` named by its caption (`Error` for
  the one `WebView2Browser` shows when a WebView2 fails to initialize), class `#32770`, owned
  by the Shell; `tree -Window Error -Depth 3` prints its `[Text]` children, which is the whole
  message, and `invoke -Window Error -Control OK` dismisses it. Read before dismissing: that
  particular one exits Bloom on close. A raw `MessageBox` ignores `BLOOM_AUTOMATION_MONITOR`,
  so it lands on the developer's screen even in a headless run.
- **`close` really closes.** `close -Window Shell` shuts Bloom down exactly like the title-bar
  X (and with the launcher, takes the whole stack with it). Use it only on the window you mean.
- **The dialog's own WebView2 and CDP.** Outside `--e2e`, every ReactControl gets its own
  WebView2 environment and browser process, and each is given the same
  `--remote-debugging-port`. It can happen that while the Book Making tab is showing,
  the CDP endpoint listed *only* the dialog's page, and the shell page came back when the dialog
  closed. So the endpoint can flip between browser processes; re-list targets after a WinForms
  dialog opens or closes rather than holding on to a page handle.
- **Under `--e2e`, opening the Settings dialog currently kills Bloom.** See "WinForms surfaces
  are invisible to CDP" in `src/BloomE2E/AUTOMATION-DEBT.md` for the cause (a WebView2 DPI
  awareness mismatch against the shared e2e environment) before writing a test that opens it.

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
