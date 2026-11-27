// A book whose .htm is gone but whose backup (bookhtml.bak) is still there is not lost: when the
// person selects it, Bloom puts a copy of the backup in the .htm's place, tells the person so in a
// dialog, and opens the book for editing.
//
// Automates the manual test "Recover Missing HTML" (Test Case ID 33).

import * as fs from "node:fs";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editablePageFrame,
    editBook,
    getPages,
    makeBookFromTemplate,
    typeInGroup,
    waitForEditablePage,
} from "../helpers/bookMaking";
import {
    findBookFolderById,
    listEditableBooks,
    selectBook,
    waitForCollectionReady,
} from "../helpers/collection";
import { chooseImageFile } from "../helpers/images";
import { clickBookTileThroughOneProblemDialog } from "../helpers/problemDialogs";
import { setUiStateViaApi } from "../helpers/uiLanguage";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "recover-missing-book-html", languages: ["en"] },
});

// Bloom's name for a book's backup of its .htm (BookStorage.BackupFilename).
const kBackupFile = "bookhtml.bak";
// What Bloom puts before the name of an unreadable .htm that it set aside to restore the backup
// (BookStorage.PrefixForCorruptHtmFiles).
const kBrokenPrefix = "_broken_";

const kRecoveredTitle = "Book that loses its html";
const kOtherTitle = "Book left selected";
// Text the test types into the book, before and after it first leaves the book.
const kFirstText = "Text typed when the book was made.";
const kSecondText = "A small change made on coming back.";
// What the test makes of the typed text in the backup, so that the recovered book shows that it
// came from the backup.
const kBackupVersion = "The typed text, as the backup has it.";

// What Bloom says, in English, when it restored a book's backup (BookStorage.CorruptBook). The
// test pins the UI to English to compare it.
const kRestoredBackupNotice =
    "Bloom had a problem reading this book and recovered by restoring a recent backup.";

const kPicture = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
    "bird.png",
);

/** The names of the .htm files in `folder`. */
function htmFilesIn(folder: string): string[] {
    return fs.readdirSync(folder).filter((f) => /\.html?$/i.test(f));
}

/** The book's .htm: the one .htm in its folder. */
function bookHtmPath(folder: string): string {
    const htms = htmFilesIn(folder);
    expect(htms, `${folder} should hold exactly one .htm`).toHaveLength(1);
    return Path.join(folder, htms[0]);
}

test("a book whose .htm was deleted is recovered from its backup [Test Case ID 33]", async ({
    page,
    bloomApp,
    problemDialogWatcher,
    step,
}) => {
    test.setTimeout(400000);
    let folder = "";
    let bookId = "";
    let otherFolder = "";
    let htmName = "";
    let pageCount = 0;

    await step(
        "Make a book with pages of text and a picture, and another book",
        async () => {
            await setUiStateViaApi(bloomApp, "en", false);
            page = bloomApp.page;
            await makeBookFromTemplate(page, "Basic Book");
            await typeInGroup(page, ".bookTitle", "en", kRecoveredTitle);
            await chooseImageFile(page, kPicture);
            await addPage(page, "Just Text", 2);
            await typeInGroup(
                page,
                ".bloom-translationGroup",
                "en",
                kFirstText,
            );
            await makeBookFromTemplate(page, "Basic Book");
            await typeInGroup(page, ".bookTitle", "en", kOtherTitle);
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
                .toEqual([kOtherTitle, kRecoveredTitle].sort());
            const books = await listEditableBooks(page);
            bookId = books.find((b) => b.title === kRecoveredTitle)!.id;
            folder = findBookFolderById(bloomApp.collectionDir, bookId);
            otherFolder = findBookFolderById(
                bloomApp.collectionDir,
                books.find((b) => b.title === kOtherTitle)!.id,
            );
        },
    );

    // Bloom backs up a book's .htm when it saves the book again, so the test comes back to it once.
    await step(
        "Come back to the book, make a small change, and leave it",
        async () => {
            await editBook(page, folder);
            await typeInGroup(
                page,
                ".bloom-translationGroup",
                "en",
                kSecondText,
            );
            await switchTab(page, "collection");
            await expect
                .poll(() => fs.readFileSync(bookHtmPath(folder), "utf8"), {
                    timeout: 30000,
                    message:
                        "Bloom never saved the small change to the book's .htm",
                })
                .toContain(kSecondText);
            expect(
                fs.existsSync(Path.join(folder, kBackupFile)),
                `Sanity check: Bloom should have made ${kBackupFile} for the book`,
            ).toBe(true);
            htmName = Path.basename(bookHtmPath(folder));
            await editBook(page, folder);
            pageCount = (await getPages(page)).length;
            expect(
                pageCount,
                "Sanity check: the book should have its cover pages and the two pages added",
            ).toBeGreaterThan(2);
            await switchTab(page, "collection");
            // Bloom reopens the selected book at its next start; leave the other one selected, so
            // that the test selects this book itself and can watch for Bloom's dialog.
            await selectBook(page, otherFolder);
        },
    );

    const current = await step(
        "Quit Bloom, delete the book's .htm but leave its backup, and start Bloom",
        () =>
            bloomApp.restart(() => {
                const backupPath = Path.join(folder, kBackupFile);
                const backup = fs.readFileSync(backupPath, "utf8");
                const typed = [kSecondText, kFirstText].find((text) =>
                    backup.includes(text),
                );
                expect(
                    typed,
                    `test setup: ${kBackupFile} should hold text the test typed`,
                ).toBeDefined();
                const backupAsLeft = backup.split(typed!).join(kBackupVersion);
                fs.writeFileSync(backupPath, backupAsLeft, "utf8");
                fs.rmSync(bookHtmPath(folder));
                expect(
                    htmFilesIn(folder),
                    "test setup: the book's folder should have no .htm left",
                ).toEqual([]);
            }),
    );
    if (!current) throw new Error("Bloom did not restart.");
    const restarted = current;

    await step(
        "Select the book: Bloom restores its backup and says so",
        async () => {
            await waitForCollectionReady(restarted);
            await switchTab(restarted, "collection");
            expect(
                (await listEditableBooks(restarted)).map((b) => b.id),
                "the book with no .htm should still be in the collection",
            ).toContain(bookId);
            expect(
                await clickBookTileThroughOneProblemDialog(
                    restarted,
                    problemDialogWatcher,
                    bookId,
                ),
                "the dialog should say that Bloom restored a backup of the book",
            ).toContain(kRestoredBackupNotice);
            expect(
                htmFilesIn(folder),
                "Bloom should have put the book's .htm back under its name",
            ).toEqual([htmName]);
            const restored = fs.readFileSync(
                Path.join(folder, htmName),
                "utf8",
            );
            expect(
                restored,
                "the restored book should hold the backup's version of the text",
            ).toContain(kBackupVersion);
            expect(
                restored,
                "the restored book should still show its picture",
            ).toContain("bird.png");
            expect(
                fs
                    .readdirSync(folder)
                    .filter((f) => f.startsWith(kBrokenPrefix)),
                "with no .htm to set aside, Bloom should set nothing aside",
            ).toEqual([]);
        },
    );

    await step("The book opens for editing, with all its pages", async () => {
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
    });
});
