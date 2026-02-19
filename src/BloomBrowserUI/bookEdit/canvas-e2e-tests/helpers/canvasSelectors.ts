// Centralized selectors for canvas Playwright tests.
//
// Naming convention:
//   - Selectors in this file are the single source of truth for locating UI elements.
//   - Specs should NEVER contain ad-hoc CSS/XPath selectors; always reference this module.
//   - Keys in `paletteItems` match the CanvasElementType values where possible.

export const canvasSelectors = {
    toolbox: {
        root: "#canvasToolControls",
        paletteItems: {
            // Row 1: speech bubble, image placeholder, video
            speech: 'img[draggable="true"][src*="comic-icon.svg"]',
            // ImagePlaceholderIcon renders an SVG with viewBox="0 0 352 348"
            image: '[draggable="true"] svg[viewBox="0 0 352 348"]',
            video: 'img[draggable="true"][src*="sign-language-overlay.svg"]',
            // Row 2: text block, caption (Span l10n component renders as <span>)
            text: 'span[draggable="true"]:has-text("Text Block")',
            caption: 'span[draggable="true"]:has-text("Caption")',
            // Navigation section (inside TriangleCollapse, initially collapsed)
            "navigation-image-with-label-button":
                '[draggable="true"] img[src*="imageWithLabelButtonPaletteItem.svg"]',
            "navigation-image-button":
                '[draggable="true"] img[src*="imageButtonPaletteItem.svg"]',
            "navigation-label-button":
                '[draggable="true"] img[src*="labelButtonPaletteItem.svg"]',
            "book-link-grid":
                'img[draggable="true"][src*="bookGridPaletteItem.svg"]',
        },
        // Navigation section toggle (collapsed by default)
        navigationCollapseToggle: 'div:has-text("Navigation") >> button',
        optionsRegion: "#canvasToolControlOptionsRegion",
        noOptionsSection: "#noOptionsSection",
        // Toolbox attribute controls
        styleDropdown: "#canvasElement-style-dropdown",
        outlineColorDropdown: "#canvasElement-outlineColor-dropdown",
        showTailCheckbox: 'label:has-text("Show Tail") input[type="checkbox"]',
        roundedCornersCheckbox:
            'label:has-text("Rounded Corners") input[type="checkbox"]',
        // ColorBar component doesn't apply the id prop; use the parent
        // FormControl's label[for] to locate the sibling color bar div.
        textColorBar:
            ':has(> label[for="text-color-bar"]) > .MuiInput-formControl',
        backgroundColorBar:
            ':has(> label[for="background-color-bar"]) > .MuiInput-formControl',
    },
    page: {
        canvas: ".bloom-canvas",
        canvasElements: ".bloom-canvas-element",
        activeCanvasElement: '[data-bloom-active="true"]',
        hasCanvasElementClass: ".bloom-has-canvas-element",
        backgroundImage: ".bloom-backgroundImage",
        // Context controls overlay on the page frame
        contextControls: "#canvas-element-context-controls",
        contextControlsVisible: "#canvas-element-context-controls:visible",
        contextToolbar: "#canvas-element-context-controls",
        contextToolbarButtons: "#canvas-element-context-controls button",
        contextToolbarMenuButton:
            "#canvas-element-context-controls button:last-of-type",
        contextMenuList: ".MuiMenu-list",
        contextMenuListVisible: ".MuiMenu-list:visible",
        contextMenuItems: ".MuiMenu-list li[role='menuitem']",
        // Canvas element internals
        bloomEditable: ".bloom-editable",
        imageContainer: ".bloom-imageContainer",
        videoContainer: ".bloom-videoContainer",
        translationGroup: ".bloom-translationGroup",
        // Draggable attributes
        draggableElement: "[data-draggable-id]",
        targetElement: "[data-target-of]",
        // Selection / resize handles
        selectionFrame: ".bloom-ui-selectionFrame",
        resizeHandles: ".bloom-ui-resize-handle",
    },
} as const;

export type CanvasPaletteItemKey =
    keyof typeof canvasSelectors.toolbox.paletteItems;

export type CanvasToolboxControlKey =
    | "styleDropdown"
    | "showTailCheckbox"
    | "roundedCornersCheckbox"
    | "textColorBar"
    | "backgroundColorBar"
    | "outlineColorDropdown";

export const toolboxControlSelectorMap: Record<
    CanvasToolboxControlKey,
    string
> = {
    styleDropdown: canvasSelectors.toolbox.styleDropdown,
    showTailCheckbox: canvasSelectors.toolbox.showTailCheckbox,
    roundedCornersCheckbox: canvasSelectors.toolbox.roundedCornersCheckbox,
    textColorBar: canvasSelectors.toolbox.textColorBar,
    backgroundColorBar: canvasSelectors.toolbox.backgroundColorBar,
    outlineColorDropdown: canvasSelectors.toolbox.outlineColorDropdown,
};

export const getContextMenuItemSelector = (label: string): string => {
    return `${canvasSelectors.page.contextMenuList} li:has-text("${label}")`;
};
