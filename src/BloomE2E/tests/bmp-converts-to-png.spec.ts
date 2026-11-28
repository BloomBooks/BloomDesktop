// A black-and-white line drawing saved as a BMP goes into a book as a PNG, and on a coloured page
// its white is transparent, so the page's colour shows through it. Browsers draw BMPs poorly and
// Bloom cannot make one transparent, so when it adds a picture in a format that is not for the web
// it saves a PNG in the book (ImageUtils.ProcessAndSaveImageIntoFolder). Like any line-art PNG,
// that PNG keeps its white in the file: the page asks for each picture on a coloured page with
// `?transparent=yes` (bloomImages.getImageTransparencyMode), and BloomServer answers with a copy
// whose white is transparent when the picture looks like line art
// (ImageUtils.ShouldMakeBackgroundTransparent).
//
// Automates the manual test "BMP Converts To PNG" (Test Case ID 262).
//
// The manual case adds a line drawing from the team's drive; the test makes its own: a BMP that is
// opaque white with a black square over its middle third. It chooses that file in the Image
// Chooser on a content page, checks that the book has a PNG of it and no BMP, gives the page a
// colour as the Book and Page Settings dialog does, and reads what the Edit tab paints: the page's
// colour where the picture is white, and black where it is black.

import * as fs from "node:fs";
import * as Path from "node:path";
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
import { setPageBackgroundColor } from "../helpers/bookSettings";
import { chooseImageFileInChooser, FIRST_IMAGE } from "../helpers/images";
import {
    describeRgba,
    isRgbaClose,
    kOpaqueBlack,
    kOpaqueWhite,
    readImageFileColors,
    readPaintedPictureColors,
    type IPicturePoint,
    type IRgba,
} from "../helpers/paintedPixels";
import { setUiStateViaApi } from "../helpers/uiLanguage";

test.use({
    collectionSpec: { name: "bmp-converts-to-png", languages: ["en"] },
});

const BOOK_TITLE = "TC262 BMP Picture";

const PICTURE_SIZE = 240;

// A colour far from white and black, so that neither the picture's white nor its black could pass
// for it.
const PAGE_COLOR_CSS = "#3a7bd5";
const PAGE_COLOR: IRgba = { r: 0x3a, g: 0x7b, b: 0xd5, a: 255 };

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

const PNG_SIGNATURE = Buffer.from([
    0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a,
]);

/**
 * Write a square 24-bit BMP of `size` pixels to `filePath`: opaque white, with an opaque black
 * square over its middle third, and nothing else, as a scanned line drawing saved as a BMP is.
 */
function writeBlackSquareOnWhiteBmp(filePath: string, size: number): void {
    const rowBytes = Math.ceil((size * 3) / 4) * 4;
    const pixelBytes = rowBytes * size;
    const headerBytes = 14 + 40;
    const bmp = Buffer.alloc(headerBytes + pixelBytes);
    bmp.write("BM", 0, "ascii");
    bmp.writeUInt32LE(bmp.length, 2);
    bmp.writeUInt32LE(headerBytes, 10);
    bmp.writeUInt32LE(40, 14); // BITMAPINFOHEADER
    bmp.writeInt32LE(size, 18);
    bmp.writeInt32LE(size, 22); // positive: rows run from the bottom up
    bmp.writeUInt16LE(1, 26); // planes
    bmp.writeUInt16LE(24, 28); // bits per pixel
    bmp.writeUInt32LE(0, 30); // no compression
    bmp.writeUInt32LE(pixelBytes, 34);
    bmp.writeInt32LE(2835, 38); // 72 dpi
    bmp.writeInt32LE(2835, 42);
    const from = Math.round(size / 3);
    const to = Math.round((2 * size) / 3);
    for (let row = 0; row < size; row++) {
        const y = size - 1 - row;
        for (let x = 0; x < size; x++) {
            const black = x >= from && x < to && y >= from && y < to;
            bmp.fill(
                black ? 0 : 255,
                headerBytes + row * rowBytes + x * 3,
                headerBytes + row * rowBytes + x * 3 + 3,
            );
        }
    }
    fs.mkdirSync(Path.dirname(filePath), { recursive: true });
    fs.writeFileSync(filePath, bmp);
}

test("a black-and-white line drawing saved as a BMP goes into the book as a PNG, and shows the page's colour through its white [Test Case ID 262]", async ({
    bloomApp,
    step,
}, testInfo) => {
    test.setTimeout(300000);
    // The Image Chooser's helpers find its buttons by their English labels, and a fresh Bloom
    // takes the operating system's UI language.
    await setUiStateViaApi(bloomApp, "en", false);
    const page = bloomApp.page;

    const bmpPath = testInfo.outputPath("tc262 line drawing.bmp");
    writeBlackSquareOnWhiteBmp(bmpPath, PICTURE_SIZE);
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
        "Add the line drawing saved as a BMP with the Image Chooser",
        async () => {
            pictureFileName = await chooseImageFileInChooser(page, bmpPath);
        },
    );

    await step("Check that the book has the picture as a PNG", async () => {
        expect(
            pictureFileName.toLowerCase().endsWith(".png"),
            `the page should show the picture as a .png file, but shows ${pictureFileName}`,
        ).toBe(true);
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
        const bytes = fs.readFileSync(Path.join(bookFolder, match!));
        expect(
            bytes.subarray(0, PNG_SIGNATURE.length).equals(PNG_SIGNATURE),
            `${match} in the book's folder should be a PNG file, but starts with ` +
                bytes.subarray(0, 8).toString("hex"),
        ).toBe(true);
        expect(
            files.filter((f) => f.toLowerCase().endsWith(".bmp")),
            "the book's folder should hold no BMP",
        ).toEqual([]);
        // It is the same picture: white with the black square, as in the BMP.
        const colors = await readImageFileColors(
            page,
            Path.join(bookFolder, match!),
            [...WHITE_POINTS, BLACK_POINT],
        );
        for (const [index, point] of WHITE_POINTS.entries()) {
            expect(
                isRgbaClose(colors[index], kOpaqueWhite, CHANNEL_TOLERANCE),
                `the PNG should be white at its ${point.name}, as the BMP is, but is ${describeRgba(colors[index])}`,
            ).toBe(true);
        }
        expect(
            isRgbaClose(
                colors[WHITE_POINTS.length],
                kOpaqueBlack,
                CHANNEL_TOLERANCE,
            ),
            `the PNG should be black at its ${BLACK_POINT.name}, as the BMP is, but is ${describeRgba(colors[WHITE_POINTS.length])}`,
        ).toBe(true);
    });

    await step(
        "Give the page a colour, and check that it shows through the picture's white",
        async () => {
            await setPageBackgroundColor(page, PAGE_COLOR_CSS);
            // Leaving the page saves its colour; coming back shows it as Bloom does a coloured page.
            await reloadPageBeingEdited(page);
            const picture = editablePageFrame(page)
                .locator(FIRST_IMAGE)
                .first();
            const src = (await picture.getAttribute("src")) ?? "";
            expect(
                src,
                "test setup: the coloured page should ask Bloom for its picture with transparent=yes",
            ).toContain("transparent=yes");

            const painted = await readPaintedPictureColors(page, picture, [
                ...WHITE_POINTS,
                BLACK_POINT,
            ]);
            for (const [index, point] of WHITE_POINTS.entries()) {
                expect(
                    isRgbaClose(painted[index], PAGE_COLOR, CHANNEL_TOLERANCE),
                    `at its ${point.name}, the picture should show the page's colour ` +
                        `${describeRgba(PAGE_COLOR)} through its white, but shows ${describeRgba(painted[index])}`,
                ).toBe(true);
            }
            const centre = painted[WHITE_POINTS.length];
            expect(
                isRgbaClose(centre, kOpaqueBlack, CHANNEL_TOLERANCE),
                `at its ${BLACK_POINT.name}, the picture should be black, but is ${describeRgba(centre)}`,
            ).toBe(true);
        },
    );
});
