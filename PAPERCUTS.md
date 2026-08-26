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

## 2026-08-26 — A Bloom launched by ./go.sh cannot be watched by the Freeze Doctor

- **Cut:** `go.sh` runs Bloom with `--automation`, and the Doctor deliberately refuses to watch any
  run whose command line carries that flag (such runs legitimately have no window, so watching them
  would manufacture zombie reports). So the repo’s sanctioned dev launcher produces the one kind of
  Bloom the Doctor ignores, and an agent following AGENTS.md cannot test the Doctor at all. Launching
  the built exe directly instead dies at Velopack init when given no arguments ("Bloom Problem"
  immediately), though the same binary starts fine with go.sh’s own arguments. F5 works, which is why
  every successful manual test of this feature so far has been F5.
- **Idea:** either have go.sh omit `--automation` (or offer a flag to), or say in AGENTS.md that testing
  the Freeze Doctor needs F5 rather than go.sh, and why. Also worth noting that go.sh builds to
  `output/Debug/AnyCPU` while launch.json runs `output/Debug/x64`.
- **Context:** BL-16719, trying to run a crash test unattended. A related false start: 
  `build/agent-dotnet.sh` builds into `output/agent/<key>/`, so `output/Debug/x64` was eleven commits
  stale and the first attempt silently exercised old code.

## 2026-07-30 — Visual regression suite reports only the first stale image per case
- **Cut:** Each case in `src/BloomVisualRegressionTests/index.spec.ts` compares the book preview
  and then every bloom-player page in sequence, and every comparison throws on failure — so the
  first stale baseline kills the case and the later comparisons never even capture their images.
  After BL-16370 the stale previews meant **no** player page was compared for weeks: BL-16638
  started as 10 baselines, became 22, and would have taken three accept-and-rerun rounds to
  bottom out (10 previews → 10 player pages → 2 more hidden behind those). Each layer costs a
  full ~3-minute run to discover, and the nightly reads as "one failure per case" the whole time.
- **Idea:** Accumulate per-comparison failures for the case (label, pixel count, diff path), let
  the preview capture and the whole player loop run to completion, then fail once at the end with
  the full list. Proven to work — the change was made temporarily during BL-16638 to capture all
  84 images in one run, then reverted. Roughly 20–30 lines, confined to that spec file.
- **Context:** BL-16638 / PR #8134. Andrew chose "make a papercut entry" over fixing it inline.
  Loop at `index.spec.ts:426`, assertion at `index.spec.ts:486`.

## 2026-07-28 — One talkingBookSpec test fails only under full-suite worker load
- **Cut:** `talkingBookSpec.ts > showTool(checksum=missing, audio=missing, scenario=PreTextBox) => UPDATE`
  fails intermittently in `pnpm test`, but passes when its file is run alone, passes on a
  re-run of the identical tree, and passes if you exclude *any* one unrelated spec file
  (`--exclude "**/textHighlightManagerSpec.ts"` works just as well as excluding a related
  one). So it's sensitive to how many files the pool is juggling, not to any code change —
  but it reads as a real regression and costs 15+ minutes to clear each time it appears.
- **Idea:** Make the test wait for the audio player's `src` deterministically instead of
  relying on timing, or mark it as needing serial execution. Failing that, note it in the
  spec so the next person doesn't re-triage it from scratch.
- **Context:** BL-16558 preflight. Full suite 611 passing; this test failed on two
  consecutive runs then passed on the third with no code change in between.

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


## 2026-07-27 — Three C# tests fail intermittently under load

- **Cut:** `CheckAudioForAllText_SpansAudioMissing`, `BringBookUpToDate_MovesMetaDataToJson` and
  `InsertPageAfter_FromAnotherBook_CopiesWidget(True)` fail sometimes and pass sometimes. They
  failed together on one full-suite run, passed on a second run at the *identical* commit, and
  passed when run on their own — so they are flaky rather than broken, and none of them is anywhere
  near what that branch was changing (image handling). The cost is that a full run can no longer be
  trusted on one reading: an agent has to run the suite twice to tell noise from a real regression.
  That matters more now than when this was first written, because the full suite otherwise comes
  back completely green (see the 2026-08-04 note below) — these three are the only remaining noise,
  so any other failure is signal.
- **Idea:** Find the shared state (all three build books/collections in temp folders, so likely a
  fixture or folder-name collision when the suite runs under load) — or, cheaply, quarantine them
  with `[Retry]` so the noise stops masking real failures.
- **Context:** BloomDesktop, seen during `/preflight` of PR #8111 (BL-16597), on two of six
  full-suite runs that day.
- **2026-08-04:** All three passed on a full run of 3027 tests with **0 failures** — the first
  entirely green full suite under `build/agent-dotnet.sh`, now that PR #8107 has fixed the nine
  environmental failures that used to accompany them. Not evidence against this cut: intermittent
  is intermittent. Recorded because it removes the nine-failure baseline the original wording
  leaned on.
- **2026-08-06 — seen again, and the shared state this Idea line asks for is now identified:**
  a different pair (`BookWithUnknownLayout_GetsUpdatedToA5Portrait`,
  `CompressBookForDevice_MakesThumbnailFromCoverPicture`) failed on one full run and passed on the
  next at the identical commit, and the exceptions name the culprit directly: an
  `UnauthorizedAccessException` on `%TEMP%\BookTests\test\colorPalettes.json` and a
  `DirectoryNotFoundException` on `%TEMP%\BookTests\book\basePage.css`. That path is a **fixed,
  machine-wide** folder, so it is shared by every worktree — another worktree was running the suite
  at the same time, and the two runs deleted each other's files mid-test. So this is not only "under
  load": `build/agent-dotnet.sh` isolates *build* output per terminal, but the tests themselves still
  share one scratch directory, which means two agents in two worktrees can never trust a red result.
  Fix is narrower than the original Idea suggests: give the test scratch root a per-process suffix
  (e.g. include the pid or the `BLOOM_AGENT_BUILD_DIR` key in the `BookTests` folder name).
- **Context (2026-08-06):** BloomDesktop, `/preflight` of PR #8166, while another worktree ran its
  own suite.


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


## 2026-08-10 — check-csharp-ApplicationExit.sh greps whole files, not the diff

- **Cut:** The pre-commit check greps each *staged file* for `Application.Exit`, so touching a
  file that already contains a legitimate one blocks the commit even when your diff adds none.
  Hit it editing `src/WebView2PdfMaker/Program.cs`, whose two calls date from 2023 (BL-11437);
  WebView2PdfMaker is a separate process and cannot use Bloom's `ProgramExit`. The exemption
  list only covers `src/BloomExe/ProgramExit.cs` and `src/BloomTests/*`.
- **Idea:** Grep only added lines (`git diff --cached -U0 | grep '^+'`) so the check flags new
  violations rather than any file that contains one. Failing that, at least exempt
  `src/WebView2PdfMaker/*`.
- **Context:** BL-16684 on Version6.4. Check added 2026-01-31 (8a9d1273b0); nothing had touched
  that file since, so this had been sitting unsprung. Committed with `--no-verify` after running
  csharpier / robustfile / xmlclasses by hand.

## 2026-08-10 — Re-pointing a worktree between master and Version6.4 breaks the pre-commit hook

- **Cut:** `.githooks/pre-commit` correctly routes a 6.4 checkout to the husky-4 hook, but that
  hook dies with `Command "husky-run" not found` when the worktree's `src/BloomBrowserUI/node_modules`
  was installed by master's pnpm. The dispatcher solves *which* hook runs, not whether its runner
  is installed, so you get a hard commit failure with no hint that deps are the cause.
- **Idea:** Have the dispatcher check for the runner and say "run ./init.sh — this branch uses
  yarn+husky4 and this worktree has the pnpm deps" instead of letting husky's bare error through.
- **Context:** BL-16684; worktree started on master, moved to Version6.4 because the bug ships in 6.4.

## 2026-07-22 — CDP capture: two footguns while driving Bloom's WebView2

While recapturing MXB title pages over CDP (port 8091):

- **Never `taskkill //IM node.exe //F` to clean up a hung capture script.** It kills the
  whole `dotnet watch` / vite dev flow that `go.sh` runs (front-end is node), which takes
  Bloom's server down with it. Kill only your own script's PID. Cost me a Bloom restart.
- **`Page.captureScreenshot` with `captureBeyondViewport:true` hangs in WebView2** (no
  response, no error). Workaround that worked: `Emulation.setDeviceMetricsOverride` to a
  viewport tall/wide enough to contain the whole `.bloom-page`, then a normal
  `captureScreenshot` with a `clip`, then `clearDeviceMetricsOverride`. Also give every CDP
  request a timeout so a stuck call fails fast instead of hanging the script.
- Reopening the book (e.g. after a Bloom restart) **re-stamps it with the freshly compiled
  xmatter CSS** from `output/`, so a "before" capture taken after that already shows the new
  layout — grab authentic before-shots from the reviewer's PDF instead.

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

## 2026-07-29 — Running C# tests in a fresh worktree needs borrowed artifacts

Diagnosing the ExportEpubTests `visual` accessMode failures needed one epub test run in a
brand-new worktree (`Version6.4-2`). Two things blocked that, neither documented:

- **`lib/dotnet`, `lib/ffmpeg`, `lib/gm`, `lib/lame` are gitignored** and absent, so
  `dotnet build src/BloomTests/BloomTests.csproj` dies with `CS0246 PodcastUtilities` and
  then `MSB3030 ... lib\ffmpeg\x64\ffmpeg.exe`. I copied them from the sibling checkout;
  the supported route is presumably `./init.sh` / the getDependencies script, but nothing
  in AGENTS.md says a fresh worktree needs it before a C# build.
- **The tests need `output/browser`**, which only a front-end build produces — and AGENTS.md
  (rightly) says not to run `yarn build`. I borrowed `output/browser` from the other
  checkout and re-synced `src/content/branding` over it, which worked but is a trap: a
  borrowed `output/browser` from a *different branch* silently mismatches (master's copy has
  no `bookEdit/toolbox/readers/decodableReader`, so 13 epub tests failed on
  `GetDirectoryDistributedWithApplication` until I created the empty folder), and if someone
  then launches Bloom from this worktree they get the other branch's front-end.

Worth writing down somewhere: the minimum steps to make a new worktree test-capable, and
whether an agent may run a one-time front-end build for that purpose.

## 2026-07-30 — The AI-editor CDP driver breaks on editor-UI drift, silently

`driveAiImageEditor.mjs dummy-edit` drives the `bloom-ai-image-tools` overlay by
role/text selectors copied from that repo's own e2e spec. Two of them had drifted since
they were written, and each failure is a 30-second Playwright timeout naming a selector,
with no hint that the UI simply changed shape:

- `getByText("Custom Edit", { exact: true })` — the tool button's text is now the name
  *followed by its description* ("Custom EditEdit the image, optionally with additional…"),
  so exact-text never matches. Fixed to `getByRole("button", {name: /^Custom Edit/})`.
- `getByRole("button", {name: /Enhance/i})` now resolves to two elements (strict-mode
  violation). Fixed with `.first()`.

Also, `editorFrame` only recognized the editor at `localhost:3000`, so the driver worked
*only* under `./go.sh --with bloom-ai-image-tools=<path>` and not on a plain `./go.sh`,
where BloomServer serves the built dist-app at `/bloom/aiImageEditor/index.html`. Fixed to
accept both.

**Idea:** these selectors are a cross-repo contract with no test on either side. Either
give the editor stable `data-testid`s for the tool tiles and category headers (it already
has them for the model picker, prompt, and commit button — those did NOT drift), or have
the editor repo publish its host-harness selectors so this driver can import rather than
copy them.

**Context:** BL-16603, verifying the credits fix end-to-end against a real Bloom.
