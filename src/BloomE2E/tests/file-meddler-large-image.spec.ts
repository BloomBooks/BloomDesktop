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
// Under --e2e Bloom does not show a problem it reports with NonFatalProblem (it writes it to
// stderr), so a save that failed shows nowhere on screen. That is why the test reads what Bloom
// saved: the book's .htm, the picture file and the thumbnail, rather than trusting the page shown.

import * as fs from "node:fs";
import * as Path from "node:path";
import type { FrameLocator, Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { apiGetJson, apiPost } from "../helpers/api";
import { bookHtmlPath } from "../helpers/bookHtml";
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
    type IPixelSize,
    makeJpegFile,
    readImageFileSize,
} from "../helpers/images";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "file-meddler-large-image", languages: ["en"] },
});

// A photo of this size is larger than anything Bloom keeps in a book, so Bloom shrinks it while
// the meddler holds the files it writes. It is also over a megabyte even when makeJpegFile draws
// it without a GPU, as on the nightly runner.
const LARGE_WIDTH = 8000;
const LARGE_HEIGHT = 4500;
const PASTE_IMAGE_COMMAND = "EditTab.Image.PasteImage";
const PLACEHOLDER = "placeHolder.png";
// The error codes Node gives for opening a file that another process holds open without sharing.
const LOCKED_CODES = ["EBUSY", "EPERM", "EACCES"];

/** Try to open `file` for writing and close it again: "open", or the error's code. */
function tryOpen(file: string): string {
    try {
        fs.closeSync(fs.openSync(file, "r+"));
        return "open";
    } catch (error) {
        return (error as NodeJS.ErrnoException).code ?? String(error);
    }
}

/**
 * Check that meddle.exe is watching `collectionDir` and locking what changes there: write a file,
 * see it locked for a moment, then wait until it is let go and remove it.
 *
 * meddle.exe takes a moment to start watching after Bloom starts it, and it never notices a
 * change made before then. So until the lock shows, the file is written again every half second,
 * and each write is a change it locks the file for once it is watching.
 */
async function expectMeddlerLocksNewFiles(
    collectionDir: string,
): Promise<void> {
    const probe = Path.join(collectionDir, `meddler-probe-${Date.now()}.txt`);
    let lastWrite = 0;
    const lockedNow = () => {
        if (LOCKED_CODES.includes(tryOpen(probe))) return true;
        if (Date.now() - lastWrite >= 500) {
            fs.appendFileSync(probe, "Is anyone meddling with this file?\n");
            lastWrite = Date.now();
        }
        return false;
    };
    await expect
        .poll(lockedNow, {
            timeout: 30000,
            intervals: [20],
            message:
                "meddle.exe never locked a new file in the collection's folder, so it is not meddling.",
        })
        .toBe(true);
    await expect
        .poll(() => tryOpen(probe), {
            timeout: 30000,
            message: "meddle.exe never let go of the file it locked.",
        })
        .toBe("open");
    fs.rmSync(probe);
}

/** Start meddle.exe in the collection's folder, as "Meddle with New Files" does. */
async function startMeddling(page: Page, collectionDir: string): Promise<void> {
    await apiPost(
        page,
        "app/isMeddlingWithNewFiles",
        "true",
        "application/json",
    );
    // Bloom starts nothing when the build has no meddle.exe beside Bloom.exe, and then reports
    // that it is not meddling; without the meddler this test would prove nothing.
    expect(
        await apiGetJson<boolean>(page, "app/isMeddlingWithNewFiles"),
        "test setup: Bloom did not start meddle.exe (is it missing from the build output?)",
    ).toBe(true);
    await expectMeddlerLocksNewFiles(collectionDir);
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

/** The Collections tab's preview shows the book's cover with the pasted picture, loaded. */
async function expectCoverInPreview(
    page: Page,
    pictureFile: string,
): Promise<void> {
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
}

/**
 * `read()`, or `whileLocked` when it throws because meddle.exe holds the file it reads. Any other
 * error is thrown on.
 */
function unlessLocked<T>(read: () => T, whileLocked: T): T {
    try {
        return read();
    } catch (error) {
        if (LOCKED_CODES.includes((error as NodeJS.ErrnoException).code ?? ""))
            return whileLocked;
        throw error;
    }
}

/** `read()`, once meddle.exe has let go of the file it reads. */
async function readWhenFree<T>(read: () => T, what: string): Promise<T> {
    let value: T | undefined;
    await expect
        .poll(
            () =>
                unlessLocked(() => {
                    value = read();
                    return true;
                }, false),
            {
                timeout: 30000,
                message: `meddle.exe never let go of ${what}.`,
            },
        )
        .toBe(true);
    return value as T;
}

/**
 * The file name of the cover picture in the book's saved .htm, or "" when it has none yet or
 * meddle.exe is holding the file.
 */
function readSavedCoverPicture(bookFolder: string): string {
    const htmlPath = bookHtmlPath(bookFolder);
    if (!fs.existsSync(htmlPath)) return "";
    const html = unlessLocked(() => fs.readFileSync(htmlPath, "utf8"), "");
    const coverImg = /<img\b[^>]*\bdata-book="coverImage"[^>]*>/.exec(html);
    const src = coverImg && /\bsrc="([^"]*)"/.exec(coverImg[0]);
    return src ? decodeURIComponent(src[1].split("?")[0]) : "";
}

/**
 * The size of the book's thumbnail.png if Bloom wrote it at or after `since`, else undefined.
 * Undefined too while meddle.exe holds it.
 */
function readFreshThumbnailSize(
    thumbnailPath: string,
    since: number,
): IPixelSize | undefined {
    if (!fs.existsSync(thumbnailPath)) return undefined;
    if (fs.statSync(thumbnailPath).mtimeMs < since) return undefined;
    return unlessLocked<IPixelSize | undefined>(
        () => readImageFileSize(thumbnailPath),
        undefined,
    );
}

/**
 * What Bloom saved: the book's .htm has the pasted picture on its cover, the picture file is in
 * the book's folder and Bloom shrank it, and Bloom made the book a new thumbnail, which its button
 * on the Collections tab shows.
 */
async function expectBookSaved(
    page: Page,
    bookFolder: string,
    pictureFile: string,
    pastedAt: number,
): Promise<void> {
    await expect
        .poll(() => readSavedCoverPicture(bookFolder), {
            timeout: 60000,
            message: `Bloom never saved ${bookHtmlPath(bookFolder)} with the pasted picture on its cover.`,
        })
        .toBe(pictureFile);

    const saved = Path.join(bookFolder, pictureFile);
    expect(
        fs.existsSync(saved),
        `The cover shows ${pictureFile}, but it is not in the book's folder ${bookFolder}.`,
    ).toBe(true);
    const savedSize = await readWhenFree(() => readImageFileSize(saved), saved);
    expect(
        savedSize.width,
        "Bloom kept the pasted picture at its full size instead of shrinking it",
    ).toBeLessThan(LARGE_WIDTH);

    // Bloom makes the thumbnail from the cover picture, so it has the pasted photo's 16:9 shape.
    // When Bloom cannot make one, it writes its square error icon to thumbnail.png instead.
    const thumbnailPath = Path.join(bookFolder, "thumbnail.png");
    const pictureShape = LARGE_WIDTH / LARGE_HEIGHT;
    await expect
        .poll(
            () => {
                const size = readFreshThumbnailSize(thumbnailPath, pastedAt);
                return size
                    ? Math.abs(size.width / size.height - pictureShape) < 0.05
                    : false;
            },
            {
                timeout: 60000,
                message: `Bloom never made ${thumbnailPath} from the pasted picture (an old file, none, or its square error icon).`,
            },
        )
        .toBe(true);
    const thumbnailSize = await readWhenFree(
        () => readImageFileSize(thumbnailPath),
        thumbnailPath,
    );

    const bookId = await readWhenFree(
        () =>
            (
                JSON.parse(
                    fs.readFileSync(Path.join(bookFolder, "meta.json"), "utf8"),
                ) as { bookInstanceId: string }
            ).bookInstanceId,
        "the book's meta.json",
    );
    const thumbnail = page
        .locator(
            `img[src*="collections/book/thumbnail?book-id=${encodeURIComponent(bookId)}&"]`,
        )
        .first();
    // The button shows that thumbnail: a picture of its size, not Bloom's placeholder or error icon.
    await expect
        .poll(
            async () =>
                thumbnail.evaluate((img) => {
                    const image = img as HTMLImageElement;
                    return image.complete
                        ? {
                              width: image.naturalWidth,
                              height: image.naturalHeight,
                          }
                        : undefined;
                }),
            {
                timeout: 60000,
                message:
                    "The book's button on the Collections tab never showed its new thumbnail.",
            },
        )
        .toEqual(thumbnailSize);
}

for (const titled of [false, true]) {
    const name = titled ? "a new book with a title" : "a new book";
    test(`pasting a large picture on the cover of ${name} while files are meddled with [Test Case ID 257]`, async ({
        page,
        bloomApp,
    }, testInfo) => {
        test.setTimeout(400000);
        const largePicture = testInfo.outputPath("large-photo.jpg");
        await makeJpegFile(page, largePicture, LARGE_WIDTH, LARGE_HEIGHT);
        expect(
            fs.statSync(largePicture).size,
            "test setup: the large picture is over a megabyte",
        ).toBeGreaterThan(1000000);

        await startMeddling(page, bloomApp.collectionDir);
        try {
            const untitledFolder = await makeBookFromTemplate(
                page,
                "Basic Book",
            );
            const title = "Meddled Photo Book";
            if (titled) await typeInGroup(page, ".bookTitle", "en", title);

            await copyToClipboard(page, { picture: largePicture });
            const pastedAt = Date.now();
            const pictureFile = await pasteOnCover(page);

            await switchTab(page, "collection");
            await expectCoverInPreview(page, pictureFile);

            const bookFolder = titled
                ? await findBookFolder(page, title)
                : untitledFolder;
            if (titled)
                expect(
                    Path.basename(bookFolder),
                    "Bloom did not name the book's folder for its title",
                ).toBe(title);
            await expectBookSaved(page, bookFolder, pictureFile, pastedAt);

            // The meddler is still at work after all that, so the test ran with it throughout.
            await expectMeddlerLocksNewFiles(bloomApp.collectionDir);
        } finally {
            await stopMeddling(page);
        }
    });
}
