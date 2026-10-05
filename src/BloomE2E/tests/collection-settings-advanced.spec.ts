// Journey tests for the Advanced page of the new (React) Collection Settings dialog: a person turns
// off automatic updating and presses OK, and Bloom saves the choice; a person types a new name for
// the collection and OK turns into Restart.
//
// The rename stops short of pressing Restart: Bloom carries out a rename by starting a new Bloom
// process with only "--rename <from> <to>" on its command line, which drops this suite's launch
// flags (see AUTOMATION-DEBT.md, "A collection rename relaunches Bloom without its launch flags").

import { expect, test } from "../fixtures/bloomTest";
import {
    cancelCollectionSettings,
    getCollectionSettingsCheckbox,
    getCollectionSettingsOkLabel,
    getCollectionSettingsText,
    openCollectionSettings,
    saveCollectionSettings,
    setCollectionSettingsCheckbox,
    setCollectionSettingsText,
    showCollectionSettingsPage,
} from "../helpers/collectionSettings";
import { readSavedUserSetting } from "../helpers/userSettings";

const kCollectionName = "advanced-settings";

test.use({
    collectionSpec: { name: kCollectionName, languages: ["en"] },
});

test("turning off Automatically Update Bloom on the Advanced page saves it [Test Case ID 838]", async ({
    page,
    bloomApp,
}) => {
    // The launch gives this Bloom an empty settings folder, so the setting has its default, on
    // (or is not saved at all yet).
    expect(
        await readSavedUserSetting(bloomApp.userSettingsDir, "AutoUpdate"),
    ).not.toBe("False");

    await openCollectionSettings(page);
    await showCollectionSettingsPage(page, "Advanced");
    expect(
        await getCollectionSettingsCheckbox(page, "Automatically Update Bloom"),
    ).toEqual({ checked: true, enabled: true });

    await setCollectionSettingsCheckbox(
        page,
        "Automatically Update Bloom",
        false,
    );
    // A setting of Bloom itself, not of the collection: no restart.
    expect(await getCollectionSettingsOkLabel(page)).toBe("OK");
    await saveCollectionSettings(page);

    await expect
        .poll(
            () => readSavedUserSetting(bloomApp.userSettingsDir, "AutoUpdate"),
            { message: "Bloom should have saved AutoUpdate as off" },
        )
        .toBe("False");
});

test("renaming the collection on the Advanced page asks for a restart [Test Case ID 838]", async ({
    page,
}) => {
    await openCollectionSettings(page);
    await showCollectionSettingsPage(page, "Advanced");
    expect(await getCollectionSettingsText(page, "Collection Name")).toEqual({
        value: kCollectionName,
        enabled: true,
    });
    expect(await getCollectionSettingsOkLabel(page)).toBe("OK");

    await setCollectionSettingsText(page, "Collection Name", "Renamed Books");

    await expect
        .poll(() => getCollectionSettingsOkLabel(page), {
            message: "OK should become Restart once the name changes",
        })
        .toBe("Restart");

    await cancelCollectionSettings(page);
});
