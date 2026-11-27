// Bloom will not save a published file inside a collection folder, at any depth: a file there would
// be taken for part of the collection (MiscUtils.GetOutputFilePathOutsideCollectionFolder). When the
// person picks such a place in the Save As dialog, Bloom says it cannot save there, names the
// collection folder, and shows the Save As dialog again, so that they can pick another place.
//
// Automates the manual test "Block Publish Into Collection folder" (Test Case ID 401): a book is
// published as a PDF, an ePUB and a BloomPUB, each first saved into the collection folder or one of
// its subfolders, which Bloom refuses, and then into "Bloom Saved Files" under Documents, which it
// allows.

import * as fs from "node:fs";
import * as Path from "node:path";
import { execFileSync } from "node:child_process";
import { type Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { launchWithDontDisturb } from "../fixtures/launchBloom";
import { apiPost } from "../helpers/api";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import { readAndCloseBloomMessageBox } from "../helpers/bloomMessageBox";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    answerSaveAsDialog,
    getForegroundWindow,
} from "../helpers/nativeDialogs";
import { choosePdfMode } from "../helpers/pdfPrint";
import {
    selectPublishDestination,
    type PublishDestination,
} from "../helpers/publish";
import { setUiStateViaApi } from "../helpers/uiLanguage";

test.use({
    collectionSpec: {
        name: "publish-save-outside-collection",
        languages: ["en"],
        // Publishing an ePUB needs Pro.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

/** One way of publishing the book to a file, as its Publish screen offers it. */
interface IFormat {
    destination: PublishDestination;
    /** The data-testid of the screen's button that saves the file. */
    saveButton: string;
    /** The endpoint that the save button's work goes to, which answers once the file is saved. */
    endpoint: string;
    extension: string;
    /** Get the screen ready to save, once it shows. */
    prepare: (page: Page) => Promise<void>;
}

const kFormats: IFormat[] = [
    {
        destination: "PDF & Print",
        saveButton: "PublishTab.SaveButton",
        endpoint: "publish/pdf/save",
        extension: ".pdf",
        // Save PDF... is enabled once a mode is chosen and its PDF made.
        prepare: (page) => choosePdfMode(page, "Simple"),
    },
    {
        destination: "ePUB",
        saveButton: "PublishTab.Save",
        endpoint: "publish/epub/save",
        extension: ".epub",
        prepare: async () => {},
    },
    {
        destination: "BloomPUB",
        saveButton: "PublishTab.Save",
        endpoint: "publish/bloompub/file/save",
        extension: ".bloompub",
        // The screen's Save... button saves a file only when the method chosen is "file".
        prepare: async (page) => {
            await apiPost(page, "publish/bloompub/method", "file");
        },
    },
];

/** The Windows Documents folder, wherever this machine keeps it. */
function documentsFolder(): string {
    return execFileSync(
        "powershell",
        [
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            "[Environment]::GetFolderPath('MyDocuments')",
        ],
        { encoding: "utf8" },
    ).trim();
}

/**
 * Close the progress dialog that the ePUB and BloomPUB screens show while they save, once it offers
 * its Close button (PublishProgressDialogInner.tsx), if it has not gone by itself. Its Close button
 * is its only filled button; its label is localized, so it is found by how it is drawn.
 */
async function closeProgressDialog(
    page: Page,
    destination: PublishDestination,
): Promise<void> {
    const dialog = page.locator(".MuiDialog-root.progress-dialog");
    const close = dialog.locator(
        ".MuiDialogActions-root button.MuiButton-contained",
    );
    await expect
        .poll(
            async () =>
                (await dialog.count()) === 0 || (await close.isVisible()),
            {
                timeout: 60000,
                message: `The ${destination} progress dialog neither went away nor offered its Close button.`,
            },
        )
        .toBe(true);
    if ((await dialog.count()) > 0) await close.click();
    await expect(
        dialog,
        `The ${destination} progress dialog should be gone.`,
    ).toHaveCount(0, { timeout: 30000 });
}

/** Text with paths in it, as Windows compares paths: separators made one kind, letters one case. */
function comparable(text: string): string {
    return text.replace(/\//g, "\\").toLowerCase();
}

test("Bloom refuses to save a published file inside the collection folder, and saves it under Documents [Test Case ID 401]", async ({
    page,
    bloomApp,
    step,
}, testInfo) => {
    test.skip(
        launchWithDontDisturb(),
        "Save As takes the foreground, so this runs only where Bloom may take the foreground: on " +
            "CI, or with BLOOM_E2E_DONT_DISTURB=0.",
    );
    test.setTimeout(600000);

    const collectionDir = bloomApp.collectionDir;
    let bookFolder = "";
    await step("Make a book", async () => {
        // choosePdfMode finds the PDF screen's mode and Print button by their English labels.
        await setUiStateViaApi(bloomApp, "en", false);
        await makeBookFromTemplate(page, "Basic Book");
        const bookFolders = fs
            .readdirSync(collectionDir, { withFileTypes: true })
            .filter(
                (entry) =>
                    entry.isDirectory() &&
                    fs
                        .readdirSync(Path.join(collectionDir, entry.name))
                        .some((name) => name.endsWith(".htm")),
            );
        expect(
            bookFolders.map((entry) => entry.name),
            "The collection should hold the one new book.",
        ).toHaveLength(1);
        bookFolder = Path.join(collectionDir, bookFolders[0].name);
    });

    // Each format is first saved into a different place inside the collection: the collection
    // folder itself, a folder of the person's own in it, and the book's folder.
    const ownSubfolder = Path.join(collectionDir, "My Saved Files");
    fs.mkdirSync(ownSubfolder);
    const refusedFolders = [collectionDir, ownSubfolder, bookFolder];

    const savedFilesFolder = Path.join(documentsFolder(), "Bloom Saved Files");
    const madeSavedFilesFolder = !fs.existsSync(savedFilesFolder);
    fs.mkdirSync(savedFilesFolder, { recursive: true });
    // Unique to this run, so that no earlier run's file is in the way.
    const runTag = `TC-401-${process.pid}-${Date.now()}-${testInfo.retry}`;
    const savedFiles: string[] = [];

    try {
        for (const [index, format] of kFormats.entries()) {
            const refusedPath = Path.join(
                refusedFolders[index],
                `${runTag}${format.extension}`,
            );
            const allowedPath = Path.join(
                savedFilesFolder,
                `${runTag}${format.extension}`,
            );
            await step(
                `${format.destination}: Bloom refuses ${refusedPath}, then saves to ${allowedPath}`,
                async () => {
                    await selectPublishDestination(page, format.destination);
                    await format.prepare(page);
                    const save = page.getByTestId(format.saveButton);
                    await expect(
                        save,
                        `The ${format.destination} screen's save button should be enabled.`,
                    ).toBeEnabled({ timeout: 180000 });

                    const foregroundBefore = await getForegroundWindow();
                    const answered = page.waitForResponse(
                        (response) =>
                            response.request().method() === "POST" &&
                            new URL(response.url()).pathname.endsWith(
                                `/${format.endpoint}`,
                            ),
                        { timeout: 300000 },
                    );
                    await save.click();
                    await answerSaveAsDialog(
                        bloomApp.bloomPid,
                        refusedPath,
                        60000,
                        foregroundBefore,
                    );

                    const message = await readAndCloseBloomMessageBox(page);
                    // Checked by the paths it names, which read the same in every UI language.
                    expect(
                        comparable(message).includes(comparable(collectionDir)),
                        `Bloom's message should name the collection folder ${collectionDir}: "${message}"`,
                    ).toBe(true);
                    expect(
                        comparable(message).includes(comparable(refusedPath)),
                        `Bloom's message should name the place chosen, ${refusedPath}: "${message}"`,
                    ).toBe(true);

                    // Bloom shows the Save As dialog again; this time the place is allowed.
                    savedFiles.push(allowedPath);
                    await answerSaveAsDialog(
                        bloomApp.bloomPid,
                        allowedPath,
                        60000,
                        foregroundBefore,
                    );
                    const response = await answered;
                    expect(
                        response.ok(),
                        `Bloom should have finished ${format.endpoint}`,
                    ).toBe(true);
                    await expect
                        .poll(
                            () =>
                                fs.existsSync(allowedPath)
                                    ? fs.statSync(allowedPath).size
                                    : 0,
                            {
                                timeout: 120000,
                                message: `Bloom should have saved the ${format.destination} file to ${allowedPath}`,
                            },
                        )
                        .toBeGreaterThan(0);
                    expect(
                        fs.existsSync(refusedPath),
                        `Bloom should have written nothing to ${refusedPath}`,
                    ).toBe(false);
                    await closeProgressDialog(page, format.destination);
                },
            );
        }
    } finally {
        for (const file of savedFiles) fs.rmSync(file, { force: true });
        if (
            madeSavedFilesFolder &&
            fs.existsSync(savedFilesFolder) &&
            fs.readdirSync(savedFilesFolder).length === 0
        )
            fs.rmdirSync(savedFilesFolder);
    }
});
