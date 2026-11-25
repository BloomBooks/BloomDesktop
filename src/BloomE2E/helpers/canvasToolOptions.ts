// The Canvas tool's options for the selected text element (its Rounded Corners box and its
// Background Color bar), and the shape ComicalJS saves for each element of a canvas page.
//
// ComicalJS draws an element's outline and background on a drawing surface while the page is
// edited, and Bloom saves that drawing into the page as an SVG (svg.comical-generated) when the page
// is left. The SVG is what every reader of the book sees, so a test of how an element is drawn
// reads the saved SVG rather than the pixels of the editing surface.

import { expect, type Frame, type Locator, type Page } from "@playwright/test";
import { bookHtmlPath } from "./bookHtml";
import { cssColorToHex } from "./cssValues";
import { openCanvasTool } from "./canvasElements";
import * as fs from "node:fs";

/** The Rounded Corners check box in the Canvas tool. */
const ROUNDED_CORNERS =
    'label:has-text("Rounded Corners") input[type="checkbox"]';
/** The Background Color bar: the input box beside its label (the ColorBar has no id of its own). */
const BACKGROUND_COLOR_BAR =
    ':has(> label[for="background-color-bar"]) > .MuiInput-formControl';
/** Bloom's color picker dialog, and one swatch in it (the inner div is the part that takes the click). */
const COLOR_PICKER = '[role="dialog"]:has(.color-swatch)';
const COLOR_SWATCH = ".color-swatch > div:last-child";

/** The Rounded Corners check box, once the Canvas tool is open. */
export async function roundedCornersBox(page: Page): Promise<Locator> {
    const toolbox = await openCanvasTool(page);
    const box = toolbox.locator(ROUNDED_CORNERS).first();
    await box.waitFor({ state: "visible", timeout: 15000 });
    return box;
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
 * Choose the selected element's background color from the Canvas tool's Background Color bar:
 * open the color picker, click the swatch of `color` ("#rrggbb", one of the picker's palette), and
 * OK it. Throws, listing the swatches, if the palette has no such color.
 */
export async function chooseCanvasElementBackgroundColor(
    page: Page,
    color: string,
): Promise<void> {
    const toolbox = await openCanvasTool(page);
    await toolbox.locator(BACKGROUND_COLOR_BAR).first().click();
    const frame = await colorPickerFrame(page);
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
    await dialog.getByRole("button", { name: "OK" }).click();
    await expect(dialog, "The color picker did not close after OK.").toBeHidden(
        { timeout: 15000 },
    );
}

/** One shape that ComicalJS saved for a canvas page: a path that is filled or stroked. */
export interface ISavedShape {
    /** The shape's fill and stroke attributes, to tell the shapes apart in a failure message. */
    fill: string;
    stroke: string;
    /** The size of the shape's bounding box, in the SVG's units. */
    width: number;
    height: number;
    /**
     * How many of the four corners of the bounding box the shape covers, with its fill or its
     * stroke. A plain rectangle covers all four; a rectangle with rounded corners covers none.
     */
    cornersCovered: number;
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
                        const covered = corners.filter(([x, y]) => {
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
                                width: Math.round(box.width),
                                height: Math.round(box.height),
                                cornersCovered: covered,
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
