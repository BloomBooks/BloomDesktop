import { expect, type Page } from "@playwright/test";
import type { IProblemDialogWatcher } from "../fixtures/problemDialogWatcher";
import { apiPost } from "./api";
import { clickBookTile } from "./collection";

/**
 * The text of every Bloom notify/problem dialog showing in any page of Bloom's browser, each with
 * its runs of white space made one space.
 */
export async function readProblemDialogs(page: Page): Promise<string[]> {
    const browser = page.context().browser();
    const pages = browser
        ? browser.contexts().flatMap((c) => c.pages())
        : page.context().pages();
    const texts: string[] = [];
    for (const candidate of pages) {
        if (candidate.url().startsWith("devtools://")) continue;
        try {
            const dialogs = candidate.locator(
                ".problem-dialog .dialog-content",
            );
            for (const text of await dialogs.allInnerTexts())
                texts.push(text.replace(/\s+/g, " ").trim());
        } catch (error) {
            // A page that closed while we asked is not a dialog; any other failure is real.
            if (candidate.isClosed()) continue;
            throw error;
        }
    }
    return texts;
}

/**
 * Click a book's tile in the Collections tab when selecting it is expected to raise exactly one
 * Bloom notify dialog, as reading a damaged book does. Waits for that dialog, closes it, waits for
 * the selection to finish, and returns the dialog's text. The problem dialog watcher is paused
 * meanwhile, since the test raises this dialog on purpose.
 */
export async function clickBookTileThroughOneProblemDialog(
    page: Page,
    problemDialogWatcher: IProblemDialogWatcher,
    bookId: string,
): Promise<string> {
    const resumeWatcher = await problemDialogWatcher.pause();
    try {
        // Bloom reads the book when it is selected, and shows its dialog modally while doing so, so
        // the selection is not awaited until the dialog has been closed.
        let selectError: unknown;
        const selected = clickBookTile(page, bookId).catch((error: unknown) => {
            selectError = error;
        });
        let shown: string[] = [];
        await expect
            .poll(
                async () => {
                    if (selectError) throw selectError;
                    shown = await readProblemDialogs(page);
                    return shown.length;
                },
                {
                    timeout: 60000,
                    message: "Bloom never showed a dialog about the book",
                },
            )
            .toBe(1);
        await apiPost(page, "common/closeReactDialog");
        await expect
            .poll(async () => (await readProblemDialogs(page)).length, {
                timeout: 30000,
                message: "the dialog never closed",
            })
            .toBe(0);
        await selected;
        if (selectError) throw selectError;
        return shown[0];
    } finally {
        resumeWatcher();
    }
}
