// Each of the PDF & Print screen's three modes, for an A5 book.
// Automates Test Case ID 145, "All PDF Modes A5", in the Notion test inventory.
//
// An A5 portrait book (a Basic Book) has three PDFs:
//
//  - Simple: every page of the book, one to a sheet of A5, in the book's order.
//  - Booklet Cover: only the four cover pages, two to a sheet of landscape A4, arranged so that the
//    sheet printed on both sides and folded is the cover: the back and front covers on one side, the
//    inside covers on the other.
//  - Booklet Insides: every page but the covers, two to a sheet of landscape A4, arranged so that
//    the sheets printed on both sides, stacked and folded are the book's inside pages in order.
//
// Each picture in the book has a size no other picture has, and that is how it is recognized in a
// PDF, which does not keep a picture's name. So the test can tell which page of the book was drawn
// on which sheet, and on which face of it. Every page but the title page has a picture, so no two
// pages can trade places unseen. The inside back cover and the back cover take one through their
// Custom layout, which needs a subscription, so the collection has one. The inside front cover (the
// credits page) is marked by the book's license badge.
//
// The UI is set to English, because the PDF & Print screen and its modes are found by their labels.
//
// What stays manual: the case's note to watch Bloom's performance (Performance Measuring) while
// the PDFs are made, which is for a person to judge. Which half of a sheet a page is drawn on is
// not checked either: a sheet's pictures are read from the PDF without their positions.

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import { bookHtmlPath } from "../helpers/bookHtml";
import {
    addPage,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    waitForEditablePage,
} from "../helpers/bookMaking";
import {
    chooseImageFile,
    makeJpegFile,
    readImageFileSize,
    type IPixelSize,
} from "../helpers/images";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { choosePageLayout } from "../helpers/customCover";
import { getPageSize } from "../helpers/pageSize";
import {
    choosePdfMode,
    getPdfPreviewPageBoxes,
    getPdfPreviewPicturesByPage,
    type IPdfBox,
    type PdfMode,
} from "../helpers/pdfPrint";
import {
    getCurrentUiLanguageTag,
    setUiStateViaApi,
} from "../helpers/uiLanguage";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "pdf-modes-a5",
        languages: ["en"],
        // A custom layout for a cover is a subscription feature.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

/** The sheet of each PDF, in millimetres: A5 portrait for Simple, A4 landscape for a booklet. */
const kA5PortraitMm = { width: 148, height: 210 };
const kA4LandscapeMm = { width: 297, height: 210 };

/**
 * How far a measurement in the PDF may be from the exact figure. The PDF maker draws the page in
 * whole pixels, which makes an A5 page 0.18mm wider than 148mm.
 */
const kToleranceMm = 0.3;

/** The pictures of the front cover, the inside back cover and the back cover. */
const kFrontCoverPicture: IPixelSize = { width: 331, height: 227 };
const kInsideBackCoverPicture: IPixelSize = { width: 353, height: 233 };
const kBackCoverPicture: IPixelSize = { width: 359, height: 239 };

/**
 * One picture for each content page, in the order of the pages. With the one inside page of a
 * Basic Book's front matter, seven content pages make eight inside pages: two sheets, with no
 * blank page added.
 */
const kContentPictures: IPixelSize[] = [
    { width: 301, height: 203 },
    { width: 307, height: 209 },
    { width: 311, height: 211 },
    { width: 313, height: 219 },
    { width: 317, height: 221 },
    { width: 337, height: 223 },
    { width: 347, height: 229 },
];

/** The ids of the pages of the saved book whose classes include "cover", as Bloom tests them. */
const readCoverPageIds = (bookFolder: string): Set<string> => {
    const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
    const ids = new Set<string>();
    for (const tag of html.match(/<div\b[^>]*>/g) ?? []) {
        const classes = /\sclass="([^"]*)"/.exec(tag)?.[1] ?? "";
        if (!classes.split(/\s+/).includes("bloom-page")) continue;
        const id = /\sid="([^"]*)"/.exec(tag)?.[1];
        if (id && classes.toLowerCase().includes("cover")) ids.add(id);
    }
    return ids;
};

/** The pictures on each sheet of a PDF, in an order that does not depend on how the PDF lists them. */
const sorted = (pictures: IPixelSize[]): IPixelSize[] =>
    [...pictures].sort((a, b) => a.width - b.width);

/** Check that every sheet of the PDF being previewed is `size`, portrait or landscape as given. */
const expectEverySheetIs = (
    sheets: { paper: IPdfBox }[],
    size: { width: number; height: number },
    mode: PdfMode,
) => {
    for (const [index, sheet] of sheets.entries()) {
        const width = sheet.paper.right - sheet.paper.left;
        const height = sheet.paper.top - sheet.paper.bottom;
        expect(
            Math.abs(width - size.width),
            `Sheet ${index + 1} of the ${mode} PDF is ${width.toFixed(1)}mm wide, not ${size.width}mm.`,
        ).toBeLessThanOrEqual(kToleranceMm);
        expect(
            Math.abs(height - size.height),
            `Sheet ${index + 1} of the ${mode} PDF is ${height.toFixed(1)}mm high, not ${size.height}mm.`,
        ).toBeLessThanOrEqual(kToleranceMm);
    }
};

test("an A5 book makes a Simple PDF, a Booklet Cover and Booklet Insides, each with its pages on the right sheets [Test Case ID 145]", async ({
    bloomApp,
    step,
}, testInfo) => {
    test.setTimeout(600000);
    if ((await getCurrentUiLanguageTag(bloomApp.page)) !== "en")
        await setUiStateViaApi(bloomApp, "en", false);
    const page = bloomApp.page;

    const book = await step(
        "Make an A5 book with a different picture on the covers and on each content page",
        async () => {
            const folder = await makeBookFromTemplate(page, "Basic Book");
            expect(
                await getPageSize(page),
                "test setup: a Basic Book should be A5 portrait",
            ).toBe("A5Portrait");
            await addPage(page, "Basic Text & Image", kContentPictures.length);
            const contentPages = await getContentPages(page);
            expect(
                contentPages,
                "test setup: the book should have a content page for each picture",
            ).toHaveLength(kContentPictures.length);
            const pageIds = (await getPages(page)).map((p) => p.id);
            // The inside back cover and the back cover are the last two pages.
            const pictures = new Map<string, IPixelSize>([
                [pageIds[0], kFrontCoverPicture],
                [pageIds[pageIds.length - 2], kInsideBackCoverPicture],
                [pageIds[pageIds.length - 1], kBackCoverPicture],
                ...contentPages.map(
                    (p, i) =>
                        [p.id, kContentPictures[i]] as [string, IPixelSize],
                ),
            ]);
            for (const [index, [pageId, size]] of [...pictures].entries()) {
                const file = testInfo.outputPath(`picture-${index}.jpg`);
                await makeJpegFile(page, file, size.width, size.height);
                await goToPage(page, pageId);
                if (
                    size !== kFrontCoverPicture &&
                    !contentPages.some((p) => p.id === pageId)
                )
                    await choosePageLayout(page, "Custom");
                await chooseImageFile(page, file);
            }
            return {
                folder,
                pageIds,
                contentIds: contentPages.map((p) => p.id),
                pictures,
            };
        },
    );
    if (!book) throw new Error("The book was never made.");
    const { pageIds, contentIds, pictures } = book;
    // The credits page, the inside front cover, shows the book's license badge, which Bloom keeps
    // in the book folder.
    const licenseBadge = readImageFileSize(
        Path.join(book.folder, "license.png"),
    );
    expect(
        [...pictures.values()].some(
            (p) =>
                p.width === licenseBadge.width &&
                p.height === licenseBadge.height,
        ),
        "test setup: the license badge should differ in size from the test's own pictures",
    ).toBe(false);
    pictures.set(pageIds[1], licenseBadge);
    /**
     * The pictures on one sheet of a PDF that mark a page of the book, sorted. Bloom draws pictures
     * of its own on some pages (a small one on several of them), which say nothing about where the
     * book's pages went.
     */
    const ourPictures = (drawn: IPixelSize[]): IPixelSize[] =>
        sorted(
            drawn.filter((p) =>
                [...pictures.values()].some(
                    (ours) =>
                        ours.width === p.width && ours.height === p.height,
                ),
            ),
        );
    const pictureOfPage = (id: string): IPixelSize[] => {
        const picture = pictures.get(id);
        return picture ? [picture] : [];
    };

    // Bloom puts in the Booklet Cover each page whose classes say "cover", and every other page in
    // the Booklet Insides (Book.cs, GetDomForPdf).
    const coverClassed = readCoverPageIds(book.folder);
    const coverIds = pageIds.filter((id) => coverClassed.has(id));
    const insideIds = pageIds.filter((id) => !coverClassed.has(id));
    expect(
        coverIds,
        "test setup: the book should have four cover pages: front, inside front, inside back and back",
    ).toEqual([
        pageIds[0],
        pageIds[1],
        pageIds[pageIds.length - 2],
        pageIds[pageIds.length - 1],
    ]);
    expect(
        insideIds.length,
        "test setup: the inside pages should fill whole sheets (four pages to a sheet), so that the PDF maker adds no blank page",
    ).toBe(8);
    expect(
        insideIds.slice(-contentIds.length),
        "test setup: the content pages should be the last inside pages",
    ).toEqual(contentIds);

    await step(
        "Simple: every page, one to a sheet of A5, in order",
        async () => {
            await choosePdfMode(page, "Simple");
            const sheets = await getPdfPreviewPageBoxes(page);
            expect(
                sheets,
                "The Simple PDF should have one sheet for each page of the book.",
            ).toHaveLength(pageIds.length);
            expectEverySheetIs(sheets, kA5PortraitMm, "Simple");
            expect(
                (await getPdfPreviewPicturesByPage(page)).map(ourPictures),
                "The Simple PDF should draw each page's picture on that page's sheet, in the book's order.",
            ).toEqual(pageIds.map(pictureOfPage));
        },
    );

    // A booklet's pages, numbered from 1 in the order they are read, go on the sheets so that
    // folding the stack puts them in order: of N pages, the first side of the first sheet has
    // pages N and 1, its other side 2 and N-1, the next sheet N-2 and 3, then 4 and N-3, and so on.
    const sheetsOfBooklet = (ids: string[]): IPixelSize[][] => {
        const sides: IPixelSize[][] = [];
        for (
            let low = 0, high = ids.length - 1;
            low < high;
            low += 2, high -= 2
        ) {
            sides.push(
                sorted([
                    ...pictureOfPage(ids[high]),
                    ...pictureOfPage(ids[low]),
                ]),
            );
            sides.push(
                sorted([
                    ...pictureOfPage(ids[low + 1]),
                    ...pictureOfPage(ids[high - 1]),
                ]),
            );
        }
        return sides;
    };

    await step(
        "Booklet Cover: the four covers, on both sides of one sheet of landscape A4",
        async () => {
            // Leaving the Publish tab gives the PDF & Print screen a fresh start, with no mode chosen.
            await switchTab(page, "edit");
            await waitForEditablePage(page);
            await choosePdfMode(page, "Booklet Cover");
            const sheets = await getPdfPreviewPageBoxes(page);
            expect(
                sheets,
                "The Booklet Cover PDF should have the two sides of one sheet: the outside and the inside of the cover.",
            ).toHaveLength(2);
            expectEverySheetIs(sheets, kA4LandscapeMm, "Booklet Cover");
            expect(
                (await getPdfPreviewPicturesByPage(page, true)).map(
                    ourPictures,
                ),
                "The Booklet Cover PDF should draw the front and back covers' pictures on the outside of the cover, the inside covers' on the inside, and no content page's picture.",
            ).toEqual(sheetsOfBooklet(coverIds));
        },
    );

    await step(
        "Booklet Insides: the inside pages, arranged for folding, on landscape A4",
        async () => {
            await switchTab(page, "edit");
            await waitForEditablePage(page);
            await choosePdfMode(page, "Booklet Insides");
            const sheets = await getPdfPreviewPageBoxes(page);
            expect(
                sheets,
                "The Booklet Insides PDF should have two sides for each four inside pages.",
            ).toHaveLength(insideIds.length / 2);
            expectEverySheetIs(sheets, kA4LandscapeMm, "Booklet Insides");
            expect(
                (await getPdfPreviewPicturesByPage(page, true)).map(
                    ourPictures,
                ),
                "The Booklet Insides PDF should draw each content page's picture on the side of the sheet that puts it in order when folded, and no cover's picture.",
            ).toEqual(sheetsOfBooklet(insideIds));
        },
    );
});
