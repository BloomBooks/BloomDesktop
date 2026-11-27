// The collection's Page Numbering Style decides which digits the page numbers of a PDF are written
// in. Automates the manual test "Custom Page Numbering PDF" [Test Case ID 150]: a person changes
// Page Numbering in Collection Settings to something other than the usual digits, publishes a PDF,
// and confirms that the PDF numbers its pages in the style they chose.
//
// The style is now on the first language's More page of the Languages page of Collection Settings
// ("Page Numbering Style"), and changing it makes OK a Restart. The test makes a book with more than
// nine numbered pages, so that some numbers have two digits, and first makes its PDF with the usual
// digits. That run is a control: it shows that the numbers are read from the PDF where they are
// drawn, and it gives each page's number. After Devanagari is chosen, the PDF must draw each of
// those numbers, at the same place on the same page, written in Devanagari digits, and no page may
// still draw a number in the usual digits. The numbers are read from the PDF file itself, as a PDF
// reader copies its text (pdfPrint.ts).
//
// Not done: the case's note to turn on Performance Measuring and watch it while testing. That is
// something for the tester to keep an eye on, not a check of the PDF.

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { addPage, getPages, makeBookFromTemplate } from "../helpers/bookMaking";
import { selectBook } from "../helpers/collection";
import {
    chooseCollectionSettingsListChoiceByValue,
    closeCollectionSettingsLanguageMore,
    openCollectionSettings,
    openCollectionSettingsLanguageMore,
    readSavedCollectionSetting,
    restartFromCollectionSettings,
    showCollectionSettingsPage,
} from "../helpers/collectionSettings";
import {
    choosePdfMode,
    getPdfPreviewPageCount,
    getPdfPreviewPageDrawing,
    type IPdfTextRun,
} from "../helpers/pdfPrint";
import { setUiStateViaApi } from "../helpers/uiLanguage";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "pdf-custom-page-numbering", languages: ["en"] },
});

const kLanguage1 = "Written Language 1";
const kPageNumberingStyle = "Page Numbering Style";
/** The style chosen, by its value in the list, and its ten digits, 0 to 9. */
const kStyle = "Devanagari";
const kStyleDigits = "०१२३४५६७८९";

/** A page number as the PDF draws it on one of its pages. */
interface IDrawnNumber {
    pdfPage: number;
    text: string;
    x: number;
    y: number;
}

/** Write a number in the usual digits with the chosen style's digits instead. */
function inStyle(usualDigits: string): string {
    return [...usualDigits].map((d) => kStyleDigits[Number(d)]).join("");
}

/**
 * The numbers a page draws written wholly in these digits, each with where it starts. The PDF may
 * draw one number as several runs (a digit from a fallback font is a run of its own), so runs of
 * digits on one baseline are joined, left to right, into one number.
 */
function numbersIn(texts: IPdfTextRun[], digits: string): IPdfTextRun[] {
    const number = new RegExp(`^[${digits}]+$`, "u");
    const runs = texts
        .filter((run) => number.test(run.text.trim()))
        .sort((a, b) => a.y - b.y || a.x - b.x);
    const joined: IPdfTextRun[] = [];
    for (const run of runs) {
        const last = joined[joined.length - 1];
        if (last && Math.abs(last.y - run.y) < 0.5)
            last.text += run.text.trim();
        else joined.push({ ...run, text: run.text.trim() });
    }
    return joined;
}

/** Make the Simple PDF of the selected book and read the text runs of each of its pages. */
async function readPdfTexts(page: Page): Promise<IPdfTextRun[][]> {
    await choosePdfMode(page, "Simple");
    const pageCount = await getPdfPreviewPageCount(page);
    expect(
        pageCount,
        "The PDF should have one page for each page of the book.",
    ).toBe((await getPages(page)).length);
    const texts: IPdfTextRun[][] = [];
    for (let i = 0; i < pageCount; i++)
        texts.push((await getPdfPreviewPageDrawing(page, i)).texts);
    return texts;
}

test("a PDF numbers its pages in the collection's Page Numbering Style [Test Case ID 150]", async ({
    bloomApp,
    step,
}) => {
    test.setTimeout(480000);

    // The Settings helpers find the dialog's pages and rows by their English names. Switching the
    // UI language can replace the shell page, so the page is read from bloomApp after it.
    await setUiStateViaApi(bloomApp, "en", false);
    const page = bloomApp.page;

    const bookFolder = (await step(
        "Make a book with more than nine numbered pages, so that some page numbers have two digits",
        async () => {
            const folder = await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Just Text", 10);
            return folder;
        },
    ))!;

    const usualNumbers = (await step(
        "With the usual page numbering, the PDF numbers its content pages in the usual digits, one after another",
        async () => {
            expect(
                readSavedCollectionSetting(
                    bloomApp.collectionDir,
                    "PageNumberStyle",
                ),
                "sanity check: the collection starts with the usual digits",
            ).toBe("Decimal");
            const texts = await readPdfTexts(page);
            const drawn: IDrawnNumber[] = [];
            texts.forEach((pageTexts, pdfPage) => {
                const numbers = numbersIn(pageTexts, "0-9");
                expect(
                    numbers.length,
                    `Page ${pdfPage + 1} of the PDF should draw at most one page number. It draws: ${JSON.stringify(numbers)}`,
                ).toBeLessThanOrEqual(1);
                if (numbers.length === 1)
                    drawn.push({
                        pdfPage,
                        text: numbers[0].text.trim(),
                        x: numbers[0].x,
                        y: numbers[0].y,
                    });
            });
            const contentPages = (await getPages(page))
                .map((p, index) => ({ ...p, index }))
                .filter((p) => p.isContentPage);
            expect(
                drawn.map((n) => n.pdfPage),
                "The PDF should draw a page number on each content page and on no other page.",
            ).toEqual(contentPages.map((p) => p.index));
            // Bloom counts the title page too, so the first content page need not be 1; each
            // content page after it is one more.
            const first = Number(drawn[0].text);
            expect(
                drawn.map((n) => n.text),
                "The PDF should number the content pages one after another.",
            ).toEqual(drawn.map((_, i) => String(first + i)));
            expect(
                drawn.some((n) => n.text.length > 1),
                "sanity check: some page number has more than one digit",
            ).toBe(true);
            return drawn;
        },
    ))!;

    const reopened = (await step(
        `Choose ${kStyle} as the ${kPageNumberingStyle} on the first language's More page of Collection Settings, and click Restart`,
        async () => {
            await switchTab(page, "collection");
            await openCollectionSettings(page);
            await showCollectionSettingsPage(page, "Languages");
            await openCollectionSettingsLanguageMore(page, kLanguage1);
            await chooseCollectionSettingsListChoiceByValue(
                page,
                kPageNumberingStyle,
                kStyle,
            );
            await closeCollectionSettingsLanguageMore(page);
            return restartFromCollectionSettings(bloomApp);
        },
    ))!;
    expect(
        readSavedCollectionSetting(bloomApp.collectionDir, "PageNumberStyle"),
        `Bloom should have saved ${kStyle} as the collection's page numbering style.`,
    ).toBe(kStyle);

    await step(
        `Publish the PDF again: each page number is written in ${kStyle} digits, where the usual one was`,
        async () => {
            await selectBook(reopened, bookFolder);
            const texts = await readPdfTexts(reopened);
            texts.forEach((pageTexts, pdfPage) => {
                const usual = numbersIn(pageTexts, "0-9");
                expect(
                    usual,
                    `Page ${pdfPage + 1} of the PDF still draws a number in the usual digits.`,
                ).toEqual([]);
                const styled = numbersIn(pageTexts, kStyleDigits);
                const was = usualNumbers.find((n) => n.pdfPage === pdfPage);
                if (!was) {
                    expect(
                        styled,
                        `Page ${pdfPage + 1} of the PDF had no page number before, and should have none now.`,
                    ).toEqual([]);
                    return;
                }
                expect(
                    styled.map((run) => run.text.trim()),
                    `Page ${pdfPage + 1} of the PDF should draw its number ${was.text} as ${inStyle(was.text)}. It draws: ${JSON.stringify(pageTexts.map((run) => run.text))}`,
                ).toEqual([inStyle(was.text)]);
                // The number keeps its place on the page: within 5 mm of where the usual one
                // started (other digits are wider or narrower, and a centred number moves a little).
                expect(
                    Math.abs(styled[0].x - was.x),
                    `The page number on page ${pdfPage + 1} moved across the page.`,
                ).toBeLessThan(5);
                expect(
                    Math.abs(styled[0].y - was.y),
                    `The page number on page ${pdfPage + 1} moved up or down the page.`,
                ).toBeLessThan(5);
            });
        },
    );
});
