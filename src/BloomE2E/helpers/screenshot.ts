// Capture an image of one element in Bloom's WebView2, including an element taller than the
// window.
//
// A book page is the case that forces this. A `.bloom-page` in the Edit tab is usually taller than
// the WebView2's window, and the obvious route, `Page.captureScreenshot` with
// `captureBeyondViewport: true`, hangs in WebView2: no response, no error, and the run dies on a
// timeout with nothing to read. The pattern that works, and the one this module encodes, is:
//
//   1. Enlarge the window with Emulation.setDeviceMetricsOverride, big enough for the whole
//      element.
//   2. Measure the element only after that, because enlarging the window re-lays out the page.
//   3. Screenshot with a `clip` for the element's box.
//   4. Emulation.clearDeviceMetricsOverride, always, even when the capture failed. A left-over
//      override leaves the rest of the run driving a Bloom of the wrong size.
//
// Every CDP request also gets a timeout, because a WebView2 CDP call that never answers otherwise
// stops the run rather than failing it. (AUTOMATION-DEBT.md: "Driver-level CDP footguns that the
// automation library must absorb".)
//
// Tests do not open CDP sessions of their own. If you need a capture this file does not do, add it
// here.

import type { CDPSession, Locator, Page } from "@playwright/test";
import { editablePageFrame, waitForEditablePage } from "./bookMaking";

/** An element's image, and the size of that image in pixels. */
export interface IElementImage {
    /** The PNG bytes. */
    png: Buffer;
    /** The image's width in pixels, read from the PNG itself. */
    width: number;
    /** The image's height in pixels, read from the PNG itself. */
    height: number;
    /**
     * The element's own box, in CSS pixels, at the moment we captured it. The image should be this
     * size (to within the rounding CDP does), so a caller can check that it captured the element
     * rather than the window.
     */
    elementWidth: number;
    /** The element's own height in CSS pixels at the moment we captured it. */
    elementHeight: number;
}

/** The CDP method names Playwright's session accepts. */
type CdpMethod = Parameters<CDPSession["send"]>[0];

/** How long any one CDP request may take before we call the driver stuck. */
const CDP_TIMEOUT_MS = 30000;

// The largest window we will pretend to have. A book page cannot legitimately need more than this,
// and an absurd number here would make WebView2 try to allocate an absurd surface.
const MAX_OVERRIDE_PIXELS = 8000;

/**
 * Capture the `.bloom-page` the Edit tab is showing.
 *
 * This waits for the Edit tab to have a page with editable text in it first, so a caller does not
 * have to; call goToPage (helpers/bookMaking.ts) first to choose which page.
 */
export async function captureCurrentBookPage(
    page: Page,
    timeoutMs = 90000,
): Promise<IElementImage> {
    await waitForEditablePage(page, timeoutMs);
    const bloomPage = editablePageFrame(page).locator(".bloom-page").first();
    return captureElement(bloomPage, timeoutMs);
}

/**
 * Capture one element as a PNG, whatever its size, and return the bytes with the image's real
 * dimensions.
 *
 * The element may be inside an iframe: the box is measured in the top document's coordinates,
 * which is what CDP's clip wants. Fails with a message naming the locator when the element has no
 * on-screen box, which is what a collapsed or zero-size container looks like from here.
 */
export async function captureElement(
    locator: Locator,
    timeoutMs = 30000,
): Promise<IElementImage> {
    const page = locator.page();
    await locator.waitFor({ state: "visible", timeout: timeoutMs });

    const session = await page.context().newCDPSession(page);
    // Set once the capture has produced an image, so the cleanup below can tell a clear failure
    // that is the only thing wrong from one that is trailing a capture error.
    let captured: IElementImage | undefined;
    try {
        // How big the window has to be for the whole element to be laid out at once. Ask for the
        // element's own scroll size, plus where it sits, rather than the document's size: the
        // document includes page-list thumbnails and other chrome we are not capturing.
        const wanted = await elementExtent(locator, timeoutMs);
        // An element bigger than the cap would be clipped to its full box against a window that
        // was never made large enough, so the image would be a truncated element rather than a
        // failure, and nothing downstream could tell: the box we measure afterwards is measured
        // inside the too-small window, so it agrees with the truncated image. Say so instead.
        if (
            wanted.right > MAX_OVERRIDE_PIXELS ||
            wanted.bottom > MAX_OVERRIDE_PIXELS
        ) {
            throw new Error(
                `captureElement cannot capture this element: laying all of it out needs a window ` +
                    `${Math.ceil(wanted.right)}x${Math.ceil(wanted.bottom)} pixels, and the ` +
                    `largest this helper will ask WebView2 for is ${MAX_OVERRIDE_PIXELS}. ` +
                    `Capture a smaller part of it, or raise MAX_OVERRIDE_PIXELS if WebView2 can ` +
                    `still allocate that.`,
            );
        }
        const viewport = page.viewportSize();
        const overrideWidth = clampOverride(
            Math.max(wanted.right, viewport?.width ?? 0),
        );
        const overrideHeight = clampOverride(
            Math.max(wanted.bottom, viewport?.height ?? 0),
        );

        await sendWithTimeout(session, "Emulation.setDeviceMetricsOverride", {
            width: overrideWidth,
            height: overrideHeight,
            deviceScaleFactor: 1,
            mobile: false,
        });

        // Measure only now. Enlarging the window re-lays out the page, and on a book page it
        // genuinely moves things: the Edit tab centres the page in the space it has.
        const box = await documentBox(locator, timeoutMs);

        const result = (await sendWithTimeout(
            session,
            "Page.captureScreenshot",
            {
                format: "png",
                // No captureBeyondViewport: it hangs in WebView2. The override above is what makes
                // the whole element fit inside the window instead.
                clip: {
                    x: box.x,
                    y: box.y,
                    width: box.width,
                    height: box.height,
                    scale: 1,
                },
            },
        )) as { data: string };

        const png = Buffer.from(result.data, "base64");
        const size = readPngSize(png);
        captured = {
            png,
            width: size.width,
            height: size.height,
            elementWidth: box.width,
            elementHeight: box.height,
        };
        return captured;
    } finally {
        // Always, including after a failed capture: the next test in this worker drives the same
        // Bloom, and an emulated 8000-pixel window would make everything it sees wrong.
        let clearError: unknown;
        await sendWithTimeout(
            session,
            "Emulation.clearDeviceMetricsOverride",
            {},
        ).catch((error) => {
            clearError = error;
        });
        await session.detach().catch(() => undefined);

        if (clearError !== undefined) {
            // The window is still the size we made it, and the rest of this worker's tests would
            // measure that Bloom rather than the real one. Fail rather than warn.
            //
            // Only when the capture itself succeeded, though. If we are in this block because the
            // capture threw, that error is the one the test needs to read, and throwing here would
            // replace it. In that case the message is all we can give.
            const message = `captureElement could not clear the window size override, so the rest of this worker's tests would drive a Bloom of the wrong size: ${clearError}`;
            if (captured) {
                throw new Error(message);
            }
            console.error(message);
        }
    }
}

/**
 * Keep an override within what WebView2 can reasonably allocate, and never ask for zero.
 *
 * The caller checks the element against MAX_OVERRIDE_PIXELS before calling this, so the cap here
 * only ever applies to the viewport's own size. A silently capped element would be captured
 * truncated rather than reported.
 */
function clampOverride(pixels: number): number {
    return Math.max(1, Math.min(MAX_OVERRIDE_PIXELS, Math.ceil(pixels)));
}

/**
 * How far right and down the element reaches in the top document, counting its own scrollable
 * content. This is what the window has to be enlarged to before the element is fully laid out.
 */
async function elementExtent(
    locator: Locator,
    timeoutMs: number,
): Promise<{ right: number; bottom: number }> {
    const box = await requireBoundingBox(locator, timeoutMs);
    const scroll = await locator.evaluate((element) => ({
        width: element.scrollWidth,
        height: element.scrollHeight,
    }));
    return {
        right: box.x + Math.max(box.width, scroll.width),
        bottom: box.y + Math.max(box.height, scroll.height),
    };
}

/**
 * The element's box in the TOP document's coordinates, which is the space CDP's clip is in.
 * Playwright reports a box relative to the top document's window, so add that window's scroll.
 */
async function documentBox(
    locator: Locator,
    timeoutMs: number,
): Promise<{ x: number; y: number; width: number; height: number }> {
    const box = await requireBoundingBox(locator, timeoutMs);
    const scroll = await locator.page().evaluate(() => ({
        x: window.scrollX,
        y: window.scrollY,
    }));
    return {
        x: box.x + scroll.x,
        y: box.y + scroll.y,
        width: box.width,
        height: box.height,
    };
}

/** The element's on-screen box, or an error saying which element had none. */
async function requireBoundingBox(
    locator: Locator,
    timeoutMs: number,
): Promise<{ x: number; y: number; width: number; height: number }> {
    const box = await locator.boundingBox({ timeout: timeoutMs });
    if (!box)
        throw new Error(
            `${locator} is visible but has no bounding box, so there is nothing to capture. ` +
                `It may be inside a collapsed or zero-size container.`,
        );
    if (box.width < 1 || box.height < 1)
        throw new Error(
            `${locator} measures ${box.width}x${box.height}, which is too small to capture.`,
        );
    return box;
}

/**
 * Send one CDP request, and fail rather than hang when WebView2 never answers. A CDP call with no
 * reply is the failure mode this whole module exists to absorb, so it gets its own deadline here
 * instead of relying on Playwright's test timeout to notice.
 */
async function sendWithTimeout<TMethod extends CdpMethod>(
    session: CDPSession,
    method: TMethod,
    params: object,
): Promise<unknown> {
    let timer: NodeJS.Timeout | undefined;
    const expired = new Promise<never>((_resolve, reject) => {
        timer = setTimeout(
            () =>
                reject(
                    new Error(
                        `The CDP request ${method} got no reply within ${CDP_TIMEOUT_MS / 1000}s. ` +
                            `WebView2 stops answering rather than failing, so treat this as the ` +
                            `driver being stuck.`,
                    ),
                ),
            CDP_TIMEOUT_MS,
        );
    });
    try {
        // `as never` only because the two arguments are typed as one pair per method, and this
        // wrapper is deliberately method-agnostic; the method name itself is still checked.
        return await Promise.race([
            session.send(method, params as never),
            expired,
        ]);
    } finally {
        if (timer) clearTimeout(timer);
    }
}

/**
 * Read a PNG's pixel dimensions out of its header, so a caller can assert on the size of what it
 * captured without this package depending on an image library.
 */
export function readPngSize(png: Buffer): { width: number; height: number } {
    // 8-byte signature, then a 4-byte length and the "IHDR" tag, then width and height as 32-bit
    // big-endian integers.
    const signature = "89504e470d0a1a0a";
    if (png.length < 24 || png.subarray(0, 8).toString("hex") !== signature)
        throw new Error(
            `That is not a PNG: ${png.length} bytes starting ${png.subarray(0, 8).toString("hex")}.`,
        );
    return { width: png.readUInt32BE(16), height: png.readUInt32BE(20) };
}
