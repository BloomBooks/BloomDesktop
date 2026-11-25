// What a person sees when Bloom cannot rename the collection's folder because another program is
// using it. The manual case gets there by opening Bloom from File Explorer, which leaves the folder
// held (BL-11004); any process whose current directory is the folder holds it the same way, and
// that is what this test starts, so the rename fails every time rather than only when Explorer
// happens to hold on.
//
// Bloom renames the folder in a new copy of itself, during that copy's startup, before its server
// is up. So the failure is reported in a plain WinForms "Bloom Problem" box rather than the React
// problem dialog, and the test reads and closes it over UI Automation.

import { spawn, type ChildProcess } from "node:child_process";
import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import {
    describeCollectionFolder,
    openCollectionSettings,
    setCollectionSettingsText,
    showCollectionSettingsPage,
    getCollectionSettingsOkLabel,
} from "../helpers/collectionSettings";
import { realClick } from "../helpers/realClick";
import {
    killBloomsWithCommandLine,
    readAndCloseBloomProblemBox,
} from "../helpers/winformsProblemBox";

const kCollectionName = "folder-in-use";
const kNewCollectionName = "Renamed While In Use";

test.use({
    collectionSpec: { name: kCollectionName, languages: ["en"] },
});

/** Start a process that does nothing, with `folder` as its current directory, which holds it. */
function holdFolder(folder: string): ChildProcess {
    return spawn(process.execPath, ["-e", "setInterval(() => {}, 1 << 30)"], {
        cwd: folder,
        stdio: "ignore",
    });
}

test("renaming a collection whose folder another program holds leaves it as it was, and Bloom says it could not finish [Test Case ID 400]", async ({
    page,
    bloomApp,
}) => {
    const oldDir = bloomApp.collectionDir;
    const newDir = Path.join(Path.dirname(oldDir), kNewCollectionName);
    // The copy of Bloom that does the rename, which the fixture does not track.
    const renamingBloom = ["--rename", bloomApp.userSettingsDir];
    const holder = holdFolder(oldDir);
    try {
        await openCollectionSettings(page);
        await showCollectionSettingsPage(page, "Advanced");
        await setCollectionSettingsText(
            page,
            "Collection Name",
            kNewCollectionName,
        );
        await expect
            .poll(() => getCollectionSettingsOkLabel(page), {
                message: "OK should become Restart once the name changes",
            })
            .toBe("Restart");

        const pageClosed = page.waitForEvent("close", { timeout: 60000 });
        pageClosed.catch(() => undefined);
        // Bloom can tear the page down before Playwright finishes its click; a "closed" error
        // means the click landed.
        await realClick(
            page.getByRole("button", { name: "Restart", exact: true }),
        ).catch((error) => {
            if (!/closed/i.test(String(error))) throw error;
        });
        await pageClosed;

        // The copy that does the rename keeps trying for a few seconds before it gives up.
        const box = await readAndCloseBloomProblemBox(renamingBloom, 90000);
        expect(
            box,
            "Bloom never reported that it could not rename the collection's folder",
        ).toBeDefined();
        expect(box!.texts.join(" ")).toContain(
            "Bloom could not finish renaming your collection folder. Restart your computer and try again.",
        );

        expect(describeCollectionFolder(oldDir)).toEqual({
            name: kCollectionName,
            settingsFiles: [`${kCollectionName}.bloomCollection`],
        });
        expect(describeCollectionFolder(newDir)).toBeUndefined();
    } finally {
        holder.kill();
        // When the test failed before the problem box was closed, the renaming copy is still up.
        killBloomsWithCommandLine(renamingBloom);
        // Bloom exits once its problem box is closed; start it again on the collection, so the
        // worker is left with a Bloom as every test expects to find it.
        await bloomApp.restart();
    }
});
