import { afterEach, describe, expect, it, vi } from "vitest";
import {
    placementOf,
    refreshOverflowIndicatorsSoon,
} from "./overflowIndicators";

// These need real layout (offsets and client rectangles), so this runs in the "browser" vitest
// project, not jsdom.

// The chips' localized text would come from Bloom's API; the English is enough here.
vi.mock("../../lib/localizationManager/localizationManager", () => ({
    default: { asyncGetText: () => ({ done: () => undefined }) },
}));
// The real scale is read from the page iframe, which a test has none of.
vi.mock("../js/editableDivUtils", () => ({
    EditableDivUtils: { getPageScale: () => 1 },
}));

const kLineHeightPx = 20;

// Adds a page 600 by 400 pixels at the top left of the viewport holding `html`, and returns its
// first text box. overflowIndicators.less is not loaded, so this gives the drawing layer the one
// rule of it that placement depends on.
function addPage(html: string): HTMLElement {
    const page = document.createElement("div");
    page.id = "overflowIndicatorsTestPage";
    page.className = "bloom-page";
    page.style.cssText =
        "position: absolute; top: 0; left: 0; width: 600px; height: 400px; margin: 0;";
    page.innerHTML = `
        <style>#bloom-overflow-indicator-layer, #bloom-overflow-indicator-layer > * { position: absolute; }</style>
        ${html}`;
    document.body.appendChild(page);
    return page.querySelector<HTMLElement>(".bloom-editable, textarea")!;
}

// The style of a text box 200 by 100 pixels with lines kLineHeightPx tall, at (left, top).
function boxStyle(left: number, top: number): string {
    return `position: absolute; left: ${left}px; top: ${top}px; width: 200px; height: 100px;
        overflow: hidden; font: 14px Arial; line-height: ${kLineHeightPx}px; padding: 0;
        border: 0; margin: 0;`;
}

// Ten lines of text: twice what a box styled by boxStyle shows.
const kLines = Array.from({ length: 10 }, (_, i) => `Line ${i + 1}`);
// The ten lines as paragraphs, for a text box.
const kTenLines = kLines.map((l) => `<p style="margin: 0">${l}</p>`).join("");

// Makes a page with a text box 200 by 100 pixels at (50, 40) holding ten lines of text, so the
// box shows five and the other five do not fit.
function makePageWithBoxTooFull(): HTMLElement {
    return addPage(
        `<div class="bloom-editable overflow" style="${boxStyle(50, 40)}">${kTenLines}</div>`,
    );
}

// Lets the redraw that refreshOverflowIndicatorsSoon schedules for the next frame happen.
async function redraw(): Promise<void> {
    refreshOverflowIndicatorsSoon();
    await new Promise((resolve) => requestAnimationFrame(resolve));
    await new Promise((resolve) => requestAnimationFrame(resolve));
}

// The client rectangle of the one drawing with this class, or undefined if there is none.
function drawn(className: string): DOMRect | undefined {
    const drawings = document.querySelectorAll(
        `#bloom-overflow-indicator-layer .${className}`,
    );
    expect(drawings.length).toBeLessThanOrEqual(1);
    return drawings[0]?.getBoundingClientRect();
}

// Makes a page with a canvas element 200 by 100 pixels at (100, 50) in it, rotated by `degrees`
// when that is not zero, holding a text box 50 by 30 pixels at (10, 20) inside the canvas
// element's 2-pixel border.
function makePageWithCanvasElement(degrees: number): HTMLElement {
    return addPage(`
        <div class="bloom-canvas-element${degrees ? " bloom-rotated" : ""}"
             style="position: absolute; left: 100px; top: 50px; width: 200px; height: 100px;
                    border: 2px solid black; box-sizing: border-box;${degrees ? ` transform: rotate(${degrees}deg);` : ""}">
            <div class="bloom-editable" style="position: absolute; left: 10px; top: 20px;
                    width: 50px; height: 30px;"></div>
        </div>`);
}

afterEach(() => {
    document.getElementById("overflowIndicatorsTestPage")?.remove();
    document.getElementById("bloom-overflow-indicator-layer")?.remove();
});

describe("the marks on a box with more text than fits", () => {
    it("are at the box's bottom edge before it is scrolled", async () => {
        const box = makePageWithBoxTooFull();
        // Sanity check: half the text doesn't fit.
        expect(box.scrollHeight).toBeCloseTo(10 * kLineHeightPx, -1);
        expect(box.clientHeight).toBe(100);

        await redraw();

        expect(drawn("bloom-overflow-chip")!.top).toBeCloseTo(140);
        expect(
            document.querySelector(
                "#bloom-overflow-indicator-layer .bloom-overflow-chip",
            )!.textContent,
        ).toBe("↓ Overflowing");
        // The last line that fits fades out above the edge.
        const fade = drawn("bloom-overflow-fadeLastLine")!;
        expect(fade.top).toBeCloseTo(140 - kLineHeightPx);
        expect(fade.bottom).toBeCloseTo(140);
        // Nothing that doesn't fit is in view, so nothing is washed.
        expect(drawn("bloom-overflow-cutOffText")).toBeUndefined();
    });

    it("follow the text as the box scrolls, washing the lines that don't fit", async () => {
        const box = makePageWithBoxTooFull();
        await redraw();
        expect(drawn("bloom-overflow-chip")!.top).toBeCloseTo(140);

        box.scrollTop = 3 * kLineHeightPx;
        expect(box.scrollTop).toBe(60);
        await redraw();

        // The cut is where the box's bottom was, moved up with the text: between line 5, the
        // last that fits, and line 6.
        const cutViewportPx = 140 - 60;
        expect(drawn("bloom-overflow-chip")!.top).toBeCloseTo(cutViewportPx);
        const wash = drawn("bloom-overflow-cutOffText")!;
        expect(wash.top).toBeCloseTo(cutViewportPx);
        expect(wash.bottom).toBeCloseTo(140);
        expect(wash.left).toBeCloseTo(50);
        expect(wash.width).toBeCloseTo(200);
        // The edge is no longer the cut, so the fade goes.
        expect(drawn("bloom-overflow-fadeLastLine")).toBeUndefined();
    });
});

describe("the marks for other kinds of overflow", () => {
    it("mark a textarea with more text than fits, as in a calendar's day cells", async () => {
        const textarea = addPage(
            `<textarea class="overflow" style="${boxStyle(50, 40)} resize: none;"></textarea>`,
        );
        textarea.value = kLines.join("\n");
        // Sanity check: half the text doesn't fit.
        expect(textarea.scrollHeight).toBeGreaterThan(textarea.clientHeight);

        await redraw();

        expect(drawn("bloom-overflow-chip")!.top).toBeCloseTo(140);
    });

    it("mark both the box and the page when a box with too much text also pushes past the margin", async () => {
        // The margin box ends at y 220; the box runs from 170 to 270.
        addPage(`
            <div class="marginBox childOverflowingThis" style="position: absolute; left: 20px;
                    top: 20px; width: 400px; height: 200px;">
                <div class="bloom-editable overflow thisOverflowingParent"
                     style="${boxStyle(0, 150)}">${kTenLines}</div>
            </div>`);

        await redraw();

        const chips = Array.from(
            document.querySelectorAll(
                "#bloom-overflow-indicator-layer .bloom-overflow-chip",
            ),
        );
        // Sanity check: one chip for the box and one for the page.
        expect(chips.length).toBe(2);
        const boxChip = chips.find((c) => c.textContent === "↓ Overflowing")!;
        expect(boxChip.getBoundingClientRect().top).toBeCloseTo(270);
        const pastMargin = drawn("bloom-overflow-pastContainer")!;
        expect(pastMargin.top).toBeCloseTo(220);
        expect(pastMargin.bottom).toBeCloseTo(270);
    });
});

describe("the fade on the last line that fits", () => {
    // The color the fade ends in, which overflowIndicators.ts hands to the stylesheet.
    function fadeColor(): string {
        return document
            .querySelector<HTMLElement>(
                "#bloom-overflow-indicator-layer .bloom-overflow-fadeLastLine",
            )!
            .style.getPropertyValue("--overflow-background");
    }

    it.each([
        {
            where: "the box's own background",
            html: `<div class="bloom-editable overflow" style="${boxStyle(50, 40)} background-color: rgb(20, 40, 60);">${kTenLines}</div>`,
            expected: "rgb(20, 40, 60)",
        },
        {
            where: "the color of what the box is on, when the box has none",
            html: `<div style="position: absolute; left: 0; top: 0; width: 400px; height: 300px; background-color: rgb(250, 220, 130);">
                    <div class="bloom-editable overflow" style="${boxStyle(50, 40)}">${kTenLines}</div>
                </div>`,
            expected: "rgb(250, 220, 130)",
        },
        {
            where: "white, when nothing behind the box has a color",
            html: `<div class="bloom-editable overflow" style="${boxStyle(50, 40)}">${kTenLines}</div>`,
            expected: "white",
        },
    ])("ends in $where", async ({ html, expected }) => {
        addPage(html);

        await redraw();

        expect(fadeColor()).toBe(expected);
    });
});

describe("a page that scrolls instead of overflowing (Device and Ebook layouts)", () => {
    // Makes a page with a box of ten lines that shows five, as makePageWithBoxTooFull does, on
    // a page of the given layout, the box wrapped in `wrapperClass` when one is given.
    function addScrollingPage(layout: string, wrapperClass?: string) {
        const box = `<div class="bloom-editable overflow" style="${boxStyle(50, 40)}">${kTenLines}</div>`;
        addPage(
            wrapperClass ? `<div class="${wrapperClass}">${box}</div>` : box,
        );
        document
            .getElementById("overflowIndicatorsTestPage")!
            .classList.add(layout);
    }

    function drawingCount(): number {
        return document.querySelectorAll("#bloom-overflow-indicator-layer > *")
            .length;
    }

    it.each(["Device16x9Portrait", "Ebook2x3Portrait"])(
        "does not mark a box with too much text on a %s page",
        async (layout) => {
            // Sanity check: the same box on a paper page is marked.
            makePageWithBoxTooFull();
            await redraw();
            expect(drawingCount()).toBeGreaterThan(0);
            document.getElementById("overflowIndicatorsTestPage")!.remove();

            addScrollingPage(layout);
            await redraw();

            expect(drawingCount()).toBe(0);
        },
    );

    it("still marks text that does not fit on a button", async () => {
        addScrollingPage("Device16x9Portrait", "bloom-canvas-button");

        await redraw();

        expect(drawn("bloom-overflow-chip")!.top).toBeCloseTo(140);
    });
});

describe("placementOf", () => {
    it("uses the box's own rectangle when it is not rotated", () => {
        const box = makePageWithCanvasElement(0);

        const placement = placementOf(box, 1);

        expect(placement.rotation).toBeUndefined();
        expect(placement.rectViewportPx.left).toBeCloseTo(112);
        expect(placement.rectViewportPx.top).toBeCloseTo(72);
        expect(placement.rectViewportPx.width).toBeCloseTo(50);
        expect(placement.rectViewportPx.height).toBeCloseTo(30);
    });

    it("gives where a box in a rotated canvas element would be upright, and the rotation", () => {
        const box = makePageWithCanvasElement(90);
        // Sanity check: the browser's rectangle for the box is the turned one, 30 wide and 50
        // tall, so it can't be used as it is.
        expect(box.getBoundingClientRect().width).toBeCloseTo(30);
        expect(box.getBoundingClientRect().height).toBeCloseTo(50);

        const placement = placementOf(box, 1);

        expect(placement.rectViewportPx.left).toBeCloseTo(112);
        expect(placement.rectViewportPx.top).toBeCloseTo(72);
        expect(placement.rectViewportPx.width).toBeCloseTo(50);
        expect(placement.rectViewportPx.height).toBeCloseTo(30);
        expect(placement.rotation!.degrees).toBe(90);
        expect(placement.rotation!.frameViewportPx.left).toBeCloseTo(100);
        expect(placement.rotation!.frameViewportPx.top).toBeCloseTo(50);
        expect(placement.rotation!.frameViewportPx.width).toBeCloseTo(200);
        expect(placement.rotation!.frameViewportPx.height).toBeCloseTo(100);
    });
});
