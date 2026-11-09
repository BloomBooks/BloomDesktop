import { afterEach, describe, expect, test } from "vitest";

// Tests for where the view of the whole book puts each page: side by side for a book folded down
// the side, one above the other for a calendar, one by one for a book sized for a screen.

import {
    getIndexOfPageAfterBlankPage,
    getSpreadShape,
    isLaidOutRightToLeft,
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
