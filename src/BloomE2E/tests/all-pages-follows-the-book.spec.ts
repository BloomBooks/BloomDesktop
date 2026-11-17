// With the Edit tab showing all the pages of the book, the pages around the page being edited
// follow what the user does to the book: adding, duplicating, removing and moving pages, a change
// made in the Format dialog, a different page size, and a different book (BL-16929).
//
// The tests are serial because each one starts from the books the one before it left behind.

import { expect, test } from "../fixtures/bloomTest";
import { addPageFromDialog, openAddPageDialog } from "../helpers/addPageDialog";
import {
    addPage,
    clickInGroup,
    editBook,
    getContentPages,
    getPages,
    getShownPageId,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
    waitForEditablePage,
    waitForEditTabSettled,
    type IBookPage,
} from "../helpers/bookMaking";
import {
    chooseFontSize,
    closeFormatDialog,
    openFormatDialog,
    scrollFormatGearIntoView,
    showFormatDialogTab,
} from "../helpers/formatDialog";
import { isChangeLayoutMode, setChangeLayoutMode } from "../helpers/origami";
import {
    confirmRemovePageDialog,
    duplicatePageWithButton,
    escapeRemovePageDialog,
    getRemovalMark,
    movePageToSlotOf,
    openRemovePageDialog,
} from "../helpers/pageList";
import { setPageSize } from "../helpers/pageSize";
import { selectPage } from "../helpers/pageThumbnails";
import {
    choosePageView,
    clickPageViewSegment,
    doesPageFrameScrollSideways,
    expectFacingPageBesidePageBeingEdited,
    expectGridToMatchBook,
    getChangeLayoutSwitchPlacement,
    getFontSizeOfOtherPage,
    getGridPageIds,
    getPagePlacement,
    isOtherPageDrawnAtSize,
    kMaxPagesInGrid,
    scrollPageFrameBy,
} from "../helpers/pageView";
import { getZoom, setZoom } from "../helpers/workspace";
import type { Page } from "@playwright/test";

test.use({
    collectionSpec: { name: "all-pages-follows", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

// A book of A5 portrait pages, and one made later whose pages are wider.
let bookFolder: string;
let widerBookFolder: string;

const kGroup = ".bloom-translationGroup";

// How long BL-16893's page layout update may take: it loads every page of the book off-screen.
const kPageLayoutUpdateTimeout = 120_000;

/** Wait until the Edit tab's zoom is `zoom`. */
async function expectZoom(page: Page, zoom: number, when: string) {
    await expect
        .poll(async () => (await getZoom(page)).zoom, { message: when })
        .toBe(zoom);
}

/** The page among `after` that is not among `before`. */
function newPage(before: IBookPage[], after: IBookPage[]): IBookPage {
    const added = after.filter((p) => !before.some((b) => b.id === p.id));
    expect(added.length, "exactly one page should be new").toBe(1);
    return added[0];
}

/**
 * Wait until All Pages has every page around the page being edited at this page size, after a
 * change that makes Bloom bring the book's page layout up to date and load the page again.
 */
async function waitForGridAtPageSize(page: Page, layoutId: string) {
    await expect
        .poll(
            async () => {
                try {
                    const shown = await getShownPageId(page);
                    const other = (await getGridPageIds(page)).find(
                        (id) => id !== shown,
                    );
                    if (!other) return false;
                    return await isOtherPageDrawnAtSize(page, other, layoutId);
                } catch {
                    // The page frame is replaced while the update runs.
                    return false;
                }
            },
            {
                timeout: kPageLayoutUpdateTimeout,
                message: `All Pages never drew the other pages at ${layoutId}.`,
            },
        )
        .toBe(true);
    await waitForEditablePage(page);
    await waitForEditTabSettled(page);
}

test.describe("All Pages follows the book", () => {
    test("builds a book with six text pages", async ({ page }) => {
        test.setTimeout(300000);
        bookFolder = await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Just Text", 6);
        const contentPages = await getContentPages(page);
        for (let i = 0; i < contentPages.length; i++) {
            await goToPage(page, contentPages[i].id);
            await typeInGroup(page, kGroup, "en", `Words on page ${i + 1}`);
        }
        await goToPage(page, contentPages[1].id);
        await choosePageView(page, "all");
    });

    test("adding, duplicating, removing and moving a page each leave All Pages showing the book's new order, with a page that can be edited", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await goToPage(page, (await getContentPages(page))[1].id);
        await expectGridToMatchBook(page);

        // Add a page with the Add Page dialog.
        let before = await getPages(page);
        await openAddPageDialog(page);
        await addPageFromDialog(page, "Just Text", "Basic Book");
        const added = newPage(before, await getPages(page));
        await waitForEditablePage(page);
        expect(await getShownPageId(page)).toBe(added.id);
        await expectGridToMatchBook(page);
        await typeInGroup(page, kGroup, "en", "Typed on the added page");

        // Duplicate it with the button under the page list.
        before = await getPages(page);
        const copy = await duplicatePageWithButton(page);
        await waitForEditablePage(page);
        await expectGridToMatchBook(page);
        const afterDuplicate = await getPages(page);
        expect(afterDuplicate.findIndex((p) => p.id === copy.id)).toBe(
            afterDuplicate.findIndex((p) => p.id === added.id) + 1,
        );
        await goToPage(page, copy.id);
        await expectGridToMatchBook(page);
        await typeInGroup(page, kGroup, "en", "Typed on the copy");

        // Remove it: Escape leaves the page alone, and Remove takes away that page only.
        before = await getPages(page);
        await openRemovePageDialog(page);
        expect(await getRemovalMark(page)).toBeDefined();
        await escapeRemovePageDialog(page);
        expect(await getRemovalMark(page)).toBeUndefined();
        expect((await getPages(page)).map((p) => p.id)).toEqual(
            before.map((p) => p.id),
        );
        expect(await getShownPageId(page)).toBe(copy.id);
        await openRemovePageDialog(page);
        await confirmRemovePageDialog(page);
        await waitForEditablePage(page);
        // The front and back matter keep their ids here, so the pages left are exactly the others.
        expect((await getPages(page)).map((p) => p.id)).toEqual(
            before.filter((p) => p.id !== copy.id).map((p) => p.id),
        );
        await expectGridToMatchBook(page);
        expect(await getRemovalMark(page)).toBeUndefined();

        // Move a page by dragging its thumbnail.
        const contentPages = await getContentPages(page);
        await goToPage(page, contentPages[1].id);
        await movePageToSlotOf(page, contentPages[1].id, contentPages[0].id);
        await waitForEditablePage(page);
        expect((await getContentPages(page))[0].id).toBe(contentPages[1].id);
        await expectGridToMatchBook(page);
        await goToPage(page, contentPages[1].id);
        await typeInGroup(page, kGroup, "en", "Typed on the moved page");
    });

    test("a font size chosen in the Format dialog shows on the other pages at once", async ({
        page,
    }) => {
        const contentPages = await getContentPages(page);
        const edited = contentPages[2];
        const other = contentPages[3];
        await goToPage(page, edited.id);
        await expectGridToMatchBook(page);
        const kNewSize = 30;
        expect(
            await getFontSizeOfOtherPage(page, other.id),
            "test setup: the other page should not start at the size the test chooses",
        ).not.toBe(kNewSize);
        await clickInGroup(page, kGroup, "en");
        await scrollFormatGearIntoView(page);
        await openFormatDialog(page);
        await showFormatDialogTab(page, "characters");

        // THE ACTION UNDER TEST: choose a size, and leave the dialog open and the page where it is.
        await chooseFontSize(page, kNewSize);

        await expect
            .poll(() => getFontSizeOfOtherPage(page, other.id), {
                message:
                    "The other page did not take the new font size while the Format dialog was still open.",
            })
            .toBe(kNewSize);
        expect(await getShownPageId(page)).toBe(edited.id);
        await closeFormatDialog(page);
    });

    test("the Change Layout switch stays put while the pages scroll, and still works", async ({
        page,
    }) => {
        const contentPages = await getContentPages(page);
        await goToPage(page, contentPages[0].id);
        await expectGridToMatchBook(page);
        const before = await getChangeLayoutSwitchPlacement(page);
        const pageBefore = await getPagePlacement(page, contentPages[0].id);

        const scrolled = await scrollPageFrameBy(page, 200);

        expect(
            scrolled,
            "test setup: the pages should have scrolled",
        ).toBeGreaterThan(50);
        const pageAfter = await getPagePlacement(page, contentPages[0].id);
        expect(pageAfter.page.top).toBeLessThan(pageBefore.page.top - 50);
        expect(await getChangeLayoutSwitchPlacement(page)).toEqual(before);
        expect(before.top).toBeGreaterThanOrEqual(0);
        expect(before.bottom).toBeLessThanOrEqual(pageAfter.visible.top + 1);

        await setChangeLayoutMode(page, true);
        expect(await isChangeLayoutMode(page)).toBe(true);
        expect(await getChangeLayoutSwitchPlacement(page)).toEqual(before);
        await setChangeLayoutMode(page, false);
        expect(await isChangeLayoutMode(page)).toBe(false);
    });

    test("All Pages zooms out for a book with wider pages, when chosen and when the book is opened after a restart, and keeps a zoom the user then gives", async ({
        page,
        bloomApp,
    }) => {
        test.setTimeout(600000);
        // All Pages at the zoom that fits two A5 portrait pages, then a little further out.
        await choosePageView(page, "one");
        const { minZoom, maxZoom } = await getZoom(page);
        await setZoom(page, maxZoom);
        await choosePageView(page, "all");
        await expect
            .poll(async () => (await getZoom(page)).zoom)
            .toBeLessThan(maxZoom);
        expect(await doesPageFrameScrollSideways(page)).toBe(false);
        const fitsPortrait = (await getZoom(page)).zoom;
        const furtherOut = fitsPortrait - 10;
        expect(
            furtherOut,
            "test setup: there should be room to zoom out further",
        ).toBeGreaterThanOrEqual(minZoom);
        await setZoom(page, furtherOut);

        // A second book, with A4 landscape pages, made with One Page showing.
        await choosePageView(page, "one");
        widerBookFolder = await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Just Text", 4);
        await setPageSize(page, "A4Landscape");

        // THE ACTION UNDER TEST: choose All Pages in the wider book. (The page size change left
        // the book needing its page layout update, which this starts, so the page loads again.)
        await clickPageViewSegment(page, "all");
        await waitForGridAtPageSize(page, "A4Landscape");
        await expectGridToMatchBook(page);
        await expect
            .poll(async () => (await getZoom(page)).zoom, {
                message:
                    "All Pages did not zoom out for pages too wide to fit two across.",
            })
            .toBeLessThan(furtherOut);
        await expect.poll(() => doesPageFrameScrollSideways(page)).toBe(false);
        const fitsLandscape = (await getZoom(page)).zoom;
        const contentPages = await getContentPages(page);
        await goToPage(page, contentPages[1].id);
        await expectGridToMatchBook(page);
        await expectFacingPageBesidePageBeingEdited(page);

        // Back in the first book, All Pages keeps the smaller zoom, when the book opens and when
        // All Pages is chosen again.
        await editBook(page, bookFolder);
        await expectGridToMatchBook(page);
        await expectZoom(page, fitsLandscape, "opening the first book");
        await choosePageView(page, "one");
        await choosePageView(page, "all");
        await expectZoom(page, fitsLandscape, "choosing All Pages again");

        // Give All Pages a zoom that fits this book and not the wider one, and start Bloom again.
        // (In one session, a zoom larger than the last one Bloom fitted counts as the user's own
        // choice, which Bloom leaves alone.) Showing a page of the wider book then zooms out, with
        // no new choice of All Pages.
        await setZoom(page, fitsPortrait);
        const shell = await bloomApp.quitAndRestart();
        await editBook(shell, bookFolder);
        await expectGridToMatchBook(shell);
        await expectZoom(shell, fitsPortrait, "after starting Bloom again");
        await editBook(shell, widerBookFolder);
        await expectGridToMatchBook(shell);
        await expectZoom(shell, fitsLandscape, "opening the wider book");
        await expect.poll(() => doesPageFrameScrollSideways(shell)).toBe(false);

        // A zoom the user gives after that is kept from page to page, though two pages no longer
        // fit across.
        const zoomedIn = fitsLandscape + 20;
        await setZoom(shell, zoomedIn);
        expect(
            await doesPageFrameScrollSideways(shell),
            "test setup: zoomed in, the pages should be wider than the view",
        ).toBe(true);
        await goToPage(shell, (await getContentPages(shell))[2].id);
        await expectGridToMatchBook(shell);
        await expectZoom(shell, zoomedIn, "going to another page");
    });

    test("a very small page size keeps the facing pages side by side without zooming out, and a size for a screen shows single pages in reading order", async ({
        page,
    }) => {
        test.setTimeout(600000);
        // Start from the zoom that fits two of the book's A4 landscape pages.
        await choosePageView(page, "one");
        await choosePageView(page, "all");
        const contentPages = await getContentPages(page);
        await goToPage(page, contentPages[1].id);
        const zoomBefore = (await getZoom(page)).zoom;

        await setPageSize(page, "A6Portrait");
        await waitForGridAtPageSize(page, "A6Portrait");
        await expectGridToMatchBook(page);
        await expectFacingPageBesidePageBeingEdited(page);
        expect(await doesPageFrameScrollSideways(page)).toBe(false);
        expect(
            (await getZoom(page)).zoom,
            "Two smaller pages fit across, so All Pages should not have zoomed out",
        ).toBe(zoomBefore);

        await setPageSize(page, "Device16x9Landscape");
        await waitForGridAtPageSize(page, "Device16x9Landscape");
        const pages = await getPages(page);
        expect(pages.length).toBeLessThan(kMaxPagesInGrid);
        // Every page of the book has a place, in the book's order, and there is no blank page.
        await expect
            .poll(() => getGridPageIds(page))
            .toEqual(pages.map((p) => p.id));
        const places = [];
        for (const p of pages)
            places.push((await getPagePlacement(page, p.id)).page);
        // Reading order: each page is to the right of the one before it, or starts a lower row.
        for (let i = 1; i < places.length; i++) {
            const sameRow = Math.abs(places[i].top - places[i - 1].top) < 2;
            expect(
                sameRow
                    ? places[i].left >= places[i - 1].right - 1
                    : places[i].top >= places[i - 1].bottom - 1,
                `Page ${i + 1} is at (${places[i].left}, ${places[i].top}), which does not follow page ${i} at (${places[i - 1].left}, ${places[i - 1].top}).`,
            ).toBe(true);
        }
        // The front cover starts the first row. With facing pages it stands alone on the right.
        const leftmost = Math.min(...places.map((r) => r.left));
        expect(Math.abs(places[0].left - leftmost)).toBeLessThan(2);

        await setPageSize(page, "A5Portrait");
        await waitForGridAtPageSize(page, "A5Portrait");
    });

    test("in a long book, clicking the last page and then the first in the page list shows the pages around each", async ({
        page,
    }) => {
        test.setTimeout(600000);
        await goToPage(page, (await getContentPages(page))[0].id);
        await addPage(page, "Just Text", 22);
        const pages = await getPages(page);
        expect(
            pages.length,
            "test setup: the book should have more pages than All Pages lays out",
        ).toBeGreaterThan(kMaxPagesInGrid + 2);
        const first = pages[0];
        const last = pages[pages.length - 1];

        await selectPage(page, last.id);
        await expectGridToMatchBook(page);
        let inGrid = await getGridPageIds(page);
        expect(inGrid[inGrid.length - 1]).toBe(last.id);
        expect(inGrid).not.toContain(first.id);

        await selectPage(page, first.id);
        await expectGridToMatchBook(page);
        inGrid = await getGridPageIds(page);
        expect(inGrid[0]).toBe(first.id);
        expect(inGrid).not.toContain(last.id);
    });
});
