// Read the colours of pixels: those of a picture file, and those the browser actually paints where
// an <img> is shown, which is the picture composited over whatever is behind it.
//
// A primitive: surface modules give the locator of the picture, and these read it. Node has no
// image decoder or encoder, so the pictures are decoded and drawn in the browser.

import * as fs from "node:fs";
import * as Path from "node:path";
import type { Locator, Page } from "@playwright/test";

/** A pixel's colour, each channel 0 to 255. */
export interface IRgba {
    r: number;
    g: number;
    b: number;
    a: number;
}

/** A point in a picture, as a fraction of its width and height, with a name for messages. */
export interface IPicturePoint {
    name: string;
    x: number;
    y: number;
}

export const kOpaqueWhite: IRgba = { r: 255, g: 255, b: 255, a: 255 };
export const kOpaqueBlack: IRgba = { r: 0, g: 0, b: 0, a: 255 };

/** A colour as "rgba(r, g, b, a)", for messages. */
export function describeRgba(color: IRgba): string {
    return `rgba(${color.r}, ${color.g}, ${color.b}, ${color.a})`;
}

/** True when no channel of `actual` is further than `tolerance` from that of `expected`. */
export function isRgbaClose(
    actual: IRgba,
    expected: IRgba,
    tolerance: number,
): boolean {
    return (
        Math.abs(actual.r - expected.r) <= tolerance &&
        Math.abs(actual.g - expected.g) <= tolerance &&
        Math.abs(actual.b - expected.b) <= tolerance &&
        Math.abs(actual.a - expected.a) <= tolerance
    );
}

/**
 * Write a square PNG of `size` pixels to `filePath`: opaque white, with an opaque black square
 * over its middle third. It has only those two colours and no anti-aliasing, so Bloom takes it for
 * line art (ImageUtils.ShouldMakeBackgroundTransparent) and makes its white transparent wherever
 * it shows line art on a coloured page.
 */
export async function makeBlackSquareOnWhitePng(
    page: Page,
    filePath: string,
    size: number,
): Promise<void> {
    const base64 = await page.evaluate(async (size) => {
        const canvas = new OffscreenCanvas(size, size);
        const context = canvas.getContext("2d")!;
        context.fillStyle = "#FFFFFF";
        context.fillRect(0, 0, size, size);
        context.fillStyle = "#000000";
        const from = Math.round(size / 3);
        const to = Math.round((2 * size) / 3);
        context.fillRect(from, from, to - from, to - from);
        const blob = await canvas.convertToBlob({ type: "image/png" });
        const bytes = new Uint8Array(await blob.arrayBuffer());
        let binary = "";
        for (let i = 0; i < bytes.length; i += 0x8000)
            binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
        return btoa(binary);
    }, size);
    fs.mkdirSync(Path.dirname(filePath), { recursive: true });
    fs.writeFileSync(filePath, Buffer.from(base64, "base64"));
}

/**
 * Write a PNG of `width` by `height` pixels to `filePath`, every pixel the opaque `color`. With no
 * white in it, Bloom shows it as it is (no part made transparent), so it is a picture whose colour
 * is easy to find again in anything made from it, such as a thumbnail.
 */
export async function makeSolidColorPng(
    page: Page,
    filePath: string,
    width: number,
    height: number,
    color: IRgba,
): Promise<void> {
    const base64 = await page.evaluate(
        async ({ width, height, color }) => {
            const canvas = new OffscreenCanvas(width, height);
            const context = canvas.getContext("2d")!;
            context.fillStyle = `rgb(${color.r}, ${color.g}, ${color.b})`;
            context.fillRect(0, 0, width, height);
            const blob = await canvas.convertToBlob({ type: "image/png" });
            const bytes = new Uint8Array(await blob.arrayBuffer());
            let binary = "";
            for (let i = 0; i < bytes.length; i += 0x8000)
                binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
            return btoa(binary);
        },
        { width, height, color },
    );
    fs.mkdirSync(Path.dirname(filePath), { recursive: true });
    fs.writeFileSync(filePath, Buffer.from(base64, "base64"));
}

/** The colours of a PNG or JPEG file's pixels at each of `points`. */
export async function readImageFileColors(
    page: Page,
    filePath: string,
    points: IPicturePoint[],
): Promise<IRgba[]> {
    return readEncodedImageColors(page, fs.readFileSync(filePath), points);
}

/**
 * The colours the browser paints at each of `points` of the picture an <img> shows: the picture
 * over whatever is behind it, and under anything drawn over it. Waits for the picture to load.
 *
 * Each point is scrolled to the middle of the <img>'s frame and its pixel is taken from a
 * screenshot of the window, so a point below the window, or under the suite's step caption at the
 * bottom of a small window, is still read. The points are fractions of the picture as drawn: the
 * <img> must stretch its picture over its box (object-fit "fill") or fit it inside, centred and
 * keeping its shape (object-fit "contain"); this throws for any other fit.
 */
export async function readPaintedPictureColors(
    page: Page,
    picture: Locator,
    points: IPicturePoint[],
): Promise<IRgba[]> {
    await picture.waitFor({ state: "visible", timeout: 30000 });
    await picture.evaluate(
        (img) =>
            (img as HTMLImageElement).decode().catch(() => {
                throw new Error(
                    `The picture ${(img as HTMLImageElement).src} did not load.`,
                );
            }),
        undefined,
        { timeout: 30000 },
    );
    const content = await picture.evaluate((element) => {
        const img = element as HTMLImageElement;
        const style = getComputedStyle(img);
        if (style.objectFit === "fill")
            return { left: 0, top: 0, width: 1, height: 1 };
        // Only a centred "contain" is mapped below; any other fit or position would put the
        // points somewhere else in the picture.
        if (style.objectFit !== "contain" || style.objectPosition !== "50% 50%")
            throw new Error(
                `readPaintedPictureColors handles object-fit "fill" and a centred "contain", ` +
                    `not object-fit "${style.objectFit}" at "${style.objectPosition}".`,
            );
        const scale = Math.min(
            img.clientWidth / img.naturalWidth,
            img.clientHeight / img.naturalHeight,
        );
        const width = (img.naturalWidth * scale) / img.clientWidth;
        const height = (img.naturalHeight * scale) / img.clientHeight;
        return { left: (1 - width) / 2, top: (1 - height) / 2, width, height };
    });

    const colors: IRgba[] = [];
    for (const point of points) {
        const fraction = {
            x: content.left + point.x * content.width,
            y: content.top + point.y * content.height,
        };
        await picture.evaluate((img, fraction) => {
            img.scrollIntoView({ block: "center" });
            const box = img.getBoundingClientRect();
            window.scrollBy(
                0,
                box.top + fraction.y * box.height - window.innerHeight / 2,
            );
        }, fraction);
        const box = await picture.boundingBox();
        if (!box)
            throw new Error(
                `The picture has no box on screen when reading its ${point.name}.`,
            );
        const shot = await page.screenshot({
            clip: {
                x: Math.floor(box.x + fraction.x * box.width),
                y: Math.floor(box.y + fraction.y * box.height),
                width: 1,
                height: 1,
            },
        });
        const [color] = await readEncodedImageColors(page, shot, [
            { name: point.name, x: 0.5, y: 0.5 },
        ]);
        colors.push(color);
    }
    return colors;
}

/**
 * The colour the browser paints at `point` of the window, in the page's own coordinates (where
 * page.mouse goes): everything there composited, whatever lies in front. Taken from a screenshot
 * of that one pixel, so the point must be inside the window; this throws when it is not.
 */
export async function readPaintedColorAt(
    page: Page,
    point: { x: number; y: number },
): Promise<IRgba> {
    const window = await page.evaluate(() => ({
        width: innerWidth,
        height: innerHeight,
    }));
    const x = Math.floor(point.x);
    const y = Math.floor(point.y);
    if (x < 0 || y < 0 || x >= window.width || y >= window.height)
        throw new Error(
            `The point (${x}, ${y}) is outside the window (${window.width} x ${window.height}), ` +
                `so no screenshot shows it.`,
        );
    const shot = await page.screenshot({ clip: { x, y, width: 1, height: 1 } });
    const [color] = await readEncodedImageColors(page, shot, [
        { name: `(${x}, ${y})`, x: 0.5, y: 0.5 },
    ]);
    return color;
}

/** The background colour an element is drawn with, as its computed style gives it. */
export async function readBackgroundColor(element: Locator): Promise<IRgba> {
    return element.evaluate((e) => {
        const probe = document.createElement("canvas").getContext("2d")!;
        probe.fillStyle = getComputedStyle(e).backgroundColor;
        probe.fillRect(0, 0, 1, 1);
        const [r, g, b, a] = probe.getImageData(0, 0, 1, 1).data;
        return { r, g, b, a };
    });
}

/** The colours of a PNG or JPEG picture's pixels at each of `points`, from its encoded bytes. */
export async function readEncodedImageColors(
    page: Page,
    bytes: Buffer,
    points: IPicturePoint[],
): Promise<IRgba[]> {
    return page.evaluate(
        async ({ base64, points }) => {
            const binary = atob(base64);
            const data = new Uint8Array(binary.length);
            for (let i = 0; i < binary.length; i++)
                data[i] = binary.charCodeAt(i);
            const bitmap = await createImageBitmap(new Blob([data]));
            const canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
            const context = canvas.getContext("2d")!;
            context.drawImage(bitmap, 0, 0);
            return points.map((p) => {
                const x = Math.min(
                    bitmap.width - 1,
                    Math.floor(p.x * bitmap.width),
                );
                const y = Math.min(
                    bitmap.height - 1,
                    Math.floor(p.y * bitmap.height),
                );
                const [r, g, b, a] = context.getImageData(x, y, 1, 1).data;
                return { r, g, b, a };
            });
        },
        { base64: bytes.toString("base64"), points },
    );
}
