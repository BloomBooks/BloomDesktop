// Drive and read the Publish tab: its destination chooser, and the Text Languages check box list.
//
// The list is shared by the Web and BloomPUB screens, which both render PublishLanguagesGroup and
// both write BookInfo.PublishSettings.BloomLibrary.TextLangs. So "does the other screen agree" is a
// real question a test can ask, not a formality.

import { expect, type Frame, type Locator, type Page } from "@playwright/test";
import { apiGetJson } from "./api";
import { switchTab } from "./workspace";

/** The publish destinations, by the label each one shows in the left-hand column. */
export type PublishDestination =
    | "PDF & Print"
    | "Web"
    | "BloomPUB"
    | "Apps"
    | "ePUB"
    | "Audio or Video";

/**
 * What publish/languagesInBook says about one language of the book. This is Bloom's own view of
 * the same state the check boxes show, so a test can assert on it instead of scraping the DOM.
 * Mirrors C#'s LanguagePublishInfo.
 */
export interface ILanguagePublishInfo {
    code: string;
    name: string;
    /** False when at least one content-page translation group lacks this language. */
    complete: boolean;
    includeText: boolean;
    containsAnyAudio: boolean;
    includeAudio: boolean;
    /** True when the book currently shows this language, which forces it into the publication. */
    required: boolean;
}

/** One row of the Text Languages list, as a reader of the screen sees it. */
export interface ITextLanguageRow {
    /** The language's name, as the row shows it: the collection's name for it, or its autonym. */
    name: string;
    /** True when the row carries the "(incomplete translation)" sub-label. */
    incomplete: boolean;
    checked: boolean;
    /** True for a language the book shows: it is required, so the box cannot be cleared. */
    disabled: boolean;
}

/**
 * Go to the Publish tab and click one of its destinations, without waiting for what that screen
 * shows. Each screen's own module waits for its own content; they do not all show the same things
 * (the Web screen, for one, shows no Text Languages list for a book that has no text).
 */
export async function selectPublishDestination(
    page: Page,
    destination: PublishDestination,
): Promise<void> {
    await switchTab(page, "publish");
    const target = page.getByRole("tab", { name: destination, exact: true });
    await target.waitFor({ state: "visible", timeout: 30000 });
    await target.click();
}

/**
 * Go to the Publish tab, open one of its destinations, and wait for its Text Languages list. A
 * book must already be selected. Use this for the screens whose subject is that list; the Web
 * screen has its own opener in helpers/libraryPublish.ts.
 */
export async function openPublishDestination(
    page: Page,
    destination: PublishDestination,
): Promise<void> {
    await selectPublishDestination(page, destination);
    await textLanguagesGroup(page).waitFor({
        state: "visible",
        timeout: 60000,
    });
}

/**
 * Go to the Publish tab and wait until it has decided what to show: either its list of
 * destinations, or the notice that this book cannot be published at this subscription tier.
 *
 * The tab shows a blank screen until publish/getInitialPublishTabInfo answers, so a test that
 * looked at once could see neither.
 */
export async function openPublishTab(page: Page): Promise<void> {
    await switchTab(page, "publish");
    await expect
        .poll(
            async () =>
                (await getPublishDestinationsOffered(page)).length > 0 ||
                (await isPublishingBlockedNoticeShowing(page)),
            {
                timeout: 60000,
                message:
                    "The Publish tab showed neither a destination to publish to nor a notice " +
                    "saying why it cannot be published.",
            },
        )
        .toBe(true);
}

/**
 * The destinations the Publish tab is offering, by the label each shows. Empty when the tab is
 * showing something instead of its destinations, which is what happens when the book uses a
 * feature above the collection's subscription tier: the notice replaces the whole list.
 */
export async function getPublishDestinationsOffered(
    page: Page,
): Promise<string[]> {
    // The destination strip is react-tabs' own tab list, told apart from the workspace's tabs by
    // that class. One member of it is deliberately invisible: it is the "nothing chosen yet" tab.
    return page
        .locator(".react-tabs__tab-list .react-tabs__tab:not(.invisible_tab)")
        .evaluateAll((tabs) =>
            tabs.map((tab) => (tab.textContent ?? "").trim()).filter(Boolean),
        );
}

/**
 * True while the Publish tab is showing the notice that says the book uses a feature the
 * collection's subscription tier does not include. Found by its test id, because every word of it
 * is localized (PublishingBookRequiresHigherTierNotice.tsx).
 */
export async function isPublishingBlockedNoticeShowing(
    page: Page,
): Promise<boolean> {
    return (
        (await page
            .locator('[data-testid="publishing-blocked-notice"]')
            .count()) > 0
    );
}

/** What the notice says, for a failure message. Empty when it is not showing. */
export async function getPublishingBlockedNoticeText(
    page: Page,
): Promise<string> {
    const notice = page.locator('[data-testid="publishing-blocked-notice"]');
    if ((await notice.count()) === 0) return "";
    return (await notice.first().innerText()).replace(/\s+/g, " ").trim();
}

/** The Text Languages group, told apart from the Talking Book group by its test id. */
export function textLanguagesGroup(page: Page): Locator {
    return page.getByTestId("text-languages-group");
}

/** Every row of the Text Languages list, in the order the screen shows them. */
export async function getTextLanguageRows(
    page: Page,
): Promise<ITextLanguageRow[]> {
    await textLanguagesGroup(page).waitFor({
        state: "visible",
        timeout: 60000,
    });
    return page.evaluate(() => {
        const group = document.querySelector(
            '[data-testid="text-languages-group"]',
        );
        if (!group) return [];
        return [...group.querySelectorAll('input[type="checkbox"]')].map(
            (input) => {
                const box = input as HTMLInputElement;
                const label = box.closest("label");
                const lines = (label?.innerText ?? "")
                    .split("\n")
                    .map((line) => line.trim())
                    .filter(Boolean);
                return {
                    name: lines[0] ?? "",
                    incomplete: lines.some((line) =>
                        line.includes("incomplete translation"),
                    ),
                    checked: box.checked,
                    disabled: box.disabled,
                };
            },
        );
    });
}

/**
 * Wait until the Text Languages list settles into `expected`, and return it. The list is filled
 * from publish/languagesInBook after the screen mounts, so reading it once races that fetch.
 */
export async function expectTextLanguageRows(
    page: Page,
    expected: ITextLanguageRow[],
    message: string,
): Promise<void> {
    await expect
        .poll(async () => JSON.stringify(await getTextLanguageRows(page)), {
            timeout: 30000,
            message,
        })
        .toBe(JSON.stringify(expected));
}

/**
 * Wait until the Text Languages list holds exactly `expected`, in any order, and return it.
 *
 * Use this where the position of a row is not part of the behavior being tested. Where it is,
 * such as an incomplete language sorting last, use expectTextLanguageRows instead.
 */
export async function expectTextLanguageRowsInAnyOrder(
    page: Page,
    expected: ITextLanguageRow[],
    message: string,
): Promise<void> {
    const byName = (rows: ITextLanguageRow[]) =>
        JSON.stringify([...rows].sort((a, b) => a.name.localeCompare(b.name)));
    await expect
        .poll(async () => byName(await getTextLanguageRows(page)), {
            timeout: 30000,
            message,
        })
        .toBe(byName(expected));
}

/** Click one language's check box, by the name its row shows. This is the action under test. */
export async function clickTextLanguage(
    page: Page,
    name: string,
): Promise<void> {
    const row = textLanguagesGroup(page)
        .locator("label")
        .filter({ hasText: name })
        .first();
    await row.waitFor({ state: "visible", timeout: 30000 });
    await row.locator('input[type="checkbox"]').click();
}

/**
 * Click PREVIEW on the BloomPUB screen and wait for bloom-player to show the book.
 *
 * The preview is the publication itself, so it is where a test can see whether clearing a
 * language's check box really kept that language out.
 */
export async function showBloomPubPreview(page: Page): Promise<Frame> {
    await page.locator('[aria-label="refresh preview"]').click();
    let player: Frame | undefined;
    await expect
        .poll(
            async () => {
                player = page
                    .frames()
                    .find((f) => f.url().includes("bloomplayer.htm"));
                if (!player) return 0;
                // The book's own pages, not the language chooser: bloom-player offers that button
                // only for a book with more than one language, so waiting for it left a
                // single-language book's preview looking as though it had never loaded.
                return player
                    .locator(".bloom-page")
                    .count()
                    .catch(() => 0);
            },
            {
                timeout: 120000,
                message: "The BloomPUB preview never showed the book.",
            },
        )
        .toBeGreaterThan(0);
    return player!;
}

/**
 * The languages bloom-player offers in its "Languages in this book:" menu, in the order shown.
 * Each is the name the language calls itself, which is not the name the check box list shows.
 *
 * The menu is closed again, so the preview is left as it was found.
 */
export async function getPreviewLanguages(player: Frame): Promise<string[]> {
    await player.locator('[aria-label="Choose Language"]').click();
    const options = player.locator('[aria-label="languages"] label');
    await options.first().waitFor({ state: "visible", timeout: 30000 });
    const names = (await options.allInnerTexts()).map((t) => t.trim());
    await player.getByRole("button", { name: "Close" }).click();
    return names;
}

/** What Bloom itself says about the languages in the selected book. */
export async function getLanguagesInBook(
    page: Page,
): Promise<ILanguagePublishInfo[]> {
    return apiGetJson<ILanguagePublishInfo[]>(page, "publish/languagesInBook");
}

/**
 * The tooltip text shown for one row, after hovering it the way a reader would. Bloom's tooltip
 * needs a real pointer over the row; it is not in the DOM until then.
 *
 * The pointer moves off every row first, and the previous tooltip has to go, because reading the
 * tooltip while the last one is still leaving returns the last one's words.
 */
export async function getTooltipForLanguage(
    page: Page,
    name: string,
): Promise<string> {
    const tooltip = page.locator('[role="tooltip"]');
    await page.mouse.move(0, 0);
    await expect
        .poll(async () => tooltip.count(), {
            timeout: 30000,
            message: "The tooltip from the row hovered before never went away.",
        })
        .toBe(0);

    const row = textLanguagesGroup(page)
        .locator("label")
        .filter({ hasText: name })
        .first();
    await row.hover();
    await tooltip.first().waitFor({ state: "visible", timeout: 30000 });
    return (await tooltip.first().innerText()).trim();
}
