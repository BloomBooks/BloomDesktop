// Drive the Text Box Properties dialog, which Change Layout mode offers on each text box through
// the gear beside the box's "Text Box" label (TextBoxProperties.ts in BloomBrowserUI). The dialog
// and its gear are both in the page's own iframe.
//
// The dialog's tabs are found by their localization ids, and its language choices by their radio
// buttons' values, so the UI language does not matter.

import { expect, type Locator, type Page } from "@playwright/test";
import { editablePageFrame } from "./bookMaking";
import { setChangeLayoutMode } from "./origami";
import type { VerticalAlignment } from "./textBoxScrollBars";

/**
 * The languages the dialog's Language tab offers a text box, by the value of each radio button,
 * which is also what Bloom writes into the box's data-default-languages attribute. "normal" shows
 * whichever languages the book shows; the others show only that one of the collection's languages.
 */
const LANGUAGE_VALUE = {
    normal: "Auto",
    L1: "V",
    L2: "N1",
    L3: "N2",
} as const;

/** A choice on the Language tab of the Text Box Properties dialog. */
export type TextBoxLanguage = keyof typeof LANGUAGE_VALUE;

/** The Text Box Properties dialog, whether or not it is open. */
export function textBoxPropertiesDialog(page: Page): Locator {
    return editablePageFrame(page).locator("#text-properties-dialog");
}

/**
 * Open the Text Box Properties dialog of a text box on the page, by clicking the gear beside its
 * "Text Box" label, and wait until it shows. `textBoxIndex` counts the page's text boxes in
 * document order, from 0. Requires Change Layout mode, which is the only time Bloom draws that
 * label. The dialog opens on its Language tab.
 */
export async function openTextBoxProperties(
    page: Page,
    textBoxIndex = 0,
): Promise<Locator> {
    await editablePageFrame(page)
        .locator(".textBox-identifier .formatButton")
        .nth(textBoxIndex)
        .click();
    const dialog = textBoxPropertiesDialog(page);
    await dialog.waitFor({ state: "visible", timeout: 15000 });
    return dialog;
}

/**
 * Close the Text Box Properties dialog the way a person does, by clicking the page outside it, and
 * wait until it has gone.
 */
export async function closeTextBoxProperties(page: Page): Promise<void> {
    await editablePageFrame(page)
        .locator("body")
        .click({ position: { x: 2, y: 2 } });
    await expect(
        textBoxPropertiesDialog(page),
        "Clicking outside the Text Box Properties dialog did not close it.",
    ).toBeHidden({ timeout: 15000 });
}

/**
 * Which choice the open dialog's Language tab has selected, which is the language the text box was
 * last set to show. Throws when none of the choices is selected.
 */
export async function getCheckedTextBoxLanguage(
    page: Page,
): Promise<TextBoxLanguage> {
    const value = await textBoxPropertiesDialog(page)
        .locator('input[name="languageRadioGroup"]:checked')
        .getAttribute("value", { timeout: 15000 });
    const choice = (Object.keys(LANGUAGE_VALUE) as TextBoxLanguage[]).find(
        (key) => LANGUAGE_VALUE[key] === value,
    );
    if (!choice)
        throw new Error(
            `The Text Box Properties dialog has "${value}" selected on its Language tab, which is ` +
                `none of ${Object.values(LANGUAGE_VALUE).join(", ")}.`,
        );
    return choice;
}

/**
 * Make the first text box on the page being edited show `language`, the way a person does: turn
 * Change Layout on, open the box's Text Box Properties, choose that language on the Language tab,
 * click outside the dialog to close it, and turn Change Layout off again. Returns once Bloom has
 * rebuilt the page, so the page shows what the choice does.
 *
 * The page must allow a custom layout, such as one added with addPage(page, "Just Text").
 */
export async function setTextBoxLanguage(
    page: Page,
    language: TextBoxLanguage,
): Promise<void> {
    await setChangeLayoutMode(page, true);
    await chooseTextBoxLanguage(page, language);
    await setChangeLayoutMode(page, false);
}

/**
 * In Change Layout mode, open one text box's Text Box Properties, choose `language` on the
 * Language tab, and click outside the dialog to close it. `textBoxIndex` counts the page's text
 * boxes in document order, from 0. The page shows what the choice does only once Change Layout is
 * turned off, so a test can set several boxes before it turns it off.
 */
export async function chooseTextBoxLanguage(
    page: Page,
    language: TextBoxLanguage,
    textBoxIndex = 0,
): Promise<void> {
    const dialog = await openTextBoxProperties(page, textBoxIndex);
    const radio = dialog.locator(
        `input[name="languageRadioGroup"][value="${LANGUAGE_VALUE[language]}"]`,
    );
    await radio.click();
    await expect(
        radio,
        `The "${language}" choice in Text Box Properties did not become selected.`,
    ).toBeChecked();
    await closeTextBoxProperties(page);
}

/**
 * Give the first text box on the page being edited this vertical alignment, the way a person does:
 * turn Change Layout on, open the box's Text Box Properties, choose the alignment on the Alignment
 * tab, click outside the dialog to close it, and turn Change Layout off again. Returns once Bloom
 * has rebuilt the page.
 */
export async function setTextBoxVerticalAlignment(
    page: Page,
    alignment: VerticalAlignment,
): Promise<void> {
    await setChangeLayoutMode(page, true);
    const dialog = await openTextBoxProperties(page);
    // The dialog opens on its Language tab; the alignment buttons are on the next one.
    await dialog
        .locator('h2.tab[data-i18n="EditTab.TextBoxProperties.Alignment"]')
        .click();
    const button = dialog.locator(`#align-${alignment}`);
    await button.click();
    await expect(
        button,
        `The "${alignment}" alignment in Text Box Properties did not become selected.`,
    ).toHaveClass(/selectedIcon/);
    await closeTextBoxProperties(page);
    await setChangeLayoutMode(page, false);
}
