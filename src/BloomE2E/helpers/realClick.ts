// Click something with real mouse events.
//
// Some of Bloom's UI ignores a synthetic click. Book tiles in the collection grid, the Settings
// button, and the publish PREVIEW button all listen for the mousedown/mouseup sequence (or for
// pointer state a dispatched `click` event never sets), so `element.click()` from inside the page
// does nothing at all — no error, no effect, just a test that fails a timeout later on something
// unrelated.
//
// realClick moves the mouse to the element's centre and presses and releases there, which is what
// a person does. Playwright's own locator.click() already does this over CDP, so prefer it; reach
// for realClick when you have coordinates rather than a locator, or when you need the pointer to
// travel to the target first (hover-revealed controls).
//
// Tests never hand-roll Input.dispatchMouseEvent. If you need a gesture this file does not have,
// add it here.

import type { Locator, Page } from "@playwright/test";

/**
 * Click the element with a real mouse press and release at its centre, after scrolling it into
 * view. Throws a message naming the locator when the element has no on-screen box.
 */
export async function realClick(
    locator: Locator,
    timeoutMs = 15000,
): Promise<void> {
    await locator.waitFor({ state: "visible", timeout: timeoutMs });
    await locator.scrollIntoViewIfNeeded({ timeout: timeoutMs });
    const box = await locator.boundingBox({ timeout: timeoutMs });
    if (!box)
        throw new Error(
            `${locator} is visible but has no bounding box, so there is nowhere to click. ` +
                `It may be inside a collapsed or zero-size container.`,
        );
    await realClickAt(
        locator.page(),
        box.x + box.width / 2,
        box.y + box.height / 2,
    );
}

/**
 * Click at a point in the page's own coordinates, with a real mouse press and release. Use this
 * for a target that has no locator of its own, such as a spot on a canvas.
 */
export async function realClickAt(
    page: Page,
    x: number,
    y: number,
): Promise<void> {
    // Move first: controls that appear on hover need the pointer to arrive before it presses, and
    // a press with no preceding move leaves some handlers with no pointer position to read.
    await page.mouse.move(x, y);
    await page.mouse.down();
    await page.mouse.up();
}
