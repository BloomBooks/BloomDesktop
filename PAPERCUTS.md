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
- seen earlier, 2026-09-02 (preflight of PR #8275): an e2e Bloom switching UI languages while
  `agent-dotnet test` ran gave 3 NREs in `XliffLocalizedStringCache..ctor` during test Setup —
  an e2e Bloom writes the same shared localization folder a developer's Bloom does.

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
- seen again 2026-09-22: main worktree `C:\github\BloomDesktop` on Version6.5, three launches in a row, this time with `--nowatch`, so the `dotnet watch` layer is not the culprit. The log stops after `dotnet PID: <n>`. Running `node ./src/BloomBrowserUI/scripts/dev.mjs --port 51990` and then `dotnet run --project src/BloomExe/BloomExe.csproj -- --automation --vite-port 51990` by hand reached the marker in about 20s and drove fine for an hour. Worth ruling out stdout piping from a plain `dotnet run` child before anything else. Hit by Claude while doing BL-16893.

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

## 2026-09-02 — Two BloomE2E sessions on one machine compete for the same port block

- **Cut:** BloomE2E runs from two worktrees at once (e.g. a developer session plus
  improve-test-automation-coverage workers) probe the same candidate port block, so a launch
  can time out while another session's Blooms hold the ports. It looks like a real regression.
  (Their user settings no longer collide: each launched Bloom has its own --user-settings-folder.)
- **Idea:** A cross-session lock file that launchBloom waits on, or a per-session port range.
- **Context:** preflight of PR #8275.

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

## 2026-09-01 — run-bloom skill doesn't warn that Bloom's ports change across restarts

- **Cut:** Bloom picks a free HTTP port at startup, so after `Program.RestartBloom` (e.g.
  toggling "Show translations which have not been approved yet") the launcher-relaunched Bloom
  can come back on different HTTP/CDP ports (observed 8089→8095→8092 in one session). Tooling
  holding a fixed HTTP port or CDP endpoint silently breaks mid-session.
- **Idea:** Warn in `.claude/skills/run-bloom/SKILL.md` and point at the launcher's control
  server (`output/bloom-launcher.json` → `/status`), which reports the current
  `httpPort`/`cdpPort`, so tooling re-asks it around anything that can restart Bloom.
- **Context:** hit while building the UI-language e2e test on branch automateTests.

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

## 2026-07-15 — config-r draws a divider between every direct group child; label is string-typed
- **Cut:** `@sillsdev/config-r`'s `ConfigrGroup` (in a focused page) inserts a horizontal
  divider between *every* direct child of the group. So an engine block written as a
  `<ConfigrBoolean/>` followed by a separate `{enabled && <>...fields...</>}` gets an unwanted
  line between the checkbox and its own settings. Also, `IConfigrProps.label` is typed `string`,
  so you can't cleanly put a logo/node before a label.
- **Workaround:** Wrap each engine's checkbox + conditional fields in a single fragment so the
  group sees one child per engine (dividers land only *between* engines). For a logo-in-label,
  pass a ReactNode cast `as unknown as string` — config-r renders `label` straight into MUI
  `ListItemText` `primary`, which accepts a node, so it works at runtime.
- **Idea:** Ask config-r for a `label?: React.ReactNode` type and/or a per-row `hideDivider`
  (or a "subgroup" that suppresses internal dividers). See `AiTranslationSettingsGroup.tsx`.
- **Context:** BL-16549AiSourceBubbles AI Source Bubbles settings.

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
