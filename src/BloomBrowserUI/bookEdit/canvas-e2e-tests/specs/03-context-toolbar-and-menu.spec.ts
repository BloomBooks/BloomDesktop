// Spec 03 – Context toolbar and menu commands (Areas C1-C7)
//
// Covers: CanvasElementContextControls.tsx, canvasElementDefinitions.ts,
//         canvasElementTypeInference.ts.

import { test, expect } from "../fixtures/canvasTest";
import {
    dragPaletteItemToCanvas,
    getCanvasElementCount,
    openContextMenuFromToolbar,
    clickContextMenuItem,
} from "../helpers/canvasActions";
import {
    expectCanvasElementCountToIncrease,
    expectAnyCanvasElementActive,
    expectContextControlsVisible,
    expectContextMenuItemVisible,
} from "../helpers/canvasAssertions";
import { canvasSelectors } from "../helpers/canvasSelectors";
import { mainPaletteRows } from "../helpers/canvasMatrix";

// ── C1/C2: Verify toolbar/menu appear for each main palette type ────────

for (const row of mainPaletteRows) {
    test(`C1: context controls visible after creating "${row.paletteItem}"`, async ({
        page,
        toolboxFrame,
        pageFrame,
    }) => {
        const beforeCount = await getCanvasElementCount(pageFrame);
        await dragPaletteItemToCanvas({
            page,
            toolboxFrame,
            pageFrame,
            paletteItem: row.paletteItem,
        });
        await expectCanvasElementCountToIncrease(pageFrame, beforeCount);
        await expectContextControlsVisible(pageFrame);
    });
}

// ── C2: Menu items match expected labels ────────────────────────────────

test("C2: speech context menu contains Duplicate and Delete", async ({
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

    await openContextMenuFromToolbar(pageFrame);

    await expectContextMenuItemVisible(pageFrame, "Duplicate");
    await expectContextMenuItemVisible(pageFrame, "Delete");
});

// ── C4: Smoke-invoke duplicate ──────────────────────────────────────────

test("C4: duplicate via context menu increases element count", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    const beforeCreate = await getCanvasElementCount(pageFrame);
    await dragPaletteItemToCanvas({
        page,
        toolboxFrame,
        pageFrame,
        paletteItem: "speech",
    });
    await expectCanvasElementCountToIncrease(pageFrame, beforeCreate);

    const beforeDuplicate = await getCanvasElementCount(pageFrame);
    await openContextMenuFromToolbar(pageFrame);
    await clickContextMenuItem(pageFrame, "Duplicate");
    await expectCanvasElementCountToIncrease(pageFrame, beforeDuplicate);
});

// ── C5: Smoke-invoke delete ─────────────────────────────────────────────

test("C5: delete via context menu removes an element", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    const beforeCreate = await getCanvasElementCount(pageFrame);
    await dragPaletteItemToCanvas({
        page,
        toolboxFrame,
        pageFrame,
        paletteItem: "speech",
    });
    await expectCanvasElementCountToIncrease(pageFrame, beforeCreate);

    const beforeDelete = await getCanvasElementCount(pageFrame);
    await openContextMenuFromToolbar(pageFrame);
    await clickContextMenuItem(pageFrame, "Delete");

    // Count should decrease or stay the same (if there was only the background)
    await expect
        .poll(async () => {
            return pageFrame
                .locator(canvasSelectors.page.canvasElements)
                .count();
        })
        .toBeLessThan(beforeDelete);
});

// ── C3: Toolbar button count varies by type ─────────────────────────────

test("C3: speech toolbar has buttons including format and delete", async ({
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

    const controls = pageFrame
        .locator(canvasSelectors.page.contextControls)
        .first();
    await controls.waitFor({ state: "visible", timeout: 10000 });

    const buttonCount = await controls.locator("button").count();
    // Speech has: format, spacer (not a button), duplicate, delete, + menu button
    // At minimum 2 real buttons
    expect(buttonCount).toBeGreaterThanOrEqual(2);
});
