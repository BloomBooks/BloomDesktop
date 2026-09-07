// What "Choose Different Layout" does to a picture the person has placed in a text block.
//
// WHAT THIS IS ABOUT. Choosing a different layout for a page does not rewrite the text: Bloom
// imports the template page and MOVES the existing bloom-editable nodes into it
// (HtmlDom.MigrateEditableData -> MigrateChildren), so the inline image wrapper arrives intact,
// geometry and all, in a box that may be a different shape. That is the same stale-offset
// problem as a page-size change, reached by a different route, and it is why MigrateChildren
// already carries data-imgsizebasedon across for bloom-canvas: "or we don't get the right
// adjustments for the probably changed size". An inline image's own baseline attribute
// (data-inline-image-offset-basedon) sits on the wrapper inside the editable, so it travels with
// the nodes that are moved, and the re-measure at page setup does the rest.
//
// The layouts here go from Just Text, whose one text block has the whole page, to Basic Text &
// Image, whose text block gives half its height to a picture. That is the shape change a person
// actually makes, and it shortens the block by more than the offset this test puts in it.
//
// NOT TESTED HERE, and not a defect of this feature: going to a layout with FEWER text blocks
// discards the extra ones outright, picture and text alike (MigrateChildren stops at
// Math.Min(template, old), and the single-page path passes allowDataLoss: true). Text is lost the
// same way, so that is Bloom's existing answer for this menu item rather than anything to do with
// inline images.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { chooseDifferentLayout } from "../helpers/addPageDialog";
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
import { runPageMenuCommand, selectPage } from "../helpers/pageThumbnails";

test.use({
    collectionSpec: { name: "inline-images-change-layout", languages: ["en"] },
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

test("a picture survives choosing a different layout for its page, and keeps the text on the page [Test Case ID 815]", async ({
    page,
}) => {
    test.setTimeout(300000);
    const contentPageId = await makePageWithAPictureLowInItsText(
        page,
        "Inline Images Change Layout",
    );

    const before = await wherePictureSits(page);
    console.log(`[probe] Just Text: ${JSON.stringify(before)}`);
    expect(
        before.dock,
        "The setup did not leave the picture in the full-width band, so this test is measuring the wrong thing.",
    ).toBe("middle");
    expect(
        before.offsetPx,
        "The setup did not move the picture down the block, so there is no offset for a layout change to strand.",
    ).toBeGreaterThan(40);
    expect(
        before.overflowPx,
        "The block already held more text than it could show before the layout change.",
    ).toBeLessThanOrEqual(0);

    // THE ACTION UNDER TEST: the page menu's own command, and the dialog's own button.
    await selectPage(page, contentPageId);
    await runPageMenuCommand(page, contentPageId, "Choose Different Layout");
    await chooseDifferentLayout(page, "Basic Text & Image", "Basic Book");

    const after = await wherePictureSits(page);
    console.log(`[probe] Basic Text & Image: ${JSON.stringify(after)}`);
    // First that it is there at all. The wrapper is moved with the text, so losing it would mean
    // the migration dropped content the person had put on the page.
    expect(
        after.widthPercent,
        "The picture did not survive the layout change at all.",
    ).toBeGreaterThan(0);
    expect(
        after.dock,
        "The layout change moved the picture to a different dock.",
    ).toBe(before.dock);
    // And then the invariant a drag keeps: no text pushed off the end of the block.
    expect(
        after.overflowPx,
        `Choosing the "Basic Text & Image" layout pushed ${Math.round(after.overflowPx)}px of ` +
            `text -- about ${(after.overflowPx / after.lineHeightPx).toFixed(1)} lines -- off the ` +
            `end of the block, which no drag could have done. The offset is ` +
            `${Math.round(after.offsetPx)}px, measured when the block was ` +
            `${Math.round(before.blockHeightPx)}px tall and ${Math.round(before.blockWidthPx)}px ` +
            `wide, and it is now ${Math.round(after.blockHeightPx)}px by ` +
            `${Math.round(after.blockWidthPx)}px.`,
    ).toBeLessThanOrEqual(0);
    expect(
        after.roomBelowPx,
        `Choosing the "Basic Text & Image" layout left the picture ` +
            `${Math.round(-after.roomBelowPx)}px past the end of everything the block holds.`,
    ).toBeGreaterThan(-2);
    // And the person can still see it without hunting for it.
    expect(
        after.pictureTopInBlockPx,
        `Choosing the "Basic Text & Image" layout put the top of the picture ` +
            `${Math.round(after.pictureTopInBlockPx)}px down a block only ` +
            `${Math.round(after.blockHeightPx)}px tall.`,
    ).toBeLessThan(after.blockHeightPx);
});
