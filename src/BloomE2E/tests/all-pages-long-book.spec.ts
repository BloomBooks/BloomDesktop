// A book of a few hundred pages with the Edit tab showing all the pages (BL-16929): changing pages
// must take about as long as in a short book. All Pages lays out at most the 24 or so pages around
// the page being edited, and draws only those near the screen, so the cost of a page change should
// not grow with the book.
//
// Times on a shared machine are noisy, so the test compares medians measured in the same run,
// rather than a time against a fixed limit: the long book against a short one, and what All Pages
// adds over One Page in each book (which separates All Pages from what every page change costs in
// a big book, such as saving it).

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    waitForEditablePage,
    waitForEditTabSettled,
    type IBookPage,
} from "../helpers/bookMaking";
import { clickPageThumbnail } from "../helpers/pageList";
import {
    choosePageView,
    clickTextOfPageInGrid,
    getFacingPage,
    waitForPageToBeShown,
} from "../helpers/pageView";
import { getZoom, setZoom } from "../helpers/workspace";
import type { Page } from "@playwright/test";

test.use({
    collectionSpec: { name: "all-pages-long-book", languages: ["en"] },
});

const median = (values: number[]) =>
    [...values].sort((a, b) => a - b)[Math.floor(values.length / 2)];

/**
 * The median time, in milliseconds, of five page changes between two facing pages in the middle of
 * the book being edited, from the click until the new page shows: with All Pages, a click on the
 * other page; with One Page, a click on its thumbnail.
 */
async function timePageChanges(
    page: Page,
    view: "one" | "all",
    a: IBookPage,
    b: IBookPage,
): Promise<number> {
    await goToPage(page, a.id);
    await choosePageView(page, view);
    if (view === "all") await setZoom(page, (await getZoom(page)).minZoom);
    await waitForEditTabSettled(page);
    const times: number[] = [];
    let target = b;
    for (let i = 0; i < 5; i++) {
        const started = Date.now();
        if (view === "all") await clickTextOfPageInGrid(page, target.id);
        else await clickPageThumbnail(page, target.id);
        await waitForPageToBeShown(page, target.id);
        times.push(Date.now() - started);
        await waitForEditablePage(page);
        await waitForEditTabSettled(page);
        target = target === b ? a : b;
    }
    return median(times);
}

test("in a book of 300 pages at the smallest zoom, a page change with All Pages takes about as long as in a short book", async ({
    page,
}) => {
    test.setTimeout(1800000);
    const measured: Record<string, number> = {};
    for (const [name, count] of [
        ["short", 20],
        ["long", 300],
    ] as const) {
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Just Text", count);
        const content = await getContentPages(page);
        const a = content[Math.floor(content.length / 2)];
        const b = getFacingPage(await getPages(page), a.id);

        // THE ACTION UNDER TEST: page changes in each view.
        measured[`${name} all`] = await timePageChanges(page, "all", a, b);
        measured[`${name} one`] = await timePageChanges(page, "one", a, b);
    }
    const report = Object.entries(measured)
        .map(([k, v]) => `${k}: ${v}ms`)
        .join(", ");
    console.log(`Median page change times: ${report}`);
    expect(
        measured["long all"],
        `With All Pages, a page change in the long book took much longer than in the short one (${report}).`,
    ).toBeLessThan(measured["short all"] * 2 + 500);
    const extraLong = measured["long all"] - measured["long one"];
    const extraShort = measured["short all"] - measured["short one"];
    expect(
        extraLong,
        `What All Pages adds to a page change grew with the book (${report}).`,
    ).toBeLessThan(Math.max(extraShort, 0) * 2 + 500);
});
