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

const createSpeechElement = async ({ page, toolboxFrame, pageFrame }) => {
    const beforeCount = await getCanvasElementCount(pageFrame);
    await dragPaletteItemToCanvas({
        page,
        toolboxFrame,
        pageFrame,
        paletteItem: "speech",
    });
    await expectCanvasElementCountToIncrease(pageFrame, beforeCount);
    await expectAnyCanvasElementActive(pageFrame);
};

// ── B1: Select and move element with mouse drag ────────────────────────

test("B1: move a canvas element by mouse drag", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    await createSpeechElement({ page, toolboxFrame, pageFrame });

    await dragActiveCanvasElementByOffset(page, pageFrame, 60, 40);

    const active = getActiveCanvasElement(pageFrame);
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
    test(`B2: resize from ${corner} corner`, async ({
        page,
        toolboxFrame,
        pageFrame,
    }) => {
        await createSpeechElement({ page, toolboxFrame, pageFrame });

        const { activeElement } = await resizeActiveElementFromCorner(
            page,
            pageFrame,
            corner,
            30,
            20,
        );

        await expectElementVisible(activeElement);
        await expectElementHasPositiveSize(activeElement);
    });
}

// ── B3: Resize from side handles ────────────────────────────────────────

test("B3: resize from right side handle", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    await createSpeechElement({ page, toolboxFrame, pageFrame });

    const { activeElement } = await resizeActiveElementFromSide(
        page,
        pageFrame,
        "right",
        40,
    );

    await expectElementVisible(activeElement);
    await expectElementHasPositiveSize(activeElement);
});

test("B3: resize from bottom side handle", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    await createSpeechElement({ page, toolboxFrame, pageFrame });

    const { activeElement } = await resizeActiveElementFromSide(
        page,
        pageFrame,
        "bottom",
        30,
    );

    await expectElementVisible(activeElement);
    await expectElementHasPositiveSize(activeElement);
});

// ── B5: Selection frame follows active element ──────────────────────────

test("B5: context controls are visible for selected element", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    await createSpeechElement({ page, toolboxFrame, pageFrame });

    await expectContextControlsVisible(pageFrame);
});

// ── B6: Manipulated element remains visible and valid ───────────────────

test("B6: element remains visible and valid after move", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    await createSpeechElement({ page, toolboxFrame, pageFrame });

    await dragActiveCanvasElementByOffset(page, pageFrame, 50, 30);

    const active = getActiveCanvasElement(pageFrame);
    await expectElementVisible(active);
    await expectElementHasPositiveSize(active);
});

test("B6: element remains visible and valid after resize", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    await createSpeechElement({ page, toolboxFrame, pageFrame });

    await resizeActiveElementFromCorner(
        page,
        pageFrame,
        "bottom-right",
        40,
        30,
    );

    const active = getActiveCanvasElement(pageFrame);
    await expectElementVisible(active);
    await expectElementHasPositiveSize(active);
});
