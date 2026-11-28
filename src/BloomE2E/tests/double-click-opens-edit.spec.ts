// A double-click on a book's tile in the Collections tab opens that book in the Edit tab, whether
// the book was selected already or not (BL-12820: in 5.5 a double-click on a book that was not
// selected only selected it).
//
// Automates the manual test "Double-Click Opens Edit" (Test Case ID 101).

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { readPagesOnDisk } from "../helpers/bookHtml";
import {
    getShownPageId,
    makeBookFromTemplate,
    waitForEditTabSettled,
} from "../helpers/bookMaking";
import {
    doubleClickBookTile,
    expectBookTileSelected,
    findBookFolderById,
    getSelectedBookId,
    listEditableBooks,
} from "../helpers/collection";
import { getTabs, switchTab, waitForActiveTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "double-click-opens-edit", languages: ["en"] },
});

/**
 * Check that Bloom has gone to the Edit tab with the book `bookId` selected, and that the page the
 * Edit tab shows is one of that book's pages, read from the book's file on disk.
 */
async function expectBookOpenInEdit(
    page: Page,
    collectionDir: string,
    bookId: string,
    which: string,
): Promise<void> {
    await waitForActiveTab(page, "edit");
    expect(
        await getSelectedBookId(page),
        `the double-click should have selected ${which}`,
    ).toBe(bookId);
    await waitForEditTabSettled(page);
    const pageIds = (
        await readPagesOnDisk(page, findBookFolderById(collectionDir, bookId))
    ).map((p) => p.id);
    let shown: string | undefined;
    await expect
        .poll(
            async () => {
                shown = await getShownPageId(page);
                return shown !== undefined && pageIds.includes(shown);
            },
            { timeout: 30000 },
        )
        .toBe(true)
        .catch(() => {
            throw new Error(
                `The Edit tab should show a page of ${which}, one of [${pageIds.join(", ")}], ` +
                    `but it shows ${shown ?? "no page"}.`,
            );
        });
}

test("a double-click on a book's tile opens it in the Edit tab, selected or not [Test Case ID 101]", async ({
    page,
    bloomApp,
    step,
}) => {
    let firstBook = "";
    let secondBook = "";
    await step("Make two books and show the Collections tab", async () => {
        await makeBookFromTemplate(page, "Basic Book");
        await makeBookFromTemplate(page, "Basic Book");
        await switchTab(page, "collection");
        const books = await listEditableBooks(page);
        expect(
            books,
            "Sanity check: the collection holds the two books",
        ).toHaveLength(2);
        secondBook = (await getSelectedBookId(page))!;
        expect(
            books.map((b) => b.id),
            "Sanity check: Bloom leaves one of the two books selected",
        ).toContain(secondBook);
        firstBook = books.find((b) => b.id !== secondBook)!.id;
    });

    await step(
        "Double-click the book that is not selected (BL-12820)",
        async () => {
            await doubleClickBookTile(page, firstBook);
            await expectBookOpenInEdit(
                page,
                bloomApp.collectionDir,
                firstBook,
                "the book that was not selected",
            );
        },
    );

    await step("Go back and double-click the book now selected", async () => {
        await switchTab(page, "collection");
        expect(
            (await getTabs(page)).tabStates.edit,
            "Sanity check: the Edit tab is no longer the one showing",
        ).not.toBe("active");
        await expectBookTileSelected(page, firstBook);
        await doubleClickBookTile(page, firstBook);
        await expectBookOpenInEdit(
            page,
            bloomApp.collectionDir,
            firstBook,
            "the book that was already selected",
        );
    });

    await step(
        "Go back and double-click the other book, now not selected",
        async () => {
            await switchTab(page, "collection");
            // The first book is the one selected, so this one is not.
            await expectBookTileSelected(page, firstBook);
            await doubleClickBookTile(page, secondBook);
            await expectBookOpenInEdit(
                page,
                bloomApp.collectionDir,
                secondBook,
                "the other book",
            );
        },
    );
});
