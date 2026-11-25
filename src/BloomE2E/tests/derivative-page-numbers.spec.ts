// Page numbers in a derivative of a shell book, at every page size and with every Page Numbers
// choice. Automates the manual test "Derivative Page Sizes and Layouts" [Test Case ID 94].
//
// The manual test makes a fresh derivative of the "A Family Learns about Immunisations" shell, tries
// various page sizes and layouts with page numbers on and off, and checks that a page number shows
// wherever it is not meant to be hidden. This test makes the same derivative and goes through every
// size the book offers (portrait, landscape, and the Device and Ebook sizes). At each one it gives the book each
// Page Numbers choice (Automatic, Left, Center, Right and Hidden) and reads the page number on a
// right-hand and a left-hand content page: drawn, on the page, not drawn over, showing the page's own
// number, and on the side the choice asks for; or, with Hidden, not drawn at all.
//
// Sizes and Page Numbers are set by the routes their menus post on (pageSize.ts, bookSettings.ts),
// since what they do to the pages is the subject here, not the menus.

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    getContentPages,
    goToPage,
    makeBookFromSourceBook,
    type IBookPage,
} from "../helpers/bookMaking";
import {
    setBookAppearance,
    type PageNumberPosition,
} from "../helpers/bookSettings";
import { getPageNumberShown } from "../helpers/pageNumbers";
import {
    getPageSize,
    getPageSizeChoices,
    setPageSize,
} from "../helpers/pageSize";

test.use({
    collectionSpec: { name: "derivative-page-numbers", languages: ["en"] },
});

const kShell = "A Family Learns about Immunisations";

const kPositions: PageNumberPosition[] = [
    "automatic",
    "left",
    "center",
    "right",
    "hidden",
];

/** Where a page number should sit across the page. */
type Placement = "left" | "center" | "right" | "none";

/**
 * Where a page number belongs. Paper sizes print as a book, so Automatic puts the number on the
 * outside edge of the spread: the left of a left-hand page, the right of a right-hand one. Device
 * and Ebook sizes, which are read one page at a time, centre it (pageNumbers.less).
 */
function expectedPlacement(
    position: PageNumberPosition,
    layoutId: string,
    side: string,
): Placement {
    switch (position) {
        case "hidden":
            return "none";
        case "left":
        case "center":
        case "right":
            return position;
        case "automatic":
            if (layoutId.startsWith("Device") || layoutId.startsWith("Ebook"))
                return "center";
            return side === "side-left" ? "left" : "right";
    }
}

/** Read the page number of the page being edited and check it against what belongs there. */
async function expectPageNumber(
    page: Page,
    where: string,
    position: PageNumberPosition,
    layoutId: string,
): Promise<void> {
    const shown = await getPageNumberShown(page);
    const placement = expectedPlacement(position, layoutId, shown.side);
    const described = `${where}: ${JSON.stringify(shown)}`;
    expect(
        shown.pageNumber,
        `the page has no number of its own. ${described}`,
    ).not.toBe("");
    if (placement === "none") {
        expect(
            shown.drawn,
            `the page number shows with Hidden. ${described}`,
        ).toBe(false);
        return;
    }
    expect(shown.drawn, `no page number is drawn. ${described}`).toBe(true);
    expect(
        shown.text,
        `the page number is not the page's own. ${described}`,
    ).toBe(shown.pageNumber);
    expect(
        shown.fontSizePx,
        `the page number has no size. ${described}`,
    ).toBeGreaterThan(4);
    expect(
        shown.colorAlpha,
        `the page number is transparent. ${described}`,
    ).toBeGreaterThan(0);
    // Wholly on the page, where it is printed and shown.
    expect(
        shown.box.left,
        `the page number runs off the page. ${described}`,
    ).toBeGreaterThanOrEqual(0);
    expect(
        shown.box.top,
        `the page number runs off the page. ${described}`,
    ).toBeGreaterThanOrEqual(0);
    expect(
        shown.box.right,
        `the page number runs off the page. ${described}`,
    ).toBeLessThanOrEqual(shown.pageWidth);
    expect(
        shown.box.bottom,
        `the page number runs off the page. ${described}`,
    ).toBeLessThanOrEqual(shown.pageHeight);
    expect(
        shown.onTop,
        `something is drawn over the page number. ${described}`,
    ).toBe(true);

    const middle = shown.pageWidth / 2;
    switch (placement) {
        case "left":
            expect(
                shown.box.right,
                `the page number is not on the left. ${described}`,
            ).toBeLessThan(middle);
            break;
        case "right":
            expect(
                shown.box.left,
                `the page number is not on the right. ${described}`,
            ).toBeGreaterThan(middle);
            break;
        case "center":
            // Only that the number covers the middle of the page: on paper sizes Center puts the
            // number's left edge there, not its middle (BL-17167).
            expect(
                shown.box.left,
                `the page number is not in the middle. ${described}`,
            ).toBeLessThanOrEqual(middle + 1);
            expect(
                shown.box.right,
                `the page number is not in the middle. ${described}`,
            ).toBeGreaterThanOrEqual(middle - 1);
            break;
    }
}

test("a derivative of a shell shows its page numbers at every page size unless they are hidden [Test Case ID 94]", async ({
    page,
}) => {
    test.setTimeout(1200000);
    await makeBookFromSourceBook(page, "Sample Shells", kShell);

    // A right-hand and a left-hand page: Automatic puts their numbers on opposite sides.
    const contentPages = await getContentPages(page);
    expect(
        contentPages.length,
        "the derivative should have the shell's content pages",
    ).toBeGreaterThanOrEqual(2);
    const pagesToRead: IBookPage[] = contentPages.slice(0, 2);

    const sizes = (await getPageSizeChoices(page)).choices.map((c) => c.id);
    // The shell is A5 portrait; "various page sizes" needs more than that, and the device sizes,
    // where Automatic works differently, among them.
    expect(sizes, "the sizes the derivative offers").toContain("A5Portrait");
    expect(sizes.some((s) => s.startsWith("Device"))).toBe(true);
    expect(sizes.some((s) => s.endsWith("Landscape"))).toBe(true);

    for (const layoutId of sizes) {
        await goToPage(page, pagesToRead[0].id);
        if ((await getPageSize(page)) !== layoutId)
            await setPageSize(page, layoutId);
        for (const position of kPositions) {
            await setBookAppearance(page, { pageNumberPosition: position });
            for (const bookPage of pagesToRead) {
                await goToPage(page, bookPage.id);
                await expectPageNumber(
                    page,
                    `${layoutId}, Page Numbers ${position}, the ${pagesToRead.indexOf(bookPage) === 0 ? "first" : "second"} content page`,
                    position,
                    layoutId,
                );
            }
        }
    }
});
