// Put an image on the page being edited, and crop it.
//
// Choosing an image is setup for the tests that use it, and its production route ends in a native
// file picker or a network image search, neither of which a test can drive (see AUTOMATION-DEBT.md,
// "Native OS dialogs hang automation"). So chooseImageFile takes the route the image chooser takes
// once a picture is chosen: it hands the file to Bloom's imageGallery/imageGalleryResult endpoint,
// which copies it into the book, and then applies the result to the page exactly as the chooser
// does, through the page bundle's changeImageByElement.
//
// Cropping IS driven through the real UI: a mouse drag on a side handle of the selected image.
//
// Every function here works on the page's first image slot by default. A page can hold more than
// one picture box, so each takes an optional `within` locator to say which one to act on; pass an
// element that holds a picture box and everything below applies to the picture in it instead.

import { expect, type Locator, type Page } from "@playwright/test";
import * as fs from "node:fs";
import * as Path from "node:path";
import { apiPost } from "./api";
import { editablePageFrame } from "./bookMaking";
import {
    clickCanvasElementMenuItem,
    selectCanvasElementAt,
} from "./canvasElements";
import { realClick } from "./realClick";

/** The first image on the page: the background picture of the page's first image slot. */
const FIRST_IMAGE =
    ".bloom-canvas .bloom-backgroundImage .bloom-imageContainer img";

/** What the first image slot shows while it holds no picture. */
const PLACEHOLDER = "placeHolder.png";
/** The Paste image command on a picture's "..." menu, by its localization id. */
const PASTE_IMAGE_COMMAND = "EditTab.Image.PasteImage";

/** The side handles of the selected image's control frame, by compass side. */
const SIDE_HANDLE: Record<"n" | "s" | "e" | "w", string> = {
    n: "#canvas-element-control-frame .bloom-ui-canvas-element-side-handle-n",
    s: "#canvas-element-control-frame .bloom-ui-canvas-element-side-handle-s",
    e: "#canvas-element-control-frame .bloom-ui-canvas-element-side-handle-e",
    w: "#canvas-element-control-frame .bloom-ui-canvas-element-side-handle-w",
};

/** How an image is placed in its slot, read from the page being shown. */
export interface IImagePlacement {
    /** The file name in the image's src, e.g. "bird.png"; "placeHolder.png" while the slot is empty. */
    fileName: string;
    /** The width of the image itself, in pixels as displayed. */
    imageWidth: number;
    /** The width of the visible slot, in pixels. Less than imageWidth when the image is cropped. */
    slotWidth: number;
    /** True when the image has been cropped: it is wider or taller than the slot showing it. */
    cropped: boolean;
    /** True when the browser has the image's picture: it loaded rather than failed. */
    loaded: boolean;
}

/**
 * Put the image file at `filePath` into the first image slot of the page being edited, the way
 * choosing that file in the image chooser would, and wait until the page shows it.
 *
 * Like the real chooser, this makes the change undoable. Pass `undoable` false for a test that
 * needs the Undo button to skip the picture.
 */
export async function chooseImageFile(
    page: Page,
    filePath: string,
    within?: Locator,
    undoable = true,
): Promise<void> {
    const result = await apiPost(
        page,
        "imageGallery/imageGalleryResult",
        JSON.stringify({ localPath: filePath, provider: "local-disk" }),
        "application/json",
    );
    const info = JSON.parse(result.body) as {
        src: string;
        copyright: string;
        creator: string;
        license: string;
    };
    const target = imageIn(page, within);
    await target.waitFor({ state: "attached", timeout: 30000 });
    // The image element itself is the handle: changeImageByElement takes the <img> the chooser was
    // opened on, so passing the one we found scopes the change to that slot.
    await target.evaluate(
        (img, imageInfo) => {
            const bundle = (
                window as unknown as {
                    editablePageBundle: {
                        changeImageByElement: (
                            img: HTMLElement,
                            info: typeof imageInfo,
                        ) => void;
                    };
                }
            ).editablePageBundle;
            bundle.changeImageByElement(img as HTMLElement, imageInfo);
        },
        { ...info, undoable: undoable ? "true" : "false" },
    );
    const fileName = Path.basename(decodeURIComponent(info.src));
    await expect
        .poll(async () => (await getImagePlacement(page, within)).fileName, {
            timeout: 30000,
            message: `The page never showed ${fileName} in the image slot.`,
        })
        .toBe(fileName);
    // Bloom sizes the slot to the picture once it has loaded; wait for that, or a crop that
    // follows would measure the slot mid-adjustment.
    await expect
        .poll(
            async () =>
                target.evaluate((img) => {
                    const image = img as HTMLImageElement;
                    return image.naturalWidth > 0 && image.clientWidth > 0;
                }),
            {
                timeout: 30000,
                message: `${fileName} never finished loading on the page.`,
            },
        )
        .toBe(true);
}

/** How an image on the page being shown is placed in its slot. */
export async function getImagePlacement(
    page: Page,
    within?: Locator,
): Promise<IImagePlacement> {
    const img = imageIn(page, within);
    await img.waitFor({ state: "attached", timeout: 30000 });
    return img.evaluate((element) => {
        const image = element as HTMLImageElement;
        const slot = image.closest(".bloom-canvas-element") as HTMLElement;
        // Without any query: Bloom adds one to some pictures, e.g. "?transparent=yes" to a PNG it
        // shows as line art, and that is not part of the file's name.
        const src = (image.getAttribute("src") ?? "").split("?")[0];
        return {
            fileName: decodeURIComponent(src.split("/").pop() ?? src),
            imageWidth: image.clientWidth,
            slotWidth: slot.clientWidth,
            cropped:
                image.clientWidth > slot.clientWidth + 1 ||
                image.clientHeight > slot.clientHeight + 1,
            loaded: image.complete && image.naturalWidth > 0,
        };
    });
}

/**
 * Crop the first image on the page being edited by `pixels` from one side, the way a person does:
 * click the image to select it, then drag that side's handle inward. Waits until the page shows
 * the image cropped.
 *
 * Ctrl is held during the drag. Without it, a drag that ends near the "fill the slot" position
 * snaps to it instead of cropping, so a small crop could silently become no crop.
 */
export async function cropImage(
    page: Page,
    side: "n" | "s" | "e" | "w",
    pixels: number,
    within?: Locator,
): Promise<void> {
    await selectImage(page, within);
    const handle = editablePageFrame(page).locator(SIDE_HANDLE[side]);
    await handle.waitFor({ state: "visible", timeout: 30000 });
    const box = (await handle.boundingBox())!;
    const x = box.x + box.width / 2;
    const y = box.y + box.height / 2;
    // Inward: an east or south handle moves toward the negative, a north or west one the other way.
    let dx = 0;
    let dy = 0;
    if (side === "e") dx = -pixels;
    else if (side === "w") dx = pixels;
    else if (side === "s") dy = -pixels;
    else dy = pixels;
    await page.mouse.move(x, y);
    await page.keyboard.down("Control");
    await page.mouse.down();
    await page.mouse.move(x + dx, y + dy, { steps: 10 });
    await page.mouse.up();
    await page.keyboard.up("Control");
    await expect
        .poll(async () => (await getImagePlacement(page, within)).cropped, {
            timeout: 30000,
            message: `Dragging the ${side} handle by ${pixels}px did not crop the image.`,
        })
        .toBe(true);
}

/**
 * Select the first image on the page being edited, the way a person does, by clicking it, and wait
 * until Bloom shows the frame with its handles around it. A selected picture is also what the Undo
 * button needs before it will undo a change to that picture.
 */
export async function selectImage(page: Page, within?: Locator): Promise<void> {
    const img = imageIn(page, within);
    await img.waitFor({ state: "visible", timeout: 30000 });
    // A real press at the picture's centre. The drawing canvas Bloom lays over the page takes the
    // click and selects the picture under it, so Playwright's own click, which refuses to press
    // on an element something else covers, would wait forever here.
    await realClick(img);
    await editablePageFrame(page)
        .locator(SIDE_HANDLE.e)
        .waitFor({ state: "visible", timeout: 30000 });
}

/**
 * How a picture is rotated and mirrored on screen: the 2x2 part of a CSS transform, with the part
 * that moves things left out. Upright and unmirrored is { a: 1, b: 0, c: 0, d: 1 }. A picture
 * rotated 90 degrees clockwise is { a: 0, b: 1, c: -1, d: 0 }; one mirrored left to right is
 * { a: -1, b: 0, c: 0, d: 1 }. Each number is rounded to three places.
 */
export interface IPictureRotation {
    a: number;
    b: number;
    c: number;
    d: number;
}

/** The IPictureRotation of a picture that is neither rotated nor mirrored. */
export const kUprightPicture: IPictureRotation = { a: 1, b: 0, c: 0, d: 1 };

/**
 * `rotation` as it looks after the picture is mirrored about one of its own axes: "horizontal" swaps
 * the picture's own left and right, "vertical" its own top and bottom. This is what the Flip commands
 * promise, however the picture was rotated before, so flipping and then rotating gives the same as
 * rotating and then flipping.
 */
export function mirroredAboutOwnAxis(
    rotation: IPictureRotation,
    axis: "horizontal" | "vertical",
): IPictureRotation {
    // Mirroring before the rotation is the rotation times the mirror matrix, which negates one
    // column: (a, b) is where the picture's own x axis goes, (c, d) its own y axis.
    const clean = (x: number) => (x === 0 ? 0 : x);
    if (axis === "horizontal")
        return {
            a: clean(-rotation.a),
            b: clean(-rotation.b),
            c: rotation.c,
            d: rotation.d,
        };
    return {
        a: rotation.a,
        b: rotation.b,
        c: clean(-rotation.c),
        d: clean(-rotation.d),
    };
}

/**
 * How a picture on the page being shown is rotated and mirrored on screen, whatever did it: a rotation
 * of its whole box (the rotation knob, or Rotate right on an overlay) and a rotation or mirror of the
 * picture inside the box (Rotate right on the page's background picture, and Flip) combine here
 * into the one answer a reader's eye gives.
 */
export async function getPictureRotation(
    page: Page,
    within?: Locator,
): Promise<IPictureRotation> {
    const img = imageIn(page, within);
    await img.waitFor({ state: "attached", timeout: 30000 });
    return img.evaluate((element) => {
        const view = element.ownerDocument.defaultView!;
        const matrixOf = (el: Element) => {
            const transform = view.getComputedStyle(el).transform;
            return new view.DOMMatrix(
                transform && transform !== "none" ? transform : undefined,
            );
        };
        const box = element.closest(".bloom-canvas-element")!;
        const m = matrixOf(box).multiply(matrixOf(element));
        const round = (x: number) => {
            const r = Math.round(x * 1000) / 1000;
            return r === 0 ? 0 : r;
        };
        return { a: round(m.a), b: round(m.b), c: round(m.c), d: round(m.d) };
    });
}

/** The inline style values that lay out one element, exactly as the book saves them. */
export interface IInlineLayout {
    left: string;
    top: string;
    width: string;
    height: string;
    transform: string;
}

/**
 * How a picture and the canvas element that holds it are laid out, as the values of their inline
 * styles, exactly as they will be saved in the book. The crop, the rotation and mirror of the picture,
 * and the size, place and rotation of its box all live here, so two equal answers mean the picture is
 * laid out exactly the same. Values, not the style attribute's text: the order of the declarations
 * in that text depends on the order code set them in, which says nothing about the layout.
 */
export async function getPictureInlineLayout(
    page: Page,
    within?: Locator,
): Promise<{ picture: IInlineLayout; box: IInlineLayout }> {
    const img = imageIn(page, within);
    await img.waitFor({ state: "attached", timeout: 30000 });
    return img.evaluate((element) => {
        const layoutOf = (el: HTMLElement) => ({
            left: el.style.left,
            top: el.style.top,
            width: el.style.width,
            height: el.style.height,
            transform: el.style.transform,
        });
        return {
            picture: layoutOf(element as HTMLElement),
            box: layoutOf(
                element.closest(".bloom-canvas-element") as HTMLElement,
            ),
        };
    });
}

/**
 * The picture to act on: the one inside `within` when a scope is given, otherwise the page's first
 * image slot.
 */
/**
 * Paste the picture on Bloom's clipboard into the empty first image slot of the page being
 * edited, such as a new book's cover picture, the way a person does: select the picture's box,
 * then choose Paste image from its menu. Returns the file name the slot shows once the picture
 * has loaded. Put the picture on the clipboard first, with copyToClipboard.
 */
export async function pasteImageIntoEmptySlot(page: Page): Promise<string> {
    const before = await getImagePlacement(page);
    expect(
        before.fileName,
        "test setup: the page's first image slot starts with no picture",
    ).toBe(PLACEHOLDER);
    // An empty picture shows no image to click, so select the picture's box itself.
    await selectCanvasElementAt(
        editablePageFrame(page)
            .locator(
                ".bloom-canvas .bloom-canvas-element.bloom-backgroundImage",
            )
            .first(),
        "the page's first picture",
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
                message: "The page never showed the pasted picture.",
            },
        )
        .toBe(true);
    return (await getImagePlacement(page)).fileName;
}

function imageIn(page: Page, within?: Locator): Locator {
    if (within) return within.locator(".bloom-imageContainer img").first();
    return editablePageFrame(page).locator(FIRST_IMAGE).first();
}

/** The size of a picture in pixels. */
export interface IPixelSize {
    width: number;
    height: number;
}

/**
 * Write a photo-like JPEG of exactly `width` by `height` pixels to `filePath`, for a test that
 * needs a picture of a given size, such as one larger than any size Bloom publishes. The picture
 * is a colour gradient with soft blobs of colour across it, so that it compresses, and shrinks
 * when scaled down, the way a photo does. It is drawn by the browser in `page`, because Node has no JPEG
 * encoder; nothing is added to Bloom or the book.
 */
export async function makeJpegFile(
    page: Page,
    filePath: string,
    width: number,
    height: number,
): Promise<void> {
    const base64 = await page.evaluate(
        async ({ width, height }) => {
            const canvas = new OffscreenCanvas(width, height);
            const context = canvas.getContext("2d")!;
            const gradient = context.createLinearGradient(0, 0, width, height);
            gradient.addColorStop(0, "#2a6f97");
            gradient.addColorStop(0.5, "#f4a259");
            gradient.addColorStop(1, "#5b8e7d");
            context.fillStyle = gradient;
            context.fillRect(0, 0, width, height);
            // Soft blobs, as in a photo, not hard edges. Bloom keeps a picture at its own size when
            // shrinking it would make the file larger (BL-11441, ImageUtils
            // .TryResizeImageWithGraphicsMagick), and a picture full of hard edges does grow when
            // GraphicsMagick scales it down and re-encodes it.
            const blobSize = Math.max(width, height) / 12;
            for (let i = 0; i < 160; i++) {
                const x = (i * 7919) % width;
                const y = (i * 104729) % height;
                const blob = context.createRadialGradient(
                    x,
                    y,
                    0,
                    x,
                    y,
                    blobSize,
                );
                blob.addColorStop(
                    0,
                    `rgba(${(i * 37) % 256},${(i * 91) % 256},${(i * 53) % 256},0.6)`,
                );
                blob.addColorStop(1, "rgba(0,0,0,0)");
                context.fillStyle = blob;
                context.fillRect(
                    x - blobSize,
                    y - blobSize,
                    2 * blobSize,
                    2 * blobSize,
                );
            }
            const blob = await canvas.convertToBlob({
                type: "image/jpeg",
                quality: 0.92,
            });
            const bytes = new Uint8Array(await blob.arrayBuffer());
            let binary = "";
            for (let i = 0; i < bytes.length; i += 0x8000)
                binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
            return btoa(binary);
        },
        { width, height },
    );
    fs.mkdirSync(Path.dirname(filePath), { recursive: true });
    fs.writeFileSync(filePath, Buffer.from(base64, "base64"));
    const made = readImageFileSize(filePath);
    if (made.width !== width || made.height !== height)
        throw new Error(
            `The JPEG made for the test is ${made.width}x${made.height}, not ${width}x${height}.`,
        );
}

/**
 * The size in pixels of a JPEG or PNG file on disk, read from the file's own header. Throws,
 * naming the file, when it is neither.
 */
export function readImageFileSize(filePath: string): IPixelSize {
    const bytes = fs.readFileSync(filePath);
    if (bytes.subarray(0, 8).toString("hex") === "89504e470d0a1a0a")
        return {
            width: bytes.readUInt32BE(16),
            height: bytes.readUInt32BE(20),
        };
    if (bytes[0] !== 0xff || bytes[1] !== 0xd8)
        throw new Error(`${filePath} is neither a JPEG nor a PNG.`);
    // Walk the JPEG's segments to its start-of-frame, which holds the height and then the width.
    // Every segment but the markers without a length (none occur before the frame) is
    // 0xFF, a marker byte, and a two-byte length that counts itself.
    let offset = 2;
    while (offset + 9 < bytes.length) {
        if (bytes[offset] !== 0xff)
            throw new Error(
                `${filePath} has a broken JPEG segment at byte ${offset}.`,
            );
        const marker = bytes[offset + 1];
        const isStartOfFrame =
            marker >= 0xc0 &&
            marker <= 0xcf &&
            marker !== 0xc4 &&
            marker !== 0xc8 &&
            marker !== 0xcc;
        if (isStartOfFrame)
            return {
                height: bytes.readUInt16BE(offset + 5),
                width: bytes.readUInt16BE(offset + 7),
            };
        offset += 2 + bytes.readUInt16BE(offset + 2);
    }
    throw new Error(`${filePath} is a JPEG with no start-of-frame segment.`);
}
