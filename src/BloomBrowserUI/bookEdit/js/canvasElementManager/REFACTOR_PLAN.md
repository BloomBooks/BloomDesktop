# CanvasElementManager refactor plan (goal: keep CanvasElementManager.ts ≤ 2000 lines)

Context: [src/BloomBrowserUI/bookEdit/js/CanvasElementManager.ts](../CanvasElementManager.ts) is ~7,600 lines and mixes multiple subsystems (creation, selection UI, pointer interactions, duplication, clipboard, alternates, game integration).

## Rules of engagement
- [x] Prefer cohesive subsystem extraction over “random helpers”.
- [x] Preserve existing behavior and public surface area.
- [x] Keep toolbox/page-iframe bundling boundaries safe (avoid accidentally importing page bundle into toolbox bundle).
- [x] Validate at each checkpoint with live UI tests using `bloom-canvas-tool-testing` skill guidance.

After each checkpoint, create a git commit with a description explaining what you did.

---

## Checkpoints (live testing)

### Checkpoint 0 — Baseline sanity
- [x] Open `http://localhost:8089/bloom/CURRENTPAGE`
- [x] Confirm editable page iframe is present (page-memsim-*.htm)
- [x] Confirm toolbox iframe is present (toolboxContent) and Canvas Tool tab exists
- [x] Confirm there is at least one `.bloom-canvas` on the current page

Live tests:
- [ ] Drag a Canvas Tool item from toolbox to a distinct point on the page canvas (repeat 2-3 drop points).
- [ ] Verify created element appears and is selectable.
- [ ] Measure: intended drop point (clientX/clientY), created element bounding rect / `style.left/top`, and delta.
- [ ] Repeat once at a different zoom level.

### Checkpoint 1 — After extracting element factories
- [x] Verify drag/drop from toolbox creates the same element types as before (speech/caption/rectangle/image/video/navigation/button/link-grid where available)
- [x] Verify new element placement relative to drop point still matches expected behavior
- [x] Verify selection + context controls still appear for the new element

Live tests:
- [ ] Drag a text-based item (speech/caption) to the page; verify selection border + context controls.
- [ ] Drag an image-based item to the page; verify placeholder image + crop/resize handles.
- [ ] Drag a navigation button item; verify it has correct structure and is selectable.
- [ ] Measure drop point vs element position (2 distinct points).

### Checkpoint 2 — After extracting clipboard paste flow
- [ ] Paste image from clipboard into empty canvas/background (where applicable)
- [ ] Paste into selected placeholder image overlay
- [ ] Paste on a non-empty canvas to create new overlay (subscription permitting)

Live tests:
- [ ] With a selected placeholder image overlay: paste image; verify it replaces placeholder and stays aligned.
- [ ] With no selected overlay: paste image; verify behavior matches prior (background vs new overlay).

### Checkpoint 3 — After extracting duplication (+ audio copy)
- [ ] Duplicate a text bubble; verify text + style + tail + alternates preserved
- [ ] Duplicate an image overlay; verify image retained + sizing correct
- [ ] Duplicate draggable/game items (if in drag-activity start tab)

Live tests:
- [ ] Duplicate selected element and measure: new element offset, size match, and tail offset.

### Checkpoint 4 — After toolbox type-only import cleanup
- [ ] Confirm no runtime regressions; Canvas Tool still opens and reflects selection changes

Live tests:
- [ ] Switch between two elements; toolbox controls update.

### Checkpoint 5 — After extracting selection UI controller
- [x] Verify focus/tab/zoom-change hacks still prevent unwanted selection jumps
- [x] Verify context menu and dialogs do not cause accidental active-element changes

Live tests:
- [x] Select element, open context menu ("..."); open a dialog; close it; verify selection remains stable.

### Checkpoint 6 — After extracting pointer interactions controller
- [ ] Drag move with snapping and shift-axis lock
- [ ] Resize with snapping; guides appear; crop handles work for images

Live tests:
- [ ] Drag element with and without Ctrl (snapping off with Ctrl).
- [ ] Drag with Shift (axis lock).
- [ ] Resize element corner; verify guides appear and element snaps.

### Checkpoint 7 — After extracting alternates + game integration
- [ ] Switch languages and verify alternates preserved (positions/tails)
- [ ] Drag-activity targets remain in sync with draggables

Live tests:
- [ ] On a page with alternates: switch language, verify bubble/tails update.
- [ ] In a drag activity: move draggable, verify target stays aligned.

---

## Execution steps (checkbox checklist)

### Phase A — Preparation
- [x] (A1) Baseline live test (Checkpoint 0)
- [x] (A2) Add a line-count guard note: record starting line counts in this doc

### Phase B — Reduce size by cohesive subsystem extraction
- [x] (B1) Extract element creation + templates into `CanvasElementFactories.ts` (Checkpoint 1)
- [x] (B2) Extract clipboard paste into `CanvasElementClipboard.ts` (Checkpoint 2)
- [x] (B3) Extract duplication + cloning into `CanvasElementDuplication.ts` (+ `CanvasElementAudioDuplication.ts`) (Checkpoint 3)
- [x] (B4) Toolbox bundling safety: switch toolbox imports to `import type` + move shared types into dependency-light module (Checkpoint 4)
- [x] (B5) Extract selection UI + focus/control frame into `CanvasElementSelectionUi.ts` (Checkpoint 5)
- [ ] (B6) Extract pointer interactions (drag/resize/crop) into `CanvasElementPointerInteractions.ts` (Checkpoint 6)
- [x] (B6) Extract pointer interactions + handle drag (drag/select/context menu in `CanvasElementPointerInteractions.ts`; resize/crop/side-handle/move-crop in `CanvasElementHandleDragInteractions.ts`) (Checkpoint 6)
- [x] (B7) Extract alternates + game/draggable integration modules (expand `CanvasElementAlternates.ts`; add `CanvasElementDraggableIntegration.ts`) (Checkpoint 7)
- [x] (B8) Extract editing suspension + origami splitter interactions into `CanvasElementEditingSuspension.ts`

### Phase C — Finish
- [ ] (C1) Ensure [src/BloomBrowserUI/bookEdit/js/CanvasElementManager.ts](../CanvasElementManager.ts) ≤ 2000 lines
- [ ] (C2) Run `yarn lint` (no `yarn build`)
- [ ] (C3) Run existing unit tests covering moved helpers (vitest suite that already exists for manager helpers)
- [ ] (C4) Final live test sweep: drag/drop, selection, resize, duplicate, paste

## Line counts
- [x] Starting: CanvasElementManager.ts 7610 lines
- [ ] Target: CanvasElementManager.ts ≤ 2000 lines
- [x] Current: CanvasElementManager.ts 4120 lines
- [x] Current: CanvasElementManager.ts 3955 lines
