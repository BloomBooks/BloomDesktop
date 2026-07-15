---
name: run-bloom
description: Run, launch, screenshot, or drive the Bloom desktop app (Bloom.exe with embedded WebView2). Use when you need to start Bloom from this worktree, attach to a running Bloom, switch workspace tabs, take a screenshot, inspect DOM/console/network over CDP, or stop a Bloom instance.
model: sonnet
---

# Run Bloom (desktop app)

Bloom is a C#/WinForms shell hosting a React UI in WebView2. You drive it
programmatically: launch with `./go.sh`, discover the instance's HTTP/CDP
ports, then attach over CDP. The driver scripts live in
`.github/skills/bloom-automation/` (shared with Copilot agents) plus
`screenshotBloom.mjs` in this skill directory. All paths below are relative
to the repo root; all commands are bash unless noted.

For deep detail (multi-instance rules, exe-backed Playwright tests, CDP
workflow), read `.github/skills/bloom-automation/SKILL.md`. This skill is the
verified quick path.

## Prerequisites

Dev machines already have: node 22 + pnpm 11.5.2 (see .node-version and the
packageManager field), .NET SDK 10, WebView2 runtime. On a machine missing
dependencies, run `./init.sh` (fetches C# deps, pnpm installs, initial
`pnpm build`) — documented but not re-verified here; a failed C# build with
CS0246 errors (missing `PodcastUtilities` etc.) means `./init.sh` is needed.

Never run `pnpm build` while a watch/dev build is running (see AGENTS.md).
Never launch a previously built `Bloom.exe` directly — it can be stale.

## Step 1: Is Bloom already running?

```bash
node .github/skills/bloom-automation/bloomProcessStatus.mjs --running-bloom --json
```

Look at `runningBloomInstances` — each entry has `httpPort`, `cdpPort`,
`processId`, `detectedRepoRoot`, `vitePort`. This discovery is HTTP-based
(scans Bloom's standard port range, asks each instance
`/bloom/api/common/instanceInfo`) and keeps working even when WMI breaks
(see Gotchas). If an instance from **this** worktree is running, reuse it
and skip to Step 3. Don't kill another worktree's Bloom without asking.

## Step 2: Launch from source

Run in a **background** task (it is a long-lived launcher — never wait for
exit):

```bash
./go.sh > /tmp/go-bloom.log 2>&1   # run_in_background
```

It starts Vite on a random port, waits for quiescence, then `dotnet watch
run` on `src/BloomExe`. Poll the log for the ready line (~2–4 min when the
C# build is warm):

```bash
until grep -qE "Bloom ready\. HTTP|exited shortly after" /tmp/go-bloom.log; do sleep 2; done
grep -E "Bloom ready\. HTTP" /tmp/go-bloom.log | tail -1
# => Bloom ready. HTTP 8092, CDP 8094, Bloom PID 51040.
```

The same info is on the machine-readable line
`BLOOM_AUTOMATION_READY {"processId":...,"httpPort":...,"cdpPort":...}`.
Use that HTTP port as the identity of your instance in every later command.
Multiple instances coexist; each takes the next port block (8089, 8092, …).

Sanity check the instance:

```bash
curl -s http://localhost:<httpPort>/bloom/api/common/instanceInfo
```

## Step 3: Drive it

Switch workspace tabs (clicks the real tab over CDP, waits for the backend
to report it active):

```bash
node .github/skills/bloom-automation/switchWorkspaceTab.mjs --http-port <httpPort> --tab edit --json
# --tab collection | edit | publish
```

Screenshot the embedded browser (lands in git-ignored `output/`):

```bash
node .claude/skills/run-bloom/screenshotBloom.mjs --http-port <httpPort> --out output/screenshots/bloom.png --json
```

For arbitrary DOM/console/network work, attach Playwright (loaded from
`src/BloomBrowserUI/react_components/component-tester`) to
`http://localhost:<cdpPort>` with `chromium.connectOverCDP` — see
`switchWorkspaceTab.mjs` and `screenshotBloom.mjs` as templates. On the Edit
tab, the page content lives inside the iframe named `page`; the top-level
document is shell UI.

## Step 4: Stop the instance you started

```bash
node .github/skills/bloom-automation/killBloomProcess.mjs --http-port <httpPort> --json
```

Check that `killedProcessIds` includes **the Bloom PID itself**, not just
its `dotnet` parents. In real runs here it was sometimes `[]` (WMI blind +
`taskkill` failing silently) and sometimes partial (parents killed, Bloom.exe
survived). For any PID still standing, fall back to PowerShell:

```powershell
Stop-Process -Id <bloomPid> -Force
```

Then stop the `./go.sh` background task itself (TaskStop or kill its shell).
This matters: after Bloom.exe dies, its `dotnet watch` chain stays alive
("Waiting for a file to change before restarting") and will **relaunch Bloom
on the next C# file edit** if left orphaned.

## Human path

`./go.sh` in a terminal; the Bloom window opens on the desktop; Ctrl+C shuts
the whole flow down.

## Gotchas (all hit in real runs)

- **WMI/wmic can go blind mid-session.** `bloomProcessStatus.mjs` (plain
  mode) and `killBloomProcess.mjs` enumerate processes via `wmic`; on this
  machine WMI stopped answering partway through a session — status reported
  zero Bloom processes while one was demonstrably serving HTTP, and
  `Get-CimInstance` hung for minutes. Trust the HTTP-based
  `--running-bloom` discovery and `instanceInfo` over process enumeration.
- **`killBloomProcess.mjs` under-kills.** Observed both `killedProcessIds:
  []` for a valid target and partial kills where the `dotnet watch` parents
  died but Bloom.exe survived. Always verify the port went dark
  (`instanceInfo` curl fails) and the Bloom PID is gone; `Stop-Process` any
  survivors.
- **Orphaned `dotnet watch` chains relaunch Bloom.** If Bloom.exe is killed
  but its watcher chain survives (e.g. someone kills Blooms from Task
  Manager), the watchers sit at "Waiting for a file to change" and respawn
  Bloom on the next C# edit. Check with `bloomProcessStatus.mjs --json`
  (`watchProcesses`) and `Stop-Process` stale ones.
- **Never type `taskkill /PID ...` in Git Bash** — MSYS rewrites `/PID` to
  `C:/Program Files/Git/PID`. Use the node helpers or PowerShell.
- **Grep for `Bloom ready\. HTTP`, not `Bloom ready\.`** — early in the log
  watchBloomExe prints an instructional message that *quotes* the phrase
  `'Bloom ready.'`, which matches the looser pattern long before launch
  completes.
- **`dotnet watch` noise:** the launch log contains scary
  `⚠ msbuild: [Failure] Package 'X' was restored using .NETFramework...`
  lines. They are warnings; launch still succeeds. Don't grep the log for
  bare `Failure`/`error` as a failure signal — wait for `Bloom ready.` or
  `exited shortly after` instead.
- **`bloomProcessStatus.mjs` may print
  `Assertion failed: !(handle->flags & UV_HANDLE_CLOSING)`** (libuv, on
  exit, after the JSON is complete). Ignore it; the JSON on stdout is valid.
- **Agent shells may auto-background kill commands.** Capture the task
  output file and read it; don't assume the inline result is the output.
- `body.className` of the top-level page is `developer` in dev builds;
  page URL looks like `http://localhost:<port>/bloom/C%3A/...Temp/bloomXXXX.htm`.

## Tests

Exe-backed Playwright suite (not re-run this session; see
`.github/skills/bloom-automation/SKILL.md` for detail): from
`src/BloomBrowserUI/react_components/component-tester`,
`BLOOM_HTTP_PORT=<httpPort> pnpm playwright test --config playwright.bloom-exe.config.ts`.
TypeScript unit tests: `pnpm test` in `src/BloomBrowserUI` (Vitest).
