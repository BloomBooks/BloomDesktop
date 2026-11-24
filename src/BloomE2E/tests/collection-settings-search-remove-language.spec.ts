// Removing the third language from the search results of the Collection Settings dialog. The
// results show the language's settings from its More page beside its Remove button, which the
// Languages page never does, so this is a path of its own: the language must stay removed and
// be saved that way.

import { expect, test } from "../fixtures/bloomTest";
import {
    cancelCollectionSettings,
    clearCollectionSettingsSearch,
    getCollectionSettingsLanguage,
    getCollectionSettingsSearchResults,
    openCollectionSettings,
    removeCollectionSettingsLanguage,
    restartFromCollectionSettings,
    searchCollectionSettings,
    showCollectionSettingsPage,
} from "../helpers/collectionSettings";

const kLanguage3 = "Written Language 3";

test.use({
    collectionSpec: {
        name: "search-remove-language",
        languages: ["en", "fr", "es"],
    },
});

test("a third language removed in the search results stays removed and is saved", async ({
    page,
    bloomApp,
}) => {
    await openCollectionSettings(page);
    await showCollectionSettingsPage(page, "Languages");
    expect(
        await getCollectionSettingsLanguage(page, kLanguage3),
        "sanity check: the collection starts with a third language",
    ).not.toBe("");

    await searchCollectionSettings(page, kLanguage3);
    expect(
        (await getCollectionSettingsSearchResults(page)).settings,
        "the results show the language's checkboxes from its More page beside Remove",
    ).toContain("This is a right to left script");
    await removeCollectionSettingsLanguage(page, kLanguage3);

    await clearCollectionSettingsSearch(page);
    expect(await getCollectionSettingsLanguage(page, kLanguage3)).toBe("");

    // Removing a language needs a restart.
    const reopened = await restartFromCollectionSettings(bloomApp);
    await openCollectionSettings(reopened);
    await showCollectionSettingsPage(reopened, "Languages");
    expect(await getCollectionSettingsLanguage(reopened, kLanguage3)).toBe("");
    await cancelCollectionSettings(reopened);
});
