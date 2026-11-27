// A line-art picture (dark lines on a white background) on a coloured cover shows with its white
// made transparent in the Collections tab's preview of the book, so that the cover's colour shows
// through it, as it does in the Edit tab. Bloom keeps the picture's file as it is, and makes the
// white transparent as it serves the picture: the preview's page asks for each picture on a page
// with a coloured background with `?transparent=yes` (HtmlDom.AddTransparencyParamToImages), and
// BloomServer answers with a copy whose near-white background is transparent when the picture
// looks like line art (ImageUtils.ShouldMakeBackgroundTransparent). In Bloom 5.1 the preview
// showed such pictures on white (BL-10625).
//
// Automates the manual test "Transparent Front Cover Image" (Test Case ID 259).
//
// The test makes a black-on-white PNG, puts it on the cover of a new book, which Bloom gives one of
// its cover colours, and reads what the preview paints: the preview's cover picture must show the
// cover's colour where the picture is white, and black where it is black. Once the preview has
// been read, which is after leaving the Edit tab saved the book, it checks that the picture's file
// in the book's folder still has its white background, so that it is the preview that made the
// white transparent, not a change to the file.

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import {
    findBookFolder,
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    collectionPreviewCover,
    collectionPreviewCoverPicture,
    expectBookInPreview,
} from "../helpers/collection";
import { chooseImageFile, getImagePlacement } from "../helpers/images";
import {
    describeRgba,
    isRgbaClose,
    kOpaqueBlack,
    kOpaqueWhite,
    makeBlackSquareOnWhitePng,
    readBackgroundColor,
    readImageFileColors,
    readPaintedPictureColors,
    type IPicturePoint,
} from "../helpers/paintedPixels";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "transparent-cover-image", languages: ["en"] },
});

const BOOK_TITLE = "TC259 Transparent Cover";

const PICTURE_SIZE = 240;

// Points well inside the picture's white margin, away from its edges and from the black square in
// its middle third, so that scaling the picture down cannot blend them with anything else.
const WHITE_POINTS: IPicturePoint[] = [
    { name: "top left", x: 0.12, y: 0.12 },
    { name: "top right", x: 0.88, y: 0.12 },
    { name: "bottom left", x: 0.12, y: 0.88 },
    { name: "bottom right", x: 0.88, y: 0.88 },
];
const BLACK_POINT: IPicturePoint = { name: "centre", x: 0.5, y: 0.5 };

/** How far a channel of a pixel may be from the colour expected there. */
const CHANNEL_TOLERANCE = 8;

test("a line-art picture on a coloured cover shows the cover's colour through its white in the Collections tab's preview [Test Case ID 259]", async ({
    page,
    step,
}, testInfo) => {
    test.setTimeout(300000);

    const picturePath = testInfo.outputPath("tc259-line-art.png");
    let pictureFileName = "";

    await step(
        "Make a book and put a line-art picture on its cover",
        async () => {
            await makeBookFromTemplate(page, "Basic Book");
            const cover = (await getPages(page))[0];
            await goToPage(page, cover.id);
            await typeInGroup(page, ".bookTitle", "en", BOOK_TITLE);
            await makeBlackSquareOnWhitePng(page, picturePath, PICTURE_SIZE);
            await chooseImageFile(page, picturePath);
            pictureFileName = (await getImagePlacement(page)).fileName;
        },
    );

    await step(
        "Go to the Collections tab, which previews the book",
        async () => {
            await switchTab(page, "collection");
            await expectBookInPreview(page, BOOK_TITLE);
        },
    );

    await step(
        "Check that the preview shows the cover's colour through the picture's white",
        async () => {
            const coverColor = await readBackgroundColor(
                collectionPreviewCover(page),
            );
            // Sanity check: on a white cover the picture's white would look the same whether or
            // not Bloom made it transparent.
            expect(
                Math.min(coverColor.r, coverColor.g, coverColor.b),
                `the preview's cover should be coloured, but its background is ${describeRgba(coverColor)}`,
            ).toBeLessThan(240);
            expect(
                coverColor.a,
                "the preview's cover colour should be opaque",
            ).toBe(255);

            const painted = await readPaintedPictureColors(
                page,
                collectionPreviewCoverPicture(page),
                [...WHITE_POINTS, BLACK_POINT],
            );
            for (const [index, point] of WHITE_POINTS.entries()) {
                expect(
                    isRgbaClose(painted[index], coverColor, CHANNEL_TOLERANCE),
                    `at its ${point.name}, the preview's cover picture should show the cover's colour ` +
                        `${describeRgba(coverColor)} through its white, but shows ${describeRgba(painted[index])}`,
                ).toBe(true);
            }
            const centre = painted[WHITE_POINTS.length];
            expect(
                isRgbaClose(centre, kOpaqueBlack, CHANNEL_TOLERANCE),
                `at its ${BLACK_POINT.name}, the preview's cover picture should be black, but is ${describeRgba(centre)}`,
            ).toBe(true);
        },
    );

    await step(
        "Check that the picture's file in the book still has its white background",
        async () => {
            // Bloom renamed the book's folder to match its title when it saved the book.
            const bookFolder = await findBookFolder(page, BOOK_TITLE);
            const files = fs.readdirSync(bookFolder);
            const match = files.find(
                (f) => f.toLowerCase() === pictureFileName.toLowerCase(),
            );
            expect(
                match,
                `the book's folder has no ${pictureFileName}. Its files: ${files.join(", ")}`,
            ).toBeDefined();
            const colors = await readImageFileColors(
                page,
                Path.join(bookFolder, match!),
                [...WHITE_POINTS, BLACK_POINT],
            );
            for (const [index, point] of WHITE_POINTS.entries()) {
                expect(
                    isRgbaClose(colors[index], kOpaqueWhite, CHANNEL_TOLERANCE),
                    `the picture in the book's folder should be opaque white at its ${point.name}, but is ${describeRgba(colors[index])}`,
                ).toBe(true);
            }
            expect(
                isRgbaClose(
                    colors[WHITE_POINTS.length],
                    kOpaqueBlack,
                    CHANNEL_TOLERANCE,
                ),
                `the picture in the book's folder should be black at its ${BLACK_POINT.name}, but is ${describeRgba(colors[WHITE_POINTS.length])}`,
            ).toBe(true);
        },
    );
});
