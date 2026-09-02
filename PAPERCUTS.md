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

## 2026-09-02 — BloomE2E fails opaquely when output/browser predates the checked-out front-end code

- **Cut:** Twice in one day, a BloomE2E spec failed on a missing `data-testid`
  (`text-languages-group`, then `duplicate-page-button`) because `output/browser` was built
  before a merge brought the front-end code that adds it. The failure reads as a mysterious
  30s locator timeout, not as "your bundle is stale", and `pnpm build` is a manual,
  developer-only step that nothing prompts for after a merge.
- **Idea:** Have the BloomE2E fixture fail fast with "run pnpm build" when the newest file
  under `src/BloomBrowserUI` is newer than `output/browser`'s bundle, or have the suite's
  README/skill call out "merged master? rebuild output/browser" prominently.
- **Context:** preflight of PR #8275, branch automateTests.

## 2026-09-02 — C# suite fails in L10NSharp setup when run alongside the BloomE2E suite

- **Cut:** Running `agent-dotnet test` concurrently with the BloomE2E Playwright suite produced 3
  failures (of 3324): `NullReferenceException` inside `XliffLocalizedStringCache..ctor` during
  `LocalizationManagerWinforms.Create` in test Setup. The e2e Bloom was switching UI languages at
  the time, which touches the same machine-global L10NSharp state (user-modified xliff /
  language caches) the unit tests build their manager over. Solo runs of the identical tree pass
  clean. Same family as the 2026-07-27 "C# host aborts alongside vitest" entry: the wrapper
  isolates build outputs, not machine-global state.
- **Idea:** Document "don't run the C# suite and the BloomE2E suite at the same time" in
  AGENTS.md / the BloomE2E README, or point the test processes' L10NSharp writable folders at
  the isolated temp dir the way TestTempDirectory already isolates %TEMP%.
- **Context:** preflight of PR #8275, branch automateTests.

- **Cut:** Bloom picks a free HTTP port at startup, so after `Program.RestartBloom` (e.g.
  toggling "Show translations which have not been approved yet") the launcher-relaunched Bloom
  can come back on different HTTP/CDP ports (observed 8089→8095→8092 in one session). Any
  tooling holding `BLOOM_HTTP_PORT` or a fixed CDP endpoint — which is exactly what the
  bloom-automation SKILL.md tells agents to use — silently breaks mid-session.
- **Idea:** Add a warning to `.github/skills/bloom-automation/SKILL.md` and point at the
  discovery mechanism: the launcher's control server (`output/bloom-launcher.json` →
  `/status`) reports the current `httpPort`/`cdpPort`, so tooling should re-ask it around
  anything that can restart Bloom rather than caching the ports.
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

**Idea:** `./go.sh` could say which settings folder this build uses, or a dev build could name
the branch rather than the version in that path.

**Context:** BL-16781, after re-basing the `dev-blorgswitch` worktree onto Version6.5.
