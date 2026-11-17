import { afterEach, describe, expect, test } from "vitest";

// Tests for where the view of the whole book puts each page: side by side for a book folded down
// the side, one above the other for a calendar, one by one for a book sized for a screen.

import {
    getIndexOfPageAfterBlankPage,
    getPagesToShow,
    getSpreadShape,
    isBlankPageShown,
    isLaidOutRightToLeft,
    kMaxPagesInGrid,
    makeGridLayout,
    pagesAcrossASpread,
} from "./bookGridLayout";

const kPageWidth = 100;
const kPageHeight = 150;
const kGap = 10;

function makePage(sizeClass: string): HTMLElement {
    const page = document.createElement("div");
    page.classList.add("bloom-page", sizeClass);
    document.body.appendChild(page);
    return page;
}

function markAsCalendar(): void {
    const meta = document.createElement("meta");
    meta.setAttribute("name", "defaultBookletLayout");
    meta.setAttribute("content", "Calendar");
    document.head.appendChild(meta);
}

afterEach(() => {
    document.head.innerHTML = "";
    document.body.innerHTML = "";
});

describe("getSpreadShape", () => {
    test("a printed book has two pages side by side", () => {
        expect(getSpreadShape(makePage("A5Portrait"))).toEqual({
            pagesPerSpread: 2,
            pagesAreStacked: false,
        });
    });

    test("a calendar has two pages, one above the other", () => {
        const page = makePage("A5Landscape");
        expect(getSpreadShape(page).pagesAreStacked).toBe(false);
        markAsCalendar();
        expect(getSpreadShape(page)).toEqual({
            pagesPerSpread: 2,
            pagesAreStacked: true,
        });
    });

    test.each([
        "Device16x9Portrait",
        "Device16x9Landscape",
        "Ebook2x3Portrait",
        "Ebook7x5Landscape",
    ])("a book sized for a screen (%s) has one page at a time", (size) => {
        expect(getSpreadShape(makePage(size))).toEqual({
            pagesPerSpread: 1,
            pagesAreStacked: false,
        });
    });

    test("a calendar sized for a screen is read one page at a time", () => {
        markAsCalendar();
        expect(
            getSpreadShape(makePage("Device16x9Landscape")).pagesPerSpread,
        ).toBe(1);
    });
});

describe("isLaidOutRightToLeft", () => {
    test("reads the book's direction from the side Bloom puts a page on", () => {
        const page = makePage("A5Portrait");
        page.classList.add("side-right");
        // Left to right, the front cover (index 0) and every even page are on the right.
        expect(isLaidOutRightToLeft(page, 0)).toBe(false);
        expect(isLaidOutRightToLeft(page, 3)).toBe(true);
        page.classList.replace("side-right", "side-left");
        expect(isLaidOutRightToLeft(page, 0)).toBe(true);
        expect(isLaidOutRightToLeft(page, 3)).toBe(false);
    });
});

describe("makeGridLayout", () => {
    test("a printed book: cover alone on the right, then left and right pages", () => {
        const shape = { pagesPerSpread: 2 as const, pagesAreStacked: false };
        expect(pagesAcrossASpread(shape)).toBe(2);
        // Room for exactly two spreads: 2 * 200 + 10.
        const grid = makeGridLayout(
            shape,
            kPageWidth,
            kPageHeight,
            410,
            kGap,
            kGap,
            false,
        );
        expect(grid.spreadsPerRow).toBe(2);
        expect(grid.positionOfPage(0)).toEqual({ x: 100, y: 0 }); // cover, right
        expect(grid.positionOfPage(1)).toEqual({ x: 210, y: 0 }); // left
        expect(grid.positionOfPage(2)).toEqual({ x: 310, y: 0 }); // right
        expect(grid.positionOfPage(3)).toEqual({ x: 0, y: 160 }); // next row, left
        expect(grid.positionOfPage(4)).toEqual({ x: 100, y: 160 });
    });

    test("a calendar: cover alone at the bottom, then each upper page above its lower page", () => {
        const shape = { pagesPerSpread: 2 as const, pagesAreStacked: true };
        expect(pagesAcrossASpread(shape)).toBe(1);
        // Room for exactly two one-page-wide spreads: 2 * 100 + 10.
        const grid = makeGridLayout(
            shape,
            kPageWidth,
            kPageHeight,
            210,
            kGap,
            kGap,
            false,
        );
        expect(grid.spreadsPerRow).toBe(2);
        expect(grid.spreadHeight).toBe(300);
        expect(grid.positionOfPage(0)).toEqual({ x: 0, y: 150 }); // cover, lower
        expect(grid.positionOfPage(1)).toEqual({ x: 110, y: 0 }); // upper
        expect(grid.positionOfPage(2)).toEqual({ x: 110, y: 150 }); // lower
        expect(grid.positionOfPage(3)).toEqual({ x: 0, y: 310 }); // next row, upper
        expect(grid.positionOfPage(4)).toEqual({ x: 0, y: 460 });
    });

    test("a book sized for a screen: every page by itself, cover first", () => {
        const shape = { pagesPerSpread: 1 as const, pagesAreStacked: false };
        expect(pagesAcrossASpread(shape)).toBe(1);
        // Room for exactly three pages: 3 * 100 + 2 * 10.
        const grid = makeGridLayout(
            shape,
            kPageWidth,
            kPageHeight,
            320,
            kGap,
            kGap,
            false,
        );
        expect(grid.spreadsPerRow).toBe(3);
        expect(grid.positionOfPage(0)).toEqual({ x: 0, y: 0 });
        expect(grid.positionOfPage(1)).toEqual({ x: 110, y: 0 });
        expect(grid.positionOfPage(2)).toEqual({ x: 220, y: 0 });
        expect(grid.positionOfPage(3)).toEqual({ x: 0, y: 160 });
    });

    test("always at least one spread to a row, however narrow the space", () => {
        const shape = { pagesPerSpread: 2 as const, pagesAreStacked: false };
        const grid = makeGridLayout(
            shape,
            kPageWidth,
            kPageHeight,
            50,
            kGap,
            kGap,
            false,
        );
        expect(grid.spreadsPerRow).toBe(1);
        expect(grid.positionOfPage(1)).toEqual({ x: 0, y: 160 });
    });

    test("a book read right to left: cover alone on the left, rows running right to left", () => {
        const shape = { pagesPerSpread: 2 as const, pagesAreStacked: false };
        // Room for exactly two spreads, as in the left-to-right test above.
        const grid = makeGridLayout(
            shape,
            kPageWidth,
            kPageHeight,
            410,
            kGap,
            kGap,
            true,
        );
        expect(grid.spreadsPerRow).toBe(2);
        expect(grid.positionOfPage(0)).toEqual({ x: 210, y: 0 }); // cover, left
        expect(grid.positionOfPage(1)).toEqual({ x: 100, y: 0 }); // right
        expect(grid.positionOfPage(2)).toEqual({ x: 0, y: 0 }); // left
        expect(grid.positionOfPage(3)).toEqual({ x: 310, y: 160 }); // next row, right
        expect(grid.positionOfPage(4)).toEqual({ x: 210, y: 160 });
    });
});

describe("getIndexOfPageAfterBlankPage", () => {
    const sideBySide = { pagesPerSpread: 2 as const, pagesAreStacked: false };
    // A front cover, content pages, then an inside back cover and a back cover.
    function makeBook(contentPages: number): { isXMatter: boolean }[] {
        return [
            { isXMatter: true },
            ...Array.from({ length: contentPages }, () => ({
                isXMatter: false,
            })),
            { isXMatter: true },
            { isXMatter: true },
        ];
    }

    test("an even number of pages already leaves the back cover alone", () => {
        const book = makeBook(3);
        expect(book.length).toBe(6);
        expect(getIndexOfPageAfterBlankPage(sideBySide, book)).toBeUndefined();
    });

    test("an odd number of pages gets a blank page before the inside back cover", () => {
        const book = makeBook(2);
        expect(book.length).toBe(5);
        expect(getIndexOfPageAfterBlankPage(sideBySide, book)).toBe(3);
    });

    test("with no inside back cover, the blank page goes before the back cover", () => {
        const book = [
            { isXMatter: true },
            { isXMatter: false },
            { isXMatter: false },
            { isXMatter: false },
            { isXMatter: true },
        ];
        expect(getIndexOfPageAfterBlankPage(sideBySide, book)).toBe(4);
    });

    test("a calendar gets one too, but a book sized for a screen has no facing pages", () => {
        const book = makeBook(2);
        expect(
            getIndexOfPageAfterBlankPage(
                { pagesPerSpread: 2, pagesAreStacked: true },
                book,
            ),
        ).toBe(3);
        expect(
            getIndexOfPageAfterBlankPage(
                { pagesPerSpread: 1, pagesAreStacked: false },
                book,
            ),
        ).toBeUndefined();
    });

    test("the blank page takes a place of its own and the back cover stands alone", () => {
        // Room for exactly two spreads, as in the makeGridLayout tests.
        const grid = makeGridLayout(
            sideBySide,
            kPageWidth,
            kPageHeight,
            410,
            kGap,
            kGap,
            false,
            3,
        );
        expect(grid.positionOfPage(2)).toEqual({ x: 310, y: 0 }); // last content page, right
        expect(grid.positionOfBlankPage()).toEqual({ x: 0, y: 160 }); // left
        expect(grid.positionOfPage(3)).toEqual({ x: 100, y: 160 }); // inside back cover, right
        expect(grid.positionOfPage(4)).toEqual({ x: 210, y: 160 }); // back cover, alone
        expect(
            makeGridLayout(
                sideBySide,
                kPageWidth,
                kPageHeight,
                410,
                kGap,
                kGap,
                false,
            ).positionOfBlankPage(),
        ).toBeUndefined();
    });
});

describe("getPagesToShow", () => {
    const sideBySide = { pagesPerSpread: 2 as const, pagesAreStacked: false };
    const oneByOne = { pagesPerSpread: 1 as const, pagesAreStacked: false };
    const calendar = { pagesPerSpread: 2 as const, pagesAreStacked: true };

    // How many pages a range holds.
    function countOf(range: { first: number; last: number }): number {
        return range.last - range.first + 1;
    }

    test("the limit is the one these tests are written for", () => {
        expect(kMaxPagesInGrid).toBe(24);
    });

    test("a book of no more pages than the limit shows them all", () => {
        expect(getPagesToShow(sideBySide, 10, 5)).toEqual({
            first: 0,
            last: 9,
        });
        expect(getPagesToShow(sideBySide, 24, 23)).toEqual({
            first: 0,
            last: 23,
        });
        expect(getPagesToShow(oneByOne, 24, 0)).toEqual({ first: 0, last: 23 });
    });

    test("in the middle of a long book: the limit, whole spreads, the page being edited in the middle", () => {
        const range = getPagesToShow(sideBySide, 200, 100);
        expect(range).toEqual({ first: 89, last: 112 });
        expect(countOf(range)).toBe(kMaxPagesInGrid);
        // Odd indexes are left pages, so the range starts and ends with whole spreads.
        expect(range.first % 2).toBe(1);
        expect(range.last % 2).toBe(0);
        // A left page in the middle gets the same view as its facing right page.
        expect(getPagesToShow(sideBySide, 200, 99)).toEqual(range);
    });

    test("near the front cover it starts at the cover, and leaves out a spread it would cut in half", () => {
        const range = getPagesToShow(sideBySide, 200, 3);
        // The cover stands alone, so whole spreads make 23 pages: page 23 would need page 24.
        expect(range).toEqual({ first: 0, last: 22 });
        expect(getPagesToShow(sideBySide, 200, 0)).toEqual(range);
    });

    test("near the back cover it ends at the last page", () => {
        // An even number of pages: the back cover stands alone, so whole spreads make 23 pages.
        expect(getPagesToShow(sideBySide, 200, 197)).toEqual({
            first: 177,
            last: 199,
        });
        expect(getPagesToShow(sideBySide, 200, 190)).toEqual({
            first: 177,
            last: 199,
        });
        // An odd number of pages: the blank page goes before the inside back cover (index 199),
        // takes no part of the limit, and the full limit is shown.
        const blankPageBefore = 199;
        const range = getPagesToShow(sideBySide, 201, 198, blankPageBefore);
        expect(range).toEqual({ first: 177, last: 200 });
        expect(countOf(range)).toBe(kMaxPagesInGrid);
        expect(isBlankPageShown(sideBySide, blankPageBefore, range)).toBe(true);
        expect(
            isBlankPageShown(
                sideBySide,
                blankPageBefore,
                getPagesToShow(sideBySide, 201, 100, blankPageBefore),
            ),
        ).toBe(false);
    });

    test("one page to a spread: any page can start the range", () => {
        expect(getPagesToShow(oneByOne, 100, 50)).toEqual({
            first: 39,
            last: 62,
        });
        expect(getPagesToShow(oneByOne, 100, 2)).toEqual({
            first: 0,
            last: 23,
        });
        expect(getPagesToShow(oneByOne, 100, 98)).toEqual({
            first: 76,
            last: 99,
        });
    });

    test("a calendar pairs its pages as a book folded down the side does", () => {
        const range = getPagesToShow(calendar, 50, 25);
        expect(range).toEqual({ first: 15, last: 38 });
        expect(range).toEqual(getPagesToShow(sideBySide, 50, 25));
    });
});

describe("makeGridLayout, showing only some of the pages", () => {
    const sideBySide = { pagesPerSpread: 2 as const, pagesAreStacked: false };
    // Room for exactly two spreads, as in the makeGridLayout tests above.
    function layOut(
        rightToLeft: boolean,
        blankPageBefore?: number,
        pagesShown?: { first: number; last: number },
    ) {
        return makeGridLayout(
            sideBySide,
            kPageWidth,
            kPageHeight,
            410,
            kGap,
            kGap,
            rightToLeft,
            blankPageBefore,
            pagesShown,
        );
    }

    test("the first row is the one holding the first page shown, and each page keeps its column", () => {
        const whole = layOut(false);
        // Page 5 is the left page of spread 3, the second spread of the second row.
        expect(whole.positionOfPage(5)).toEqual({ x: 210, y: 160 });
        const part = layOut(false, undefined, { first: 5, last: 12 });
        expect(part.positionOfPage(5)).toEqual({ x: 210, y: 0 });
        expect(part.positionOfPage(6)).toEqual({ x: 310, y: 0 });
        expect(part.positionOfPage(7)).toEqual({ x: 0, y: 160 });
        for (let index = 5; index <= 12; index++) {
            const p = part.positionOfPage(index);
            const w = whole.positionOfPage(index);
            expect(p.x).toBe(w.x);
            expect(p.y).toBe(w.y - 160);
        }
    });

    test("a book read right to left keeps its mirrored columns", () => {
        const whole = layOut(true);
        const part = layOut(true, undefined, { first: 5, last: 12 });
        expect(part.positionOfPage(5)).toEqual({
            x: whole.positionOfPage(5).x,
            y: 0,
        });
        expect(part.positionOfPage(5).x).toBe(100);
    });

    test("the blank page has a place only when its spread is shown", () => {
        // Seven pages: the blank page goes before the inside back cover (index 5).
        expect(
            layOut(false, 5).positionOfBlankPage(),
            "Sanity check: with the whole book shown, the blank page has a place",
        ).toEqual({ x: 210, y: 160 });
        expect(
            layOut(false, 5, { first: 1, last: 4 }).positionOfBlankPage(),
        ).toBeUndefined();
        const shown = layOut(false, 5, { first: 3, last: 6 });
        // Spread 3 (the blank page and the inside back cover) is in the first row shown.
        expect(shown.positionOfBlankPage()).toEqual({ x: 210, y: 0 });
        expect(shown.positionOfPage(5)).toEqual({ x: 310, y: 0 });
        expect(shown.positionOfPage(6)).toEqual({ x: 0, y: 160 });
    });
});

describe("the blank page before the back cover, at the end of the pages shown", () => {
    test("in a calendar it can be the lower half of the last spread shown, so it is the lowest page in the grid", () => {
        const calendar = { pagesPerSpread: 2 as const, pagesAreStacked: true };
        const pageCount = 52;
        const blankPageBefore = 50;
        // Find a page being edited whose range ends just before the page after the blank page.
        let range: { first: number; last: number } | undefined;
        for (let edited = 0; edited < pageCount && !range; edited++) {
            const r = getPagesToShow(
                calendar,
                pageCount,
                edited,
                blankPageBefore,
            );
            if (r.last === blankPageBefore - 1) range = r;
        }
        if (!range)
            throw new Error(
                "Sanity check: some page being edited should give such a range",
            );
        expect(isBlankPageShown(calendar, blankPageBefore, range)).toBe(true);
        const grid = makeGridLayout(
            calendar,
            kPageWidth,
            kPageHeight,
            1000,
            kGap,
            kGap,
            false,
            blankPageBefore,
            range,
        );
        const blank = grid.positionOfBlankPage();
        expect(blank).toBeDefined();
        expect(blank!.y).toBe(grid.positionOfPage(range.last).y + kPageHeight);
    });
});
