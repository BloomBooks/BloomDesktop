// Resize the selected canvas element by its handles one step at a time, and read what Bloom shows
// while a resize is under way.
//
// dragCanvasElementCorner and dragCanvasElementSide in canvasElements.ts make a whole drag at once.
// A test of the guides needs to stop with the button still down, because Bloom shows them only
// while a handle is held: CanvasGuideProvider.ts draws a red line where an edge or middle of the
// element being resized lines up with another element's, and green bars across every element that
// has the same width or height as it, and takes them all away when the button comes up.

import { type Locator, type Page } from "@playwright/test";
import { editablePageFrame } from "./bookMaking";
import { type Corner, type Side } from "./canvasElements";
import { type IRect } from "./geometry";

/** A handle of the selected canvas element: one of its four corners, or one of its four sides. */
export type ResizeHandle = { corner: Corner } | { side: Side };

/** What a handle is called in a failure message. */
export function describeHandle(handle: ResizeHandle): string {
    return "corner" in handle
        ? `${handle.corner} corner handle`
        : `${handle.side} side handle`;
}

/**
 * The selected canvas element's handle. Bloom draws the handles in a frame of their own over the
 * selected element (#canvas-element-control-frame), and editMode.less hides the ones that the
 * element does not have, so a test asks whether one is visible to ask whether the element has it.
 */
export function resizeHandle(page: Page, handle: ResizeHandle): Locator {
    const name =
        "corner" in handle
            ? `resize-handle-${handle.corner}`
            : `side-handle-${handle.side}`;
    return editablePageFrame(page).locator(
        `#canvas-element-control-frame .bloom-ui-canvas-element-${name}`,
    );
}

/**
 * The size and place of a canvas element in the page's own pixels, the unit Bloom resizes and snaps
 * in, whatever the zoom. `width` and `height` are what Bloom last wrote into the element's style;
 * `clientWidth` and `clientHeight` are what it measures when a resize starts, and the grid
 * snapping counts from them.
 */
export interface IElementSize {
    left: number;
    top: number;
    width: number;
    height: number;
    clientWidth: number;
    clientHeight: number;
}

/** Read `element`'s size and place; see IElementSize. Throws when Bloom has not styled its size. */
export async function getElementSize(element: Locator): Promise<IElementSize> {
    const size = await element.evaluate((e) => {
        const el = e as HTMLElement;
        return {
            left: parseFloat(el.style.left),
            top: parseFloat(el.style.top),
            width: parseFloat(el.style.width),
            height: parseFloat(el.style.height),
            clientWidth: el.clientWidth,
            clientHeight: el.clientHeight,
        };
    });
    if ([size.left, size.top, size.width, size.height].some(isNaN))
        throw new Error(
            `The canvas element has no size or place in its style: ${JSON.stringify(size)}.`,
        );
    return size;
}

/**
 * How many screen pixels one of the page's own pixels takes at the present zoom, measured from
 * `element` itself rather than taken from the zoom setting, so that it is what the page really is.
 */
export async function getPageScale(element: Locator): Promise<number> {
    const box = await element.boundingBox();
    if (!box) throw new Error("The canvas element has no on-screen box.");
    // offsetWidth, like the on-screen box, includes the element's border.
    const width = await element.evaluate((e) => (e as HTMLElement).offsetWidth);
    return box.width / width;
}

/** A handle that a test is holding down: where the pointer pressed it. */
export interface IHeldHandle {
    x: number;
    y: number;
}

/**
 * Press the mouse button on one of the selected element's handles and keep it down. Move it with
 * moveHeldHandle and let go with page.mouse.up(). Scrolls the handle into view first, as a person
 * would, because a press on a part of the page that is not showing lands on nothing.
 */
export async function pressHandle(
    page: Page,
    handle: ResizeHandle,
): Promise<IHeldHandle> {
    const target = resizeHandle(page, handle);
    await target.waitFor({ state: "visible", timeout: 30000 });
    await target.scrollIntoViewIfNeeded({ timeout: 30000 });
    const box = await target.boundingBox();
    if (!box)
        throw new Error(`The ${describeHandle(handle)} has no on-screen box.`);
    const x = box.x + box.width / 2;
    const y = box.y + box.height / 2;
    await page.mouse.move(x, y);
    await page.mouse.down();
    return { x, y };
}

/**
 * Move a held handle to `dx`, `dy` screen pixels from where it was pressed. The pointer moves in
 * steps, as a hand does, and Bloom resizes the element at each.
 */
export async function moveHeldHandle(
    page: Page,
    held: IHeldHandle,
    dx: number,
    dy: number,
): Promise<void> {
    await page.mouse.move(held.x + dx, held.y + dy, { steps: 8 });
}

/** One guide Bloom is showing over the page, and where, in the same coordinates as boundingBox. */
export interface IGuideShown {
    /** "alignment" is a red line; "same-size" is a green bar across an element. */
    kind: "alignment" | "same-size";
    /** "horizontal" runs left to right; "vertical" runs top to bottom. */
    direction: "horizontal" | "vertical";
    rect: IRect;
}

/**
 * Every guide Bloom is showing over the page being edited. CanvasGuideProvider.ts keeps a pool of
 * red lines, hidden until they are needed, and adds a green bar for each element of the same size
 * as the one being resized, so this reads only the ones whose display is not "none".
 */
export async function getGuidesShown(page: Page): Promise<IGuideShown[]> {
    const guides = editablePageFrame(page).locator(
        ".bloom-ui-canvas-guide-line, .bloom-ui-canvas-equal-dimension-indicator",
    );
    const states = await guides.evaluateAll((elements) =>
        elements.map((e) => ({
            shown: (e as HTMLElement).style.display !== "none",
            alignment: e.classList.contains("bloom-ui-canvas-guide-line"),
        })),
    );
    const shown: IGuideShown[] = [];
    for (let i = 0; i < states.length; i++) {
        if (!states[i].shown) continue;
        const box = await guides.nth(i).boundingBox();
        if (!box) continue;
        shown.push({
            kind: states[i].alignment ? "alignment" : "same-size",
            direction: box.width >= box.height ? "horizontal" : "vertical",
            rect: box,
        });
    }
    return shown;
}
