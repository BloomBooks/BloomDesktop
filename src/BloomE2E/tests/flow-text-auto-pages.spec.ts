// Bloom adding and removing the pages a run of text needs, by itself.
//
// "Create pages and flow text" makes the pages a run needs once. The book setting behind these
// tests, "Automatically add & remove pages", keeps doing it: while it is on, a refit that finds
// the run no longer fits its last box adds text-only pages after that box, and a refit that
// leaves pages of the chain empty takes them out, the page the author is editing included. The
// chain's first page is the one page never taken out.
//
// A book here holds TWO runs of text, one after the other, because the risk the setting carries
// is that Bloom edits the wrong pages: a run that grows must not push its way into the run after
// it, and a run that shrinks must not take that run's pages with it. A font size and a paper size
// belong to the whole book, so the second run is refitted by the same change as the first and may
// gain or lose pages of its own; what every test reads it back for is that it kept its text, kept
// the page it starts on, and stayed a block of pages after the first run rather than among it.
//
// The runs are read out of the book through C#, page by page, rather than off the pages, because
// opening a page settles it: a test that read the pages could not tell a page Bloom added or
// removed from a change that happened only because the test looked. Each reading therefore parks
// the Edit tab on a page outside both chains first, so that the page being edited is written into
// the book and what the book says is all of a piece.
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
    buildLongText,
    clickCreatePagesAndContinue,
    clickReflowNow,
    doubleFontSizeOfBox,
    getAutoPages,
    getBookChains,
    getPageBeingEditedId,
    IFlowChain,
    isAutoPagesChecked,
    isReflowPendingShown,
    isWalkPending,
    kTextForSeveralPages,
    pasteText,
    runPendingReflow,
    setAutoPages,
    setAutoPagesViaBubble,
    waitForReflowIdle,
} from "../helpers/flowText";
import { getPageSizeChoices, setPageSize } from "../helpers/pageSize";

test.use({
    collectionSpec: { name: "flow-text-auto-pages", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

/**
 * The text of the second run. Shorter than the first run's text, so the two runs are told apart by
 * how much text they hold as well as by which pages they are on.
 */
const kTextForTheSecondRun = buildLongText(2500);

// The first run of text: the chain it belongs to, the page it starts on, and everything that went
// into it.
let chainAId: string;
let chainAFirstPageId: string;
const chainAText = kTextForSeveralPages;

// The second run of text, and what its pages held when it was made.
let chainBId: string;
let chainBWhenMade: IPageText[];

/** One page of a run, and the text it holds. */
interface IPageText {
    pageId: string;
    text: string;
}

/**
 * Leave both chains, so that the page being edited is written into the book and what the book says
 * about the chains is all of a piece. The page parked on carries no box of either chain.
 */
async function parkOffTheChains(page: Page): Promise<void> {
    // A refit still waiting to be run would make this a reading of the pages as they were before
    // the change. Parking is a page turn, which would start it, so run it here instead: what the
    // book says then has nothing in flight behind it.
    await runPendingReflow(page);
    const chainPages = (await getBookChains(page)).flatMap((chain) =>
        chain.groups.map((group) => group.pageId),
    );
    const parking = (await getPages(page)).find(
        (candidate) => !chainPages.includes(candidate.id),
    );
    if (!parking) {
        throw new Error(
            "Every page of this book belongs to a chain, so there is nowhere to park.",
        );
    }
    await goToPage(page, parking.id);
    await waitForReflowIdle(page);
}

/**
 * Both runs of text as the book holds them, read after parking outside them. These tests are about
 * two runs of text, so any other number of chains is a failure rather than a choice.
 */
async function readBothChains(page: Page): Promise<IFlowChain[]> {
    await parkOffTheChains(page);
    const chains = await getBookChains(page);
    expect(
        chains,
        "These tests are about two runs of text, so the book must hold exactly two chains.",
    ).toHaveLength(2);
    return chains;
}

/** The chain with this id, out of the chains the book holds. */
function chainWithId(chains: IFlowChain[], chainId: string): IFlowChain {
    const found = chains.find((chain) => chain.chainId === chainId);
    if (!found) {
        throw new Error(
            `The book no longer holds the chain ${chainId}. It holds: ` +
                `${chains.map((chain) => chain.chainId).join(", ")}.`,
        );
    }
    return found;
}

/** The pages of a chain and the text each one holds, in flow order. */
function pagesAndText(chain: IFlowChain): IPageText[] {
    return chain.groups.map((group) => ({
        pageId: group.pageId,
        text: group.textByLang["en"],
    }));
}

/** The text of each page of a chain, in flow order, which is what assertRunIsIntact reads. */
function readRun(chain: IFlowChain): string[] {
    return chain.groups.map((group) => group.textByLang["en"]);
}

/**
 * Assert that the second run came through unhurt. A font size and a paper size belong to the whole
 * book, so the second run is refitted by the same change as the first and may gain or lose pages
 * of its own: what it may not do is lose text, lose the page it starts on, or become tangled with
 * the run before it.
 *
 * The last of those is the whole risk of Bloom adding and removing pages by itself. The two runs
 * have to stay two blocks of pages, one after the other, so a page of the first run among the
 * pages of the second means Bloom put a page where the wrong run was.
 */
async function expectRunsStillSeparate(
    page: Page,
    chains: IFlowChain[],
    when: string,
): Promise<void> {
    const secondRun = pagesAndText(chainWithId(chains, chainBId));
    assertRunIsIntact(
        secondRun.map((entry) => entry.text),
        kTextForTheSecondRun,
    );
    expect(
        secondRun.map((entry) => entry.pageId),
        `${when}, the page the second run starts on is gone. The page a run starts on is ` +
            `never taken out.`,
    ).toContain(chainBWhenMade[0].pageId);

    const order = await pageOrder(page);
    const firstRunAt = pagesAndText(chainWithId(chains, chainAId)).map(
        (entry) => order.indexOf(entry.pageId),
    );
    const secondRunAt = secondRun.map((entry) => order.indexOf(entry.pageId));
    expect(
        Math.min(...secondRunAt),
        `${when}, a page of the second run comes before a page of the first. The runs are two ` +
            `blocks of pages, one after the other.`,
    ).toBeGreaterThan(Math.max(...firstRunAt));
    expect(
        firstRunAt.filter(
            (at) =>
                at > Math.min(...secondRunAt) && at < Math.max(...secondRunAt),
        ),
        `${when}, a page of the first run sits between two pages of the second, so a page was ` +
            `added to the wrong run.`,
    ).toHaveLength(0);
}

/**
 * Where each page of the book comes in it, by page id. A page Bloom added has to sit between the
 * run it belongs to and the run after it, and this is what the test compares to say so.
 */
async function pageOrder(page: Page): Promise<string[]> {
    return (await getPages(page)).map((candidate) => candidate.id);
}

/**
 * The size this book is drawn at through these tests, and the largest it offers. The sizes are
 * named A4, A5, A6 and so on, where a bigger number is a smaller page, so the ordering is by paper
 * name rather than by anything we can measure here.
 */
function findSizes(choiceIds: string[]): { normal: string; largest: string } {
    const wanted = (name: string) =>
        choiceIds.find((id) => id === name) ??
        (() => {
            throw new Error(
                `This book does not offer "${name}". It offers: ${choiceIds.join(", ")}.`,
            );
        })();
    return { normal: wanted("A5Portrait"), largest: wanted("A4Portrait") };
}

/** Ask for the refit the page being edited says is waiting, having first said that one is. */
async function expectPendingThenReflow(page: Page): Promise<void> {
    await expect
        .poll(() => isWalkPending(page), {
            timeout: 30000,
            message:
                "The change has to leave a refit of the later pages waiting to be run.",
        })
        .toBe(true);
    expect(
        await isReflowPendingShown(page),
        "The page being edited carries a box of the chain, so it has to offer the refit.",
    ).toBe(true);
    await clickReflowNow(page);
}

test.describe("adding and removing pages for a run of text automatically", () => {
    test("taking the offer to make pages turns the setting on [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(600000);
        await makeBookFromTemplate(page, "Basic Book");

        // The first run of text, over pages Bloom makes for it.
        chainAFirstPageId = await addJustTextPage(page);
        await pasteText(page, 0, chainAText);
        // Start from the setting off, so that the assertion below is about the button rather than
        // about whatever a new book happens to begin with.
        await setAutoPages(page, false);

        // THE ACTION UNDER TEST: take the offer, which both makes the pages this run needs now and
        // tells Bloom to keep this book's runs fitted with pages from here on.
        await clickCreatePagesAndContinue(page, 0);

        expect(
            await getAutoPages(page),
            "Making the pages a run needs is how an author asks for pages to be managed, so " +
                "the button has to turn the setting on.",
        ).toBe(true);

        // The second run of text, after the first. The author is on the last page of the first
        // run, so the page added here lands between that run and the end of the book.
        await addJustTextPage(page);
        await pasteText(page, 0, kTextForTheSecondRun);
        await clickCreatePagesAndContinue(page, 0);

        const chains = await readBothChains(page);
        const first = chains.find((chain) =>
            chain.groups.some((group) => group.pageId === chainAFirstPageId),
        );
        if (!first) {
            throw new Error(
                "No chain holds the page the first run of text was pasted into.",
            );
        }
        chainAId = first.chainId;
        chainBId = chains.filter((chain) => chain.chainId !== chainAId)[0]
            .chainId;
        chainBWhenMade = pagesAndText(chainWithId(chains, chainBId));

        expect(
            first.groups.length,
            "This much text needs at least three pages, so at least two had to be made.",
        ).toBeGreaterThanOrEqual(3);
        expect(
            chainBWhenMade.length,
            "The second run needs a page of its own beyond the one it was pasted into.",
        ).toBeGreaterThanOrEqual(2);
        assertRunIsIntact(readRun(first), chainAText);
        assertRunIsIntact(
            chainBWhenMade.map((entry) => entry.text),
            kTextForTheSecondRun,
        );

        // The runs are in the book in the order they were made, which is what the tests below
        // check Bloom's own pages are put between.
        const order = await pageOrder(page);
        expect(
            order.indexOf(chainBWhenMade[0].pageId),
            "The second run has to come after the first, or nothing below is about what " +
                "Bloom does between two runs.",
        ).toBeGreaterThan(
            order.indexOf(
                pagesAndText(first)[pagesAndText(first).length - 1].pageId,
            ),
        );
    });

    test("a bigger font gives the first run more pages, in place [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(600000);
        const before = await readBothChains(page);
        const chainABefore = pagesAndText(chainWithId(before, chainAId));
        const pagesBefore = await pageOrder(page);
        const lastPageOfChainABefore =
            chainABefore[chainABefore.length - 1].pageId;

        await goToPage(page, chainAFirstPageId);
        await waitForReflowIdle(page);

        // THE ACTION UNDER TEST: double the font size of the box on the page being edited. The
        // style belongs to the whole book, so the first run now needs more pages than it has, and
        // the setting says Bloom is to make them.
        await doubleFontSizeOfBox(page, 0);
        await waitForReflowIdle(page);

        await expect
            .poll(() => isWalkPending(page), {
                timeout: 30000,
                message:
                    "A bigger font has to leave a refit of the later pages waiting.",
            })
            .toBe(true);
        expect(
            await isAutoPagesChecked(page),
            "The panel offering the refit has to show that Bloom will add and remove pages " +
                "for it, because that is what the refit is about to do.",
        ).toBe(true);

        // THE SECOND ACTION UNDER TEST: ask for the refit. It is the refit, not the font change,
        // that finds the run does not fit and adds the pages.
        await clickReflowNow(page);
        expect(
            await isWalkPending(page),
            "Reflow now has to leave nothing waiting behind it.",
        ).toBe(false);

        // The author asked for the refit and it made pages, so Bloom takes them to where their
        // text now ends. Read before anything else navigates: reading the chains parks the Edit
        // tab off them.
        await expect
            .poll(() => getPageBeingEditedId(page), {
                timeout: 30000,
                message:
                    "The refit added pages, so the author has to be taken off the page they " +
                    "asked from to the page the run of text now ends on.",
            })
            .not.toBe(chainAFirstPageId);
        const pageAfterReflow = await getPageBeingEditedId(page);

        const after = await readBothChains(page);
        const chainAAfter = pagesAndText(chainWithId(after, chainAId));
        expect(
            pageAfterReflow,
            "The author has to be looking at the page the run of text now ends on.",
        ).toBe(chainAAfter[chainAAfter.length - 1].pageId);
        expect(
            chainAAfter.length,
            "Twice the font size fits about a quarter of the text on a page, so the run has " +
                "to have been given pages it did not have.",
        ).toBeGreaterThan(chainABefore.length);
        assertRunIsIntact(
            chainAAfter.map((entry) => entry.text),
            chainAText,
        );

        // The pages Bloom added belong to this run, so they go where the run is: after the page it
        // used to end on, and before the run that follows.
        const order = await pageOrder(page);
        const added = chainAAfter
            .map((entry) => entry.pageId)
            .filter((pageId) => !pagesBefore.includes(pageId));
        expect(
            added,
            "The pages the run gained have to be pages the book did not have before.",
        ).toHaveLength(chainAAfter.length - chainABefore.length);
        for (const pageId of added) {
            expect(
                order.indexOf(pageId),
                `Page ${pageId} was added for the first run, so it has to come after the page ` +
                    `that run used to end on.`,
            ).toBeGreaterThan(order.indexOf(lastPageOfChainABefore));
            expect(
                order.indexOf(pageId),
                `Page ${pageId} was added for the first run, so it has to come before the ` +
                    `second run rather than in the middle of it.`,
            ).toBeLessThan(order.indexOf(chainBWhenMade[0].pageId));
        }

        await expectRunsStillSeparate(
            page,
            after,
            "After the first run gained pages",
        );
    });

    test("a larger page size takes the first run's spare pages away [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(600000);
        const before = await readBothChains(page);
        const chainABefore = pagesAndText(chainWithId(before, chainAId));
        const pageCountAfterGrowing = (await pageOrder(page)).length;
        const sizes = findSizes(
            (await getPageSizeChoices(page)).choices.map((c) => c.id),
        );

        // The page the run of text ends on, which a larger page size will leave empty. The author
        // is put on it on purpose: a page they are looking at is taken out like any other once
        // the run no longer reaches it, and that is what this test is about.
        const lastPageOfChainA = chainABefore[chainABefore.length - 1].pageId;
        const orderBefore = await pageOrder(page);
        const pageAfterTheLastOfChainA =
            orderBefore[orderBefore.indexOf(lastPageOfChainA) + 1];
        await goToPage(page, lastPageOfChainA);
        await waitForReflowIdle(page);

        // THE ACTION UNDER TEST: draw the book at the largest page size it offers. Each page now
        // holds much more of the run, so the pages at the end of it are left with nothing, and the
        // setting says Bloom is to take them out.
        await setPageSize(page, sizes.largest);
        await waitForReflowIdle(page);
        await expectPendingThenReflow(page);
        expect(
            await isWalkPending(page),
            "Reflow now has to leave nothing waiting behind it.",
        ).toBe(false);

        // THE SECOND ASSERTION UNDER TEST: the page the author was on has gone with the rest, and
        // Bloom has moved them to the page beside it.
        await expect
            .poll(() => getPageBeingEditedId(page), {
                timeout: 30000,
                message:
                    `The refit emptied page ${lastPageOfChainA}, which the author was on, so ` +
                    `Bloom has to take it out and show the page beside it.`,
            })
            .toBe(pageAfterTheLastOfChainA);

        const after = await readBothChains(page);
        const chainAAfter = pagesAndText(chainWithId(after, chainAId));
        expect(
            (await pageOrder(page)).includes(lastPageOfChainA),
            `Page ${lastPageOfChainA} was left holding nothing but an empty box of the run, ` +
                `so it has to be gone from the book even though the author was looking at it.`,
        ).toBe(false);
        expect(
            chainAAfter.length,
            "A larger page holds more of the run, so the pages the run no longer reaches have " +
                "to have been taken out.",
        ).toBeLessThan(chainABefore.length);
        expect(
            chainAAfter[0].pageId,
            "The page the run starts on is never taken out, however little of the run is " +
                "left to it.",
        ).toBe(chainAFirstPageId);
        assertRunIsIntact(
            chainAAfter.map((entry) => entry.text),
            chainAText,
        );

        expect(
            (await pageOrder(page)).length,
            "Pages the runs no longer reach are taken out, so the book has to be shorter than " +
                "it was when the first run was given pages.",
        ).toBeLessThan(pageCountAfterGrowing);

        await expectRunsStillSeparate(
            page,
            after,
            "After the first run gave up pages",
        );

        // An A4 page is taller than the Edit tab's viewport, which puts the format gear out of
        // reach. Leave the book at the size the tests above worked at.
        await goToPage(page, chainAFirstPageId);
        await waitForReflowIdle(page);
        await setPageSize(page, sizes.normal);
        await waitForReflowIdle(page);
        await runPendingReflow(page);
    });

    test("the checkbox in the panel turns the setting off and on again [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(600000);
        expect(
            await getAutoPages(page),
            "The tests above leave the setting on, which is what this one starts from.",
        ).toBe(true);
        const sizes = findSizes(
            (await getPageSizeChoices(page)).choices.map((c) => c.id),
        );

        // The panel is up only while a refit is waiting, so leave one waiting. Going back to the
        // size the book is already drawn at afterwards is what clears it again.
        await goToPage(page, chainAFirstPageId);
        await waitForReflowIdle(page);
        await setPageSize(page, sizes.largest);
        await waitForReflowIdle(page);
        await expect
            .poll(() => isReflowPendingShown(page), {
                timeout: 30000,
                message:
                    "A new paper size has to leave a refit waiting, or the panel with the " +
                    "checkbox never comes up.",
            })
            .toBe(true);
        expect(await isAutoPagesChecked(page)).toBe(true);

        // THE ACTION UNDER TEST: untick the checkbox the way an author does.
        await setAutoPagesViaBubble(page, false);
        expect(
            await getAutoPages(page),
            "Unticking the checkbox has to be what Bloom holds for the book, not only what " +
                "the panel draws.",
        ).toBe(false);

        // THE SECOND ACTION UNDER TEST: tick it again.
        await setAutoPagesViaBubble(page, true);
        expect(
            await getAutoPages(page),
            "Ticking the checkbox again has to put the setting back.",
        ).toBe(true);

        // Leave the book at the size the tests above worked at, with nothing waiting.
        await setPageSize(page, sizes.normal);
        await waitForReflowIdle(page);
        await runPendingReflow(page);
        await expectRunsStillSeparate(
            page,
            await readBothChains(page),
            "After the checkbox was unticked and ticked again",
        );
    });
});
