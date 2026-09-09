// Text flowing between two text boxes on ONE page: the offer the empty box makes, what happens
// when it is taken, and what deleting does to text that has already moved.
//
// The page is Basic Book's "Image in Middle": a text box, a picture, and a second text box. Two
// boxes on one page is the rarer case of this feature -- most linked text crosses pages -- but
// it is the case the browser settles by itself, with no help from C#, so it is where the flow
// rules are easiest to pin down.
//
// The Test Case IDs in the titles are marked TBD: this feature has no rows in the Notion test
// inventory yet, and the ids are allocated there when it lands, not by this file.
//
// The tests are serial because each one starts from the page the one before it left behind.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    clickContinueText,
    getBoxTexts,
    getCaretOwner,
    getChainId,
    hasContinuationParagraph,
    hasOverflowMarker,
    hasOverflowWarning,
    isContinueButtonShown,
    kTextTooLongForOneBox,
    makeTwoBoxFlowPage,
    pressKeyAtEndOfBox,
    pressKeyAtStartOfBox,
    seedTalkingBookMarkup,
    typeParagraphAtEnd,
} from "../helpers/flowText";

test.use({
    collectionSpec: { name: "flow-text-same-page", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

// The page every test in this file works on, and a second page to leave it for.
let flowPageId: string;
let otherPageId: string;

/** The whitespace of a box's text is not the point of any of these tests. */
function normalize(text: string): string {
    return text.replace(/\s+/g, " ").trim();
}

test.describe("text flowing between two boxes on one page", () => {
    test("builds a book with a two-box page", async ({ page }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        flowPageId = await makeTwoBoxFlowPage(page);
        // Somewhere to go and come back from, which is how a test saves and reloads a page.
        await addPage(page, "Just Text");
        const contentPages = await getContentPages(page);
        otherPageId = contentPages[contentPages.length - 1].id;
        await goToPage(page, flowPageId);

        // The page starts as two empty boxes, and an empty box with nothing overflowing above
        // it has nothing to offer.
        expect(await getBoxTexts(page)).toEqual(["", ""]);
        expect(await isContinueButtonShown(page, 1)).toBe(false);
    });

    test("the empty box offers to continue an overflowing box, and takes its text [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await goToPage(page, flowPageId);
        await typeParagraphAtEnd(page, 0, kTextTooLongForOneBox);

        // The top box now holds more than fits, and the empty box below it says so.
        expect(await hasOverflowMarker(page, 0)).toBe(true);
        expect(await isContinueButtonShown(page, 1)).toBe(true);

        // THE ACTION UNDER TEST: take the offer.
        await clickContinueText(page, 1);

        // The text is now split across the two boxes, and none of it was lost.
        const texts = await getBoxTexts(page);
        expect(texts[0].length).toBeGreaterThan(0);
        expect(texts[1].length).toBeGreaterThan(0);
        expect(normalize(texts.join(" "))).toBe(
            normalize(kTextTooLongForOneBox),
        );

        // The second half is the same paragraph carried on, not a new one.
        expect(await hasContinuationParagraph(page, 1)).toBe(true);

        // The top box is no longer where the text runs out, so it neither warns nor marks.
        expect(await hasOverflowWarning(page, 0)).toBe(false);
        expect(await hasOverflowMarker(page, 0)).toBe(false);

        // The two boxes are one chain.
        const chainId = await getChainId(page, 0);
        expect(chainId).toBeTruthy();
        expect(await getChainId(page, 1)).toBe(chainId);

        // The caret keeps its place in the text rather than its place on the page: it was
        // after the last character typed, that character is one of the ones that moved, so
        // the caret is in the second box, still after it.
        expect(await getCaretOwner(page)).toBe(1);
    });

    // The chain id and the continuation paragraph do survive the round trip; the text does
    // not. The pass that runs as the page opens measures the text again and moves a word or
    // two forward, and each word it moves arrives with no space in front of it, so the reader
    // sees "usedtostand,thewater". CKEditor writes the paragraph's trailing space as a
    // zero-width filler, and the word that moves carries that filler with it instead of a
    // space. Fixing it means deciding how the flow holds the space at a box boundary, which
    // is a decision about the engine, not about this test. See AUTOMATION-DEBT.md, "A word
    // moved by the pass loses the space in front of it".
    test.fixme(
        "the link and the text survive leaving the page and coming back [Test Case ID TBD]",
        async ({ page }) => {
            test.setTimeout(300000);
            await goToPage(page, flowPageId);
            const chainId = await getChainId(page, 0);

            // Bloom writes a page when the book leaves it, so this saves and reloads.
            await goToPage(page, otherPageId);
            await goToPage(page, flowPageId);

            expect(await getChainId(page, 0)).toBe(chainId);
            expect(await getChainId(page, 1)).toBe(chainId);
            expect(await hasContinuationParagraph(page, 1)).toBe(true);

            // Every word is still there, and still in order. Which word the split falls on
            // is not part of the claim: the pass measures the text again as the page opens,
            // so a word can sit on the other side of the break than it did before.
            const texts = await getBoxTexts(page);
            expect(texts[0].length).toBeGreaterThan(0);
            expect(texts[1].length).toBeGreaterThan(0);
            expect(normalize(texts.join(" "))).toBe(
                normalize(kTextTooLongForOneBox),
            );
        },
    );

    test("backspace at the start of the second box pulls text back [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await goToPage(page, flowPageId);
        const before = await getBoxTexts(page);

        // THE ACTION UNDER TEST: one Backspace at the very start of the box that received
        // the text, which joins it back onto the box above.
        await pressKeyAtStartOfBox(page, 1, "Backspace");

        // One character fewer in the two boxes together: the key reached across the seam and
        // took the last character of the box above. Where the boundary between the boxes
        // falls afterwards is not part of the claim, because the pass measures the text
        // again and can move a word either way.
        const after = await getBoxTexts(page);
        expect(normalize(after.join(" ")).length).toBe(
            normalize(before.join(" ")).length - 1,
        );
    });

    test("delete at the end of the first box takes a character from the second [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await goToPage(page, flowPageId);
        const before = await getBoxTexts(page);

        // THE ACTION UNDER TEST: one Delete at the very end of the first box.
        await pressKeyAtEndOfBox(page, 0, "Delete");

        // One character fewer in the two boxes together: the key reached across the seam and
        // took the first character of the second box. Where the boundary between the boxes
        // falls afterwards is not part of the claim, because the pass measures the text again
        // and can move a word either way.
        const after = await getBoxTexts(page);
        expect(normalize(after.join(" ")).length).toBe(
            normalize(before.join(" ")).length - 1,
        );
    });

    test("a box with talking-book audio offers nothing [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        // A page of its own, because this test needs a box that overflows with an empty box
        // under it, and the page the tests above left behind has its text already shared.
        const audioPageId = await makeTwoBoxFlowPage(page);
        await goToPage(page, audioPageId);
        await typeParagraphAtEnd(page, 0, kTextTooLongForOneBox);
        expect(await isContinueButtonShown(page, 1)).toBe(true);

        // Moving the text of a recorded box would leave the recording pointing at words that
        // are no longer there, so flow refuses such a box.
        await seedTalkingBookMarkup(page, 0);

        expect(await isContinueButtonShown(page, 1)).toBe(false);
    });

    // A bilingual page needs the collection's second language turned on for the book, and each
    // language's second box then flows on its own. setContentLanguages does the first half;
    // what is missing is a helper that says which languages a PAGE is showing, so the test can
    // address each language's boxes without guessing which tags reached the page. See
    // AUTOMATION-DEBT.md, "No helper reports the languages a page is showing".
    test.fixme(
        "each language of a bilingual page flows on its own [Test Case ID TBD]",
        async () => {
            // Blocked: see the comment above.
        },
    );
});
