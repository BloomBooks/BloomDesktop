// The Image Description tool's "This image should not be described." box belongs to one picture:
// ticking it marks that picture (aria-hidden="true" on its canvas, which an ePUB and Bloom Player
// read to skip it), and the box shows each picture's own mark again whenever that picture's page is
// shown, after other pages, and after Bloom is closed and opened again.
//
// Automates the manual test "Remember Skip-Description Setting" (Test Case ID 577).

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editBook,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    getPicturesMarkedNotDescribed,
    imageNotDescribedBox,
    openImageDescriptionTool,
    readSavedPicturesMarkedNotDescribed,
    setImageNotDescribed,
} from "../helpers/imageDescription";
import { chooseImageFile } from "../helpers/images";
import { enableToolForBook, toolboxFrame } from "../helpers/toolbox";

test.use({
    collectionSpec: {
        name: "image-description-not-needed-remembered",
        languages: ["en"],
    },
});

const IMAGES = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
);

/**
 * Show a content page by way of the cover, and wait until the tool has taken up each in turn. The
 * tool greys out its box on front matter, so the box's group losing that grey shows that the box
 * now speaks for the content page, not for whichever page was shown before it.
 */
async function showContentPage(
    page: Page,
    coverId: string,
    pageId: string,
): Promise<void> {
    const boxGroup = toolboxFrame(page).locator(
        ".imgDescLabelBlock:has(.imageDescriptionCheck)",
    );
    await goToPage(page, coverId);
    await openImageDescriptionTool(page);
    await expect(boxGroup, "the box is greyed out on the cover").toHaveClass(
        /\bdisabled\b/,
    );
    await goToPage(page, pageId);
    await expect(
        boxGroup,
        "the box is no longer greyed out on a content page",
    ).not.toHaveClass(/\bdisabled\b/);
}

/**
 * Wait until the page being edited and the tool both say whether its one picture should not be
 * described: the picture's own mark, and the box.
 */
async function expectPictureNotDescribed(
    page: Page,
    where: string,
    notDescribed: boolean,
): Promise<void> {
    await openImageDescriptionTool(page);
    await expect
        .poll(
            async () => ({
                pictures: await getPicturesMarkedNotDescribed(page),
                box: await imageNotDescribedBox(page).isChecked(),
            }),
            {
                message: `${where}: the picture's mark and the "should not be described" box`,
                timeout: 30000,
            },
        )
        .toEqual({ pictures: [notDescribed], box: notDescribed });
}

test("the image description tool remembers which pictures should not be described [Test Case ID 577]", async ({
    page,
    bloomApp,
    step,
}) => {
    test.setTimeout(600000);

    const made = await step(
        "Make a book with two content pages, each with its own picture, and open the Image Description tool",
        async () => {
            const bookFolder = await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Basic Text & Image", 2);
            await enableToolForBook(page, bookFolder, "imageDescription");
            const contentPages = await getContentPages(page);
            expect(
                contentPages.length,
                "test setup: the book has the two content pages added",
            ).toBe(2);
            const [first, second] = contentPages;
            const cover = (await getPages(page))[0];
            expect(
                cover.isContentPage,
                "test setup: the first page is the cover",
            ).toBe(false);

            await goToPage(page, first.id);
            await chooseImageFile(page, Path.join(IMAGES, "bird.png"));
            await goToPage(page, second.id);
            await chooseImageFile(
                page,
                Path.join(IMAGES, "house-landscape.png"),
            );
            return {
                bookFolder,
                cover: cover.id,
                first: first.id,
                second: second.id,
            };
        },
    );
    if (!made) throw new Error("The book was not made.");
    const { bookFolder, cover, first, second } = made;

    await step(
        "Tick the box for the first page's picture only; it stays with that picture across page changes",
        async () => {
            await showContentPage(page, cover, first);
            await expectPictureNotDescribed(
                page,
                "The first page, before ticking",
                false,
            );
            await setImageNotDescribed(page, true);
            await expectPictureNotDescribed(
                page,
                "The first page, just ticked",
                true,
            );

            await showContentPage(page, cover, second);
            await expectPictureNotDescribed(
                page,
                "The second page, after the first page's box was ticked",
                false,
            );
            await showContentPage(page, cover, first);
            await expectPictureNotDescribed(
                page,
                "Back on the first page",
                true,
            );
            await showContentPage(page, cover, second);
            await expectPictureNotDescribed(
                page,
                "Back on the second page",
                false,
            );
        },
    );

    let shell = page;
    await step(
        "Close Bloom and open it again: the book saved the mark, and the box shows it on the first page only",
        async () => {
            shell = await bloomApp.quitAndRestart();
            expect(
                await readSavedPicturesMarkedNotDescribed(
                    shell,
                    bookFolder,
                    first,
                ),
                "the saved first page",
            ).toEqual([true]);
            expect(
                await readSavedPicturesMarkedNotDescribed(
                    shell,
                    bookFolder,
                    second,
                ),
                "the saved second page",
            ).toEqual([false]);

            await editBook(shell, bookFolder);
            await showContentPage(shell, cover, first);
            await expectPictureNotDescribed(
                shell,
                "The first page after a restart",
                true,
            );
            await showContentPage(shell, cover, second);
            await expectPictureNotDescribed(
                shell,
                "The second page after a restart",
                false,
            );
        },
    );

    await step(
        "Move the mark: tick the second page's box and untick the first's, then close and open Bloom again",
        async () => {
            await setImageNotDescribed(shell, true);
            await showContentPage(shell, cover, first);
            await expectPictureNotDescribed(
                shell,
                "The first page, before unticking",
                true,
            );
            await setImageNotDescribed(shell, false);
            await showContentPage(shell, cover, second);
            await expectPictureNotDescribed(
                shell,
                "The second page, after its box was ticked",
                true,
            );
            await showContentPage(shell, cover, first);
            await expectPictureNotDescribed(
                shell,
                "The first page, after its box was unticked",
                false,
            );

            shell = await bloomApp.quitAndRestart();
            expect(
                await readSavedPicturesMarkedNotDescribed(
                    shell,
                    bookFolder,
                    first,
                ),
                "the saved first page, after unticking",
            ).toEqual([false]);
            expect(
                await readSavedPicturesMarkedNotDescribed(
                    shell,
                    bookFolder,
                    second,
                ),
                "the saved second page, after ticking",
            ).toEqual([true]);

            await editBook(shell, bookFolder);
            await showContentPage(shell, cover, second);
            await expectPictureNotDescribed(
                shell,
                "The second page after a second restart",
                true,
            );
            await showContentPage(shell, cover, first);
            await expectPictureNotDescribed(
                shell,
                "The first page after a second restart",
                false,
            );
        },
    );
});
