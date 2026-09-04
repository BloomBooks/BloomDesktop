// Drive Publish: Web's "Upload this collection" bulk upload, and read what it did.
//
// Bulk upload is the split button beside "Upload Book" on the Publish: Web screen: its dropdown
// offers "Upload this collection" and "Upload folder of collections". Picking one sets it as the
// button's action; then the button runs it (see bloomSplitButton.tsx). The button shows only to a
// signed-in user and is enabled only when the selected book is ready to upload and the agreements
// are ticked, which is the same gate a single upload passes.
//
// The upload itself runs in a second Bloom that Bloom starts (BloomLibraryPublishModel.BulkUpload),
// so the result does not come back through the screen. That second Bloom writes BloomBulkUploadLog.txt
// into the collection folder, and its last lines say how many books were uploaded, updated and
// skipped. A test reads that, which is also how a person reads a bulk upload's result.

import { expect, type Page } from "@playwright/test";
import * as fs from "node:fs";
import * as Path from "node:path";
import { openPublishToWeb } from "./libraryPublish";

/** The name of the log the bulk-upload child process writes into the collection folder. */
const BULK_UPLOAD_LOG = "BloomBulkUploadLog.txt";

/** What one bulk upload did, read from its log's final tally. */
export interface IBulkUploadResult {
    /** Books uploaded for the first time. */
    newBooks: number;
    /** Books that had changed and were re-uploaded. */
    updated: number;
    /** Books skipped because nothing had changed since the last upload. */
    skipped: number;
    /** The whole log, for a failure message when the tally is not what a test expected. */
    log: string;
}

/** The path of the bulk-upload log in a collection folder. */
function logPath(collectionDir: string): string {
    return Path.join(collectionDir, BULK_UPLOAD_LOG);
}

/**
 * Remove the bulk-upload log, so the next upload's result is read fresh rather than from a tally an
 * earlier round left. Call before each upload; each round writes the whole log again.
 */
export function clearBulkUploadLog(collectionDir: string): void {
    fs.rmSync(logPath(collectionDir), { force: true });
}

/**
 * Start a bulk upload of the whole collection through the split button, the way a person does:
 * open the dropdown beside Upload Book, pick "Upload this collection", and click the button. A book
 * must already be selected, the user signed in, and the agreements ticked, or the button is
 * disabled. This does not wait for the upload to finish; see waitForBulkUploadResult.
 */
export async function startCollectionUpload(page: Page): Promise<void> {
    const buttons = page.getByTestId("upload-buttons");
    // Open the dropdown. The arrow button is the split button's second button.
    await buttons.getByRole("button", { name: "select upload source" }).click();
    // The menu is rendered at the document root, not inside upload-buttons. The label is
    // "Upload this Collection"; match case-insensitively so a capitalization tweak does not break it.
    await page
        .getByRole("menuitem", { name: /upload this collection/i })
        .click();
    // Picking only selected it; the primary button runs it.
    const primary = buttons.getByRole("button", {
        name: /upload this collection/i,
    });
    await expect(primary).toBeEnabled({ timeout: 15000 });
    await primary.click();
}

/**
 * Wait until the bulk-upload child process has finished and return its tally. It writes the log as
 * it goes and ends with the three counts, so this polls for the "Skipped ... books" line — the last
 * of the three — then reads all three. Throws with the log when it does not finish in time.
 */
export async function waitForBulkUploadResult(
    collectionDir: string,
    timeoutMs = 180000,
): Promise<IBulkUploadResult> {
    const file = logPath(collectionDir);
    const skippedLine = /Skipped (\d+) books/;
    await expect
        .poll(
            () => (fs.existsSync(file) ? fs.readFileSync(file, "utf8") : ""),
            {
                timeout: timeoutMs,
                message: `The bulk upload never finished: ${file} did not report a final tally.`,
            },
        )
        .toMatch(skippedLine);

    const log = fs.readFileSync(file, "utf8");
    const count = (pattern: RegExp): number => {
        const match = log.match(pattern);
        if (!match)
            throw new Error(
                `The bulk-upload log did not report "${pattern.source}". Log:\n${log}`,
            );
        return Number(match[1]);
    };
    return {
        newBooks: count(/Uploaded (\d+) new books/),
        updated: count(/Updated (\d+) books/),
        skipped: count(/Skipped (\d+) books/),
        log,
    };
}

/**
 * Try to upload the collection with no bookshelf set, and return the message Bloom refuses with.
 * Bloom will not bulk-upload a collection that has no Bloom Library bookshelf; it tells the user to
 * set one. The message goes to the Upload screen's progress box, which is what this reads.
 */
export async function uploadCollectionExpectingBookshelfWarning(
    page: Page,
): Promise<string> {
    await startCollectionUpload(page);
    const progress = page.getByTestId("progress-box-log");
    await expect(progress).toContainText("bookshelf", {
        ignoreCase: true,
        timeout: 30000,
    });
    return (await progress.innerText()).trim();
}

/** Re-export so a test opens the Web screen and drives bulk upload from one import. */
export { openPublishToWeb };
