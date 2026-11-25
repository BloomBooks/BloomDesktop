// Bloom's problem dialog must not get lost behind Bloom's main window. The dialog has no taskbar
// button of its own, and it is modal, so a user who switched to another program and back, and found
// Bloom's main window covering the dialog, would have a Bloom that ignores every click with nothing
// to show why (BL-10292, BL-12412). Bloom prevents that by giving the dialog an owner: Windows keeps
// an owned window above its owner, wherever the owner goes in the stack.
//
// The user's Alt-Tab is played here by moving Bloom's main window to the bottom of the desktop's
// stack and then back up past the nearest window of another program, without activating it. The
// run never takes the keyboard, and Bloom ends up above only the one window that lay just above it
// at the bottom of the stack. Windows moves the windows a window owns along with it, as it does
// when Alt-Tab brings Bloom back.

import type { Page } from "@playwright/test";
import type { IBloomWindow } from "../helpers/windowStacking";
import { expect, test } from "../fixtures/bloomTest";
import { apiPost } from "../helpers/api";
import {
    findBloomMainWindow,
    findNearestOtherWindowAbove,
    isWindowAbove,
    listTopLevelWindows,
    moveWindowJustAbove,
    moveWindowToBottom,
} from "../helpers/windowStacking";

test.use({
    collectionSpec: { name: "error-dialog-stays-on-top", languages: ["en"] },
});

// The heading the test gives its problem, which the dialog shows at its top.
const kHeading = "Test problem raised by TC-53";

/** Whether some page of Bloom's browser shows the problem dialog with the test's heading. */
async function isProblemDialogShowing(page: Page): Promise<boolean> {
    const browser = page.context().browser();
    const pages = browser
        ? browser.contexts().flatMap((c) => c.pages())
        : page.context().pages();
    for (const candidate of pages) {
        if (candidate.url().startsWith("devtools://")) continue;
        const headings = candidate
            .locator(".problem-dialog .report-heading")
            .filter({ hasText: kHeading });
        try {
            if (
                (await headings.count()) > 0 &&
                (await headings.first().isVisible())
            )
                return true;
        } catch (error) {
            // A page that closed while we asked is not the dialog; any other failure is real.
            if (candidate.isClosed()) continue;
            throw error;
        }
    }
    return false;
}

test("the problem dialog stays above Bloom's window when the user switches away and back [Test Case ID 53]", async ({
    page,
    bloomApp,
    problemDialogWatcher,
}) => {
    const bloomPid = bloomApp.bloomPid;
    const mainWindow = await findBloomMainWindow(bloomPid);
    const windowsBefore = new Set(
        (await listTopLevelWindows(bloomPid)).map((w) => w.hwnd),
    );

    // This test raises the problem dialog on purpose and closes it itself, so the watcher that
    // turns a problem dialog into a test failure must leave this one alone.
    const resumeWatcher = await problemDialogWatcher.pause();
    try {
        let dialogClosed: Promise<unknown> | undefined;
        let requestError: unknown;
        let dialog: IBloomWindow | undefined;
        try {
            // The same call that the Report button of a failed task makes. Bloom answers it only
            // once the dialog has closed.
            dialogClosed = apiPost(
                page,
                "problemReport/showDialog",
                JSON.stringify({
                    message: "A problem raised by an end-to-end test.",
                    shortMessage: kHeading,
                }),
                "application/json",
            ).catch((error: unknown) => {
                requestError = error;
            });

            await expect
                .poll(
                    async () => {
                        // A refused request will never open the dialog; say why at once.
                        if (requestError) throw requestError;
                        dialog = (await listTopLevelWindows(bloomPid)).find(
                            (w) => w.visible && !windowsBefore.has(w.hwnd),
                        );
                        return (
                            dialog !== undefined &&
                            (await isProblemDialogShowing(page))
                        );
                    },
                    {
                        message:
                            "Bloom showed its problem dialog, with the test's heading, in a window of its own",
                        timeout: 30000,
                    },
                )
                .toBe(true);
            const dialogWindow = dialog!.hwnd;
            expect(
                dialog!.owner,
                "the problem dialog's owner is Bloom's main window",
            ).toBe(mainWindow);

            const expectDialogShowingAboveBloom = async (when: string) => {
                const now = (await listTopLevelWindows(bloomPid)).find(
                    (w) => w.hwnd === dialogWindow,
                );
                expect(now?.visible, `the dialog is showing ${when}`).toBe(
                    true,
                );
                expect(
                    now?.minimized,
                    `the dialog is not minimized ${when}`,
                ).toBe(false);
                expect(
                    await isWindowAbove(dialogWindow, mainWindow),
                    `the dialog lies above Bloom's main window ${when}`,
                ).toBe(true);
            };

            await expectDialogShowingAboveBloom("once it opens");
            await moveWindowToBottom(mainWindow);
            await expectDialogShowingAboveBloom(
                "after the user switched to another program",
            );
            // Bring Bloom back up past the nearest window of another program that now lies
            // above it: one window at the bottom of the desktop's stack, not the window the
            // developer is working in.
            const coveringWindow = await findNearestOtherWindowAbove(
                dialogWindow,
                bloomPid,
            );
            expect(
                coveringWindow,
                "some window of another program lies above Bloom once Bloom is at the bottom",
            ).not.toBe(0);
            await moveWindowJustAbove(mainWindow, coveringWindow);
            expect(
                await isWindowAbove(mainWindow, coveringWindow),
                "Bloom's main window came back up past the window that covered it",
            ).toBe(true);
            await expectDialogShowingAboveBloom(
                "after the user came back to Bloom",
            );
            expect(
                await isProblemDialogShowing(page),
                "the dialog still shows its content after the user came back to Bloom",
            ).toBe(true);
        } finally {
            if (dialogClosed && !requestError) {
                // The same action as the dialog's own Close button; never Submit, which would
                // send the report.
                await apiPost(page, "common/closeReactDialog");
                await dialogClosed;
                if (dialog) {
                    const closedWindow = dialog.hwnd;
                    await expect
                        .poll(
                            async () =>
                                (await listTopLevelWindows(bloomPid)).some(
                                    (w) => w.hwnd === closedWindow && w.visible,
                                ),
                            {
                                message: "the problem dialog closed",
                                timeout: 30000,
                            },
                        )
                        .toBe(false);
                }
            }
        }
    } finally {
        // Whatever happened above, so the fixture's final scan still sees any dialog left open.
        resumeWatcher();
    }
});
