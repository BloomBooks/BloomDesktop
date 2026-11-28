// The "Report a Problem" dialog as a person opens it, from the Help menu in the top bar.
//
// It is the same dialog that Bloom raises for an error (ProblemDialog.tsx), hosted in a WinForms
// window of its own with its own WebView2, so it is a separate CDP page and not part of the shell
// document. The fixture's problemDialogWatcher closes every such dialog and fails the test with
// it, so the watcher is paused while the one here is open.
//
// Nothing here presses Submit, and nothing may: that sends a report, a screenshot and the book to
// Bloom's servers.

import { expect, type Page } from "@playwright/test";
import type { IProblemDialogWatcher } from "../fixtures/problemDialogWatcher";
import { apiPost, getUiString } from "./api";

const kDialogRoot = ".problem-dialog";

/** The page showing a problem dialog, among every page of the Bloom that `page` belongs to. */
async function findProblemDialogPage(page: Page): Promise<Page | undefined> {
    const browser = page.context().browser();
    if (!browser)
        throw new Error(
            "The Bloom page has no browser connection, so its dialogs cannot be found.",
        );
    for (const candidate of browser.contexts().flatMap((c) => c.pages())) {
        if (candidate.url().startsWith("devtools://")) continue;
        const count = await candidate
            .locator(kDialogRoot)
            .count()
            // A page can close or navigate while it is asked; that page is not the dialog.
            .catch(() => 0);
        if (count > 0) return candidate;
    }
    return undefined;
}

/**
 * Choose Help > Report a Problem in the top bar, as a person does, and run `body` with the page of
 * the dialog that opens. The dialog is closed afterwards without submitting anything, whether or
 * not `body` succeeded. `watcher` is the fixture's problemDialogWatcher, which would otherwise
 * close the dialog and fail the test for it. Returns what `body` returns.
 */
export async function withReportAProblemDialog<T>(
    page: Page,
    watcher: IProblemDialogWatcher,
    body: (dialog: Page) => Promise<T>,
): Promise<T> {
    const menuItemText = await getUiString(
        page,
        "HelpMenu.ReportAProblemToolStripMenuItem",
        "Report a Problem...",
    );
    const resumeWatcher = await watcher.pause();
    try {
        await page.locator("#helpMenuButton").click();
        // The Help menu's entries carry no test id, so the entry is found by what it says.
        await page
            .getByRole("menuitem", { name: menuItemText, exact: true })
            .click();
        let dialog: Page | undefined;
        await expect
            .poll(
                async () => {
                    dialog = await findProblemDialogPage(page);
                    return dialog !== undefined;
                },
                {
                    timeout: 30000,
                    message:
                        "Help > Report a Problem never opened the Report a Problem dialog.",
                },
            )
            .toBe(true);
        try {
            return await body(dialog!);
        } finally {
            await closeProblemDialog(page);
        }
    } finally {
        resumeWatcher();
    }
}

/**
 * Close the problem dialog the way its own Cancel and Close buttons do, which submits nothing,
 * and return once it is gone.
 */
async function closeProblemDialog(page: Page): Promise<void> {
    await apiPost(page, "common/closeReactDialog");
    await expect
        .poll(async () => (await findProblemDialogPage(page)) === undefined, {
            timeout: 30000,
            message: "The Report a Problem dialog never closed.",
        })
        .toBe(true);
}

/**
 * Check that the dialog's Email box holds `email`. Polled, because the dialog asks Bloom for the
 * reporter's address after it has drawn itself.
 */
export async function expectReporterEmail(
    dialog: Page,
    email: string,
): Promise<void> {
    await expect(
        dialog.locator(`${kDialogRoot} .email input`),
        "the Report a Problem dialog's Email box",
    ).toHaveValue(email);
}

/**
 * Press the dialog's Learn More, which shows the diagnostic information that Bloom would send
 * with the report, and return that text once Bloom has made it.
 */
export async function showReportDiagnosticInfo(dialog: Page): Promise<string> {
    const learnMoreText = await getUiString(
        dialog,
        "Common.LearnMore",
        "Learn More",
    );
    // The screen shows a "Loading..." message in the report's place until Bloom has answered, so
    // what the screen shows is compared with Bloom's answer and not with that message.
    // Awaited together, so that a click that fails leaves no wait for the answer behind it.
    const [answer] = await Promise.all([
        dialog.waitForResponse(
            (response) =>
                response.url().includes("problemReport/diagnosticInfo"),
            { timeout: 60000 },
        ),
        dialog
            .locator(kDialogRoot)
            .getByRole("button", { name: learnMoreText, exact: true })
            .click(),
    ]);
    const diagnosticInfo = await answer.text();
    expect(
        diagnosticInfo,
        "Bloom's diagnostic information for the report",
    ).not.toBe("");
    const report = dialog.locator(`${kDialogRoot} #report`);
    await expect
        .poll(() => report.textContent(), {
            message:
                "The screen behind Learn More never showed the diagnostic information.",
        })
        .toBe(diagnosticInfo);
    return diagnosticInfo;
}
