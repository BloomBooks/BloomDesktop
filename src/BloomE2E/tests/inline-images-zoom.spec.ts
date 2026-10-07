// Tests dragging a picture while the edit view is zoomed.
//
// The drag code works with two kinds of pixels. getBoundingClientRect and a pointer event's
// clientX/clientY are in viewport pixels, while clientHeight, scrollTop and the custom properties
// that store the picture's position are in layout pixels. Bloom zooms by scaling a container
// around the page (SetupPageZoom, #page-scaling-container), so the two differ by the zoom factor.
// The drag converts between them with computeViewportPxPerLayoutPx, measured once at pointerdown.
//
// If that ratio is wrong, the picture moves away from the cursor, further the longer the drag.
// People change the zoom often, so the drag is likely to be used at zoom levels nobody tested.
//
// At each of three zoom levels, the test compares how far the pointer moved with how far the
// picture moved. At 100% the code would pass even if it ignored the ratio, so the smallest and
// largest zooms are the ones that can catch a mistake. The test allows a difference of up to one
// and a half lines of text, because the picture's position also depends on how the text wraps
// around it, and the float can land a line higher or lower as the text rewraps.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    addInlineImage,
    beginInlineImageDrag,
    changeInlineImagePicture,
    dragInlineImageToDock,
    endInlineImageDrag,
    getBlockLineHeightPx,
    getInlineImageRects,
    moveInlineImageDragTo,
    scrollBlockToTop,
    scrollInlineImageToTop,
} from "../helpers/inlineImages";
import { getZoom, setZoom } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "inline-images-zoom", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

const BLOCK = ".bloom-translationGroup";
const LANG = "en";

// Long enough to reach the bottom of the block at every zoom, so a downward drag has room to move.
const TEXT =
    "The kingfisher waits on the branch above the pool, still enough that the water forgets it " +
    "is there. It watches the shadows move under the surface. When it drops, it drops straight, " +
    "and the pool closes over the place where it went in. A moment later it is back on the " +
    "branch with a fish, and the water is still again. The children on the bank have learned " +
    "to wait as well, and they do not talk while the bird is fishing.";

const fixtureImage = (name: string) =>
    Path.resolve(
        Path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "fixtures",
        "images",
        name,
    );

let imageId: string;

/**
 * Drags the picture down by moving the pointer `pointerTravelViewportPx`, and returns how far
 * the picture moved on the screen. Both distances are in viewport pixels, so they can be compared
 * at any zoom.
 */
const dragDownAndMeasure = async (
    page: Page,
    pointerTravelViewportPx: number,
): Promise<{ pictureMovedViewportPx: number; blockHeightPx: number }> => {
    // Scroll so that both the place the drag starts and the place it ends are on screen, however
    // small the window and however large the zoom.
    await scrollInlineImageToTop(page, BLOCK, LANG, imageId);
    const before = await getInlineImageRects(page, BLOCK, LANG, imageId);
    await beginInlineImageDrag(page, BLOCK, LANG, imageId);
    await moveInlineImageDragTo(page, {
        x: before.picture.x + before.picture.width / 2,
        y:
            before.picture.y +
            before.picture.height / 2 +
            pointerTravelViewportPx,
    });
    await endInlineImageDrag(page);
    const after = await getInlineImageRects(page, BLOCK, LANG, imageId);
    return {
        pictureMovedViewportPx: after.picture.y - before.picture.y,
        blockHeightPx: after.block.height,
    };
};

test("a drag follows the pointer at every zoom [Test Case ID 815]", async ({
    page,
}) => {
    test.setTimeout(300000);
    await makeBookFromTemplate(page, "Basic Book");
    await typeInGroup(page, ".bookTitle", "en", "Inline Images Zoom");
    await addPage(page, "Just Text");
    const [contentPage] = await getContentPages(page);
    await goToPage(page, contentPage.id);
    await typeInGroup(page, BLOCK, LANG, TEXT);

    imageId = await addInlineImage(page, BLOCK, LANG);
    await changeInlineImagePicture(
        page,
        BLOCK,
        LANG,
        imageId,
        fixtureImage("bird.png"),
    );

    const { zoom: zoomAtStart, minZoom, maxZoom } = await getZoom(page);
    // The zoom Bloom starts at, and the smallest and largest zooms it allows, so the test uses
    // values a person can choose.
    const zoomsToTry = [zoomAtStart, minZoom, maxZoom];

    try {
        for (const zoom of zoomsToTry) {
            await setZoom(page, zoom);
            // Start each drag with the picture docked in the middle at the top of the text.
            await scrollBlockToTop(page, BLOCK, LANG);
            await dragInlineImageToDock(page, BLOCK, LANG, imageId, "middle");

            // getBlockLineHeightPx reads the block's CSS line-height, which is in layout pixels
            // and so is the same at every zoom. On the screen a line is that times the zoom, and
            // the pointer moves on the screen, so the distances below are in viewport pixels.
            const lineHeightLayoutPx = await getBlockLineHeightPx(
                page,
                BLOCK,
                LANG,
            );
            const lineHeightViewportPx = (lineHeightLayoutPx * zoom) / 100;
            // Drag three lines down. The distance is in lines instead of a fraction of the block
            // because the block is three times as tall on screen at 300% as at 100%, and a
            // quarter of that would put the pointer below the visible area. The drag puts back a
            // move that would leave the block without room for its text
            // (shouldRevertInlineImageMove), and that would tell us nothing about the zoom.
            const pointerTravelViewportPx = Math.round(
                3 * lineHeightViewportPx,
            );
            const { pictureMovedViewportPx } = await dragDownAndMeasure(
                page,
                pointerTravelViewportPx,
            );

            expect(
                Math.abs(pictureMovedViewportPx - pointerTravelViewportPx),
                `At ${zoom}% zoom the pointer travelled ${pointerTravelViewportPx}px down the ` +
                    `screen and the picture moved ${Math.round(pictureMovedViewportPx)}px, which ` +
                    `is ${Math.round(Math.abs(pictureMovedViewportPx - pointerTravelViewportPx) / lineHeightViewportPx)} ` +
                    `lines away from the pointer. The drag converts between viewport pixels ` +
                    `(clientY, getBoundingClientRect) and layout pixels (the offset it stores) ` +
                    `using one ratio measured at pointerdown, and Bloom draws the zoom by ` +
                    `scaling a container around the page, so that ratio IS the zoom.`,
            ).toBeLessThanOrEqual(lineHeightViewportPx * 1.5);
        }
    } finally {
        // Bloom saves the zoom as a user setting, so put it back the way it was.
        await setZoom(page, zoomAtStart);
    }
});
