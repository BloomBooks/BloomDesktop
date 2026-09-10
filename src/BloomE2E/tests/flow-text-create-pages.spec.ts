// Having Bloom make the pages a run of text still needs.
//
// Bloom never adds pages by itself, so up to here an author with three pages of text had to add
// each page and take the offer on it. This is the shortcut: the box where the run of text ends
// with more still to place offers to make the pages the rest needs, and clicking it makes them
// and divides the text among them without the author visiting any of them.
//
// The run of text is read out of the book through C#, page by page, rather than off the pages,
// because opening a page settles it: a test that read the pages could not tell text that the
// making put there from text that arrived only because the test looked. Each reading therefore
// parks the Edit tab on a page outside the chain first, so that the page being edited is written
// into the book and what the book says is all of a piece.
//
// The Test Case IDs in the titles are marked TBD: this feature has no rows in the Notion test
// inventory yet, and the ids are allocated there when it lands, not by this file.
//
// The tests are serial because each one starts from the book the one before it left behind.

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    editablePageFrame,
    getPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    addJustTextPage,
    assertRunIsIntact,
    clickCreatePagesAndContinue,
    getBookChains,
    getCreatePagesButtonLabel,
    hasOverflowMarker,
    hasOverflowWarning,
    IFlowChain,
    isCreatePagesButtonShown,
    kTextForSeveralPages,
    pasteText,
    runPendingReflow,
    typeParagraphAtEnd,
    waitForReflowIdle,
} from "../helpers/flowText";
import { pageListFrame } from "../helpers/pageList";

test.use({
    collectionSpec: { name: "flow-text-create-pages", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

/** The English label the button wears; the string is EditTab.FlowText.CreatePagesAndContinue. */
const kButtonLabel = "Create pages and flow text";

// The page the run of text starts on, and everything typed into it so far.
let firstPageId: string;
let textThatWentIn: string;

/**
 * Leave the chain, so that the page being edited is written into the book and what the book says
 * about the chain is all of a piece. The page parked on carries no box of the chain.
 */
async function parkOffTheChain(page: Page): Promise<void> {
    // Opening a page of the run can move text between it and the page beside it, which leaves the
    // pages after that to be refitted. That refit waits for the author, and parking is a page
    // turn, which is one of the two things that starts it, so run it here: what the book says
    // afterwards then has nothing in flight behind it.
    await runPendingReflow(page);
    const chains = await getBookChains(page);
    const chainPages = chains.flatMap((chain) =>
        chain.groups.map((group) => group.pageId),
    );
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
 * The one chain of the book, read after parking outside it. A test that reads a run of text is a
 * test about one run of text, so more than one chain is a failure rather than a choice.
 */
async function readTheChain(page: Page) {
    await parkOffTheChain(page);
    const chains = await getBookChains(page);
    expect(
        chains,
        "These tests are about one run of text, so the book must hold exactly one chain.",
    ).toHaveLength(1);
    return chains[0];
}

/**
 * The page the Edit tab has moved on to, having been showing the page named here. Bloom is asked
 * for the jump after the reply comes back, so a test that read the tab straight away could read
 * the page it started on and say nothing at all; this waits for the move.
 */
async function pageMovedOnFrom(
    page: Page,
    wasShowing: string,
): Promise<string> {
    const showing = async () =>
        await editablePageFrame(page)
            .locator(".bloom-page[id]")
            .first()
            .getAttribute("id")
            .catch(() => null);
    await expect.poll(showing, { timeout: 30000 }).not.toBe(wasShowing);
    return (await showing()) ?? "";
}

/** The text of each page of a chain, in flow order, as the book holds it. */
function readRun(chain: IFlowChain): string[] {
    return chain.groups.map((group) => group.textByLang["en"]);
}

/**
 * Assert that no box of the run is empty. A run of text is divided so that each box holds what
 * fits it, so an empty box in the middle of one means text went missing rather than moved. Read
 * before each step as well as after, so that a failure names the step that lost it.
 */
function expectNoBoxIsEmpty(chain: IFlowChain, when: string): void {
    const lengths = readRun(chain).map((text) => text.length);
    expect(
        lengths.filter((length) => length === 0),
        `${when}, the run is divided as ${lengths.join(", ")} characters. An empty box means ` +
            `text went missing rather than moved.`,
    ).toHaveLength(0);
}

/**
 * Open every page of the chain and assert that Bloom is not warning about any of its boxes. The
 * making divided the text so that each page holds what fits, so every page has to be full rather
 * than overfull, the last one included.
 *
 * Opening the pages is safe here: a page that already holds what fits has nothing to move, which
 * is the very thing being checked. Leaves the Edit tab on the last page of the chain.
 */
async function expectNoPageOverflows(page: Page): Promise<void> {
    const chain = await readTheChain(page);
    for (const group of chain.groups) {
        await goToPage(page, group.pageId);
        await waitForReflowIdle(page);
        expect(
            await hasOverflowWarning(page, group.indexInPage),
            `Page ${group.pageId} warns that its text does not fit, but the run was divided ` +
                `so that every page holds only what fits.`,
        ).toBe(false);
        // Opening a page settles it, and settling one page of a run can move text between it and
        // the page beside it. Read the whole run after each one, so that a page that loses text
        // rather than moving it is named here rather than at the end.
        expectNoBoxIsEmpty(
            await readTheChain(page),
            `After opening page ${group.pageId} of the run`,
        );
    }
}

/**
 * How many readings in a row have to agree before a thumbnail is believed. A thumbnail is
 * redrawn whenever Bloom refits its page, so one reading can be of a thumbnail about to be
 * replaced.
 */
const kAgreeingReadings = 4;

/**
 * Assert that this page's thumbnail shows no red warning triangle. The triangle is drawn from the
 * page's saved HTML, so it is what an author sees about a page they are not looking at.
 */
async function expectNoWarningTriangle(
    page: Page,
    pageId: string,
): Promise<void> {
    await expect(
        pageListFrame(page).locator(
            `.gridItem[id="${pageId}"] .pageContainer .bloom-page`,
        ),
        `The thumbnail of page ${pageId} was never drawn, so nothing can be read off it.`,
    ).toHaveCount(1, { timeout: 60000 });

    let agreeing = 0;
    await expect
        .poll(
            async () => {
                const triangles = await pageListFrame(page)
                    .locator(`.gridItem[id="${pageId}"] .pageOverflowsIcon`)
                    .count();
                agreeing = triangles === 0 ? agreeing + 1 : 0;
                return agreeing;
            },
            {
                timeout: 60000,
                intervals: [200, 300, 500],
                message:
                    `The thumbnail of page ${pageId} keeps its warning triangle, but the ` +
                    `page holds only the text that fits it.`,
            },
        )
        .toBeGreaterThanOrEqual(kAgreeingReadings);
}

test.describe("making the pages a run of text needs", () => {
    test("one long paste becomes a run of text over pages Bloom makes [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        firstPageId = await addJustTextPage(page);
        textThatWentIn = kTextForSeveralPages;
        await pasteText(page, 0, textThatWentIn);

        // Sanity check the start state: one box, holding more text than fits, in no chain, so
        // there is nowhere for the rest of the text to go and the offer is the way out.
        expect(
            await hasOverflowMarker(page, 0),
            "The pasted text has to be more than the page holds, or there is nothing to offer.",
        ).toBe(true);
        expect(await getBookChains(page)).toHaveLength(0);
        expect(await isCreatePagesButtonShown(page, 0)).toBe(true);
        expect(await getCreatePagesButtonLabel(page, 0)).toBe(kButtonLabel);

        // THE ACTION UNDER TEST.
        await clickCreatePagesAndContinue(page, 0);

        // Where the Edit tab is now, read before anything navigates: taking the offer moves the
        // author to the last page it made, and reading the chain parks the tab off the chain.
        const pageAfterTakingTheOffer = await pageMovedOnFrom(
            page,
            firstPageId,
        );

        const chain = await readTheChain(page);
        expect(
            chain.groups.length,
            "This much text needs at least three pages, so at least two had to be made.",
        ).toBeGreaterThanOrEqual(3);
        expect(
            chain.groups[0].pageId,
            "The run has to start on the page the text was typed into.",
        ).toBe(firstPageId);
        expect(
            pageAfterTakingTheOffer,
            "The author has to be looking at the page the run of text now ends on.",
        ).toBe(chain.groups[chain.groups.length - 1].pageId);
        // Not a word lost or repeated at any of the joins the making produced.
        assertRunIsIntact(readRun(chain), textThatWentIn);
    });

    test("every page Bloom made holds what fits and nothing complains [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        const chain = await readTheChain(page);
        const lastPageId = chain.groups[chain.groups.length - 1].pageId;

        expectNoBoxIsEmpty(chain, "After making the pages");
        await expectNoPageOverflows(page);
        await expectNoWarningTriangle(page, lastPageId);
        expectNoBoxIsEmpty(
            await readTheChain(page),
            "After opening every page of the run",
        );

        // The run ends here now, so there is nothing left to offer to make pages for.
        await goToPage(page, lastPageId);
        await waitForReflowIdle(page);
        expect(
            await isCreatePagesButtonShown(
                page,
                chain.groups[chain.groups.length - 1].indexInPage,
            ),
            "The flow is complete, so the last box must not still be offering to make pages.",
        ).toBe(false);
    });

    test("typing more text at the end offers to make more pages [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        const before = await readTheChain(page);
        expectNoBoxIsEmpty(before, "Before this test adds more text");
        const lastGroup = before.groups[before.groups.length - 1];
        const pagesBefore = before.groups.length;

        // Reading the chain parks the browser on a page that carries none of it, so the page
        // to type on is opened after that, not before.
        await goToPage(page, lastGroup.pageId);
        await waitForReflowIdle(page);

        // Enough more text to need pages of its own. The last box of the run is not full,
        // so a few sentences would simply fit in it and nothing would be offered.
        const more = ` ${kTextForSeveralPages}`;
        await typeParagraphAtEnd(page, lastGroup.indexInPage, more);
        textThatWentIn += more;

        expect(
            await isCreatePagesButtonShown(page, lastGroup.indexInPage),
            "The last box of the chain now holds more than fits, so it has to offer again.",
        ).toBe(true);

        // THE ACTION UNDER TEST: take the offer on a box that is already linked, so the pages
        // made join the chain that is there rather than starting a new one.
        await clickCreatePagesAndContinue(page, lastGroup.indexInPage);

        const pageAfterTakingTheOffer = await pageMovedOnFrom(
            page,
            lastGroup.pageId,
        );

        const after = await readTheChain(page);
        expect(
            pageAfterTakingTheOffer,
            "The author has to be looking at the page the run of text now ends on.",
        ).toBe(after.groups[after.groups.length - 1].pageId);
        expect(
            after.chainId,
            "The chain must be the same one, not a new one.",
        ).toBe(before.chainId);
        expect(
            after.groups.length,
            "More text than the chain could hold has to have been given more pages.",
        ).toBeGreaterThan(pagesBefore);
        assertRunIsIntact(readRun(after), textThatWentIn);
    });
});
