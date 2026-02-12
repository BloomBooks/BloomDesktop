// Spec 05 – Draggable integration / game-specific behavior (Areas F1-F5)
//
// Covers: CanvasElementDraggableIntegration.ts, canvasElementDraggables.ts,
//         CanvasElementContextControls.tsx.
//
// Note: Draggable features are only available on Bloom Games pages. These
// tests verify the core draggable attribute and target pairing behavior
// when it is possible to observe them on CURRENTPAGE.

import { test, expect } from "../fixtures/canvasTest";
import {
    dragPaletteItemToCanvas,
    getCanvasElementCount,
    getActiveCanvasElement,
} from "../helpers/canvasActions";
import {
    expectCanvasElementCountToIncrease,
    expectAnyCanvasElementActive,
} from "../helpers/canvasAssertions";
import { canvasSelectors } from "../helpers/canvasSelectors";

// ── F1: Draggable attribute is not present on normal (non-game) pages ───

test("F1: speech element on a normal page does not have data-draggable-id", async ({
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

    const active = getActiveCanvasElement(pageFrame);

    // On a normal (non-game) page, elements should NOT have draggable id
    const hasDraggableId = await active.evaluate((el) =>
        el.hasAttribute("data-draggable-id"),
    );
    expect(hasDraggableId).toBe(false);
});

// ── F-general: No targets exist on a non-game page ─────────────────────

test("F-general: no draggable targets on a normal page", async ({
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

    const targetCount = await pageFrame
        .locator(canvasSelectors.page.targetElement)
        .count();
    expect(targetCount).toBe(0);
});
