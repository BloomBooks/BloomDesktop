// An inline image's credits: "Set Image Information..." on the picture's own menu.
//
// WHAT THIS IS ABOUT. A picture's credits (copyright, license) live in two places: in the image
// file itself, and on the page as data-copyright/data-license attributes of the <img>, which Bloom
// copies from the file after the Copyright and License dialog saves it. The attributes are what the
// Edit tab reads: the toolbar's missing-information warning shows while data-copyright is empty
// (buildCanvasElementControlRegistryContext.ts), and the credits page's "paste image credits" link
// gathers them from the whole book.
//
// An inline image is one <img> per language block of the translation group, the hidden lang="z"
// prototype included, all showing the same file. So the credits have to reach every copy: the copy
// a reader sees, the copies of other languages, and the prototype that a language added later is
// cloned from. That is the part only a real Bloom can show, because the attributes are written by
// C# after the dialog saves (EditingModel.UpdateMetaData), and the page is then reloaded and
// normalized by the front end (normalizeInlineImages in inlineImages.ts).
//
// The picture is bird.png from fixtures/images, which carries no credits of its own, so the
// warning is there to see before the dialog and gone after it.

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
import { setCopyrightHolder } from "../helpers/copyrightAndLicense";
import {
    addInlineImage,
    changeInlineImagePicture,
    clickInBlockText,
    getInlineImageInEveryLanguage,
    getInlineImageToolbarButtons,
    openInlineImageInformation,
    readSavedInlineImages,
    selectInlineImage,
} from "../helpers/inlineImages";

test.use({
    collectionSpec: { name: "inline-images-image-credits", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

const BLOCK = ".bloom-translationGroup";
const LANG = "en";
const BOOK_TITLE = "Inline Images Image Credits";

// The copyright holder the test types into the dialog. Distinctive, so that finding it in an
// attribute cannot be an accident.
const HOLDER = "Kingfisher Pictures";

const fixtureImage = (name: string) =>
    Path.resolve(
        Path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "fixtures",
        "images",
        name,
    );

// Set by the first test.
let imageId: string;
let bookFolder: string;

/**
 * The copies of the image whose credits do not name HOLDER, each as "<language>: <its
 * data-copyright>", so that a failing poll prints exactly which copies are wrong. The dialog's
 * save reloads the page, so a read can land on a page that is being rebuilt; that read, and one
 * that finds fewer than two copies, reports a problem rather than an empty (passing) list.
 */
async function copiesWithoutTheCredits(
    page: Parameters<typeof getInlineImageInEveryLanguage>[0],
): Promise<string[]> {
    try {
        const copies = await getInlineImageInEveryLanguage(
            page,
            BLOCK,
            imageId,
        );
        if (copies.length < 2)
            return [`(only ${copies.length} copy of the picture on the page)`];
        return copies
            .filter((copy) => !copy.copyright.includes(HOLDER))
            .map((copy) => `${copy.languageTag}: "${copy.copyright}"`);
    } catch (error) {
        const firstLine = (error as Error).message.split(/\r?\n/)[0];
        return [`(page not readable: ${firstLine})`];
    }
}

test.describe("an inline image's credits [Test Case ID 815]", () => {
    test("a picture with no credits shows the missing-information warning", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await typeInGroup(page, ".bookTitle", "en", BOOK_TITLE);
        await addPage(page, "Just Text");
        const [contentPage] = await getContentPages(page);
        await goToPage(page, contentPage.id);
        await typeInGroup(
            page,
            BLOCK,
            LANG,
            "The kingfisher waits on the branch above the pool, still enough that the water " +
                "forgets it is there.",
        );
        bookFolder = await findBookFolder(page, BOOK_TITLE);

        imageId = await addInlineImage(page, BLOCK, LANG);
        await changeInlineImagePicture(
            page,
            BLOCK,
            LANG,
            imageId,
            fixtureImage("bird.png"),
        );

        const copies = await getInlineImageInEveryLanguage(
            page,
            BLOCK,
            imageId,
        );
        expect(
            copies.length,
            "The group holds fewer than two copies of the picture (the shown language and the " +
                'lang="z" prototype), so this test would be measuring nothing.',
        ).toBeGreaterThan(1);
        for (const copy of copies)
            expect(
                copy.copyright,
                `bird.png carries no credits, yet the "${copy.languageTag}" copy has some.`,
            ).toBe("");

        // Select the picture afresh, so that the toolbar is built for the picture as it is now
        // rather than for the placeholder it was when Add Image selected it.
        await clickInBlockText(page, BLOCK, LANG);
        await selectInlineImage(page, BLOCK, LANG, imageId);
        expect(
            (await getInlineImageToolbarButtons(page)).map(
                (button) => button.id,
            ),
            "A real picture with no credits should put the missing-information warning on the " +
                "toolbar.",
        ).toContain("missingMetadata");
    });

    test("Set Image Information gives every language's copy the credits, and the warning goes away", async ({
        page,
    }) => {
        // THE ACTION UNDER TEST: the picture's own menu, and the real Copyright and License
        // dialog, which saves the credits into the file and then reloads the page.
        await openInlineImageInformation(page, BLOCK, LANG, imageId);
        await setCopyrightHolder(page, HOLDER);

        await expect
            .poll(() => copiesWithoutTheCredits(page), {
                timeout: 30000,
                message:
                    `After Set Image Information, these copies of the picture do not carry ` +
                    `"${HOLDER}" in their data-copyright. A copy that lacks it shows the ` +
                    `missing-information warning, and the lang="z" prototype hands its credits ` +
                    `to any language added later.`,
            })
            .toEqual([]);

        await clickInBlockText(page, BLOCK, LANG);
        await selectInlineImage(page, BLOCK, LANG, imageId);
        expect(
            (await getInlineImageToolbarButtons(page)).map(
                (button) => button.id,
            ),
            "The picture has credits now, so the missing-information warning should be gone.",
        ).not.toContain("missingMetadata");
    });

    test("the credits survive leaving the page and coming back", async ({
        page,
    }) => {
        const [contentPage] = await getContentPages(page);
        // Bloom writes a page only when the book leaves it, so this is the save.
        const [firstXmatter] = (await getPages(page)).filter(
            (one) => !one.isContentPage,
        );
        await goToPage(page, firstXmatter.id);

        const saved = (await readSavedInlineImages(page, bookFolder)).filter(
            (one) => one.id === imageId,
        );
        expect(
            saved.length,
            "The saved book holds fewer than two copies of the picture.",
        ).toBeGreaterThan(1);
        for (const copy of saved)
            expect(
                copy.copyright,
                `The saved "${copy.languageTag}" copy of the picture lost its credits.`,
            ).toContain(HOLDER);

        await goToPage(page, contentPage.id);
        for (const copy of await getInlineImageInEveryLanguage(
            page,
            BLOCK,
            imageId,
        ))
            expect(
                copy.copyright,
                `The "${copy.languageTag}" copy came back without its credits.`,
            ).toContain(HOLDER);
    });
});
