// Where the format gear sits on an Edge to Edge page, with and without full bleed (BL-15958).
// Automates Test Case ID 857 in the Notion test inventory.
//
// Clicking into a text box shows the format gear at the box's bottom-left corner. Edge to Edge
// takes the margins off content pages, so without full bleed the text box reaches the bottom of
// the page and the page number sits over the box's bottom-left corner; the theme then moves the
// gear 20px to the right, out from under the number, whenever the number can be at the left
// (Page Numbers Automatic or Left). With full bleed, Edge to Edge keeps text the standard page
// margin from the cut line, so the page number sits in that margin below the text box and the
// gear stays at the corner whatever Page Numbers says.
//
// One A5 book with a text-only page is measured in the Edit tab. The gear's distance from the
// corner with Page Numbers at Center, where nothing ever moves it, is the corner; the other
// settings are compared with that.
//
// Full bleed needs an Enterprise subscription, so the collection has one.

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    clickInGroup,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    type IBookPage,
} from "../helpers/bookMaking";
import {
    isPageDrawnWithFullBleed,
    setBookAppearance,
    type PageNumberPosition,
} from "../helpers/bookSettings";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { getFormatGearInsetFromTextBox } from "../helpers/formatDialog";
import { getPageSize } from "../helpers/pageSize";

test.use({
    collectionSpec: {
        name: "edge-to-edge-format-gear",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

test.describe.configure({ mode: "serial" });

/** How far Edge to Edge moves the gear to clear the page number (--formatButton-pageNumber-dodge). */
const kDodgePx = 20;

/** Layout rounds to device pixels, and the gear's position is read through the Edit tab's zoom. */
const kTolerancePx = 1;

// The book's text-only page. Set by the first test.
let textPage: IBookPage;
// How far the gear sits from the text box's corner when nothing moves it. Set by the second test.
let cornerInset: number;

/**
 * Give the book Edge to Edge with the given full bleed and Page Numbers settings, click into the
 * text page's text box, and return how far its format gear is from the box's left edge.
 */
async function measureGearInset(
    page: Page,
    fullBleed: boolean,
    pageNumberPosition: PageNumberPosition,
): Promise<number> {
    await setBookAppearance(page, {
        cssThemeName: "edge-to-edge",
        fullBleed,
        pageNumberPosition,
    });
    expect(
        await isPageDrawnWithFullBleed(page),
        `the page should be drawn ${fullBleed ? "with" : "without"} full bleed`,
    ).toBe(fullBleed);
    await goToPage(page, textPage.id);
    await clickInGroup(page, ".bloom-translationGroup", "en");
    return getFormatGearInsetFromTextBox(page);
}

test.describe("the format gear on Edge to Edge pages", () => {
    test("builds an A5 book with a text-only page", async ({ page }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Just Text");
        [textPage] = await getContentPages(page);
        await goToPage(page, textPage.id);
        expect(await getPageSize(page)).toBe("A5Portrait");
    });

    test("without full bleed, the gear moves clear of a page number that can be at the left [Test Case ID 857]", async ({
        page,
    }) => {
        cornerInset = await measureGearInset(page, false, "center");
        // Sanity check: a gear already moved by the dodge would make every comparison below
        // meaningless.
        expect(
            cornerInset,
            "with Page Numbers at Center the gear should be at the corner, not moved",
        ).toBeLessThan(kDodgePx);

        for (const position of ["automatic", "left"] as const)
            expect(
                Math.abs(
                    (await measureGearInset(page, false, position)) -
                        cornerInset -
                        kDodgePx,
                ),
                `with Page Numbers ${position}, how far the gear's move differs from ${kDodgePx}px`,
            ).toBeLessThanOrEqual(kTolerancePx);
        expect(
            Math.abs(
                (await measureGearInset(page, false, "right")) - cornerInset,
            ),
            "with Page Numbers Right, how far the gear moved",
        ).toBeLessThanOrEqual(kTolerancePx);
    });

    test("with full bleed, the gear stays at the corner whatever Page Numbers says [Test Case ID 857]", async ({
        page,
    }) => {
        for (const position of [
            "automatic",
            "left",
            "center",
            "right",
        ] as const)
            expect(
                Math.abs(
                    (await measureGearInset(page, true, position)) -
                        cornerInset,
                ),
                `with full bleed and Page Numbers ${position}, how far the gear moved`,
            ).toBeLessThanOrEqual(kTolerancePx);
    });
});
