// Speech bubbles and their tails are drawn in front of pictures, whatever the Layer order of the
// canvas elements says, while a video is drawn in front of bubbles and tails. Notion test case
// "Bubble Tail Layer Order" (Test Case ID 329).
//
// ComicalJS draws every bubble and tail on one drawing surface laid over the canvas, which Bloom
// stacks above picture elements and below video boxes; the Layer commands reorder the canvas
// elements themselves. So the tests read what the browser paints where a picture or a video
// overlaps the bubble: at a point of the tail and a point of the bubble's body where ComicalJS
// painted solidly, the screen shows the drawing's colour over a picture and the video's colour
// under a video. A point of the picture or video that the drawing leaves clear shows that
// element's colour, which proves it is there, and is what the video's colour is read from.
//
// The tests are serial because each one starts from the page the one before it left behind.

import { type Locator, type Page } from "@playwright/test";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editablePageFrame,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    type IBookPage,
} from "../helpers/bookMaking";
import {
    canvasElement,
    clearCanvasElementSelection,
    deleteCanvasElement,
    dragPaletteItemOntoCanvas,
    getCanvasRect,
    getRect,
    selectCanvasElement,
} from "../helpers/canvasElements";
import {
    chooseLayerCommand,
    getStackingOrder,
    selectStackedCanvasElement,
} from "../helpers/canvasLayers";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { type IRect } from "../helpers/geometry";
import { chooseImageFile } from "../helpers/images";
import {
    describeRgba,
    isRgbaClose,
    readImageFileColors,
    readPaintedColorAt,
    type IRgba,
} from "../helpers/paintedPixels";
import {
    drawingPointOnScreen,
    findOpaqueDrawingPointNear,
    isDrawingClearAt,
    readTail,
    type IOpaqueDrawingPoint,
    type IScreenPoint,
} from "../helpers/speechBubbleTail";
import {
    chooseVideoFileFromCanvasMenu,
    SHORT_VIDEO,
    videoBoxes,
} from "../helpers/videos";

test.use({
    collectionSpec: {
        name: "canvas-bubble-tail-layer-order",
        languages: ["en"],
        // The Canvas tool offers video elements only from the Pro tier up.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

test.describe.configure({ mode: "serial" });

/** A picture of one solid colour, so every point of it shows that colour. */
const kBlueSquare = Path.join(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
    "blue-square.png",
);

/**
 * How far a painted pixel may be from the colour expected there. The page is composited and
 * screenshotted without scaling, so solid colours come through nearly exactly.
 */
const kTolerance = 10;

// Set by the first test. The bubble is named as canvasLayers.ts names elements, by where it sits.
let bubble: string;
let canvasPage: IBookPage;
let otherPage: IBookPage;
/** Where the element over the bubble is dropped: across the bubble's right end and its tail. */
let dropAt: { xFraction: number; yFraction: number };
/** A point of the tail and one of the bubble's body where ComicalJS paints solidly. */
let tailPoint: IOpaqueDrawingPoint;
let bodyPoint: IOpaqueDrawingPoint;
let pictureColor: IRgba;

/** True when `point` lies inside `rect`, at least `margin` pixels in from every edge. */
function isInside(point: IScreenPoint, rect: IRect, margin = 3): boolean {
    return (
        point.x >= rect.x + margin &&
        point.x <= rect.x + rect.width - margin &&
        point.y >= rect.y + margin &&
        point.y <= rect.y + rect.height - margin
    );
}

/**
 * Leave the canvas page and come back to it, then clear the selection, so that the page shows what
 * Bloom saved with nothing selected: no selection frame, toolbar or tail handles drawn over the
 * points read. A click off the canvas alone does not do: after an element is added or changed,
 * Bloom selects it again in a timer (SetupElements in bloomEditing.ts), which can come after the
 * click. A page that has just loaded has that timer too, and it ends by selecting the element that
 * was selected when the page was left, so once that element shows as selected the timer is done,
 * and the click that clears the selection stays cleared. Something is always selected when this
 * leaves the page, since every step before it adds, fills or moves an element.
 */
async function reopenCanvasPage(page: Page): Promise<void> {
    await goToPage(page, otherPage.id);
    await goToPage(page, canvasPage.id);
    const selected = editablePageFrame(page).locator(
        '.bloom-canvas-element[data-bloom-active="true"]',
    );
    await expect(
        selected,
        "Coming back to the canvas page, Bloom never selected the element that was selected when " +
            "it was left.",
    ).toHaveCount(1, { timeout: 30000 });
    await clearCanvasElementSelection(page);
}

/**
 * Check that the element drawn in `rect` (`what` names it) covers both sample points, so that what
 * shows there is either it or the bubble drawn in front of it.
 */
function expectCoversSamplePoints(rect: IRect, what: string): void {
    expect(
        isInside(tailPoint, rect),
        `The ${what} does not cover the tail's sample point.`,
    ).toBe(true);
    expect(
        isInside(bodyPoint, rect),
        `The ${what} does not cover the bubble's sample point.`,
    ).toBe(true);
}

/**
 * Check that the screen shows `expected` at `point`; `what` says what that means, for the message.
 */
async function expectPaintedAt(
    page: Page,
    point: IScreenPoint,
    expected: IRgba,
    what: string,
): Promise<void> {
    const painted = await readPaintedColorAt(page, point);
    expect(
        isRgbaClose(painted, expected, kTolerance),
        `${what}: the screen shows ${describeRgba(painted)}, not ${describeRgba(expected)}.`,
    ).toBe(true);
}

/**
 * Check that the bubble's tail and body are drawn over the picture element `picture`: both sample
 * points show the drawing's own colour, and a point of the picture the drawing leaves clear shows
 * the picture's.
 */
async function expectBubbleInFrontOfPicture(
    page: Page,
    picture: Locator,
    order: string,
): Promise<void> {
    await reopenCanvasPage(page);
    const pictureRect = await getRect(picture, "the picture element");
    expectCoversSamplePoints(pictureRect, "picture");
    // The reloaded picture shows once the browser has decoded it.
    await picture
        .locator("img")
        .evaluate((img) => (img as HTMLImageElement).decode());
    const clearPoint = {
        x: pictureRect.x + 10,
        y: pictureRect.y + pictureRect.height - 10,
    };
    expect(
        await isDrawingClearAt(page, clearPoint),
        "The point chosen to show the picture has part of the bubble drawn on it.",
    ).toBe(true);
    await expectPaintedAt(
        page,
        clearPoint,
        pictureColor,
        `Where nothing of the bubble is drawn, the picture should show (${order})`,
    );
    await expectPaintedAt(
        page,
        tailPoint,
        tailPoint.color,
        `The tail should be drawn over the picture (${order})`,
    );
    await expectPaintedAt(
        page,
        bodyPoint,
        bodyPoint.color,
        `The bubble should be drawn over the picture (${order})`,
    );
}

/**
 * Check that the video in `videoBox` is drawn over the bubble's tail and body: both sample points
 * show the video's colour, as a point of the video the drawing leaves clear does. The suite's
 * short video is one flat colour, which the clear points confirm.
 */
async function expectVideoInFrontOfBubble(
    page: Page,
    videoBox: Locator,
    order: string,
): Promise<void> {
    await reopenCanvasPage(page);
    const videoRect = await getRect(videoBox, "the video box");
    expectCoversSamplePoints(videoRect, "video");
    const clearPoints = [
        { x: videoRect.x + 10, y: videoRect.y + videoRect.height - 10 },
        {
            x: videoRect.x + videoRect.width - 10,
            y: videoRect.y + videoRect.height - 10,
        },
    ];
    for (const point of clearPoints)
        expect(
            await isDrawingClearAt(page, point),
            "A point chosen to show the video has part of the bubble drawn on it.",
        ).toBe(true);
    // The reloaded video shows its first frame once it has decoded it. Until then its box shows
    // what is behind it, so wait until both clear points show one colour that is neither of the
    // drawing's: the video's flat colour, and one the check can tell from the drawing.
    let videoColor: IRgba = { r: 0, g: 0, b: 0, a: 0 };
    await expect
        .poll(
            async () => {
                const [first, second] = [
                    await readPaintedColorAt(page, clearPoints[0]),
                    await readPaintedColorAt(page, clearPoints[1]),
                ];
                videoColor = first;
                return (
                    isRgbaClose(first, second, kTolerance) &&
                    [tailPoint, bodyPoint].every(
                        (p) => !isRgbaClose(first, p.color, kTolerance * 4),
                    )
                );
            },
            {
                timeout: 30000,
                message:
                    "The video never showed one flat colour, different from the bubble's, at the " +
                    `points the bubble leaves clear (${order}).`,
            },
        )
        .toBe(true);
    for (const [point, what] of [
        [tailPoint, "tail"],
        [bodyPoint, "bubble"],
    ] as const) {
        await expectPaintedAt(
            page,
            point,
            videoColor,
            `The video should be drawn over the ${what} (${order})`,
        );
    }
}

test.describe("bubble and tail layer order [Test Case ID 329]", () => {
    test("builds a canvas page with a speech bubble and finds solid points of its tail and body", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Canvas");
        // A page to leave the canvas page for; see reopenCanvasPage.
        [canvasPage] = await getContentPages(page);
        otherPage = (await getPages(page)).find((p) => !p.isContentPage)!;
        const index = await dragPaletteItemOntoCanvas(page, "speech", {
            xFraction: 0.3,
            yFraction: 0.25,
        });
        [bubble] = await getStackingOrder(page);
        await reopenCanvasPage(page);

        const element = canvasElement(page, index);
        const tail = await readTail(element);
        tailPoint = await findOpaqueDrawingPointNear(
            page,
            await drawingPointOnScreen(page, tail.midpointX, tail.midpointY),
        );
        const bubbleRect = await getRect(element, "the speech bubble");
        bodyPoint = await findOpaqueDrawingPointNear(page, {
            x: bubbleRect.x + bubbleRect.width - 15,
            y: bubbleRect.y + bubbleRect.height / 2 + 4,
        });

        // Drop the next element so that it covers the bubble's right end and its tail.
        const canvasRect = await getCanvasRect(page);
        dropAt = {
            xFraction:
                (bubbleRect.x + bubbleRect.width - 20 - canvasRect.x) /
                canvasRect.width,
            yFraction:
                (bubbleRect.y + bubbleRect.height - canvasRect.y) /
                canvasRect.height,
        };

        // Sanity: with nothing over the bubble, the screen shows the drawing at both points.
        await expectPaintedAt(
            page,
            tailPoint,
            tailPoint.color,
            "With nothing on the page but the bubble, its tail should show",
        );
        await expectPaintedAt(
            page,
            bodyPoint,
            bodyPoint.color,
            "With nothing on the page but the bubble, its body should show",
        );

        [pictureColor] = await readImageFileColors(page, kBlueSquare, [
            { name: "middle", x: 0.5, y: 0.5 },
        ]);
    });

    test("the bubble and its tail are drawn over a picture added after it", async ({
        page,
    }) => {
        const index = await dragPaletteItemOntoCanvas(page, "image", dropAt);
        const picture = canvasElement(page, index);
        await chooseImageFile(page, kBlueSquare, picture);
        // The picture came last, so it is in front of the bubble in the Layer order.
        expect((await getStackingOrder(page))[0]).toBe(bubble);
        // Sanity: the picture's colour is not the drawing's, so the check can tell them apart.
        for (const point of [tailPoint, bodyPoint])
            expect(isRgbaClose(pictureColor, point.color, kTolerance)).toBe(
                false,
            );

        await expectBubbleInFrontOfPicture(
            page,
            picture,
            "picture in front in the Layer order",
        );
    });

    test("the bubble stays over the picture when the Layer order puts the picture behind it", async ({
        page,
    }) => {
        await selectStackedCanvasElement(page, bubble);
        await chooseLayerCommand(page, "Bring to Front");
        expect((await getStackingOrder(page)).at(-1)).toBe(bubble);

        const picture = canvasElement(page, 1);
        expect(await picture.locator("img").getAttribute("src")).toContain(
            "blue-square",
        );
        await expectBubbleInFrontOfPicture(
            page,
            picture,
            "bubble in front in the Layer order",
        );
    });

    test("a video over the bubble is drawn in front of the bubble and its tail", async ({
        page,
    }) => {
        test.setTimeout(300000);
        // Take the picture away, so only the video is over the bubble.
        await selectCanvasElement(page, 1);
        await deleteCanvasElement(page);

        const index = await dragPaletteItemOntoCanvas(page, "video", dropAt);
        await selectCanvasElement(page, index);
        // Found on its own rather than by the element's index, which the Layer command below changes.
        const videoBox = videoBoxes(page);
        await expect(
            videoBox,
            "The page should have one video box.",
        ).toHaveCount(1);
        await chooseVideoFileFromCanvasMenu(page, videoBox, SHORT_VIDEO);
        // The video came last, so it is in front of the bubble in the Layer order.
        expect((await getStackingOrder(page))[0]).toBe(bubble);

        await expectVideoInFrontOfBubble(
            page,
            videoBox,
            "video in front in the Layer order",
        );

        await selectStackedCanvasElement(page, bubble);
        await chooseLayerCommand(page, "Bring to Front");
        expect((await getStackingOrder(page)).at(-1)).toBe(bubble);
        await expectVideoInFrontOfBubble(
            page,
            videoBox,
            "bubble in front in the Layer order",
        );
    });
});
