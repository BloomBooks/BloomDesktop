// Drive the Copyright and License dialog.
//
// The dialog is reached from more than one place (the Edit tab's copyright button, and the
// "Click to fix" link on the Missing Copyright warning in Publish: Web), and it is a React
// dialog rendered into whichever page opened it, so a test drives it on the page it is already
// holding.

import { expect, type Locator, type Page } from "@playwright/test";

// There is deliberately no direct-API "set the copyright" helper. The obvious endpoint,
// copyrightAndLicense/bookCopyrightAndLicense, is handled on Bloom's UI thread and deadlocks when a
// test posts it (the fetch comes from the WebView2 whose UI thread the handler then blocks), on the
// Edit tab and off it alike. A test sets copyright the way a person does, through the dialog below.

/** The open dialog, whichever page opened it. */
function dialog(page: Page): Locator {
    return page
        .getByRole("dialog")
        .filter({ hasText: "Copyright and License" });
}

/** Wait until the Copyright and License dialog is open and ready to type in. */
export async function waitForCopyrightDialog(page: Page): Promise<void> {
    await dialog(page)
        .getByLabel("Copyright Holder")
        .waitFor({ state: "visible", timeout: 60000 });
}

/**
 * Give the book a copyright holder in the open Copyright and License dialog, and click OK.
 *
 * The year is left as the dialog offers it (this year), because that is what a user accepts. OK
 * stays disabled until both fields are valid, so this waits for it rather than clicking blind.
 * Returns once the dialog has closed; whoever opened it decides what to check next.
 */
export async function setCopyrightHolder(
    page: Page,
    holder: string,
): Promise<void> {
    await waitForCopyrightDialog(page);
    const holderField = dialog(page).getByLabel("Copyright Holder");
    await holderField.click();
    await holderField.fill(holder);
    await expect(holderField).toHaveValue(holder, { timeout: 15000 });

    const ok = dialog(page).getByRole("button", { name: "OK", exact: true });
    await expect(ok).toBeEnabled({ timeout: 15000 });
    await ok.click();
    await expect(dialog(page)).toHaveCount(0, { timeout: 30000 });
}
