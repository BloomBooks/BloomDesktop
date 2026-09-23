// What cutting a text block in half in Change Layout mode does to a picture placed low in it.
//
// WHAT THIS IS ABOUT. Origami changes the shape of a block without any template or page size
// being involved: splitting a section leaves the text in a fraction of the height it had. That is
// the third route to the same stale offset as inline-images-page-size and the Choose Different
// Layout test, and it is the one that needs no dialog at all.
//
// Nothing about the page is written while the person is arranging boxes. Leaving Change Layout
// mode is what makes origami save the page and ask Bloom to rebuild it
// (changeLayoutModeToggleClickHandler posts common/saveChangesAndRethinkPageEvent), so the
// re-measure at page setup is what has to catch this, and there is no need for origami to call it
// itself. This test is what says so.

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
    scrollBlockToTop,
} from "../helpers/inlineImages";
import { setChangeLayoutMode, splitSection } from "../helpers/origami";

test.use({
    collectionSpec: { name: "inline-images-origami", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

const BLOCK = ".bloom-translationGroup";
const LANG = "en";

// Short enough that the SHORTENED block can still hold it all. Any more and the layout change
// overflows the block whatever the picture does -- half the height with the same text -- and
// then the overflow this test asserts on says nothing about the picture's offset, which is what
// it is here to watch. Plain text of that length overflows the same way, so that is Bloom's
// answer for the menu item rather than a defect of inline images.
const TEXT =
    "The kingfisher waits on the branch above the pool, still enough that the water forgets it " +
    "is there. It watches the shadows move under the surface.";

const fixtureImage = (name: string) =>
    Path.resolve(
        Path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "fixtures",
        "images",
        name,
    );

let imageId: string;

/** Everything about where the picture sits, in one reading, for comparing across layouts. */
const wherePictureSits = async (page: Page) => {
    const image = await getInlineImage(page, BLOCK, LANG, imageId);
    const rects = await getInlineImageRects(page, BLOCK, LANG, imageId);
    return {
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
        lineHeightPx: await getBlockLineHeightPx(page, BLOCK, LANG),
        pictureTopInBlockPx: rects.picture.y - rects.block.y,
        blockHeightPx: rects.block.height,
        blockWidthPx: rects.block.width,
    };
};

/**
 * A page with the picture in the full-width band as far down the text as the drag will take it,
 * which is the state a shorter block cannot hold. Returns the page's id.
 */
const makePageWithAPictureLowInItsText = async (
    page: Page,
    title: string,
): Promise<string> => {
    await makeBookFromTemplate(page, "Basic Book");
    await typeInGroup(page, ".bookTitle", "en", title);
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
    await scrollBlockToTop(page, BLOCK, LANG);
    await dragInlineImageToDock(page, BLOCK, LANG, imageId, "middle");
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
    return contentPage.id;
};

test("a picture survives having its block cut in half in Change Layout mode [Test Case ID 815]", async ({
    page,
}) => {
    test.setTimeout(300000);
    await makePageWithAPictureLowInItsText(page, "Inline Images Origami");

    const before = await wherePictureSits(page);
    expect(
        before.overflowPx,
        "The block already held more text than it could show before the layout was changed.",
    ).toBeLessThanOrEqual(0);

    // THE ACTION UNDER TEST: split the text block's section in two, which leaves the text in the
    // lower half of the height it had. Nothing about the page is written until the person leaves
    // Change Layout mode -- that is when origami saves the page and asks Bloom to rebuild it --
    // so the re-measure at page setup is what has to catch this.
    await setChangeLayoutMode(page, true);
    await splitSection(page, "top");
    await setChangeLayoutMode(page, false);

    const after = await wherePictureSits(page);
    expect(
        after.widthPercent,
        "The picture did not survive the split at all.",
    ).toBeGreaterThan(0);
    expect(
        after.blockHeightPx,
        "The split did not shorten the block, so this test is not measuring what it means to.",
    ).toBeLessThan(before.blockHeightPx - 10);
    expect(
        after.overflowPx,
        `Splitting the block pushed ${Math.round(after.overflowPx)}px of text -- about ` +
            `${(after.overflowPx / after.lineHeightPx).toFixed(1)} lines -- off the end of it, ` +
            `which no drag could have done. The offset is ${Math.round(after.offsetPx)}px, ` +
            `measured when the block was ${Math.round(before.blockHeightPx)}px tall, and the ` +
            `block is now ${Math.round(after.blockHeightPx)}px tall.`,
    ).toBeLessThanOrEqual(0);
    expect(
        after.pictureTopInBlockPx,
        `Splitting the block put the top of the picture ${Math.round(after.pictureTopInBlockPx)}px ` +
            `down a block only ${Math.round(after.blockHeightPx)}px tall.`,
    ).toBeLessThan(after.blockHeightPx);
});
