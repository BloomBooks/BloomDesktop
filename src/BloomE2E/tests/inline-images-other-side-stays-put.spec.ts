// Checks that moving or resizing one inline image, in a block that holds several, leaves a picture
// docked on the other side where it was (BL-16822).
//
// The layout: two pictures docked right, one directly below the other, and one docked left whose
// top is a little below the top of the lower right one. Each floating picture clears the earlier
// ones on its own side, and a float can be placed no higher than any float before it in the
// markup, on either side. So when the upper right picture grows downward, by being dragged down or
// made larger, it pushes the lower right one down, and if the left picture came after the lower
// right one in the markup, it would be pushed down with it, although nothing touched it.
// reorderInFloatingCluster puts the floats in the order of the highest top each can reach
// (computeInlineImageFloatOrder), so the pushed lower right picture goes after the left one, and
// the left one stays where it was. A resize does the same.
//
// The lower right picture is pushed down when the dragged one would overlap it. That is expected,
// so the tests do not check it.
//
// Each test builds the layout on a page of its own, so either can run, and fail, alone.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
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
    dragInlineImageUp,
    getInlineImage,
    getInlineImageRects,
    resizeInlineImage,
    scrollBlockToTop,
} from "../helpers/inlineImages";

test.use({
    collectionSpec: {
        name: "inline-images-other-side-stays-put",
        languages: ["en"],
    },
});

const BLOCK = ".bloom-translationGroup";
const LANG = "en";
const TEXT =
    "The kingfisher waits on the branch above the pool, still enough that the water forgets it " +
    "is there. When it drops, it drops straight, and the pool closes over the place where it went " +
    "in. The children on the bank have learned to wait as well.";
// How far below the top of the upper right picture the left picture's top goes, in heights of
// the upper right picture. More than 1 puts it below the top of the lower right picture.
const LEFT_TOP_IN_PICTURE_HEIGHTS = 1.2;

const fixtureImage = (name: string) =>
    Path.resolve(
        Path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "fixtures",
        "images",
        name,
    );

type Page = import("@playwright/test").Page;

// Whether this worker's Bloom already has the book the tests add their pages to.
let bookMade = false;

const pictureRect = async (page: Page, id: string) =>
    (await getInlineImageRects(page, BLOCK, LANG, id)).picture;

/**
 * Make a Just Text page with text and the three pictures of the layout described at the top of
 * this file, check that the layout is what the tests need, and return the pictures' ids.
 */
async function makePageWithThreePictures(
    page: Page,
): Promise<{ upperRight: string; lowerRight: string; left: string }> {
    if (!bookMade) {
        await makeBookFromTemplate(page, "Basic Book");
        bookMade = true;
    }
    await addPage(page, "Just Text");
    const pages = await getContentPages(page);
    await goToPage(page, pages[pages.length - 1].id);
    await typeInGroup(page, BLOCK, LANG, TEXT);
    const upperRight = await addInlineImage(page, BLOCK, LANG);
    const lowerRight = await addInlineImage(page, BLOCK, LANG);
    const left = await addInlineImage(page, BLOCK, LANG);
    // A placeholder hides its img and draws a flower instead, and the gestures below aim at the
    // img, so each picture gets a real one.
    for (const id of [upperRight, lowerRight, left])
        await changeInlineImagePicture(
            page,
            BLOCK,
            LANG,
            id,
            fixtureImage("bird.png"),
        );
    await scrollBlockToTop(page, BLOCK, LANG);
    await dragInlineImageToDock(page, BLOCK, LANG, left, "left");
    const upper = await pictureRect(page, upperRight);
    const wanted = upper.y + upper.height * LEFT_TOP_IN_PICTURE_HEIGHTS;
    const leftNow = (await pictureRect(page, left)).y;
    if (leftNow > wanted)
        await dragInlineImageUp(page, BLOCK, LANG, left, leftNow - wanted);
    else if (leftNow < wanted)
        await dragInlineImageDown(page, BLOCK, LANG, left, wanted - leftNow);

    expect(
        [
            (await getInlineImage(page, BLOCK, LANG, upperRight)).dock,
            (await getInlineImage(page, BLOCK, LANG, lowerRight)).dock,
            (await getInlineImage(page, BLOCK, LANG, left)).dock,
        ],
        "The layout should have two pictures docked right and one docked left.",
    ).toEqual(["right", "right", "left"]);
    const upperTop = (await pictureRect(page, upperRight)).y;
    const lowerTop = (await pictureRect(page, lowerRight)).y;
    const leftTop = (await pictureRect(page, left)).y;
    expect(
        lowerTop,
        "The two right pictures should be stacked, the second directly below the first.",
    ).toBeGreaterThanOrEqual(upperTop + upper.height - 1);
    expect(
        leftTop > lowerTop && leftTop < lowerTop + upper.height / 2,
        `The left picture should start a little below the top of the lower right one, but it ` +
            `starts at ${leftTop}, with the right pictures at ${upperTop} and ${lowerTop}.`,
    ).toBe(true);
    return { upperRight, lowerRight, left };
}

test("dragging one picture down, pushing the one below it, does not move the picture on the other side [Test Case ID 815]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    let ids = { upperRight: "", lowerRight: "", left: "" };
    await step("Make a page with text and three pictures", async () => {
        ids = await makePageWithThreePictures(page);
    });
    const upper = await pictureRect(page, ids.upperRight);
    const lowerTop = (await pictureRect(page, ids.lowerRight)).y;
    const leftTop = (await pictureRect(page, ids.left)).y;

    await step(
        "Drag the upper right picture down half its height",
        async () => {
            await dragInlineImageDown(
                page,
                BLOCK,
                LANG,
                ids.upperRight,
                Math.round(upper.height / 2),
            );
        },
    );
    // Sanity check: the drag has to push the lower right picture past the left one's top, or
    // the left one would have nothing to be pushed by.
    const lowerAfter = (await pictureRect(page, ids.lowerRight)).y;
    expect(
        lowerAfter,
        `The drag should have pushed the lower right picture below the left picture's top ` +
            `(${leftTop}), but it went from ${lowerTop} to ${lowerAfter}.`,
    ).toBeGreaterThan(leftTop + 1);

    const leftAfter = (await pictureRect(page, ids.left)).y;
    expect(
        Math.abs(leftAfter - leftTop),
        `Dragging the upper right picture down moved the left picture from ${leftTop} to ` +
            `${leftAfter}, along with the lower right picture it pushed.`,
    ).toBeLessThanOrEqual(1);
});

test("resizing one picture larger, pushing the one below it, does not move the picture on the other side [Test Case ID 815]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    let ids = { upperRight: "", lowerRight: "", left: "" };
    await step("Make a page with text and three pictures", async () => {
        ids = await makePageWithThreePictures(page);
    });
    const lowerTop = (await pictureRect(page, ids.lowerRight)).y;
    const leftTop = (await pictureRect(page, ids.left)).y;
    const widthBefore = (
        await getInlineImage(page, BLOCK, LANG, ids.upperRight)
    ).widthPercent;

    let width = 0;
    await step("Make the upper right picture larger", async () => {
        width = await resizeInlineImage(
            page,
            BLOCK,
            LANG,
            ids.upperRight,
            "sw",
            40,
        );
    });
    expect(
        width,
        "The resize should have made the upper right picture wider.",
    ).toBeGreaterThan(widthBefore);
    const lowerAfter = (await pictureRect(page, ids.lowerRight)).y;
    expect(
        lowerAfter,
        `The resize should have pushed the lower right picture below the left picture's top ` +
            `(${leftTop}), but it went from ${lowerTop} to ${lowerAfter}.`,
    ).toBeGreaterThan(leftTop + 1);

    const leftAfter = (await pictureRect(page, ids.left)).y;
    expect(
        Math.abs(leftAfter - leftTop),
        `Resizing the upper right picture moved the left picture from ${leftTop} to ${leftAfter}, ` +
            `along with the lower right picture it pushed.`,
    ).toBeLessThanOrEqual(1);
});
