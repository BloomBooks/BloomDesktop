// Pasting a picture that was copied as a file (Ctrl+C in File Explorer) leaves that file where it
// was (Test Case ID 267, BL-12799).
//
// A file copied in File Explorer goes on the clipboard as a path, not as a picture, so Bloom reads
// the picture from the person's own file. A picture larger than Bloom keeps in a book is then
// shrunk, and that shrinking once cleaned up after itself by deleting the file it had read: the
// person's original. So the picture here is one Bloom has to shrink, and it goes on Bloom's
// clipboard the way File Explorer's Copy puts it there, as a copied file.
//
// The paste goes through the real UI, the Paste image command on the cover picture's menu.

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import { copyToClipboard, getClipboard } from "../helpers/clipboard";
import { fingerprintFolder } from "../helpers/files";
import {
    makeJpegFile,
    pasteImageIntoEmptySlot,
    readImageFileSize,
} from "../helpers/images";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "paste-image-keeps-source", languages: ["en"] },
});

// Larger than anything Bloom keeps in a book, so Bloom shrinks it.
const LARGE_WIDTH = 6000;
const LARGE_HEIGHT = 3375;
const SOURCE_NAME = "large-photo.jpg";

test("pasting a large picture copied in File Explorer does not delete the original file [Test Case ID 267]", async ({
    page,
    step,
}, testInfo) => {
    test.setTimeout(300000);
    // The picture has a folder to itself, outside the collection, as a person's own photo does.
    const sourceFolder = testInfo.outputPath("pictures to copy");
    const source = Path.join(sourceFolder, SOURCE_NAME);
    fs.mkdirSync(sourceFolder, { recursive: true });
    await makeJpegFile(page, source, LARGE_WIDTH, LARGE_HEIGHT);
    const sourceFolderBefore = fingerprintFolder(sourceFolder);
    expect(
        Object.keys(sourceFolderBefore),
        "test setup: the folder holds the picture to copy and nothing else",
    ).toEqual([SOURCE_NAME]);
    expect(
        readImageFileSize(source),
        "test setup: the picture to copy has the size it was made with",
    ).toEqual({ width: LARGE_WIDTH, height: LARGE_HEIGHT });

    const bookFolder = await makeBookFromTemplate(page, "Basic Book");

    await step("Copy the large picture in File Explorer", async () => {
        await copyToClipboard(page, { files: [source] });
        // sanity check: the clipboard holds the file alone, not the picture itself or its path as
        // text, so Bloom can only get the picture by reading the person's file.
        expect(await getClipboard(page)).toEqual({ files: [source] });
    });

    const pictureFile = await step(
        "Paste the picture on the cover with Paste image",
        async () => pasteImageIntoEmptySlot(page),
    );
    if (!pictureFile) throw new Error("The paste step did not run.");

    await step("Check that Bloom shrank its own copy", async () => {
        // Without this the test could pass on a picture Bloom never had to shrink, and it is the
        // shrinking that put the original at risk.
        const copy = Path.join(bookFolder, pictureFile);
        expect(
            fs.existsSync(copy),
            `The cover shows ${pictureFile}, but it is not in the book's folder ${bookFolder}.`,
        ).toBe(true);
        expect(
            readImageFileSize(copy).width,
            "Bloom kept the pasted picture at its full size, so this test did not make it shrink one.",
        ).toBeLessThan(LARGE_WIDTH);
    });

    await step("Check that the original file is still there", async () => {
        expect(
            fingerprintFolder(sourceFolder),
            `Pasting the picture changed the folder it was copied from (${sourceFolder}): ` +
                "the original file should still be there, unchanged, with nothing beside it.",
        ).toEqual(sourceFolderBefore);
    });

    // Leaving the Edit tab saves the book, the last of what Bloom does with a pasted picture.
    await step("Go to the Collections tab, which saves the book", async () => {
        await switchTab(page, "collection");
    });

    await step(
        "Check that the original file is still there after the save",
        async () => {
            expect(
                fingerprintFolder(sourceFolder),
                `Saving the book changed the folder the picture was copied from (${sourceFolder}).`,
            ).toEqual(sourceFolderBefore);
        },
    );
});
