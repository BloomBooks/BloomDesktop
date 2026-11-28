// A photograph saved as a PNG is shown and published as a JPEG, without Bloom saying anything about
// it. A photo makes a PNG several times larger than a JPEG of the same picture, so wherever Bloom
// hands a picture on, to the Edit tab or into a publication, it tries a JPEG of a PNG that has no
// transparency, and uses the JPEG when it is under half the PNG's size
// (ImageUtils.AdjustImageForDisplay, TryChangeFormatToJpegIfHelpful). The book's folder keeps the
// file the person chose, as it is, so that a printed book gets the picture at full quality
// (ImageUtils.ProcessAndSaveImageIntoFolder copies a PNG or JPEG as it is, adding only the
// picture's licence and credits to its metadata).
//
// Automates the manual test "PNG Photo Converts To JPG" (Test Case ID 266).
//
// The manual case adds a photograph saved as a PNG; the test makes its own, a gradient with fine
// grain over it (makePhotoLikePng). It chooses that file in the Image Chooser on a content page,
// and checks that the book keeps the PNG, that the Edit tab is given a JPEG of the same picture,
// and that a BloomPUB carries a JPEG of the same picture and no PNG of it. That Bloom says nothing
// about any of this is checked by the suite itself, which fails a test when Bloom shows a problem.

import * as fs from "node:fs";
import * as Path from "node:path";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editablePageFrame,
    findBookFolder,
    getPages,
    goToPage,
    makeBookFromTemplate,
    reloadPageBeingEdited,
    typeInGroup,
} from "../helpers/bookMaking";
import { chooseImageFileInChooser, FIRST_IMAGE } from "../helpers/images";
import {
    compareEncodedPictures,
    makePhotoLikePng,
} from "../helpers/paintedPixels";
import { selectPublishDestination, stageBloomPub } from "../helpers/publish";
import { setUiStateViaApi } from "../helpers/uiLanguage";

test.use({
    collectionSpec: { name: "png-photo-converts-to-jpg", languages: ["en"] },
});

const BOOK_TITLE = "TC266 PNG Photo";

// Small enough that Bloom shrinks it nowhere, so each JPEG made of it has its size in pixels.
const PHOTO_SIZE = { width: 640, height: 480 };

// A JPEG of the photo at Bloom's quality (92) differs from it by about two levels a channel; a
// different picture, or the picture shifted or scaled, by tens.
const MAX_MEAN_CHANNEL_DIFFERENCE = 5;

const PNG_SIGNATURE = "89504e470d0a1a0a";
const JPEG_SIGNATURE = "ffd8ff";

function describeStart(bytes: Buffer): string {
    return bytes.subarray(0, 8).toString("hex");
}

/**
 * Check that `bytes` are a JPEG of the same picture as the PNG `photo`: same size in pixels, and
 * pixels within a JPEG's error of the PNG's. `what` names the bytes in messages.
 */
async function expectJpegOfPhoto(
    page: Page,
    bytes: Buffer,
    photo: Buffer,
    what: string,
): Promise<void> {
    expect(
        describeStart(bytes).startsWith(JPEG_SIGNATURE),
        `${what} should be a JPEG, but starts with ${describeStart(bytes)}`,
    ).toBe(true);
    const comparison = await compareEncodedPictures(page, bytes, photo);
    expect(
        comparison.firstSize,
        `${what} should have the photo's size in pixels`,
    ).toEqual(comparison.secondSize);
    expect(
        comparison.meanChannelDifference!,
        `${what} should show the photo: its pixels should differ from the PNG's by no more than a JPEG's error`,
    ).toBeLessThanOrEqual(MAX_MEAN_CHANNEL_DIFFERENCE);
}

test("a photograph saved as a PNG stays a PNG in the book, and is shown and published as a JPEG [Test Case ID 266]", async ({
    bloomApp,
    step,
}, testInfo) => {
    test.setTimeout(300000);
    // The Image Chooser's helpers find its buttons by their English labels, and a fresh Bloom
    // takes the operating system's UI language.
    await setUiStateViaApi(bloomApp, "en", false);
    const page = bloomApp.page;

    const photoPath = testInfo.outputPath("tc266 photo.png");
    await makePhotoLikePng(
        page,
        photoPath,
        PHOTO_SIZE.width,
        PHOTO_SIZE.height,
    );
    const photo = fs.readFileSync(photoPath);
    let pictureFileName = "";
    let contentPageId = "";

    await step("Make a book with a page that has a picture", async () => {
        await makeBookFromTemplate(page, "Basic Book");
        const cover = (await getPages(page))[0];
        await goToPage(page, cover.id);
        await typeInGroup(page, ".bookTitle", "en", BOOK_TITLE);
        await addPage(page, "Basic Text & Image");
        const contentPages = (await getPages(page)).filter(
            (p) => p.isContentPage,
        );
        expect(
            contentPages.length,
            "test setup: the book should have the one content page just added",
        ).toBe(1);
        contentPageId = contentPages[0].id;
        await goToPage(page, contentPageId);
    });

    await step(
        "Add the photograph saved as a PNG with the Image Chooser",
        async () => {
            pictureFileName = await chooseImageFileInChooser(page, photoPath);
        },
    );

    await step(
        "Check that the book keeps the photograph as the PNG chosen",
        async () => {
            // Leaving the page saves the book; Bloom renamed its folder to match its title then.
            await reloadPageBeingEdited(page);
            const bookFolder = await findBookFolder(page, BOOK_TITLE);
            const files = fs.readdirSync(bookFolder);
            const match = files.find(
                (f) => f.toLowerCase() === pictureFileName.toLowerCase(),
            );
            expect(
                match,
                `the book's folder has no ${pictureFileName}. Its files: ${files.join(", ")}`,
            ).toBeDefined();
            const saved = fs.readFileSync(Path.join(bookFolder, match!));
            expect(
                describeStart(saved),
                `${match} in the book's folder should be a PNG`,
            ).toBe(PNG_SIGNATURE);
            // Bloom writes the picture's licence and credits into the file, so its bytes differ
            // from the PNG chosen; its pixels must not.
            const comparison = await compareEncodedPictures(page, saved, photo);
            expect(
                comparison.firstSize,
                `${match} in the book's folder should have the photo's size in pixels`,
            ).toEqual(comparison.secondSize);
            expect(
                comparison.meanChannelDifference,
                `${match} in the book's folder should have exactly the pixels of the PNG chosen`,
            ).toBe(0);
        },
    );

    await step(
        "Check that the Edit tab is given the photograph as a JPEG",
        async () => {
            const picture = editablePageFrame(page)
                .locator(FIRST_IMAGE)
                .first();
            await picture.waitFor({ state: "visible", timeout: 30000 });
            // What Bloom answers when the page asks for the picture, as the browser drawing the
            // page gets it.
            const base64 = await picture.evaluate(async (element) => {
                const img = element as HTMLImageElement;
                await img.decode();
                const response = await fetch(img.currentSrc);
                if (!response.ok)
                    throw new Error(
                        `Bloom answered ${response.status} for ${img.currentSrc}`,
                    );
                const bytes = new Uint8Array(await response.arrayBuffer());
                let binary = "";
                for (let i = 0; i < bytes.length; i += 0x8000)
                    binary += String.fromCharCode(
                        ...bytes.subarray(i, i + 0x8000),
                    );
                return btoa(binary);
            });
            await expectJpegOfPhoto(
                page,
                Buffer.from(base64, "base64"),
                photo,
                "the picture Bloom gives the Edit tab",
            );
        },
    );

    const staged = await step("Publish the book as a BloomPUB", async () => {
        await selectPublishDestination(page, "BloomPUB");
        return stageBloomPub(page);
    });
    if (!staged) throw new Error("The BloomPUB was never staged.");

    await step(
        "Check that the BloomPUB carries the photograph as a JPEG, and no PNG of it",
        async () => {
            const htmFiles = fs
                .readdirSync(staged)
                .filter((f) => f.toLowerCase().endsWith(".htm"));
            expect(
                htmFiles.length,
                `the staged BloomPUB should have one .htm; it has ${htmFiles.join(", ")}`,
            ).toBe(1);
            const html = fs.readFileSync(
                Path.join(staged, htmFiles[0]),
                "utf8",
            );
            const sources = await page.evaluate(
                ({ html, pageId }) => {
                    const doc = new DOMParser().parseFromString(
                        html,
                        "text/html",
                    );
                    const contentPage = doc.getElementById(pageId);
                    if (!contentPage) return undefined;
                    // A BloomPUB gives a picture box that holds only its picture the picture as
                    // the box's background-image (bloom-background-image-in-style-attr), and any
                    // other picture as an <img>.
                    const sources: string[] = [];
                    for (const canvas of Array.from(
                        contentPage.querySelectorAll<HTMLElement>(
                            ".bloom-canvas",
                        ),
                    )) {
                        const background = canvas.style.backgroundImage.match(
                            /url\(["']?(.*?)["']?\)/,
                        );
                        if (background) sources.push(background[1]);
                        for (const img of Array.from(
                            canvas.querySelectorAll("img"),
                        ))
                            sources.push(img.getAttribute("src") ?? "");
                    }
                    return sources;
                },
                { html, pageId: contentPageId },
            );
            expect(
                sources,
                `the BloomPUB's book should have the content page ${contentPageId}`,
            ).toBeDefined();
            expect(
                sources!.length,
                `the BloomPUB's content page should show one picture; it shows ${sources!.join(", ")}`,
            ).toBe(1);
            const published = decodeURIComponent(sources![0].split("?")[0]);
            expect(
                /\.jpe?g$/i.test(published),
                `the BloomPUB's content page should show a .jpg, but shows ${published}`,
            ).toBe(true);
            const publishedPath = Path.join(staged, published);
            expect(
                fs.existsSync(publishedPath),
                `the BloomPUB should hold ${published}, which its content page shows`,
            ).toBe(true);
            await expectJpegOfPhoto(
                page,
                fs.readFileSync(publishedPath),
                photo,
                `${published} in the BloomPUB`,
            );
            const files = fs.readdirSync(staged);
            expect(
                files.filter(
                    (f) => f.toLowerCase() === pictureFileName.toLowerCase(),
                ),
                `the BloomPUB should not also carry the PNG ${pictureFileName}. Its files: ${files.join(", ")}`,
            ).toEqual([]);
        },
    );
});
