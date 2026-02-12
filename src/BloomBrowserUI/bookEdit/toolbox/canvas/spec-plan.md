# Canvas Playwright Test Suite Checklist (`CURRENTPAGE`)

## Goal Checklist

- [ ] Maintain comprehensive coverage for canvas creation/editing flows.
- [ ] Keep tests minimal by moving complexity into shared helpers.
- [ ] Group coverage by behavior and by underlying canvas modules.
- [ ] Ensure every scenario runs against `http://localhost:8089/bloom/CURRENTPAGE`.

## Non-Negotiable Constraints Checklist

- [ ] Run every test in `CURRENTPAGE` context.
- [ ] Use real Playwright drag gestures (no synthetic JS drag/drop dispatch).
- [ ] Keep tests iframe-aware (toolbox iframe + page iframe).
- [ ] Prefer semantic assertions over style-only assertions.
- [ ] Keep design helper-first and data-driven (avoid repetitive long test bodies).

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

- [ ] Implement `gotoCurrentPage(page)`.
- [ ] Implement `getPageFrame(page)`.
- [ ] Implement `getToolboxFrame(page)`.
- [ ] Implement `openCanvasToolTab(page)`.
- [ ] Implement `waitForCanvasReady(pageFrame)`.
- [ ] Add robust waiting for iframe/document readiness.
- [ ] Add fail-fast error messages for missing expected frames/selectors.

## 2) Selectors (`canvasSelectors.ts`)

- [ ] Centralize toolbox palette selectors.
- [ ] Centralize page canvas selectors.
- [ ] Centralize context toolbar selectors.
- [ ] Centralize context menu selectors.
- [ ] Centralize toolbox control selectors (style/tail/colors/outline/rounded-corners).
- [ ] Add selector naming convention notes to avoid ad-hoc selectors in specs.

## 3) Actions (`canvasActions.ts`)

- [ ] Implement `dragPaletteItemToCanvas(paletteKey, dropPoint)`.
- [ ] Implement `selectCanvasElement(indexOrPredicate)`.
- [ ] Implement `openContextMenuOnSelectedElement()`.
- [ ] Implement `clickToolbarCommand(commandKey)`.
- [ ] Implement `clickMenuCommand(commandKey)`.
- [ ] Implement `moveSelectedElementByMouse(dx, dy, modifiers?)`.
- [ ] Implement `resizeSelectedElementFromCorner(corner, dx, dy, modifiers?)`.
- [ ] Implement `resizeSelectedElementFromSide(side, delta, modifiers?)`.
- [ ] Implement `keyboardNudgeSelectedElement(key, modifiers?)`.
- [ ] Implement `setStyle(styleValue)`.
- [ ] Implement `setShowTail(enabled)`.
- [ ] Implement `setTextColor(colorOrDefault)`.
- [ ] Implement `setBackgroundColor(color, opacity?)`.
- [ ] Implement `setOutlineColor(colorOrNone)`.
- [ ] Implement `setRoundedCorners(enabled)`.
- [ ] Add coordinate conversion utilities for cross-iframe drag/drop assertions.

## 4) Assertions (`canvasAssertions.ts`)

- [ ] Implement `expectCanvasElementCount(deltaOrTotal)`.
- [ ] Implement `expectSelectedElementType(expectedType)`.
- [ ] Implement `expectElementNearDropPoint(dropPoint, tolerancePx)`.
- [ ] Implement `expectToolbarButtonsForType(type)`.
- [ ] Implement `expectMenuSectionsForType(type)`.
- [ ] Implement `expectCommandEnabled(commandKey, enabled)`.
- [ ] Implement `expectDraggableTargetPairing()`.
- [ ] Implement `expectGridSnapped(position, grid=10)`.
- [ ] Add helper-level assertion messages for quick debugging.

## 5) Data matrix (`canvasMatrix.ts`)

- [ ] Define matrix row shape (`paletteKey`, `expectedType`, expected controls).
- [ ] Add rows for all `CanvasElementType` values in `canvasElementDefinitions.ts`.
- [ ] Add expected menu sections per type.
- [ ] Add expected toolbar buttons per type.
- [ ] Add flags for toolbox-attribute support per type.
- [ ] Add flags for draggability-toggle support per type.
- [ ] Add a validation step to keep matrix in sync with definitions/type inference.

---

## Coverage Checklist by Area

## A. Drag from toolbox onto canvas

**Primary files:** `CanvasElementItem.tsx`, `CanvasElementFactories.ts`, `canvasElementDraggables.ts`, `canvasElementConstants.ts`

- [ ] A1: Drag each palette element type to canvas and verify creation.
- [ ] A2: Drop at multiple points and verify placement within tolerance.
- [ ] A3: Verify toolbox->page iframe coordinate mapping correctness.
- [ ] A4: Verify draggable-specific elements get expected draggable IDs/targets.
- [ ] A5: Verify canvas class state reflects element presence (`bloom-has-canvas-element`).

## B. Manipulate location/size of canvas elements

**Primary files:** `CanvasElementPointerInteractions.ts`, `CanvasElementHandleDragInteractions.ts`, `CanvasElementSelectionUi.ts`, `CanvasElementPositioning.ts`, `CanvasElementGeometry.ts`

- [ ] B1: Select and move element with mouse drag.
- [ ] B2: Resize from all 4 corners.
- [ ] B3: Resize from side handles.
- [ ] B4: Exercise image crop/move-crop interactions.
- [ ] B5: Verify selection frame alignment/follow behavior.
- [ ] B6: Verify manipulated element remains visible/valid.

## C. Context toolbar and menu commands

**Primary files:** `CanvasElementContextControls.tsx`, `canvasElementDefinitions.ts`, `canvasElementTypeInference.ts`

- [ ] C1: Verify toolbar command set matches registry per inferred type.
- [ ] C2: Verify menu sections match registry per inferred type.
- [ ] C3: Verify command visibility/enabled rules (duplicate/delete/background restrictions, etc.).
- [ ] C4: Smoke invoke toolbar/menu duplicate command where valid.
- [ ] C5: Smoke invoke toolbar/menu delete command where valid.
- [ ] C6: Smoke invoke format command where valid.
- [ ] C7: Smoke invoke link-grid choose books command where valid.

## D. Toolbox attribute controls

**Primary files:** `CanvasToolControls.tsx`

- [ ] D1: Verify style dropdown updates selected element family.
- [ ] D2: Verify show-tail toggle behavior.
- [ ] D3: Verify text color chooser/default behavior.
- [ ] D4: Verify background color chooser behavior (including transparency cases).
- [ ] D5: Verify outer outline color dropdown behavior.
- [ ] D6: Verify rounded-corners toggle behavior.
- [ ] D7: Verify enable/disable rules for rounded corners by style/background state.
- [ ] D8: Verify special attribute-control behavior for button types.
- [ ] D9: Verify special attribute-control behavior for link-grid type.

## E. Keyboard movement + snapping + guides

**Primary files:** `CanvasElementKeyboardProvider.ts`, `CanvasSnapProvider.ts`, `CanvasGuideProvider.ts`

- [ ] E1: Verify arrow-key movement uses grid step.
- [ ] E2: Verify Ctrl+arrow precise movement (1px).
- [ ] E3: Verify Shift axis lock behavior during drag.
- [ ] E4: Verify snapped coordinates use grid=10 by default.
- [ ] E5: Verify guide elements appear/disappear as expected (class-level checks only).

## F. Draggable integration/game-specific behavior

**Primary files:** `CanvasElementDraggableIntegration.ts`, `canvasElementDraggables.ts`, `CanvasElementContextControls.tsx`

- [ ] F1: Verify draggability toggle appears only where allowed.
- [ ] F2: Verify enabling draggability sets/keeps `data-draggable-id`.
- [ ] F3: Verify corresponding target creation/association.
- [ ] F4: Verify detached target cleanup behavior.
- [ ] F5: Verify ordering places draggables at end when applicable.

## G. Duplication and child bubbles

**Primary files:** `CanvasElementDuplication.ts`, `CanvasElementFactories.ts`, `CanvasElementBubbleLevelUtils.ts`

- [ ] G1: Verify duplicate creates expected new element/family.
- [ ] G2: Verify duplicate preserves key content/state essentials.
- [ ] G3: Verify duplicate restrictions (background/special cases).
- [ ] G4: Verify add-child-bubble command behavior and relationships.
- [ ] G5: Verify rectangle reorder sanity behavior after duplication.

## H. Background image and resize adjustments

**Primary files:** `CanvasElementBackgroundImageManager.ts`, `CanvasElementCanvasResizeAdjustments.ts`, `CanvasElementEditingSuspension.ts`

- [ ] H1: Verify background image conversion/management scenarios.
- [ ] H2: Verify expand-to-fill-space availability and behavior.
- [ ] H3: Verify canvas resize keeps child elements aligned reasonably.
- [ ] H4: Verify suspend/resume editing around splitter-like interactions.

## I. Clipboard/paste image flows

**Primary file:** `CanvasElementClipboard.ts`

- [ ] I1: Verify paste into selected placeholder image updates in place.
- [ ] I2: Verify paste creates new image canvas element when no suitable selection.
- [ ] I3: Verify start/correct/wrong mode class behavior when harness can control mode.

## J. Type inference and fallback robustness

**Primary files:** `canvasElementTypeInference.ts`, `canvasElementDefinitions.ts`, `CanvasElementContextControls.tsx`

- [ ] J1: Verify inferred type for representative DOM structures.
- [ ] J2: Verify unknown/unregistered structures safely degrade to `none`.
- [ ] J3: Verify command/menu rendering remains stable under fallback type.

---

## Module-to-Area Coverage Mapping Checklist

- [ ] Confirm `CanvasToolControls.tsx` coverage is represented by Area D.
- [ ] Confirm `CanvasElementItem.tsx` + toolbox canvas files are represented by Area A.
- [ ] Confirm `CanvasElementContextControls.tsx` coverage is represented by Area C (+ E/F aspects).
- [ ] Confirm `CanvasElementFactories.ts` coverage is represented by Areas A/G.
- [ ] Confirm pointer/handle/selection UI modules are represented by Area B.
- [ ] Confirm keyboard/snap/guide modules are represented by Area E.
- [ ] Confirm draggable integration module is represented by Area F.
- [ ] Confirm duplication module is represented by Area G.
- [ ] Confirm background/resize/suspension modules are represented by Area H.
- [ ] Confirm clipboard module is represented by Area I.
- [ ] Confirm type inference + definitions are represented by Areas J/C.

---

## Minimality Strategy Checklist

- [ ] Use one matrix to drive type/menu/toolbar contract tests.
- [ ] Use one shared setup path (`gotoCurrentPage + openCanvasToolTab`) across all specs.
- [ ] Keep spec files intent-focused; move implementation details to helpers.
- [ ] Prefer state assertions over styling assertions.
- [ ] Keep contract tests separate from interaction tests.

---

## Execution Phase Checklist

### Phase 1 (high value, fast)

- [ ] Complete Area A.
- [ ] Complete Area B.
- [ ] Complete Area C.
- [ ] Complete Area D.
- [ ] Complete minimal Area E (keyboard/grid movement core).

### Phase 2 (interaction depth)

- [ ] Complete Area F.
- [ ] Complete Area G.
- [ ] Complete Area H.

### Phase 3 (edge/robustness)

- [ ] Complete Area I.
- [ ] Complete Area J.
- [ ] Add cross-state/game-mode variants where feasible.

---

## Risks / Notes Tracking Checklist

- [ ] Document which commands only support availability/invocation tests due to native dialogs.
- [ ] Keep all position assertions in a consistent coordinate space (page-frame client coordinates).
- [ ] Add at least one drag/drop coverage case at non-default zoom/scaling.

---

## Definition of Done Checklist

- [ ] Every area (A-J) has representative implemented scenarios.
- [ ] Type/menu/toolbar contract checks cover all registered `CanvasElementType` values.
- [ ] All tests run in `CURRENTPAGE` context.
- [ ] Drag/drop tests use real mouse gestures.
- [ ] Shared helpers keep individual specs short and readable.
