// The Edit tab's page list shows each page as it is, so a change to a page shows in its thumbnail
// once the person moves to another page (Bloom saves the page it leaves and asks the list to draw
// it again, BL-9039). The Collections tab's tile for a book shows its thumbnail.png, which Bloom
// makes from the cover picture, so a new cover picture shows there once the person goes back to
// the Collections tab. Automates the manual test "Thumbnails Update On Edit", which asks for both
// with a new book and with an existing one: here the same book, first just made, then after Bloom
// has been closed and started again.
//
// Each round puts a picture of a different shape on the cover (a wide house, then a tall bird), so
// the tile's thumbnail, which keeps the cover picture's proportions, shows which cover it was made
// from.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editBook,
    getContentPages,
    getPages,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    readBookId,
    readBookTileThumbnail,
    type ITileThumbnail,
} from "../helpers/collection";
import { chooseImageFile, getImagePlacement } from "../helpers/images";
import { pageListFrame, selectPage } from "../helpers/pageThumbnails";
import type { StepFunction } from "../helpers/caption";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "thumbnails-update-on-edit", languages: ["en"] },
});

const fixtureImage = (name: string) =>
    Path.resolve(
        Path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "fixtures",
        "images",
        name,
    );

/** One round of edits: the text and picture for the content page, and the cover picture. */
interface IEdits {
    text: string;
    contentPicture: string;
    coverPicture: string;
    /** Whether the cover picture is wider than it is tall. */
    coverIsWide: boolean;
}

const NEW_BOOK_EDITS: IEdits = {
    text: "TC387 first text",
    contentPicture: "bird.png",
    coverPicture: "house-landscape.png",
    coverIsWide: true,
};

const EXISTING_BOOK_EDITS: IEdits = {
    text: "TC387 second text",
    contentPicture: "house-landscape.png",
    coverPicture: "bird.png",
    coverIsWide: false,
};

/** A page's thumbnail in the Edit tab's page list. */
function pageThumbnail(page: Page, pageId: string) {
    return pageListFrame(page).locator(
        `#pageGridWrapper .gridItem[id="${pageId}"]`,
    );
}

/** Wait until the page list's thumbnail of `pageId` shows a loaded picture whose file is `fileName`. */
async function expectThumbnailShowsPicture(
    page: Page,
    pageId: string,
    fileName: string,
    where: string,
) {
    const picture = pageThumbnail(page, pageId)
        .locator(`img[src*="${fileName}"]`)
        .first();
    await expect
        .poll(
            () =>
                picture
                    .evaluate((img) => {
                        const image = img as HTMLImageElement;
                        return image.complete && image.naturalWidth > 0;
                    })
                    .catch(() => false),
            {
                timeout: 30000,
                message: `The page list's thumbnail of the ${where} never showed ${fileName}.`,
            },
        )
        .toBe(true);
}

/**
 * Put the fixture picture `fixtureName` into the first picture slot of the page being edited, and
 * return the name of the file it has in the book. Bloom gives a picture chosen a second time a new
 * name (house-landscape1.png), so that is the name the thumbnail must come to show. Checks first
 * that the page list's thumbnail of the page does not already show a picture of that name.
 */
async function choosePictureNotInThumbnail(
    page: Page,
    pageId: string,
    fixtureName: string,
    where: string,
): Promise<string> {
    const shownBefore = await pageThumbnail(page, pageId)
        .locator("img")
        .evaluateAll((imgs) =>
            imgs.map((img) => {
                const src = (img.getAttribute("src") ?? "").split("?")[0];
                return decodeURIComponent(src.split("/").pop() ?? src);
            }),
        );
    await chooseImageFile(page, fixtureImage(fixtureName));
    const fileName = (await getImagePlacement(page)).fileName;
    expect(
        shownBefore,
        `test setup: the ${where}'s thumbnail already showed ${fileName} before it went on the page`,
    ).not.toContain(fileName);
    return fileName;
}

/**
 * Make the edits of one round on the book the Edit tab is showing, checking after each that the
 * page list's thumbnail of the page changed shows it.
 */
async function editAndCheckPageThumbnails(
    page: Page,
    coverId: string,
    contentPageId: string,
    edits: IEdits,
    step: StepFunction,
) {
    await step(
        "Type on the content page, go to the cover, and check the content page's thumbnail shows the text",
        async () => {
            await selectPage(page, contentPageId);
            const thumbnailText = pageThumbnail(page, contentPageId).locator(
                ".bloom-editable[lang='en']",
            );
            await expect(
                thumbnailText.filter({ hasText: edits.text }),
                "test setup: the content page's thumbnail already shows the text about to be typed",
            ).toHaveCount(0);
            await typeInGroup(
                page,
                ".bloom-translationGroup",
                "en",
                edits.text,
            );
            await selectPage(page, coverId);
            await expect(
                thumbnailText.filter({ hasText: edits.text }),
                "the content page's thumbnail does not show the text typed on the page",
            ).toHaveCount(1, { timeout: 30000 });
        },
    );

    await step(
        `Put ${edits.contentPicture} on the content page, go to the cover, and check the content page's thumbnail shows it`,
        async () => {
            await selectPage(page, contentPageId);
            const fileName = await choosePictureNotInThumbnail(
                page,
                contentPageId,
                edits.contentPicture,
                "content page",
            );
            await selectPage(page, coverId);
            await expectThumbnailShowsPicture(
                page,
                contentPageId,
                fileName,
                "content page",
            );
            // The text typed before is still there: the thumbnail is of the page as it now is.
            await expect(
                pageThumbnail(page, contentPageId)
                    .locator(".bloom-editable[lang='en']")
                    .filter({ hasText: edits.text }),
                "the content page's thumbnail lost the text typed on the page",
            ).toHaveCount(1);
        },
    );

    await step(
        `Put ${edits.coverPicture} on the cover, go to the content page, and check the cover's thumbnail shows it`,
        async () => {
            const fileName = await choosePictureNotInThumbnail(
                page,
                coverId,
                edits.coverPicture,
                "cover",
            );
            await selectPage(page, contentPageId);
            await expectThumbnailShowsPicture(page, coverId, fileName, "cover");
        },
    );
}

/** Whether `after` is a different picture from `before`. */
function differ(before: ITileThumbnail, after: ITileThumbnail): boolean {
    return (
        before.width !== after.width ||
        before.height !== after.height ||
        before.pixels.some((v, i) => v !== after.pixels[i])
    );
}

/**
 * Wait until the Collections tab's tile of the book shows a thumbnail other than `before`, and check
 * that it is a thumbnail of the new cover picture. Bloom makes the new thumbnail.png after the
 * Collections tab shows, so the tile may first show the one made before the cover changed.
 */
async function expectTileThumbnailOfNewCover(
    page: Page,
    bookFolder: string,
    before: ITileThumbnail,
    edits: IEdits,
): Promise<ITileThumbnail> {
    let after: ITileThumbnail = before;
    await expect
        .poll(
            async () => {
                after = await readBookTileThumbnail(
                    page,
                    bookFolder,
                    readBookId(bookFolder),
                );
                return differ(before, after);
            },
            {
                timeout: 60000,
                message: `The book's tile kept the thumbnail it had before ${edits.coverPicture} went on the cover (${before.width}x${before.height}).`,
            },
        )
        .toBe(true);
    const shape = `${after.width}x${after.height}`;
    if (edits.coverIsWide)
        expect(
            after.width,
            `the tile's thumbnail (${shape}) is not wide like ${edits.coverPicture}, the new cover picture`,
        ).toBeGreaterThan(after.height * 1.5);
    else
        expect(
            after.height,
            `the tile's thumbnail (${shape}) is not tall like ${edits.coverPicture}, the new cover picture`,
        ).toBeGreaterThan(after.width);
    return after;
}

test("page thumbnails follow edits and the collection thumbnail follows the cover, in a new book and an existing one [Test Case ID 387]", async ({
    page,
    bloomApp,
    step,
}) => {
    test.setTimeout(600000);

    const made = await step(
        "Make a Basic Book with a content page, and read its tile's thumbnail",
        async () => {
            const bookFolder = await makeBookFromTemplate(page, "Basic Book");
            // A book made from a template starts with front and back matter only.
            await addPage(page, "Basic Text & Image");
            const contentPages = await getContentPages(page);
            const coverId = (await getPages(page))[0].id;
            await switchTab(page, "collection");
            const tile = await readBookTileThumbnail(
                page,
                bookFolder,
                readBookId(bookFolder),
            );
            await editBook(page, bookFolder);
            return {
                bookFolder,
                coverId,
                contentPageId: contentPages[0].id,
                tile,
            };
        },
    );
    if (!made) throw new Error("No book was made.");

    await editAndCheckPageThumbnails(
        page,
        made.coverId,
        made.contentPageId,
        NEW_BOOK_EDITS,
        step,
    );

    // The book's folder is named for its title, and a book with no title keeps the name Bloom
    // first gave it, so the folder stays where it was made.
    const newBookTile = await step(
        "Go to the Collections tab and check the book's tile shows the new cover",
        async () => {
            await switchTab(page, "collection");
            return expectTileThumbnailOfNewCover(
                page,
                made.bookFolder,
                made.tile,
                NEW_BOOK_EDITS,
            );
        },
    );
    if (!newBookTile) throw new Error("The new book's tile was not read.");

    const restarted = await step(
        "Close Bloom, start it again, and open the book in the Edit tab",
        async () => {
            const newPage = await bloomApp.restart();
            await editBook(newPage, made.bookFolder);
            return newPage;
        },
    );
    if (!restarted) throw new Error("Bloom did not start again.");

    await editAndCheckPageThumbnails(
        restarted,
        made.coverId,
        made.contentPageId,
        EXISTING_BOOK_EDITS,
        step,
    );

    await step(
        "Go to the Collections tab and check the existing book's tile shows its new cover",
        async () => {
            await switchTab(restarted, "collection");
            await expectTileThumbnailOfNewCover(
                restarted,
                made.bookFolder,
                newBookTile,
                EXISTING_BOOK_EDITS,
            );
        },
    );
});
