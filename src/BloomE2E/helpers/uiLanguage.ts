// Drive and read the UI language menu at the top right of Bloom's top bar.
//
// Two things here are unlike the rest of the helper layer, and both are deliberate.
//
// The menu's language entries carry no test id. LocalizableMenuItem uses the localization id as
// its test id, and a language entry has no localization id to use -- its label is a language name,
// not a localized string. So the only handle on an entry is its visible text. That is normally the
// smell this suite avoids, but for these entries the text IS the contract: the React menu sends
// the chosen entry back to C# by display name (see WorkspaceView.HandleUiLanguageAction), so a
// label that did not match what workspace/uiLanguages reports would be an entry nobody could
// select. Asserting on it is therefore testing the round trip, not settling for a weak selector.
//
// Second, the pseudo-locale's own label is deliberately not localizable (see
// WorkspaceView.kPseudoLocalizationMenuText): it has to stay recognizable while the rest of the UI
// is pseudolocalized, so that whoever turned it on can find their way back out. Matching it by
// text is safe in a way that matching a real language's name would not be.

import { expect, type Page } from "@playwright/test";
import { apiGet, apiGetJson } from "./api";

/**
 * What the pseudo-locale is called in the menu. Bloom hard-codes this (it is not localizable, and
 * Palaso has no useful name for a pseudo-locale), so a test may match on it. See BL-16748.
 */
export const kPseudoEnglishUiLanguage = "Pseudo-English (i18n test)";

/**
 * The UI language names Bloom is currently offering, in the order the menu shows them: real
 * languages sorted by name, then the pseudo-locale last when this build offers it.
 *
 * These are display names rather than language tags because that is what the menu round-trips;
 * see the note at the top of this file.
 */
export async function getOfferedUiLanguages(page: Page): Promise<string[]> {
    return apiGetJson<string[]>(page, "workspace/uiLanguages");
}

/** The language tag Bloom is currently showing its UI in, e.g. "en" or "qps-ploc". */
export async function getCurrentUiLanguageTag(page: Page): Promise<string> {
    return (await apiGet(page, "i18n/uilang")).body;
}

/**
 * Open the UI language menu by clicking its real top-bar button, and return once its entries are
 * showing. Close it with closeUiLanguageMenu.
 */
export async function openUiLanguageMenu(page: Page): Promise<void> {
    await page.locator("#uiLanguageMenuButton").click();
    await page
        .getByRole("menuitem")
        .first()
        .waitFor({ state: "visible", timeout: 30000 });
}

/** Close the UI language menu without choosing anything. */
export async function closeUiLanguageMenu(page: Page): Promise<void> {
    await page.keyboard.press("Escape");
    await expect
        .poll(async () => page.getByRole("menuitem").count(), {
            timeout: 30000,
            message: "The UI language menu never closed.",
        })
        .toBe(0);
}

/**
 * The entries showing in the open UI language menu, top to bottom, as the user reads them. This
 * includes the two command entries below the languages ("Help us translate Bloom (web)" and the
 * show-unapproved toggle); use getOfferedUiLanguages for the languages alone.
 */
export async function getUiLanguageMenuEntries(page: Page): Promise<string[]> {
    return page.getByRole("menuitem").allInnerTexts();
}
