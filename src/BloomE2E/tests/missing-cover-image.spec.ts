// A book whose cover picture's file has gone missing from its folder: Bloom shows the browser's
// broken-image icon where the picture was, in the Collections tab's preview and in the Edit tab,
// and the Edit tab adds a message naming the missing file. Over the missing picture the Edit tab
// offers the Change Image button, so the person can choose another picture, but not the image
// information (copyright) button: clicking it would only report an error, and it would cover part
// of the message. (With the file gone Bloom no longer has the picture's copyright, so it marks the
// button imgMetadataProblem, which would always show; editMode.less hides that button on a canvas
// whose picture is marked bloom-imageLoadError.)
//
// Automates the manual test "Handle Missing Image" (Test Case ID 754).
//
// The case chooses a car from the Art of Reading collection, which comes with its copyright. The
// test chooses a picture file of its own and gives it a copyright holder through the image
// information button, so that the picture, like the car, has its copyright and the button is the
// ordinary copyright button rather than the red "missing information" one. Before the file goes,
// the test checks that the copyright button does show over the picture, so that its absence later
// is down to the missing file.

import * as fs from "node:fs";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import {
    editablePageFrame,
    findBookFolder,
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    expectChangeImageButton,
    expectImageInfoButton,
    hoverOverCanvas,
    imageInfoButton,
    movePointerOffPage,
    pictureCanvasOf,
} from "../helpers/canvasElements";
import {
    clickBookTile,
    collectionPreviewCoverPicture,
    expectBookInPreview,
    readBookId,
} from "../helpers/collection";
import { setCopyrightHolder } from "../helpers/copyrightAndLicense";
import {
    chooseImageFile,
    FIRST_IMAGE,
    getImagePlacement,
    getPictureLoad,
    type IPictureLoad,
} from "../helpers/images";
import { realClick } from "../helpers/realClick";
import {
    getCurrentUiLanguageTag,
    setUiStateViaApi,
} from "../helpers/uiLanguage";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "missing-cover-image", languages: ["en"] },
});

const kTestBookTitle = "test book";
const kOtherBookTitle = "another book";

const kPicture = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
    "house-landscape.png",
);

// A copyright holder unusual enough that finding it on the picture cannot be a coincidence.
const kHolder = "Missing Picture Studios";

// The message Bloom puts on a picture it cannot show (EditTab.Image.AltMsg, in bloomImages.ts and
// Book.cs). The UI is pinned to English below.
const kMissingMessage =
    "This image, {0}, is missing or was loading too slowly.";

// Where the pointer rests to hover the cover's picture: away from the buttons in its top corners.
const kPictureMiddle = { xFraction: 0.5, yFraction: 0.6 };

// The size, in pixels each way, of the icon the browser draws for a picture it could not load.
const kBrokenImageIconSize = 16;

// How many pixels of the top and left edges of a broken picture's box may be hidden without hiding
// its icon.
const kHiddenEdgeAllowance = 2;

/** The Edit tab's cover picture, the first picture on the page being edited. */
function editCoverPicture(page: Parameters<typeof editablePageFrame>[0]) {
    return editablePageFrame(page).locator(FIRST_IMAGE).first();
}

/** The picture box that holds the Edit tab's cover picture. */
function coverCanvas(page: Parameters<typeof editablePageFrame>[0]) {
    return pictureCanvasOf(editCoverPicture(page));
}

/** The pane of the Edit tab's cover picture: the background canvas element that holds it. */
function coverPicturePane(page: Parameters<typeof editablePageFrame>[0]) {
    return editCoverPicture(page).locator(
        "xpath=ancestor::div[contains(concat(' ', @class, ' '), ' bloom-backgroundImage ')][1]",
    );
}

// An image element the browser cannot load is drawn as its broken-image icon (16 pixels square) at
// the top left of the image's box, with the alt text on its right. Both are cut off by the image's
// box and by any enclosing element that hides its overflow, so what a person sees is what fits in
// the part of the box that shows, counted from its top left corner.

/**
 * Check that `load` is a picture the browser failed to load, and that the part of its box that
 * shows has room for the broken-image icon, and also for the whole alt text beside it when
 * `withMessage` is true.
 */
function expectBrokenPicture(
    load: IPictureLoad,
    where: string,
    withMessage: boolean,
): void {
    expect(
        load.loaded,
        `${where}, the browser should have failed to load the deleted picture, but it has it.`,
    ).toBe(false);
    const shown = load.shown;
    const describe = `${load.width}x${load.height}, of which ${shown.width}x${shown.height} at (${shown.x}, ${shown.y}) shows`;
    // The icon has a little blank margin of its own, so a pixel or two of the box's edge may be
    // hidden (as the preview's picture box hides one on each side) without hiding the icon.
    expect(
        shown.x <= kHiddenEdgeAllowance && shown.y <= kHiddenEdgeAllowance,
        `${where}, the top left corner of the broken picture's box, where its icon goes, is hidden (box ${describe}).`,
    ).toBe(true);
    expect(
        shown.y + shown.height,
        `${where}, too little of the broken picture's box shows for its icon (box ${describe}).`,
    ).toBeGreaterThanOrEqual(kBrokenImageIconSize);
    const neededWidth = withMessage
        ? load.altTextWidth + kBrokenImageIconSize
        : kBrokenImageIconSize;
    expect(
        shown.x + shown.width,
        withMessage
            ? `${where}, too little of the broken picture's box shows for its icon and the ${load.altTextWidth}-pixel message "${load.alt}" beside it (box ${describe}).`
            : `${where}, too little of the broken picture's box shows for its icon (box ${describe}).`,
    ).toBeGreaterThanOrEqual(neededWidth);
}

/** The size of the box `box` locates, which must be drawn. */
async function getBoxSize(
    box: ReturnType<typeof coverCanvas>,
): Promise<{ width: number; height: number }> {
    await box.waitFor({ state: "visible", timeout: 30000 });
    const rect = await box.boundingBox();
    if (!rect) throw new Error("The box is visible but has no on-screen size.");
    return { width: rect.width, height: rect.height };
}

test("a cover picture whose file is missing shows as a broken picture, with Change Image but no copyright button [Test Case ID 754]", async ({
    bloomApp,
    step,
}) => {
    test.setTimeout(400000);

    // The Copyright and License dialog's fields and buttons are found by their English wording,
    // and the missing-picture message is compared in English; a freshly launched Bloom takes its
    // UI language from the operating system.
    if ((await getCurrentUiLanguageTag(bloomApp.page)) !== "en")
        await setUiStateViaApi(bloomApp, "en", false);
    const page = bloomApp.page;

    let pictureFileName = "";
    let canvasSizeWithPicture = { width: 0, height: 0 };

    await step("Make another book to click on later", async () => {
        await makeBookFromTemplate(page, "Basic Book");
        await goToPage(page, (await getPages(page))[0].id);
        await typeInGroup(page, ".bookTitle", "en", kOtherBookTitle);
    });

    await step(
        "Make a Basic book titled 'test book' and choose a picture for its cover",
        async () => {
            await makeBookFromTemplate(page, "Basic Book");
            await goToPage(page, (await getPages(page))[0].id);
            await typeInGroup(page, ".bookTitle", "en", kTestBookTitle);
            await chooseImageFile(page, kPicture);
            pictureFileName = (await getImagePlacement(page)).fileName;
        },
    );

    await step("Give the picture its copyright", async () => {
        // A picture with no copyright gets the red "missing information" button, which always
        // shows; that is the button to click.
        await expectImageInfoButton(
            page,
            { showing: true, missingInfo: true },
            "The cover picture, which has no copyright yet, does not show the red image information button.",
            coverCanvas(page),
        );
        await realClick(imageInfoButton(page, coverCanvas(page)));
        await setCopyrightHolder(page, kHolder);
        await expect
            .poll(
                async () =>
                    editCoverPicture(page).evaluate(
                        (img) => img.getAttribute("data-copyright") ?? "",
                    ),
                {
                    timeout: 30000,
                    message:
                        "OK in the Copyright and License dialog did not put the copyright on the cover picture.",
                },
            )
            .toContain(kHolder);
    });

    await step(
        "Verify that the chosen picture appears on the front cover",
        async () => {
            const load = await getPictureLoad(editCoverPicture(page));
            expect(load.fileName, "The cover shows some other picture.").toBe(
                pictureFileName,
            );
            expect(load.loaded, "The cover picture did not load.").toBe(true);
            await expect(
                editCoverPicture(page),
                "The cover picture is not visible.",
            ).toBeVisible();
            canvasSizeWithPicture = await getBoxSize(coverCanvas(page));
            // Sanity check for the end of the test: over a picture that is there and has its
            // copyright, the copyright button shows while the pointer is over the picture.
            await hoverOverCanvas(page, kPictureMiddle, coverCanvas(page));
            await expectImageInfoButton(
                page,
                { showing: true, missingInfo: false },
                "Sanity check: the copyright button does not show over the cover picture, which is there and has its copyright.",
                coverCanvas(page),
            );
            await expectChangeImageButton(
                page,
                true,
                "Sanity check: the Change Image button does not show over the cover picture.",
                coverCanvas(page),
            );
            await movePointerOffPage(page);
        },
    );

    let testBookId = "";
    let otherBookId = "";

    await step(
        "Go back to the Collection tab and delete the picture's file from the book's folder",
        async () => {
            await switchTab(page, "collection");
            await expectBookInPreview(page, kTestBookTitle);
            // Sanity check: before the file goes, the preview shows the picture.
            const before = await getPictureLoad(
                collectionPreviewCoverPicture(page),
            );
            expect(
                before.fileName,
                "Sanity check: the preview's cover shows some other picture.",
            ).toBe(pictureFileName);
            expect(
                before.loaded,
                "Sanity check: the preview's cover picture did not load before its file was deleted.",
            ).toBe(true);

            // Leaving the Edit tab saved the book, which renamed its folder for its title.
            const bookFolder = await findBookFolder(page, kTestBookTitle);
            testBookId = readBookId(bookFolder);
            otherBookId = readBookId(
                await findBookFolder(page, kOtherBookTitle),
            );
            const picturePath = Path.join(bookFolder, pictureFileName);
            expect(
                fs.existsSync(picturePath),
                `Sanity check: the book's folder has no ${pictureFileName}.`,
            ).toBe(true);
            fs.rmSync(picturePath);
        },
    );

    await step("Click on another book, then on 'test book' again", async () => {
        await clickBookTile(page, otherBookId);
        await expectBookInPreview(page, kOtherBookTitle);
        await clickBookTile(page, testBookId);
        await expectBookInPreview(page, kTestBookTitle);
    });

    await step(
        "Verify a broken picture shows on the front cover of the book preview",
        async () => {
            const picture = collectionPreviewCoverPicture(page);
            const load = await getPictureLoad(picture);
            expect(
                load.fileName,
                "The preview's cover no longer asks for the deleted picture.",
            ).toBe(pictureFileName);
            // The case asks only for the icon here; the preview's picture box is too short to
            // show the message as well.
            expectBrokenPicture(load, "In the preview", false);
            await expect(
                picture,
                "The preview's broken cover picture is not visible.",
            ).toBeVisible();
        },
    );

    await step("Click EDIT THIS BOOK", async () => {
        await switchTab(page, "edit");
        await goToPage(page, (await getPages(page))[0].id);
    });

    await step(
        "Verify a broken picture shows in the cover's picture box, with a message that the file is missing",
        async () => {
            const picture = editCoverPicture(page);
            await expect
                .poll(
                    async () => (await getPictureLoad(picture)).markedLoadError,
                    {
                        timeout: 30000,
                        message:
                            "Bloom never marked the cover picture as one that failed to load.",
                    },
                )
                .toBe(true);
            const load = await getPictureLoad(picture);
            expect(
                load.fileName,
                "The cover no longer asks for the deleted picture.",
            ).toBe(pictureFileName);
            expectBrokenPicture(load, "In the Edit tab", true);
            // In Bloom 6.2 a missing picture shrank its whole picture box (BL-14241); the box
            // should keep the size it had while it showed the picture.
            const canvasSize = await getBoxSize(coverCanvas(page));
            expect(
                Math.abs(canvasSize.width - canvasSizeWithPicture.width),
                `The cover's picture box was ${canvasSizeWithPicture.width} wide with its picture, and is ${canvasSize.width} wide without it.`,
            ).toBeLessThan(2);
            expect(
                Math.abs(canvasSize.height - canvasSizeWithPicture.height),
                `The cover's picture box was ${canvasSizeWithPicture.height} high with its picture, and is ${canvasSize.height} high without it.`,
            ).toBeLessThan(2);
            // Bloom fits the picture's own pane (the background canvas element) to the box; for a
            // picture that failed to load it should fill the box (its border may reach a pixel
            // past each side), leaving room for the message, rather than shrinking.
            const paneSize = await getBoxSize(coverPicturePane(page));
            expect(
                paneSize.width,
                `The missing picture's pane is ${paneSize.width} wide, in a picture box ${canvasSize.width} wide.`,
            ).toBeGreaterThan(canvasSize.width - 2);
            expect(
                paneSize.height,
                `The missing picture's pane is ${paneSize.height} high, in a picture box ${canvasSize.height} high.`,
            ).toBeGreaterThan(canvasSize.height - 2);
            await expect(
                picture,
                "The broken cover picture is not visible.",
            ).toBeVisible();
            await expect
                .poll(async () => (await getPictureLoad(picture)).alt, {
                    timeout: 30000,
                    message:
                        "The broken cover picture does not carry the message that its file is missing.",
                })
                .toBe(kMissingMessage.replace("{0}", pictureFileName));
        },
    );

    await step(
        "Verify the missing picture shows the Change Image button but no copyright button",
        async () => {
            await hoverOverCanvas(page, kPictureMiddle, coverCanvas(page));
            await expectChangeImageButton(
                page,
                true,
                "The Change Image button does not show over the missing cover picture.",
                coverCanvas(page),
            );
            // Bloom adds the button only once its tooltip's translation has come back, so wait for
            // it to be in the page (expectImageInfoButton checks that) before judging it hidden.
            await expectImageInfoButton(
                page,
                { showing: false },
                "The copyright button shows over the missing cover picture.",
                coverCanvas(page),
            );
            await movePointerOffPage(page);
            await expectImageInfoButton(
                page,
                { showing: false },
                "The copyright button shows over the missing cover picture once the pointer has left the page.",
                coverCanvas(page),
            );
        },
    );
});
