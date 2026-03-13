---
name: bloom-canvas-e2e-testing
description: build and run automated Playwright end-to-end tests for Canvas Tool behavior on CURRENTPAGE.
---

## Scope
Use this skill when the user wants automated Playwright tests (not manual devtools reproduction) for Canvas Tool behavior.

This skill is for:
- creating and maintaining `bookEdit/canvas-e2e-tests` tests
- verifying drag/drop and canvas interactions with real mouse gestures
- running focused Canvas E2E checks against Bloom Edit Tab

This skill is not for:
- manual-only reproduction (use the manual canvas tool testing skill)
- component-harness tests under `react_components/*/*.uitest.ts`

## Required context
- Bloom is running and serving Edit Tab at `http://localhost:8089/bloom/CURRENTPAGE`
- Current page includes `.bloom-canvas`
- Canvas tool is available in toolbox
- Playwright runtime dependencies are installed in:
  - `src/BloomBrowserUI`

## Primary URL
- `http://localhost:8089/bloom/CURRENTPAGE`

## Runtime and command model
Use the `src/BloomBrowserUI` package and run the canvas suite via the root e2e script.

1) Install once (or when deps change):
- `cd src/BloomBrowserUI`
- `yarn install`

2) Run one canvas test:
- `cd src/BloomBrowserUI`
- `yarn e2e canvas specs/01-toolbox-drag-to-canvas.spec.ts`

3) Run the full canvas suite:
- `cd src/BloomBrowserUI`
- `yarn e2e canvas`

## Frame model (critical)
Bloom Edit Tab has multiple iframes. Use frame names first:

- Toolbox frame:
  - name: `toolbox`
  - URL usually includes `toolboxContent`
- Editable page frame:
  - name: `page`
  - URL usually includes `page-memsim-...htm`
- Do not treat top `CURRENTPAGE` frame as editable page content.

## Reliable selectors and activation
- Canvas tool tab header: `h3[data-toolid="canvasTool"]`
- Canvas tool root: `#canvasToolControls`
- Canvas surface: `.bloom-canvas`
- Created elements: `.bloom-canvas-element`
- Speech/comic palette item: `img[src*="comic-icon.svg"]`

Before clicking the canvas tool header, first check whether `#canvasToolControls` is already visible.

## Drag/drop requirements
- Use real Playwright mouse gestures (`page.mouse.down/move/up`), not synthetic dispatched drag events.
- Prefer distinct drop points.
- Verify outcomes semantically:
  - element count increase (`.bloom-canvas-element`)
  - position/rect checks where relevant

## Critical safety rule (Image Toolbox)
- Do **not** run any action that opens the native Image Toolbox window.
- In Canvas context menus/toolbars, never invoke commands that route to `doImageCommand(..., "change")`.
- In practice, do not click:
  - `Choose image from your computer...`
  - `Change image`
- Do **not** invoke native video capture/file-picker commands either.
- In practice, do not click:
  - `Choose Video from your Computer...`
  - `Record yourself...`
- If coverage needs those commands, verify command presence/enabled state only (do not invoke).

## Minimal proof recipe
A valid non-trivial proof test should:
1. Open `CURRENTPAGE`
2. Resolve toolbox + page frames
3. Ensure Canvas tool active
4. Drag a palette item onto `.bloom-canvas`
5. Assert `.bloom-canvas-element` count increased

## Troubleshooting
- If test says "No tests found": verify path filter is relative to the config `testDir`.
- If command says `playwright: not found`: run `yarn install` in `src/BloomBrowserUI`.
- If canvas waits time out: confirm you selected the `page` frame, not top frame.
- If canvas tab click times out: check whether Canvas controls are already visible and skip click in that case.


## Pointers
- Avoid time-based waiting; use DOM-based checks when possible. Feel free to add data-test-ids attributes or other hooks in the app code if needed for reliable testing.

