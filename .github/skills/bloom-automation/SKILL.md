---
name: bloom-automation
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
- The helper launch script chooses an explicit port block by default instead of relying on Bloom's standard search. It allocates HTTP bases from `18089`, `18099`, `18109`, ... and reserves `http`, `http+1`, and `http+2`, with CDP on `http+3`.
- Running Bloom reports its actual HTTP and CDP ports through `http://localhost:<port>/bloom/api/common/instanceInfo`.
- Bloom's embedded WebView2 still defaults to CDP port `9222` in debug builds when no explicit `--cdp-port` argument is supplied.

## Commands

Examples below assume you are somewhere inside the repository and first compute the repo root once:

```bash
repo_root="$(git rev-parse --show-toplevel)"
```

Terminal:
- In this VS Code workspace, the shared bash terminal keeps whatever cwd the previous command left behind.
- Prefer running the helper through `$repo_root/.github/skills/...` so the command does not depend on the current working directory.

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
node "$repo_root/.github/skills/bloom-automation/bloomProcessStatus.mjs" --http-port 18089 --json
```

Reports Bloom.exe processes, detected repo roots, attributable `dotnet watch` parents, ambiguous watchers, and whether the workspace API and CDP endpoint are reachable.

Use `--running-bloom` when the user explicitly wants the already-running Bloom instead of a worktree-owned instance. This scans Bloom's standard HTTP port range, asks any running Bloom for `common/instanceInfo`, and reports the ports that instance says it is using.
Use `--http-port <port>` when you launched Bloom through `bloomRun.mjs` and want the exact instance that owns that HTTP port. This is the preferred multi-instance workflow because it gives you the precise Bloom PID and CDP port even when several Blooms from the same worktree are running.
### Kill Bloom

```bash
node "$repo_root/.github/skills/bloom-automation/killBloomProcess.mjs"
node "$repo_root/.github/skills/bloom-automation/killBloomProcess.mjs" --only-mismatched
node "$repo_root/.github/skills/bloom-automation/killBloomProcess.mjs" --http-port 18089
node "$repo_root/.github/skills/bloom-automation/killBloomProcess.mjs" --pid 12345 --watch-pid 12340
```

Use the plain form to stop all detected Bloom-related processes. Use `--only-mismatched` to stop only the Bloom instance that does not belong to the current worktree.
Use `--http-port <port>` to stop the exact Bloom instance bound to that HTTP port, together with any `dotnet` parent in its process chain. Use `--pid` or `--watch-pid` only when you already know the exact process IDs you want to stop.

Important: if Bloom was started with `dotnet watch run`, killing only `Bloom.exe` is not enough because the watcher will restart it. Prefer the provided kill script so the watcher and child process are both terminated.

### Start Bloom

```bash
node "$repo_root/.github/skills/bloom-automation/bloomRun.mjs"
node "$repo_root/.github/skills/bloom-automation/bloomRun.mjs" --watch
node "$repo_root/.github/skills/bloom-automation/bloomRun.mjs" --http-port 18089 --cdp-port 18092
```

Use `--watch` when you expect to iterate. These helpers compute the repo root, acquire a lock on a non-overlapping port block, pass explicit `--http-port` and `--cdp-port` arguments to Bloom, and print the `dotnet` PID immediately plus the Bloom PID once `common/instanceInfo` becomes reachable.
`bloomRun.mjs` is intentionally long-lived: for normal launches it keeps running until the launched Bloom instance exits, even if `dotnet run` returns earlier after spawning `Bloom.exe`. If Bloom reports ready and then dies shortly afterward, the helper now reports that as a failed launch instead of silently succeeding.

Agent workflow for `bloomRun.mjs`:
- Start it in a background terminal.
- Do not wait for the command to finish. A successful launch is the `Bloom ready. HTTP ..., websocket ..., CDP ..., Bloom PID ...` line, not process exit.
- If the helper later reports that the Bloom PID exited shortly after reporting ready, treat that as a failed launch and do not target that HTTP port.
- After starting it, read or poll that background terminal's output until the `Bloom ready.` line appears, then use the reported HTTP port as the identity of the new instance.
- After you have the HTTP port, continue with `bloomProcessStatus.mjs --http-port <port> --json`, `webview2Targets.mjs --http-port <port> --json --wait`, or `switchWorkspaceTab.mjs --http-port <port> --tab ...`.
- Keep the background terminal open for the lifetime of that Bloom instance. The helper may outlive the `dotnet` process because it continues tracking the actual Bloom PID and holding the port lease.

### Discover the CDP target

```bash
node "$repo_root/.github/skills/bloom-automation/webview2Targets.mjs"
node "$repo_root/.github/skills/bloom-automation/webview2Targets.mjs" --json --wait
node "$repo_root/.github/skills/bloom-automation/webview2Targets.mjs" --running-bloom --json --wait
node "$repo_root/.github/skills/bloom-automation/webview2Targets.mjs" --http-port 18089 --json --wait
```

Use `--wait` after startup so the command blocks until the embedded browser target is available.

### Switch a workspace tab

```bash
node "$repo_root/.github/skills/bloom-automation/switchWorkspaceTab.mjs" --running-bloom --tab edit --json
node "$repo_root/.github/skills/bloom-automation/switchWorkspaceTab.mjs" --http-port 18089 --tab publish --json
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
2. If you need a fresh automation-owned instance, start it with `node .github/skills/bloom-automation/bloomRun.mjs --watch` or `node .github/skills/bloom-automation/bloomRun.mjs`.
3. Copy the printed HTTP and CDP ports. If you need the exact PID later, run `node .github/skills/bloom-automation/bloomProcessStatus.mjs --http-port <httpPort> --json`.
4. If you instead want to reuse a current-worktree instance that Bloom found by itself, only then use repo-root matching and `--only-mismatched` cleanup.
5. Run `node .github/skills/bloom-automation/webview2Targets.mjs --http-port <httpPort> --json --wait` to discover the live WebView2 target for that exact instance when you need debugging detail.
6. Use `node .github/skills/bloom-automation/switchWorkspaceTab.mjs --http-port <httpPort> --tab <collection|edit|publish>` for top bar interactions, or attach another confirmed client to the reported `cdpOrigin` if you need lower-level inspection.
7. Manipulate the UI by clicking or typing in the attached browser context. Do not use Bloom API endpoints to simulate the user action itself.
8. Use browser-native inspection for DOM, console, and network.
9. If the task is test-related, run the exe-backed Playwright suite with `BLOOM_HTTP_PORT=<httpPort> BLOOM_CDP_PORT=<cdpPort> yarn playwright test --config playwright.bloom-exe.config.ts`.

## Running Bloom Workflow
Use this when the user says to reuse the already-running Bloom.

1. Run `node .github/skills/bloom-automation/bloomProcessStatus.mjs --running-bloom --json`.
2. If no running Bloom instance is reported, tell the user there is no running Bloom to reuse.
3. If one is reported, do not kill or restart it because of worktree mismatch.
4. Use `node .github/skills/bloom-automation/switchWorkspaceTab.mjs --running-bloom --tab <collection|edit|publish>` for top bar actions, or `node .github/skills/bloom-automation/webview2Targets.mjs --running-bloom --json --wait` when you need CDP target detail.
5. Attach to the reported instance and work only against the ports it reported about itself.

## Rules

### Reuse the current worktree instance
- Reuse it.
- Attach over CDP and drive the UI directly.
- Do not restart unless the user explicitly wants a fresh run or you need to load new code.

### Treat wrong-worktree Bloom as a blocker
- Treat that as a blocker because it produces extremely confusing results.
- Report the detected repo root from `node .github/skills/bloom-automation/bloomProcessStatus.mjs`.
- Kill the mismatched process with `node .github/skills/bloom-automation/killBloomProcess.mjs --only-mismatched`.
- Then start the current worktree.

### Start with the helper, not raw watch commands
- Start it from the current worktree.
- Prefer `node .github/skills/bloom-automation/bloomRun.mjs --watch` if you expect to iterate.
- Prefer `node .github/skills/bloom-automation/bloomRun.mjs` if you only need a single verification run.
- Treat the printed HTTP port as the identity of that instance. Use `bloomProcessStatus.mjs --http-port <port>`, `webview2Targets.mjs --http-port <port>`, and `killBloomProcess.mjs --http-port <port>` to target it precisely.
- Never wait for `bloomRun.mjs` to exit as a readiness signal. It is a long-lived launcher. Wait for the `Bloom ready.` line in the background terminal output instead, and treat a later `Bloom PID ... exited shortly after reporting ready` message as a failed launch.

### Reuse the running Bloom when the user asks for it
- Run `node .github/skills/bloom-automation/bloomProcessStatus.mjs --running-bloom --json`.
- Reuse the returned running Bloom instance even if it does not match the current worktree.
- Use `node .github/skills/bloom-automation/switchWorkspaceTab.mjs --running-bloom --tab <collection|edit|publish>` for direct top bar interaction, or discover its CDP target with `node .github/skills/bloom-automation/webview2Targets.mjs --running-bloom --json --wait` when you need the raw target details.
- Do not kill or restart it unless the user explicitly asks for that.

### Prove browser-native access when needed
- Show the CDP target from `node .github/skills/bloom-automation/webview2Targets.mjs --json --wait`.
- Attach with Playwright.
- Demonstrate reading `body.className`, the top-bar iframe, console messages, and the `workspace/selectTab` request.
- For multi-instance work, prefer `webview2Targets.mjs --http-port <port> --json --wait` and the matching `cdpOrigin` it reports.

### Verified two-instance smoke path
- Launch one instance on `--http-port 18089 --cdp-port 18092`.
- Launch a second instance on `--http-port 18099 --cdp-port 18102`.
- Target the first instance with `switchWorkspaceTab.mjs --http-port 18089 --tab edit`.
- Target the second instance with `switchWorkspaceTab.mjs --http-port 18099 --tab publish`.
- Use explicit ports throughout; do not mix `--running-bloom` with this workflow.

## Confirmed Path

- `playwright` Node library via the `cdpOrigin` reported by `common/instanceInfo` or `webview2Targets.mjs`
- `@playwright/test` runner via the exe-backed suite in `src/BloomBrowserUI/react_components/component-tester`

Not confirmed here:
- `chrome-devtools-mcp` as an attached client to Bloom's existing WebView2 target
- the current Playwright MCP browser wrappers as an attached client to Bloom's existing WebView2 target

Reason: the current MCP wrappers in this environment control their own browser instance and do not expose a way to attach to an already-running external CDP endpoint. Until those tools add explicit attach support, prefer the script plus Playwright path above.

## Tests
- Run from `src/BloomBrowserUI/react_components/component-tester`.
- Use `BLOOM_HTTP_PORT=<httpPort> BLOOM_CDP_PORT=<cdpPort> yarn playwright test --config playwright.bloom-exe.config.ts`.
- Run one file with `BLOOM_HTTP_PORT=<httpPort> BLOOM_CDP_PORT=<cdpPort> yarn playwright test --config playwright.bloom-exe.config.ts ../TopBar/component-tests/bloom-exe-tabs.uitest.ts`.

These tests attach to the real Bloom.exe target over CDP and verify tab switching plus console and network observation.

## Notes
- Prefer the Node helpers over PowerShell. The Node scripts use `wmic`, `taskkill`, and `dotnet` directly because the PowerShell path proved too brittle.
- Prefer the checked-in Node helper commands over raw Windows shell commands. Subagents should normally run `node .github/skills/bloom-automation/bloomProcessStatus.mjs --json`, `node .github/skills/bloom-automation/killBloomProcess.mjs --only-mismatched`, `node .github/skills/bloom-automation/bloomRun.mjs`, `node .github/skills/bloom-automation/webview2Targets.mjs --json --wait`, and `node .github/skills/bloom-automation/switchWorkspaceTab.mjs --running-bloom --tab edit`, not ad hoc `wmic` commands.
- `bloomRun.mjs` keeps a lightweight lease file for the chosen HTTP block while it is running so concurrent agents do not both choose the same automation ports.
- For agent-driven launches, the background terminal is part of the control plane. Leave it running and poll its output for `Bloom ready.` instead of waiting for command completion. The helper may keep running after `dotnet` exits because it is tracking the actual Bloom process.
- Exact-target cleanup is intentionally strict: `killBloomProcess.mjs --http-port <port>` should only kill the instance that actually reports that HTTP port, and should fail without killing anything if that target cannot be resolved.
- When reporting work, include the helper commands you used so reviewers can confirm the workflow stayed on the supported path.
- Wrong-worktree detection is authoritative when a real `Bloom.exe` child exists or when `dotnet watch` was started with an absolute `--project` path.
- A standalone `dotnet watch` started with a relative project path may not expose enough information to attribute it to a worktree. For current-worktree automation, start Bloom through `node .github/skills/bloom-automation/bloomRun.mjs --watch`, which always uses an absolute path. For the already-running Bloom workflow, use `--running-bloom` instead of trying to infer a worktree.
- When more than one Bloom is running from the same worktree, repo-root matching is not enough. Use the explicit HTTP port workflow.

## Completion Checks
- Bloom's status is known: not running, running from current worktree, or running from different worktree.
- Any mismatched Bloom instance has been stopped before running the current worktree, unless you intentionally started a separate explicit-port instance.
- The chosen HTTP port returns `common/instanceInfo`, including the exact Bloom PID and CDP port.
- The reported CDP endpoint responds at `<cdpOrigin>/json/version`.
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
- `Use bloom-automation to determine whether Bloom is already running from this worktree and attach Playwright to the embedded browser.`
- `Use bloom-automation to switch the already-running Bloom to the Edit tab.`
- `Use bloom-automation to kill the wrong-worktree Bloom and start the current checkout with dotnet watch.`
- `Use bloom-automation to run the exe-backed Playwright top bar smoke tests against the actual Bloom.exe window.`
