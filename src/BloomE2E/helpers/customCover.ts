// The page layout menu that a cover offers above the page: "Standard Layout" or "Custom Layout".
// A custom layout turns everything on the cover into canvas elements on one canvas, which a person
// can then move, resize and add to with the Canvas tool (customXmatterPage.tsx). Custom is a
// subscription feature (CustomXMatterPage), so a test that chooses it opens a collection with a
// subscription, e.g. kEnterpriseSubscriptionCode.

import { expect, type Page } from "@playwright/test";
import {
    editablePageFrame,
    waitForEditablePage,
    waitForEditTabSettled,
} from "./bookMaking";

/** The two layouts the page layout menu offers. */
export type PageLayout = "Standard" | "Custom";

/**
 * Choose the layout of the page being edited, a cover, on the page layout menu above the page, the
 * way a person does: click the menu, then the layout. Does nothing when the page has that layout
 * already. Returns once the page has it and Bloom is editing the page again.
 *
 * Choosing Custom for the first time converts the cover and then has Bloom load the page afresh
 * (editView/jumpToPage), so the page in the frame is replaced a moment after the conversion shows.
 * This waits for that reload to settle; a click aimed at the page before then lands on a page that
 * is about to go.
 */
export async function choosePageLayout(
    page: Page,
    layout: PageLayout,
): Promise<void> {
    const wantCustom = layout === "Custom";
    if ((await isCustomLayout(page)) === wantCustom) return;
    const frame = editablePageFrame(page);
    const button = frame.locator('[data-testid="page-layout-menu-button"]');
    if ((await button.count()) === 0)
        throw new Error(
            "The page being edited offers no page layout menu. Only a cover offers one.",
        );
    await button.click();
    await frame
        .locator(`li[data-testid="EditTab.CustomCover.${layout}"]`)
        .click();
    await expect
        .poll(async () => isCustomLayout(page), {
            timeout: 60000,
            message: `Choosing the ${layout} layout did not give the page that layout.`,
        })
        .toBe(wantCustom);
    await waitForEditTabSettled(page);
    await waitForEditablePage(page);
}

/** Whether the page being edited has a custom layout. */
async function isCustomLayout(page: Page): Promise<boolean> {
    return (
        (await editablePageFrame(page)
            .locator(".bloom-page.bloom-customLayout")
            .count()
            .catch(() => 0)) > 0
    );
}
