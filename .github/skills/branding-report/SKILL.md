# branding-report

Render a Bloom book's pages across a matrix of **branding × page × layout × xmatter** by driving
the running Bloom, screenshot each page into a folder, and explore the results in a filterable
viewer (or bake a self-contained HTML report to share).

Use this when asked to "survey the brandings", "show every branding's back cover / title page",
"compare a branding across layouts", or otherwise audit how branding/appearance renders across
Bloom's real xmatter — at full fidelity (real cover color, appearance CSS, generated QR, etc.).

The heavy loop is pure Node driving Bloom's HTTP API + headless-Chrome CDP — **no AI tokens**.

## Prerequisites

1. **Bloom running from source** via `./go.sh` (a Debug build). It must include:
   - the DEBUG-only multi-axis set-state handler on `POST /bloom/api/settings/branding`
     (accepts `{branding,layout,xmatter}` JSON; see `CollectionSettingsApi.cs`), and
   - the update-guard `try/finally` in `Book.cs` (`BringBookUpToDate`) so a failing cell can't
     wedge the book / pop the "two updates at once" dialog.
   Both ship on branch `BL-16370`. If Bloom was already running before those (or the BL-16370
   badge changes in `BookStorage.cs`) were added, **fully restart it** — hot-reload leaves the new
   `BrandingBadgeHtmlByToken` static field null, which NREs every capture (see `README-gotchas.md`).
2. A collection open with at least one book (any book works; branding shows mainly on
   cover/title/credits/back-cover pages).
3. Google Chrome installed (path in `survey.mjs` `CHROME`).

Confirm Bloom is up: `curl http://localhost:8089/bloom/testconnection` → `OK`.

## Axes / parameters (`survey.mjs`)

| Flag | Values | Default |
|---|---|---|
| `--brandings` | comma list, or `all` (every folder under `src/content/branding`) | `Default` |
| `--pages` | `front,title,credits,insideFront,insideBack,back` or `all` | `front,title,credits,back` |
| `--layouts` | e.g. `A5Portrait,Device16x9Landscape` | current (unchanged) |
| `--xmatters` | e.g. `Factory,Device,Kyrgyzstan2020` | current (unchanged) |
| `--book` | path to the book folder | currently-selected / first non-factory book |
| `--out` | output dir | `./branding-report-out` |
| `--base` | Bloom base URL | `http://localhost:8089/bloom` |
| `--settle-ms`, `--load-ms` | tuning | 400 / 1500 |

Loop order is `layout → xmatter → branding` (one re-hydrate per combo) × `pages` (many
screenshots per preview load). Cost ≈ ~2–3s per branding/layout/xmatter combo; extra pages are
sub-second. ~70 brandings × 1 layout × a few pages ≈ 4–5 min.

## Two ways to use it

- **Interactive control panel (`control.mjs`)** — recommended for exploring. A live web UI:
  check brandings down the left and they render *now*; layouts and pages are chosen globally
  (top-right) and apply to all checked brandings. Turning a new layout/page ON re-runs the
  already-checked brandings to fill the gap; turning one OFF just hides it (no re-render).
  Captures stream into a matrix (the default view) as a single serial queue drives the one
  running Bloom. It shares the capture engine and writes the same `manifest.json`, so the batch
  viewer / `build-report.mjs` work on the same folder.
- **Batch survey (`survey.mjs`)** — one command captures a whole axis matrix headless, then
  explore with the read-only viewer (`serve.mjs`) or bake a report (`build-report.mjs`).

Both share `capture-core.mjs` (one warm headless Chrome + the selected book, restored on exit).

### Interactive

```bash
cd .github/skills/branding-report
node control.mjs --out branding-report-out      # -> http://localhost:8798/
# open the printed URL; check brandings, pick layouts/pages. Ctrl-C restores the book.
```

### Batch

```bash
cd .github/skills/branding-report

# 1) Capture (0 tokens). Examples:
node survey.mjs --brandings all --pages back
node survey.mjs --brandings Default,WorldVision,Kyrgyzstan2020 --pages back,title,credits
node survey.mjs --brandings Default --pages back --layouts A5Portrait,Device16x9Landscape
node survey.mjs --brandings Default,Kyrgyzstan2020 --pages back --xmatters Factory,Kyrgyzstan2020

# 2a) DEFAULT next action: open the interactive viewer (matrix / grid / detail,
#     filter along every axis), then open http://localhost:8799/ in the browser.
node serve.mjs branding-report-out        # -> http://localhost:8799/

# 2b) OR bake a shareable, self-contained HTML (data-URI images; good for a Claude Artifact):
node build-report.mjs --out branding-report-out --pages back --group-by branding
```

After capture completes, the default action is to launch the interactive viewer
(`node serve.mjs branding-report-out`) in the background and open its URL
(http://localhost:8799/) in the browser — unless the user asked for the baked HTML
report or a Claude Artifact instead.

`survey.mjs` writes `branding-report-out/<layout>/<xmatter>/<branding>/<page>.png` plus
`manifest.json` (axis metadata per cell, including `ok:false` + `error` for pages that don't
exist under a given branding/xmatter — e.g. Kyrgyzstan's slots on a non-Kyrgyzstan xmatter).

It restores the book's original branding (and layout/xmatter, if those were varied) at the end.
The subscription override is in-memory only — never persisted — so your real subscription is intact
after a Bloom restart regardless.

## Caveats

- **The book's layout drives geometry.** On a Device16x9 book, badges render landscape; portrait
  books differ. Vary `--layouts` or use a portrait book to see both.
- **xmatter-coupled brandings** (e.g. Kyrgyzstan2020) place content in slots that only exist in
  their own xmatter; on a foreign xmatter their pages look empty. **But** `--xmatters` can only
  apply packs the collection actually offers (see `GET /bloom/api/settings/xmatter` →
  `xmatterOfferings`: typically Factory, Device, Traditional, SuperPaperSaver, SIL-PNG).
  Project-specific packs like `Kyrgyzstan2020` aren't offered and silently fall back — to survey
  such a branding faithfully, open a collection that provides its xmatter.
- **Local-Community** needs personalization; `survey.mjs` sends it a `Sample-Community-LC`
  descriptor automatically.
- Full self-contained reports can't hold thousands of images — use the **viewer** for big matrices,
  **build-report** for curated subsets.

See `README-gotchas.md` for the hard-won details behind these.
