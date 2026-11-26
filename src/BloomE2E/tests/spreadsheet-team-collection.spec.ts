// The two spreadsheet commands in the More submenu of a book's menu, in a Team Collection: Export
// to Spreadsheet is offered on every book, checked out or not, since it only reads the book;
// Import Content from Spreadsheet changes the book, so it is greyed out unless the book is checked
// out to me. The test makes a Team Collection of two books (which Bloom checks in), reads both
// books' menus, checks one out and reads both again, then checks it back in.
//
// Automates the manual test "Team Collection Spreadsheet Access" (Test Case ID 625).

import * as fs from "node:fs";
import * as os from "node:os";
import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import type { Page } from "@playwright/test";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import {
    clickBookTile,
    listEditableBooks,
    readSelectedBookMenu,
} from "../helpers/collection";
import type { IBookMenuEntry } from "../helpers/collection";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    checkInSelectedBook,
    checkOutSelectedBook,
    createTeamCollection,
    isSelectedBookSaveable,
} from "../helpers/teamCollection";
import { switchTab } from "../helpers/workspace";

// Team Collections need a tier above Basic and their experimental feature; the spreadsheet
// commands need Local Community or above, so Enterprise gives them as ordinary commands.
test.use({
    collectionSpec: {
        name: "spreadsheet-team-collection",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
    experimentalFeatures: ["team-collections"],
});

const kExportToSpreadsheet = "CollectionTab.BookMenu.ExportToSpreadsheet";
const kImportFromSpreadsheet =
    "CollectionTab.BookMenu.ImportContentFromSpreadsheet";

type SpreadsheetCommands = Record<
    string,
    Omit<IBookMenuEntry, "label"> | undefined
>;

/** What the selected book's More submenu says of each spreadsheet command. */
async function readSpreadsheetCommands(
    page: Page,
): Promise<SpreadsheetCommands> {
    const { more } = await readSelectedBookMenu(page, "right-click");
    const pick = (testId: string) => {
        const entry = more.find((e) => e.testId === testId);
        if (!entry) return undefined;
        const { label: _label, ...rest } = entry;
        return rest;
    };
    return {
        [kExportToSpreadsheet]: pick(kExportToSpreadsheet),
        [kImportFromSpreadsheet]: pick(kImportFromSpreadsheet),
    };
}

/** Export always offered; Import offered only when `checkedOut`. Neither needs a subscription. */
function expectedCommands(checkedOut: boolean): SpreadsheetCommands {
    return {
        [kExportToSpreadsheet]: {
            testId: kExportToSpreadsheet,
            enabled: true,
            needsSubscription: false,
        },
        [kImportFromSpreadsheet]: {
            testId: kImportFromSpreadsheet,
            enabled: checkedOut,
            needsSubscription: false,
        },
    };
}

/**
 * Check that Bloom agrees whether the selected book is checked out to me, then check what its menu
 * offers. Select the book with clickBookTile first, once: a second click on its tile soon after
 * the first is a double-click, which opens the book in the Edit tab.
 */
async function expectCommandsForSelectedBook(
    page: Page,
    bookName: string,
    checkedOut: boolean,
): Promise<void> {
    expect(
        await isSelectedBookSaveable(page),
        `Sanity check: Bloom should say ${bookName} is ${checkedOut ? "" : "not "}checked out to me`,
    ).toBe(checkedOut);
    expect(
        await readSpreadsheetCommands(page),
        checkedOut
            ? `${bookName} is checked out to me, so both commands should be offered`
            : `${bookName} is checked in, so Export should be offered and Import greyed out`,
    ).toEqual(expectedCommands(checkedOut));
}

test("in a Team Collection, Export to Spreadsheet is offered on every book but Import only on a book checked out to me [Test Case ID 625]", async ({
    page,
    bloomApp,
    step,
}) => {
    const sharedFolder = fs.mkdtempSync(
        Path.join(os.tmpdir(), "bloom-e2e-tc-spreadsheet-"),
    );
    try {
        await step("Make two books", async () => {
            await makeBookFromTemplate(page, "Basic Book");
            await makeBookFromTemplate(page, "Basic Book");
            await switchTab(page, "collection");
        });
        const books = await listEditableBooks(page);
        expect(
            books,
            "Sanity check: the collection holds the two books",
        ).toHaveLength(2);
        const [first, second] = books;

        let tcPage = page;
        await step(
            "Make the collection a Team Collection, which checks both books in",
            async () => {
                tcPage = await createTeamCollection(bloomApp, sharedFolder);
            },
        );

        await step(
            "Both books checked in: Export offered, Import greyed out",
            async () => {
                await clickBookTile(tcPage, first.id);
                await expectCommandsForSelectedBook(
                    tcPage,
                    "the first book",
                    false,
                );
                await clickBookTile(tcPage, second.id);
                await expectCommandsForSelectedBook(
                    tcPage,
                    "the second book",
                    false,
                );
            },
        );

        await step(
            "First book checked out: Import offered on it, still greyed out on the other",
            async () => {
                await clickBookTile(tcPage, first.id);
                await checkOutSelectedBook(tcPage);
                await expectCommandsForSelectedBook(
                    tcPage,
                    "the first book",
                    true,
                );
                await clickBookTile(tcPage, second.id);
                await expectCommandsForSelectedBook(
                    tcPage,
                    "the second book",
                    false,
                );
            },
        );

        await step(
            "First book checked back in: Import greyed out on it again",
            async () => {
                await clickBookTile(tcPage, first.id);
                await checkInSelectedBook(tcPage);
                await expectCommandsForSelectedBook(
                    tcPage,
                    "the first book",
                    false,
                );
            },
        );
    } finally {
        fs.rmSync(sharedFolder, { recursive: true, force: true });
    }
});
