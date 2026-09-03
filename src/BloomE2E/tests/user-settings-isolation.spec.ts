// A test's Bloom keeps its user settings (user.config: UI language, page zoom, the Bloom Library
// login, and the rest of Settings.Default) in a folder of its own inside the run's temp folder,
// not in the %LOCALAPPDATA%\SIL\Bloom\<version> folder that every Bloom of that build shares. So
// a run starts from default settings, and what it saves dies with the run, instead of starting
// from whatever the developer's Bloom saved last and leaving its own changes behind for them.
//
// This spec checks that the machinery holds: Bloom is using the folder the fixture gave it, a
// setting a test changes lands there, and it is still there after a restart within the run. It is
// infrastructure, so it has no "[Test Case ID N]" tag.

import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import {
    getUserSettingsFolder,
    readSavedUserSetting,
} from "../helpers/userSettings";
import { getZoom, setZoom } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "user-settings-isolation", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

/** Compare two folder paths the way Windows does. */
function sameFolder(a: string, b: string): boolean {
    return Path.resolve(a).toLowerCase() === Path.resolve(b).toLowerCase();
}

test.describe("a test's Bloom keeps its user settings to itself", () => {
    test("Bloom keeps its user settings in the folder the run gave it", async ({
        page,
        bloomApp,
    }) => {
        const folder = await getUserSettingsFolder(page);
        expect(
            sameFolder(folder, bloomApp.userSettingsDir),
            `Bloom keeps its user settings in ${folder}, not in the run's own ${bloomApp.userSettingsDir}.`,
        ).toBe(true);

        // Bloom accepted the license for us at startup (there is nobody to click Accept) and saved
        // that, so the folder already holds a user.config, and this is what is in it.
        expect(
            readSavedUserSetting(bloomApp.userSettingsDir, "LicenseAccepted"),
        ).toBe("True");
    });

    test("a setting changed in the test's Bloom is saved in that folder", async ({
        page,
        bloomApp,
    }) => {
        test.setTimeout(300000);
        // The zoom is a user setting Bloom saves on its own, a couple of seconds after it changes,
        // and there is a zoom only while a book is being edited.
        await makeBookFromTemplate(page, "Basic Book");
        const { zoom, minZoom, maxZoom } = await getZoom(page);
        // A fresh settings folder means Bloom starts at its default zoom, whatever the developer's
        // own Bloom is zoomed to.
        expect(zoom).toBe(100);
        const newZoom = Math.min(zoom + 20, maxZoom);
        expect(newZoom).toBeGreaterThanOrEqual(minZoom);

        await setZoom(page, newZoom);

        await expect
            .poll(
                () =>
                    readSavedUserSetting(bloomApp.userSettingsDir, "PageZoom"),
                {
                    timeout: 15000,
                    message: `Bloom never saved the zoom of ${newZoom}% to ${bloomApp.userSettingsDir}.`,
                },
            )
            .toBe(String(newZoom));
    });

    test("the saved setting is still there after a restart within the run", async ({
        bloomApp,
    }) => {
        test.setTimeout(300000);
        const savedZoom = readSavedUserSetting(
            bloomApp.userSettingsDir,
            "PageZoom",
        );
        expect(
            savedZoom,
            "test setup: the previous test saved a zoom",
        ).toBeDefined();

        const page = await bloomApp.restart();

        expect(
            sameFolder(
                await getUserSettingsFolder(page),
                bloomApp.userSettingsDir,
            ),
        ).toBe(true);
        expect(readSavedUserSetting(bloomApp.userSettingsDir, "PageZoom")).toBe(
            savedZoom,
        );
    });
});
