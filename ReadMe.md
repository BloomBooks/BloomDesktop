# Books for every child, in every language

Bloom is an [award winning](http://allchildrenreading.org/sil-international-wins-enabling-writers-prize-for-software-solution-to-childrens-book-shortage/) software solution to the children's book shortage among most of the world's languages. It is an application for Windows and [Linux](https://bloomlibrary.org/page/create/linux) that dramatically "lowers the bar" for creating, translating, and sharing books. With Bloom, communities can do the work for themselves instead of depending on outsiders. For more information, see [https://bloomlibrary.org/about](https://bloomlibrary.org/about). For user documentation, see [https://docs.bloomlibrary.org](https://docs.bloomlibrary.org).

Internally, Bloom is a hybrid. It started as a C#/WinForms app with an embedded browser for editing documents and an embedded Adobe Acrobat for displaying PDF outputs. It is growing up to be a pure React-driven offline-capable web app, with a C# backend. In its current state, Bloom is hybrid of C#/web app in which the bits of the UI are gradually moving to html.

# Building

1. Install [vite-plus (`vp`)](https://vite.plus) globally on your computer. It reads the `.node-version` and `packageManager` fields in the repo and provides the correct node and pnpm. (We previously used volta, but it does not fully support pnpm.)

2. Install other dependencies:

```bash
./init.sh
```

3. Run a hot-reloading server for the front end and a "watch" run of the back end.
  ```bash
  ./go.sh
  ```

# Developing

### Go Dark

We don't want developer and tester runs (and crashes) polluting our statistics. Add the environment variable "feedback" with value "off".

### Set up formatting

In a terminal, run `dotnet tool restore`. This will install any tools we have put in .config/dotnet-tools.json along with the correct versions.

In Visual Studio, under Extensions, install "CSharpier". The extension's version will not be the same as the CSharpier installed by 'dotnet tool restore', but that's not a problem.

In Tools/Options, under CSharpier:General, set `Reformat with CSharpier on Save` to `true`. Note that you should set it for this solution, not globally.

You should also install the CSharpier extension in VSCode.

CSharpier should be using the version specified in `.config/dotnet-tools.json`.

When upgrading to a new version of CSharpier, to format everything, run `dotnet csharpier format src/BloomExe src/BloomTests src/WebView2PdfMaker`.

To hide reformatting-only commits from git blame, add the sha of the commit to `.git-blame-ignore-revs`.

For Typescript formatting, we use the Prettier extension in VSCode.

### Updating as you edit files

To rebuild on typescript, less, and md changes in BloomBrowserUI, use `pnpm watch`.

To rebuild less and other "content" on changes, see the various scripts in `src/content`'s package.json.

For fast hot-reloading, first do one pnpm build, to get all the (so-far) static assets. Then run pnpm dev. Currently only some parts of the Bloom UI benefit from this (the ones implemented using ReactControl, including ReactDialog). You may need to run pnpm watch in another terminal.

It may be helpful before submitting a PR to turn off pnpm dev and run pnpm build, then do a quick smoke test of your work. pnpm build creates the transpiled files that will be used by Bloom in production.

If you have the team's agent skills installed (see the Skills section of AGENTS.md), the `/preflight` skill automates the rest of the pre-review checklist: typecheck, lint, tests, draft PR, and the bot review gauntlet.

### Developing a library alongside Bloom

Bloom uses several libraries that we maintain in their own repositories (they behave like a loose monorepo). By default, `./go.sh` uses each library exactly as installed in `node_modules` (i.e. as last `yarn install`ed / published) — you don't need their source.

When you're also working on one of those libraries, link your local checkout of it with the repeatable `--with` flag so your edits flow into the running Bloom:

```bash
./go.sh --with LIBRARY           # auto-discover a sibling checkout
./go.sh --with LIBRARY=PATH      # or point at an explicit checkout path
./go.sh --with bloom-player --with @sillsdev/config-r   # link more than one
```

`LIBRARY` is one of the package names listed below; `PATH` is a checkout directory. With no path, the checkout is auto-discovered as a sibling of the Bloom repo (e.g. `../bloom-player`, or `../../bloom-player` from a git worktree). `go.sh` starts and stops the library's dev processes for you and tears them down on Ctrl-C; the library's own dependencies must already be installed in its checkout (`pnpm install` or `yarn install` there, as appropriate).

The linkable libraries (by package name) and what `--with` does for each:

- `bloom-ai-image-tools` —  `--with` runs the library's own Vite dev server (`pnpm dev`) and points Bloom at it, giving full HMR inside the editor.
- `bloom-player` — `--with` aliases the package to your checkout and runs its watch-builds (both the imported library output and the standalone player assets), so your changes appear in Bloom's UI on reload.
- `@sillsdev/config-r` — `--with` aliases it to your checkout and runs its watch-build (`yarn build:dev`).

The list of linkable libraries lives in `src/BloomBrowserUI/scripts/devLibraries.mjs`; add an entry there to make another library linkable.

### Windows Defender exclusions

For performance reasons, you probably want to exclude at least the following in the Windows Defender settings:

- node.exe (process)
- Bloom source code folder (e.g. `C:/dev/BloomDesktop`)

### Typescript unit tests

These are now being run using Vitest in the BloomBrowserUI folder (where all our Typescript currently lives). You can run 'pnpm test' in a terminal there, and it will automatically re-run affected tests on every Save. There is also a vitest extension you can install in VsCode, which supports a new panel showing all the tests and allowing them to be run and debugged; it also puts icons in the test files that support running and debugging tests. Breakpoints can be set in VSCode itself. It takes a few seconds for a debug session to start.

For now, all tests are being run using Node and JsDom. This approach has limitations; JsDom's emulation of the browser DOM is imperfect. In particular, you can't do much with a Canvas, and you can't get layout measurements. The file vitest.setup.ts contains various mocks to make jsdom work a little better. Eventually, we hope to be able to run a subset of tests using a real browser.

# Other Info

### Run more than one copy of Bloom

Hold down CTRL as you launch Bloom.

### Kanban / Bug Reports

We use [YouTrack](https://issues.bloomlibrary.org) Kanban boards. Errors (via email or api) also flow into YouTrack, and we do some support from there by @mentioning users.

### Continuous Build System

Each time code is checked in, an automatic build begins on our [TeamCity build server](https://build.palaso.org/project/Bloom), running all the unit tests. Similarly, when there is a new version of some Bloom dependency (e.g. `bloom-player`), that server automatically rebuilds and publishes Bloom Alpha. Public channels are released by pressing a button on the TeamCity page. This "publish" process builds Bloom, runs tests, makes an installer, uploads it to S3, and writes out a little bit of json which the [Bloom download page](http://bloomlibrary.org/downloads) uses to display version-specific information to the user.

### Linux Development

5.4 is the last version for Linux until we get rid of WinForms.

See the `Version5.4` branch and ReadMe if you need to update it.

See [this page](https://bloomlibrary.org/page/create/linux) for how to run it.

### Localization

UI localization happens on [Crowdin](https://crowdin.com/project/sil-bloom).

### Registry settings

One responsibility of Bloom desktop is to handle url's starting with "bloom://"", such as those used on bloomlibrary.org when the user clicks "Translate into *your* language!" Making this work requires some registry settings. These are automatically created when you run Bloom. If you have multiple versions installed, just make sure that the one you ran most recently is the one you want to do the download.

# License

Bloom is open source, using the [MIT License](http://sil.mit-license.org). It is Copyright SIL Global.

"Bloom" is a registered trademark of SIL Global.