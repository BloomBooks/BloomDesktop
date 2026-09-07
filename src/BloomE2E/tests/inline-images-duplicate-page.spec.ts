// Duplicating a page that has a picture in its text.
//
// WHAT THIS IS ABOUT. Duplicate Page goes through Book.InsertPageAfter, whose BookStarter.
// UniqueifyIds renews only .//img[@id] -- so every copy of the page keeps the SAME
// data-bloom-inline-image-id as the page it came from. That is deliberate and harmless, because
// every lookup this feature does is scoped to a translation group: syncInlineImagesFromEditable,
// getInlineImageById and normalizeInlineImages are all handed a group or an editable and never
// search the page, let alone the book. But it is load-bearing and invisible, which is exactly the
// kind of thing that gets broken by a later change that looks harmless -- a document-wide
// querySelector would do it -- so this pins it down: changing one page's picture must leave the
// other page's alone.
//
// The image FILE is copied by InsertPageAfter, but only `if (!RobustFile.Exists(path))`, so a
// page pasted into a book that already has a different file of the same name silently takes the
// other book's picture. That is how every picture in Bloom is copied, canvas elements included,
// so it is not this feature's to fix; it is noted here so the next person reading this file knows
// it was looked at.

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

    // THE ACTION UNDER TEST.
    await duplicatePageWithContextMenu(page, firstPage.id);
    const contentPages = await getContentPages(page);
    expect(
        contentPages.length,
        "Duplicate Page did not add a page, so this test is measuring nothing.",
    ).toBe(2);
    const copyId = contentPages.find((p) => p.id !== firstPage.id)!.id;

    // The copy has the picture, and it has the same identity, which is what this pins down.
    await goToPage(page, copyId);
    const onTheCopy = (await getInlineImages(page, BLOCK)).flatMap(
        (block) => block.images,
    );
    console.log(
        `[probe] on the copy: ${JSON.stringify(onTheCopy.map((i) => [i.languageTag, i.id, i.widthPercent, i.fileName]))}`,
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

    // THE THING THAT MATTERS: changing the copy's picture leaves the original's alone. The two
    // share an id, so anything that looked one up across the page, or across the book, would hit
    // the wrong one.
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
