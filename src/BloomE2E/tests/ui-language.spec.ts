// Changing Bloom's UI language localizes the whole workspace - the journey test for the
// UI-language menu, automating the manual case "change UI language repeatedly" (Test Case ID 69).
//
// The test cycles the real menu through English -> French -> Spanish -> Turkish, and after each
// change verifies one visible string from each mechanism that puts localized text into the web
// UI (see IUiLanguageStrings in helpers/uiLanguage.ts): the useL10n hook, LocalizableElement
// class components, the top bar's <Span>, and C#-localized data delivered through the API. It
// then turns on "Show translations which have not been approved yet" and verifies the gate both
// ways, using Turkish: every string checked here has been translated into Turkish on Crowdin but
// never approved (no approved="yes" in DistFiles/localization/tr/Bloom.xlf), and those
// translations have not changed since January 2018, so they are safe to depend on. While the
// setting is off, the Turkish UI must fall back to English for these strings; once it is on, the
// Turkish text must appear.
//
// The UI language and the unapproved-translations setting are user settings, and this Bloom
// keeps its own (the launch fixture gives it a fresh user-settings folder), so the test changes
// them freely and has nothing to put back.
//
// Turkish must be in the language menu for the approved-only leg: dev/alpha builds list a
// language once >=1% of its strings are approved (Turkish has ~7%), which holds for any Bloom
// built from source. A release-channel build's 25% threshold would hide it.

import { expect, test } from "../fixtures/bloomTest";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import {
    chooseUiLanguage,
    expectUiStrings,
    getShowUnapprovedTranslations,
    getCurrentUiLanguageTag,
    setShowUnapprovedTranslations,
    setUiStateViaApi,
    type IUiLanguageStrings,
} from "../helpers/uiLanguage";
import { switchTab } from "../helpers/workspace";

// The collection's own content is irrelevant here: every string checked comes from the workspace
// shell or from the factory source collections, which any collection shows.
test.use({ collectionSpec: { name: "ui-language", languages: ["en"] } });

const stringsByLanguage: Record<string, IUiLanguageStrings> = {
    en: {
        templates: "Templates",
        sources: "Sources For New Books",
        editTab: "Edit",
        basicBook: "Basic Book",
        languageMenuButton: "English",
    },
    fr: {
        templates: "Modèles",
        sources: "Sources pour des nouveaux livres",
        editTab: "Éditer",
        basicBook: "Livre simple",
        languageMenuButton: "français",
    },
    es: {
        templates: "Plantillas",
        sources: "Fuentes para nuevos libros",
        editTab: "Editar",
        basicBook: "Libro básico",
        languageMenuButton: "español",
    },
    // Translated on Crowdin but never approved - see the header comment.
    tr: {
        templates: "Şablonlar",
        sources: "Yeni Kitaplar İçin Kaynaklar",
        editTab: "Düzenle",
        basicBook: "Temel Kitap",
        languageMenuButton: "Türkçe",
    },
};

test("change UI language repeatedly [Test Case ID 69]", async ({
    page,
    bloomApp,
}, testInfo) => {
    // Five project reopens, two full Bloom restarts, and making a book - plus the launch of
    // Bloom itself, which the worker fixture charges to this (first) test. Each helper has its
    // own tighter wait with a more specific message.
    testInfo.setTimeout(15 * 60 * 1000);

    // The assertions assume English with approved-only translations. A fresh Bloom has the
    // latter, but its UI language follows the operating system's. (No UI refresh is needed here:
    // if the language changed, the reopen refreshed everything.)
    if ((await getCurrentUiLanguageTag(page)) !== "en") {
        await setUiStateViaApi(bloomApp, "en", false);
    }
    // The top bar hides the Edit tab until a book is selected, and that tab's label is one of the
    // strings verified after every language change - so make a book, then come back to the
    // Collections tab, where the other checked strings live. (The selection survives the project
    // reopens below.)
    await makeBookFromTemplate(bloomApp.page, "Basic Book");
    await switchTab(bloomApp.page, "collection");
    // Sanity-check the starting state so the assertions below cannot pass for the wrong reason,
    // then verify the English strings before anything changes.
    expect(await getCurrentUiLanguageTag(bloomApp.page)).toBe("en");
    expect(await getShowUnapprovedTranslations(bloomApp.page)).toBe(false);
    await expectUiStrings(bloomApp.page, stringsByLanguage.en);

    await chooseUiLanguage(bloomApp, "fr");
    await expectUiStrings(bloomApp.page, stringsByLanguage.fr);

    await chooseUiLanguage(bloomApp, "es");
    await expectUiStrings(bloomApp.page, stringsByLanguage.es);

    // Turkish while unapproved translations are still hidden: Bloom is IN Turkish (the menu button
    // label proves it), but every string checked must fall back to English, because its Turkish
    // translation exists and is unapproved.
    await chooseUiLanguage(bloomApp, "tr");
    await expectUiStrings(bloomApp.page, {
        ...stringsByLanguage.en,
        languageMenuButton: stringsByLanguage.tr.languageMenuButton,
    });

    // Turn on "Show translations which have not been approved yet"; the same strings must now show
    // their unapproved Turkish text.
    await setShowUnapprovedTranslations(bloomApp, true);
    await expectUiStrings(bloomApp.page, stringsByLanguage.tr);

    // Back to English with the setting off again, through the same UI, which re-verifies both
    // directions of the gate.
    await chooseUiLanguage(bloomApp, "en");
    await setShowUnapprovedTranslations(bloomApp, false);
    await expectUiStrings(bloomApp.page, stringsByLanguage.en);
});
