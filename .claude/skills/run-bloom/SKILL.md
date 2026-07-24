---
name: run-bloom
description: Run, launch, screenshot, or drive the Bloom desktop app (Bloom.exe with embedded WebView2). Use when you need to start Bloom from this worktree, attach to a running Bloom, switch workspace tabs, take a screenshot, inspect DOM/console/network over CDP, or stop a Bloom instance.
model: sonnet
---

# Run Bloom (desktop app) — quick path

Bloom is a C#/WinForms shell hosting a React UI in WebView2. You drive it
programmatically through the dev launcher's control API and CDP. This skill is
the task-oriented quick path; the **authoritative reference** for every
mechanism, fallback, rule, and field-verified gotcha is
`.github/skills/bloom-automation/SKILL.md` (shared with Copilot agents; the
driver scripts live in that directory). All paths are repo-root-relative;
commands are bash.

## 1. Make sure Bloom is running

```bash
node .github/skills/bloom-automation/launcherControl.mjs --status --json
# exit 0 → launcher live; status has state, httpPort, cdpPort, vitePort, bloomProcessId
# exit 2 → nobody home (or starting:true = a launch is underway; never start another)
node .github/skills/bloom-automation/launcherControl.mjs --ensure-running --wait-ready --json
```

`--ensure-running` handles everything: stale discovery files, a launcher
mid-startup (waits instead of double-launching), an uninitialized worktree
(go.mjs runs ./init.sh itself, `phase:"init"`), and starting the stack
decoupled from your session (Orca terminal tab when available, else detached
to `output/bloom-launcher.log`). Never launch `./go.sh` tied to your own
shell except when debugging the launcher itself, and never run an
already-built `Bloom.exe` directly (stale).

## 2. Get a .NET change into Bloom

```bash
node .github/skills/bloom-automation/launcherControl.mjs --restart --wait-ready --json
```

THE way — never ask the human to quit/restart, never kill-and-relaunch by
hand. Returns the fresh ports. Front-end (.ts/.tsx/.less) edits need no
restart at all: the Vite dev server pushes them into the running Bloom.

## 3. Drive it

```bash
node .github/skills/bloom-automation/switchWorkspaceTab.mjs --http-port <httpPort> --tab edit --json   # collection | edit | publish
node .claude/skills/run-bloom/screenshotBloom.mjs --http-port <httpPort> --out output/screenshots/bloom.png --json
```

For arbitrary DOM/console/network work, attach Playwright (loaded from
`src/BloomBrowserUI/react_components/component-tester`) to
`http://localhost:<cdpPort>` with `chromium.connectOverCDP` — the two scripts
above are templates. On the Edit tab the page content is inside the iframe
named `page`. If a "Bloom had a problem" dialog appears, use
`dismissProblemDialog.mjs` (see bloom-automation SKILL.md).

## 4. Stop it

```bash
node .github/skills/bloom-automation/launcherControl.mjs --quit-bloom   # Bloom off (graceful), launcher parked for --start/--restart
node .github/skills/bloom-automation/launcherControl.mjs --shutdown     # everything down: Bloom, dotnet watch, launcher, Vite
```

## Behavior notes

- **The human closing Bloom (window X) shuts the whole stack down** (by
  design, to free memory). A launcher that was there and is gone now usually
  means exactly that — `--ensure-running` again when needed. dotnet-watch
  rebuilds after C# edits do NOT tear the stack down.
- `/status`'s `sourceChangedSinceReady` says whether a restart would pick up
  .NET changes; it also drives the dev-only restart toast Bloom shows itself
  (bottom right, non-expiring, with a "Restart" action).
- Human path: `./go.sh` in a terminal; Ctrl+C tears everything down.

## No launcher? (Bloom started some other way)

Discover instances with
`node .github/skills/bloom-automation/bloomProcessStatus.mjs --running-bloom --json`
(HTTP-based; each entry has httpPort/cdpPort/processId/detectedRepoRoot) and
stop with `killBloomProcess.mjs`. That path has sharp edges (WMI going blind,
under-kills, orphaned watchers) — read the **Field-verified gotchas** in
`.github/skills/bloom-automation/SKILL.md` before relying on it, and don't
kill another worktree's Bloom without asking.
