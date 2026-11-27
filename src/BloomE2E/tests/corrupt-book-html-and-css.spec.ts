// A book whose .htm is unreadable, or whose basePage.css is broken, must not crash Bloom.
//
// - When the .htm cannot be read but its backup (bookhtml.bak) can, opening the book puts the
//   backup back in its place, keeps the broken file beside it, tells the person so in a dialog,
//   and opens the book for editing.
// - A basePage.css with no open braces leaves the book editable as though nothing happened.
// - When the .htm cannot be read and there is no backup, Bloom shows the book's problem in the
//   Collections tab's preview instead of opening it; the other books stay editable, and the broken
//   book can still be deleted.
//
// Automates the manual test "Handle Corrupt HTML CSS" (Test Case ID 684).

import * as fs from "node:fs";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { apiPost } from "../helpers/api";
import {
    addPage,
    editablePageFrame,
    getPages,
    editBook,
    makeBookFromTemplate,
    typeInGroup,
    waitForEditablePage,
} from "../helpers/bookMaking";
import {
    chooseFromSelectedBookMenuByTestId,
    clickBookTile,
    collectionBookPreview,
    findBookFolderById,
    listEditableBooks,
    selectBook,
    waitForCollectionReady,
} from "../helpers/collection";
import { wasMovedToRecycleBinSince } from "../helpers/fileExplorer";
import { chooseImageFile } from "../helpers/images";
import { setUiStateViaApi } from "../helpers/uiLanguage";
import { confirmRecycleBox } from "../helpers/winformsConfirmRecycle";
import { getTabs, switchTab, waitForActiveTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "corrupt-book-html-and-css", languages: ["en"] },
});

// Bloom's name for a book's backup of its .htm (BookStorage.BackupFilename).
const kBackupFile = "bookhtml.bak";
// What Bloom puts before the name of an unreadable .htm that it set aside to restore the backup
// (BookStorage.PrefixForCorruptHtmFiles).
const kBrokenPrefix = "_broken_";

const kGoodTitle = "Book that stays good";
const kEditedGoodTitle = "Book that stays good, edited";
const kBrokenTitle = "Book to break";
// Text the test types into the book to break, before and after it first leaves the book.
const kFirstText = "Text typed when the book was made.";
const kSecondText = "A small change made on coming back.";
const kThirdText = "Typed after the stylesheet broke.";

// What Bloom says, in English, when it restored a book's backup (BookStorage.CorruptBook), and
// what the preview of a book it cannot read starts with (Errors.BookProblem). The test pins the UI
// to English to compare them.
const kRestoredBackupNotice =
    "Bloom had a problem reading this book and recovered by restoring a recent backup.";
const kBookProblemNotice =
    "Bloom had a problem showing this book. This doesn't mean your work is lost";

const kPicture = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
    "bird.png",
);

/** The book's .htm: the one .htm in its folder whose name is not the broken prefix's. */
function bookHtmPath(folder: string): string {
    const htms = fs
        .readdirSync(folder)
        .filter((f) => /\.htm$/i.test(f) && !f.startsWith(kBrokenPrefix));
    expect(htms, `${folder} should hold exactly one book .htm`).toHaveLength(1);
    return Path.join(folder, htms[0]);
}

/** The files in `folder` that Bloom set aside as unreadable. */
function brokenFilesIn(folder: string): string[] {
    return fs.readdirSync(folder).filter((f) => f.startsWith(kBrokenPrefix));
}

/**
 * Make the book's .htm unreadable even to a web browser's lenient parser, as the manual case does:
 * <head> becomes <heead> and <body> becomes <boody>. Returns what the .htm now holds.
 */
function corruptHtm(folder: string): string {
    const path = bookHtmPath(folder);
    const html = fs.readFileSync(path, "utf8");
    expect(html, "test setup: the .htm has a <head>").toMatch(/<head>/);
    expect(html, "test setup: the .htm has a <body").toMatch(/<body[\s>]/);
    const broken = html
        .replace(/<head>/g, "<heead>")
        .replace(/<\/head>/g, "</heead>")
        .replace(/<body([\s>])/g, "<boody$1")
        .replace(/<\/body>/g, "</boody>");
    fs.writeFileSync(path, broken, "utf8");
    expect(
        fs.readFileSync(path, "utf8"),
        "test setup: the .htm has no <head> or <body> left",
    ).not.toMatch(/<head>|<body[\s>]/);
    return broken;
}

/** The size, in CSS pixels, of the page the Edit tab shows. */
async function readShownPageSize(
    page: Page,
): Promise<{ width: number; height: number }> {
    return editablePageFrame(page)
        .locator(".bloom-page")
        .evaluate((element) => {
            const box = element.getBoundingClientRect();
            return { width: box.width, height: box.height };
        });
}

/** The text of every Bloom notify/problem dialog showing in any page of Bloom's browser. */
async function readProblemDialogs(page: Page): Promise<string[]> {
    const browser = page.context().browser();
    const pages = browser
        ? browser.contexts().flatMap((c) => c.pages())
        : page.context().pages();
    const texts: string[] = [];
    for (const candidate of pages) {
        if (candidate.url().startsWith("devtools://")) continue;
        try {
            const dialogs = candidate.locator(
                ".problem-dialog .dialog-content",
            );
            for (const text of await dialogs.allInnerTexts())
                texts.push(text.replace(/\s+/g, " ").trim());
        } catch (error) {
            // A page that closed while we asked is not a dialog; any other failure is real.
            if (candidate.isClosed()) continue;
            throw error;
        }
    }
    return texts;
}

test("a book with an unreadable .htm or a broken basePage.css does not crash Bloom [Test Case ID 684]", async ({
    page,
    bloomApp,
    problemDialogWatcher,
    step,
}) => {
    test.setTimeout(600000);
    let goodFolder = "";
    let goodId = "";
    let brokenFolder = "";
    let brokenId = "";
    let pageCount = 0;
    let pageSize = { width: 0, height: 0 };
    let originalCss = "";
    let backupContent: string[] = [];
    let corruptedHtml = "";

    await step(
        "Make two books, one with pages of text and a picture",
        async () => {
            await setUiStateViaApi(bloomApp, "en", false);
            page = bloomApp.page;
            await makeBookFromTemplate(page, "Basic Book");
            await typeInGroup(page, ".bookTitle", "en", kGoodTitle);
            await makeBookFromTemplate(page, "Basic Book");
            await typeInGroup(page, ".bookTitle", "en", kBrokenTitle);
            await chooseImageFile(page, kPicture);
            await addPage(page, "Just Text", 2);
            await typeInGroup(
                page,
                ".bloom-translationGroup",
                "en",
                kFirstText,
            );
            await switchTab(page, "collection");
            await expect
                .poll(
                    async () =>
                        (await listEditableBooks(page))
                            .map((b) => b.title)
                            .sort(),
                    {
                        timeout: 30000,
                        message:
                            "Sanity check: the collection should hold the two books just made",
                    },
                )
                .toEqual([kBrokenTitle, kGoodTitle].sort());
            const books = await listEditableBooks(page);
            goodId = books.find((b) => b.title === kGoodTitle)!.id;
            goodFolder = findBookFolderById(bloomApp.collectionDir, goodId);
            brokenId = books.find((b) => b.title === kBrokenTitle)!.id;
            brokenFolder = findBookFolderById(bloomApp.collectionDir, brokenId);
        },
    );

    await step(
        "Come back to the book, make a small change, and leave it",
        async () => {
            await editBook(page, brokenFolder);
            await typeInGroup(
                page,
                ".bloom-translationGroup",
                "en",
                kSecondText,
            );
            await switchTab(page, "collection");
            await expect
                .poll(
                    () => fs.readFileSync(bookHtmPath(brokenFolder), "utf8"),
                    {
                        timeout: 30000,
                        message:
                            "Bloom never saved the small change to the book's .htm",
                    },
                )
                .toContain(kSecondText);
            expect(
                fs.existsSync(Path.join(brokenFolder, kBackupFile)),
                `Sanity check: Bloom should have made ${kBackupFile} for the book`,
            ).toBe(true);
            await editBook(page, brokenFolder);
            pageCount = (await getPages(page)).length;
            expect(
                pageCount,
                "Sanity check: the book should have its cover pages and the two pages added",
            ).toBeGreaterThan(2);
            await switchTab(page, "collection");
            // Bloom reopens the selected book at its next start; leave the good one selected, so that
            // the broken book is first read when the test opens it.
            await selectBook(page, goodFolder);
        },
    );

    // Step 2 of the case: the .htm is unreadable, and the backup is there.
    let current = await step(
        "Quit Bloom, make the book's .htm unreadable, and start Bloom",
        () =>
            bloomApp.restart(() => {
                const backup = fs.readFileSync(
                    Path.join(brokenFolder, kBackupFile),
                    "utf8",
                );
                backupContent = [kFirstText, kSecondText, "bird.png"].filter(
                    (content) => backup.includes(content),
                );
                expect(
                    backupContent,
                    "test setup: the backup should hold the picture and some of the typed text",
                ).toContain("bird.png");
                expect(
                    backupContent.length,
                    "test setup: the backup should hold some of the typed text",
                ).toBeGreaterThan(1);
                corruptedHtml = corruptHtm(brokenFolder);
            }),
    );
    if (!current) throw new Error("Bloom did not restart.");
    const restarted = current;

    await step(
        "Open the broken book: Bloom restores its backup and says so",
        async () => {
            await waitForCollectionReady(restarted);
            // This step raises Bloom's notice dialog on purpose and closes it itself.
            const resumeWatcher = await problemDialogWatcher.pause();
            try {
                await switchTab(restarted, "collection");
                // Bloom reads the book when it is selected, and shows its dialog modally while doing
                // so, so the selection is not awaited until the dialog has been closed.
                let selectError: unknown;
                const selected = clickBookTile(restarted, brokenId).catch(
                    (error: unknown) => {
                        selectError = error;
                    },
                );
                let shown: string[] = [];
                await expect
                    .poll(
                        async () => {
                            if (selectError) throw selectError;
                            shown = await readProblemDialogs(restarted);
                            return shown.length;
                        },
                        {
                            timeout: 60000,
                            message:
                                "Bloom never showed a dialog about the broken book",
                        },
                    )
                    .toBe(1);
                expect(
                    shown[0],
                    "the dialog should say that Bloom restored a backup of the book",
                ).toContain(kRestoredBackupNotice);
                await apiPost(restarted, "common/closeReactDialog");
                await expect
                    .poll(
                        async () =>
                            (await readProblemDialogs(restarted)).length,
                        {
                            timeout: 30000,
                            message: "the dialog never closed",
                        },
                    )
                    .toBe(0);
                await selected;
                if (selectError) throw selectError;
            } finally {
                resumeWatcher();
            }
            const broken = brokenFilesIn(brokenFolder);
            expect(
                broken,
                "Bloom should keep the unreadable .htm beside the book, under a _broken_ name",
            ).toHaveLength(1);
            expect(
                fs.readFileSync(Path.join(brokenFolder, broken[0]), "utf8"),
                "the file Bloom set aside should be the unreadable .htm, unchanged",
            ).toBe(corruptedHtml);
            const restored = fs.readFileSync(bookHtmPath(brokenFolder), "utf8");
            expect(
                restored,
                "the restored .htm should have its <head> back",
            ).toMatch(/<head>/);
            expect(
                restored,
                "the restored .htm should have its <body> back",
            ).toMatch(/<body[\s>]/);
            // The backup's text and picture come back with it.
            for (const content of backupContent)
                expect(
                    restored,
                    `the restored book should still hold "${content}" from its backup`,
                ).toContain(content);
        },
    );

    await step("The book opens for editing", async () => {
        await switchTab(restarted, "edit");
        await waitForEditablePage(restarted);
        await expect(
            editablePageFrame(restarted).locator(".bloom-page"),
            "the Edit tab should show a page of the restored book",
        ).toHaveCount(1);
        expect(
            (await getPages(restarted)).length,
            "the restored book should have all its pages",
        ).toBe(pageCount);
        pageSize = await readShownPageSize(restarted);
        expect(
            pageSize.width,
            "Sanity check: the page shown should have a size",
        ).toBeGreaterThan(0);
    });

    // Step 3 of the case: basePage.css loses all its open braces.
    current = await step(
        "Quit Bloom, remove every { from the book's basePage.css, and start Bloom",
        () =>
            bloomApp.restart(() => {
                const css = Path.join(brokenFolder, "basePage.css");
                originalCss = fs.readFileSync(css, "utf8");
                expect(
                    originalCss,
                    "test setup: basePage.css has open braces",
                ).toContain("{");
                fs.writeFileSync(css, originalCss.replace(/\{/g, ""), "utf8");
            }),
    );
    if (!current) throw new Error("Bloom did not restart.");
    const afterCss = current;

    await step("The book is editable as though nothing happened", async () => {
        await waitForCollectionReady(afterCss);
        await editBook(afterCss, brokenFolder);
        expect(
            fs.readFileSync(Path.join(brokenFolder, "basePage.css"), "utf8"),
            "Bloom should have put the book's basePage.css back as it was",
        ).toBe(originalCss);
        expect(
            await readShownPageSize(afterCss),
            "the page should be laid out as it was before the stylesheet broke",
        ).toEqual(pageSize);
        await typeInGroup(
            afterCss,
            ".bloom-translationGroup",
            "en",
            kThirdText,
        );
        await switchTab(afterCss, "collection");
        await expect
            .poll(() => fs.readFileSync(bookHtmPath(brokenFolder), "utf8"), {
                timeout: 30000,
                message:
                    "Bloom never saved what was typed after the stylesheet broke",
            })
            .toContain(kThirdText);
        await selectBook(afterCss, goodFolder);
    });

    // Step 4 of the case: the .htm is unreadable, and there is no backup.
    current = await step(
        "Quit Bloom, delete the backup, make the .htm unreadable, and start Bloom",
        () =>
            bloomApp.restart(() => {
                fs.rmSync(Path.join(brokenFolder, kBackupFile));
                corruptHtm(brokenFolder);
            }),
    );
    if (!current) throw new Error("Bloom did not restart.");
    const noBackup = current;

    await step(
        "Select the broken book: the preview shows its problem",
        async () => {
            await waitForCollectionReady(noBackup);
            await waitForActiveTab(noBackup, "collection");
            await clickBookTile(noBackup, brokenId);
            await expect(
                collectionBookPreview(noBackup).locator("body"),
                "the preview should show Bloom's notice about the book's problem",
            ).toContainText(kBookProblemNotice, { timeout: 60000 });
            // A book Bloom cannot read is not offered for editing.
            expect((await getTabs(noBackup)).tabStates.edit).toBe("hidden");
            expect(
                brokenFilesIn(brokenFolder),
                "with no backup to restore, Bloom should set nothing more aside",
            ).toHaveLength(1);
        },
    );

    await step("Another book in the collection is still editable", async () => {
        await editBook(noBackup, goodFolder);
        await typeInGroup(noBackup, ".bookTitle", "en", kEditedGoodTitle);
        await switchTab(noBackup, "collection");
        // A new title renames the book's folder, so find it again by its id.
        await expect
            .poll(
                () =>
                    fs.readFileSync(
                        bookHtmPath(
                            findBookFolderById(bloomApp.collectionDir, goodId),
                        ),
                        "utf8",
                    ),
                {
                    timeout: 30000,
                    message:
                        "Bloom never saved what was typed in the other book",
                },
            )
            .toContain(kEditedGoodTitle);
    });

    await step("The broken book can be deleted", async () => {
        await clickBookTile(noBackup, brokenId);
        await expect(
            collectionBookPreview(noBackup).locator("body"),
            "Sanity check: the broken book should be the one selected",
        ).toContainText(kBookProblemNotice, { timeout: 60000 });
        // The file system may round the Recycle Bin record's time down, so allow a second.
        const beforeDelete = new Date(Date.now() - 1000);
        await chooseFromSelectedBookMenuByTestId(
            noBackup,
            "CollectionTab.BookMenu.DeleteBook",
        );
        await confirmRecycleBox(bloomApp.bloomPid, 30000);
        await expect
            .poll(
                async () =>
                    (await listEditableBooks(noBackup)).map((b) => b.id),
                {
                    timeout: 30000,
                    message:
                        "the broken book should leave the collection, and only it",
                },
            )
            .toEqual([goodId]);
        expect(
            fs.existsSync(brokenFolder),
            "the broken book's folder should be gone from the collection",
        ).toBe(false);
        expect(
            await wasMovedToRecycleBinSince(brokenFolder, beforeDelete),
            "Delete Book should move the broken book's folder to the Recycle Bin",
        ).toBe(true);
    });
});
