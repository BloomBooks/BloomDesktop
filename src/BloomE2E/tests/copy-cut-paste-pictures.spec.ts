// Pasting a large picture into a picture box: Bloom shrinks it to fit 3840x2800 and keeps the
// credits (copyright, creator, license) that the file carries, and a copy of that picture pasted
// into another page's picture box keeps its size and credits.
// Automates the manual test "Copy Cut Paste Pictures" (Test Case ID 281).
//
// The two fixtures, large-with-credits.jpg and large-with-credits.png, are 4200x3000 gradients
// with credits in their XMP, made with ImageMagick:
//   magick -size 3000x4200 gradient:red-blue -rotate 90 -quality 70 -profile meta.xmp large-with-credits.jpg
//   magick -size 3000x4200 gradient:yellow-green -rotate 90 -depth 8 -profile meta.xmp large-with-credits.png
// where meta.xmp sets dc:rights, dc:creator and cc:license to the values in CREDITS below. A
// gradient has many colors and no white, so Bloom does not stop the JPEG paste to warn that the
// picture should have been a PNG (JpegWarningDialog, a WinForms dialog no test can answer).
//
// Under --e2e Bloom's clipboard is its own (helpers/clipboard.ts): `files` stands for a file
// copied in File Explorer, and `picture` for a picture that another program, such as a word
// processor, put on the clipboard. That `picture` is always a file on disk, so the paste of a
// picture that has no file behind it (a screenshot, say) is not covered here.
//
// The JPEG test is the journey test for the picture menu's Copy Image and Paste Image: it goes to
// each page by clicking its thumbnail, selects the picture box with a click and runs both commands
// from the picture's menu. Like the other journey tests, it makes its book and adds its pages
// through the API, since those are other UI paths. The other tests also go to their pages through
// the API. Each test makes what it needs, so none depends on another having run or passed.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import { copyToClipboard, getClipboard } from "../helpers/clipboard";
import { selectPage } from "../helpers/pageThumbnails";
import {
    copyPictureInBox,
    getPictureInBox,
    pasteIntoPictureBox,
    readPictureFileSize,
    type IPictureInBox,
} from "../helpers/pictureClipboard";

test.use({
    collectionSpec: { name: "copy-cut-paste-pictures", languages: ["en"] },
});

const fixture = (name: string) =>
    Path.resolve(
        Path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "fixtures",
        "images",
        name,
    );
const LARGE_JPEG = fixture("large-with-credits.jpg");
const LARGE_PNG = fixture("large-with-credits.png");

/** The largest picture Bloom keeps: it shrinks a larger one to fit inside this. */
const MAX_WIDTH = 3840;
const MAX_HEIGHT = 2800;

/** The credits in both fixtures' XMP, as Bloom shows them on the picture. */
const CREDITS = {
    copyright: "Copyright 2026, Test Owner",
    creator: "Test Artist",
    license: "cc-by",
};

/**
 * Whether this worker's Bloom has made the book the tests share. Each test that needs the book
 * makes it if no test before it in this worker did, so any one test can be run by itself.
 */
let bookMade = false;

/** Make the book the tests share, unless a test before this one in this worker made it. */
async function ensureBook(page: Page): Promise<void> {
    if (bookMade) return;
    await makeBookFromTemplate(page, "Basic Book");
    bookMade = true;
}

/**
 * Add `count` Basic Text & Image pages, each with one picture box, to the book through the API,
 * and return their ids in the order they were added.
 */
async function addPagesWithPictureBoxes(
    page: Page,
    count: number,
): Promise<string[]> {
    await ensureBook(page);
    const added: string[] = [];
    for (let i = 0; i < count; i++) {
        const before = (await getContentPages(page)).map((p) => p.id);
        await addPage(page, "Basic Text & Image");
        const newPages = (await getContentPages(page)).filter(
            (p) => !before.includes(p.id),
        );
        expect(newPages, "the page just added").toHaveLength(1);
        added.push(newPages[0].id);
    }
    return added;
}

/** Check that the picture box of the page being edited is empty. */
async function expectEmptyPictureBox(page: Page): Promise<void> {
    expect(
        (await getPictureInBox(page)).fileName,
        "the picture box should be empty before the paste",
    ).toBe("placeHolder.png");
}

const creditsOf = (picture: IPictureInBox) => ({
    copyright: picture.copyright,
    creator: picture.creator,
    license: picture.license,
});

/** Check that a pasted picture fits 3840x2800, and was shrunk from `original` in proportion. */
function expectShrunkToFit(
    picture: IPictureInBox,
    original: { width: number; height: number },
): void {
    expect(picture.width, "the picture's width").toBeLessThanOrEqual(MAX_WIDTH);
    expect(picture.height, "the picture's height").toBeLessThanOrEqual(
        MAX_HEIGHT,
    );
    expect(
        picture.width,
        "the picture should have been shrunk, not left at its size",
    ).toBeLessThan(original.width);
    expect(
        Math.abs(
            picture.width / picture.height - original.width / original.height,
        ),
        "the shrunk picture should keep the original's proportions",
    ).toBeLessThan(0.01);
}

/**
 * Copy the picture in the page's box with Copy Image, and wait until the clipboard holds that
 * picture. The clipboard is first given something else, so that a Copy Image that did nothing
 * cannot leave the file copied earlier there to be pasted again.
 */
async function copyPictureToClipboard(
    page: Page,
    picture: IPictureInBox,
): Promise<void> {
    await copyToClipboard(page, { text: "something else" });
    expect((await getClipboard(page)).picture).toBeUndefined();
    await copyPictureInBox(page);
    await expect
        .poll(
            async () => {
                const copied = (await getClipboard(page)).picture;
                return copied ? Path.win32.basename(copied) : "(no picture)";
            },
            {
                timeout: 15000,
                message: "Copy Image did not put the picture on the clipboard.",
            },
        )
        .toBe(picture.fileName);
}

/**
 * Paste a picture file as if copied in File Explorer into the box on the first page, check its
 * size and credits, then copy it with Copy Image and paste it into the box on the second page,
 * and check that the copy has the same size and credits. `showPage` takes the Edit tab to a page.
 */
async function pasteFromExplorerThenCopyToNextPage(
    page: Page,
    file: string,
    [first, second]: string[],
    showPage: (id: string) => Promise<void>,
): Promise<void> {
    const original = readPictureFileSize(file);
    // Sanity check: the fixture must be too big, or nothing would need shrinking.
    expect(original.width).toBeGreaterThan(MAX_WIDTH);

    await showPage(first);
    await expectEmptyPictureBox(page);
    await copyToClipboard(page, { files: [file] });
    const pasted = await pasteIntoPictureBox(page);
    expectShrunkToFit(pasted, original);
    expect(creditsOf(pasted), "the credits in the file").toEqual(CREDITS);

    await copyPictureToClipboard(page, pasted);
    await showPage(second);
    await expectEmptyPictureBox(page);
    const copy = await pasteIntoPictureBox(page);
    expect(
        { width: copy.width, height: copy.height },
        "the copy's size should be the size of the picture copied",
    ).toEqual({ width: pasted.width, height: pasted.height });
    expect(creditsOf(copy), "the copy's credits").toEqual(CREDITS);
}

test.describe("Copy Cut Paste Pictures", () => {
    test("a large JPEG from File Explorer is shrunk and keeps its credits, and so does a copy of it on another page, all through the menus [Test Case ID 281]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        const ids = await addPagesWithPictureBoxes(page, 2);
        await pasteFromExplorerThenCopyToNextPage(page, LARGE_JPEG, ids, (id) =>
            selectPage(page, id),
        );
    });

    test("a large PNG from File Explorer is shrunk and keeps its credits, and so does a copy of it on another page [Test Case ID 281]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        const ids = await addPagesWithPictureBoxes(page, 2);
        await pasteFromExplorerThenCopyToNextPage(page, LARGE_PNG, ids, (id) =>
            goToPage(page, id),
        );
    });

    test("a large picture that another program put on the clipboard is shrunk to fit [Test Case ID 281]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        const ids = await addPagesWithPictureBoxes(page, 2);
        for (const [id, file] of [
            [ids[0], LARGE_JPEG],
            [ids[1], LARGE_PNG],
        ] as const) {
            await goToPage(page, id);
            await expectEmptyPictureBox(page);
            await copyToClipboard(page, { picture: file });
            expectShrunkToFit(
                await pasteIntoPictureBox(page),
                readPictureFileSize(file),
            );
        }
    });
});
