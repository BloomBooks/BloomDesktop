// A "Choose Word from Image" game page goes through a spreadsheet: Export to Spreadsheet... names
// the page's kind in the "[page type]" column (the column a person sees as "Page Type") and writes
// its picture and its words, and Import Content from Spreadsheet... into a new blank book makes a
// page of the same kind again, with the same picture and with each word in the same answer button.
//
// Automates the manual test "Spreadsheet Activity Page Handling" (Test Case ID 640). Its Notion card
// has no steps of its own; the steps are the ones of its source, TC259 in
// BloomBooks/bloom-test-cases.
//
// The case enters a subscription code in the Settings dialog; this test sets it in the collection
// (collectionSpec), since the dialog has tests of its own. The case finds a picture of a car with the
// image toolbox's search, which needs the Art Of Reading collection; this test puts a picture from
// its fixtures on the page, as choosing a file does. The case also gives the book a title and a
// copyright, which go through a spreadsheet as book data (spreadsheet-xmatter.spec.ts, Test Case ID
// 637), so this test leaves them out.
//
// Bloom opens the exported spreadsheet in the machine's spreadsheet program and brings it to the
// front, under --dont-disturb too, so the test does not run where it would take a developer's
// keyboard; see "Export to Spreadsheet opens the spreadsheet" in AUTOMATION-DEBT.md.

import * as fs from "node:fs";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { launchWithDontDisturb } from "../fixtures/launchBloom";
import { bookHtmlPath } from "../helpers/bookHtml";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    chooseChoicePromptPicture,
    typeChoiceWords,
} from "../helpers/choiceActivities";
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
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "spreadsheet-choice-activity",
        languages: ["en"],
        // Game pages, and spreadsheet export and import, need a subscription that includes them.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const LANG = "en";

/** The template page's label, which Bloom writes in the Page Type column and looks up on import. */
const kGamePageType = "Choose Word from Image";

/** The words the case types: the first, into the right-answer button, and then the two others. */
const kAnswers = { correct: "Apple", wrong: ["Test", "Car"] };

const kPicture = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
    "bird.png",
);

/** The column labels of an export, which the import reads (InternalSpreadsheet.cs). */
const kRowTypeColumnLabel = "[row type]";
const kPageTypeColumnLabel = "[page type]";
const kImageSourceColumnLabel = "[image source]";
const kTextColumnLabel = `[${LANG}]`;
const kPageContentRowLabel = "[page content]";
/** The heading that the second row of an export gives the page type column. */
const kPageTypeColumnHeading = "Page Type";

/** A "Choose Word from Image" page, as a book's saved file has it. */
interface IChoicePageOnDisk {
    /** The template pages that the page was made from (data-pagelineage). */
    pageLineage: string;
    /** The src of the picture the reader matches a word to, as the file holds it. */
    promptPicture: string;
    /** The text of each answer button in LANG, with its role, in the order the page holds them. */
    answers: { role: string; word: string }[];
}

/** The content pages of the book in `bookFolder` that are choice activities, as its file has them. */
async function readChoicePages(
    page: Page,
    bookFolder: string,
): Promise<IChoicePageOnDisk[]> {
    const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
    return page.evaluate(
        ({ source, lang }) => {
            const dom = new DOMParser().parseFromString(source, "text/html");
            return Array.from(
                dom.querySelectorAll(
                    '.bloom-page[data-activity="simple-dom-choice"]',
                ),
            ).map((bloomPage) => ({
                pageLineage: bloomPage.getAttribute("data-pagelineage") ?? "",
                promptPicture:
                    bloomPage
                        .querySelector(".imageThenChoices > .bloom-canvas img")
                        ?.getAttribute("src") ?? "",
                answers: Array.from(
                    bloomPage.querySelectorAll(
                        ".player-button[data-activityrole]",
                    ),
                ).map((button) => ({
                    role: button.getAttribute("data-activityrole") ?? "",
                    word: (
                        button.querySelector(`.bloom-editable[lang="${lang}"]`)
                            ?.textContent ?? ""
                    ).trim(),
                })),
            }));
        },
        { source: html, lang: LANG },
    );
}

/** The answers as a choice page should hold them: the right answer first, as the template has it. */
const kExpectedAnswers = [
    { role: "correct-answer", word: kAnswers.correct },
    ...kAnswers.wrong.map((word) => ({ role: "wrong-answer", word })),
];

test.skip(
    launchWithDontDisturb(),
    "Bloom opens the exported spreadsheet in the machine's spreadsheet program and brings it to " +
        "the front, which would take the keyboard. Run with BLOOM_E2E_DONT_DISTURB=0.",
);

test("a Choose Word from Image page goes into the spreadsheet with its page type and comes back on import [Test Case ID 640]", async ({
    bloomApp,
    step,
}, testInfo) => {
    test.setTimeout(600000);
    const page = bloomApp.page;

    const bookId = await step(
        "Make a book with a Choose Word from Image page",
        async () => {
            const folder = await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, kGamePageType);
            await addPage(page, "Just Text", 1, "Basic Book");
            const contentPages = await getContentPages(page);
            const gamePage = contentPages.at(-2)!;
            const textPage = contentPages.at(-1)!;
            await goToPage(page, gamePage.id);
            await chooseChoicePromptPicture(page, kPicture);
            await typeChoiceWords(page, LANG, kAnswers);
            // Leaving the page saves it.
            await goToPage(page, textPage.id);
            return readBookId(folder);
        },
    );
    if (!bookId) throw new Error("The book was never made.");

    const original = await step(
        "Check the book holds the game page",
        async () => {
            const book = findBookFolderById(bloomApp.collectionDir, bookId);
            const pages = await readChoicePages(page, book);
            expect(
                pages.map((p) => p.answers),
                "Sanity check: the book should have one choice page, holding the words",
            ).toEqual([kExpectedAnswers]);
            expect(
                pages[0].pageLineage,
                "Sanity check: the game page should name the template page it was made from",
            ).not.toBe("");
            expect(
                pages[0].promptPicture,
                "Sanity check: the game page should hold the chosen picture",
            ).toMatch(/^bird/);
            expect(
                fs.existsSync(Path.join(book, pages[0].promptPicture)),
                "Sanity check: the book's folder should hold the picture",
            ).toBe(true);
            return pages[0];
        },
    );
    if (!original) throw new Error("The book holds no game page.");

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
        "Check the spreadsheet names the page's type and holds its picture and words",
        async () => {
            const rows = await readSpreadsheetRows(page, spreadsheet);
            const labels = rows[0] ?? [];
            const column = (label: string) => {
                const index = labels.indexOf(label);
                expect(
                    index,
                    `the spreadsheet should have a "${label}" column; its labels are ${JSON.stringify(labels)}`,
                ).toBeGreaterThanOrEqual(0);
                return index;
            };
            const rowTypeColumn = column(kRowTypeColumnLabel);
            const pageTypeColumn = column(kPageTypeColumnLabel);
            const imageColumn = column(kImageSourceColumnLabel);
            const textColumn = column(kTextColumnLabel);
            // The second row gives each column the heading a person reads; the case looks for the
            // page's kind under "Page Type".
            expect(
                rows[1]?.[pageTypeColumn],
                "the page type column's heading should be the one the case looks under",
            ).toBe(kPageTypeColumnHeading);

            const contentRows = rows
                .slice(2)
                .filter((row) => row[rowTypeColumn] === kPageContentRowLabel);
            const gameRowIndex = contentRows.findIndex(
                (row) => row[pageTypeColumn] === kGamePageType,
            );
            expect(
                gameRowIndex,
                `a page content row should have "${kGamePageType}" as its page type; the page types are ${JSON.stringify(contentRows.map((row) => row[pageTypeColumn] ?? ""))}`,
            ).toBeGreaterThanOrEqual(0);
            // Bloom writes a page's first row with its type, and the page's further rows without
            // one, so the page's rows run up to the next row that names a type.
            const nextTyped = contentRows.findIndex(
                (row, index) =>
                    index > gameRowIndex && (row[pageTypeColumn] ?? "") !== "",
            );
            const gameRows = contentRows.slice(
                gameRowIndex,
                nextTyped < 0 ? undefined : nextTyped,
            );
            const words = gameRows.map((row) => row[textColumn] ?? "");
            for (const word of [kAnswers.correct, ...kAnswers.wrong])
                expect(
                    words,
                    `the game page's rows should hold the word "${word}"`,
                ).toContain(word);
            expect(
                words.indexOf(kAnswers.correct),
                "the right answer should come before the wrong ones, as the page holds them",
            ).toBeLessThan(words.indexOf(kAnswers.wrong[0]));
            const pictures = gameRows
                .map((row) => row[imageColumn] ?? "")
                .filter((source) => source !== "");
            // Bloom builds the path with Path.Combine, so it has this machine's separator.
            expect(
                pictures,
                "the game page's rows should name its picture",
            ).toEqual([
                Path.join("images", Path.basename(original.promptPicture)),
            ]);
            expect(
                fs.existsSync(
                    Path.join(Path.dirname(spreadsheet), pictures[0]),
                ),
                "the export should carry the picture beside the spreadsheet",
            ).toBe(true);
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

    await step(
        "Check the new book has the game page, with its picture and each word in its button",
        async () => {
            const imported = findBookFolderById(
                bloomApp.collectionDir,
                newBookId,
            );
            const pages = await readChoicePages(page, imported);
            expect(
                pages.map((p) => ({
                    pageLineage: p.pageLineage,
                    promptPicture: p.promptPicture,
                    answers: p.answers,
                })),
                "the new book should have one Choose Word from Image page, made from the same template page, with the same picture and words",
            ).toEqual([
                {
                    pageLineage: original.pageLineage,
                    promptPicture: original.promptPicture,
                    answers: kExpectedAnswers,
                },
            ]);
            expect(
                fs.existsSync(Path.join(imported, original.promptPicture)),
                "the new book's folder should hold the picture",
            ).toBe(true);
        },
    );
});
