// The language chooser: Bloom's "Choose Language" dialog around the ethnolib chooser. Bloom opens
// it from the Languages page of Collection Settings ("Change…", "Set…") and from the Publish tab's
// sign-language link (see helpers/publish.ts). These helpers drive it once it is open, the way a
// person does: type into the search box, click a result, then OK or Cancel.

import { expect, type Locator, type Page } from "@playwright/test";

/** The open language chooser. Its title is in English: the suite runs Bloom's UI in English. */
function languageChooser(page: Page): Locator {
    return page.getByRole("dialog").filter({ hasText: "Choose Language" });
}

/** Wait until the language chooser is showing. */
export async function waitForLanguageChooser(page: Page): Promise<void> {
    await languageChooser(page).waitFor({ state: "visible", timeout: 30000 });
}

/**
 * In the open chooser, search for `searchText` and click the result whose language tag is `tag`,
 * e.g. "American Sign Language" and "ase". The choice is not made until OK
 * (confirmLanguageChooser).
 */
export async function pickLanguageInChooser(
    page: Page,
    searchText: string,
    tag: string,
): Promise<void> {
    const chooser = languageChooser(page);
    await chooser.getByRole("textbox").first().fill(searchText);
    // Each result card shows its tag on its own; the search results come in as you type.
    const result = chooser.getByText(tag, { exact: true }).first();
    try {
        await result.waitFor({ state: "visible", timeout: 30000 });
    } catch (e) {
        throw new Error(
            `Searching the language chooser for "${searchText}" offered no language "${tag}". ` +
                `It showed: ${(await chooser.innerText()).slice(0, 500)}`,
            { cause: e },
        );
    }
    await result.click();
}

/** Click OK in the language chooser and wait for it to close. */
export async function confirmLanguageChooser(page: Page): Promise<void> {
    const chooser = languageChooser(page);
    await chooser.getByRole("button", { name: "OK", exact: true }).click();
    await expect(
        chooser,
        "The language chooser stayed open after OK.",
    ).toBeHidden();
}

/** Click Cancel in the language chooser and wait for it to close. */
export async function cancelLanguageChooser(page: Page): Promise<void> {
    const chooser = languageChooser(page);
    await chooser.getByRole("button", { name: "Cancel", exact: true }).click();
    await expect(
        chooser,
        "The language chooser stayed open after Cancel.",
    ).toBeHidden();
}
