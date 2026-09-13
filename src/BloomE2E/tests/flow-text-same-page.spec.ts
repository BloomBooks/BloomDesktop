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
    getPageLanguages,
    goToPage,
    makeBookFromTemplate,
    setContentLanguages,
} from "../helpers/bookMaking";
import {
    assertRunIsIntact,
    clickContinueText,
    enableFlowTextFeature,
    getBoxTexts,
    getCaretOwner,
    getChainId,
    hasContinuationParagraph,
    hasOverflowMarker,
    hasOverflowWarning,
    isContinueButtonShown,
    kFlowTextCollection,
    kFlowTextFeatures,
    kTextTooLongForOneBox,
    makeTwoBoxFlowPage,
    pressKeyAtEndOfBox,
    pressKeyAtStartOfBox,
    seedTalkingBookMarkup,
    typeParagraphAtEnd,
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

    test("the link and the text survive leaving the page and coming back [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await goToPage(page, flowPageId);
        const chainId = await getChainId(page, 0);

        // THE ACTION UNDER TEST: leave the page and come back. Bloom writes a page when the
        // book leaves it, so this saves and reloads, and a pass runs as the page opens.
        await goToPage(page, otherPageId);
        await goToPage(page, flowPageId);

        expect(await getChainId(page, 0)).toBe(chainId);
        expect(await getChainId(page, 1)).toBe(chainId);
        expect(await hasContinuationParagraph(page, 1)).toBe(true);

        // Every word is still there, and still in order. Which word the split falls on is not
        // part of the claim: the pass measures the text again as the page opens, so a word can
        // sit on the other side of the break than it did before.
        const texts = await getBoxTexts(page);
        expect(texts[0].length).toBeGreaterThan(0);
        expect(texts[1].length).toBeGreaterThan(0);
        assertRunIsIntact(texts, kTextTooLongForOneBox);
    });

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

    test("each language of a bilingual page flows on its own [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        // A page of its own again, and both of the collection's languages showing on it, so that
        // each translation group holds two boxes: one per language.
        const bilingualPageId = await makeTwoBoxFlowPage(page);
        await setContentLanguages(page, ["en", "fr"]);
        await goToPage(page, bilingualPageId);
        await waitForReflowIdle(page);
        const languages = await getPageLanguages(page);
        expect(
            languages,
            "This test addresses one language's boxes at a time, so the page has to be showing " +
                "both, in this order.",
        ).toEqual(["en", "fr"]);

        // THE ACTION UNDER TEST, once per language: text that overflows the top box of a group is
        // carried into the box below it — the box below it IN THAT LANGUAGE.
        await typeParagraphAtEnd(page, 0, kTextTooLongForOneBox, "en");
        expect(
            await isContinueButtonShown(page, 1, "en"),
            "The first language's lower box has to offer to carry its own language's text on.",
        ).toBe(true);
        expect(
            await isContinueButtonShown(page, 1, "fr"),
            "The second language's boxes are empty and nothing of theirs overflows, so nothing " +
                "is offered there.",
        ).toBe(false);
        await clickContinueText(page, 1, "en");

        const firstLanguageAfter = await getBoxTexts(page, "en");
        expect(
            await getBoxTexts(page, "fr"),
            "Flowing one language must not write in the other language's boxes.",
        ).toEqual(["", ""]);

        // Now the second language, whose text is its own words so that a word in the wrong box can
        // be named. It is three times what the first language needed: a bilingual page divides
        // each group between its languages, so neither box is the size kTextTooLongForOneBox was
        // measured against.
        //
        // No offer is taken this time, and none is expected. A chain belongs to the translation
        // GROUP rather than to a box (E2eTestingApi reads the chain id off the group), so taking
        // the offer above joined the two groups for every language they hold. The second
        // language's text has nothing left to ask for: its own boxes are already the boxes of a
        // chain, and what overflows one moves into the next by itself.
        const secondLanguageText = Array.from(
            { length: 360 },
            (_unused, index) => "f" + String(index + 1).padStart(4, "0"),
        ).join(" ");
        await typeParagraphAtEnd(page, 0, secondLanguageText, "fr");
        expect(
            await isContinueButtonShown(page, 1, "fr"),
            "The groups are already chained, so the second language's boxes have nothing to " +
                "offer: its text moves between them without being asked.",
        ).toBe(false);

        const secondLanguageAfter = await getBoxTexts(page, "fr");
        expect(
            secondLanguageAfter[1].length,
            "The second language's text has to have been carried into its own lower box.",
        ).toBeGreaterThan(0);
        assertRunIsIntact(secondLanguageAfter, secondLanguageText);
        expect(
            await getBoxTexts(page, "en"),
            "Flowing the second language must not have moved the first language's text.",
        ).toEqual(firstLanguageAfter);
        expect(
            secondLanguageAfter.join(" ").match(/w\d{4}/g) ?? [],
            "The second language's boxes hold none of the first language's words.",
        ).toEqual([]);
    });
});
