# Canvas Elements: Registry-driven context menu + mini-toolbar

This folder contains the **declarative system** that controls what operations are offered for different kinds of “canvas elements” (the overlays that sit on top of images).

At a high level:

- The **page iframe** owns the editing engine (`CanvasElementManager`).
- The **React UI** (`CanvasElementContextControls`) renders the context menu + mini-toolbar for the currently-selected canvas element.
- A small, dependency-light **registry** (`canvasElementDefinitions`) describes which menu sections, toolbar controls, and tool-panel sections each element type supports.
- Element “type” is determined by **DOM inference** (`inferCanvasElementType`).

Important constraint (current product-cycle requirement):

- **No new book HTML format changes.** This system does **not** persist a type marker into the document. Everything is derived from the DOM.

## Intentional overrides (read this first)

Some behavior in this system is intentionally non-default to satisfy product constraints:

- **Unknown inferred type falls back to `none` controls** instead of throwing, so mixed-version content degrades safely.
- **Navigation image buttons hide `missingMetadata` on the toolbar** but still allow it in the menu.
- **Link-grid toolbar text uses primary blue** to match existing clickable toolbar affordances.
- **Canvas control spacing is normalized via one stack `gap` rule**, and canvas clears `BloomCheckbox` default top padding in this context to avoid uneven spacing.

## Key files (start here)

### The registry
- `canvasElementDefinitions.ts`
  - The central registry: `canvasElementDefinitions: Record<CanvasElementType, ICanvasElementDefinition>`
  - Each entry lists `menuSections`, `toolbar`, and `toolPanel` sections used to resolve menu rows, mini-toolbar buttons, and right-panel controls.

- `canvasElementTypes.ts`
  - The canonical union type `CanvasElementType`.

### Type inference (no persistence)
- `canvasElementTypeInference.ts`
  - `inferCanvasElementType(canvasElement: HTMLElement): CanvasElementType | undefined`
  - Inference based on existing DOM structure/classes.
  - Unknown/undefined inferred types are logged and fall back to `none` controls.
  - **Keep this file dependency-light** because it is imported across bundle boundaries.

### Shared DOM constants & helpers (dependency-light)
- `canvasElementConstants.ts`
  - Shared class/selector constants like `kCanvasElementClass`, `kBloomCanvasClass`, etc.

- `canvasElementDomUtils.ts`
  - DOM helpers like `updateCanvasElementClass()`.

- `canvasElementDraggables.ts`
  - Draggable-related helpers (used by game tools).

### Cross-frame bridge to the page editor engine
- `canvasElementUtils.ts`
  - `getCanvasElementManager()` fetches the page-frame manager via bundle exports.
  - This file intentionally imports `bloomFrames` and is therefore *not* dependency-light.
  - Prefer importing selectors/constants from `canvasElementConstants.ts` instead.

## How the context menu + mini-toolbar are built

The main UI is implemented in:

- `../../js/canvasElementManager/CanvasElementContextControls.tsx`

That component:

1. **Infers a type**: `inferCanvasElementType(props.canvasElement)`.
2. Builds a control context using `buildControlContext()`.
3. Resolves menu/toolbar controls from `canvasElementDefinitions` via `getMenuSections()` and `getToolbarItems()` in `canvasControlHelpers.ts`.
4. Applies per-control availability rules and renders the resolved rows/buttons.

## Architecture flow diagrams

### Resolver layer role (`canvasControlHelpers.ts`)

`canvasControlHelpers.ts` is the resolver layer that takes declarative inputs plus runtime state and emits the concrete controls the UI will render.

Inputs it combines:

- **Element definition** from `canvasElementDefinitions.ts` (what this element type is allowed to show/order).
- **Runtime instance context (`ctx`)** from `buildControlContext.ts` (what this specific selected DOM element can show *right now*).
- **Global "kitchen sink" control catalog** from `canvasControlRegistry.ts`:
  - `controlRegistry` = all known controls and their behavior.
  - `controlSections` = section-to-control grouping for each surface.

Outputs it emits:

- `getMenuSections(...)` -> section-ordered `IResolvedControl[][]` with menu rows attached.
- `getToolbarItems(...)` -> ordered toolbar items (`IResolvedControl` + optional `"spacer"`).
- `getToolPanelControls(...)` -> ordered panel components for the right tool panel.

### Menu rendering flow (selected DOM element -> rendered menu, with file ownership)

```text
[Selected canvas element DOM node]
        |
        v
[CanvasElementContextControls.tsx]
CanvasElementContextControls(props.canvasElement)
        |
        v
[buildControlContext.ts]
buildControlContext(canvasElement)
  - calls inferCanvasElementType(...)
      in [canvasElementTypeInference.ts]
  - inferCanvasElementType(canvasElement)
  - compute capability/state flags (hasImage, isInDraggableGame, ...)
        |
        v
[canvasElementDefinitions.ts]
Lookup element definition
  canvasElementDefinitions[ctx.elementType] ?? canvasElementDefinitions.none
        |
        v
[canvasControlHelpers.ts]
getMenuSections(definition, ctx, runtime)
  - takes 3 inputs:
      1) per-element definition from [canvasElementDefinitions.ts]
      2) runtime instance context (`ctx`) from [buildControlContext.ts]
      3) global catalog (`controlRegistry` + `controlSections`) from [canvasControlRegistry.ts]
  - emits section-ordered resolved rows (`IResolvedControl[][]`)
        |
        v
[CanvasElementContextControls.tsx]
CanvasElementContextControls.convertControlMenuRows(...)
  - converts IControlMenuRow[] into IMenuItemWithSubmenu[]
    (shape used by localizable menu components)
  - attaches onClick handlers
        |
        v
[CanvasElementContextControls.tsx]
joinMenuSectionsWithSingleDividers(...)
  - keep deterministic section order
  - add exactly one divider between non-empty sections
        |
        v
[CanvasElementContextControls.tsx render()]
menuOptions.map(...) -> <Menu> + <LocalizableMenuItem/>
                     + <LocalizableNestedMenuItem/>
```

In other words: yes, the renderer reads a data structure and converts it to MUI menu nodes.
- Source data structure: `IControlMenuRow[]` resolved by `getMenuSections()` in `canvasControlHelpers.ts`.
- UI-ready structure: `IMenuItemWithSubmenu[]` produced by `convertControlMenuRows()` in `CanvasElementContextControls.tsx`.
- Final render: `menuOptions.map(...)` in `CanvasElementContextControls.tsx` returns MUI `Menu`, `LocalizableMenuItem`, and `LocalizableNestedMenuItem` elements.

### Deterministic ordering and dividers

`CanvasElementContextControls.tsx` uses a single helper that guarantees:

- Fixed section ordering
- Exactly one divider between *non-empty* sections

See `joinMenuSectionsWithSingleDividers()`.

### The “section” model

The registry and UI both use section IDs (`SectionId` in `canvasControlTypes.ts`), with section contents defined in `controlSections` in `canvasControlRegistry.ts`.

Current menu section IDs:

- `url`
- `video`
- `image`
- `audio`
- `bubble` (e.g. “Add Child Bubble”)
- `text`
- `wholeElement`

Per-element menu section order is defined by each element definition's `menuSections` array in `canvasElementDefinitions.ts`.

### Mini-toolbar

The mini-toolbar is driven by `toolbar` in `canvasElementDefinitions.ts`.

- `toolbar` is the **sole source of truth** for which toolbar controls exist for a given element type, and the order they appear.
- The list supports explicit spacing using the special token `"spacer"`.
- `CanvasElementContextControls.tsx` still performs runtime capability checks (e.g. only show `missingMetadata` when metadata is missing).

Mini-toolbar render ownership:

```text
[canvasElementDefinitions.ts]
definition.toolbar (ordered control ids + optional "spacer")
  |
  v
[canvasControlHelpers.ts]
getToolbarItems(definition, ctx, runtime)
  - combines definition + runtime ctx + controlRegistry/controlSections
  - applies visibility/enabled rules
  - normalizes spacer placement
  - emits ordered resolved toolbar items
  |
  v
[CanvasElementContextControls.tsx]
getToolbarItemForResolvedControl(...)
  - converts each resolved item into a React node/button
  |
  v
[CanvasElementContextControls.tsx render()]
toolbarItems.map(...) renders mini-toolbar UI
```

## Guide: common tasks

### How to add a new canvas element *type*

Example: you want a new element type `sticker`.

1. Add the type to `CanvasElementType` in `canvasElementTypes.ts`.

2. Add a definition in `canvasElementDefinitions.ts`:

   - Decide which sections are relevant to your new type.
  - Add the list to `menuSections`.

3. Update `inferCanvasElementType()` in `canvasElementTypeInference.ts` so the new type can be detected reliably.

  - Do not add new persisted markers/attributes to book HTML to support this system.

4. Ensure creation code produces a DOM structure that inference can recognize.

   - New canvas elements are created by the page engine (`CanvasElementManager.addCanvasElement(...)`) and/or by tool UI that calls into it.
   - If your element is created from the toolbox, make sure the created DOM contains the marker(s) you rely on in inference.

5. Update UI behavior (optional):

   - If you need special menu items or toolbar buttons, add them to the appropriate section array in `CanvasElementContextControls.tsx`.

### How to change what shows in the element toolbar

The toolbar visibility is controlled in two layers:

1. **Registry-level definition**
  - Edit the element’s `toolbar` in `canvasElementDefinitions.ts`.
   - This list defines **all** mini-toolbar controls (and their order) for that element type.
   - Insert `"spacer"` entries where you want visual separation.

2. **Runtime capability checks in `CanvasElementContextControls.tsx`**
   - Some controls still depend on current state, e.g.:
     - `missingMetadata` only shows when metadata is missing
     - certain buttons may be suppressed in game contexts

### How to add a new menu/toolbar *section*

Add a new section only if it is truly a distinct group that should be separated by a divider/spacer.

1. Add a new string literal to `SectionId` in `canvasControlTypes.ts`.
2. Add a section entry to `controlSections` in `canvasControlRegistry.ts`.
3. Map controls to that section's `menu` and/or `toolPanel` surfaces.
4. Update relevant `menuSections` and/or `toolPanel` lists in `canvasElementDefinitions.ts`.

Because the menu joiner adds exactly one divider between non-empty sections, a “new section” is the right tool when you want a guaranteed HR between groups (e.g. separating “Add Child Bubble” from other text actions).

## Troubleshooting

### A menu section disappeared

- Check the inferred type: `inferCanvasElementType()` might be returning a different type than expected.
- Check the registry entry for that type in `canvasElementDefinitions.ts`.
- Check runtime checks in `CanvasElementContextControls.tsx` that may be preventing item creation (e.g. nav buttons, draggability constraints).

### Don’t introduce file-format changes

- Do not add new persisted `data-*` markers to canvas elements to support this system.
- Keep inference based on existing DOM structure/classes.

## Notes on bundle boundaries

This system is designed to avoid accidental runtime import cycles:

- Keep `canvasElementConstants.ts`, `canvasElementDomUtils.ts`, `canvasElementDraggables.ts`, and `canvasElementTypeInference.ts` dependency-light.
- Keep cross-frame access to the page-frame manager in `canvasElementUtils.ts` (bridge module).

## Where the page-side editing logic lives

The editing engine is in the page iframe and is centered around `CanvasElementManager`.

Related helpers (split out of the original large manager file) live under:

- `../../js/canvasElementManager/`

Public entry points that other code can call without pulling in the whole manager live in:

- `../../js/canvasElementManager/CanvasElementManagerPublicFunctions.ts`
