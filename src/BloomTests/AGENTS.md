# Building and testing the C# code

Also read the `AGENTS.md` at the root of this repository.

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
- **Tests retire their BloomServers, they do not dispose them.** A fixture that made a server
  listen calls `RetiredTestServers.Retire(server)`; the listener is closed a few fixtures later,
  once whatever it was serving has certainly finished. Disposing on the spot is what used to kill
  the test host (BL-16667). If you add a fixture that calls `EnsureListening`, retire it the same
  way rather than calling `Dispose`.
- This wrapper is for **building and running tests only**. To *run* Bloom, still use
  `./go.sh` (see "Running Bloom" in the root `AGENTS.md`) — the wrapper builds no `Bloom.exe` apphost.
  (`BloomPdfMaker.exe` is the one apphost it does build, because Bloom's PDF code shells
  out to that file by name and the PDF tests fail without it.)
- The full C# suite is expected to be **green** through this wrapper. If you see the
  PdfMaker or xmatter-locating tests fail, that is a real regression in the wrapper /
  `Directory.Build.props`, not the known environment noise it used to be.
- The first build in a fresh terminal is a full (cold) build into that terminal's private
  tree; subsequent builds there are incremental. `output/` is gitignored.
- Never pass `--no-build` to `dotnet test`. Always let dotnet build the test project first so
  the tests run against the latest code. A stale DLL can cause tests to pass or fail against an
  old version of the code, hiding real regressions.

## Temp folders are isolated per test run too

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

## Don't assume the tests are running in English

**The tests must not assume English any more than the production code may** (see "Don't assume
the machine is running in English" in the root `AGENTS.md`). A test asserting `"1.5 MB"` fails in
French for a reason that has nothing to do with the code under test. Either assert culture-agnostically (see how
`LicenseCheckerTests` uses `CurrentCulture.TextInfo.ListSeparator`), or — where the production
string really should be invariant, as log lines should be — fix the production code and leave the
test asserting the period.

The weekly `.github/workflows/culture-sweep.yml` already runs the whole suite under `fr-FR` and
`tr-TR`, so **do not run under another culture as a matter of routine** — it costs a full build and
a full run for nothing new. Do it only when your change parses or formats numbers, dates, or casing,
or when reproducing a sweep failure locally. `src/BloomTests/TestCulture.cs` makes it one environment
variable, and does nothing when it is unset:

```bash
BLOOM_TEST_CULTURE=fr-FR build/agent-dotnet.sh test src/BloomTests/BloomTests.csproj
```

The sweep is a CI matrix dimension rather than an NUnit category: it re-runs the *same* tests in a
different environment rather than adding new ones.