// What the page thumbnails say about a run of linked text boxes.
//
// A thumbnail shows a red warning triangle for a page that holds more text than fits it, which
// it reads out of the page's saved HTML (the pageOverflows class, in PageThumbnail). A page whose
// box hands its extra text to a box on a later page is full rather than overfull: the text has
// somewhere to go, so the triangle would be reporting a problem the reader does not have.
//
// The pages of a chain are refitted by Bloom, off-screen, without anyone opening them
// (FlowTextWalk), so nothing on those pages corrects what they say about themselves. That is what
// this test is about: the whole run, seen from the page list rather than from a page.
//
// The run is longer than three pages hold, on purpose: the last page of the chain then really
// does hold more than fits, and its triangle is the check that a triangle can be seen at all.
//
// The Test Case ID in the title is marked TBD: this feature has no rows in the Notion test
// inventory yet, and the ids are allocated there when it lands, not by this file.

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    getPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    addJustTextPage,
    buildLongText,
    clickContinueText,
    doubleFontSizeOfBox,
    getBookChains,
    kFlowTextCollection,
    kTextForSeveralPages,
    pagesWithWarningTriangles,
    pasteText,
    runPendingReflow,
    waitForReflowIdle,
    waitForThumbnails,
} from "../helpers/flowText";
import { pageListFrame } from "../helpers/pageList";
import { switchTab } from "../helpers/workspace";

// The same collection object as every other flow-text spec, which is what lets all of them
// run on one Bloom rather than one each. kFlowTextCollection says how that works and why a
// test here cannot be disturbed by the file before it.
test.use({ collectionSpec: kFlowTextCollection });

// How many readings in a row have to agree before the thumbnails are believed. A thumbnail is
// redrawn whenever Bloom refits its page, so one reading can be of a thumbnail that is about to
// be replaced.
const kAgreeingReadings = 4;

/** How many of these pages' thumbnails show the warning triangle. */
async function countWarningTriangles(
    page: Page,
    pageIds: string[],
): Promise<number> {
    return (await pagesWithWarningTriangles(page, pageIds)).length;
}

/**
 * The font size each of these pages' thumbnails is drawn at, in the page list's own document. A
 * thumbnail is laid out with the page list's stylesheet rather than the book's, so the size comes
 * from the inline style attribute on the translation group and from nowhere else.
 */
async function thumbnailFontSizes(
    page: Page,
    pageIds: string[],
): Promise<string[]> {
    const sizes: string[] = [];
    for (const pageId of pageIds) {
        sizes.push(
            await pageListFrame(page)
                .locator(
                    `.gridItem[id="${pageId}"] .pageContainer .bloom-page ` +
                        `.bloom-translationGroup`,
                )
                .first()
                .evaluate((group) => window.getComputedStyle(group).fontSize),
        );
    }
    return sizes;
}

/**
 * Leave the chain, so that the page being edited is written into the book: Bloom saves that page
 * only when the book moves off it, and until then its thumbnail is of what it held before.
 */
async function parkOffTheChain(
    page: Page,
    chainPageIds: string[],
): Promise<void> {
    // A thumbnail is drawn from the page's saved HTML, so a refit still waiting to be run would
    // leave the thumbnails showing what the pages held before it. Parking is a page turn, which
    // starts such a refit, so run it here rather than reading thumbnails that are about to be
    // redrawn.
    await runPendingReflow(page);
    const parking = (await getPages(page)).find(
        (candidate) => !chainPageIds.includes(candidate.id),
    );
    if (!parking) {
        throw new Error(
            "Every page of this book belongs to the chain, so there is nowhere to park.",
        );
    }
    await goToPage(page, parking.id);
    await waitForReflowIdle(page);
}

test.describe("the page thumbnails of a run of linked text boxes", () => {
    test("no page of the run warns of overflowing while its text flows on [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        const firstPageId = await addJustTextPage(page);
        await pasteText(page, 0, kTextForSeveralPages);
        const secondPageId = await addJustTextPage(page);
        await clickContinueText(page, 0);
        const thirdPageId = await addJustTextPage(page);
        await clickContinueText(page, 0);

        const chainPageIds = [firstPageId, secondPageId, thirdPageId];
        await parkOffTheChain(page, chainPageIds);
        await waitForThumbnails(page, chainPageIds);
        // THE ASSERTION UNDER TEST: the first two pages hand their extra text to the page after
        // them, so neither is overflowing, whatever it said about itself while it was being
        // edited. The third holds the rest of the run, which is more than fits it, and nothing
        // follows it: its triangle is the real thing, and proof that a triangle can be seen.
        const flowingOn = [firstPageId, secondPageId];
        let agreed = 0;
        let lastWarned: string[] = [];
        try {
            await expect
                .poll(
                    async () => {
                        lastWarned = await pagesWithWarningTriangles(
                            page,
                            flowingOn,
                        );
                        agreed = lastWarned.length === 0 ? agreed + 1 : 0;
                        return agreed;
                    },
                    { timeout: 60000, intervals: [200, 250, 500, 500] },
                )
                .toBeGreaterThanOrEqual(kAgreeingReadings);
        } catch (error) {
            throw new Error(
                "A page of the run went on warning that it holds more text than fits, " +
                    "though its text flows on to the page after it. Pages warned: " +
                    lastWarned
                        .map((id) => `${chainPageIds.indexOf(id) + 1} (${id})`)
                        .join(", ") +
                    `. ${(error as Error).message}`,
            );
        }

        await expect
            .poll(async () => countWarningTriangles(page, [thirdPageId]), {
                timeout: 60000,
                intervals: [200, 250, 500, 500],
                message:
                    "The end of the chain holds text that has nowhere to go, so its " +
                    "thumbnail has to show the warning triangle.",
            })
            .toBe(1);
    });

    test("every page of the run is drawn at the new font size [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        // Two pages is the whole of what this needs: one the author changes the size on, and one
        // the refit writes without anyone opening it.
        // Asked from the Edit tab, Bloom makes the book but never loads a page of it.
        await switchTab(page, "collection");
        await makeBookFromTemplate(page, "Basic Book");
        const firstPageId = await addJustTextPage(page);
        await pasteText(page, 0, buildLongText(2000));
        const secondPageId = await addJustTextPage(page);
        await clickContinueText(page, 0);

        const chainPageIds = [firstPageId, secondPageId];
        await parkOffTheChain(page, chainPageIds);
        await waitForThumbnails(page, chainPageIds);
        const sizesBefore = await thumbnailFontSizes(page, chainPageIds);

        await goToPage(page, firstPageId);
        await waitForReflowIdle(page);
        // THE ACTION UNDER TEST: double the size of the style the run is written in, then ask for
        // the refit that carries the change through the pages the author is not looking at.
        await doubleFontSizeOfBox(page, 0);
        await waitForReflowIdle(page);
        await runPendingReflow(page);

        const pageIdsNow = await chainPageIdsNow(page);
        await parkOffTheChain(page, pageIdsNow);
        await waitForThumbnails(page, pageIdsNow);

        await expect
            .poll(
                async () =>
                    new Set(await thumbnailFontSizes(page, pageIdsNow)).size,
                {
                    timeout: 60000,
                    intervals: [200, 250, 500, 500],
                    message:
                        "A thumbnail is drawn from the inline font size on the translation group, " +
                        "which Bloom writes only onto the page being edited unless the refit " +
                        "copies it back. So every page of the run has to be drawn at one size.",
                },
            )
            .toBe(1);

        const sizesAfter = await thumbnailFontSizes(page, pageIdsNow);
        expect(
            sizesAfter[0],
            "The size the thumbnails are drawn at has to have changed, or they would all " +
                "agree whether or not the refit copied anything back.",
        ).not.toBe(sizesBefore[0]);
    });
});

/** The pages of the book's one chain, in flow order. */
async function chainPageIdsNow(page: Page): Promise<string[]> {
    const chains = await getBookChains(page);
    expect(
        chains,
        "This test is about one run of text, so the book must hold exactly one chain.",
    ).toHaveLength(1);
    return chains[0].groups.map((group) => group.pageId);
}
