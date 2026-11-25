// A name of the user's own for the collection's first language, given in Collection Settings, is
// the name Bloom shows for that language: on a new book's front cover, and on the Collection tab's
// banner that counts the language's books on Bloom Library. The name is the manual case's, with
// characters that a file name cannot hold.
//
// Test Case ID 156, "Custom Language Name Display". What stays manual: the file name that Save
// PDF... proposes, which only the operating system's Save As dialog shows. Under automation that
// dialog never appeared (see the pull request that added this test).

import { expect, test } from "../fixtures/bloomTest";
import { getUiString } from "../helpers/api";
import { editablePageFrame, makeBookFromTemplate } from "../helpers/bookMaking";
import {
    getCollectionSettingsLanguage,
    openCollectionSettings,
    openCollectionSettingsLanguageChooser,
    readSavedLanguageSetting,
    restartFromCollectionSettings,
    showCollectionSettingsPage,
} from "../helpers/collectionSettings";
import {
    confirmLanguageChooser,
    setLanguageChooserName,
} from "../helpers/languageChooser";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "custom-language-name",
        languages: ["fr", "en"],
        // No sign language, as a new collection has: the banner names the sign language when the
        // collection has one, and a collection file that says nothing of it gets English.
        signLanguage: "",
    },
});

// The name the manual case gives, with a question mark, a slash, a backslash and a double quote.
const kCustomName = '!?/\\"foo';

test("a custom name for the first language shows on the front cover and on the Bloom Library banner [Test Case ID 156]", async ({
    page,
    bloomApp,
    step,
}) => {
    test.setTimeout(300000);

    await step(
        "Give the first language a name of the user's own in Collection Settings",
        async () => {
            // A test Bloom starts in the Windows UI language, so find the page and the group by
            // the names it gives them in that language.
            const languagesPage = await getUiString(
                page,
                "CollectionSettingsDialog.LanguageTab.LanguageTabLabel",
                "Languages",
            );
            const language1 = await getUiString(
                page,
                "CollectionSettingsDialog.LanguagesPage.WrittenLanguage1",
                "Written Language 1",
            );
            await openCollectionSettings(page);
            await showCollectionSettingsPage(page, languagesPage);
            expect(
                await getCollectionSettingsLanguage(page, language1),
                "sanity check: the collection starts in French",
            ).toMatch(/\(fr\)$/);
            await openCollectionSettingsLanguageChooser(page, language1);
            await setLanguageChooserName(page, kCustomName);
            await confirmLanguageChooser(page);
            expect(await getCollectionSettingsLanguage(page, language1)).toBe(
                `${kCustomName} (fr)`,
            );
        },
    );

    // A step returns undefined only when it is marked fixme, which these are not.
    const reopened = (await step(
        "Click Restart, so Bloom saves the name and reopens the collection",
        () => restartFromCollectionSettings(bloomApp),
    ))!;
    expect(
        readSavedLanguageSetting(bloomApp.collectionDir, 1, "LanguageName"),
        "sanity check: Bloom saved the custom name",
    ).toBe(kCustomName);

    await step("Make a Basic Book", () =>
        makeBookFromTemplate(reopened, "Basic Book"),
    );

    await step(
        "The front cover names the language by the custom name",
        async () => {
            await expect(
                editablePageFrame(reopened).locator(
                    ".bloom-page.outsideFrontCover .coverBottomLangName",
                ),
            ).toHaveText(kCustomName);
        },
    );

    // Last, since it needs Bloom Library: Bloom asks it for the language's count of books.
    await step(
        "The Bloom Library banner on the Collection tab names the language by the custom name",
        async () => {
            await switchTab(reopened, "collection");
            const banner = reopened.getByTestId("books-on-blorg-progress");
            const bar = banner.getByRole("progressbar");
            // The banner keeps its label clipped out of sight until it has the language's count of
            // books from Bloom Library, its goal and the language's name; then it sets the bar's
            // percentage.
            await expect
                .poll(
                    async () => Number(await bar.getAttribute("aria-valuenow")),
                    {
                        message:
                            "The banner never got the language's count of books from Bloom Library; is it reachable?",
                        timeout: 60000,
                    },
                )
                .toBeGreaterThanOrEqual(0);
            await expect(
                banner.getByTestId("books-on-blorg-count"),
            ).toContainText(kCustomName);
        },
    );
});
