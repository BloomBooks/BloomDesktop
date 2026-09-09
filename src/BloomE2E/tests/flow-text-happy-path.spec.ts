// The journey this whole feature exists for: a person pastes more text than one page holds, and
// carries it on over the pages that follow.
//
// Every page here is Basic Book's "Just Text": one text box and nothing else, which is the page a
// person uses for a long run of text. The text crosses pages, so C# does the moving for the boxes
// the browser cannot see, and the test reads the result back through the page it is editing and
// through the chains C# reports for the whole book.
//
// The Test Case IDs in the titles are marked TBD: this feature has no rows in the Notion test
// inventory yet, and the ids are allocated there when it lands, not by this file.
//
// The tests are serial because each one starts from the book the one before it left behind.

import { expect, test } from "../fixtures/bloomTest";
import {
    getPageNumberLabel,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import { selectBook } from "../helpers/collection";
import { switchTab } from "../helpers/workspace";
import {
    addJustTextPage,
    assertRunIsIntact,
    clickContinueText,
    getBookChains,
    getBoxTexts,
    getChainId,
    getContinueButtonLabel,
    getFlowsFromLabel,
    getRunTexts,
    hasContinuationParagraph,
    hasOverflowMarker,
    hasOverflowWarning,
    isContinueButtonShown,
    kTextForSeveralPages,
    pasteText,
    typeParagraphAtEnd,
    waitForReflowIdle,
} from "../helpers/flowText";

test.use({
    collectionSpec: { name: "flow-text-happy-path", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

// What the test types at the end of the run. Its words are not indexed words, so they can be
// told apart from the text that was pasted in.
const kTypedTail = "Then the boat came back.";

// The book these tests build, so the one that restarts Bloom can select it again.
let bookFolder: string;

// The pages of the run of text, in the order the text flows through them.
let firstPageId: string;
let secondPageId: string;
let thirdPageId: string;

test.describe("carrying one run of text over several pages", () => {
    test("a page of pasted text says where its text stops fitting [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        bookFolder = await makeBookFromTemplate(page, "Basic Book");
        firstPageId = await addJustTextPage(page);

        // THE ACTION UNDER TEST: put in far more text than the box can hold.
        await pasteText(page, 0, kTextForSeveralPages);

        expect(await hasOverflowWarning(page, 0)).toBe(true);
        // The mark is what a later empty box offers to continue from, so without it nothing
        // else in this file can happen.
        expect(await hasOverflowMarker(page, 0)).toBe(true);
        expect(await getChainId(page, 0)).toBeUndefined();
    });

    test("a new page offers to continue the text of the page before it [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);

        // THE ACTION UNDER TEST: add a page. Its empty box has to find the overflowing box on
        // the earlier page, which is a question only C# can answer.
        secondPageId = await addJustTextPage(page);

        expect(await isContinueButtonShown(page, 0)).toBe(true);
        expect(await getContinueButtonLabel(page, 0)).toBe(
            `Continue text from page ${await getPageNumberLabel(page, firstPageId)}`,
        );
    });

    test("taking the offer brings the text that did not fit onto this page [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);

        // THE ACTION UNDER TEST: take the offer.
        await clickContinueText(page, 0);

        const arrived = (await getBoxTexts(page))[0];
        expect(arrived.length).toBeGreaterThan(0);
        // The text carries on in the middle of the sentence it was cut at, so this box's first
        // paragraph is the tail of a paragraph that began on the page before: no indent, no
        // gap above it.
        expect(await hasContinuationParagraph(page, 0)).toBe(true);
        expect(await getChainId(page, 0)).toBeTruthy();

        // The earlier page now holds only what fits in it, so it no longer complains, and it no
        // longer says where its text runs out: the text has somewhere to go.
        await goToPage(page, firstPageId);
        expect(await hasOverflowWarning(page, 0)).toBe(false);
        expect(await hasOverflowMarker(page, 0)).toBe(false);
    });

    test("a page that received more text than fits says so before anyone types in it [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        // The page before holds one page of the text, so this page received two pages' worth.
        await goToPage(page, secondPageId);
        expect(await hasOverflowMarker(page, 0)).toBe(true);
        expect(await hasOverflowWarning(page, 0)).toBe(true);

        // The warning is there when the page is opened again, not only after the text
        // arrived.
        await goToPage(page, firstPageId);
        await goToPage(page, secondPageId);
        expect(await hasOverflowWarning(page, 0)).toBe(true);

        // The box also says in words where its text comes from, because the box it continues is
        // on another page and so there is nothing on this page to see.
        expect(await getFlowsFromLabel(page, 0)).toBe(
            `Text flows here from page ${await getPageNumberLabel(page, firstPageId)}`,
        );

        // The first page starts the chain, so its box wears no such label.
        await goToPage(page, firstPageId);
        expect(await getFlowsFromLabel(page, 0)).toBeUndefined();
    });

    test("the text is one run: nothing is lost or repeated at the join [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);

        // Every word of what went in is in the book once, in order, however the boxes divided
        // it. Each word says where it comes in the run, so a word lost or repeated at a join
        // fails here and names the join.
        assertRunIsIntact(await getRunTexts(page), kTextForSeveralPages);
    });

    test("typing at the end of the second page still works [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await goToPage(page, secondPageId);
        const before = (await getBoxTexts(page))[0];

        // THE ACTION UNDER TEST: type at the end of the text on this page.
        await typeParagraphAtEnd(page, 0, ` ${kTypedTail}`);

        const after = (await getBoxTexts(page))[0];
        expect(after).not.toBe(before);
        // This is the last box of the chain, so the text that no longer fits has nowhere to go
        // and the box goes on saying where its text runs out. The next test takes that offer up.
        expect(await hasOverflowMarker(page, 0)).toBe(true);
        // Nothing the typing pushed about is lost, and the typed words are at the end of the run.
        assertRunIsIntact(
            await getRunTexts(page),
            `${kTextForSeveralPages} ${kTypedTail}`,
        );
    });

    test("a third page can continue the run in its turn [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await goToPage(page, secondPageId);
        expect(await hasOverflowMarker(page, 0)).toBe(true);

        // THE ACTION UNDER TEST: add a third page and take its offer.
        thirdPageId = await addJustTextPage(page);
        expect(await getContinueButtonLabel(page, 0)).toBe(
            `Continue text from page ${await getPageNumberLabel(page, secondPageId)}`,
        );
        await clickContinueText(page, 0);

        expect((await getBoxTexts(page))[0].length).toBeGreaterThan(0);
        const chains = await getBookChains(page);
        expect(chains).toHaveLength(1);
        expect(chains[0].groups.map((group) => group.pageId)).toEqual([
            firstPageId,
            secondPageId,
            thirdPageId,
        ]);
        // The run is still one run over three pages, with the typed words at the end of it.
        assertRunIsIntact(
            await getRunTexts(page),
            `${kTextForSeveralPages} ${kTypedTail}`,
        );
    });

    test("the run of text survives a restart of Bloom [Test Case ID TBD]", async ({
        page,
        bloomApp,
    }) => {
        test.setTimeout(300000);
        // The restart kills Bloom, which writes nothing on its way out, so leave the page that
        // was last edited first: that is what writes it into the book file.
        await goToPage(page, firstPageId);
        await waitForReflowIdle(page);
        const before = await getBookChains(page);
        // Sanity check the start state: all three pages are in the chain, holding the whole run,
        // before the restart.
        expect(before[0].groups).toHaveLength(3);
        assertRunIsIntact(
            before[0].groups.map((group) => group.textByLang["en"]),
            `${kTextForSeveralPages} ${kTypedTail}`,
        );

        // THE ACTION UNDER TEST: quit Bloom and start it again on the same collection. The
        // chain and the text are page markup, so they have to come back from the saved book.
        const restarted = await bloomApp.restart();
        // Bloom comes back on the Collections tab with nothing selected, so the book has to be
        // chosen again before any page of it can be reached.
        await selectBook(restarted, bookFolder);
        await switchTab(restarted, "edit");
        await goToPage(restarted, thirdPageId);
        await waitForReflowIdle(restarted);

        const after = await getBookChains(restarted);
        expect(after).toHaveLength(1);
        expect(after[0].groups.map((group) => group.pageId)).toEqual(
            before[0].groups.map((group) => group.pageId),
        );
        // The whole run comes back, in order. Which box each word lands in is measured afresh
        // on the page that loads, and the prediction the measurer makes and the point at which
        // the layout stopped drawing differ by about a line, so the division can shift.
        assertRunIsIntact(
            after[0].groups.map((group) => group.textByLang["en"]),
            `${kTextForSeveralPages} ${kTypedTail}`,
        );
    });
});
