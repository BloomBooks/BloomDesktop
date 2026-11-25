// The handles that resize a canvas element on a Canvas page.
//
// A text bubble has handles on its left and right sides and none on its corners. A picture button
// can be stretched to any shape from each corner, while a picture or a video keeps its shape, so
// that what it shows is never squashed. A resize lands on a 10-pixel grid unless Ctrl is held, at
// any zoom. While a handle is held, a red line shows where an edge of the element lines up with
// another element's, and green bars cross every element of the same height (or width) as it.
//
// Sizes are read in the page's own pixels, the unit Bloom resizes and snaps in; drags are made in
// screen pixels, which at a zoom other than 100% are not the same.
//
// Automates "Resize Bubble Handles" (Test Case ID 303).

import { type Locator, type Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    activeCanvasElement,
    canvasElement,
    dragCanvasElementCorner,
    dragCanvasElementSide,
    dragPaletteItemOntoCanvas,
    openCanvasToolNavigationSection,
    selectCanvasElement,
    type Corner,
    type PaletteItem,
} from "../helpers/canvasElements";
import {
    getElementSize,
    getGuidesShown,
    getPageScale,
    moveHeldHandle,
    pressHandle,
    resizeHandle,
    type IGuideShown,
} from "../helpers/canvasResize";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { bottom, right } from "../helpers/geometry";
import { watchForScriptErrors } from "../helpers/scriptErrors";
import { chooseVideoFileFromCanvasMenu, SHORT_VIDEO } from "../helpers/videos";
import { setZoom } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "canvas-resize-handles",
        languages: ["en"],
        // Below Pro, the Canvas tool's palette sits behind a subscription notice.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

/** Bloom's grid, in the page's own pixels (gridSize in CanvasSnapProvider.ts). */
const kGrid = 10;

/**
 * Each corner, and a drag of it that would change the element's width and height by different
 * amounts. None ends below the element: a picture keeps its shape, so its corner does not follow
 * the pointer all the way, and a release over the toolbar under the element unselects it (BL-17170).
 */
const kCornerDrags: { corner: Corner; dx: number; dy: number }[] = [
    { corner: "se", dx: 50, dy: 15 },
    { corner: "nw", dx: 40, dy: 5 },
    { corner: "ne", dx: 50, dy: -15 },
    { corner: "sw", dx: -40, dy: 5 },
];

/**
 * Add a Canvas page, drop `item` on it at `at`, and select it. Returns the element's index on the
 * page (a Canvas page starts with its background picture's element, so it is not 0).
 *
 * The default place is up and to the left, because the drags of a test grow the element down and
 * to the right, and a handle pushed off the canvas can no longer be pressed.
 */
async function addCanvasPageWith(
    page: Page,
    item: PaletteItem,
    at: { xFraction: number; yFraction: number } = {
        xFraction: 0.25,
        yFraction: 0.3,
    },
): Promise<number> {
    const before = (await getContentPages(page)).map((p) => p.id);
    await addPage(page, "Canvas");
    const added = (await getContentPages(page)).find(
        (p) => !before.includes(p.id),
    );
    if (!added) throw new Error("Adding a Canvas page added no content page.");
    await goToPage(page, added.id);
    return dropOnCanvas(page, item, at);
}

/** Drop `item` on the canvas of the page being edited at `at`, select it, and return its index. */
async function dropOnCanvas(
    page: Page,
    item: PaletteItem,
    at: { xFraction: number; yFraction: number },
): Promise<number> {
    if (item === "navigation-image-button")
        await openCanvasToolNavigationSection(page);
    const index = await dragPaletteItemOntoCanvas(page, item, at);
    await ensureSelected(page, index);
    return index;
}

/**
 * Select the element at `index` unless it already is. A second click on a selected picture would
 * be a click on the picture itself, which is not what a test of its handles means to do.
 */
async function ensureSelected(page: Page, index: number): Promise<Locator> {
    const element = canvasElement(page, index);
    if ((await element.getAttribute("data-bloom-active")) !== "true")
        await selectCanvasElement(page, index);
    return element;
}

/**
 * Scroll the page so that `element` is in the middle of the window, as a person would before
 * taking hold of its handles. In a small window at a high zoom, a handle at the bottom of what is
 * showing sits under the caption the suite draws there, which takes the press.
 */
async function scrollToMiddle(element: Locator): Promise<void> {
    await element.evaluate((e) =>
        e.scrollIntoView({ block: "center", inline: "center" }),
    );
}

/** Width over height. */
function shapeOf(size: { width: number; height: number }): number {
    return size.width / size.height;
}

/**
 * Drag each corner of the selected element in turn, and check that its shape is kept (a picture
 * or a video) or changed (anything else) each time.
 */
async function expectCornersResize(
    page: Page,
    element: Locator,
    what: string,
    shape: "kept" | "free",
): Promise<void> {
    for (const drag of kCornerDrags) {
        await scrollToMiddle(element);
        const before = await getElementSize(element);
        await dragCanvasElementCorner(page, drag.corner, drag.dx, drag.dy);
        const after = await getElementSize(element);
        const where = `${what}, after a drag of the ${drag.corner} corner (from ${before.width}x${before.height} to ${after.width}x${after.height})`;
        expect(
            Math.abs(after.width - before.width),
            `${where}: the width should have changed`,
        ).toBeGreaterThan(5);
        const change = Math.abs(shapeOf(after) / shapeOf(before) - 1);
        if (shape === "kept")
            expect(
                change,
                `${where}: the shape should be the same as before`,
            ).toBeLessThan(0.01);
        else
            expect(
                change,
                `${where}: the shape should have changed with the drag`,
            ).toBeGreaterThan(0.02);
    }
}

/**
 * A screen distance that would leave a size that is now `from` (in the page's own pixels) a little
 * more than 30 pixels longer and 4 past a grid line, so that snapping to the grid has to move it.
 */
function distanceOffTheGrid(from: number, scale: number): number {
    const wanted = Math.ceil((from + 30) / kGrid) * kGrid + 4;
    return Math.round((wanted - from) * scale);
}

/**
 * What a size of `from` becomes when a handle is dragged `screenDistance` outwards at `scale`:
 * snapped to the grid, or not. Checks first that the unsnapped size is well off the grid, so that
 * the two answers cannot be the same.
 */
function expectedSize(
    from: number,
    screenDistance: number,
    scale: number,
    snapped: boolean,
): number {
    const free = from + screenDistance / scale;
    const offGrid = free % kGrid;
    expect(
        offGrid > 2 && offGrid < 8,
        `The test should drag to a size well off the grid, not ${free}.`,
    ).toBe(true);
    return snapped ? Math.round(free / kGrid) * kGrid : free;
}

/**
 * Drag the selected element's east side, or its south-east corner, outwards twice: once as is, when
 * the width (and for the corner the height) should land on the grid, and once with Ctrl held, when
 * it should land exactly where the pointer took it.
 */
async function expectResizeSnapsUnlessCtrl(
    page: Page,
    element: Locator,
    what: string,
    handle: "e" | "se",
): Promise<void> {
    const scale = await getPageScale(element);
    for (const ctrl of [false, true]) {
        await scrollToMiddle(element);
        const before = await getElementSize(element);
        const dx = distanceOffTheGrid(before.clientWidth, scale);
        const dy = distanceOffTheGrid(before.clientHeight, scale);
        const width = expectedSize(before.clientWidth, dx, scale, !ctrl);
        const height = expectedSize(before.clientHeight, dy, scale, !ctrl);
        if (ctrl) await page.keyboard.down("Control");
        try {
            if (handle === "e") await dragCanvasElementSide(page, "e", dx);
            else await dragCanvasElementCorner(page, "se", dx, dy);
        } finally {
            if (ctrl) await page.keyboard.up("Control");
        }
        const after = await getElementSize(element);
        const how = ctrl ? "with Ctrl held" : "without Ctrl";
        // Without Ctrl the size is a whole number of grid squares; with it, the screen distance
        // turned into page pixels, which the pointer's whole screen pixels make inexact at a zoom.
        const tolerance = ctrl ? 0.5 / scale + 0.01 : 0.01;
        expect(
            Math.abs(after.width - width),
            `${what}: dragging the ${handle} handle ${how} from a width of ${before.clientWidth} should give ${width}, and gave ${after.width}`,
        ).toBeLessThanOrEqual(tolerance);
        if (handle === "se")
            expect(
                Math.abs(after.height - height),
                `${what}: dragging the se handle ${how} from a height of ${before.clientHeight} should give ${height}, and gave ${after.height}`,
            ).toBeLessThanOrEqual(tolerance);
    }
}

/** The guides of one kind and direction that Bloom shows, described for a failure message. */
function guidesOf(
    guides: IGuideShown[],
    kind: IGuideShown["kind"],
    direction: IGuideShown["direction"],
): IGuideShown[] {
    return guides.filter((g) => g.kind === kind && g.direction === direction);
}

/** Whether a vertical green bar crosses the middle of `box`. */
function crossesMiddleOf(
    bar: IGuideShown,
    box: { x: number; width: number },
): boolean {
    return (
        Math.abs(bar.rect.x + bar.rect.width / 2 - (box.x + box.width / 2)) <= 3
    );
}

/** Where `element` is on screen. */
async function boxOf(element: Locator, what: string) {
    const box = await element.boundingBox();
    if (!box) throw new Error(`${what} has no on-screen box.`);
    return box;
}

test("Resize handles resize each kind of canvas element as it should, on the grid, with guides [Test Case ID 303]", async ({
    page,
    step,
}) => {
    test.setTimeout(600000);
    await makeBookFromTemplate(page, "Basic Book");
    const errors = watchForScriptErrors(page);
    try {
        await step(
            "A text bubble has side handles and no corner handles; they snap to the grid unless Ctrl is held",
            async () => {
                // In the middle, since it is widened to the left as well as to the right.
                const index = await addCanvasPageWith(page, "speech", {
                    xFraction: 0.5,
                    yFraction: 0.3,
                });
                const bubble = canvasElement(page, index);
                for (const corner of ["nw", "ne", "sw", "se"] as Corner[])
                    await expect(
                        resizeHandle(page, { corner }),
                        `A text bubble should have no ${corner} corner handle.`,
                    ).toBeHidden();
                for (const side of ["e", "w"] as const)
                    await expect(
                        resizeHandle(page, { side }),
                        `A text bubble should have a handle on its ${side} side.`,
                    ).toBeVisible();
                await expectResizeSnapsUnlessCtrl(
                    page,
                    bubble,
                    "A text bubble",
                    "e",
                );
                const before = await getElementSize(bubble);
                const { before: rectBefore, after: rectAfter } =
                    await dragCanvasElementSide(page, "w", -40);
                const after = await getElementSize(bubble);
                expect(
                    after.width,
                    "Dragging a text bubble's w side outwards should widen it.",
                ).toBeGreaterThan(before.width);
                expect(
                    Math.abs(right(rectAfter) - right(rectBefore)),
                    "Dragging a text bubble's w side should leave its right edge where it was.",
                ).toBeLessThanOrEqual(1.5);
            },
        );

        await step(
            "A picture button can be stretched to any shape from each corner, on the grid unless Ctrl is held",
            async () => {
                const index = await addCanvasPageWith(
                    page,
                    "navigation-image-button",
                );
                const button = canvasElement(page, index);
                await expectCornersResize(
                    page,
                    button,
                    "A picture button",
                    "free",
                );
                await expectResizeSnapsUnlessCtrl(
                    page,
                    button,
                    "A picture button",
                    "se",
                );
            },
        );

        await step("A picture keeps its shape from each corner", async () => {
            const index = await addCanvasPageWith(page, "image");
            const picture = canvasElement(page, index);
            await expectCornersResize(page, picture, "A picture", "kept");
        });

        await step("A video keeps its shape from each corner", async () => {
            const index = await addCanvasPageWith(page, "video");
            const video = canvasElement(page, index);
            await chooseVideoFileFromCanvasMenu(
                page,
                video.locator(".bloom-videoContainer"),
                SHORT_VIDEO,
            );
            await ensureSelected(page, index);
            await expectCornersResize(page, video, "A video", "kept");
        });

        await step(
            "At 150% zoom the handles still keep a picture's shape and snap to the grid",
            async () => {
                await setZoom(page, 150);
                // Each element on a page of its own, up and to the left, so that all of it is
                // on screen at this size.
                const pictureIndex = await addCanvasPageWith(page, "image");
                const picture = canvasElement(page, pictureIndex);
                expect(
                    await getPageScale(picture),
                    "At 150% zoom the page should be drawn half as large again.",
                ).toBeCloseTo(1.5, 1);
                await expectCornersResize(
                    page,
                    picture,
                    "A picture at 150%",
                    "kept",
                );
                const buttonIndex = await addCanvasPageWith(
                    page,
                    "navigation-image-button",
                );
                await expectResizeSnapsUnlessCtrl(
                    page,
                    canvasElement(page, buttonIndex),
                    "A picture button at 150%",
                    "se",
                );
                const bubbleIndex = await addCanvasPageWith(page, "speech");
                await expectResizeSnapsUnlessCtrl(
                    page,
                    canvasElement(page, bubbleIndex),
                    "A text bubble at 150%",
                    "e",
                );
                await setZoom(page, 100);
            },
        );

        await step(
            "While a handle is held, green bars mark elements of the same height and a red line marks lined-up edges",
            async () => {
                const otherIndex = await addCanvasPageWith(
                    page,
                    "navigation-image-button",
                    { xFraction: 0.7, yFraction: 0.6 },
                );
                const resizedIndex = await dropOnCanvas(
                    page,
                    "navigation-image-button",
                    { xFraction: 0.3, yFraction: 0.25 },
                );
                const other = canvasElement(page, otherIndex);
                const resized = await ensureSelected(page, resizedIndex);
                const otherSize = await getElementSize(other);
                expect(
                    (await getElementSize(resized)).height,
                    "Two new picture buttons should start the same height.",
                ).toBe(otherSize.height);
                const otherBox = await boxOf(other, "The other button");
                const startBox = await boxOf(
                    resized,
                    "The button being resized",
                );

                // Ctrl keeps the grid out of it, so the button's edges go exactly where the
                // pointer takes them.
                await page.keyboard.down("Control");
                let released = false;
                try {
                    const held = await pressHandle(page, { corner: "se" });
                    await moveHeldHandle(page, held, 40, 0);
                    await expect
                        .poll(
                            async () => {
                                const bars = guidesOf(
                                    await getGuidesShown(page),
                                    "same-size",
                                    "vertical",
                                );
                                const box = await boxOf(
                                    activeCanvasElement(page),
                                    "The button being resized",
                                );
                                return (
                                    `on the button being resized: ${bars.some((b) => crossesMiddleOf(b, box))}; ` +
                                    `on the other button: ${bars.some((b) => crossesMiddleOf(b, otherBox))}`
                                );
                            },
                            {
                                timeout: 10000,
                                message:
                                    "Widening a button while it is as high as the other should put a green bar across both.",
                            },
                        )
                        .toBe(
                            "on the button being resized: true; on the other button: true",
                        );

                    await moveHeldHandle(page, held, 40, 23);
                    await expect
                        .poll(
                            async () =>
                                guidesOf(
                                    await getGuidesShown(page),
                                    "same-size",
                                    "vertical",
                                ).some((b) => crossesMiddleOf(b, otherBox)),
                            {
                                timeout: 10000,
                                message:
                                    "Once the button is taller than the other, the other should lose its green bar.",
                            },
                        )
                        .toBe(false);
                    const lineAtOtherBottom = async () =>
                        guidesOf(
                            await getGuidesShown(page),
                            "alignment",
                            "horizontal",
                        ).some(
                            (line) =>
                                Math.abs(
                                    line.rect.y +
                                        line.rect.height / 2 -
                                        bottom(otherBox),
                                ) <= 2 &&
                                right(line.rect) >= right(otherBox) - 2,
                        );
                    expect(
                        await lineAtOtherBottom(),
                        "Before the bottoms line up there should be no red line along the other button's bottom.",
                    ).toBe(false);

                    await moveHeldHandle(
                        page,
                        held,
                        40,
                        Math.round(bottom(otherBox) - bottom(startBox)),
                    );
                    await expect
                        .poll(lineAtOtherBottom, {
                            timeout: 10000,
                            message:
                                "Lining the button's bottom up with the other's should draw a red line along both.",
                        })
                        .toBe(true);

                    await page.keyboard.up("Control");
                    await page.mouse.up();
                    released = true;
                } finally {
                    if (!released) {
                        await page.keyboard.up("Control");
                        await page.mouse.up();
                    }
                }
                await expect
                    .poll(async () => (await getGuidesShown(page)).length, {
                        timeout: 10000,
                        message:
                            "Letting go of the handle should take every guide away.",
                    })
                    .toBe(0);
            },
        );

        expect(errors.errors(), "Bloom raised a script error.").toEqual([]);
    } finally {
        errors.stop();
    }
});
