// What a person sees when they open a .bloomCollection file from inside a ZIP file without
// unzipping it first. Windows Explorer opens such a file by extracting it, alone, to
// %TEMP%\Temp1_<zip file name>\<folder>\, and starting Bloom with that path, as it would for any
// .bloomCollection. This test makes the same folder and starts Bloom the same way. Bloom refuses
// before its server is up, in a plain WinForms message box, so the test reads and closes the box
// over UI Automation.

import { execFile, type ChildProcess } from "node:child_process";
import { randomUUID } from "node:crypto";
import * as fs from "node:fs";
import * as os from "node:os";
import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import { getUiString } from "../helpers/api";
import {
    environmentForBloom,
    findBloomExe,
    launchWithDontDisturb,
} from "../fixtures/launchBloom";
import {
    killBloomsWithCommandLine,
    readAndCloseBloomMessageBox,
} from "../helpers/winformsProblemBox";

const kCollectionName = "zipped-collection";

test.use({
    collectionSpec: { name: kCollectionName, languages: ["en"] },
});

// The message is a raw MessageBox, which Windows brings to the front and gives the keyboard
// whatever Bloom asks, so --dont-disturb cannot keep it from the developer. It runs where Bloom
// behaves as it does for a user: on CI, or with BLOOM_E2E_DONT_DISTURB=0.
test.skip(
    launchWithDontDisturb(),
    "The message box would take the developer's keyboard; set BLOOM_E2E_DONT_DISTURB=0 to run it.",
);

test("opening a collection from inside a ZIP file tells the user to unzip it first, and opens nothing [Test Case ID 35]", async ({
    bloomApp,
    page,
}) => {
    // What Explorer leaves behind when the collection's folder was zipped and the user opens the
    // .bloomCollection file inside the zip: only that one file, under the temp folder, in a folder
    // named after the zip.
    const extractedZip = Path.join(
        os.tmpdir(),
        `Temp1_${kCollectionName}-${randomUUID()}.zip`,
    );
    const settingsFileName = `${kCollectionName}.bloomCollection`;
    const extractedSettingsFile = Path.join(
        extractedZip,
        kCollectionName,
        settingsFileName,
    );
    fs.mkdirSync(Path.dirname(extractedSettingsFile), { recursive: true });
    fs.copyFileSync(
        Path.join(bloomApp.collectionDir, settingsFileName),
        extractedSettingsFile,
    );
    // A user-settings folder of its own, so this Bloom leaves the worker's Bloom's settings alone.
    const userSettingsDir = fs.mkdtempSync(
        Path.join(os.tmpdir(), "bloom-e2e-zip-settings-"),
    );

    // The Bloom that Explorer would start, picked out by the extracted file in its command line.
    const zipBloom = [extractedSettingsFile];
    let bloom: ChildProcess | undefined;
    try {
        const args = [
            extractedSettingsFile,
            "--e2e",
            "--automation",
            "--user-settings-folder",
            userSettingsDir,
        ];
        bloom = execFile(findBloomExe(), args, { env: environmentForBloom() });
        let exitCode: number | null | undefined;
        const exited = new Promise<number | null>((resolve) =>
            bloom!.on("exit", (code) => {
                exitCode = code;
                resolve(code);
            }),
        );

        const box = await readAndCloseBloomMessageBox(zipBloom, "", 120000);
        expect(
            box,
            "Bloom never showed a message about the collection inside a ZIP file" +
                (exitCode === undefined
                    ? " and is still running"
                    : `; it quit with exit code ${exitCode}`),
        ).toBeDefined();
        // In the UI language that the worker's Bloom, also started on default user settings, uses.
        const unzipFirst = await getUiString(
            page,
            "OpenCreateCloneControl.InZipFileMessage",
            "It looks like you are trying to open a Bloom Collection from inside of a ZIP file. You need to first unzip the ZIP file, and then open the collection.",
        );
        expect(box!.texts.join(" ")).toContain(unzipFirst);

        // Closing the box ends that Bloom without opening the collection.
        const exitAfterBox = await Promise.race([
            exited,
            new Promise<string>((resolve) =>
                setTimeout(() => resolve("still running"), 60000),
            ),
        ]);
        expect(
            exitAfterBox,
            "Bloom should quit, having opened nothing, once the message is closed",
        ).toBe(1);
        expect(
            fs.readdirSync(Path.dirname(extractedSettingsFile)),
            "Bloom should leave the extracted folder as Explorer made it",
        ).toEqual([settingsFileName]);
    } finally {
        // When the test failed before the box was closed, that Bloom is still up.
        try {
            killBloomsWithCommandLine(zipBloom);
        } finally {
            fs.rmSync(extractedZip, { recursive: true, force: true });
            fs.rmSync(userSettingsDir, { recursive: true, force: true });
        }
    }
});
