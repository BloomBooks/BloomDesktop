// The Canvas tool's options for the selected text element (its Rounded Corners box and its
// Background Color bar), and the shape ComicalJS saves for each element of a canvas page.
//
// ComicalJS draws an element's outline and background on a drawing surface while the page is
// edited, and Bloom saves that drawing into the page as an SVG (svg.comical-generated) when the page
// is left. The SVG is what every reader of the book sees, so a test of how an element is drawn
// reads the saved SVG rather than the pixels of the editing surface.

import { expect, type Frame, type Locator, type Page } from "@playwright/test";
import { bookHtmlPath } from "./bookHtml";
import { closeColorPickerWith, colorPicker, typeInHexBox } from "./colorPicker";
import { cssColorToHex } from "./cssValues";
import { openCanvasTool } from "./canvasElements";
import * as fs from "node:fs";

/**
 * The check boxes that go with the Element Type choice in the Canvas tool: Show Tail, then Rounded
 * Corners (CanvasToolControls.tsx). Found by where they sit rather than by their labels, which are
 * in the UI language, and that follows Windows' language on a machine with no Bloom settings.
 */
const ELEMENT_TYPE_CHECK_BOXES =
    '.MuiFormControl-root:has(#canvasElement-style-dropdown) input[type="checkbox"]';
/**
 * The Element Type choice: MUI's Select, whose id is on its hidden input, and the list it opens
 * (CanvasToolControls.tsx gives the list's menu the class below). Each choice in the list carries
 * its value, a ComicalJS style name, as data-value; its label is in the UI language.
 */
const ELEMENT_TYPE_INPUT = "#canvasElement-style-dropdown";
const ELEMENT_TYPE_SELECT = `.MuiFormControl-root:has(${ELEMENT_TYPE_INPUT}) .MuiSelect-select`;
const ELEMENT_TYPE_MENU = ".canvasElement-options-dropdown-menu";
/** The Background Color bar: the input box beside its label (the ColorBar has no id of its own). */
const BACKGROUND_COLOR_BAR =
    ':has(> label[for="background-color-bar"]) > .MuiInput-formControl';
/** Bloom's color picker dialog, and one swatch in it (the inner div is the part that takes the click). */
const COLOR_PICKER = '[role="dialog"]:has(.color-swatch)';
const COLOR_SWATCH = ".color-swatch > div:last-child";

/**
 * One of the check boxes beside the Element Type choice, once the Canvas tool is open: 0 for Show
 * Tail, 1 for Rounded Corners.
 */
async function elementTypeCheckBox(
    page: Page,
    position: 0 | 1,
): Promise<Locator> {
    const toolbox = await openCanvasTool(page);
    const boxes = toolbox.locator(ELEMENT_TYPE_CHECK_BOXES);
    await expect(
        boxes,
        "The Element Type choice should have two check boxes beside it: Show Tail and Rounded Corners.",
    ).toHaveCount(2, { timeout: 15000 });
    const box = boxes.nth(position);
    await box.waitFor({ state: "visible", timeout: 15000 });
    return box;
}

/** The Rounded Corners check box, once the Canvas tool is open. */
export async function roundedCornersBox(page: Page): Promise<Locator> {
    return elementTypeCheckBox(page, 1);
}

/** The Show Tail check box, once the Canvas tool is open. */
export async function showTailBox(page: Page): Promise<Locator> {
    return elementTypeCheckBox(page, 0);
}

/**
 * Tick or untick Show Tail for the selected element, the way a person does, and wait for the box
 * to show it. Fails if the box is disabled, which it is for a child bubble or a button.
 */
export async function setShowTail(page: Page, show: boolean): Promise<void> {
    const box = await showTailBox(page);
    await expect(
        box,
        "Show Tail is disabled for the selected element.",
    ).toBeEnabled({
        timeout: 15000,
    });
    if ((await box.isChecked()) !== show) await box.click();
    await expect(
        box,
        `Clicking Show Tail did not ${show ? "tick" : "untick"} it.`,
    ).toBeChecked({ checked: show, timeout: 15000 });
}

/**
 * The Element Type choices the Canvas tool offers for a text element, by the ComicalJS style each
 * one gives (CanvasToolControls.tsx): "pointedArcs" is the one the list calls Exclamation, and
 * "none" is Just Text.
 */
export type CanvasElementType =
    | "caption"
    | "pointedArcs"
    | "none"
    | "speech"
    | "ellipse"
    | "thought"
    | "circle"
    | "rectangle";

/** The Element Type the Canvas tool shows for the selected element. */
export async function getCanvasElementTypeShown(page: Page): Promise<string> {
    const toolbox = await openCanvasTool(page);
    return toolbox.locator(ELEMENT_TYPE_INPUT).inputValue({ timeout: 15000 });
}

/**
 * Choose the selected element's Element Type in the Canvas tool, the way a person does: open the
 * list and click the choice. Waits for the tool to show the new type.
 */
export async function chooseCanvasElementType(
    page: Page,
    type: CanvasElementType,
): Promise<void> {
    const toolbox = await openCanvasTool(page);
    await toolbox.locator(ELEMENT_TYPE_SELECT).first().click();
    const choice = toolbox.locator(
        `${ELEMENT_TYPE_MENU} li[data-value="${type}"]`,
    );
    await choice.waitFor({ state: "visible", timeout: 15000 });
    await choice.click();
    await expect(
        toolbox.locator(ELEMENT_TYPE_MENU),
        "The Element Type list did not close after a choice was clicked.",
    ).toBeHidden({ timeout: 15000 });
    await expect
        .poll(() => getCanvasElementTypeShown(page), {
            timeout: 15000,
            message: `Choosing the Element Type "${type}" did not show it in the Canvas tool.`,
        })
        .toBe(type);
}

/**
 * Tick or untick Rounded Corners for the selected element, the way a person does, and wait for the
 * box to show it. Fails if the box is disabled, which it is for an element whose style takes no
 * rounded corners.
 */
export async function setRoundedCorners(
    page: Page,
    rounded: boolean,
): Promise<void> {
    const box = await roundedCornersBox(page);
    await expect(
        box,
        "Rounded Corners is disabled for the selected element.",
    ).toBeEnabled({ timeout: 15000 });
    if ((await box.isChecked()) !== rounded) await box.click();
    await expect(
        box,
        `Clicking Rounded Corners did not ${rounded ? "tick" : "untick"} it.`,
    ).toBeChecked({ checked: rounded, timeout: 15000 });
}

/**
 * The frame that is showing Bloom's color picker. The Canvas tool asks the Edit tab's workspace to
 * show it, so it is not in the toolbox's frame; look in every frame rather than depend on which.
 */
async function colorPickerFrame(page: Page): Promise<Frame> {
    let found: Frame | undefined;
    await expect
        .poll(
            async () => {
                for (const frame of page.frames()) {
                    const visible = await frame
                        .locator(COLOR_PICKER)
                        .first()
                        .isVisible()
                        .catch(() => false);
                    if (visible) {
                        found = frame;
                        return true;
                    }
                }
                return false;
            },
            {
                timeout: 15000,
                message: "The color picker did not open.",
            },
        )
        .toBe(true);
    return found!;
}

/**
 * Open the color picker from the Canvas tool's Background Color bar, for the selected element, and
 * return the frame that shows it, for the helpers of colorPicker.ts.
 */
export async function openCanvasElementBackgroundColorPicker(
    page: Page,
): Promise<Frame> {
    const toolbox = await openCanvasTool(page);
    await toolbox.locator(BACKGROUND_COLOR_BAR).first().click();
    return colorPickerFrame(page);
}

/**
 * Choose the selected element's background color from the Canvas tool's Background Color bar:
 * open the color picker, click the swatch of `color` ("#rrggbb", one of the picker's palette), and
 * OK it. Throws, listing the swatches, if the palette has no such color.
 */
export async function chooseCanvasElementBackgroundColor(
    page: Page,
    color: string,
): Promise<void> {
    const frame = await openCanvasElementBackgroundColorPicker(page);
    const dialog = frame.locator(COLOR_PICKER).first();
    const swatches = dialog.locator(COLOR_SWATCH);
    await swatches.first().waitFor({ state: "visible", timeout: 15000 });
    const colors = (
        await swatches.evaluateAll((elements) =>
            elements.map((e) => getComputedStyle(e).backgroundColor),
        )
    ).map(cssColorToHex);
    const index = colors.indexOf(color.toLowerCase());
    if (index < 0)
        throw new Error(
            `The color picker has no swatch of ${color}. Its swatches are: ${colors.join(", ")}.`,
        );
    await swatches.nth(index).click();
    await dialog.locator('[data-testid="dialog-ok"]').click();
    await expect(dialog, "The color picker did not close after OK.").toBeHidden(
        { timeout: 15000 },
    );
}

/**
 * Give the selected element a background color the picker's palette does not offer (its
 * background palette has only black, white, gray and transparent): open the color picker from the
 * Canvas tool's Background Color bar, type `color` ("#rrggbb") into its hex box, and OK it.
 */
export async function typeCanvasElementBackgroundColor(
    page: Page,
    color: string,
): Promise<void> {
    const frame = await openCanvasElementBackgroundColorPicker(page);
    await typeInHexBox(frame, color);
    // Read the hex box alone: the picker leaves out its alpha slider for a caption, so a reader
    // of every control would fail there.
    await expect(
        colorPicker(frame).locator('input[type="text"]'),
        `The color picker's hex box did not take ${color}.`,
    ).toHaveValue(new RegExp(`^${color}(ff)?$`, "i"), { timeout: 15000 });
    await closeColorPickerWith(frame, "OK");
}

/** One shape that ComicalJS saved for a canvas page: a path that is filled or stroked. */
export interface ISavedShape {
    /** The shape's fill and stroke attributes, to tell the shapes apart in a failure message. */
    fill: string;
    stroke: string;
    /**
     * How opaque the shape's fill is drawn, from 0 to 1: its opacity times its fill-opacity times
     * the alpha of its fill color. 0 when it has no fill.
     */
    fillOpacity: number;
    /**
     * The top left corner of the shape's bounding box, in the SVG's units, which are the drawing
     * surface's pixels, the same as the points of a bubble's data-bubble spec.
     */
    x: number;
    y: number;
    /** The size of the shape's bounding box, in the SVG's units. */
    width: number;
    height: number;
    /**
     * How many of the four corners of the bounding box the shape covers, with its fill or its
     * stroke. A plain rectangle covers all four; a rectangle with rounded corners covers none.
     */
    cornersCovered: number;
    /**
     * How many of eight points along the edges of the bounding box, three eighths and five eighths
     * of the way along each, the shape covers. A rectangle covers all eight, rounded corners or
     * not; an ellipse, which also leaves the corners empty, covers none.
     */
    edgePointsCovered: number;
}

/**
 * The shapes in the drawing that Bloom saved for one page of the book: every filled or stroked
 * path of the page's svg.comical-generated, largest first. Empty when the saved page has no such
 * drawing.
 *
 * The SVG is parsed and measured inside Bloom's own page (the package has no SVG library); it is
 * put there only for as long as the measuring takes.
 */
export async function readSavedCanvasShapes(
    page: Page,
    bookFolder: string,
    pageId: string,
): Promise<ISavedShape[]> {
    const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
    return page.evaluate(
        ({ source, id }) => {
            const saved = new DOMParser().parseFromString(source, "text/html");
            const svg = saved.querySelector(
                `.bloom-page[id="${id}"] svg.comical-generated`,
            );
            if (!svg) return [];
            // Measuring needs the SVG in a live document; put it out of sight.
            const holder = document.createElement("div");
            holder.style.position = "absolute";
            holder.style.left = "-100000px";
            holder.style.top = "0";
            const live = document.importNode(svg, true) as SVGSVGElement;
            holder.appendChild(live);
            document.body.appendChild(holder);
            try {
                const shapes = Array.from(
                    live.querySelectorAll<SVGGeometryElement>(
                        "path, rect, ellipse, circle, polygon",
                    ),
                )
                    .map((shape) => {
                        const style = getComputedStyle(shape);
                        // A shape painted at zero opacity is drawn but not seen (ComicalJS so
                        // draws an element with a transparent background).
                        const shown =
                            parseFloat(style.opacity) > 0 &&
                            style.visibility !== "hidden" &&
                            style.display !== "none";
                        const filled =
                            shown &&
                            style.fill !== "none" &&
                            parseFloat(style.fillOpacity) > 0;
                        // The computed fill is "rgb(r, g, b)" or "rgba(r, g, b, a)".
                        const fillAlpha = /^rgba\(.*,\s*([\d.]+)\s*\)$/.exec(
                            style.fill,
                        );
                        const fillOpacity = filled
                            ? parseFloat(style.opacity) *
                              parseFloat(style.fillOpacity) *
                              (fillAlpha ? parseFloat(fillAlpha[1]) : 1)
                            : 0;
                        const stroked =
                            shown &&
                            style.stroke !== "none" &&
                            parseFloat(style.strokeOpacity) > 0 &&
                            parseFloat(style.strokeWidth) > 0;
                        const box = shape.getBBox();
                        // Just inside each corner of the bounding box, in the shape's own units.
                        const inset = Math.min(
                            0.5,
                            box.width / 4,
                            box.height / 4,
                        );
                        const corners = [
                            [box.x + inset, box.y + inset],
                            [box.x + box.width - inset, box.y + inset],
                            [box.x + inset, box.y + box.height - inset],
                            [
                                box.x + box.width - inset,
                                box.y + box.height - inset,
                            ],
                        ];
                        const left = box.x + inset;
                        const right = box.x + box.width - inset;
                        const top = box.y + inset;
                        const bottom = box.y + box.height - inset;
                        const edgePoints = [0.375, 0.625].flatMap((f) => {
                            const x = box.x + box.width * f;
                            const y = box.y + box.height * f;
                            return [
                                [left, y],
                                [right, y],
                                [x, top],
                                [x, bottom],
                            ];
                        });
                        const coveredOf = (points: number[][]) =>
                            points.filter(([x, y]) => {
                                const point = new DOMPoint(x, y);
                                return (
                                    (filled && shape.isPointInFill(point)) ||
                                    (stroked && shape.isPointInStroke(point))
                                );
                            }).length;
                        return {
                            visible: filled || stroked,
                            shape: {
                                fill: shape.getAttribute("fill") ?? style.fill,
                                stroke:
                                    shape.getAttribute("stroke") ??
                                    style.stroke,
                                fillOpacity,
                                x: Math.round(box.x),
                                y: Math.round(box.y),
                                width: Math.round(box.width),
                                height: Math.round(box.height),
                                cornersCovered: coveredOf(corners),
                                edgePointsCovered: coveredOf(edgePoints),
                            },
                        };
                    })
                    .filter(
                        (s) =>
                            s.visible &&
                            s.shape.width > 0 &&
                            s.shape.height > 0,
                    )
                    .map((s) => s.shape);
                return shapes.sort(
                    (a, b) => b.width * b.height - a.width * a.height,
                );
            } finally {
                holder.remove();
            }
        },
        { source: html, id: pageId },
    );
}

/** What a canvas element's saved data-bubble says about how ComicalJS draws it. */
export interface ISavedBubbleSpec {
    /** The ComicalJS style, the Element Type's value (e.g. "pointedArcs" for Exclamation). */
    style: string;
    /** The background colors as saved, e.g. ["#ff1616"]; empty when it has none. */
    backgroundColors: string[];
    /** Where each of its tails ends, in the drawing surface's pixels; empty when it has no tail. */
    tailTips: { x: number; y: number }[];
}

/**
 * The data-bubble of each canvas element on one page of the book saved in `bookFolder`, in
 * document order, so the nth entry is the element canvasElement(page, n) finds while the page is
 * edited; undefined for an element with no data-bubble. `page` is used only as a DOM parser.
 * Throws if the saved book has no page with that id.
 */
export async function readSavedBubbleSpecs(
    page: Page,
    bookFolder: string,
    pageId: string,
): Promise<(ISavedBubbleSpec | undefined)[]> {
    const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
    const bubbles = await page.evaluate(
        ({ source, id }) => {
            const saved = new DOMParser().parseFromString(source, "text/html");
            const savedPage = saved.querySelector(`.bloom-page[id="${id}"]`);
            if (!savedPage) return null;
            return Array.from(
                savedPage.querySelectorAll(".bloom-canvas-element"),
            ).map((e) => e.getAttribute("data-bubble"));
        },
        { source: html, id: pageId },
    );
    if (!bubbles)
        throw new Error(`The saved book has no page with the id ${pageId}.`);
    return bubbles.map((json) => {
        if (!json) return undefined;
        // Bloom stores the spec with backquotes in place of double quotes, to keep it out of the
        // way of the attribute's own quotes.
        const spec = JSON.parse(json.replace(/`/g, '"')) as {
            style?: string;
            backgroundColors?: string[];
            tails?: { tipX: number; tipY: number }[];
        };
        return {
            style: spec.style ?? "",
            backgroundColors: spec.backgroundColors ?? [],
            tailTips: (spec.tails ?? []).map((t) => ({
                x: t.tipX,
                y: t.tipY,
            })),
        };
    });
}
