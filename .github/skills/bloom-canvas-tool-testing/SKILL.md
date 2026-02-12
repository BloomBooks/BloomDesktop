---
name: bloom-canvas-tool-manual-testing
description: reproduce and verify Canvas Element behaviors manually via chrome-devtools-mcp, not in a playwright test.
---

## Scope
Use this skill when the user reports a regression involving Canvas Tool interactions (especially drag/drop from the toolbox onto the page) and asks you to reproduce and verify fixes using a browser.

This skill assumes:
- Bloom is running locally and serving the Edit Tab
- The current page has an element with class `.bloom-canvas`
- The current page has the Canvas Tool tab available in the toolbox
- The user has started the vite dev server for the frontend code

## Primary test URL
  - `http://localhost:8089/bloom/CURRENTPAGE`

## Reproduction approach (required)
When testing or verifying a UI regression:
- Do not rely only on synthetic JS event dispatch.
- Use browser automation/tools to perform an actual drag/drop gesture.

## Finding things
1. If your task involves the toolbox, identify that the toolbox iframe (often `.../toolboxContent`) and confirm the Canvas Tool tab is selected.
2. The page we are editing is in an iframe (a `page-memsim-...htm`)
3. On the page, you can locate the canvas we are editing as an element with the `.bloom-canvas` class.


## If you are performing drag/drop
1. Perform a drag from the toolbox item onto a distinct point on the page (test with multiple drop points).
2. Verify outcome by measuring:
   - The intended drop point (clientX/clientY over the page frame)
   - The created element’s bounding rect and/or `style.left/top`
   - The delta between drop point and element location
   - Test with multiple zoom levels and page scaling to confirm consistent behavior

## Notes
- Bloom’s edit UI uses multiple iframes; coordinate systems (screen/client/page) often differ between frames.
- Page scaling (`transform: scale(...)`) can affect `getBoundingClientRect()` values; prefer consistent coordinate spaces when comparing.
