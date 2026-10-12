// Exporting a book to a spreadsheet, and importing a spreadsheet back into it.
//
// Both commands are on the book's context menu in the Collection tab. Normally the export ends by
// opening the .xlsx in the machine's spreadsheet program, which would put an Excel window on the
// developer's screen that no test could close, and the import opens a native file chooser, which
// would hang a test run.
//
// Under --e2e Bloom avoids both. The export records the path it wrote instead of opening the file,
// and reports it at e2e/lastExportedSpreadsheet. The import's file chooser takes its answer from
// e2e/nextFileToChoose. See AUTOMATION-DEBT.md, "Exporting a spreadsheet launches Excel".
//
// Both features need a subscription tier of LocalCommunity or better (FeatureRegistry.cs), so a
// test launches its collection with kEnterpriseSubscriptionCode.

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, type Page } from "@playwright/test";
import { apiGet, apiGetJson, apiPost } from "./api";
import { waitForCollectionReady } from "./collection";
import { switchTab } from "./workspace";

/** One book as collections/books reports it. Only the fields used here. */
interface IBookInCollection {
    id: string;
    folderPath: string;
}

/**
 * Export the selected book to a spreadsheet under `parentFolder`, and return the path of the
 * .xlsx file. Creates `parentFolder` if it is not there.
 *
 * This is the one step in this file that does not go through the UI, so treat it as setup. The
 * Export dialog's Choose Folder and Export buttons have no test id, so a test cannot click them.
 * Instead this sends the same `spreadsheet/export` POST that the dialog's Export button sends, so
 * everything after the dialog (the exporter, the progress dialog, the images and the finished
 * file) is done by Bloom's own code.
 *
 * Waits for the export to finish. The export runs in the background behind a progress dialog, and
 * Bloom sets e2e/lastExportedSpreadsheet only once the file is written, so polling for the path
 * is how this knows the export is done.
 */
export async function exportBookToSpreadsheet(
    page: Page,
    parentFolder: string,
): Promise<string> {
    fs.mkdirSync(parentFolder, { recursive: true });
    // The Collection tab has to be showing. The export reports its progress into a dialog embedded
    // in that tab's document, and it does no work until that dialog says it is ready to receive
    // messages (BrowserProgressDialog). If started from another tab, the export never begins, and
    // nothing on screen says so.
    await switchTab(page, "collection");
    await waitForCollectionReady(page);
    await apiPost(
        page,
        "spreadsheet/export",
        JSON.stringify({ parentFolderPath: parentFolder }),
        "application/json",
    );
    await expect
        .poll(
            async () =>
                (await apiGet(page, "e2e/lastExportedSpreadsheet")).body,
            {
                timeout: 120000,
                message:
                    "The export never reported a finished spreadsheet. An export that fails " +
                    "reports nothing at all, so look for a problem message in Bloom's progress " +
                    `dialog. The target folder was ${parentFolder}.`,
            },
        )
        .not.toBe("");
    const path = (await apiGet(page, "e2e/lastExportedSpreadsheet")).body;
    if (!fs.existsSync(path))
        throw new Error(
            `Bloom says it exported to ${path}, but there is no file there.`,
        );
    return path;
}

/**
 * Import a spreadsheet into the book in `bookFolder`, the way a person does: right-click the book
 * in the Collection tab, and choose "Import Content from Spreadsheet...". The native file chooser
 * that command opens is pre-answered with `xlsxPath`, so no dialog appears.
 *
 * The import always leaves its progress dialog open for the person to read (SpreadsheetImporter).
 * This closes it, and closing it is also what makes Bloom re-read the book. Waits until the book
 * on disk has been rewritten and the dialog is gone. Leaves the Collection tab showing, with the
 * book selected.
 *
 * Returns the book's folder, which may differ from the one passed in. A spreadsheet that changes
 * the title makes Bloom rename the folder and the .htm inside it (BringBookUpToDate ends by calling
 * UpdateBookFileAndFolderName). So this tracks the book by its id instead of its path, and a
 * caller that looks at the files afterwards should use the folder this returns.
 */
export async function importSpreadsheetIntoBook(
    page: Page,
    bookFolder: string,
    xlsxPath: string,
): Promise<string> {
    const collectionFolder = Path.dirname(bookFolder);
    const bookId = readBookInstanceId(bookFolder);
    const before = fs.statSync(bookHtmlPath(bookFolder)).mtimeMs;

    // Tell Bloom which file to choose before the command opens the chooser; see the note at the
    // top of this file.
    await apiPost(page, "e2e/nextFileToChoose", xlsxPath, "text/plain");

    await switchTab(page, "collection");
    await waitForCollectionReady(page);
    const book = await findBookInCollection(page, bookFolder);
    await page
        .locator(`.book-button[data-book-id="${book.id}"] .bookButton`)
        .click({ button: "right" });

    // Both spreadsheet commands are on the menu's "More" submenu, which has to be opened first.
    // This finds "More" by its English label, so it breaks if that label changes. The submenu is
    // rendered by a third-party NestedMenuItem, which has no test id, unlike every other item in
    // this menu (see AUTOMATION-DEBT.md, "The Edit tab's page thumbnail menu has no stable test
    // ids").
    const more = page
        .locator('[role="menu"] li')
        .filter({ hasText: /^More$/ })
        .first();
    await more.waitFor({ state: "visible", timeout: 30000 });
    await more.click();

    const command = page.locator(
        '[data-testid="CollectionTab.BookMenu.ImportContentFromSpreadsheet"]',
    );
    await command.waitFor({ state: "visible", timeout: 30000 });
    await command.click();

    // Poll until the book's .htm has been rewritten, finding the folder again on each poll. A
    // spreadsheet that changes the title makes Bloom rename the folder and the file, and from
    // outside Bloom the rename is not atomic, so there can be a moment when neither name exists.
    // At such a moment this returns the starting mtime, which the poll treats as "not rewritten
    // yet", so it keeps waiting. Returning anything else, or throwing, would end the wait early.
    const currentHtmlMtime = (): number => {
        try {
            return fs.statSync(
                bookHtmlPath(findBookFolderById(collectionFolder, bookId)),
            ).mtimeMs;
        } catch {
            return before;
        }
    };
    await expect
        .poll(currentHtmlMtime, {
            timeout: 120000,
            message:
                `The import never rewrote the .htm of the book ${bookId} in ` +
                `${collectionFolder}. An import that fails leaves the book alone and says so in ` +
                "Bloom's progress dialog.",
        })
        .not.toBe(before);

    // The import keeps its progress dialog open until the person closes it, and Bloom re-reads the
    // book only then (the doWhenProgressCloses callback). If a test skipped this, it would see the
    // book as it was before the import, and its next click would land on the dialog's backdrop.
    const close = page.getByTestId("Common.Close");
    await close.waitFor({ state: "visible", timeout: 120000 });
    await close.click();
    await close.waitFor({ state: "hidden", timeout: 30000 });
    return findBookFolderById(collectionFolder, bookId);
}

/** Read the book's id from its meta.json. Renaming the book does not change its id. */
function readBookInstanceId(bookFolder: string): string {
    const metaPath = Path.join(bookFolder, "meta.json");
    const id = JSON.parse(fs.readFileSync(metaPath, "utf8")).bookInstanceId;
    if (!id) throw new Error(`${metaPath} has no bookInstanceId.`);
    return id;
}

/**
 * Find the folder that currently holds the book with this id. Every book folder in a collection has
 * a meta.json with the book's id, so this finds the book even after an import has renamed its
 * folder.
 */
function findBookFolderById(collectionFolder: string, bookId: string): string {
    const folders = fs
        .readdirSync(collectionFolder, { withFileTypes: true })
        .filter((entry) => entry.isDirectory())
        .map((entry) => Path.join(collectionFolder, entry.name))
        .filter((folder) => fs.existsSync(Path.join(folder, "meta.json")));
    const found = folders.filter(
        (folder) => readBookInstanceId(folder) === bookId,
    );
    if (found.length !== 1)
        throw new Error(
            `Expected one book with id ${bookId} in ${collectionFolder}, found ` +
                `${found.length}: ${found.join(", ") || "(none)"}.`,
        );
    return found[0];
}

/** The path of the book's .htm file. Throws, listing what it found, unless there is exactly one. */
function bookHtmlPath(bookFolder: string): string {
    const names = fs
        .readdirSync(bookFolder)
        .filter((name) => name.endsWith(".htm"));
    if (names.length !== 1)
        throw new Error(
            `Expected one .htm in ${bookFolder}, found ${names.length}: ` +
                `${names.join(", ") || "(none)"}.`,
        );
    return Path.join(bookFolder, names[0]);
}

/** Ask Bloom for the book in the editable collection whose folder is `bookFolder`. */
async function findBookInCollection(
    page: Page,
    bookFolder: string,
): Promise<IBookInCollection> {
    const collectionId = Path.dirname(bookFolder);
    const books = await apiGetJson<IBookInCollection[]>(
        page,
        `collections/books?collection-id=${encodeURIComponent(collectionId)}`,
    );
    const found = books.find(
        (one) => Path.resolve(one.folderPath) === Path.resolve(bookFolder),
    );
    if (!found)
        throw new Error(
            `The collection has no book in ${bookFolder}. It holds: ` +
                `${books.map((one) => one.folderPath).join(", ") || "(no books)"}.`,
        );
    return found;
}
