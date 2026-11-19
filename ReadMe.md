# Books for every child, in every language

Bloom is an [award-winning](https://allchildrenreading.org/sil-international-wins-enabling-writers-prize-for-software-solution-to-childrens-book-shortage/) software solution to the children's book shortage among most of the world's languages. It is a Windows application (an older version, 5.4, [runs on Linux](https://bloomlibrary.org/page/create/linux)) that dramatically "lowers the bar" for creating, translating, and sharing books. With Bloom, communities can do the work for themselves instead of depending on outsiders. For more information, see [https://bloomlibrary.org/about](https://bloomlibrary.org/about). For user documentation, see [https://docs.bloomlibrary.org](https://docs.bloomlibrary.org).

Internally, Bloom is a C# app hosting a React/TypeScript front end in WebView2. It started as a WinForms app, and its UI is gradually moving to the web side.

[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/bloombooks/bloomdesktop)

# Building

1. Install [vite-plus (`vp`)](https://vite.plus) globally on your computer. It reads the repo's `.node-version` file and the `packageManager` field in `src/BloomBrowserUI/package.json`, and provides the correct node and pnpm.

2. Install other dependencies:

```bash
./init.sh
```

`init.sh` also does the first front-end build.

3. Run a hot-reloading server for the front end and a "watch" run of the back end, and launch Bloom:
  ```bash
  ./go.sh
  ```

# Developing

### Go Dark

We don't want developer and tester runs (and crashes) polluting our statistics. Set the environment variable `FEEDBACK` to `off`.

### Set up formatting

In a terminal, run `dotnet tool restore`. This will install any tools we have put in .config/dotnet-tools.json along with the correct versions.

In Visual Studio, under Extensions, install "CSharpier". The extension's version will not be the same as the CSharpier installed by 'dotnet tool restore', but that's not a problem.

In Tools/Options, under CSharpier:General, set `Reformat with CSharpier on Save` to `true`. Note that you should set it for this solution, not globally.

You should also install the CSharpier extension in VS Code.

CSharpier should be using the version specified in `.config/dotnet-tools.json`.

When upgrading to a new version of CSharpier, to format everything, run `dotnet csharpier format src`.

To hide reformatting-only commits from git blame, add the sha of the commit to `.git-blame-ignore-revs`.

The pre-commit hook (installed by `init.sh`) runs CSharpier and Prettier on staged files.

If using VS Code, you can also use the Prettier extension.

### Updating as you edit files

`./go.sh` handles this: it runs the Vite dev server and file watchers for the front end, and `dotnet watch` for the C#. (`--nowatch` skips watching the C#.)

A few entry points don't work with the Vite dev server yet. If an edit doesn't show up, also run `pnpm watch` in `src/BloomBrowserUI`.

Before submitting a PR, consider stopping `./go.sh`, running `pnpm build` in `src/BloomBrowserUI`, and smoke-testing in a Bloom launched without the dev server (e.g. from Visual Studio). That build produces the bundles production uses.

If you have the team's agent skills installed (see the Skills section of AGENTS.md), the `/preflight` skill automates the rest of the pre-review checklist: typecheck, lint, tests, draft PR, and the bot review gauntlet.

### Developing a library alongside Bloom

Bloom uses several libraries that we maintain in their own repositories (they behave like a loose monorepo). By default, `./go.sh` uses each library exactly as installed in `node_modules` (i.e. as last `pnpm install`ed / published) — you don't need their source.

When you're also working on one of those libraries, link your local checkout of it with the repeatable `--with` flag so your edits flow into the running Bloom:

```bash
./go.sh --with LIBRARY           # auto-discover a sibling checkout
./go.sh --with LIBRARY=PATH      # or point at an explicit checkout path
./go.sh --with bloom-player --with @sillsdev/config-r   # link more than one
```

`LIBRARY` is one of the package names listed below; `PATH` is a checkout directory. With no path, the checkout is auto-discovered as a sibling of the Bloom repo (e.g. `../bloom-player`, or `../../bloom-player` from a git worktree). `go.sh` starts and stops the library's dev processes for you and tears them down on Ctrl-C; the library's own dependencies must already be installed in its checkout (run `pnpm install` there).

The linkable libraries (by package name) and what `--with` does for each:

- `bloom-ai-image-tools` —  `--with` runs the library's own Vite dev server (`pnpm dev`) and points Bloom at it, giving full HMR inside the editor.
- `bloom-image-gallery` — `--with` aliases the package to your checkout. Bloom compiles its source directly, so no watch-build is needed.
- `bloom-player` — `--with` aliases the package to your checkout and runs its watch-builds (both the imported library output and the standalone player assets), so your changes appear in Bloom's UI on reload.
- `@sillsdev/config-r` — `--with` aliases it to your checkout and runs its watch-build (`pnpm run build:dev`).

The list of linkable libraries lives in `src/BloomBrowserUI/scripts/devLibraries.mjs`; add an entry there to make another library linkable.

### Windows Defender exclusions

For performance reasons, you probably want to exclude at least the following in the Windows Defender settings:

- node.exe (process)
- Bloom source code folder (e.g. `C:/dev/BloomDesktop`)

### TypeScript unit tests

These are run using Vitest in the BloomBrowserUI folder (where all our TypeScript currently lives). In a terminal there, `pnpm test` runs them once, and `pnpm test:watch` re-runs affected tests on every save. There is also a Vitest extension you can install in VS Code, which supports a new panel showing all the tests and allowing them to be run and debugged; it also puts icons in the test files that support running and debugging tests. Breakpoints can be set in VS Code itself. It takes a few seconds for a debug session to start.

Most tests run using Node and jsdom. This approach has limitations; jsdom's emulation of the browser DOM is imperfect. In particular, you can't do much with a Canvas, and you can't get layout measurements. The file `vitest.setup.ts` contains various mocks to make jsdom work a little better. Tests that need real layout go in `*.browser.spec.ts` files, which run in headless Edge.

### Other tests

- C# unit tests: see `src/BloomTests/AGENTS.md`.
- End-to-end tests: see `src/BloomE2E/README.md`.

# Other Info

### Run more than one copy of Bloom

Hold down CTRL as you launch Bloom.

### Kanban / Bug Reports

We use [YouTrack](https://issues.bloomlibrary.org) Kanban boards. Errors (via email or api) also flow into YouTrack, and we do some support from there by @mentioning users.

### Continuous Build System

Each time code is checked in, an automatic build begins on our [TeamCity build server](https://build.palaso.org/project/Bloom), running all the unit tests. Similarly, when there is a new version of some Bloom dependency (e.g. `bloom-player`), that server automatically rebuilds and publishes Bloom Alpha. Public channels are released by pressing a button on the TeamCity page. This "publish" process builds Bloom, runs tests, makes an installer, uploads it to S3, and writes out a little bit of json which the [Bloom download page](https://bloomlibrary.org/downloads) uses to display version-specific information to the user.

A nightly GitHub Actions workflow (`.github/workflows/nightly.yml`) runs the longer test suites, including end-to-end tests.

### Linux Development

5.4 is the last version for Linux until we get rid of WinForms.

See the `Version5.4` branch and ReadMe if you need to update it.

See [this page](https://bloomlibrary.org/page/create/linux) for how to run it.

### Localization

UI localization happens on [Crowdin](https://crowdin.com/project/sil-bloom). See `DistFiles/localization/README.md`.

### Registry settings

One responsibility of Bloom desktop is to handle URLs starting with "bloom://", such as those used on bloomlibrary.org when the user clicks "Translate into *your* language!" Bloom writes the needed registry settings each time it runs, so if you have several versions installed, the one you ran last handles these links.

# License

Bloom is open source, using the [MIT License](https://sil.mit-license.org). It is Copyright SIL Global.

"Bloom" is a registered trademark of SIL Global.