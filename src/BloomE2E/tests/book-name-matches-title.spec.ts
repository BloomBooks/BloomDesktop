// A book's name on the Collections tab follows its title. A new book starts with a placeholder
// name; once the person types a title on the cover and the book is saved, its tile on the
// Collections tab shows that title, and Bloom renames the book's folder and its .htm file to match.
// When the person later changes the title, the tile, the folder and the file follow again.
//
// Automates the manual test "Book Name Matches Title" (Test Case ID 391).

import * as fs from "node:fs";
import * as Path from "node:path";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    makeBookFromTemplate,
    typeInGroup,
    waitForEditablePage,
} from "../helpers/bookMaking";
import { findBookFolderById, readBookId } from "../helpers/collection";
import { getPageIds, selectPage } from "../helpers/pageThumbnails";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "book-name-matches-title", languages: ["en"] },
});

// Both are short enough that the tile shows them whole (a longer label is truncated to two lines).
const kFirstTitle = "Sunrise Hills";
const kSecondTitle = "Moon Over Sea";

/** The words under the book's tile on the Collections tab. */
function tileLabel(page: Page, bookId: string) {
    return page.locator(`.book-button[data-book-id="${bookId}"] .bookButton`);
}

/**
 * Wait until the book with `bookId` lives in a folder named `title`, holding `<title>.htm`, and
 * return that folder.
 */
async function expectFolderAndFileNamed(
    collectionDir: string,
    bookId: string,
    title: string,
): Promise<string> {
    await expect
        .poll(() => Path.basename(findBookFolderById(collectionDir, bookId)), {
            timeout: 30000,
            message: `the book's folder should be renamed to "${title}"`,
        })
        .toBe(title);
    const folder = findBookFolderById(collectionDir, bookId);
    const htmFiles = fs
        .readdirSync(folder)
        .filter((name) => name.toLowerCase().endsWith(".htm"));
    expect(
        htmFiles,
        `the book's .htm file should be renamed to "${title}.htm"`,
    ).toEqual([`${title}.htm`]);
    return folder;
}

test("the book's name on the Collections tab, its folder and its file follow its title [Test Case ID 391]", async ({
    page,
    bloomApp,
    step,
}) => {
    test.setTimeout(300000);
    let bookId = "";
    let placeholderFolder = "";

    await step("Make a new book and see its placeholder name", async () => {
        placeholderFolder = await makeBookFromTemplate(page, "Basic Book");
        bookId = readBookId(placeholderFolder);
        // The placeholder's words follow Bloom's UI language, so only its difference from the
        // title is checked.
        expect(
            Path.basename(placeholderFolder),
            "Sanity check: a book with no title yet is not in a folder named for the title to come",
        ).not.toBe(kFirstTitle);
        await switchTab(page, "collection");
        await expect(
            tileLabel(page, bookId),
            "Sanity check: the new book should have a tile on the Collections tab",
        ).toBeVisible({ timeout: 30000 });
        await expect(
            tileLabel(page, bookId),
            "Sanity check: before it has a title, the tile should not show the title to come",
        ).not.toContainText(kFirstTitle);
    });

    await step("Type a title on the cover", async () => {
        await switchTab(page, "edit");
        await waitForEditablePage(page);
        await typeInGroup(page, ".bookTitle", "en", kFirstTitle);
        // Leaving the Edit tab saves the book, which renames it.
        await switchTab(page, "collection");
    });

    await step(
        "See the Collections tab show the title as the book's name",
        async () => {
            await expect(
                tileLabel(page, bookId),
                "the book's tile on the Collections tab should show its new title",
            ).toHaveText(kFirstTitle, { timeout: 30000 });
            await expectFolderAndFileNamed(
                bloomApp.collectionDir,
                bookId,
                kFirstTitle,
            );
            expect(
                fs.existsSync(placeholderFolder),
                "the placeholder folder should be gone",
            ).toBe(false);
        },
    );

    let firstFolder = "";
    await step("Change the title", async () => {
        firstFolder = findBookFolderById(bloomApp.collectionDir, bookId);
        await switchTab(page, "edit");
        await waitForEditablePage(page);
        // The Edit tab may come back on another page; the title is typed on the cover.
        const [coverId] = await getPageIds(page);
        await selectPage(page, coverId);
        await typeInGroup(page, ".bookTitle", "en", kSecondTitle);
        await switchTab(page, "collection");
    });

    await step("See the Collections tab show the changed title", async () => {
        await expect(
            tileLabel(page, bookId),
            "the book's tile on the Collections tab should show the changed title",
        ).toHaveText(kSecondTitle, { timeout: 30000 });
        await expectFolderAndFileNamed(
            bloomApp.collectionDir,
            bookId,
            kSecondTitle,
        );
        expect(
            fs.existsSync(firstFolder),
            "the folder named for the first title should be gone",
        ).toBe(false);
    });
});
