import { expect, type Frame, type Locator, type Page } from "playwright/test";
import {
    getPageFrame,
    getToolboxFrame,
    gotoCurrentPage,
    openCanvasToolTab,
    waitForCanvasReady,
} from "./canvasFrames";
import { canvasSelectors, type CanvasPaletteItemKey } from "./canvasSelectors";

type BoundingBox = {
    x: number;
    y: number;
    width: number;
    height: number;
};

type ICanvasElementManagerForEval = {
    setActiveElement: (element: HTMLElement | undefined) => void;
    deleteCurrentCanvasElement: () => void;
    duplicateCanvasElement: () => void;
};

type IEditablePageBundleWindow = Window & {
    editablePageBundle?: {
        getTheOneCanvasElementManager?: () =>
            | ICanvasElementManagerForEval
            | undefined;
    };
};

// ── Types ───────────────────────────────────────────────────────────────

export interface ICanvasTestContext {
    toolboxFrame: Frame;
    pageFrame: Frame;
}

export interface ICanvasPageContext extends ICanvasTestContext {
    page: Page;
}

const nativeDialogMenuCommands = new Set([
    "Choose image from your computer...",
    "Change image",
    "Choose Video from your Computer...",
    "Record yourself...",
]);

const assertNativeDialogCommandNotInvoked = (label: string): void => {
    if (nativeDialogMenuCommands.has(label)) {
        throw new Error(
            `Refusing to invoke context-menu command \"${label}\" because it opens a native dialog and can hang the canvas e2e host. Assert visibility/enabled state only.`,
        );
    }
};

interface IDropOffset {
    x: number;
    y: number;
}

export interface ICreatedCanvasElement {
    index: number;
    element: Locator;
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
    options?: { navigate?: boolean },
): Promise<ICanvasPageContext> => {
    if (options?.navigate ?? true) {
        await gotoCurrentPage(page);
    }
    const toolboxFrame = await getToolboxFrame(page);
    const pageFrame = await getPageFrame(page);
    await openCanvasToolTab(toolboxFrame);
    await waitForCanvasReady(pageFrame);

    return {
        page,
        toolboxFrame,
        pageFrame,
    };
};

// ── Element count ───────────────────────────────────────────────────────

export const getCanvasElementCount = async (
    canvasContext: ICanvasTestContext,
): Promise<number> => {
    return canvasContext.pageFrame
        .locator(canvasSelectors.page.canvasElements)
        .count();
};

export const createCanvasElementWithRetry = async (params: {
    canvasContext: ICanvasPageContext;
    paletteItem: CanvasPaletteItemKey;
    dropOffset?: IDropOffset;
    maxAttempts?: number;
}): Promise<ICreatedCanvasElement> => {
    const maxAttempts = params.maxAttempts ?? 3;

    for (let attempt = 0; attempt < maxAttempts; attempt++) {
        const beforeCount = await getCanvasElementCount(params.canvasContext);

        await dragPaletteItemToCanvas({
            canvasContext: params.canvasContext,
            paletteItem: params.paletteItem,
            dropOffset: params.dropOffset,
        });

        try {
            await expect
                .poll(
                    async () => {
                        return getCanvasElementCount(params.canvasContext);
                    },
                    {
                        message: `Expected canvas element count to exceed ${beforeCount}`,
                        timeout: 10000,
                    },
                )
                .toBeGreaterThan(beforeCount);

            return {
                index: beforeCount,
                element: params.canvasContext.pageFrame
                    .locator(canvasSelectors.page.canvasElements)
                    .nth(beforeCount),
            };
        } catch (error) {
            if (attempt === maxAttempts - 1) {
                throw error;
            }
        }
    }

    throw new Error("Could not create canvas element after bounded retries.");
};

const waitForCanvasElementCountBelow = async (
    canvasContext: ICanvasTestContext,
    upperExclusive: number,
    timeoutMs = 2500,
): Promise<boolean> => {
    const endTime = Date.now() + timeoutMs;
    while (Date.now() < endTime) {
        const count = await getCanvasElementCount(canvasContext);
        if (count < upperExclusive) {
            return true;
        }
        await canvasContext.pageFrame.page().waitForTimeout(100);
    }
    return false;
};

const deleteLastCanvasElementViaManager = async (
    canvasContext: ICanvasTestContext,
): Promise<void> => {
    // TODO: Replace manager-based teardown deletion with pure UI deletion once
    // overlay-canvas pointer interception is resolved for shared-mode cleanup.
    await canvasContext.pageFrame.evaluate((selector: string) => {
        const bundle = (window as IEditablePageBundleWindow).editablePageBundle;
        const manager = bundle?.getTheOneCanvasElementManager?.();
        if (!manager) {
            throw new Error("CanvasElementManager is not available.");
        }

        const elements = Array.from(
            document.querySelectorAll(selector),
        ) as HTMLElement[];
        if (elements.length === 0) {
            return;
        }

        const lastElement = elements[elements.length - 1];
        manager.setActiveElement(lastElement);
        manager.deleteCurrentCanvasElement();
    }, canvasSelectors.page.canvasElements);
};

/**
 * Remove user-created canvas elements until the count reaches targetCount.
 * Intended for test cleanup in shared-page mode.
 */
export const removeCanvasElementsDownToCount = async (
    canvasContext: ICanvasTestContext,
    targetCount: number,
): Promise<void> => {
    const maxAttempts = 200;

    for (let attempt = 0; attempt < maxAttempts; attempt++) {
        const beforeCount = await getCanvasElementCount(canvasContext);
        if (beforeCount <= targetCount) {
            return;
        }

        await deleteLastCanvasElementViaManager(canvasContext);

        if (await waitForCanvasElementCountBelow(canvasContext, beforeCount)) {
            continue;
        }

        throw new Error(
            `Could not delete canvas element during cleanup (count stayed at ${beforeCount}).`,
        );
    }

    throw new Error(
        `Cleanup exceeded ${maxAttempts} attempts while reducing canvas elements to ${targetCount}.`,
    );
};

export const duplicateActiveCanvasElementViaManager = async (
    canvasContext: ICanvasTestContext,
): Promise<void> => {
    // TODO: Replace this manager shortcut with a pure UI duplicate path once
    // shared-mode selection/click interception is fully stabilized.
    await canvasContext.pageFrame.evaluate(() => {
        const bundle = (window as IEditablePageBundleWindow).editablePageBundle;
        const manager = bundle?.getTheOneCanvasElementManager?.();
        if (!manager) {
            throw new Error("CanvasElementManager is not available.");
        }
        manager.duplicateCanvasElement();
    });
};

export const deleteActiveCanvasElementViaManager = async (
    canvasContext: ICanvasTestContext,
): Promise<void> => {
    // TODO: Replace this manager shortcut with a pure UI delete path once
    // shared-mode selection/click interception is fully stabilized.
    await canvasContext.pageFrame.evaluate(() => {
        const bundle = (window as IEditablePageBundleWindow).editablePageBundle;
        const manager = bundle?.getTheOneCanvasElementManager?.();
        if (!manager) {
            throw new Error("CanvasElementManager is not available.");
        }
        manager.deleteCurrentCanvasElement();
    });
};

export const clearActiveCanvasElementViaManager = async (
    canvasContext: ICanvasTestContext,
): Promise<void> => {
    // TODO: Replace manager-based deselection with a UI path once we have a
    // stable click-target for clearing selection in shared mode.
    await canvasContext.pageFrame.evaluate(() => {
        const bundle = (window as IEditablePageBundleWindow).editablePageBundle;
        const manager = bundle?.getTheOneCanvasElementManager?.();
        if (!manager) {
            throw new Error("CanvasElementManager is not available.");
        }
        manager.setActiveElement(undefined);
    });
};

// ── Drag from palette ───────────────────────────────────────────────────

/**
 * Expand the Navigation TriangleCollapse in the toolbox so that navigation
 * palette items become visible. Idempotent if already expanded.
 */
export const expandNavigationSection = async (
    canvasContext: ICanvasTestContext,
): Promise<void> => {
    // Check if a navigation-only palette item is already visible
    const navItem = canvasContext.toolboxFrame
        .locator(
            canvasSelectors.toolbox.paletteItems["navigation-image-button"],
        )
        .first();
    if (await navItem.isVisible().catch(() => false)) {
        return;
    }
    // Click the triangle collapse toggle
    const toggle = canvasContext.toolboxFrame
        .locator(canvasSelectors.toolbox.navigationCollapseToggle)
        .first();
    await toggle.click();
    await navItem.waitFor({ state: "visible", timeout: 5000 });
};

export const dragPaletteItemToCanvas = async (params: {
    canvasContext: ICanvasPageContext;
    paletteItem: CanvasPaletteItemKey;
    dropOffset?: IDropOffset;
}): Promise<void> => {
    const paletteSelector =
        canvasSelectors.toolbox.paletteItems[params.paletteItem];
    const source = params.canvasContext.toolboxFrame
        .locator(`${paletteSelector}:visible`)
        .first();
    await source.waitFor({
        state: "visible",
        timeout: 10000,
    });

    const canvas = params.canvasContext.pageFrame
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

    const targetOffsetX = Math.max(5, Math.min(offset.x, canvasBox.width - 5));
    const targetOffsetY = Math.max(5, Math.min(offset.y, canvasBox.height - 5));

    const sourceX = sourceBox.x + sourceBox.width / 2;
    const sourceY = sourceBox.y + sourceBox.height / 2;
    const targetX = canvasBox.x + targetOffsetX;
    const targetY = canvasBox.y + targetOffsetY;

    await params.canvasContext.page.mouse.move(sourceX, sourceY);
    await params.canvasContext.page.mouse.down();
    await params.canvasContext.page.mouse.move(targetX, targetY, {
        steps: 16,
    });
    await params.canvasContext.page.mouse.up();
};

// ── Selection ───────────────────────────────────────────────────────────

export const selectCanvasElementAtIndex = async (
    canvasContext: ICanvasTestContext,
    index: number,
): Promise<Locator> => {
    const maxAttempts = 4;

    for (let attempt = 0; attempt < maxAttempts; attempt++) {
        const element = canvasContext.pageFrame
            .locator(canvasSelectors.page.canvasElements)
            .nth(index);
        await element.waitFor({
            state: "visible",
            timeout: 10000,
        });

        await canvasContext.pageFrame
            .page()
            .keyboard.press("Escape")
            .catch(() => undefined);

        try {
            await element.click({ force: true });
            return element;
        } catch (error) {
            if (attempt === maxAttempts - 1) {
                throw error;
            }
            await canvasContext.pageFrame.page().waitForTimeout(100);
        }
    }

    throw new Error(`Could not select canvas element at index ${index}.`);
};

export const getActiveCanvasElement = (
    canvasContext: ICanvasTestContext,
): Locator => {
    return canvasContext.pageFrame
        .locator(canvasSelectors.page.activeCanvasElement)
        .first();
};

// ── Move by mouse drag ──────────────────────────────────────────────────

export const dragActiveCanvasElementByOffset = async (
    canvasContext: ICanvasPageContext,
    dx: number,
    dy: number,
    modifiers?: { shift?: boolean; element?: Locator },
): Promise<{ activeElement: Locator; beforeBounds: BoundingBox }> => {
    const activeElement =
        modifiers?.element ?? getActiveCanvasElement(canvasContext);
    await activeElement.waitFor({
        state: "visible",
        timeout: 10000,
    });

    const beforeBounds = await getRequiredBoundingBox(
        activeElement,
        "active canvas element",
    );

    let startX = beforeBounds.x + beforeBounds.width / 2;
    let startY = beforeBounds.y + beforeBounds.height / 2;

    const editableLocator = activeElement.locator(
        `${canvasSelectors.page.bloomEditable}:visible`,
    );
    const editableBox =
        (await editableLocator.count()) > 0
            ? await editableLocator.first().boundingBox()
            : null;
    if (editableBox) {
        const isInsideElementBounds = (x: number, y: number): boolean => {
            return (
                x >= beforeBounds.x + 1 &&
                x <= beforeBounds.x + beforeBounds.width - 1 &&
                y >= beforeBounds.y + 1 &&
                y <= beforeBounds.y + beforeBounds.height - 1
            );
        };

        const edgePadding = 2;
        const aroundEditableCandidates = [
            {
                x: editableBox.x - edgePadding,
                y: editableBox.y + editableBox.height / 2,
            },
            {
                x: editableBox.x + editableBox.width + edgePadding,
                y: editableBox.y + editableBox.height / 2,
            },
            {
                x: editableBox.x + editableBox.width / 2,
                y: editableBox.y - edgePadding,
            },
            {
                x: editableBox.x + editableBox.width / 2,
                y: editableBox.y + editableBox.height + edgePadding,
            },
        ];

        const validCandidate = aroundEditableCandidates.find((point) =>
            isInsideElementBounds(point.x, point.y),
        );
        if (validCandidate) {
            startX = validCandidate.x;
            startY = validCandidate.y;
        }
    }

    await canvasContext.page.mouse.move(startX, startY);
    if (modifiers?.shift) {
        await canvasContext.page.keyboard.down("Shift");
    }

    try {
        await canvasContext.page.mouse.down();
        await canvasContext.page.mouse.move(startX + dx, startY + dy, {
            steps: 10,
        });
        await canvasContext.page.mouse.up();
    } finally {
        if (modifiers?.shift) {
            await canvasContext.page.keyboard.up("Shift");
        }
    }

    return {
        activeElement,
        beforeBounds,
    };
};

// ── Resize from corner ──────────────────────────────────────────────────

export const resizeActiveElementFromCorner = async (
    canvasContext: ICanvasPageContext,
    corner: ResizeCorner,
    dx: number,
    dy: number,
    modifiers?: { shift?: boolean },
): Promise<{ activeElement: Locator; beforeBounds: BoundingBox }> => {
    const activeElement = getActiveCanvasElement(canvasContext);
    await activeElement.waitFor({ state: "visible", timeout: 10000 });

    const beforeBounds = await getRequiredBoundingBox(
        activeElement,
        "active canvas element (resize corner)",
    );

    const { xFrac, yFrac } = cornerOffsets[corner];
    const handleX = beforeBounds.x + beforeBounds.width * xFrac;
    const handleY = beforeBounds.y + beforeBounds.height * yFrac;

    await canvasContext.page.mouse.move(handleX, handleY);
    if (modifiers?.shift) {
        await canvasContext.page.keyboard.down("Shift");
    }
    await canvasContext.page.mouse.down();
    await canvasContext.page.mouse.move(handleX + dx, handleY + dy, {
        steps: 10,
    });
    await canvasContext.page.mouse.up();
    if (modifiers?.shift) {
        await canvasContext.page.keyboard.up("Shift");
    }

    return { activeElement, beforeBounds };
};

// ── Resize from side ────────────────────────────────────────────────────

export const resizeActiveElementFromSide = async (
    canvasContext: ICanvasPageContext,
    side: ResizeSide,
    delta: number,
    modifiers?: { shift?: boolean },
): Promise<{ activeElement: Locator; beforeBounds: BoundingBox }> => {
    const dx = side === "left" || side === "right" ? delta : 0;
    const dy = side === "top" || side === "bottom" ? delta : 0;

    const activeElement = getActiveCanvasElement(canvasContext);
    await activeElement.waitFor({ state: "visible", timeout: 10000 });

    const beforeBounds = await getRequiredBoundingBox(
        activeElement,
        "active canvas element (resize side)",
    );

    const { xFrac, yFrac } = sideOffsets[side];
    const handleX = beforeBounds.x + beforeBounds.width * xFrac;
    const handleY = beforeBounds.y + beforeBounds.height * yFrac;

    await canvasContext.page.mouse.move(handleX, handleY);
    if (modifiers?.shift) {
        await canvasContext.page.keyboard.down("Shift");
    }
    await canvasContext.page.mouse.down();
    await canvasContext.page.mouse.move(handleX + dx, handleY + dy, {
        steps: 10,
    });
    await canvasContext.page.mouse.up();
    if (modifiers?.shift) {
        await canvasContext.page.keyboard.up("Shift");
    }

    return { activeElement, beforeBounds };
};

// ── Keyboard nudge ──────────────────────────────────────────────────────

export const keyboardNudge = async (
    canvasContext: ICanvasPageContext,
    key: "ArrowUp" | "ArrowDown" | "ArrowLeft" | "ArrowRight",
    modifiers?: { ctrl?: boolean; shift?: boolean },
): Promise<void> => {
    const mods: string[] = [];
    if (modifiers?.ctrl) mods.push("Control");
    if (modifiers?.shift) mods.push("Shift");

    const combo = mods.length > 0 ? `${mods.join("+")}+${key}` : key;
    await canvasContext.page.keyboard.press(combo);
};

// ── Context menu / toolbar ──────────────────────────────────────────────

export const openContextMenuFromToolbar = async (
    canvasContext: ICanvasTestContext,
): Promise<void> => {
    const visibleMenu = canvasContext.pageFrame
        .locator(canvasSelectors.page.contextMenuListVisible)
        .first();
    if (await visibleMenu.isVisible().catch(() => false)) {
        return;
    }

    const controls = canvasContext.pageFrame
        .locator(canvasSelectors.page.contextControlsVisible)
        .first();

    if (!(await controls.isVisible().catch(() => false))) {
        const active = canvasContext.pageFrame
            .locator(canvasSelectors.page.activeCanvasElement)
            .first();
        if (await active.isVisible().catch(() => false)) {
            await active.click({ force: true }).catch(() => undefined);
        } else {
            const firstCanvasElement = canvasContext.pageFrame
                .locator(canvasSelectors.page.canvasElements)
                .first();
            if (await firstCanvasElement.isVisible().catch(() => false)) {
                await firstCanvasElement
                    .click({ force: true })
                    .catch(() => undefined);
            }
        }
    }

    await controls.waitFor({
        state: "visible",
        timeout: 10000,
    });

    const menuButton = controls.locator("button").last();
    try {
        await menuButton.click();
    } catch {
        await canvasContext.pageFrame
            .page()
            .keyboard.press("Escape")
            .catch(() => undefined);
        await menuButton.click({ force: true });
    }

    await canvasContext.pageFrame
        .locator(canvasSelectors.page.contextMenuListVisible)
        .first()
        .waitFor({
            state: "visible",
            timeout: 10000,
        });
};

export const clickContextMenuItem = async (
    canvasContext: ICanvasTestContext,
    label: string,
): Promise<void> => {
    assertNativeDialogCommandNotInvoked(label);

    const maxAttempts = 3;

    for (let attempt = 0; attempt < maxAttempts; attempt++) {
        const menuItem = canvasContext.pageFrame
            .locator(
                `${canvasSelectors.page.contextMenuListVisible} li:has-text("${label}")`,
            )
            .first();
        await menuItem.waitFor({
            state: "visible",
            timeout: 10000,
        });

        try {
            await menuItem.click({ force: true });
            return;
        } catch (error) {
            if (attempt === maxAttempts - 1) {
                throw error;
            }
            await openContextMenuFromToolbar(canvasContext);
        }
    }
};

export const dismissCanvasDialogsIfPresent = async (
    canvasContext: ICanvasPageContext,
): Promise<void> => {
    const tryDismissInScope = async (root: Page | Frame): Promise<boolean> => {
        const dialog = root.locator(".MuiDialog-root:visible").first();
        if (!(await dialog.isVisible().catch(() => false))) {
            return false;
        }

        const closeButton = dialog
            .locator(
                'button:has-text("OK"), button:has-text("Close"), button:has-text("Cancel")',
            )
            .first();

        if (await closeButton.isVisible().catch(() => false)) {
            await closeButton.click({ force: true }).catch(async () => {
                await canvasContext.page.keyboard.press("Escape");
            });
        } else {
            await canvasContext.page.keyboard.press("Escape");
        }

        await dialog
            .waitFor({ state: "hidden", timeout: 2000 })
            .catch(() => undefined);
        return true;
    };

    for (let attempt = 0; attempt < 6; attempt++) {
        const dismissedTop = await tryDismissInScope(canvasContext.page);
        const dismissedFrame = await tryDismissInScope(canvasContext.pageFrame);
        if (!dismissedTop && !dismissedFrame) {
            return;
        }
        await canvasContext.page.waitForTimeout(100);
    }
};

export const getContextToolbarButtonCount = async (
    canvasContext: ICanvasTestContext,
): Promise<number> => {
    const controls = canvasContext.pageFrame
        .locator(canvasSelectors.page.contextControlsVisible)
        .first();
    await controls.waitFor({ state: "visible", timeout: 10000 });
    return controls.locator("button").count();
};

// ── Toolbox attribute controls ──────────────────────────────────────────

export const setStyleDropdown = async (
    canvasContext: ICanvasTestContext,
    value: string,
): Promise<void> => {
    const maxAttempts = 3;
    const normalizedTarget = value.toLowerCase();
    const styleInput = canvasContext.toolboxFrame
        .locator("#canvasElement-style-dropdown")
        .first();

    for (let attempt = 0; attempt < maxAttempts; attempt++) {
        const dropdown = canvasContext.toolboxFrame
            .locator("#mui-component-select-style")
            .first();
        await dropdown.waitFor({ state: "visible", timeout: 5000 });
        await dropdown.click({ force: true });

        const option = canvasContext.toolboxFrame
            .locator(
                `.canvasElement-options-dropdown-menu li[data-value="${value}"]`,
            )
            .last();
        await option.waitFor({ state: "visible", timeout: 5000 });

        try {
            await option.click({ force: true });
        } catch (error) {
            if (attempt === maxAttempts - 1) {
                throw error;
            }
            continue;
        }

        const timeoutMs = 3000;
        const endTime = Date.now() + timeoutMs;
        while (Date.now() < endTime) {
            const selectedValue = (await styleInput.inputValue()).toLowerCase();
            if (selectedValue === normalizedTarget) {
                return;
            }
            await canvasContext.toolboxFrame.page().waitForTimeout(100);
        }

        if (attempt === maxAttempts - 1) {
            throw new Error(
                `Style dropdown did not change to ${normalizedTarget} within ${timeoutMs}ms.`,
            );
        }
    }

    throw new Error(`Style dropdown could not be set to ${normalizedTarget}.`);
};

export const setShowTail = async (
    canvasContext: ICanvasTestContext,
    enabled: boolean,
): Promise<void> => {
    const checkbox = canvasContext.toolboxFrame
        .locator(canvasSelectors.toolbox.showTailCheckbox)
        .first();
    await checkbox.waitFor({ state: "visible", timeout: 5000 });
    const isChecked = await checkbox.isChecked();
    if (isChecked !== enabled) {
        await checkbox.click();
    }
};

export const setRoundedCorners = async (
    canvasContext: ICanvasTestContext,
    enabled: boolean,
): Promise<void> => {
    const checkbox = canvasContext.toolboxFrame
        .locator(canvasSelectors.toolbox.roundedCornersCheckbox)
        .first();
    await checkbox.waitFor({ state: "visible", timeout: 5000 });
    const isChecked = await checkbox.isChecked();
    if (isChecked !== enabled) {
        await checkbox.click();
    }
};

export const clickTextColorBar = async (
    canvasContext: ICanvasTestContext,
): Promise<void> => {
    const bar = canvasContext.toolboxFrame
        .locator(canvasSelectors.toolbox.textColorBar)
        .first();
    await bar.waitFor({ state: "visible", timeout: 5000 });
    await bar.click();
};

export const clickBackgroundColorBar = async (
    canvasContext: ICanvasTestContext,
): Promise<void> => {
    const bar = canvasContext.toolboxFrame
        .locator(canvasSelectors.toolbox.backgroundColorBar)
        .first();
    await bar.waitFor({ state: "visible", timeout: 5000 });
    await bar.click();
};

export const setOutlineColorDropdown = async (
    canvasContext: ICanvasTestContext,
    value: string,
): Promise<void> => {
    const input = canvasContext.toolboxFrame
        .locator("#canvasElement-outlineColor-dropdown")
        .first();
    await input.waitFor({ state: "visible", timeout: 5000 });

    const normalizedTarget = value.toLowerCase();
    if ((await input.inputValue()).toLowerCase() === normalizedTarget) {
        return;
    }

    const maxAttempts = 4;
    for (let attempt = 0; attempt < maxAttempts; attempt++) {
        const dropdown = canvasContext.toolboxFrame
            .locator("#mui-component-select-outlineColor")
            .first();
        await dropdown.waitFor({ state: "visible", timeout: 5000 });
        await dropdown.click({ force: true });

        const option = canvasContext.toolboxFrame
            .locator(
                `.canvasElement-options-dropdown-menu li[data-value="${value}"]`,
            )
            .last();

        try {
            await option.waitFor({ state: "visible", timeout: 3000 });
            await option.click({ force: true });
        } catch (error) {
            if (attempt === maxAttempts - 1) {
                throw error;
            }
            continue;
        }

        const timeoutMs = 2000;
        const endTime = Date.now() + timeoutMs;
        while (Date.now() < endTime) {
            const updatedValue = (await input.inputValue()).toLowerCase();
            if (updatedValue === normalizedTarget) {
                return;
            }
            await canvasContext.toolboxFrame.page().waitForTimeout(100);
        }
    }

    throw new Error(
        `Outline color dropdown did not change to ${normalizedTarget}.`,
    );
};

// ── Toolbar button commands ─────────────────────────────────────────────

/**
 * Click a toolbar button in the context controls by its zero-based index
 * (excluding the menu button which is always last).
 */
export const clickToolbarButtonByIndex = async (
    canvasContext: ICanvasTestContext,
    buttonIndex: number,
): Promise<void> => {
    const controls = canvasContext.pageFrame
        .locator(canvasSelectors.page.contextControlsVisible)
        .first();
    await controls.waitFor({ state: "visible", timeout: 10000 });

    const button = controls.locator("button").nth(buttonIndex);
    await button.click();
};

// ── Coordinate conversion ───────────────────────────────────────────────

/**
 * Convert page-frame-relative coordinates to top-level page coordinates.
 * Useful for cross-iframe assertions where bounding boxes are reported in
 * the page-frame coordinate space but mouse events operate in the top-level
 * coordinate space.
 */
export const pageFrameToTopLevel = async (
    canvasContext: ICanvasPageContext,
    x: number,
    y: number,
): Promise<{ x: number; y: number }> => {
    const frameElement = await canvasContext.pageFrame.frameElement();
    const frameBox = await frameElement.boundingBox();
    if (!frameBox) {
        throw new Error("Could not get page iframe bounding box.");
    }
    return { x: x + frameBox.x, y: y + frameBox.y };
};
