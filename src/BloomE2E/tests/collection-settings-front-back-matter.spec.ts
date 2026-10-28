// Journey tests for the Front & Back Matter page of the new (React) Collection Settings dialog: a
// person chooses a front/back matter style, writes a QR code caption (watching the badge preview
// follow it) and a place name, presses Restart, and Bloom saves them and reopens the collection;
// under a branding that chooses the style, the page offers only that one.
//
// The tests share one Bloom and one collection, and run in order: the last one changes the
// branding.

import { expect, test } from "../fixtures/bloomTest";
import {
    cancelCollectionSettings,
    chooseCollectionSettingsListChoice,
    getCollectionSettingsCheckbox,
    getCollectionSettingsList,
    getCollectionSettingsOkLabel,
    getCollectionSettingsText,
    getQrCodeBadgePreviewCaption,
    openCollectionSettings,
    readSavedCollectionSetting,
    restartFromCollectionSettings,
    setBranding,
    setCollectionSettingsText,
    showCollectionSettingsPage,
} from "../helpers/collectionSettings";

const kPageName = "Front & Back Matter";

test.use({
    collectionSpec: {
        name: "front-back-matter-settings",
        languages: ["en"],
    },
});

test.describe.configure({ mode: "serial" });

test("choosing a style, a QR code caption and a country saves them and reopens the collection", async ({
    page,
    bloomApp,
}) => {
    // The collection starts on Factory, which the dialog calls Paper Saver, with no caption of
    // its own and no country. (Bloom has saved the file as it opened the collection, so the
    // caption is there, empty; the country may not be.)
    expect(
        readSavedCollectionSetting(bloomApp.collectionDir, "XMatterPack"),
    ).toBe("Factory");
    expect(
        readSavedCollectionSetting(
            bloomApp.collectionDir,
            "BadgeQrCodeLabel",
        ) ?? "",
    ).toBe("");
    expect(
        readSavedCollectionSetting(bloomApp.collectionDir, "Country") ?? "",
    ).toBe("");

    await openCollectionSettings(page);
    await showCollectionSettingsPage(page, kPageName);
    // Polled: the list of styles comes after the page shows.
    await expect
        .poll(() => getCollectionSettingsList(page, "Style"), {
            message: "the Style list should show Paper Saver and be enabled",
        })
        .toEqual({ shown: "Paper Saver", enabled: true });
    // A collection shows QR codes unless it is told not to.
    expect(await getCollectionSettingsCheckbox(page, "Show QR Codes")).toEqual({
        checked: true,
        enabled: true,
    });
    expect(await getCollectionSettingsOkLabel(page)).toBe("OK");

    await chooseCollectionSettingsListChoice(page, "Style", "Traditional");
    await setCollectionSettingsText(page, "Caption", "Read more in {0}:");
    await expect
        .poll(() => getQrCodeBadgePreviewCaption(page), {
            message:
                "the badge preview should follow the caption as it is typed",
        })
        .toBe("Read more in English:");
    await setCollectionSettingsText(page, "Country", "Kenya");
    // Every book has to be rebuilt with the new style and badge, so these need a restart.
    await expect.poll(() => getCollectionSettingsOkLabel(page)).toBe("Restart");

    const reopened = await restartFromCollectionSettings(bloomApp);

    expect(
        readSavedCollectionSetting(bloomApp.collectionDir, "XMatterPack"),
    ).toBe("Traditional");
    expect(
        readSavedCollectionSetting(bloomApp.collectionDir, "BadgeQrCodeLabel"),
    ).toBe("Read more in {0}:");
    expect(readSavedCollectionSetting(bloomApp.collectionDir, "Country")).toBe(
        "Kenya",
    );
    await openCollectionSettings(reopened);
    await showCollectionSettingsPage(reopened, kPageName);
    await expect
        .poll(() => getCollectionSettingsList(reopened, "Style"))
        .toEqual({ shown: "Traditional", enabled: true });
    expect(await getCollectionSettingsText(reopened, "Country")).toEqual({
        value: "Kenya",
        enabled: true,
    });
    await cancelCollectionSettings(reopened);
});

test("under a branding that chooses the style, the page offers only that style", async ({
    bloomApp,
}) => {
    const page = bloomApp.page;
    // Stands for entering a subscription code whose branding chooses the Story Producer pack.
    await setBranding(page, "Story-Producer-App");

    await openCollectionSettings(page);
    await showCollectionSettingsPage(page, kPageName);

    await expect
        .poll(() => getCollectionSettingsList(page, "Style"), {
            message: "the Style list should show only Story Producer, disabled",
        })
        .toEqual({ shown: "Story Producer", enabled: false });
    await cancelCollectionSettings(page);
});
