// Spec 02 – Select, move, resize, crop (Areas B1-B6)
//
// Covers: CanvasElementPointerInteractions.ts, CanvasElementHandleDragInteractions.ts,
//         CanvasElementSelectionUi.ts, CanvasElementPositioning.ts, CanvasElementGeometry.ts.

import { test, expect } from "../fixtures/canvasTest";
import {
    dragPaletteItemToCanvas,
    getCanvasElementCount,
    getActiveCanvasElement,
    dragActiveCanvasElementByOffset,
    resizeActiveElementFromCorner,
    resizeActiveElementFromSide,
} from "../helpers/canvasActions";
import {
    expectCanvasElementCountToIncrease,
    expectAnyCanvasElementActive,
    expectElementVisible,
    expectElementHasPositiveSize,
    expectContextControlsVisible,
} from "../helpers/canvasAssertions";

// ── Helper: create a speech element and ensure it's active ──────────────

const createSpeechElement = async (canvasTestContext) => {
    const beforeCount = await getCanvasElementCount(canvasTestContext);
    await dragPaletteItemToCanvas({
        canvasContext: canvasTestContext,
        paletteItem: "speech",
    });
    await expectCanvasElementCountToIncrease(canvasTestContext, beforeCount);
    await expectAnyCanvasElementActive(canvasTestContext);
};

// ── B1: Select and move element with mouse drag ────────────────────────

test("B1: move a canvas element by mouse drag", async ({
    canvasTestContext,
}) => {
    await createSpeechElement(canvasTestContext);

    await dragActiveCanvasElementByOffset(canvasTestContext, 60, 40);

    const active = getActiveCanvasElement(canvasTestContext);
    await expectElementVisible(active);
    await expectElementHasPositiveSize(active);
});

// ── B2: Resize from corners ─────────────────────────────────────────────

const corners = [
    "bottom-right",
    "bottom-left",
    "top-right",
    "top-left",
] as const;

for (const corner of corners) {
    test(`B2: resize from ${corner} corner`, async ({ canvasTestContext }) => {
        await createSpeechElement(canvasTestContext);

        const { activeElement } = await resizeActiveElementFromCorner(
            canvasTestContext,
            corner,
            30,
            20,
        );

        await expectElementVisible(activeElement);
        await expectElementHasPositiveSize(activeElement);
    });
}

// ── B3: Resize from side handles ────────────────────────────────────────

test("B3: resize from right side handle", async ({ canvasTestContext }) => {
    await createSpeechElement(canvasTestContext);

    const { activeElement } = await resizeActiveElementFromSide(
        canvasTestContext,
        "right",
        40,
    );

    await expectElementVisible(activeElement);
    await expectElementHasPositiveSize(activeElement);
});

test("B3: resize from bottom side handle", async ({ canvasTestContext }) => {
    await createSpeechElement(canvasTestContext);

    const { activeElement } = await resizeActiveElementFromSide(
        canvasTestContext,
        "bottom",
        30,
    );

    await expectElementVisible(activeElement);
    await expectElementHasPositiveSize(activeElement);
});

// ── B5: Selection frame follows active element ──────────────────────────

test("B5: context controls are visible for selected element", async ({
    canvasTestContext,
}) => {
    await createSpeechElement(canvasTestContext);

    await expectContextControlsVisible(canvasTestContext);
});

// ── B6: Manipulated element remains visible and valid ───────────────────

test("B6: element remains visible and valid after move", async ({
    canvasTestContext,
}) => {
    await createSpeechElement(canvasTestContext);

    await dragActiveCanvasElementByOffset(canvasTestContext, 50, 30);

    const active = getActiveCanvasElement(canvasTestContext);
    await expectElementVisible(active);
    await expectElementHasPositiveSize(active);
});

test("B6: element remains visible and valid after resize", async ({
    canvasTestContext,
}) => {
    await createSpeechElement(canvasTestContext);

    await resizeActiveElementFromCorner(
        canvasTestContext,
        "bottom-right",
        40,
        30,
    );

    const active = getActiveCanvasElement(canvasTestContext);
    await expectElementVisible(active);
    await expectElementHasPositiveSize(active);
});
