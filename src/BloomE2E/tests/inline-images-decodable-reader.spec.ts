// Checks that an inline image's menu still works with the Decodable Reader tool open (BL-16822).
//
// The reader tools mark up the text, and when the text loses the focus they tidy it with
// EditableDivUtils.doCkEditorCleanup, which put CKEditor's copy of the text back into the block.
// That rebuilt the inline image wrappers too, while the picture's menu, and the Image Chooser the
// menu opens, still held the old ones. So "Choose image from your computer..." changed a picture
// that was no longer on the page, and Delete deleted nothing. safelyReplaceContentWithCkEditorData
// now keeps the live wrappers. Clicking a menu command takes the focus from the text, so these
// tests go through the real menu and the real Image Chooser, which is the only route that holds a
// wrapper across that moment.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    addInlineImage,
    changeInlineImagePicture,
    chooseInlineImagePictureFromMenu,
    deleteInlineImage,
    getInlineImageInEveryLanguage,
    getInlineImages,
} from "../helpers/inlineImages";
import {
    expandReaderTool,
    makeBasicBookWithReaderTool,
} from "../helpers/readerTools";

test.use({
    collectionSpec: {
        name: "inline-images-decodable-reader",
        languages: ["en", "fr"],
    },
});

const BLOCK = ".bloom-translationGroup";
const LANG = "en";
const TEXT = "The cat sat on the mat. The dog sat on the log.";
// The picture put in first, and the one then chosen from the menu.
const FIRST_PICTURE = "house-landscape.png";
const CHOSEN_PICTURE = "bird.png";

const fixtureImage = (name: string) =>
    Path.resolve(
        Path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "fixtures",
        "images",
        name,
    );

type Page = import("@playwright/test").Page;

// Whether this worker's Bloom already has the reader book the tests add their pages to.
let bookMade = false;

/**
 * Add a Just Text page to a book with the Decodable Reader tool open, type on it, insert a picture
 * and give it FIRST_PICTURE, and return the picture's id. Each test makes its own page, so either
 * can run, and fail, alone.
 */
async function makeReaderPageWithPicture(page: Page): Promise<string> {
    if (!bookMade) {
        await makeBasicBookWithReaderTool(page, "decodableReader");
        bookMade = true;
    }
    await addPage(page, "Just Text");
    const pages = await getContentPages(page);
    await goToPage(page, pages[pages.length - 1].id);
    await expandReaderTool(page, "decodableReader");
    await typeInGroup(page, BLOCK, LANG, TEXT);
    const id = await addInlineImage(page, BLOCK, LANG);
    // A placeholder draws its flower as a background and hides the img, so a picture is put in
    // first, for the right-click to land on. This route finds the img afresh, so it is not the
    // one under test.
    await changeInlineImagePicture(
        page,
        BLOCK,
        LANG,
        id,
        fixtureImage(FIRST_PICTURE),
    );
    const copies = await getInlineImageInEveryLanguage(page, BLOCK, id);
    expect(
        copies.length,
        "The picture should be in more than one language block, so that every language can be checked.",
    ).toBeGreaterThan(1);
    // Bloom adds a number to the file name when the book already has the file, as it does on
    // the second page, so this checks only that every copy shows the same picture, and that it is
    // not the placeholder.
    const names = copies.map((copy) => copy.fileName);
    expect(
        names.every((name) => name === names[0] && name !== "placeHolder.png"),
        `Every language block should show ${FIRST_PICTURE} before the test acts on it, but they ` +
            `show ${names.join(", ")}.`,
    ).toBe(true);
    return id;
}

test("with the Decodable Reader open, Choose image changes the inline image's picture in every language [Test Case ID 815]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    let imageId = "";
    await step(
        "Make a page with text and a picture, with the Decodable Reader open",
        async () => {
            imageId = await makeReaderPageWithPicture(page);
        },
    );
    // chooseInlineImagePictureFromMenu fails, naming the languages that do show the picture,
    // unless every copy ends up showing it.
    await step("Choose a picture from the picture's menu", async () => {
        await chooseInlineImagePictureFromMenu(
            page,
            BLOCK,
            LANG,
            imageId,
            fixtureImage(CHOSEN_PICTURE),
        );
    });
});

test("with the Decodable Reader open, Delete on the inline image's menu removes it from every language [Test Case ID 815]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    let imageId = "";
    await step(
        "Make a page with text and a picture, with the Decodable Reader open",
        async () => {
            imageId = await makeReaderPageWithPicture(page);
        },
    );
    // deleteInlineImage fails unless every copy goes.
    await step("Delete the picture from its menu", async () => {
        await deleteInlineImage(page, BLOCK, LANG, imageId);
    });
    expect(
        (await getInlineImages(page, BLOCK)).flatMap((block) => block.images),
        "Delete left inline images in the block.",
    ).toEqual([]);
});
