# canvasElementManager

## High-level structure

### Core orchestrator
- `CanvasElementManager.ts`
  - Main runtime coordinator used by page code.
  - Wires providers/controllers and preserves public surface used elsewhere.

### Public entry points used by other modules
- `CanvasElementManagerPublicFunctions.ts`
  - Lightweight exports intended for external callers.
  - Keeps external consumers from needing full manager import when possible.

### UI/context controls
- `CanvasElementContextControls.tsx`
- `CanvasElementSelectionUi.ts`

### Input/guide/snap providers
- `CanvasElementKeyboardProvider.ts`
- `CanvasGuideProvider.ts`
- `CanvasSnapProvider.ts`

### manager subsystems
- `CanvasElementFactories.ts`
- `CanvasElementClipboard.ts`
- `CanvasElementDuplication.ts`
- `CanvasElementPointerInteractions.ts`
- `CanvasElementHandleDragInteractions.ts`
- `CanvasElementEditingSuspension.ts`
- `CanvasElementDraggableIntegration.ts`
- `CanvasElementCanvasResizeAdjustments.ts`
- `CanvasElementBackgroundImageManager.ts`
- `CanvasElementAlternates.ts`
- `CanvasElementGeometry.ts`
- `CanvasElementPositioning.ts`
- `CanvasElementBubbleLevelUtils.ts`
- `CanvasElementSharedTypes.ts`

