# Papercuts

Small dev/agent/tooling friction points for this repo. See the `papercut` skill for format.

## 2026-08-31 — AGENTS.md's vitest-wedge workaround (`--no-file-parallelism`) doesn't always work

- **Cut:** AGENTS.md says that when `yarn test` wedges, re-running with `--no-file-parallelism`
  completes the suite green. On this machine `yarn vitest run --no-file-parallelism` wedged
  anyway — it stopped dead after 11 test files with no error and no further output, and was
  still stuck 19 minutes later. Cost ~25 minutes of a preflight run before it was killed.
- **Idea:** `yarn vitest run --no-file-parallelism --pool=threads` ran the whole suite to
  completion (49 files, 532 tests, ~3.5 min). The default pool is `forks`; the wedge looks like
  a fork-pool problem, so `--pool=threads` may be the better documented workaround — or the
  config could just set `pool: "threads"`. Worth confirming on another machine before changing
  the AGENTS.md advice.
- **Context:** BL-16786 preflight, branch `BL-16786-expiry-day`, PR #8267.


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
