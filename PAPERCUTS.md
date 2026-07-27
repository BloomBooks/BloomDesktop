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


## 2026-07-27 — ToolboxRoot's Playwright component tests have been dead since React 18

- **Cut:** All 7 tests in `react_components/ToolboxRootTestHarness/component-tests/
  toolbox-root-react.uitest.ts` fail on master before rendering anything: the component-tester
  workspace pins `react-dom` 17.0.2, so Vite can't resolve `react-dom/client`, which
  `utils/reactRender.tsx` has imported since the app moved to React 18. Nobody noticed because
  these tests aren't in CI. That harness was the natural home for a regression test of the
  toolbox's activation bridge (BL-16602); the test had to go into a vitest spec instead.
- **Idea:** Bump `react`/`react-dom`/`@types/*` in
  `src/BloomBrowserUI/react_components/component-tester/package.json` to 18 to match the app,
  re-verify the 7 tests, and consider whether these should run in CI — a harness nothing runs
  will rot again.
- **Context:** BloomDesktop, found during `/preflight` of PR #8112 (BL-16602). Note the
  component-tester README already says these aren't run by CI or any script.


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
  Related to the 2026-07-15 entry below, but distinct: that one is about *which* tests fail under
  the wrapper, this one is about the wrapper reporting failure as success.


## 2026-07-15 — agent-dotnet full suite can never be fully green (9 environmental failures)

- **Cut:** Running the full C# suite through `build/agent-dotnet.sh` always fails 9 tests for
  environment reasons: the per-terminal agent output tree lacks `BloomPdfMaker.exe` (4
  `PdfMakerTests` failures) and `XMatterHelper` can't locate the checked-in
  `src/BloomTests/xMatter/Test-XMatter` packs from that tree (5 failures in
  `XMatterHelperTests` and `InsertPageAfter_FromDifferentBook_MergesStyles`). Agents can't get
  a green full-suite baseline, so every preflight has to re-establish that these 9 are noise.
- seen again: 2026-07-27, preflight of moreHighlightFixes2 (PR #8100) — same 9, re-triaged as
  environmental; developer decided "leave as is" for that run.
- seen again: 2026-07-27, preflight of BL-16602 (PR #8112) — same 9, identical across two
  complete runs, re-triaged from scratch a third time in one day. Developer chose "log it as a
  papercut so the wrapper gets fixed", so this is now waiting on a fix rather than another
  re-triage.
- seen again: 2026-07-27, preflight of game-theme-editor (PR #8086) — **the count is no longer 9,
  it is 19**, so "the known 9" is now a misleading baseline that costs time rather than saving it.
  The extra 10 are the same class of problem (the private output tree lacks files the fixtures
  expect): 8 × `CompressBookForDevice_*` and 2 × `AddAudioOverlay_NoSubElementPlaybackModes_*`,
  failing with `DirectoryNotFoundException`, `ArgumentException: The value cannot be an empty
  string (Parameter 'path')`, `ArgumentException: Drive name must be a root directory`, and one
  stylesheet-hash mismatch. Enumerating them needed `--logger "trx;LogFileName=cs.trx"` and
  parsing `src/BloomTests/TestResults/cs.trx`, because the console summary only prints the count
  and piping the run through `tail` discards the per-test lines. **Tip for the next agent:** use
  the trx logger from the start, and treat the expected-failure set as "PdfMaker + XMatter +
  BloomPub/publish path fixtures", not a fixed number.
- **Idea:** Make the wrapper copy/link `BloomPdfMaker.exe` into its private output tree and fix
  the xmatter file-locator path (or document the known failures in AGENTS.md as expected under
  the wrapper). Whatever the fix, record the expected set *by test-class pattern* rather than by
  count, since the count grows as master adds tests in the same areas.
- **Context:** BloomDesktop, found during `/preflight` of PR #8067 (speedUpCSharpTests).


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
- **Correction + much cheaper workaround (2026-07-27, preflight of PR #8086):** the committed
  style is not an "older pnpm serialization" — it is simply **prettier's** output. `pnpm-lock.yaml`
  is not in `.prettierignore`, so prettier owns the file, and running
  `node_modules/.bin/prettier --write pnpm-lock.yaml` right after any `pnpm install` restores the
  committed style exactly (verified: `--check` then passes, and the diff drops from ~30,400 lines
  to only the lines the install actually changed). So there is **no need to hand-patch hash
  occurrences** — install normally, then run prettier on the lock. This branch had arrived with an
  un-prettified lock, which is what made its diff 30,400 lines; one prettier run reduced it to 58.
- **Better idea, given the above:** add `pnpm-lock.yaml` to the pre-commit prettier/lint-staged
  step so the reformat can never be forgotten, or add it to `.prettierignore` and accept pnpm's
  own style. Either removes the drift permanently; the current state (prettier owns it, pnpm
  rewrites it, nothing enforces re-running prettier) is the worst of both.
- **Context:** BL image-chooser integration PR (BloomDesktop #8059); local pnpm 11.5.2.

## 2026-07-11 — Can't screenshot Bloom's WinForms modal dialogs via CDP
- **Cut:** Verifying a `WireUpForWinforms` modal (e.g. `CollectionChooserDialog`) is painful. The modal opens in a separate WebView2 that is NOT exposed on the main CDP endpoint (`/json/list` shows only the main workspace page), so `screenshotBloom.mjs` can't capture it. Vite React Fast Refresh also closes any open modal on HMR. I fell back to OS-level screen capture, which needed the `AttachThreadInput` foregrounding trick because the ORCA host window covers the screen and `SetForegroundWindow` from a background process is blocked.
- **Idea:** Have the `run-bloom` / `bloom-automation` skill document a supported way to screenshot modal dialogs — e.g. expose the dialog's WebView2 on a discoverable CDP port, or ship a helper that does the force-foreground + region capture.
- **Context:** ImproveVisuals branch, restyling the Open/Create Collections dialog to the 2A design.
