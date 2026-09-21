---
name: run-bloom
description: Run, restart, stop, or drive the Bloom desktop app (Bloom.exe with its embedded WebView2) from this worktree — start it through the dev launcher, attach over CDP to inspect DOM/console/network or take a screenshot, switch workspace tabs, drive its HTTP API, dismiss a "Bloom had a problem" dialog, find or stop a Bloom another worktree started. Use whenever a task needs a running Bloom or needs to look at what Bloom is showing.
argument-hint: "what you need: status, start, restart after a C# change, screenshot, attach, stop; and which worktree"
---

# Run Bloom (desktop app)

Bloom is a C#/WinForms shell hosting a React UI in WebView2. You drive it through the dev
launcher's control API and CDP. This file is the quick path; `reference.md` beside it has every
mechanism, rule, and field-verified gotcha, and the driver scripts live in this folder. Paths
are repo-root-relative; commands are bash.

## 1. Make sure Bloom is running

```bash
node .claude/skills/run-bloom/launcherControl.mjs --status --json
# exit 0 → launcher live; status has state, httpPort, cdpPort, vitePort, bloomProcessId
# exit 2 → nobody home (or starting:true = a launch is underway; never start another)
node .claude/skills/run-bloom/launcherControl.mjs --ensure-running --wait-ready --json
```

`--ensure-running` handles everything: stale discovery files, a launcher mid-startup (waits
instead of double-launching), an uninitialized worktree (go.mjs runs `./init.sh` itself,
`phase:"init"`), and starting the stack decoupled from your session (an Orca terminal tab when
available, else detached to `output/bloom-launcher.log`). Never launch `./go.sh` tied to your own
shell except when debugging the launcher itself, and never run an already-built `Bloom.exe`
directly (it is stale).

A cold first build outlasts `--wait-ready`'s default patience: the wait can give up while
`--status` still says `state:"building"`, and that is not a failure. Pass `--timeout-ms 600000`,
run the command as a background task and act on its completion; never sleep-poll `--status`, and
never re-invoke `--ensure-running` after a timeout without checking `--status` first.

## 2. Get a .NET change into Bloom

```bash
node .claude/skills/run-bloom/launcherControl.mjs --restart --wait-ready --json
```

The only way: never ask the human to quit and restart, never kill and relaunch by hand. It
returns the fresh ports. Front-end (`.ts`/`.tsx`/`.less`) edits need no restart; the Vite dev
server pushes them into the running Bloom.

## 3. Drive it

```bash
node .claude/skills/run-bloom/switchWorkspaceTab.mjs --http-port <httpPort> --tab edit --json   # collection | edit | publish
node .claude/skills/run-bloom/screenshotBloom.mjs --http-port <httpPort> --out output/screenshots/bloom.png --json
node .claude/skills/run-bloom/webview2Targets.mjs --http-port <httpPort> --json --wait          # the live CDP target
```

For arbitrary DOM/console/network work, attach Playwright (loaded from
`src/BloomBrowserUI/react_components/component-tester`) to `http://127.0.0.1:<cdpPort>` with
`chromium.connectOverCDP`; the scripts above are templates. On the Edit tab the page content is
inside the iframe named `page`. Drive the UI by clicking and typing; use Bloom's HTTP API only for
scripted batch setup, from inside the page (`reference.md`, "Driving Bloom HTTP APIs over CDP").
Never send a request to a live Bloom just to see whether an endpoint exists: an unknown endpoint
raises a modal error dialog on the developer's screen. Grep `src/BloomExe/web` instead.

If a **"Bloom had a problem"** dialog appears, never leave it or click past it:
`node .claude/skills/run-bloom/dismissProblemDialog.mjs --http-port <httpPort> --json` gathers the
underlying exception and closes it without submitting a report. A problem that reappears after
being closed is a real bug in the code under test.

## 4. Stop it

```bash
node .claude/skills/run-bloom/launcherControl.mjs --quit-bloom   # Bloom off (graceful); launcher parked for --start/--restart
node .claude/skills/run-bloom/launcherControl.mjs --shutdown     # everything down: Bloom, dotnet watch, launcher, Vite
```

## Behavior notes

- **The human closing Bloom (window X) shuts the whole stack down** (by design, to free memory).
  A launcher that was there and is gone usually means exactly that; `--ensure-running` again when
  needed. dotnet-watch rebuilds after C# edits do not tear the stack down.
- `/status`'s `sourceChangedSinceReady` says whether a restart would pick up .NET changes; it also
  drives the dev-only restart toast Bloom shows itself.
- **Port 8089 is first-come, not per-worktree.** Always take `httpPort`/`cdpPort` from the
  launcher status; a hard-coded 8089 may be another worktree's Bloom.
- Human path: `./go.sh` in a terminal; Ctrl+C tears everything down.

## No launcher? (Bloom started some other way)

Discover instances with `node .claude/skills/run-bloom/bloomProcessStatus.mjs --running-bloom --json`
(HTTP-based; each entry has httpPort/cdpPort/processId/detectedRepoRoot) and stop with
`killBloomProcess.mjs`. That path has sharp edges (WMI going blind, under-kills, orphaned
watchers): read "Field-verified gotchas" in `reference.md` first, and never kill another
worktree's Bloom without asking. A Bloom from the wrong worktree is a blocker, not something to
work around.

## Beyond the web content

The "Edit with AI…" image editor spans a third frame and has a free dummy model for zero-cost
runs: `ai-image-editor-driving.md` in this folder and `driveAiImageEditor.mjs`.
