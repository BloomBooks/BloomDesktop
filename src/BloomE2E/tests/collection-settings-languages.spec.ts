// Journey test for the Languages page of the new (React) Collection Settings dialog: a person
// changes the first language's Default Font, then on its More page its line spacing and the reader
// tools' alphabet, presses Restart, and Bloom saves them and reopens the collection, keeping the
// decodable stages the alphabet belongs to.

import { expect, test } from "../fixtures/bloomTest";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import { selectBook } from "../helpers/collection";
import {
    cancelCollectionSettings,
    chooseCollectionSettingsLanguageFont,
    chooseCollectionSettingsListChoice,
    closeCollectionSettingsLanguageMore,
    getCollectionSettingsLanguage,
    getCollectionSettingsLanguageFont,
    getCollectionSettingsLineSpacingLabel,
    getCollectionSettingsList,
    getCollectionSettingsOkLabel,
    getCollectionSettingsText,
    openCollectionSettings,
    openCollectionSettingsLanguageMore,
    readSavedLanguageSetting,
    restartFromCollectionSettings,
    setCollectionSettingsText,
    showCollectionSettingsPage,
} from "../helpers/collectionSettings";
import { pickFont } from "../helpers/fontChooser";
import {
    getSavedReaderSettings,
    useKnownReaderStages,
} from "../helpers/readerSetup";
import { switchTab } from "../helpers/workspace";

const kLanguage1 = "Local Language";
const kLineSpacing = "Default line spacing";
const kAlphabet = "Letters and Letter Combinations";
// The alphabet useKnownReaderStages gives, with "zh" added.
const kNewAlphabet =
    "a b c ch d e f g h i j k l m n ng o p q r s sh t th u v w x y z zh";

test.use({
    collectionSpec: { name: "languages-settings", languages: ["en"] },
});

test("changing a language's font, line spacing and alphabet saves them and reopens the collection [Test Case ID 852]", async ({
    page,
    bloomApp,
}) => {
    // The reader tools' settings belong to the collection but are read through the book being
    // edited, so the collection needs a book, and known stages for the alphabet to keep.
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    await useKnownReaderStages(page);
    const stagesBefore = (await getSavedReaderSettings(page)).stages;
    expect(stagesBefore.length, "sanity check: the stages were set up").toBe(4);
    await switchTab(page, "collection");
    // Times New Roman is on every Windows machine, and is not the collection's starting font.
    const newFont = await pickFont(page, "microsoft", ["Times New Roman"]);
    const lineSpacing = await getCollectionSettingsLineSpacingLabel(page, 1.5);

    await openCollectionSettings(page);
    await showCollectionSettingsPage(page, "Languages");
    expect(await getCollectionSettingsLanguage(page, kLanguage1)).toBe(
        "English (en)",
    );
    // Polled: the fonts arrive after the page shows.
    await expect
        .poll(() => getCollectionSettingsLanguageFont(page, kLanguage1))
        .not.toBe(newFont);
    await chooseCollectionSettingsLanguageFont(page, kLanguage1, newFont);

    await openCollectionSettingsLanguageMore(page, kLanguage1);
    expect((await getCollectionSettingsList(page, kLineSpacing)).shown).toBe(
        "Auto",
    );
    await chooseCollectionSettingsListChoice(page, kLineSpacing, lineSpacing);
    await setCollectionSettingsText(page, kAlphabet, kNewAlphabet);
    await closeCollectionSettingsLanguageMore(page);
    // A font change has to reach every book, so it needs a restart.
    await expect.poll(() => getCollectionSettingsOkLabel(page)).toBe("Restart");

    const reopened = await restartFromCollectionSettings(bloomApp);

    expect(
        readSavedLanguageSetting(
            bloomApp.collectionDir,
            1,
            "DefaultLanguageFontName",
        ),
    ).toBe(newFont);
    expect(
        readSavedLanguageSetting(
            bloomApp.collectionDir,
            1,
            "LanguageLineHeight",
        ),
    ).toBe("1.5");
    await selectBook(reopened, bookFolder);
    const readerSettings = await getSavedReaderSettings(reopened);
    expect(readerSettings.letters).toBe(kNewAlphabet);
    expect(
        readerSettings.stages,
        "saving the alphabet must keep the decodable stages",
    ).toEqual(stagesBefore);

    await openCollectionSettings(reopened);
    await showCollectionSettingsPage(reopened, "Languages");
    await expect
        .poll(() => getCollectionSettingsLanguageFont(reopened, kLanguage1))
        .toBe(newFont);
    await openCollectionSettingsLanguageMore(reopened, kLanguage1);
    expect(
        (await getCollectionSettingsList(reopened, kLineSpacing)).shown,
    ).toBe(lineSpacing);
    expect((await getCollectionSettingsText(reopened, kAlphabet)).value).toBe(
        kNewAlphabet,
    );
    await cancelCollectionSettings(reopened);
});
