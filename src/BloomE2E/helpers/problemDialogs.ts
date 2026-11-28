import { expect, type Page } from "@playwright/test";
import type { IProblemDialogWatcher } from "../fixtures/problemDialogWatcher";
import * as http from "node:http";
import { clickBookTile } from "./collection";

/** Every page of Bloom's browser but DevTools, which is where a problem dialog may show. */
function bloomPages(page: Page): Page[] {
    const browser = page.context().browser();
    const pages = browser
        ? browser.contexts().flatMap((c) => c.pages())
        : page.context().pages();
    return pages.filter((p) => !p.url().startsWith("devtools://"));
}

/**
 * The text of every Bloom notify/problem dialog showing in any page of Bloom's browser, each with
 * its runs of white space made one space.
 */
export async function readProblemDialogs(page: Page): Promise<string[]> {
    const texts: string[] = [];
    for (const candidate of bloomPages(page)) {
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
 * Wait for Bloom's report dialog (ReportDialog.tsx), the one that gathers a problem report, to show
 * its form, and return the page of Bloom's browser that it shows in: a React dialog
 * (ReactDialog.cs) is a page of its own.
 */
export async function waitForReportDialogPage(page: Page): Promise<Page> {
    let dialogPage: Page | undefined;
    await expect
        .poll(
            async () => {
                for (const candidate of bloomPages(page)) {
                    const count = await candidate
                        .locator(".problem-dialog .what_were_you_doing")
                        .count()
                        // A page that closed or navigated while we asked is not the dialog.
                        .catch(() => 0);
                    if (count > 0) {
                        dialogPage = candidate;
                        return true;
                    }
                }
                return false;
            },
            {
                timeout: 60000,
                message: "Bloom's Report a Problem dialog never showed",
            },
        )
        .toBe(true);
    return dialogPage!;
}

/** One button at the foot of a Bloom problem dialog. */
export interface IProblemDialogButton {
    /** Its data-testid (a BloomButton's l10n key, e.g. "Common.Close"), or "" when it has none. */
    testId: string;
    /** Its id, e.g. "errorReportButton" for a notify dialog's Report button, or "". */
    id: string;
    /** What it says, as shown. */
    text: string;
}

/** What one of Bloom's problem dialogs shows, read from its own page. */
export interface IProblemDialogShown {
    /**
     * "notify" for the small dialog that only tells the user something (NotifyDialog.tsx),
     * "report" for the one that gathers a problem report (ReportDialog.tsx).
     */
    kind: "notify" | "report";
    /** The coloured title bar's text. */
    title: string;
    /**
     * A notify dialog's message, or a report dialog's heading (what went wrong), with runs of white
     * space made one space. "" until the dialog has it.
     */
    message: string;
    /** The buttons at its foot, in document order. */
    buttons: IProblemDialogButton[];
    /**
     * Whether every button at its foot lies inside the dialog's window, so that a long message
     * has not pushed one out of reach.
     */
    buttonsInsideWindow: boolean;
}

interface IFoundProblemDialog {
    page: Page;
    shown: IProblemDialogShown;
}

async function findProblemDialogsShown(
    page: Page,
): Promise<IFoundProblemDialog[]> {
    const found: IFoundProblemDialog[] = [];
    for (const candidate of bloomPages(page)) {
        let shown: IProblemDialogShown[];
        try {
            shown = await candidate.evaluate(() =>
                Array.from(document.querySelectorAll(".problem-dialog")).map(
                    (dialog) => {
                        const tidy = (text: string | null | undefined) =>
                            (text ?? "").replace(/\s+/g, " ").trim();
                        const notifyContent =
                            dialog.querySelector(".dialog-content");
                        const heading = dialog.querySelector(".report-heading");
                        const buttons = Array.from(
                            dialog.querySelectorAll(
                                ".MuiDialogActions-root button",
                            ),
                        ) as HTMLElement[];
                        return {
                            kind: notifyContent
                                ? ("notify" as const)
                                : ("report" as const),
                            title: tidy(
                                dialog.querySelector(".dialog-title")
                                    ?.textContent,
                            ),
                            // innerText, so that a <br> in the message reads as a space.
                            message: tidy(
                                (
                                    (notifyContent ??
                                        heading) as HTMLElement | null
                                )?.innerText,
                            ),
                            buttons: buttons.map((b) => ({
                                testId: b.getAttribute("data-testid") ?? "",
                                id: b.id,
                                text: tidy(b.textContent),
                            })),
                            buttonsInsideWindow: buttons.every((b) => {
                                const box = b.getBoundingClientRect();
                                return (
                                    box.width > 0 &&
                                    box.height > 0 &&
                                    box.top >= 0 &&
                                    box.left >= 0 &&
                                    box.bottom <= window.innerHeight &&
                                    box.right <= window.innerWidth
                                );
                            }),
                        };
                    },
                ),
            );
        } catch (error) {
            // A page that closed or navigated while we asked is not a dialog.
            if (candidate.isClosed()) continue;
            if (/context was destroyed|Target closed/i.test(String(error)))
                continue;
            throw error;
        }
        for (const one of shown) found.push({ page: candidate, shown: one });
    }
    return found;
}

/** What every Bloom problem dialog showing in any page of Bloom's browser shows. */
export async function readProblemDialogsShown(
    page: Page,
): Promise<IProblemDialogShown[]> {
    return (await findProblemDialogsShown(page)).map((f) => f.shown);
}

/**
 * Close the React dialog (ReactDialog.cs) showing now, such as a problem dialog or a Bloom message
 * box, the way its own Close button does, with common/closeReactDialog, which never submits a
 * report. `page` is any of Bloom's pages, for the port. The request goes from Node rather than
 * through a page (helpers/api.ts): every page of Bloom's browser shares one small pool of
 * connections to Bloom's server, and while the modal dialog holds Bloom's UI thread, requests
 * waiting on that thread can fill the pool, so a close sent from a page may never leave the
 * browser. Bloom accepts the request because its Host is localhost.
 */
export function closeReactDialogFromNode(page: Page): Promise<void> {
    const port = Number(new URL(page.url()).port);
    return new Promise((resolve, reject) => {
        const req = http.request(
            {
                host: "localhost",
                port,
                path: "/bloom/api/common/closeReactDialog",
                method: "POST",
                headers: { "Content-Length": 0 },
                timeout: 30000,
            },
            (res) => {
                res.resume();
                res.on("end", () =>
                    res.statusCode && res.statusCode < 400
                        ? resolve()
                        : reject(
                              new Error(
                                  `common/closeReactDialog answered ${res.statusCode}`,
                              ),
                          ),
                );
            },
        );
        req.on("timeout", () =>
            req.destroy(new Error("common/closeReactDialog got no answer")),
        );
        req.on("error", reject);
        req.end();
    });
}

/** Whether a process with this id is still running. */
function isProcessRunning(pid: number): boolean {
    try {
        process.kill(pid, 0);
        return true;
    } catch (error) {
        return (error as NodeJS.ErrnoException).code === "EPERM";
    }
}

/**
 * Do something that is expected to make Bloom show exactly one of its problem dialogs, close that
 * dialog the way its own Close button does, and return what it showed once it had its whole
 * content. Never presses Submit, which sends a report to Bloom's servers. Bloom shows the dialog
 * modally on the thread that raised it, which is often the thread that answers the action's
 * request, so the action is started but not awaited until the dialog has gone. The problem dialog
 * watcher is paused meanwhile, since the test raises this dialog on purpose.
 *
 * The action must not call Bloom's API from a page (helpers/api.ts) once it has raised the dialog,
 * as switchTab's waitForActiveTab does: such a call waits for the dialog and holds one of the
 * connections that the dialog needs to get its message (see closeReactDialogFromNode). Use
 * clickTab, and wait for the tab once the dialog has closed.
 *
 * `bloomQuitsWithPid` is the process id of a Bloom that is expected to quit once the dialog is
 * closed, as it does after a fatal problem; this then waits for that process to end, and the
 * action may fail, since its Bloom is gone.
 */
export async function doThroughOneProblemDialog(
    page: Page,
    problemDialogWatcher: IProblemDialogWatcher,
    action: () => Promise<unknown>,
    bloomQuitsWithPid?: number,
): Promise<IProblemDialogShown> {
    const resumeWatcher = await problemDialogWatcher.pause();
    try {
        let actionError: unknown;
        let actionFailed = false;
        const acted = action().catch((error: unknown) => {
            actionFailed = true;
            actionError = error;
        });
        let found: IFoundProblemDialog[] = [];
        // Polling what the dialogs show, so that a failure says what each one showed.
        await expect
            .poll(
                async () => {
                    if (actionFailed && !bloomQuitsWithPid) throw actionError;
                    found = await findProblemDialogsShown(page);
                    // A dialog is ready once it has its title, its message and its buttons: a
                    // report dialog asks Bloom for its heading after it opens. In a UI language
                    // other than English, the title and labels show in English until their
                    // translations arrive, so a test that checks them pins the UI language.
                    const ready =
                        found.length === 1 &&
                        found[0].shown.title !== "" &&
                        found[0].shown.message !== "" &&
                        found[0].shown.buttons.length > 0 &&
                        found[0].shown.buttons.every((b) => b.text !== "");
                    return ready ? found.length : found.map((f) => f.shown);
                },
                {
                    timeout: 60000,
                    message:
                        "Bloom should show exactly one problem dialog, with its whole content",
                },
            )
            .toBe(1);
        const { page: dialogPage, shown } = found[0];
        if (bloomQuitsWithPid) {
            expect(
                isProcessRunning(bloomQuitsWithPid),
                `Bloom (process ${bloomQuitsWithPid}) is still running while its "${shown.title}" dialog shows`,
            ).toBe(true);
            // Bloom quits as soon as the dialog closes, which may be before it answers: a dropped
            // connection is all that may go wrong with the close. Any other failure (an error
            // status, no Bloom listening) means the close did not happen.
            await closeReactDialogFromNode(page).catch((error: unknown) => {
                const code = (error as NodeJS.ErrnoException).code;
                if (
                    code !== "ECONNRESET" &&
                    !/socket hang up/i.test(String(error))
                )
                    throw error;
            });
            await expect
                .poll(() => isProcessRunning(bloomQuitsWithPid), {
                    timeout: 30000,
                    message: `Bloom (process ${bloomQuitsWithPid}) should quit once its "${shown.title}" dialog was closed`,
                })
                .toBe(false);
            await acted;
            return shown;
        }
        await closeReactDialogFromNode(page);
        await expect
            .poll(
                async () =>
                    dialogPage.isClosed() ||
                    (await dialogPage
                        .locator(".problem-dialog")
                        .count()
                        // A page that closed or navigated no longer shows the dialog.
                        .catch(() => 0)) === 0,
                {
                    timeout: 30000,
                    message: `the dialog that said "${shown.message}" never closed`,
                },
            )
            .toBe(true);
        await acted;
        if (actionFailed)
            throw new Error(
                `The action failed after Bloom's dialog said "${shown.message}": ${String(actionError)}`,
                { cause: actionError },
            );
        return shown;
    } finally {
        resumeWatcher();
    }
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
    const shown = await doThroughOneProblemDialog(
        page,
        problemDialogWatcher,
        () => clickBookTile(page, bookId),
    );
    return shown.message;
}
