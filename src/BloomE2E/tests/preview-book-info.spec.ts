// The info icon above the Collections tab's preview tells where the selected book is on disk, its
// id and its theme (Test Case ID 751). The collection holds two books in different themes, so a
// tip that showed another book's facts, or kept the first book's after the second is selected,
// would fail.

import * as fs from "fs";
import { expect } from "@playwright/test";
import { test } from "../fixtures/bloomTest";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import { getBookAppearance, setBookAppearance } from "../helpers/bookSettings";
import {
    clickBookTile,
    findBookFolderById,
    getSelectedBookId,
    readBookId,
    readBookInfoIndicator,
} from "../helpers/collection";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "preview-book-info", languages: ["en"] },
});

/** The same folder, however each path spells it (case, short names, separators). */
function expectSameFolder(shown: string, expected: string, what: string) {
    expect(fs.existsSync(shown), `${what}: "${shown}" should exist`).toBe(true);
    expect(
        fs.realpathSync.native(shown).toLowerCase(),
        `${what} should be the book's folder`,
    ).toBe(fs.realpathSync.native(expected).toLowerCase());
}

test("the preview's info icon shows the book's path, id and theme [Test Case ID 751]", async ({
    bloomApp,
    step,
}) => {
    // Two books made and one book's theme changed.
    test.setTimeout(300000);
    const page = bloomApp.page;
    const books: { id: string; theme: string }[] = [];

    await step("Make two books in different themes", async () => {
        for (const theme of ["default", "edge-to-edge"]) {
            await makeBookFromTemplate(page, "Basic Book");
            if ((await getBookAppearance(page)).cssThemeName !== theme)
                await setBookAppearance(page, { cssThemeName: theme });
            const id = await getSelectedBookId(page);
            if (!id) throw new Error("Bloom has no book selected.");
            books.push({ id, theme });
        }
        expect(books[0].id, "the two books should have different ids").not.toBe(
            books[1].id,
        );
        await switchTab(page, "collection");
    });

    for (const [index, book] of books.entries()) {
        await step(
            `Select book ${index + 1} (${book.theme} theme) and click the info icon of its preview`,
            async () => {
                await clickBookTile(page, book.id);
                const info = await readBookInfoIndicator(page);
                const folder = findBookFolderById(
                    bloomApp.collectionDir,
                    book.id,
                );
                expectSameFolder(info.path, folder, "the path on disk");
                expect(info.id, "the book id shown").toBe(book.id);
                expect(
                    readBookId(info.path),
                    "the id shown should be the one in the shown folder's meta.json",
                ).toBe(info.id);
                expect(info.theme, "the theme shown").toBe(book.theme);
            },
        );
    }
});
