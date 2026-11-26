// Journey test for Cancel in the Collection Settings dialog: a person changes settings on several
// pages (the languages, among them a third language and a sign language, a font, an experimental
// feature, a setting of Bloom itself and a place name), sees OK turn into Restart, and clicks
// Cancel. Nothing is saved, Bloom does not reopen the collection, and the dialog opens again on the
// settings as they were (BL-12371 saved some of them anyway). Then the same person shows that OK
// says Restart only while a change that needs one is there, and that Restart saves the changes and
// reopens the collection.

import type { Page } from "@playwright/test";
import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import {
    cancelCollectionSettings,
    chooseCollectionSettingsLanguage,
    chooseCollectionSettingsLanguageFont,
    getCollectionSettingsCheckbox,
    getCollectionSettingsLanguage,
    getCollectionSettingsLanguageFont,
    getCollectionSettingsOkLabel,
    getCollectionSettingsText,
    kEnterpriseSubscriptionCode,
    openCollectionSettings,
    readSavedLanguageSetting,
    removeCollectionSettingsLanguage,
    restartFromCollectionSettings,
    setCollectionSettingsCheckbox,
    setCollectionSettingsText,
    showCollectionSettingsPage,
} from "../helpers/collectionSettings";
import { pickFont } from "../helpers/fontChooser";
import { type IChooserLanguage } from "../helpers/languageChooser";
import {
    getSavedExperimentalFeatures,
    readSavedUserSetting,
} from "../helpers/userSettings";

// Team Collections needs a subscription tier above Basic, or its checkbox is disabled.
test.use({
    collectionSpec: {
        name: "cancel-settings",
        languages: ["en", "fr"],
        signLanguage: "",
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const kLanguage1 = "Written Language 1";
const kLanguage2 = "Written Language 2";
const kLanguage3 = "Written Language 3";
const kSignLanguage = "Sign Language";
const kTeamCollections = "Team Collections";
const kTeamCollectionsToken = "team-collections";
const kAutoUpdate = "Automatically Update Bloom";
const kFrontBackMatter = "Front & Back Matter";
// The chooser's cards show the code the collection will use. The dialog then shows each language by
// its own name with that code after it, so the test looks only at the code.
const kSpanish: IChooserLanguage = { search: "Spanish", code: "es" };
// The chooser asks which script German is in.
const kGerman: IChooserLanguage = {
    search: "German",
    code: "de",
    script: "Latin",
};
const kAmericanSignLanguage: IChooserLanguage = {
    search: "American Sign Language",
    code: "ase",
};

/** What the dialog shows for every setting this test changes. */
interface IDialogState {
    language1: string;
    language2: string;
    language3: string;
    signLanguage: string;
    language1Font: string;
    teamCollections: boolean;
    autoUpdate: boolean;
    country: string;
}

/**
 * Read, page by page, every setting this test changes from the open dialog. Leaves the dialog on
 * its Advanced page.
 */
async function readDialogState(page: Page): Promise<IDialogState> {
    await showCollectionSettingsPage(page, "Languages");
    const language1 = await getCollectionSettingsLanguage(page, kLanguage1);
    const language2 = await getCollectionSettingsLanguage(page, kLanguage2);
    const language3 = await getCollectionSettingsLanguage(page, kLanguage3);
    const signLanguage = await getCollectionSettingsLanguage(
        page,
        kSignLanguage,
    );
    // The fonts arrive after the page shows; until then the list shows none.
    let language1Font = "";
    await expect
        .poll(
            async () =>
                (language1Font = await getCollectionSettingsLanguageFont(
                    page,
                    kLanguage1,
                )),
            { message: "the Default Font of language 1 never showed" },
        )
        .not.toBe("");
    await showCollectionSettingsPage(page, kFrontBackMatter);
    const country = (await getCollectionSettingsText(page, "Country")).value;
    await showCollectionSettingsPage(page, "Experimental");
    // The box stays disabled until Bloom has said the subscription includes the feature.
    await expect
        .poll(
            async () =>
                (await getCollectionSettingsCheckbox(page, kTeamCollections))
                    .enabled,
        )
        .toBe(true);
    const teamCollections = (
        await getCollectionSettingsCheckbox(page, kTeamCollections)
    ).checked;
    await showCollectionSettingsPage(page, "Advanced");
    const autoUpdate = (await getCollectionSettingsCheckbox(page, kAutoUpdate))
        .checked;
    return {
        language1,
        language2,
        language3,
        signLanguage,
        language1Font,
        teamCollections,
        autoUpdate,
        country,
    };
}

/** The text of the collection's settings file, where a save puts the collection's settings. */
function readSettingsFile(collectionDir: string): string {
    const settingsFile = fs
        .readdirSync(collectionDir)
        .find((name) => name.endsWith(".bloomCollection"));
    if (!settingsFile)
        throw new Error(`There is no .bloomCollection in ${collectionDir}.`);
    return fs.readFileSync(Path.join(collectionDir, settingsFile), "utf8");
}

test("Cancel in Collection Settings discards every change, and only a change that needs a restart makes OK restart Bloom [Test Case ID 116]", async ({
    page,
    bloomApp,
    step,
}) => {
    // Bloom saved the settings file as it opened the collection, so this is the file as Bloom
    // keeps it.
    const settingsFileBefore = readSettingsFile(bloomApp.collectionDir);
    // The launch gives this Bloom an empty settings folder: nothing turned on or off yet.
    expect(
        await getSavedExperimentalFeatures(bloomApp.userSettingsDir),
    ).not.toContain(kTeamCollectionsToken);
    expect(
        await readSavedUserSetting(bloomApp.userSettingsDir, "AutoUpdate"),
    ).not.toBe("False");
    // Times New Roman is on every Windows machine, and is not the collection's starting font.
    const newFont = await pickFont(page, "microsoft", ["Times New Roman"]);

    // Any POST of the settings is a save; Cancel must not make one.
    const saves: string[] = [];
    page.on("request", (request) => {
        if (
            request.method() === "POST" &&
            /\/bloom\/api\/collection\/settings(\?|$)/.test(request.url())
        )
            saves.push(request.postData() ?? "");
    });

    let stateBefore: IDialogState | undefined;
    await step("Open Settings and read what it shows", async () => {
        await openCollectionSettings(page);
        stateBefore = await readDialogState(page);
        expect(stateBefore.language1).toBe("English (en)");
        expect(stateBefore.language2, "sanity check: French").toMatch(
            /\(fr\)$/,
        );
        expect(stateBefore.language3).toBe("");
        expect(stateBefore.signLanguage).toBe("");
        expect(stateBefore.language1Font).not.toBe(newFont);
        expect(stateBefore.teamCollections).toBe(false);
        expect(stateBefore.autoUpdate).toBe(true);
        expect(stateBefore.country).toBe("");
        expect(await getCollectionSettingsOkLabel(page)).toBe("OK");
    });

    await step("Change a setting on every page", async () => {
        await showCollectionSettingsPage(page, "Languages");
        await chooseCollectionSettingsLanguage(page, kLanguage2, kSpanish);
        await chooseCollectionSettingsLanguage(page, kLanguage3, kGerman);
        await chooseCollectionSettingsLanguage(
            page,
            kSignLanguage,
            kAmericanSignLanguage,
        );
        await chooseCollectionSettingsLanguageFont(page, kLanguage1, newFont);
        await showCollectionSettingsPage(page, kFrontBackMatter);
        await setCollectionSettingsText(page, "Country", "Kenya");
        await showCollectionSettingsPage(page, "Experimental");
        await setCollectionSettingsCheckbox(page, kTeamCollections, true);
        await showCollectionSettingsPage(page, "Advanced");
        await setCollectionSettingsCheckbox(page, kAutoUpdate, false);

        // Every change is in the dialog, so Cancel has something to throw away.
        expect(await readDialogState(page)).toEqual({
            language1: stateBefore!.language1,
            language2: expect.stringMatching(/\(es\)$/),
            language3: expect.stringMatching(/\(de\)$/),
            signLanguage: expect.stringMatching(/\(ase\)$/),
            language1Font: newFont,
            teamCollections: true,
            autoUpdate: false,
            country: "Kenya",
        });
        await expect
            .poll(() => getCollectionSettingsOkLabel(page), {
                message:
                    "the languages, font and Team Collections need a restart",
            })
            .toBe("Restart");
    });

    await step(
        "Click Cancel: nothing is saved and Bloom does not restart",
        async () => {
            await cancelCollectionSettings(page);
            expect(saves, "Cancel must not post the settings").toEqual([]);
            expect(
                page.isClosed(),
                "Cancel must not reopen the collection",
            ).toBe(false);
            expect(readSettingsFile(bloomApp.collectionDir)).toBe(
                settingsFileBefore,
            );
            expect(
                await getSavedExperimentalFeatures(bloomApp.userSettingsDir),
            ).not.toContain(kTeamCollectionsToken);
            expect(
                await readSavedUserSetting(
                    bloomApp.userSettingsDir,
                    "AutoUpdate",
                ),
            ).not.toBe("False");
        },
    );

    await step(
        "Open Settings again: it shows the settings as they were",
        async () => {
            await openCollectionSettings(page);
            expect(await readDialogState(page)).toEqual(stateBefore);
            expect(await getCollectionSettingsOkLabel(page)).toBe("OK");
        },
    );

    await step(
        "OK says Restart only while a change that needs one is there",
        async () => {
            // A setting of Bloom itself needs no restart.
            await setCollectionSettingsCheckbox(page, kAutoUpdate, false);
            expect(await getCollectionSettingsOkLabel(page)).toBe("OK");
            await showCollectionSettingsPage(page, "Languages");
            await chooseCollectionSettingsLanguage(page, kLanguage3, kGerman);
            await expect
                .poll(() => getCollectionSettingsOkLabel(page), {
                    message: "a third language needs a restart",
                })
                .toBe("Restart");
            // Taking the language away again leaves nothing that needs one.
            await removeCollectionSettingsLanguage(page, kLanguage3);
            await expect
                .poll(() => getCollectionSettingsOkLabel(page), {
                    message: "with the third language removed again, OK is OK",
                })
                .toBe("OK");
            await chooseCollectionSettingsLanguage(page, kLanguage3, kGerman);
            await expect
                .poll(() => getCollectionSettingsOkLabel(page))
                .toBe("Restart");
        },
    );

    // A step returns undefined only when it is marked fixme, which this is not.
    const reopened = (await step(
        "Click Restart: Bloom saves both changes and reopens the collection",
        () => restartFromCollectionSettings(bloomApp),
    ))!;

    await step(
        "Check Bloom saved both changes, and still has them",
        async () => {
            expect(saves.length, "Restart posts the settings once").toBe(1);
            expect(
                readSavedLanguageSetting(
                    bloomApp.collectionDir,
                    3,
                    "LanguageIso639Code",
                ),
            ).toBe("de");
            await expect
                .poll(() =>
                    readSavedUserSetting(
                        bloomApp.userSettingsDir,
                        "AutoUpdate",
                    ),
                )
                .toBe("False");
            await openCollectionSettings(reopened);
            await showCollectionSettingsPage(reopened, "Languages");
            expect(
                await getCollectionSettingsLanguage(reopened, kLanguage3),
            ).toMatch(/\(de\)$/);
            await showCollectionSettingsPage(reopened, "Advanced");
            expect(
                (await getCollectionSettingsCheckbox(reopened, kAutoUpdate))
                    .checked,
            ).toBe(false);
            await cancelCollectionSettings(reopened);
        },
    );
});
