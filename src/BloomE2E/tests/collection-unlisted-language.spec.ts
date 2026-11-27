// A collection whose language is not in the language chooser's list: the chooser's "Create
// Unlisted Language" builds a private-use tag from the name and country typed, qaa-<country>-x-<name>
// (e.g. qaa-PG-x-Whatcham), and that tag must reach the collection's settings file, the books made
// in it, and what Bloom publishes from them.
//
// Automates the manual test "Collection With Unlisted Language" (Test Case ID 114). The case starts
// a new collection and makes the language there, in the New Collection wizard. That wizard is a
// WinForms dialog that the suite cannot open (AUTOMATION-DEBT.md, "WinForms surfaces are invisible
// to CDP"), so its steps "Start a new collection" and "Finish the wizard... with no crash" are not
// checked here. The wizard shows the same language chooser that the Languages page of Collection
// Settings opens, so this test makes the unlisted language there, for the collection's first
// language, and checks the rest of the case from that point on.

import type { Page } from "@playwright/test";
import * as fs from "fs";
import * as Path from "path";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import { bookHtmlPath } from "../helpers/bookHtml";
import {
    cancelCollectionSettings,
    getCollectionSettingsLanguage,
    kEnterpriseSubscriptionCode,
    openCollectionSettings,
    openCollectionSettingsLanguageChooser,
    readSavedLanguageSetting,
    restartFromCollectionSettings,
    showCollectionSettingsPage,
} from "../helpers/collectionSettings";
import {
    getEpubPackageDublinCore,
    openEpubPublishScreen,
    readEpubPreviewPackage,
    showEpubPreview,
} from "../helpers/epubPublish";
import {
    cancelLanguageChooser,
    confirmLanguageChooser,
    getLanguageChooserSelection,
} from "../helpers/languageChooser";
import {
    chooseCustomizeCountry,
    customizeButton,
    getCustomizeTag,
    okCustomizeDialog,
    searchForUnlistedLanguage,
    typeUnlistedLanguageName,
} from "../helpers/languagePicker";
import {
    getLanguagesInBook,
    selectPublishDestination,
    stageBloomPub,
} from "../helpers/publish";
import { realClick } from "../helpers/realClick";
import { setUiStateViaApi } from "../helpers/uiLanguage";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "unlisted-language",
        languages: ["en"],
        // Publishing an ePUB needs Pro.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const kLanguage1 = "Written Language 1";
// The case's own example name. It matches no language the chooser knows.
const kName = "Whatcham";
const kCountry = "Papua New Guinea";
const kTag = "qaa-PG-x-Whatcham";
const kText = "Whatcham words on a page.";

test("an unlisted language gets the tag qaa-<country>-x-<name>, which the collection, a book in it and its publications keep [Test Case ID 114]", async ({
    page,
    bloomApp,
    step,
}) => {
    test.setTimeout(420000);

    await step(
        "On the Languages page of Collection Settings, open the chooser for the first language",
        async () => {
            // The helpers find the Settings dialog's pages and groups, and the chooser's
            // countries, by their English names.
            await setUiStateViaApi(bloomApp, "en", false);
            await openCollectionSettings(page);
            await showCollectionSettingsPage(page, "Languages");
            expect(
                await getCollectionSettingsLanguage(page, kLanguage1),
                "sanity check: the collection starts in English",
            ).toBe("English (en)");
            await openCollectionSettingsLanguageChooser(page, kLanguage1);
        },
    );

    await step(
        `Type "${kName}", which matches no language, and click Create Unlisted Language`,
        async () => {
            // With no language selected, the button that customizes one is Create Unlisted
            // Language instead, and its dialog asks for the language's name.
            await searchForUnlistedLanguage(page, kName);
            await realClick(customizeButton(page));
        },
    );

    await step(
        `Fill in the name and country (${kCountry}), and click OK`,
        async () => {
            await typeUnlistedLanguageName(page, kName);
            await chooseCustomizeCountry(page, new RegExp(`^${kCountry}$`));
            expect(await getCustomizeTag(page)).toBe(kTag);
            await okCustomizeDialog(page);
            expect(await getLanguageChooserSelection(page)).toEqual({
                language: undefined,
                script: undefined,
                name: kName,
                tag: kTag,
            });
            await confirmLanguageChooser(page);
            expect(await getCollectionSettingsLanguage(page, kLanguage1)).toBe(
                `${kName} (${kTag})`,
            );
        },
    );

    // A step returns undefined only when it is marked fixme, which these are not.
    const reopened = (await step(
        "Click Restart, so Bloom saves the language and reopens the collection",
        () => restartFromCollectionSettings(bloomApp),
    ))!;

    await step(
        "Check the .bloomCollection file has the tag and the name, and Bloom still shows them",
        async () => {
            expect(
                readSavedLanguageSetting(
                    bloomApp.collectionDir,
                    1,
                    "LanguageIso639Code",
                ),
            ).toBe(kTag);
            expect(
                readSavedLanguageSetting(
                    bloomApp.collectionDir,
                    1,
                    "LanguageName",
                ),
            ).toBe(kName);
            await openCollectionSettings(reopened);
            await showCollectionSettingsPage(reopened, "Languages");
            expect(
                await getCollectionSettingsLanguage(reopened, kLanguage1),
            ).toBe(`${kName} (${kTag})`);
            await openCollectionSettingsLanguageChooser(reopened, kLanguage1);
            expect(await getLanguageChooserSelection(reopened)).toEqual({
                language: undefined,
                script: undefined,
                name: kName,
                tag: kTag,
            });
            await cancelLanguageChooser(reopened);
            await cancelCollectionSettings(reopened);
        },
    );

    await step(
        "Make a book, type in its first-language box, and check its HTML carries the tag",
        async () => {
            // The book gets no title, so its folder keeps the name it was made with.
            const folder = await makeBookFromTemplate(reopened, "Basic Book");
            await addPage(reopened, "Just Text");
            await goToPage(reopened, (await getContentPages(reopened))[0].id);
            await typeInGroup(reopened, ".bloom-translationGroup", kTag, kText);
            // Leaving the Edit tab saves the page.
            await switchTab(reopened, "publish");
            await expect
                .poll(
                    async () =>
                        readLanguageMarks(
                            reopened,
                            fs.readFileSync(bookHtmlPath(folder), "utf8"),
                        ),
                    { message: "the book's HTML on disk", timeout: 30000 },
                )
                .toEqual({ contentLanguage1: kTag, langOfText: [kTag] });
        },
    );

    await step(
        "Publish it as a BloomPUB, and check the tag travels with the text",
        async () => {
            await selectPublishDestination(reopened, "BloomPUB");
            // Bloom names a language whose name was typed by that name, then the standard one in
            // brackets: here "Whatcham (Language Not Listed (qaa))".
            const languages = await getLanguagesInBook(reopened);
            expect(
                languages.find((l) => l.code === kTag)?.name,
                `the Publish tab's languages: ${JSON.stringify(languages)}`,
            ).toMatch(new RegExp(`^${kName} \\(`));
            const staged = await stageBloomPub(reopened);
            const stagedHtml = fs
                .readdirSync(staged)
                .filter((name) => name.endsWith(".htm"));
            expect(
                stagedHtml,
                `the staged BloomPUB in ${staged} should have one .htm`,
            ).toHaveLength(1);
            const html = fs.readFileSync(
                Path.join(staged, stagedHtml[0]),
                "utf8",
            );
            expect(await readLanguageMarks(reopened, html)).toEqual({
                contentLanguage1: kTag,
                langOfText: [kTag],
            });
        },
    );

    await step(
        "Publish it as an ePUB, and check the ePUB's language is the tag",
        async () => {
            await openEpubPublishScreen(reopened);
            await showEpubPreview(reopened);
            const packageText = await readEpubPreviewPackage(reopened);
            expect(
                await getEpubPackageDublinCore(
                    reopened,
                    packageText,
                    "language",
                ),
            ).toEqual([kTag]);
        },
    );
});

/**
 * Where a book's HTML (the book's own, or a published copy of it) says its first language is: the
 * data-div's contentLanguage1, and the lang of the text box that holds kText.
 */
async function readLanguageMarks(
    page: Page,
    html: string,
): Promise<{ contentLanguage1: string | undefined; langOfText: string[] }> {
    return page.evaluate(
        ({ source, text }) => {
            const doc = new DOMParser().parseFromString(source, "text/html");
            const contentLanguage1 = doc
                .querySelector('#bloomDataDiv [data-book="contentLanguage1"]')
                ?.textContent?.trim();
            const langOfText = [...doc.querySelectorAll(".bloom-editable")]
                .filter((box) => box.textContent?.includes(text))
                .map((box) => box.getAttribute("lang") ?? "(no lang)");
            return { contentLanguage1, langOfText };
        },
        { source: html, text: kText },
    );
}
