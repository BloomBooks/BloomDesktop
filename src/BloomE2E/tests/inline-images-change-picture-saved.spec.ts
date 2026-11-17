// Checks that a new picture chosen for an inline image that was already on the page when it loaded
// is the one that is shown and saved after the page is left and loaded again (BL-16822). This is
// the behaviour a person sees.
//
// When a page loads, CKEditor copies the src of every img in a block it manages into
// data-cke-saved-src, and its getData() writes that copy out as the src. changeImageInfo in
// bloomEditing.ts updates data-cke-saved-src along with the src, so that anything that rebuilds
// a block from getData() keeps the new picture. This test does not show that change on its own:
// Bloom saves the page from the src, so without it the saved book still has the new picture here.
// The route by which getData() did bring the old picture back, a reader tool tidying the text when
// it loses the focus, is covered by inline-images-decodable-reader.spec.ts. A picture inserted
// since the page loaded has no data-cke-saved-src, so the test first leaves the page and comes back.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    findBookFolder,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    reloadPageBeingEdited,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    addInlineImage,
    changeInlineImagePicture,
    chooseInlineImagePictureFromMenu,
    getInlineImageInEveryLanguage,
    readSavedInlineImages,
} from "../helpers/inlineImages";
import { setPageSize } from "../helpers/pageSize";

test.use({
    collectionSpec: {
        name: "inline-images-change-picture-saved",
        languages: ["en", "fr"],
    },
});

const BLOCK = ".bloom-translationGroup";
const LANG = "en";
const TITLE = "Change Picture Saved";
const TEXT =
    "The kingfisher waits on the branch above the pool, still enough that the water forgets it " +
    "is there.";
const FIRST_PICTURE = "bird.png";
const SECOND_PICTURE = "blue-square.png";

const fixtureImage = (name: string) =>
    Path.resolve(
        Path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "fixtures",
        "images",
        name,
    );

test("a new picture for an inline image that was on the page when it loaded is saved [Test Case ID 815]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    let imageId = "";
    await step(
        "Make a page with a picture in its text, and leave it and come back",
        async () => {
            await makeBookFromTemplate(page, "Basic Book");
            await typeInGroup(page, ".bookTitle", "en", TITLE);
            await addPage(page, "Just Text");
            await goToPage(page, (await getContentPages(page))[0].id);
            await typeInGroup(page, BLOCK, LANG, TEXT);
            imageId = await addInlineImage(page, BLOCK, LANG);
            await changeInlineImagePicture(
                page,
                BLOCK,
                LANG,
                imageId,
                fixtureImage(FIRST_PICTURE),
            );
            // Leaving the page saves it, and coming back loads it with the picture already
            // there, so CKEditor records its src.
            await reloadPageBeingEdited(page);
        },
    );
    const bookFolder = await findBookFolder(page, TITLE);
    const copiesBeforeChange = await getInlineImageInEveryLanguage(
        page,
        BLOCK,
        imageId,
    );
    const languages = copiesBeforeChange.map((copy) => copy.languageTag);
    expect(
        languages.length,
        "The picture should be in more than one language block, so that every language's " +
            "saved copy can be checked.",
    ).toBeGreaterThan(1);
    expect(
        copiesBeforeChange.map((copy) => copy.fileName),
        `Every copy should show ${FIRST_PICTURE} after the reload, before it is changed.`,
    ).toEqual(languages.map(() => FIRST_PICTURE));

    await step(
        "Change the picture, then leave the page and come back",
        async () => {
            await changeInlineImagePicture(
                page,
                BLOCK,
                LANG,
                imageId,
                fixtureImage(SECOND_PICTURE),
            );
            await reloadPageBeingEdited(page);
        },
    );

    const saved = (await readSavedInlineImages(page, bookFolder))
        .filter((image) => image.id === imageId)
        .map((image) => ({
            languageTag: image.languageTag,
            file: decodeURIComponent(image.source.split("?")[0]),
        }));
    expect(
        saved,
        `The saved book should have ${SECOND_PICTURE} in every language's copy of the picture. ` +
            `A copy saved with ${FIRST_PICTURE} means the page was saved from CKEditor's record ` +
            `of the src the picture had when the page loaded (data-cke-saved-src).`,
    ).toEqual(
        languages.map((languageTag) => ({ languageTag, file: SECOND_PICTURE })),
    );
    expect(
        (await getInlineImageInEveryLanguage(page, BLOCK, imageId)).map(
            (copy) => copy.fileName,
        ),
        `After the page was saved and loaded again, every copy should show ${SECOND_PICTURE}.`,
    ).toEqual(languages.map(() => SECOND_PICTURE));
});

// The report that led to this file (BL-16822) was of a changed picture that went back to the old
// one when the book was changed from A5 Portrait to A5 Landscape, after which the new picture's
// file could not be used again in the book. Changing the page size saves the page and builds it
// again, as leaving it does. Like the test above, this checks what a person sees, and Bloom's
// normal save reads the src, so it does not fail on data-cke-saved-src alone.
test("a new picture for an inline image is still there after the book changes to another page size, and its file can be used for another picture [Test Case ID 815]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    const title = "Change Picture Then Page Size";
    let imageId = "";
    await step(
        "Make a page with a picture in its text, and leave it and come back",
        async () => {
            await makeBookFromTemplate(page, "Basic Book");
            await typeInGroup(page, ".bookTitle", "en", title);
            await addPage(page, "Just Text");
            await goToPage(page, (await getContentPages(page))[0].id);
            await typeInGroup(page, BLOCK, LANG, TEXT);
            imageId = await addInlineImage(page, BLOCK, LANG);
            await changeInlineImagePicture(
                page,
                BLOCK,
                LANG,
                imageId,
                fixtureImage(FIRST_PICTURE),
            );
            await reloadPageBeingEdited(page);
        },
    );
    const bookFolder = await findBookFolder(page, title);
    const languages = (
        await getInlineImageInEveryLanguage(page, BLOCK, imageId)
    ).map((copy) => copy.languageTag);
    expect(
        languages.length,
        "The picture should be in more than one language block, so that every language's " +
            "copy can be checked.",
    ).toBeGreaterThan(1);

    await step(
        "Choose another picture from the picture's menu, then change the book to A5 Landscape",
        async () => {
            await chooseInlineImagePictureFromMenu(
                page,
                BLOCK,
                LANG,
                imageId,
                fixtureImage(SECOND_PICTURE),
            );
            await setPageSize(page, "A5Landscape");
        },
    );
    expect(
        (await getInlineImageInEveryLanguage(page, BLOCK, imageId)).map(
            (copy) => copy.fileName,
        ),
        `After the book changed to A5 Landscape, every copy should show ${SECOND_PICTURE}, the ` +
            `picture chosen last.`,
    ).toEqual(languages.map(() => SECOND_PICTURE));
    expect(
        (await readSavedInlineImages(page, bookFolder))
            .filter((image) => image.id === imageId)
            .map((image) => decodeURIComponent(image.source.split("?")[0])),
        `The saved book should have ${SECOND_PICTURE} in every language's copy of the picture.`,
    ).toEqual(languages.map(() => SECOND_PICTURE));

    // chooseInlineImagePictureFromMenu fails unless every language's copy ends up showing the
    // file that was chosen.
    await step(
        "Choose that same file for a picture on another page",
        async () => {
            await addPage(page, "Just Text");
            const pages = await getContentPages(page);
            await goToPage(page, pages[pages.length - 1].id);
            await typeInGroup(page, BLOCK, LANG, TEXT);
            const otherId = await addInlineImage(page, BLOCK, LANG);
            await changeInlineImagePicture(
                page,
                BLOCK,
                LANG,
                otherId,
                fixtureImage(FIRST_PICTURE),
            );
            await chooseInlineImagePictureFromMenu(
                page,
                BLOCK,
                LANG,
                otherId,
                fixtureImage(SECOND_PICTURE),
            );
        },
    );
});
