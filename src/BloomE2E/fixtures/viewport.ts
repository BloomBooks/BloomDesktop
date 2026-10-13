// Run the suite at a window size other than the one the machine gives Bloom.
//
// A run on a developer's monitor sees a big, maximized Bloom. The nightly CI runner gives Bloom a
// page area of about 1008x681, where the lower part of an A5 page is below the fold and a long menu
// scrolls. A spec can pass every time on the first and fail every night on the second. Set
// BLOOM_E2E_VIEWPORT=nightly to see what the nightly sees, or WIDTHxHEIGHT for any other size; the
// fixture then emulates that size with a CDP device-metrics override for the whole run.
//
// The override belongs to one CDP session, and closing the connection ends it, so the fixture
// applies it again after every reconnect. captureElement (helpers/screenshot.ts) sets an override
// of its own and clears it afterwards, so it puts this one back when it is done.

import type { CDPSession, Page } from "@playwright/test";

export const kViewportVariable = "BLOOM_E2E_VIEWPORT";

/** The page area Bloom gets on the nightly CI runner (measured 2026-10-06). */
const kNightlyViewport = { width: 1008, height: 681 };

/** The session that holds this worker's override, or undefined when the run did not ask for one. */
let viewportSession: CDPSession | undefined;

/**
 * The size BLOOM_E2E_VIEWPORT asks for, or undefined when it is unset. Throws on a value it cannot
 * read, so a typo fails the run instead of quietly testing at the machine's size.
 */
export function requestedViewport():
    | { width: number; height: number }
    | undefined {
    const value = process.env[kViewportVariable]?.trim().toLowerCase();
    if (!value) return undefined;
    if (value === "nightly") return kNightlyViewport;
    const match = /^(\d+)x(\d+)$/.exec(value);
    if (match) {
        const width = Number(match[1]);
        const height = Number(match[2]);
        if (width >= 400 && height >= 300) return { width, height };
    }
    throw new Error(
        `${kViewportVariable} must be "nightly" or WIDTHxHEIGHT, at least 400x300 (Bloom's ` +
            `smallest window). It is "${process.env[kViewportVariable]}".`,
    );
}

/**
 * Emulate the size BLOOM_E2E_VIEWPORT asks for on this page, through a session that stays open
 * until the fixture's connection closes. Does nothing when the variable is unset. Call it every
 * time the fixture connects to a Bloom page.
 */
export async function applyRequestedViewport(page: Page): Promise<void> {
    const size = requestedViewport();
    if (!size) return;
    viewportSession = await page.context().newCDPSession(page);
    await setOverride(viewportSession, size);
}

/**
 * Put the requested size back after something else changed or cleared the device-metrics
 * override. Does nothing when the run did not ask for a size.
 */
export async function restoreRequestedViewport(): Promise<void> {
    const size = requestedViewport();
    if (!size || !viewportSession) return;
    await setOverride(viewportSession, size);
}

const setOverride = (
    session: CDPSession,
    size: { width: number; height: number },
) =>
    session.send("Emulation.setDeviceMetricsOverride", {
        width: size.width,
        height: size.height,
        deviceScaleFactor: 1,
        mobile: false,
    });
