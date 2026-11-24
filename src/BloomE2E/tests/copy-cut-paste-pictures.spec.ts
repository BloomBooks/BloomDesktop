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
// processor, put on the clipboard.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    type IBookPage,
} from "../helpers/bookMaking";
import { copyToClipboard, getClipboard } from "../helpers/clipboard";
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

test.describe.configure({ mode: "serial" });

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

/** The pages, each with one empty picture box. Set by the first test. */
let pages: IBookPage[];

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
 * Paste a picture file as if copied in File Explorer into pages[first], check its size and
 * credits, then copy it with Copy Image and paste it into pages[first + 1], and check that the
 * copy has the same size and credits.
 */
async function pasteFromExplorerThenCopyToNextPage(
    page: Parameters<typeof getPictureInBox>[0],
    file: string,
    first: number,
): Promise<void> {
    const original = readPictureFileSize(file);
    // Sanity check: the fixture must be too big, or nothing would need shrinking.
    expect(original.width).toBeGreaterThan(MAX_WIDTH);

    await goToPage(page, pages[first].id);
    await copyToClipboard(page, { files: [file] });
    const pasted = await pasteIntoPictureBox(page);
    expectShrunkToFit(pasted, original);
    expect(creditsOf(pasted), "the credits in the file").toEqual(CREDITS);

    await copyPictureInBox(page);
    await goToPage(page, pages[first + 1].id);
    const copy = await pasteIntoPictureBox(page);
    expect(
        { width: copy.width, height: copy.height },
        "the copy's size should be the size of the picture copied",
    ).toEqual({ width: pasted.width, height: pasted.height });
    expect(creditsOf(copy), "the copy's credits").toEqual(CREDITS);
}

test.describe("Copy Cut Paste Pictures", () => {
    test("builds a book with six pages that each have an empty picture box", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        for (let i = 0; i < 6; i++) await addPage(page, "Basic Text & Image");
        pages = await getContentPages(page);
        expect(pages).toHaveLength(6);
        for (const bookPage of pages) {
            await goToPage(page, bookPage.id);
            expect((await getPictureInBox(page)).fileName).toBe(
                "placeHolder.png",
            );
        }
    });

    test("a large JPEG from File Explorer is shrunk and keeps its credits, and so does a copy of it on another page [Test Case ID 281]", async ({
        page,
    }) => {
        await pasteFromExplorerThenCopyToNextPage(page, LARGE_JPEG, 0);
    });

    test("a large PNG from File Explorer is shrunk and keeps its credits, and so does a copy of it on another page [Test Case ID 281]", async ({
        page,
    }) => {
        await pasteFromExplorerThenCopyToNextPage(page, LARGE_PNG, 2);
    });

    test("a large picture that another program put on the clipboard is shrunk to fit [Test Case ID 281]", async ({
        page,
    }) => {
        for (const [index, file] of [
            [4, LARGE_JPEG],
            [5, LARGE_PNG],
        ] as const) {
            await goToPage(page, pages[index].id);
            await copyToClipboard(page, { picture: file });
            expectShrunkToFit(
                await pasteIntoPictureBox(page),
                readPictureFileSize(file),
            );
        }
    });

    test("Copy Image puts the picture on the clipboard for another program [Test Case ID 281]", async ({
        page,
    }) => {
        await goToPage(page, pages[0].id);
        await copyToClipboard(page, { text: "something else" });
        await copyPictureInBox(page);
        await expect
            .poll(async () => (await getClipboard(page)).picture ?? "", {
                timeout: 15000,
                message: "Copy Image did not put a picture on the clipboard.",
            })
            .toMatch(/large-with-credits\.jpg$/);
    });
});
