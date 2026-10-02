// Drive Publish: Web's "Upload this collection" bulk upload, and read what it did.
//
// Bulk upload is the split button beside "Upload Book" on the Publish: Web screen: its dropdown
// offers "Upload this collection" and "Upload folder of collections". Picking one sets it as the
// button's action; then the button runs it (see bloomSplitButton.tsx). The button shows only to a
// signed-in user and is enabled only when the selected book is ready to upload and the agreements
// are ticked, which is the same gate a single upload passes.
//
// The upload itself runs in a second Bloom that Bloom starts (BloomLibraryPublishModel.BulkUpload),
// so the result does not come back through the screen. That second Bloom writes two files into the
// collection folder: BloomBulkUploadLog.txt, the log a person reads, and, when it has finished,
// BloomBulkUploadResults.json (BulkUploader.ResultsFileName), the same outcome in a form a program
// can read: the tallies, and for each book its outcome and where its files went. A test reads the
// results file, and keeps the log only to explain a failure.

import { expect, type Page } from "@playwright/test";
import * as fs from "node:fs";
import * as Path from "node:path";
import { acceptAllAgreements, openPublishToWeb } from "./libraryPublish";

/** The name of the log the bulk-upload child process writes into the collection folder. */
const BULK_UPLOAD_LOG = "BloomBulkUploadLog.txt";

/** The results file it writes beside the log once it has finished (BulkUploader.ResultsFileName). */
const BULK_UPLOAD_RESULTS = "BloomBulkUploadResults.json";

/** What happened to one book in a bulk upload, as the results file records it. */
export interface IBulkUploadBookResult {
    /** The book's folder, as the uploading Bloom saw it. */
    folder: string;
    outcome: "new" | "updated" | "skipped" | "failed";
    /**
     * Where the book's files were uploaded, a baseUrl in the form a book's record holds; null
     * unless the book was uploaded. Read uploaded files from here rather than from the book's record
     * on the server, which can point at an older upload (BL-16921).
     */
    baseUrl: string | null;
}

/** What one bulk upload did, read from its results file. */
export interface IBulkUploadResult {
    /** Books uploaded for the first time. */
    newBooks: number;
    /** Books that had changed and were re-uploaded. */
    updated: number;
    /** Books skipped because nothing had changed since the last upload. */
    skipped: number;
    /** Books that could not be uploaded. */
    failed: number;
    /** Each book the upload looked at. */
    books: IBulkUploadBookResult[];
    /** The whole log, for a failure message when the result is not what a test expected. */
    log: string;
}

/** The path of the bulk-upload log in a collection folder. */
function logPath(collectionDir: string): string {
    return Path.join(collectionDir, BULK_UPLOAD_LOG);
}

/** The path of the bulk-upload results file in a collection folder. */
function resultsPath(collectionDir: string): string {
    return Path.join(collectionDir, BULK_UPLOAD_RESULTS);
}

/**
 * Remove the bulk-upload log and results file, so the next upload's result is read fresh rather
 * than from what an earlier round left. Call before each upload; each round writes both again.
 */
export function clearBulkUploadLog(collectionDir: string): void {
    fs.rmSync(logPath(collectionDir), { force: true });
    fs.rmSync(resultsPath(collectionDir), { force: true });
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
 * How long to give one bulk upload.
 *
 * The child Bloom makes a thumbnail, a PDF preview, a PDF from the HTML and a Ghostscript
 * compression pass for every book it uploads, and then sends each one to dev.bloomlibrary.org, so a
 * round is real work on a runner that is slower than a developer machine and often busy with the
 * rest of the suite. Rounds land between a few seconds and a couple of minutes.
 *
 * This is about three times the slowest round measured. Its real job is to bound how long a STUCK
 * upload burns runner time before failing, so keep it near that rather than padding it for
 * headroom: a round that approaches this is far more likely to be hung than slow. Read the
 * `[bulk upload] finished in Ns` line from a recent green run before changing the number.
 */
const kBulkUploadTimeoutMs = 300000;

/**
 * Wait until the bulk-upload child process has finished and return what it did. It writes its
 * results file only once it has finished, so this polls for that file (and for it to parse, since
 * a read can catch it half-written).
 *
 * When it does not finish in time, the failure says how far the upload actually got. That is the
 * thing worth knowing and it used to be missing: "stuck inside Compressing PDF on book 1" and
 * "never wrote a line at all" are different problems with different fixes, and telling them apart
 * meant downloading the run's artifact and unzipping it.
 */
export async function waitForBulkUploadResult(
    collectionDir: string,
    timeoutMs = kBulkUploadTimeoutMs,
): Promise<IBulkUploadResult> {
    const file = logPath(collectionDir);
    const readLog = () =>
        fs.existsSync(file) ? fs.readFileSync(file, "utf8") : "";
    let results: Omit<IBulkUploadResult, "log"> | undefined;
    const readResults = (): boolean => {
        const resultsFile = resultsPath(collectionDir);
        if (!fs.existsSync(resultsFile)) return false;
        try {
            results = JSON.parse(fs.readFileSync(resultsFile, "utf8"));
            return true;
        } catch {
            return false; // caught while it was being written; the next poll reads it whole
        }
    };
    const startedAt = Date.now();
    try {
        await expect.poll(readResults, { timeout: timeoutMs }).toBe(true);
        // Report how long a round that worked actually took. Nobody knows yet what a bulk upload
        // costs on the runner — the first measurement anyone had was of a round that never
        // finished — and the timeout above cannot be right-sized until a few real numbers come
        // back from the nightly.
        console.error(
            `[bulk upload] finished in ${Math.round((Date.now() - startedAt) / 1000)}s`,
        );
    } catch {
        const lines = readLog()
            .split(/\r?\n/)
            .map((line) => line.trimEnd())
            .filter((line) => line.length > 0);
        const howFar = lines.length
            ? lines.slice(-8).join("\n    ")
            : "(the child Bloom never wrote a line — it may not have started at all)";
        throw new Error(
            `The bulk upload did not finish within ${Math.round(timeoutMs / 1000)}s: ` +
                `it never wrote ${BULK_UPLOAD_RESULTS}.\n` +
                `  How far it got, from the end of that log:\n    ${howFar}`,
        );
    }

    return { ...results!, log: readLog() };
}

/**
 * Upload the whole collection and wait for its result, the way a person does from a selected book:
 * go to Publish: Web, tick the agreements, run "Upload this collection", and read the tally the
 * child Bloom wrote. A book must be selected and the user signed in. Coming back to Publish: Web
 * and ticking already-ticked agreements is harmless, so a test calls this for every upload round,
 * wherever it left Bloom in between.
 */
export async function uploadCollection(
    page: Page,
    collectionDir: string,
): Promise<IBulkUploadResult> {
    await openPublishToWeb(page);
    await acceptAllAgreements(page);
    clearBulkUploadLog(collectionDir);
    await startCollectionUpload(page);
    return waitForBulkUploadResult(collectionDir);
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
