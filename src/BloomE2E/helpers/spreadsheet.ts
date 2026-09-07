// Exporting a book to a spreadsheet, and importing a spreadsheet back into it.
//
// Both commands are on the book's context menu in the Collection tab, and both used to be
// undrivable. Export finished by opening the .xlsx in whatever the machine uses for a
// spreadsheet, which put an Excel window on the developer's screen that no test could close.
// Import opens a native file chooser, which hangs a run.
//
// Bloom answers both under --e2e now. The export records the path it wrote instead of opening the
// file, and reports it at e2e/lastExportedSpreadsheet; the import's chooser takes its answer from
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
 * SETUP, not a UI path, and the one step here that is: the Export dialog's Choose Folder and
 * Export buttons carry no test id, so a test cannot click them. What this does drive is the same
 * `spreadsheet/export` POST the dialog's Export button sends, so everything after the dialog --
 * the exporter, the progress dialog, the images, and the finished file -- is Bloom's own work.
 *
 * Waits for the export to finish. That wait is what the returned path is for: the export runs in
 * the background behind a progress dialog, and Bloom fills e2e/lastExportedSpreadsheet in only
 * when the file is written.
 */
export async function exportBookToSpreadsheet(
    page: Page,
    parentFolder: string,
): Promise<string> {
    fs.mkdirSync(parentFolder, { recursive: true });
    // The Collection tab has to be showing. The export reports its progress into a dialog embedded
    // in that tab's document, and it waits for that dialog to say it is ready to receive messages
    // before it does any work (BrowserProgressDialog). Started from another tab, the export simply
    // never begins, with nothing on screen to say so.
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
 * The import always leaves its progress dialog up for the reader (SpreadsheetImporter), so this
 * closes it, which is also what makes Bloom re-read the book. Waits until the book on disk has
 * been rewritten and the dialog is gone. The caller is left in the Collection tab with the book
 * selected.
 *
 * Returns the book's folder, which is not always the one passed in: a spreadsheet that changes the
 * title renames the folder and the .htm inside it (BringBookUpToDate ends in
 * UpdateBookFileAndFolderName). So the book is followed by its id here rather than by its path,
 * and a caller that goes back to the files afterwards should use what this returns.
 */
export async function importSpreadsheetIntoBook(
    page: Page,
    bookFolder: string,
    xlsxPath: string,
): Promise<string> {
    const collectionFolder = Path.dirname(bookFolder);
    const bookId = readBookInstanceId(bookFolder);
    const before = fs.statSync(bookHtmlPath(bookFolder)).mtimeMs;

    // Arm the chooser before the command opens it; see the note at the top of this file.
    await apiPost(page, "e2e/nextFileToChoose", xlsxPath, "text/plain");

    await switchTab(page, "collection");
    await waitForCollectionReady(page);
    const book = await findBookInCollection(page, bookFolder);
    await page
        .locator(`.book-button[data-book-id="${book.id}"] .bookButton`)
        .click({ button: "right" });

    // Both spreadsheet commands are on the menu's "More" submenu, which has to be opened first.
    // Matching it by its English label is the weak point of this helper: a nested menu item is
    // rendered by a third-party NestedMenuItem that carries no test id, unlike every other item
    // in this menu (see AUTOMATION-DEBT.md, "The Edit tab's page thumbnail menu has no stable
    // test ids").
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

    // Poll for the book's .htm having been rewritten, resolving the folder each time round: a
    // spreadsheet that changes the title makes Bloom rename the folder and the file, and the
    // rename is not atomic from out here, so a moment when neither name resolves is normal. Such
    // a moment reports the mtime we started with, which reads as "nothing yet" and keeps the wait
    // going; reporting anything else (or throwing) would end it, in the wrong direction.
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

    // The import deliberately keeps its progress dialog up until the reader closes it, and Bloom
    // re-reads the book only then (the doWhenProgressCloses callback). So a test that skipped this
    // would go on to look at the book Bloom had before the import, and its click on anything else
    // would land on the dialog's backdrop.
    const close = page.getByTestId("Common.Close");
    await close.waitFor({ state: "visible", timeout: 120000 });
    await close.click();
    await close.waitFor({ state: "hidden", timeout: 30000 });
    return findBookFolderById(collectionFolder, bookId);
}

/** The book's id, from its meta.json: the one thing about it a rename cannot change. */
function readBookInstanceId(bookFolder: string): string {
    const metaPath = Path.join(bookFolder, "meta.json");
    const id = JSON.parse(fs.readFileSync(metaPath, "utf8")).bookInstanceId;
    if (!id) throw new Error(`${metaPath} has no bookInstanceId.`);
    return id;
}

/**
 * The folder the book with this id is in now. Every book folder of a collection holds a meta.json
 * carrying its id, so this finds it wherever an import has renamed it to.
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

/** The book's .htm file. Throws when the folder holds none, naming what it does hold. */
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

/** The book of the editable collection whose folder is `bookFolder`, as Bloom reports it. */
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
