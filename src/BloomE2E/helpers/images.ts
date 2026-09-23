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
    within?: Locator,
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
        { ...info, undoable: "false" },
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
    within?: Locator,
): Promise<void> {
    const frame = editablePageFrame(page);
    const img = imageIn(page, within);
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
        .poll(async () => (await getImagePlacement(page, within)).cropped, {
            timeout: 30000,
            message: `Dragging the ${side} handle by ${pixels}px did not crop the image.`,
        })
        .toBe(true);
}

/**
 * How a picture is turned and mirrored on screen: the 2x2 part of a CSS transform, with the part
 * that moves things left out. Upright and unmirrored is { a: 1, b: 0, c: 0, d: 1 }. A picture
 * turned a quarter turn clockwise is { a: 0, b: 1, c: -1, d: 0 }; one mirrored left to right is
 * { a: -1, b: 0, c: 0, d: 1 }. Each number is rounded to three places.
 */
export interface IPictureTurn {
    a: number;
    b: number;
    c: number;
    d: number;
}

/** The IPictureTurn of a picture that is neither turned nor mirrored. */
export const kUprightPicture: IPictureTurn = { a: 1, b: 0, c: 0, d: 1 };

/**
 * `turn` as it looks after the picture is mirrored about the screen's own axis: "horizontal" swaps
 * what is on the left and the right of the screen, "vertical" what is at the top and the bottom.
 * This is what the Flip commands promise, however the picture was turned before.
 */
export function mirroredOnScreen(
    turn: IPictureTurn,
    axis: "horizontal" | "vertical",
): IPictureTurn {
    // Mirroring after the turn is the mirror matrix times the turn, which negates one row.
    const clean = (x: number) => (x === 0 ? 0 : x);
    if (axis === "horizontal")
        return { a: clean(-turn.a), b: turn.b, c: clean(-turn.c), d: turn.d };
    return { a: turn.a, b: clean(-turn.b), c: turn.c, d: clean(-turn.d) };
}

/**
 * How a picture on the page being shown is turned and mirrored on screen, whatever did it: a turn
 * of its whole box (the rotation knob, or Rotate right on an overlay) and a turn or mirror of the
 * picture inside the box (Rotate right on the page's background picture, and Flip) combine here
 * into the one answer a reader's eye gives.
 */
export async function getPictureTurn(
    page: Page,
    within?: Locator,
): Promise<IPictureTurn> {
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
 * styles, exactly as they will be saved in the book. The crop, the turn and mirror of the picture,
 * and the size, place and turn of its box all live here, so two equal answers mean the picture is
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
 * Which transparency the picture's Transparency submenu has ticked: "Auto" unless the person chose
 * Transparent or Opaque, which Bloom records as a class on the picture.
 */
export async function getImageTransparencyChoice(
    page: Page,
    within?: Locator,
): Promise<"Auto" | "Transparent" | "Opaque"> {
    const img = imageIn(page, within);
    await img.waitFor({ state: "attached", timeout: 30000 });
    return img.evaluate((element) => {
        if (element.classList.contains("bloom-transparent"))
            return "Transparent";
        if (element.classList.contains("bloom-opaque")) return "Opaque";
        return "Auto";
    });
}

/**
 * The picture to act on: the one inside `within` when a scope is given, otherwise the page's first
 * image slot.
 */
function imageIn(page: Page, within?: Locator): Locator {
    if (within) return within.locator(".bloom-imageContainer img").first();
    return editablePageFrame(page).locator(FIRST_IMAGE).first();
}
