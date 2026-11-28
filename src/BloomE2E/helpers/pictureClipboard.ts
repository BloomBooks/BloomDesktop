// Copying and pasting the picture in a page's picture box with the commands in the picture's "..."
// menu (Copy Image, Paste Image), and reading what the box then holds: the picture's size and the
// credits (copyright, creator, license) Bloom keeps on its <img>.
//
// What is on the clipboard before a paste is set with copyToClipboard (helpers/clipboard.ts):
// `files` for a file copied in File Explorer, `picture` for a picture another program copied.

import * as fs from "node:fs";
import { expect, type Page } from "@playwright/test";
import { editablePageFrame } from "./bookMaking";
import {
    clickCanvasElementMenuItem,
    waitForCanvasElementToolbar,
} from "./canvasElements";
import { realClick } from "./realClick";

/** The picture box of a page with one picture, as Basic Text & Image has. */
const PICTURE_BOX = ".bloom-canvas";
/** The picture itself. Its src is "placeHolder.png" while the box is empty. */
const PICTURE = `${PICTURE_BOX} .bloom-imageContainer img`;

/** What a picture box holds. */
export interface IPictureInBox {
    /** The picture's file in the book folder, e.g. "photo.jpg". */
    fileName: string;
    /** The picture's own size, in pixels. */
    width: number;
    height: number;
    /** The credits Bloom keeps with the picture; "" when it has none. */
    copyright: string;
    creator: string;
    license: string;
}

/** Read what the first picture box of the page being edited holds. */
export async function getPictureInBox(page: Page): Promise<IPictureInBox> {
    return editablePageFrame(page)
        .locator(PICTURE)
        .first()
        .evaluate(async (img: HTMLImageElement) => {
            // naturalWidth is 0 until the picture has loaded.
            await img.decode().catch(() => undefined);
            return {
                fileName: decodeURIComponent(img.getAttribute("src") ?? ""),
                width: img.naturalWidth,
                height: img.naturalHeight,
                copyright: img.getAttribute("data-copyright") ?? "",
                creator: img.getAttribute("data-creator") ?? "",
                license: img.getAttribute("data-license") ?? "",
            };
        });
}

/** Click the first picture box of the page, as a person does, and wait for its toolbar. */
async function selectPictureBox(page: Page): Promise<void> {
    // A real press: the canvas Bloom lays over the page takes the click (see selectImage in
    // helpers/images.ts). The box, not the <img>, because an empty box hides its <img>.
    await realClick(editablePageFrame(page).locator(PICTURE_BOX).first());
    await waitForCanvasElementToolbar(page);
}

/**
 * Paste what Bloom's clipboard holds into the first picture box of the page with the picture's
 * Paste Image command, and wait until the box shows a new picture that has loaded.
 */
export async function pasteIntoPictureBox(page: Page): Promise<IPictureInBox> {
    const before = (await getPictureInBox(page)).fileName;
    await selectPictureBox(page);
    await clickCanvasElementMenuItem(page, "EditTab.Image.PasteImage");
    let pasted: IPictureInBox | undefined;
    await expect
        .poll(
            async () => {
                pasted = await getPictureInBox(page);
                return pasted.fileName !== before && pasted.width > 0;
            },
            {
                timeout: 30000,
                message: `The picture box still shows ${before} after Paste Image.`,
            },
        )
        .toBe(true);
    return pasted!;
}

/** Copy the first picture of the page with the picture's Copy Image command. */
export async function copyPictureInBox(page: Page): Promise<void> {
    await selectPictureBox(page);
    await clickCanvasElementMenuItem(page, "EditTab.Image.CopyImage");
}

/** The width and height of a PNG or JPEG file, read from its header. */
export function readPictureFileSize(path: string): {
    width: number;
    height: number;
} {
    const bytes = fs.readFileSync(path);
    if (bytes.readUInt32BE(0) === 0x89504e47)
        return {
            width: bytes.readUInt32BE(16),
            height: bytes.readUInt32BE(20),
        };
    if (bytes.readUInt16BE(0) === 0xffd8) {
        // Walk the JPEG's segments to the frame header (SOF0-SOF15, but not DHT, JPG or DAC).
        let offset = 2;
        while (offset < bytes.length) {
            const marker = bytes.readUInt16BE(offset);
            const length = bytes.readUInt16BE(offset + 2);
            if (
                marker >= 0xffc0 &&
                marker <= 0xffcf &&
                ![0xffc4, 0xffc8, 0xffcc].includes(marker)
            )
                return {
                    height: bytes.readUInt16BE(offset + 5),
                    width: bytes.readUInt16BE(offset + 7),
                };
            offset += 2 + length;
        }
    }
    throw new Error(
        `${path} is not a PNG or JPEG whose size this helper can read.`,
    );
}
