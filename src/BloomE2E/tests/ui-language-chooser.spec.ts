// Changing Bloom's UI language from inside the Choose Collection dialog - the other place the
// language control lives, covering the startup half of the manual case "change UI language
// repeatedly" (Test Case ID 69).
//
// The dialog only really matters when Bloom starts with no collection to reopen, so this file
// sets startAtChooser: the fixture blanks the MRU list (touching the developer's machine-wide
// user.config - backed up and spliced back afterward; see launchBloomIntoChooser) and Bloom
// lands in the chooser. There the test operates the real language menu and verifies the
// dialog's own strings change - the <h1> title (useL10n), the Create New Collection button (a
// BloomButton LocalizableElement), and the language button's label (C#-localized data) - then
// opens a collection and verifies the whole workspace comes up in the chosen language too.

import { test } from "../fixtures/bloomTest";
import {
    chooseUiLanguageInChooser,
    expectChooserStrings,
    expectUiStrings,
    openCollectionFromChooser,
} from "../helpers/uiLanguage";

test.use({
    startAtChooser: true,
    // The collection the test opens FROM the dialog; its content is irrelevant here.
    collectionSpec: { name: "ui-language-chooser", languages: ["en"] },
});

test("change UI language in the Choose Collection dialog [Test Case ID 69]", async ({
    chooserApp,
}, testInfo) => {
    // A cold Bloom launch (charged to this first test), a dialog rebuild, and a collection
    // open, each with its own tighter wait.
    testInfo.setTimeout(10 * 60 * 1000);

    // The dialog in English first, so the French assertions below cannot pass for the wrong
    // reason. (The fixture's launch normalized the profile to English.)
    await expectChooserStrings(chooserApp.page, {
        title: "Open / Create Collections",
        createButton: "Create New Collection",
        languageMenuButton: "English",
    });

    // Choose French in the dialog's own language menu. Bloom closes the dialog and opens a new
    // one so every string re-fetches; the dialog itself must come back in French.
    await chooseUiLanguageInChooser(chooserApp, "fr");
    await expectChooserStrings(chooserApp.page, {
        title: "Ouvrir/Créer des collections",
        createButton: "Créer une nouvelle collection",
        languageMenuButton: "français",
    });

    // Open a collection from the chooser; the whole workspace must come up in French.
    // (No editTab entry: a fresh collection has no selected book, so the tab is hidden.)
    await openCollectionFromChooser(chooserApp);
    await expectUiStrings(chooserApp.page, {
        templates: "Modèles",
        sources: "Sources pour des nouveaux livres",
        basicBook: "Livre simple",
        languageMenuButton: "français",
    });
});
