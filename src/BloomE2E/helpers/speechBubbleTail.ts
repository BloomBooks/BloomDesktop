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
    const tail = (await readBubbleFamilySpec(element)).tails[0];
    if (!tail)
        throw new Error(
            `The bubble's data-bubble has no tail: ${await element.getAttribute("data-bubble")}`,
        );
    return {
        tipX: tail.tipX,
        tipY: tail.tipY,
        midpointX: tail.midpointX,
        midpointY: tail.midpointY,
        autoCurve: tail.autoCurve,
    };
}

/**
 * One tail of a bubble, with what says which kind it is. A child bubble's one tail is a joiner:
 * ComicalJS draws it as the link to the bubble before it in the family, and ends it at that
 * bubble's middle. Any other tail points at whatever the person aimed it at.
 */
export interface IBubbleTail extends ITailSpec {
    joiner: boolean;
}

/** What a bubble's data-bubble says about its tails and its place in a family of bubbles. */
export interface IBubbleFamilySpec {
    /** Every tail, in the order ComicalJS keeps them. */
    tails: IBubbleTail[];
    /**
     * The bubble's place in its family: 1 for the parent, 2 for its first child, and so on.
     * Undefined for a bubble that has never had a child or been one.
     */
    order: number | undefined;
}

/**
 * Every tail of the bubble `element` locates, and the bubble's place in its family (a bubble and
 * the child bubbles joined to it), as its data-bubble says.
 */
export async function readBubbleFamilySpec(
    element: Locator,
): Promise<IBubbleFamilySpec> {
    const json = await element.getAttribute("data-bubble");
    if (!json) throw new Error("The canvas element has no data-bubble spec.");
    // Bloom stores the spec with backquotes in place of double quotes, to keep it out of the way
    // of the attribute's own quotes.
    const spec = JSON.parse(json.replace(/`/g, '"')) as {
        tails?: (Omit<ITailSpec, "autoCurve"> & {
            autoCurve?: boolean;
            joiner?: boolean;
        })[];
        order?: number;
    };
    return {
        tails: (spec.tails ?? []).map((tail) => ({
            tipX: tail.tipX,
            tipY: tail.tipY,
            midpointX: tail.midpointX,
            midpointY: tail.midpointY,
            autoCurve: !!tail.autoCurve,
            joiner: !!tail.joiner,
        })),
        order: spec.order,
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

/** A point of the screen where the bubble drawing is opaque, and the colour drawn there. */
export interface IOpaqueDrawingPoint extends IScreenPoint {
    color: { r: number; g: number; b: number; a: number };
}

/**
 * The nearest pixel of the screen to `near` that ComicalJS's drawing covers completely, in one
 * opaque colour: every pixel of the drawing that falls inside that screen pixel is that colour, so
 * none of it is a smoothed edge. Whatever lies under such a pixel cannot show through the drawing,
 * so the browser paints it in the drawing's own colour unless something is in front of the
 * drawing. Returns the middle of that screen pixel. Throws when there is none within `radius`
 * pixels.
 */
export async function findOpaqueDrawingPointNear(
    page: Page,
    near: IScreenPoint,
    radius = 8,
): Promise<IOpaqueDrawingPoint> {
    const drawing = comicalDrawing(page);
    const box = await drawing.boundingBox({ timeout: 15000 });
    if (!box)
        throw new Error(
            "The bubble drawing surface (canvas.comical-editing) has no on-screen box.",
        );
    const found = await drawing.evaluate(
        (c, args) => {
            const surface = c as HTMLCanvasElement;
            // Screen pixels per layout pixel (the page's zoom), and backing pixels per layout pixel.
            const scale = args.box.width / surface.offsetWidth;
            const ratio = surface.width / surface.offsetWidth;
            const context = surface.getContext("2d")!;
            // The colour of the drawing over the whole of screen pixel (sx, sy), or undefined when
            // the drawing's pixels there are not all the same opaque colour.
            const solidColorOf = (sx: number, sy: number) => {
                const toBacking = (screen: number, origin: number) =>
                    ((screen - origin) / scale) * ratio;
                const left = Math.floor(toBacking(sx, args.box.x));
                const right = Math.ceil(toBacking(sx + 1, args.box.x));
                const top = Math.floor(toBacking(sy, args.box.y));
                const bottom = Math.ceil(toBacking(sy + 1, args.box.y));
                const d = context.getImageData(
                    left,
                    top,
                    right - left,
                    bottom - top,
                ).data;
                for (let i = 0; i < d.length; i += 4)
                    if (
                        d[i + 3] !== 255 ||
                        d[i] !== d[0] ||
                        d[i + 1] !== d[1] ||
                        d[i + 2] !== d[2]
                    )
                        return undefined;
                return { r: d[0], g: d[1], b: d[2], a: 255 };
            };
            const cx = Math.floor(args.near.x);
            const cy = Math.floor(args.near.y);
            for (let r = 0; r <= args.radius; r++)
                for (let dx = -r; dx <= r; dx++)
                    for (let dy = -r; dy <= r; dy++) {
                        if (Math.max(Math.abs(dx), Math.abs(dy)) !== r)
                            continue;
                        const color = solidColorOf(cx + dx, cy + dy);
                        if (color)
                            return {
                                x: cx + dx + 0.5,
                                y: cy + dy + 0.5,
                                color,
                            };
                    }
            return undefined;
        },
        { near, radius, box },
    );
    if (!found)
        throw new Error(
            `The bubble drawing covers no screen pixel solidly within ${radius} pixels of ` +
                `(${Math.round(near.x)}, ${Math.round(near.y)}): nothing of a bubble or tail is ` +
                `drawn there.`,
        );
    return found;
}

/**
 * Whether ComicalJS's drawing leaves the whole of the screen pixel at `at` untouched (fully
 * transparent), so that whatever lies under the drawing shows there.
 */
export async function isDrawingClearAt(
    page: Page,
    at: IScreenPoint,
): Promise<boolean> {
    const drawing = comicalDrawing(page);
    const box = await drawing.boundingBox({ timeout: 15000 });
    if (!box)
        throw new Error(
            "The bubble drawing surface (canvas.comical-editing) has no on-screen box.",
        );
    return drawing.evaluate(
        (c, args) => {
            const surface = c as HTMLCanvasElement;
            const scale = args.box.width / surface.offsetWidth;
            const ratio = surface.width / surface.offsetWidth;
            const toBacking = (screen: number, origin: number) =>
                ((screen - origin) / scale) * ratio;
            const sx = Math.floor(args.at.x);
            const sy = Math.floor(args.at.y);
            const left = Math.floor(toBacking(sx, args.box.x));
            const top = Math.floor(toBacking(sy, args.box.y));
            const d = surface
                .getContext("2d")!
                .getImageData(
                    left,
                    top,
                    Math.ceil(toBacking(sx + 1, args.box.x)) - left,
                    Math.ceil(toBacking(sy + 1, args.box.y)) - top,
                ).data;
            for (let i = 3; i < d.length; i += 4) if (d[i] !== 0) return false;
            return true;
        },
        { at, box },
    );
}

/** How a tail handle is drawn at a point: filled, an open circle, or not there at all. */
export type HandleDrawing = "filled" | "open" | "none";

/** The radius ComicalJS gives a handle's circle (Handle in comicaljs), in drawing pixels. */
const kHandleRadius = 5;
/** How many directions round a handle's circle are looked at for its outline. */
const kRingDirections = 16;
/** How many of those must show the outline for the circle to count as drawn. */
const kRingDirectionsNeeded = 12;

/**
 * How the drawing shows a handle centred at a point of the drawing surface. A handle's circle is
 * outlined in the handle color whether it is filled or not; a filled one has that color at its
 * middle too, while an open one lets the tail it sits on show through there. Reads the pixels
 * ComicalJS last painted.
 */
export async function getHandleDrawingAt(
    page: Page,
    x: number,
    y: number,
): Promise<HandleDrawing> {
    return comicalDrawing(page).evaluate(
        (c, args) => {
            const drawing = c as HTMLCanvasElement;
            // paper.js may give the surface more backing pixels than layout pixels (on a high-DPI
            // display), so map points onto the backing store.
            const ratio = drawing.width / drawing.offsetWidth;
            const context = drawing.getContext("2d")!;
            const pixelAt = (px: number, py: number) => {
                const d = context.getImageData(
                    Math.round(px * ratio),
                    Math.round(py * ratio),
                    1,
                    1,
                ).data;
                return { r: d[0], g: d[1], b: d[2], a: d[3] };
            };
            const { color, tolerance } = args;
            const centre = pixelAt(args.x, args.y);
            const filled =
                centre.a === 255 &&
                Math.abs(centre.r - color.r) <= tolerance &&
                Math.abs(centre.g - color.g) <= tolerance &&
                Math.abs(centre.b - color.b) <= tolerance;
            // The outline is a line one pixel wide, so the browser smooths it into whatever lies
            // under it (the tail's white or black). Count a pixel as outline when it is opaque
            // and clearly leans to the handle's teal: green and blue well above red, which
            // neither white, black nor grey has.
            const isOutline = (p: {
                r: number;
                g: number;
                b: number;
                a: number;
            }) => p.a >= 128 && p.g - p.r >= 40 && p.b - p.r >= 40;
            let directionsWithOutline = 0;
            for (let i = 0; i < args.directions; i++) {
                const angle = (2 * Math.PI * i) / args.directions;
                for (let r = args.radius - 1; r <= args.radius + 1; r += 0.25) {
                    if (
                        isOutline(
                            pixelAt(
                                args.x + r * Math.cos(angle),
                                args.y + r * Math.sin(angle),
                            ),
                        )
                    ) {
                        directionsWithOutline++;
                        break;
                    }
                }
            }
            if (directionsWithOutline < args.needed) return "none";
            return filled ? "filled" : "open";
        },
        {
            x,
            y,
            color: kTailHandleColor,
            tolerance: kColorTolerance,
            radius: kHandleRadius,
            directions: kRingDirections,
            needed: kRingDirectionsNeeded,
        },
    );
}

/**
 * Wait until the selected bubble's tail midpoint handle, where its data-bubble says the midpoint
 * is, is drawn as `expected`.
 */
export async function expectMidpointHandle(
    page: Page,
    element: Locator,
    expected: HandleDrawing,
    why: string,
): Promise<void> {
    await expect
        .poll(
            async () => {
                const tail = await readTail(element);
                return getHandleDrawingAt(page, tail.midpointX, tail.midpointY);
            },
            { timeout: 15000, message: why },
        )
        .toBe(expected);
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
