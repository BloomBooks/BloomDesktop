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

## 2026-08-24 — The book folder's own basePage.css can be older than the one you just built

- **Cut:** Bloom serves `basePage.css` for the edit page out of the *book* folder, and the copy
  there (`<collection>/<book>/basePage.css`) was 71330 bytes with no bloom-table rules at all,
  byte-for-byte the size of `D:/bloom/output/browser/bookLayout/basePage.css` from Aug 8, while
  this worktree's freshly built copy was 74244 bytes and had them. The symptom does not look like
  a CSS problem: the table loses `display: grid`, so every cell becomes a full-width block, cells
  report a height of 1px, and Bloom's picture-fitting code writes nonsense geometry from those
  sizes. Rebuilding the worktree's `basePage.css` changes nothing, because nothing re-copies it.
- **Idea:** When a table (or anything else whose CSS lives in `basePage.css`) is not laid out as
  expected, fetch the stylesheet the page actually loaded and grep it, rather than reading the
  built file: `link[rel=stylesheet]` in the page iframe points at the book folder. Copying
  `output/browser/bookLayout/basePage.css` over the book's copy fixes it immediately. Worth
  finding out what decides not to re-copy it, and whether a book last opened by another checkout's
  Bloom keeps that checkout's support files.
- **Context:** `Add-Tables`, verifying table picture cells in the running Bloom. Cost about an
  hour of chasing a layout bug that was a stale stylesheet.

## 2026-08-24 — A changed bloom-table.css never reaches basePage.css

- **Cut:** `basePage.less` pulls the library's structural styles in with
  `@import (inline) ".../node_modules/bloom-table/dist/bloom-table.css"`, but `build:less-inner`
  (watchLessManager.js) decides whether to recompile by comparing the mtimes of the imports LESS
  reports, and that inline CSS is not among them. So after the library changes its CSS the built
  `output/browser/bookLayout/basePage.css` stays stale and the running Bloom lays tables out by
  the old rules, with nothing saying so.
- **Idea:** Have the manager count an inline-imported file among an entry's dependencies (the
  regex in `scanLessImports` already matches `@import (inline) "..."`; it is `resolveLessImport`
  plus the post-compile `result.imports` list that drop it). Meanwhile: delete
  `output/browser/bookLayout/basePage.css` and run `pnpm --dir src/content run build:less-inner`,
  which rebuilds when the output is missing.
- **Context:** `Add-Tables`, updating Bloom to the current bloom-table. The one stale property was
  `overflow: hidden` where the library now needs `overflow: clip` for nested tables.

## 2026-08-20 — Rebuilding a pnpm-linked front-end dependency needs a whole new go.sh session

- **Cut:** `bloom-table` is linked from a sibling repo, and after `vp pack` there the running
  Bloom kept executing the old code. The launcher's `/restart` does not help: it restarts
  Bloom.exe, but the Vite dev server from the first `go.sh` survives and keeps serving the
  module it transformed at startup (`/@id/bloom-table` was 1462507 bytes stale against a 1462511
  byte file on disk). Killing that one node process to force a fresh server killed the launcher
  with it, so the control API vanished and the developer's Bloom went down.
- **Idea:** Either have `go.sh` watch the dist of linked deps and restart Vite, or give the
  launcher a documented "restart Vite too" action. Meanwhile the skill note that says "restart
  Bloom" should say "stop the session and run `./go.sh` again", because a `/restart` reads as
  enough and is not. `curl http://localhost:<vitePort>/@id/<dep>` and grep for your change is the
  cheap way to tell whether the server is stale.
- **Context:** `Add-Tables` branch, removing the table toolbox and taking the latest bloom-table.
  Cost about twenty minutes plus an unplanned relaunch of the developer's Bloom.

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
## 2026-09-02 — notion_automation.py needs Python, which not every dev machine has

- **Cut:** `.github/skills/improve-test-automation-coverage/notion_automation.py` is the only way the
  add-e2e-test flow reads or updates a Notion test card, and both it and the skill text assume `py`.
  On a machine with no Python (only the Windows Store stub) every `show`/`set` fails, and an agent
  ends up hand-porting the script to Node before it can read the card.
- **Idea:** Rewrite it as `notion_automation.mjs`: the repo already requires Node and the script is
  stdlib-only (`urllib` → `fetch`), so nothing else changes; update the skill text and worker brief.
- **Context:** Hit while automating Test Case ID 356 on a machine with no Python.

## 2026-09-01 — VR suite: a slow first preview load fails its case via Playwright's default 30s goto timeout

- **Cut:** The first case after Bloom starts pays for the first book-preview load (~33s observed
  on a dev machine vs 6–10s for later cases). `loadPreviewAndWaitUntilReady`
  (`src/BloomVisualRegressionTests/index.spec.ts:360`) lets a `page.goto` TimeoutError escape its
  retry loop, and the 30s limit is Playwright's default navigation timeout, not the suite's own
  120s test timeout — so one slow first load fails the whole case.
- **Idea:** Give the first `goto` (or all of them) an explicit longer timeout, or catch the
  TimeoutError inside the retry loop.
- **Context:** Seen once in three otherwise-identical local runs while verifying the
  bloom-testing-inputs rewire; not the BL-16612 hang (Bloom kept serving all later cases).

## 2026-08-31 — AGENTS.md's vitest-wedge workaround (`--no-file-parallelism`) doesn't always work

- **Cut:** AGENTS.md ("If the front-end test suite seems to hang, re-run it with
  `--no-file-parallelism`") promises that re-running that way completes the suite green. It
  didn't: `yarn vitest run --no-file-parallelism` stopped dead after 11 test files with no error
  and no summary, and was still stuck 19 minutes later. Cost ~25 minutes of a preflight run
  before it was killed. The advice is stated as a reliable fix, so the natural response is to
  keep waiting rather than to try something else.
- **Idea:** `yarn vitest run --no-file-parallelism --pool=threads` ran the whole suite in ~3.5
  minutes (49 files, 532 tests). The default pool is `forks`, so the wedge looks fork-specific —
  either document `--pool=threads` as the workaround or set `pool: "threads"` in
  `vite.config.mts`. Worth reproducing on another machine before rewriting the AGENTS.md advice.
- **Context:** BloomDesktop, during `/preflight` of PR #8267 (BL-16786). Not the same cut as the
  2026-07-27 entry (C# host aborting alongside vitest): both the wedged run and the successful
  `--pool=threads` run overlapped a `dotnet test`, so concurrency wasn't the differentiator here.

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

## 2026-07-24 — agent-dotnet.sh collides with itself when a build and a test run overlap
- **Cut:** The wrapper isolates per *terminal*, not per *command*, so a `build` started while
  that same terminal's `test` is still running fails with MSB3027 — "Bloom.dll ... locked by:
  testhost". Agents that kick a full suite into the background and keep working hit this and
  can mistake it for a real build break.
- **Idea:** Either serialize (a lock file in `output/agent/<key>/`) or give a concurrent
  invocation its own subtree, and make the error message say "another agent-dotnet command is
  using this tree" instead of a raw MSBuild copy failure.
- **Context:** BloomDesktop, `/preflight` of PR #8107 (dev launcher control API).
- **seen again 2026-08-26:** `/preflight` of PR #8239 (BL-16763). The failed copy was read as a
  build break for a comment-only commit, which had already been pushed.

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

**seen again 2026-08-31 (BL-15958):** same cut, this time as `Could not find the directory
output\browser\appearanceMigrations` in `AppearanceSettingsTests`. The sequence that finally
made a fresh worktree testable: `build/getDependencies-windows.sh` (its CS0246 error names
`PodcastUtilities.PortableDevices`, which reads as a missing NuGet package, not a fetch step),
then `pnpm install` in `src/BloomBrowserUI` **and separately** in `src/content` (without the
second one the build stops at `checkForNodeModules.js`), then a full
`pnpm -C src/BloomBrowserUI build`. `build/agent-vite.sh` is not enough: it skips the
content-copy steps the tests need.

## 2026-08-28 — Moving a worktree between master and Version6.5 changes which settings file Bloom reads

`BloomExe.csproj` sets `<Version>` per branch: 6.6.0.0 on master, 6.5.0.0 on Version6.5.
`CrossPlatformSettingsProvider` puts the user settings under
`%LOCALAPPDATA%\SIL\Bloom\<version>\user.config`, so re-basing a worktree from master onto
Version6.5 silently swaps Bloom onto a different settings file.

The symptom names nothing: Bloom opens some old collection you have not used for weeks, and
here it was one that crashes on open. Opening a good collection in another dev Bloom does not
help, because that copy is 6.6.0.0 and writes the other file. `MruProjects` is only the most
visible setting; every other user setting jumps too.

**Workaround:** edit `%LOCALAPPDATA%\SIL\Bloom\6.5.0.0\user.config` and put the collection you
want first in `MruProjects`, or delete the entries so Bloom shows the collection chooser.

**Context:** BL-16603, verifying the credits fix end-to-end against a real Bloom.

## A rebuilt bloom-table never reaches the running Bloom until the dev server restarts

**2026-08-20, Add-Tables.** `vite.config.mts` deliberately puts `bloom-table` in
`optimizeDeps.exclude` with a comment saying that pre-bundling would cache a stale copy, and
that excluding it "makes Vite serve the dist live, so a `vp pack` in the sibling repo shows up".
It does not show up. The page loads it as `/@fs/D:/bloom-table/dist/bloom-table.mjs?t=<stamp>`,
and Vite keeps serving the transform it cached under that exact URL: the file is outside the
project root, so nothing watches it, so the stamp never changes and the cache is never
invalidated. A page reload, a cache-disabled reload, deleting `node_modules/.vite/deps`, and
`launcherControl.mjs --restart` all leave the old library in place.

The cost is a wrong diagnosis, not just lost time: the new code is served correctly for the
Bloom-side file and only the library is stale, so the console fills with
`TypeError: dragToResize.beginResizeAtPoint is not a function` from a line that plainly calls a
method the built `.d.mts` and `.mjs` both contain. It reads as a build or export problem in the
library.

What worked: `launcherControl.mjs --shutdown` then `--ensure-running --wait-ready`, i.e. a fresh
Vite. Note the ports change, so re-read `output/bloom-launcher.json`, and Bloom comes back on the
collection tab (`switchWorkspaceTab.mjs --running-bloom --tab edit`).

**Idea:** either add `D:/bloom-table/dist` to `server.watch`, or have `go.sh` run bloom-table's
`build:watch` when it is linked, so a `vp pack` there triggers the invalidation Vite needs.
**Idea:** `./go.sh` could say which settings folder this build uses, or a dev build could name
the branch rather than the version in that path.

**Context:** BL-16781, after re-basing the `dev-blorgswitch` worktree onto Version6.5.

## An e2e test cannot use a `data-testid` you just added to the front end

**2026-09-04, Add-Tables.** `src/BloomE2E` launches a real `Bloom.exe`, and that Bloom loads its
UI from the shared `output\browser`, not from a Vite dev server. So a `data-testid` added to a
`.tsx` file is invisible to the test until someone repopulates `output\browser` with a full
`pnpm build` — which AGENTS.md tells agents not to run, because it wrecks the dev server and the
Bloom the developer has running against it. The two rules meet in a place with no move in it: the
skill says to prefer a testid over matching an English label, and the environment says the testid
cannot take effect.

The failure does not look like a build problem. The locator simply finds nothing, in a Bloom whose
markup is correct in the source you are reading, so it reads as a wrong selector and you go
looking for a different one.

What worked: match the English `aria-label` the library writes, and add testids only in
`D:\bloom-table\src`, which is a separate build. Cost, roughly an hour, twice.

**Idea:** have the e2e fixture build the front end into its own tree (the way
`build/agent-vite.sh` already does) and point the Bloom it launches at that, so a test runs
against the source in the worktree rather than against whatever was last built.

**Context:** adding the table end-to-end tests, `tables-core.spec.ts` and `tables-extended.spec.ts`.

## Two agents running e2e suites at once fail each other's launches, and it looks like a fixture bug

**2026-09-05, Add-Tables.** Two worktrees running Playwright suites on this machine each made the
other's `Bloom.exe` slow to start, past the fixture's two-minute readiness limit, twice in five
runs. The message is `Bloom did not open the collection within 120s` followed by the list of
instances the fixture can see, and the collection it says it wanted is right there in that list,
because the diagnostic look happens a second after the last poll gave up. So it reads as a
discovery bug, and half an hour goes into the innocent code. Neither run is told the other exists.

Raised the limit to four minutes in `src/BloomE2E/fixtures/launchBloom.ts` and wrote it up in
`src/BloomE2E/AUTOMATION-DEBT.md`.

**Idea:** a machine-wide lock, or refuse to start while a Bloom launched by another e2e run is up,
and say so. Agents in two worktrees is now the normal case, not the odd one.

**Context:** gating `tables-gating.spec.ts` through three consecutive clean runs.
