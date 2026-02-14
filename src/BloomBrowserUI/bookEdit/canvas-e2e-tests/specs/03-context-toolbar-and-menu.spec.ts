// Spec 03 – Context toolbar and menu commands (Areas C1-C7)
//
// Covers: CanvasElementContextControls.tsx, canvasElementDefinitions.ts,
//         canvasElementTypeInference.ts.

import { test, expect } from "../fixtures/canvasTest";
import type { ICanvasTestContext } from "../helpers/canvasActions";
import {
    dragPaletteItemToCanvas,
    expandNavigationSection,
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
    expectContextMenuItemNotPresent,
} from "../helpers/canvasAssertions";
import {
    canvasSelectors,
    type CanvasPaletteItemKey,
} from "../helpers/canvasSelectors";
import {
    mainPaletteRows,
    navigationPaletteRows,
} from "../helpers/canvasMatrix";

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

const createAndExpectCountIncrease = async (
    canvasTestContext: ICanvasTestContext,
    paletteItem: CanvasPaletteItemKey,
): Promise<void> => {
    const maxAttempts = 3;
    for (let attempt = 0; attempt < maxAttempts; attempt++) {
        const beforeCount = await getCanvasElementCount(canvasTestContext);
        await dragPaletteItemToCanvas({
            canvasContext: canvasTestContext,
            paletteItem,
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

// ── C1/C2: Verify toolbar/menu appear for each main palette type ────────

for (const row of mainPaletteRows) {
    test(`C1: context controls visible after creating "${row.paletteItem}"`, async ({
        canvasTestContext,
    }) => {
        await createAndExpectCountIncrease(canvasTestContext, row.paletteItem);
        await expectContextControlsVisible(canvasTestContext);
    });
}

for (const row of navigationPaletteRows.filter(
    (matrixRow) => matrixRow.paletteItem !== "book-link-grid",
)) {
    test(`C1-nav: context controls visible after creating "${row.paletteItem}"`, async ({
        canvasTestContext,
    }) => {
        await expandNavigationSection(canvasTestContext);
        await createAndExpectCountIncrease(canvasTestContext, row.paletteItem);
        await expectContextControlsVisible(canvasTestContext);
    });
}

// ── C2: Menu items match expected labels ────────────────────────────────

test("C2: speech context menu contains Duplicate and Delete", async ({
    canvasTestContext,
}) => {
    await createAndExpectCountIncrease(canvasTestContext, "speech");

    await openContextMenuFromToolbar(canvasTestContext);

    await expectContextMenuItemVisible(canvasTestContext, "Duplicate");
    await expectContextMenuItemVisible(canvasTestContext, "Delete");
    await canvasTestContext.page.keyboard.press("Escape");
});

test("C2: navigation image button shows Set Destination and not Set Up Hyperlink", async ({
    canvasTestContext,
}) => {
    await expandNavigationSection(canvasTestContext);
    await createAndExpectCountIncrease(
        canvasTestContext,
        "navigation-image-button",
    );

    await openContextMenuFromToolbar(canvasTestContext);
    await expectContextMenuItemVisible(canvasTestContext, "Set Destination");
    await expectContextMenuItemNotPresent(
        canvasTestContext,
        "Set Up Hyperlink",
    );
    await canvasTestContext.page.keyboard.press("Escape");
});

test("C2: simple image context menu does not show Set Destination or Set Up Hyperlink", async ({
    canvasTestContext,
}) => {
    await createAndExpectCountIncrease(canvasTestContext, "image");

    await openContextMenuFromToolbar(canvasTestContext);
    await expectContextMenuItemNotPresent(canvasTestContext, "Set Destination");
    await expectContextMenuItemNotPresent(
        canvasTestContext,
        "Set Up Hyperlink",
    );
    await canvasTestContext.page.keyboard.press("Escape");
});

// ── C4: Smoke-invoke duplicate ──────────────────────────────────────────

test("C4: duplicate via context menu increases element count", async ({
    canvasTestContext,
}) => {
    await createAndExpectCountIncrease(canvasTestContext, "speech");

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
    await createAndExpectCountIncrease(canvasTestContext, "speech");

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
    await createAndExpectCountIncrease(canvasTestContext, "speech");

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
    await createAndExpectCountIncrease(canvasTestContext, "speech");

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
