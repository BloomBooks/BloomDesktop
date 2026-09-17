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

const createSpeechElement = async (canvasTestContext) => {
    const maxAttempts = 3;

    for (let attempt = 0; attempt < maxAttempts; attempt++) {
        const beforeCount = await getCanvasElementCount(canvasTestContext);
        await dragPaletteItemToCanvas({
            canvasContext: canvasTestContext,
            paletteItem: "speech",
        });

        try {
            await expectCanvasElementCountToIncrease(
                canvasTestContext,
                beforeCount,
            );
            return;
        } catch (error) {
            if (attempt === maxAttempts - 1) {
                throw error;
            }
        }
    }
};

// ── F1: Draggable attribute is not present on normal (non-game) pages ───

test("F1: speech element on a normal page does not have data-draggable-id", async ({
    canvasTestContext,
}) => {
    await createSpeechElement(canvasTestContext);

    const active = getActiveCanvasElement(canvasTestContext);

    // On a normal (non-game) page, elements should NOT have draggable id
    const hasDraggableId = await active.evaluate((el) =>
        el.hasAttribute("data-draggable-id"),
    );
    expect(hasDraggableId).toBe(false);
});

// ── F-general: No targets exist on a non-game page ─────────────────────

test("F-general: no draggable targets on a normal page", async ({
    canvasTestContext,
}) => {
    await createSpeechElement(canvasTestContext);

    const targetCount = await canvasTestContext.pageFrame
        .locator(canvasSelectors.page.targetElement)
        .count();
    expect(targetCount).toBe(0);
});
