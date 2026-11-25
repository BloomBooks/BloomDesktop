// The tail of a speech bubble on a canvas, and the handles ComicalJS draws to shape it.
//
// ComicalJS keeps each bubble's shape in its element's data-bubble attribute, a JSON spec whose
// `tails` give each tail's tip and midpoint, and `autoCurve`: true while ComicalJS places the
// midpoint itself, false once a person has dragged it somewhere. While a bubble is selected, it
// draws a small circle on each of those points, on a drawing surface (canvas.comical-editing) laid
// over the page's canvas. The midpoint's circle is filled with the handle color when autoCurve is
// false and left open when it is true (Handle.setAutoMode in comicaljs). The circles are paper.js
// shapes, not elements, so this finds them from the spec and reads the drawing to see whether one
// is filled.

import { expect, type Locator, type Page } from "@playwright/test";
import { canvas } from "./canvasElements";

/** One tail of a bubble, as ComicalJS saved it, in the drawing surface's own coordinates. */
export interface ITailSpec {
    tipX: number;
    tipY: number;
    midpointX: number;
    midpointY: number;
    autoCurve: boolean;
}

/** A point in the page's own coordinates, where page.mouse goes. */
export interface IScreenPoint {
    x: number;
    y: number;
}

/**
 * The color Bloom gives tail handles (Comical.setUserInterfaceProperties in
 * CanvasElementManager.ts, kBloomBlue), which fills a midpoint handle that is not in auto mode.
 */
const kTailHandleColor = { r: 0x1d, g: 0x94, b: 0xa4 };
/** How far each channel of a drawn pixel may be from kTailHandleColor and still be that color. */
const kColorTolerance = 12;

/** The drawing surface ComicalJS lays over the page's canvas while the page is edited. */
function comicalDrawing(page: Page): Locator {
    return canvas(page).locator("canvas.comical-editing").first();
}

/**
 * The first tail of the bubble `element` locates, as its data-bubble says. Throws when the bubble
 * has no tail.
 */
export async function readTail(element: Locator): Promise<ITailSpec> {
    const json = await element.getAttribute("data-bubble");
    if (!json) throw new Error("The canvas element has no data-bubble spec.");
    // Bloom stores the spec with backquotes in place of double quotes, to keep it out of the way
    // of the attribute's own quotes.
    const spec = JSON.parse(json.replace(/`/g, '"')) as {
        tails?: ITailSpec[];
    };
    const tail = spec.tails?.[0];
    if (!tail) throw new Error(`The bubble's data-bubble has no tail: ${json}`);
    return {
        tipX: tail.tipX,
        tipY: tail.tipY,
        midpointX: tail.midpointX,
        midpointY: tail.midpointY,
        autoCurve: !!tail.autoCurve,
    };
}

/**
 * Where on the page a point of the drawing surface is shown. The surface's coordinates are its
 * layout pixels, which the page's zoom scales.
 */
export async function drawingPointOnScreen(
    page: Page,
    x: number,
    y: number,
): Promise<IScreenPoint> {
    const drawing = comicalDrawing(page);
    const box = await drawing.boundingBox({ timeout: 15000 });
    if (!box)
        throw new Error(
            "The bubble drawing surface (canvas.comical-editing) has no on-screen box.",
        );
    const layoutWidth = await drawing.evaluate(
        (c) => (c as HTMLCanvasElement).offsetWidth,
    );
    const scale = box.width / layoutWidth;
    return { x: box.x + x * scale, y: box.y + y * scale };
}

/**
 * Whether the drawing shows the handle color at a point of the drawing surface: true at the middle
 * of a filled handle, false at the middle of an open one, where the tail it sits on shows through.
 * Reads the pixel ComicalJS last painted there.
 */
async function isHandleColorDrawnAt(
    page: Page,
    x: number,
    y: number,
): Promise<boolean> {
    const pixel = await comicalDrawing(page).evaluate(
        (c, at) => {
            const drawing = c as HTMLCanvasElement;
            // paper.js may give the surface more backing pixels than layout pixels (on a high-DPI
            // display), so map the point onto the backing store.
            const ratio = drawing.width / drawing.offsetWidth;
            const data = drawing
                .getContext("2d")!
                .getImageData(
                    Math.round(at.x * ratio),
                    Math.round(at.y * ratio),
                    1,
                    1,
                ).data;
            return { r: data[0], g: data[1], b: data[2], a: data[3] };
        },
        { x, y },
    );
    return (
        pixel.a === 255 &&
        Math.abs(pixel.r - kTailHandleColor.r) <= kColorTolerance &&
        Math.abs(pixel.g - kTailHandleColor.g) <= kColorTolerance &&
        Math.abs(pixel.b - kTailHandleColor.b) <= kColorTolerance
    );
}

/**
 * Wait until the selected bubble's tail midpoint handle, where its data-bubble says the midpoint
 * is, is drawn filled or open.
 */
export async function expectMidpointHandleFilled(
    page: Page,
    element: Locator,
    filled: boolean,
    why: string,
): Promise<void> {
    await expect
        .poll(
            async () => {
                const tail = await readTail(element);
                return isHandleColorDrawnAt(
                    page,
                    tail.midpointX,
                    tail.midpointY,
                );
            },
            { timeout: 15000, message: why },
        )
        .toBe(filled);
}

/**
 * Drag the selected bubble's tail midpoint handle by (dx, dy) in the drawing surface's
 * coordinates, pressing on the handle and moving in small steps, as a person does. Returns the
 * tail once ComicalJS has saved the dragged midpoint.
 */
export async function dragTailMidpoint(
    page: Page,
    element: Locator,
    dx: number,
    dy: number,
): Promise<ITailSpec> {
    const before = await readTail(element);
    const start = await drawingPointOnScreen(
        page,
        before.midpointX,
        before.midpointY,
    );
    const end = await drawingPointOnScreen(
        page,
        before.midpointX + dx,
        before.midpointY + dy,
    );
    const steps = 10;
    await page.mouse.move(start.x, start.y);
    try {
        await page.mouse.down();
        for (let i = 1; i <= steps; i++) {
            await page.mouse.move(
                start.x + ((end.x - start.x) * i) / steps,
                start.y + ((end.y - start.y) * i) / steps,
            );
        }
    } finally {
        await page.mouse.up();
    }
    await expect
        .poll(async () => (await readTail(element)).autoCurve, {
            timeout: 15000,
            message:
                "Dragging the tail's midpoint handle did not take the tail out of auto mode " +
                `(the tail was ${JSON.stringify(before)}).`,
        })
        .toBe(false);
    return readTail(element);
}

/**
 * Double-click the selected bubble's tail midpoint handle where its data-bubble says it is.
 */
export async function doubleClickTailMidpoint(
    page: Page,
    element: Locator,
): Promise<void> {
    const tail = await readTail(element);
    const at = await drawingPointOnScreen(page, tail.midpointX, tail.midpointY);
    await page.mouse.move(at.x, at.y);
    await page.mouse.dblclick(at.x, at.y);
}
