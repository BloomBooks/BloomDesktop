# Canvas Controls Refactor — Implementation Plan

Based on [canvas-controls-plan.md](canvas-controls-plan.md). This document tracks
the concrete implementation steps, organized by phase.

---

## Pre-implementation: Plan Gaps to Resolve

These items were identified during plan review and should be addressed before or
during implementation.

- [x] **Add `enabled` callbacks for controls that currently have disabled states:**
  - `resetImage` — disabled when image is not cropped (`!img?.style?.width`)
  - `expandToFillSpace` — disabled when `!canExpandBackgroundImage`
  - `playVideoEarlier` — disabled when no previous video container
  - `playVideoLater` — disabled when no next video container
- [x] **Surface-specific icon overrides.** `missingMetadata` uses `MissingMetadataIcon` on toolbar but `CopyrightIcon` on menu. `expandToFillSpace` uses `FillSpaceIcon` on toolbar but an asset `<img>` on menu. Either add `menu.icon` / `toolbar.icon` optional overrides on `ICommandControlDefinition`, or convert to unified icons.
- [x] **Audio parent-menu-item label:** current image-variant shows the sound filename (minus `.mp3`). The plan's `buildMenuItem` shows static `"Choose..."`. Decide: preserve dynamic label in `buildMenuItem`, or unify.
- [x] **Add `IControlContext` flags for video enabled state:**
  - `hasPreviousVideoContainer: boolean`
  - `hasNextVideoContainer: boolean`
- [ ] **Deselected-element tool panel state.** Current code has `case undefined` fallthrough to text/bubble controls. Clarify: does the plan keep this behavior via an implicit "show last selected type's controls" rule, or explicitly via the `none` element definition?
- [x] **Write concrete element definitions for all 11 types** (plan only shows 4 examples).

---

## Phase 1 — Parity Inventory

Goal: document and lock down every current behavior so regressions are detectable.

- [ ] **1.1** Audit all toolbar button visibility/enabled conditions per element type; record in a matrix table.
- [ ] **1.2** Audit all menu item visibility/enabled/disabled conditions per element type and menu section; record in matrix.
- [ ] **1.3** Audit all tool-panel controls per element type (including button, book-grid, deselected states); record in matrix.
- [ ] **1.4** Audit audio submenu behavior for both image and text variants; document exact menu-item set, labels, icons, enabled states, and dynamic label rules.
- [ ] **1.5** Audit focus-management behavior: `setMenuOpen`, `ignoreFocusChanges`, `skipNextFocusChange`, dialog-launching pattern.
- [ ] **1.6** Audit subscription/feature-gating on menu items and tool panel (`featureName`, `RequiresSubscriptionOverlayWrapper`).
- [ ] **1.7** Audit draggability toggle logic and `togglePartOfRightAnswer` visibility/behavior.
- [ ] **1.8** Identify existing e2e tests that cover context controls behavior; note gaps.
- [ ] **1.9** Add/update e2e tests for high-risk behaviors before starting implementation:
  - Audio nested submenu (image variant + text variant)
  - Draggability toggle and "Part of Right Answer" menu items
  - Navigation button panel controls (text color, background color, image fill)
  - `missingMetadata` toolbar-only vs menu behavior
  - `fillBackground` toggle on rectangle elements
  - Section auto-divider behavior (non-empty sections only)

---

## Phase 2 — Core Type System & Registry

Goal: introduce the new modules with full type definitions and registry data,
without wiring them to any rendering yet.

### 2.1 — Types module

- [x] **2.1.1** Create `src/BloomBrowserUI/bookEdit/toolbox/canvas/canvasControlTypes.ts`:
  - `ControlId` string literal union (all commands + dynamic menu row ids)
  - `SectionId` string literal union
  - `IControlContext` interface
  - `IControlRuntime` interface
  - `IControlIcon` type
  - `IControlMenuRow` discriminated union (`IControlMenuCommandRow` | `IControlMenuHelpRow`)
  - `IBaseControlDefinition`, `ICommandControlDefinition`, `IPanelOnlyControlDefinition`
  - `IControlDefinition` discriminated union
  - `IControlSection` interface
  - `ICanvasElementDefinition` interface (with `menuSections`, `toolbar`, `toolPanel`, `availabilityRules`)
  - `ICanvasToolsPanelState` interface
  - `AvailabilityRulesMap` type alias

### 2.2 — Control registry

- [x] **2.2.1** Create `src/BloomBrowserUI/bookEdit/toolbox/canvas/canvasControlRegistry.ts`:
  - `controlRegistry: Record<ControlId, IControlDefinition>` — define every control:
    - `chooseImage`, `pasteImage`, `copyImage`, `missingMetadata`, `resetImage`, `expandToFillSpace`, `imageFillMode`
    - `chooseVideo`, `recordVideo`, `playVideoEarlier`, `playVideoLater`
    - `format`, `copyText`, `pasteText`, `autoHeight`, `fillBackground`
    - `addChildBubble`, `bubbleStyle`, `showTail`, `roundedCorners`, `textColor`, `backgroundColor`, `outlineColor`
    - `setDestination`
    - `linkGridChooseBooks`
    - `duplicate`, `delete`, `toggleDraggable`, `togglePartOfRightAnswer`
    - `chooseAudio` (with `menu.buildMenuItem` for image/text variants)
  - `controlSections: Record<SectionId, IControlSection>` — section-to-surface-control mapping

### 2.3 — Shared availability presets

- [x] **2.3.1** Create `src/BloomBrowserUI/bookEdit/toolbox/canvas/canvasAvailabilityPresets.ts`:
  - `imageAvailabilityRules` — chooseImage, pasteImage, copyImage, resetImage, missingMetadata (surfacePolicy), expandToFillSpace
  - `videoAvailabilityRules` — chooseVideo, recordVideo, playVideoEarlier, playVideoLater
  - `audioAvailabilityRules` — chooseAudio
  - `textAvailabilityRules` — format, copyText, pasteText, autoHeight, fillBackground
  - `bubbleAvailabilityRules` — addChildBubble
  - `wholeElementAvailabilityRules` — duplicate, delete (surfacePolicy), toggleDraggable, togglePartOfRightAnswer

### 2.4 — Element definitions

- [x] **2.4.1** Create `src/BloomBrowserUI/bookEdit/toolbox/canvas/canvasElementNewDefinitions.ts` (temporary name during dual-path phase):
  - `imageCanvasElementDefinition`
  - `videoCanvasElementDefinition`
  - `soundCanvasElementDefinition`
  - `rectangleCanvasElementDefinition`
  - `speechCanvasElementDefinition`
  - `captionCanvasElementDefinition`
  - `bookLinkGridDefinition`
  - `navigationImageButtonDefinition`
  - `navigationImageWithLabelButtonDefinition`
  - `navigationLabelButtonDefinition`
  - `noneCanvasElementDefinition`
  - `canvasElementDefinitionsNew: Record<CanvasElementType, ICanvasElementDefinition>` export

### 2.5 — Context builder

- [x] **2.5.1** Create `src/BloomBrowserUI/bookEdit/toolbox/canvas/buildControlContext.ts`:
  - `buildControlContext(canvasElement: HTMLElement): IControlContext`
  - Isolates all DOM querying (image presence, video presence, draggability flags, game context, etc.)
  - Unit-testable with mock elements

### 2.6 — Rendering helpers

- [x] **2.6.1** Create `src/BloomBrowserUI/bookEdit/toolbox/canvas/canvasControlHelpers.ts`:
  - `getToolbarItems(definition, ctx)` → `Array<IResolvedControl | { id: "spacer" }>`
  - `getMenuSections(definition, ctx)` → `IResolvedControl[][]`
  - `getToolPanelControls(definition, ctx)` → component/ctx pairs
  - Each helper: iterates sections/toolbar list, looks up `availabilityRules`, resolves `surfacePolicy`, returns visible items only
  - Unit-testable with mock definitions and contexts

---

## Phase 3 — Tool Panel Controls as Components

Goal: convert each tool-panel control into a standalone `React.FunctionComponent<{ ctx; panelState }>`.

- [ ] **3.1** Create `BubbleStyleControl` component (style dropdown)
- [ ] **3.2** Create `ShowTailControl` component (checkbox)
- [ ] **3.3** Create `RoundedCornersControl` component (checkbox)
- [ ] **3.4** Create `OutlineColorControl` component (dropdown)
- [ ] **3.5** Create `TextColorControl` component (color picker)
- [ ] **3.6** Create `BackgroundColorControl` component (color picker)
- [ ] **3.7** Create `ImageFillModeControl` component (dropdown)
- [ ] **3.8** Register all as `kind: "panel"` entries in `controlRegistry`

---

## Phase 4 — Dual-Path Adapter

Goal: wire the new registry into the existing rendering components behind a
feature flag or dev-mode switch, so both old and new paths can run.

- [x] **4.1** Add a `useNewCanvasControls` flag (env var, localStorage, or build setting).
- [x] **4.2** In `CanvasElementContextControls.tsx`, add an adapter branch:
  - When flag is on: call `buildControlContext`, then `getToolbarItems` / `getMenuSections`
  - When flag is off: run existing code unchanged
  - Both branches must produce the same rendered output for parity testing
- [x] **4.3** In `CanvasToolControls.tsx`, add an adapter branch:
  - When flag is on: call `buildControlContext`, then `getToolPanelControls`, render component list
  - When flag is off: run existing `switch (canvasElementType)` code
- [x] **4.4** Verify focus-management behavior is preserved:
  - `IControlRuntime.closeMenu` wired to existing `setMenuOpen(open, launchingDialog)`
  - Menu open → `ignoreFocusChanges(true)`; close → `setTimeout(() => ignoreFocusChanges(false, launchingDialog), 0)`
  - Menu button uses `onMouseDown` (preventDefault) + `onMouseUp` (open) pattern preserved
- [ ] **4.5** Verify subscription gating:
  - `RequiresSubscriptionOverlayWrapper` still wraps tool panel
- [x] **4.6** Verify menu rendering:
  - `keepMounted` behavior preserved for positioning (BL-14549)
  - Section dividers auto-inserted between non-empty sections
  - Help rows render as non-clickable content
  - Submenu rows render via `LocalizableNestedMenuItem`

---

## Phase 5 — Parity Testing

Goal: confirm the new path produces identical behavior to the old path.

- [x] **5.1** Run full e2e test suite with new-path flag on; all existing tests must pass.
  - Current run status (new-path flag on): `122 passed`, `0 failed`, `3 flaky`, `1 skipped`.
- [ ] **5.2** Test each element type manually:
  - [ ] `image` — toolbar, menu, no tool-panel controls
  - [ ] `video` — toolbar, menu, no tool-panel controls
  - [ ] `sound` — toolbar (duplicate/delete only), menu, no tool-panel controls
  - [ ] `rectangle` — toolbar, menu (fillBackground toggle), tool panel (bubble+text controls)
  - [ ] `speech` — toolbar, menu, tool panel (bubble+text controls)
  - [ ] `caption` — toolbar, menu, tool panel (bubble+text controls)
  - [ ] `book-link-grid` — toolbar (composite choose-books), menu, tool panel (background color only)
  - [ ] `navigation-image-button` — toolbar, menu, tool panel (text color?, background color, image fill)
  - [ ] `navigation-image-with-label-button` — toolbar, menu, tool panel
  - [ ] `navigation-label-button` — toolbar, menu, tool panel
  - [ ] `none` / unknown type — toolbar (duplicate/delete), menu (wholeElement section)
- [x] **5.3** Test audio submenu variants:
  - [x] Image element in drag game: None / current-sound / Choose... / help row
  - [x] Text element in drag game: Use Talking Book Tool (label reflects audio state)
- [x] **5.4** Test draggability:
  - [x] Toggle draggable on/off
  - [x] "Part of Right Answer" visible only when draggable
  - [ ] `canToggleDraggability` logic (excludes gifs, rectangles, sentence items, background, audio)
- [x] **5.5** Test focus lifecycle:
  - [x] Open menu from toolbar button — no unexpected focus steal
  - [x] Right-click menu opens at anchor position
  - [x] Close menu without dialog — focus restored
  - [x] Close menu with dialog launch — `skipNextFocusChange` semantics preserved
- [x] **5.6** Test subscription gating:
  - [x] `setDestination` shows subscription badge when applicable
  - [x] Tool panel wrapped in `RequiresSubscriptionOverlayWrapper`
- [x] **5.7** Test background-image element:
  - [x] "Background Image" label shown on toolbar
  - [x] Delete hidden on toolbar but visible on menu; disabled when placeholder
  - [x] Duplicate hidden
  - [x] Expand to Fill Space visible, enabled/disabled correctly
- [x] **5.8** Confirm disabled states render correctly:
  - [x] `copyImage` disabled when placeholder
  - [x] `resetImage` disabled when not cropped
  - [x] Delete disabled for background-image placeholder and special game elements
  - [x] `expandToFillSpace` disabled when already fills space
  - [x] `playVideoEarlier`/`playVideoLater` disabled when no adjacent container
- [x] **5.9** Availability-rules e2e coverage from `canvasAvailabilityPresets.ts` + `canvasElementNewDefinitions.ts`.
  - [x] `autoHeight` hidden for button element types (`navigation-*`)
  - [x] `fillBackground` visible only when inferred rectangle style
  - [x] `addChildBubble` hidden in draggable-game activity and visible otherwise
  - [x] `chooseAudio` visible only in draggable-game context for text/image-capable elements
  - [x] `toggleDraggable` visible only when `canToggleDraggability` conditions are met
  - [x] `togglePartOfRightAnswer` hidden before draggable id exists, visible after toggling draggable
  - [x] `playVideoEarlier`/`playVideoLater` enabled state reflects previous/next container availability
  - [x] `expandToFillSpace` visible on background-image elements and enabled state tracks manager `canExpandToFillSpace()`
  - [x] `duplicate`/`delete` availability for `isBackgroundImage` and `isSpecialGameElement` conditions
  - Implemented in `bookEdit/canvas-e2e-tests/specs/13-availability-rules.spec.ts`.
  - Follow-up parity checks in the same spec:
    - `K7`: text-audio submenu shows `Use Talking Book Tool` in drag-game context
    - `K8`: image-audio submenu coverage for current-sound label path + choose/help rows
    - `K9`: draggable toggle on/off and right-answer menu visibility transitions
    - `K10`: background-image toolbar label visibility
  - Additional phase-5 parity coverage in `bookEdit/canvas-e2e-tests/specs/14-phase5-lifecycle-subscription-disabled.spec.ts`:
    - `L1`–`L3`: focus lifecycle checks (toolbar open/close, right-click anchor positioning, dialog-launch close path)
    - `S1`–`S2`: subscription gating checks (`Set Destination` badge path and tool-panel overlay wrapper)
    - `D1`–`D4`: disabled-state checks for placeholder image commands, background delete/duplicate rules, fit-space disabled path, and no-adjacent-video disabled states

---

## Phase 6 — Cutover

Goal: make the new path the only path.

- [ ] **6.1** Remove the dual-path flag; new path is always active.
- [ ] **6.2** Remove the old `canvasElementCommands` record from `CanvasElementContextControls.tsx`.
- [ ] **6.3** Remove old per-section menu-building code (inline `push` calls for `imageMenuItems`, `videoMenuItems`, etc.).
- [ ] **6.4** Remove old `getControlOptionsRegion()` / `switch(canvasElementType)` from `CanvasToolControls.tsx`.
- [ ] **6.5** Remove old toolbar-item building code (`makeToolbarButton`, `getToolbarItemForButton`, etc.).
- [ ] **6.6** Replace old `canvasElementDefinitions` with new `canvasElementDefinitionsNew`; rename to `canvasElementDefinitions`.
- [ ] **6.7** Remove old types: `CanvasElementMenuSection`, `CanvasElementToolbarButton`, `CanvasElementCommandId`, old `ICanvasElementDefinition`.
- [ ] **6.8** Update imports throughout codebase to use new module paths.
- [ ] **6.9** Run full e2e test suite again to confirm no regressions.

---

## Phase 7 — Cleanup

- [ ] **7.1** Rename `canvasElementNewDefinitions.ts` → merge into `canvasElementDefinitions.ts`.
- [ ] **7.2** Remove any dead code, unused imports, temp adapter scaffolding.
- [ ] **7.3** Verify `none` fallback definition still provides graceful degradation for unrecognized types.
- [ ] **7.4** Update `AGENTS.md` / `README` documentation if architecture descriptions need updating.
- [ ] **7.5** Final e2e test pass.

---

## File Map (new files)

| File | Purpose |
|------|---------|
| `bookEdit/toolbox/canvas/canvasControlTypes.ts` | All type definitions for the control system |
| `bookEdit/toolbox/canvas/canvasControlRegistry.ts` | `controlRegistry` + `controlSections` |
| `bookEdit/toolbox/canvas/canvasAvailabilityPresets.ts` | Shared `availabilityRules` presets |
| `bookEdit/toolbox/canvas/canvasElementNewDefinitions.ts` | New element definitions (11 types) |
| `bookEdit/toolbox/canvas/buildControlContext.ts` | `buildControlContext()` DOM → `IControlContext` |
| `bookEdit/toolbox/canvas/canvasControlHelpers.ts` | `getToolbarItems`, `getMenuSections`, `getToolPanelControls` |
| `bookEdit/toolbox/canvas/panelControls/BubbleStyleControl.tsx` | Style dropdown component |
| `bookEdit/toolbox/canvas/panelControls/ShowTailControl.tsx` | Show Tail checkbox component |
| `bookEdit/toolbox/canvas/panelControls/RoundedCornersControl.tsx` | Rounded Corners checkbox component |
| `bookEdit/toolbox/canvas/panelControls/OutlineColorControl.tsx` | Outline Color dropdown component |
| `bookEdit/toolbox/canvas/panelControls/TextColorControl.tsx` | Text Color picker component |
| `bookEdit/toolbox/canvas/panelControls/BackgroundColorControl.tsx` | Background Color picker component |
| `bookEdit/toolbox/canvas/panelControls/ImageFillModeControl.tsx` | Image Fill Mode dropdown component |

## Files Modified (existing)

| File | Change |
|------|--------|
| `CanvasElementContextControls.tsx` | Add dual-path adapter for toolbar + menu rendering |
| `CanvasToolControls.tsx` | Add dual-path adapter for tool-panel rendering |
| `canvasElementDefinitions.ts` | Eventually replaced by new definitions |
| `canvasElementTypes.ts` | No change (types remain the same) |

---

## Risk Notes

- **Biggest risk:** subtle focus-management regressions. The current `ignoreFocusChanges` / `skipNextFocusChange` dance is brittle and must be preserved exactly.
- **Second risk:** audio submenu behavior. The two variants (image vs text) have different label-computation logic, different submenu item sets, and async state dependencies.
- **Third risk:** tool-panel Comical dependency. Panel control components need access to `Bubble`/`BubbleSpec` from the Comical library, and some state (like `isChild`, `isBubble`, `styleSupportsRoundedCorners`) depends on it.
- **Helpful:** the existing e2e test infrastructure provides a safety net. Expanding coverage before starting implementation (Phase 1.9) significantly reduces regression risk.
