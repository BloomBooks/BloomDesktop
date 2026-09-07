// Dragging a picture while the edit view is zoomed.
//
// WHAT THIS IS ABOUT. The drag arithmetic works in two units at once. getBoundingClientRect and
// a pointer event's clientX/clientY are VIEWPORT pixels, while clientHeight, scrollTop and the
// custom properties the picture's position is stored in are LAYOUT pixels. Bloom draws the zoom
// by scaling a container around the page (SetupPageZoom / #page-scaling-container), so the two
// differ by exactly the zoom, and computeViewportPxPerLayoutPx is where the drag reconciles them
// -- measured once, at pointerdown.
//
// A wrong ratio does not fail quietly: the picture slides away from the cursor, by more the
// further it is dragged. The zoom is a control people reach for constantly, so this is the
// arithmetic most likely to be exercised at a value nobody tested.
//
// WHAT IS CHECKED. The pointer's own travel against the distance the picture actually moved, at
// each of three zooms. That comparison is the whole question: at 100% a correct implementation and
// one that ignores the ratio entirely give the same answer, so the smaller and larger zooms are
// what have anything to say. The tolerance is a line of text, because the picture's position is
// quantized by nothing but the block's own layout, and a float's arrival point can differ by a
// line's worth as the text rewraps around it.

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
} from "../helpers/inlineImages";
import { getZoom, setZoom } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "inline-images-zoom", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

const BLOCK = ".bloom-translationGroup";
const LANG = "en";

// Long enough that the block has lines all the way down it at every zoom, so a drag downward has
// somewhere to go.
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
 * Drag the picture down by `pointerTravelViewportPx` of real pointer movement, and report how far
 * the picture itself moved on the screen. Both are viewport pixels -- what the person's hand did
 * and what their eye saw -- so they are comparable whatever the zoom is.
 */
const dragDownAndMeasure = async (
    page: Page,
    pointerTravelViewportPx: number,
): Promise<{ pictureMovedViewportPx: number; blockHeightPx: number }> => {
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
    // The extremes Bloom itself allows, plus the middle, so the test asks about the values a
    // person can actually reach rather than ones invented here.
    const zoomsToTry = [zoomAtStart, minZoom, maxZoom];

    try {
        for (const zoom of zoomsToTry) {
            await setZoom(page, zoom);
            // Start each attempt from the same place: the full-width band at the top of the text.
            await scrollBlockToTop(page, BLOCK, LANG);
            await dragInlineImageToDock(page, BLOCK, LANG, imageId, "middle");

            // getBlockLineHeightPx reads the block's CSS line-height, which is a LAYOUT pixel
            // value and so is the same number at every zoom. On the screen a line is that
            // multiplied by the zoom, and the pointer travels on the screen, so everything below
            // is in the scaled units.
            const lineHeightLayoutPx = await getBlockLineHeightPx(
                page,
                BLOCK,
                LANG,
            );
            const lineHeightViewportPx = (lineHeightLayoutPx * zoom) / 100;
            // Three lines. Measured in lines rather than as a fraction of the block because the
            // block is three times as tall on screen at 300% as at 100%, and a quarter of THAT
            // puts the pointer below everything the person can see -- a drag to somewhere the
            // block has no room for is refused outright by the fit-or-revert rule, which says
            // nothing about the arithmetic this test is asking about.
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
        // Bloom saves the zoom as a user setting, so leave it as it was found.
        await setZoom(page, zoomAtStart);
    }
});
