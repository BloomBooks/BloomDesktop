---
name: bird-book
description: Use when building or extending a Bloom "wildlife identifier" book from a spreadsheet of species — parses the sheet, downloads & resizes photos, generates bird pages, and (critically) embeds copyright/license/photographer metadata into the image files so Bloom keeps it.
argument-hint: "species spreadsheet (.xlsx) path and target Bloom.htm path"
---

# Bird / wildlife identifier book builder

Reproducible pipeline that turns a species spreadsheet into bird pages inside a Bloom
book ("identifier" layout: photo on the left, name/size/abundance/habitat/food card on
the right, two species per page). Built originally for
`Identification de la faune` from `solomon_islands_birds.xlsx`.

Pairs with the [edit-bloom-book](../edit-bloom-book/SKILL.md) skill, which documents the
Bloom HTML schema and ships the structural validator used below.

## ⚠️ The two things that bite you

1. **Image copyright/license/photographer must live in the image FILES, not just the
   HTML.** Bloom treats each image file's embedded metadata (XMP/EXIF) as the source of
   truth. On load/save it runs `ImageUpdater.UpdateImgMetadataAttributesToMatchImage()`,
   which reads metadata from the file and **rewrites** the `<img>` `data-copyright` /
   `data-creator` / `data-license` attributes from it — *removing* them when the file
   has none. So freshly downloaded photos show the red "?©" badge and your HTML
   attributes get blanked on the next save. Fix: embed metadata into the files with
   Bloom's own libpalaso `Metadata` API (see `embed-metadata/`).

2. **Close Bloom before editing the book on disk.** Bloom owns the open book file and
   re-saves it from its in-memory copy, silently clobbering external edits (and
   re-stripping image attributes). Every "my change vanished" symptom traces back to
   Bloom being open. Edit only while Bloom is closed, then open it.

## Field mapping (spreadsheet → page)

Spreadsheet columns A–K: French Name, English Name, Latin Name, Size, Abundance,
Habitats, Food, Photo URL, License, Photographer, Copyright. They map to a species card:

| Card field (style class)        | Source column        | Notes |
|---------------------------------|----------------------|-------|
| `Name-style` (lang=fr, big)     | A French Name        | vernacular/V |
| `normal-style` #1 (lang=en)     | B English Name       | |
| `LatinName-style` (lang=en)     | C Latin Name         | |
| `normal-style` #2 (lang=en)     | D Size               | |
| `abundance-style` (lang=en)     | E Abundance          | |
| `Habitat-style` (lang=en)       | F Habitats           | letter codes; rendered by the "BirdGuide Icons" font |
| `Food-style` (lang=en)          | G Food               | letter codes; same font |
| `<img>` data-copyright/creator/license | K / J / I    | also embedded into the file |

Images use **cover/fill** positioning (like Bloom's "expand"): the
`.bloom-canvas-element.bloom-backgroundImage` is sized to the full container and the
`<img>` gets an inline `width` + negative `top`/`left` so it fills and crops.

## Prerequisites

- Node.js (uses `jsdom` from `src/BloomBrowserUI/node_modules`, same as the validator).
- Windows PowerShell or pwsh with `System.Drawing` (for download + resize).
- .NET SDK (net8.0) and Bloom's built output (`output/Debug/AnyCPU`) for the metadata tool.
- A pristine copy of the target book whose single custom content page acts as the page
  **template** to clone (the builder reads it, then deletes it).

> Scripts in `scripts/` carry the absolute paths used in the original run as constants /
> defaults near the top. Edit those (book folder, spreadsheet, temp paths) for a new book.

## Pipeline

```
.xlsx ──parse-spreadsheet.mjs──▶ birds.json
birds.json ──download-images.ps1──▶ bNNN.jpg in book folder + manifest.json
birds.json + manifest.json + template book ──build-book.mjs──▶ Bloom.htm (pages added)
birds.json + manifest.json ──BloomMeta (C#)──▶ metadata embedded in bNNN.jpg + meta_results.json
meta_results.json ──patch-html-attributes.mjs──▶ Bloom.htm data-* attrs (optional; matches files)
validateBloomBook.mjs ──▶ confirm structure
```

### 1. Parse the spreadsheet
`.xlsx` is a zip; this reads `sharedStrings.xml` + `sheet1.xml` directly (no Excel libs).
```
node scripts/parse-spreadsheet.mjs        # writes birds.json; prints license/URL audit
```
Image filename convention: row N (data rows start at row 2) → `b{N-1:000}.jpg`
(row 2 → `b001.jpg`).

### 2. Download + resize photos  (Bloom CLOSED not required here)
```
pwsh scripts/download-images.ps1 -Book "<book folder>" -BirdsJson "<birds.json>" -ManifestOut "<manifest.json>"
```
- Resizes to ≤1200 px long edge (no upscaling) — ~300 DPI for these quarter-A5
  containers (Bloom's own tooltip: ~1088 px fills them at 300 DPI). Re-encodes JPEG q85.
- **Throttled (2.5 s) + 429 backoff + resumable** (skips files already on disk). Re-run
  until the manifest shows everything saved. Use a real, contactable User-Agent.

### 3. Build the bird pages  (Bloom CLOSED)
```
node scripts/build-book.mjs               # clones template page, deletes it, inserts ~2-per-page
```
- Clones the book's known-good custom content page via jsdom, fills each card, applies
  cover-fit image styles, alternates side-right/left + page numbers.
- Splices new page HTML textually so the rest of the file stays byte-identical.
- Odd species count → last page's bottom slot is left empty (placeholder image).

### 4. Embed metadata into the image files  (the real copyright fix)
Build the tool and run it from Bloom's output folder so deps resolve:
```
dotnet build embed-metadata/bloommeta.csproj -c Release
copy bin\Release\bloommeta.dll + bloommeta.runtimeconfig.json  ->  output\Debug\AnyCPU\
cd output\Debug\AnyCPU
dotnet bloommeta.dll "<book folder>" "<birds.json>" "<manifest.json>" "<meta_results.json>" --test   # sanity
dotnet bloommeta.dll "<book folder>" "<birds.json>" "<manifest.json>" "<meta_results.json>"           # all
```
Uses libpalaso `SIL.Windows.Forms.ClearShare.Metadata` →
`WriteIntellectualPropertyOnly(path)`. Verified round-trip: Bloom reads back the
copyright, creator, and license token (`cc-by-sa`, etc.). **Note:** `data-license`
stores the license *family* only — version (3.0 vs 4.0) is not encoded. GFDL / blank
licenses become a custom license.

### 5. (Optional) Patch HTML attributes to match
Since Bloom repopulates attributes from the files on open, this is optional, but makes
the HTML correct immediately (e.g. for preview/publish before Bloom touches it):
```
node scripts/patch-html-attributes.mjs    # sets data-* on each bNNN.jpg <img> from meta_results.json
```

### 6. Validate
```
node ../edit-bloom-book/validateBloomBook.mjs "<book>/<Book>.htm"
```

## Files

- `scripts/parse-spreadsheet.mjs` — `.xlsx` → `birds.json` (+ license/URL audit).
- `scripts/download-images.ps1` — throttled, resumable photo download + resize → `bNNN.jpg` + manifest.
- `scripts/build-book.mjs` — generate/insert bird pages from a cloned template page.
- `scripts/patch-html-attributes.mjs` — write `data-copyright/creator/license` into `<img>` tags.
- `embed-metadata/BloomMeta.cs.txt` + `bloommeta.csproj` — net8 tool embedding IP into image files via Bloom's API. (Source kept as `.cs.txt` so Bloom's C# pre-commit hooks skip this non-product helper; the csproj compiles it explicitly.)
- `sample-data/birds.json`, `manifest.json`, `meta_results.json` — example artifacts from the Solomon Islands birds run (77 species; row 75 had no photo URL).
