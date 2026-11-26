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
// its cover colours, and reads what the preview paints: a screenshot of the preview's cover picture
// must show the cover's colour where the picture is white, and black where it is black. It also
// checks that the picture's file in the book's folder still has its white background, so that it
// is the preview that made the white transparent.

import * as fs from "node:fs";
import * as Path from "node:path";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    collectionBookPreview,
    expectBookInPreview,
} from "../helpers/collection";
import { chooseImageFile, getImagePlacement } from "../helpers/images";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "transparent-cover-image", languages: ["en"] },
});

const BOOK_TITLE = "TC259 Transparent Cover";

// The picture: a white square with a black square in its middle third. Only black and white, with
// no anti-aliasing, so that Bloom takes it for line art.
const PICTURE_SIZE = 240;
const BLACK_FROM = PICTURE_SIZE / 3;
const BLACK_TO = (2 * PICTURE_SIZE) / 3;

/** A point in the picture, as a fraction of its width and height. */
interface IPicturePoint {
    name: string;
    x: number;
    y: number;
}

// Points well inside the picture's white margin, away from its edges and from the black square,
// so that scaling the picture down cannot blend them with anything else.
const WHITE_POINTS: IPicturePoint[] = [
    { name: "top left", x: 0.12, y: 0.12 },
    { name: "top right", x: 0.88, y: 0.12 },
    { name: "bottom left", x: 0.12, y: 0.88 },
    { name: "bottom right", x: 0.88, y: 0.88 },
];
const BLACK_POINT: IPicturePoint = { name: "centre", x: 0.5, y: 0.5 };

/** How far a channel of a painted pixel may be from the colour expected there. */
const CHANNEL_TOLERANCE = 8;

interface IRgba {
    r: number;
    g: number;
    b: number;
    a: number;
}

function describe(color: IRgba): string {
    return `rgba(${color.r}, ${color.g}, ${color.b}, ${color.a})`;
}

function isClose(actual: IRgba, expected: IRgba): boolean {
    return (
        Math.abs(actual.r - expected.r) <= CHANNEL_TOLERANCE &&
        Math.abs(actual.g - expected.g) <= CHANNEL_TOLERANCE &&
        Math.abs(actual.b - expected.b) <= CHANNEL_TOLERANCE &&
        Math.abs(actual.a - expected.a) <= CHANNEL_TOLERANCE
    );
}

/** Draw the test's black-on-white picture in the browser, and write it as a PNG to `filePath`. */
async function makeLineArtPng(page: Page, filePath: string): Promise<void> {
    const base64 = await page.evaluate(
        async ({ size, from, to }) => {
            const canvas = new OffscreenCanvas(size, size);
            const context = canvas.getContext("2d")!;
            context.fillStyle = "#FFFFFF";
            context.fillRect(0, 0, size, size);
            context.fillStyle = "#000000";
            context.fillRect(from, from, to - from, to - from);
            const blob = await canvas.convertToBlob({ type: "image/png" });
            const bytes = new Uint8Array(await blob.arrayBuffer());
            let binary = "";
            for (let i = 0; i < bytes.length; i += 0x8000)
                binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
            return btoa(binary);
        },
        { size: PICTURE_SIZE, from: BLACK_FROM, to: BLACK_TO },
    );
    fs.mkdirSync(Path.dirname(filePath), { recursive: true });
    fs.writeFileSync(filePath, Buffer.from(base64, "base64"));
}

/**
 * Decode the PNG or JPEG `bytes` in the browser and return the colour of the pixel at each of
 * `points`, given as fractions of the rectangle `content` (also in fractions of the image) that
 * holds the picture.
 */
async function readPixels(
    page: Page,
    bytes: Buffer,
    points: IPicturePoint[],
    content = { left: 0, top: 0, width: 1, height: 1 },
): Promise<IRgba[]> {
    return page.evaluate(
        async ({ base64, points, content }) => {
            const binary = atob(base64);
            const data = new Uint8Array(binary.length);
            for (let i = 0; i < binary.length; i++)
                data[i] = binary.charCodeAt(i);
            const bitmap = await createImageBitmap(new Blob([data]));
            const canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
            const context = canvas.getContext("2d")!;
            context.drawImage(bitmap, 0, 0);
            return points.map((p) => {
                const x = Math.floor(
                    (content.left + p.x * content.width) * bitmap.width,
                );
                const y = Math.floor(
                    (content.top + p.y * content.height) * bitmap.height,
                );
                const [r, g, b, a] = context.getImageData(x, y, 1, 1).data;
                return { r, g, b, a };
            });
        },
        { base64: bytes.toString("base64"), points, content },
    );
}

test("a line-art picture on a coloured cover shows the cover's colour through its white in the Collections tab's preview [Test Case ID 259]", async ({
    page,
    step,
}, testInfo) => {
    test.setTimeout(300000);

    const picturePath = testInfo.outputPath("tc259-line-art.png");
    let bookFolder = "";

    await step(
        "Make a book and put a line-art picture on its cover",
        async () => {
            bookFolder = await makeBookFromTemplate(page, "Basic Book");
            const cover = (await getPages(page))[0];
            await goToPage(page, cover.id);
            await typeInGroup(page, ".bookTitle", "en", BOOK_TITLE);
            await makeLineArtPng(page, picturePath);
            await chooseImageFile(page, picturePath);
        },
    );

    const pictureFile = await step(
        "Check that the picture's file in the book still has a white background",
        async () => {
            const fileName = (await getImagePlacement(page)).fileName;
            const files = fs.readdirSync(bookFolder);
            const match = files.find(
                (f) => f.toLowerCase() === fileName.toLowerCase(),
            );
            expect(
                match,
                `the book's folder has no ${fileName}. Its files: ${files.join(", ")}`,
            ).toBeDefined();
            const filePath = Path.join(bookFolder, match!);
            const [white] = await readPixels(page, fs.readFileSync(filePath), [
                WHITE_POINTS[0],
            ]);
            expect(
                isClose(white, { r: 255, g: 255, b: 255, a: 255 }),
                `the picture in the book's folder should be opaque white at its ${WHITE_POINTS[0].name}, but is ${describe(white)}`,
            ).toBe(true);
            return filePath;
        },
    );
    if (!pictureFile) throw new Error("The picture's file was not found.");

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
            const preview = collectionBookPreview(page);
            const coverPage = preview.locator(".bloom-page.coverColor").first();
            const picture = coverPage
                .locator(
                    ".bloom-canvas .bloom-backgroundImage .bloom-imageContainer img",
                )
                .first();
            await expect(
                picture,
                "the preview's cover should show the picture",
            ).toBeVisible({ timeout: 30000 });
            await expect
                .poll(
                    () =>
                        picture.evaluate(
                            (img) => (img as HTMLImageElement).naturalWidth,
                        ),
                    {
                        timeout: 30000,
                        message:
                            "the preview's cover picture never finished loading",
                    },
                )
                .toBeGreaterThan(0);

            const coverColor = await coverPage.evaluate((pageDiv) => {
                const probe = document
                    .createElement("canvas")
                    .getContext("2d")!;
                probe.fillStyle = getComputedStyle(pageDiv).backgroundColor;
                probe.fillRect(0, 0, 1, 1);
                const [r, g, b, a] = probe.getImageData(0, 0, 1, 1).data;
                return { r, g, b, a };
            });
            // Sanity check: on a white cover the picture's white would look the same whether or
            // not Bloom made it transparent.
            expect(
                Math.min(coverColor.r, coverColor.g, coverColor.b),
                `the preview's cover should be coloured, but its background is ${describe(coverColor)}`,
            ).toBeLessThan(240);
            expect(
                coverColor.a,
                "the preview's cover colour should be opaque",
            ).toBe(255);

            // Where in its box the picture is drawn, as fractions of the box: an <img> scales its
            // picture to fit, keeping its shape, unless its object-fit says otherwise.
            const content = await picture.evaluate((element) => {
                const img = element as HTMLImageElement;
                const fit = getComputedStyle(img).objectFit;
                const boxWidth = img.clientWidth;
                const boxHeight = img.clientHeight;
                if (fit === "fill")
                    return { left: 0, top: 0, width: 1, height: 1 };
                const scale = Math.min(
                    boxWidth / img.naturalWidth,
                    boxHeight / img.naturalHeight,
                );
                const width = (img.naturalWidth * scale) / boxWidth;
                const height = (img.naturalHeight * scale) / boxHeight;
                return {
                    left: (1 - width) / 2,
                    top: (1 - height) / 2,
                    width,
                    height,
                };
            });

            // A screenshot shows what the preview actually paints at each point: the picture over
            // whatever is behind it. The picture can be taller than the preview, and the suite's
            // step caption covers the bottom of a small window, so each point is scrolled to the
            // middle of the preview's frame before its pixel is taken from the window.
            const painted: IRgba[] = [];
            for (const point of [...WHITE_POINTS, BLACK_POINT]) {
                const fraction = {
                    x: content.left + point.x * content.width,
                    y: content.top + point.y * content.height,
                };
                await picture.evaluate((img, fraction) => {
                    img.scrollIntoView({ block: "center" });
                    const box = img.getBoundingClientRect();
                    window.scrollBy(
                        0,
                        box.top +
                            fraction.y * box.height -
                            window.innerHeight / 2,
                    );
                }, fraction);
                const box = await picture.boundingBox();
                if (!box)
                    throw new Error(
                        "The preview's cover picture has no box on screen.",
                    );
                const shot = await page.screenshot({
                    clip: {
                        x: Math.floor(box.x + fraction.x * box.width),
                        y: Math.floor(box.y + fraction.y * box.height),
                        width: 1,
                        height: 1,
                    },
                });
                const [pixel] = await readPixels(page, shot, [
                    { name: point.name, x: 0.5, y: 0.5 },
                ]);
                painted.push(pixel);
            }
            for (const [index, point] of WHITE_POINTS.entries()) {
                expect(
                    isClose(painted[index], coverColor),
                    `at its ${point.name}, the preview's cover picture should show the cover's colour ` +
                        `${describe(coverColor)} through its white, but shows ${describe(painted[index])}`,
                ).toBe(true);
            }
            const centre = painted[WHITE_POINTS.length];
            expect(
                isClose(centre, { r: 0, g: 0, b: 0, a: 255 }),
                `at its ${BLACK_POINT.name}, the preview's cover picture should be black, but is ${describe(centre)}`,
            ).toBe(true);
        },
    );
});
