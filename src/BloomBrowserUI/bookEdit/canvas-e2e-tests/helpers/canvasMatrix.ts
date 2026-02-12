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

// ── Types ───────────────────────────────────────────────────────────────

export type CanvasElementMenuSection =
    | "url"
    | "video"
    | "image"
    | "audio"
    | "bubble"
    | "text"
    | "wholeElementCommands";

export type CanvasElementToolbarButton =
    | "spacer"
    | "setDestination"
    | "chooseVideo"
    | "recordVideo"
    | "chooseImage"
    | "pasteImage"
    | "missingMetadata"
    | "expandToFillSpace"
    | "format"
    | "duplicate"
    | "delete"
    | "linkGridChooseBooks";

export interface ICanvasMatrixRow {
    /** Key that matches a `canvasSelectors.toolbox.paletteItems` entry. */
    paletteItem: CanvasPaletteItemKey;
    /** The `CanvasElementType` string this palette item creates. */
    expectedType: string;
    /** Menu section keys expected when this element is selected. */
    menuSections: CanvasElementMenuSection[];
    /** Toolbar button keys expected when this element is selected. */
    toolbarButtons: CanvasElementToolbarButton[];
    /** Toolbox attribute controls visible when this element type is selected. */
    expectedToolboxControls: CanvasToolboxControlKey[];
    /** True if the element can be toggled to a draggable in game context. */
    supportsDraggableToggle: boolean;
    /** True if the navigation TriangleCollapse must be opened first. */
    requiresNavigationExpand: boolean;
    /** Context menu labels expected to appear for this element. */
    menuCommandLabels: string[];
}

// ── Matrix rows ─────────────────────────────────────────────────────────

export const canvasMatrix: ICanvasMatrixRow[] = [
    // ─── Row 1 palette items ───
    {
        paletteItem: "speech",
        expectedType: "speech",
        menuSections: ["audio", "bubble", "text", "wholeElementCommands"],
        toolbarButtons: ["format", "spacer", "duplicate", "delete"],
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
    },
    {
        paletteItem: "image",
        expectedType: "image",
        menuSections: ["image", "audio", "wholeElementCommands"],
        toolbarButtons: [
            "missingMetadata",
            "chooseImage",
            "pasteImage",
            "expandToFillSpace",
            "spacer",
            "duplicate",
            "delete",
        ],
        expectedToolboxControls: [],
        supportsDraggableToggle: false,
        requiresNavigationExpand: false,
        menuCommandLabels: ["Duplicate", "Delete"],
    },
    {
        paletteItem: "video",
        expectedType: "video",
        menuSections: ["video", "wholeElementCommands"],
        toolbarButtons: [
            "chooseVideo",
            "recordVideo",
            "spacer",
            "duplicate",
            "delete",
        ],
        expectedToolboxControls: [],
        supportsDraggableToggle: false,
        requiresNavigationExpand: false,
        menuCommandLabels: ["Duplicate", "Delete"],
    },

    // ─── Row 2 palette items ───
    {
        paletteItem: "text",
        expectedType: "speech",
        menuSections: ["audio", "bubble", "text", "wholeElementCommands"],
        toolbarButtons: ["format", "spacer", "duplicate", "delete"],
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
    },
    {
        paletteItem: "caption",
        expectedType: "caption",
        menuSections: ["audio", "bubble", "text", "wholeElementCommands"],
        toolbarButtons: ["format", "spacer", "duplicate", "delete"],
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
    },

    // ─── Navigation palette items (require expanding TriangleCollapse) ───
    {
        paletteItem: "navigation-image-with-label-button",
        expectedType: "navigation-image-with-label-button",
        menuSections: ["url", "image", "text", "wholeElementCommands"],
        toolbarButtons: [
            "setDestination",
            "chooseImage",
            "pasteImage",
            "spacer",
            "duplicate",
            "delete",
        ],
        expectedToolboxControls: ["textColorBar", "backgroundColorBar"],
        supportsDraggableToggle: false,
        requiresNavigationExpand: true,
        menuCommandLabels: ["Duplicate", "Delete"],
    },
    {
        paletteItem: "navigation-image-button",
        expectedType: "navigation-image-button",
        menuSections: ["url", "image", "wholeElementCommands"],
        toolbarButtons: [
            "setDestination",
            "chooseImage",
            "pasteImage",
            "spacer",
            "duplicate",
            "delete",
        ],
        expectedToolboxControls: ["backgroundColorBar"],
        supportsDraggableToggle: false,
        requiresNavigationExpand: true,
        menuCommandLabels: ["Duplicate", "Delete"],
    },
    {
        paletteItem: "navigation-label-button",
        expectedType: "navigation-label-button",
        menuSections: ["url", "text", "wholeElementCommands"],
        toolbarButtons: ["setDestination", "spacer", "duplicate", "delete"],
        expectedToolboxControls: ["textColorBar", "backgroundColorBar"],
        supportsDraggableToggle: false,
        requiresNavigationExpand: true,
        menuCommandLabels: ["Duplicate", "Delete"],
    },
    {
        paletteItem: "book-link-grid",
        expectedType: "book-link-grid",
        menuSections: ["text"],
        toolbarButtons: ["linkGridChooseBooks"],
        expectedToolboxControls: ["backgroundColorBar"],
        supportsDraggableToggle: false,
        requiresNavigationExpand: true,
        menuCommandLabels: [],
    },
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
