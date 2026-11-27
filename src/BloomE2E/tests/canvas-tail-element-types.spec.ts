// A canvas element's tail, shown or hidden with the Canvas tool's Show Tail box, is drawn in the
// kind that goes with the element's Element Type: ComicalJS makes a Thought bubble's tail a trail
// of little bubbles, a Caption's, Rectangle's or Just Text's a straight line, and every other
// type's a curved wedge joined to the bubble (Bubble.makeTail in comicaljs). Changing the Element
// Type while the tail shows redraws the tail in the new type's kind.
//
// A change of type keeps the tail, except that a tail the old type has by default is dropped when
// the new type has none by default (Bubble.mergeWithNewBubbleProps in comicaljs): Speech, Ellipse
// and Thought start with a tail, the other types without. The test checks that rule too, ticks
// Show Tail again where the change dropped the tail, and orders the types so that every change
// goes to a type whose tail is of another kind.
//
// The test reads what Bloom saved: the element's data-bubble, which holds its type and its tail's
// tip, and the drawing ComicalJS saved for the page (svg.comical-generated), which is what every
// reader of the book sees. For each type it saves the page with the tail shown, with the tail's
// tip dragged somewhere else, and with the tail hidden. The shapes of a drawing with the tail that
// the drawing without it lacks are the tail's (for a curved wedge, the bubble's outline grown to
// take in the tail), and they must reach the saved tip.
//
// Automates "Show Tail Comic Styles" (Test Case ID 328).

import { type Locator, type Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    waitForSnapshotStreamIdle,
} from "../helpers/bookMaking";
import {
    canvasElement,
    dragPaletteItemOntoCanvas,
    selectCanvasElement,
} from "../helpers/canvasElements";
import {
    chooseCanvasElementType,
    readSavedBubbleSpecs,
    readSavedCanvasShapes,
    setShowTail,
    showTailBox,
    type CanvasElementType,
    type ISavedShape,
} from "../helpers/canvasToolOptions";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { cssColorToHex } from "../helpers/cssValues";
import { watchForScriptErrors } from "../helpers/scriptErrors";
import { dragTailTip, readTail } from "../helpers/speechBubbleTail";

test.use({
    collectionSpec: {
        name: "canvas-tail-element-types",
        languages: ["en"],
        // Below Pro, the Canvas tool's options sit behind a subscription notice.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

/** The kinds of tail ComicalJS draws, or none at all. */
type TailKind =
    | "curved wedge"
    | "trail of little bubbles"
    | "straight line"
    | "no tail";

/**
 * Every Element Type, in the order the test gives them to the element, with the kind of tail each
 * one draws and whether it has a tail by default (Bubble.getDefaultBubbleSpec in comicaljs).
 */
const kElementTypes: {
    type: CanvasElementType;
    tail: TailKind;
    tailByDefault: boolean;
}[] = [
    { type: "speech", tail: "curved wedge", tailByDefault: true },
    { type: "thought", tail: "trail of little bubbles", tailByDefault: true },
    { type: "ellipse", tail: "curved wedge", tailByDefault: true },
    { type: "rectangle", tail: "straight line", tailByDefault: false },
    { type: "circle", tail: "curved wedge", tailByDefault: false },
    { type: "caption", tail: "straight line", tailByDefault: false },
    { type: "pointedArcs", tail: "curved wedge", tailByDefault: false },
    { type: "none", tail: "straight line", tailByDefault: false },
];

/** How far the test drags a tail's tip, in the drawing surface's pixels. */
const kTipDrag = 40;
/**
 * How far from the saved tip the tail's drawing may end, in the drawing's pixels: the last little
 * bubble of a thought tail stops a few pixels short of the tip.
 */
const kTipReach = 6;
/** The largest a little bubble of a thought tail is drawn, in the drawing's pixels. */
const kLittleBubbleSize = 30;
/** How far a shape may move between two saves and still count as the same shape. */
const kSameShapeTolerance = 1;

/** A point in the drawing surface's pixels. */
interface IPoint {
    x: number;
    y: number;
}

/** What Bloom saved for the element and its page. */
interface ISaved {
    tailTips: IPoint[];
    shapes: ISavedShape[];
}

/**
 * Save the page by leaving it for the cover, wait for the saved book to give the element at
 * `index` the Element Type `style` and `tailCount` tails, then return what was saved. Comes back to
 * the page and selects the element again.
 */
async function savePage(
    page: Page,
    bookFolder: string,
    pageId: string,
    index: number,
    style: string,
    tailCount: number,
    why: string,
): Promise<ISaved> {
    // Leaving a page saves the last snapshot of it that Bloom received, which is sent a moment
    // after the page last changed; let the change reach Bloom first.
    await waitForSnapshotStreamIdle(page);
    const cover = (await getPages(page))[0];
    await goToPage(page, cover.id);
    await expect
        .poll(
            async () => {
                const spec = (
                    await readSavedBubbleSpecs(page, bookFolder, pageId)
                )[index];
                return { style: spec?.style, tails: spec?.tailTips.length };
            },
            { timeout: 15000, message: why },
        )
        .toEqual({ style, tails: tailCount });
    const spec = (await readSavedBubbleSpecs(page, bookFolder, pageId))[index]!;
    const shapes = await readSavedCanvasShapes(page, bookFolder, pageId);
    await goToPage(page, pageId);
    await selectCanvasElement(page, index);
    return { tailTips: spec.tailTips, shapes };
}

/**
 * A saved fill or stroke as "#rrggbb" where it is a color, and as "gradient" where it is one:
 * ComicalJS writes the same color as "#ffffff" in one drawing and "rgb(255, 255, 255)" in another.
 */
function paint(value: string): string {
    // A gradient (a Caption's background) is referred to by an id made afresh for each drawing.
    if (value.startsWith("url(")) return "gradient";
    try {
        return cssColorToHex(value);
    } catch {
        return value;
    }
}

/** Whether two saved shapes are the same shape, painted the same, in the same place. */
function sameShape(a: ISavedShape, b: ISavedShape): boolean {
    return (
        paint(a.fill) === paint(b.fill) &&
        paint(a.stroke) === paint(b.stroke) &&
        Math.abs(a.x - b.x) <= kSameShapeTolerance &&
        Math.abs(a.y - b.y) <= kSameShapeTolerance &&
        Math.abs(a.width - b.width) <= kSameShapeTolerance &&
        Math.abs(a.height - b.height) <= kSameShapeTolerance
    );
}

/** Whether a shape is filled with something that can be seen. */
function isFilled(shape: ISavedShape): boolean {
    return shape.fillOpacity > 0.5;
}

/** Whether a shape's outline is drawn in a color that shows on a white page. */
function isDarkStroke(shape: ISavedShape): boolean {
    return paint(shape.stroke) === "#000000";
}

/** How far `point` lies outside the bounding box of `shapes` (0 when inside). */
function distanceOutside(shapes: ISavedShape[], point: IPoint): number {
    const left = Math.min(...shapes.map((s) => s.x));
    const top = Math.min(...shapes.map((s) => s.y));
    const right = Math.max(...shapes.map((s) => s.x + s.width));
    const bottom = Math.max(...shapes.map((s) => s.y + s.height));
    const dx = Math.max(left - point.x, 0, point.x - right);
    const dy = Math.max(top - point.y, 0, point.y - bottom);
    return Math.hypot(dx, dy);
}

/**
 * The kind of tail the saved drawing `withTail` shows that `withoutTail`, the same element's
 * drawing with its tail hidden, lacks, and whether that tail reaches `tip`. The shapes of
 * `withTail` that `withoutTail` lacks are the tail's:
 * - a trail of little bubbles: two or more small filled shapes, each with a dark outline;
 * - a curved wedge: one filled shape with a dark outline that takes in the whole of the bubble's
 *   own outline, since ComicalJS joins a curved tail to its bubble;
 * - a straight line: dark-outlined shapes, none of them filled.
 */
function describeTail(
    withTail: ISavedShape[],
    withoutTail: ISavedShape[],
    tip: IPoint,
): { kind: TailKind | "unrecognized"; reachesTip: boolean } {
    const tailShapes = withTail.filter(
        (s) => !withoutTail.some((w) => sameShape(s, w)),
    );
    if (tailShapes.length === 0) return { kind: "no tail", reachesTip: false };
    const outlined = tailShapes.filter(isDarkStroke);
    const filled = outlined.filter(isFilled);
    const reachesTip =
        outlined.length > 0 && distanceOutside(outlined, tip) <= kTipReach;
    // The bubble's own filled, outlined shape, as drawn without the tail.
    const bubble = withoutTail.filter((s) => isFilled(s) && isDarkStroke(s));
    if (
        filled.length >= 2 &&
        filled.every(
            (s) =>
                s.width <= kLittleBubbleSize && s.height <= kLittleBubbleSize,
        )
    )
        return { kind: "trail of little bubbles", reachesTip };
    if (
        filled.length === 1 &&
        bubble.length === 1 &&
        filled[0].x <= bubble[0].x + kSameShapeTolerance &&
        filled[0].y <= bubble[0].y + kSameShapeTolerance &&
        filled[0].x + filled[0].width >=
            bubble[0].x + bubble[0].width - kSameShapeTolerance &&
        filled[0].y + filled[0].height >=
            bubble[0].y + bubble[0].height - kSameShapeTolerance
    )
        return { kind: "curved wedge", reachesTip };
    if (outlined.length > 0 && filled.length === 0)
        return { kind: "straight line", reachesTip };
    return { kind: "unrecognized", reachesTip };
}

/**
 * The direction, as a unit vector in the drawing surface's coordinates, from the middle of the
 * bubble towards its tail's tip. Dragging the tip further that way keeps it outside the bubble.
 */
async function awayFromBubble(bubble: Locator, tip: IPoint): Promise<IPoint> {
    // The drawing surface sits at the top left of the canvas, which is the bubble's offset parent.
    const centre = await bubble.evaluate((e) => {
        const element = e as HTMLElement;
        return {
            x: element.offsetLeft + element.offsetWidth / 2,
            y: element.offsetTop + element.offsetHeight / 2,
        };
    });
    const dx = tip.x - centre.x;
    const dy = tip.y - centre.y;
    const length = Math.hypot(dx, dy);
    if (length === 0)
        throw new Error("The tail's tip is at the middle of its bubble.");
    return { x: dx / length, y: dy / length };
}

test("Show Tail draws the tail of each Element Type, and the tail follows a change of type [Test Case ID 328]", async ({
    page,
    step,
}) => {
    test.setTimeout(600000);
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    const errors = watchForScriptErrors(page);
    try {
        let pageId = "";
        let index = -1;
        let bubble!: Locator;

        await step("Put a speech bubble on a Canvas page", async () => {
            const before = (await getContentPages(page)).map((p) => p.id);
            await addPage(page, "Canvas");
            const added = (await getContentPages(page)).find(
                (p) => !before.includes(p.id),
            );
            if (!added)
                throw new Error("Adding a Canvas page added no content page.");
            pageId = added.id;
            await goToPage(page, pageId);
            index = await dragPaletteItemOntoCanvas(page, "speech");
            await selectCanvasElement(page, index);
            bubble = canvasElement(page, index);
            await expect(
                await showTailBox(page),
                "A new speech bubble should have Show Tail ticked.",
            ).toBeChecked({ timeout: 15000 });
        });

        let previous: (typeof kElementTypes)[number] | undefined;
        for (const current of kElementTypes) {
            const { type, tail } = current;
            await step(
                previous
                    ? `With the tail shown, change the Element Type from ${previous.type} to ${type}`
                    : `Element Type ${type}`,
                async () => {
                    if (!previous) return;
                    await chooseCanvasElementType(page, type);
                    // The tail stays unless it was the old type's default and the new type has
                    // none by default.
                    const kept =
                        !previous.tailByDefault || current.tailByDefault;
                    await expect(
                        await showTailBox(page),
                        kept
                            ? `After the change from ${previous.type} to ${type}, Show Tail should still be ticked.`
                            : `The change from ${previous.type}, which has a tail by default, to ` +
                                  `${type}, which has none, should untick Show Tail.`,
                    ).toBeChecked({ checked: kept, timeout: 15000 });
                    if (!kept) {
                        await savePage(
                            page,
                            bookFolder,
                            pageId,
                            index,
                            type,
                            0,
                            `The change from ${previous.type} to ${type} did not save the element without a tail.`,
                        );
                        await setShowTail(page, true);
                    }
                },
            );

            let shown!: ISaved;
            let moved!: ISaved;
            await step(
                `Save the ${type} element with its tail shown`,
                async () => {
                    shown = await savePage(
                        page,
                        bookFolder,
                        pageId,
                        index,
                        type,
                        1,
                        `The ${type} element was not saved with its tail.`,
                    );
                },
            );
            await step(`Drag the ${type} element's tail tip`, async () => {
                const before = await readTail(bubble);
                const away = await awayFromBubble(bubble, {
                    x: before.tipX,
                    y: before.tipY,
                });
                await dragTailTip(
                    page,
                    bubble,
                    away.x * kTipDrag,
                    away.y * kTipDrag,
                );
                moved = await savePage(
                    page,
                    bookFolder,
                    pageId,
                    index,
                    type,
                    1,
                    `The ${type} element was not saved with its moved tail.`,
                );
                expect(
                    Math.hypot(
                        moved.tailTips[0].x - shown.tailTips[0].x,
                        moved.tailTips[0].y - shown.tailTips[0].y,
                    ),
                    `The ${type} element's saved tail tip did not move far: it went from ` +
                        `${JSON.stringify(shown.tailTips[0])} to ${JSON.stringify(moved.tailTips[0])}.`,
                ).toBeGreaterThan(kTipDrag / 2);
            });
            await step(`Untick Show Tail for the ${type} element`, async () => {
                await setShowTail(page, false);
                const hidden = await savePage(
                    page,
                    bookFolder,
                    pageId,
                    index,
                    type,
                    0,
                    `The ${type} element was not saved without its tail.`,
                );
                const drawings = (saved: ISaved) =>
                    JSON.stringify(saved.shapes);
                expect(
                    describeTail(
                        shown.shapes,
                        hidden.shapes,
                        shown.tailTips[0],
                    ),
                    `The ${type} element's saved drawing should show a ${tail} reaching its tip ` +
                        `${JSON.stringify(shown.tailTips[0])}. With the tail: ${drawings(shown)}. ` +
                        `Without: ${drawings(hidden)}.`,
                ).toEqual({ kind: tail, reachesTip: true });
                expect(
                    describeTail(
                        moved.shapes,
                        hidden.shapes,
                        moved.tailTips[0],
                    ),
                    `After its tip was dragged, the ${type} element's saved drawing should show a ` +
                        `${tail} reaching the new tip ${JSON.stringify(moved.tailTips[0])}. With the ` +
                        `tail: ${drawings(moved)}. Without: ${drawings(hidden)}.`,
                ).toEqual({ kind: tail, reachesTip: true });
                expect(
                    JSON.stringify(moved.shapes),
                    `The ${type} element's saved drawing did not change when its tail tip was dragged.`,
                ).not.toBe(JSON.stringify(shown.shapes));
            });
            await step(
                `Tick Show Tail again for the ${type} element`,
                async () => {
                    await setShowTail(page, true);
                    await expect
                        .poll(
                            async () =>
                                (await readTail(bubble).catch(
                                    () => undefined,
                                )) !== undefined,
                            {
                                timeout: 15000,
                                message: `Ticking Show Tail did not give the ${type} element a tail again.`,
                            },
                        )
                        .toBe(true);
                },
            );
            previous = current;
        }

        expect(errors.errors(), "Bloom raised a script error.").toEqual([]);
    } finally {
        errors.stop();
    }
});
