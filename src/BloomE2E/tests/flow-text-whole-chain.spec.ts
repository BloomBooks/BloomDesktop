// Refitting a whole chain of linked text boxes without visiting its pages.
//
// The browser edits one page and can measure only that page, so a change that alters where the
// text breaks everywhere at once — a new paper size, a bigger font — leaves the later pages of a
// chain holding what they held before. Bloom refits them off-screen instead (FlowTextWalk), and
// these tests are about that: they change one thing, then read the whole run of text back out of
// the book through C#, without opening the pages that had to change.
//
// Reading the run through the chain C# reports is the point. Opening a page would settle it, so a
// test that visited the pages could not tell a refit that happened from one that happened only
// because the test looked.
//
// One page does have to be left, though: Bloom writes the page being edited into the book only
// when the book moves off it, so the book's copy of that page lags behind the browser's until
// then. Each reading therefore parks the Edit tab on a page outside the chain first. Parking
// there is not visiting a page of the chain, and no box of the chain settles because of it.
//
// The Test Case IDs in the titles are marked TBD: this feature has no rows in the Notion test
// inventory yet, and the ids are allocated there when it lands, not by this file.
//
// The tests are serial because each one starts from the book the one before it left behind.

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    getPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    addJustTextPage,
    assertRunIsIntact,
    clickContinueText,
    doubleFontSizeOfBox,
    getBookChains,
    getBoxTexts,
    hasOverflowWarning,
    isProgressDialogOpen,
    kTextForSeveralPages,
    pasteText,
    waitForReflowIdle,
} from "../helpers/flowText";
import { getPageSizeChoices, setPageSize } from "../helpers/pageSize";

test.use({
    collectionSpec: { name: "flow-text-whole-chain", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

// The pages of the run of text, in the order the text flows through them.
let firstPageId: string;
let secondPageId: string;
let thirdPageId: string;

/**
 * Leave the chain, so that the page being edited is written into the book and what the book says
 * about the chain is all of a piece. The page parked on carries no box of the chain.
 */
async function parkOffTheChain(page: Page): Promise<void> {
    const chainPages = [firstPageId, secondPageId, thirdPageId];
    const parking = (await getPages(page)).find(
        (candidate) => !chainPages.includes(candidate.id),
    );
    if (!parking) {
        throw new Error(
            "Every page of this book belongs to the chain, so there is nowhere to park.",
        );
    }
    await goToPage(page, parking.id);
    await waitForReflowIdle(page);
}

/**
 * The text of each page of the chain, in flow order, read out of the book rather than off a
 * page. Leaves the Edit tab parked outside the chain.
 */
async function readRun(page: Page): Promise<string[]> {
    await parkOffTheChain(page);
    const chains = await getBookChains(page);
    expect(
        chains,
        "These tests are about one run of text, so the book must hold exactly one chain.",
    ).toHaveLength(1);
    return chains[0].groups.map((group) => group.textByLang["en"]);
}

/** Show the first page of the chain, which is where the changes below are made from. */
async function editTheFirstPage(page: Page): Promise<void> {
    await goToPage(page, firstPageId);
    await waitForReflowIdle(page);
}

/**
 * A page size this book offers that is smaller than the one it is drawn at now, and one that is
 * larger. The sizes are named A4, A5, A6 and so on, where a bigger number is a smaller page, so
 * the ordering is by paper name rather than by anything we can measure here.
 */
function findSizes(choiceIds: string[]): { smaller: string; larger: string } {
    const wanted = (name: string) =>
        choiceIds.find((id) => id === name) ??
        (() => {
            throw new Error(
                `This book does not offer "${name}". It offers: ${choiceIds.join(", ")}.`,
            );
        })();
    return { smaller: wanted("A6Portrait"), larger: wanted("A5Portrait") };
}

test.describe("refitting a whole chain without visiting its pages", () => {
    test("a run of text is carried over three pages [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        firstPageId = await addJustTextPage(page);
        await pasteText(page, 0, kTextForSeveralPages);

        secondPageId = await addJustTextPage(page);
        await clickContinueText(page, 0);
        thirdPageId = await addJustTextPage(page);
        await clickContinueText(page, 0);

        // Every test below changes something while this page is the one being edited. That
        // matters: the page being edited belongs to the browser, and Bloom refits the pages
        // after it. Sitting on the first page therefore puts the whole rest of the chain in
        // Bloom's hands, which is what these tests are about.
        await editTheFirstPage(page);

        const texts = await readRun(page);
        const chains = await getBookChains(page);
        expect(chains).toHaveLength(1);
        expect(chains[0].groups.map((group) => group.pageId)).toEqual([
            firstPageId,
            secondPageId,
            thirdPageId,
        ]);
        assertRunIsIntact(texts, kTextForSeveralPages);
    });

    test("a smaller page size refits the pages nobody opened [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        const before = await readRun(page);
        const sizes = findSizes(
            (await getPageSizeChoices(page)).choices.map((c) => c.id),
        );
        await editTheFirstPage(page);

        // THE ACTION UNDER TEST: draw the book at a smaller page size. Only the first page is
        // open, so the second and third pages are refitted without anyone going to them.
        await setPageSize(page, sizes.smaller);

        // The refit happens off-screen, so the progress dialog is the only thing that tells the
        // author Bloom is working and that stops them editing while it does.
        await expect
            .poll(() => isProgressDialogOpen(page), { timeout: 10000 })
            .toBe(true);

        await waitForReflowIdle(page);
        expect(
            await isProgressDialogOpen(page),
            "The dialog must go away by itself once the refit is done.",
        ).toBe(false);

        const after = await readRun(page);
        expect(
            after[0].length,
            "A smaller page holds less text, so the first page must have given some up.",
        ).toBeLessThan(before[0].length);
        expect(
            after[2].length,
            "The text the earlier pages gave up has to have arrived at the end of the chain.",
        ).toBeGreaterThan(before[2].length);
        // Not a word lost or repeated at any of the joins the refit made.
        assertRunIsIntact(after, kTextForSeveralPages);

        // The middle page is full rather than overfull: the refit gave it what fits, so opening
        // it now finds nothing to complain about and nothing left to move.
        await goToPage(page, secondPageId);
        await waitForReflowIdle(page);
        expect(await hasOverflowWarning(page, 0)).toBe(false);
        expect(
            (await getBoxTexts(page))[0],
            "Opening the middle page must find it already holding what fits, so the visit " +
                "moves nothing.",
        ).toBe(after[1]);
    });

    test("a larger page size fills the earlier pages again [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        const before = await readRun(page);
        const sizes = findSizes(
            (await getPageSizeChoices(page)).choices.map((c) => c.id),
        );
        await editTheFirstPage(page);

        // THE ACTION UNDER TEST: draw the book larger again. The text has to come back to the
        // earlier pages, which is the opposite direction from the test above.
        await setPageSize(page, sizes.larger);
        await waitForReflowIdle(page);

        const after = await readRun(page);
        expect(
            after[0].length,
            "A larger page holds more text, so the first page must have taken some back.",
        ).toBeGreaterThan(before[0].length);
        expect(
            after[2].length,
            "The end of the chain has to be emptier once the pages before it hold more.",
        ).toBeLessThan(before[2].length);
        assertRunIsIntact(after, kTextForSeveralPages);
    });

    test("doubling the font size on the first page moves text on the pages after it [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        const before = await readRun(page);
        await editTheFirstPage(page);

        // THE ACTION UNDER TEST: double the font size of the box on the page being edited. The
        // style belongs to the whole book, so every page of the chain now breaks its text
        // somewhere else, and only the first page is open.
        await doubleFontSizeOfBox(page, 0);
        await waitForReflowIdle(page);

        const after = await readRun(page);
        expect(
            after[0].length,
            "Twice the font size fits about a quarter of the text, so the first page must " +
                "have given text up.",
        ).toBeLessThan(before[0].length);
        expect(
            after[1],
            "The second page was never opened, so if it holds what it held the refit did not " +
                "reach it.",
        ).not.toBe(before[1]);
        expect(
            after[2],
            "Nor was the third page opened, and it is where the text pushed off the others " +
                "has to end up.",
        ).not.toBe(before[2]);
        assertRunIsIntact(after, kTextForSeveralPages);
    });
});
