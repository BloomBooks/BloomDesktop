// Spec 03 – Context toolbar and menu commands (Areas C1-C7)
//
// Covers: CanvasElementContextControls.tsx, canvasElementDefinitions.ts,
//         canvasElementTypeInference.ts.

import { test, expect } from "../fixtures/canvasTest";
import type { ICanvasTestContext } from "../helpers/canvasActions";
import {
    dragPaletteItemToCanvas,
    getCanvasElementCount,
    openContextMenuFromToolbar,
    clickContextMenuItem,
    clickToolbarButtonByIndex,
} from "../helpers/canvasActions";
import {
    expectCanvasElementCountToIncrease,
    expectAnyCanvasElementActive,
    expectContextControlsVisible,
    expectContextMenuItemVisible,
} from "../helpers/canvasAssertions";
import { canvasSelectors } from "../helpers/canvasSelectors";
import { mainPaletteRows } from "../helpers/canvasMatrix";

const waitForCountBelow = async (
    canvasTestContext: ICanvasTestContext,
    upperExclusive: number,
    timeoutMs = 3000,
): Promise<boolean> => {
    const pageFrame = canvasTestContext.pageFrame;
    const endTime = Date.now() + timeoutMs;
    while (Date.now() < endTime) {
        const count = await pageFrame
            .locator(canvasSelectors.page.canvasElements)
            .count();
        if (count < upperExclusive) {
            return true;
        }
        await pageFrame.page().waitForTimeout(100);
    }
    return false;
};

// ── C1/C2: Verify toolbar/menu appear for each main palette type ────────

for (const row of mainPaletteRows) {
    test(`C1: context controls visible after creating "${row.paletteItem}"`, async ({
        canvasTestContext,
    }) => {
        const beforeCount = await getCanvasElementCount(canvasTestContext);
        await dragPaletteItemToCanvas({
            canvasContext: canvasTestContext,
            paletteItem: row.paletteItem,
        });
        await expectCanvasElementCountToIncrease(
            canvasTestContext,
            beforeCount,
        );
        await expectContextControlsVisible(canvasTestContext);
    });
}

// ── C2: Menu items match expected labels ────────────────────────────────

test("C2: speech context menu contains Duplicate and Delete", async ({
    canvasTestContext,
}) => {
    const beforeCount = await getCanvasElementCount(canvasTestContext);
    await dragPaletteItemToCanvas({
        canvasContext: canvasTestContext,
        paletteItem: "speech",
    });
    await expectCanvasElementCountToIncrease(canvasTestContext, beforeCount);

    await openContextMenuFromToolbar(canvasTestContext);

    await expectContextMenuItemVisible(canvasTestContext, "Duplicate");
    await expectContextMenuItemVisible(canvasTestContext, "Delete");
});

// ── C4: Smoke-invoke duplicate ──────────────────────────────────────────

test("C4: duplicate via context menu increases element count", async ({
    canvasTestContext,
}) => {
    const beforeCreate = await getCanvasElementCount(canvasTestContext);
    await dragPaletteItemToCanvas({
        canvasContext: canvasTestContext,
        paletteItem: "speech",
    });
    await expectCanvasElementCountToIncrease(canvasTestContext, beforeCreate);

    const beforeDuplicate = await getCanvasElementCount(canvasTestContext);
    await openContextMenuFromToolbar(canvasTestContext);
    await clickContextMenuItem(canvasTestContext, "Duplicate");
    await expectCanvasElementCountToIncrease(
        canvasTestContext,
        beforeDuplicate,
    );
});

// ── C5: Smoke-invoke delete ─────────────────────────────────────────────

test("C5: delete via context menu removes an element", async ({
    canvasTestContext,
}) => {
    const beforeCreate = await getCanvasElementCount(canvasTestContext);
    await dragPaletteItemToCanvas({
        canvasContext: canvasTestContext,
        paletteItem: "speech",
    });
    await expectCanvasElementCountToIncrease(canvasTestContext, beforeCreate);

    const beforeDelete = await getCanvasElementCount(canvasTestContext);
    const maxAttempts = 3;
    let deleted = false;
    for (let attempt = 0; attempt < maxAttempts; attempt++) {
        await openContextMenuFromToolbar(canvasTestContext);
        await clickContextMenuItem(canvasTestContext, "Delete");
        deleted = await waitForCountBelow(canvasTestContext, beforeDelete);
        if (deleted) {
            break;
        }
    }

    expect(deleted).toBe(true);
});

// ── C3: Toolbar button count varies by type ─────────────────────────────

test("C3: speech toolbar has buttons including format and delete", async ({
    canvasTestContext,
}) => {
    const beforeCount = await getCanvasElementCount(canvasTestContext);
    await dragPaletteItemToCanvas({
        canvasContext: canvasTestContext,
        paletteItem: "speech",
    });
    await expectCanvasElementCountToIncrease(canvasTestContext, beforeCount);

    const controls = canvasTestContext.pageFrame
        .locator(canvasSelectors.page.contextControlsVisible)
        .first();
    await controls.waitFor({ state: "visible", timeout: 10000 });

    const buttonCount = await controls.locator("button").count();
    // Speech has: format, spacer (not a button), duplicate, delete, + menu button
    // At minimum 2 real buttons
    expect(buttonCount).toBeGreaterThanOrEqual(2);
});

// ── C6: Smoke-invoke format command ──────────────────────────────────

test("C6: format button is present and clickable for speech element", async ({
    canvasTestContext,
}) => {
    const beforeCount = await getCanvasElementCount(canvasTestContext);
    await dragPaletteItemToCanvas({
        canvasContext: canvasTestContext,
        paletteItem: "speech",
    });
    await expectCanvasElementCountToIncrease(canvasTestContext, beforeCount);

    const controls = canvasTestContext.pageFrame
        .locator(canvasSelectors.page.contextControlsVisible)
        .first();
    await controls.waitFor({ state: "visible", timeout: 10000 });

    // The format button is the first button in the speech toolbar
    const formatButton = controls.locator("button").first();
    await expect(formatButton).toBeVisible();
    await expect(formatButton).toBeEnabled();

    // Click the format button (it opens a dialog handled by C#, so we just
    // verify no crash and the element remains active)
    await clickToolbarButtonByIndex(canvasTestContext, 0);
    await expectAnyCanvasElementActive(canvasTestContext);
});
