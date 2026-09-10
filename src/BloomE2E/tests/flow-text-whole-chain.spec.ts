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
// The refit does not start by itself. A change like these records with Bloom that the pages after
// this one are out of date, and the author says when the work happens: with Reflow now, or by
// turning a page. So each test here asks for it, which is also the thing an author has to do.
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
    clickReflowNow,
    doubleFontSizeOfBox,
    getBookChains,
    getBoxTexts,
    hasOverflowWarning,
    isProgressDialogOpen,
    isReflowPendingShown,
    isWalkPending,
    kTextForSeveralPages,
    pasteText,
    runPendingReflow,
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
    // A refit still waiting to be run would make this a reading of the pages as they were before
    // the change. Parking is a page turn, which would start it, so run it here instead: what the
    // book says then has nothing in flight behind it.
    await runPendingReflow(page);
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
function findSizes(choiceIds: string[]): {
    smaller: string;
    larger: string;
    largest: string;
} {
    const wanted = (name: string) =>
        choiceIds.find((id) => id === name) ??
        (() => {
            throw new Error(
                `This book does not offer "${name}". It offers: ${choiceIds.join(", ")}.`,
            );
        })();
    return {
        smaller: wanted("A6Portrait"),
        larger: wanted("A5Portrait"),
        largest: wanted("A4Portrait"),
    };
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

        // Nothing has moved beyond the next page: the page being edited settles itself once it
        // is rebuilt at the new size, hands what no longer fits to the next page, and then asks
        // Bloom to refit the pages after that. That request waits for the author, and the
        // bubble on this page is where the author is told so.
        await waitForReflowIdle(page);
        await expect
            .poll(() => isWalkPending(page), {
                timeout: 30000,
                message:
                    "A new paper size has to leave a refit of the later pages waiting.",
            })
            .toBe(true);
        expect(await isReflowPendingShown(page)).toBe(true);

        // THE SECOND ACTION UNDER TEST: ask for the refit. It happens off-screen, so the
        // progress dialog is the only thing that tells the author Bloom is working and that
        // stops them editing while it does.
        await clickReflowNow(page);
        expect(
            await isProgressDialogOpen(page),
            "The dialog must go away by itself once the refit is done.",
        ).toBe(false);
        expect(
            await isWalkPending(page),
            "Reflow now has to leave nothing waiting behind it.",
        ).toBe(false);
        expect(
            await isReflowPendingShown(page),
            "The bubble is still up, though there is no longer a refit for it to offer.",
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

        expect(
            await isWalkPending(page),
            "A new paper size has to leave a refit of the later pages waiting.",
        ).toBe(true);
        await clickReflowNow(page);
        expect(
            await isReflowPendingShown(page),
            "The bubble is still up, though there is no longer a refit for it to offer.",
        ).toBe(false);

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

    test("a page size change made on the last page refits the page being edited too [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        const before = await readRun(page);
        const sizes = findSizes(
            (await getPageSizeChoices(page)).choices.map((c) => c.id),
        );

        // The last page of the chain is the one being edited. It has no page after it for the
        // browser to hand text to, so everything about this change is in Bloom's hands: the
        // pages before it, and the page being edited itself.
        await goToPage(page, thirdPageId);
        await waitForReflowIdle(page);

        // THE ACTION UNDER TEST: draw the book smaller while on the last page. The earlier pages
        // hold less, so text has to arrive here, on the page being edited.
        await setPageSize(page, sizes.smaller);
        await waitForReflowIdle(page);
        await expect
            .poll(() => isReflowPendingShown(page), {
                timeout: 30000,
                message:
                    "A new paper size has to offer the refit on the last page as on any other.",
            })
            .toBe(true);
        await clickReflowNow(page);
        await expect
            .poll(async () => (await getBoxTexts(page))[0].length, {
                timeout: 30000,
                message:
                    "The page being edited has to take the text the smaller earlier pages gave up.",
            })
            .toBeGreaterThan(before[2].length);
        expect(await isWalkPending(page)).toBe(false);
        expect(await isReflowPendingShown(page)).toBe(false);

        const afterSmaller = await readRun(page);
        assertRunIsIntact(afterSmaller, kTextForSeveralPages);
        await goToPage(page, thirdPageId);
        await waitForReflowIdle(page);

        // THE SECOND ACTION UNDER TEST: draw the book much larger, still on the last page. The
        // earlier pages take most of the run back, so this page is left with the end of it.
        await setPageSize(page, sizes.largest);
        await waitForReflowIdle(page);
        await expect
            .poll(() => isReflowPendingShown(page), { timeout: 30000 })
            .toBe(true);
        await clickReflowNow(page);
        const lastWord = kTextForSeveralPages.slice(
            kTextForSeveralPages.lastIndexOf(" ") + 1,
        );
        await expect
            .poll(async () => (await getBoxTexts(page))[0].trim(), {
                timeout: 30000,
                message:
                    "At this size the earlier pages hold most of the run, so the page being " +
                    "edited must be left holding only its end.",
            })
            .toMatch(new RegExp("^.{0,200}" + lastWord + "$"));
        expect(await isWalkPending(page)).toBe(false);
        expect(await isReflowPendingShown(page)).toBe(false);

        // The refit reached the pages before this one, and left nothing waiting on them.
        const afterLargest = await readRun(page);
        assertRunIsIntact(afterLargest, kTextForSeveralPages);
        expect(afterLargest[0].length).toBeGreaterThan(afterSmaller[0].length);
        await goToPage(page, secondPageId);
        await waitForReflowIdle(page);
        expect(await isReflowPendingShown(page)).toBe(false);
        expect(await hasOverflowWarning(page, 0)).toBe(false);
        expect((await getBoxTexts(page))[0]).toBe(afterLargest[1]);

        // An A4 page is taller than the Edit tab's viewport, which puts the format gear the
        // test after this one clicks out of reach. Leave the book at the size it was drawn at.
        await setPageSize(page, sizes.larger);
        await waitForReflowIdle(page);
        await runPendingReflow(page);
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

        await expect
            .poll(() => isWalkPending(page), {
                timeout: 30000,
                message:
                    "A bigger font has to leave a refit of the later pages waiting.",
            })
            .toBe(true);
        await clickReflowNow(page);
        expect(
            await isReflowPendingShown(page),
            "The bubble is still up, though there is no longer a refit for it to offer.",
        ).toBe(false);

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
