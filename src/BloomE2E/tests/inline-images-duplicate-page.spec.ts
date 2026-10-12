// Checks duplicating a page that has a picture in its text.
//
// Duplicate Page goes through Book.InsertPageAfter, which calls BookStarter.UniqueifyIds. That
// gives new ids only to .//img[@id], so the copy of the page keeps the same
// data-bloom-inline-image-id as the page it came from. This is intended and does no harm, because
// this feature only ever looks up an inline image within one translation group:
// syncInlineImagesFromEditable, getInlineImageById and normalizeInlineImages are all given a group
// or an editable, and none of them searches the whole page or book. Nothing in the code shows that
// it depends on this, so a later change could easily break it, for example by using a
// document-wide querySelector. This test checks that changing one page's picture leaves the other
// page's picture alone.
//
// InsertPageAfter also copies the image file, but only `if (!RobustFile.Exists(path))`. So a page
// pasted into a book that already has a different file with the same name shows that book's
// picture, with no warning. Bloom copies every picture this way, including those in canvas
// elements, so this feature does not fix it; this comment records that it was looked at.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    addInlineImage,
    changeInlineImagePicture,
    getInlineImages,
    resizeInlineImage,
} from "../helpers/inlineImages";
import { duplicatePageWithContextMenu } from "../helpers/pageList";

test.use({
    collectionSpec: {
        name: "inline-images-duplicate-page",
        languages: ["en"],
    },
});

test.describe.configure({ mode: "serial" });

const BLOCK = ".bloom-translationGroup";
const LANG = "en";

const fixtureImage = (name: string) =>
    Path.resolve(
        Path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "fixtures",
        "images",
        name,
    );

test("a duplicated page's picture is independent of the original's [Test Case ID 815]", async ({
    page,
}) => {
    test.setTimeout(300000);
    await makeBookFromTemplate(page, "Basic Book");
    await typeInGroup(page, ".bookTitle", "en", "Inline Images Duplicate Page");
    await addPage(page, "Just Text");
    const [firstPage] = await getContentPages(page);
    await goToPage(page, firstPage.id);
    await typeInGroup(
        page,
        BLOCK,
        LANG,
        "Some text for the picture to sit in.",
    );

    const imageId = await addInlineImage(page, BLOCK, LANG);
    await changeInlineImagePicture(
        page,
        BLOCK,
        LANG,
        imageId,
        fixtureImage("bird.png"),
    );
    const widthAtStart = (await getInlineImages(page, BLOCK)).flatMap(
        (block) => block.images,
    )[0].widthPercent;

    // The action under test.
    await duplicatePageWithContextMenu(page, firstPage.id);
    const contentPages = await getContentPages(page);
    expect(
        contentPages.length,
        "Duplicate Page did not add a page, so this test is measuring nothing.",
    ).toBe(2);
    const copyId = contentPages.find((p) => p.id !== firstPage.id)!.id;

    // The copied page has the picture, with the same data-bloom-inline-image-id as the original.
    await goToPage(page, copyId);
    const onTheCopy = (await getInlineImages(page, BLOCK)).flatMap(
        (block) => block.images,
    );
    expect(
        onTheCopy.length,
        "The duplicated page has no picture in its text.",
    ).toBeGreaterThan(0);
    expect(
        onTheCopy[0].fileName,
        "The duplicated page's picture points at a different file from the original's.",
    ).toBe("bird.png");
    expect(
        onTheCopy.every((image) => image.id === imageId),
        `The duplicated page's picture has a different identity (${onTheCopy[0].id}) from the ` +
            `original's (${imageId}). Nothing is wrong with that in itself, but the tests and ` +
            `comments in this feature are written on the understanding that UniqueifyIds does ` +
            `NOT renew this attribute, so a change here means those need rereading.`,
    ).toBe(true);

    // The main check: changing the picture on the copied page must leave the original's alone.
    // The two have the same id, so code that looked the picture up across the whole page or book
    // could find the wrong one.
    const widened = await resizeInlineImage(
        page,
        BLOCK,
        LANG,
        imageId,
        "se",
        40,
    );
    expect(
        widened,
        "The resize did not change the copy's picture, so this test cannot say whether the change leaked.",
    ).not.toBe(widthAtStart);

    await goToPage(page, firstPage.id);
    const backOnTheOriginal = (await getInlineImages(page, BLOCK)).flatMap(
        (block) => block.images,
    );
    for (const image of backOnTheOriginal)
        expect(
            image.widthPercent,
            `Resizing the duplicated page's picture changed the original page's too: the ` +
                `"${image.languageTag}" copy there is now ${image.widthPercent}% instead of ` +
                `${widthAtStart}%. The two pages' pictures share a ` +
                `data-bloom-inline-image-id, so a lookup that is not scoped to one translation ` +
                `group reaches across pages.`,
        ).toBe(widthAtStart);
});
