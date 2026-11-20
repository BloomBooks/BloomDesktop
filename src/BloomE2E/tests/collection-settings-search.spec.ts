// Searching the new (React) Collection Settings dialog: a person types into the search box above
// the pages and the dialog shows only what matches, from every page at once. These tests check that
// each kind of text a person might remember leads to the setting: its label, its description, the
// title of the page it is on, the heading of the group it sits under, and one of the choices of a
// drop-down list. The checks are spread over top-level pages and the "More" page of a language,
// which is a page inside a group inside the Languages page.

import { expect, test } from "../fixtures/bloomTest";
import {
    cancelCollectionSettings,
    clearCollectionSettingsSearch,
    getCollectionSettingsSearchResults,
    getShowingCollectionSettingsPage,
    openCollectionSettings,
    searchCollectionSettings,
} from "../helpers/collectionSettings";

test.use({
    collectionSpec: { name: "settings-search", languages: ["en", "fr"] },
});

test.beforeEach(async ({ page }) => {
    await openCollectionSettings(page);
});

test.afterEach(async ({ page }) => {
    await cancelCollectionSettings(page);
});

// The settings the Places group of the Front & Back Matter page holds.
const kPlaceSettings = ["Country", "Province", "District"];

test("searching finds a setting by its label", async ({ page }) => {
    // On a top-level page.
    await searchCollectionSettings(page, "Collection Name");
    let results = await getCollectionSettingsSearchResults(page);
    expect(results.settings).toContain("Collection Name");
    expect(results.settings).not.toContain("Automatically Update Bloom");

    // On a language's More page, under the Languages page.
    await searchCollectionSettings(page, "right to left");
    results = await getCollectionSettingsSearchResults(page);
    expect(results.settings).toContain("This is a right to left script");
    expect(results.settings).not.toContain("Collection Name");
});

test("searching finds a setting by words in its description", async ({
    page,
}) => {
    // "When a new version of Bloom is available, Bloom will get it for you."
    await searchCollectionSettings(page, "will get it for you");
    let results = await getCollectionSettingsSearchResults(page);
    expect(results.settings).toContain("Automatically Update Bloom");
    expect(results.settings).not.toContain("Collection Name");

    // "Use this to increase the font size when this language is shown in places like the
    // Decodable Reader Tool."
    await searchCollectionSettings(page, "Decodable Reader Tool");
    results = await getCollectionSettingsSearchResults(page);
    expect(results.settings).toContain("Font size when displayed in tools");
    expect(results.settings).not.toContain("Default line spacing");
});

test("searching for a page's title shows the settings on that page", async ({
    page,
}) => {
    await searchCollectionSettings(page, "Front & Back Matter");
    const results = await getCollectionSettingsSearchResults(page);
    expect(results.pages).toContain("Front & Back Matter");
    expect(results.settings).toEqual(
        expect.arrayContaining(["Style", "Show QR Codes", ...kPlaceSettings]),
    );
    expect(results.settings).not.toContain("Collection Name");
});

test("searching for a group's heading shows the settings in that group", async ({
    page,
}) => {
    // A group on a top-level page.
    await searchCollectionSettings(page, "Places");
    let results = await getCollectionSettingsSearchResults(page);
    expect(results.groups).toContain("Places");
    expect(results.settings).toEqual(expect.arrayContaining(kPlaceSettings));
    expect(results.settings).not.toContain("Show QR Codes");

    // A group on a language's More page.
    await searchCollectionSettings(page, "Fonts");
    results = await getCollectionSettingsSearchResults(page);
    expect(results.groups).toContain("Fonts");
    expect(results.settings).toEqual(
        expect.arrayContaining([
            "Font size when displayed in tools",
            "Default line spacing",
        ]),
    );
    expect(results.settings).not.toContain("This is a right to left script");
});

test("searching finds a drop-down list by one of its choices", async ({
    page,
}) => {
    // A front/back matter pack, on the Front & Back Matter page. The collection uses Paper Saver,
    // so this is a choice the list is not showing.
    await searchCollectionSettings(page, "Traditional");
    let results = await getCollectionSettingsSearchResults(page);
    expect(results.settings).toContain("Style");
    expect(results.settings).not.toContain("Country");

    // A page numbering style, on Language 1's More page.
    await searchCollectionSettings(page, "Gurmukhi");
    results = await getCollectionSettingsSearchResults(page);
    expect(results.settings).toContain("Page Numbering Style");
    expect(results.settings).not.toContain("Default line spacing");
});

// The Subscription page's introduction is Markdown in a ConfigrStatic, not a Config-R setting.
// Config-R searches the text written directly in such an element, so it finds this.
test("searching finds text on a page that is not a Config-R setting", async ({
    page,
}) => {
    await searchCollectionSettings(page, "advanced features");
    const results = await getCollectionSettingsSearchResults(page);
    expect(results.pages).toEqual(["Subscription"]);
    expect(results.text).toContain("we offer advanced features");
});

test("searching ignores upper and lower case", async ({ page }) => {
    await searchCollectionSettings(page, "cOLLECTION nAME");
    const results = await getCollectionSettingsSearchResults(page);
    expect(results.settings).toContain("Collection Name");
});

test("a search that matches nothing shows no settings", async ({ page }) => {
    await searchCollectionSettings(page, "xyzzy");
    const results = await getCollectionSettingsSearchResults(page);
    expect(results).toMatchObject({ pages: [], groups: [], settings: [] });
    expect(results.text).toContain("No matches");
});

test("clearing the search shows the pages again", async ({ page }) => {
    // Sanity check: the dialog opens on the Languages page.
    expect(await getShowingCollectionSettingsPage(page)).toBe("Languages");
    const languagesPage = await getCollectionSettingsSearchResults(page);
    expect(languagesPage.groups).toContain("Written Language 1");

    await searchCollectionSettings(page, "Collection Name");
    expect(
        (await getCollectionSettingsSearchResults(page)).groups,
    ).not.toContain("Written Language 1");

    await clearCollectionSettingsSearch(page);
    expect(await getShowingCollectionSettingsPage(page)).toBe("Languages");
    const afterClearing = await getCollectionSettingsSearchResults(page);
    expect(afterClearing.settings).toEqual(languagesPage.settings);
    expect(afterClearing.groups).toEqual(languagesPage.groups);
});
