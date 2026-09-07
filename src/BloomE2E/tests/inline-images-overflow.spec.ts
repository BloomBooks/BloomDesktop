// An inline image in a text block whose text does NOT fit the block.
//
// WHY THIS IS ITS OWN FILE. Every other inline-image test works in a block with room to spare,
// which is the case the feature was built against. This one is about the opposite case, and it
// follows the steps a person took with a real book: a picture placed on a full page, the page
// then changed to a smaller size so that the text needed scrolling, and from then on the picture
// could not be moved -- "it was just stuck there".
//
// WHY THE GESTURE HAS TO BE REAL. The rule that froze the picture is measured on the block's
// scroll overflow after each move of the drag, so it exists only in a real layout: jsdom lays
// nothing out, reports every box as empty and never scrolls. The vitest suite covers the
// decision itself (shouldRevertInlineImageMove in inlineImageInteractions.test.ts); only a real
// mouse in a real WebView2 shows whether a person can move the picture.
//
// WHY A SWEEP OF DRAGS RATHER THAN ONE. Whether a single move adds scroll overflow depends on
// where the lines of text happen to fall, so one drag can get through even while the block is
// unusable. A run of moves down and back up is what "I can put it where I want it" means, and it
// is what tells the two rules apart: with the old rule at least one of these moves is undone,
// and the drag helper says which one.

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

// Enough text to fill a Just Text page at A5 and to overflow it at A6, which is the change that
// put the person's book into the state this test is about.
const TEXT = (
    "The kingfisher waits on the branch above the pool, still enough that the water forgets it " +
    "is there. It watches the shadows move under the surface. When it drops, it drops straight, " +
    "and the pool closes over the place where it went in. A moment later it is back on the " +
    "branch with a fish, and the water is still again. The children on the bank have learned " +
    "to wait as well, and they do not talk while the bird is fishing. "
).repeat(2);

// The picture both tests work on. The second carries on from where the first leaves off, in the
// same book on the same page, because building an overflowing A6 block takes most of the run.
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

    // Down the block once while it still fits, so the same gesture is known to work before the
    // page is made smaller. A failure here is not the bug this test is about.
    await scrollBlockToTop(page, BLOCK, LANG);
    const offsetWhileItFitsPx = await dragInlineImageDown(
        page,
        BLOCK,
        LANG,
        imageId,
        60,
    );
    expect(offsetWhileItFitsPx).toBeGreaterThan(0);

    // The step that caused the trouble: a smaller page, so the same text no longer fits.
    await setPageSize(page, "A6Portrait");
    await scrollBlockToTop(page, BLOCK, LANG);

    // Sanity check the state the rest of the test rests on. Without this the test could pass in
    // a block with room to spare and prove nothing.
    expect(
        await getBlockScrollOverflowPx(page, BLOCK, LANG),
        "The block's text still fits at A6, so this test is not exercising an overflowing block.",
    ).toBeGreaterThan(0);

    // And that the gestures below will land on the picture: in a block this small the picture is
    // easily scrolled out of view, and a drag aimed at where it is not reads as the picture
    // refusing to move.
    const rects = await getInlineImageRects(page, BLOCK, LANG, imageId);
    expectInside(rects.picture, rects.block, "the picture", "the block");

    // THE ACTION UNDER TEST: move the picture down the block and back up again, a step at a
    // time. Each step changes how much room the text below the picture needs, which is what the
    // old rule read as "this move made the block overflow" and undid.
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

    // It stays where the last drag left it rather than springing back once the gesture ends.
    expect((await getInlineImage(page, BLOCK, LANG, imageId)).offsetPx).toBe(
        offsetPx,
    );

    // The block still does not fit its text, which is the state the whole test is about: the
    // change under test loosened a rule for such a block, it did not make the text fit.
    expect(await getBlockScrollOverflowPx(page, BLOCK, LANG)).toBeGreaterThan(
        0,
    );
});

/**
 * Assert that the middle of the picture -- the point a drag keeps at the pointer -- is inside
 * the part of the block that is on the screen.
 *
 * Not that the whole picture is. A drag moves the picture's MIDDLE to the pointer, so a pointer
 * held just inside an edge puts the picture's far half over that edge however the block
 * scrolls, and demanding the whole picture would be demanding that the block scroll further
 * than the person pointed, which would run the text away from the cursor. What must not happen
 * is the picture sliding away under the edge altogether, and its middle staying on the screen
 * is exactly that.
 */
/**
 * Where the block's scroll comes to rest while the pointer is held still, in the page's own
 * pixels: two readings a beat apart that agree.
 *
 * "It scrolled at all" is too weak a question. Something else in the page nudges a block's
 * scroll when an image inside it moves, so a single pixel of scroll proves nothing about a drag
 * being followed; where the scroll ENDS UP while the person goes on holding the picture at the
 * edge is what says whether they can reach the end of the text.
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
    // Carries on from the test above, in the block that test made too small for its text. A
    // block that fits has nothing to scroll, so this only means anything here.
    expect(
        await getBlockScrollOverflowPx(page, BLOCK, LANG),
        "The block's text fits, so there is no scrolling for a drag to follow.",
    ).toBeGreaterThan(0);
    await scrollBlockToTop(page, BLOCK, LANG);
    expect(await getBlockScrollTopPx(page, BLOCK, LANG)).toBe(0);

    const atStart = await getInlineImageRects(page, BLOCK, LANG, imageId);

    // THE ACTION UNDER TEST: hold the picture just inside the bottom edge of the block. Most of
    // the text is below what the block can show, so carrying the picture on down means the block
    // has to scroll; if it does not, the picture goes under the edge and the person loses sight
    // of the thing they are dragging.
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
    // And it goes on scrolling for as long as the picture is held there, until the picture has
    // reached the end of the text: a block that stops part way is a block whose later lines the
    // picture cannot be dragged to.
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
    // And letting go leaves it showing: the drag ends where the block was scrolled to, so the
    // picture the person has just put down is in front of them, not off the screen.
    const afterHeldLow = await getInlineImageRects(page, BLOCK, LANG, imageId);
    expectPictureMiddleShowing(
        afterHeldLow.picture,
        afterHeldLow.block,
        "After the drag to the bottom edge ended,",
    );

    // And the same at the other edge: with the block now scrolled down, holding the picture at
    // the top edge has to bring the text back up.
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
    // And likewise all the way back to the top of the text. What settles here is the PICTURE's
    // own position in the text, not the block's scroll: the two part company at the top, because
    // once the picture is at the start of the text there is nothing above it for the block to
    // scroll to, and the block can be showing the picture flush against its top edge with a line
    // or so of the text still above the window. The offset is the thing the person is setting.
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
