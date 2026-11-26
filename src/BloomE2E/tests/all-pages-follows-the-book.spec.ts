// With the Edit tab showing all the pages of the book, the pages around the page being edited
// follow what the user does to the book: adding, duplicating, removing and moving pages, a change
// made in the Format dialog, a different page size, and a different book (BL-16929).
//
// The tests are serial because each one starts from the books the one before it left behind.

import { expect, test } from "../fixtures/bloomTest";
import { addPageFromDialog, openAddPageDialog } from "../helpers/addPageDialog";
import { getBookAppearance, setBookAppearance } from "../helpers/bookSettings";
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
import { readBook, setBookMeta } from "../helpers/bookHtml";
import { holdNextRequest } from "../helpers/heldRequest";
import { isChangeLayoutMode, setChangeLayoutMode } from "../helpers/origami";
import {
    getUpdateBookProgressCount,
    isUpdateBookProgressShowing,
    holdAllPagesRequestForPageLayoutUpdate,
    holdPageLayoutUpdate,
    makeBookLookOlder,
    readPageLayoutUpdateLevel,
    startCountingUpdateBookProgress,
    waitForUpdateBookProgressToClose,
    waitForUpdateBookProgressToShow,
} from "../helpers/pageLayoutUpdate";
import {
    clickPageMenuItem,
    confirmRemovePageDialog,
    duplicatePageWithButton,
    escapeRemovePageDialog,
    getRemovalMark,
    movePageToSlotOf,
    openRemovePageDialog,
} from "../helpers/pageList";
import { askForPageSize, getPageSize, setPageSize } from "../helpers/pageSize";
import { selectPage } from "../helpers/pageThumbnails";
import {
    choosePageView,
    clickPageInGrid,
    clickPageViewSegment,
    clickTextOfPageInGrid,
    clickWhereOtherPageIsShown,
    getOtherPagesOnScreen,
    countGridLayoutChangesDuring,
    doesPageFrameScrollSideways,
    expectFacingPageBesidePageBeingEdited,
    expectGridToMatchBook,
    getChangeLayoutSwitchPlacement,
    getFacingPage,
    getFontSizeOfOtherPage,
    getGridPageIds,
    getGridPlaces,
    getMarginBoxOnPage,
    getPagePlacement,
    isOtherPageDrawnAtSize,
    isPageChangeUnderway,
    kMaxPagesInGrid,
    scrollPageFrameBy,
    waitForGridToHoldStill,
    waitForPageToBeShown,
} from "../helpers/pageView";
import {
    getWindowSize,
    resizeWindow,
    restoreWindowSize,
} from "../helpers/windowSize";
import {
    clickZoomButton,
    getZoom,
    setZoom,
    switchTab,
} from "../helpers/workspace";
import type { Page } from "@playwright/test";

test.use({
    collectionSpec: { name: "all-pages-follows", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

// A book of A5 portrait pages, and one made later whose pages are wider.
let bookFolder: string;
let widerBookFolder: string;
// A book that Bloom treats as a calendar.
let calendarFolder: string;

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

    test("the zoom buttons, and a window made narrower and wider, keep the page being edited where it is on screen, and each time the pages then hold still", async ({
        page,
    }) => {
        test.setTimeout(300000);
        const edited = (await getContentPages(page))[2];
        await goToPage(page, edited.id);
        await choosePageView(page, "all");
        await waitForGridToHoldStill(page);
        const { width, height } = await getWindowSize(page);
        const zoomBefore = (await getZoom(page)).zoom;

        // How many pages the row of the page being edited holds.
        const pagesInRow = async () => {
            const top = (await getPagePlacement(page, edited.id)).page.top;
            return Object.values(await getGridPlaces(page)).filter(
                (p) => Math.abs(p.top - top) < 2,
            ).length;
        };
        // Do one thing, then check that the page being edited is where it was, and that the
        // pages hold still. Returns how many pages the row of the page being edited holds. When
        // the rows take more or fewer pairs, the page goes to its place in the new row, so only its
        // height on screen can stay the same.
        const doAndCheck = async (
            name: string,
            act: () => Promise<unknown>,
        ): Promise<number> => {
            const before = (await getPagePlacement(page, edited.id)).page;
            const acrossBefore = await pagesInRow();

            // THE ACTION UNDER TEST
            await act();

            await waitForGridToHoldStill(page);
            const after = (await getPagePlacement(page, edited.id)).page;
            const acrossAfter = await pagesInRow();
            const where = `After "${name}", the page being edited moved from (${Math.round(before.left)}, ${Math.round(before.top)}) to (${Math.round(after.left)}, ${Math.round(after.top)}).`;
            if (acrossAfter === acrossBefore)
                expect(Math.abs(after.left - before.left), where).toBeLessThan(
                    3,
                );
            expect(Math.abs(after.top - before.top), where).toBeLessThan(3);
            expect(
                await countGridLayoutChangesDuring(page, 1000),
                `After "${name}", the pages did not hold still.`,
            ).toBe(0);
            return acrossAfter;
        };

        await doAndCheck("zoom out", () => clickZoomButton(page, "out"));
        await doAndCheck("zoom in", () => clickZoomButton(page, "in"));
        // Zoom out until a row holds two pairs, then make the window narrower in small steps
        // until it holds one, and wider again: one of those widths is where the rows go from one
        // pair across to two, the width where the view used to flick between the two.
        let across = await doAndCheck("zoom out", () =>
            clickZoomButton(page, "out"),
        );
        for (let i = 0; across < 4 && i < 10; i++)
            across = await doAndCheck("zoom out", () =>
                clickZoomButton(page, "out"),
            );
        expect(
            across,
            "test setup: a row should hold two pairs",
        ).toBeGreaterThanOrEqual(4);
        let w = width;
        while (across >= 4 && w > 500) {
            w -= 40;
            across = await doAndCheck(`make the window ${w}px wide`, () =>
                resizeWindow(page, { width: w, height }),
            );
        }
        expect(
            across,
            "test setup: a narrower window should hold one pair to a row",
        ).toBeLessThan(4);
        while (w < width) {
            w += 40;
            across = await doAndCheck(`make the window ${w}px wide`, () =>
                resizeWindow(page, { width: w, height }),
            );
        }
        expect(
            across,
            "Back at the starting width, a row should hold two pairs again.",
        ).toBeGreaterThanOrEqual(4);
        await restoreWindowSize(page);
        await setZoom(page, zoomBefore);
        await waitForGridToHoldStill(page);

        // Bloom is not left busy: the Collections tab opens promptly.
        const started = Date.now();
        await switchTab(page, "collection");
        expect(
            Date.now() - started,
            "The Collections tab took too long to open after the zooming and resizing.",
        ).toBeLessThan(10000);
        await switchTab(page, "edit");
        await waitForEditablePage(page);
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

    test("when Bloom starts again in a narrower window, All Pages zooms out so the pair fits, with no new choice of All Pages", async ({
        page,
        bloomApp,
    }) => {
        test.setTimeout(300000);
        // All Pages, fitted to the book in the window the run started with.
        await editBook(page, bookFolder);
        await choosePageView(page, "one");
        await choosePageView(page, "all");
        await expect.poll(() => doesPageFrameScrollSideways(page)).toBe(false);
        const fittedBefore = (await getZoom(page)).zoom;
        const { width, height } = await getWindowSize(page);

        // THE ACTION UNDER TEST: close Bloom, and open it again in a narrower window.
        const shell = await bloomApp.quitAndRestart();
        await resizeWindow(shell, { width: width - 250, height });
        await editBook(shell, bookFolder);

        await expectGridToMatchBook(shell);
        await expect
            .poll(async () => (await getZoom(shell)).zoom, {
                message:
                    "All Pages did not zoom out for a window too narrow for the pair at the old zoom.",
            })
            .toBeLessThan(fittedBefore);
        await expect.poll(() => doesPageFrameScrollSideways(shell)).toBe(false);
        await restoreWindowSize(shell);
        // The tests after this one work on the wider book.
        await editBook(shell, widerBookFolder);
        await expectGridToMatchBook(shell);
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

    test("after the book's appearance changes, every page around the one being edited is drawn in the new appearance", async ({
        page,
    }) => {
        test.setTimeout(300000);
        const contentPages = await getContentPages(page);
        const edited = contentPages[1];
        await goToPage(page, edited.id);
        await choosePageView(page, "all");
        await expectGridToMatchBook(page);
        // The theme gives front and back matter margins of their own, so compare content pages.
        const contentIds = new Set(contentPages.map((p) => p.id));
        const others = (await getGridPageIds(page)).filter(
            (id) => id !== edited.id && contentIds.has(id),
        );
        const before = await getMarginBoxOnPage(page, edited.id);
        const theme =
            (await getBookAppearance(page)).cssThemeName === "edge-to-edge"
                ? "default"
                : "edge-to-edge";

        // THE ACTION UNDER TEST: a new appearance, saved as OK in Book and Page Settings saves it.
        // A new appearance needs the page layout update, which runs behind its progress box.
        await setBookAppearance(page, { cssThemeName: theme });

        await expect
            .poll(
                async () =>
                    getMarginBoxOnPage(page, edited.id).catch(() => before),
                { timeout: kPageLayoutUpdateTimeout },
            )
            .not.toEqual(before);
        await waitForEditablePage(page);
        await waitForEditTabSettled(page);
        await expectGridToMatchBook(page);
        expect(await getShownPageId(page)).toBe(edited.id);
        const after = await getMarginBoxOnPage(page, edited.id);
        expect(
            others.length,
            "test setup: other content pages should be showing",
        ).toBeGreaterThan(2);
        for (const other of others)
            await expect
                .poll(
                    () => getMarginBoxOnPage(page, other).catch(() => before),
                    {
                        timeout: 30000,
                        message: `Page ${other} was not drawn in the new appearance.`,
                    },
                )
                .toEqual(after);
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

    test("after a page size change with One Page, choosing All Pages and clicking another page before the progress box goes to that page, and the update comes back to it", async ({
        page,
    }) => {
        test.setTimeout(600000);
        const pages = await getPages(page);
        const start = (await getContentPages(page))[2];
        const target = getFacingPage(pages, start.id);
        await goToPage(page, start.id);
        await choosePageView(page, "one");
        const newSize =
            (await getPageSize(page)) === "A5Landscape"
                ? "A5Portrait"
                : "A5Landscape";
        await setPageSize(page, newSize);
        await startCountingUpdateBookProgress(page);
        const held = await holdAllPagesRequestForPageLayoutUpdate(page);

        // THE ACTION UNDER TEST: choose All Pages, and click another page before the page that was
        // showing has asked Bloom for the update.
        await clickPageViewSegment(page, "all");
        await held.requested;
        await clickTextOfPageInGrid(page, target.id);

        // The page clicked asks for the update itself once it shows, and Bloom starts it then.
        await expect
            .poll(() => getUpdateBookProgressCount(page), {
                timeout: 60000,
                message:
                    "Bloom never brought the book up to date after the click on another page.",
            })
            .toBeGreaterThan(0);
        // Only now does the page that was showing ask, too late to start a second update.
        await held.release();
        await waitForGridAtPageSize(page, newSize);
        await expectGridToMatchBook(page);
        expect(
            await getShownPageId(page),
            "Bloom did not come back to the page that was clicked.",
        ).toBe(target.id);
        expect(await getUpdateBookProgressCount(page)).toBe(1);
    });

    test("a click on another page while the progress box is up changes nothing, and once it is done the page being edited is back and other pages open on a click", async ({
        page,
    }) => {
        test.setTimeout(600000);
        const pages = await getPages(page);
        const edited = (await getContentPages(page))[2];
        const other = getFacingPage(pages, edited.id);
        await goToPage(page, edited.id);
        await choosePageView(page, "all");
        await waitForEditTabSettled(page);
        const newSize =
            (await getPageSize(page)) === "A5Landscape"
                ? "A5Portrait"
                : "A5Landscape";
        const hold = await holdPageLayoutUpdate(page);
        await askForPageSize(page, newSize);
        await hold.requested;
        await waitForUpdateBookProgressToShow(page);

        // THE ACTION UNDER TEST: click another page, which shows behind the progress box.
        const [shownBehind] = await getOtherPagesOnScreen(page);
        expect(
            shownBehind,
            "test setup: another page should show behind the progress box",
        ).toBeDefined();
        await clickWhereOtherPageIsShown(page, shownBehind);

        await expect
            .poll(() => isPageChangeUnderway(page), {
                timeout: 5000,
                message:
                    "The wait cursor stayed over the pages after a click Bloom could not take.",
            })
            .toBe(false);
        expect(
            await isUpdateBookProgressShowing(page),
            "test setup: the progress box should still be up",
        ).toBe(true);
        await hold.release();
        await waitForUpdateBookProgressToClose(page);
        await waitForGridAtPageSize(page, newSize);
        expect(await getShownPageId(page)).toBe(edited.id);
        await clickTextOfPageInGrid(page, other.id);
        await waitForPageToBeShown(page, other.id);
    });

    test("Duplicate Page from the page list's menu, overtaken by a click on another page in the grid, goes to the page clicked and still duplicates the page right-clicked", async ({
        page,
    }) => {
        test.setTimeout(300000);
        // The page list offers its menu only on the page being edited.
        const rightClicked = (await getContentPages(page))[6];
        const clicked = getFacingPage(await getPages(page), rightClicked.id);
        await goToPage(page, rightClicked.id);
        await typeInGroup(page, kGroup, "en", "The page to duplicate");
        await choosePageView(page, "all");
        await waitForEditTabSettled(page);
        const before = await getPages(page);
        const held = await holdNextRequest(
            page,
            "**/bloom/api/pageList/contextMenuItemClicked*",
        );

        // THE ACTION UNDER TEST: Duplicate Page on one page, and at once a click on another page in
        // the grid, whose request reaches Bloom before the menu's.
        await clickPageMenuItem(page, rightClicked.id, "Duplicate Page");
        await held.requested;
        await clickTextOfPageInGrid(page, clicked.id);
        await waitForPageToBeShown(page, clicked.id);
        await held.release();

        await expect
            .poll(async () => (await getPages(page)).length, {
                timeout: 60000,
                message: "Bloom never duplicated the page.",
            })
            .toBe(before.length + 1);
        await waitForEditTabSettled(page);
        const after = await getPages(page);
        const copy = newPage(before, after);
        expect(
            after.findIndex((p) => p.id === copy.id),
            "The copy is not right after the page that was right-clicked.",
        ).toBe(after.findIndex((p) => p.id === rightClicked.id) + 1);
        await goToPage(page, rightClicked.id);
        await expect
            .poll(
                async () =>
                    (await readBook(page, widerBookFolder)).pages.find(
                        (p) => p.id === copy.id,
                    )?.text,
            )
            .toContain("The page to duplicate");
    });

    test("a calendar shows each upper page above the page it hangs over, with the front cover alone in the lower place", async ({
        page,
        bloomApp,
    }) => {
        test.setTimeout(600000);
        await choosePageView(page, "one");
        calendarFolder = await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Just Text", 28);
        // A calendar is a book whose defaultBookletLayout is Calendar. Bloom reads that only from
        // the saved book, so give it the meta while Bloom is stopped.
        await switchTab(page, "collection");
        const shell = await bloomApp.restart(() =>
            setBookMeta(calendarFolder, "defaultBookletLayout", "Calendar"),
        );
        await editBook(shell, calendarFolder);
        const pages = await getPages(shell);
        await goToPage(shell, pages[1].id);

        // THE ACTION UNDER TEST: choose All Pages in a calendar.
        await choosePageView(shell, "all");

        await expectGridToMatchBook(shell);
        const places = await getGridPlaces(shell);
        const where = (i: number) =>
            `${pages[i].caption} at (${Math.round(places[pages[i].id].left)}, ${Math.round(places[pages[i].id].top)})-(${Math.round(places[pages[i].id].right)}, ${Math.round(places[pages[i].id].bottom)})`;
        // Each page after the cover with an odd index is the upper page of its spread, and the
        // page after it hangs below it. (Near the end a blank page shifts this; the grid here
        // stops well before the end.)
        let pairs = 0;
        for (let i = 1; i + 1 < pages.length; i += 2) {
            const upper = places[pages[i].id];
            const lower = places[pages[i + 1].id];
            if (!upper || !lower) continue;
            pairs++;
            expect(
                Math.abs(upper.left - lower.left) < 2 &&
                    Math.abs(upper.bottom - lower.top) < 2,
                `${where(i)} should be directly above ${where(i + 1)}.`,
            ).toBe(true);
        }
        expect(
            pairs,
            "test setup: the grid should hold several spreads",
        ).toBeGreaterThan(5);
        // The cover stands alone in the lower place of the first spread, before page 1's spread.
        const cover = places[pages[0].id];
        const page1 = places[pages[1].id];
        expect(
            Math.abs(cover.top - page1.bottom) < 2 && cover.right <= page1.left,
            `The cover, ${where(0)}, should be in the lower place, to the left of ${where(1)}.`,
        ).toBe(true);
    });

    test("in a calendar longer than the grid, a page clicked in the bottom row opens exactly where it was on screen", async ({
        page,
    }) => {
        test.setTimeout(300000);
        const pages = await getPages(page);
        const edited = pages[Math.round((pages.length * 2) / 3)];
        await goToPage(page, edited.id);
        await expectGridToMatchBook(page);
        expect(
            (await getGridPageIds(page)).length,
            "test setup: the calendar should be longer than the grid",
        ).toBeLessThan(pages.length);
        await scrollPageFrameBy(page, 100000);
        const places = await getGridPlaces(page);
        const [targetId] = Object.entries(places)
            .filter(([id]) => id !== edited.id)
            .sort(
                (a, b) => b[1].bottom - a[1].bottom || a[1].left - b[1].left,
            )[0];
        const before = await getPagePlacement(page, targetId);
        const y =
            Math.min(
                (before.page.top + before.page.bottom) / 2,
                before.visible.bottom - 20,
            ) - before.visible.top;

        // THE ACTION UNDER TEST: a real click on a page in the bottom row.
        await clickPageInGrid(page, targetId, y);

        await waitForPageToBeShown(page, targetId);
        await waitForEditablePage(page);
        const after = await getPagePlacement(page, targetId);
        const describe = `It was at (${before.page.left}, ${before.page.top}) and opened at (${after.page.left}, ${after.page.top}).`;
        expect(
            Math.abs(after.page.left - before.page.left),
            describe,
        ).toBeLessThan(3);
        expect(
            Math.abs(after.page.top - before.page.top),
            describe,
        ).toBeLessThan(3);
    });

    test("a book from an older Bloom, opened with All Pages chosen, is brought up to date once, and going from page to page does not do it again", async ({
        page,
        bloomApp,
    }) => {
        test.setTimeout(600000);
        const level = readPageLayoutUpdateLevel(bookFolder);
        expect(level, "test setup: the book should record its level").toMatch(
            /^\d+$/,
        );
        // Bloom reopens the book it had selected, the calendar, not this one.
        await switchTab(page, "collection");
        const shell = await bloomApp.restart(() =>
            makeBookLookOlder(bookFolder),
        );
        await startCountingUpdateBookProgress(shell);

        // THE ACTION UNDER TEST: open the book, with All Pages chosen, and go from page to page.
        await editBook(shell, bookFolder);
        await expect
            .poll(() => readPageLayoutUpdateLevel(bookFolder), {
                timeout: kPageLayoutUpdateTimeout,
                message: "Bloom never brought the older book up to date.",
            })
            .toBe(level);
        await waitForEditablePage(shell);
        await expectGridToMatchBook(shell);
        for (const p of (await getContentPages(shell)).slice(0, 5)) {
            await selectPage(shell, p.id);
            await expectGridToMatchBook(shell);
        }
        await waitForEditTabSettled(shell);
        expect(
            await getUpdateBookProgressCount(shell),
            "The Update Book progress box should have come up exactly once.",
        ).toBe(1);
    });
});
