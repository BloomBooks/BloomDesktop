// Checks where an inline image stops when it is dragged down a block that has room below its text
// (BL-16822).
//
// There are two places near the bottom. A picture dragged down as far as it goes stops with its
// bottom edge on the bottom of the block, in the middle third, and half a line above it in the left
// or right third, where the picture keeps its bottom margin. The other place is the dock after the
// text (the bottom dock), which puts the picture straight after the last line, below any picture
// that hangs below the text, and after the pictures already in that dock. A picture dragged down
// the middle third is put there while its top is within one line below where that dock would put
// it (computeInlineImageDock, tryBottomDock), so that dock can be reached without moving the
// pointer below the block.
//
// Before that, the middle third changed to the bottom dock as soon as the picture's bottom edge
// reached the bottom of the block, so the picture jumped back up to just after the text and could
// not be left on the bottom of the block. In the left and right thirds the limit on the picture's
// distance down left out its bottom margin, so the last part of a move made the block overflow and
// the whole move was undone, which left the picture wherever the move before had put it.
//
// Each test builds its own page, so each can run, and fail, alone.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    reloadPageBeingEdited,
    typeInGroup,
} from "../helpers/bookMaking";
import { bottom } from "../helpers/geometry";
import {
    addInlineImage,
    changeInlineImagePicture,
    dragInlineImageInSteps,
    dragInlineImageToBottomOfBlock,
    dragInlineImageToBottomOfBlockInOneMove,
    dragInlineImageToDock,
    dragInlineImageUp,
    getBlockLineHeightPx,
    getBlockScrollOverflowPx,
    getInlineImage,
    getInlineImageInEveryLanguage,
    getInlineImageRects,
    getRoomBelowInlineImagePx,
    type IInlineImageDragStep,
    scrollBlockToTop,
    putCaretAtEndOfBlockText,
    typeAtCaretInBlock,
} from "../helpers/inlineImages";
import { getZoom, setZoom } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "inline-images-rest-on-bottom",
        languages: ["en", "fr"],
    },
});

const BLOCK = ".bloom-translationGroup";
const LANG = "en";
// A few lines, so that a Just Text page has plenty of empty space below them.
const TEXT =
    "The kingfisher waits on the branch above the pool, still enough that the water forgets it " +
    "is there. When it drops, it drops straight.";
// One line, so that a picture beside it reaches below it.
const ONE_LINE = "The kingfisher waits.";
// Enough text to fill most of a Just Text page, with room left for the picture.
const PARAGRAPH =
    "The kingfisher waits on the branch above the pool, still enough that the water forgets it " +
    "is there. It watches the shadows move under the surface. When it drops, it drops straight, " +
    "and the pool closes over the place where it went in. A moment later it is back on the " +
    "branch with a fish, and the water is still again. The children on the bank have learned " +
    "to wait as well, and they do not talk while the bird is fishing. ";
const FULL_TEXT = PARAGRAPH.repeat(3).trim();
// How close two positions on the screen have to be to count as the same place, in pixels.
const SAME_PLACE_PX = 1.5;

type Third = "left" | "middle" | "right";

const fixtureImage = (name: string) =>
    Path.resolve(
        Path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "fixtures",
        "images",
        name,
    );

// Whether this worker's Bloom already has the book the tests add their pages to.
let bookMade = false;

/** Add a Just Text page to the book and type `text` on it. */
async function makeTextPage(page: Page, text: string): Promise<void> {
    if (!bookMade) {
        await makeBookFromTemplate(page, "Basic Book");
        bookMade = true;
    }
    await addPage(page, "Just Text");
    const pages = await getContentPages(page);
    await goToPage(page, pages[pages.length - 1].id);
    await typeInGroup(page, BLOCK, LANG, text);
}

/**
 * Insert a picture in the block, which goes in docked at the top right, give it a real picture (a
 * placeholder hides its img, and the gestures aim at the img), and return its id.
 */
async function addPicture(page: Page): Promise<string> {
    const id = await addInlineImage(page, BLOCK, LANG);
    await changeInlineImagePicture(
        page,
        BLOCK,
        LANG,
        id,
        fixtureImage("bird.png"),
    );
    return id;
}

/** The height of one line of the block's text on the screen, at the zoom the page is drawn at. */
async function lineHeightOnScreen(page: Page): Promise<number> {
    return (
        ((await getBlockLineHeightPx(page, BLOCK, LANG)) *
            (await getZoom(page)).zoom) /
        100
    );
}

/** The dock a picture is in, and how far its bottom edge is above the bottom edge of its block. */
async function restingPlace(
    page: Page,
    id: string,
): Promise<{ dock: string; gapBelowPx: number }> {
    const rects = await getInlineImageRects(page, BLOCK, LANG, id);
    return {
        dock: (await getInlineImage(page, BLOCK, LANG, id)).dock,
        gapBelowPx: bottom(rects.block) - bottom(rects.picture),
    };
}

/**
 * Check that a picture has stopped at the bottom of its block in this third: in the same dock, with
 * its bottom edge on the bottom edge of the block in the middle third, or within a line above it
 * in the left or right third, and with nothing pushed out of the block.
 */
async function expectAtBottomOfBlock(
    page: Page,
    id: string,
    third: Third,
    how: string,
): Promise<{ dock: string; gapBelowPx: number }> {
    const place = await restingPlace(page, id);
    const line = await lineHeightOnScreen(page);
    expect(
        place.dock,
        `${how}, the picture should still be a ${third} picture. "bottom" means it jumped back ` +
            `up to just after the text.`,
    ).toBe(third);
    expect(
        place.gapBelowPx,
        `${how}, the picture's bottom edge is ${Math.round(-place.gapBelowPx)}px below the ` +
            `bottom of its block.`,
    ).toBeGreaterThanOrEqual(-SAME_PLACE_PX);
    // In the middle third the picture stops on the bottom edge. In the left and right thirds it
    // stops half an em higher, which is less than a line.
    const allowed = third === "middle" ? line / 4 : line;
    expect(
        place.gapBelowPx,
        `${how}, the picture stopped ${Math.round(place.gapBelowPx)}px ` +
            `(${(place.gapBelowPx / line).toFixed(1)} lines) above the bottom of its block.`,
    ).toBeLessThanOrEqual(allowed);
    expect(
        await getBlockScrollOverflowPx(page, BLOCK, LANG),
        `${how}, the block holds more than it has room for.`,
    ).toBeLessThanOrEqual(0);
    return place;
}

for (const third of ["middle", "left", "right"] as const) {
    test(`a picture dragged down the ${third} of its block stops at the bottom of the block, slowly or in one quick move, and stays there [Test Case ID 815]`, async ({
        page,
        step,
    }) => {
        test.setTimeout(300000);
        let id = "";
        await step(
            "Make a page with a few lines of text and a picture",
            async () => {
                await makeTextPage(page, TEXT);
                id = await addPicture(page);
                await scrollBlockToTop(page, BLOCK, LANG);
                if (third !== "right")
                    await dragInlineImageToDock(page, BLOCK, LANG, id, third);
            },
        );

        await step("Drag the picture down as far as it goes", async () => {
            await dragInlineImageToBottomOfBlock(page, BLOCK, LANG, id, third);
        });
        const slow = await expectAtBottomOfBlock(
            page,
            id,
            third,
            "After a drag down to the bottom of the block",
        );

        await step(
            "Drag it back up a few lines, then to the bottom in one movement",
            async () => {
                await dragInlineImageUp(
                    page,
                    BLOCK,
                    LANG,
                    id,
                    Math.round(6 * (await lineHeightOnScreen(page))),
                );
                await dragInlineImageToBottomOfBlockInOneMove(
                    page,
                    BLOCK,
                    LANG,
                    id,
                );
            },
        );
        const quick = await expectAtBottomOfBlock(
            page,
            id,
            third,
            "After one quick move past the bottom of the block",
        );
        expect(
            Math.abs(quick.gapBelowPx - slow.gapBelowPx),
            `A quick move left the picture ${Math.round(quick.gapBelowPx)}px above the bottom ` +
                `of the block, and a slow drag ${Math.round(slow.gapBelowPx)}px. They should ` +
                `end in the same place.`,
        ).toBeLessThanOrEqual(SAME_PLACE_PX);

        await step("Leave the page and come back", async () => {
            await reloadPageBeingEdited(page);
        });
        const reloaded = await expectAtBottomOfBlock(
            page,
            id,
            third,
            "After leaving the page and coming back",
        );
        expect(
            Math.abs(reloaded.gapBelowPx - quick.gapBelowPx),
            `The picture was ${Math.round(quick.gapBelowPx)}px above the bottom of the block ` +
                `and came back ${Math.round(reloaded.gapBelowPx)}px above it.`,
        ).toBeLessThanOrEqual(SAME_PLACE_PX);
    });
}

for (const zoom of [50, 200]) {
    test(`at ${zoom}% zoom, a picture dragged down the middle of its block stops with its bottom edge on the bottom of the block [Test Case ID 815]`, async ({
        page,
        step,
    }) => {
        test.setTimeout(300000);
        let id = "";
        await step(
            "Make a page with a few lines of text and a picture",
            async () => {
                await makeTextPage(page, TEXT);
                id = await addPicture(page);
                // Before the zoom, because at 200% the page is wider than a small window and the
                // middle of the block can be out of sight.
                await scrollBlockToTop(page, BLOCK, LANG);
                await dragInlineImageToDock(page, BLOCK, LANG, id, "middle");
            },
        );
        const zoomAtStart = (await getZoom(page)).zoom;
        try {
            await step(
                `Zoom to ${zoom}% and drag the picture down the middle`,
                async () => {
                    await setZoom(page, zoom);
                    await dragInlineImageToBottomOfBlock(
                        page,
                        BLOCK,
                        LANG,
                        id,
                        "middle",
                    );
                },
            );
            await expectAtBottomOfBlock(
                page,
                id,
                "middle",
                `At ${zoom}% zoom, after a drag down to the bottom of the block`,
            );
        } finally {
            // Bloom saves the zoom as a user setting, so put it back the way it was.
            await setZoom(page, zoomAtStart);
        }
    });
}

test("in a block full of text, a picture dragged down the middle to the end has all the text above it and pushes nothing out of the block [Test Case ID 815]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    let id = "";
    await step(
        "Make a page full of text with a picture in the middle",
        async () => {
            await makeTextPage(page, FULL_TEXT);
            id = await addPicture(page);
            await scrollBlockToTop(page, BLOCK, LANG);
            await dragInlineImageToDock(page, BLOCK, LANG, id, "middle");
        },
    );
    const line = await getBlockLineHeightPx(page, BLOCK, LANG);
    expect(
        await getBlockScrollOverflowPx(page, BLOCK, LANG),
        "The text and the picture should fit the block before the drag.",
    ).toBeLessThanOrEqual(0);
    const pictureBefore = (await getInlineImageRects(page, BLOCK, LANG, id))
        .picture;
    const roomBefore = await getRoomBelowInlineImagePx(page, BLOCK, LANG, id);
    expect(
        roomBefore,
        "The block should have more below the picture than the picture is tall, or the " +
            "picture has nothing to move past.",
    ).toBeGreaterThan(pictureBefore.height);

    await step(
        "Drag the picture down the middle as far as it goes",
        async () => {
            await dragInlineImageToBottomOfBlock(
                page,
                BLOCK,
                LANG,
                id,
                "middle",
            );
        },
    );
    await expectAtBottomOfBlock(
        page,
        id,
        "middle",
        "In a block full of text, after a drag down to the end of the block",
    );
    expect(
        await getRoomBelowInlineImagePx(page, BLOCK, LANG, id),
        "Text was left below the picture, so the text did not all move above it.",
    ).toBeLessThan(line);
});

/** The steps of a drag as text, for a failure message. */
function describeSteps(steps: IInlineImageDragStep[]): string {
    return steps
        .map(
            (s) =>
                `${s.dock} pointer ${Math.round(s.pointerTopInBlock)} picture ${Math.round(s.pictureTopInBlock)}`,
        )
        .join("; ");
}

/** The first run of consecutive steps in the bottom dock, as the indexes of its first and last step. */
function firstRunInBottomDock(
    steps: IInlineImageDragStep[],
): { first: number; last: number } | undefined {
    const first = steps.findIndex((s) => s.dock === "bottom");
    if (first < 0) return undefined;
    let last = first;
    while (last + 1 < steps.length && steps[last + 1].dock === "bottom") last++;
    return { first, last };
}

/** True once a drag has been in the bottom dock and has then left it for three steps. */
const caughtAndReleased = (steps: IInlineImageDragStep[]) =>
    steps.some((s) => s.dock === "bottom") &&
    steps.length >= 3 &&
    steps.slice(-3).every((s) => s.dock !== "bottom");

test("a picture dragged down the middle is put straight after the text while its top is within a line below it, and typing more text moves it down [Test Case ID 815]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    let id = "";
    await step(
        "Make a page with a few lines of text and a picture in the middle",
        async () => {
            await makeTextPage(page, TEXT);
            id = await addPicture(page);
            await scrollBlockToTop(page, BLOCK, LANG);
            await dragInlineImageToDock(page, BLOCK, LANG, id, "middle");
        },
    );
    const line = await lineHeightOnScreen(page);
    const stepPx = Math.max(3, Math.round(line / 4));

    let down: IInlineImageDragStep[] = [];
    await step(
        "Drag the picture down a quarter of a line at a time",
        async () => {
            down = await dragInlineImageInSteps(
                page,
                BLOCK,
                LANG,
                id,
                stepPx,
                caughtAndReleased,
            );
        },
    );
    const run = firstRunInBottomDock(down);
    expect(
        run,
        `Dragged down the middle of the block a quarter of a line at a time, the picture was ` +
            `never put straight after the text. The steps: ${describeSteps(down)}.`,
    ).toBeDefined();
    const caught = down.slice(run!.first, run!.last + 1);
    const after = down.slice(run!.last + 1);
    expect(
        down.every(
            (s) => s.pointerTopInBlock + s.picture.height / 2 < s.block.height,
        ),
        "The pointer left the block during the drag, so this did not show that the place " +
            "straight after the text can be reached from inside the block.",
    ).toBe(true);
    // Straight after the text, the picture is centred and stays where it is while the pointer
    // goes on for about a line.
    expect(
        Math.max(...caught.map((s) => s.pictureTopInBlock)) -
            Math.min(...caught.map((s) => s.pictureTopInBlock)),
        `The picture moved while it was straight after the text. The steps: ${describeSteps(down)}.`,
    ).toBeLessThanOrEqual(SAME_PLACE_PX);
    expect(
        Math.abs(
            caught[0].picture.x +
                caught[0].picture.width / 2 -
                (caught[0].block.x + caught[0].block.width / 2),
        ),
        "Straight after the text, the picture should be centred in the block.",
    ).toBeLessThanOrEqual(2);
    const heldForPx = caught.length * stepPx;
    expect(
        heldForPx >= line / 2 && heldForPx <= line * 1.5 + stepPx,
        `The picture stayed straight after the text for ${heldForPx}px of dragging, which is ` +
            `${(heldForPx / line).toFixed(1)} lines; it should be about one line. The steps: ` +
            `${describeSteps(down)}.`,
    ).toBe(true);
    // Dragged further, it follows the pointer again.
    expect(
        after.length === 3 &&
            after.every(
                (s) =>
                    s.dock === "middle" &&
                    s.pictureTopInBlock > caught[0].pictureTopInBlock &&
                    Math.abs(s.pictureTopInBlock - s.pointerTopInBlock) <= line,
            ),
        `Dragged further down, the picture should follow the pointer as a middle picture. The ` +
            `steps: ${describeSteps(down)}.`,
    ).toBe(true);

    let up: IInlineImageDragStep[] = [];
    await step(
        "Drag it back up to just under the text, and let go",
        async () => {
            up = await dragInlineImageInSteps(
                page,
                BLOCK,
                LANG,
                id,
                -stepPx,
                (steps) => steps[steps.length - 1].dock === "bottom",
                20,
            );
        },
    );
    expect(
        up[up.length - 1].dock,
        `Dragged back up to just under the text, the picture was not put straight after the ` +
            `text again. The steps: ${describeSteps(up)}.`,
    ).toBe("bottom");
    await expect
        .poll(
            async () =>
                (await getInlineImageInEveryLanguage(page, BLOCK, id)).filter(
                    (copy) => copy.dock !== "bottom",
                ).length,
            {
                timeout: 30000,
                message:
                    "After the picture was let go straight after the text, every language's " +
                    "copy should be there.",
            },
        )
        .toBe(0);

    const topBefore = (await getInlineImageRects(page, BLOCK, LANG, id)).picture
        .y;
    const blockTopBefore = (await getInlineImageRects(page, BLOCK, LANG, id))
        .block.y;
    await step("Type more text", async () => {
        await putCaretAtEndOfBlockText(page, BLOCK, LANG);
        await typeAtCaretInBlock(
            page,
            BLOCK,
            LANG,
            " A moment later it is back on the branch with a fish, and the water is still " +
                "again. The children on the bank have learned to wait as well, and they do " +
                "not talk while the bird is fishing.",
        );
    });
    const afterTyping = await getInlineImageRects(page, BLOCK, LANG, id);
    expect(
        afterTyping.picture.y - afterTyping.block.y,
        "More text was typed above a picture that sits straight after the text, and the " +
            "picture did not move down with the end of the text.",
    ).toBeGreaterThan(topBefore - blockTopBefore + line / 2);
    expect((await getInlineImage(page, BLOCK, LANG, id)).dock).toBe("bottom");

    await step("Drag it on down to the bottom of the block", async () => {
        await dragInlineImageToBottomOfBlock(page, BLOCK, LANG, id, "middle");
    });
    await expectAtBottomOfBlock(
        page,
        id,
        "middle",
        "Dragged on down from straight after the text to the bottom of the block",
    );
});

test("a picture whose middle is dragged below the block is put centred straight after the text, and dragged back up it follows the pointer again [Test Case ID 815]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    let id = "";
    await step(
        "Make a page with a few lines of text and a picture",
        async () => {
            await makeTextPage(page, TEXT);
            id = await addPicture(page);
            await scrollBlockToTop(page, BLOCK, LANG);
        },
    );
    await step(
        "Drag the picture until its middle is below the block",
        async () => {
            await dragInlineImageToDock(page, BLOCK, LANG, id, "bottom");
        },
    );
    const rects = await getInlineImageRects(page, BLOCK, LANG, id);
    expect(
        Math.abs(
            rects.picture.x +
                rects.picture.width / 2 -
                (rects.block.x + rects.block.width / 2),
        ),
        "After the drag below the block, the picture should be centred in the block.",
    ).toBeLessThanOrEqual(2);
    // The text is a few lines, so straight after it is in the upper half of a Just Text page's
    // block, well above where the pointer let go.
    expect(
        rects.picture.y - rects.block.y,
        "After the drag below the block, the picture should sit straight after the text, near " +
            "the top of the block, not at the bottom where it was let go.",
    ).toBeLessThan(rects.block.height / 2);
    expect(
        await getRoomBelowInlineImagePx(page, BLOCK, LANG, id),
        "Text was left below the picture, so it is not straight after the text.",
    ).toBeGreaterThanOrEqual(0);

    const line = await lineHeightOnScreen(page);
    const stepPx = Math.max(3, Math.round(line / 4));
    let up: IInlineImageDragStep[] = [];
    await step(
        "Drag it back up into the text a quarter of a line at a time",
        async () => {
            up = await dragInlineImageInSteps(
                page,
                BLOCK,
                LANG,
                id,
                -stepPx,
                (steps) =>
                    steps.length >= 3 &&
                    steps.slice(-3).every((s) => s.dock !== "bottom"),
                40,
            );
        },
    );
    const last = up[up.length - 1];
    expect(
        last.dock === "middle" &&
            Math.abs(last.pictureTopInBlock - last.pointerTopInBlock) <= line,
        `Dragged back up into the block, the picture should follow the pointer as a middle ` +
            `picture. The steps: ${describeSteps(up)}.`,
    ).toBe(true);
    expect(
        last.pictureTopInBlock,
        `Dragged back up, the picture should have moved up from straight after the text. The ` +
            `steps: ${describeSteps(up)}.`,
    ).toBeLessThan(rects.picture.y - rects.block.y - SAME_PLACE_PX);
});

test("beside a picture that hangs below the text, a picture dragged down the middle is put below that picture, not just under the text [Test Case ID 815]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    let hanging = "";
    let dragged = "";
    await step(
        "Make a page with one line of text and a picture on the left",
        async () => {
            await makeTextPage(page, ONE_LINE);
            hanging = await addPicture(page);
            await scrollBlockToTop(page, BLOCK, LANG);
            await dragInlineImageToDock(page, BLOCK, LANG, hanging, "left");
            dragged = await addPicture(page);
        },
    );
    const line = await lineHeightOnScreen(page);
    const stepPx = Math.max(3, Math.round(line / 4));
    const hangingBefore = await getInlineImageRects(page, BLOCK, LANG, hanging);
    const hangingBottomInBlock =
        bottom(hangingBefore.picture) - hangingBefore.block.y;
    expect(
        hangingBottomInBlock,
        "The left picture should reach more than two lines down the block, well below the one " +
            "line of text, or this test cannot tell the two places apart.",
    ).toBeGreaterThan(line * 3);

    let steps: IInlineImageDragStep[] = [];
    await step(
        "Drag the second picture to the middle and down a quarter of a line at a time",
        async () => {
            steps = await dragInlineImageInSteps(
                page,
                BLOCK,
                LANG,
                dragged,
                stepPx,
                (soFar) => soFar[soFar.length - 1].dock === "bottom",
                120,
                "middle",
            );
        },
    );
    const last = steps[steps.length - 1];
    expect(
        last.dock,
        `Dragged down the middle of the block, the second picture was never put after the ` +
            `text. The steps: ${describeSteps(steps)}.`,
    ).toBe("bottom");
    expect(
        last.pointerTopInBlock,
        `The second picture was put after the text when its top was dragged to ` +
            `${Math.round(last.pointerTopInBlock)}px down the block, but the left picture ` +
            `reaches ${Math.round(hangingBottomInBlock)}px down. That is just under the text, ` +
            `beside the left picture. The steps: ${describeSteps(steps)}.`,
    ).toBeGreaterThanOrEqual(hangingBottomInBlock - SAME_PLACE_PX);
    expect(
        last.pointerTopInBlock,
        `The second picture was only put after the text when its top was dragged to ` +
            `${Math.round(last.pointerTopInBlock)}px down the block, more than a line and a ` +
            `half below the bottom of the left picture (${Math.round(hangingBottomInBlock)}px).`,
    ).toBeLessThanOrEqual(hangingBottomInBlock + line * 1.5 + stepPx);

    const hangingAfter = await getInlineImageRects(page, BLOCK, LANG, hanging);
    const draggedAfter = await getInlineImageRects(page, BLOCK, LANG, dragged);
    expect(
        Math.abs(
            hangingAfter.picture.y -
                hangingAfter.block.y -
                (hangingBefore.picture.y - hangingBefore.block.y),
        ),
        "The drag moved the left picture.",
    ).toBeLessThanOrEqual(SAME_PLACE_PX);
    expect(
        draggedAfter.picture.y,
        "The second picture should sit below the left picture.",
    ).toBeGreaterThanOrEqual(bottom(hangingAfter.picture) - SAME_PLACE_PX);
    expect(
        Math.abs(
            draggedAfter.picture.x +
                draggedAfter.picture.width / 2 -
                (draggedAfter.block.x + draggedAfter.block.width / 2),
        ),
        "The second picture should be centred in the block.",
    ).toBeLessThanOrEqual(2);
});

test("a picture dragged down the middle is put after a picture already straight after the text, and the upper of the two stays put for about a line of dragging [Test Case ID 815]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    let first = "";
    let second = "";
    await step(
        "Make a page with a few lines of text, a picture straight after them, and a second picture",
        async () => {
            await makeTextPage(page, TEXT);
            // Both pictures go in before the first is moved: Insert Image is on the menu of the
            // text, and the drag below the block can scroll the text out of sight.
            first = await addPicture(page);
            second = await addPicture(page);
            await scrollBlockToTop(page, BLOCK, LANG);
            await dragInlineImageToDock(page, BLOCK, LANG, first, "bottom");
        },
    );
    const line = await lineHeightOnScreen(page);
    const stepPx = Math.max(3, Math.round(line / 4));
    let steps: IInlineImageDragStep[] = [];
    await step(
        "Drag the second picture to the middle and down a quarter of a line at a time",
        async () => {
            steps = await dragInlineImageInSteps(
                page,
                BLOCK,
                LANG,
                second,
                stepPx,
                (soFar) => soFar[soFar.length - 1].dock === "bottom",
                160,
                "middle",
            );
        },
    );
    const last = steps[steps.length - 1];
    expect(
        last.dock,
        `Dragged down the middle of the block, the second picture was never put after the ` +
            `first. The steps: ${describeSteps(steps)}.`,
    ).toBe("bottom");
    // The first picture is measured after the drag. Until the second picture was put after it,
    // the second picture hung beside or above it and held it lower down.
    const firstAfter = await getInlineImageRects(page, BLOCK, LANG, first);
    const secondAfter = await getInlineImageRects(page, BLOCK, LANG, second);
    const firstBottomInBlock = bottom(firstAfter.picture) - firstAfter.block.y;
    expect(
        last.pointerTopInBlock >= firstBottomInBlock - SAME_PLACE_PX &&
            last.pointerTopInBlock <= firstBottomInBlock + line * 1.5 + stepPx,
        `The second picture was put after the text when its top was dragged to ` +
            `${Math.round(last.pointerTopInBlock)}px down the block. That should happen when ` +
            `its top is just under the first picture, whose bottom is ` +
            `${Math.round(firstBottomInBlock)}px down. The steps: ${describeSteps(steps)}.`,
    ).toBe(true);
    expect(
        steps.length,
        "The second picture was put after the first at the first step of the drag, so this " +
            "did not show that it moves as a middle picture until its top reaches that place.",
    ).toBeGreaterThan(1);
    expect(
        secondAfter.picture.y,
        "The second picture should sit below the first, which was there before it.",
    ).toBeGreaterThanOrEqual(bottom(firstAfter.picture) - SAME_PLACE_PX);
    expect(
        (await getInlineImage(page, BLOCK, LANG, second)).slot.index,
        "The second picture should come after the first in the block.",
    ).toBeGreaterThan(
        (await getInlineImage(page, BLOCK, LANG, first)).slot.index,
    );

    let upper: IInlineImageDragStep[] = [];
    await step(
        "Drag the upper picture down a quarter of a line at a time",
        async () => {
            upper = await dragInlineImageInSteps(
                page,
                BLOCK,
                LANG,
                first,
                stepPx,
                (soFar) =>
                    soFar.length >= 3 &&
                    soFar.slice(-3).every((s) => s.dock !== "bottom"),
                40,
            );
        },
    );
    const startTop = firstAfter.picture.y - firstAfter.block.y;
    const held = upper.findIndex((s) => s.dock !== "bottom");
    expect(
        held,
        `The upper picture never left its place straight after the text. The steps: ` +
            `${describeSteps(upper)}.`,
    ).toBeGreaterThan(-1);
    const heldForPx = held * stepPx;
    expect(
        heldForPx >= line / 2 && heldForPx <= line * 1.5 + stepPx,
        `The upper picture stayed where it was for ${heldForPx}px of dragging, which is ` +
            `${(heldForPx / line).toFixed(1)} lines; it should be about one line. The steps: ` +
            `${describeSteps(upper)}.`,
    ).toBe(true);
    expect(
        upper
            .slice(0, held)
            .every(
                (s) =>
                    Math.abs(s.pictureTopInBlock - startTop) <= SAME_PLACE_PX,
            ),
        `The upper picture moved before it left its place straight after the text. It began ` +
            `${Math.round(startTop)}px down the block. The steps: ${describeSteps(upper)}.`,
    ).toBe(true);
    const end = upper[upper.length - 1];
    expect(
        end.dock === "middle" &&
            Math.abs(end.pictureTopInBlock - end.pointerTopInBlock) <= line,
        `Dragged further, the upper picture should follow the pointer as a middle picture. The ` +
            `steps: ${describeSteps(upper)}.`,
    ).toBe(true);
});
