// Checks how far down its block an inline image can be moved.
//
// Someone moving a picture down a block expects to be able to stop at any line: with six lines
// above it, or seven, or with one last line below it. Only the choice of dock (left, right, the
// full-width band, or below the text) is meant to come from a fixed set; how far down the band
// sits is a distance in pixels. John found, while testing the ball picture in a full A6 block,
// that the picture could go at the bottom or about five lines higher and nowhere in between.
//
// This is a separate file from inline-images.spec.ts because that file drags once to each dock
// to check it can be reached. This test asks what positions are available between two docks: it
// takes the picture to the far end of a block and then moves it down one line at a time, so it
// needs a block whose text it knows and a page of its own.
//
// It does not step a line at a time the whole way down. A full A5 block is more than twenty
// lines, which would take most of a minute, and every position in the middle of the block is
// reached by the same arithmetic. So one drag covers most of the distance, as a person would do
// it, and the single-line steps are used only for the last few lines, where the bottom dock
// takes over and where the reported bug stopped the band too early.

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
    getBlockLineHeightPx,
    getRoomBelowInlineImagePx,
    nudgeInlineImageDown,
    scrollBlockToTop,
} from "../helpers/inlineImages";

test.use({
    collectionSpec: {
        name: "inline-images-vertical-position",
        languages: ["en"],
    },
});

test.describe.configure({ mode: "serial" });

const BLOCK = ".bloom-translationGroup";
const LANG = "en";

// Enough text that the block has lines all the way down it, so that "how far down can the
// picture go" is a question with a visible answer at every step.
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

test("the picture can be put right down to the end of the block's text, not just in its upper part [Test Case ID 815]", async ({
    page,
}) => {
    test.setTimeout(300000);
    await makeBookFromTemplate(page, "Basic Book");
    await typeInGroup(page, ".bookTitle", "en", "Inline Images Vertical");
    await addPage(page, "Just Text");
    const [contentPage] = await getContentPages(page);
    await goToPage(page, contentPage.id);
    await typeInGroup(page, BLOCK, LANG, TEXT);

    const imageId = await addInlineImage(page, BLOCK, LANG);
    await changeInlineImagePicture(
        page,
        BLOCK,
        LANG,
        imageId,
        fixtureImage("bird.png"),
    );

    // This test is about the full-width band, because it is the dock whose position is a
    // distance down the block instead of a side.
    await scrollBlockToTop(page, BLOCK, LANG);
    await dragInlineImageToDock(page, BLOCK, LANG, imageId, "middle");

    const lineHeightPx = await getBlockLineHeightPx(page, BLOCK, LANG);
    expect(
        lineHeightPx,
        "The block reports no line height, so this test cannot say what a line's worth of room is.",
    ).toBeGreaterThan(0);

    // The action under test has two parts. First, one drag carries the picture down to within a
    // few lines of the end of the text, the way a person moves a picture in one gesture. If the
    // band cannot be dragged this far at all, the test fails here instead of after twenty
    // nudges. kLinesToStep is the number of lines left for the single-line steps to cover.
    const kLinesToStep = 4;
    const roomAtTopPx = await getRoomBelowInlineImagePx(
        page,
        BLOCK,
        LANG,
        imageId,
    );
    if (roomAtTopPx > kLinesToStep * lineHeightPx)
        await dragInlineImageDown(
            page,
            BLOCK,
            LANG,
            imageId,
            Math.round(roomAtTopPx - kLinesToStep * lineHeightPx),
        );

    // Then move it down one line at a time until the bottom dock takes over, keeping track of
    // the lowest position the band reached. The assertion below is measured in lines, so smaller
    // steps would tell us nothing more. The step limit allows for text that rewraps as the
    // picture moves through it, and is small enough that the loop ends quickly if the band stops
    // moving.
    let deepestBandRoomBelowPx = await getRoomBelowInlineImagePx(
        page,
        BLOCK,
        LANG,
        imageId,
    );
    const positions: string[] = [];
    for (let step = 1; step <= kLinesToStep + 4; step++) {
        const at = await nudgeInlineImageDown(
            page,
            BLOCK,
            LANG,
            imageId,
            Math.round(lineHeightPx),
        );
        positions.push(
            `${at.dock} offset ${at.offsetPx} room below ${Math.round(at.roomBelowPx)}`,
        );
        if (at.dock === "bottom") break;
        if (at.dock === "middle")
            deepestBandRoomBelowPx = Math.min(
                deepestBandRoomBelowPx,
                at.roomBelowPx,
            );
    }

    // At its lowest band position, less than one line of the block's content should be below
    // the picture. More than a line means there is text the person cannot move the picture past.
    // That was the reported bug: the band stopped several lines short of the end, and the next
    // position a drag offered was the bottom dock.
    expect(
        deepestBandRoomBelowPx,
        `The lowest place the band would go left ${Math.round(deepestBandRoomBelowPx)}px of the ` +
            `block below the picture, which is ${(deepestBandRoomBelowPx / lineHeightPx).toFixed(1)} ` +
            `lines of text the picture cannot be put after. The last few of the ${positions.length} ` +
            `places the sweep stopped at: ${positions.slice(-6).join("; ")}.`,
    ).toBeLessThan(lineHeightPx);
});
