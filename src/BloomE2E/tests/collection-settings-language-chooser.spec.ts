// What Bloom does with a language picked in the language chooser, from the Languages page of
// Collection Settings. The chooser itself (search, cards, tag building) has its own tests in
// EthnoLib; these start at its OK. A language written in more than one script must reach Bloom
// under that script's name for it (BL-15190), the chooser must reopen on the choice Bloom kept,
// a name the user typed must stay, and a right-to-left script must make the language run right to
// left in the editor, after a restart too (BL-13982).
//
// Test Case ID 701, "Language Chooser Selection Reaches Bloom Correctly". Two of its checks are
// not here: the sign language's round trip from the Publish tab, which is the last test of
// publish-sign-language.spec.ts, since that file already makes a book with a video, and the Country a new
// collection gets from its language, which is chosen in the New Collection wizard, a WinForms
// dialog that the suite cannot open (AUTOMATION-DEBT.md, "WinForms surfaces are invisible to
// CDP").

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    editablePageFrame,
} from "../helpers/bookMaking";
import { selectBook } from "../helpers/collection";
import {
    cancelCollectionSettings,
    chooseCollectionSettingsLanguage,
    getCollectionSettingsLanguage,
    openCollectionSettings,
    openCollectionSettingsLanguageChooser,
    readSavedLanguageSetting,
    restartFromCollectionSettings,
    showCollectionSettingsPage,
} from "../helpers/collectionSettings";
import {
    cancelLanguageChooser,
    confirmLanguageChooser,
    getLanguageChooserSelection,
    setLanguageChooserName,
} from "../helpers/languageChooser";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "language-chooser-settings", languages: ["en"] },
});

// The second test starts from the language the first one saved.
test.describe.configure({ mode: "serial" });

const kLanguage1 = "Written Language 1";
// Chechen is written in Cyrillic, Arabic and Latin, and has a name of its own in Cyrillic.
const kChechen = { search: "Chechen", code: "ce" };
// The chooser's language cards carry the ISO 639-3 code; its tag uses the shorter "ce".
const kChechenCard = "che";
const kChechenInCyrillic = "Нохчийн мотт";
const kCustomName = "Chechen of the hills";

test("a language picked in one of its scripts reaches Bloom under that script's name, the chooser reopens on it, and a name the user types stays [Test Case ID 701]", async ({
    page,
    bloomApp,
    step,
}) => {
    await step("Open the Languages page of Collection Settings", async () => {
        await openCollectionSettings(page);
        await showCollectionSettingsPage(page, "Languages");
        expect(
            await getCollectionSettingsLanguage(page, kLanguage1),
            "sanity check: the collection starts in English",
        ).toBe("English (en)");
    });

    await step("Pick Chechen in Cyrillic for the first language", async () => {
        await chooseCollectionSettingsLanguage(page, kLanguage1, {
            ...kChechen,
            script: "Cyrillic",
        });
        // Not the English name, and not the Arabic script's name (BL-15190).
        expect(await getCollectionSettingsLanguage(page, kLanguage1)).toBe(
            `${kChechenInCyrillic} (ce)`,
        );
    });

    await step("Reopen the chooser: it shows the same choice", async () => {
        await openCollectionSettingsLanguageChooser(page, kLanguage1);
        expect(await getLanguageChooserSelection(page)).toEqual({
            language: kChechenCard,
            script: "Cyrl",
            name: kChechenInCyrillic,
            tag: "ce",
        });
    });

    await step("Type a name of the user's own and click OK", async () => {
        await setLanguageChooserName(page, kCustomName);
        await confirmLanguageChooser(page);
        expect(await getCollectionSettingsLanguage(page, kLanguage1)).toBe(
            `${kCustomName} (ce)`,
        );
    });

    await step("Reopen the chooser: it keeps that name", async () => {
        await openCollectionSettingsLanguageChooser(page, kLanguage1);
        expect(await getLanguageChooserSelection(page)).toEqual({
            language: kChechenCard,
            script: "Cyrl",
            name: kCustomName,
            tag: "ce",
        });
        await cancelLanguageChooser(page);
    });

    // A step returns undefined only when it is marked fixme, which these are not.
    const reopened = (await step(
        "Click Restart, so Bloom saves the language and reopens the collection",
        () => restartFromCollectionSettings(bloomApp),
    ))!;

    await step("Check Bloom saved the name, and still has it", async () => {
        expect(
            readSavedLanguageSetting(
                bloomApp.collectionDir,
                1,
                "LanguageIso639Code",
            ),
        ).toBe("ce");
        expect(
            readSavedLanguageSetting(bloomApp.collectionDir, 1, "LanguageName"),
        ).toBe(kCustomName);
        await openCollectionSettings(reopened);
        await showCollectionSettingsPage(reopened, "Languages");
        expect(await getCollectionSettingsLanguage(reopened, kLanguage1)).toBe(
            `${kCustomName} (ce)`,
        );
        await openCollectionSettingsLanguageChooser(reopened, kLanguage1);
        expect(await getLanguageChooserSelection(reopened)).toEqual({
            language: kChechenCard,
            script: "Cyrl",
            name: kCustomName,
            tag: "ce",
        });
        await cancelLanguageChooser(reopened);
        await cancelCollectionSettings(reopened);
    });
});

test("a right-to-left script makes the language run right to left in the editor, and still after a restart [Test Case ID 701]", async ({
    page,
    bloomApp,
    step,
}) => {
    test.setTimeout(300000);
    // The first test leaves Chechen in Cyrillic, so only the script changes here.
    expect(
        readSavedLanguageSetting(
            bloomApp.collectionDir,
            1,
            "LanguageIso639Code",
        ),
        "sanity check: the previous test left Chechen in Cyrillic (run the whole file, in order)",
    ).toBe("ce");
    expect(
        readSavedLanguageSetting(bloomApp.collectionDir, 1, "IsLanguageRtl"),
        "sanity check: the first language is saved as left to right",
    ).toBe("false");

    const reopened = (await step(
        "Pick Chechen in Arabic script for the first language, and click Restart",
        async () => {
            await openCollectionSettings(page);
            await showCollectionSettingsPage(page, "Languages");
            await chooseCollectionSettingsLanguage(page, kLanguage1, {
                ...kChechen,
                script: "Arabic",
            });
            expect(
                await getCollectionSettingsLanguage(page, kLanguage1),
            ).toMatch(/\(ce-Arab\)$/);
            return restartFromCollectionSettings(bloomApp);
        },
    ))!;
    expect(
        readSavedLanguageSetting(bloomApp.collectionDir, 1, "IsLanguageRtl"),
    ).toBe("true");

    const bookFolder = (await step(
        "Make a book and check its first-language text runs right to left",
        async () => {
            const folder = await makeBookFromTemplate(reopened, "Basic Book");
            await addPage(reopened, "Just Text");
            await goToPage(reopened, (await getContentPages(reopened))[0].id);
            expect(await getTextDirection(reopened, "ce-Arab")).toBe("rtl");
            return folder;
        },
    ))!;

    await step(
        "Restart Bloom and check the text still runs right to left",
        async () => {
            await switchTab(reopened, "collection");
            const afterRestart = await bloomApp.restart();
            await selectBook(afterRestart, bookFolder);
            await switchTab(afterRestart, "edit");
            await goToPage(
                afterRestart,
                (await getContentPages(afterRestart))[0].id,
            );
            expect(await getTextDirection(afterRestart, "ce-Arab")).toBe("rtl");
        },
    );
});

/**
 * Which way the page being edited lays out the text of its showing box in one language, "ltr" or
 * "rtl": the computed direction, which is what a person typing there sees.
 */
async function getTextDirection(page: Page, languageTag: string) {
    const box = editablePageFrame(page)
        .locator(`.bloom-editable[lang="${languageTag}"]`)
        .filter({ visible: true })
        .first();
    await box.waitFor({ state: "visible", timeout: 30000 });
    return box.evaluate((element) => getComputedStyle(element).direction);
}
