// Bloom's problem dialog must not get lost behind Bloom's main window. The dialog has no taskbar
// button of its own, and it is modal, so a user who switched to another program and back, and found
// Bloom's main window covering the dialog, would have a Bloom that ignores every click with nothing
// to show why (BL-10292, BL-12412). Bloom prevents that by giving the dialog an owner: Windows keeps
// an owned window above its owner, wherever the owner goes in the stack.
//
// The user's Alt-Tab is played here by moving Bloom's main window to the bottom of the desktop's
// stack and back to the top, without activating it, so the run never takes the keyboard. Windows
// moves the windows a window owns along with it, as it does when Alt-Tab brings Bloom back.

import type { IBloomWindow } from "../helpers/windowStacking";
import { expect, test } from "../fixtures/bloomTest";
import { apiPost } from "../helpers/api";
import {
    findBloomMainWindow,
    isWindowAbove,
    listTopLevelWindows,
    moveWindowInStack,
} from "../helpers/windowStacking";

test.use({
    collectionSpec: { name: "error-dialog-stays-on-top", languages: ["en"] },
});

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
    let dialogClosed: Promise<unknown> | undefined;
    let dialog: IBloomWindow | undefined;
    try {
        // The same call that the Report button of a failed task makes. Bloom answers it only
        // once the dialog has closed.
        dialogClosed = apiPost(
            page,
            "problemReport/showDialog",
            JSON.stringify({
                message: "A problem raised by an end-to-end test.",
                shortMessage: "Test problem",
            }),
            "application/json",
        ).catch((error: unknown) => error);

        await expect
            .poll(
                async () => {
                    dialog = (await listTopLevelWindows(bloomPid)).find(
                        (w) => w.visible && !windowsBefore.has(w.hwnd),
                    );
                    return dialog !== undefined;
                },
                {
                    message: "Bloom showed no problem dialog",
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
            expect(now?.visible, `the dialog is showing ${when}`).toBe(true);
            expect(now?.minimized, `the dialog is not minimized ${when}`).toBe(
                false,
            );
            expect(
                await isWindowAbove(dialogWindow, mainWindow),
                `the dialog lies above Bloom's main window ${when}`,
            ).toBe(true);
        };

        await expectDialogShowingAboveBloom("once it opens");
        await moveWindowInStack(mainWindow, "bottom");
        await expectDialogShowingAboveBloom(
            "after the user switched to another program",
        );
        await moveWindowInStack(mainWindow, "top");
        await expectDialogShowingAboveBloom(
            "after the user came back to Bloom",
        );
    } finally {
        if (dialogClosed) {
            // The same action as the dialog's own Close button; never Submit, which would send
            // the report.
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
        resumeWatcher();
    }
});
