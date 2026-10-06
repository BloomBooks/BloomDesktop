// What editing leaves in the saved book: nothing at all when the user only looks, and no editing
// leftovers when they do edit.
//
// Saving builds the page from a copy of the live page with the editor's markup taken off, and
// Bloom writes a page only when that copy says something the book does not already say (BL-13502;
// see src/BloomExe/Edit/SavingWithoutReloading.md). Two ways that can go wrong: something the
// editor adds makes an untouched page look edited, so merely looking at a book rewrites it; or
// something the editor adds is not taken off, and ends up in the book.
//
// The leftovers test automates "Clean Saved Book HTML" (Test Case ID 663). The unchanged-book test
// is a new card; its Test Case ID goes in its title once the card exists.
//
// The tests are serial because each one works on the book the first one builds.

import * as fs from "node:fs";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
    waitForBloomToHaveTyping,
} from "../helpers/bookMaking";
import {
    bookHtmlPath,
    findEditorLeftovers,
    readBookIgnoringOrder,
} from "../helpers/bookHtml";
import { selectBook } from "../helpers/collection";
import { selectPage } from "../helpers/pageThumbnails";
import { openReaderTool } from "../helpers/readerTools";
import { openToolboxWithTalkingBook } from "../helpers/talkingBook";
import { enableToolForBook, openTool } from "../helpers/toolbox";
import { switchTab } from "../helpers/workspace";
import type { Page } from "@playwright/test";

test.use({
    collectionSpec: { name: "saved-book-stays-clean", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

// Leftovers tracked by BL-9992, which Test Case ID 663 says to ignore.
const BL_9992_CLASSES = [
    "ui-draggable",
    "ui-resizable",
    "hoverUp",
    "ui-audioCurrent",
];

let bookFolder: string;

/** Click every page's thumbnail in turn, touching nothing on any of them. */
async function visitEveryPage(page: Page) {
    for (const p of await getPages(page)) await selectPage(page, p.id);
}

test.describe("the saved book stays clean", () => {
    test("builds a book with two text and picture pages", async ({ page }) => {
        test.setTimeout(300000);
        bookFolder = await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Basic Text & Image", 2);
        for (const [index, p] of (await getContentPages(page)).entries()) {
            await goToPage(page, p.id);
            const text = `Page ${index + 1} has some text.`;
            await typeInGroup(page, ".bloom-translationGroup", "en", text);
            await waitForBloomToHaveTyping(page, text);
        }
        await switchTab(page, "collection");
    });

    test("visiting every page without editing does not rewrite the book", async ({
        page,
    }) => {
        test.setTimeout(300000);
        // One visit first: opening a page can legitimately settle something once (the cover's
        // title padding, for example, is measured the first time the cover is shown), and that is
        // not what this test is about.
        await selectBook(page, bookFolder);
        await switchTab(page, "edit");
        await visitEveryPage(page);
        await switchTab(page, "collection");

        const before = await readBookIgnoringOrder(page, bookFolder);
        const writtenBefore = fs.statSync(bookHtmlPath(bookFolder)).mtimeMs;

        await switchTab(page, "edit");
        await visitEveryPage(page);
        await switchTab(page, "collection");

        expect(
            await readBookIgnoringOrder(page, bookFolder),
            "Looking at the pages changed what the book says.",
        ).toBe(before);
        expect(
            fs.statSync(bookHtmlPath(bookFolder)).mtimeMs,
            "Looking at the pages made Bloom write the book file, though nothing in it changed.",
        ).toBe(writtenBefore);
    });

    test("editing with tools open leaves no editor leftovers in the saved book [Test Case ID 663]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await selectBook(page, bookFolder);
        await enableToolForBook(page, bookFolder, "imageDescription");
        await enableToolForBook(page, bookFolder, "leveledReader");
        const [first, second] = await getContentPages(page);

        await goToPage(page, first.id);
        await openToolboxWithTalkingBook(page);
        await typeInGroup(
            page,
            ".bloom-translationGroup",
            "en",
            "Typed with Talking Book open. Two sentences.",
        );
        await waitForBloomToHaveTyping(page, "Two sentences.");

        await selectPage(page, second.id);
        await openTool(page, "imageDescription", ".imgDescLabelBlock");
        await openReaderTool(page, "leveledReader");
        await typeInGroup(
            page,
            ".bloom-translationGroup",
            "en",
            "Typed with the image description and reader tools open.",
        );
        await waitForBloomToHaveTyping(page, "reader tools open.");

        await switchTab(page, "collection");
        expect(
            await findEditorLeftovers(page, bookFolder, BL_9992_CLASSES),
        ).toEqual([]);
    });
});
