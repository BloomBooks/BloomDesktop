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

import { expect, type Page } from "@playwright/test";
import * as Path from "node:path";
import { apiPost } from "./api";
import { editablePageFrame } from "./bookMaking";
import { realClick } from "./realClick";

/** The first image on the page: the background picture of the page's first image slot. */
const FIRST_IMAGE =
    ".bloom-canvas .bloom-backgroundImage .bloom-imageContainer img";

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
}

/**
 * Put the image file at `filePath` into the first image slot of the page being edited, the way
 * choosing that file in the image chooser would, and wait until the page shows it.
 */
export async function chooseImageFile(
    page: Page,
    filePath: string,
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
    const frame = editablePageFrame(page);
    await frame.locator(FIRST_IMAGE).first().waitFor({ state: "attached" });
    await frame.evaluate(
        ({ selector, imageInfo }) => {
            const img = document.querySelector(selector) as HTMLElement;
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
            bundle.changeImageByElement(img, imageInfo);
        },
        {
            selector: FIRST_IMAGE,
            imageInfo: { ...info, undoable: "false" },
        },
    );
    const fileName = Path.basename(decodeURIComponent(info.src));
    await expect
        .poll(async () => (await getImagePlacement(page)).fileName, {
            timeout: 30000,
            message: `The page never showed ${fileName} in its first image slot.`,
        })
        .toBe(fileName);
    // Bloom sizes the slot to the picture once it has loaded; wait for that, or a crop that
    // follows would measure the slot mid-adjustment.
    await expect
        .poll(
            async () =>
                frame
                    .locator(FIRST_IMAGE)
                    .first()
                    .evaluate((img) => {
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

/** How the first image on the page being shown is placed in its slot. */
export async function getImagePlacement(page: Page): Promise<IImagePlacement> {
    const img = editablePageFrame(page).locator(FIRST_IMAGE).first();
    await img.waitFor({ state: "attached", timeout: 30000 });
    return img.evaluate((element) => {
        const image = element as HTMLImageElement;
        const slot = image.closest(".bloom-canvas-element") as HTMLElement;
        const src = image.getAttribute("src") ?? "";
        return {
            fileName: decodeURIComponent(src.split("/").pop() ?? src),
            imageWidth: image.clientWidth,
            slotWidth: slot.clientWidth,
            cropped:
                image.clientWidth > slot.clientWidth + 1 ||
                image.clientHeight > slot.clientHeight + 1,
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
): Promise<void> {
    const frame = editablePageFrame(page);
    const img = frame.locator(FIRST_IMAGE).first();
    await img.waitFor({ state: "visible", timeout: 30000 });
    // A real press at the picture's centre. The drawing canvas Bloom lays over the page takes the
    // click and selects the picture under it, so Playwright's own click, which refuses to press
    // on an element something else covers, would wait forever here.
    await realClick(img);
    const handle = frame.locator(SIDE_HANDLE[side]);
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
        .poll(async () => (await getImagePlacement(page)).cropped, {
            timeout: 30000,
            message: `Dragging the ${side} handle by ${pixels}px did not crop the image.`,
        })
        .toBe(true);
}
