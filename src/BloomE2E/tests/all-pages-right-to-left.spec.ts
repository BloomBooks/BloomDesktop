// All Pages in a book whose first language is written right to left (BL-16929). Such a book is read
// from the other side, so its spreads are mirrored: the front cover stands where a left page would,
// page 1 is the right page of the next spread, and each row runs from right to left. Bloom marks
// every page with the side of the spread it falls on, and the PDF takes its margins from that mark,
// so the grid must put each page on the side its mark says.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import { readPageSides } from "../helpers/bookHtml";
import {
    choosePageView,
    expectGridToMatchBook,
    getGridPlaces,
} from "../helpers/pageView";

test.use({
    collectionSpec: {
        name: "all-pages-rtl",
        languages: ["ar"],
        rightToLeft: true,
    },
});

test("in a right-to-left book, All Pages puts the cover alone on the left, then runs each row from right to left, every page on the side the book marks it", async ({
    page,
}) => {
    test.setTimeout(300000);
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Just Text", 6);
    const content = await getContentPages(page);
    await goToPage(page, content[0].id);

    // THE ACTION UNDER TEST: choose All Pages.
    await choosePageView(page, "all");

    await expectGridToMatchBook(page);
    const pages = await getPages(page);
    const places = await getGridPlaces(page);
    const sides = readPageSides(bookFolder);
    const shown = pages.filter((p) => places[p.id]);
    expect(shown.length, "the grid should hold the whole book").toBe(
        pages.length,
    );
    const describe = (i: number) =>
        `${shown[i].caption} at (${Math.round(places[shown[i].id].left)}, ${Math.round(places[shown[i].id].top)})`;

    // The book marks its pages the right-to-left way: the cover is a left page, page 1 a right page.
    expect(sides[pages[0].id], "the cover's side in the saved book").toBe(
        "left",
    );
    expect(sides[pages[1].id], "the side of the page after the cover").toBe(
        "right",
    );

    // Reading order: each page is to the left of the one before it, or starts a lower row.
    for (let i = 1; i < shown.length; i++) {
        const before = places[shown[i - 1].id];
        const now = places[shown[i].id];
        const sameRow = Math.abs(now.top - before.top) < 2;
        expect(
            sameRow
                ? now.right <= before.left + 1
                : now.top >= before.bottom - 1,
            `${describe(i)} does not follow ${describe(i - 1)} from right to left.`,
        ).toBe(true);
    }
    // Page 1 is the right page of the spread after the cover, facing page 2 on its left.
    const page1 = places[pages[1].id];
    const page2 = places[pages[2].id];
    expect(
        Math.abs(page1.left - page2.right),
        `${describe(1)}, ${describe(2)}`,
    ).toBeLessThan(2);
    // Each page is drawn on the side its mark says: of two pages that touch, the left one is
    // marked left and the right one right.
    for (let i = 1; i < shown.length; i++) {
        const a = places[shown[i - 1].id];
        const b = places[shown[i].id];
        if (Math.abs(a.top - b.top) < 2 && Math.abs(b.right - a.left) < 2) {
            expect(
                sides[shown[i].id],
                `${describe(i)} is the left page of its spread`,
            ).toBe("left");
            expect(
                sides[shown[i - 1].id],
                `${describe(i - 1)} is the right page of its spread`,
            ).toBe("right");
        }
    }
    // The cover stands alone: nothing touches it on either side.
    const cover = places[pages[0].id];
    for (const p of shown.slice(1)) {
        const r = places[p.id];
        if (Math.abs(r.top - cover.top) >= 2) continue;
        expect(
            Math.abs(r.right - cover.left) < 2 ||
                Math.abs(r.left - cover.right) < 2,
            `${p.caption} touches the cover, which should stand alone.`,
        ).toBe(false);
    }
});
