// Where each page goes in the view of the book around the page being edited (see bookGridView.ts),
// and which of the book's pages that view shows (in a long book, only those near the page being
// edited; see getPagesToShow()). The pages are grouped the way the finished book is read:
// - a book folded down the side shows each left page beside the right page facing it;
// - a calendar (a book whose defaultBookletLayout meta is "Calendar"; see
//   Book.GetBookletLayoutMethod() in C#) folds across the middle, so each page hangs above the
//   page facing it;
// - a book sized for a screen (see kScrollingLayouts) has no facing pages, so each page stands
//   by itself.
// In the first two the front cover has no facing page: it stands alone where a right page (or,
// in a calendar, a lower page) would be.
// A book whose first language reads right to left is read from the other side, so everything is
// mirrored: the front cover stands where a left page would be, and each row runs right to left.
// With facing pages, the inside back cover and the back cover are the two sides of the last sheet,
// so the back cover stands alone too. When the pages alone would put the back cover beside the
// inside back cover, the printed book has a blank page before the inside back cover, and so does
// the layout (see getIndexOfPageAfterBlankPage()).

import { kScrollingLayouts } from "./scrollingLayouts";

export interface ISpreadShape {
    pagesPerSpread: 1 | 2;
    // True if the two pages of a spread are one above the other, rather than side by side.
    pagesAreStacked: boolean;
}

// Horizontal space between one spread and the next.
export const kSpreadGap = 40;
// Vertical space between rows.
export const kRowGap = 40;

// The most pages of the book the grid lays out, the page being edited among them (see
// getPagesToShow()). It bounds how many pages are fetched and drawn when the page changes (each
// drawn page costs tens of milliseconds) and how much memory the grid's page frames take: at a
// small zoom nearly all of them are on screen at once. The number is to be tuned on a slow machine.
export const kMaxPagesInGrid = 24;

/** The pages the grid shows: the indexes in the book of the first and the last of them. */
export interface IPageRange {
    first: number;
    last: number;
}

export interface IGridLayout {
    spreadsPerRow: number;
    spreadWidth: number;
    spreadHeight: number;
    // Where the page at this index in the book goes, relative to the top left of the grid.
    positionOfPage(pageIndex: number): { x: number; y: number };
    // Where the blank page goes, if the layout has one and it is among the spreads shown.
    positionOfBlankPage(): { x: number; y: number } | undefined;
}

/** How the pages of the book holding this page are grouped into spreads. */
export function getSpreadShape(page: Element): ISpreadShape {
    if (kScrollingLayouts.some((layout) => page.classList.contains(layout))) {
        return { pagesPerSpread: 1, pagesAreStacked: false };
    }
    if (
        page.ownerDocument.querySelector(
            'meta[name="defaultBookletLayout"][content="Calendar"]',
        )
    ) {
        return { pagesPerSpread: 2, pagesAreStacked: true };
    }
    return { pagesPerSpread: 2, pagesAreStacked: false };
}

/**
 * True if the book holding this page, the page at this index in it, is laid out to be read right to
 * left. Bloom marks every page with the side of the spread it falls on, and in such a book the front
 * cover (index 0) is on the left (see UpdateSideClass in HtmlDom.cs).
 */
export function isLaidOutRightToLeft(
    page: Element,
    pageIndex: number,
): boolean {
    return page.classList.contains(
        pageIndex % 2 === 0 ? "side-left" : "side-right",
    );
}

/**
 * The index of the page that a blank page comes before, or undefined if the book needs none. A book
 * with facing pages needs one when the back cover (the last page) would otherwise face the page
 * before it. The blank page goes before the inside back cover, which is back matter, or before the
 * back cover if the page before it is not back matter.
 */
export function getIndexOfPageAfterBlankPage(
    shape: ISpreadShape,
    pages: { isXMatter: boolean }[],
): number | undefined {
    // The front cover takes the second place of the first spread, so the back cover is the second
    // page of its spread when the number of pages is odd.
    if (
        shape.pagesPerSpread !== 2 ||
        pages.length < 3 ||
        pages.length % 2 === 0
    ) {
        return undefined;
    }
    const lastIndex = pages.length - 1;
    return pages[lastIndex - 1].isXMatter ? lastIndex - 1 : lastIndex;
}

/** How many pages wide one spread is. */
export function pagesAcrossASpread(shape: ISpreadShape): number {
    return shape.pagesAreStacked ? 1 : shape.pagesPerSpread;
}

// The place in the book's sequence of places (two to a spread, or one) that the page at this index
// takes. With two pages to a spread the cover stands alone in the second place of the first spread,
// so page i takes place i + 1; a blank page before blankPageBefore moves that page and every one
// after it along one more place.
function placeOfPage(
    shape: ISpreadShape,
    pageIndex: number,
    blankPageBefore: number | undefined,
): number {
    const coverOffset = shape.pagesPerSpread === 2 ? 1 : 0;
    const blankOffset =
        blankPageBefore !== undefined && pageIndex >= blankPageBefore ? 1 : 0;
    return pageIndex + coverOffset + blankOffset;
}

// The spread, counted from the front of the book, that the page at this index is in.
function spreadOfPage(
    shape: ISpreadShape,
    pageIndex: number,
    blankPageBefore: number | undefined,
): number {
    return Math.floor(
        placeOfPage(shape, pageIndex, blankPageBefore) / shape.pagesPerSpread,
    );
}

// The spread, counted from the front of the book, that the blank page before blankPageBefore is in.
function spreadOfBlankPage(
    shape: ISpreadShape,
    blankPageBefore: number,
): number {
    // The blank page takes the place that the page after it would have had without it.
    return Math.floor(
        placeOfPage(shape, blankPageBefore, undefined) / shape.pagesPerSpread,
    );
}

/**
 * The pages of a book of pageCount pages that the grid shows around the page being edited (at
 * editedIndex): whole spreads only, with no more than kMaxPagesInGrid pages in them. Starting from
 * the spread of the page being edited, it adds the next spread and the one before in turn, so the
 * page being edited ends up roughly in the middle; once one end of the book is reached, it goes on
 * adding at the other end, so a long book always shows as many pages as fit within the limit. So the
 * range starts where a spread does (with two pages to a spread, at the cover or a left page), and a
 * spread that would take the count past the limit is left out whole rather than cut in half: next to
 * the cover or the back cover, which stand alone, that can leave one page fewer than the limit. A
 * book of no more than kMaxPagesInGrid pages shows every page. blankPageBefore is as for
 * makeGridLayout(); the blank page is not one of the book's pages, so it does not count towards the
 * limit.
 */
export function getPagesToShow(
    shape: ISpreadShape,
    pageCount: number,
    editedIndex: number,
    blankPageBefore?: number,
): IPageRange {
    const lastSpreadOfBook = spreadOfPage(
        shape,
        pageCount - 1,
        blankPageBefore,
    );
    // How many of the book's pages are in each spread: one in the cover's spread, the back cover's
    // and the blank page's, two in the rest (or one each, with one page to a spread).
    const pagesInSpread = new Array<number>(lastSpreadOfBook + 1).fill(0);
    for (let index = 0; index < pageCount; index++) {
        pagesInSpread[spreadOfPage(shape, index, blankPageBefore)]++;
    }
    let firstSpreadShown = spreadOfPage(shape, editedIndex, blankPageBefore);
    let lastSpreadShown = firstSpreadShown;
    let count = pagesInSpread[firstSpreadShown];
    let addAfterNext = true;
    for (;;) {
        const canAddAfter =
            lastSpreadShown < lastSpreadOfBook &&
            count + pagesInSpread[lastSpreadShown + 1] <= kMaxPagesInGrid;
        const canAddBefore =
            firstSpreadShown > 0 &&
            count + pagesInSpread[firstSpreadShown - 1] <= kMaxPagesInGrid;
        if (!canAddAfter && !canAddBefore) break;
        if (canAddAfter && (addAfterNext || !canAddBefore)) {
            lastSpreadShown++;
            count += pagesInSpread[lastSpreadShown];
            addAfterNext = false;
        } else {
            firstSpreadShown--;
            count += pagesInSpread[firstSpreadShown];
            addAfterNext = true;
        }
    }
    let first = editedIndex;
    while (
        first > 0 &&
        spreadOfPage(shape, first - 1, blankPageBefore) >= firstSpreadShown
    ) {
        first--;
    }
    let last = editedIndex;
    while (
        last < pageCount - 1 &&
        spreadOfPage(shape, last + 1, blankPageBefore) <= lastSpreadShown
    ) {
        last++;
    }
    return { first, last };
}

/**
 * Lay the book out in rows of spreads, as many spreads to a row as fit in availableWidth (but at
 * least one), with spreadGap between spreads and rowGap between rows, mirrored if rightToLeft. If
 * blankPageBefore is given, a blank page takes the place before the page at that index, and every
 * page from there on moves along one place.
 *
 * If pagesShown is given (see getPagesToShow()), only those pages are laid out: the first row of the
 * grid is the row that holds the first of them, and the blank page has a position only if it is in
 * one of their spreads. Each spread keeps the column it has when the whole book is laid out, so that
 * whichever pages around it are shown, a page is at the same distance from the side of the grid.
 */
export function makeGridLayout(
    shape: ISpreadShape,
    pageWidth: number,
    pageHeight: number,
    availableWidth: number,
    spreadGap: number,
    rowGap: number,
    rightToLeft: boolean,
    blankPageBefore?: number,
    pagesShown?: IPageRange,
): IGridLayout {
    const spreadWidth = pagesAcrossASpread(shape) * pageWidth;
    const spreadHeight = shape.pagesAreStacked ? 2 * pageHeight : pageHeight;
    const spreadsPerRow = Math.max(
        1,
        Math.floor((availableWidth + spreadGap) / (spreadWidth + spreadGap)),
    );
    const firstSpreadShown = pagesShown
        ? spreadOfPage(shape, pagesShown.first, blankPageBefore)
        : 0;
    const firstRowShown = Math.floor(firstSpreadShown / spreadsPerRow);
    const rowWidth = spreadsPerRow * (spreadWidth + spreadGap) - spreadGap;
    const positionOfPlace = (place: number) => {
        const spread = Math.floor(place / shape.pagesPerSpread);
        const placeInSpread = place % shape.pagesPerSpread;
        const x = (spread % spreadsPerRow) * (spreadWidth + spreadGap);
        const y =
            (Math.floor(spread / spreadsPerRow) - firstRowShown) *
            (spreadHeight + rowGap);
        const position = shape.pagesAreStacked
            ? { x, y: y + placeInSpread * pageHeight }
            : { x: x + placeInSpread * pageWidth, y };
        if (rightToLeft) {
            position.x = rowWidth - position.x - pageWidth;
        }
        return position;
    };
    return {
        spreadsPerRow,
        spreadWidth,
        spreadHeight,
        positionOfPage: (pageIndex: number) =>
            positionOfPlace(placeOfPage(shape, pageIndex, blankPageBefore)),
        positionOfBlankPage: () =>
            blankPageBefore === undefined ||
            !isBlankPageShown(shape, blankPageBefore, pagesShown)
                ? undefined
                : positionOfPlace(
                      placeOfPage(shape, blankPageBefore, undefined),
                  ),
    };
}

/**
 * True if the blank page before the page at blankPageBefore (see getIndexOfPageAfterBlankPage()) is
 * in one of the spreads of pagesShown, or pagesShown is not given, meaning the whole book is shown.
 */
export function isBlankPageShown(
    shape: ISpreadShape,
    blankPageBefore: number,
    pagesShown?: IPageRange,
): boolean {
    if (!pagesShown) return true;
    const spread = spreadOfBlankPage(shape, blankPageBefore);
    return (
        spread >= spreadOfPage(shape, pagesShown.first, blankPageBefore) &&
        spread <= spreadOfPage(shape, pagesShown.last, blankPageBefore)
    );
}
