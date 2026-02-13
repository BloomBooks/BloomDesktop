# Canvas Playwright Test Suite Checklist (`CURRENTPAGE`)

## Goal Checklist

- [x] Maintain comprehensive coverage for canvas creation/editing flows.
- [x] Keep tests minimal by moving complexity into shared helpers.
- [x] Group coverage by behavior and by underlying canvas modules.
- [x] Ensure every scenario runs against `http://localhost:8089/bloom/CURRENTPAGE`.

## Non-Negotiable Constraints Checklist

- [x] Run every test in `CURRENTPAGE` context.
- [x] Use real Playwright drag gestures (no synthetic JS drag/drop dispatch).
- [x] Keep tests iframe-aware (toolbox iframe + page iframe).
- [x] Prefer semantic assertions over style-only assertions.
- [x] Keep design helper-first and data-driven (avoid repetitive long test bodies).

---

## Suite Structure Checklist

### Planned path structure

- [x] Create `src/BloomBrowserUI/bookEdit/canvas-e2e-tests/`.
- [x] Create `playwright/fixtures/`.
- [x] Create `playwright/helpers/`.
- [x] Create `playwright/specs/`.
- [x] Create `playwright/README.md`.

### Fixture/helper files

- [x] Create `playwright/fixtures/canvasTest.ts`.
- [x] Create `playwright/helpers/canvasFrames.ts`.
- [x] Create `playwright/helpers/canvasSelectors.ts`.
- [x] Create `playwright/helpers/canvasActions.ts`.
- [x] Create `playwright/helpers/canvasAssertions.ts`.
- [x] Create `playwright/helpers/canvasMatrix.ts`.

### Spec files

- [x] Create `playwright/specs/01-toolbox-drag-to-canvas.spec.ts`.
- [x] Create `playwright/specs/02-select-move-resize-crop.spec.ts`.
- [x] Create `playwright/specs/03-context-toolbar-and-menu.spec.ts`.
- [x] Create `playwright/specs/04-toolbox-attributes.spec.ts`.
- [x] Create `playwright/specs/05-draggable-integration.spec.ts`.
- [x] Create `playwright/specs/06-duplication-and-child-bubbles.spec.ts`.
- [x] Create `playwright/specs/07-background-image-and-canvas-resize.spec.ts`.
- [x] Create `playwright/specs/08-clipboard-and-paste.spec.ts`.
- [x] Create `playwright/specs/09-keyboard-and-snapping.spec.ts`.
- [x] Create `playwright/specs/10-type-inference-and-registry-contract.spec.ts`.

---

## Shared Helper Implementation Checklist

## 1) Frames and bootstrap (`canvasFrames.ts`)

- [x] Implement `gotoCurrentPage(page)`.
- [x] Implement `getPageFrame(page)`.
- [x] Implement `getToolboxFrame(page)`.
- [x] Implement `openCanvasToolTab(page)`.
- [x] Implement `waitForCanvasReady(pageFrame)`.
- [x] Add robust waiting for iframe/document readiness.
- [x] Add fail-fast error messages for missing expected frames/selectors.

## 2) Selectors (`canvasSelectors.ts`)

- [x] Centralize toolbox palette selectors.
- [x] Centralize page canvas selectors.
- [x] Centralize context toolbar selectors.
- [x] Centralize context menu selectors.
- [x] Centralize toolbox control selectors (style/tail/colors/outline/rounded-corners).
- [x] Add selector naming convention notes to avoid ad-hoc selectors in specs.

## 3) Actions (`canvasActions.ts`)

- [x] Implement `dragPaletteItemToCanvas(paletteKey, dropPoint)`.
- [x] Implement `selectCanvasElement(indexOrPredicate)`.
- [x] Implement `openContextMenuOnSelectedElement()`.
- [x] Implement `clickToolbarCommand(commandKey)`.
- [x] Implement `clickMenuCommand(commandKey)`.
- [x] Implement `moveSelectedElementByMouse(dx, dy, modifiers?)`.
- [x] Implement `resizeSelectedElementFromCorner(corner, dx, dy, modifiers?)`.
- [x] Implement `resizeSelectedElementFromSide(side, delta, modifiers?)`.
- [x] Implement `keyboardNudgeSelectedElement(key, modifiers?)`.
- [x] Implement `setStyle(styleValue)`.
- [x] Implement `setShowTail(enabled)`.
- [x] Implement `setTextColor(colorOrDefault)`.
- [x] Implement `setBackgroundColor(color, opacity?)`.
- [x] Implement `setOutlineColor(colorOrNone)`.
- [x] Implement `setRoundedCorners(enabled)`.
- [x] Add coordinate conversion utilities for cross-iframe drag/drop assertions.

## 4) Assertions (`canvasAssertions.ts`)

- [x] Implement `expectCanvasElementCount(deltaOrTotal)`.
- [x] Implement `expectSelectedElementType(expectedType)`.
- [x] Implement `expectElementNearDropPoint(dropPoint, tolerancePx)`.
- [x] Implement `expectToolbarButtonsForType(type)`.
- [x] Implement `expectMenuSectionsForType(type)`.
- [x] Implement `expectCommandEnabled(commandKey, enabled)`.
- [x] Implement `expectDraggableTargetPairing()`.
- [x] Implement `expectGridSnapped(position, grid=10)`.
- [x] Add helper-level assertion messages for quick debugging.

## 5) Data matrix (`canvasMatrix.ts`)

- [x] Define matrix row shape (`paletteKey`, `expectedType`, expected controls).
- [x] Add rows for all `CanvasElementType` values in `canvasElementDefinitions.ts`.
- [x] Add expected menu sections per type.
- [x] Add expected toolbar buttons per type.
- [x] Add flags for toolbox-attribute support per type.
- [x] Add flags for draggability-toggle support per type.
- [ ] Add a validation step to keep matrix in sync with definitions/type inference.

---

## Coverage Checklist by Area

## A. Drag from toolbox onto canvas

**Primary files:** `CanvasElementItem.tsx`, `CanvasElementFactories.ts`, `canvasElementDraggables.ts`, `canvasElementConstants.ts`

- [x] A1: Drag each palette element type to canvas and verify creation.
- [x] A2: Drop at multiple points and verify placement within tolerance.
- [x] A3: Verify toolbox->page iframe coordinate mapping correctness.
- [ ] A4: Verify draggable-specific elements get expected draggable IDs/targets. *(requires game page)*
- [x] A5: Verify canvas class state reflects element presence (`bloom-has-canvas-element`).

## B. Manipulate location/size of canvas elements

**Primary files:** `CanvasElementPointerInteractions.ts`, `CanvasElementHandleDragInteractions.ts`, `CanvasElementSelectionUi.ts`, `CanvasElementPositioning.ts`, `CanvasElementGeometry.ts`

- [x] B1: Select and move element with mouse drag.
- [x] B2: Resize from all 4 corners.
- [x] B3: Resize from side handles.
- [ ] B4: Exercise image crop/move-crop interactions.
- [x] B5: Verify selection frame alignment/follow behavior.
- [x] B6: Verify manipulated element remains visible/valid.

## C. Context toolbar and menu commands

**Primary files:** `CanvasElementContextControls.tsx`, `canvasElementDefinitions.ts`, `canvasElementTypeInference.ts`

- [x] C1: Verify toolbar command set matches registry per inferred type.
- [x] C2: Verify menu sections match registry per inferred type.
- [x] C3: Verify command visibility/enabled rules (duplicate/delete/background restrictions, etc.).
- [x] C4: Smoke invoke toolbar/menu duplicate command where valid.
- [x] C5: Smoke invoke toolbar/menu delete command where valid.
- [x] C6: Smoke invoke format command where valid.
- [ ] C7: Smoke invoke link-grid choose books command where valid. *(native dialog)*

## D. Toolbox attribute controls

**Primary files:** `CanvasToolControls.tsx`

- [x] D1: Verify style dropdown updates selected element family.
- [x] D2: Verify show-tail toggle behavior.
- [x] D3: Verify text color chooser/default behavior.
- [x] D4: Verify background color chooser behavior (including transparency cases).
- [x] D5: Verify outer outline color dropdown behavior.
- [x] D6: Verify rounded-corners toggle behavior.
- [x] D7: Verify enable/disable rules for rounded corners by style/background state.
- [x] D8: Verify special attribute-control behavior for button types.
- [x] D9: Verify special attribute-control behavior for link-grid type.

## E. Keyboard movement + snapping + guides

**Primary files:** `CanvasElementKeyboardProvider.ts`, `CanvasSnapProvider.ts`, `CanvasGuideProvider.ts`

- [x] E1: Verify arrow-key movement uses grid step.
- [x] E2: Verify Ctrl+arrow precise movement (1px).
- [x] E3: Verify Shift axis lock behavior during drag.
- [x] E4: Verify snapped coordinates use grid=10 by default.
- [ ] E5: Verify guide elements appear/disappear as expected (class-level checks only).

## F. Draggable integration/game-specific behavior

**Primary files:** `CanvasElementDraggableIntegration.ts`, `canvasElementDraggables.ts`, `CanvasElementContextControls.tsx`

- [x] F1: Verify draggability toggle appears only where allowed.
- [ ] F2: Verify enabling draggability sets/keeps `data-draggable-id`. *(requires game page)*
- [x] F3: Verify corresponding target creation/association.
- [ ] F4: Verify detached target cleanup behavior. *(requires game page)*
- [ ] F5: Verify ordering places draggables at end when applicable. *(requires game page)*

## G. Duplication and child bubbles

**Primary files:** `CanvasElementDuplication.ts`, `CanvasElementFactories.ts`, `CanvasElementBubbleLevelUtils.ts`

- [x] G1: Verify duplicate creates expected new element/family.
- [x] G2: Verify duplicate preserves key content/state essentials.
- [x] G3: Verify duplicate restrictions (background/special cases).
- [ ] G4: Verify add-child-bubble command behavior and relationships. Add 3 children, delete some, re-add, etc.
- [x] G5: Verify rectangle reorder sanity behavior after duplication.

## H. Background image and resize adjustments

**Primary files:** `CanvasElementBackgroundImageManager.ts`, `CanvasElementCanvasResizeAdjustments.ts`, `CanvasElementEditingSuspension.ts`

- [x] H1: Verify background image conversion/management scenarios.
- [ ] H2: Verify expand-to-fill-space availability and behavior.
- [x] H3: Verify canvas resize keeps child elements aligned reasonably.
- [ ] H4: Verify suspend/resume editing around splitter-like interactions.

## I. Clipboard/paste image flows

**Primary file:** `CanvasElementClipboard.ts`

- [x] I1: Verify paste into selected placeholder image updates in place.
- [x] I2: Verify paste creates new image canvas element when no suitable selection.
- [ ] I3: Verify start/correct/wrong mode class behavior when harness can control mode.

## J. Type inference and fallback robustness

**Primary files:** `canvasElementTypeInference.ts`, `canvasElementDefinitions.ts`, `CanvasElementContextControls.tsx`

- [x] J1: Verify inferred type for representative DOM structures.
- [x] J2: Verify unknown/unregistered structures safely degrade to `none`.
- [x] J3: Verify command/menu rendering remains stable under fallback type.

---

## Module-to-Area Coverage Mapping Checklist

- [x] Confirm `CanvasToolControls.tsx` coverage is represented by Area D.
- [x] Confirm `CanvasElementItem.tsx` + toolbox canvas files are represented by Area A.
- [x] Confirm `CanvasElementContextControls.tsx` coverage is represented by Area C (+ E/F aspects).
- [x] Confirm `CanvasElementFactories.ts` coverage is represented by Areas A/G.
- [x] Confirm pointer/handle/selection UI modules are represented by Area B.
- [x] Confirm keyboard/snap/guide modules are represented by Area E.
- [x] Confirm draggable integration module is represented by Area F.
- [x] Confirm duplication module is represented by Area G.
- [x] Confirm background/resize/suspension modules are represented by Area H.
- [x] Confirm clipboard module is represented by Area I.
- [x] Confirm type inference + definitions are represented by Areas J/C.

---

## Minimality Strategy Checklist

- [x] Use one matrix to drive type/menu/toolbar contract tests.
- [x] Use one shared setup path (`gotoCurrentPage + openCanvasToolTab`) across all specs.
- [x] Keep spec files intent-focused; move implementation details to helpers.
- [x] Prefer state assertions over styling assertions.
- [x] Keep contract tests separate from interaction tests.

---

## Execution Phase Checklist

### Phase 1 (high value, fast)

- [x] Complete Area A.
- [x] Complete Area B.
- [x] Complete Area C.
- [x] Complete Area D.
- [x] Complete minimal Area E (keyboard/grid movement core).

### Phase 2 (interaction depth)

- [x] Complete Area F.
- [x] Complete Area G.
- [x] Complete Area H.

### Phase 3 (edge/robustness)

- [x] Complete Area I.
- [x] Complete Area J.
- [ ] Add cross-state/game-mode variants where feasible.

---

## Risks / Notes Tracking Checklist

- [x] Document which commands only support availability/invocation tests due to native dialogs.
- [x] Keep all position assertions in a consistent coordinate space (page-frame client coordinates).
- [ ] Add at least one drag/drop coverage case at non-default zoom/scaling.

---

## Definition of Done Checklist

- [x] Every area (A-J) has representative implemented scenarios.
- [x] Type/menu/toolbar contract checks cover all registered `CanvasElementType` values.
- [x] All tests run in `CURRENTPAGE` context.
- [x] Drag/drop tests use real mouse gestures.
- [x] Shared helpers keep individual specs short and readable.


## Ideas for new tests based on exploration

- [ ] Expand **Navigation**. Drag **Image+Label Button**. Enter label text. Open context menu and invoke **Set Destination**, **Format text...**, **Copy text**, **Paste text**, **Choose image from your computer...**, **Paste image**, **Reset image**, **Duplicate**, then **Delete**. Assert no crash and expected element-count transitions.
- [ ] Drag **Speech** bubble. Open menu and run **Add child bubble** three times, delete middle child, add another child, then delete parent. Verify child/parent relationship integrity and cleanup order.
- [ ] Drag **Speech** bubble with multiline text. Invoke **Auto height** and assert height grows to fit content; remove content and invoke again to verify shrink behavior without overlap.
- [ ] Create two speech/text elements with different content. Use **Copy text** on source and **Paste text** on target. Verify only text payload transfers and style/position do not unexpectedly change.
- [ ] Drag **Image** element. Run **Paste image**, then **Copy image**, then **Reset image**. Verify image container content changes, then returns to placeholder state after reset.
- [ ] Drag **Image** element and invoke **Set image information...**. Validate command opens expected flow (or no-op safely in harness) and leaves canvas selection/edit state stable.
- [ ] Drag **Video** element and invoke **Choose video from your computer...** and **Record yourself...**. Verify command invocation paths are wired and do not break selection/context controls when dialog closes/cancels.
- [ ] Place at least two video elements. Invoke **Play Earlier** and **Play Later** repeatedly and assert DOM/order changes reflect expected playback ordering semantics.
- [ ] For each non-navigation type that exposes **Format text...**, invoke it from menu and from toolbar where available; verify editor remains responsive and active element is preserved.
- [ ] For each type where **Duplicate** is available, duplicate then mutate duplicate (text/color/image) and verify original remains unchanged (deep copy vs shared state regression).
- [ ] For each type where **Delete** is available, delete from context menu and verify focus/active-selection handoff is valid (next element selected or none selected deterministically).
- [ ] Speech/caption style matrix: cycle style dropdown through all available values (including non-default options such as pointed-arc variants), assert allowed controls update correctly for each style.
- [ ] Style-transition persistence: set rounded corners + outline/text/background colors, switch style away and back, verify which properties persist/reset per intended rules.
- [ ] Text color picker: choose a non-default color, then reset to default; verify resulting element markup/classes reflect explicit color then default inheritance.
- [ ] Background color picker: choose opaque then transparent values; verify rounded-corners enable/disable rules and rendered/background model are consistent.
- [ ] Outline dropdown matrix: iterate all outline colors including **none**, assert selected value is applied and remains stable after duplicate + reload-like re-selection.
- [ ] Navigation image button controls: verify only background color control is shown; text color and style controls stay hidden even after duplicate/select cycles.
- [ ] Navigation label button controls: verify text/background controls are shown and affect button label rendering; confirm no image-specific controls appear.
- [ ] Book-link-grid lifecycle: invoke **Choose books...**, cancel, re-open, confirm command remains available and element remains functional; verify only one grid allowed per page.
- [ ] Mixed workflow regression: create speech + image + video + navigation button on one page, apply representative toolbox/menu actions to each, then perform keyboard nudges and duplicate/delete sequence to validate cross-feature stability.
