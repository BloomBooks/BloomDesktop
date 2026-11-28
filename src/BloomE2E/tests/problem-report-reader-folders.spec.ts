// Test Case ID 447, "Allowed Words Subdirectories": reporting a problem with a decodable reader book
// should send the collection's "Allowed Words" files along with the book, kept in their folder
// rather than flattened into the zip's top level (BL-10483). Bloom zips the "Sample Texts" folder
// the same way (ProblemReportApi.AddReaderInfo), and the old version of the case (bloom-test-cases
// 349) checked both, so this test does too.
//
// The case reports the problem from the book that BL-10483's reporter sent. This test makes its own
// decodable reader instead: a book from the Decodable Reader template, whose stages come from three
// allowed-word lists in the collection's "Allowed Words" folder. The lists are written straight into
// that folder, as the toolbox's "Use allowed words" file chooser (a WinForms open-file dialog,
// ReadersApi.ShowSelectAllowedWordsFileDialog) copies them there. One file in "Sample Texts" has the
// same name as one of the lists, so a report that flattened the folders could not keep both.
//
// What an online report would send to Bloom's server, it sends only after filing a real issue on
// Bloom's YouTrack. So the test submits the report with Bloom offline (helpers/
// offlineProblemReport.ts), where Bloom puts the same zipped book in the zip it offers to email
// (ProblemReportApi.MakeEmailableReportFile, CreateReportZipFile).

import * as fs from "node:fs";
import * as Path from "node:path";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import { readZip } from "../helpers/bulkBloomPub";
import {
    submitReportOffline,
    withBloomOfflineForReports,
} from "../helpers/offlineProblemReport";
import {
    closeReactDialogFromNode,
    waitForReportDialogPage,
} from "../helpers/problemDialogs";
import { changeSavedReaderSettings } from "../helpers/readerSetup";
import { chooseReportAProblemFromHelpMenu } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "report-problem-reader-folders",
        languages: ["en"],
    },
});

// The collection's reader folders and what the test puts in them. "stage 1.txt" is in both.
const kReaderFiles: Record<string, Record<string, string>> = {
    "Allowed Words": {
        "stage 1.txt": "the king did a good deed",
        "stage 2.txt": "gold wish wine god",
        "stage 3.txt": "warned swayed touched satyr",
    },
    "Sample Texts": {
        "stage 1.txt": "Midas asked for everything he touched to turn to gold.",
    },
};

// The text the case types on its new page.
const kPageText =
    "The Greek king Midas did a good deed for a Satyr. This prompted Dionysus, the god of wine, " +
    "to grant him a wish. Midas asked for everything he touched to turn to gold. Dionysus’ " +
    "warned him not to do so, but Midas could not be swayed.";

function writeReaderFiles(collectionDir: string): void {
    for (const [folder, files] of Object.entries(kReaderFiles)) {
        const folderPath = Path.join(collectionDir, folder);
        fs.mkdirSync(folderPath, { recursive: true });
        for (const [name, text] of Object.entries(files))
            fs.writeFileSync(Path.join(folderPath, name), text);
    }
}

/** The diagnostic report that the dialog's Learn More shows, once it has loaded. */
async function readDiagnosticReport(dialogPage: Page): Promise<string> {
    const report = dialogPage.locator(".problem-dialog #report");
    let text = "";
    await expect
        .poll(
            async () => {
                text = await report.innerText().catch(() => "");
                // The report lists the book's collection settings file last among its files.
                return /\.bloomCollection\s*$/m.test(text);
            },
            {
                timeout: 60000,
                message:
                    "Learn More never showed the diagnostic report with the files bundled with the book",
            },
        )
        .toBe(true);
    return text;
}

test("TC-447: a problem report keeps the collection's Allowed Words and Sample Texts in their folders", async ({
    bloomApp,
    problemDialogWatcher,
}) => {
    test.setTimeout(300000);
    await withBloomOfflineForReports(bloomApp, async (offline) => {
        const page = offline.page;
        const collectionDir = bloomApp.collectionDir;

        await test.step("make a decodable reader whose stages come from allowed-word lists", async () => {
            await makeBookFromTemplate(page, "Decodable Reader");
            writeReaderFiles(collectionDir);
            await changeSavedReaderSettings(page, (settings) => {
                settings.useAllowedWords = 1;
                settings.stages = Object.keys(
                    kReaderFiles["Allowed Words"],
                ).map((file, index) => ({
                    name: String(index + 1),
                    letters: "",
                    sightWords: "",
                    allowedWordsFile: file,
                }));
            });
        });

        await test.step("add a Just Text page and type on it", async () => {
            await addPage(page, "Just Text");
            await typeInGroup(page, ".bloom-translationGroup", "en", kPageText);
        });

        const resumeWatcher = await problemDialogWatcher.pause();
        try {
            const dialogPage =
                await test.step("open Report a Problem", async () => {
                    await chooseReportAProblemFromHelpMenu(page);
                    return waitForReportDialogPage(page);
                });
            await expect(
                dialogPage.locator(".problem-dialog .includeBook input"),
                "Report a Problem should offer to include the book, ticked",
            ).toBeChecked();

            await test.step("Learn More lists each reader file in its folder", async () => {
                await dialogPage.getByTestId("Common.LearnMore").click();
                const report = await readDiagnosticReport(dialogPage);
                const listed = report.split(/\r?\n/).map((line) => line.trim());
                for (const [folder, files] of Object.entries(kReaderFiles)) {
                    for (const name of Object.keys(files)) {
                        expect(
                            listed,
                            `the diagnostic report should list ${folder}\\${name}`,
                        ).toContain(Path.join(collectionDir, folder, name));
                    }
                }
                await dialogPage.getByTestId("Common.BackButton").click();
            });

            const zipPath =
                await test.step("submit the report offline; Bloom offers a zip to email", () =>
                    submitReportOffline(
                        offline,
                        dialogPage,
                        `TC-447 e2e: reader folders in a problem report ${Date.now()}`,
                        "tc447-e2e@example.com",
                    ));

            await test.step("the zipped book keeps the Allowed Words and Sample Texts folders", async () => {
                const entries = readZip(fs.readFileSync(zipPath));
                const bookZip = entries.find((e) =>
                    e.name.endsWith(".BloomProblemBook"),
                );
                expect(
                    bookZip,
                    `the report's zip should hold the zipped book; it holds ${entries.map((e) => e.name).join(", ")}`,
                ).toBeDefined();
                const bookEntries = readZip(bookZip!.content);
                const names = bookEntries.map((e) => e.name);
                for (const [folder, files] of Object.entries(kReaderFiles)) {
                    for (const [name, text] of Object.entries(files)) {
                        const entry = bookEntries.find(
                            (e) => e.name === `${folder}/${name}`,
                        );
                        expect(
                            entry,
                            `the zipped book should hold ${folder}/${name}; it holds ${names.join(", ")}`,
                        ).toBeDefined();
                        expect(
                            entry!.content.toString("utf8"),
                            `${folder}/${name} should be the collection's own file`,
                        ).toBe(text);
                    }
                }
                const flattened = names.filter((name) =>
                    Object.values(kReaderFiles).some((files) => name in files),
                );
                expect(
                    flattened,
                    "no reader file should be at the zipped book's top level",
                ).toEqual([]);
            });

            await test.step("close the dialog", async () => {
                await closeReactDialogFromNode(page);
                await expect
                    .poll(
                        async () =>
                            dialogPage.isClosed() ||
                            (await dialogPage
                                .locator(".problem-dialog")
                                .count()
                                .catch(() => 0)) === 0,
                        { timeout: 30000 },
                    )
                    .toBe(true);
            });
        } finally {
            resumeWatcher();
        }
    });
});
