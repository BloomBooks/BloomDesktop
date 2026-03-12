---
name: bloom-exe-cdp-automation
description: Use when an agent needs to determine if Bloom is already running, detect whether the running Bloom came from a different worktree, kill Bloom or dotnet-watch parents, start Bloom from the current worktree, attach to the embedded WebView2 over CDP, inspect DOM/console/network, or run Playwright tests against the actual exe instead of CURRENTPAGE.
argument-hint: "repo root or worktree, task such as status, restart, attach, run exe-backed tests"
user-invocable: true
---

# Bloom Exe CDP Automation

## Outcome
Use the real embedded WebView2 inside Bloom.exe as the automation target. Determine whether Bloom is already running, whether it belongs to this worktree, stop the right processes when necessary, start the current worktree, discover the live CDP target, and drive the UI through the embedded browser instead of Bloom APIs.

## When To Use
- You need to know whether Bloom is already running.
- You need to know whether the running Bloom came from the wrong worktree.
- You need to kill a confusing stale Bloom or `dotnet watch` parent process.
- You need to start Bloom from the current worktree.
- You need to attach to the embedded WebView2 for DOM, console, network, and screenshot/debug access.
- You need Playwright tests to hit the actual exe instead of `http://localhost:8089/bloom/CURRENTPAGE` in a separate browser tab.

## Default Assumptions
- Current repo root is derived automatically from the script location.
- Bloom project path is `src/BloomExe/BloomExe.csproj`.
- Current-worktree launches normally start their HTTP server search at `http://localhost:8089` and use the next odd ports if needed.
- Running Bloom reports its actual HTTP and CDP ports through `http://localhost:<port>/bloom/api/common/instanceInfo`.
- Bloom's embedded WebView2 currently exposes CDP on port `9222` in debug builds.

## Commands

Run all of these from `src/BloomBrowserUI` unless noted otherwise.

Important:
- Agents using this skill MUST use the checked-in helper scripts below, not package.json aliases and not ad hoc `wmic` commands.
- In this workspace, assume the default terminal is bash unless you explicitly opened another shell. Do not use cmd-only syntax such as `cd /d D:\...` in bash.
- Do not run raw `wmic ...` commands from a bash terminal as part of this skill workflow.
- Do not redirect WMIC output to temp files from bash.
- The VS Code bash terminals in this workspace have shown bracketed-paste/shell-integration problems where ad hoc WMIC commands appear to hang or are injected incorrectly. The checked-in Node wrappers avoid that by calling WMIC directly without going through shell redirection.
- Only fall back to raw Windows commands if the checked-in wrappers themselves are broken and you are explicitly debugging them. If you do that, prefer `cmd /c` over bash redirection.

### Status

```bash
node ../../.github/skills/bloom-exe-cdp-automation/bloomProcessStatus.mjs
node ../../.github/skills/bloom-exe-cdp-automation/bloomProcessStatus.mjs --json
node ../../.github/skills/bloom-exe-cdp-automation/bloomProcessStatus.mjs --running-bloom --json
```

Reports Bloom.exe processes, detected repo roots, attributable `dotnet watch` parents, ambiguous watchers, and whether the workspace API and CDP endpoint are reachable.

Use `--running-bloom` when the user explicitly wants the already-running Bloom instead of a worktree-owned instance. This scans Bloom's standard HTTP port range, asks any running Bloom for `common/instanceInfo`, and reports the ports that instance says it is using.
### Kill Bloom

```bash
node ../../.github/skills/bloom-exe-cdp-automation/killBloomProcess.mjs
node ../../.github/skills/bloom-exe-cdp-automation/killBloomProcess.mjs --only-mismatched
```

Use the plain form to stop all detected Bloom-related processes. Use `--only-mismatched` to stop only the Bloom instance that does not belong to the current worktree.

Important: if Bloom was started with `dotnet watch run`, killing only `Bloom.exe` is not enough because the watcher will restart it. Prefer the provided kill script so the watcher and child process are both terminated.

### Start Bloom

```bash
node ../../.github/skills/bloom-exe-cdp-automation/bloomRun.mjs
node ../../.github/skills/bloom-exe-cdp-automation/bloomRun.mjs --watch
```

Use `--watch` when you expect to iterate. These helpers compute the repo root and pass an absolute `--project` path to `dotnet`, which avoids ambiguous relative watcher command lines.

### Discover the CDP target

```bash
node ../../.github/skills/bloom-exe-cdp-automation/webview2Targets.mjs
node ../../.github/skills/bloom-exe-cdp-automation/webview2Targets.mjs --json --wait
node ../../.github/skills/bloom-exe-cdp-automation/webview2Targets.mjs --running-bloom --json --wait
```

Use `--wait` after startup so the command blocks until the embedded browser target is available.

## Core Workflow
1. Run `node ../../.github/skills/bloom-exe-cdp-automation/bloomProcessStatus.mjs --json`.
2. If Bloom is not running, start it from the current worktree with `node ../../.github/skills/bloom-exe-cdp-automation/bloomRun.mjs --watch` or `node ../../.github/skills/bloom-exe-cdp-automation/bloomRun.mjs`.
3. If Bloom is running from a different worktree, stop it with `node ../../.github/skills/bloom-exe-cdp-automation/killBloomProcess.mjs --only-mismatched`.
4. If Bloom is running from the correct worktree and the task only needs browser automation, do not restart it.
5. Run `node ../../.github/skills/bloom-exe-cdp-automation/webview2Targets.mjs --json --wait` to discover the live WebView2 target.
6. Attach a confirmed client to `http://localhost:9222`.
7. Manipulate the UI by clicking or typing in the attached browser context. Do not use Bloom API endpoints to simulate the user action itself.
8. Use browser-native inspection for DOM, console, and network.
9. If the task is test-related, run the exe-backed Playwright suite.

## Running Bloom Workflow
Use this when the user says to reuse the already-running Bloom.

1. Run `node ../../.github/skills/bloom-exe-cdp-automation/bloomProcessStatus.mjs --running-bloom --json`.
2. If no running Bloom instance is reported, tell the user there is no running Bloom to reuse.
3. If one is reported, do not kill or restart it because of worktree mismatch.
4. Run `node ../../.github/skills/bloom-exe-cdp-automation/webview2Targets.mjs --running-bloom --json --wait`.
5. Attach to the reported instance and work only against the ports it reported about itself.

## Rules

### Reuse the current worktree instance
- Reuse it.
- Attach over CDP and drive the UI directly.
- Do not restart unless the user explicitly wants a fresh run or you need to load new code.

### Treat wrong-worktree Bloom as a blocker
- Treat that as a blocker because it produces extremely confusing results.
- Report the detected repo root from `node ../../.github/skills/bloom-exe-cdp-automation/bloomProcessStatus.mjs`.
- Kill the mismatched process with `node ../../.github/skills/bloom-exe-cdp-automation/killBloomProcess.mjs --only-mismatched`.
- Then start the current worktree.

### Start with the helper, not raw watch commands
- Start it from the current worktree.
- Prefer `node ../../.github/skills/bloom-exe-cdp-automation/bloomRun.mjs --watch` if you expect to iterate.
- Prefer `node ../../.github/skills/bloom-exe-cdp-automation/bloomRun.mjs` if you only need a single verification run.

### Reuse the running Bloom when the user asks for it
- Run `node ../../.github/skills/bloom-exe-cdp-automation/bloomProcessStatus.mjs --running-bloom --json`.
- Reuse the returned running Bloom instance even if it does not match the current worktree.
- Discover its CDP target with `node ../../.github/skills/bloom-exe-cdp-automation/webview2Targets.mjs --running-bloom --json --wait`.
- Do not kill or restart it unless the user explicitly asks for that.

### Prove browser-native access when needed
- Show the CDP target from `node ../../.github/skills/bloom-exe-cdp-automation/webview2Targets.mjs --json --wait`.
- Attach with Playwright.
- Demonstrate reading `body.className`, the top-bar iframe, console messages, and the `workspace/selectTab` request.

## Confirmed Path

- `playwright` Node library via `chromium.connectOverCDP("http://localhost:9222")`
- `@playwright/test` runner via the exe-backed suite in `src/BloomBrowserUI/react_components/component-tester`

Not confirmed here:
- `chrome-devtools-mcp` as an attached client to Bloom's existing WebView2 target
- the current Playwright MCP browser wrappers as an attached client to Bloom's existing WebView2 target

Reason: the current MCP wrappers in this environment control their own browser instance and do not expose a way to attach to an already-running external CDP endpoint. Until those tools add explicit attach support, prefer the script plus Playwright path above.

## Tests
- Run from `src/BloomBrowserUI/react_components/component-tester`.
- Use `yarn playwright test --config playwright.bloom-exe.config.ts`.
- Run one file with `yarn playwright test --config playwright.bloom-exe.config.ts ../TopBar/component-tests/bloom-exe-tabs.uitest.ts`.

These tests attach to the real Bloom.exe target over CDP and verify tab switching plus console and network observation.

## Notes
- Prefer the Node helpers over PowerShell. The Node scripts use `wmic`, `taskkill`, and `dotnet` directly because the PowerShell path proved too brittle.
- Prefer the checked-in Node helper commands over raw Windows shell commands. Subagents should normally run `node ../../.github/skills/bloom-exe-cdp-automation/bloomProcessStatus.mjs --json`, `node ../../.github/skills/bloom-exe-cdp-automation/killBloomProcess.mjs --only-mismatched`, `node ../../.github/skills/bloom-exe-cdp-automation/bloomRun.mjs`, and `node ../../.github/skills/bloom-exe-cdp-automation/webview2Targets.mjs --json --wait`, not ad hoc `wmic` commands.
- When reporting work, include the helper commands you used so reviewers can confirm the workflow stayed on the supported path.
- Wrong-worktree detection is authoritative when a real `Bloom.exe` child exists or when `dotnet watch` was started with an absolute `--project` path.
- A standalone `dotnet watch` started with a relative project path may not expose enough information to attribute it to a worktree. For current-worktree automation, start Bloom through `node ../../.github/skills/bloom-exe-cdp-automation/bloomRun.mjs --watch`, which always uses an absolute path. For the already-running Bloom workflow, use `--running-bloom` instead of trying to infer a worktree.

## Completion Checks
- Bloom's status is known: not running, running from current worktree, or running from different worktree.
- Any mismatched Bloom instance has been stopped before running the current worktree.
- The CDP endpoint responds at `http://localhost:9222/json/version`.
- `node ../../.github/skills/bloom-exe-cdp-automation/webview2Targets.mjs --json --wait` returns a real Bloom target.
- The automation client can read DOM state and interact with the embedded top bar.
- If tests were requested, the exe-backed Playwright suite passes.

## Output Contract
Report:
- whether Bloom was already running
- which repo root the running Bloom came from
- whether you killed a mismatched or stale process
- which command you used to start Bloom
- which client attached successfully
- what browser-native evidence you collected: DOM state, console output, network request, tab state, or test results

## Example Prompts
- `Use bloom-exe-cdp-automation to determine whether Bloom is already running from this worktree and attach Playwright to the embedded browser.`
- `Use bloom-exe-cdp-automation to kill the wrong-worktree Bloom and start the current checkout with dotnet watch.`
- `Use bloom-exe-cdp-automation to run the exe-backed Playwright top bar smoke tests against the actual Bloom.exe window.`
