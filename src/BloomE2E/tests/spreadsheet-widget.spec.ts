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
import {
    chooseWidgetOnPage,
    getWidgetText,
    writeWidgetFolder,
} from "../helpers/widgets";
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
/**
 * The same, as the book's file holds it in the widget's iframe: URL-encoded, with its slashes left
 * as they are, so that the widget's own relative links resolve inside its folder.
 */
const kWidgetSourceInBook = encodeURI(kWidgetSource);

/** The first column labels of an export, which the import reads (InternalSpreadsheet.cs). */
const kRowTypeColumnLabel = "[row type]";
const kWidgetSourceColumnLabel = "[activities source]";
const kPageContentRowLabel = "[page content]";
/** The heading that the second row of an export gives the widget column. */
const kWidgetColumnHeading = "Widgets";

/**
 * A file that the widget carries besides its index.html, in a folder of its own, as a real widget
 * carries scripts, pictures and sounds. Export and import copy the widget's whole folder.
 */
const kWidgetAsset = Path.join("assets", "data.txt");
const kWidgetAssetText = "TC-639 widget asset";

/** One widget box of a book, as the book's saved file has it. */
interface IWidgetBox {
    pageId: string;
    /** The template pages that the box's page was made from (data-pagelineage). */
    pageLineage: string;
    /** The src of the box's iframe, as the file holds it. */
    src: string;
    /** Whether the box still waits for a widget to be chosen. */
    empty: boolean;
}

/** The widget boxes of the book in `bookFolder`, as its saved file has them. */
async function readWidgetBoxes(
    page: Page,
    bookFolder: string,
): Promise<IWidgetBox[]> {
    const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
    return page.evaluate((source) => {
        const dom = new DOMParser().parseFromString(source, "text/html");
        return Array.from(
            dom.querySelectorAll(".bloom-page .bloom-widgetContainer"),
        ).map((box) => {
            const bloomPage = box.closest(".bloom-page")!;
            return {
                pageId: bloomPage.id,
                pageLineage: bloomPage.getAttribute("data-pagelineage") ?? "",
                src: box.querySelector("iframe")?.getAttribute("src") ?? "",
                empty: box.classList.contains("bloom-noWidgetSelected"),
            };
        });
    }, html);
}

/** Check that the widget's folder at `widgetFolder` holds the widget's page and its asset. */
function expectWidgetFolder(widgetFolder: string, what: string): void {
    for (const [file, text] of [
        ["index.html", kWidgetText],
        [kWidgetAsset, kWidgetAssetText],
    ]) {
        const path = Path.join(widgetFolder, file);
        expect(fs.existsSync(path), `${what} should have ${path}`).toBe(true);
        expect(fs.readFileSync(path, "utf8")).toContain(text);
    }
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
            const widgetIndex = writeWidgetFolder(
                testInfo.outputPath("widgets"),
                kWidgetName,
                kWidgetText,
            );
            const asset = Path.join(Path.dirname(widgetIndex), kWidgetAsset);
            fs.mkdirSync(Path.dirname(asset), { recursive: true });
            fs.writeFileSync(asset, kWidgetAssetText);
            await goToPage(page, widgetPage.id);
            await chooseWidgetOnPage(page, widgetIndex);
            // Leaving the page saves it.
            await goToPage(page, textPage.id);
            return readBookId(folder);
        },
    );
    if (!bookId) throw new Error("The book was never made.");

    const original = await step("Check the book holds the widget", async () => {
        const book = findBookFolderById(bloomApp.collectionDir, bookId);
        const boxes = await readWidgetBoxes(page, book);
        expect(
            boxes.map((box) => ({ src: box.src, empty: box.empty })),
            "Sanity check: the book should have one widget box, holding the chosen widget",
        ).toEqual([{ src: kWidgetSourceInBook, empty: false }]);
        expect(
            boxes[0].pageLineage,
            "Sanity check: the widget page should name the template page it was made from",
        ).not.toBe("");
        expectWidgetFolder(
            Path.join(book, Path.dirname(kWidgetSource)),
            "Sanity check: the book",
        );
        return boxes[0];
    });
    if (!original) throw new Error("The book holds no widget.");

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

            expectWidgetFolder(
                Path.join(
                    Path.dirname(spreadsheet),
                    Path.dirname(kWidgetSource),
                ),
                "the export",
            );
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
            // The same template page as the original's: a Captive Widget Page, which also holds
            // a widget, would hide Bloom Reader's navigation.
            expect(
                boxes.map((box) => ({
                    pageLineage: box.pageLineage,
                    src: box.src,
                    empty: box.empty,
                })),
                "the new book should have one widget box, holding the widget, on the same kind of page as the original's",
            ).toEqual([
                {
                    pageLineage: original.pageLineage,
                    src: kWidgetSourceInBook,
                    empty: false,
                },
            ]);
            expectWidgetFolder(
                Path.join(imported, Path.dirname(kWidgetSource)),
                "the new book",
            );
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
