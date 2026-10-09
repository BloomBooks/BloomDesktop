// What editing leaves in the saved book: nothing at all when the user only looks, and no editing
// leftovers when they do edit.
//
// Saving builds the page from a copy of the live page with the editor's markup taken off, and
// Bloom writes a page only when that copy says something the book does not already say (BL-13502;
// see src/BloomExe/Edit/SavingWithoutReloading.md). Two ways that can go wrong: something the
// editor adds makes an untouched page look edited, so merely looking at a book rewrites it; or
// something the editor adds is not taken off, and ends up in the book.
//
// The leftovers test automates "Clean Saved Book HTML" (Test Case ID 663); the unchanged-book tests
// automate "Looking at Pages Does Not Rewrite the Book" (Test Case ID 842).
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
    waitForPageToSettle,
    type IBookPage,
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

/**
 * Visit `pagesToVisit` twice, touching nothing, and check that the second round leaves the book file
 * as it was: nothing in it changes, and Bloom does not write it at all. The first round is allowed
 * to settle things that legitimately change once when a page is first shown (the cover's title
 * padding, for example). Each round starts and ends on a content page, so that entering and
 * leaving the Edit tab only ever leaves a page that is being visited.
 */
async function expectVisitingWritesNothing(
    page: Page,
    pagesToVisit: (pages: IBookPage[]) => IBookPage[],
) {
    const round = async () => {
        await switchTab(page, "edit");
        const pages = await getPages(page);
        const [firstContentPage] = pages.filter((p) => p.isContentPage);
        for (const p of [
            firstContentPage,
            ...pagesToVisit(pages),
            firstContentPage,
        ]) {
            await selectPage(page, p.id);
            await waitForPageToSettle(page);
        }
        await switchTab(page, "collection");
    };
    await selectBook(page, bookFolder);
    await round();

    const before = await readBookIgnoringOrder(page, bookFolder);
    const writtenBefore = fs.statSync(bookHtmlPath(bookFolder)).mtimeMs;
    await round();

    expect(
        await readBookIgnoringOrder(page, bookFolder),
        "Looking at the pages changed what the book says.",
    ).toBe(before);
    expect(
        fs.statSync(bookHtmlPath(bookFolder)).mtimeMs,
        "Looking at the pages made Bloom write the book file, though nothing in it changed.",
    ).toBe(writtenBefore);
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

    test("visiting the content pages and the cover without editing does not rewrite the book [Test Case ID 842]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await expectVisitingWritesNothing(page, (pages) =>
            pages.filter(
                (p, index) => p.isContentPage || index === 0, // index 0 is the front cover
            ),
        );
    });

    // The test collection has no branding images, so the outside back cover's branding block is a
    // missing optional image on every visit.
    test("visiting every page, front and back matter included, without editing does not rewrite the book [Test Case ID 842]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await expectVisitingWritesNothing(page, (pages) => pages);
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
        // The page's own text box, not the image description group the tool adds in front of it.
        await typeInGroup(
            page,
            ".bloom-translationGroup:not(.bloom-imageDescription)",
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
