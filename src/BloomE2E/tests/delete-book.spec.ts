// Delete Book on the Collections tab: the book's menu offers Delete Book, Bloom asks whether to
// move the book to the Recycle Bin, and once that is confirmed the book's folder goes to the
// Recycle Bin, its tile leaves the collection, and no book is left selected. The other book in the
// collection stays as it was.
//
// Automates the manual test "Delete A Book" (Test Case ID 388).

import * as fs from "node:fs";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { makeBookFromTemplate, typeInGroup } from "../helpers/bookMaking";
import {
    chooseFromSelectedBookMenuByTestId,
    clickBookTile,
    expectBookInPreview,
    expectNoBookInPreview,
    findBookFolderById,
    listEditableBooks,
} from "../helpers/collection";
import { wasMovedToRecycleBinSince } from "../helpers/fileExplorer";
import { confirmRecycleBox } from "../helpers/winformsConfirmRecycle";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "delete-book", languages: ["en"] },
});

// The titles of the two books, which also name their folders.
const kKeptTitle = "Book to keep";
const kDeletedTitle = "Book to delete";

/** The tile of the book with id `bookId` on the Collections tab. */
function bookTile(page: Page, bookId: string) {
    return page.locator(`.book-button[data-book-id="${bookId}"]`);
}

test("Delete Book moves the selected book to the Recycle Bin and out of the collection [Test Case ID 388]", async ({
    page,
    bloomApp,
    step,
}) => {
    let keptId = "";
    let deletedId = "";
    let keptFolder = "";
    let deletedFolder = "";

    await step("Make two books and show the Collections tab", async () => {
        await makeBookFromTemplate(page, "Basic Book");
        await typeInGroup(page, ".bookTitle", "en", kDeletedTitle);
        await makeBookFromTemplate(page, "Basic Book");
        await typeInGroup(page, ".bookTitle", "en", kKeptTitle);
        await switchTab(page, "collection");
        // The collection learns a title when the book is saved, a moment after its page is left.
        await expect
            .poll(
                async () =>
                    (await listEditableBooks(page)).map((b) => b.title).sort(),
                {
                    timeout: 30000,
                    message:
                        "Sanity check: the collection should hold the two books just made",
                },
            )
            .toEqual([kDeletedTitle, kKeptTitle].sort());
        const books = await listEditableBooks(page);
        keptId = books.find((b) => b.title === kKeptTitle)!.id;
        deletedId = books.find((b) => b.title === kDeletedTitle)!.id;
        keptFolder = findBookFolderById(bloomApp.collectionDir, keptId);
        deletedFolder = findBookFolderById(bloomApp.collectionDir, deletedId);
        await expect(
            bookTile(page, deletedId),
            "Sanity check: the book to delete should have a tile on the Collections tab",
        ).toBeVisible({ timeout: 30000 });
        await expect(
            bookTile(page, keptId),
            "Sanity check: the book to keep should have a tile on the Collections tab",
        ).toBeVisible();
    });

    let beforeDelete = new Date();
    await step("Select the book and choose Delete Book", async () => {
        await clickBookTile(page, deletedId);
        await expectBookInPreview(page, kDeletedTitle);
        // The file system may round the Recycle Bin record's time down, so allow a second.
        beforeDelete = new Date(Date.now() - 1000);
        await chooseFromSelectedBookMenuByTestId(
            page,
            "CollectionTab.BookMenu.DeleteBook",
        );
    });

    await step("Confirm that the book goes to the Recycle Bin", async () => {
        const message = await confirmRecycleBox(bloomApp.bloomPid, 30000);
        expect(
            message,
            "the box asking to move the book to the Recycle Bin should name the selected book",
        ).toContain(kDeletedTitle);
    });

    await step("See the book gone, and the other book kept", async () => {
        await expect
            .poll(
                async () => (await listEditableBooks(page)).map((b) => b.id),
                {
                    timeout: 30000,
                    message:
                        "the deleted book should leave the collection, and only it",
                },
            )
            .toEqual([keptId]);
        await expect(
            bookTile(page, deletedId),
            "the deleted book's tile should leave the Collections tab",
        ).toHaveCount(0, { timeout: 30000 });
        await expect(
            bookTile(page, keptId),
            "the other book's tile should stay on the Collections tab",
        ).toBeVisible();
        expect(
            fs.existsSync(deletedFolder),
            `the deleted book's folder ${deletedFolder} should be gone from the collection`,
        ).toBe(false);
        expect(
            fs.existsSync(keptFolder),
            `the other book's folder ${keptFolder} should stay in the collection`,
        ).toBe(true);
        expect(
            await wasMovedToRecycleBinSince(deletedFolder, beforeDelete),
            "Delete Book should move the book's folder to the Recycle Bin",
        ).toBe(true);
        await expectNoBookInPreview(page);
    });
});
