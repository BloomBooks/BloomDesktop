// Bloom's answer when someone reaches for a feature their subscription tier does not include:
// the "Bloom Subscription Feature" dialog, and the badge that opens it.
//
// Two things make this awkward enough to need a helper. The dialog is mounted into whichever
// document the surface that raised it lives in -- the Edit tab's page iframe for the page layout
// controls, the shell document for a publish screen -- so everything here looks through every
// frame rather than assuming one. And every word in it is localized, so the only stable handle is
// the test id on its body, which is present only while the dialog is open (see
// RequiresSubscriptionDialog in react_components/requiresSubscription.tsx).
//
// A test asserting that NO dialog appeared should use expectNoSubscriptionDialog, which watches
// for a while: the dialog is raised by a click, and the click returns before React has rendered.

import { expect, type Frame, type Page } from "@playwright/test";

/** The body of the open subscription dialog. Absent when it is shut. */
const DIALOG = '[data-testid="requires-subscription-dialog"]';

/** How long expectNoSubscriptionDialog watches before it believes no dialog is coming. */
const NOT_COMING_MS = 1500;

/** The frame showing the subscription dialog, or undefined when no frame is. */
async function frameWithDialog(page: Page): Promise<Frame | undefined> {
    for (const frame of page.frames()) {
        // A frame can be navigating, in which case asking it anything throws; that frame is not
        // showing a dialog we could act on.
        const count = await frame
            .locator(DIALOG)
            .count()
            .catch(() => 0);
        if (count > 0) return frame;
    }
    return undefined;
}

/** True while the subscription dialog is open in any of Bloom's frames. */
export async function isSubscriptionDialogShowing(
    page: Page,
): Promise<boolean> {
    return !!(await frameWithDialog(page));
}

/**
 * Wait until the subscription dialog opens. Use it after the action that should raise it, so that
 * the failure message is about the dialog rather than about a locator timing out.
 */
export async function waitForSubscriptionDialog(
    page: Page,
    what = "the last action",
): Promise<void> {
    await expect
        .poll(() => isSubscriptionDialogShowing(page), {
            timeout: 30000,
            message: `${what} should have opened the dialog that says what subscription this feature needs.`,
        })
        .toBe(true);
}

/**
 * Assert that no subscription dialog appears. Watches for a while rather than asking once: the
 * dialog is raised in answer to a click, and the click returns before React has rendered it, so a
 * single look would pass however wrong the product was.
 */
export async function expectNoSubscriptionDialog(
    page: Page,
    what = "nothing here",
): Promise<void> {
    const deadline = Date.now() + NOT_COMING_MS;
    while (Date.now() < deadline) {
        expect(
            await isSubscriptionDialogShowing(page),
            `${what} should have opened no subscription dialog, but one is showing.`,
        ).toBe(false);
    }
}

/**
 * Shut the open subscription dialog with its Close button, and wait until it has gone. Throws when
 * no dialog is open, because a test that closes a dialog that never opened is not testing what it
 * thinks.
 */
export async function closeSubscriptionDialog(page: Page): Promise<void> {
    const frame = await frameWithDialog(page);
    if (!frame)
        throw new Error(
            "There is no subscription dialog open, so there is nothing to close.",
        );
    await frame
        .locator(DIALOG)
        .getByRole("button", { name: "Close" })
        .click({ timeout: 15000 });
    await expect
        .poll(() => isSubscriptionDialogShowing(page), {
            timeout: 30000,
            message: "The subscription dialog never closed.",
        })
        .toBe(false);
}
