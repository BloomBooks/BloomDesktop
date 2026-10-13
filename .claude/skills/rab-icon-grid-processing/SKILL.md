---
name: rab-icon-grid-processing
description: Turn incoming Reading App Builder icon artwork (a grid, a sprite sheet, or padded PNGs) into the square, tightly-framed bloom-app-icon-<n>.png files bundled under DistFiles/appbuilder-icons, sample-first. Use when new RAB app icons arrive or existing ones look too small because of transparent padding.
argument-hint: "incoming icon source, target subset, and whether to do a sample pass or full batch"
---

# RAB icon processing

## Outcome
Icon artwork becomes usable Reading App Builder icons: legible at app-icon size, square PNGs with
transparent backgrounds, named `bloom-app-icon-<number>.png`, stored flat under
`DistFiles/appbuilder-icons`, and visually checked on a sample before the whole batch is touched.

## Before you start, settle these with the user
What the source is (a grid image, a sprite sheet, or already-split PNGs); whether the job is
visual only, layout only, or both; whether to pilot on one or two icons first or a full batch is
already approved; whether the filenames are already the final ids or need renaming; and what the
narrowest validation is once the icons are in place. Unstated answers default to: pilot first,
keep existing names, validate with the RAB icon tests.

## Workflow
1. **Inspect the source.** A single grid image, a sprite sheet, or already-split PNGs? Decide
   whether the controlling problem is splitting, transparent padding, or naming.
2. **Pilot on one or two icons** that show the problem clearly, and look at the results before
   touching the rest. Never batch-transform first.
3. **Grid or sprite sheet:** split into individual transparent PNGs using consistent cell
   geometry, then evaluate padding on the extracted files.
4. **Padded PNGs:** find the smallest rectangle of pixels above a low alpha threshold, clone it,
   draw it centred on a transparent 512×512 canvas scaled to leave a consistent margin. Save
   through a temp file and replace the original only after all source handles are disposed
   (avoids GDI+ save and file-lock errors on Windows).
5. **Batch** the rest with the same rule once the pilot reads well. Keep the margin consistent
   across the batch; fail on duplicate destination names instead of overwriting.
6. **Validate**: icons visibly larger and clearer, nothing clipped, still square and transparent,
   and Bloom's discovery still finds them — run the RAB icon tests
   (`build/agent-dotnet.sh test src/BloomTests/BloomTests.csproj --filter GetAvailableIconChoices`).
   Discovery code and tests live near `src/BloomExe/Publish/Rab` and `src/BloomTests/Publish/Rab`.

## Scripts in this folder
- `cropAppBuilderIconSamples.ps1` — alpha-bound crop and repad of selected icons (the pilot
  tool; generalize its path list for a batch).
- `extractAppBuilderIconGridSamples.ps1` — split white-background icon grids into numbered
  `bloom-app-icon-<number>` PNGs.
- `flattenAppBuilderIcons.mjs` — move one-file-per-folder icons into a flat root (the bundled
  set already is flat; kept for a future batch that arrives nested).

Use PowerShell with `System.Drawing` for the image work; it needs nothing installed.

## What good looks like
The icon fills most of the square without feeling cramped, centring and baseline look
intentional, and the batch reads as one consistent set.
