---
name: rab-icon-grid-processing
description: 'Process an incoming grid or batch of icon images into usable Reading App Builder icons. Use when RAB icon assets arrive as a grid, sprite sheet, nested folders, or padded PNGs that need splitting, cropping, repadding, flattening, compatibility updates, and sample-first validation.'
argument-hint: 'incoming icon source, target subset, and whether to do a sample pass or full batch'
user-invocable: true
---

# RAB Icon Grid Processing

## Outcome
Turn incoming icon artwork into usable Reading App Builder icon assets that are visually legible at app-icon size, stored in the layout Bloom expects, and validated before broad rollout.

## When To Use
- You receive a grid, sprite sheet, or batch of candidate icon images for Reading App Builder.
- The icons are technically valid PNGs but look too small because of excessive transparent padding.
- The icons are stored one-per-folder and need to be flattened into a single bundled icon directory.
- Bloom's RAB icon discovery code needs to be updated to match an asset-layout change.
- You want to try one or two sample icons first before touching the entire set.

## Default Assumptions
- Target icons should remain square PNGs with transparent backgrounds.
- A 512x512 master icon is the safest working size when repadding or regenerating icon art.
- Unless the user says otherwise, name outputs `bloom-app-icon-<number>`.
- In this repo, bundled App Builder icons live under `DistFiles/appbuilder-icons`.
- If the asset layout changes, update both the backend discovery path and the tests in the same pass.
- Prefer process tasks or direct scripts over bash-heavy image tooling if the shell environment is unstable.

## Inputs To Confirm
- What is the incoming source: a grid image, sprite sheet, flat PNG batch, or folder-per-icon bundle?
- Is the task visual-only, layout-only, or both?
- Should the agent run a one-or-two-icon pilot first, or has the user already approved a full batch?
- Are filenames already the desired final IDs, or do they need renaming?
- Does Bloom code currently assume the old bundled layout?
- What is the narrowest validation available after the change?

## Core Workflow
1. Inspect the incoming asset set.
   Determine whether the source is a single grid, a sprite sheet, flat PNG files, or a folder-per-icon layout.
2. Identify the controlling problem before editing.
   Decide whether the main issue is splitting, transparent padding, directory layout, naming, or code compatibility.
3. Start with a representative sample.
   Pick one or two icons that clearly show the problem and use them as a reversible pilot.
4. If the icons are already individual files but too padded, crop to visible alpha bounds.
   Measure visible pixels using alpha, trim the transparent border, then redraw onto a square transparent canvas with a consistent margin.
5. If the assets are stored one-per-folder, flatten them only after checking for filename collisions.
   Move the PNGs into the target root, remove emptied folders, and keep the filename-based identity stable.
6. If the source is a grid or sprite sheet, split it into individual transparent PNGs before any crop-and-repad pass.
   Use the grid cell geometry or visible bounds consistently across the full batch.
7. Validate the pilot visually before batch rollout.
   Open the updated sample icons and confirm they read better without clipping or awkward framing.
8. Only after the pilot succeeds, run the same transformation across the remaining icons.
9. If Bloom code depends on the old asset layout, update the code path and tests in the same change.
10. Finish with the narrowest executable validation available and note any unrelated blockers separately.

## Decision Points

### Already Split vs Grid Source
- If each icon is already a standalone PNG, avoid resampling until after a padding check.
- If the source is a grid or sprite sheet, split first, then evaluate padding and framing on the extracted files.

### Sample First vs Full Batch
- Use a sample-first pass when visual quality is uncertain or the user explicitly wants to review direction.
- Use a full batch only after one or two icons clearly validate the approach.

### Layout Change vs Visual-Only Change
- If only the art framing changes, keep the code path alone unless filenames or directories also change.
- If the directory layout changes, make the backend and tests accept the new layout and, when practical, preserve legacy compatibility during the transition.

### Tool Choice
- If a repo-local script already solves the problem, reuse or adapt it.
- If external image tools are missing or unreliable, use a simple checked-in scriptable path such as PowerShell plus `System.Drawing` for alpha-bound cropping.
- If bash terminals are unreliable, prefer process tasks or non-bash execution paths.

## Batch Rollout Safeguards
- Do not batch-transform every icon until at least one representative sample has been visually checked.
- Keep the crop margin consistent across the batch unless a specific icon family clearly needs a different rule.
- Fail on duplicate destination filenames instead of silently overwriting files.
- If the batch changes asset layout, keep the code compatible with both layouts during the migration when practical.
- Separate unrelated validation failures from icon-work regressions; do not weaken the workflow just to get a green run.

## What Good Looks Like
- The icon reads clearly even at small app-icon sizes.
- The artwork fills most of the square without feeling cramped.
- The baseline and centering feel intentional instead of accidental.
- The whole batch feels visually consistent when shown together.
- The repo layout and loader logic are simpler after the change, not more ad hoc.

## Implementation Guidance

### Alpha Crop And Repad
- Find the smallest rectangle containing pixels above a low alpha threshold.
- Clone that region.
- Draw it onto a transparent 512x512 canvas.
- Scale proportionally so the art fits inside the canvas with a consistent margin.
- Save through a temporary file and replace the original only after all source handles are disposed.

### File Layout Migration
- Keep the destination flat directory deterministic.
- Fail on naming collisions instead of silently overwriting.
- Remove emptied folders after successful moves.
- If Bloom currently supports the old layout, keep compatibility until the asset migration is complete.

### Bloom Code Updates
- Update the owning RAB icon discovery abstraction instead of patching UI consumers.
- Add or adjust tests for the new layout.
- When transitioning layouts, prefer accepting both the flat root and the legacy folder form until the repo is fully migrated.

## Validation Checklist
- The updated icon is visibly larger and clearer than the original.
- No important artwork is clipped.
- Transparent padding is reduced consistently.
- The output file remains square and transparent.
- Flattened directories contain the expected files and no leftover collisions.
- Backend discovery still finds the icons.
- Focused tests or a narrow compile check were attempted after substantive changes.
- Any failing validation that is unrelated to the icon work is called out explicitly rather than worked around.

## Repo-Specific Notes
- In this repository, the practical workflow is often: sample crop first, visually inspect, then batch the rest.
- For RAB icon layout work, backend discovery and tests live near `src/BloomExe/Publish/Rab` and `src/BloomTests/Publish/Rab`.
- For bundled icons, prefer a flat `DistFiles/appbuilder-icons` directory once the code path supports it.
- If a script rewrites PNGs in place on Windows, use a temp-file replacement flow to avoid file-lock and GDI+ save errors.

## Current Repo Helpers
- `.github/skills/rab-icon-grid-processing/flattenAppBuilderIcons.mjs` is the current helper for moving one-file-per-folder bundled icons into a flat `DistFiles/appbuilder-icons` root.
- `.github/skills/rab-icon-grid-processing/cropAppBuilderIconSamples.ps1` is the current sample-first helper for alpha-bound crop and repad of selected icons.
- `.github/skills/rab-icon-grid-processing/extractAppBuilderIconGridSamples.ps1` is the current sample-first helper for splitting white-background icon grids into numbered `bloom-app-icon-<number>` PNG outputs under `output/copilot-verify`.
- Treat the PowerShell crop script as a targeted pilot tool. If you need to process the whole batch, generalize the path list and rerun the same visual-validation workflow rather than assuming every icon should be cropped identically without review.

## Naming Convention
- Unless the user provides a different mapping, number extracted outputs sequentially as `bloom-app-icon-<number>`.
- Keep numbering deterministic within a batch so later review comments can refer to stable filenames.

## Example Prompts
- `/rab-icon-grid-processing Process this incoming grid into RAB-ready icons and show me two samples before doing the rest.`
- `/rab-icon-grid-processing These App Builder icons are too padded. Crop and repad two examples, then tell me whether the same rule is safe for the batch.`
- `/rab-icon-grid-processing Flatten these bundled icon folders into DistFiles/appbuilder-icons and update Bloom's RAB discovery if needed.`
- `/rab-icon-grid-processing Take this sprite sheet of candidate icons, split it, normalize the outputs, and validate that Bloom still discovers them.`

## Completion Criteria
- The user can inspect one or two representative icons and confirm the visual direction.
- The asset layout is in the intended final form for the current step.
- Any required Bloom code and tests have been updated for layout changes.
- The narrowest available validation has been run, or a specific unrelated blocker has been documented.