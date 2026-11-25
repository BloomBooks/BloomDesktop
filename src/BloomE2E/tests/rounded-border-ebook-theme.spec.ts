// The Rounded Border Ebook page theme, on the four Ebook page sizes it is made for.
// Automates Test Case ID 97 in the Notion test inventory.
//
// The theme draws each content page as a dark page with a white frame whose corners are rounded.
// It does this only for the Ebook sizes, portrait and landscape: a paper size is drawn without
// the frame. Front and back matter pages never get it.
//
// One book with one content page is given the theme through Book and Page Settings, the way a
// person does, and is then drawn at each Ebook size in turn. On every size the test measures the
// content page's frame, and then every front and back matter page.
//
// What stays manual: whether the result looks good. The test measures whether the frame's
// corners are rounded and that the frame sits inside the page; it does not look at the colours,
// the pictures, the page number's circle, or the page as a whole. The visual-regression suite
// compares a picture of a 16x9 Landscape book in this theme with a reference
// (src/BloomVisualRegressionTests).

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
    choosePageThemeInBookSettings,
    getBookAppearance,
    okBookSettings,
    openBookSettings,
    setBookAppearance,
} from "../helpers/bookSettings";
import {
    getMarginBoxDistancesFromEdge,
    getMarginBoxRoundedCorners,
    kAllCorners,
} from "../helpers/pageMargins";
import { getPageSize, setPageSize } from "../helpers/pageSize";

test.use({
    collectionSpec: { name: "rounded-border-theme", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

/** The theme, by the name of its file and by the name the Page Theme list shows. */
const kRoundedBorderTheme = "rounded-border-ebook";
const kRoundedBorderThemeLabel = "Rounded Border Ebook";

/** The Ebook page sizes, by layout id, with the names the size menu shows for them. */
const kEbookSizes = [
    { id: "Device16x9Portrait", name: "Ebook 9x16 Portrait" },
    { id: "Ebook2x3Portrait", name: "Ebook 2x3 Portrait" },
    { id: "Device16x9Landscape", name: "Ebook 16x9 Landscape" },
    { id: "Ebook7x5Landscape", name: "Ebook 7x5 Landscape" },
];

// The book's content page, and its front and back matter pages. Set by the first test.
let contentPage: IBookPage;
let xmatterPages: IBookPage[];

/** Put the book in the Rounded Border Ebook theme, unless it is in it already. */
async function useRoundedBorderTheme(
    page: Parameters<typeof getBookAppearance>[0],
): Promise<void> {
    if ((await getBookAppearance(page)).cssThemeName !== kRoundedBorderTheme)
        await setBookAppearance(page, { cssThemeName: kRoundedBorderTheme });
}

test.describe("Rounded Border Ebook theme", () => {
    test("builds a book with a content page", async ({ page }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Basic Text & Image");
        [contentPage] = await getContentPages(page);
        xmatterPages = (await getPages(page)).filter((p) => !p.isContentPage);
        expect(xmatterPages.length).toBeGreaterThan(0);
        expect((await getBookAppearance(page)).cssThemeName).toBe("default");
    });

    test("choosing the theme in Book and Page Settings gives an Ebook page a rounded frame [Test Case ID 97]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await setPageSize(page, kEbookSizes[0].id);
        await goToPage(page, contentPage.id);
        // In the Default theme no corner is rounded, so what is measured after the change is the
        // theme's doing.
        expect(await getMarginBoxRoundedCorners(page)).toEqual([]);

        // THE ACTION UNDER TEST: choose the theme in the dialog and press OK.
        await openBookSettings(page, "Theme & Layout");
        await choosePageThemeInBookSettings(page, kRoundedBorderThemeLabel);
        await okBookSettings(page);

        expect((await getBookAppearance(page)).cssThemeName).toBe(
            kRoundedBorderTheme,
        );
        await goToPage(page, contentPage.id);
        expect(await getMarginBoxRoundedCorners(page)).toEqual(kAllCorners);
    });

    for (const size of kEbookSizes) {
        test(`at ${size.name}, content pages have a rounded frame and front and back matter pages do not [Test Case ID 97]`, async ({
            page,
        }) => {
            test.setTimeout(300000);
            await useRoundedBorderTheme(page);
            if ((await getPageSize(page)) !== size.id)
                await setPageSize(page, size.id);

            await goToPage(page, contentPage.id);
            expect(await getPageSize(page)).toBe(size.id);
            expect(
                await getMarginBoxRoundedCorners(page),
                "the corners of the content page's frame that are rounded",
            ).toEqual(kAllCorners);
            // The frame is drawn inside the page, with the dark page showing all round it.
            const margins = await getMarginBoxDistancesFromEdge(page);
            for (const [side, mm] of Object.entries(margins))
                expect(
                    mm,
                    `the frame's distance from the ${side} edge of the page, in mm`,
                ).toBeGreaterThan(0);

            for (const xmatterPage of xmatterPages) {
                await goToPage(page, xmatterPage.id);
                expect(
                    await getMarginBoxRoundedCorners(page),
                    `the rounded corners of the ${xmatterPage.caption} page's margin box`,
                ).toEqual([]);
            }
        });
    }

    test("at a paper size, the theme leaves content pages without a rounded frame [Test Case ID 97]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await useRoundedBorderTheme(page);
        await setPageSize(page, "A5Portrait");
        await goToPage(page, contentPage.id);
        expect(await getPageSize(page)).toBe("A5Portrait");
        expect(await getMarginBoxRoundedCorners(page)).toEqual([]);
    });
});
