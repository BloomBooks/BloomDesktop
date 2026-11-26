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
    expectOnePictureNotDescribed,
    readSavedPicturesMarkedNotDescribed,
    setImageNotDescribed,
    showContentPageInImageDescriptionTool,
} from "../helpers/imageDescription";
import { chooseImageFile } from "../helpers/images";
import { enableToolForBook } from "../helpers/toolbox";

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

/** The book the test makes: its cover and its two content pages, each with one picture. */
interface IBook {
    bookFolder: string;
    cover: string;
    first: string;
    second: string;
}

/**
 * Quit Bloom the way a person does and start it again, then check that the saved book and the
 * reopened tool both give each content page's picture the mark expected. Returns the new shell page.
 */
async function restartAndExpectMarks(
    bloomApp: { quitAndRestart: () => Promise<Page> },
    book: IBook,
    expected: { first: boolean; second: boolean },
    when: string,
): Promise<Page> {
    const shell = await bloomApp.quitAndRestart();
    expect(
        await readSavedPicturesMarkedNotDescribed(
            shell,
            book.bookFolder,
            book.first,
        ),
        `the saved first page, ${when}`,
    ).toEqual([expected.first]);
    expect(
        await readSavedPicturesMarkedNotDescribed(
            shell,
            book.bookFolder,
            book.second,
        ),
        `the saved second page, ${when}`,
    ).toEqual([expected.second]);

    await editBook(shell, book.bookFolder);
    for (const which of ["first", "second"] as const) {
        await showContentPageInImageDescriptionTool(
            shell,
            book.cover,
            book[which],
        );
        await expectOnePictureNotDescribed(
            shell,
            `The ${which} page, ${when}`,
            expected[which],
        );
    }
    return shell;
}

test("the image description tool remembers which pictures should not be described [Test Case ID 577]", async ({
    page,
    bloomApp,
    step,
}) => {
    test.setTimeout(600000);

    const book = await step(
        "Make a book with two content pages, each with its own picture",
        async (): Promise<IBook> => {
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
    if (!book) throw new Error("The book was not made.");
    const { cover, first, second } = book;

    await step(
        "Tick the box for the first page's picture only; it stays with that picture across page changes",
        async () => {
            await showContentPageInImageDescriptionTool(page, cover, first);
            await expectOnePictureNotDescribed(
                page,
                "The first page, before ticking",
                false,
            );
            await setImageNotDescribed(page, true);
            await expectOnePictureNotDescribed(
                page,
                "The first page, just ticked",
                true,
            );

            for (let round = 1; round <= 2; round++) {
                await showContentPageInImageDescriptionTool(
                    page,
                    cover,
                    second,
                );
                await expectOnePictureNotDescribed(
                    page,
                    `The second page (visit ${round}), with only the first page's box ticked`,
                    false,
                );
                await showContentPageInImageDescriptionTool(page, cover, first);
                await expectOnePictureNotDescribed(
                    page,
                    `Back on the first page (visit ${round})`,
                    true,
                );
            }
        },
    );

    const shell = await step(
        "Close Bloom and open it again: the book saved the mark, and the box shows it on the first page only",
        async () =>
            restartAndExpectMarks(
                bloomApp,
                book,
                { first: true, second: false },
                "after a restart",
            ),
    );
    if (!shell) throw new Error("Bloom did not restart.");

    await step(
        "Move the mark: untick the first page's box, tick the second's, and close Bloom straight from that page",
        async () => {
            await showContentPageInImageDescriptionTool(shell, cover, first);
            await setImageNotDescribed(shell, false);
            await expectOnePictureNotDescribed(
                shell,
                "The first page, just unticked",
                false,
            );
            await showContentPageInImageDescriptionTool(shell, cover, second);
            await expectOnePictureNotDescribed(
                shell,
                "The second page, before ticking",
                false,
            );
            // Bloom is closed from this page right after the click, with no page change between,
            // so only saving on the way out can keep the second page's mark.
            await setImageNotDescribed(shell, true);
            await expectOnePictureNotDescribed(
                shell,
                "The second page, just ticked",
                true,
            );
            await restartAndExpectMarks(
                bloomApp,
                book,
                { first: false, second: true },
                "after a restart from the second page",
            );
        },
    );
});
