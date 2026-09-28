// Where each page goes in the view of the whole book around the page being edited (see
// bookGridView.ts). The pages are grouped the way the finished book is read:
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

export interface IGridLayout {
    spreadsPerRow: number;
    spreadWidth: number;
    spreadHeight: number;
    // Where the page at this index in the book goes, relative to the top left of the grid.
    positionOfPage(pageIndex: number): { x: number; y: number };
    // Where the blank page goes, if the layout has one.
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

/**
 * Lay the book out in rows of spreads, as many spreads to a row as fit in availableWidth (but at
 * least one), with spreadGap between spreads and rowGap between rows, mirrored if rightToLeft. If
 * blankPageBefore is given, a blank page takes the place before the page at that index, and every
 * page from there on moves along one place.
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
): IGridLayout {
    const spreadWidth = pagesAcrossASpread(shape) * pageWidth;
    const spreadHeight = shape.pagesAreStacked ? 2 * pageHeight : pageHeight;
    const spreadsPerRow = Math.max(
        1,
        Math.floor((availableWidth + spreadGap) / (spreadWidth + spreadGap)),
    );
    // With two pages to a spread the cover stands alone in the second place of the first spread,
    // so page i takes place i + 1.
    const coverOffset = shape.pagesPerSpread === 2 ? 1 : 0;
    const rowWidth = spreadsPerRow * (spreadWidth + spreadGap) - spreadGap;
    const positionOfPlace = (place: number) => {
        const spread = Math.floor(place / shape.pagesPerSpread);
        const placeInSpread = place % shape.pagesPerSpread;
        const x = (spread % spreadsPerRow) * (spreadWidth + spreadGap);
        const y = Math.floor(spread / spreadsPerRow) * (spreadHeight + rowGap);
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
            positionOfPlace(
                pageIndex +
                    coverOffset +
                    (blankPageBefore !== undefined &&
                    pageIndex >= blankPageBefore
                        ? 1
                        : 0),
            ),
        positionOfBlankPage: () =>
            blankPageBefore === undefined
                ? undefined
                : positionOfPlace(blankPageBefore + coverOffset),
    };
}
