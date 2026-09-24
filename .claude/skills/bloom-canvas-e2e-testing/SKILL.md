---
name: bloom-canvas-e2e-testing
description: Build and run automated Playwright end-to-end tests for Canvas Tool behavior (drag from the toolbox onto the page, canvas element interactions) against a running Bloom's Edit tab at CURRENTPAGE. Use when asked for canvas e2e tests, to reproduce a canvas drag/drop regression with a test, or to run the canvas suite.
---

# Canvas e2e tests

The suite, its helpers, the frame model, the selectors, the run commands and the stability rules
all live with the tests: read and follow
`src/BloomBrowserUI/bookEdit/canvas-e2e-tests/AGENTS.md`. This skill only says when to reach for
it and repeats the one rule that can hang a run.

**Use it for** automated Playwright tests of Canvas Tool behavior, verified with real mouse
gestures. **Not for** component-harness tests under `react_components/*/*.uitest.ts`
(`component-test` skill).

**Before you start:** Bloom is running and serving the Edit tab, the current page has a
`.bloom-canvas`, and the Canvas tool is in the toolbox. Take the port from
`launcherControl.mjs --status` (see `run-bloom`); 8089 is first-come across worktrees.

**Never let a native OS dialog open unprepared** (file picker, video capture, "Record
yourself..."): Playwright cannot see it and the run hangs. The AGENTS.md above lists which
commands are safe and which reach a picker.
