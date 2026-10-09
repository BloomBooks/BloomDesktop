import { describe, expect, it, test, vi } from "vitest";

// comicaljs draws bubbles on a canvas, which jsdom lacks; these tests only need positions.
vi.mock("comicaljs", () => ({
    Bubble: class {
        static getBubbleSpec() {
            return { spec: "none", tails: [] };
        }
        mergeWithNewBubbleProps() {}
    },
    Comical: { update: vi.fn() },
}));
vi.mock("../bloomImages", () => ({
    getImageFromCanvasElement: (element: HTMLElement) =>
        element.getElementsByTagName("img")[0],
    isPlaceHolderImage: () => false,
}));
vi.mock("./CanvasElementAlternates", () => ({
    adjustCanvasElementAlternates: vi.fn(),
}));

import type { BubbleSpec } from "comicaljs";
import {
    adjustCanvasElementChildrenIfSizeChanged,
    comicalDrawsSomethingFor,
} from "./CanvasElementResizeAdjustments";

// jsdom has no layout, so give each element the box the browser would report.
function setBox(
    element: HTMLElement,
    box: { left: number; top: number; width: number; height: number },
) {
    for (const [name, value] of [
        ["offsetLeft", box.left],
        ["offsetTop", box.top],
        ["clientWidth", box.width],
        ["clientHeight", box.height],
        ["clientLeft", 0],
        ["clientTop", 0],
    ] as const) {
        Object.defineProperty(element, name, {
            configurable: true,
            value: Math.round(value),
        });
    }
}

describe("adjustCanvasElementChildrenIfSizeChanged", () => {
    test("an overlay keeps its place on a background picture that overhangs its bloom-canvas", () => {
        // A 400 x 300 picture area whose background picture overhangs each edge by 1px
        // (adjustBackgroundImageSize's bleed), with a text overlay at x = 100.
        document.body.innerHTML = `<div class="bloom-canvas" data-imgsizebasedon="400,300">
            <div class="bloom-canvas-element bloom-backgroundImage" style="left: -1px; top: -1px; width: 402px; height: 302px;">
                <div class="bloom-imageContainer"><img src="a.jpg"></div>
            </div>
            <div class="bloom-canvas-element" style="left: 100px; top: 100px; width: 50px; height: 20px;"></div>
        </div>`;
        const bloomCanvas = document.querySelector(
            ".bloom-canvas",
        ) as HTMLElement;
        const [bg, overlay] = Array.from(
            bloomCanvas.getElementsByClassName("bloom-canvas-element"),
        ) as HTMLElement[];
        setBox(bg, { left: -1, top: -1, width: 402, height: 302 });
        setBox(overlay, { left: 100, top: 100, width: 50, height: 20 });
        // The origami splitter has widened the pane to 600 x 300.
        setBox(bloomCanvas, { left: 0, top: 0, width: 600, height: 300 });
        const adjustBackgroundImageSize = vi.fn();
        // sanity check: the overlay starts where we put it
        expect(overlay.style.left).toBe("100px");

        adjustCanvasElementChildrenIfSizeChanged(
            bloomCanvas,
            adjustBackgroundImageSize,
        );

        // The picture (still 300 high, so still 400 wide) is centred in the wider pane, 100px
        // in from the left, and the overlay moves with it.
        expect(overlay.style.left).toBe("200px");
        expect(overlay.style.top).toBe("100px");
        expect(bloomCanvas.getAttribute("data-imgsizebasedon")).toBe("600,300");
        expect(adjustBackgroundImageSize).toHaveBeenCalledWith(
            bloomCanvas,
            bg,
            false,
        );
    });
});

// A plain text box: style "none", no tails, nothing chosen for its colours.
function plainTextBox(extra: Partial<BubbleSpec> = {}): BubbleSpec {
    return { version: "1.0", style: "none", tails: [], level: 1, ...extra };
}

describe("comicalDrawsSomethingFor", () => {
    it("is false for a plain text box, which Comical draws nothing for", () => {
        expect(comicalDrawsSomethingFor(plainTextBox())).toBe(false);
    });

    it("is true for any bubble style", () => {
        expect(
            comicalDrawsSomethingFor(plainTextBox({ style: "speech" })),
        ).toBe(true);
        expect(
            comicalDrawsSomethingFor(plainTextBox({ style: "caption" })),
        ).toBe(true);
    });

    it("is true for a plain text box with a tail", () => {
        const tail = { tipX: 1, tipY: 2, midpointX: 3, midpointY: 4 };
        expect(comicalDrawsSomethingFor(plainTextBox({ tails: [tail] }))).toBe(
            true,
        );
    });

    it("is true for a plain text box with a background colour, which Comical paints in a box", () => {
        expect(
            comicalDrawsSomethingFor(
                plainTextBox({ backgroundColors: ["#ffff00"] }),
            ),
        ).toBe(true);
    });

    it("is false for a plain text box whose background is explicitly transparent", () => {
        expect(
            comicalDrawsSomethingFor(
                plainTextBox({ backgroundColors: ["transparent"] }),
            ),
        ).toBe(false);
    });

    it("is true for a plain text box with an outer border colour", () => {
        expect(
            comicalDrawsSomethingFor(plainTextBox({ outerBorderColor: "red" })),
        ).toBe(true);
    });
});
