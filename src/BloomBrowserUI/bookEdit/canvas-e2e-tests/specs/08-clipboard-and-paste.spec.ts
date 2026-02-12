// Spec 08 – Clipboard and paste image flows (Areas I1-I3)
//
// Covers: CanvasElementClipboard.ts.
//
// Note: True clipboard paste is difficult to test reliably in Playwright
// because it requires real clipboard access and native image data. These
// tests verify the preconditions and behaviors around paste readiness.

import { test, expect } from "../fixtures/canvasTest";
import type { Frame, Page } from "playwright/test";
import {
    dragPaletteItemToCanvas,
    getCanvasElementCount,
} from "../helpers/canvasActions";
import {
    expectCanvasElementCountToIncrease,
    expectAnyCanvasElementActive,
} from "../helpers/canvasAssertions";
import { canvasSelectors } from "../helpers/canvasSelectors";

const createElementWithRetry = async ({
    page,
    toolboxFrame,
    pageFrame,
    paletteItem,
}: {
    page: Page;
    toolboxFrame: Frame;
    pageFrame: Frame;
    paletteItem: "image" | "video" | "speech";
}) => {
    const maxAttempts = 3;

    for (let attempt = 0; attempt < maxAttempts; attempt++) {
        const beforeCount = await getCanvasElementCount(pageFrame);
        await dragPaletteItemToCanvas({
            page,
            toolboxFrame,
            pageFrame,
            paletteItem,
        });

        try {
            await expectCanvasElementCountToIncrease(pageFrame, beforeCount);
            return;
        } catch (error) {
            if (attempt === maxAttempts - 1) {
                throw error;
            }
        }
    }
};

// ── I1: Image element has an image container (paste target) ─────────────

test("I1: newly created image element has an image container", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    await createElementWithRetry({
        page,
        toolboxFrame,
        pageFrame,
        paletteItem: "image",
    });

    const afterCount = await getCanvasElementCount(pageFrame);
    const newest = pageFrame
        .locator(canvasSelectors.page.canvasElements)
        .nth(afterCount - 1);
    const imgContainerCount = await newest
        .locator(canvasSelectors.page.imageContainer)
        .count();
    expect(imgContainerCount).toBeGreaterThan(0);
});

// ── I2: Video element has a video container ─────────────────────────────

test("I2: newly created video element has a video container", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    await createElementWithRetry({
        page,
        toolboxFrame,
        pageFrame,
        paletteItem: "video",
    });

    const afterCount = await getCanvasElementCount(pageFrame);
    const newest = pageFrame
        .locator(canvasSelectors.page.canvasElements)
        .nth(afterCount - 1);
    const videoContainerCount = await newest
        .locator(canvasSelectors.page.videoContainer)
        .count();
    expect(videoContainerCount).toBeGreaterThan(0);
});

// ── I-general: Speech element has editable text (paste target for text) ──

test("I-general: speech element has bloom-editable for text content", async ({
    page,
    toolboxFrame,
    pageFrame,
}) => {
    await createElementWithRetry({
        page,
        toolboxFrame,
        pageFrame,
        paletteItem: "speech",
    });

    const afterCount = await getCanvasElementCount(pageFrame);
    const newest = pageFrame
        .locator(canvasSelectors.page.canvasElements)
        .nth(afterCount - 1);
    const editableCount = await newest
        .locator(canvasSelectors.page.bloomEditable)
        .count();
    expect(editableCount).toBeGreaterThan(0);
});
