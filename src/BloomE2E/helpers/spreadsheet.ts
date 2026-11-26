// Export a book to a spreadsheet and import one into a book, with the commands of the More submenu
// of the selected book's menu on the Collections tab. Both run in Bloom's progress box; the folder
// and file choosers they open are answered through Bloom's e2e hook (e2e/nextFileToChoose), since
// a test cannot reach a native chooser.

import { expect, type Locator, type Page } from "@playwright/test";
import * as fs from "node:fs";
import * as Path from "node:path";
import { apiPost, getUiString } from "./api";
import { chooseFromSelectedBookMoreMenuByTestId } from "./collection";
import { realClick } from "./realClick";

/** What one run of a spreadsheet command said in the progress box. */
export interface ISpreadsheetProgress {
    /** Every message, in order. */
    messages: string[];
    /** The messages Bloom marked as errors (ProgressKind.Error or Fatal). */
    errors: string[];
    /** The messages Bloom marked as warnings (ProgressKind.Warning). */
    warnings: string[];
}

/** The progress box's log, where each message is a paragraph. */
function progressLog(page: Page): Locator {
    return page.getByTestId("progress-box-log");
}

/** The progress dialog, which holds the log. */
function progressDialog(page: Page): Locator {
    return page.getByRole("dialog").filter({ has: progressLog(page) });
}

/** What the progress box shows now. */
async function readProgress(page: Page): Promise<ISpreadsheetProgress> {
    const log = progressLog(page);
    const trimmed = (texts: string[]) => texts.map((text) => text.trim());
    return {
        messages: trimmed(await log.locator("p").allInnerTexts()),
        errors: trimmed(
            await log
                .locator(
                    'p[data-progress-severity="error"], p[data-progress-severity="fatal"]',
                )
                .allInnerTexts(),
        ),
        warnings: trimmed(
            await log
                .locator('p[data-progress-severity="warning"]')
                .allInnerTexts(),
        ),
    };
}

/**
 * Export the selected book to a spreadsheet in a folder of its own inside `parentFolder`, as a
 * person does with More > Export to Spreadsheet... and the dialog's Choose Folder and Export
 * buttons, and return the spreadsheet's path once Bloom has written it.
 *
 * Bloom then opens the spreadsheet in whatever program the machine opens .xlsx files with, and
 * brings that program's window to the front; nothing in Bloom holds that back under --e2e (see
 * AUTOMATION-DEBT.md).
 *
 * When the export goes well Bloom closes its progress box by itself; when it reports an error or a
 * warning the box stays open, and this fails with what the box says.
 */
export async function exportSelectedBookToSpreadsheet(
    page: Page,
    parentFolder: string,
): Promise<string> {
    await chooseFromSelectedBookMoreMenuByTestId(
        page,
        "CollectionTab.BookMenu.ExportToSpreadsheet",
    );
    const dialog = page.getByRole("dialog").filter({
        hasText: await getUiString(
            page,
            "Spreadsheet.ExportDialog.folderLabel",
            "Target folder for the spreadsheet and images:",
        ),
    });
    await expect(
        dialog,
        "the Export to Spreadsheet dialog should open",
    ).toBeVisible({
        timeout: 15000,
    });

    await apiPost(
        page,
        "e2e/nextFileToChoose",
        Path.resolve(parentFolder),
        "text/plain",
    );
    // "Choose Folder" (Common.ChooseFolder) has no entry in Bloom's localization files, so it reads
    // the same in every UI language. Asking Bloom to translate it would make Bloom report the
    // missing string as a problem.
    await realClick(
        dialog.getByRole("button", { name: "Choose Folder", exact: true }),
    );
    // The dialog shows the chosen folder once Bloom sends it back over the web socket.
    await expect(
        dialog.getByText(Path.resolve(parentFolder), { exact: true }),
        "the dialog should show the folder chosen for the spreadsheet",
    ).toBeVisible({ timeout: 15000 });

    const bookFolders = new Set(fs.readdirSync(parentFolder));
    await realClick(
        dialog.getByRole("button", {
            name: await getUiString(
                page,
                "Spreadsheet.ExportDialog.ExportButton",
                "Export",
            ),
            exact: true,
        }),
    );
    await expect(
        dialog,
        "the Export to Spreadsheet dialog should close",
    ).toBeHidden();

    // Bloom makes one folder for the book, named after the book's file, and writes the
    // spreadsheet there last, after the pictures.
    let spreadsheet: string | undefined;
    let progress: ISpreadsheetProgress = {
        messages: [],
        errors: [],
        warnings: [],
    };
    await expect
        .poll(
            async () => {
                progress = await readProgress(page);
                if (progress.errors.length + progress.warnings.length > 0)
                    return "problems";
                const made = fs
                    .readdirSync(parentFolder)
                    .filter((name) => !bookFolders.has(name));
                spreadsheet = made
                    .map((name) =>
                        Path.join(parentFolder, name, `${name}.xlsx`),
                    )
                    .find((path) => fs.existsSync(path));
                return !!spreadsheet &&
                    (await progressDialog(page).count()) === 0
                    ? "done"
                    : "working";
            },
            {
                timeout: 120000,
                message:
                    "Bloom never finished writing the spreadsheet and closing its progress box",
            },
        )
        .not.toBe("working");
    if (progress.errors.length + progress.warnings.length > 0)
        throw new Error(
            `The export reported problems:\n${progress.messages.join("\n")}`,
        );
    return spreadsheet!;
}

/**
 * Import the spreadsheet at `spreadsheetPath` into the selected book, as a person does with
 * More > Import Content from Spreadsheet..., choosing the file in the chooser. Bloom leaves its
 * progress box open when the import is over, whatever happened; this waits until the box says
 * the import is done, closes it with its Close button, and returns what it said.
 */
export async function importSpreadsheetIntoSelectedBook(
    page: Page,
    spreadsheetPath: string,
): Promise<ISpreadsheetProgress> {
    if (!fs.existsSync(spreadsheetPath))
        throw new Error(`There is no spreadsheet at ${spreadsheetPath}.`);
    await apiPost(
        page,
        "e2e/nextFileToChoose",
        Path.resolve(spreadsheetPath),
        "text/plain",
    );
    await chooseFromSelectedBookMoreMenuByTestId(
        page,
        "CollectionTab.BookMenu.ImportContentFromSpreadsheet",
    );

    // The importer's last message is "Done" (SpreadsheetImporter.ImportAsync), after any
    // warnings; an import that stops early ends with an error instead.
    await expect
        .poll(
            async () => {
                const progress = await readProgress(page);
                return (
                    progress.errors.length > 0 ||
                    progress.messages.at(-1) === "Done"
                );
            },
            {
                timeout: 180000,
                message: "The spreadsheet import never said it was done",
            },
        )
        .toBe(true);
    const progress = await readProgress(page);

    const dialog = progressDialog(page);
    await realClick(
        dialog.getByRole("button", {
            name: await getUiString(page, "Common.Close", "Close"),
            exact: true,
        }),
    );
    await expect(dialog, "the progress box should close").toBeHidden();
    return progress;
}
