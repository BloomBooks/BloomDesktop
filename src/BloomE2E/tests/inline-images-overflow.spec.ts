// Tests an inline image in a text block whose text does not fit in the block.
//
// The other inline-image tests use a block with room to spare. This file follows the steps a
// person took with a real book: they put a picture on a full page, then changed the page to a
// smaller size so the text needed scrolling, and after that the picture could not be moved.
//
// The test drags with the mouse in WebView2 because the code that decides whether to undo a move
// measures the block's scroll overflow after each step of the drag. jsdom does no layout: every
// box has zero size and nothing scrolls. The vitest suite tests that decision by itself
// (shouldRevertInlineImageMove in inlineImageInteractions.test.ts).
//
// The test makes several drags down and back up. Whether one move adds scroll overflow depends on
// where the lines of text fall, so a single drag can succeed even when most moves in the block
// would be undone. If any of these moves is undone, the drag helper reports which one.

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
    dragInlineImageDown,
    dragInlineImageUp,
    endInlineImageDrag,
    getBlockLineHeightPx,
    getBlockScrollOverflowPx,
    getBlockScrollTopPx,
    getInlineImage,
    getInlineImageRects,
    moveInlineImageDragTo,
    scrollBlockToTop,
} from "../helpers/inlineImages";
import { expectInside, IRect } from "../helpers/geometry";
import { setPageSize } from "../helpers/pageSize";

test.use({
    collectionSpec: { name: "inline-images-overflow", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

const BLOCK = ".bloom-translationGroup";
const LANG = "en";

// Enough text to fill a Just Text page at A5 and overflow it at A6. Changing from A5 to A6 is
// what made the text overflow in the person's book.
const TEXT = (
    "The kingfisher waits on the branch above the pool, still enough that the water forgets it " +
    "is there. It watches the shadows move under the surface. When it drops, it drops straight, " +
    "and the pool closes over the place where it went in. A moment later it is back on the " +
    "branch with a fish, and the water is still again. The children on the bank have learned " +
    "to wait as well, and they do not talk while the bird is fishing. "
).repeat(2);

// The picture both tests work on. The second test continues in the book and page the first one
// left, because setting up the overflowing A6 block takes most of the run time.
let imageId: string;

const fixtureImage = (name: string) =>
    Path.resolve(
        Path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "fixtures",
        "images",
        name,
    );

test("a picture in a block whose text overflows can still be moved [Test Case ID 815]", async ({
    page,
}) => {
    test.setTimeout(300000);
    await makeBookFromTemplate(page, "Basic Book");
    await typeInGroup(page, ".bookTitle", "en", "Inline Images Overflow");
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

    // Drag down once while the text still fits, to show the drag works before the page gets
    // smaller. A failure here is some other problem than the one this test is about.
    await scrollBlockToTop(page, BLOCK, LANG);
    const offsetWhileItFitsPx = await dragInlineImageDown(
        page,
        BLOCK,
        LANG,
        imageId,
        60,
    );
    expect(offsetWhileItFitsPx).toBeGreaterThan(0);

    // Make the page smaller, so the same text no longer fits.
    await setPageSize(page, "A6Portrait");
    await scrollBlockToTop(page, BLOCK, LANG);

    // Check that the text overflows. Without this check the test could pass in a block with room
    // to spare and prove nothing.
    expect(
        await getBlockScrollOverflowPx(page, BLOCK, LANG),
        "The block's text still fits at A6, so this test is not exercising an overflowing block.",
    ).toBeGreaterThan(0);

    // Also check that the picture is on screen, so the drags below start on it. In a block this
    // small the picture can easily be scrolled out of view, and a drag that misses it would look
    // like the picture refusing to move.
    const rects = await getInlineImageRects(page, BLOCK, LANG, imageId);
    expectInside(rects.picture, rects.block, "the picture", "the block");

    // THE ACTION UNDER TEST: move the picture down the block and back up again, a step at a
    // time. Each step changes how much room the text below the picture needs, and the code must
    // not treat that change as the move making the block overflow and undo it.
    const kStepPx = 50;
    const kSteps = 4;
    let offsetPx = (await getInlineImage(page, BLOCK, LANG, imageId)).offsetPx;
    for (let step = 1; step <= kSteps; step++) {
        const moved = await dragInlineImageDown(
            page,
            BLOCK,
            LANG,
            imageId,
            kStepPx,
        );
        expect(moved, `Down step ${step} of ${kSteps}`).toBeGreaterThan(
            offsetPx,
        );
        offsetPx = moved;
    }
    for (let step = 1; step <= kSteps; step++) {
        const moved = await dragInlineImageUp(
            page,
            BLOCK,
            LANG,
            imageId,
            kStepPx,
        );
        expect(moved, `Up step ${step} of ${kSteps}`).toBeLessThan(offsetPx);
        offsetPx = moved;
    }

    // The picture stays where the last drag left it after the drag ends.
    expect((await getInlineImage(page, BLOCK, LANG, imageId)).offsetPx).toBe(
        offsetPx,
    );

    // The text still overflows the block. Moving the picture is allowed in an overflowing block,
    // and the moves did not make the text fit.
    expect(await getBlockScrollOverflowPx(page, BLOCK, LANG)).toBeGreaterThan(
        0,
    );
});

/**
 * Returns the block's scrollTop, in page pixels, once it stops changing while the pointer is
 * held still: two readings `stillMs` apart that agree.
 *
 * Checking only that the block scrolled at all is too weak. Other code in the page nudges a
 * block's scroll when an image inside it moves, so a pixel of scroll does not show that the
 * block followed the drag. Where the scroll stops while the person keeps holding the picture at
 * the edge shows whether they can reach the end of the text.
 */
const scrollTopWhereItSettles = async (
    page: Page,
    stillMs = 700,
): Promise<number> => {
    let previous = -1;
    for (let read = 0; read < 40; read++) {
        const now = await getBlockScrollTopPx(page, BLOCK, LANG);
        if (now === previous) return now;
        previous = now;
        await page.waitForTimeout(stillMs);
    }
    return previous;
};

/**
 * Asserts that the middle of the picture, which is the point a drag keeps under the pointer, is
 * inside the part of the block that is on the screen.
 *
 * It does not require the whole picture to be showing. A drag keeps the picture's middle under
 * the pointer, so with the pointer held just inside an edge, half the picture is past that edge
 * however the block scrolls. Requiring the whole picture would mean the block scrolling further
 * than the person pointed, which would move the text away from the cursor. The failure this
 * checks for is the picture sliding out of sight under the edge, and that is the same as its
 * middle leaving the screen.
 */
const expectPictureMiddleShowing = (
    picture: IRect,
    block: IRect,
    when: string,
) => {
    const middleY = picture.y + picture.height / 2;
    const where =
        `${when} the middle of the picture was at y ${Math.round(middleY)}, and the part of the ` +
        `block on the screen runs from ${Math.round(block.y)} to ` +
        `${Math.round(block.y + block.height)}.`;
    expect(middleY, where).toBeGreaterThanOrEqual(block.y);
    expect(middleY, where).toBeLessThanOrEqual(block.y + block.height);
};

test("the block scrolls to follow the picture when it is dragged past an edge", async ({
    page,
}) => {
    test.setTimeout(300000);
    // This continues in the block the test above made too small for its text. A block whose
    // text fits has nothing to scroll, so this test needs that one.
    expect(
        await getBlockScrollOverflowPx(page, BLOCK, LANG),
        "The block's text fits, so there is no scrolling for a drag to follow.",
    ).toBeGreaterThan(0);
    await scrollBlockToTop(page, BLOCK, LANG);
    expect(await getBlockScrollTopPx(page, BLOCK, LANG)).toBe(0);

    const atStart = await getInlineImageRects(page, BLOCK, LANG, imageId);

    // THE ACTION UNDER TEST: hold the picture just inside the bottom edge of the block. Most of
    // the text is below the part of the block that shows, so to keep moving the picture down the
    // block has to scroll. If it does not, the picture goes under the edge and the person can no
    // longer see what they are dragging.
    await beginInlineImageDrag(page, BLOCK, LANG, imageId);
    await moveInlineImageDragTo(page, {
        x: atStart.block.x + atStart.block.width / 2,
        y: atStart.block.y + atStart.block.height - 8,
    });
    await expect
        .poll(async () => getBlockScrollTopPx(page, BLOCK, LANG), {
            timeout: 15000,
            message:
                "Holding the picture at the bottom edge of the block never scrolled the block, " +
                "so the picture is being dragged into text the person cannot see.",
        })
        .toBeGreaterThan(0);
    // The block keeps scrolling while the picture is held there, until the picture reaches the
    // end of the text. If the scrolling stopped part way, the picture could not be dragged to
    // the last lines.
    const overflowPx = await getBlockScrollOverflowPx(page, BLOCK, LANG);
    const lineHeightPx = await getBlockLineHeightPx(page, BLOCK, LANG);
    const settledLowPx = await scrollTopWhereItSettles(page);
    expect(
        settledLowPx,
        `Holding the picture at the bottom edge scrolled the block to ${Math.round(settledLowPx)} ` +
            `and stopped, ${Math.round(overflowPx - settledLowPx)}px short of the end of the ` +
            `text at ${Math.round(overflowPx)} -- so the last ` +
            `${((overflowPx - settledLowPx) / lineHeightPx).toFixed(1)} lines are out of reach.`,
    ).toBeGreaterThanOrEqual(overflowPx - lineHeightPx);
    const whileHeldLow = await getInlineImageRects(page, BLOCK, LANG, imageId);
    expectPictureMiddleShowing(
        whileHeldLow.picture,
        whileHeldLow.block,
        "While the picture was held at the bottom edge of the block,",
    );
    await endInlineImageDrag(page);
    // After the person lets go, the picture is still on screen, because the block stays scrolled
    // to where the drag took it.
    const afterHeldLow = await getInlineImageRects(page, BLOCK, LANG, imageId);
    expectPictureMiddleShowing(
        afterHeldLow.picture,
        afterHeldLow.block,
        "After the drag to the bottom edge ended,",
    );

    // Now check the top edge. The block is scrolled down, so holding the picture at the top
    // edge has to scroll the text back.
    const scrolledDownPx = await getBlockScrollTopPx(page, BLOCK, LANG);
    expect(scrolledDownPx).toBeGreaterThan(0);
    const beforeUp = await getInlineImageRects(page, BLOCK, LANG, imageId);
    await beginInlineImageDrag(page, BLOCK, LANG, imageId);
    await moveInlineImageDragTo(page, {
        x: beforeUp.block.x + beforeUp.block.width / 2,
        y: beforeUp.block.y + 8,
    });
    await expect
        .poll(async () => getBlockScrollTopPx(page, BLOCK, LANG), {
            timeout: 15000,
            message:
                "Holding the picture at the top edge of the block never scrolled the block back " +
                "up, so a picture dragged off the top cannot be seen.",
        })
        .toBeLessThan(scrolledDownPx);
    // It should go all the way back to the start of the text. Here the test checks the
    // picture's offset instead of the block's scroll. Once the picture is at the start of the
    // text there is nothing above it to scroll to, but the block can show the picture against its
    // top edge with a line or so of text still scrolled out of sight above. The offset is what the
    // person is setting, so that is what has to come back to the top.
    const settledHighPx = await scrollTopWhereItSettles(page);
    const highOffsetPx = (await getInlineImage(page, BLOCK, LANG, imageId))
        .offsetPx;
    expect(
        highOffsetPx,
        `Holding the picture at the top edge brought it back to an offset of ` +
            `${Math.round(highOffsetPx)}px, ${(highOffsetPx / lineHeightPx).toFixed(1)} lines ` +
            `below the start of the text, so those lines cannot be put above the picture. The ` +
            `block's scroll settled at ${Math.round(settledHighPx)}.`,
    ).toBeLessThanOrEqual(lineHeightPx);
    const whileHeldHigh = await getInlineImageRects(page, BLOCK, LANG, imageId);
    expectPictureMiddleShowing(
        whileHeldHigh.picture,
        whileHeldHigh.block,
        "While the picture was held at the top edge of the block,",
    );
    await endInlineImageDrag(page);
    const afterHeldHigh = await getInlineImageRects(page, BLOCK, LANG, imageId);
    expectPictureMiddleShowing(
        afterHeldHigh.picture,
        afterHeldHigh.block,
        "After the drag to the top edge ended,",
    );
});
