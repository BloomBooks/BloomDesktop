// An inline (Word-style) image has to survive a spreadsheet round trip. The feature is described
// in INLINE-IMAGES-PLAN.md.
//
// WHY THIS IS WORTH AN E2E TEST. A spreadsheet holds one row per text block. An image inside a
// text block is the one thing a round trip could silently drop, because there is no column for it:
// the exporter writes an [inline image] row of its own and the geometry as JSON in a hidden
// column, and the importer builds the wrapper again from those. That is covered in C# by
// src/BloomTests/Spreadsheet/SpreadsheetInlineImageTests.cs, at the level of the exporter and the
// importer. What is left, and what this file covers, is the two menu commands and the real file
// passing between them: a real .xlsx that Bloom wrote, chosen through the real Import command.
//
// The identity attribute is deliberately NOT asserted across the round trip. The importer builds
// a new wrapper and gives it a fresh data-bloom-inline-image-id, because nothing outside the page
// refers to that id; what has to survive is the picture and its geometry.

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

    // A picture with geometry of its own, so that a round trip which merely put a picture back
    // could not pass: it is docked left, not at the default right, and it is wider than the
    // default 40%.
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

    // Bloom writes a page only when the book leaves it, so this is the save the export reads.
    const [firstXmatter] = (await getPages(page)).filter(
        (one) => !one.isContentPage,
    );
    await goToPage(page, firstXmatter.id);

    // THE FIRST ACTION UNDER TEST: the export. Under --e2e it writes the file and says where it
    // put it, instead of opening it in the machine's spreadsheet program.
    const spreadsheet = await exportBookToSpreadsheet(
        page,
        Path.join(bloomApp.collectionDir, "spreadsheet-exports"),
    );
    expect(spreadsheet.endsWith(".xlsx")).toBe(true);

    // Take the image out of the book, so that finding it afterwards can only mean the spreadsheet
    // carried it. Without this step an import that changed nothing would pass. The export left the
    // Collection tab showing, so go back to editing first.
    await switchTab(page, "edit");
    await goToPage(page, contentPage.id);
    await deleteInlineImage(page, BLOCK, LANG, imageId);
    expect(
        (await getInlineImages(page, BLOCK)).flatMap((block) => block.images),
    ).toEqual([]);
    await goToPage(page, firstXmatter.id);

    // THE SECOND ACTION UNDER TEST: a real "Import Content from Spreadsheet..." on the book's
    // context menu, answering its file chooser with the file the export just wrote.
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

    // Every block of the group has one copy again, and they share one identity, which is what the
    // edit-time sync needs. The id itself is a new one; see the note at the top of this file.
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
    // The text of the block came back as well, and the picture still leads it.
    const shown = await getInlineImage(page, BLOCK, LANG, copies[0].id);
    expect(shown.slot.index).toBe(0);
    expect(shown.slot.childCount).toBeGreaterThan(1);
});
