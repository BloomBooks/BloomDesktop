// Pasting a large picture on a new book's cover while something else holds the book's new files
// open, as antivirus programs and sync clients such as Dropbox do (Test Case ID 257).
//
// The top bar's context menu has a developer command, "Meddle with New Files", that starts
// meddle.exe in the collection's folder. meddle.exe watches that folder and, for a moment, opens
// every file that is created or changed there so that nobody else may touch it. So everything
// Bloom writes while it runs (the picture it copies into the book, the book's .htm, its thumbnail,
// the folder it renames when the book gets a title) finds the file locked at first, and Bloom has
// to cope. Turning the meddler on is the test's setup, so it goes through the API that the menu
// command posts; the paste and the trip to the Collections tab go through the real UI.
//
// A problem that Bloom reports instead of coping shows as its "Bloom had a problem" dialog, which
// the fixture's watcher turns into a failure of the test.

import * as fs from "node:fs";
import * as Path from "node:path";
import type { FrameLocator, Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { apiGetJson, apiPost } from "../helpers/api";
import {
    editablePageFrame,
    findBookFolder,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    clickCanvasElementMenuItem,
    selectCanvasElementAt,
} from "../helpers/canvasElements";
import { copyToClipboard } from "../helpers/clipboard";
import {
    getImagePlacement,
    makeJpegFile,
    readImageFileSize,
} from "../helpers/images";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "file-meddler-large-image", languages: ["en"] },
});

// A photo of this size is several megabytes, and larger than anything Bloom keeps in a book, so
// Bloom shrinks it while the meddler holds the files it writes.
const LARGE_WIDTH = 6000;
const LARGE_HEIGHT = 3375;
const PASTE_IMAGE_COMMAND = "EditTab.Image.PasteImage";
const PLACEHOLDER = "placeHolder.png";

/** Start meddle.exe in the collection's folder, as "Meddle with New Files" does. */
async function startMeddling(page: Page): Promise<void> {
    await apiPost(
        page,
        "app/isMeddlingWithNewFiles",
        "true",
        "application/json",
    );
    // Bloom reports meddling only once it has started meddle.exe, and starts nothing when the
    // build has no meddle.exe beside Bloom.exe; without it this test would prove nothing.
    expect(
        await apiGetJson<boolean>(page, "app/isMeddlingWithNewFiles"),
        "test setup: Bloom did not start meddle.exe (is it missing from the build output?)",
    ).toBe(true);
}

async function stopMeddling(page: Page): Promise<void> {
    await apiPost(
        page,
        "app/isMeddlingWithNewFiles",
        "false",
        "application/json",
    );
}

/**
 * Paste the picture on Bloom's clipboard into the cover picture the way a person does: select the
 * picture, then choose Paste image from its menu. Returns the file name the cover shows once the
 * picture has loaded.
 */
async function pasteOnCover(page: Page): Promise<string> {
    const before = await getImagePlacement(page);
    expect(
        before.fileName,
        "test setup: a new book's cover starts with no picture",
    ).toBe(PLACEHOLDER);
    // An empty picture shows no image to click, so select the picture's box itself.
    await selectCanvasElementAt(
        editablePageFrame(page)
            .locator(
                ".bloom-canvas .bloom-canvas-element.bloom-backgroundImage",
            )
            .first(),
        "the cover picture",
    );
    await clickCanvasElementMenuItem(page, PASTE_IMAGE_COMMAND);
    await expect
        .poll(
            async () => {
                const placement = await getImagePlacement(page);
                return placement.fileName !== PLACEHOLDER && placement.loaded;
            },
            {
                timeout: 120000,
                message:
                    "The cover never showed the pasted picture while the meddler was running.",
            },
        )
        .toBe(true);
    return (await getImagePlacement(page)).fileName;
}

/** The book preview on the right of the Collections tab. */
function collectionBookPreview(page: Page): FrameLocator {
    return page.frameLocator('iframe[src*="book-preview/index.htm"]');
}

/**
 * Go to the Collections tab and check that it shows the book's cover with the pasted picture in
 * the preview on the right, and a thumbnail on the book's button.
 */
async function expectCoverInCollectionsTab(
    page: Page,
    pictureFile: string,
): Promise<void> {
    await switchTab(page, "collection");
    const coverPicture = collectionBookPreview(page)
        .locator(
            ".bloom-page.outsideFrontCover .bloom-canvas .bloom-backgroundImage img",
        )
        .first();
    await expect
        .poll(
            async () =>
                coverPicture.evaluate((img) => {
                    const image = img as HTMLImageElement;
                    const src = decodeURIComponent(
                        (image.getAttribute("src") ?? "").split("?")[0],
                    );
                    return {
                        file: src.split("/").pop(),
                        loaded: image.complete && image.naturalWidth > 0,
                    };
                }),
            {
                timeout: 60000,
                message:
                    "The Collections tab's preview never showed the cover with the pasted picture.",
            },
        )
        .toEqual({ file: pictureFile, loaded: true });

    const thumbnail = page
        .locator('img[src*="collections/book/thumbnail"]')
        .first();
    await expect
        .poll(
            async () =>
                thumbnail.evaluate(
                    (img) =>
                        (img as HTMLImageElement).complete &&
                        (img as HTMLImageElement).naturalWidth > 0,
                ),
            {
                timeout: 60000,
                message:
                    "The book's button on the Collections tab never showed a thumbnail.",
            },
        )
        .toBe(true);
}

/** The picture Bloom saved in the book's folder: it exists, and Bloom shrank it. */
function expectPictureSaved(bookFolder: string, pictureFile: string): void {
    const saved = Path.join(bookFolder, pictureFile);
    expect(
        fs.existsSync(saved),
        `The cover shows ${pictureFile}, but it is not in the book's folder ${bookFolder}.`,
    ).toBe(true);
    const size = readImageFileSize(saved);
    expect(
        size.width,
        "Bloom kept the pasted picture at its full size instead of shrinking it",
    ).toBeLessThan(LARGE_WIDTH);
}

for (const titled of [false, true]) {
    const name = titled ? "a new book with a title" : "a new book";
    test(`pasting a large picture on the cover of ${name} while files are meddled with [Test Case ID 257]`, async ({
        page,
    }, testInfo) => {
        test.setTimeout(400000);
        const largePicture = testInfo.outputPath("large-photo.jpg");
        await makeJpegFile(page, largePicture, LARGE_WIDTH, LARGE_HEIGHT);
        expect(
            fs.statSync(largePicture).size,
            "test setup: the large picture is several megabytes",
        ).toBeGreaterThan(1000000);

        await startMeddling(page);
        try {
            const untitledFolder = await makeBookFromTemplate(
                page,
                "Basic Book",
            );
            const title = "Meddled Photo Book";
            if (titled) await typeInGroup(page, ".bookTitle", "en", title);

            await copyToClipboard(page, { picture: largePicture });
            const pictureFile = await pasteOnCover(page);

            await expectCoverInCollectionsTab(page, pictureFile);

            const bookFolder = titled
                ? await findBookFolder(page, title)
                : untitledFolder;
            if (titled)
                expect(
                    Path.basename(bookFolder),
                    "Bloom did not name the book's folder for its title",
                ).toBe(title);
            expectPictureSaved(bookFolder, pictureFile);
        } finally {
            await stopMeddling(page);
        }
    });
}
