// The PDF of a book that uses full bleed, made with its print bleed for a print shop.
// Automates Test Case ID 152 in the Notion test inventory.
//
// With "Use full bleed page layout" on, and "Exclude print bleed around page edges" off on the
// PDF & Print screen, Bloom makes each page of the PDF larger than the book's page, so that
// pictures can run past where the printer cuts, and records in each page two boxes that a print
// shop reads from the file and that Acrobat draws as lines: the trim box, where the cut goes, which
// is the size of the book's page; and the bleed box, 3mm outside the cut on every side. Outside
// the bleed the paper has a white strip that holds the crop marks. Bloom's PDF maker has also been
// known to put a blank page after each page of a full bleed book, so the PDF must have exactly one
// page for each page of the book.
//
// The manual case reads the paper size and the trim lines in Acrobat. This reads the same boxes
// from the PDF file the screen previews. The case gives the paper as 6mm larger than A5, with the
// trim lines 3mm inside its edge. That was so before Bloom drew crop marks (BL-15958); the strip
// that holds them is outside those 3mm, so the test measures the 3mm from the bleed box, and
// checks that the paper holds it.
//
// Full bleed needs an Enterprise subscription, so the collection has one.
//
// What stays manual: the Save PDF button, which opens the operating system's save dialog. Without
// a CMYK colour profile chosen, saving copies the file the screen is previewing.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    isPageDrawnWithFullBleed,
    okBookSettings,
    openBookSettings,
    setFullBleedInBookSettings,
} from "../helpers/bookSettings";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { choosePageLayout } from "../helpers/customCover";
import { chooseImageFile } from "../helpers/images";
import { kBleedMm } from "../helpers/pageMargins";
import { getPageSize, setPageSize } from "../helpers/pageSize";
import {
    choosePdfMode,
    getPdfPreviewPageBoxes,
    setExcludePrintBleed,
    type IPdfBox,
    type IPdfPageBoxes,
} from "../helpers/pdfPrint";

test.use({
    collectionSpec: {
        name: "full-bleed-pdf",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

/** The size of an A5 portrait page, which is where the printer cuts the paper. */
const kA5PortraitMm = { width: 148, height: 210 };

/**
 * How far a measurement in the PDF may be from the exact figure. The PDF maker draws the page in
 * whole pixels, which makes an A5 page 0.18mm wider than 148mm.
 */
const kToleranceMm = 0.3;

// A picture with detail in it, shipped with the suite.
const kCoverPicture = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
    "house-landscape.png",
);

test("the PDF of a full bleed book marks the trim at the size of the page and the bleed 3mm outside it, and has one page for each page of the book [Test Case ID 152]", async ({
    page,
    step,
}) => {
    test.setTimeout(420000);

    await step(
        "Make an A5 book from the Paper Comic Book template",
        async () => {
            await makeBookFromTemplate(page, "Paper Comic Book");
            await setPageSize(page, "A5Portrait");
            // Content pages as well as front and back matter, so a page added or dropped anywhere
            // in the book shows in the count.
            await addPage(page, "Comic Full Page", 2);
        },
    );

    await step(
        "Give the cover a custom layout, with a picture that fills it",
        async () => {
            const cover = (await getPages(page))[0];
            await goToPage(page, cover.id);
            await choosePageLayout(page, "Custom");
            await chooseImageFile(page, kCoverPicture);
        },
    );

    await step(
        "Turn on Use full bleed page layout in Book and Page Settings",
        async () => {
            await openBookSettings(page, "Print Publishing");
            await setFullBleedInBookSettings(page, true);
            await okBookSettings(page);
            // Sanity checks: the rest stands on an A5 book that Bloom draws with its bleed.
            expect(await getPageSize(page)).toBe("A5Portrait");
            expect(
                await isPageDrawnWithFullBleed(page),
                "Bloom is not drawing the book's pages with full bleed.",
            ).toBe(true);
        },
    );

    const bookPageCount = (await getPages(page)).length;

    let pdfPages: IPdfPageBoxes[] = [];
    await step("Make the PDF with its print bleed", async () => {
        await setExcludePrintBleed(page, false);
        await choosePdfMode(page, "Simple");
        pdfPages = await getPdfPreviewPageBoxes(page);
    });

    await step(
        "Check the PDF's page count, trim lines, bleed and paper size",
        () => {
            expect(
                pdfPages.length,
                "The PDF should have one page for each page of the book, with none added or missing.",
            ).toBe(bookPageCount);

            for (const [index, pdfPage] of pdfPages.entries()) {
                const which = `page object ${index + 1} of the PDF`;
                const trim = requireBox(pdfPage.trim, "trim", which);
                const bleed = requireBox(pdfPage.bleed, "bleed", which);

                // The printer cuts the paper to the size of the book's page.
                expectMm(
                    trim.right - trim.left,
                    kA5PortraitMm.width,
                    `the width between the trim lines of ${which}`,
                );
                expectMm(
                    trim.top - trim.bottom,
                    kA5PortraitMm.height,
                    `the height between the trim lines of ${which}`,
                );

                // The trim lines sit 3mm inside the bleed, on every side.
                expectMm(
                    trim.left - bleed.left,
                    kBleedMm,
                    `the bleed left of the trim line of ${which}`,
                );
                expectMm(
                    trim.bottom - bleed.bottom,
                    kBleedMm,
                    `the bleed below the trim line of ${which}`,
                );
                expectMm(
                    bleed.right - trim.right,
                    kBleedMm,
                    `the bleed right of the trim line of ${which}`,
                );
                expectMm(
                    bleed.top - trim.top,
                    kBleedMm,
                    `the bleed above the trim line of ${which}`,
                );

                // The paper is large enough to hold the bleed on every side, so it is at least
                // 6mm larger than the page each way: 154 x 216 mm for A5.
                const paper = pdfPage.paper;
                expect(
                    Math.min(
                        bleed.left - paper.left,
                        bleed.bottom - paper.bottom,
                        paper.right - bleed.right,
                        paper.top - bleed.top,
                    ),
                    `The bleed of ${which} runs off the paper: paper ${describeBox(paper)}, bleed ${describeBox(bleed)}.`,
                ).toBeGreaterThanOrEqual(0);
            }
        },
    );
});

/** The box of a PDF page that the test needs, or a failure that says the PDF records none. */
function requireBox(
    box: IPdfBox | undefined,
    name: string,
    which: string,
): IPdfBox {
    if (!box)
        throw new Error(
            `${which} records no ${name} box, so a PDF reader has no ${name} lines to show.`,
        );
    return box;
}

/** A box's edges, for a failure message. */
function describeBox(box: IPdfBox): string {
    return `left ${box.left.toFixed(2)}, bottom ${box.bottom.toFixed(2)}, right ${box.right.toFixed(2)}, top ${box.top.toFixed(2)} (mm)`;
}

/** Assert that a measurement from the PDF, in millimetres, is the expected one within rounding. */
function expectMm(actual: number, expected: number, what: string): void {
    expect(
        Math.abs(actual - expected),
        `${what} should be ${expected}mm, and is ${actual.toFixed(3)}mm`,
    ).toBeLessThanOrEqual(kToleranceMm);
}
