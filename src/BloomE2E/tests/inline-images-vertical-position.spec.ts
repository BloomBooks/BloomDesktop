// How far down its block an inline image can be put.
//
// WHAT THIS IS ABOUT. A person moving a picture down a block expects to be able to stop
// anywhere: with six lines above it, or seven, or with one last line below it. The docks are
// the only thing that is supposed to be quantized -- left, right, the full-width band, and
// below the text -- and how far down the band sits is a distance in pixels, not a choice from
// a list. What made this a test is the report that it was a choice from a list: the picture
// could go at the bottom, or about five lines higher, and nowhere in between (John, live
// testing, with the ball picture in a full A6 block).
//
// WHY IT IS ITS OWN FILE. inline-images.spec.ts checks that each dock can be reached, one drag
// per dock, which is a different question from what the space between two docks offers. This
// one takes the picture to the far end of the block and then steps it down a line at a time, so
// it needs a block whose text it knows and a page it does not share.
//
// WHY IT DOES NOT STEP THE WHOLE WAY DOWN. It used to, and a full A5 block is twenty-odd lines
// of one drag each, which is most of a minute for the sake of the last two or three of them.
// Every position in the middle of the block was reached by the same arithmetic and told us the
// same thing. So one drag covers the distance -- which is also the gesture a person makes -- and
// the stepping is spent only where the answer is in doubt: the few lines at the end, where the
// bottom dock takes over and where the reported bug had the band giving out early.

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

    // The full-width band is the dock this is about: it is the one whose position down the
    // block is a distance rather than a side.
    await scrollBlockToTop(page, BLOCK, LANG);
    await dragInlineImageToDock(page, BLOCK, LANG, imageId, "middle");

    const lineHeightPx = await getBlockLineHeightPx(page, BLOCK, LANG);
    expect(
        lineHeightPx,
        "The block reports no line height, so this test cannot say what a line's worth of room is.",
    ).toBeGreaterThan(0);

    // THE ACTION UNDER TEST, in two parts. First one drag that carries the picture down to
    // within a few lines of the end of the text: a person moves a picture in one gesture, and a
    // band that cannot be dragged this far down at all fails here rather than after twenty
    // nudges. kLinesToStep is how much is left for the stepping to cover.
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

    // Then step it down a line at a time until the bottom dock takes over, remembering the
    // lowest band position it reached. One line is the granularity the assertion below is in, so
    // there is nothing to be had from finer steps. The limit is generous enough to cover a block
    // whose text rewraps as the picture moves through it, and small enough to fail rather than
    // grind if the band stops moving.
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

    // The picture ran out of block before it ran out of positions: less than one line of the
    // block's content is left below it. More than that is text the person could not get the
    // picture past, which is the reported bug -- the band stopped several lines short and the
    // next thing a drag offered was the bottom dock.
    expect(
        deepestBandRoomBelowPx,
        `The lowest place the band would go left ${Math.round(deepestBandRoomBelowPx)}px of the ` +
            `block below the picture, which is ${(deepestBandRoomBelowPx / lineHeightPx).toFixed(1)} ` +
            `lines of text the picture cannot be put after. The last few of the ${positions.length} ` +
            `places the sweep stopped at: ${positions.slice(-6).join("; ")}.`,
    ).toBeLessThan(lineHeightPx);
});
