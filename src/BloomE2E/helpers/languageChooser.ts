// The language chooser: Bloom's "Choose Language" dialog around the ethnolib chooser. Bloom opens
// it from the Languages page of Collection Settings ("Change…", "Set…"; see
// chooseCollectionSettingsLanguage in helpers/collectionSettings.ts) and from the Publish tab's
// sign-language link (see helpers/publish.ts). These helpers drive it once it is open, the way a
// person does: type into the search box, click a result, then OK or Cancel.

import { expect, type Locator, type Page } from "@playwright/test";

/** A language to pick in the language chooser. */
export interface IChooserLanguage {
    /** What to type in the chooser's search box, e.g. "American Sign Language". */
    search: string;
    /** The language's code as the chooser shows it on the result, e.g. "ase". */
    code: string;
    /**
     * The script to pick when the chooser asks for one (it does for a language written in
     * several, such as Arabic: "Arabic", "Hebrew" or "Syriac").
     */
    script?: string;
    /** A name to show the language by, in place of the chooser's own. */
    customName?: string;
}

/**
 * The language chooser that is showing. Its title is in English: the suite runs Bloom's UI in
 * English. An earlier chooser can stay in the page, hidden, after it closes, so only a visible one
 * counts.
 */
function languageChooser(page: Page): Locator {
    return page
        .getByRole("dialog")
        .filter({ hasText: "Choose Language", visible: true })
        .last();
}

/** Wait until the language chooser is showing. */
export async function waitForLanguageChooser(page: Page): Promise<void> {
    await languageChooser(page).waitFor({ state: "visible", timeout: 30000 });
}

/**
 * In the open chooser, search for the language and click its result, then pick its script if the
 * chooser asks for one and type its custom name if it has one. The choice is not made until OK
 * (confirmLanguageChooser).
 */
export async function pickLanguageInChooser(
    page: Page,
    language: IChooserLanguage,
): Promise<void> {
    const chooser = languageChooser(page);
    await chooser.locator("#search-bar").fill(language.search);
    // Each result card shows its code on its own; the search results come in as you type.
    const result = chooser.getByText(language.code, { exact: true }).first();
    try {
        await result.waitFor({ state: "visible", timeout: 30000 });
    } catch (e) {
        throw new Error(
            `Searching the language chooser for "${language.search}" offered no language "${language.code}". ` +
                `It showed: ${(await chooser.innerText()).slice(0, 500)}`,
            { cause: e },
        );
    }
    await result.click();
    if (language.script) {
        // Each script is a card whose name is an h2. The language's own result card has an h2
        // too, which can have the same name (Arabic), and the script cards come after it.
        await chooser
            .locator("button.MuiCardActionArea-root h2")
            .filter({ hasText: new RegExp(`^${language.script}$`) })
            .last()
            .click();
    }
    if (language.customName)
        await chooser.locator("#language-name-bar").fill(language.customName);
}

/** What the open language chooser has selected, as a person sees it there. */
export interface IChooserSelection {
    /**
     * The code on the selected language's card, e.g. "ce", or undefined when no language card is
     * selected.
     */
    language: string | undefined;
    /**
     * The code of the selected script's card, e.g. "Cyrl", or undefined when no script card is
     * selected (the language is written in one script, so the chooser shows none).
     */
    script: string | undefined;
    /** The name in the box that names the language, which is what Bloom will call it. */
    name: string;
    /** The language tag the chooser shows under that box, e.g. "ce-Cyrl". */
    tag: string;
}

/**
 * Read what the open chooser has selected: the highlighted language and script cards, the name
 * and the tag. A chooser opened on a language the collection already has starts out like this, so
 * this is how a test sees whether it came back with the earlier choice.
 */
export async function getLanguageChooserSelection(
    page: Page,
): Promise<IChooserSelection> {
    const chooser = languageChooser(page);
    // The chooser marks the selected card's button; the language card holds a
    // data-testid="language-card-<code>" and a script card one of "script-card-<code>".
    const selectedCode = async (prefix: string) => {
        const card = chooser
            .locator(".selected-option-card-button")
            .locator(`[data-testid^="${prefix}"]`);
        const count = await card.count();
        if (count > 1)
            throw new Error(
                `The language chooser has ${count} selected cards whose test id starts "${prefix}".`,
            );
        if (count === 0) return undefined;
        return (await card.getAttribute("data-testid"))!.slice(prefix.length);
    };
    return {
        language: await selectedCode("language-card-"),
        script: await selectedCode("script-card-"),
        name: await chooser.locator("#language-name-bar").inputValue(),
        tag: (
            await chooser.getByTestId("right-panel-langtag-preview").innerText()
        ).trim(),
    };
}

/**
 * Replace the name in the open chooser's name box with `name`, as a person does who wants the
 * language shown by a name of their own. The choice is not made until OK.
 */
export async function setLanguageChooserName(
    page: Page,
    name: string,
): Promise<void> {
    const nameBox = languageChooser(page).locator("#language-name-bar");
    await nameBox.fill(name);
    await expect(nameBox).toHaveValue(name);
}

/** Click OK in the language chooser and wait for it to close. */
export async function confirmLanguageChooser(page: Page): Promise<void> {
    const ok = languageChooser(page).getByRole("button", {
        name: "OK",
        exact: true,
    });
    await expect(
        ok,
        "the chooser's OK is still disabled: the chooser may be asking for a script",
    ).toBeEnabled();
    await closeLanguageChooserWith(page, ok, "OK");
}

/** Click Cancel in the language chooser and wait for it to close. */
export async function cancelLanguageChooser(page: Page): Promise<void> {
    const cancel = languageChooser(page).getByRole("button", {
        name: "Cancel",
        exact: true,
    });
    await closeLanguageChooserWith(page, cancel, "Cancel");
}

async function closeLanguageChooserWith(
    page: Page,
    button: Locator,
    buttonName: string,
): Promise<void> {
    await button.click();
    await expect(
        languageChooser(page),
        `The language chooser stayed open after ${buttonName}.`,
    ).toHaveCount(0);
}
