// The Motion tool: its two rectangles on the page's picture, the gestures that move and resize
// them, and what its Preview and the BloomPUB preview show while they pan and zoom.
//
// A rectangle and a motion are both described here as a region of the picture's canvas, in
// fractions of the canvas's width and height, which is also how Bloom saves them
// (data-initialrect and data-finalrect: "left top width height"). So a test can compare what it
// sees on the screen with what Bloom saved, and with what a preview showed, whatever the zoom.

import { expect, type Frame, type Page } from "@playwright/test";
import { editablePageFrame } from "./bookMaking";

/** A region of the picture's canvas, in fractions of the canvas's width and height. */
export interface IMotionRegion {
    left: number;
    top: number;
    width: number;
    height: number;
}

/** Rectangle "1", where the motion starts, and rectangle "2", where it ends. */
export type MotionRectangle = "start" | "end";

const kRectangleIds: Record<MotionRectangle, string> = {
    start: "animationStart",
    end: "animationEnd",
};

const kSavedAttributes: Record<MotionRectangle, string> = {
    start: "data-initialrect",
    end: "data-finalrect",
};

/** How far apart two fractions of the canvas may be and still count as the same edge. */
export const kRegionTolerance = 0.02;

/** The two rectangles as the page shows them, and the two Bloom has saved on the canvas. */
export interface IMotionRectangles {
    shown: Record<MotionRectangle, IMotionRegion>;
    saved: Record<MotionRectangle, IMotionRegion | undefined>;
    /** The canvas's width divided by its height. */
    canvasAspectRatio: number;
}

/**
 * Read where the Motion tool's two rectangles are on the picture of the page being edited, and
 * what Bloom has saved for them. Throws when the tool is not showing its rectangles.
 */
export async function getMotionRectangles(
    page: Page,
): Promise<IMotionRectangles> {
    return editablePageFrame(page).evaluate(
        ({ ids, attributes }) => {
            const canvas = document.querySelector(
                ".bloom-canvas",
            ) as HTMLElement;
            const canvasBox = canvas.getBoundingClientRect();
            const shown = (id: string) => {
                const rect = document.getElementById(id);
                if (!rect)
                    throw new Error(
                        `The page has no #${id}, so the Motion tool is not showing its rectangles.`,
                    );
                const box = rect.getBoundingClientRect();
                return {
                    left: (box.left - canvasBox.left) / canvasBox.width,
                    top: (box.top - canvasBox.top) / canvasBox.height,
                    width: box.width / canvasBox.width,
                    height: box.height / canvasBox.height,
                };
            };
            const saved = (attribute: string) => {
                const value = canvas.getAttribute(attribute);
                if (!value) return undefined;
                const [left, top, width, height] = value
                    .split(" ")
                    .map(parseFloat);
                return { left, top, width, height };
            };
            return {
                shown: { start: shown(ids.start), end: shown(ids.end) },
                saved: {
                    start: saved(attributes.start),
                    end: saved(attributes.end),
                },
                canvasAspectRatio: canvasBox.width / canvasBox.height,
            };
        },
        { ids: kRectangleIds, attributes: kSavedAttributes },
    );
}

/** Wait until the Motion tool shows both of its rectangles on the page's picture. */
export async function waitForMotionRectangles(page: Page): Promise<void> {
    const frame = editablePageFrame(page);
    for (const id of Object.values(kRectangleIds))
        await frame
            .locator(`#${id}`)
            .waitFor({ state: "visible", timeout: 30000 });
}

/** One gesture on a rectangle: drag its "1" or "2" label to move it, or its corner to resize it. */
export type MotionGesture = "move" | "resize";

/**
 * Move a rectangle by dragging its numbered label, or resize it by dragging its bottom right
 * corner, by `dx`, `dy` pixels of the screen, the way a person does. Waits until Bloom has saved
 * the rectangle's new place (the tool saves when the drag ends), and returns the rectangles before
 * and after.
 */
export async function dragMotionRectangle(
    page: Page,
    rectangle: MotionRectangle,
    gesture: MotionGesture,
    dx: number,
    dy: number,
): Promise<{ before: IMotionRectangles; after: IMotionRectangles }> {
    const frame = editablePageFrame(page);
    const id = kRectangleIds[rectangle];
    // Both rectangles have a corner handle with the id "resizeHandle", so it is found by its class
    // inside the rectangle.
    const handle = frame.locator(
        gesture === "move"
            ? `#${id} > .bloom-dragHandleAnimation`
            : `#${id} > .ui-resizable-se`,
    );
    await handle.waitFor({ state: "visible", timeout: 30000 });
    await handle.scrollIntoViewIfNeeded({ timeout: 30000 });
    const before = await getMotionRectangles(page);
    const box = await handle.boundingBox({ timeout: 30000 });
    if (!box)
        throw new Error(
            `The ${gesture} handle of the ${rectangle} rectangle has no box on the screen.`,
        );
    const x = box.x + box.width / 2;
    const y = box.y + box.height / 2;
    await page.mouse.move(x, y);
    await page.mouse.down();
    await page.mouse.move(x + dx, y + dy, { steps: 12 });
    await page.mouse.up();
    await expect
        .poll(
            async () => {
                const saved = (await getMotionRectangles(page)).saved[
                    rectangle
                ];
                const old = before.saved[rectangle];
                if (!saved || !old) return 0;
                return (
                    Math.abs(saved.left - old.left) +
                    Math.abs(saved.top - old.top) +
                    Math.abs(saved.width - old.width) +
                    Math.abs(saved.height - old.height)
                );
            },
            {
                timeout: 30000,
                message:
                    `Dragging the ${rectangle} rectangle's ${gesture} handle by ${dx},${dy} ` +
                    `did not change what Bloom saved for it.`,
            },
        )
        .toBeGreaterThan(kRegionTolerance);
    return { before, after: await getMotionRectangles(page) };
}

/** What a preview showed of the picture while it played: the region in view, frame by frame. */
export interface IMotionRecording {
    /** The region of the canvas in view in each frame that the motion moved, in order. */
    regions: IMotionRegion[];
}

// The in-page recorder. It runs in the document that plays the motion, and on every animation
// frame reads which part of the animated copy of the canvas shows through the window in front of
// it. That is what the reader sees, however the player moves and scales the copy to get there.
// It keeps only frames in which the copy has a transform, which is when the motion has started,
// and stops once the window is gone or the copy has stood still for a while after moving.
interface IRecorderWindow {
    __motionRecording?: {
        regions: IMotionRegion[];
        done: boolean;
    };
}

/**
 * Start recording what the motion in `frame` shows, before whatever starts it. `windowSelector`
 * finds the element that frames the motion (the player's overflow-hidden wrapper); the animated
 * copy of the canvas is its first child. Call waitForMotionRecording afterwards for the result.
 */
export async function startMotionRecording(
    frame: Frame,
    windowSelector: string,
): Promise<void> {
    await frame.evaluate((selector) => {
        const recording = { regions: [] as IMotionRegion[], done: false };
        (window as unknown as IRecorderWindow).__motionRecording = recording;
        let seen = false;
        let lastTransform = "";
        let stillFrames = 0;
        // Frames with no change after the motion has moved, before the recording ends. The motion
        // eases out, so its last frames change little but still change; a still stretch this long
        // only comes once it has stopped.
        const kStillFramesToEnd = 20;
        const sample = () => {
            const view = document.querySelector(selector) as HTMLElement | null;
            const copy = view?.firstElementChild as HTMLElement | null;
            if (!view || !copy) {
                if (seen) {
                    recording.done = true;
                    return;
                }
                requestAnimationFrame(sample);
                return;
            }
            const transform = copy.style.transform;
            if (transform) {
                seen = true;
                if (transform === lastTransform) {
                    stillFrames++;
                    if (stillFrames >= kStillFramesToEnd) {
                        recording.done = true;
                        return;
                    }
                } else {
                    stillFrames = 0;
                    lastTransform = transform;
                    const viewBox = view.getBoundingClientRect();
                    const copyBox = copy.getBoundingClientRect();
                    recording.regions.push({
                        left: (viewBox.left - copyBox.left) / copyBox.width,
                        top: (viewBox.top - copyBox.top) / copyBox.height,
                        width: viewBox.width / copyBox.width,
                        height: viewBox.height / copyBox.height,
                    });
                }
            }
            requestAnimationFrame(sample);
        };
        requestAnimationFrame(sample);
    }, windowSelector);
}

/** Wait until the recording startMotionRecording began has ended, and return it. */
export async function waitForMotionRecording(
    frame: Frame,
    what: string,
): Promise<IMotionRecording> {
    await expect
        .poll(
            async () =>
                frame.evaluate(
                    () =>
                        !!(window as unknown as IRecorderWindow)
                            .__motionRecording?.done,
                ),
            {
                timeout: 60000,
                message: `${what} never played a motion that came to rest.`,
            },
        )
        .toBe(true);
    return frame.evaluate(() => ({
        regions: (window as unknown as IRecorderWindow).__motionRecording!
            .regions,
    }));
}

/** Describe a region for a failure message. */
export function describeRegion(region: IMotionRegion): string {
    const f = (n: number) => n.toFixed(3);
    return `left ${f(region.left)}, top ${f(region.top)}, width ${f(region.width)}, height ${f(region.height)}`;
}

/** Expect two regions to be the same, edge for edge, within kRegionTolerance (or `tolerance`). */
export function expectSameRegion(
    actual: IMotionRegion,
    expected: IMotionRegion,
    what: string,
    tolerance = kRegionTolerance,
): void {
    const off = Math.max(
        Math.abs(actual.left - expected.left),
        Math.abs(actual.top - expected.top),
        Math.abs(actual.width - expected.width),
        Math.abs(actual.height - expected.height),
    );
    expect(
        off,
        `${what}: expected ${describeRegion(expected)}, got ${describeRegion(actual)}.`,
    ).toBeLessThanOrEqual(tolerance);
}

/**
 * Expect a recorded motion to have started on `from` and ended on `to`, passing through regions
 * between them on the way: what the reader sees pans and zooms from rectangle 1 to rectangle 2.
 */
export function expectMotionFromTo(
    recording: IMotionRecording,
    from: IMotionRegion,
    to: IMotionRegion,
    what: string,
): void {
    const regions = recording.regions;
    // A four-second motion is hundreds of frames; a handful would mean it jumped rather than moved.
    expect(
        regions.length,
        `${what} showed only ${regions.length} different frames, so it did not move smoothly.`,
    ).toBeGreaterThan(10);
    // The first frame we see can come a frame after the motion started, when it has moved a
    // little already; it eases in, so very little.
    expectSameRegion(regions[0], from, `${what}, its first frame`, 0.03);
    expectSameRegion(
        regions[regions.length - 1],
        to,
        `${what}, its last frame`,
        0.03,
    );
    // Every frame in between lies between the two, edge for edge: it goes one way, not back and
    // forth.
    const edges: (keyof IMotionRegion)[] = ["left", "top", "width", "height"];
    for (const [index, region] of regions.entries())
        for (const edge of edges) {
            const low = Math.min(from[edge], to[edge]) - 0.03;
            const high = Math.max(from[edge], to[edge]) + 0.03;
            expect(
                region[edge] >= low && region[edge] <= high,
                `${what}, frame ${index}: its ${edge} ${region[edge].toFixed(3)} is not between ` +
                    `rectangle 1's and rectangle 2's (${from[edge].toFixed(3)}, ${to[edge].toFixed(3)}).`,
            ).toBe(true);
        }
}
