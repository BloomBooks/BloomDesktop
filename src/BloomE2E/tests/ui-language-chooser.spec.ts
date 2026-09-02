// Changing Bloom's UI language from inside the Choose Collection dialog - the other place the
// language control lives, covering the startup half of the manual case "change UI language
// repeatedly" (Test Case ID 69).
//
// The dialog only really matters when Bloom starts with no collection to reopen: the test
// blanks the MRU list (backed up and restored by launchBloomIntoChooser), starts
// Bloom, and lands in the chooser. There it operates the real language menu and verifies the
// dialog's own strings change - the <h1> title (useL10n), the Create New Collection button (a
// BloomButton LocalizableElement), and the language button's label (C#-localized data) - then
// opens a collection and verifies the workspace comes up in the chosen language too.
//
// This file deliberately imports Playwright's own `test`, NOT ../fixtures/bloomTest: that
// fixture's automatic problem-dialog watcher depends on bloomApp, which launches Bloom on a
// collection - exactly what a chooser test must not have. launchBloomIntoChooser owns the whole
// lifecycle here, including restoring the developer's machine-wide settings file (MRU, UI
// language, everything) when it stops, even on failure.

import { test } from "@playwright/test";
import { launchBloomIntoChooser } from "../fixtures/launchBloom";
import {
    attachToChooser,
    chooseUiLanguageInChooser,
    expectChooserStrings,
    expectUiStrings,
    openCollectionFromChooser,
} from "../helpers/uiLanguage";

test("change UI language in the Choose Collection dialog [Test Case ID 69]", async ({}, testInfo) => {
    // A cold Bloom launch, a dialog rebuild, and a collection open, each with its own wait.
    testInfo.setTimeout(10 * 60 * 1000);

    const launched = await launchBloomIntoChooser({
        name: "ui-language-chooser",
        languages: ["en"],
    });
    let connection = await attachToChooser(launched.cdpPort);
    try {
        // The dialog in English first, so the French assertions below cannot pass for the
        // wrong reason.
        await expectChooserStrings(connection.page, {
            title: "Open / Create Collections",
            createButton: "Create New Collection",
            languageMenuButton: "English",
        });

        // Choose French in the dialog's own language menu. Bloom closes the dialog and opens
        // a new one so every string re-fetches; the dialog itself must come back in French.
        connection = await chooseUiLanguageInChooser(
            connection,
            launched.cdpPort,
            "fr",
        );
        await expectChooserStrings(connection.page, {
            title: "Ouvrir/Créer des collections",
            createButton: "Créer une nouvelle collection",
            languageMenuButton: "français",
        });

        // Open a collection from the chooser; the whole workspace must come up in French.
        // (No editTab entry: a fresh collection has no selected book, so the tab is hidden.)
        connection = await openCollectionFromChooser(
            connection,
            launched.cdpPort,
            launched.collectionToOpen,
        );
        await expectUiStrings(connection.page, {
            templates: "Modèles",
            sources: "Sources pour des nouveaux livres",
            basicBook: "Livre simple",
            languageMenuButton: "français",
        });
    } finally {
        await connection.browser.close().catch(() => undefined);
        // Kills Bloom and splices the developer's MRU and language settings back into the
        // settings file, reverting the MRU blanking and the language this test set.
        await launched.stop();
    }
});
