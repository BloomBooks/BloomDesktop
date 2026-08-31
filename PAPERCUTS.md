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

## 2026-09-17 — A running Bloom locks Bloom.xlf and fails the C# suite
- **Cut:** `LocalizationManager.Create` writes `%LOCALAPPDATA%\SIL\Bloom\localizations\en\Bloom.xlf`,
  a machine-global path outside the per-run temp isolation, so the developer's own running Bloom.exe
  collides with a test run: `IOException ... being used by another process` out of `BookDataTests.Setup`
  (also seen as an NRE in `XliffTransUnitUpdater..ctor` from the same path). It looks like a flaky test.
- **Idea:** point the localization folder at the per-run temp dir the way `TestTempDirectory.cs` already
  does for everything else, or at minimum say in AGENTS.md that this one path is still shared — it
  currently reads as though `agent-dotnet.sh` plus temp isolation fully solve "build/test while Bloom runs".
- **Context:** hit twice on BL-16806 (PR #8286); passed clean on re-run both times.

## 2026-09-16 — The React component tests log 249 errors in a fully green run
- **Cut:** A passing nightly (35076535729) carries 249 `[WebServer] Error reported from component:
  {"message":"Unexpected promise failure ..."}` lines, all inside the React component-test step —
  150 bare `404`s and 99 on `/bloom/api/editView/setModalState`. Nothing fails on them, and they
  are not new. The cost is that a *real* error of that shape is now invisible: nobody can pick one
  new "Unexpected promise failure" out of 249 expected ones, so the one place those errors would be
  noticed is the one place they cannot be.
- **Idea:** Either stub those endpoints in the component-test harness, or have the components skip
  the calls when no Bloom server is behind them — then a line of that shape in the log means
  something again. Failing that, assert the count so a jump is visible.
- **Context:** Found while reading a *passing* nightly for what it could teach; the same lines are
  in the previous night's failing run, so this is long-standing, not a regression.

## 2026-09-15 — A unit test can pass while exercising nothing, and nobody notices
- **Cut:** `audioRecordingSpec.ts`'s `importRecording() encodes special characters` passed for years
  without testing anything. It mocked `fileIO/chooseFile` on `axios.get`, but `importRecordingAsync`
  asks with `postJson` (a POST), so the import gave up on its first line and none of the path ran —
  and because *every* assertion in the test is commented out (with a note that tracking the request
  details "doesn't seem worth it"), it still went green. A test named for an assertion it does not
  make is worse than no test: it tells you the case is covered. This one would have caught BL-16873.
- **Idea:** Sweep the front-end specs for `it(...)` blocks with no live `expect`, and either restore
  an assertion or delete them. A lint rule (`vitest/expect-expect`) would make it permanent.
- **Context:** Found while fixing BL-16873; I repaired the shared mock but left that test's
  commented-out assertions alone, so the cut is still open.

## 2026-09-11 — The Bloom log an e2e failure keeps is only reachable by unzipping the trace

- **Cut:** #8343's `keepEvidenceOnFailure` attaches Bloom's `Log.txt` with `testInfo.attach({body})`.
  The collection copy lands as real files under `test-results/<test>/collection/`, but the log does
  not land anywhere you can open: it is not in `test-results/<test>/`, not in
  `playwright-report/data/`, and the run's console prints only its truncated first line. Reading it
  from the nightly's `e2e-report` artifact meant unzipping `trace.zip` and guessing which
  `resources/<40-hex>` blob it was.
- **Idea:** write it with `testInfo.outputPath("bloom-log.txt")` and attach by `path`, so it sits
  beside `collection/` as a plain file in the artifact.
- **Context:** hit while reading the 2026-09-11 nightly for the cover-title failure
  (`src/BloomE2E/AUTOMATION-DEBT.md`); cost about ten minutes of hunting.

## 2026-09-11 — pnpm skips the install entirely after a rebase changes the lockfile

- **Cut:** After rebasing onto a master that had refreshed `pnpm-lock.yaml`, `pnpm install` in
  `src/BloomBrowserUI` printed "Already up to date" and did nothing. So did `pnpm install --force`.
  So did `--force` after I deleted `node_modules/bloom-image-gallery` outright — pnpm cheerfully
  reported up-to-date with the package physically absent. The culprit is pnpm 11's fast-path cache
  `node_modules/.pnpm-workspace-state-v1.json`; it was dated 2026-09-02 and `--force` does not
  invalidate it. Deleting that one file made the install run for real, which then pulled the new
  gallery commit *and* bloom-player 2.20.1-alpha.6 -> 2.20.3-alpha.1. Both had been silently stale.
- **Idea:** Note it in AGENTS.md next to the front-end build guidance: "Already up to date" from
  pnpm is not evidence — if a dependency looks stale after a rebase, delete
  `node_modules/.pnpm-workspace-state-v1.json` and install again. Better, have a script verify the
  installed tree against the lockfile resolution rather than trusting the message.
- **Context:** BloomDesktop BL-16748-pseudo-english, PR #8283. Andrew noticed the symptom ("we don't
  seem to be getting the latest image-chooser-gallery") — nothing in the tooling flagged it.

## 2026-09-11 — switchWorkspaceTab.mjs drives whichever Bloom page it happens to find first

- **Cut:** `switchWorkspaceTab.mjs --tab publish` failed with "Could not find a workspace tab
  control" twice in a row while Bloom was running normally. `getBloomPage` takes the *first* CDP
  page whose URL contains `/bloom/`, and that was the open Settings dialog, which has no workspace
  tabs. Separately, on a fresh launch with no book selected only `workspace-tab-collection` exists
  in the DOM, so the script cannot reach edit/publish until something selects a book — the error
  message is the same in both cases and names neither.
- **Idea:** Pick the page that actually has `[data-testid="workspace-top-bar"]` rather than the
  first URL match, and when the requested tab is absent say which tabs *are* present (and that a
  book may need selecting) instead of the generic not-found message.
- **Context:** BloomDesktop PR #8283 preflight; worked around by driving Playwright directly.

## 2026-09-04 — Launcher times out waiting for BLOOM_AUTOMATION_READY while a direct dotnet watch works
- **Cut:** `go.mjs` / `launcherControl.mjs --ensure-running` built Bloom in ~6s, printed `dotnet watch ⌚ Loaded 2 project(s)`, then never saw the ready marker and tore the whole stack down after the 120s `launchTimeoutMs` in `scripts/watchBloomExe.mjs` — three times in a row. Running `dotnet watch run --project src/BloomExe/BloomExe.csproj --non-interactive -- --automation` by hand from the same shell started Bloom and printed `BLOOM_AUTOMATION_READY` within seconds (alongside a running BetaInternal, so it was not the single-instance token).
- **Idea:** Find what differs when `watchBloomExe.mjs` spawns dotnet watch (`--vite-port`/`--label` args, control-port env, stdout piping) and make the launcher print dotnet watch's later output or the Bloom PID's window titles when it gives up, so the failure is diagnosable. Consider making the timeout configurable.
- **Context:** worktree Format-Gear-Positioning-356 at the Version6.5 tip, while fixing BL-16809; hit by Claude.

## 2026-09-04 — An e2e test cannot use a data-testid you just added to the front end
- **Cut:** `src/BloomE2E` launches a real `Bloom.exe`, and that Bloom loads its UI from the
  shared `output\browser`, not from a Vite dev server. So a `data-testid` added to a `.tsx`
  file is invisible to the test until someone repopulates `output\browser` with a full
  `pnpm build`, which AGENTS.md tells agents not to run, because it wrecks the dev server and
  the Bloom the developer has running against it. The add-e2e-test skill says to prefer a
  testid over matching an English label, and the environment says the testid cannot take
  effect. The failure does not look like a build problem: the locator finds nothing, in a
  Bloom whose markup is correct in the source you are reading, so it reads as a wrong selector
  and you go looking for a different one. Cost, roughly an hour, twice.
- **Workaround:** start a Vite dev server on port 5173 and set `BLOOM_E2E_VITE_PORT=5173` for
  the run (README, "Testing a front-end change"). That needs 5173 free, which it was not here:
  another project's dev server held it. Failing that, match an English `aria-label` the front
  end already writes, or ask the developer to run the full build once no Bloom is running from
  that worktree.
- **Idea:** have the e2e fixture build the front end into its own tree (the way
  `build/agent-vite.sh` already does) and point the Bloom it launches at that, so a test runs
  against the source in the worktree rather than against whatever was last built, on whatever
  port is free.
- **Context:** the Add-Tables branch's e2e tests, 2026-09-04; ported to the e2e-infrastructure
  branch with the tests' helpers.
- **Update 2026-09-05:** the fixture now refuses to launch a Bloom whose bundle, or whose
  `Bloom.dll`, is older than its source, and names the newer file (`assertBuildIsNotStale` in
  `src/BloomE2E/fixtures/launchBloom.ts`). The stale build still has to be rebuilt by hand, or
  bypassed with `BLOOM_E2E_VITE_PORT`, but it can no longer fail a test in silence.

## 2026-09-03 — The e2e fixture launches a stale Bloom.exe when output/Debug/x64 is older than AnyCPU

- **Cut:** `findBloomExe` in `src/BloomE2E/fixtures/launchBloom.ts` tries `Debug/x64` before
  `Debug/AnyCPU` in a fixed order. `dotnet build src/BloomExe/BloomExe.csproj` writes to `AnyCPU`, so
  a leftover `x64` folder from an earlier build wins, and the suite runs an old exe against the
  freshly built `output/browser`. It looked like a broken test: a blank Collections tab,
  `e2e/isCollectionReady` never true, and `BLOOM_AUTOMATION_MONITOR` ignored (the exe predated it).
  Nothing in the run says which exe was launched; `common/instanceInfo` does.
- **Idea:** pick the newest `Bloom.exe` among the candidates (or the one matching the newest
  `output/browser`), and log the chosen path once at launch so a stale exe is visible in the output.
- **Context:** Test Case ID 358, PR #8289; cost about 20 minutes of misdiagnosis.

## 2026-09-02 — notion_automation.py needs Python, which not every dev machine has

- **Cut:** `.github/skills/improve-test-automation-coverage/notion_automation.py` is the only way the
  add-e2e-test flow reads or updates a Notion test card, and both it and the skill text assume `py`.
  On a machine with no Python (only the Windows Store stub) every `show`/`set` fails, and an agent
  ends up hand-porting the script to Node before it can read the card.
- **Idea:** Rewrite it as `notion_automation.mjs`: the repo already requires Node and the script is
  stdlib-only (`urllib` → `fetch`), so nothing else changes; update the skill text and worker brief.
- **Context:** Hit while automating Test Case ID 356 on a machine with no Python.
- seen again 2026-09-03 (Test Case ID 358): ported to Node once more, this time with the card
  split (`[Automated portion]` / `[Manual portion]`, related both ways) that `add-e2e-test` asks
  for and the Python script has no command for either.

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

**Idea:** `./go.sh` could say which settings folder this build uses, or a dev build could name
the branch rather than the version in that path.

**Context:** BL-16781, after re-basing the `dev-blorgswitch` worktree onto Version6.5.

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
- seen again 2026-09-18: from the other direction. A dev Bloom crashed (FailFast from a `Debug.Assert`
  during BloomPUB publish) and the Doctor could not have caught it even without `--automation`:
  `RunFreezeDoctor` is False in the dev user.config, and the Debug build puts only
  `BloomFreezeDoctor.Protocol.dll` beside `Bloom.exe` (no `BloomFreezeDoctor.exe`), so
  `DoctorLauncher.FindTheDoctor` finds nothing and `RequestDumpBeforeDying` returns before it logs.
  Only the orphaned session json (no `Exit` block) recorded that the run ended badly. If the Doctor
  is meant to be usable in dev, go.mjs/init.sh need to build and place it and the setting needs a dev default.
## 2026-08-27 — No front-end test runs in a worktree until bloom-table is linked

- **Cut:** In a fresh worktree, *every* vitest file that reaches `bloomEditing.ts` dies with
  `Failed to resolve import "bloom-table"`, because `bloom-table` is not on npm and
  `src/BloomBrowserUI/package.json` only mentions it in a comment key. Nothing in AGENTS.md's
  "front-end checks are always safe — run them freely" says the suite cannot run at all yet.
- **Idea:** Have `init.sh` link `bloom-table` (or say so in AGENTS.md next to `pnpm test`).
  `New-Item -ItemType Junction node_modules/bloom-table -> D:\bloom-table` also works and,
  unlike `pnpm link`, leaves `package.json` clean.
- **Context:** `grid-calendar`, adding a StyleEditor spec.

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
- **seen again 2026-09-11:** a new mechanism for the same lock — stopping the backgrounded
  `agent-dotnet.sh test` task (TaskStop) does **not** kill its `testhost.exe` child, which keeps
  holding `Bloom.dll`, so every later build in that terminal fails MSB3027 until you
  `taskkill //PID <testhost> //F` by hand. Killing the task is not enough; the wrapper should reap
  its own test host, or say that an orphan is still holding the tree.

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

## An NUnit test that reads a factory template silently tests a stale build

**2026-08-28, BL-16777-table-calendar.** `BloomFileLocator.GetFactoryBookTemplateDirectory`
resolves to `output/browser/templates/template books/<name>/`, which only the content build
populates. Agents are told not to run `pnpm build` (it wipes `output/browser` under the
developer's running Bloom), and `build/agent-dotnet.sh` does not build content, so a new test
that creates a book from a template runs against whatever the developer last built. In this case
`Wall Calendar.html` there was two hours old and `meta.json` predated the change under test, so
the test reported page counts for the old template and a `meta.json` edit appeared to do nothing.
Nothing in the failure says "stale artifact"; it reads as a bug in the markup you just wrote.

What worked: compile the one template by hand into `output/browser` the way the watch does, and
copy the non-pug files across:

```bash
OUT="output/browser/templates/template books/Wall Calendar"
SRC="src/content/templates/template books/Wall Calendar"
src/content/node_modules/.bin/pug "$SRC/Wall Calendar.pug" --pretty --out "$OUT"
src/content/node_modules/.bin/lessc "$SRC/wallCalendar.less" "$OUT/wallCalendar.css"
cp "$SRC/meta.json" "$OUT/meta.json"
```

Deleted source files also linger in `$OUT`, because `cpx` only copies.

**Idea:** a `build/agent-content.sh` that rebuilds one template folder (pug + less + file copy +
prune) into `output/browser` without `clean.js`, and a line in AGENTS.md saying that any test
touching `GetFactoryBookTemplateDirectory` needs it first.

## Changing the selected book over the API while the Edit tab is open kills a Debug Bloom

`POST collections/selectAndEditBook` (and `collections/selected-book`) while the Edit tab is
already open on a *different* book leaves the edit view showing the old book: the handler ends
with `ChangeTab(edit)`, which is a no-op when that tab is already active (the BL-8382 guard), so
only the model moves. The next page request then fails a null guard in
`EditingModel.GetEditPageIframeContents` (`Value cannot be null. (Parameter 'Could not find
expected page')`), and on a Debug build that `Debug.Fail` is `Environment.FailFast`: the whole
process dies, taking the launcher stack with it. The Event Log (`.NET Runtime` provider) has the
message; the app window just vanishes. This killed Bloom three times in one afternoon before the
pattern was spotted.

What works: drive the human order, one step at a time — `POST workspace/selectTab
{tab:"collection"}`, poll `GET workspace/tabs` until `tabStates.collection == "active"` (leaving
Edit saves the page first, and "active" appears only after that save), then change the selected
book, then `selectTab` back to `edit`. `calendar.spec.ts`'s `reopenTheCalendarBook` /
`selectTabAndWait` are a worked example. A fix inside the API (leave the tab, then select once
the save completes) was written and then backed out on 2026-08-28 at John's request; if this
keeps biting, that is the shape it took.

## Three ways one shared Bloom bites several agents at once

All three of these hit one session on 2026-08-29, in which six agents drove a single `go.sh`
Bloom.

**A book switch over the API wedges the edit view.** This is the papercut above, met head on:
selecting `Book-2c0df36d` and posting `app/makeOrEditBook` while the Edit tab was open on
another agent's book left the edit WebView2 showing only a spinner, with one CDP target whose
URL still named the *other* book and no `page` iframe. Posting `editView/setModalState false`
twice cleared `navigationLocked` and re-enabled the tabs, but the edit view itself never came
back; only `launcherControl.mjs --restart` fixed it. **So the agent instructions that hand out
the two-POST recipe must carry the "leave Edit first" order**: `workspace/selectTab
{tab:"collection"}`, poll `workspace/tabs` until collection is active, change the selected book,
then `selectTab` back to edit. Driven that way, the same switch worked every time afterwards.

**A menu left open by a crashed script silently redirects the next script's clicks.** A MUI menu
puts an invisible backdrop over the whole page. A script that throws part way through (a locator
timeout, a detached frame) leaves the menu up, and the *next* script's "click the grid's button"
lands on whatever menu item sits at those coordinates. That produced a string of changes nobody
asked for: a first day of the week set to Sunday, a year set to 2028. It looks exactly like a
racing teammate, and it is not. **Start every automation script by pressing Escape and confirming
no menu is painted** (`.MuiPopover-root:not([aria-hidden="true"])` — a closed MUI menu still has
a bounding box, so measuring width is not a test of whether it is open).

**`frame.locator("button").nth(i)` does not mean the i-th visible button.** Indices gathered from
a filtered list of visible buttons do not line up with the unfiltered `nth()` order, so the
"click the last button" idiom clicked Delete instead of the "..." menu and removed a calendar
grid from a teammate's cover page. Recovery: Bloom's Undo did nothing ("There is nothing to
undo"), but the deletion was only in the DOM, so the saved `.htm` still had the element; lifting
its markup out of the file and inserting it back into `.bloom-canvas` restored the page exactly,
and the next save wrote it back. **Identify a control by something that names it** (here
`svg[data-testid='MoreHorizSharpIcon']` versus `DeleteOutlineIcon`), never by position in a list.

**Also worth knowing:** a nested item of the canvas element's context menu opens its submenu on
click, while the page grid's own button menu opens on hover; and another agent editing front-end
source detaches the `page` frame every few minutes through Vite reloads, so any script that must
survive that has to re-acquire the frame and retry rather than hold one reference.

**A second rebuild of a linked front-end library does not reach the running Bloom, and the
symptom is a spinner that never ends.** The Vite dev server that `go.sh` starts serves a linked
dependency's built module by absolute path (`/@fs/D:/bloom-table/dist/bloom-table.mjs`) and caches
the transform under a query of its own (`?t=<timestamp>`). Rollup replaces that file on each
build rather than writing it in place, so Vite's watcher stops following it after the first
change; the first `pnpm run build` in the library did reach Bloom, and the second did not.
Bloom then sits on the startup spinner with nothing in the console, because the failure is a
rejected dynamic import inside the ReactControl page's own `main()`. Importing the entry by hand
in that page names the real problem: `does not provide an export named '<the new export>'`.
Diagnosis takes two curls, the plain module URL and the same URL with `?v=probe` added; fresh
content only from the second one means the cache is stale. The only cure found was to stop the
whole session (`POST /shutdown`) and run `./go.sh` again, which costs about five minutes and
changes the Vite and control ports. Worth fixing at the source: either add the linked
dependency's built files to `server.watch` in the Vite config, or give the launcher control API a
`/restart-vite` endpoint, since `/restart` restarts only Bloom.exe.

**`POST /restart` on the launcher control API can take the whole `go.sh` session with it.** One
restart, asked for to be sure a rebuilt linked library was really loaded, killed Bloom (the log
says PID did not close gracefully within 10 s, force-killing), started launch 2, and then
`dotnet exited before Bloom reported automation-ready startup info` followed by
`Bloom exe flow exited with code 3221225794` (0xC0000142, a DLL initialisation failure) and
`[go] Shutting down`. The launcher then deletes `output/bloom-launcher.json`, so the control port
stops answering and every probe fails with connection refused, which reads as "Bloom is wedged"
rather than "there is no session any more". Recovery is to run `./go.sh` again, which takes about
a minute and gives new Vite and control ports; check `output/bloom-launcher.json` for them rather
than reusing the old numbers. So prefer not to restart at all: the previous entry's spinner trap
did not recur this time, and the dev server log showed `page reload D:/bloom-table/dist/bloom-table.mjs`,
meaning the second library rebuild did reach the running Bloom on its own.

**Vite listens on `::1` only, so `curl http://127.0.0.1:<vitePort>/...` fails with connection
refused.** `netstat` shows `TCP [::1]:60069 LISTENING` and nothing on the IPv4 address. Use
`http://localhost:<port>/` for the curl-the-module check (it resolves to `::1`), even though
`127.0.0.1` is right for the launcher control API and Bloom's own HTTP server. Inside the page
frame a relative `/@fs/...` import fails too, because that frame is served from Bloom's server on
port 8089, not from Vite: import the absolute `http://localhost:<vitePort>/@fs/...` URL there.

**Importing a Vite module from inside the page frame makes Bloom raise "Cannot Find File".** The
edit view's `page` frame is served by Bloom's own server on port 8089, so a relative
`import("/@fs/D:/bloom-table/dist/bloom-table.mjs")` goes to Bloom, not to Vite.
`BloomServer.ReportMissingFile` then reports `Cannot Find File` for
`@fs/D:/bloom-table/dist/bloom-table.mjs` (`NonFatalProblem.Report(ModalIf.Beta, …)`, which is
modal on a Debug build), and the report dialog lands on top of the developer's screen. Use the
absolute `http://localhost:<vitePort>/@fs/...` URL for that check. Worse, `dismissProblemDialog.mjs`
could not see the dialog: every WebView2 asks for the same remote-debugging port, only the first
window binds it, and the dialog's window opened later, so `/json/list` showed one page and the
helper said no dialog was showing. The log and the visible-window list are the checks that work;
see the note added to `.github/skills/bloom-automation/SKILL.md`.

**A `.tsx` edit can leave Bloom's whole window blank on a spinner, and only a relaunch brings it
back.** Editing `bookEdit/js/canvasElementManager/CanvasElementContextControls.tsx` gave Vite a
full page reload of the app shell, and the shell then sat forever on its dark spinner: no window
title change, no error overlay, nothing in the console. What had failed was the shell's one
`await import('http://localhost:<vitePort>/app/App.entry.tsx')`, and everything about the
diagnosis pointed the wrong way. `fetch` of that same URL returned 200 with
`content-type: text/javascript`; a crawl of all 705 modules in the entry's graph found not one
non-200; leaf modules and the optimized deps imported fine; and importing the entry again with a
fresh `?retry=` query failed just the same, so it is not the browser remembering a failed module
either. Only the top-level document was affected, so this is not a broken file: `pnpm typecheck`,
`pnpm lint` and all 871 front-end tests were green throughout, and the same code loaded correctly
after a relaunch. Do not go looking for the guilty module. Recovery, and prefer it over
`POST /restart` (see the entry above, which killed the `go.sh` session): `POST /quit-bloom` then
`POST /start` on the launcher control port, which relaunches on the same ports in about a minute,
and then `POST /bloom/api/app/makeOrEditBook` plus `POST /bloom/api/pageList/pageClicked` to get
back to the page under test. Symptom worth remembering: the CDP target list holds a single page
titled `ReactControl (Vite appBundle)` whose only frame is unnamed, with no `page`, `toolbox` or
`pageList` frames.
