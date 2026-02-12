// Spec 10 – Type inference and registry contract (Areas J1-J3)
//
// Covers: canvasElementTypeInference.ts, canvasElementDefinitions.ts,
//         CanvasElementContextControls.tsx.

import { test, expect } from "../fixtures/canvasTest";
import {
    dragPaletteItemToCanvas,
    getCanvasElementCount,
    getActiveCanvasElement,
    openContextMenuFromToolbar,
} from "../helpers/canvasActions";
import {
    expectCanvasElementCountToIncrease,
    expectAnyCanvasElementActive,
    expectContextControlsVisible,
} from "../helpers/canvasAssertions";
import { canvasSelectors } from "../helpers/canvasSelectors";
import { mainPaletteRows } from "../helpers/canvasMatrix";

// ── J1: Each palette type produces an element that the context controls
//        can handle (toolbar appears without error) ──────────────────────

for (const row of mainPaletteRows) {
    test(`J1: "${row.paletteItem}" element gets context controls without error`, async ({
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
        await expectAnyCanvasElementActive(pageFrame);

        // The context controls should render without JS errors
        await expectContextControlsVisible(pageFrame);
    });
}

// ── J2: Speech element has bloom-editable (type inference requirement) ──

test("J2: speech element has internal bloom-editable (inferrable as speech)", async ({
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
    const hasEditable =
        (await active.locator(canvasSelectors.page.bloomEditable).count()) > 0;
    expect(hasEditable).toBe(true);
});

// ── J2: Image element has bloom-imageContainer ──────────────────────────

test("J2: image element has internal imageContainer (inferrable as image)", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    const beforeCount = await getCanvasElementCount(pageFrame);
    await dragPaletteItemToCanvas({
        page,
        toolboxFrame,
        pageFrame,
        paletteItem: "image",
    });
    await expectCanvasElementCountToIncrease(pageFrame, beforeCount);
    const afterCount = await getCanvasElementCount(pageFrame);
    const newest = pageFrame
        .locator(canvasSelectors.page.canvasElements)
        .nth(afterCount - 1);
    const hasImageContainer =
        (await newest.locator(canvasSelectors.page.imageContainer).count()) > 0;
    expect(hasImageContainer).toBe(true);
});

// ── J2: Video element has bloom-videoContainer ──────────────────────────

test("J2: video element has internal videoContainer (inferrable as video)", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    const beforeCount = await getCanvasElementCount(pageFrame);
    await dragPaletteItemToCanvas({
        page,
        toolboxFrame,
        pageFrame,
        paletteItem: "video",
    });
    await expectCanvasElementCountToIncrease(pageFrame, beforeCount);
    const afterCount = await getCanvasElementCount(pageFrame);
    const newest = pageFrame
        .locator(canvasSelectors.page.canvasElements)
        .nth(afterCount - 1);
    const hasVideoContainer =
        (await newest.locator(canvasSelectors.page.videoContainer).count()) > 0;
    expect(hasVideoContainer).toBe(true);
});

// ── J3: Context menu renders stable content for speech ──────────────────

test("J3: context menu for speech renders stable items", async ({
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

    // The menu should have at least some items and render without crash
    const menuItems = pageFrame
        .locator(canvasSelectors.page.contextMenuList)
        .first()
        .locator("li");
    const count = await menuItems.count();
    expect(count).toBeGreaterThan(0);
});
