// What the flow does to paragraphs, which is the shape an author's text actually has.
//
// Moving text from one box to the next is not moving characters: a paragraph the move cuts in half
// has to read as ONE paragraph to a reader, across the join, so the piece that lands at the top of
// the next box is marked (data-flow-continuation) and drawn with no indent and no gap above it.
// When the text comes back, the two pieces have to become one paragraph again, with the mark gone.
// And where the cut happens to fall between two paragraphs, nothing is being carried on, so no mark
// belongs there at all.
//
// These are the cases the rest of the suite cannot see. Every spec's run of text is made of
// paragraphs now (kParagraphsForSeveralPages), but the readings the other specs make are of the
// text, and text alone cannot tell a paragraph that was split from two paragraphs that were always
// two. assertParagraphsAreIntact is the reading that can.
//
// The Test Case IDs in the titles are marked TBD: this feature has no rows in the Notion test
// inventory yet, and the ids are allocated there when it lands, not by this file.
//
// The tests are serial because each one starts from the book the one before it left behind.

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { goToPage, makeBookFromTemplate } from "../helpers/bookMaking";
import {
    addJustTextPage,
    assertParagraphsAreIntact,
    assertRunIsIntact,
    buildLongText,
    clearBox,
    clickContinueText,
    enableFlowTextFeature,
    getBoxParagraphs,
    getRunParagraphs,
    hasOverflowMarker,
    isContinueButtonShown,
    kFlowTextCollection,
    kFlowTextFeatures,
    kParagraphsForSeveralPages,
    kTextForSeveralPages,
    makeTwoBoxFlowPage,
    setFontSizeOfBox,
    splitIntoParagraphs,
    typeParagraphs,
    waitForReflowIdle,
} from "../helpers/flowText";

// The same collection object as every other flow-text spec, which is what lets all of them
// run on one Bloom rather than one each. kFlowTextCollection says how that works and why a
// test here cannot be disturbed by the file before it.
test.use({
    collectionSpec: kFlowTextCollection,
    experimentalFeatures: kFlowTextFeatures,
});

// Flow text needs a paid subscription as well as the feature token above. Without it Bloom does
// not offer the feature at all and every test here fails at once; see kFlowTextCollection.
test.beforeAll(async ({ bloomApp }) => {
    await enableFlowTextFeature(bloomApp.page);
});

test.describe.configure({ mode: "serial" });

/**
 * Three paragraphs of indexed words, weighted 3:1:1. The first alone is more than the top box of a
 * two-box page holds — a little more than kTextTooLongForOneBox, the size calibrated for exactly
 * that — so the flow has to cut inside the FIRST paragraph, which is the case these tests start
 * from. The other two are short so that the cut paragraph has room to come back whole when the
 * text is drawn smaller, which is what the second test needs.
 */
const kThreeParagraphs = ((fifths: string[]) => [
    fifths.slice(0, 3).join(" "),
    fifths[3],
    fifths[4],
])(splitIntoParagraphs(buildLongText(1200), 5));

/**
 * The size the second test draws the text at to bring the whole run back into one box, and the
 * size the third puts it back to. 12 pt is what a new Basic Book's normal style already is, and 7
 * is the smallest the Format dialog offers: it has a fixed list (7, 8, 9, 10, 11, 12, 13, 14, 16,
 * 18 and up), so a test that simply halves the size it finds asks for a 6 that is not there.
 *
 * At 7 pt against 12 the text needs about a third of the room, which is enough for the paragraph
 * that was cut to fit in the top box again. It is not enough for the whole run: the cut moves
 * along to a later paragraph rather than going away, which is what the second test says.
 */
const kSmallFontSize = 7;
const kNormalFontSize = 12;

// The page whose two boxes the first tests flow between.
let twoBoxPageId: string;

/**
 * Take the lower box's offer to carry on the text of the box above, when it is making one. Text
 * that no longer overflows takes the lower box out of the chain, so a test that puts a long run
 * back in has to join the two boxes again; one that only changed the text of an already-linked
 * pair has nothing to do here.
 */
async function linkTheBoxesIfNeeded(page: Page): Promise<void> {
    if (await isContinueButtonShown(page, 1)) await clickContinueText(page, 1);
}

test.describe("paragraphs that a flow cuts in half and joins again", () => {
    test("a paragraph cut at a box boundary carries on in the next box [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        twoBoxPageId = await makeTwoBoxFlowPage(page);

        // THE ACTION UNDER TEST: put in three paragraphs, of which the first alone is more than
        // the top box holds, and take the offer to carry on in the box below.
        await typeParagraphs(page, 0, kThreeParagraphs);
        expect(
            await hasOverflowMarker(page, 0),
            "The first paragraph has to be more than the top box holds, or nothing is cut.",
        ).toBe(true);
        await clickContinueText(page, 1);

        const top = await getBoxParagraphs(page, 0);
        const bottom = await getBoxParagraphs(page, 1);
        expect(
            top.length,
            "The top box has to keep the beginning of the first paragraph and nothing else.",
        ).toBe(1);
        expect(
            top[0].isContinuation,
            "The first paragraph of the whole run carries nothing on.",
        ).toBe(false);
        expect(
            bottom[0].isContinuation,
            "The piece that lands in the next box is the rest of a paragraph, so it has to say " +
                "so: without the mark a reader sees a new paragraph, indented, in the middle of " +
                "a sentence.",
        ).toBe(true);

        // Nothing else is marked: the paragraphs that arrived whole are not continuations of
        // anything, whichever box they ended up in.
        expect(
            bottom.slice(1).map((paragraph) => paragraph.isContinuation),
            "Only the first paragraph of a box can be carrying one on.",
        ).toEqual(bottom.slice(1).map(() => false));

        assertParagraphsAreIntact([top, bottom], kThreeParagraphs);
    });

    test("the two halves become one paragraph again when the text fits [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await goToPage(page, twoBoxPageId);
        await waitForReflowIdle(page);
        // Sanity check the start state: the split this test undoes is still there.
        expect(
            (await getBoxParagraphs(page, 1))[0].isContinuation,
            "This test undoes a split paragraph, so there has to be one to undo.",
        ).toBe(true);

        // THE ACTION UNDER TEST: draw the text small enough that the paragraph that was cut fits
        // in the top box again. The half in the box below has to come back and join the half it
        // was cut from, rather than staying a paragraph of its own.
        await setFontSizeOfBox(page, 0, kSmallFontSize);

        const top = await getBoxParagraphs(page, 0);
        const bottom = await getBoxParagraphs(page, 1);
        expect(
            top[0].text.replace(/\s+/g, " ").trim(),
            "The paragraph that was cut in half has to be back in one piece, in the box it " +
                "started in.",
        ).toBe(kThreeParagraphs[0].replace(/\s+/g, " ").trim());
        // And none of it is left behind in the box below. That is the whole of the rejoin: the
        // words that had been carried on are back with the words they were cut from.
        //
        // The box below is not expected to be EMPTY, and the run is not expected to be free of
        // marks. Drawing the text smaller moves the cut along the run rather than doing away with
        // it: the top box now holds the first paragraph and as much of the next as fits, so a mark
        // still sits where the cut now is. assertParagraphsAreIntact is what checks that the mark
        // is in the right place, wherever that has become.
        const wordsOfTheFirstParagraph: string[] =
            kThreeParagraphs[0].match(/w\d{4}/g) ?? [];
        const wordsBelow: string[] =
            bottom
                .map((paragraph) => paragraph.text)
                .join(" ")
                .match(/w\d{4}/g) ?? [];
        const wordsLeftBelow = wordsBelow.filter((word) =>
            wordsOfTheFirstParagraph.includes(word),
        );
        expect(
            wordsLeftBelow,
            "Words of the paragraph that was cut are still in the box below, so the two halves " +
                "did not come back together.",
        ).toEqual([]);
        assertParagraphsAreIntact([top, bottom], kThreeParagraphs);
    });

    test("a cut that falls between two paragraphs marks nothing [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        // A page of its own, with two empty boxes. The page the tests above left behind has text
        // in its lower box, and what goes in below replaces only the text of the TOP box: the run
        // would be whatever this test typed plus whatever was already carried on, which is not a
        // run this test knows the paragraphs of.
        const measuringPageId = await makeTwoBoxFlowPage(page);
        await goToPage(page, measuringPageId);
        await waitForReflowIdle(page);
        // Back to the size the book was drawn at before the test above shrank it: the style
        // belongs to the whole book, so a new page is drawn at whatever that test left.
        await setFontSizeOfBox(page, 0, kNormalFontSize);

        // Arranging this case means knowing where the top box runs out, which no test can know
        // in advance: it depends on the page size, the font and the machine's text rendering. So
        // the box is asked. One paragraph goes in, long enough to be cut; what the top box keeps
        // of it is exactly what fits, and that becomes the first paragraph of the real text.
        await typeParagraphs(page, 0, [kTextForSeveralPages]);
        await linkTheBoxesIfNeeded(page);
        let firstParagraph = (await getBoxParagraphs(page, 0))[0].text.trim();
        const secondParagraph = kParagraphsForSeveralPages[0];

        // The measuring is not exact to the word — the box may take one more word once the text no
        // longer has to leave room for a cut — so the arrangement is checked and the first
        // paragraph shortened by a few words until it holds. Five attempts is far more than the
        // one or two this needs; the message says what could not be arranged rather than leaving
        // the real assertion below to fail obscurely.
        // Whether it holds is read from the TEXT, never from the mark: the mark is what this test
        // exists to check, so arranging the case by it would prove nothing.
        const sameWords = (a: string, b: string) =>
            a.replace(/\s+/g, " ").trim() === b.replace(/\s+/g, " ").trim();
        let arranged = false;
        for (let attempt = 0; attempt < 5 && !arranged; attempt++) {
            // The lower box is emptied first. Typing replaces the text of the TOP box only, so
            // whatever the measuring step carried down there would still be sitting under the new
            // text, and the run would be the two paragraphs this test typed followed by the tail
            // of the one it measured with.
            await clearBox(page, 1);
            await typeParagraphs(page, 0, [firstParagraph, secondParagraph]);
            await linkTheBoxesIfNeeded(page);
            const top = await getBoxParagraphs(page, 0);
            const bottom = await getBoxParagraphs(page, 1);
            // The top box holds the whole of the first paragraph and no word of the second, and
            // the second has reached the box below: the cut fell exactly between them.
            arranged =
                top.length === 1 &&
                sameWords(top[0].text, firstParagraph) &&
                bottom.length > 0;
            if (!arranged)
                firstParagraph = firstParagraph
                    .split(" ")
                    .slice(0, -3)
                    .join(" ");
        }
        expect(
            arranged,
            "This test needs the top box to end exactly where the first paragraph ends, and " +
                "trimming the first paragraph never got it there.",
        ).toBe(true);

        // THE ASSERTION THIS TEST EXISTS FOR: the cut fell between two paragraphs, so the box
        // below begins a paragraph of its own. Marking it as carrying one on would run two
        // separate paragraphs together in the reader's eye.
        const bottom = await getBoxParagraphs(page, 1);
        expect(
            bottom.length,
            "The second paragraph has to have gone to the box below for this to mean anything.",
        ).toBeGreaterThan(0);
        expect(
            bottom[0].isContinuation,
            "Nothing was cut in half here, so nothing is carried on.",
        ).toBe(false);
        assertParagraphsAreIntact(
            [await getBoxParagraphs(page, 0), bottom],
            [firstParagraph, secondParagraph],
        );
    });
});

test.describe("paragraphs that a flow carries across pages", () => {
    test("a paragraph cut at a page boundary carries on on the next page [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        // A book of its own: this is about the boxes C# moves text between, which are on
        // different pages and never in the browser together.
        await makeBookFromTemplate(page, "Basic Book");
        await addJustTextPage(page);
        await typeParagraphs(page, 0, kParagraphsForSeveralPages);
        await addJustTextPage(page);
        await clickContinueText(page, 0);
        await addJustTextPage(page);
        await clickContinueText(page, 0);

        const perBox = await getRunParagraphs(page);
        expect(
            perBox.length,
            "The run is sized to need three pages, so it has to be spread over three boxes.",
        ).toBe(3);

        // Every join between two pages either lands inside a paragraph, and is marked, or lands
        // between two, and is not. Both are right; what would be wrong is a piece of a paragraph
        // arriving unmarked, and assertParagraphsAreIntact is what catches that: it rebuilds the
        // paragraphs the marks claim and compares them with the ones that went in.
        assertParagraphsAreIntact(perBox, kParagraphsForSeveralPages);
        assertRunIsIntact(
            perBox.map((box) =>
                box.map((paragraph) => paragraph.text).join(" "),
            ),
            kTextForSeveralPages,
        );

        // At least one of the two joins has to have fallen inside a paragraph, or this test is
        // quietly passing without exercising a cut at all. The run is six paragraphs over three
        // pages, so the odds of both joins landing exactly on a paragraph end are small; if this
        // ever fails, the run's paragraph count is what to change.
        expect(
            perBox.some((box) => box[0]?.isContinuation),
            "No page of the run begins with the rest of a paragraph, so this test did not " +
                "exercise a cut. Change the number of paragraphs the run is made of.",
        ).toBe(true);
    });
});
