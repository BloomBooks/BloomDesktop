// Editing a run of text that already crosses pages: the text moves on when a page has too much
// and comes back when a page has room, and the caret goes where the person's own words went.
//
// Two "Just Text" pages, linked, with more text than the first one holds. The second page's box
// is not in the browser while the first page is being edited, so every move here goes through
// C# (FlowTextApi) rather than through the browser alone.
//
// The Test Case IDs in the titles are marked TBD: this feature has no rows in the Notion test
// inventory yet, and the ids are allocated there when it lands, not by this file.
//
// The tests are serial because each one starts from the book the one before it left behind.

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    getShownPageId,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    addJustTextPage,
    assertNoWordAppearsTwice,
    assertRunIsIntact,
    clearBox,
    clickContinueText,
    doubleFontSizeOfBox,
    getBoxTexts,
    getCaretOwner,
    getChainId,
    getRunTexts,
    hasOverflowWarning,
    kTextForSeveralPages,
    makeTwoLinkedJustTextPages,
    pasteText,
    pressKeyAtStartOfBox,
    typeNewParagraphAfter,
    typeParagraphAtEnd,
} from "../helpers/flowText";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "flow-text-cross-page", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

// The two pages the text runs through.
let firstPageId: string;
let secondPageId: string;

/** What the box of one page holds, whichever page is showing at the moment. */
async function textOfPage(page: Page, pageId: string): Promise<string> {
    await goToPage(page, pageId);
    return (await getBoxTexts(page))[0];
}

test.describe("editing a run of text that crosses pages", () => {
    test("builds two linked pages holding one run of text", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        firstPageId = await addJustTextPage(page);
        await pasteText(page, 0, kTextForSeveralPages);
        secondPageId = await addJustTextPage(page);
        await clickContinueText(page, 0);

        expect(await getChainId(page, 0)).toBeTruthy();
        expect((await getBoxTexts(page))[0].length).toBeGreaterThan(0);
    });

    test("text comes back from the next page when this one is given room [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        const secondBefore = await textOfPage(page, secondPageId);
        await goToPage(page, firstPageId);

        // THE ACTION UNDER TEST: replace the first page's text with a single short line, which
        // leaves most of the box empty.
        await pasteText(page, 0, "A short line to start with.");

        // The first page fills up again from the second page's text, and the second page holds
        // less than it did.
        const firstAfter = (await getBoxTexts(page))[0];
        expect(firstAfter.length).toBeGreaterThan(
            "A short line to start with.".length,
        );
        expect(firstAfter).toContain("A short line to start with.");
        const secondAfter = await textOfPage(page, secondPageId);
        expect(secondAfter.length).toBeLessThan(secondBefore.length);
        // The text that came back moved: it is not in both pages at once.
        assertNoWordAppearsTwice(await getRunTexts(page));
    });

    test("typing past the end of a page carries the caret onto the next page [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await goToPage(page, firstPageId);

        // THE ACTION UNDER TEST: type at the end of the first page until the words no longer
        // fit. The text the person is typing moves to the next page, so the caret follows it:
        // Bloom shows that page with the caret in its box.
        await typeParagraphAtEnd(
            page,
            0,
            " " + kTextForSeveralPages.slice(0, 1500),
        );

        expect(await getShownPageId(page)).toBe(secondPageId);
        expect(await getCaretOwner(page)).toBe(0);
        // The page the words came from holds no more than fits, which is a question about that
        // page, so go back to it: the box read above is the one on the page now shown.
        await goToPage(page, firstPageId);
        expect(await hasOverflowWarning(page, 0)).toBe(false);
    });

    test("emptying a page brings the next page's text back into it [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        const secondBefore = await textOfPage(page, secondPageId);
        expect(secondBefore.length).toBeGreaterThan(0);
        await goToPage(page, firstPageId);

        // THE ACTION UNDER TEST: select everything on the first page and delete it.
        await clearBox(page, 0);

        // The box does not stay empty: it is the first box of the run, so the text that had
        // moved on comes back into it.
        const firstAfter = (await getBoxTexts(page))[0];
        expect(firstAfter.length).toBeGreaterThan(0);
        expect(await getChainId(page, 0)).toBeTruthy();
        const secondAfter = await textOfPage(page, secondPageId);
        expect(secondAfter.length).toBeLessThan(secondBefore.length);
    });

    test("Backspace at the start of a continued page does nothing [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        const secondBefore = await textOfPage(page, secondPageId);

        // THE ACTION UNDER TEST: Backspace with the caret at the very start of the second
        // page's box. There is no box before it on this page, so nothing joins to anything:
        // pulling the pages together is not a gesture this feature offers.
        await pressKeyAtStartOfBox(page, 0, "Backspace");

        // The box holds exactly what it held: nothing was pulled in from the page before, and
        // nothing of this box went there. The whole run is not read here, because visiting the
        // other page measures it again and that can move about a line of text either way (see
        // AUTOMATION-DEBT.md).
        expect((await getBoxTexts(page))[0]).toBe(secondBefore);
        expect(await getCaretOwner(page)).toBe(0);
    });
});

/**
 * A paragraph of about 200 characters whose every word is its own, so that the words a person
 * typed can be told apart from the words that were already in the run.
 */
const kTypedParagraph = Array.from(
    { length: 33 },
    (_unused, i) => "t" + String(i + 1).padStart(4, "0"),
).join(" ");

/** The words of that paragraph, in the order they were typed. */
const kTypedWords = kTypedParagraph.split(" ");

test.describe("changing a page whose text already runs on to the next page", () => {
    // Each of these builds its own book: one changes the size of the text and the other adds a
    // paragraph, and neither should start from what the other left behind. A book of its own is
    // what makes the pair of pages it builds the only pages of the run, so the offer to continue
    // an earlier page's text is not in the way of the box it types into.

    test("doubling the size of the text moves much more of it onto the next page [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        // The book of the tests above is open in the Edit tab; a new book is made from the
        // Collections tab.
        await switchTab(page, "collection");
        await makeBookFromTemplate(page, "Basic Book");
        const {
            firstPageId: pageOne,
            secondPageId: pageTwo,
            paragraphs,
        } = await makeTwoLinkedJustTextPages(page);
        const secondBefore = (await textOfPage(page, pageTwo)).length;
        const firstBefore = (await textOfPage(page, pageOne)).length;
        // Sanity check the start state: the first page holds most of the text, so that what
        // moves below is a real change rather than rounding.
        expect(secondBefore).toBeGreaterThan(0);
        expect(firstBefore).toBeGreaterThan(secondBefore);

        // THE ACTION UNDER TEST: make the text twice the size. Nothing about the text changes;
        // the first page's box simply holds much less of it. (The size belongs to the style, so
        // the second page's box grows with it; that only makes more text leave the first page.)
        const points = await doubleFontSizeOfBox(page, 0);
        expect(points).toBeGreaterThan(0);

        // It is still the same run of text, in the same order. This comes first, because a word
        // lost at the join is the failure worth naming.
        assertRunIsIntact(await getRunTexts(page), paragraphs.join(" "));
        // Text twice as tall and twice as wide fits about a quarter as many characters in the
        // same box, so most of what the first page held has gone on to the second page.
        const secondAfter = (await textOfPage(page, pageTwo)).length;
        expect(secondAfter - secondBefore).toBeGreaterThanOrEqual(
            firstBefore / 2,
        );
    });

    test("a paragraph typed in the middle of a page joins the run once, in order [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        // The book of the tests above is open in the Edit tab; a new book is made from the
        // Collections tab.
        await switchTab(page, "collection");
        await makeBookFromTemplate(page, "Basic Book");
        const { firstPageId: pageOne, paragraphs } =
            await makeTwoLinkedJustTextPages(page);
        // Sanity check the start state: the caret has to go at the end of a paragraph with
        // paragraphs on both sides of it.
        expect(paragraphs.length).toBeGreaterThanOrEqual(3);
        await goToPage(page, pageOne);

        // THE ACTION UNDER TEST: at the end of the second of the page's paragraphs, press Enter
        // and type a paragraph of about 200 characters.
        await typeNewParagraphAfter(page, 0, 1, kTypedParagraph);

        const expected = [
            ...paragraphs.slice(0, 2),
            kTypedParagraph,
            ...paragraphs.slice(2),
        ].join(" ");
        const runTexts = await getRunTexts(page);
        // The typed words are checked on their own first, because "the words I typed are in the
        // book once and in order" is the thing a person would look for, and it says which word
        // is wrong rather than which character.
        expectTypedWordsAppearOnceInOrder(runTexts.join(" "));
        assertRunIsIntact(runTexts, expected);
    });
});

/** Each word of kTypedParagraph is in the run of text exactly once, and they are still in order. */
function expectTypedWordsAppearOnceInOrder(runText: string): void {
    let previous = -1;
    for (const word of kTypedWords) {
        const occurrences = runText.split(word).length - 1;
        expect(
            occurrences,
            `The typed word "${word}" is in the run of text ${occurrences} times, not once.`,
        ).toBe(1);
        const at = runText.indexOf(word);
        expect(
            at,
            `The typed word "${word}" comes before the word typed before it.`,
        ).toBeGreaterThan(previous);
        previous = at;
    }
}
