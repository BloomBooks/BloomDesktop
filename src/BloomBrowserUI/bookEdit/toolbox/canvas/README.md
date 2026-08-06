# Canvas Elements: Registry-driven context menu + mini-toolbar

This folder contains the **declarative system** that controls what operations are offered for different kinds of “canvas elements” (the overlays that sit on top of images).

At a high level:

- The **page iframe** owns the editing engine (`CanvasElementManager`).
- The **React UI** (`CanvasElementContextControls`) renders the context menu + mini-toolbar for the currently-selected canvas element.
- A small, dependency-light **registry** (`canvasElementControlRegistry`) maps each canvas element type to the menu sections, toolbar buttons, tool-panel sections, and availability overrides it supports.
- The concrete command behavior, labels, icons, hints, and panel components live in `canvasControlRegistry.ts`.
- Element “type” is determined by **DOM inference** (`inferCanvasElementType`).

Important constraint (current product-cycle requirement):

- **No new book HTML format changes.** This system does **not** persist a type marker into the document. Everything is derived from the DOM.

## Key files (start here)

### The registry
- `canvasElementControlRegistry.ts`
  - The central registry: `canvasElementControlRegistry: Record<CanvasElementType, ICanvasElementControlConfiguration>`
  - Each entry lists the allowed `menuSections`, `toolbar`, `toolPanel`, and any per-control availability overrides.

- `canvasControlRegistry.ts`
  - The concrete command/panel registry.
  - Each control entry provides the implementation, labels, icons, hints, and any menu-row construction.

- `canvasElementTypes.ts`
  - The canonical union type `CanvasElementType`.

### Type inference (no persistence)
- `canvasElementTypeInference.ts`
  - `inferCanvasElementType(canvasElement: HTMLElement): CanvasElementType | undefined`
  - Inference based on existing DOM structure/classes.
  - `CanvasElementContextControls` falls back to `none` if inference returns `undefined` or an unregistered type, so mixed-version content degrades safely.
  - **Keep this file dependency-light** because it is imported across bundle boundaries.

### Shared DOM constants & helpers (dependency-light)
- `canvasElementConstants.ts`
  - Shared class/selector constants like `kCanvasElementClass`, `kBloomCanvasClass`, etc.

- `canvasElementDomUtils.ts`
  - DOM helpers like `updateCanvasElementClass()`.

- `canvasElementDraggables.ts`
  - Draggable-related helpers (used by game tools).

### Cross-frame bridge to the page editor engine
- `canvasElementPageBridge.ts`
  - `getCanvasElementManager()` fetches the page-frame manager via bundle exports.
  - This file intentionally imports `bloomFrames` and is therefore *not* dependency-light.
  - It is specifically a Toolbox-to-page bridge module, not a catch-all utils file.
  - Prefer importing selectors/constants from `canvasElementConstants.ts` instead.

## How the context menu + mini-toolbar are built

The main UI is implemented in:

- `../../js/CanvasElementContextControls.tsx`

That component:

1. **Infers a type**: `inferCanvasElementType(props.canvasElement)`.
2. Looks up allowed sections from `canvasElementControlRegistry`.
3. Builds menu-item arrays for each section (e.g. `urlMenuItems`, `imageMenuItems`, …).
4. Assembles them in a fixed order and filters by the registry.

### Deterministic ordering and dividers

`CanvasElementContextControls.tsx` uses a single helper that guarantees:

- Fixed section ordering
- Exactly one divider between *non-empty* sections

See `joinMenuSectionsWithSingleDividers()`.

### The “section” model

The registry and UI both use `CanvasElementMenuSection` (in `canvasElementControlRegistry.ts`). Current menu sections:

- `url`
- `video`
- `image`
- `audio`
- `bubble` (e.g. “Add Child Bubble”)
- `text`
- `wholeElementCommands`

The `orderedMenuSections` list in `CanvasElementContextControls.tsx` is the authoritative menu section order.

### Mini-toolbar

The mini-toolbar is driven by `toolbarButtons` in `canvasElementControlRegistry.ts`.

- `toolbarButtons` is the **sole source of truth** for which toolbar controls exist for a given canvas element type, and the order they appear.
- The list supports explicit spacing using the special token `"spacer"`.
- `CanvasElementContextControls.tsx` still performs runtime capability checks (e.g. only show `missingMetadata` when metadata is missing).

## Guide: common tasks

### How to add a new canvas element *type*

Example: you want a new canvas element type `sticker`.

1. Add the type to `CanvasElementType` in `canvasElementTypes.ts`.

2. Add a definition in `canvasElementControlRegistry.ts`:

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
  - Edit the element’s `toolbarButtons` in `canvasElementControlRegistry.ts`.
  - This list defines **all** mini-toolbar controls (and their order) for that canvas element type.
   - Insert `"spacer"` entries where you want visual separation.

2. **Runtime capability checks in `CanvasElementContextControls.tsx`**
   - Some controls still depend on current state, e.g.:
     - `missingMetadata` only shows when metadata is missing
     - certain buttons may be suppressed in game contexts

### How to add a new menu/toolbar *section*

Add a new section only if it is truly a distinct group that should be separated by a divider/spacer.

1. Add a new string literal to `CanvasElementMenuSection` in `canvasElementControlRegistry.ts`.
2. Add the new section to `orderedMenuSections` in `CanvasElementContextControls.tsx`.
3. Add the matching menu-item array and populate it.
4. Update relevant `menuSections` lists for types that should show it.

Because the menu joiner adds exactly one divider between non-empty sections, a “new section” is the right tool when you want a guaranteed HR between groups (e.g. separating “Add Child Bubble” from other text actions).

## Troubleshooting

### A menu section disappeared

- Check the inferred type: `inferCanvasElementType()` might be returning a different type than expected.
- Check the registry entry for that type in `canvasElementControlRegistry.ts`.
- Check runtime checks in `CanvasElementContextControls.tsx` that may be preventing item creation (e.g. nav buttons, draggability constraints).

### Don’t introduce file-format changes

- Do not add new persisted `data-*` markers to canvas elements to support this system.
- Keep inference based on existing DOM structure/classes.

## Notes on bundle boundaries

This system is designed to avoid accidental runtime import cycles:

- Keep `canvasElementConstants.ts`, `canvasElementDomUtils.ts`, `canvasElementDraggables.ts`, and `canvasElementTypeInference.ts` dependency-light.
- Keep cross-frame access to the page-frame manager in `canvasElementPageBridge.ts` (bridge module).

## Where the page-side editing logic lives

The editing engine is in the page iframe and is centered around `CanvasElementManager`.

Related helpers (split out of the original large manager file) live under:

- `../../js/canvasElementManager/`

Public entry points that other code can call without pulling in the whole manager live in:

- `../../js/canvasElementManager/CanvasElementToolboxBridge.ts`
