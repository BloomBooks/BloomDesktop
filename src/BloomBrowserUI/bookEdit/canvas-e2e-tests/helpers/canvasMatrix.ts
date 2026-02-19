// Data-driven matrix that maps every palette-draggable canvas element type to
// its expected UI contract: which menu sections appear, which toolbar buttons
// show, which toolbox attribute controls are visible, and whether the type
// supports draggable-toggle behavior.
//
// This matrix is the single source of truth for contract/registry tests. Keep
// it in sync with `canvasElementDefinitions.ts` and `CanvasToolControls.tsx`.

import type {
    CanvasPaletteItemKey,
    CanvasToolboxControlKey,
} from "./canvasSelectors";
import type { CanvasElementType } from "../../toolbox/canvas/canvasElementTypes";

// ── Types ───────────────────────────────────────────────────────────────

export interface ICanvasMatrixRow {
    /** Key that matches a `canvasSelectors.toolbox.paletteItems` entry. */
    paletteItem: CanvasPaletteItemKey;
    /** The `CanvasElementType` string this palette item creates. */
    expectedType: string;
    /** Toolbox attribute controls visible when this element type is selected. */
    expectedToolboxControls: CanvasToolboxControlKey[];
    /** True if the element can be toggled to a draggable in game context. */
    supportsDraggableToggle: boolean;
    /** True if the navigation TriangleCollapse must be opened first. */
    requiresNavigationExpand: boolean;
    /** Context menu labels expected to appear for this element. */
    menuCommandLabels: string[];
}

const makeMatrixRow = (props: {
    paletteItem: CanvasPaletteItemKey;
    expectedType: CanvasElementType;
    expectedToolboxControls: CanvasToolboxControlKey[];
    supportsDraggableToggle: boolean;
    requiresNavigationExpand: boolean;
    menuCommandLabels: string[];
}): ICanvasMatrixRow => {
    return {
        paletteItem: props.paletteItem,
        expectedType: props.expectedType,
        expectedToolboxControls: props.expectedToolboxControls,
        supportsDraggableToggle: props.supportsDraggableToggle,
        requiresNavigationExpand: props.requiresNavigationExpand,
        menuCommandLabels: props.menuCommandLabels,
    };
};

// ── Matrix rows ─────────────────────────────────────────────────────────

export const canvasMatrix: ICanvasMatrixRow[] = [
    // ─── Row 1 palette items ───
    makeMatrixRow({
        paletteItem: "speech",
        expectedType: "speech",
        expectedToolboxControls: [
            "styleDropdown",
            "showTailCheckbox",
            "roundedCornersCheckbox",
            "textColorBar",
            "backgroundColorBar",
            "outlineColorDropdown",
        ],
        supportsDraggableToggle: false,
        requiresNavigationExpand: false,
        menuCommandLabels: ["Duplicate", "Delete"],
    }),
    makeMatrixRow({
        paletteItem: "image",
        expectedType: "image",
        expectedToolboxControls: [],
        supportsDraggableToggle: false,
        requiresNavigationExpand: false,
        menuCommandLabels: ["Duplicate", "Delete"],
    }),
    makeMatrixRow({
        paletteItem: "video",
        expectedType: "video",
        expectedToolboxControls: [],
        supportsDraggableToggle: false,
        requiresNavigationExpand: false,
        menuCommandLabels: ["Duplicate", "Delete"],
    }),

    // ─── Row 2 palette items ───
    makeMatrixRow({
        paletteItem: "text",
        expectedType: "speech",
        expectedToolboxControls: [
            "styleDropdown",
            "showTailCheckbox",
            "roundedCornersCheckbox",
            "textColorBar",
            "backgroundColorBar",
            "outlineColorDropdown",
        ],
        supportsDraggableToggle: false,
        requiresNavigationExpand: false,
        menuCommandLabels: ["Duplicate", "Delete"],
    }),
    makeMatrixRow({
        paletteItem: "caption",
        expectedType: "caption",
        expectedToolboxControls: [
            "styleDropdown",
            "showTailCheckbox",
            "roundedCornersCheckbox",
            "textColorBar",
            "backgroundColorBar",
            "outlineColorDropdown",
        ],
        supportsDraggableToggle: false,
        requiresNavigationExpand: false,
        menuCommandLabels: ["Duplicate", "Delete"],
    }),

    // ─── Navigation palette items (require expanding TriangleCollapse) ───
    makeMatrixRow({
        paletteItem: "navigation-image-with-label-button",
        expectedType: "navigation-image-with-label-button",
        expectedToolboxControls: ["textColorBar", "backgroundColorBar"],
        supportsDraggableToggle: false,
        requiresNavigationExpand: true,
        menuCommandLabels: ["Set Destination", "Duplicate", "Delete"],
    }),
    makeMatrixRow({
        paletteItem: "navigation-image-button",
        expectedType: "navigation-image-button",
        expectedToolboxControls: ["backgroundColorBar"],
        supportsDraggableToggle: false,
        requiresNavigationExpand: true,
        menuCommandLabels: ["Set Destination", "Duplicate", "Delete"],
    }),
    makeMatrixRow({
        paletteItem: "navigation-label-button",
        expectedType: "navigation-label-button",
        expectedToolboxControls: ["textColorBar", "backgroundColorBar"],
        supportsDraggableToggle: false,
        requiresNavigationExpand: true,
        menuCommandLabels: ["Set Destination", "Duplicate", "Delete"],
    }),
    makeMatrixRow({
        paletteItem: "book-link-grid",
        expectedType: "book-link-grid",
        expectedToolboxControls: ["backgroundColorBar"],
        supportsDraggableToggle: false,
        requiresNavigationExpand: true,
        menuCommandLabels: ["Choose books..."],
    }),
];

// ── Convenience accessors ───────────────────────────────────────────────

/** Return the matrix row for a given palette item key, or throw. */
export const getMatrixRow = (
    paletteItem: CanvasPaletteItemKey,
): ICanvasMatrixRow => {
    const row = canvasMatrix.find((r) => r.paletteItem === paletteItem);
    if (!row) {
        throw new Error(
            `canvasMatrix has no row for palette item "${paletteItem}".`,
        );
    }
    return row;
};

/** Rows that can be dragged without expanding the Navigation section. */
export const mainPaletteRows = canvasMatrix.filter(
    (r) => !r.requiresNavigationExpand,
);

/** Rows that require the Navigation section to be expanded first. */
export const navigationPaletteRows = canvasMatrix.filter(
    (r) => r.requiresNavigationExpand,
);

// ── Legacy compatibility ────────────────────────────────────────────────
// The original matrix shape used by spec 04. Kept for backwards compatibility
// during the transition; new specs should use `canvasMatrix` directly.

export interface ICanvasPaletteExpectation {
    paletteItem: CanvasPaletteItemKey;
    menuCommandLabels: string[];
    expectedToolboxControls: CanvasToolboxControlKey[];
}

export const canvasPaletteExpectations: ICanvasPaletteExpectation[] =
    canvasMatrix.map((row) => ({
        paletteItem: row.paletteItem,
        menuCommandLabels: row.menuCommandLabels,
        expectedToolboxControls: row.expectedToolboxControls,
    }));
