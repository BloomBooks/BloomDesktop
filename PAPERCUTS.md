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
