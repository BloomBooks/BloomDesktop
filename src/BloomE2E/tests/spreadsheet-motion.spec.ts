// A Motion Book goes through a spreadsheet. A book with motion on the pictures of two pages is
// exported with More > Export to Spreadsheet..., the rows of its second page are deleted from the
// spreadsheet, and the spreadsheet is imported into the same book with More > Import Content from
// Spreadsheet...: the second page goes, and the first keeps its picture and its motion.
//
// Automates the manual test "Spreadsheet Motion Book Handling" (Test Case ID 642). The case's
// steps are in bloom-test-cases, test case 255. They search Art of Reading for a car picture; the
// test makes a picture file of its own instead. Motion is on for a page as soon as the Motion tool
// is open on it and the page has a picture: the tool then saves its start and end rectangles
// (data-initialrect and data-finalrect on the page's bloom-canvas). The case's step 63 imports
// into "The Moon and the Cap" and then looks for the deleted page in the book it exported; the
// test imports into the exported book, which is where the case looks.
//
// The case checks that the motion plays when Preview is clicked. The test checks that Bloom starts
// the preview; whether the picture moves well is for a person to see.
//
// Bloom opens the exported spreadsheet in the machine's spreadsheet program and brings it to the
// front, under --dont-disturb too, so the test does not run where it would take a developer's
// keyboard; see "Export to Spreadsheet opens the spreadsheet" in AUTOMATION-DEBT.md. The test has
// no spreadsheet program to drive, so it deletes the rows by rewriting the file
// (helpers/spreadsheetEditing.ts).

import * as fs from "node:fs";
import * as Path from "node:path";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { launchWithDontDisturb } from "../fixtures/launchBloom";
import { bookHtmlPath } from "../helpers/bookHtml";
import {
    addPage,
    editablePageFrame,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    findBookFolderById,
    readBookId,
    selectBook,
} from "../helpers/collection";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    chooseImageFile,
    makeJpegFile,
    picturePixels,
} from "../helpers/images";
import {
    exportSelectedBookToSpreadsheet,
    importSpreadsheetIntoSelectedBook,
    readSpreadsheetRows,
} from "../helpers/spreadsheet";
import { deleteSpreadsheetPageRows } from "../helpers/spreadsheetEditing";
import { enableToolForBook, expandTool } from "../helpers/toolbox";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "spreadsheet-motion",
        languages: ["en"],
        // The Motion tool and the spreadsheet commands need a subscription that includes them.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

/** The Add Page labels of the two pages the case adds ("Basic Text & Picture" and "Just Picture" in its steps), in Basic Book. */
const kPictureAndTextPage = "Basic Text & Image";
const kJustPicturePage = "Just an Image";

/** The Motion tool's controls: its "Enable motion on this page" check box and its Preview button. */
const kMotionCheckbox =
    ".ui-motionBody .enable-checkbox input[type='checkbox']";
const kMotionPreview = "#motion-preview";

/** The first column labels of an export, which the import reads (InternalSpreadsheet.cs). */
const kPageNumberColumnLabel = "(exported page)";
const kImageSourceColumnLabel = "[image source]";

/** One content page of a book, as its saved file has it. */
interface IMotionPage {
    id: string;
    /** The page's number, which the spreadsheet's "(exported page)" column gives it. */
    pageNumber: string;
    /** The file name of the picture in the page's first bloom-canvas. */
    picture: string;
    /** Where the motion starts and ends, or null when the page has no motion. */
    initialRect: string | null;
    finalRect: string | null;
}

/** The content pages of the book in `bookFolder`, as its saved file has them. */
async function readMotionPages(
    page: Page,
    bookFolder: string,
): Promise<IMotionPage[]> {
    const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
    return page.evaluate((source) => {
        const dom = new DOMParser().parseFromString(source, "text/html");
        return Array.from(dom.querySelectorAll(".bloom-page.numberedPage")).map(
            (bloomPage) => {
                const canvas = bloomPage.querySelector(".bloom-canvas");
                const img = canvas?.querySelector(
                    ".bloom-backgroundImage .bloom-imageContainer img",
                );
                return {
                    id: bloomPage.id,
                    pageNumber:
                        bloomPage.getAttribute("data-page-number") ?? "",
                    picture: decodeURIComponent(img?.getAttribute("src") ?? ""),
                    initialRect:
                        canvas?.getAttribute("data-initialrect") ?? null,
                    finalRect: canvas?.getAttribute("data-finalrect") ?? null,
                };
            },
        );
    }, html);
}

/**
 * Put the picture at `picturePath` on the page being edited, with the Motion tool open, and check
 * that the tool turns motion on for the page and that its Preview starts playing.
 */
async function giveShownPageMotion(
    page: Page,
    picturePath: string,
): Promise<void> {
    await chooseImageFile(page, picturePath);
    const tool = await expandTool(page, "motion", kMotionCheckbox);
    await expect(
        tool.locator(kMotionCheckbox),
        'the Motion tool should have "Enable motion on this page" checked for a page with a picture',
    ).toBeChecked();
    const canvas = editablePageFrame(page).locator(".bloom-canvas").first();
    await expect(
        canvas,
        "the Motion tool should give the picture a start rectangle",
    ).toHaveAttribute("data-initialrect", /\S/, { timeout: 30000 });
    await expect(
        canvas,
        "the Motion tool should give the picture an end rectangle",
    ).toHaveAttribute("data-finalrect", /\S/);
    const preview = tool.locator(kMotionPreview);
    await preview.click();
    await expect(
        preview,
        "clicking Preview should start playing the motion",
    ).toHaveClass(/\bplaying\b/, { timeout: 30000 });
    await preview.click();
    await expect(
        preview,
        "clicking Preview again should stop the motion",
    ).not.toHaveClass(/\bplaying\b/, { timeout: 30000 });
}

test.skip(
    launchWithDontDisturb(),
    "Bloom opens the exported spreadsheet in the machine's spreadsheet program and brings it to " +
        "the front, which would take the keyboard. Run with BLOOM_E2E_DONT_DISTURB=0.",
);

test("a Motion Book loses the page whose rows were deleted from its spreadsheet and keeps the other's motion [Test Case ID 642]", async ({
    bloomApp,
    step,
}, testInfo) => {
    test.setTimeout(600000);
    const page = bloomApp.page;

    const picturePath = testInfo.outputPath("car.jpg");
    const bookId = await step(
        "Make a book with motion on the pictures of two pages",
        async () => {
            const folder = await makeBookFromTemplate(page, "Basic Book");
            await makeJpegFile(page, picturePath, 800, 600);
            await enableToolForBook(page, folder, "motion");
            await addPage(page, kPictureAndTextPage, 1, "Basic Book");
            await addPage(page, kJustPicturePage, 1, "Basic Book");
            const contentPages = await getContentPages(page);
            for (const contentPage of contentPages) {
                await goToPage(page, contentPage.id);
                await giveShownPageMotion(page, picturePath);
            }
            // Leaving the last page saves it.
            await goToPage(page, contentPages[0].id);
            return readBookId(folder);
        },
    );
    if (!bookId) throw new Error("The book was never made.");
    const motionBook = () => findBookFolderById(bloomApp.collectionDir, bookId);

    const original = await step(
        "Check the book holds both pictures with their motion",
        async () => {
            const pages = await readMotionPages(page, motionBook());
            expect(
                pages.length,
                "Sanity check: the book should have the two pages the test added",
            ).toBe(2);
            for (const [index, motionPage] of pages.entries()) {
                expect(
                    motionPage.picture,
                    `Sanity check: page ${index + 1} should have a picture`,
                ).not.toMatch(/^$|placeHolder/);
                expect(
                    motionPage.initialRect,
                    `Sanity check: page ${index + 1}'s picture should have a start rectangle`,
                ).toBeTruthy();
                expect(
                    motionPage.finalRect,
                    `Sanity check: page ${index + 1}'s picture should have an end rectangle`,
                ).toBeTruthy();
            }
            return pages;
        },
    );
    if (!original) throw new Error("The book has no motion pages.");
    // The import copies each picture into the book again, under a new name when the old one is
    // taken, so the first page's picture is compared by its pixels.
    const firstPictureBefore = await picturePixels(
        page,
        Path.join(motionBook(), original[0].picture),
    );

    const exportFolder = testInfo.outputPath("spreadsheets");
    fs.mkdirSync(exportFolder, { recursive: true });
    const bothPages = await step("Export it to a spreadsheet", async () => {
        await switchTab(page, "collection");
        await selectBook(page, motionBook());
        return exportSelectedBookToSpreadsheet(page, exportFolder);
    });
    if (!bothPages) throw new Error("The book was not exported.");

    await step(
        "Check the spreadsheet has the picture of each page",
        async () => {
            const rows = await readSpreadsheetRows(page, bothPages);
            const labels = rows[0] ?? [];
            const pageColumn = labels.indexOf(kPageNumberColumnLabel);
            const imageColumn = labels.indexOf(kImageSourceColumnLabel);
            expect(
                [pageColumn, imageColumn].every((column) => column >= 0),
                `the spreadsheet should have "${kPageNumberColumnLabel}" and "${kImageSourceColumnLabel}" columns; its labels are ${JSON.stringify(labels)}`,
            ).toBe(true);
            // Front matter rows, such as the cover's picture, have no page number.
            const pictures = rows
                .slice(2)
                .filter(
                    (row) =>
                        (row[imageColumn] ?? "") !== "" &&
                        (row[pageColumn] ?? "") !== "",
                )
                .map((row) => ({
                    pageNumber: row[pageColumn],
                    // The column holds the picture's path with backslashes (Path.Combine).
                    picture: Path.win32.basename(row[imageColumn]),
                }));
            expect(pictures, "each page's picture should have a row").toEqual(
                original.map((p) => ({
                    pageNumber: p.pageNumber,
                    picture: p.picture,
                })),
            );
        },
    );

    const firstPageOnly = Path.join(
        Path.dirname(bothPages),
        "Motion Book without page 2.xlsx",
    );
    await step("Delete the second page's rows from the spreadsheet", () => {
        expect(
            deleteSpreadsheetPageRows(
                bothPages,
                Number(original[1].pageNumber),
                firstPageOnly,
            ),
            "the second page should have at least one row",
        ).toBeGreaterThan(0);
    });

    await step(
        "Import the spreadsheet without the second page into the Motion Book",
        async () => {
            await switchTab(page, "collection");
            await selectBook(page, motionBook());
            bloomApp.takeNonFatalProblems();
            const progress = await importSpreadsheetIntoSelectedBook(
                page,
                firstPageOnly,
            );
            expect(
                [...progress.errors, ...progress.warnings],
                `The import said:\n${progress.messages.join("\n")}`,
            ).toEqual([]);
            expect(
                bloomApp.takeNonFatalProblems(),
                "Bloom should report no problem during the export and import",
            ).toEqual([]);
        },
    );

    await step(
        "Check the book lost its second page and the first kept its picture and motion",
        async () => {
            const after = await readMotionPages(page, motionBook());
            expect(
                after.map((p) => ({
                    initialRect: p.initialRect,
                    finalRect: p.finalRect,
                })),
                "the book should have only its first page, with the same motion",
            ).toEqual([
                {
                    initialRect: original[0].initialRect,
                    finalRect: original[0].finalRect,
                },
            ]);
            expect(
                await picturePixels(
                    page,
                    Path.join(motionBook(), after[0].picture),
                ),
                `the first page should still show its picture (it shows ${after[0].picture})`,
            ).toBe(firstPictureBefore);
        },
    );
});
