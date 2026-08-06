---
name: branding-viewer
description: Open the branding viewer — survey how a Bloom book renders across branding × page × layout × xmatter by driving a running Bloom, and explore the screenshots in a live web UI. Use when asked to "run the branding viewer", "survey the brandings", "show every branding's back cover", "compare a branding across layouts", or to audit how branding/appearance renders across Bloom's real xmatter.
model: sonnet
---

# Branding viewer — quick path

**The tool is not in this repo.** It lives at https://github.com/BloomBooks/branding-viewer so a
tester with only a CI-built Bloom can run it. This repo owns the other half of the contract: the
`--e2e` endpoints in `src/BloomExe/web/controllers/E2eTestingApi.cs`.

```
GET  /bloom/api/e2e/surveyOptions   what this build can survey, plus its version
POST /bloom/api/e2e/setState        {branding, layout, xmatter}; omitted fields unchanged
```

If you change either shape, change it in both repos, and remember a tester may be on an older
Bloom than the one you're developing against.

## 1. Launch Bloom with `--e2e`

This is the part that catches people out: **`./go.sh` does not pass `--e2e`**, so a Bloom started
the usual way will not serve these endpoints and the viewer will refuse to start. Build, then
launch the exe directly with a collection argument:

```bash
build/agent-dotnet.sh build src/BloomExe/BloomExe.csproj   # or a plain dotnet build for the apphost
./output/Debug/AnyCPU/Bloom.exe "/path/to/Some Collection/Some Collection.bloomCollection" --e2e --automation
```

`--e2e` is a runtime check, not `#if DEBUG`, so a Release build works too. That is deliberate and
load-bearing for the tester story; don't "tidy" it into a compile-time guard.

**Work on a copy of the collection.** The tool changes the collection's branding and the selected
book's layout, restoring them on Ctrl-C. A hard kill skips that restore. `src/BloomVisualRegressionTests/collections/basic`
is a good throwaway to copy, and it contains both an A5 Portrait and a 16x9 Landscape book.

## 2. Run the viewer

```bash
git clone https://github.com/BloomBooks/branding-viewer   # once
cd branding-viewer
bun control.mjs                                   # interactive panel -> http://localhost:8798/
bun survey.mjs --brandings all --pages back       # batch capture
bun serve.mjs branding-viewer-out                 # read-only viewer  -> http://localhost:8799/
```

Run it in the background, open the printed URL for the user (`Start-Process http://localhost:8798/`),
and report the URL in chat. Flags: `[--out DIR] [--port 8798] [--book PATH] [--base URL]`.

In the panel: check brandings down the left and they render now; layouts and pages are chosen
globally and apply to every checked branding. Turning a new layout or page on backfills the
already-checked brandings; turning one off just hides it.

Testers use the prebuilt exe from the repo's releases page instead of cloning, and need Chrome or
Edge (the tool drives one over CDP and ships no browser; Edge is on every Windows box).

## 3. Read the results honestly

- **Match the layout to the question.** Device16x9**Portrait** proves nothing about
  Device16x9**Landscape**. Back-cover overflow bugs in particular only show on short pages.
- **Know which Bloom you captured against.** The tool talks to whatever answers on port 8089,
  which is often a Bloom from a different worktree than the one you're editing. Check with
  `Get-Process Bloom | Select-Object Path`, and say which build the captures came from.
  `surveyOptions` reports `bloomVersion`, so the manifest can be attributed.
- **A branding rendering "empty" is often xmatter coupling**, not a bug: some brandings put content
  in slots that exist only in their own xmatter pack, and a collection can only offer the packs it
  has. `surveyOptions.xmatterOfferings` tells you what is actually available.

## Behaviour notes

- If the viewer exits saying `surveyOptions` was not answered, the Bloom was launched without
  `--e2e`, or predates the endpoints. There is no way to survey a build older than them.
- `surveyOptions.layouts` is empty until a book is selected, because Bloom derives layout choices
  from the selected book's stylesheets. The tool re-asks after selecting; if you call the endpoint
  by hand, do the same.
- Output folders (`*-out/`) hold the screenshots, `manifest.json`, and a throwaway Chromium
  profile. They are gitignored in the tool's repo.
