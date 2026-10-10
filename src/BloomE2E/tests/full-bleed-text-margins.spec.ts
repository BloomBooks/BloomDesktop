// Where the margins of a full-bleed book's pages end up, measured from where the printer cuts the
// paper (BL-15958).
// Automates Test Case ID 846 in the Notion test inventory.
//
// With "Use full bleed page layout" on, Bloom draws each page enlarged so that pictures run past the
// cut line. Text must not move with them: it sits the same distance from the cut as it does from
// the paper's edge in a book without full bleed. Edge to Edge, which takes the margins off content
// pages so pictures can reach the edge, keeps its text at the Default theme's margin from the cut
// when full bleed is on, and its pictures still run into the bleed.
//
// One A5 book with one picture page is measured in the Edit tab four ways: Default and Edge to
// Edge, each without and with full bleed. The cases without full bleed guard against the margin
// rules, which every page of every book goes through, changing for the books that never use bleed.
// Full bleed needs an Enterprise subscription, so the collection has one.
//
// What stays manual: the PDF itself (Publish > PDF & Print), at other paper sizes and in landscape,
// and where page numbers land. The Edit tab draws the page with the same rules the PDF is made from.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
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
import { getPageSize } from "../helpers/pageSize";
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

/** The Default theme's margin on every side of an A5 page (appearance-theme-default.css). */
const kA5DefaultMarginMm = 12;

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
            allSides(kA5DefaultMarginMm),
            "the picture page's margins",
        );
        await goToPage(page, creditsPage.id);
        expectDistancesFromEdge(
            await getMarginBoxDistancesFromEdge(page),
            allSides(kA5DefaultMarginMm),
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
            allSides(kA5DefaultMarginMm),
            "the picture page's margins",
        );
        await goToPage(page, creditsPage.id);
        expectDistancesFromEdge(
            await getMarginBoxDistancesFromEdge(page),
            allSides(kA5DefaultMarginMm),
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

    test("with full bleed, Edge to Edge keeps text at the Default margin from the cut line and runs pictures into the bleed [Test Case ID 846]", async ({
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
                left: kA5DefaultMarginMm,
                right: kA5DefaultMarginMm,
                bottom: kA5DefaultMarginMm,
            },
            "the text under the picture",
        );
        await goToPage(page, creditsPage.id);
        expectDistancesFromEdge(
            await getMarginBoxDistancesFromEdge(page),
            allSides(kA5DefaultMarginMm),
            "the credits page's margins",
        );
    });
});
