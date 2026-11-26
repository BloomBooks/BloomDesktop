// The BloomPUB image resolution in Book and Page Settings: a book starts at HD, and a BloomPUB
// carries each picture reduced to fit the resolution chosen, Small, HD, Full HD or 4K.
// Automates the manual test "4K Image BloomPUB Resolutions" (Test Case ID 403).
//
// The book has three pictures larger than 4K. Two have the sizes of the photos the manual test
// uses: a portrait one, 2560x3840, and a landscape one, 3992x2242. The test makes them itself, so
// their byte counts differ from those photos', and it checks sizes in pixels only.
//
// Bloom reduces a picture wider than 3840 when it is put in the book (ImageUtils.MaxLength), so
// the 3992x2242 picture already fits 4K by the time it is published, and would pass at 4K even if
// publishing left it alone. The third picture, a landscape one of 3840x2500, is there for that: it
// goes into the book unreduced, and only publishing can make it fit 4K.
//
// Bloom can come out a pixel or two short of the largest size that fits (BL-16829): its resize
// rounds the long side down before it fits the picture in the box. The card's own expected sizes
// already show that (Dylan 1278x718 at HD), so the expectation allows two pixels short and nothing
// over.
//
// What stays manual, and why, is in the pull request: the byte counts, uploading to Bloom Library
// and the BloomPUBs the harvester makes there, and saving a real .bloompub file, which goes through
// a native save dialog. The test reads the files Bloom stages for a BloomPUB instead, which are the
// files an unzipped .bloompub holds (see stageBloomPub).
//
// The tests are serial because they all publish one book. Whichever runs first makes it, so one
// of them can be run alone by its title.

import * as Path from "node:path";
import type { Page, TestInfo } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    waitForEditablePage,
} from "../helpers/bookMaking";
import {
    cancelBookSettings,
    getBloomPubImageSettings,
    getBloomPubResolutionInBookSettings,
    kBloomPubResolutions,
    okBookSettings,
    openBookSettings,
    setBloomPubResolutionInBookSettings,
    type BloomPubResolutionName,
} from "../helpers/bookSettings";
import {
    chooseImageFile,
    getImagePlacement,
    makeJpegFile,
    readImageFileSize,
    type IPixelSize,
} from "../helpers/images";
import {
    getStagedImageSize,
    selectPublishDestination,
    stageBloomPub,
} from "../helpers/publish";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "bloompub-image-resolution", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

// The pictures, one to a page. The first two have the sizes of the manual test's photos
// "1 Samuel.jpg" and "2 Dylan.jpg". `imported` is the largest size Bloom keeps the picture at in
// the book: its own size, unless that is over Bloom's limit for a picture in a book (3840 on the
// long side), as the second one's is.
const PICTURES: {
    which: string;
    fileName: string;
    source: IPixelSize;
    imported: IPixelSize;
}[] = [
    {
        which: "portrait",
        fileName: "Samuel.jpg",
        source: { width: 2560, height: 3840 },
        imported: { width: 2560, height: 3840 },
    },
    {
        which: "landscape",
        fileName: "Dylan.jpg",
        source: { width: 3992, height: 2242 },
        imported: { width: 3840, height: 2156 },
    },
    {
        which: "tall landscape",
        fileName: "Tall.jpg",
        source: { width: 3840, height: 2500 },
        imported: { width: 3840, height: 2500 },
    },
];

// How many pixels short of the largest size that fits a published picture may be (BL-16829).
const ROUNDING_SHORTFALL = 2;

// The name of each picture's file in the book, in the order of PICTURES, once the book is made.
let pictureFiles: string[] | undefined;

/**
 * Make the book that every test here publishes, unless an earlier test already has: a Basic Book
 * with one page for each of PICTURES, holding that picture. Checks that each picture is in the
 * book at its `imported` size, within BL-16829's rounding, since a picture Bloom reduced further
 * than that on the way in would prove nothing about publishing.
 */
const ensureBookWithPictures = async (page: Page, testInfo: TestInfo) => {
    if (pictureFiles) return;
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Basic Text & Image", PICTURES.length);
    const pages = await getContentPages(page);
    const files: string[] = [];
    for (const [index, picture] of PICTURES.entries()) {
        const path = testInfo.outputPath(picture.fileName);
        await makeJpegFile(
            page,
            path,
            picture.source.width,
            picture.source.height,
        );
        await goToPage(page, pages[index].id);
        await chooseImageFile(page, path);
        const fileName = (await getImagePlacement(page)).fileName;
        const inBook = readImageFileSize(Path.join(bookFolder, fileName));
        const described = `The ${picture.which} picture (${picture.source.width}x${picture.source.height}) is ${inBook.width}x${inBook.height} in the book, not ${picture.imported.width}x${picture.imported.height}.`;
        // A picture Bloom reduced on the way in can be short by the same rounding as a published
        // one (the card lists the second as 3839x2156); one it kept is exactly its own size.
        const shortfall =
            picture.imported.width < picture.source.width
                ? ROUNDING_SHORTFALL
                : 0;
        for (const side of ["width", "height"] as const) {
            expect(inBook[side], described).toBeLessThanOrEqual(
                picture.imported[side],
            );
            expect(inBook[side], described).toBeGreaterThanOrEqual(
                picture.imported[side] - shortfall,
            );
        }
        files.push(fileName);
    }
    // Leaving the page saves it.
    await goToPage(page, pages[0].id);
    pictureFiles = files;
};

/**
 * The size a picture of `source` size should have in a BloomPUB made at `resolution`: the largest
 * that fits its box, turned on its side for a portrait picture, and never larger than the picture.
 */
const sizeThatFits = (
    source: IPixelSize,
    resolution: BloomPubResolutionName,
): IPixelSize & { boxWidth: number; boxHeight: number } => {
    const box = kBloomPubResolutions.find((r) => r.name === resolution)!;
    const portrait = source.height > source.width;
    const boxWidth = portrait ? box.maxHeight : box.maxWidth;
    const boxHeight = portrait ? box.maxWidth : box.maxHeight;
    const scale = Math.min(
        1,
        boxWidth / source.width,
        boxHeight / source.height,
    );
    return {
        width: source.width * scale,
        height: source.height * scale,
        boxWidth,
        boxHeight,
    };
};

/** Check that a published picture has the size that fits `resolution`, within BL-16829's rounding. */
const expectPublishedSize = (
    published: IPixelSize,
    source: IPixelSize,
    resolution: BloomPubResolutionName,
    which: string,
) => {
    const fit = sizeThatFits(source, resolution);
    const described = `The ${which} picture (${source.width}x${source.height}) came out ${published.width}x${published.height} in a BloomPUB at ${resolution}; the largest that fits is ${fit.width.toFixed(1)}x${fit.height.toFixed(1)}`;
    expect(published.width, described).toBeLessThanOrEqual(fit.boxWidth);
    expect(published.height, described).toBeLessThanOrEqual(fit.boxHeight);
    expect(published.width, described).toBeLessThanOrEqual(
        Math.ceil(fit.width),
    );
    expect(published.height, described).toBeLessThanOrEqual(
        Math.ceil(fit.height),
    );
    expect(published.width, described).toBeGreaterThanOrEqual(
        Math.floor(fit.width) - ROUNDING_SHORTFALL,
    );
    expect(published.height, described).toBeGreaterThanOrEqual(
        Math.floor(fit.height) - ROUNDING_SHORTFALL,
    );
};

test.describe("BloomPUB image resolution", () => {
    test("a new book is set to publish BloomPUB pictures at HD (1280x720) [Test Case ID 403]", async ({
        page,
        step,
    }, testInfo) => {
        test.setTimeout(300000);
        await step("Make a book with a 4K picture on each page", async () =>
            ensureBookWithPictures(page, testInfo),
        );

        await step("Check the BloomPUB page of Book Settings", async () => {
            await openBookSettings(page, "BloomPUB");
            expect(
                await getBloomPubResolutionInBookSettings(page),
                "A new book's BloomPUB image resolution is not HD.",
            ).toBe("HD");
            expect(await getBloomPubImageSettings(page)).toEqual({
                maxWidth: 1280,
                maxHeight: 720,
            });
            await cancelBookSettings(page);
        });
    });

    // HD first, with the setting the book started with; then each other size, chosen on the slider.
    for (const resolution of ["HD", "Full HD", "Small", "4K"] as const) {
        test(`a BloomPUB at ${resolution} carries each picture reduced to fit ${resolution} [Test Case ID 403]`, async ({
            page,
            step,
        }, testInfo) => {
            test.setTimeout(300000);
            await step("Make the book, unless it is already made", async () =>
                ensureBookWithPictures(page, testInfo),
            );
            if (resolution !== "HD")
                await step(
                    `Set the BloomPUB resolution to ${resolution}`,
                    async () => {
                        await switchTab(page, "edit");
                        await waitForEditablePage(page);
                        await openBookSettings(page, "BloomPUB");
                        await setBloomPubResolutionInBookSettings(
                            page,
                            resolution,
                        );
                        await okBookSettings(page);
                        const box = kBloomPubResolutions.find(
                            (r) => r.name === resolution,
                        )!;
                        expect(
                            await getBloomPubImageSettings(page),
                            `Book Settings did not save the ${resolution} resolution.`,
                        ).toEqual({
                            maxWidth: box.maxWidth,
                            maxHeight: box.maxHeight,
                        });
                    },
                );

            const staged = await step(
                "Publish the book as a BloomPUB",
                async () => {
                    // A book with no text has no language lists to wait for, so this does not use
                    // openPublishDestination; staging waits for whatever it needs.
                    await selectPublishDestination(page, "BloomPUB");
                    return stageBloomPub(page);
                },
            );
            if (!staged) throw new Error("The BloomPUB was never staged.");

            await step(
                "Check the size of each picture in the BloomPUB",
                async () => {
                    for (const [index, picture] of PICTURES.entries())
                        expectPublishedSize(
                            getStagedImageSize(staged, pictureFiles![index]),
                            picture.source,
                            resolution,
                            picture.which,
                        );
                },
            );
        });
    }
});
