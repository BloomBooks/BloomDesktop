// Tests what happens to a picture's distance down its block when the book is shown at another
// page size.
//
// Three custom properties hold an inline image's size and position. `--inline-image-width` is a
// percentage of the block and `--inline-image-aspect-ratio` is a ratio, but
// `--inline-image-offset` is a distance in layout pixels. Changing the page size only changes
// classes on the page div; it does not rewrite any HTML or inline style. If the offset stayed the
// same, the picture would stay the same distance below the start of the text however short the
// block became, and the text after the picture would be pushed off the end of the block.
//
// A drag never leaves the block in that state: it puts back any move that would make the block
// overflow (shouldRevertInlineImageMove). adjustInlineImageOffsetsIfBlockSizeChanged recalculates
// the offset when the block changes size, much as adjustCanvasElementChildrenIfSizeChanged and
// `data-imgsizebasedon` do for canvas elements. This test checks that after a page size change
// the block does not overflow and the picture is still in view.

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
    changeInlineImagePicture,
    dragInlineImageDown,
    dragInlineImageToDock,
    getBlockLineHeightPx,
    getBlockScrollOverflowPx,
    getInlineImage,
    getInlineImageRects,
    getRoomBelowInlineImagePx,
    blockIsMarkedOverflowing,
    scrollBlockToTop,
} from "../helpers/inlineImages";
import {
    getPageSize,
    getPageSizeChoices,
    setPageSize,
} from "../helpers/pageSize";

test.use({
    collectionSpec: { name: "inline-images-page-size", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

const BLOCK = ".bloom-translationGroup";
const LANG = "en";

// Enough text to reach the bottom of the block at A5, so the picture can be put far down, and
// enough that a smaller page has to wrap it into more lines.
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

/** Measures everything about where the picture is, so the test can compare page sizes. */
const wherePictureSits = async (page: Page) => {
    const image = await getInlineImage(page, BLOCK, LANG, imageId);
    const rects = await getInlineImageRects(page, BLOCK, LANG, imageId);
    const lineHeightPx = await getBlockLineHeightPx(page, BLOCK, LANG);
    return {
        pageSize: await getPageSize(page),
        dock: image.dock,
        offsetPx: image.offsetPx,
        widthPercent: image.widthPercent,
        roomBelowPx: await getRoomBelowInlineImagePx(
            page,
            BLOCK,
            LANG,
            imageId,
        ),
        overflowPx: await getBlockScrollOverflowPx(page, BLOCK, LANG),
        markedOverflowing: await blockIsMarkedOverflowing(page, BLOCK, LANG),
        lineHeightPx,
        // How far below the top of the block's visible area the picture is drawn. This is
        // what the person sees.
        pictureTopInBlockPx: rects.picture.y - rects.block.y,
        blockHeightPx: rects.block.height,
        blockWidthPx: rects.block.width,
    };
};

test("a picture stays inside its block when the book is drawn at another size [Test Case ID 815]", async ({
    page,
}) => {
    test.setTimeout(300000);
    await makeBookFromTemplate(page, "Basic Book");
    await typeInGroup(page, ".bookTitle", "en", "Inline Images Page Size");
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

    // Dock the picture in the middle, as a full-width band. Its position down the block is a
    // distance in pixels, so a page size change can leave it too far down.
    await scrollBlockToTop(page, BLOCK, LANG);
    await dragInlineImageToDock(page, BLOCK, LANG, imageId, "middle");
    // Drag it as far down the block as the drag allows. A person filling an A5 page may put the
    // picture near the end of the text, which is the largest offset the block allows, and an
    // offset measured on the tallest page is the one a shorter page cannot hold. A smaller offset
    // (145px) fits at every size: line height does not change with page size, so the same
    // offset leaves the same number of lines above the picture. Only a large offset uses up the
    // room below the picture.
    const roomAtStartPx = await getRoomBelowInlineImagePx(
        page,
        BLOCK,
        LANG,
        imageId,
    );
    await dragInlineImageDown(
        page,
        BLOCK,
        LANG,
        imageId,
        Math.max(
            40,
            roomAtStartPx - (await getBlockLineHeightPx(page, BLOCK, LANG)),
        ),
    );

    const atA5 = await wherePictureSits(page);
    expect(
        atA5.dock,
        "The setup did not leave the picture in the full-width band, so this test is measuring the wrong thing.",
    ).toBe("middle");
    expect(
        atA5.offsetPx,
        "The setup did not move the picture down the block, so there is no offset for a size change to strand.",
    ).toBeGreaterThan(40);
    // Check the starting state. The drag's clamp keeps the picture within the block's content,
    // and the size change must not undo that.
    expect(
        atA5.roomBelowPx,
        "The picture was already past the end of the block's content before any size change, so the drag itself is at fault, not the size change.",
    ).toBeGreaterThan(-2);
    // The block does not overflow before the size change; the checks below test that it still
    // does not afterward. The drag ensures this, because it puts back any move that would make
    // the block overflow.
    expect(
        atA5.overflowPx,
        "The block already held more text than it could show before any size change, so there is nothing for the size change to be blamed for.",
    ).toBeLessThanOrEqual(0);

    // THE ACTION UNDER TEST: change to each page size this book offers that changes the block's
    // shape.
    const sizes = (await getPageSizeChoices(page)).choices.map((c) => c.id);
    for (const size of ["A6Portrait", "A5Landscape"].filter((s) =>
        sizes.includes(s),
    )) {
        await setPageSize(page, size);
        await scrollBlockToTop(page, BLOCK, LANG);
        const after = await wherePictureSits(page);
        expect(
            after.overflowPx,
            `Drawing the book at ${size} pushed ${Math.round(after.overflowPx)}px of text -- ` +
                `about ${(after.overflowPx / after.lineHeightPx).toFixed(1)} lines -- off the end ` +
                `of the block, which no drag could have done: a move that would overflow the ` +
                `block is put back where it started. The offset is still ` +
                `${Math.round(after.offsetPx)}px, measured when the block was ` +
                `${Math.round(atA5.blockHeightPx)}px tall and ${Math.round(atA5.blockWidthPx)}px ` +
                `wide, and it is now ${Math.round(after.blockHeightPx)}px by ` +
                `${Math.round(after.blockWidthPx)}px.`,
        ).toBeLessThanOrEqual(0);
        // markedOverflowing is measured above but not asserted, because it is false even when
        // text has been pushed off the end. OverflowChecker does not count a block it has allowed
        // to scroll as overflowing, so Bloom gives the person no warning that the lines after the
        // picture are gone. That is why the test checks the overflow in pixels instead.
        //
        // The top of the picture must also be within the block's visible area, so the person
        // does not have to scroll to find it.
        expect(
            after.pictureTopInBlockPx,
            `Drawing the book at ${size} put the top of the picture ` +
                `${Math.round(after.pictureTopInBlockPx)}px down a block only ` +
                `${Math.round(after.blockHeightPx)}px tall, so the person has to scroll to find ` +
                `the picture they placed.`,
        ).toBeLessThan(after.blockHeightPx);
    }
});
