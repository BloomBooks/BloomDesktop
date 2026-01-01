---
name: component-scope
description: Launch / Show / look at a component in a shared, remote-debugging-enabled browser and collaborate in the same tab. We call this the "scope".
metadata:
  short-description: Show a component in a browser tab ready for using by both the dev and the agent.
---

When the user says anything like:
- "scope this"
- “show the component”
- “launch the component”
- “open the component”
- "scope the component"
- "look at the scope"

Do NOT diagnose, investigate, explain, or propose options.

Do exactly this, in order:
0) Preflight: if `scope-mcp` is available, call `list_pages`.
  - If you see a tab whose URL looks like the Vite harness (e.g. `http://127.0.0.1:####/?modulePath=...`), the scope is already running.
  - Select that tab and do ONE minimal sanity check pass (see below), then STOP.

1) If you know which component they mean (because it was the last one discussed in this chat), run its component-local `scope.sh`.
2) Else, if you are currently working inside a specific component folder and it contains `scope.sh`, run that.
3) Else ask exactly ONE question: “Which component folder (must be under `src/BloomBrowserUI/react_components/`)?”

Then do NOT open any more browsers. The scope script already opens a Chromium tab that tools can attach to via remote debugging.

Immediately after running `scope.sh`, do exactly ONE “sanity check” pass and then STOP:
1) Verify the launch succeeded by confirming the script printed something like:
  - “opened a scope on port …”
2) Verify scope mcp can see the page (minimal chrome-devtools-9222 check):
  - call `list_pages` on the scope-mcp
  - confirm there is a tab whose URL looks like the Vite harness (e.g. `http://127.0.0.1:####/?modulePath=...`)
3) Select the harness tab and take ONE snapshot to confirm it is actually reachable.
   - If the snapshot shows an unreachable/connection-refused page, re-run `scope.sh` once (it starts/reuses Vite) and then re-run steps (2) and (3), then STOP.
4) If the MCP tools show `about:blank`, then you opened two browsers instead of one. Close the "about:blank" one.

Only after the user asks to interact with the component (click/type/inspect), you may do additional MCP calls beyond the sanity check.

Never call `open_simple_browser` as part of this workflow. Never open any browsers. `scope.sh` does that.

Hard rules (to prevent over-thinking):
- Do NOT run extra terminal diagnostics after `scope.sh` (no `python`, no `curl` to `/json/list`, no port-scanning, no grepping logs).
- Do NOT try alternative launch methods unless the user explicitly asks.
- Do NOT attempt to “fix” the environment; just run the script, do the minimal MCP sanity check, then stop.


Example:

`cd src/BloomBrowserUI/react_components/color-picking && ./scope.sh`

This script:
- starts/reuses the Vite dev server if needed
- launches Chrome (or Edge) with remote debugging enabled and a dedicated profile dir
- prints the debug port for your use

If the "scope-mcp" isn't running, tell the user to use this config in mcp.json and make sure it is started and made available to agents:
        ```
        "scope-mcp": {
            "command": "npx",
            "args": [
                "-y",
                "chrome-devtools-mcp@latest",
                "--browser-url=http://127.0.0.1:9223"
            ]
        }
        ```
