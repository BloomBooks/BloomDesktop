// The collection's own menu on the Collections tab (the triangle beside the collection's name):
// it offers its commands, and each one does its work. Open or Create Another Collection shows the
// collection chooser; Import .bloomSource File(s) adds the chosen book; Troubleshooting > Do Checks
// of All Books finds no problem in a new book whose cover has no picture yet (BL-15712), and does
// find a picture that is missing; Do Updates of All Books puts back a support file a book lost;
// Rescue Missing Images puts the missing picture back from the folder chosen, and says nothing is
// missing any more (BL-15713); Make Bloom Pack of Shell Books and Make Reader Template Bloom Pack
// write a Bloom Pack of the collection, the second with its books marked as templates.
//
// Automates the manual test "Collection Dropdown Menu" (Test Case ID 660).
//
// Each Troubleshooting command still ends with a plain Windows message box, which
// helpers/nativeDialogs.ts reads and closes over UI Automation. Save As for a Bloom Pack is
// answered by arming it (armFileChooser), so it never shows. The commands that save a file or open
// a log are in a test of their own: Bloom opens the log in Notepad and the Bloom Pack in File
// Explorer, so that test does not run where it would take a developer's keyboard; see "Collection
// menu commands open Notepad and File Explorer" in AUTOMATION-DEBT.md.

import { createHash, randomUUID } from "node:crypto";
import * as fs from "node:fs";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { launchWithDontDisturb } from "../fixtures/launchBloom";
import {
    findBookFolder,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import { readZip } from "../helpers/bulkBloomPub";
import {
    chooseFromCollectionMenu,
    closeCollectionMenu,
    listEditableBooks,
    openCollectionMenu,
    readCollectionMenu,
} from "../helpers/collection";
import { chooseImageFile, getImagePlacement } from "../helpers/images";
import {
    armFileChooser,
    getForegroundWindow,
    readAndCloseMessageBox,
} from "../helpers/nativeDialogs";
import { writeZip } from "../helpers/spreadsheetEditing";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "collection-menu", languages: ["en"] },
});

// The menu's entries, by their test ids (their localization ids), as CollectionsTabPane.tsx lists
// them.
const kOpenOrCreateCollection = "CollectionTab.OpenCreateCollectionMenuItem";
const kImportBloomSource = "CollectionTab.ImportBloomSource";
const kMakeShellBloomPack = "CollectionTab.MakeBloomPackOfShellBooks";
const kMakeReaderTemplateBloomPack =
    "CollectionTab.AddMakeReaderTemplateBloomPackToolStripMenuItem";
const kTroubleshooting = "CollectionTab.ContextMenu.Troubleshooting";
const kDoChecks = "CollectionTab.CollectionMenu.doChecksOfAllBooks";
const kDoUpdates = "CollectionTab.CollectionMenu.doChecksAndUpdatesOfAllBooks";
const kRescueMissingImages = "CollectionTab.CollectionMenu.rescueMissingImages";

// What Bloom's message boxes say at the end of the Troubleshooting commands (CollectionModel.cs).
// Bloom does not localize them.
const kNoProblems = "Bloom didn't find any problems.";
const kProblemsFound = "Bloom will now open a list of problems it found.";
const kNoMoreMissingImages =
    "There are no more missing images. Bloom will now open a log of what it did.";

/** A support file that Bloom copies into every book folder and puts back when it updates a book. */
const kSupportFile = "origami.css";

/** A picture of the suite's fixtures. */
const kBird = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
    "bird.png",
);

/** A hash of the file at `path`, to tell whether two files hold the same bytes. */
function hashFile(path: string): string {
    return createHash("sha256").update(fs.readFileSync(path)).digest("hex");
}

/** Make a book from Basic Book with the title `title`, and return its folder once it is saved. */
async function makeTitledBook(page: Page, title: string): Promise<string> {
    await makeBookFromTemplate(page, "Basic Book");
    await typeInGroup(page, ".bookTitle", "en", title);
    await switchTab(page, "collection");
    return findBookFolder(page, title);
}

/**
 * Run `act`, which starts a command of the collection menu and answers whatever message box it
 * shows, and wait until Bloom has finished the command, which is when it answers the menu's request
 * for `endpoint`: Bloom answers only once its dialogs are closed. Returns what `act` returned.
 */
async function runCollectionCommand<T>(
    page: Page,
    endpoint: string,
    act: () => Promise<T>,
): Promise<T> {
    const answered = page.waitForResponse(
        (response) =>
            response.request().method() === "POST" &&
            new URL(response.url()).pathname.endsWith(`/${endpoint}`),
        { timeout: 180000 },
    );
    const result = await act();
    const response = await answered;
    expect(response.ok(), `Bloom should have done ${endpoint}`).toBe(true);
    return result;
}

/** Choose Troubleshooting > Do Checks of All Books, and return what Bloom's message box says at its end. */
function doChecksOfAllBooks(page: Page, bloomPid: number): Promise<string> {
    return runCollectionCommand(
        page,
        "collections/doChecksOfAllBooks",
        async () => {
            const foregroundBefore = await getForegroundWindow();
            await chooseFromCollectionMenu(page, [kTroubleshooting, kDoChecks]);
            return readAndCloseMessageBox(bloomPid, 120000, foregroundBefore);
        },
    );
}

/** The files of a Bloom Pack, by their path in it, once Bloom has written all of it. */
function readBloomPack(path: string): Map<string, Buffer> {
    expect(fs.existsSync(path), `Bloom should have written ${path}`).toBe(true);
    return new Map(
        readZip(fs.readFileSync(path)).map((e) => [e.name, e.content]),
    );
}

/** The book `bookFolder`'s meta.json in a Bloom Pack of its collection; fails when it is not there. */
function bloomPackMetaJson(
    pack: Map<string, Buffer>,
    bookFolder: string,
): { bookInstanceId: string; suitableForMakingShells?: boolean } {
    const collectionName = Path.basename(Path.dirname(bookFolder));
    const bookName = Path.basename(bookFolder);
    // A Bloom Pack of a collection holds the collection's folder, with its books inside.
    const entry = pack.get(`${collectionName}/${bookName}/meta.json`);
    expect(
        pack.has(`${collectionName}/${bookName}/${bookName}.htm`),
        `The Bloom Pack should hold the book "${bookName}"; it holds ${[...pack.keys()].join(", ")}`,
    ).toBe(true);
    expect(
        entry,
        `The Bloom Pack should hold the meta.json of "${bookName}"`,
    ).toBeDefined();
    return JSON.parse(entry!.toString("utf8"));
}

test("The collection menu offers its commands, and those that need no saved file do their work [Test Case ID 660]", async ({
    page,
    bloomApp,
    step,
}, testInfo) => {
    let bookFolder = "";
    await step("Make a book whose cover has no picture yet", async () => {
        bookFolder = await makeTitledBook(page, "Menu book");
        const html = fs.readFileSync(
            Path.join(bookFolder, `${Path.basename(bookFolder)}.htm`),
            "utf8",
        );
        expect(
            html,
            "Sanity check: the new book's cover should still show the placeholder picture",
        ).toContain("placeHolder.png");
    });

    await step("The menu offers every command", async () => {
        await openCollectionMenu(page);
        expect(await readCollectionMenu(page)).toEqual([
            kOpenOrCreateCollection,
            kImportBloomSource,
            kMakeShellBloomPack,
            kMakeReaderTemplateBloomPack,
            kTroubleshooting,
        ]);
        expect(await readCollectionMenu(page, kTroubleshooting)).toEqual([
            kDoChecks,
            kDoUpdates,
            kRescueMissingImages,
        ]);
        await closeCollectionMenu(page);
    });

    await step(
        "Open or Create Another Collection shows the collection chooser",
        async () => {
            await chooseFromCollectionMenu(page, [kOpenOrCreateCollection]);
            const collectionName = Path.basename(bloomApp.collectionDir);
            const dialog = page.getByRole("dialog");
            // The chooser offers the collections Bloom opened lately, each as a card: a button holding
            // the collection's name in a paragraph of its own.
            await expect(
                dialog.getByRole("button").filter({
                    has: page
                        .locator("p")
                        .getByText(collectionName, { exact: true }),
                }),
                "The collection chooser should offer the collection that is open",
            ).toBeVisible({ timeout: 30000 });
            await page.keyboard.press("Escape");
            await expect(
                dialog,
                "Escape should close the collection chooser",
            ).toHaveCount(0);
        },
    );

    const importedId = randomUUID();
    await step("Import .bloomSource File(s) adds the chosen book", async () => {
        // A .bloomSource is a zip of one book's folder, its files at the zip's root. This one is a
        // copy of the book made above, given an id of its own so that it is a new book here.
        const entries = fs
            .readdirSync(bookFolder, { recursive: true, withFileTypes: true })
            .filter((e) => e.isFile())
            .map((e) => {
                const full = Path.join(e.parentPath, e.name);
                const name = Path.relative(bookFolder, full)
                    .split(Path.sep)
                    .join("/");
                let data = fs.readFileSync(full);
                if (name === "meta.json") {
                    const meta = JSON.parse(data.toString("utf8"));
                    meta.bookInstanceId = importedId;
                    data = Buffer.from(JSON.stringify(meta), "utf8");
                }
                return { name, data };
            });
        const bloomSource = testInfo.outputPath("Imported book.bloomSource");
        fs.mkdirSync(Path.dirname(bloomSource), { recursive: true });
        fs.writeFileSync(bloomSource, writeZip(entries));

        expect(
            (await listEditableBooks(page)).map((b) => b.id),
            "Sanity check: the collection should not have the book to import yet",
        ).not.toContain(importedId);
        await armFileChooser(page, bloomSource);
        await chooseFromCollectionMenu(page, [kImportBloomSource]);
        // Bloom asks whether to import the book to edit or as a derivative.
        const dialog = page.getByRole("dialog");
        await dialog.locator('input[type="radio"][value="edit"]').check();
        await dialog.getByTestId("dialog-ok").click();
        const otherBooks = async () =>
            (await listEditableBooks(page)).filter(
                (b) => Path.resolve(b.folderPath) !== Path.resolve(bookFolder),
            );
        await expect
            .poll(async () => (await otherBooks()).map((b) => b.id), {
                timeout: 60000,
                message: "The imported book should join the collection",
            })
            .toEqual([importedId]);
        // Bloom names the folder of a book whose title another book has with the title and its id.
        const imported = (await otherBooks())[0].folderPath;
        const importedHtml = fs.readFileSync(
            Path.join(imported, `${Path.basename(imported)}.htm`),
            "utf8",
        );
        expect(
            importedHtml,
            "The imported book should have the chosen book's title",
        ).toContain("Menu book");
    });

    await step(
        "Do Checks of All Books finds no problem in a book whose cover has no picture (BL-15712)",
        async () => {
            const message = await doChecksOfAllBooks(page, bloomApp.bloomPid);
            expect(message).toBe(kNoProblems);
        },
    );

    await step(
        "Do Updates of All Books puts back a support file each book lost",
        async () => {
            const books = await listEditableBooks(page);
            expect(
                books.length,
                "Sanity check: the collection should hold the two books",
            ).toBe(2);
            const supportFiles = books.map((b) =>
                Path.join(b.folderPath, kSupportFile),
            );
            for (const file of supportFiles) {
                expect(
                    fs.existsSync(file),
                    `Sanity check: ${file} should be there to remove`,
                ).toBe(true);
                fs.rmSync(file);
            }
            await runCollectionCommand(
                page,
                "collections/doUpdatesOfAllBooks",
                async () => {
                    await chooseFromCollectionMenu(page, [
                        kTroubleshooting,
                        kDoUpdates,
                    ]);
                },
            );
            for (const file of supportFiles)
                expect(
                    fs.existsSync(file),
                    `Do Updates should have put back ${file}`,
                ).toBe(true);
        },
    );
});

test("The collection menu's commands that save a Bloom Pack or open a log do their work [Test Case ID 660]", async ({
    page,
    bloomApp,
    step,
}, testInfo) => {
    test.skip(
        launchWithDontDisturb(),
        "Bloom opens Notepad and File Explorer, so this runs only " +
            "where Bloom may take the foreground: on CI, or with BLOOM_E2E_DONT_DISTURB=0.",
    );
    let bookFolder = "";
    let pictureFile = "";
    await step("Make a book with a picture on its cover", async () => {
        await makeBookFromTemplate(page, "Basic Book");
        await chooseImageFile(page, kBird);
        pictureFile = (await getImagePlacement(page)).fileName;
        await typeInGroup(page, ".bookTitle", "en", "Pack book");
        await switchTab(page, "collection");
        bookFolder = await findBookFolder(page, "Pack book");
        expect(
            fs.existsSync(Path.join(bookFolder, pictureFile)),
            `Sanity check: the book should hold its picture ${pictureFile}`,
        ).toBe(true);
    });

    await step(
        "Make Bloom Pack of Shell Books writes the collection to a Bloom Pack",
        async () => {
            const packPath = testInfo.outputPath("Shell books.BloomPack");
            fs.mkdirSync(Path.dirname(packPath), { recursive: true });
            await armFileChooser(page, packPath);
            await runCollectionCommand(
                page,
                "collections/makeShellBooksBloompack",
                () => chooseFromCollectionMenu(page, [kMakeShellBloomPack]),
            );
            const pack = readBloomPack(packPath);
            const meta = bloomPackMetaJson(pack, bookFolder);
            const metaOnDisk = JSON.parse(
                fs.readFileSync(Path.join(bookFolder, "meta.json"), "utf8"),
            );
            expect(meta.bookInstanceId).toBe(metaOnDisk.bookInstanceId);
            // A shell book goes as it is: not marked as a template.
            expect(meta.suitableForMakingShells ?? false).toBe(false);
            const collectionName = Path.basename(bloomApp.collectionDir);
            const bookName = Path.basename(bookFolder);
            expect(
                pack.get(`${collectionName}/${bookName}/${pictureFile}`),
                "The Bloom Pack should hold the book's picture as it is",
            ).toEqual(fs.readFileSync(Path.join(bookFolder, pictureFile)));
        },
    );

    await step(
        "Make Reader Template Bloom Pack writes the collection's books as templates",
        async () => {
            const packPath = testInfo.outputPath("Reader templates.BloomPack");
            await chooseFromCollectionMenu(page, [
                kMakeReaderTemplateBloomPack,
            ]);
            const dialog = page.getByRole("dialog");
            const save = dialog.locator("#saveButton");
            const checkbox = dialog.getByRole("checkbox");
            // Save waits both for the dialog's list of the books it will make into templates and
            // for the checkbox. Tick the box, wait until Save is offered (so the list has loaded),
            // then untick it: Save must be withdrawn, since only the checkbox holds it back now.
            await expect(
                dialog.getByText("Pack book", { exact: true }),
                "The dialog should list the collection's book",
            ).toBeVisible({ timeout: 30000 });
            await expect(checkbox).not.toBeChecked();
            await checkbox.check();
            await expect(save).toBeEnabled({ timeout: 30000 });
            await checkbox.uncheck();
            await expect(
                save,
                "Save should be offered only while the checkbox is ticked",
            ).toBeDisabled();
            await checkbox.check();
            await expect(save).toBeEnabled();
            await armFileChooser(page, packPath);
            await runCollectionCommand(page, "collections/makeBloompack", () =>
                save.click(),
            );
            const meta = bloomPackMetaJson(readBloomPack(packPath), bookFolder);
            expect(meta.suitableForMakingShells).toBe(true);
            // Bloom leaves the collection menu open behind this dialog, so it shows again once the
            // dialog closes (BL-17214). Close it, as a person would, so that the next command can
            // open the menu.
            if (await page.locator('[role="menuitem"]:visible').count())
                await closeCollectionMenu(page);
        },
    );

    const savedPicture = testInfo.outputPath("rescue", pictureFile);
    await step(
        "Do Checks of All Books finds a picture that is missing",
        async () => {
            const bookPicture = Path.join(bookFolder, pictureFile);
            expect(
                await doChecksOfAllBooks(page, bloomApp.bloomPid),
                "Sanity check: Do Checks should find no problem while the picture is there",
            ).toBe(kNoProblems);
            fs.mkdirSync(Path.dirname(savedPicture), { recursive: true });
            fs.copyFileSync(bookPicture, savedPicture);
            fs.rmSync(bookPicture);
            const message = await doChecksOfAllBooks(page, bloomApp.bloomPid);
            expect(message).toBe(kProblemsFound);
        },
    );

    await step(
        "Rescue Missing Images puts the picture back from the folder chosen (BL-15713)",
        async () => {
            const bookPicture = Path.join(bookFolder, pictureFile);
            expect(
                fs.existsSync(bookPicture),
                "Sanity check: the picture should be missing",
            ).toBe(false);
            await armFileChooser(page, Path.dirname(savedPicture));
            const message = await runCollectionCommand(
                page,
                "collections/rescueMissingImages",
                async () => {
                    const foregroundBefore = await getForegroundWindow();
                    await chooseFromCollectionMenu(page, [
                        kTroubleshooting,
                        kRescueMissingImages,
                    ]);
                    return readAndCloseMessageBox(
                        bloomApp.bloomPid,
                        120000,
                        foregroundBefore,
                    );
                },
            );
            expect(message).toBe(kNoMoreMissingImages);
            expect(
                fs.existsSync(bookPicture),
                "The picture should be back in the book",
            ).toBe(true);
            expect(hashFile(bookPicture)).toBe(hashFile(savedPicture));
        },
    );
});
