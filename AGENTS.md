This project has a web front-end at src/BloomBrowserUI.
The front-end uses pnpm 11.5.2. Never ever use npm or yarn.

# Architecture

- C# backend
- web front-end in React/Typescript
- WebView2 for hosting the web front-end in the desktop app
- We strictly control both ends of the API.
    - Don't worry about legacy API support. If you need to change the API, just change it on both sides.
    - Don't be overly defensive about error handling. If the API is used incorrectly, it's fine for it to throw an error. We want to know about it so we can fix it.

# Code Style

- Always use arrow functions and function components in React
- do not destructure props
- do not define a props data type unless it is huge
- example: export const SomeComponent: React.FunctionComponent<{initiallySelectedGroupIndex: number;}> = (props) => {...}

- Avoid removing existing comments unless your changes make them inaccurate/obsolete
- Avoid adding a comment like "// add this line".

- Style elements using the css macro from @emotion/react directly on the element being styled, using the css prop. E.g. `<div css={css`color:red`}>`

- Where possible style things using @emotion/react rather than using sx objects.

- Avoid stacking/nesting ternary (`? :`) operators (e.g. `a ? x : b ? y : z`). They're too hard for humans to read. Use an if/else-if chain (or a switch) instead. A single, non-nested ternary is fine.

- For Typescript coding style, see ./src/BloomBrowserUI/AGENTS.md

# Testing

- Fail Fast. Don't write code that silently works around failed dependencies. If a dependency is missing we should fail. Javascript itself will fail if we try to use a missing dependency, and that's fine. E.g. if you expect a foo to be defined, don't write "if(foo){}". Just use foo and if it's null, fine, we'll get an error, which is good.
- Try to make it so that test failures indicate what went wrong. For example, `fail("An error occurred in setup; we should not have gotten here")` would be better than `expect(false).toBeTruthy();` and `expect(foo).toBe(3);` would be better than `expect(foo === 3).toBe(true);`.
- Add sanity checks to guard against falsely passing tests. For example, when unit testing a method, sanity check that the test data values are as expected before you call the method, and then after you call the method you can verify that those values have changed as expected.
- When running C# tests with `dotnet test`, never pass `--no-build`. Always let dotnet build the test project first so the tests run against the latest code. A stale DLL can cause tests to pass or fail against an old version of the code, hiding real regressions.

## The opt-in Reading App Builder real-build test

`BloomTests.Publish.Rab.RabRealBuildTests.SetupAndBuildAsync_RealReadingAppBuilderBuild_CreatesValidApk`
is the only test that exercises a real Reading App Builder installation end to end: it builds a
BloomPUB into an actual signed Android APK with RAB and Gradle, and checks the result. **It is worth
running after any change under `src/BloomExe/Publish/Rab/`** — nothing else covers that path for
real.

- It needs RAB installed (Bloom's own toolchain under
  `%LOCALAPPDATA%\SIL\Bloom\ReadingAppBuilder\` counts) and **`BLOOM_RUN_RAB_MANUAL_TESTS=1`** set.
  Without the variable it calls `Assert.Ignore`.
- It takes **about 70 seconds**, because it runs a real Gradle build.
- It is `[Category("SkipOnTeamCity")]` / `[Category("RequiresReadingAppBuilder")]`, so **CI never
  runs it**. If it breaks, only someone running it deliberately will find out.

```bash
BLOOM_RUN_RAB_MANUAL_TESTS=1 build/agent-dotnet.sh test src/BloomTests/BloomTests.csproj \
  --filter "FullyQualifiedName~RabRealBuildTests"
```

## Building / testing C# while a Bloom is running

The developer often has a Bloom running (via `./go.sh`) so they can watch your changes
live. That running `Bloom.exe` locks `output\Debug\AnyCPU\Bloom.exe` and `Bloom.dll`, so a
plain `dotnet build`/`dotnet test` fails at the copy step with **MSB3027** ("being used by
another process"). The same collision happens between two builds in separate terminals in
one worktree.

**So build and run C# tests through the wrapper, not `dotnet` directly:**

```bash
build/agent-dotnet.sh test src/BloomTests/BloomTests.csproj --filter "FullyQualifiedName~UrlPathStringTests"
build/agent-dotnet.sh build src/BloomExe/BloomExe.csproj
```

(PowerShell: `build/agent-dotnet.ps1 test ...`.) It takes the exact same arguments as
`dotnet`; it just redirects the whole build (obj + bin) into a private per-terminal tree
under `output/agent/<key>/` so your build/test never touches the locked shared output. This
means you do **not** need to stop the developer's Bloom to build or run unit tests, and
multiple terminals can build/test at once. See `Directory.Build.props` for how it works.

- For `test`, the wrapper **judges the run and prints a verdict as its last line**, so
  `[agent-dotnet] test run completed. Passed! ...` is the only thing you need to read (and it
  survives `| tail`). Do not judge a run by the `Passed!`/`Failed!` summary above it: a run whose
  test host is killed part way through still prints a passing summary of however many tests got to
  run. The wrapper catches that and says `*** TEST RUN ABORTED ***`, and exits non-zero, as it
  does for ordinary failures. The text it looks for lives in `build/test-abort-markers.txt`.
- This wrapper is for **building and running tests only**. To *run* Bloom, still use
  `./go.sh` (see "Running Bloom" below) — the wrapper builds no `Bloom.exe` apphost.
  (`BloomPdfMaker.exe` is the one apphost it does build, because Bloom's PDF code shells
  out to that file by name and the PDF tests fail without it.)
- The full C# suite is expected to be **green** through this wrapper. If you see the
  PdfMaker or xmatter-locating tests fail, that is a real regression in the wrapper /
  `Directory.Build.props`, not the known environment noise it used to be.
- The first build in a fresh terminal is a full (cold) build into that terminal's private
  tree; subsequent builds there are incremental. `output/` is gitignored.

### Temp folders are isolated per test run too

The build tree is not the only thing two concurrent runs would otherwise share. Our tests name
their scratch folders after themselves (`new TemporaryFolder("SomeFixtureTests")`), which are
machine-global paths, and `TemporaryFolder` **deletes** an existing folder of that name before
creating it — so one run's setup would delete another run's in-flight folder.

`src/BloomTests/TestTempDirectory.cs` prevents that: before any fixture runs, it points this
process's temp directory at `%TEMP%\BloomTests\<key>-p<pid>\`. You therefore do **not** need to
invent unique folder names in tests — keep naming a temp folder after your fixture, and it is
already scoped to the run. It also means production code writing to temp while under test is
isolated as well.

Two consequences worth knowing:

- **After a failing run the folder is kept**, so you can look at what the failing test wrote; the
  path is printed on standard error at the end of the run. Passing runs delete theirs, and
  anything older than a day is cleared by the next run.
- **If the folder cannot be deleted, the run says so** — again on standard error, naming one file
  that is still open and the reason the OS gave, without failing the run. That normally means a
  test finished without disposing something; worth chasing, because a leaked handle can make
  later runs behave oddly.
- Note that standard error is the only channel `dotnet test` shows at its default verbosity —
  `Console.Out`, `TestContext.Out` and `TestContext.Progress` are all swallowed. Use
  `Console.Error` for anything a developer must see.
- Every temp path is longer by `BloomTests\<key>-p<pid>\`. Deeply-nested temp paths in tests are
  that much closer to `MAX_PATH`.

## Building / testing the front-end (web UI) while Bloom is running

The developer usually launches Bloom with `./go.sh`, which starts a **Vite dev server** and
has Bloom's WebView2 load the UI from it (not from a `vite build --watch`). Two consequences:

- **Editing `.ts`/`.tsx`/`.less` needs no build at all.** The dev server pushes your change
  into the running Bloom; to see it, attach and observe via the `bloom-automation` skill — do
  **not** build. How the change lands varies: a `.less`/CSS edit hot-swaps in place (no
  reload); a `.tsx` edit often triggers a Vite full page reload (React Fast Refresh falls back
  to it), and for app-shell / entry components that reload briefly blanks the view until Bloom
  re-navigates. So when observing over CDP, wait for the page to settle (or switch tabs and
  back) before concluding an edit "didn't apply". (A few entry points aren't served by the dev
  server and rely on a separate `pnpm watch` = `vite build --watch`; if the developer is
  running that instead, your edits are still rebuilt for you — you still don't build.)
- **Don't run `pnpm build` here** (see below): it wipes and repopulates the shared
  `output\browser` via `clean.js`, disrupting the Bloom running against it, and it does
  nothing useful anyway because the running Bloom loads JS from the dev server, not from
  `output\browser`.

**Automated front-end checks are always safe — run them freely.** None of these build or
touch `output\browser`, so they never disturb the dev server or a watch:

- `pnpm test` (Vitest) — runs in jsdom and transforms modules in memory. This is your primary
  "does my logic/component work" check. (`pnpm lint` and `pnpm typecheck` are likewise safe.)

**To confirm the real production bundle compiles** — bundling / CommonJS-interop errors and
the manifest post-build step that the lenient dev server never exercises — use the isolated
wrapper, the front-end twin of `build/agent-dotnet.sh`:

```bash
build/agent-vite.sh
```

(PowerShell: `build/agent-vite.ps1`.) It sets `BLOOM_UI_OUTDIR` so the whole Vite build lands
in a private per-terminal tree under `output/agent/<key>/browser`, never touching the shared
`output\browser` or any running dev server / watch, so multiple terminals can run it at once.
Like the C# wrapper it is **build-only**: it confirms the bundle compiles; it does *not* let a
running Bloom load those bundles (Bloom reads the fixed `output\browser` / dev server). It
skips the pug/LESS/markdown/static-copy steps, so it is a fast pure-bundle check.


# Terminal
The vscode terminal often loses the first character sent from copilot agents. So if you send "cd" it might just say "bash: d: command not found". Try prefixing commands with a space.

# Running Bloom
- Do not run an already-built `Bloom.exe` directly, because it may be stale and miss local code changes.
- Use a source-aware launcher that picks up the current repo state. Right now the default launcher is `./go.sh` at the repo root. If a build fails with errors like missing `PodcastUtilities`, `IDevice`, or other types/namespaces
  that "could not be found" (CS0246) in files such as `src/BloomExe/Publish/BloomPub/usb/AndroidDeviceUsbConnection.cs`, the problem is probably that this worktree has not got its dependencies yet. Fix that with `./init.sh`.

- Do not launch Bloom with `dotnet run` or `node scripts/watchBloomExe.mjs` unless you are specifically working on the launcher scripts themselves or a better repo-supported source-aware launcher has been documented.

If you create new files for temporary purposes (e.g. output or artifact or log files), be sure to clean them up when you're done and be careful not to accidentally commit them.

# Don't run the full `pnpm build` yourself
You have a complete set of faster, non-disruptive alternatives, so don't run the full `pnpm build`:
- **Checks** — `pnpm lint`, `pnpm typecheck`, `pnpm test`. None of these build or touch `output\browser`.
- **Confirm the real production bundle compiles** — `build/agent-vite.sh`, which builds into an isolated tree and leaves `output\browser` alone (see "Building / testing the front-end (web UI) while Bloom is running" above).
- **See a change in the running Bloom** — just edit the source; the dev server pushes it in. No build.

The full `pnpm build` exists to (re)populate the shared `output\browser` — `clean.js` plus content assets plus the bundle. It's slow, and it wrecks any running Vite dev server / `--watch` and the Bloom loading from it, so it's a developer/CI job, not something to spring on a live session. If you think you genuinely need it, ask the developer to run it (they can stop Bloom first) rather than running it yourself.

# Localization
Whenever you add, modify, or review localizable strings (XLF entries), follow `.github/skills/xlf-strings/SKILL.md`.

The one rule that applies at all times even outside that skill: **only ever edit files under `DistFiles/localization/en/`** — never touch the other language subdirectories.

# Commenting
All public methods should have a comment. So should most private ones!

# Git Committing
Always include a good description when creating a git commit.

# Issue tracker
This project tracks work in **YouTrack**, at https://issues.bloomlibrary.org/youtrack (Kanban
boards). Ticket ids look like **`BL-16572`** (`BL-` plus a number). The skill that talks to it is
**`youtrack-api`** — use it for any tracker operation (read an issue, find the id for the current
work, list/post comments, set an issue's State); the higher-level `youtrack-*` skills build on it.

To find the ticket id for the branch you are on, look for a `BL-XXXXX` token in the branch name,
then the PR title, then recent commit messages. Not every branch has a card — some work (small
cleanups, branding tweaks, tooling) is done without one, so finding no id is a normal outcome, not
a reason to go hunting.

# Skills
Reusable, task-specific procedures for this repo live in `.github/skills/<name>/SKILL.md`.
When a request matches one of these, READ the matching `SKILL.md` and follow it as the
authoritative procedure (it may have more files alongside it). These may not be auto-loaded
for non-copilot agents, so you may have to open the file yourself.

Team-wide workflow skills that are not specific to this repo (the preflight → self-review →
peer-review pipeline, Devin and Reviewable review handling, YouTrack operations) live in
https://github.com/BloomBooks/bloom-team-skills — install per its README (clone + symlink
into `~/.claude/skills`).
