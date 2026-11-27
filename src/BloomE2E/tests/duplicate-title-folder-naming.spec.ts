// Automates the manual test "Duplicate Title Folder Naming" (Test Case ID 392).
//
// Bloom names a book's folder, and the .htm file in it, after the book's title. Two books of one
// collection can have the same title, and two folders cannot have the same name. So the second
// book's folder and file get the title followed by " - " and the first eight digits of that book's
// own id (BookStorage.GetUniqueBookFolderName).

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import {
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import { bookHtmlPath } from "../helpers/bookHtml";
import { readBookId } from "../helpers/collection";
import { listBookHtmlFiles, waitForBookFolderRenamed } from "../helpers/files";
import type { Page } from "@playwright/test";

test.use({
    collectionSpec: {
        name: "duplicate-title-folder-naming",
        languages: ["en"],
    },
});

/**
 * Make a book from Basic Book, type `title` on its cover, and save it by going to another page.
 * Returns the book's id and the folder Bloom renamed it to.
 */
async function makeBookWithTitle(
    page: Page,
    collectionDir: string,
    title: string,
): Promise<{ id: string; folder: string }> {
    const untitledFolder = await makeBookFromTemplate(page, "Basic Book");
    // Bloom's placeholder name for an untitled book is translated, so check only that it is not
    // already the title's (otherwise the rename below would prove nothing).
    expect(
        Path.basename(untitledFolder).startsWith(title),
        "test setup: a book with no title yet is not in a folder named for the title",
    ).toBe(false);
    const id = readBookId(untitledFolder);
    const pages = await getPages(page);
    expect(
        pages.length,
        "test setup: the new book has a page besides its cover to go to",
    ).toBeGreaterThan(1);

    await typeInGroup(page, ".bookTitle", "en", title);
    // Going to another page is what saves the title, and so what renames the folder.
    await goToPage(page, pages[1].id);
    const folder = await waitForBookFolderRenamed(
        collectionDir,
        id,
        untitledFolder,
    );
    return { id, folder };
}

test("a second book with the same title gets a folder and file named with part of its id [Test Case ID 392]", async ({
    page,
    bloomApp,
    step,
}) => {
    test.setTimeout(300000);
    const title = "Two Books One Title";

    const first = await step("Make a book and give it a title", () =>
        makeBookWithTitle(page, bloomApp.collectionDir, title),
    );
    if (!first) throw new Error("No first book was made.");
    expect(
        Path.basename(first.folder),
        "the first book with a title has a folder named for the title alone",
    ).toBe(title);
    expect(
        listBookHtmlFiles(first.folder),
        "the first book's file is named for the title alone",
    ).toEqual([`${title}.htm`]);

    const second = await step(
        "Make a second book and give it the same title",
        () => makeBookWithTitle(page, bloomApp.collectionDir, title),
    );
    if (!second) throw new Error("No second book was made.");
    expect(second.id, "test setup: the two books are different books").not.toBe(
        first.id,
    );

    // "A portion of the book ID": the first eight digits of the id, after the title.
    const nameWithIdPortion = `${title} - ${second.id.replace(/-/g, "").substring(0, 8).toLowerCase()}`;
    expect(
        Path.basename(second.folder),
        "the second book's folder is named for the title and part of the book's id",
    ).toBe(nameWithIdPortion);
    expect(
        listBookHtmlFiles(second.folder),
        "the second book's file is named like its folder",
    ).toEqual([`${nameWithIdPortion}.htm`]);

    expect(
        readBookId(first.folder),
        "the first book is still in the folder named for the title alone",
    ).toBe(first.id);
    expect(
        fs.existsSync(bookHtmlPath(first.folder)),
        "the first book's file is still there under its own name",
    ).toBe(true);
});
