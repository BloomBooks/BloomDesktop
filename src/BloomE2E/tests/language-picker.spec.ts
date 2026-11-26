// The language chooser (EthnoLib's LanguageChooser in Bloom's LanguageChooserDialog), opened from
// the Languages page of the new Collection Settings dialog: how it opens and cancels, its search,
// its script variants, its Customize dialog, the right-to-left setting it gives each language, and
// its strings in another UI language.
//
// The card's route "from Create New Collection" is not here: the New Collection wizard is a
// WinForms surface that CDP cannot reach (AUTOMATION-DEBT.md, "WinForms surfaces are invisible to
// CDP").
//
// One Bloom serves every test in this file, so every test but the last two leaves the collection's
// settings as it found them, by cancelling the Settings dialog.

import { expect, test } from "../fixtures/bloomTest";
import {
    cancelCollectionSettings,
    closeCollectionSettingsLanguageMore,
    getCollectionSettingsCheckbox,
    getCollectionSettingsLanguage,
    getCollectionSettingsOkLabel,
    openCollectionSettingsLanguageMore,
    readSavedLanguageSetting,
    restartFromCollectionSettings,
    saveCollectionSettings,
} from "../helpers/collectionSettings";
import {
    cancelCustomizeDialog,
    cancelLanguageChooser,
    cancelSettingsDialog,
    chooseCustomizeCountry,
    chooseCustomizeScript,
    chooseLanguage,
    chooseScript,
    clearLanguageSearch,
    customizeButton,
    getChosenLanguageTag,
    getCustomizeTag,
    getHorizontallyScrollingParts,
    getLanguageChooserLabels,
    getLanguageChooserTitle,
    getLanguageSearchText,
    getListedLanguageCodes,
    getOfferedScriptCodes,
    getScriptCardText,
    kEnglishLanguageChooserLabels,
    okCustomizeDialog,
    okLanguageChooser,
    openCustomizeDialog,
    openLanguageChooser,
    openLanguagesPage,
    scrollToLanguage,
    searchForLanguage,
    typeCustomizeDialect,
} from "../helpers/languagePicker";
import { chooseUiLanguage } from "../helpers/uiLanguage";
import type { Page } from "@playwright/test";

const kLanguage1 = kEnglishLanguageChooserLabels.language1;
const kLanguage2 = kEnglishLanguageChooserLabels.language2;
const kLanguage3 = kEnglishLanguageChooserLabels.language3;
const kRightToLeft = "This is a right to left script";

test.use({
    collectionSpec: { name: "language-picker", languages: ["en", "fr"] },
});

/**
 * Pick a language (and a script, when given) in the chooser for one language of the collection,
 * press OK, and return whether that language's More page then has "This is a right to left script"
 * turned on. Leaves the Languages page showing.
 */
async function chooseAndReadRightToLeft(
    page: Page,
    heading: string,
    button: "change" | "set",
    search: string,
    languageCode: string,
    scriptCode?: string,
): Promise<boolean> {
    await openLanguageChooser(page, heading, button);
    await searchForLanguage(page, search);
    await chooseLanguage(page, languageCode);
    if (scriptCode) await chooseScript(page, scriptCode);
    await okLanguageChooser(page);
    await openCollectionSettingsLanguageMore(page, heading);
    const { checked } = await getCollectionSettingsCheckbox(page, kRightToLeft);
    await closeCollectionSettingsLanguageMore(page);
    return checked;
}

test("opens the language chooser from Change... and Set..., and Cancel changes nothing [Test Case ID 761]", async ({
    page,
}) => {
    await openLanguagesPage(page);
    expect(await getCollectionSettingsLanguage(page, kLanguage1)).toBe(
        "English (en)",
    );
    expect(await getCollectionSettingsLanguage(page, kLanguage3)).toBe("");

    await openLanguageChooser(page, kLanguage1, "change");
    // The chooser starts on the language the slot has.
    await expect.poll(() => getChosenLanguageTag(page)).toBe("en");
    await searchForLanguage(page, "tok pisin");
    await chooseLanguage(page, "tpi");
    await cancelLanguageChooser(page);
    expect(await getCollectionSettingsLanguage(page, kLanguage1)).toBe(
        "English (en)",
    );

    await openLanguageChooser(page, kLanguage3, "set");
    expect(await getLanguageSearchText(page)).toBe("");
    await searchForLanguage(page, "tok pisin");
    await chooseLanguage(page, "tpi");
    await cancelLanguageChooser(page);
    expect(await getCollectionSettingsLanguage(page, kLanguage3)).toBe("");

    // OK, by contrast, puts the language in the slot.
    await openLanguageChooser(page, kLanguage3, "set");
    await searchForLanguage(page, "tok pisin");
    await chooseLanguage(page, "tpi");
    await okLanguageChooser(page);
    expect(await getCollectionSettingsLanguage(page, kLanguage3)).toBe(
        "Tok Pisin (tpi)",
    );

    await cancelCollectionSettings(page);
});

test("finds a language by name, by code and by country, the X clears the search, and nothing scrolls sideways [Test Case ID 761]", async ({
    page,
}) => {
    await openLanguagesPage(page);
    await openLanguageChooser(page, kLanguage3, "set");
    expect(await getHorizontallyScrollingParts(page)).toEqual([]);

    // The results of a search replace the earlier ones a moment after the typing, so each check of
    // the first result waits for it.
    const firstListedLanguage = async () =>
        (await getListedLanguageCodes(page))[0];

    await searchForLanguage(page, "tok pisin");
    await expect.poll(firstListedLanguage).toBe("tpi");
    expect(await getHorizontallyScrollingParts(page)).toEqual([]);

    // Icelandic is the language of Iceland, so a search for the country lists it.
    await searchForLanguage(page, "Iceland");
    await scrollToLanguage(page, "isl");
    expect(await getHorizontallyScrollingParts(page)).toEqual([]);

    // The search by code comes after a search that does not list Tok Pisin first, so that the
    // name search's results cannot pass for its own.
    await expect.poll(firstListedLanguage).not.toBe("tpi");
    await searchForLanguage(page, "tpi");
    await expect.poll(firstListedLanguage).toBe("tpi");
    expect(await getHorizontallyScrollingParts(page)).toEqual([]);

    await clearLanguageSearch(page);
    expect(await getHorizontallyScrollingParts(page)).toEqual([]);

    await cancelLanguageChooser(page);
    await cancelCollectionSettings(page);
});

test("gives Ladino a different tag for each of its three scripts [Test Case ID 761]", async ({
    page,
}) => {
    await openLanguagesPage(page);
    await openLanguageChooser(page, kLanguage3, "set");
    await searchForLanguage(page, "ladino");
    await chooseLanguage(page, "lad");
    expect(await getOfferedScriptCodes(page)).toEqual(
        expect.arrayContaining(["Hebr", "Cyrl", "Latn"]),
    );

    await chooseScript(page, "Hebr");
    await expect.poll(() => getChosenLanguageTag(page)).toBe("lad");
    await chooseScript(page, "Cyrl");
    await expect.poll(() => getChosenLanguageTag(page)).toBe("lad-Cyrl");
    await chooseScript(page, "Latn");
    await expect.poll(() => getChosenLanguageTag(page)).toBe("lad-Latn");

    await cancelLanguageChooser(page);
    await cancelCollectionSettings(page);
});

test("lists Cree first, then the Cree macrolanguage, then other Cree languages, then other names that match [Test Case ID 761]", async ({
    page,
}) => {
    await openLanguagesPage(page);
    await openLanguageChooser(page, kLanguage3, "set");
    await searchForLanguage(page, "cree");

    const top = await getListedLanguageCodes(page);
    expect(top[0], "the first entry is Cree (Woods Cree)").toBe("cwd");
    // A card is identified by its three-letter code; the macrolanguage's tag, which the card
    // shows, is "cr".
    expect(top[1], "the second entry is the Cree macrolanguage").toBe("cre");

    // Muscogee (Creek), "Mvskoke" in its own name, matches only by a part of an alternative name,
    // so it comes after the languages with Cree in their name, such as Moose Cree and Swampy Cree.
    await scrollToLanguage(page, "mus");
    const listed = await getListedLanguageCodes(page);
    const position = (code: string) => {
        const index = listed.indexOf(code);
        expect(index, `${code} is listed`).toBeGreaterThanOrEqual(0);
        return index;
    };
    expect(position("crm")).toBeGreaterThan(1);
    expect(position("csw")).toBeGreaterThan(1);
    expect(position("mus")).toBeGreaterThan(position("crm"));
    expect(position("mus")).toBeGreaterThan(position("csw"));

    await cancelLanguageChooser(page);
    await cancelCollectionSettings(page);
});

test("builds the full tag from the script, country and dialect chosen in Customize [Test Case ID 761]", async ({
    page,
}) => {
    await openLanguagesPage(page);
    await openLanguageChooser(page, kLanguage3, "set");

    await searchForLanguage(page, "spanish");
    await chooseLanguage(page, "spa");
    await openCustomizeDialog(page);
    await chooseCustomizeScript(page, /^Arabic/);
    await chooseCustomizeCountry(page, /^Andorra/);
    await typeCustomizeDialect(page, "foo");
    await expect.poll(() => getCustomizeTag(page)).toBe("es-Arab-AD-x-foo");
    // Cancel in Customize leaves the chooser with the language as it was.
    await cancelCustomizeDialog(page);
    await expect.poll(() => getChosenLanguageTag(page)).toBe("es");

    // OK in Customize gives the whole tag, with its script, country and dialect, to the chooser,
    // and OK there gives it to the collection.
    await openCustomizeDialog(page);
    await chooseCustomizeScript(page, /^Arabic/);
    await chooseCustomizeCountry(page, /^Andorra/);
    await typeCustomizeDialect(page, "foo");
    await expect.poll(() => getCustomizeTag(page)).toBe("es-Arab-AD-x-foo");
    await okCustomizeDialog(page);
    await expect
        .poll(() => getChosenLanguageTag(page))
        .toBe("es-Arab-AD-x-foo");
    await okLanguageChooser(page);
    expect(await getCollectionSettingsLanguage(page, kLanguage3)).toBe(
        // The chooser names Spanish in Spanish.
        "español (es-Arab-AD-x-foo)",
    );

    await openLanguageChooser(page, kLanguage3, "change");
    await searchForLanguage(page, "tok pisin");
    await chooseLanguage(page, "tpi");
    await openCustomizeDialog(page);
    // Exactly "Latin": the list also has variants such as "Latin (Fraktur variant)".
    await chooseCustomizeScript(page, /^Latin$/);
    await chooseCustomizeCountry(page, /^Papua New Guinea/);
    await typeCustomizeDialect(page, "foo");
    // Latin script and Papua New Guinea are what Tok Pisin has anyway, so the tag leaves them out.
    await expect.poll(() => getCustomizeTag(page)).toBe("tpi-x-foo");

    await okCustomizeDialog(page);
    await expect.poll(() => getChosenLanguageTag(page)).toBe("tpi-x-foo");
    await okLanguageChooser(page);
    expect(await getCollectionSettingsLanguage(page, kLanguage3)).toBe(
        "Tok Pisin (tpi-x-foo)",
    );

    await cancelCollectionSettings(page);
});

test("turns right to left on for a right-to-left script and off for the others, following each change of script [Test Case ID 761]", async ({
    page,
}) => {
    await openLanguagesPage(page);

    expect(
        await chooseAndReadRightToLeft(
            page,
            kLanguage1,
            "change",
            "arabic",
            "arb",
            "Arab",
        ),
        "Arabic in Arabic script",
    ).toBe(true);
    expect(
        await chooseAndReadRightToLeft(
            page,
            kLanguage1,
            "change",
            "hebrew",
            "heb",
            "Hebr",
        ),
        "Hebrew in Hebrew script",
    ).toBe(true);
    expect(
        await chooseAndReadRightToLeft(
            page,
            kLanguage1,
            "change",
            "english",
            "eng",
        ),
        "English",
    ).toBe(false);
    expect(
        await chooseAndReadRightToLeft(
            page,
            kLanguage1,
            "change",
            "french",
            "fra",
        ),
        "French",
    ).toBe(false);

    // Albanian offers several scripts, none of them right to left. Each must turn the setting off
    // right after Hebrew turned it on: the Languages page keeps the setting a language had when the
    // chooser does not say which way a script runs, so a script chosen after another Albanian one
    // would show it off whatever the chooser said.
    await openLanguageChooser(page, kLanguage1, "change");
    await searchForLanguage(page, "albanian");
    // "als": the language the chooser calls Albanian, not "sq", the macrolanguage, which offers
    // no scripts.
    await chooseLanguage(page, "als");
    const albanianScripts = await getOfferedScriptCodes(page);
    expect(
        albanianScripts.length,
        "Albanian offers several scripts",
    ).toBeGreaterThan(1);
    await cancelLanguageChooser(page);
    for (const script of albanianScripts) {
        expect(
            await chooseAndReadRightToLeft(
                page,
                kLanguage1,
                "change",
                "hebrew",
                "heb",
                "Hebr",
            ),
            `Hebrew, before Albanian in script ${script}`,
        ).toBe(true);
        expect(
            await chooseAndReadRightToLeft(
                page,
                kLanguage1,
                "change",
                "albanian",
                "als",
                script,
            ),
            `Albanian in script ${script}`,
        ).toBe(false);
    }

    // Western Neo-Aramaic in its Western Syriac script is right to left.
    await openLanguageChooser(page, kLanguage1, "change");
    await searchForLanguage(page, "western neo-aramaic");
    await chooseLanguage(page, "amw");
    const scripts = await getOfferedScriptCodes(page);
    const westernSyriac: string[] = [];
    for (const script of scripts)
        if (
            (await getScriptCardText(page, script)).includes(
                "Syriac (Western variant)",
            )
        )
            westernSyriac.push(script);
    expect(
        westernSyriac,
        "Western Neo-Aramaic offers Syriac (Western variant)",
    ).toHaveLength(1);
    await cancelLanguageChooser(page);
    expect(
        await chooseAndReadRightToLeft(
            page,
            kLanguage1,
            "change",
            "western neo-aramaic",
            "amw",
            westernSyriac[0],
        ),
        "Western Neo-Aramaic in Syriac (Western variant)",
    ).toBe(true);

    await cancelCollectionSettings(page);
});

test("sets right to left for each language of the collection separately, and saves it [Test Case ID 761]", async ({
    page,
    bloomApp,
}) => {
    await openLanguagesPage(page);
    expect(
        await chooseAndReadRightToLeft(
            page,
            kLanguage2,
            "change",
            "arabic",
            "arb",
            "Arab",
        ),
        "Language 2 in Arabic",
    ).toBe(true);
    // Hebrew, not Arabic again: a collection does not keep one language in two slots.
    expect(
        await chooseAndReadRightToLeft(
            page,
            kLanguage3,
            "set",
            "hebrew",
            "heb",
            "Hebr",
        ),
        "Language 3 in Hebrew",
    ).toBe(true);
    await openCollectionSettingsLanguageMore(page, kLanguage1);
    expect(
        (await getCollectionSettingsCheckbox(page, kRightToLeft)).checked,
        "Language 1 is still English",
    ).toBe(false);
    await closeCollectionSettingsLanguageMore(page);

    if ((await getCollectionSettingsOkLabel(page)) === "Restart")
        await restartFromCollectionSettings(bloomApp);
    else await saveCollectionSettings(page);

    const dir = bloomApp.collectionDir;
    expect(readSavedLanguageSetting(dir, 1, "IsLanguageRtl")).toBe("false");
    expect(readSavedLanguageSetting(dir, 2, "IsLanguageRtl")).toBe("true");
    expect(readSavedLanguageSetting(dir, 3, "IsLanguageRtl")).toBe("true");
});

test("shows the language chooser in the UI language, and it still finds languages [Test Case ID 761]", async ({
    bloomApp,
}) => {
    const page = await chooseUiLanguage(bloomApp, "fr");
    const french = await getLanguageChooserLabels(page);
    expect(
        french.settings,
        "sanity check: Bloom has a French translation of Settings",
    ).not.toBe(kEnglishLanguageChooserLabels.settings);

    await openLanguagesPage(page, french);
    await openLanguageChooser(page, french.language2, "change", french);

    // Bloom's own title over the chooser, and EthnoLib's own strings inside it.
    // The title arrives in French a moment after the chooser shows, so wait for it.
    await expect.poll(() => getLanguageChooserTitle(page)).toBe(french.title);
    await searchForLanguage(page, "tok pisin");
    await chooseLanguage(page, "tpi");
    await expect(customizeButton(page)).toContainText("Personnaliser");
    await expect(customizeButton(page)).not.toContainText("Customize");

    await cancelLanguageChooser(page, french);
    await cancelSettingsDialog(page, french);
    await chooseUiLanguage(bloomApp, "en");
});
