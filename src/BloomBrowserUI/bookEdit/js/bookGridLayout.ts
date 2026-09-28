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

import { kScrollingLayouts } from "./scrollingLayouts";

export interface ISpreadShape {
    pagesPerSpread: 1 | 2;
    // True if the two pages of a spread are one above the other, rather than side by side.
    pagesAreStacked: boolean;
}

export interface IGridLayout {
    spreadsPerRow: number;
    spreadWidth: number;
    spreadHeight: number;
    // Where the page at this index in the book goes, relative to the top left of the grid.
    positionOfPage(pageIndex: number): { x: number; y: number };
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

/** How many pages wide one spread is. */
export function pagesAcrossASpread(shape: ISpreadShape): number {
    return shape.pagesAreStacked ? 1 : shape.pagesPerSpread;
}

/**
 * Lay the book out in rows of spreads, as many spreads to a row as fit in availableWidth (but at
 * least one), with spreadGap between spreads and rowGap between rows.
 */
export function makeGridLayout(
    shape: ISpreadShape,
    pageWidth: number,
    pageHeight: number,
    availableWidth: number,
    spreadGap: number,
    rowGap: number,
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
    return {
        spreadsPerRow,
        spreadWidth,
        spreadHeight,
        positionOfPage: (pageIndex: number) => {
            const place = pageIndex + coverOffset;
            const spread = Math.floor(place / shape.pagesPerSpread);
            const placeInSpread = place % shape.pagesPerSpread;
            const x = (spread % spreadsPerRow) * (spreadWidth + spreadGap);
            const y =
                Math.floor(spread / spreadsPerRow) * (spreadHeight + rowGap);
            if (shape.pagesAreStacked) {
                return { x, y: y + placeInSpread * pageHeight };
            }
            return { x: x + placeInSpread * pageWidth, y };
        },
    };
}
