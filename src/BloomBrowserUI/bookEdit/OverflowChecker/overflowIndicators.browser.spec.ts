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

// Makes a page with a text box 200 by 100 pixels at (50, 40) holding ten lines of text, so the
// box shows five and the other five do not fit. overflowIndicators.less is not loaded, so this
// gives the drawing layer the one rule of it that placement depends on.
function makePageWithBoxTooFull(): HTMLElement {
    const page = document.createElement("div");
    page.id = "overflowIndicatorsTestPage";
    page.className = "bloom-page";
    page.style.cssText =
        "position: absolute; top: 0; left: 0; width: 600px; height: 400px; margin: 0;";
    const lines = Array.from(
        { length: 10 },
        (_, i) => `<p style="margin: 0">Line ${i + 1}</p>`,
    ).join("");
    page.innerHTML = `
        <style>#bloom-overflow-indicator-layer, #bloom-overflow-indicator-layer > * { position: absolute; }</style>
        <div class="bloom-editable overflow" style="position: absolute; left: 50px; top: 40px;
                width: 200px; height: 100px; overflow: hidden; font: 14px Arial;
                line-height: ${kLineHeightPx}px; padding: 0; border: 0;">${lines}</div>`;
    document.body.appendChild(page);
    return page.querySelector<HTMLElement>(".bloom-editable")!;
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
function makePage(degrees: number): {
    page: HTMLElement;
    box: HTMLElement;
} {
    const page = document.createElement("div");
    page.id = "overflowIndicatorsTestPage";
    page.className = "bloom-page";
    page.style.cssText =
        "position: absolute; top: 0; left: 0; width: 600px; height: 400px; margin: 0;";
    page.innerHTML = `
        <div class="bloom-canvas-element${degrees ? " bloom-rotated" : ""}"
             style="position: absolute; left: 100px; top: 50px; width: 200px; height: 100px;
                    border: 2px solid black; box-sizing: border-box;${degrees ? ` transform: rotate(${degrees}deg);` : ""}">
            <div class="bloom-editable" style="position: absolute; left: 10px; top: 20px;
                    width: 50px; height: 30px;"></div>
        </div>`;
    document.body.appendChild(page);
    return {
        page,
        box: page.querySelector<HTMLElement>(".bloom-editable")!,
    };
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

        expect(drawn("bloom-overflow-tornEdge")!.top).toBeCloseTo(140);
        expect(drawn("bloom-overflow-chip")).toBeDefined();
        expect(
            document.querySelector(
                "#bloom-overflow-indicator-layer .bloom-overflow-chip",
            )!.textContent,
        ).toBe("Overflowing");
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
        expect(drawn("bloom-overflow-tornEdge")!.top).toBeCloseTo(140);

        box.scrollTop = 3 * kLineHeightPx;
        expect(box.scrollTop).toBe(60);
        await redraw();

        // The cut is where the box's bottom was, moved up with the text: between line 5, the
        // last that fits, and line 6.
        const cutViewportPx = 140 - 60;
        expect(drawn("bloom-overflow-tornEdge")!.top).toBeCloseTo(
            cutViewportPx,
        );
        const wash = drawn("bloom-overflow-cutOffText")!;
        expect(wash.top).toBeCloseTo(cutViewportPx);
        expect(wash.bottom).toBeCloseTo(140);
        expect(wash.left).toBeCloseTo(50);
        expect(wash.width).toBeCloseTo(200);
        // The edge is no longer the cut, so the fade goes.
        expect(drawn("bloom-overflow-fadeLastLine")).toBeUndefined();
    });
});

describe("placementOf", () => {
    it("uses the box's own rectangle when it is not rotated", () => {
        const { page, box } = makePage(0);

        const placement = placementOf(box, page);

        expect(placement.rotation).toBeUndefined();
        expect(placement.rectViewportPx.left).toBeCloseTo(112);
        expect(placement.rectViewportPx.top).toBeCloseTo(72);
        expect(placement.rectViewportPx.width).toBeCloseTo(50);
        expect(placement.rectViewportPx.height).toBeCloseTo(30);
    });

    it("gives where a box in a rotated canvas element would be upright, and the rotation", () => {
        const { page, box } = makePage(90);
        // Sanity check: the browser's rectangle for the box is the turned one, 30 wide and 50
        // tall, so it can't be used as it is.
        expect(box.getBoundingClientRect().width).toBeCloseTo(30);
        expect(box.getBoundingClientRect().height).toBeCloseTo(50);

        const placement = placementOf(box, page);

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
