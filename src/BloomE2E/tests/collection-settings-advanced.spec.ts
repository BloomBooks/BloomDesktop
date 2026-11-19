// Journey tests for the Advanced page of the new (React) Collection Settings dialog: a person turns
// off automatic updating and presses OK, and Bloom saves the choice; a person types a new name for
// the collection and presses Restart, and Bloom renames the collection's folder and reopens it.
//
// The rename runs last: Bloom carries it out in a new copy of itself, which the fixture follows,
// so the collection folder is a different one afterwards.

import { expect, test } from "../fixtures/bloomTest";
import {
    cancelCollectionSettings,
    describeCollectionFolder,
    getCollectionSettingsCheckbox,
    getCollectionSettingsOkLabel,
    getCollectionSettingsText,
    openCollectionSettings,
    restartFromCollectionSettings,
    saveCollectionSettings,
    setCollectionSettingsCheckbox,
    setCollectionSettingsText,
    showCollectionSettingsPage,
} from "../helpers/collectionSettings";
import { readSavedUserSetting } from "../helpers/userSettings";

const kCollectionName = "advanced-settings";
const kNewCollectionName = "Renamed Books";

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

test("renaming the collection on the Advanced page renames its folder and reopens it [Test Case ID 838]", async ({
    page,
    bloomApp,
}) => {
    const oldDir = bloomApp.collectionDir;
    await openCollectionSettings(page);
    await showCollectionSettingsPage(page, "Advanced");
    expect(await getCollectionSettingsText(page, "Collection Name")).toEqual({
        value: kCollectionName,
        enabled: true,
    });
    expect(await getCollectionSettingsOkLabel(page)).toBe("OK");

    // Spaces typed around a name are not part of it.
    await setCollectionSettingsText(
        page,
        "Collection Name",
        `  ${kNewCollectionName}  `,
    );
    await expect
        .poll(() => getCollectionSettingsOkLabel(page), {
            message: "OK should become Restart once the name changes",
        })
        .toBe("Restart");

    const reopened = await restartFromCollectionSettings(
        bloomApp,
        kNewCollectionName,
    );

    expect(describeCollectionFolder(bloomApp.collectionDir)).toEqual({
        name: kNewCollectionName,
        settingsFiles: [`${kNewCollectionName}.bloomCollection`],
    });
    expect(describeCollectionFolder(oldDir)).toBeUndefined();
    await openCollectionSettings(reopened);
    await showCollectionSettingsPage(reopened, "Advanced");
    expect(
        (await getCollectionSettingsText(reopened, "Collection Name")).value,
    ).toBe(kNewCollectionName);
    await cancelCollectionSettings(reopened);
});
