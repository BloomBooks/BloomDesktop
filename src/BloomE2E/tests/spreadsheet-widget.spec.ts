// An HTML5 widget goes through a spreadsheet: Export to Spreadsheet... writes the widget page's
// widget into the "[activities source]" column (the column a person sees as "Widgets") and copies
// the widget's folder beside the spreadsheet, and Import Content from Spreadsheet... puts the same
// widget back on a widget page of another book.
//
// Automates the manual test "Spreadsheet Widget Handling" (Test Case ID 639). The case exports a
// book with the widget "01 Abau T1-Y1" from the team's shared drive; the test makes a minimal widget
// of its own instead (helpers/widgets.ts), with a space in its name as that one has, since the
// column holds the widget's path unencoded and the book holds it URL-encoded. The case's Notion
// summary names import too, so the test also imports the spreadsheet into a new book.
//
// Bloom opens the exported spreadsheet in the machine's spreadsheet program and brings it to the
// front, under --dont-disturb too, so the test does not run where it would take a developer's
// keyboard; see "Export to Spreadsheet opens the spreadsheet" in AUTOMATION-DEBT.md.

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
    exportSelectedBookToSpreadsheet,
    importSpreadsheetIntoSelectedBook,
    readSpreadsheetRows,
} from "../helpers/spreadsheet";
import { getWidgetText, putWidgetOnPage } from "../helpers/widgets";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "spreadsheet-widget",
        languages: ["en"],
        // HTML5 widgets, and spreadsheet export and import, need a subscription that includes them.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const kWidgetName = "TC-639 Widget";
const kWidgetText = "TC-639 widget through a spreadsheet";
/** Where the widget's page is, in the spreadsheet and in the book: Bloom names it this way. */
const kWidgetSource = `activities/${kWidgetName}/index.html`;

/** The first column labels of an export, which the import reads (InternalSpreadsheet.cs). */
const kRowTypeColumnLabel = "[row type]";
const kWidgetSourceColumnLabel = "[activities source]";
const kPageContentRowLabel = "[page content]";
/** The heading that the second row of an export gives the widget column. */
const kWidgetColumnHeading = "Widgets";

/** The widget boxes of the book in `bookFolder`, as its saved file has them. */
async function readWidgetBoxes(
    page: Page,
    bookFolder: string,
): Promise<{ pageId: string; src: string; empty: boolean }[]> {
    const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
    return page.evaluate((source) => {
        const dom = new DOMParser().parseFromString(source, "text/html");
        return Array.from(
            dom.querySelectorAll(".bloom-page .bloom-widgetContainer"),
        ).map((box) => ({
            pageId: box.closest(".bloom-page")!.id,
            src: box.querySelector("iframe")?.getAttribute("src") ?? "",
            empty: box.classList.contains("bloom-noWidgetSelected"),
        }));
    }, html);
}

test.skip(
    launchWithDontDisturb(),
    "Bloom opens the exported spreadsheet in the machine's spreadsheet program and brings it to " +
        "the front, which would take the keyboard. Run with BLOOM_E2E_DONT_DISTURB=0.",
);

test("a widget page's widget goes into the spreadsheet's Widgets column and comes back on import [Test Case ID 639]", async ({
    bloomApp,
    step,
}, testInfo) => {
    test.setTimeout(600000);
    const page = bloomApp.page;

    const bookId = await step(
        "Make a book with a widget on a Widget Page",
        async () => {
            const folder = await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Widget Page");
            await addPage(page, "Just Text", 1, "Basic Book");
            const contentPages = await getContentPages(page);
            const widgetPage = contentPages.at(-2)!;
            const textPage = contentPages.at(-1)!;
            await putWidgetOnPage(
                page,
                widgetPage.id,
                testInfo.outputPath("widgets"),
                kWidgetName,
                kWidgetText,
            );
            // Leaving the page saves it.
            await goToPage(page, textPage.id);
            return readBookId(folder);
        },
    );
    if (!bookId) throw new Error("The book was never made.");

    await step("Check the book holds the widget", async () => {
        const book = findBookFolderById(bloomApp.collectionDir, bookId);
        const boxes = await readWidgetBoxes(page, book);
        expect(
            boxes.map((box) => ({ src: decodeURI(box.src), empty: box.empty })),
            "Sanity check: the book should have one widget box, holding the chosen widget",
        ).toEqual([{ src: kWidgetSource, empty: false }]);
    });

    const exportFolder = testInfo.outputPath("spreadsheets");
    fs.mkdirSync(exportFolder, { recursive: true });
    const spreadsheet = await step("Export it to a spreadsheet", async () => {
        await switchTab(page, "collection");
        await selectBook(
            page,
            findBookFolderById(bloomApp.collectionDir, bookId),
        );
        return exportSelectedBookToSpreadsheet(page, exportFolder);
    });
    if (!spreadsheet) throw new Error("The book was not exported.");

    await step(
        "Check the spreadsheet names the widget in its Widgets column and carries its folder",
        async () => {
            const rows = await readSpreadsheetRows(page, spreadsheet);
            const labels = rows[0] ?? [];
            const rowTypeColumn = labels.indexOf(kRowTypeColumnLabel);
            const widgetColumn = labels.indexOf(kWidgetSourceColumnLabel);
            expect(
                rowTypeColumn,
                `the spreadsheet should have a "${kRowTypeColumnLabel}" column; its labels are ${JSON.stringify(labels)}`,
            ).toBeGreaterThanOrEqual(0);
            expect(
                widgetColumn,
                `the spreadsheet should have a "${kWidgetSourceColumnLabel}" column; its labels are ${JSON.stringify(labels)}`,
            ).toBeGreaterThanOrEqual(0);
            // The second row gives each column the heading a person reads; the case looks for the
            // widget under "Widgets".
            expect(
                rows[1]?.[widgetColumn],
                "the widget column's heading should be the one the case looks under",
            ).toBe(kWidgetColumnHeading);
            const widgetRows = rows
                .slice(2)
                .filter((row) => (row[widgetColumn] ?? "") !== "");
            expect(
                widgetRows.map((row) => ({
                    rowType: row[rowTypeColumn],
                    widget: row[widgetColumn],
                })),
                "one page content row should name the widget",
            ).toEqual([
                { rowType: kPageContentRowLabel, widget: kWidgetSource },
            ]);

            const copied = Path.join(Path.dirname(spreadsheet), kWidgetSource);
            expect(
                fs.existsSync(copied),
                `the export should copy the widget to ${copied}`,
            ).toBe(true);
            expect(fs.readFileSync(copied, "utf8")).toContain(kWidgetText);
        },
    );

    const newBookId = await step("Make a new blank book", async () => {
        await switchTab(page, "collection");
        return readBookId(await makeBookFromTemplate(page, "Basic Book"));
    });
    if (!newBookId) throw new Error("No new book was made.");

    const progress = await step(
        "Import the spreadsheet into the new book",
        async () => {
            await switchTab(page, "collection");
            await selectBook(
                page,
                findBookFolderById(bloomApp.collectionDir, newBookId),
            );
            return importSpreadsheetIntoSelectedBook(page, spreadsheet);
        },
    );
    if (!progress) throw new Error("The spreadsheet was not imported.");

    await step("Check the import reported no problem", () => {
        expect(
            [...progress.errors, ...progress.warnings],
            `The import said:\n${progress.messages.join("\n")}`,
        ).toEqual([]);
        expect(
            bloomApp.takeNonFatalProblems(),
            "Bloom should report no problem during the export and import",
        ).toEqual([]);
    });

    const widgetPageId = await step(
        "Check the new book has the widget on a widget page, with its folder",
        async () => {
            const imported = findBookFolderById(
                bloomApp.collectionDir,
                newBookId,
            );
            const boxes = await readWidgetBoxes(page, imported);
            expect(
                boxes.map((box) => ({
                    src: decodeURI(box.src),
                    empty: box.empty,
                })),
                "the new book should have one widget box, holding the widget",
            ).toEqual([{ src: kWidgetSource, empty: false }]);
            const copied = Path.join(imported, kWidgetSource);
            expect(
                fs.existsSync(copied),
                `the import should copy the widget to ${copied}`,
            ).toBe(true);
            expect(fs.readFileSync(copied, "utf8")).toContain(kWidgetText);
            return boxes[0].pageId;
        },
    );
    if (!widgetPageId) throw new Error("The new book has no widget page.");

    await step("See the widget on its page in the Edit tab", async () => {
        await switchTab(page, "edit");
        await goToPage(page, widgetPageId);
        expect(
            await getWidgetText(
                editablePageFrame(page).frameLocator(
                    ".bloom-widgetContainer iframe",
                ),
            ),
        ).toBe(kWidgetText);
    });
});
