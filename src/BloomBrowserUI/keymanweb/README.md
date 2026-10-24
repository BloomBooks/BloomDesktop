# Vendored KeymanWeb engine (18.0.249)

This folder contains the KeymanWeb (KMW) browser engine, vendored directly so
Bloom works fully offline (no CDN calls — see the root `AGENTS.md`/
`src/BloomBrowserUI/AGENTS.md` rule "Never use CDNs. This is an offline app.").
It backs `src/BloomBrowserUI/bookEdit/js/keymanWebIntegration.ts`.

See `Design/Keyboards/keyboard-setting-plan.md` (item 5) and
`Design/Keyboards/implementation-handoff.md` for the feature this supports.

## Version

**18.0.249** — the version the original POC (commit `af6f479a2`) pinned from
the Keyman CDN, verified live again on 2026-07-09.

## Files and where they came from

All fetched with `curl` from the Keyman CDN on 2026-07-09 (each verified to be
a real, non-trivial payload — not a 404 HTML page):

| File | Source URL | Size |
|---|---|---|
| `keymanweb.js` | `https://s.keyman.com/kmw/engine/18.0.249/keymanweb.js` | 429,225 bytes |
| `osk/keymanweb-osk.ttf` | `https://s.keyman.com/kmw/engine/18.0.249/osk/keymanweb-osk.ttf` | 35,408 bytes |
| `osk/kmwosk.css` | `https://s.keyman.com/kmw/engine/18.0.249/osk/kmwosk.css` | 29,074 bytes |
| `osk/globe-hint.css` | `https://s.keyman.com/kmw/engine/18.0.249/osk/globe-hint.css` | 1,132 bytes |
| `LICENSE` | `https://github.com/keymanapp/keyman/blob/master/LICENSE.md` (MIT) | — |

All files are used unmodified from the CDN.

### How the file set was determined

`keyman.init({ root, resources, fonts })` all point at this folder (see the
comments in `keymanWebIntegration.ts` explaining why all three must be set).
Rather than guess, the engine's own minified source
(`keymanweb.js`) was searched for every resource path it fetches at runtime
relative to that root:

- `${resources}/osk/keymanweb-osk.ttf` — the on-screen-keyboard's special font
  (`specialFont: { family: "SpecialOSK", files: [...] }`).
- `${resources}/osk/kmwosk.css` and `${resources}/osk/globe-hint.css` — via
  `OSKView.STYLESHEET_FILES = ["kmwosk.css", "globe-hint.css"]`, loaded from
  `osk/` under the resources root. Neither stylesheet references any further
  `@font-face`/image/etc. resources, so the set is closed.

**Deliberately NOT vendored** (confirmed by grepping `keymanweb.js` for any
reference to them — there is none, so they are dead weight for Bloom's usage):

- `kmwuitoggle.js` / `kmwuibutton.js` — alternate embedded "language toggle
  button" UI widgets for the CDN's `attachType: "auto"` mode. Bloom always
  uses `attachType: "manual"` and drives the OSK itself
  (`attachKeymanWebIfNeeded`), so these are never invoked.
- `keymanweb.js.map` — a ~2 MB sourcemap (bigger than the engine itself). Only
  useful if a developer opens browser devtools on this exact file; not needed
  to run the engine. If you need to debug into the engine internals, it's a
  single `curl` away from the URL above with `.map` appended.

Individual keyboard files (e.g. a specific language's `.js` keyboard stub) are
**not** part of this vendored set — those are fetched/cached per-collection at
runtime by the C# side (see plan items 3–4), matching KMW's normal lazy-load
model (`addKeyboards({ id, filename, languages })` with a local `filename`
pointing at the cached copy).

## Keeping the files byte-identical to upstream

Two guard rails exist so local tooling cannot silently modify these files
(the pre-commit hook's prettier pass mangled the minified engine the first
time this folder was committed):

- `src/BloomBrowserUI/.prettierignore` lists `keymanweb`.
- `.gitattributes` (repo root) marks `src/BloomBrowserUI/keymanweb/**`
  as `-text -whitespace` so git never converts line endings.

If you add files here, make sure they stay covered by both.

## Update procedure

1. Pick the new engine version `X.Y.ZZZ` (check
   `https://s.keyman.com/kmw/engine/` or a `keymanapp/keyman` release for the
   current stable KMW version).
2. Re-fetch the same four files from
   `https://s.keyman.com/kmw/engine/X.Y.ZZZ/...` (same relative paths as the
   table above), overwriting the files in this folder.
3. Re-run the "how the file set was determined" search above (grep the new
   `keymanweb.js` for resource paths under `osk/`, `ui/`, etc.) in case the new
   version added/removed lazily-fetched files — don't assume the set is
   unchanged across versions.
4. Update the version number in this README, `keymanWebIntegration.ts`'s
   version comment, and the file-size table.
5. Re-verify the OSK, keyboard attach/detach, and `HasLoaded` polling still
   work manually (see `keymanWebIntegration.ts`'s comments for why that
   polling exists) — a version bump can change internal timing or event
   names.
6. Do **not** run `yarn build` as an agent; ask a developer to build (or let
   their running dev/watch process pick the new files up — see "Dev-mode
   serving" below) so the change can be manually verified before commit.

## Dev-mode serving (how this folder reaches `output/browser`)

`output/browser` is what BloomServer actually serves to the WebView2 edit
view (`/bloom/keymanweb/...` — see `LocalHostPathToFilePath` /
`ToLocalhost()` in the C# `BloomServer`). This folder under
`src/BloomBrowserUI/keymanweb/` is only the *source*; something has to copy it
into `output/browser/keymanweb/`.

There are two copy mechanisms in this repo, and **both already handle this
folder without any change** — verified by reading (not running) the scripts:

- **`yarn dev`** (`scripts/dev.mjs`) — NOT just a Vite dev server. On startup,
  `runInitialBuilds()` globs `**/*.*` under `src/BloomBrowserUI` and copies
  every matching file to `output/browser` via `copyStaticFile.mjs`, and then
  spawns an `onchange`-based watcher that keeps re-copying any file that
  changes for as long as `yarn dev` keeps running. `copyStaticFile.mjs`
  excludes only `.ts .tsx .less .pug .md .bat` — so `keymanweb.js`,
  `osk/keymanweb-osk.ttf`, and `osk/*.css` all get copied; only this
  `README.md` is (correctly) skipped. `LICENSE` (no extension) is not
  excluded, so it gets copied too — harmless, it's just never referenced by
  the running app.
- **`yarn watch`** (`vite build --watch`) — this is a real build (`command ===
  "build"` in `vite.config.mts`), so vite's `viteStaticCopy` plugin
  (`vite.config.mts` ~604–648, the "structured: true" target that copies
  `**/*.*` except `.ts/.tsx/.pug/.md/.less/.bat`) runs on it too, same
  exclusion behavior as above.

**Conclusion — corrects an assumption in the design docs**: the plan
(`keyboard-setting-plan.md`, `implementation-handoff.md`) says a developer
must run one full `yarn build` to seed `output/browser/keymanweb/`. Based on
reading `scripts/dev.mjs` and `vite.config.mts`, that should NOT be necessary:
whichever of `yarn dev` or `yarn watch` is already running as the normal dev
loop will pick up this new folder on its own (either via `dev.mjs`'s own
static-file watcher, or via vite's build-mode static-copy plugin). If a
developer's watcher process was already running *before* this folder was
added, the safest thing is to restart that one process (`yarn dev` or `yarn
watch`) once so its *initial* full-tree copy/build runs against the new
files — after that, its live watcher keeps picking up any further edits here
automatically. **I have not run either command myself** (repo rules prohibit
`yarn build`, and I did not want to risk colliding with a watch process that
may already be running) — this is a read-of-the-scripts conclusion, not an
empirically-tested one, and it should be confirmed by the next person who
runs the full manual verification pass in the plan's "Verification" section.
