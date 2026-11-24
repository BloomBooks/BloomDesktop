// Journey test for setting and removing the optional languages on the Languages page of the new
// (React) Collection Settings dialog: a person sets a third written language through the language
// chooser (one written in several scripts, so the chooser asks for one) and a sign language,
// presses Restart, and Bloom saves both; then removes both and presses Restart again.

import { expect, test } from "../fixtures/bloomTest";
import {
    chooseCollectionSettingsLanguage,
    getCollectionSettingsLanguage,
    getCollectionSettingsOkLabel,
    openCollectionSettings,
    readSavedCollectionSetting,
    removeCollectionSettingsLanguage,
    restartFromCollectionSettings,
    showCollectionSettingsPage,
} from "../helpers/collectionSettings";

const kLanguage3 = "Written Language 3";
const kSignLanguage = "Sign Language";

test.use({
    collectionSpec: { name: "language-chooser", languages: ["en", "fr"] },
});

test("setting and removing the third language and the sign language saves them", async ({
    page,
    bloomApp,
}) => {
    expect(
        readSavedCollectionSetting(
            bloomApp.collectionDir,
            "Language3Iso639Code",
        ) ?? "",
        "sanity check: the collection starts with no third language",
    ).toBe("");

    await openCollectionSettings(page);
    await showCollectionSettingsPage(page, "Languages");
    expect(await getCollectionSettingsLanguage(page, kLanguage3)).toBe("");
    // Standard Arabic is written in more than one script, so the chooser asks which.
    await chooseCollectionSettingsLanguage(page, kLanguage3, {
        search: "standard arabic",
        code: "arb",
        script: "Arabic",
    });
    expect(await getCollectionSettingsLanguage(page, kLanguage3)).toContain(
        "(arb)",
    );
    await chooseCollectionSettingsLanguage(page, kSignLanguage, {
        search: "american sign",
        code: "ase",
    });
    expect(await getCollectionSettingsLanguage(page, kSignLanguage)).toBe(
        "American Sign Language (ase)",
    );
    await expect.poll(() => getCollectionSettingsOkLabel(page)).toBe("Restart");

    const reopened = await restartFromCollectionSettings(bloomApp);
    expect(
        readSavedCollectionSetting(
            bloomApp.collectionDir,
            "Language3Iso639Code",
        ),
    ).toBe("arb");
    expect(
        readSavedCollectionSetting(
            bloomApp.collectionDir,
            "SignLanguageIso639Code",
        ),
    ).toBe("ase");

    await openCollectionSettings(reopened);
    await showCollectionSettingsPage(reopened, "Languages");
    await removeCollectionSettingsLanguage(reopened, kLanguage3);
    await removeCollectionSettingsLanguage(reopened, kSignLanguage);
    expect(await getCollectionSettingsLanguage(reopened, kLanguage3)).toBe("");
    expect(await getCollectionSettingsLanguage(reopened, kSignLanguage)).toBe(
        "",
    );

    await restartFromCollectionSettings(bloomApp);
    expect(
        readSavedCollectionSetting(
            bloomApp.collectionDir,
            "Language3Iso639Code",
        ) ?? "",
    ).toBe("");
    expect(
        readSavedCollectionSetting(
            bloomApp.collectionDir,
            "SignLanguageIso639Code",
        ) ?? "",
    ).toBe("");
});
