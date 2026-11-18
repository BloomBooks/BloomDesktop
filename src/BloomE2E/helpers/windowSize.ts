// Make the Bloom window's page area narrower or wider in the middle of a test, as a person does by
// dragging the window's edge.
//
// This emulates the size with the same CDP device-metrics override that BLOOM_E2E_VIEWPORT uses
// (fixtures/viewport.ts): the shell page and every frame in it lay out at the new size, and their
// ResizeObservers fire, but the WinForms window does not change. That reaches whatever in the web
// UI reacts to the size; it does not reach anything WinForms decides from the window's size.

import type { Page } from "@playwright/test";
import { resetViewportSize, setViewportSize } from "../fixtures/viewport";

/**
 * Give the Bloom window's page area this size, in CSS pixels, as if the window had been resized.
 * Lasts until restoreWindowSize() or until Bloom restarts.
 */
export async function resizeWindow(
    page: Page,
    size: { width: number; height: number },
): Promise<void> {
    await setViewportSize(page, size);
    await page.waitForFunction(
        ({ width }) => window.innerWidth === width,
        { width: size.width },
        { timeout: 10000 },
    );
}

/** Put the window's page area back to the size the run started with. */
export async function restoreWindowSize(page: Page): Promise<void> {
    await resetViewportSize(page);
}

/** The size of the Bloom window's page area now, in CSS pixels. */
export async function getWindowSize(
    page: Page,
): Promise<{ width: number; height: number }> {
    return page.evaluate(() => ({
        width: window.innerWidth,
        height: window.innerHeight,
    }));
}
