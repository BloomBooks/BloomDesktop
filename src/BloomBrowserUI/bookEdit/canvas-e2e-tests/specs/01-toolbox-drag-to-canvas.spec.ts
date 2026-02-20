// Spec 01 – Drag from toolbox onto canvas (Areas A1-A5)
//
// Covers: CanvasElementItem.tsx, CanvasElementFactories.ts,
//         canvasElementDraggables.ts, canvasElementConstants.ts.

import { test, expect } from "../fixtures/canvasTest";
import {
    dragPaletteItemToCanvas,
    expandNavigationSection,
    getCanvasElementCount,
    getActiveCanvasElement,
    pageFrameToTopLevel,
} from "../helpers/canvasActions";
import {
    expectCanvasElementCountToIncrease,
    expectAnyCanvasElementActive,
    expectCanvasHasElementClass,
    expectElementVisible,
    expectElementHasPositiveSize,
    expectElementNearPoint,
} from "../helpers/canvasAssertions";
import { canvasSelectors } from "../helpers/canvasSelectors";
import {
    mainPaletteRows,
    navigationPaletteRows,
} from "../helpers/canvasMatrix";

// ── A1: Drag each main palette element type to canvas ───────────────────

for (const row of mainPaletteRows) {
    test(`A1: drag "${row.paletteItem}" onto canvas creates an element`, async ({
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
    });
}

// ── A1 (navigation): Drag navigation palette items after expanding ─────

// Navigation palette items require expanding a TriangleCollapse.
// book-link-grid is limited to one per page so it is skipped.
// The cross-iframe drag for navigation items can be flaky so we
// allow 1 retry.
for (const row of navigationPaletteRows) {
    // TODO: Replace this skip with a deterministic lifecycle test once we have
    // a stable way to reset/recreate book-link-grid across shared-mode runs.
    // TODO BL-15770: Re-enable these palette items when cross-iframe navigation
    // drag for navigation image button variants is reliable in CI/shared mode.
    const skip =
        row.paletteItem === "book-link-grid" ||
        row.paletteItem === "navigation-label-button" ||
        row.paletteItem === "navigation-image-button" ||
        row.paletteItem === "navigation-image-with-label-button";
    const testFn = skip ? test.skip : test;
    testFn(
        `A1-nav: drag "${row.paletteItem}" onto canvas creates an element`,
        async ({ canvasTestContext }) => {
            // TODO: Remove this retry annotation once cross-iframe navigation
            // palette dragging is consistently reliable in CI and headed runs.
            test.info().annotations.push({
                type: "retry",
                description: "cross-iframe drag can be flaky",
            });
            await expandNavigationSection(canvasTestContext);

            const beforeCount = await getCanvasElementCount(canvasTestContext);

            await dragPaletteItemToCanvas({
                canvasContext: canvasTestContext,
                paletteItem: row.paletteItem,
            });

            await expectCanvasElementCountToIncrease(
                canvasTestContext,
                beforeCount,
            );
        },
    );
}

// ── A2: Drop at different points and verify multiple creation ────────────

test("A2: dropping two speech items creates distinct elements", async ({
    canvasTestContext,
}) => {
    const beforeCount = await getCanvasElementCount(canvasTestContext);

    await dragPaletteItemToCanvas({
        canvasContext: canvasTestContext,
        paletteItem: "speech",
        dropOffset: { x: 60, y: 60 },
    });

    await expectCanvasElementCountToIncrease(canvasTestContext, beforeCount);

    const afterFirstCount = await getCanvasElementCount(canvasTestContext);

    await dragPaletteItemToCanvas({
        canvasContext: canvasTestContext,
        paletteItem: "speech",
        dropOffset: { x: 250, y: 200 },
    });

    await expectCanvasElementCountToIncrease(
        canvasTestContext,
        afterFirstCount,
    );
});

// ── A5: Verify canvas class state reflects element presence ─────────────

test("A5: canvas gets bloom-has-canvas-element class after dropping an element", async ({
    canvasTestContext,
}) => {
    await dragPaletteItemToCanvas({
        canvasContext: canvasTestContext,
        paletteItem: "speech",
    });

    await expectCanvasElementCountToIncrease(canvasTestContext, 0);
    await expectCanvasHasElementClass(canvasTestContext, true);
});

// ── A-general: Newly created element is selected and visible ────────────

test("newly created element is active and has positive size", async ({
    canvasTestContext,
}) => {
    await dragPaletteItemToCanvas({
        canvasContext: canvasTestContext,
        paletteItem: "speech",
    });

    await expectCanvasElementCountToIncrease(canvasTestContext, 0);
    await expectAnyCanvasElementActive(canvasTestContext);

    const active = getActiveCanvasElement(canvasTestContext);
    await expectElementVisible(active);
    await expectElementHasPositiveSize(active);
});

// ── A3: Verify coordinate mapping – element lands near the drop point ──

test("A3: element created near the specified drop offset", async ({
    canvasTestContext,
}) => {
    const dropOffset = { x: 180, y: 150 };

    const beforeCount = await getCanvasElementCount(canvasTestContext);
    await dragPaletteItemToCanvas({
        canvasContext: canvasTestContext,
        paletteItem: "speech",
        dropOffset,
    });
    await expectCanvasElementCountToIncrease(canvasTestContext, beforeCount);
    await expectAnyCanvasElementActive(canvasTestContext);

    // Compute the expected top-level coordinate by offsetting from the
    // canvas bounding box within the page frame.
    const canvasBox = await canvasTestContext.pageFrame
        .locator(canvasSelectors.page.canvas)
        .first()
        .boundingBox();
    expect(canvasBox).toBeTruthy();

    const active = getActiveCanvasElement(canvasTestContext);
    const activeBox = await active.boundingBox();
    expect(activeBox).toBeTruthy();

    // The element center should be roughly near the drop point within the
    // canvas (tolerance accounts for element sizing/centering adjustments).
    const expectedCenterX = canvasBox!.x + dropOffset.x;
    const expectedCenterY = canvasBox!.y + dropOffset.y;

    await expectElementNearPoint(active, expectedCenterX, expectedCenterY, 80);
});
