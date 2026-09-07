// What happens to a picture's position down its block when the book is drawn at another size.
//
// WHAT THIS IS ABOUT. Of the three things that record an inline image's geometry, two are
// relative -- `--inline-image-width` is a percent of the block and `--inline-image-aspect-ratio`
// is a ratio -- and one, `--inline-image-offset`, is absolute layout pixels. Changing the book's
// page size only swaps classes on the page div: no HTML is rewritten and no inline style is
// touched, and the offset's clamp runs only during a drag, so nothing re-measures the offset
// against a block that has just become shorter and narrower.
//
// So this asks what the person sees afterwards. The invariant worth holding is the one the drag
// itself maintains: a move that would push the block into overflow is put back where it started
// (shouldOccupyInlineImageMove / FIT OR REVERT). A page-size change reaches the same state the
// drag refuses to create, and nothing will ever correct it -- the offset holds the picture the
// same distance below the start of the text however short the block becomes, so the text that
// follows the picture is pushed off the end.
//
// Canvas elements solve the same problem with `data-imgsizebasedon` and
// adjustCanvasElementChildrenIfSizeChanged; inline images have no equivalent, which is why this
// test exists.

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

// Enough text to give the block lines all the way down at A5, so there is somewhere to put the
// picture, and enough that a smaller page will have to rewrap it.
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

/** Everything about where the picture sits, in one reading, for comparing across page sizes. */
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
        // How far down the block's own window the picture is drawn, which is what the person sees.
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

    // The full-width band is the dock whose position down the block is a distance, so it is the
    // one an absolute offset can strand.
    await scrollBlockToTop(page, BLOCK, LANG);
    await dragInlineImageToDock(page, BLOCK, LANG, imageId, "middle");
    // As far down the block as the drag will take it. A person filling an A5 page puts the
    // picture near the end of their text, which is the largest offset the block allows -- and an
    // absolute offset measured against the tallest page is the one a shorter page cannot hold.
    // A modest offset (145px) survived every size, because line height does not change with page
    // size, so the same offset leaves the same number of lines above the picture; what runs out
    // is the room below it, and only a large offset runs it out.
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
    // Sanity check on the starting state: the drag's own clamp holds this, and it is the thing
    // the size change is being asked not to break.
    expect(
        atA5.roomBelowPx,
        "The picture was already past the end of the block's content before any size change, so the drag itself is at fault, not the size change.",
    ).toBeGreaterThan(-2);
    // The state every assertion below is about not losing. The drag guarantees this: it puts a
    // move back where it started rather than let the block overflow.
    expect(
        atA5.overflowPx,
        "The block already held more text than it could show before any size change, so there is nothing for the size change to be blamed for.",
    ).toBeLessThanOrEqual(0);

    // THE ACTION UNDER TEST, at each size this book offers that changes the block's shape.
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
        // markedOverflowing is in the reading above rather than asserted here because it comes
        // back FALSE: Bloom does not warn about this. OverflowChecker treats a block it has
        // allowed to scroll as not overflowing, so the person is given no sign that the lines
        // after the picture have gone -- which is why the arithmetic above is what this asserts.
        // And what the person actually sees: the picture is still somewhere in the block's window,
        // not below it needing a scroll to find.
        expect(
            after.pictureTopInBlockPx,
            `Drawing the book at ${size} put the top of the picture ` +
                `${Math.round(after.pictureTopInBlockPx)}px down a block only ` +
                `${Math.round(after.blockHeightPx)}px tall, so the person has to scroll to find ` +
                `the picture they placed.`,
        ).toBeLessThan(after.blockHeightPx);
    }
});
