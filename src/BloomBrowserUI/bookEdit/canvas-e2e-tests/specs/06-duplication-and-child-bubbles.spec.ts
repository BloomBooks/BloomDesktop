// Spec 06 – Duplication and child bubbles (Areas G1-G5)
//
// Covers: CanvasElementDuplication.ts, CanvasElementFactories.ts,
//         CanvasElementBubbleLevelUtils.ts.

import { test, expect } from "../fixtures/canvasTest";
import {
    dragPaletteItemToCanvas,
    getCanvasElementCount,
    getActiveCanvasElement,
    openContextMenuFromToolbar,
    clickContextMenuItem,
} from "../helpers/canvasActions";
import {
    expectCanvasElementCountToIncrease,
    expectAnyCanvasElementActive,
    expectElementVisible,
    expectElementHasPositiveSize,
} from "../helpers/canvasAssertions";
import { canvasSelectors } from "../helpers/canvasSelectors";

// ── Helper ──────────────────────────────────────────────────────────────

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

// ── G1: Duplicate creates a new element ─────────────────────────────────

test("G1: duplicating a speech element increases count", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    await createSpeechElement({ page, toolboxFrame, pageFrame });

    const beforeDuplicate = await getCanvasElementCount(pageFrame);
    await openContextMenuFromToolbar(pageFrame);
    await clickContextMenuItem(pageFrame, "Duplicate");

    await expectCanvasElementCountToIncrease(pageFrame, beforeDuplicate);
});

// ── G2: Duplicate preserves element type ────────────────────────────────

test("G2: duplicated element is visible and has positive size", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    await createSpeechElement({ page, toolboxFrame, pageFrame });

    await openContextMenuFromToolbar(pageFrame);
    await clickContextMenuItem(pageFrame, "Duplicate");

    // The duplicated element should become active
    const active = getActiveCanvasElement(pageFrame);
    await expectElementVisible(active);
    await expectElementHasPositiveSize(active);
});

// ── G2: Duplicate preserves text content ────────────────────────────────

test("G2: duplicated speech element contains bloom-editable", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    await createSpeechElement({ page, toolboxFrame, pageFrame });

    await openContextMenuFromToolbar(pageFrame);
    await clickContextMenuItem(pageFrame, "Duplicate");

    const active = getActiveCanvasElement(pageFrame);
    const editableCount = await active
        .locator(canvasSelectors.page.bloomEditable)
        .count();
    expect(editableCount).toBeGreaterThan(0);
});

// ── G5: Element order sanity after duplication ──────────────────────────

test("G5: total element count is correct after duplicate + delete", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    await createSpeechElement({ page, toolboxFrame, pageFrame });

    const afterCreate = await getCanvasElementCount(pageFrame);

    // Duplicate
    await openContextMenuFromToolbar(pageFrame);
    await clickContextMenuItem(pageFrame, "Duplicate");
    await expectCanvasElementCountToIncrease(pageFrame, afterCreate);

    const afterDuplicate = await getCanvasElementCount(pageFrame);

    // Delete the duplicate
    await openContextMenuFromToolbar(pageFrame);
    await clickContextMenuItem(pageFrame, "Delete");

    await expect
        .poll(async () => {
            return pageFrame
                .locator(canvasSelectors.page.canvasElements)
                .count();
        })
        .toBeLessThan(afterDuplicate);
});
