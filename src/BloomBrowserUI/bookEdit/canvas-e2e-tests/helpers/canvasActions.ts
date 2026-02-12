import type { BoundingBox, Frame, Locator, Page } from "playwright/test";
import {
    getPageFrame,
    getToolboxFrame,
    gotoCurrentPage,
    openCanvasToolTab,
    waitForCanvasReady,
} from "./canvasFrames";
import {
    canvasSelectors,
    type CanvasPaletteItemKey,
    getContextMenuItemSelector,
} from "./canvasSelectors";

// ── Types ───────────────────────────────────────────────────────────────

export interface ICanvasTestContext {
    toolboxFrame: Frame;
    pageFrame: Frame;
}

interface IDropOffset {
    x: number;
    y: number;
}

interface IDragPaletteItemParams {
    page: Page;
    toolboxFrame: Frame;
    pageFrame: Frame;
    paletteItem: CanvasPaletteItemKey;
    dropOffset?: IDropOffset;
}

type ResizeCorner = "top-left" | "top-right" | "bottom-left" | "bottom-right";
type ResizeSide = "top" | "right" | "bottom" | "left";

// ── Internal helpers ────────────────────────────────────────────────────

const defaultDropOffset: IDropOffset = {
    x: 160,
    y: 120,
};

const getRequiredBoundingBox = async (
    locator: Locator,
    label: string,
): Promise<BoundingBox> => {
    const box = await locator.boundingBox();
    if (!box) {
        throw new Error(`Could not determine bounding box for ${label}.`);
    }
    return box;
};

const cornerOffsets: Record<ResizeCorner, { xFrac: number; yFrac: number }> = {
    "top-left": { xFrac: 0, yFrac: 0 },
    "top-right": { xFrac: 1, yFrac: 0 },
    "bottom-left": { xFrac: 0, yFrac: 1 },
    "bottom-right": { xFrac: 1, yFrac: 1 },
};

const sideOffsets: Record<ResizeSide, { xFrac: number; yFrac: number }> = {
    top: { xFrac: 0.5, yFrac: 0 },
    right: { xFrac: 1, yFrac: 0.5 },
    bottom: { xFrac: 0.5, yFrac: 1 },
    left: { xFrac: 0, yFrac: 0.5 },
};

// ── Bootstrap ───────────────────────────────────────────────────────────

export const openCanvasToolOnCurrentPage = async (
    page: Page,
): Promise<ICanvasTestContext> => {
    await gotoCurrentPage(page);
    const toolboxFrame = await getToolboxFrame(page);
    const pageFrame = await getPageFrame(page);
    await openCanvasToolTab(toolboxFrame);
    await waitForCanvasReady(pageFrame);

    return {
        toolboxFrame,
        pageFrame,
    };
};

// ── Element count ───────────────────────────────────────────────────────

export const getCanvasElementCount = async (
    pageFrame: Frame,
): Promise<number> => {
    return pageFrame.locator(canvasSelectors.page.canvasElements).count();
};

// ── Drag from palette ───────────────────────────────────────────────────

/**
 * Expand the Navigation TriangleCollapse in the toolbox so that navigation
 * palette items become visible. Idempotent if already expanded.
 */
export const expandNavigationSection = async (
    toolboxFrame: Frame,
): Promise<void> => {
    // Check if a navigation-only palette item is already visible
    const navItem = toolboxFrame
        .locator(
            canvasSelectors.toolbox.paletteItems["navigation-image-button"],
        )
        .first();
    if (await navItem.isVisible().catch(() => false)) {
        return;
    }
    // Click the triangle collapse toggle
    const toggle = toolboxFrame
        .locator(canvasSelectors.toolbox.navigationCollapseToggle)
        .first();
    await toggle.click();
    await navItem.waitFor({ state: "visible", timeout: 5000 });
};

export const dragPaletteItemToCanvas = async (
    params: IDragPaletteItemParams,
): Promise<void> => {
    const paletteSelector =
        canvasSelectors.toolbox.paletteItems[params.paletteItem];
    const source = params.toolboxFrame.locator(paletteSelector).first();
    await source.waitFor({
        state: "visible",
        timeout: 10000,
    });

    const canvas = params.pageFrame
        .locator(canvasSelectors.page.canvas)
        .first();
    await canvas.waitFor({
        state: "visible",
        timeout: 10000,
    });

    const sourceBox = await getRequiredBoundingBox(
        source,
        `palette item ${params.paletteItem}`,
    );
    const canvasBox = await getRequiredBoundingBox(canvas, "canvas surface");
    const offset = params.dropOffset ?? defaultDropOffset;

    const sourceX = sourceBox.x + sourceBox.width / 2;
    const sourceY = sourceBox.y + sourceBox.height / 2;
    const targetX = canvasBox.x + Math.min(offset.x, canvasBox.width - 20);
    const targetY = canvasBox.y + Math.min(offset.y, canvasBox.height - 20);

    await params.page.mouse.move(sourceX, sourceY);
    await params.page.mouse.down();
    await params.page.mouse.move(targetX, targetY, { steps: 16 });
    await params.page.mouse.up();
};

// ── Selection ───────────────────────────────────────────────────────────

export const selectCanvasElementAtIndex = async (
    pageFrame: Frame,
    index: number,
): Promise<Locator> => {
    const element = pageFrame
        .locator(canvasSelectors.page.canvasElements)
        .nth(index);
    await element.waitFor({
        state: "visible",
        timeout: 10000,
    });
    await element.click();
    return element;
};

export const getActiveCanvasElement = (pageFrame: Frame): Locator => {
    return pageFrame.locator(canvasSelectors.page.activeCanvasElement).first();
};

// ── Move by mouse drag ──────────────────────────────────────────────────

export const dragActiveCanvasElementByOffset = async (
    page: Page,
    pageFrame: Frame,
    dx: number,
    dy: number,
): Promise<{ activeElement: Locator; beforeBounds: BoundingBox }> => {
    const activeElement = getActiveCanvasElement(pageFrame);
    await activeElement.waitFor({
        state: "visible",
        timeout: 10000,
    });

    const beforeBounds = await getRequiredBoundingBox(
        activeElement,
        "active canvas element",
    );
    const localStartX = Math.max(6, Math.min(20, beforeBounds.width / 4));
    const localStartY = Math.max(6, Math.min(20, beforeBounds.height / 4));

    const startX = beforeBounds.x + localStartX;
    const startY = beforeBounds.y + localStartY;

    await page.mouse.move(startX, startY);
    await page.mouse.down();
    await page.mouse.move(startX + dx, startY + dy, { steps: 10 });
    await page.mouse.up();

    return {
        activeElement,
        beforeBounds,
    };
};

// ── Resize from corner ──────────────────────────────────────────────────

export const resizeActiveElementFromCorner = async (
    page: Page,
    pageFrame: Frame,
    corner: ResizeCorner,
    dx: number,
    dy: number,
    modifiers?: { shift?: boolean },
): Promise<{ activeElement: Locator; beforeBounds: BoundingBox }> => {
    const activeElement = getActiveCanvasElement(pageFrame);
    await activeElement.waitFor({ state: "visible", timeout: 10000 });

    const beforeBounds = await getRequiredBoundingBox(
        activeElement,
        "active canvas element (resize corner)",
    );

    const { xFrac, yFrac } = cornerOffsets[corner];
    const handleX = beforeBounds.x + beforeBounds.width * xFrac;
    const handleY = beforeBounds.y + beforeBounds.height * yFrac;

    await page.mouse.move(handleX, handleY);
    if (modifiers?.shift) {
        await page.keyboard.down("Shift");
    }
    await page.mouse.down();
    await page.mouse.move(handleX + dx, handleY + dy, { steps: 10 });
    await page.mouse.up();
    if (modifiers?.shift) {
        await page.keyboard.up("Shift");
    }

    return { activeElement, beforeBounds };
};

// ── Resize from side ────────────────────────────────────────────────────

export const resizeActiveElementFromSide = async (
    page: Page,
    pageFrame: Frame,
    side: ResizeSide,
    delta: number,
    modifiers?: { shift?: boolean },
): Promise<{ activeElement: Locator; beforeBounds: BoundingBox }> => {
    const dx = side === "left" || side === "right" ? delta : 0;
    const dy = side === "top" || side === "bottom" ? delta : 0;

    const activeElement = getActiveCanvasElement(pageFrame);
    await activeElement.waitFor({ state: "visible", timeout: 10000 });

    const beforeBounds = await getRequiredBoundingBox(
        activeElement,
        "active canvas element (resize side)",
    );

    const { xFrac, yFrac } = sideOffsets[side];
    const handleX = beforeBounds.x + beforeBounds.width * xFrac;
    const handleY = beforeBounds.y + beforeBounds.height * yFrac;

    await page.mouse.move(handleX, handleY);
    if (modifiers?.shift) {
        await page.keyboard.down("Shift");
    }
    await page.mouse.down();
    await page.mouse.move(handleX + dx, handleY + dy, { steps: 10 });
    await page.mouse.up();
    if (modifiers?.shift) {
        await page.keyboard.up("Shift");
    }

    return { activeElement, beforeBounds };
};

// ── Keyboard nudge ──────────────────────────────────────────────────────

export const keyboardNudge = async (
    page: Page,
    key: "ArrowUp" | "ArrowDown" | "ArrowLeft" | "ArrowRight",
    modifiers?: { ctrl?: boolean; shift?: boolean },
): Promise<void> => {
    const mods: string[] = [];
    if (modifiers?.ctrl) mods.push("Control");
    if (modifiers?.shift) mods.push("Shift");

    const combo = mods.length > 0 ? `${mods.join("+")}+${key}` : key;
    await page.keyboard.press(combo);
};

// ── Context menu / toolbar ──────────────────────────────────────────────

export const openContextMenuFromToolbar = async (
    pageFrame: Frame,
): Promise<void> => {
    const controls = pageFrame
        .locator(canvasSelectors.page.contextControls)
        .first();
    await controls.waitFor({
        state: "visible",
        timeout: 10000,
    });

    const menuButton = controls.locator("button").last();
    await menuButton.click();

    await pageFrame
        .locator(canvasSelectors.page.contextMenuList)
        .first()
        .waitFor({
            state: "visible",
            timeout: 10000,
        });
};

export const clickContextMenuItem = async (
    pageFrame: Frame,
    label: string,
): Promise<void> => {
    const menuItem = pageFrame
        .locator(getContextMenuItemSelector(label))
        .first();
    await menuItem.waitFor({
        state: "visible",
        timeout: 10000,
    });
    await menuItem.click();
};

export const getContextToolbarButtonCount = async (
    pageFrame: Frame,
): Promise<number> => {
    const controls = pageFrame
        .locator(canvasSelectors.page.contextControls)
        .first();
    await controls.waitFor({ state: "visible", timeout: 10000 });
    return controls.locator("button").count();
};

// ── Toolbox attribute controls ──────────────────────────────────────────

export const setStyleDropdown = async (
    toolboxFrame: Frame,
    value: string,
): Promise<void> => {
    const dropdown = toolboxFrame
        .locator("#mui-component-select-style")
        .first();
    await dropdown.waitFor({ state: "visible", timeout: 5000 });
    await dropdown.click();
    // MUI Select renders menu items in a portal
    const option = toolboxFrame
        .locator(
            `.canvasElement-options-dropdown-menu li[data-value="${value}"]`,
        )
        .first();
    await option.waitFor({ state: "visible", timeout: 5000 });
    await option.click();
};

export const setShowTail = async (
    toolboxFrame: Frame,
    enabled: boolean,
): Promise<void> => {
    const checkbox = toolboxFrame
        .locator(canvasSelectors.toolbox.showTailCheckbox)
        .first();
    await checkbox.waitFor({ state: "visible", timeout: 5000 });
    const isChecked = await checkbox.isChecked();
    if (isChecked !== enabled) {
        await checkbox.click();
    }
};

export const setRoundedCorners = async (
    toolboxFrame: Frame,
    enabled: boolean,
): Promise<void> => {
    const checkbox = toolboxFrame
        .locator(canvasSelectors.toolbox.roundedCornersCheckbox)
        .first();
    await checkbox.waitFor({ state: "visible", timeout: 5000 });
    const isChecked = await checkbox.isChecked();
    if (isChecked !== enabled) {
        await checkbox.click();
    }
};

export const clickTextColorBar = async (toolboxFrame: Frame): Promise<void> => {
    const bar = toolboxFrame
        .locator(canvasSelectors.toolbox.textColorBar)
        .first();
    await bar.waitFor({ state: "visible", timeout: 5000 });
    await bar.click();
};

export const clickBackgroundColorBar = async (
    toolboxFrame: Frame,
): Promise<void> => {
    const bar = toolboxFrame
        .locator(canvasSelectors.toolbox.backgroundColorBar)
        .first();
    await bar.waitFor({ state: "visible", timeout: 5000 });
    await bar.click();
};

export const setOutlineColorDropdown = async (
    toolboxFrame: Frame,
    value: string,
): Promise<void> => {
    const dropdown = toolboxFrame
        .locator("#mui-component-select-outlineColor")
        .first();
    await dropdown.waitFor({ state: "visible", timeout: 5000 });
    await dropdown.click();
    const option = toolboxFrame
        .locator(
            `.canvasElement-options-dropdown-menu li[data-value="${value}"]`,
        )
        .first();
    await option.waitFor({ state: "visible", timeout: 5000 });
    await option.click();
};

// ── Coordinate conversion ───────────────────────────────────────────────

/**
 * Convert page-frame-relative coordinates to top-level page coordinates.
 * Useful for cross-iframe assertions where bounding boxes are reported in
 * the page-frame coordinate space but mouse events operate in the top-level
 * coordinate space.
 */
export const pageFrameToTopLevel = async (
    page: Page,
    pageFrame: Frame,
    x: number,
    y: number,
): Promise<{ x: number; y: number }> => {
    const frameElement = await pageFrame.frameElement();
    const frameBox = await frameElement.boundingBox();
    if (!frameBox) {
        throw new Error("Could not get page iframe bounding box.");
    }
    return { x: x + frameBox.x, y: y + frameBox.y };
};
