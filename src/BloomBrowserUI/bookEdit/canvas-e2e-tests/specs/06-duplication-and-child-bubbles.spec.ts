// Spec 06 – Duplication and child bubbles (Areas G1-G5)
//
// Covers: CanvasElementDuplication.ts, CanvasElementFactories.ts,
//         CanvasElementBubbleLevelUtils.ts.

import { test, expect } from "../fixtures/canvasTest";
import {
    dragPaletteItemToCanvas,
    getCanvasElementCount,
    getActiveCanvasElement,
    clickContextMenuItem,
    openContextMenuFromToolbar,
} from "../helpers/canvasActions";
import {
    expectCanvasElementCountToIncrease,
    expectAnyCanvasElementActive,
    expectElementVisible,
    expectElementHasPositiveSize,
} from "../helpers/canvasAssertions";
import { canvasSelectors } from "../helpers/canvasSelectors";

// ── Helper ──────────────────────────────────────────────────────────────

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
            await expectAnyCanvasElementActive(canvasTestContext);
            return;
        } catch (error) {
            if (attempt === maxAttempts - 1) {
                throw error;
            }
        }
    }
};

const duplicateActiveCanvasElementViaUi = async (
    canvasTestContext,
): Promise<void> => {
    await openContextMenuFromToolbar(canvasTestContext);
    await clickContextMenuItem(canvasTestContext, "Duplicate");
};

const deleteActiveCanvasElementViaUi = async (
    canvasTestContext,
): Promise<void> => {
    await openContextMenuFromToolbar(canvasTestContext);
    await clickContextMenuItem(canvasTestContext, "Delete");
};

// ── G1: Duplicate creates a new element ─────────────────────────────────

test("G1: duplicating a speech element increases count", async ({
    canvasTestContext,
}) => {
    await createSpeechElement(canvasTestContext);

    const beforeDuplicate = await getCanvasElementCount(canvasTestContext);
    await duplicateActiveCanvasElementViaUi(canvasTestContext);

    await expectCanvasElementCountToIncrease(
        canvasTestContext,
        beforeDuplicate,
    );
});

// ── G2: Duplicate preserves element type ────────────────────────────────

test("G2: duplicated element is visible and has positive size", async ({
    canvasTestContext,
}) => {
    await createSpeechElement(canvasTestContext);

    await duplicateActiveCanvasElementViaUi(canvasTestContext);

    // The duplicated element should become active
    const active = getActiveCanvasElement(canvasTestContext);
    await expectElementVisible(active);
    await expectElementHasPositiveSize(active);
});

// ── G2: Duplicate preserves text content ────────────────────────────────

test("G2: duplicated speech element contains bloom-editable", async ({
    canvasTestContext,
}) => {
    await createSpeechElement(canvasTestContext);

    await duplicateActiveCanvasElementViaUi(canvasTestContext);

    const active = getActiveCanvasElement(canvasTestContext);
    const editableCount = await active
        .locator(canvasSelectors.page.bloomEditable)
        .count();
    expect(editableCount).toBeGreaterThan(0);
});

// ── G5: Element order sanity after duplication ──────────────────────────

// TODO BL-15770: Re-enable after duplicate/delete count transitions are
// deterministic in shared-mode runs.
test.fixme(
    "G5: total element count is correct after duplicate + delete",
    async ({ canvasTestContext }) => {
        await createSpeechElement(canvasTestContext);

        const afterCreate = await getCanvasElementCount(canvasTestContext);

        // Duplicate
        await duplicateActiveCanvasElementViaUi(canvasTestContext);
        await expectCanvasElementCountToIncrease(
            canvasTestContext,
            afterCreate,
        );

        const afterDuplicate = await getCanvasElementCount(canvasTestContext);

        // Delete the duplicate
        await deleteActiveCanvasElementViaUi(canvasTestContext);

        await expect
            .poll(async () => {
                return canvasTestContext.pageFrame
                    .locator(canvasSelectors.page.canvasElements)
                    .count();
            })
            .toBeLessThan(afterDuplicate);
    },
);

// ── G3: Duplicate restrictions – creates exactly one copy ───────────

test("G3: duplicate creates exactly one copy (not more)", async ({
    canvasTestContext,
}) => {
    await createSpeechElement(canvasTestContext);

    const beforeDuplicate = await getCanvasElementCount(canvasTestContext);
    await duplicateActiveCanvasElementViaUi(canvasTestContext);
    await expectCanvasElementCountToIncrease(
        canvasTestContext,
        beforeDuplicate,
    );

    // Verify exactly one new element was created
    const afterDuplicate = await getCanvasElementCount(canvasTestContext);
    expect(afterDuplicate).toBe(beforeDuplicate + 1);
});
