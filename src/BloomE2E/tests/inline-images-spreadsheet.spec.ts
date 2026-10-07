// Checks that an inline (Word-style) image survives exporting a book to a spreadsheet and
// importing it again. The feature is described in bookEdit/js/inlineImages.ts.
//
// A spreadsheet has one row per text block. An image inside a text block has no column of its
// own, so an export and import could drop it without any error. The exporter writes the image as
// a separate [inline image] row, with its size and position as JSON in a hidden column, and the
// importer builds the wrapper again from those. The C# tests in
// src/BloomTests/Spreadsheet/SpreadsheetInlineImageTests.cs cover the exporter and the importer.
// This file covers the two menu commands and the real file passing between them: a real .xlsx
// that Bloom wrote, chosen through the real Import command.
//
// This test does not check that data-bloom-inline-image-id is the same after the import. The
// importer builds a new wrapper and gives it a new id, because nothing outside the page refers to
// that id. What has to survive is the picture, its size, and its position.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    findBookFolder,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    addInlineImage,
    changeInlineImagePicture,
    deleteInlineImage,
    dragInlineImageToDock,
    getInlineImage,
    getInlineImages,
    resizeInlineImage,
} from "../helpers/inlineImages";
import {
    exportBookToSpreadsheet,
    importSpreadsheetIntoBook,
} from "../helpers/spreadsheet";
import { switchTab } from "../helpers/workspace";

// Exporting and importing spreadsheets needs a subscription tier of LocalCommunity or better, so
// the collection is launched with a subscription code.
test.use({
    collectionSpec: {
        name: "inline-images-spreadsheet",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

test.describe.configure({ mode: "serial" });

const BLOCK = ".bloom-translationGroup";
const LANG = "en";
const BOOK_TITLE = "Inline Images Spreadsheet";
const TEXT =
    "The kingfisher waits on the branch above the pool, still enough that the water forgets " +
    "it is there. When it drops, it drops straight, and the pool closes over the place where " +
    "it went in. The children on the bank have learned to wait as well.";

const fixtureImage = (name: string) =>
    Path.resolve(
        Path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "fixtures",
        "images",
        name,
    );

test("an inline image survives a spreadsheet round trip [Test Case ID 815]", async ({
    page,
    bloomApp,
}) => {
    test.setTimeout(300000);

    await makeBookFromTemplate(page, "Basic Book");
    await typeInGroup(page, ".bookTitle", "en", BOOK_TITLE);
    await addPage(page, "Just Text");
    const [contentPage] = await getContentPages(page);
    await goToPage(page, contentPage.id);
    await typeInGroup(page, BLOCK, LANG, TEXT);
    const bookFolder = await findBookFolder(page, BOOK_TITLE);

    // Give the picture a position and size different from the defaults, so that an import that
    // only put a picture back would fail. It is docked left instead of the default right, and it
    // is wider than the default 40%.
    const imageId = await addInlineImage(page, BLOCK, LANG);
    await changeInlineImagePicture(
        page,
        BLOCK,
        LANG,
        imageId,
        fixtureImage("bird.png"),
    );
    await dragInlineImageToDock(page, BLOCK, LANG, imageId, "left");
    await resizeInlineImage(page, BLOCK, LANG, imageId, "se", 60);
    const before = await getInlineImage(page, BLOCK, LANG, imageId);
    expect(before.widthPercent).toBeGreaterThan(40);

    // Bloom saves a page only when the person leaves it, so go to another page to save what the
    // export will read.
    const [firstXmatter] = (await getPages(page)).filter(
        (one) => !one.isContentPage,
    );
    await goToPage(page, firstXmatter.id);

    // The first action under test is the export. When Bloom runs with --e2e, the export writes the
    // file and reports where it put it, instead of opening it in the machine's spreadsheet program.
    const spreadsheet = await exportBookToSpreadsheet(
        page,
        Path.join(bloomApp.collectionDir, "spreadsheet-exports"),
    );
    expect(spreadsheet.endsWith(".xlsx")).toBe(true);

    // Delete the image from the book, so that finding it afterwards can only mean the spreadsheet
    // brought it back. Without this step an import that changed nothing would pass. The export
    // leaves the Collection tab showing, so go back to the Edit tab first.
    await switchTab(page, "edit");
    await goToPage(page, contentPage.id);
    await deleteInlineImage(page, BLOCK, LANG, imageId);
    expect(
        (await getInlineImages(page, BLOCK)).flatMap((block) => block.images),
    ).toEqual([]);
    await goToPage(page, firstXmatter.id);

    // The second action under test is a real "Import Content from Spreadsheet..." from the book's
    // context menu, choosing the file the export just wrote in its file chooser.
    await importSpreadsheetIntoBook(page, bookFolder, spreadsheet);

    await switchTab(page, "edit");
    const [pageAfterImport] = await getContentPages(page);
    await goToPage(page, pageAfterImport.id);

    const blocks = await getInlineImages(page, BLOCK);
    const copies = blocks.flatMap((block) => block.images);
    expect(
        copies.length,
        "The import put no inline image back in the block. The spreadsheet's [inline image] row " +
            "or its hidden geometry column did not survive the trip through the file.",
    ).toBeGreaterThan(0);

    // Every block of the group has one copy again, and all the copies have the same
    // data-bloom-inline-image-id, which syncInlineImagesFromEditable needs to pair them up while
    // editing. The id is a new one; see the comment at the top of this file.
    expect(blocks.map((block) => block.images.length)).toEqual(
        blocks.map(() => 1),
    );
    expect(new Set(copies.map((copy) => copy.id)).size).toBe(1);

    for (const copy of copies) {
        expect(copy.dock, "The dock did not survive the round trip.").toBe(
            "left",
        );
        expect(
            copy.widthPercent,
            "The width did not survive the round trip.",
        ).toBe(before.widthPercent);
        expect(copy.fileName).toBe("bird.png");
        expect(copy.contentEditable).toBe("false");
    }
    // The text of the block came back as well, and the picture is still its first child.
    const shown = await getInlineImage(page, BLOCK, LANG, copies[0].id);
    expect(shown.slot.index).toBe(0);
    expect(shown.slot.childCount).toBeGreaterThan(1);
});
