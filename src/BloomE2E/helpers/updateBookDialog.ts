// Notice whether Bloom brought a book's pages up to date, by watching for its progress dialog.
//
// Bloom runs that update (BookProcessor's page pass) only when the book needs it, and shows a small
// dialog with a progress bar while it does. On a short book the dialog can come and go in a second
// or two, so looking for it after the fact misses it. Instead a test starts watching BEFORE the
// action that might run the update, and asks afterwards whether the dialog ever appeared.
//
// The dialog is the only one in Bloom's shell document that holds a progress bar, which is how it
// is recognized here; its sentence is localized, so it is not a usable handle.

import { expect, type Locator, type Page } from "@playwright/test";
import { apiPost } from "./api";
import { showPublishDestination, type PublishDestination } from "./publish";

const kSawDialogFlag = "__bloomE2eSawUpdateBookDialog";

/** The update's progress dialog: the dialog in the shell document that holds a progress bar. */
function updateBookDialog(page: Page): Locator {
    return page
        .getByRole("dialog")
        .filter({ has: page.getByRole("progressbar") });
}

/** Something that can say whether the update dialog appeared since watching began. */
export interface IUpdateBookDialogWatch {
    /** True if Bloom showed its "bringing the pages up to date" progress dialog since the watch began. */
    appeared(): Promise<boolean>;
}

/**
 * Start watching Bloom's shell document for the progress dialog Bloom shows while it brings a
 * book's pages up to date. Call it just before the action that may run the update.
 */
export async function watchForUpdateBookDialog(
    page: Page,
): Promise<IUpdateBookDialogWatch> {
    await page.evaluate((flag) => {
        const w = window as unknown as Record<string, unknown>;
        const selector = '[role="dialog"] [role="progressbar"]';
        (w[`${flag}Observer`] as MutationObserver | undefined)?.disconnect();
        w[flag] = !!document.querySelector(selector);
        const observer = new MutationObserver(() => {
            if (document.querySelector(selector)) w[flag] = true;
        });
        observer.observe(document.body, { childList: true, subtree: true });
        w[`${flag}Observer`] = observer;
    }, kSawDialogFlag);
    return {
        appeared: () =>
            page.evaluate(
                (flag) =>
                    (window as unknown as Record<string, unknown>)[flag] ===
                    true,
                kSawDialogFlag,
            ),
    };
}

/**
 * Choose a Publish tool, wait until Bloom shows it, and say whether Bloom brought the book's pages
 * up to date on the way (it does that first, when the book needs it).
 */
export async function choosePublishToolAndSeeIfItUpdated(
    page: Page,
    destination: PublishDestination,
): Promise<boolean> {
    const watch = await watchForUpdateBookDialog(page);
    await showPublishDestination(page, destination);
    return watch.appeared();
}

/**
 * Make the next update of a book's pages fail when it reaches page `pageNumber` (1-based), the way
 * a page whose content could not be captured fails. Uses the e2e/failPageUpdateAt hook; the
 * setting is used up by that one update.
 */
export async function failNextPageUpdateAt(
    page: Page,
    pageNumber: number,
): Promise<void> {
    await apiPost(
        page,
        "e2e/failPageUpdateAt",
        String(pageNumber),
        "text/plain",
    );
}

/**
 * Wait until the update's progress dialog has finished with a problem, which it shows by offering
 * its Report button, and return the dialog's text, which includes the problem.
 */
export async function waitForUpdateBookDialogToReportAProblem(
    page: Page,
): Promise<string> {
    const dialog = updateBookDialog(page);
    await expect(
        dialog.locator("#progress-report"),
        "The update's progress dialog never reported a problem.",
    ).toBeVisible({ timeout: 120000 });
    return dialog.innerText();
}

/** Click the update dialog's Close button and wait for the dialog to go away. */
export async function closeUpdateBookDialog(page: Page): Promise<void> {
    const dialog = updateBookDialog(page);
    // The Close button has no id; it is the dialog's last button, after Report.
    await dialog.locator("button:not(#progress-report)").last().click();
    await expect(
        dialog,
        "The update's progress dialog did not close.",
    ).toHaveCount(0, { timeout: 30000 });
}
