# Papercuts

Small dev/agent/tooling friction points and improvement ideas — captured in the moment,
fixed later. This file holds cuts about **this repo** (its docs, scripts, build, tests, and
skills); cuts about the environment, machine setup, or team workflow go in bloom-team-skills'
`PAPERCUTS.md`. The full procedure is the `papercut` skill.

House rules:

- Add new entries at the **top**, directly under this header block.
- Entry format: `## YYYY-MM-DD — Title`, then `- **Cut:**` / `- **Idea:**` / optional
  `- **Context:**` lines. 2–5 lines total.
- Hit the same cut again? Add a dated `seen again: ...` line to the existing entry instead of
  duplicating it.
- On a merge conflict here, keep both sides' entries.
- Product bugs/features go to YouTrack instead.
- To work through the backlog, run the `papercut` skill in trim mode ("trim the papercuts").
  Fixed, promoted, or stale entries get **deleted** — the log only contains open cuts.

---

## 2026-07-24 — killBloomProcess.mjs --help kills Bloom instead of printing usage
- **Cut:** Running `node .github/skills/bloom-automation/killBloomProcess.mjs --help` to check
  its options killed the running Bloom (output: "Killed process IDs: 56212, 55352"). Unknown
  flags are ignored and the destructive default action runs, so the conventional "let me read
  the usage first" move is itself the dangerous one. The sibling scripts may have the same shape.
- **Idea:** Have these scripts recognize `--help`/`-h` (print usage, exit 0) and reject unknown
  flags instead of silently proceeding — especially the ones that kill processes.
- **Context:** BloomDesktop, during `/preflight` live verification of PR #8107.

## 2026-07-24 — agent-dotnet.sh collides with itself when a build and a test run overlap
- **Cut:** The wrapper isolates per *terminal*, not per *command*, so a `build` started while
  that same terminal's `test` is still running fails with MSB3027 — "Bloom.dll ... locked by:
  testhost". Agents that kick a full suite into the background and keep working hit this and
  can mistake it for a real build break.
- **Idea:** Either serialize (a lock file in `output/agent/<key>/`) or give a concurrent
  invocation its own subtree, and make the error message say "another agent-dotnet command is
  using this tree" instead of a raw MSBuild copy failure.
- **Context:** BloomDesktop, `/preflight` of PR #8107 (dev launcher control API).

## 2026-07-27 — The component-tester uitest suites still aren't in CI

- **Cut:** Nothing runs `react_components/component-tester`'s Playwright suites — `nightly.yml`
  runs vitest, C# and visual-regression only, and doesn't even `pnpm install` that workspace. That
  is why the whole harness sat broken (React 17 pin + a second-Playwright-copy config bug) without
  anyone noticing. It is now green at 144 passed / 25 skipped, so it will silently rot again.
- **Idea:** Add a nightly job mirroring the visual-regression one (`pnpm install --frozen-lockfile`
  in the component-tester dir, `pnpm exec playwright install chromium`, then
  `pnpm exec playwright test --config playwright.config.ts`) and publish its junit output. The
  bloom-exe config needs a running Bloom.exe, so only the component config is CI-able for now.
- **Context:** BloomDesktop, branch `fix-component-tester`. The component-tester README still
  says these are "not currently run as part of CI or other script" — update it if this lands.


## 2026-07-27 — Changing harness imports + `reuseExistingServer` = 20 bogus test failures

- **Cut:** Adding one bare import (`jquery`) to `component-harness.tsx` invalidated Vite's dep
  optimizer, but `playwright.config.ts` sets `reuseExistingServer: true`, so the already-running dev
  server served a half-rebuilt `node_modules/.vite/deps` chunk. Every BookGridSetup test (21 of
  them) failed with `styled_default is not a function` — which looks exactly like a real MUI/emotion
  regression and cost a chunk of debugging. Killing the server and deleting `node_modules/.vite`
  fixed it with no code change.
- **Idea:** Note this in the component-tester README/AGENTS ("if you add or remove an import in the
  harness, stop the dev server and `rm -rf node_modules/.vite` before trusting a red run"), or have
  the config/dev script clear the deps cache when `component-harness.tsx` is newer than it.
- **Context:** BloomDesktop, branch `fix-component-tester`.


## 2026-07-27 — Toolbox test harness has to duplicate toolboxBootstrap's tool registration

- **Cut:** `ToolboxRoot` only renders a section for a tool in `getMasterToolList()`, and tools land
  there as a side effect of importing `toolboxBootstrap.ts`. The harness can't import that module —
  it also does `$(document).ready(renderToolboxRoot)` and assigns `window.toolboxBundle`, which
  would double-render the root and clobber the stub some tests install. So
  `ToolboxRootTestHarness.tsx` now repeats the 11 `ToolBox.registerTool(...)` calls, with a
  "keep in sync" comment — i.e. a list that will drift.
- **Idea:** Extract the registrations into a side-effect-free `registerAllToolboxTools()` module
  that both `toolboxBootstrap.ts` and the harness import. Probably best folded into the toolbox
  React refactor (BL-16608 / PR #8109) rather than done separately.
- **Context:** BloomDesktop, branch `fix-component-tester`. Related: one test there is now
  `test.fixme` because it asserts on `.subscription-badge` (legacy-toolbox-only) and
  `.toolbox-react-header-icon` (never existed) via computed `background-image`; re-enabling it
  needs a decision on whether the React header renders badges/icons and what classes to expose.


## 2026-07-27 — C# test host aborts mid-run when the suite runs alongside vitest

- **Cut:** Two `build/agent-dotnet.sh test` runs started while `pnpm test` was also running
  aborted after ~347 of 2946 tests with `Test host process crashed: Unhandled exception.
  System.ObjectDisposedException: Cannot access a disposed object. Object name:
  'System.Net.HttpRequestQueueV2Handle'` inside `HttpListener` response teardown. Worse, the
  run still printed `Passed! - Failed: 0, Passed: 347` before `Test Run Aborted`, so a partial
  run reads as a pass unless you notice the total. Run alone, the same command completed all
  2946. Since the whole point of the agent wrapper is that several things can run at once,
  this quietly removes that benefit for the C# suite.
- **Idea:** Find why Bloom's test HTTP listener is disposed while a response is in flight
  (likely a shared/fixed port colliding across processes, so pick a free port per run), and
  make the wrapper fail loudly on `Test Run Aborted` rather than emitting a `Passed!` line.
- **Context:** BloomDesktop, found during `/preflight` of PR #8112 (BL-16602). Related to the
  2026-07-24 entry (wrapper reports failure as success) — same "trust the summary line, not
  the exit code" hazard, different cause.


## 2026-07-24 — agent-dotnet.sh test exits 0 even when tests fail

- **Cut:** `build/agent-dotnet.sh test src/BloomTests/BloomTests.csproj` returned exit code 0 on a
  run whose own summary line read `Failed! - Failed: 11, Passed: 2913`. Anything that trusts the
  exit code instead of parsing the output — an agent, a hook, a CI step, a background-task
  notification that just reports "completed (exit code 0)" — will conclude the suite passed. During
  this run the harness reported the failing suite as a success and it was only caught by reading the
  summary line.
- **Idea:** Have the wrapper propagate `dotnet test`'s real exit code (it presumably exits on the
  last command in a pipeline, or swallows the status while redirecting into the private output
  tree). Until then, treat "did the C# suite pass?" as a question only the output can answer.
- **Context:** BloomDesktop, found during `/preflight` of PR #7992 (BL-16459 clipboard toast).
  Distinct from the (now-fixed) cut about *which* tests fail under the wrapper — PR #8107 made
  the full suite green, so a failure there is real; this entry is about the wrapper reporting
  failure as success, which still stands.

## 2026-07-24 — go.sh "succeeds" on a fresh worktree whose output/browser was never built
- **Cut:** On a never-initialized worktree, after fixing the obvious failures (pnpm install,
  getDependencies for CS0246), `./go.sh` launches and Bloom looks healthy — but opening a book
  fails with "Cannot Find File: bookPreviewBundle.js", because a few entry points are served
  from `output/browser` (populated only by init.sh's one-shot `pnpm build`), not by the Vite
  dev server. Neither go.sh nor the run-bloom skill warns about this half-initialized state.
- **Idea:** Have go.mjs (or the run-bloom skill's preflight) check for a marker like
  `output/browser/bookPreviewBundle.js` and say "run ./init.sh first" instead of launching
  into a Bloom that fails later.
- **Context:** BuildServer worktree, while verifying the new launcher control surface.


## 2026-07-13 — pnpm-lock.yaml reformats wholesale on any install (format drift)
- **Cut:** The committed `src/BloomBrowserUI/pnpm-lock.yaml` (on master too) is in an
  older pnpm serialization style (double-quoted `lockfileVersion`, 4-space indent, and it
  resolves some deps with a `(supports-color@5.5.0)` peer suffix). But the pinned + active
  pnpm (11.5.2, per `packageManager`) writes a *different* style (single-quoted, 2-space,
  no supports-color suffix). So **any** `pnpm install` rewrites the entire lockfile,
  producing a spurious ~30k-line diff that has nothing to do with your actual change. To
  bump a single `github:` dependency's commit hash I had to hand-patch the lock (swap the
  4 hash occurrences + the integrity line) to keep the diff minimal and mergeable.
- **Idea:** Regenerate/commit the lockfile once with the pinned pnpm so committed state
  matches `packageManager` output, or document the exact pnpm invocation the team uses so
  installs are format-stable. Until then, hash bumps need a manual lock edit.
- **Context:** BL image-chooser integration PR (BloomDesktop #8059); local pnpm 11.5.2.

## 2026-07-11 — Can't screenshot Bloom's WinForms modal dialogs via CDP
- **Cut:** Verifying a `WireUpForWinforms` modal (e.g. `CollectionChooserDialog`) is painful. The modal opens in a separate WebView2 that is NOT exposed on the main CDP endpoint (`/json/list` shows only the main workspace page), so `screenshotBloom.mjs` can't capture it. Vite React Fast Refresh also closes any open modal on HMR. I fell back to OS-level screen capture, which needed the `AttachThreadInput` foregrounding trick because the ORCA host window covers the screen and `SetForegroundWindow` from a background process is blocked.
- **Idea:** Have the `run-bloom` / `bloom-automation` skill document a supported way to screenshot modal dialogs — e.g. expose the dialog's WebView2 on a discoverable CDP port, or ship a helper that does the force-foreground + region capture.
- **Context:** ImproveVisuals branch, restyling the Open/Create Collections dialog to the 2A design.
