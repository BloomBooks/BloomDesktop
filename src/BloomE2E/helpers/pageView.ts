// Drive and read the view in the Edit tab's page frame: the page view chooser in the top bar (One
// Page, or All Pages, which lays the rest of the book out in spreads around the page being edited;
// see bookGridView.ts in BloomBrowserUI), and where a page is in the frame's scrolled view.
//
// Scrolling goes through the page frame's own window: that frame's document is what scrolls, and
// the Edit tab around it never does.

import { expect, type Locator, type Page } from "@playwright/test";
import {
    editablePageFrame,
    getPages,
    getShownPageId,
    type IBookPage,
} from "./bookMaking";

// The most pages the grid of All Pages lays out: kMaxPagesInGrid in
// src/BloomBrowserUI/bookEdit/js/bookGridLayout.ts. Change the two together.
export const kMaxPagesInGrid = 24;

/**
 * The pages, in order, that All Pages lays out around the page with this id in a book whose pages
 * are side by side (every book these tests make): the rule of getPagesToShow() in
 * bookGridLayout.ts, worked out here from the book's pages. The cover stands alone, then each left
 * page beside its right page; a book with an odd number of pages has a blank page (not one of the
 * book's pages) before the inside back cover, or before the back cover if the page before it is a
 * content page, and the back cover stands alone. Whole spreads are added after and before the page's
 * own spread in turn, while the pages stay within kMaxPagesInGrid.
 */
export function getPagesInGrid(
    pages: IBookPage[],
    editedId: string,
): IBookPage[] {
    const editedIndex = pages.findIndex((p) => p.id === editedId);
    if (editedIndex < 0)
        throw new Error(`Page ${editedId} is not one of the book's pages.`);
    const n = pages.length;
    let blankPageBefore: number | undefined;
    if (n >= 3 && n % 2 === 1)
        blankPageBefore = pages[n - 2].isContentPage ? n - 1 : n - 2;
    // Each spread's pages, by index; the cover is in the second place of the first spread.
    const spreads: number[][] = [];
    pages.forEach((_, index) => {
        const place =
            index +
            1 +
            (blankPageBefore !== undefined && index >= blankPageBefore ? 1 : 0);
        const spread = Math.floor(place / 2);
        while (spreads.length <= spread) spreads.push([]);
        spreads[spread].push(index);
    });
    let first = spreads.findIndex((s) => s.includes(editedIndex));
    let last = first;
    let count = spreads[first].length;
    let addAfterNext = true;
    for (;;) {
        const canAddAfter =
            last + 1 < spreads.length &&
            count + spreads[last + 1].length <= kMaxPagesInGrid;
        const canAddBefore =
            first > 0 && count + spreads[first - 1].length <= kMaxPagesInGrid;
        if (!canAddAfter && !canAddBefore) break;
        if (canAddAfter && (addAfterNext || !canAddBefore)) {
            last++;
            count += spreads[last].length;
            addAfterNext = false;
        } else {
            first--;
            count += spreads[first].length;
            addAfterNext = true;
        }
    }
    return spreads
        .slice(first, last + 1)
        .flat()
        .map((index) => pages[index]);
}

/** How many cells the grid of All Pages has around the page being edited now. */
export async function getExpectedGridCellCount(page: Page): Promise<number> {
    const shown = await getShownPageId(page);
    if (!shown) throw new Error("The Edit tab is not showing a page.");
    return getPagesInGrid(await getPages(page), shown).length;
}

/** Where something is on screen, in the page frame's viewport. */
export interface IViewRect {
    left: number;
    top: number;
    right: number;
    bottom: number;
}

/** A page's box, and the part of the page frame's viewport not covered by the bar of controls across its top. */
export interface IPagePlacement {
    page: IViewRect;
    visible: IViewRect;
}

/**
 * Click one of the page view chooser's two segments, one page at a time or all pages, and return at
 * once. Use choosePageView unless the click starts something that changes the book's pages, such as
 * a page layout update, before the grid can be counted.
 */
export async function clickPageViewSegment(
    page: Page,
    view: "one" | "all",
): Promise<void> {
    await page
        .getByTestId(view === "all" ? "view-all-pages" : "view-one-page")
        .click();
}

/**
 * Click one of the page view chooser's two segments: one page at a time, or all pages. With All
 * Pages, returns once every page that the grid lays out (see getPagesInGrid()) has its place in the
 * page frame; with One Page, once none has.
 */
export async function choosePageView(
    page: Page,
    view: "one" | "all",
): Promise<void> {
    await clickPageViewSegment(page, view);
    await expect(
        editablePageFrame(page).locator(".bloom-book-grid-cell"),
        `The page frame never showed ${view === "all" ? "all the pages" : "one page"}.`,
    ).toHaveCount(view === "all" ? await getExpectedGridCellCount(page) : 0, {
        timeout: 30000,
    });
}

/**
 * Where the page with this id is in the page frame's view: the page being edited itself, or, with
 * All Pages, the place of another page.
 */
export async function getPagePlacement(
    page: Page,
    pageId: string,
): Promise<IPagePlacement> {
    return editablePageFrame(page).evaluate((id) => {
        const edited = document.querySelector(".bloom-page") as HTMLElement;
        const box =
            edited.id === id
                ? edited
                : document.querySelector(
                      `.bloom-book-grid-cell[data-page-id="${id}"]`,
                  );
        if (!box)
            throw new Error(
                `The page frame is not showing page ${id}. It is editing ${edited.id}` +
                    (document.querySelector(".bloom-book-grid-cell")
                        ? ", with all the pages showing."
                        : ", with only that page showing."),
            );
        const r = box.getBoundingClientRect();
        const bar = document.querySelector("body > .bloom-controls-bar");
        return {
            page: {
                left: r.left,
                top: r.top,
                right: r.right,
                bottom: r.bottom,
            },
            visible: {
                left: 0,
                top: bar ? bar.getBoundingClientRect().bottom : 0,
                right: document.documentElement.clientWidth,
                bottom: document.documentElement.clientHeight,
            },
        };
    }, pageId);
}

/** True when every part of the page is inside the visible part of the view. */
export function isWhollyInView(placement: IPagePlacement): boolean {
    const { page: p, visible: v } = placement;
    return (
        p.left >= v.left - 1 &&
        p.top >= v.top - 1 &&
        p.right <= v.right + 1 &&
        p.bottom <= v.bottom + 1
    );
}

/** True when no part of the page is inside the visible part of the view. */
export function isWhollyOutOfView(placement: IPagePlacement): boolean {
    const { page: p, visible: v } = placement;
    return (
        p.right <= v.left ||
        p.left >= v.right ||
        p.bottom <= v.top ||
        p.top >= v.bottom
    );
}

/**
 * Scroll the page frame as far from the page with this id as it goes (to the top or the bottom of
 * the book, whichever is further), and check the page is then entirely out of view. Throws, saying
 * where the page ended up, when the book is too short for that.
 */
export async function scrollPageOutOfView(
    page: Page,
    pageId: string,
): Promise<void> {
    const before = await getPagePlacement(page, pageId);
    const pageMiddle = (before.page.top + before.page.bottom) / 2;
    const viewMiddle = (before.visible.top + before.visible.bottom) / 2;
    await editablePageFrame(page).evaluate((toTheBottom) => {
        window.scrollTo(
            window.scrollX,
            toTheBottom ? document.documentElement.scrollHeight : 0,
        );
    }, pageMiddle <= viewMiddle);
    const after = await getPagePlacement(page, pageId);
    if (!isWhollyOutOfView(after))
        throw new Error(
            `The page frame cannot scroll page ${pageId} out of view: at the far end it runs ` +
                `from y=${after.page.top} to y=${after.page.bottom}, and the view from ` +
                `y=${after.visible.top} to y=${after.visible.bottom}. The book needs more pages ` +
                `(or a larger zoom).`,
        );
}

/**
 * Scroll the page frame so the top of the page with this id is `pixels` above the top of the
 * visible part of the view (below the bar of controls), leaving the rest of it on screen. Returns
 * where the page then is.
 */
export async function scrollPageTopAboveView(
    page: Page,
    pageId: string,
    pixels: number,
): Promise<IPagePlacement> {
    const before = await getPagePlacement(page, pageId);
    const dy = before.page.top - (before.visible.top - pixels);
    await editablePageFrame(page).evaluate((dy) => window.scrollBy(0, dy), dy);
    const after = await getPagePlacement(page, pageId);
    if (Math.abs(after.page.top - (after.visible.top - pixels)) > 1)
        throw new Error(
            `The page frame could not scroll page ${pageId} to ${pixels}px above the view: its top ` +
                `is at y=${after.page.top}, and the view starts at y=${after.visible.top}.`,
        );
    return after;
}

/**
 * With All Pages showing, click another page with the mouse, as a user does to edit it: across the
 * middle of it, `yInView` pixels below the top of the visible part of the view. Throws if that
 * point is not on the page.
 */
export async function clickPageInGrid(
    page: Page,
    pageId: string,
    yInView: number,
): Promise<void> {
    const placement = await getPagePlacement(page, pageId);
    const x = (placement.page.left + placement.page.right) / 2;
    const y = placement.visible.top + yInView;
    if (y < placement.page.top || y > placement.page.bottom)
        throw new Error(
            `The point ${yInView}px down the view (y=${y}) is not on page ${pageId}, which runs ` +
                `from y=${placement.page.top} to y=${placement.page.bottom}.`,
        );
    const frameBox = await page.locator("#page").boundingBox();
    if (!frameBox) throw new Error("The page frame has no box on screen.");
    await page.mouse.click(frameBox.x + x, frameBox.y + y);
}

/**
 * The ids of the other pages (not the page being edited) that are at least partly on screen in the
 * page frame that is showing, which while a page change or a page layout update is under way can be
 * the page being left.
 */
export async function getOtherPagesOnScreen(page: Page): Promise<string[]> {
    return page.evaluate(() => {
        const frame = (document.getElementById("page-outgoing") ??
            document.getElementById("page")) as HTMLIFrameElement | null;
        const doc = frame?.contentDocument;
        if (!frame || !doc) return [];
        const f = frame.getBoundingClientRect();
        return Array.from(
            doc.querySelectorAll(
                ".bloom-book-grid-cell:not(.bloom-book-grid-edited)",
            ),
        )
            .filter((cell) => {
                const r = cell.getBoundingClientRect();
                return (
                    r.bottom > 0 &&
                    r.right > 0 &&
                    r.top < f.height &&
                    r.left < f.width
                );
            })
            .map((cell) => cell.getAttribute("data-page-id") ?? "");
    });
}

/**
 * Click with the mouse in the middle of where another page is drawn on screen, whatever is on top
 * of it at the moment (a dialog's backdrop, say), as a person clicks what they see. Uses the page
 * frame that is on screen: while a page change or a page layout update is under way, that can be
 * the page being left.
 */
export async function clickWhereOtherPageIsShown(
    page: Page,
    pageId: string,
): Promise<void> {
    const point = await page.evaluate((id) => {
        const frame = (document.getElementById("page-outgoing") ??
            document.getElementById("page")) as HTMLIFrameElement | null;
        const cell = frame?.contentDocument?.querySelector(
            `.bloom-book-grid-cell[data-page-id="${id}"]`,
        );
        if (!frame || !cell) return undefined;
        const f = frame.getBoundingClientRect();
        const r = cell.getBoundingClientRect();
        // The middle of the part of the page that is inside the frame.
        const top = Math.max(r.top, 0);
        const bottom = Math.min(r.bottom, f.height);
        const left = Math.max(r.left, 0);
        const right = Math.min(r.right, f.width);
        if (bottom <= top || right <= left) return undefined;
        return {
            x: f.left + (left + right) / 2,
            y: f.top + (top + bottom) / 2,
        };
    }, pageId);
    if (!point)
        throw new Error(
            `Page ${pageId} is not drawn on screen, so there is nowhere to click it.`,
        );
    await page.mouse.click(point.x, point.y);
}

/**
 * Wait until the page being edited has finished dealing with the click that opened it (when it
 * was one of the other pages, Bloom replays that click on it; see
 * replayTheClickThatOpenedThisPage in bookGridView.ts).
 */
export async function waitForClickedPageToSettle(page: Page): Promise<void> {
    await expect
        .poll(
            () =>
                editablePageFrame(page)
                    .evaluate(() =>
                        document.documentElement.classList.contains(
                            "bloom-book-grid-opening-click-done",
                        ),
                    )
                    .catch(() => false),
            {
                timeout: 30000,
                message:
                    "The page being edited never finished dealing with the click that opened it.",
            },
        )
        .toBe(true);
}

/**
 * True while the Edit tab is changing pages: from a click on another page until the new page
 * shows (the wait cursor covers the page frame, and the page being left may still be on screen).
 */
export async function isPageChangeUnderway(page: Page): Promise<boolean> {
    return page.evaluate(
        () =>
            !!document.getElementById("page-loading-cover") ||
            !!document.getElementById("page-outgoing"),
    );
}

/**
 * The page that shares a spread with the page with this id, in a book whose pages are side by side
 * and read left to right: the front cover (the first page) stands alone on the right, so the page
 * after it is a left page. Not for the last pages of a book that has a blank page before its back
 * cover (see getPagesInGrid()).
 */
export function getFacingPage(pages: IBookPage[], pageId: string): IBookPage {
    const index = pages.findIndex((p) => p.id === pageId);
    if (index < 1)
        throw new Error(
            `Page ${pageId} is the front cover, or not one of the book's pages, so no page faces it.`,
        );
    return pages[index % 2 === 1 ? index + 1 : index - 1];
}

/**
 * With All Pages showing, check that the page being edited and the page facing it sit side by side:
 * tops level, the same height, and the edges between them touching.
 */
export async function expectFacingPageBesidePageBeingEdited(
    page: Page,
): Promise<void> {
    const pages = await getPages(page);
    const editedId = await getShownPageId(page);
    if (!editedId) throw new Error("The Edit tab is not showing a page.");
    const editedIndex = pages.findIndex((p) => p.id === editedId);
    const facing = getFacingPage(pages, editedId);
    await gridCell(page, facing.id).scrollIntoViewIfNeeded();
    const e = (await getPagePlacement(page, editedId)).page;
    const f = (await getPagePlacement(page, facing.id)).page;
    const describe =
        `The page being edited is at (${e.left}, ${e.top})-(${e.right}, ${e.bottom}) and the ` +
        `page facing it at (${f.left}, ${f.top})-(${f.right}, ${f.bottom}).`;
    expect(Math.abs(e.top - f.top), describe).toBeLessThan(2);
    expect(Math.abs(e.bottom - f.bottom), describe).toBeLessThan(2);
    // A left page (an odd index) has its facing page on its right.
    if (editedIndex % 2 === 1)
        expect(Math.abs(f.left - e.right), describe).toBeLessThan(2);
    else expect(Math.abs(e.left - f.right), describe).toBeLessThan(2);
}

/** The place the grid of All Pages gives the page with this id. */
export function gridCell(page: Page, pageId: string): Locator {
    return editablePageFrame(page).locator(
        `.bloom-book-grid-cell[data-page-id="${pageId}"]`,
    );
}

/**
 * Where All Pages puts each page it lays out, the page being edited among them, in the page frame's
 * viewport, by page id. Every place is there whether or not its page is drawn yet.
 */
export async function getGridPlaces(
    page: Page,
): Promise<Record<string, IViewRect>> {
    return editablePageFrame(page)
        .locator(".bloom-book-grid-cell")
        .evaluateAll((cells) => {
            const places: Record<string, IViewRect> = {};
            for (const cell of cells) {
                const r = cell.getBoundingClientRect();
                places[cell.getAttribute("data-page-id") ?? "?"] = {
                    left: r.left,
                    top: r.top,
                    right: r.right,
                    bottom: r.bottom,
                };
            }
            return places;
        });
}

/** The ids of the pages that the grid of All Pages has places for, in the order of the places. */
export async function getGridPageIds(page: Page): Promise<string[]> {
    return editablePageFrame(page)
        .locator(".bloom-book-grid-cell")
        .evaluateAll((cells) =>
            cells.map((c) => c.getAttribute("data-page-id") ?? ""),
        );
}

/**
 * Wait until the grid of All Pages holds exactly the pages that belong around the page being edited
 * (see getPagesInGrid()), in the book's order as Bloom reports it now. Use it after something that
 * changes the book's pages or their order.
 */
export async function expectGridToMatchBook(
    page: Page,
    timeoutMs = 30000,
): Promise<void> {
    let expected: string[] = [];
    let found: string[] = [];
    try {
        await expect
            .poll(
                async () => {
                    // The page frame is replaced while the page changes, and reading it then throws.
                    try {
                        const shown = await getShownPageId(page);
                        if (!shown) return false;
                        expected = getPagesInGrid(
                            await getPages(page),
                            shown,
                        ).map((p) => p.id);
                        found = await getGridPageIds(page);
                    } catch {
                        return false;
                    }
                    return (
                        expected.length === found.length &&
                        expected.every((id, i) => id === found[i])
                    );
                },
                { timeout: timeoutMs },
            )
            .toBe(true);
    } catch {
        throw new Error(
            `The grid of All Pages does not hold the pages around the page being edited. ` +
                `Expected [${expected.join(", ")}], found [${found.join(", ")}].`,
        );
    }
}

/**
 * One language's text box in one translation group of another page, as All Pages draws it (not the
 * page being edited). Scrolls that page into view, which is what makes the grid draw it.
 */
export async function textBoxOfOtherPage(
    page: Page,
    pageId: string,
    groupIndex = 0,
    languageTag = "en",
): Promise<Locator> {
    const cell = gridCell(page, pageId);
    await cell.scrollIntoViewIfNeeded();
    const box = cell
        .frameLocator("iframe")
        .locator(".bloom-translationGroup")
        .nth(groupIndex)
        .locator(`.bloom-editable[lang="${languageTag}"]`)
        .first();
    await expect(
        box,
        `All Pages never drew the text box of page ${pageId}.`,
    ).toBeVisible({ timeout: 15000 });
    return box;
}

/**
 * With All Pages showing, click with the mouse in a text box of another page, as a user does to
 * start typing there: on the middle of its text, or in the middle of the box when it is empty.
 * Returns at once, without waiting for that page to become the page being edited; follow it with
 * waitForPageToBeShown().
 */
export async function clickTextOfPageInGrid(
    page: Page,
    pageId: string,
    groupIndex = 0,
): Promise<void> {
    const box = await textBoxOfOtherPage(page, pageId, groupIndex);
    // Where the middle of the text is, as shares of the box, so that the scaling of the grid
    // does not matter.
    const at = await box.evaluate((element) => {
        const boxRect = element.getBoundingClientRect();
        // The first piece of text itself: a range around the paragraph would give the paragraph's
        // box, which is as wide as the text box whatever the text's length.
        const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
        let textNode = walker.nextNode();
        while (textNode && !textNode.textContent?.trim())
            textNode = walker.nextNode();
        if (!textNode) return { x: 0.5, y: 0.5 };
        const range = document.createRange();
        range.selectNodeContents(textNode);
        const lines = Array.from(range.getClientRects()).filter(
            (r) => r.width > 0 && r.height > 0,
        );
        if (lines.length === 0) return { x: 0.5, y: 0.5 };
        const line = lines[0];
        return {
            x: (line.left + line.width / 2 - boxRect.left) / boxRect.width,
            y: (line.top + line.height / 2 - boxRect.top) / boxRect.height,
        };
    });
    const pointOnScreen = async () => {
        const onScreen = await box.boundingBox();
        if (!onScreen)
            throw new Error(
                `The text box of page ${pageId} has no box on screen.`,
            );
        return {
            x: onScreen.x + at.x * onScreen.width,
            y: onScreen.y + at.y * onScreen.height,
        };
    };
    let point = await pointOnScreen();
    // A point under the bar of controls across the top of the view, or below the view, is not
    // one a person could click; scroll it to the middle of the view first.
    const frameBox = await page.locator("#page").boundingBox();
    if (!frameBox) throw new Error("The page frame has no box on screen.");
    const barBottom = await editablePageFrame(page).evaluate(
        () =>
            document
                .querySelector("body > .bloom-controls-bar")
                ?.getBoundingClientRect().bottom ?? 0,
    );
    const yInFrame = point.y - frameBox.y;
    if (yInFrame < barBottom + 10 || yInFrame > frameBox.height - 10) {
        await editablePageFrame(page).evaluate(
            (dy) => window.scrollBy(0, dy),
            yInFrame - (barBottom + frameBox.height) / 2,
        );
        point = await pointOnScreen();
    }
    await page.mouse.click(point.x, point.y);
}

/**
 * Wait until the page with this id is the page being edited and is on screen: in the page frame,
 * with the cover that the Edit tab puts over the pages during a change taken away. This is the
 * moment a user first sees the page; Bloom may still be setting up its text boxes.
 */
export async function waitForPageToBeShown(
    page: Page,
    pageId: string,
): Promise<void> {
    await expect
        .poll(() => getShownPageId(page), {
            timeout: 30000,
            message: `Page ${pageId} never became the page being edited.`,
        })
        .toBe(pageId);
    await expect
        .poll(() => isPageChangeUnderway(page), {
            timeout: 30000,
            message: `The Edit tab never finished showing page ${pageId}.`,
        })
        .toBe(false);
}

/**
 * Which translation group of the page being edited holds the text box that has the focus, counting
 * from 0 in document order, or undefined when no text box has it.
 */
export async function getFocusedGroupIndex(
    page: Page,
): Promise<number | undefined> {
    const index = await editablePageFrame(page).evaluate(() => {
        const group = document.activeElement?.closest(
            ".bloom-page .bloom-translationGroup",
        );
        if (!group || !document.activeElement?.closest(".bloom-editable"))
            return -1;
        return Array.from(
            document.querySelectorAll(".bloom-page .bloom-translationGroup"),
        ).indexOf(group);
    });
    return index < 0 ? undefined : index;
}

/** Wait until the text box in this translation group of the page being edited has the focus. */
export async function waitForFocusInGroup(
    page: Page,
    groupIndex: number,
): Promise<void> {
    await expect
        .poll(() => getFocusedGroupIndex(page).catch(() => undefined), {
            timeout: 30000,
            message: `The text box in group ${groupIndex} of the page being edited never got the focus.`,
        })
        .toBe(groupIndex);
}

/** The font size, in points, of the first text box of another page as All Pages draws it. */
export async function getFontSizeOfOtherPage(
    page: Page,
    pageId: string,
): Promise<number> {
    const box = await textBoxOfOtherPage(page, pageId);
    const px = await box.evaluate((element) =>
        parseFloat(getComputedStyle(element).fontSize),
    );
    return Math.round(px * 0.75 * 10) / 10;
}

/** Whether All Pages draws another page at this page size and orientation, e.g. "A5Portrait". */
export async function isOtherPageDrawnAtSize(
    page: Page,
    pageId: string,
    layoutId: string,
): Promise<boolean> {
    const cell = gridCell(page, pageId);
    await cell.scrollIntoViewIfNeeded();
    return cell
        .frameLocator("iframe")
        .locator(".bloom-page")
        .first()
        .evaluate(
            (element, layout) => element.classList.contains(layout),
            layoutId,
            { timeout: 15000 },
        );
}

/**
 * Where the margin box (the area inside the page's margins that holds its content) sits on the page
 * with this id, as fractions of the page's width and height, so that the grid's scaling does not
 * matter: the page being edited, or another page as All Pages draws it. The page's theme decides
 * the margins, so this tells whether a page is drawn in the book's current appearance.
 */
export async function getMarginBoxOnPage(
    page: Page,
    pageId: string,
): Promise<IViewRect> {
    const measure = (bloomPage: Element) => {
        const p = bloomPage.getBoundingClientRect();
        const box = bloomPage.querySelector(".marginBox");
        if (!box) throw new Error("The page has no margin box.");
        const m = box.getBoundingClientRect();
        const round = (n: number) => Math.round(n * 1000) / 1000;
        return {
            left: round((m.left - p.left) / p.width),
            top: round((m.top - p.top) / p.height),
            right: round((p.right - m.right) / p.width),
            bottom: round((p.bottom - m.bottom) / p.height),
        };
    };
    if ((await getShownPageId(page)) === pageId)
        return editablePageFrame(page)
            .locator(".bloom-page")
            .first()
            .evaluate(measure);
    const cell = gridCell(page, pageId);
    await cell.scrollIntoViewIfNeeded();
    return cell
        .frameLocator("iframe")
        .locator(".bloom-page")
        .first()
        .evaluate(measure, undefined, { timeout: 15000 });
}

/** The first picture on a page, as the page shows it; see getPictureOnPage(). */
export interface IShownPicture {
    /** The picture's address, with the query Bloom put on it (thumbnail=1, transparent=...). */
    src: string;
    /** The size of the picture as the server sent it, in pixels. */
    naturalWidth: number;
    naturalHeight: number;
    /** The alpha (0 to 255) of the picture's top left pixel as the server sent it. */
    topLeftAlpha: number;
}

/**
 * The first picture on the page with this id, as it is shown: on the page being edited, or on
 * another page as All Pages draws it (which this scrolls into view, so that it is drawn).
 */
export async function getPictureOnPage(
    page: Page,
    pageId: string,
): Promise<IShownPicture> {
    const describe = async (img: HTMLImageElement) => {
        if (!img.complete || !img.naturalWidth)
            await new Promise((resolve) => {
                img.addEventListener("load", resolve, { once: true });
                img.addEventListener("error", resolve, { once: true });
            });
        const canvas = img.ownerDocument.createElement("canvas");
        canvas.width = img.naturalWidth;
        canvas.height = img.naturalHeight;
        const context = canvas.getContext("2d")!;
        context.drawImage(img, 0, 0);
        return {
            src: img.getAttribute("src") ?? "",
            naturalWidth: img.naturalWidth,
            naturalHeight: img.naturalHeight,
            topLeftAlpha: context.getImageData(0, 0, 1, 1).data[3],
        };
    };
    const selector =
        ".bloom-page .bloom-imageContainer img, .bloom-page .bloom-canvas img";
    if ((await getShownPageId(page)) === pageId)
        return editablePageFrame(page)
            .locator(selector)
            .first()
            .evaluate(describe);
    const cell = gridCell(page, pageId);
    await cell.scrollIntoViewIfNeeded();
    return cell
        .frameLocator("iframe")
        .locator(selector)
        .first()
        .evaluate(describe, undefined, { timeout: 15000 });
}

/** Whether the page frame's view is wider than the frame, so that it scrolls sideways. */
export async function doesPageFrameScrollSideways(
    page: Page,
): Promise<boolean> {
    return editablePageFrame(page).evaluate(() => {
        const view = document.scrollingElement!;
        // A pixel of rounding is not scrolling.
        return view.scrollWidth > view.clientWidth + 1;
    });
}

/**
 * Where the Change Layout switch is in the page frame's view. It sits in the bar of controls across
 * the top of the frame. Throws when the page being edited offers no such switch.
 */
export async function getChangeLayoutSwitchPlacement(
    page: Page,
): Promise<IViewRect> {
    return editablePageFrame(page).evaluate(() => {
        const label = document.querySelector('label[for="changeLayoutToggle"]');
        if (!label)
            throw new Error(
                "The page being edited shows no Change Layout switch.",
            );
        const r = label.getBoundingClientRect();
        return {
            left: Math.round(r.left),
            top: Math.round(r.top),
            right: Math.round(r.right),
            bottom: Math.round(r.bottom),
        };
    });
}

/**
 * With All Pages showing, wait until every other page that is at least partly on screen in the page
 * frame is drawn.
 */
export async function waitForOtherPagesOnScreenToBeDrawn(
    page: Page,
): Promise<void> {
    let undrawn: string[] = [];
    await expect
        .poll(
            async () => {
                undrawn = await editablePageFrame(page)
                    .evaluate(() =>
                        Array.from(
                            document.querySelectorAll(
                                ".bloom-book-grid-cell:not(.bloom-book-grid-edited):not(.bloom-book-grid-rendered)",
                            ),
                        )
                            .filter((cell) => {
                                const r = cell.getBoundingClientRect();
                                return (
                                    r.bottom > 0 &&
                                    r.right > 0 &&
                                    r.top < window.innerHeight &&
                                    r.left < window.innerWidth
                                );
                            })
                            .map(
                                (cell) =>
                                    cell.getAttribute("data-page-id") ?? "?",
                            ),
                    )
                    .catch(() => ["(the page frame is changing)"]);
                return undrawn.length;
            },
            {
                timeout: 30000,
                message: `All Pages never drew every page on screen: ${undrawn.join(", ")} stayed undrawn.`,
            },
        )
        .toBe(0);
}

// Where every place in the grid and the page being edited are in the page frame's view, and how far
// it is scrolled, rounded to whole pixels: a layout that changes changes this.
function gridLayoutNow(): string {
    const rects = Array.from(
        document.querySelectorAll(".bloom-book-grid-cell, .bloom-page"),
    ).map((e) => {
        const r = e.getBoundingClientRect();
        return `${Math.round(r.left)},${Math.round(r.top)},${Math.round(r.width)}`;
    });
    return `${Math.round(window.scrollX)},${Math.round(window.scrollY)}|${rects.join(";")}`;
}

/**
 * Wait until the layout of the page frame (every page's place, and the scroll) has stayed the same
 * for `quietMs`. Fails, saying so, if it keeps changing for 30 seconds: a view that never settles.
 */
export async function waitForGridToHoldStill(
    page: Page,
    quietMs = 1000,
): Promise<void> {
    let last = "";
    let since = Date.now();
    await expect
        .poll(
            async () => {
                const now = await editablePageFrame(page)
                    .evaluate(gridLayoutNow)
                    .catch(() => "(the page frame is changing)");
                if (now !== last) {
                    last = now;
                    since = Date.now();
                }
                return Date.now() - since >= quietMs;
            },
            {
                intervals: [100],
                timeout: 30000,
                message: `The pages never held still for ${quietMs}ms.`,
            },
        )
        .toBe(true);
}

/**
 * Watch the page frame on every animation frame for `ms`, and return how many times its layout
 * (see waitForGridToHoldStill()) changed.
 */
export async function countGridLayoutChangesDuring(
    page: Page,
    ms: number,
): Promise<number> {
    return editablePageFrame(page).evaluate(
        (ms) =>
            new Promise<number>((resolve) => {
                // The same reading as gridLayoutNow(), which cannot be passed in.
                const layoutNow = () => {
                    const rects = Array.from(
                        document.querySelectorAll(
                            ".bloom-book-grid-cell, .bloom-page",
                        ),
                    ).map((e) => {
                        const r = e.getBoundingClientRect();
                        return `${Math.round(r.left)},${Math.round(r.top)},${Math.round(r.width)}`;
                    });
                    return `${Math.round(window.scrollX)},${Math.round(window.scrollY)}|${rects.join(";")}`;
                };
                let last = layoutNow();
                let changes = 0;
                const end = performance.now() + ms;
                const look = () => {
                    const now = layoutNow();
                    if (now !== last) {
                        changes++;
                        last = now;
                    }
                    if (performance.now() < end) requestAnimationFrame(look);
                    else resolve(changes);
                };
                requestAnimationFrame(look);
            }),
        ms,
    );
}

/** What the Edit tab showed on one animation frame; see startRecordingEachFrame(). */
export interface IRecordedFrame {
    /** The id of the page frame on top: "page-outgoing" while a page being left is still shown. */
    frame: string;
    /** The id of the page being edited in that frame, or undefined when it shows no page. */
    pageId?: string;
    /** Whether that frame lays out the other pages of the book around it (All Pages). */
    showsOtherPages: boolean;
    /** Other pages that are at least partly on screen and not drawn. */
    undrawnPagesOnScreen: string[];
    /** The left edge of the page list in the Edit tab. */
    pageListLeft: number;
    /** Every container around the page list that is scrolled, if any. */
    scrolledAroundPageList: string;
}

/**
 * From now on, on every animation frame of the Edit tab, record which page frame is on top, the
 * page it shows, which of the other pages on screen are not drawn, and where the page list sits.
 * The recording sees layout and the DOM, not painted pixels. Read it with stopRecordingEachFrame().
 */
export async function startRecordingEachFrame(page: Page): Promise<void> {
    await page.evaluate(() => {
        const w = window as unknown as Record<string, unknown>;
        const frames: unknown[] = [];
        w.__bloomE2eFrames = frames;
        w.__bloomE2eRecording = true;
        const record = () => {
            const top = (document.getElementById("page-outgoing") ??
                document.getElementById("page")) as HTMLIFrameElement | null;
            const doc = top?.contentDocument;
            const view = top?.getBoundingClientRect();
            const undrawn: string[] = [];
            const cells = doc
                ? Array.from(doc.querySelectorAll(".bloom-book-grid-cell"))
                : [];
            for (const cell of cells) {
                if (cell.classList.contains("bloom-book-grid-edited")) continue;
                const r = cell.getBoundingClientRect();
                const onScreen =
                    !!view &&
                    r.bottom > 0 &&
                    r.right > 0 &&
                    r.top < view.height &&
                    r.left < view.width;
                if (
                    onScreen &&
                    !cell.classList.contains("bloom-book-grid-rendered")
                )
                    undrawn.push(cell.getAttribute("data-page-id") ?? "?");
            }
            const list = document.getElementById("pageList");
            let scrolled = "";
            for (let e = list?.parentElement; e; e = e.parentElement) {
                if (e.scrollLeft !== 0 || e.scrollTop !== 0)
                    scrolled += `${e.tagName}#${e.id}.${e.className} (${e.scrollLeft},${e.scrollTop}) `;
            }
            frames.push({
                frame: top?.id ?? "(no page frame)",
                pageId: doc?.querySelector(".bloom-page")?.id || undefined,
                showsOtherPages: cells.length > 0,
                undrawnPagesOnScreen: undrawn,
                pageListLeft: Math.round(
                    list?.getBoundingClientRect().left ?? -1,
                ),
                scrolledAroundPageList: scrolled,
            });
            if (w.__bloomE2eRecording) requestAnimationFrame(record);
        };
        requestAnimationFrame(record);
    });
}

/** Stop the recording that startRecordingEachFrame() started, and return it. */
export async function stopRecordingEachFrame(
    page: Page,
): Promise<IRecordedFrame[]> {
    return page.evaluate(() => {
        const w = window as unknown as Record<string, unknown>;
        w.__bloomE2eRecording = false;
        return (w.__bloomE2eFrames ?? []) as IRecordedFrame[];
    });
}

/**
 * Scroll the page frame's view down (or, with a negative number, up) by this many pixels, and
 * return how far it went.
 */
export async function scrollPageFrameBy(
    page: Page,
    pixels: number,
): Promise<number> {
    return editablePageFrame(page).evaluate((dy) => {
        const before = window.scrollY;
        window.scrollBy(0, dy);
        return window.scrollY - before;
    }, pixels);
}
