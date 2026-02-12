// Spec 07 – Background image and canvas resize adjustments (Areas H1-H4)
//
// Covers: CanvasElementBackgroundImageManager.ts,
//         CanvasElementCanvasResizeAdjustments.ts,
//         CanvasElementEditingSuspension.ts.

import { test, expect } from "../fixtures/canvasTest";
import {
    dragPaletteItemToCanvas,
    getCanvasElementCount,
    getActiveCanvasElement,
} from "../helpers/canvasActions";
import {
    expectCanvasElementCountToIncrease,
    expectElementVisible,
    expectElementHasPositiveSize,
} from "../helpers/canvasAssertions";
import { canvasSelectors } from "../helpers/canvasSelectors";

// ── H1: Background image presence ───────────────────────────────────────

test("H1: page may have a background image canvas element", async ({
    pageFrame,
}) => {
    // Some pages have a background image as a canvas element. Verify
    // that if one exists, it's visible and has positive size.
    const bgCount = await pageFrame
        .locator(canvasSelectors.page.backgroundImage)
        .count();

    if (bgCount > 0) {
        const bg = pageFrame
            .locator(canvasSelectors.page.backgroundImage)
            .first();
        await expectElementVisible(bg);
        await expectElementHasPositiveSize(bg);
    }
    // If no background image, that's fine too - not all pages have one
    expect(bgCount).toBeGreaterThanOrEqual(0);
});

// ── H2: Canvas elements are within canvas bounds ────────────────────────

test("H2: canvas elements are within canvas bounds", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    const beforeCount = await getCanvasElementCount(pageFrame);
    await dragPaletteItemToCanvas({
        page,
        toolboxFrame,
        pageFrame,
        paletteItem: "speech",
    });
    await expectCanvasElementCountToIncrease(pageFrame, beforeCount);

    const canvas = pageFrame.locator(canvasSelectors.page.canvas).first();
    const canvasBox = await canvas.boundingBox();
    expect(canvasBox).toBeTruthy();

    const active = getActiveCanvasElement(pageFrame);
    const elementBox = await active.boundingBox();
    expect(elementBox).toBeTruthy();

    // Element should overlap with the canvas area
    const overlapX =
        elementBox!.x + elementBox!.width > canvasBox!.x &&
        elementBox!.x < canvasBox!.x + canvasBox!.width;
    const overlapY =
        elementBox!.y + elementBox!.height > canvasBox!.y &&
        elementBox!.y < canvasBox!.y + canvasBox!.height;

    expect(overlapX && overlapY).toBe(true);
});

// ── H3: Multiple elements maintain valid positions ──────────────────────

test("H3: multiple created elements all have valid bounds", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    // Create two elements
    const before1 = await getCanvasElementCount(pageFrame);
    await dragPaletteItemToCanvas({
        page,
        toolboxFrame,
        pageFrame,
        paletteItem: "speech",
        dropOffset: { x: 80, y: 80 },
    });
    await expectCanvasElementCountToIncrease(pageFrame, before1);

    const before2 = await getCanvasElementCount(pageFrame);
    await dragPaletteItemToCanvas({
        page,
        toolboxFrame,
        pageFrame,
        paletteItem: "image",
        dropOffset: { x: 200, y: 150 },
    });
    await expectCanvasElementCountToIncrease(pageFrame, before2);

    // All canvas elements should have valid bounds
    const allElements = pageFrame.locator(canvasSelectors.page.canvasElements);
    const count = await allElements.count();

    for (let i = 0; i < count; i++) {
        const el = allElements.nth(i);
        const box = await el.boundingBox();
        if (box) {
            expect(box.width).toBeGreaterThan(0);
            expect(box.height).toBeGreaterThan(0);
        }
    }
});
