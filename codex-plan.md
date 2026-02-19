# Codex Implementation Plan

## Objective
- [ ] Complete the canvas controls refactor to a registry-driven model without changing current user behavior.

## Phase 1: Baseline and Inventory
- [ ] Confirm current command surfaces (toolbar, context menu, canvas tool panel) and expected behavior per element type.
- [ ] Catalog existing control IDs, labels, icons, actions, and subscription gating usage.
- [ ] Identify duplicated control logic that should be centralized in the control registry.
- [ ] Capture migration rename map and parity notes (`wholeElementCommands` → `wholeElement`, book-link-grid `text` section mapping → `linkGrid`).

## Phase 2: Registry and Types
- [ ] Define/verify `ControlId` coverage for all top-level and dynamic menu rows.
- [ ] Finalize shared control definition types for command controls and panel-only controls.
- [ ] Ensure control definitions support shared presentation metadata and optional `featureName`.
- [ ] Add/verify runtime context shape used by all controls (`IControlContext`, `IControlRuntime`).
- [ ] Add/verify context flags required for enablement parity (`isCropped`, `hasPreviousVideoContainer`, `hasNextVideoContainer`, `currentImageSoundLabel`).
- [ ] Add/verify surface-specific icon metadata for controls that use different toolbar/menu icons.
- [ ] Document and preserve `ICanvasToolsPanelState` Comical coupling (`currentBubble` / `BubbleSpec` path).

## Phase 3: Element Declarations
- [ ] Migrate each canvas element type to declarative control placement (`toolbar`, `menuSections`, `toolPanel`).
- [ ] Move visibility/enabled policy to element availability rules.
- [ ] Replace per-surface ad hoc wiring with registry lookups.
- [ ] Add explicit definitions for all currently supported element types: `image`, `video`, `sound`, `speech`, `rectangle`, `caption`, `navigation-image-button`, `navigation-image-with-label-button`, `navigation-label-button`, `book-link-grid`, and `none`.
- [ ] Define deselected (`canvasElementType === undefined`) tool-panel behavior explicitly (legacy undefined/"text" fallthrough parity without introducing a real `text` element type).

## Phase 4: Rendering Integration
- [ ] Update toolbar rendering to consume registry definitions.
- [ ] Update menu rendering to handle static and dynamic rows from control definitions.
- [ ] Update Canvas Tool panel rendering for panel-only controls.
- [ ] Preserve existing focus/menu-close behavior for command execution.
- [ ] Implement icon resolution precedence (surface override first, then shared icon).
- [ ] Preserve audio image-variant parent label behavior (current sound filename minus `.mp3` when present).

## Phase 5: Subscription and Localization
- [ ] Apply menu row feature resolution rule (row `featureName` overrides parent control `featureName`).
- [ ] Verify subscription-disabled behavior and upgrade affordances match existing UX.
- [ ] Verify all labels/tooltips still resolve through existing localization IDs.

## Phase 6: Validation
- [ ] Run targeted canvas e2e specs for drag/drop, context controls, and menu command behavior.
- [ ] Add/update focused tests only where behavior changed or new dynamic row logic was introduced.
- [ ] Manually smoke-test key element types (image, video, text/bubble, navigation, link grid).
- [ ] Validate enable/disable parity for `resetImage`, `expandToFillSpace`, `playVideoEarlier`, and `playVideoLater`.
- [ ] Validate draggability parity: `togglePartOfRightAnswer` visibility uses draggable id, while checkmark state uses draggable target.
- [ ] Validate text-audio async default parity (`textHasAudio` initializes to true before async resolution).

## Phase 7: Cleanup and Hand-off
- [ ] Remove obsolete control wiring that is superseded by registry-driven paths.
- [ ] Keep public behavior unchanged and avoid unrelated refactors.
- [ ] Prepare PR notes summarizing migrated controls, known risks, and follow-up tasks.

## Definition of Done
- [ ] All canvas controls are declared through the registry + element declarations.
- [ ] No regression in command availability, menu contents, or panel controls.
- [ ] Relevant tests pass locally and no new lint/type errors are introduced in touched files.
