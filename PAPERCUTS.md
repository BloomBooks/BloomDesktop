# Papercuts

Small dev/agent/tooling friction points for this repo. See the `papercut` skill for format.

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
