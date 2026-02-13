import {
    expect,
    type BoundingBox,
    type Frame,
    type Locator,
} from "playwright/test";
import { canvasSelectors, toolboxControlSelectorMap } from "./canvasSelectors";

// ── Element count ───────────────────────────────────────────────────────

export const expectCanvasElementCountToIncrease = async (
    pageFrame: Frame,
    beforeCount: number,
): Promise<void> => {
    await expect
        .poll(
            async () => {
                return pageFrame
                    .locator(canvasSelectors.page.canvasElements)
                    .count();
            },
            {
                message: `Expected canvas element count to exceed ${beforeCount}`,
                timeout: 10000,
            },
        )
        .toBeGreaterThan(beforeCount);
};

export const expectCanvasElementCountToBe = async (
    pageFrame: Frame,
    expectedCount: number,
): Promise<void> => {
    await expect(
        pageFrame.locator(canvasSelectors.page.canvasElements),
    ).toHaveCount(expectedCount);
};

// ── Active element ──────────────────────────────────────────────────────

export const expectAnyCanvasElementActive = async (
    pageFrame: Frame,
): Promise<void> => {
    await expect(
        pageFrame.locator(canvasSelectors.page.activeCanvasElement),
        "Expected exactly one active canvas element",
    ).toHaveCount(1);
};

// ── Context controls ────────────────────────────────────────────────────

export const expectContextControlsVisible = async (
    pageFrame: Frame,
): Promise<void> => {
    await expect(
        pageFrame.locator(canvasSelectors.page.contextControlsVisible).first(),
        "Expected context controls to be visible",
    ).toBeVisible();
};

// ── Bounds / position ───────────────────────────────────────────────────

export const expectElementBoundsToChange = async (
    locator: Locator,
    beforeBounds: BoundingBox,
    minimumDelta = 2,
): Promise<void> => {
    await expect
        .poll(
            async () => {
                const afterBounds = await locator.boundingBox();
                if (!afterBounds) return false;
                const dx = Math.abs(afterBounds.x - beforeBounds.x);
                const dy = Math.abs(afterBounds.y - beforeBounds.y);
                return dx >= minimumDelta || dy >= minimumDelta;
            },
            {
                message: `Expected element bounds to change by at least ${minimumDelta}px`,
            },
        )
        .toBe(true);
};

export const expectElementSizeToChange = async (
    locator: Locator,
    beforeBounds: BoundingBox,
    minimumDelta = 2,
): Promise<void> => {
    await expect
        .poll(
            async () => {
                const afterBounds = await locator.boundingBox();
                if (!afterBounds) return false;
                const dw = Math.abs(afterBounds.width - beforeBounds.width);
                const dh = Math.abs(afterBounds.height - beforeBounds.height);
                return dw >= minimumDelta || dh >= minimumDelta;
            },
            {
                message: `Expected element size to change by at least ${minimumDelta}px`,
            },
        )
        .toBe(true);
};

export const expectElementNearPoint = async (
    locator: Locator,
    expectedX: number,
    expectedY: number,
    tolerancePx = 20,
): Promise<void> => {
    await expect
        .poll(
            async () => {
                const box = await locator.boundingBox();
                if (!box) return false;
                const cx = box.x + box.width / 2;
                const cy = box.y + box.height / 2;
                return (
                    Math.abs(cx - expectedX) <= tolerancePx &&
                    Math.abs(cy - expectedY) <= tolerancePx
                );
            },
            {
                message: `Expected element center near (${expectedX}, ${expectedY}) ±${tolerancePx}px`,
            },
        )
        .toBe(true);
};

// ── Toolbox options region ──────────────────────────────────────────────

export const expectToolboxOptionsDisabled = async (
    toolboxFrame: Frame,
): Promise<void> => {
    await expect(
        toolboxFrame.locator(canvasSelectors.toolbox.optionsRegion).first(),
        "Expected toolbox options region to have 'disabled' class",
    ).toHaveClass(/disabled/);
};

export const expectToolboxOptionsEnabled = async (
    toolboxFrame: Frame,
): Promise<void> => {
    await expect(
        toolboxFrame.locator(canvasSelectors.toolbox.optionsRegion).first(),
        "Expected toolbox options region to NOT have 'disabled' class",
    ).not.toHaveClass(/disabled/);
};

// ── Toolbox attribute controls visibility ───────────────────────────────

export const expectToolboxControlsVisible = async (
    toolboxFrame: Frame,
    controlKeys: ReadonlyArray<keyof typeof toolboxControlSelectorMap>,
): Promise<void> => {
    for (const controlKey of controlKeys) {
        const selector = toolboxControlSelectorMap[controlKey];
        await expect(
            toolboxFrame.locator(selector).first(),
            `Expected toolbox control "${controlKey}" to be visible`,
        ).toBeVisible();
    }
};

export const expectToolboxShowsNoOptions = async (
    toolboxFrame: Frame,
): Promise<void> => {
    await expect(
        toolboxFrame.locator(canvasSelectors.toolbox.noOptionsSection).first(),
        "Expected 'no options' section to be visible",
    ).toBeVisible();
};

// ── Context toolbar button count ────────────────────────────────────────

export const expectContextToolbarButtonCount = async (
    pageFrame: Frame,
    expectedCount: number,
): Promise<void> => {
    const controls = pageFrame
        .locator(canvasSelectors.page.contextControlsVisible)
        .first();
    await controls.waitFor({ state: "visible", timeout: 10000 });
    await expect(
        controls.locator("button"),
        `Expected ${expectedCount} toolbar buttons`,
    ).toHaveCount(expectedCount);
};

// ── Context menu items ──────────────────────────────────────────────────

export const expectContextMenuItemVisible = async (
    pageFrame: Frame,
    label: string,
): Promise<void> => {
    await expect(
        pageFrame
            .locator(
                `${canvasSelectors.page.contextMenuListVisible} li:has-text("${label}")`,
            )
            .first(),
        `Expected context menu item "${label}" to be visible`,
    ).toBeVisible();
};

export const expectContextMenuItemNotPresent = async (
    pageFrame: Frame,
    label: string,
): Promise<void> => {
    await expect(
        pageFrame
            .locator(
                `${canvasSelectors.page.contextMenuListVisible} li:has-text("${label}")`,
            )
            .first(),
    ).toHaveCount(0);
};

// ── Canvas class state ──────────────────────────────────────────────────

export const expectCanvasHasElementClass = async (
    pageFrame: Frame,
    expected: boolean,
): Promise<void> => {
    const canvas = pageFrame.locator(canvasSelectors.page.canvas).first();
    if (expected) {
        await expect(
            canvas,
            "Expected canvas to have bloom-has-canvas-element class",
        ).toHaveClass(/bloom-has-canvas-element/);
    } else {
        await expect(
            canvas,
            "Expected canvas to NOT have bloom-has-canvas-element class",
        ).not.toHaveClass(/bloom-has-canvas-element/);
    }
};

// ── Draggable attributes ────────────────────────────────────────────────

export const expectDraggableIdPresent = async (
    element: Locator,
): Promise<void> => {
    await expect(
        element,
        "Expected element to have data-draggable-id attribute",
    ).toHaveAttribute("data-draggable-id", /.+/);
};

export const expectTargetExistsForDraggable = async (
    pageFrame: Frame,
    draggableId: string,
): Promise<void> => {
    await expect(
        pageFrame.locator(`[data-target-of="${draggableId}"]`),
        `Expected a target element for draggable "${draggableId}"`,
    ).toHaveCount(1);
};

// ── Grid snapping ───────────────────────────────────────────────────────

export const expectPositionGridSnapped = async (
    locator: Locator,
    gridSize = 10,
): Promise<void> => {
    await expect
        .poll(
            async () => {
                const style = await locator.evaluate((el: HTMLElement) => ({
                    left: el.style.left,
                    top: el.style.top,
                }));
                const left = parseFloat(style.left) || 0;
                const top = parseFloat(style.top) || 0;
                return left % gridSize === 0 && top % gridSize === 0;
            },
            {
                message: `Expected element position to be snapped to grid=${gridSize}`,
            },
        )
        .toBe(true);
};

// ── Selected element type ──────────────────────────────────────────────

/**
 * Assert the active canvas element contains an expected internal structure
 * indicating a particular inferred type.
 */
export const expectSelectedElementType = async (
    pageFrame: Frame,
    expectedType: "speech" | "image" | "video" | "text" | "caption",
): Promise<void> => {
    const active = pageFrame
        .locator(canvasSelectors.page.activeCanvasElement)
        .first();
    await expect(active, "Expected an active canvas element").toBeVisible();

    switch (expectedType) {
        case "speech":
        case "text":
        case "caption":
            await expect(
                active.locator(canvasSelectors.page.bloomEditable).first(),
                `Expected active element to contain bloom-editable for type "${expectedType}"`,
            ).toBeVisible();
            break;
        case "image":
            await expect(
                active.locator(canvasSelectors.page.imageContainer).first(),
                `Expected active element to contain imageContainer for type "image"`,
            ).toBeVisible();
            break;
        case "video":
            await expect(
                active.locator(canvasSelectors.page.videoContainer).first(),
                `Expected active element to contain videoContainer for type "video"`,
            ).toBeVisible();
            break;
    }
};

// ── Command enabled/disabled ──────────────────────────────────────────

/**
 * Assert that a toolbar button at a given index is enabled or disabled.
 */
export const expectToolbarButtonEnabled = async (
    pageFrame: Frame,
    buttonIndex: number,
    enabled: boolean,
): Promise<void> => {
    const controls = pageFrame
        .locator(canvasSelectors.page.contextControlsVisible)
        .first();
    await controls.waitFor({ state: "visible", timeout: 10000 });

    const button = controls.locator("button").nth(buttonIndex);
    if (enabled) {
        await expect(
            button,
            `Expected toolbar button at index ${buttonIndex} to be enabled`,
        ).toBeEnabled();
    } else {
        await expect(
            button,
            `Expected toolbar button at index ${buttonIndex} to be disabled`,
        ).toBeDisabled();
    }
};

// ── Element visibility / validity ───────────────────────────────────────

export const expectElementVisible = async (locator: Locator): Promise<void> => {
    await expect(locator, "Expected element to be visible").toBeVisible();
};

export const expectElementHasPositiveSize = async (
    locator: Locator,
): Promise<void> => {
    const box = await locator.boundingBox();
    expect(box, "Expected element to have a bounding box").toBeTruthy();
    expect(box!.width, "Expected element width > 0").toBeGreaterThan(0);
    expect(box!.height, "Expected element height > 0").toBeGreaterThan(0);
};
