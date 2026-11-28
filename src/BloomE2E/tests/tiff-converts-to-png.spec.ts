// A TIFF picture becomes a PNG in the book (Test Case ID 263).
//
// Browsers cannot show TIFF, so when a person chooses a TIFF for a picture, Bloom saves it into
// the book's folder as a PNG (ImageUtils.ProcessAndSaveImageIntoFolder). The manual case adds a
// black-and-white line drawing saved as a TIFF ("Really PNG.tif" from the team's drive) and
// checks that it becomes a PNG. The test makes its pictures at run time: a 1-bit TIFF of a line
// drawing, and, since the drive's file is named as if it were a PNG inside, a PNG named .tif.
// It chooses each through the picture's menu and the Image Chooser, as a person does, and checks
// that the book's folder then holds a PNG file of the same drawing, and no TIFF.

import * as fs from "node:fs";
import * as Path from "node:path";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import { chooseImageFileInChooser, readImageFileSize } from "../helpers/images";
import { watchForScriptErrors } from "../helpers/scriptErrors";

test.use({
    collectionSpec: { name: "tiff-converts-to-png", languages: ["en"] },
});

// An odd size that no picture Bloom puts on a new book has.
const WIDTH = 241;
const HEIGHT = 163;

/**
 * The line drawing, as one boolean per pixel, row by row: true where it is black. A frame three
 * pixels wide around the edge and a diagonal line three pixels wide from corner to corner, on
 * white.
 */
function lineDrawing(): boolean[] {
    const black: boolean[] = [];
    for (let y = 0; y < HEIGHT; y++)
        for (let x = 0; x < WIDTH; x++) {
            const onFrame = x < 3 || y < 3 || x >= WIDTH - 3 || y >= HEIGHT - 3;
            const onDiagonal =
                Math.abs(y - Math.round((x * (HEIGHT - 1)) / (WIDTH - 1))) <= 1;
            black.push(onFrame || onDiagonal);
        }
    return black;
}

/**
 * Write `black` (from lineDrawing) to `filePath` as an uncompressed 1-bit TIFF, the way a
 * black-and-white line drawing is usually saved. Node has no TIFF encoder and the browser none
 * either, so the file is written here byte by byte: a little-endian header, one directory of
 * tags, and one strip of pixels in which a 0 bit is black.
 */
function writeLineDrawingTiff(filePath: string, black: boolean[]): void {
    const rowBytes = Math.ceil(WIDTH / 8);
    const pixels = Buffer.alloc(rowBytes * HEIGHT, 0);
    for (let y = 0; y < HEIGHT; y++)
        for (let x = 0; x < WIDTH; x++)
            if (!black[y * WIDTH + x])
                pixels[y * rowBytes + (x >> 3)] |= 0x80 >> (x & 7);

    const SHORT = 3;
    const LONG = 4;
    const RATIONAL = 5;
    const entryCount = 12;
    const directoryOffset = 8;
    const directorySize = 2 + entryCount * 12 + 4;
    const xResolutionOffset = directoryOffset + directorySize;
    const yResolutionOffset = xResolutionOffset + 8;
    const pixelsOffset = yResolutionOffset + 8;
    // Tags in ascending order, as TIFF requires: [tag, type, value or offset].
    const entries: [number, number, number][] = [
        [256, LONG, WIDTH], // ImageWidth
        [257, LONG, HEIGHT], // ImageLength
        [258, SHORT, 1], // BitsPerSample
        [259, SHORT, 1], // Compression: none
        [262, SHORT, 1], // PhotometricInterpretation: BlackIsZero
        [273, LONG, pixelsOffset], // StripOffsets
        [277, SHORT, 1], // SamplesPerPixel
        [278, LONG, HEIGHT], // RowsPerStrip
        [279, LONG, pixels.length], // StripByteCounts
        [282, RATIONAL, xResolutionOffset], // XResolution
        [283, RATIONAL, yResolutionOffset], // YResolution
        [296, SHORT, 2], // ResolutionUnit: inch
    ];
    expect(entries.length, "test setup: TIFF tag count").toBe(entryCount);

    const header = Buffer.alloc(pixelsOffset);
    header.write("II", 0, "latin1");
    header.writeUInt16LE(42, 2);
    header.writeUInt32LE(directoryOffset, 4);
    header.writeUInt16LE(entryCount, directoryOffset);
    entries.forEach(([tag, type, value], i) => {
        const at = directoryOffset + 2 + i * 12;
        header.writeUInt16LE(tag, at);
        header.writeUInt16LE(type, at + 2);
        header.writeUInt32LE(1, at + 4);
        if (type === SHORT) header.writeUInt16LE(value, at + 8);
        else header.writeUInt32LE(value, at + 8);
    });
    header.writeUInt32LE(0, directoryOffset + 2 + entryCount * 12);
    for (const offset of [xResolutionOffset, yResolutionOffset]) {
        header.writeUInt32LE(300, offset);
        header.writeUInt32LE(1, offset + 4);
    }
    fs.writeFileSync(filePath, Buffer.concat([header, pixels]));
}

/** Write `black` (from lineDrawing) as a PNG, drawn by the browser in `page`, to `filePath`. */
async function writeLineDrawingPng(
    page: Page,
    filePath: string,
    black: boolean[],
): Promise<void> {
    const base64 = await page.evaluate(
        async ({ black, width, height }) => {
            const canvas = new OffscreenCanvas(width, height);
            const context = canvas.getContext("2d")!;
            const image = context.createImageData(width, height);
            black.forEach((isBlack, i) => {
                const level = isBlack ? 0 : 255;
                image.data.set([level, level, level, 255], i * 4);
            });
            context.putImageData(image, 0, 0);
            const blob = await canvas.convertToBlob({ type: "image/png" });
            const bytes = new Uint8Array(await blob.arrayBuffer());
            let binary = "";
            for (let i = 0; i < bytes.length; i += 0x8000)
                binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
            return btoa(binary);
        },
        { black, width: WIDTH, height: HEIGHT },
    );
    fs.writeFileSync(filePath, Buffer.from(base64, "base64"));
}

/**
 * How many pixels of the picture in the PNG file at `filePath` differ from `black`: a black pixel
 * of the drawing must be dark and opaque in the file, and a white one light or transparent. The
 * file is decoded by the browser in `page`.
 */
async function pixelsDifferingFromDrawing(
    page: Page,
    filePath: string,
    black: boolean[],
): Promise<number> {
    return page.evaluate(
        async ({ base64, black }) => {
            const bytes = Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
            const bitmap = await createImageBitmap(new Blob([bytes]));
            const canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
            const context = canvas.getContext("2d")!;
            context.drawImage(bitmap, 0, 0);
            const data = context.getImageData(
                0,
                0,
                bitmap.width,
                bitmap.height,
            ).data;
            let differing = 0;
            black.forEach((isBlack, i) => {
                const [r, g, b, a] = data.subarray(i * 4, i * 4 + 4);
                const dark = a > 128 && r + g + b < 3 * 64;
                const light = a < 128 || r + g + b > 3 * 192;
                if (isBlack ? !dark : !light) differing++;
            });
            return differing;
        },
        { base64: fs.readFileSync(filePath).toString("base64"), black },
    );
}

/**
 * Make a Basic Book, choose `tiffPath` for its cover picture through the Image Chooser, and check
 * that the book's folder then holds the drawing `black` as a PNG, named .png, and no TIFF.
 */
async function chooseTiffAndExpectPng(
    page: Page,
    tiffPath: string,
    black: boolean[],
    what: string,
): Promise<void> {
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    const errors = watchForScriptErrors(page);

    const fileName = await chooseImageFileInChooser(page, tiffPath);

    expect(
        Path.extname(fileName).toLowerCase(),
        `The cover shows ${fileName} after choosing ${what}, not a .png file.`,
    ).toBe(".png");
    const saved = Path.join(bookFolder, fileName);
    expect(
        fs.readFileSync(saved).subarray(0, 8).toString("hex"),
        `${saved}, which Bloom made from ${what}, is not a PNG inside.`,
    ).toBe("89504e470d0a1a0a");
    expect(
        readImageFileSize(saved),
        `${saved} is not the size of ${what}.`,
    ).toEqual({ width: WIDTH, height: HEIGHT });
    expect(
        await pixelsDifferingFromDrawing(page, saved, black),
        `${saved} does not show the drawing in ${what}: this many pixels differ.`,
    ).toBe(0);
    const tiffsInBook = fs
        .readdirSync(bookFolder)
        .filter((name) => /\.tiff?$/i.test(name));
    expect(
        tiffsInBook,
        `Bloom put a TIFF in the book's folder after choosing ${what}.`,
    ).toEqual([]);

    errors.stop();
    expect(errors.errors(), "Bloom's UI threw script errors.").toEqual([]);
}

test("a black-and-white line drawing saved as a TIFF becomes a PNG in the book [Test Case ID 263]", async ({
    page,
}, testInfo) => {
    test.setTimeout(180000);
    const black = lineDrawing();
    const tiffPath = testInfo.outputPath("line drawing.tif");
    writeLineDrawingTiff(tiffPath, black);
    expect(
        fs.readFileSync(tiffPath).subarray(0, 4).toString("hex"),
        "test setup: the file made is not a TIFF",
    ).toBe("49492a00");

    await chooseTiffAndExpectPng(
        page,
        tiffPath,
        black,
        "a line drawing saved as a TIFF",
    );
});

test("a PNG named .tif is saved in the book as .png [Test Case ID 263]", async ({
    page,
}, testInfo) => {
    test.setTimeout(180000);
    const black = lineDrawing();
    const pngNamedTif = testInfo.outputPath("Really PNG.tif");
    await writeLineDrawingPng(page, pngNamedTif, black);
    expect(
        readImageFileSize(pngNamedTif),
        "test setup: the browser did not make a PNG of the drawing",
    ).toEqual({ width: WIDTH, height: HEIGHT });

    await chooseTiffAndExpectPng(page, pngNamedTif, black, "a PNG named .tif");
});
