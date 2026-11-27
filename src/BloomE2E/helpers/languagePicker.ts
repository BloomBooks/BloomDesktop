// Drive and read the language chooser (EthnoLib's LanguageChooser, wrapped by
// src/BloomBrowserUI/collection/LanguageChooserDialog.tsx) as the Languages page of the new
// Collection Settings dialog opens it: a button beside each language, "Change..." when the slot has
// a language and "Set..." when it has none.
//
// These are for tests of the chooser itself. A test that only needs a language picked uses
// helpers/languageChooser.ts.
//
// The chooser's own parts carry EthnoLib's test ids (search-bar, language-card-<code>,
// script-card-<code>, customization-button, ...), which are the same in every UI language, so only
// Bloom's own wording around it does. Every function that finds something by its wording takes
// that wording, defaulting to English; a test running Bloom in another UI language passes what
// getLanguageChooserLabels gives.

import { expect, type Locator, type Page } from "@playwright/test";
import { realClick } from "./realClick";
import { getUiStrings } from "./uiLanguage";

/** Bloom's wording of the controls and headings around the chooser that these helpers use. */
export interface ILanguageChooserLabels {
    /** The Collections tab's button that opens the Collection Settings dialog. */
    settings: string;
    /** The dialog's Languages page, in its list of pages. */
    languages: string;
    /** The headings of the Languages page's groups for Language 1, 2 and 3. */
    language1: string;
    language2: string;
    language3: string;
    /** The button that opens the chooser for a slot that has a language. */
    change: string;
    /** The button that opens the chooser for a slot that has none (Language 3). */
    set: string;
    /** The title Bloom puts over the chooser. */
    title: string;
    ok: string;
    cancel: string;
}

const kLabelIds: Record<keyof ILanguageChooserLabels, [string, string]> = {
    settings: ["CollectionTab.SettingsButton", "Settings"],
    languages: [
        "CollectionSettingsDialog.LanguageTab.LanguageTabLabel",
        "Languages",
    ],
    language1: [
        "CollectionSettingsDialog.LanguagesPage.WrittenLanguage1",
        "Written Language 1",
    ],
    language2: [
        "CollectionSettingsDialog.LanguagesPage.WrittenLanguage2",
        "Written Language 2",
    ],
    language3: [
        "CollectionSettingsDialog.LanguagesPage.WrittenLanguage3",
        "Written Language 3",
    ],
    change: [
        "CollectionSettingsDialog.LanguageTab.ChangeLanguageLink",
        "Change...",
    ],
    set: [
        "CollectionSettingsDialog.LanguageTab.SetThirdLanguageLink",
        "Set...",
    ],
    title: [
        "CollectionSettingsDialog.LanguageTab.ChooseLanguage",
        "Choose Language",
    ],
    ok: ["Common.OK", "OK"],
    cancel: ["Common.Cancel", "Cancel"],
};

export const kEnglishLanguageChooserLabels = Object.fromEntries(
    Object.entries(kLabelIds).map(([key, [, english]]) => [key, english]),
) as unknown as ILanguageChooserLabels;

/** The labels in the UI language Bloom is showing now, as Bloom's localization gives them. */
export async function getLanguageChooserLabels(
    page: Page,
): Promise<ILanguageChooserLabels> {
    const strings = await getUiStrings(
        page,
        Object.fromEntries(Object.values(kLabelIds)),
    );
    return Object.fromEntries(
        Object.entries(kLabelIds).map(([key, [id]]) => [key, strings[id]]),
    ) as unknown as ILanguageChooserLabels;
}

/**
 * Open the Collection Settings dialog on its Languages page, as a person does: click Settings on
 * the Collections tab, then Languages in the dialog's list of pages. Returns once the Languages
 * page shows its first language.
 */
export async function openLanguagesPage(
    page: Page,
    labels: ILanguageChooserLabels = kEnglishLanguageChooserLabels,
): Promise<void> {
    await realClick(
        page.getByRole("button", { name: labels.settings, exact: true }),
    );
    const tab = settingsDialog(page).getByRole("tab", {
        name: labels.languages,
        exact: true,
    });
    await expect(
        tab,
        "the Collection Settings dialog did not open",
    ).toBeVisible({
        timeout: 30000,
    });
    await realClick(tab);
    await expect(tab).toHaveAttribute("aria-selected", "true");
    await expect(languageGroup(page, labels.language1)).toHaveCount(1);
}

/** Click Cancel in the Collection Settings dialog, and return once it has closed. */
export async function cancelSettingsDialog(
    page: Page,
    labels: ILanguageChooserLabels = kEnglishLanguageChooserLabels,
): Promise<void> {
    const dialog = settingsDialog(page);
    await realClick(
        dialog.getByRole("button", { name: labels.cancel, exact: true }),
    );
    await expect(dialog).toHaveCount(0);
}

/** The title Bloom shows over the open chooser. */
export async function getLanguageChooserTitle(page: Page): Promise<string> {
    return (
        await languageChooserDialog(page)
            .getByRole("heading", { level: 1 })
            .innerText()
    ).trim();
}

/** The open Collection Settings dialog, in any UI language: the dialog with a list of pages. */
function settingsDialog(page: Page): Locator {
    return page.getByRole("dialog").filter({ has: page.getByRole("tab") });
}

/**
 * The group of rows for one language on the Languages page, found by its heading, e.g. "Written
 * Language 1". As in helpers/collectionSettings.ts, Config-R puts the group's rows in the first MUI
 * Paper after its heading.
 */
function languageGroup(page: Page, heading: string): Locator {
    return settingsDialog(page).locator(
        `xpath=.//h3[normalize-space(.)="${heading}"]/following::*[contains(concat(" ",normalize-space(@class)," ")," MuiPaper-root ")][1]`,
    );
}

/** The open language chooser: the dialog that has the language search box. */
export function languageChooserDialog(page: Page): Locator {
    return page
        .getByRole("dialog")
        .filter({ has: page.getByTestId("search-bar") });
}

/**
 * Open the language chooser for one language of the collection, as a person does by clicking the
 * button beside it on the Languages page: "Change..." when the slot has a language, "Set..." when
 * it has none. Fails naming the button the group does show when it is not the expected one.
 * Returns once the chooser shows its search box.
 */
export async function openLanguageChooser(
    page: Page,
    heading: string,
    button: "change" | "set",
    labels: ILanguageChooserLabels = kEnglishLanguageChooserLabels,
): Promise<void> {
    const group = languageGroup(page, heading);
    await expect(
        group,
        `the Languages page has no language group "${heading}"`,
    ).toHaveCount(1);
    const wanted = group.getByRole("button", {
        name: labels[button],
        exact: true,
    });
    if ((await wanted.count()) !== 1) {
        const shown = await group.getByRole("button").allInnerTexts();
        throw new Error(
            `"${heading}" has no "${labels[button]}" button; its buttons are: ${shown.join(", ")}.`,
        );
    }
    await realClick(wanted);
    await expect(
        languageChooserDialog(page).getByTestId("search-bar"),
        "the language chooser did not open",
    ).toBeVisible({ timeout: 15000 });
}

/** The chooser's search box. */
function searchBox(page: Page): Locator {
    return languageChooserDialog(page).locator("#search-bar");
}

/** What the chooser's search box holds. */
export async function getLanguageSearchText(page: Page): Promise<string> {
    return searchBox(page).inputValue();
}

/**
 * Type `text` into the chooser's search box, replacing what it held, and return once the list
 * shows at least one result.
 */
export async function searchForLanguage(
    page: Page,
    text: string,
): Promise<void> {
    await searchBox(page).fill(text);
    await expect(searchBox(page)).toHaveValue(text);
    await expect(
        languageCards(page).first(),
        `searching for "${text}" found no language`,
    ).toBeVisible({ timeout: 15000 });
}

/**
 * Type `text`, which names no known language, into the chooser's search box, as a person does
 * before making an unlisted language, and return once the box holds it and no language is
 * selected (the chooser's customize button then offers "Create Unlisted Language").
 */
export async function searchForUnlistedLanguage(
    page: Page,
    text: string,
): Promise<void> {
    await searchBox(page).fill(text);
    await expect(searchBox(page)).toHaveValue(text);
    await expect(
        languageChooserDialog(page).locator(".selected-option-card-button"),
        "a language is still selected, so the chooser would customize it",
    ).toHaveCount(0);
}

/** Type the name of an unlisted language into the Customize dialog's Name box. */
export async function typeUnlistedLanguageName(
    page: Page,
    name: string,
): Promise<void> {
    const box = customizeDialog(page).locator("#unlisted-lang-name-field");
    await box.fill(name);
    await expect(box).toHaveValue(name);
}

/** Clear the search box with the X at its end, and return once it is empty. */
export async function clearLanguageSearch(page: Page): Promise<void> {
    await realClick(
        languageChooserDialog(page).getByTestId("clear-search-X-button"),
    );
    await expect(searchBox(page)).toHaveValue("");
}

/** The language cards the result list has rendered, in the order it shows them. */
function languageCards(page: Page): Locator {
    return languageChooserDialog(page)
        .locator("#language-card-list")
        .locator('[data-testid^="language-card-"]');
}

/**
 * The codes of the languages the result list shows, top to bottom, e.g. ["cwd", "cr", "crm"]. The
 * list renders a card only once it is scrolled near, so this is the top of the list unless
 * scrollToLanguage has brought later cards in.
 */
export async function getListedLanguageCodes(page: Page): Promise<string[]> {
    const ids = await languageCards(page).evaluateAll((cards) =>
        cards.map((card) => card.getAttribute("data-testid") ?? ""),
    );
    return ids.map((id) => id.replace(/^language-card-/, ""));
}

/**
 * Scroll the result list until the card of the language with this code is rendered, and return it.
 * The list renders cards lazily and its results arrive in batches, so this sweeps the list a
 * screenful at a time until the card appears, and fails naming the codes it did see if it never
 * does. Use it only for a language that is expected to be listed.
 */
export async function scrollToLanguage(
    page: Page,
    code: string,
): Promise<Locator> {
    const card = languageChooserDialog(page).getByTestId(
        `language-card-${code}`,
    );
    const list = languageChooserDialog(page).locator("#language-card-list");
    await expect
        .poll(
            async () => {
                if ((await card.count()) > 0) return true;
                await list.evaluate((element: HTMLElement) => {
                    const before = element.scrollTop;
                    element.scrollBy(0, Math.max(1, element.clientHeight - 40));
                    // At the bottom: start again from the top, in case more results arrived.
                    if (element.scrollTop === before) element.scrollTop = 0;
                });
                return false;
            },
            {
                message: `the result list never showed the language "${code}"`,
                timeout: 20000,
                intervals: [150],
            },
        )
        .toBe(true);
    await card.scrollIntoViewIfNeeded();
    return card;
}

/** The clickable card around a language or script card's text. */
function optionCard(page: Page, testId: string): Locator {
    return languageChooserDialog(page)
        .locator(".option-card-button")
        .filter({ has: page.getByTestId(testId) });
}

/**
 * Click the card of the language with this code in the result list, scrolling to it first, and
 * return once the card shows as selected.
 */
export async function chooseLanguage(page: Page, code: string): Promise<void> {
    await scrollToLanguage(page, code);
    const card = optionCard(page, `language-card-${code}`);
    await realClick(card);
    await expect(card).toHaveClass(/selected-option-card-button/);
}

/** The codes of the scripts the chooser offers for the selected language, e.g. ["Hebr", "Latn"]. */
export async function getOfferedScriptCodes(page: Page): Promise<string[]> {
    const ids = await languageChooserDialog(page)
        .locator('[data-testid^="script-card-"]')
        .evaluateAll((cards) =>
            cards.map((card) => card.getAttribute("data-testid") ?? ""),
        );
    return ids.map((id) => id.replace(/^script-card-/, ""));
}

/** The name a script card shows, e.g. "Syriac (Western variant)". */
export async function getScriptCardText(
    page: Page,
    scriptCode: string,
): Promise<string> {
    return (
        await languageChooserDialog(page)
            .getByTestId(`script-card-${scriptCode}`)
            .innerText()
    ).trim();
}

/**
 * Click the card of the script with this code (e.g. "Cyrl") under the selected language, and return
 * once it shows as selected.
 */
export async function chooseScript(
    page: Page,
    scriptCode: string,
): Promise<void> {
    const card = optionCard(page, `script-card-${scriptCode}`);
    await realClick(card);
    await expect(card).toHaveClass(/selected-option-card-button/);
}

/** The language tag the chooser's right-hand panel shows for the current selection, e.g. "lad-Cyrl". */
export async function getChosenLanguageTag(page: Page): Promise<string> {
    return (
        await languageChooserDialog(page)
            .getByTestId("right-panel-langtag-preview")
            .innerText()
    ).trim();
}

/** The chooser's Customize button, which is labelled in the UI language. */
export function customizeButton(page: Page): Locator {
    return languageChooserDialog(page).getByTestId("customization-button");
}

/** The open Customize dialog. */
function customizeDialog(page: Page): Locator {
    return page.getByTestId("customization-dialog");
}

/** Click Customize and return once its dialog shows the tag it is building. */
export async function openCustomizeDialog(page: Page): Promise<void> {
    await realClick(customizeButton(page));
    await expect(
        customizeDialog(page).getByTestId("customization-dialog-tag-preview"),
        "the Customize dialog did not open",
    ).toBeVisible();
}

/**
 * Choose an entry in one of the Customize dialog's drop-down lists (`fieldId` is
 * "customize-script-field" or "customize-region-field") by opening it and clicking the first
 * option whose name matches.
 */
async function chooseInCustomizeList(
    page: Page,
    fieldId: string,
    option: string | RegExp,
): Promise<void> {
    await realClick(
        customizeDialog(page)
            .locator(`#${fieldId}-wrapper`)
            .getByRole("button", { name: "Open" }),
    );
    await realClick(page.getByRole("option", { name: option }).first());
    await expect(page.getByRole("listbox")).toHaveCount(0);
}

/** Choose the script, e.g. "Arabic", in the Customize dialog. */
export async function chooseCustomizeScript(
    page: Page,
    script: string | RegExp,
): Promise<void> {
    await chooseInCustomizeList(page, "customize-script-field", script);
}

/** Choose the country, e.g. "Andorra", in the Customize dialog. */
export async function chooseCustomizeCountry(
    page: Page,
    country: string | RegExp,
): Promise<void> {
    await chooseInCustomizeList(page, "customize-region-field", country);
}

/** Type the dialect into the Customize dialog's "Variant (dialect)" box. */
export async function typeCustomizeDialect(
    page: Page,
    dialect: string,
): Promise<void> {
    const box = customizeDialog(page).locator("#customize-variant-field");
    await box.fill(dialect);
    await expect(box).toHaveValue(dialect);
}

/** The BCP 47 tag the Customize dialog shows for what it has so far, e.g. "es-Arab-AD-x-foo". */
export async function getCustomizeTag(page: Page): Promise<string> {
    const shown = (
        await customizeDialog(page)
            .getByTestId("customization-dialog-tag-preview")
            .innerText()
    ).trim();
    // The preview reads "BCP 47 Tag: es-Arab-AD-x-foo", its label in the UI language; a tag has
    // no spaces, so it is the last word.
    return /\S+$/.exec(shown)?.[0] ?? "";
}

/**
 * Click OK in the Customize dialog and return once it has closed. The chooser then shows the tag
 * that the dialog built.
 */
export async function okCustomizeDialog(page: Page): Promise<void> {
    await realClick(
        customizeDialog(page).getByTestId("customization-dialog-ok-button"),
    );
    await expect(customizeDialog(page)).toHaveCount(0);
}

/** Click Cancel in the Customize dialog and return once it has closed. */
export async function cancelCustomizeDialog(page: Page): Promise<void> {
    await realClick(
        customizeDialog(page).getByTestId("customization-dialog-cancel-button"),
    );
    await expect(customizeDialog(page)).toHaveCount(0);
}

/**
 * The parts of the open chooser that show a horizontal scroll bar: every element that may scroll
 * sideways (overflow-x auto or scroll) and holds more than its width. Each is named by its id, or
 * its tag and classes.
 */
export async function getHorizontallyScrollingParts(
    page: Page,
): Promise<string[]> {
    return languageChooserDialog(page).evaluate((dialog) =>
        [dialog, ...Array.from(dialog.querySelectorAll("*"))]
            .filter((element) => {
                const overflowX = getComputedStyle(element).overflowX;
                return (
                    (overflowX === "auto" || overflowX === "scroll") &&
                    element.scrollWidth > element.clientWidth + 1
                );
            })
            .map(
                (element) =>
                    element.id ||
                    `${element.tagName.toLowerCase()}.${element.className}`,
            ),
    );
}

/** Click the chooser's OK and return once it has closed. */
export async function okLanguageChooser(
    page: Page,
    labels: ILanguageChooserLabels = kEnglishLanguageChooserLabels,
): Promise<void> {
    const chooser = languageChooserDialog(page);
    const ok = chooser.getByRole("button", { name: labels.ok, exact: true });
    await expect(ok, "the chooser's OK is not enabled").toBeEnabled();
    await realClick(ok);
    await expect(chooser).toHaveCount(0);
}

/** Click the chooser's Cancel and return once it has closed. */
export async function cancelLanguageChooser(
    page: Page,
    labels: ILanguageChooserLabels = kEnglishLanguageChooserLabels,
): Promise<void> {
    const chooser = languageChooserDialog(page);
    await realClick(
        chooser.getByRole("button", { name: labels.cancel, exact: true }),
    );
    await expect(chooser).toHaveCount(0);
}

/** How the open chooser fits in Bloom's window, as getLanguageChooserFit reads it. */
export interface ILanguageChooserFit {
    /** The window's inside, in CSS pixels. */
    window: { width: number; height: number };
    /** The chooser's dialog box (its MUI paper), in CSS pixels from the window's top left. */
    dialog: { left: number; top: number; right: number; bottom: number };
    /**
     * The chooser's visible controls (buttons, text boxes, its title) that do not lie wholly
     * inside the dialog box, each named by its test id, id, or text. The language cards in the
     * search results are left out, since that list scrolls by design.
     */
    controlsCutOff: string[];
    /**
     * The parts of the chooser that hold more than they show, so that a scroll bar shows: every
     * element that may scroll (overflow auto or scroll) on either axis, named as above.
     */
    scrollingParts: string[];
}

/**
 * Read how the open chooser fits in Bloom's window: the window's size, the dialog's box, which of
 * its controls stick out of that box, and which of its parts scroll.
 */
export async function getLanguageChooserFit(
    page: Page,
): Promise<ILanguageChooserFit> {
    return languageChooserDialog(page).evaluate((dialog) => {
        const paper = dialog.querySelector(".MuiDialog-paper") ?? dialog;
        const box = paper.getBoundingClientRect();
        const name = (element: Element) =>
            element.getAttribute("data-testid") ||
            element.id ||
            (element as HTMLElement).innerText?.trim().slice(0, 40) ||
            `${element.tagName.toLowerCase()}.${element.className}`;
        const list = dialog.querySelector("#language-card-list");
        const controls = Array.from(
            dialog.querySelectorAll("button, input, textarea, h1"),
        ).filter(
            (element) =>
                !list?.contains(element) &&
                (element as HTMLElement).checkVisibility({
                    visibilityProperty: true,
                }),
        );
        const controlsCutOff = controls
            .filter((element) => {
                const r = element.getBoundingClientRect();
                return (
                    r.left < box.left - 0.5 ||
                    r.top < box.top - 0.5 ||
                    r.right > box.right + 0.5 ||
                    r.bottom > box.bottom + 0.5
                );
            })
            .map(name);
        const scrolls = (overflow: string) =>
            overflow === "auto" || overflow === "scroll";
        const scrollingParts = [
            dialog,
            ...Array.from(dialog.querySelectorAll("*")),
        ]
            .filter((element) => {
                const style = getComputedStyle(element);
                return (
                    (scrolls(style.overflowX) &&
                        element.scrollWidth > element.clientWidth + 1) ||
                    (scrolls(style.overflowY) &&
                        element.scrollHeight > element.clientHeight + 1)
                );
            })
            .map(name);
        return {
            window: { width: window.innerWidth, height: window.innerHeight },
            dialog: {
                left: box.left,
                top: box.top,
                right: box.right,
                bottom: box.bottom,
            },
            controlsCutOff,
            scrollingParts,
        };
    });
}

/** What happened when a tag was typed by hand into the chooser's prompt. */
export interface ITagTypedByHand {
    /** The prompt's question. */
    prompt: string;
    /** The messages of the alerts that followed, in order (none when the tag was taken). */
    alerts: string[];
}

/**
 * Type a language tag by hand, as a person does: with the Customize dialog open, Ctrl-click its
 * tag preview, which makes the chooser ask for a tag with the browser's own prompt, and answer it
 * with `tag`. Returns once the chooser has dealt with the answer: the Customize dialog has closed
 * (the tag was taken) or an alert has come (it was refused), so that the caller can tell which.
 */
export async function typeLanguageTagByHand(
    page: Page,
    tag: string,
): Promise<ITagTypedByHand> {
    const preview = customizeDialog(page).getByTestId(
        "customization-dialog-tag-preview",
    );
    await expect(preview, "the Customize dialog is not open").toBeVisible();
    const prompts: string[] = [];
    const alerts: string[] = [];
    const answer = async (dialog: import("@playwright/test").Dialog) => {
        if (dialog.type() === "prompt") {
            prompts.push(dialog.message());
            await dialog.accept(tag);
        } else {
            if (dialog.type() === "alert") alerts.push(dialog.message());
            await dialog.dismiss();
        }
    };
    page.on("dialog", answer);
    try {
        await page.keyboard.down("Control");
        try {
            await realClick(preview);
        } finally {
            await page.keyboard.up("Control");
        }
        await expect
            .poll(() => prompts.length, {
                message: "Ctrl-clicking the tag preview did not open a prompt",
            })
            .toBe(1);
        await expect
            .poll(
                async () =>
                    alerts.length > 0 ||
                    (await customizeDialog(page).count()) === 0,
                {
                    message:
                        "after the prompt was answered, the Customize dialog neither closed nor gave an alert",
                },
            )
            .toBe(true);
    } finally {
        page.off("dialog", answer);
    }
    return { prompt: prompts[0], alerts };
}

/** Press Escape in the open chooser and return once it has closed. */
export async function escapeLanguageChooser(page: Page): Promise<void> {
    const chooser = languageChooserDialog(page);
    await expect(chooser.getByTestId("search-bar")).toBeVisible();
    await chooser.locator("#search-bar").focus();
    await page.keyboard.press("Escape");
    await expect(chooser).toHaveCount(0);
}

/** Whether the Collection Settings dialog is open. */
export async function isSettingsDialogOpen(page: Page): Promise<boolean> {
    return (await settingsDialog(page).count()) === 1;
}
