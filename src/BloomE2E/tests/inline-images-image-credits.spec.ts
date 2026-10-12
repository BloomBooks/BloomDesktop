// Checks setting an inline image's credits with "Set Image Information..." on the picture's menu.
//
// A picture's credits (copyright and license) are stored in two places: in the image file itself,
// and on the page as the data-copyright and data-license attributes of the <img>, which Bloom
// copies from the file after the Copyright and License dialog saves it. The Edit tab reads the
// attributes. The toolbar shows the missing-information warning while data-copyright is empty
// (buildCanvasElementControlRegistryContext.ts), and the credits page's "paste image credits" link
// collects them from the whole book.
//
// An inline image has one <img> in each language's block of the translation group, including the
// hidden lang="z" block that new languages are copied from, and they all show the same file. So
// the credits have to reach every copy: the one a reader sees, the ones in other languages, and
// the lang="z" one that a language added later is cloned from. Only a real Bloom can show this,
// because C# writes the attributes after the dialog saves (EditingModel.UpdateMetaData), and then
// the page is reloaded and normalizeInlineImages (in inlineImages.ts) runs on it.
//
// The picture is bird.png from fixtures/images, which has no credits of its own, so the warning
// shows before the dialog and should be gone after it.

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

// The copyright holder the test types into the dialog. It is unusual enough that finding it in an
// attribute cannot be a coincidence.
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
 * Lists the copies of the image whose credits do not name HOLDER, each as "<language>: <its
 * data-copyright>", so that a failing poll prints which copies are wrong. Saving the dialog
 * reloads the page, so a read can happen while the page is being rebuilt. In that case, or when it
 * finds fewer than two copies, it returns a line describing the problem, because an empty list
 * would pass.
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

        // Select the picture again, so that the toolbar is built for the picture as it is now,
        // and not for the placeholder that Insert Image selected.
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
        // The action under test, done through the picture's menu and the real Copyright and
        // License dialog, which saves the credits into the file and then reloads the page.
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
        // Bloom saves a page only when the person leaves it, so going to another page saves it.
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
