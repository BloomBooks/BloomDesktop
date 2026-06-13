---
name: bloom-automation
description: Use when an agent needs to determine if Bloom is already running, detect whether the running Bloom came from a different worktree, kill Bloom or dotnet-watch parents, start Bloom from the current worktree, attach to the embedded WebView2 over CDP, inspect DOM/console/network, use dev-browser to inspect or run e2e tests against the actual exe instead of CURRENTPAGE.
argument-hint: "repo root or worktree, task such as status, restart, attach, run exe-backed tests"
user-invocable: true
---

# Bloom Exe CDP Automation

## Outcome
Use the real embedded WebView2 inside Bloom.exe as the automation target. Determine whether Bloom is already running, whether it belongs to this worktree, stop the right processes when necessary, start the current worktree through a source-aware launcher, discover the live CDP target, and drive the UI through the embedded browser instead of Bloom APIs.

## When To Use
- You need to know whether Bloom is already running.
- You need to know whether the running Bloom came from the wrong worktree.
- You need to kill a confusing stale Bloom or `dotnet watch` parent process.
- You need to start Bloom from the current worktree.
- You need to attach to the embedded WebView2 for DOM, console, network, and screenshot/debug access.
- You need Playwright tests to hit the actual exe instead of `http://localhost:8089/bloom/CURRENTPAGE` in a separate browser tab.

## Default Assumptions
- Current repo root is derived automatically by the checked-in helper.
- Bloom project path is `src/BloomExe/BloomExe.csproj`.
- Fresh automation launches must not start an already-built `Bloom.exe` directly, because that can miss local source changes.
- Use a source-aware launcher. In this repo the current default is `./go.sh` at the repo root.
- `go.sh` launches the coordinated dev flow from `src/BloomBrowserUI/scripts/go.mjs`, which in turn starts the front-end and the exe together.
- In automation mode Bloom writes a machine-readable `BLOOM_AUTOMATION_READY {...}` line to the console as soon as it knows its HTTP, CDP, and process IDs.
- Running Bloom reports its actual HTTP and CDP ports through `http://localhost:<port>/bloom/api/common/instanceInfo`.

## Commands

Examples below assume you are somewhere inside the repository and first compute the repo root once:

```bash
repo_root="$(git rev-parse --show-toplevel)"
```

Terminal:
- In this VS Code workspace, the shared bash terminal keeps whatever cwd the previous command left behind.
- Prefer running the helper through `$repo_root/.github/skills/...` or `$repo_root/scripts/...` so the command does not depend on the current working directory.

Important:
- Agents using this skill MUST use the checked-in helper scripts below, not package.json aliases and not ad hoc `wmic` commands.
- In this workspace, assume the default terminal is bash unless you explicitly opened another shell. Do not use cmd-only syntax such as `cd /d D:\...` in bash.
- Do not run raw `wmic ...` commands from a bash terminal as part of this skill workflow.
- Do not redirect WMIC output to temp files from bash.
- The VS Code bash terminals in this workspace have shown bracketed-paste/shell-integration problems where ad hoc WMIC commands appear to hang or are injected incorrectly. The checked-in Node wrappers avoid that by calling WMIC directly without going through shell redirection.
- Only fall back to raw Windows commands if the checked-in wrappers themselves are broken and you are explicitly debugging them. If you do that, prefer `cmd /c` over bash redirection.

### Status

```bash
node "$repo_root/.github/skills/bloom-automation/bloomProcessStatus.mjs"
node "$repo_root/.github/skills/bloom-automation/bloomProcessStatus.mjs" --json
node "$repo_root/.github/skills/bloom-automation/bloomProcessStatus.mjs" --running-bloom --json
node "$repo_root/.github/skills/bloom-automation/bloomProcessStatus.mjs" --http-port <httpPort> --json
```

Reports Bloom.exe processes, detected repo roots, attributable `dotnet watch` parents, ambiguous watchers, and whether the workspace API and CDP endpoint are reachable.

Use `--running-bloom` when the user explicitly wants the already-running Bloom instead of a worktree-owned instance. This scans Bloom's standard HTTP port range, asks any running Bloom for `common/instanceInfo`, and reports the ports that instance says it is using.
Use `--http-port <port>` when you launched Bloom through `./go.sh` or another repo-supported source-aware launcher and want the exact instance that owns that HTTP port. This is the preferred multi-instance workflow because it gives you the precise Bloom PID and CDP port even when several Blooms from the same worktree are running.
### Kill Bloom

```bash
node "$repo_root/.github/skills/bloom-automation/killBloomProcess.mjs"
node "$repo_root/.github/skills/bloom-automation/killBloomProcess.mjs" --only-mismatched
node "$repo_root/.github/skills/bloom-automation/killBloomProcess.mjs" --http-port <httpPort>
node "$repo_root/.github/skills/bloom-automation/killBloomProcess.mjs" --pid 12345 --watch-pid 12340
```

Use the plain form to stop all detected Bloom-related processes. Use `--only-mismatched` to stop only the Bloom instance that does not belong to the current worktree.
Use `--http-port <port>` to stop the exact Bloom instance bound to that HTTP port, together with any `dotnet` parent in its process chain. Use `--pid` or `--watch-pid` only when you already know the exact process IDs you want to stop.

Important: if Bloom was started with `dotnet watch run`, killing only `Bloom.exe` is not enough because the watcher will restart it. Prefer the provided kill script so the watcher and child process are both terminated.

### Start Bloom

```bash
"$repo_root/go.sh"
```

Use `./go.sh` as the current default launcher for this repo unless a better repo-supported source-aware launcher is documented later. It starts the coordinated front-end and exe flow and still surfaces the `Bloom ready. HTTP ..., CDP ..., Bloom PID ...` line from the underlying startup script.
`go.sh` is intentionally long-lived: for normal launches it keeps the coordinated dev flow running until the launch session ends. If Bloom reports ready and then dies shortly afterward, treat that as a failed launch instead of silently succeeding.

Agent workflow for `go.sh`:
- Start it in a background terminal.
- Do not wait for the command to finish. A successful launch is the latest `Bloom ready. HTTP ..., CDP ..., Bloom PID ...` line, not process exit.
- If the launcher later reports that the Bloom PID exited shortly after reporting ready, treat that as a failed launch and do not target that HTTP port.
- After starting it, read or poll that background terminal's output until the `Bloom ready.` line appears, then use the reported HTTP port as the identity of the new instance.
- After you have the HTTP port, continue with `bloomProcessStatus.mjs --http-port <port> --json`, `webview2Targets.mjs --http-port <port> --json --wait`, or `switchWorkspaceTab.mjs --http-port <port> --tab ...`.
- Keep the background terminal open for the lifetime of that Bloom instance. If the underlying flow restarts Bloom, target the most recent `Bloom ready.` line because Bloom may choose a different HTTP port on the restart.

### Discover the CDP target

```bash
node "$repo_root/.github/skills/bloom-automation/webview2Targets.mjs"
node "$repo_root/.github/skills/bloom-automation/webview2Targets.mjs" --json --wait
node "$repo_root/.github/skills/bloom-automation/webview2Targets.mjs" --running-bloom --json --wait
node "$repo_root/.github/skills/bloom-automation/webview2Targets.mjs" --http-port <httpPort> --json --wait
```

Use `--wait` after startup so the command blocks until the embedded browser target is available.

### Switch a workspace tab

```bash
node "$repo_root/.github/skills/bloom-automation/switchWorkspaceTab.mjs" --running-bloom --tab edit --json
node "$repo_root/.github/skills/bloom-automation/switchWorkspaceTab.mjs" --http-port <httpPort> --tab publish --json
```

This helper attaches to the reported WebView2 target over CDP, clicks the real top bar tab, waits for `workspace/tabs` to report it active, and prints the resulting state.

## Minimal Running Bloom Attach Workflow

Use this exact path when the user says to reuse the already-running Bloom and you need the fewest possible steps.

1. Report the running instance:

```bash
repo_root="$(git rev-parse --show-toplevel)" && node "$repo_root/.github/skills/bloom-automation/bloomProcessStatus.mjs" --running-bloom --json
```

2. Switch the real running Bloom through the skill-local helper:

```bash
repo_root="$(git rev-parse --show-toplevel)" && node "$repo_root/.github/skills/bloom-automation/switchWorkspaceTab.mjs" --running-bloom --tab edit --json
```

3. Only if you need low-level debugging evidence, inspect the exact CDP target:

```bash
repo_root="$(git rev-parse --show-toplevel)" && node "$repo_root/.github/skills/bloom-automation/webview2Targets.mjs" --running-bloom --json --wait
```

Notes:
- `switchWorkspaceTab.mjs` lives in this skill directory and loads Playwright from `src/BloomBrowserUI/react_components/component-tester` automatically.
- The minimal action path is step 2 by itself. Run step 1 first only when you need to report the chosen HTTP/CDP ports.
- Run step 3 only when you need raw CDP target details for debugging.

## Core Workflow
1. Run `node .github/skills/bloom-automation/bloomProcessStatus.mjs --json` if you need to know whether an ordinary current-worktree instance is already running.
2. If a current-worktree instance is already running and the user did not explicitly ask for a second instance, reuse it. If you need a fresh automation-owned instance instead, first kill the existing exact target with `node .github/skills/bloom-automation/killBloomProcess.mjs` or `node .github/skills/bloom-automation/killBloomProcess.mjs --http-port <httpPort>`, then start the replacement with the current repo-supported source-aware launcher, which is `./go.sh` unless the repo documents something better.
3. Copy the printed HTTP and CDP ports. If you need the exact PID later, run `node .github/skills/bloom-automation/bloomProcessStatus.mjs --http-port <httpPort> --json`.
4. If you instead want to reuse a current-worktree instance that Bloom found by itself, only then use repo-root matching and `--only-mismatched` cleanup.
5. Run `node .github/skills/bloom-automation/webview2Targets.mjs --http-port <httpPort> --json --wait` to discover the live WebView2 target for that exact instance when you need debugging detail.
6. Use `node .github/skills/bloom-automation/switchWorkspaceTab.mjs --http-port <httpPort> --tab <collection|edit|publish>` for top bar interactions, or attach another confirmed client to `http://localhost:<cdpPort>` if you need lower-level inspection.
7. Manipulate the UI by clicking or typing in the attached browser context. Do not use Bloom API endpoints to simulate the user action itself.
8. Use browser-native inspection for DOM, console, and network.
9. If the task is test-related, run the exe-backed Playwright suite with `BLOOM_HTTP_PORT=<httpPort> pnpm exec playwright test --config playwright.bloom-exe.config.ts`.

## Running Bloom Workflow
Use this when the user says to reuse the already-running Bloom.

1. Run `node .github/skills/bloom-automation/bloomProcessStatus.mjs --running-bloom --json`.
2. If no running Bloom instance is reported, tell the user there is no running Bloom to reuse.
3. If one is reported, do not kill or restart it because of worktree mismatch.
4. Use `node .github/skills/bloom-automation/switchWorkspaceTab.mjs --running-bloom --tab <collection|edit|publish>` for top bar actions, or `node .github/skills/bloom-automation/webview2Targets.mjs --running-bloom --json --wait` when you need CDP target detail.
5. Attach to the reported instance and work only against the `httpPort` and `cdpPort` it reported about itself.

## Rules

### Reuse the current worktree instance
- Reuse it.
- Attach over CDP and drive the UI directly.
- Do not restart unless the user explicitly wants a fresh run or you need to load new code.

### Do not accumulate worktree-owned instances
- Do not start another Bloom from the same worktree just because explicit ports are available.
- Before any fresh launch, check whether a current-worktree instance is already running.
- If one is running and the user did not explicitly ask for multi-instance behavior, either reuse it or kill that exact instance before launching a replacement.
- Only keep multiple current-worktree Bloom instances alive when the user explicitly asked for that workflow or when the task itself is a verified multi-instance scenario.

### Treat wrong-worktree Bloom as a blocker
- Treat that as a blocker because it produces extremely confusing results.
- Report the detected repo root from `node .github/skills/bloom-automation/bloomProcessStatus.mjs`.
- Kill the mismatched process with `node .github/skills/bloom-automation/killBloomProcess.mjs --only-mismatched`.
- Then start the current worktree.

### Start with `go.sh`, not raw watch commands
- Start it from the current worktree.
- Use `./go.sh` for fresh launches from this repo unless a better repo-supported source-aware launcher is documented.
- Do not launch an already-built `Bloom.exe` directly, and do not call `node scripts/watchBloomExe.mjs` directly unless you are debugging the launcher implementation itself.
- Before using `./go.sh` for a fresh launch, clean up any existing current-worktree instance unless the user explicitly asked for concurrent instances.
- Treat the printed HTTP port as the identity of that instance. Use `bloomProcessStatus.mjs --http-port <port>`, `webview2Targets.mjs --http-port <port>`, and `killBloomProcess.mjs --http-port <port>` to target it precisely.
- Never wait for `go.sh` to exit as a readiness signal. It is a long-lived launcher. Wait for the latest `Bloom ready.` line in the background terminal output instead, and treat a later `Bloom PID ... exited shortly after reporting ready` message as a failed launch.

### Reuse the running Bloom when the user asks for it
- Run `node .github/skills/bloom-automation/bloomProcessStatus.mjs --running-bloom --json`.
- Reuse the returned running Bloom instance even if it does not match the current worktree.
- Use `node .github/skills/bloom-automation/switchWorkspaceTab.mjs --running-bloom --tab <collection|edit|publish>` for direct top bar interaction, or discover its CDP target with `node .github/skills/bloom-automation/webview2Targets.mjs --running-bloom --json --wait` when you need the raw target details.
- Do not kill or restart it unless the user explicitly asks for that.

### Prove browser-native access when needed
- Show the CDP target from `node .github/skills/bloom-automation/webview2Targets.mjs --json --wait`.
- Attach with Playwright.
- Demonstrate reading `body.className`, the top-bar iframe, console messages, and the `workspace/selectTab` request.
- For multi-instance work, prefer `webview2Targets.mjs --http-port <port> --json --wait` and the matching `cdpPort` it reports.

### Verified two-instance smoke path
- Launch one instance with `./go.sh` and record the HTTP port from its `Bloom ready.` line.
- Launch a second instance with `./go.sh` and record the HTTP port from its `Bloom ready.` line.
- Target the first instance with `switchWorkspaceTab.mjs --http-port <firstPort> --tab edit`.
- Target the second instance with `switchWorkspaceTab.mjs --http-port <secondPort> --tab publish`.
- Use the reported ports throughout; do not mix `--running-bloom` with this workflow.

## Confirmed Path

- `playwright` Node library via `http://localhost:<cdpPort>` using the `cdpPort` reported by `common/instanceInfo` or `webview2Targets.mjs`
- `@playwright/test` runner via the exe-backed suite in `src/BloomBrowserUI/react_components/component-tester`

Not confirmed here:
- `chrome-devtools-mcp` as an attached client to Bloom's existing WebView2 target
- the current Playwright MCP browser wrappers as an attached client to Bloom's existing WebView2 target

Reason: the current MCP wrappers in this environment control their own browser instance and do not expose a way to attach to an already-running external CDP endpoint. Until those tools add explicit attach support, prefer the `go.sh` launcher plus Playwright path above.

## Tests
- Run from `src/BloomBrowserUI/react_components/component-tester`.
- Use `BLOOM_HTTP_PORT=<httpPort> pnpm exec playwright test --config playwright.bloom-exe.config.ts`.
- Run one file with `BLOOM_HTTP_PORT=<httpPort> pnpm exec playwright test --config playwright.bloom-exe.config.ts ../TopBar/component-tests/bloom-exe-tabs.uitest.ts`.

These tests attach to the real Bloom.exe target over CDP and verify tab switching plus console and network observation.

## Notes
- Prefer the Node helpers over PowerShell. The Node scripts use `wmic`, `taskkill`, and `dotnet` directly because the PowerShell path proved too brittle.
- Prefer the checked-in repo entrypoints and helper commands over raw Windows shell commands. Subagents should normally run `./go.sh`, `node .github/skills/bloom-automation/bloomProcessStatus.mjs --json`, `node .github/skills/bloom-automation/killBloomProcess.mjs --only-mismatched`, `node .github/skills/bloom-automation/webview2Targets.mjs --json --wait`, and `node .github/skills/bloom-automation/switchWorkspaceTab.mjs --running-bloom --tab edit`, not ad hoc `wmic` commands. If the repo later documents a better source-aware launcher than `./go.sh`, prefer that documented launcher instead.
- For agent-driven launches, the background terminal is part of the control plane. Leave it running and poll its output for the latest `Bloom ready.` line instead of waiting for command completion.
- Exact-target cleanup is intentionally strict: `killBloomProcess.mjs --http-port <port>` should only kill the instance that actually reports that HTTP port, and should fail without killing anything if that target cannot be resolved.
- When reporting work, include the helper commands you used so reviewers can confirm the workflow stayed on the supported path.
- Wrong-worktree detection is authoritative when a real `Bloom.exe` child exists or when `dotnet watch` was started with an absolute `--project` path.
- When more than one Bloom is running from the same worktree, repo-root matching is not enough. Use the explicit HTTP port workflow.
- For ad hoc local debugging in this workspace, `dev-browser --connect http://localhost:<cdpPort>` can attach directly to the existing Bloom WebView2 target. Use it as a low-friction inspection client.
- After attaching to Bloom's WebView2 target, if Bloom is on the Edit tab, the editable page content lives inside the iframe named `page`; the top-level document mostly hosts shell UI plus the root dialog container.
- **"Bloom had a problem" report dialogs.** Bloom surfaces errors (including non-fatal ones, especially in Debug builds) as a modal "Bloom had a problem" dialog. It is hosted in its OWN WinForms window with its own WebView2, so it appears as a SEPARATE CDP page target — not inside the shell document or the `page` iframe — and in dev it is even served from the Vite port rather than the Bloom http port. Detect it by the `.problem-dialog` root (from `problemDialog/*.tsx`) present in ANY page target. Never just leave one sitting on screen, and never move past it silently.
  - Use `node .github/skills/bloom-automation/dismissProblemDialog.mjs --http-port <httpPort> [--wait] [--json]`. It (1) finds the dialog by DOM (so it never closes a legitimate modal), (2) GATHERS the underlying problem — it clicks the dialog's own "Learn More" to reveal the exception + missing-file/stack that Bloom would send, and prints it, and (3) closes the dialog with the SAME action as its Close button, `POST /bloom/api/common/closeReactDialog`, which does NOT submit. It drains a backlog (Bloom queues reports and shows them one at a time), gathering each, up to a cap.
  - NEVER click Submit / POST `problemReport/submit` in automation: that sends a report (with a screenshot and the book) to Bloom's servers.
  - If the SAME problem keeps reappearing after being closed, it is a real recurring error in the code under test (e.g. a resource that 404s on every render) — read the gathered detail, fix the root cause, and re-test; do not just loop-dismiss. The Bloom log at `%TEMP%\SIL\Bloom\Log-*.txt` has the same detail if you need it out-of-band, but note its writes can lag, so the dialog's own "Learn More" (what the helper scrapes) is the authoritative live source.

## Completion Checks
- Bloom's status is known: not running, running from current worktree, or running from different worktree.
- Any mismatched Bloom instance has been stopped before running the current worktree, unless you intentionally started a separate explicit-port instance.
- The chosen HTTP port returns `common/instanceInfo`, including the exact Bloom PID and CDP port.
- The reported CDP endpoint responds at `http://localhost:<cdpPort>/json/version`.
- `node .github/skills/bloom-automation/webview2Targets.mjs --http-port <httpPort> --json --wait` returns a real Bloom target.
- The automation client can read DOM state and interact with the embedded top bar.
- If tests were requested, the exe-backed Playwright suite passes.

## Output Contract
Report:
- whether Bloom was already running
- which repo root the running Bloom came from
- whether you killed a mismatched or stale process
- which command you used to start Bloom
- which HTTP/CDP ports were assigned
- which Bloom PID and `dotnet` PID were associated with that instance
- which client attached successfully
- what browser-native evidence you collected: DOM state, console output, network request, tab state, or test results

## Example Prompts
- `troubleshoot why the page is refreshing when we open page settings`

## Debugging tips
Use node or bash scripts. Avoid powershell. Use the "dev-browser" cli instead of playwright for interactive debugging/driving Bloom. Use "dev-browser --help" to see the available commands and options. If the user hasn't installed dev-browser, ask them for permission to install it (https://github.com/SawyerHood/dev-browser).

## Driving the editable page over CDP (DOM interaction)

These patterns were verified driving the real Bloom.exe WebView2 via `dev-browser --connect http://localhost:<cdpPort>`. Pages returned by `browser.getPage(...)` are full Playwright Page objects.

- **Frames.** The Edit screen is several sibling iframes under the workspace root. Get them with Playwright by name/url, not by index:
    - page (editable content): `page.frames().find(f => f.name() === 'page')`
    - toolbox: `page.frames().find(f => (f.url()||'').includes('toolbox'))`
    - others: `pageList`, plus the top/anon root.
  After a tab switch or page change the page frame reloads; re-find it and poll until your target selector exists (e.g. `.bloom-page`) before acting.
- **Clicking inside a frame: use `elementHandle.click({force:true})`, not `page.mouse.click(x,y)`.** `boundingBox()` for an element inside an iframe returns *frame-relative* coords, but `page.mouse` uses *top-level viewport* coords — so computed clicks land in the wrong place. `elementHandle.click()` is frame-aware. `{force:true}` is also needed because Bloom overlays a format-gear (`#formatButton`, `.bloom-ui`) on focused editables that intercepts normal clicks ("subtree intercepts pointer events").
- **Typing into a cell/text box.** Click the `.bloom-editable` with `{force:true}`, then `page.keyboard.type(...)`. To replace existing text: `Control+A` then `Delete` first. Re-query the editables array between cells (the DOM can re-render).
- **Saving a page.** There is no save button; navigating away persists the page. Switching workspace tabs (Collections → Edit) forces a save+reload — handy for a round-trip test. The saved book HTML is the book folder's main `<BookName>.htm`; ignore the `*-memsim-*` and `*Pagelist*` temp files. Find the book folder from a page-frame iframe `src` (e.g. `…/Bloom/<collection>/Book-xxxx/`).
- **Undo is a C# accelerator — synthetic Ctrl+Z over CDP does NOT reach it.** Instead call the exact entry points C# uses, from the *top* frame: `window.workspaceBundle.handleUndo()` and `window.workspaceBundle.canUndo()` (returns `"yes"`/`"fail"`).
- **Cross-frame bundle calls.** Page-frame functions are on `window.editablePageBundle.*` (read it inside the page frame); workspace/root functions on `window.workspaceBundle.*` (top frame). Useful for asserting state without scraping the DOM.
- **Toolbox tools.** Optional/experimental tools must first be enabled via the "More…" settings checkbox (e.g. `#<toolname>Check`, which calls `toolboxBundle.showOrHideTool_click(this)`), then activated by clicking their accordion header `[data-toolId="<id>Tool"]`. Tool buttons expose `title`/`aria-label` (e.g. `button[title="Insert Row Below"]`), not `alt`.
- **Capture console errors** while exercising React UI: `page.on('console', m => { if (m.type()==='error') errors.push(m.text()); })` — the cheapest check for "multiple React"/hook/MUI problems.
- **dev-browser script gotchas.** Scripts run in a QuickJS sandbox (no Node/`require`/`fs`). Keep them small and single-purpose; end by logging the state you need. Bump `--timeout` (e.g. 40–50) for multi-step scripts. Avoid stray bare expressions (e.g. a lone `window` line) — they can abort the script.
- **After relinking/rebuilding a linked front-end dependency**, restart Bloom (kill + `./go.sh`) rather than relying on hot reload, so Vite re-optimizes the dep; otherwise the page may run the stale pre-bundled copy.
