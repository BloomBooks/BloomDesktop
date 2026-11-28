// The title page, and in particular its box for the contributions made by writers, illustrators
// and so on (data-book="originalContributions").
//
// That box's hint bubble ends with a "Paste Image Credits" link (bloom-xmatter-mixins.pug,
// standard-originalContributions). Clicking it asks Bloom for image/imageCreditsForWholeBook,
// which lists the credits of every picture in the book with the pages each appears on, and
// appends them to the box as one <p> per credit (EditableDivUtils.pasteImageCredits). The bubble
// is a qTip2 tooltip that Bloom's editing code puts inside the page frame; it shows while the
// pointer is on the box.

import { expect, type Locator, type Page } from "@playwright/test";
import {
    editablePageFrame,
    getPages,
    goToPage,
    waitForEditablePage,
} from "./bookMaking";

/**
 * Show the book's title page in the Edit tab. Throws, listing the pages there are, when the book
 * has none. Like goToPage, leaving the page Bloom was showing saves it.
 */
export async function goToTitlePage(page: Page): Promise<void> {
    const pages = await getPages(page);
    const titlePage = pages.find(
        (p) => !p.isContentPage && /title page/i.test(p.caption),
    );
    if (!titlePage)
        throw new Error(
            `The book has no title page. Its pages: ${pages.map((p) => p.caption).join(", ")}.`,
        );
    await goToPage(page, titlePage.id);
}

/**
 * The contributions box on the title page, in the language the page shows it in. Tests read its
 * text through getOriginalContributions, and its markup from here when they must check what the
 * text hides, such as a link.
 */
export function originalContributionsBox(page: Page): Locator {
    return editablePageFrame(page)
        .locator(
            '.bloom-page .bloom-editable[data-book="originalContributions"]:visible',
        )
        .first();
}

/**
 * Click "Paste Image Credits" in the hint bubble of the title page's contributions box, the way a
 * person does: point at the box so its bubble shows, then click the link in it. Call goToTitlePage
 * first. Returns once Bloom has put at least one paragraph more into the box than it held before.
 */
export async function pasteImageCredits(page: Page): Promise<void> {
    await waitForEditablePage(page);
    const box = originalContributionsBox(page);
    await box.waitFor({ state: "visible", timeout: 30000 });
    const paragraphsBefore = await box.locator("p").count();
    await box.hover();
    const link = editablePageFrame(page)
        .locator(".qtip a[href*='PasteImageCredits']")
        .first();
    try {
        await link.waitFor({ state: "visible", timeout: 15000 });
    } catch {
        throw new Error(
            'Pointing at the title page\'s contributions box showed no "Paste Image Credits" link.',
        );
    }
    // Not realClick: in a narrow bubble the link wraps onto two lines, and the centre of its
    // bounding box can fall between them, on the bubble rather than the link. Bloom pastes only
    // when the click leaves the link focused (EditableDivUtils.pasteImageCredits reads
    // document.activeElement), so such a click does nothing. Playwright's click presses on a point
    // of the link's first line and checks that the link is what receives it.
    await link.click({ timeout: 15000 });
    await expect
        .poll(async () => box.locator("p").count(), {
            timeout: 30000,
            message:
                'Clicking "Paste Image Credits" put nothing into the title page\'s contributions box.',
        })
        .toBeGreaterThan(paragraphsBefore);
}

/**
 * The paragraphs in the title page's contributions box, each as the reader sees it, trimmed, with
 * empty ones left out. Call goToTitlePage first.
 */
export async function getOriginalContributions(page: Page): Promise<string[]> {
    await waitForEditablePage(page);
    const box = originalContributionsBox(page);
    await box.waitFor({ state: "attached", timeout: 30000 });
    return box.evaluate((element) =>
        Array.from(element.querySelectorAll("p"))
            .map((p) => (p as HTMLElement).innerText.trim())
            .filter((text) => text.length > 0),
    );
}
