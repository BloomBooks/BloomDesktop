// Where the margins of a full-bleed book's pages end up, measured from where the printer cuts the
// paper (BL-15958).
// Automates Test Case ID 846 in the Notion test inventory.
//
// With "Use full bleed page layout" on, Bloom draws each page enlarged so that pictures run past the
// cut line. Text must not move with them: it sits the same distance from the cut as it does from
// the paper's edge in a book without full bleed. Edge to Edge, which takes the margins off content
// pages so pictures can reach the edge, keeps its text at the standard page margin from the cut
// when full bleed is on, and its pictures still run into the bleed.
//
// One A5 book with one picture page is measured in the Edit tab four ways: Default and Edge to
// Edge, each without and with full bleed. The cases without full bleed guard against the margin
// rules, which every page of every book goes through, changing for the books that never use bleed.
// Then the book is switched to A4, which unlike A5 has a gutter on the inside edge, and given a
// second picture page, so that Edge to Edge with full bleed is measured on a left and a right page.
// Full bleed needs an Enterprise subscription, so the collection has one.
//
// What stays manual: the PDF itself (Publish > PDF & Print), the other paper sizes, landscape,
// and where page numbers land. The Edit tab draws the page with the same rules the PDF is made from.

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editablePageFrame,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    type IBookPage,
} from "../helpers/bookMaking";
import {
    isPageDrawnWithFullBleed,
    setBookAppearance,
} from "../helpers/bookSettings";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { getPageSize, setPageSize } from "../helpers/pageSize";
import {
    expectDistancesFromEdge,
    expectPastEdge,
    getMarginBoxDistancesFromEdge,
    getPictureDistancesFromEdge,
    getTextAreaDistancesFromEdge,
    kBleedMm,
} from "../helpers/pageMargins";

test.use({
    collectionSpec: {
        name: "full-bleed-margins",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

test.describe.configure({ mode: "serial" });

/** The standard page margin on every side of an A5 page, which the Default theme uses
 * (--standard-page-margin in appearance-theme-default.css). */
const kA5StandardMarginMm = 12;

/** The standard page margin on every side of an A4 page. The Default theme also gives A4 a
 * 10mm gutter, which Edge to Edge removes. */
const kA4StandardMarginMm = 15;

/** Edge to Edge's margin on front and back matter when there is no bleed. */
const kEdgeToEdgeXmatterMarginMm = 3;

/** The same distance on all four sides. */
const allSides = (mm: number) => ({ left: mm, top: mm, right: mm, bottom: mm });

// The book's picture page and its credits page. Set by the first test.
let picturePage: IBookPage;
let creditsPage: IBookPage;

test.describe("text margins with and without full bleed", () => {
    test("builds an A5 book with a picture page", async ({ page }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Basic Text & Image");
        [picturePage] = await getContentPages(page);
        const credits = (await getPages(page)).find((p) =>
            /credits/i.test(p.caption),
        );
        if (!credits)
            throw new Error(
                `The book has no credits page; its pages are ${(
                    await getPages(page)
                )
                    .map((p) => p.caption)
                    .join(", ")}.`,
            );
        creditsPage = credits;
        await goToPage(page, picturePage.id);
        // The margins below are A5's; the rest of the file rests on that.
        expect(await getPageSize(page)).toBe("A5Portrait");
    });

    test("without full bleed, the Default theme's margins are measured from the page edge [Test Case ID 846]", async ({
        page,
    }) => {
        await setBookAppearance(page, {
            cssThemeName: "default",
            fullBleed: false,
        });
        expect(await isPageDrawnWithFullBleed(page)).toBe(false);

        await goToPage(page, picturePage.id);
        expectDistancesFromEdge(
            await getMarginBoxDistancesFromEdge(page),
            allSides(kA5StandardMarginMm),
            "the picture page's margins",
        );
        await goToPage(page, creditsPage.id);
        expectDistancesFromEdge(
            await getMarginBoxDistancesFromEdge(page),
            allSides(kA5StandardMarginMm),
            "the credits page's margins",
        );
    });

    test("with full bleed, the Default theme's margins are measured from the cut line [Test Case ID 846]", async ({
        page,
    }) => {
        await setBookAppearance(page, {
            cssThemeName: "default",
            fullBleed: true,
        });
        expect(await isPageDrawnWithFullBleed(page)).toBe(true);

        await goToPage(page, picturePage.id);
        expectDistancesFromEdge(
            await getMarginBoxDistancesFromEdge(page),
            allSides(kA5StandardMarginMm),
            "the picture page's margins",
        );
        await goToPage(page, creditsPage.id);
        expectDistancesFromEdge(
            await getMarginBoxDistancesFromEdge(page),
            allSides(kA5StandardMarginMm),
            "the credits page's margins",
        );
    });

    test("without full bleed, Edge to Edge has no margin on content pages and a small one on front matter [Test Case ID 846]", async ({
        page,
    }) => {
        await setBookAppearance(page, {
            cssThemeName: "edge-to-edge",
            fullBleed: false,
        });
        expect(await isPageDrawnWithFullBleed(page)).toBe(false);

        await goToPage(page, picturePage.id);
        expectDistancesFromEdge(
            await getMarginBoxDistancesFromEdge(page),
            allSides(0),
            "the picture page's margins",
        );
        await goToPage(page, creditsPage.id);
        expectDistancesFromEdge(
            await getMarginBoxDistancesFromEdge(page),
            allSides(kEdgeToEdgeXmatterMarginMm),
            "the credits page's margins",
        );
    });

    test("with full bleed, Edge to Edge keeps text at the standard page margin from the cut line and runs pictures into the bleed [Test Case ID 846]", async ({
        page,
    }) => {
        await setBookAppearance(page, {
            cssThemeName: "edge-to-edge",
            fullBleed: true,
        });
        expect(await isPageDrawnWithFullBleed(page)).toBe(true);

        await goToPage(page, picturePage.id);
        // The picture fills the top of the page, so it runs off the left, top and right edges.
        expectPastEdge(
            await getPictureDistancesFromEdge(page),
            ["left", "top", "right"],
            kBleedMm,
            "the picture",
        );
        // The text box is below the picture, so its top is not on an edge of the page.
        expectDistancesFromEdge(
            await getTextAreaDistancesFromEdge(page),
            {
                left: kA5StandardMarginMm,
                right: kA5StandardMarginMm,
                bottom: kA5StandardMarginMm,
            },
            "the text under the picture",
        );
        await goToPage(page, creditsPage.id);
        expectDistancesFromEdge(
            await getMarginBoxDistancesFromEdge(page),
            allSides(kA5StandardMarginMm),
            "the credits page's margins",
        );
    });

    // A4 and the other large sizes have a gutter on the inside edge of each page, which A5 does
    // not, and which Edge to Edge has to remove too (BL-17025). Left and right pages have it on
    // opposite sides, so the book needs one of each.
    test("with full bleed at A4, Edge to Edge runs pictures into the bleed on the inside edge of left and right pages too [Test Case ID 846]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await setBookAppearance(page, {
            cssThemeName: "edge-to-edge",
            fullBleed: true,
        });
        await setPageSize(page, "A4Portrait");
        await addPage(page, "Basic Text & Image");
        const contentPages = await getContentPages(page);
        expect(contentPages.length).toBe(2);

        const sidesSeen: string[] = [];
        for (const contentPage of contentPages) {
            await goToPage(page, contentPage.id);
            expect(await getPageSize(page)).toBe("A4Portrait");
            expect(await isPageDrawnWithFullBleed(page)).toBe(true);
            const side = await getPageSide(page);
            sidesSeen.push(side);
            expectPastEdge(
                await getPictureDistancesFromEdge(page),
                ["left", "top", "right"],
                kBleedMm,
                `the picture on the ${side} page`,
            );
            expectDistancesFromEdge(
                await getTextAreaDistancesFromEdge(page),
                {
                    left: kA4StandardMarginMm,
                    right: kA4StandardMarginMm,
                    bottom: kA4StandardMarginMm,
                },
                `the text under the picture on the ${side} page`,
            );
        }
        expect(sidesSeen.sort()).toEqual(["side-left", "side-right"]);
    });
});

/** Whether the page being edited is a left or a right page: "side-left" or "side-right". */
async function getPageSide(page: Page): Promise<string> {
    const classes = await editablePageFrame(page)
        .locator(".bloom-page")
        .first()
        .getAttribute("class");
    const side = classes
        ?.split(/\s+/)
        .find((c) => /^side-(left|right)$/.test(c));
    if (!side)
        throw new Error(
            `The page being edited has neither side-left nor side-right: "${classes}".`,
        );
    return side;
}
