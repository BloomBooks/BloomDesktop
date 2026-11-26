// A picture whose file name has special characters in it, above all "&", is found wherever Bloom
// shows the book: in the Edit tab, in the PDF, in a BloomPUB, in an ePUB, and again after Bloom
// is restarted.
// Automates the manual test "Import Images Special Characters" (Test Case ID 270).
//
// The case comes from BL-9111, where making a PDF of a book with a picture called
// "Tosi-boy&torch - Crop - resize (772x1024).jpg" could not find the picture: the name was written
// into a URL as "boy&amp;torch". The first picture here has that name. The other two carry the
// rest of the characters that mean something in a URL or in HTML, and letters outside ASCII.
//
// Each picture has a size no other picture in the book has, and that is how it is recognized in a
// publication: a PDF does not keep a picture's name, and an ePUB gives it another one.
//
// The BloomPUB and the ePUB are read from the Publish tab's previews, which show the files that
// Save... would write. Save... itself opens the operating system's save dialog.

import * as Path from "node:path";
import type { Frame, Locator } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editBook,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import { activePreviewPage, goToPreviewPage } from "../helpers/bloomPlayer";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    openEpubPublishScreen,
    showEpubPreview,
    showEpubPreviewPage,
} from "../helpers/epubPublish";
import {
    chooseImageFileInChooser,
    getImagePlacement,
    getPicturesShownIn,
    makeJpegFile,
    readImageFileSize,
    type IPixelSize,
} from "../helpers/images";
import {
    choosePdfMode,
    getPdfPreviewPicturesByPage,
} from "../helpers/pdfPrint";
import {
    selectPublishDestination,
    showBloomPubPreview,
} from "../helpers/publish";

test.use({
    collectionSpec: {
        name: "image-file-special-characters",
        languages: ["en"],
        // Publishing an ePUB needs a subscription.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

// The pictures, one to a page. Every name is one that Windows allows for a file.
const PICTURES: { fileName: string; why: string; size: IPixelSize }[] = [
    {
        fileName: "Tosi-boy&torch - Crop - resize (772x1024).jpg",
        why: "an ampersand (the name from BL-9111)",
        size: { width: 317, height: 211 },
    },
    {
        fileName: "it's 100% #1 + more; a=b, @home [v2] {x} ~ $.jpg",
        why: "the characters that mean something in a URL",
        size: { width: 263, height: 347 },
    },
    {
        fileName: "café 木 ก &amp; &#38;.jpg",
        why: "letters outside ASCII, and text that reads as an HTML entity",
        size: { width: 389, height: 233 },
    },
];

/**
 * Check that the page drawn in `container` shows exactly one picture, and that it is `picture`,
 * loaded: a picture the browser could not load has no size.
 */
const expectPageShowsPicture = async (
    container: Locator | Frame,
    picture: (typeof PICTURES)[number],
    where: string,
) => {
    await expect
        .poll(
            async () =>
                (await getPicturesShownIn(container)).map(
                    ({ width, height }) => ({ width, height }),
                ),
            {
                timeout: 30000,
                message:
                    `${where} should show the picture "${picture.fileName}" (${picture.why}) ` +
                    `at its own size, and no other picture.`,
            },
        )
        .toEqual([picture.size]);
};

test("pictures whose file names have special characters show in Edit, the PDF, a BloomPUB and an ePUB, and after a restart [Test Case ID 270]", async ({
    page,
    bloomApp,
    step,
}, testInfo) => {
    test.setTimeout(420000);

    const book = await step(
        "Make a book with a page for each picture",
        async () => {
            const folder = await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Basic Text & Image", PICTURES.length);
            const pages = await getContentPages(page);
            expect(
                pages,
                "test setup: the book should have a content page for each picture",
            ).toHaveLength(PICTURES.length);
            return { folder, pages };
        },
    );
    if (!book) throw new Error("The book was never made.");

    for (const [index, picture] of PICTURES.entries())
        await step(
            `Choose a picture whose name has ${picture.why}`,
            async () => {
                const source = testInfo.outputPath(picture.fileName);
                await makeJpegFile(
                    page,
                    source,
                    picture.size.width,
                    picture.size.height,
                );
                await goToPage(page, book.pages[index].id);
                expect(
                    await chooseImageFileInChooser(page, source),
                    "Bloom should keep the picture's name, or this tests nothing about the name.",
                ).toBe(picture.fileName);
                expect(
                    readImageFileSize(Path.join(book.folder, picture.fileName)),
                    "test setup: the picture should be in the book at its own size, by which the publications are checked",
                ).toEqual(picture.size);
            },
        );

    await step("Make the PDF and look at each picture's page", async () => {
        // The Simple PDF has a page for each page of the book, in the book's order.
        const pagesOfBook = (await getPages(page)).map((p) => p.id);
        await choosePdfMode(page, "Simple");
        const picturesByPage = await getPdfPreviewPicturesByPage(page);
        expect(
            picturesByPage,
            "test setup: the Simple PDF should have a page for each page of the book",
        ).toHaveLength(pagesOfBook.length);
        for (const [index, picture] of PICTURES.entries())
            expect(
                picturesByPage[pagesOfBook.indexOf(book.pages[index].id)],
                `The PDF should draw the picture "${picture.fileName}" (${picture.why}) on its page, and no other picture.`,
            ).toEqual([picture.size]);
    });

    await step(
        "Preview the BloomPUB and look at each picture's page",
        async () => {
            await selectPublishDestination(page, "BloomPUB");
            const player = await showBloomPubPreview(page);
            for (const [index, picture] of PICTURES.entries()) {
                await goToPreviewPage(page, player, book.pages[index].id);
                await expectPageShowsPicture(
                    activePreviewPage(player),
                    picture,
                    "The BloomPUB",
                );
            }
        },
    );

    await step("Preview the ePUB and look at each picture's page", async () => {
        await openEpubPublishScreen(page);
        await showEpubPreview(page);
        for (const [index, picture] of PICTURES.entries())
            await expectPageShowsPicture(
                (await showEpubPreviewPage(page, book.pages[index].id)).frame,
                picture,
                "The ePUB",
            );
    });

    await step(
        "Restart Bloom and look at each picture's page in Edit",
        async () => {
            const shell = await bloomApp.restart();
            await editBook(shell, book.folder);
            for (const [index, picture] of PICTURES.entries()) {
                await goToPage(shell, book.pages[index].id);
                await expect
                    .poll(
                        async () => {
                            const { fileName, loaded } =
                                await getImagePlacement(shell);
                            return { fileName, loaded };
                        },
                        {
                            timeout: 30000,
                            message: `After a restart, the Edit tab should show the picture "${picture.fileName}" (${picture.why}).`,
                        },
                    )
                    .toEqual({ fileName: picture.fileName, loaded: true });
            }
        },
    );
});
