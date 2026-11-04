import { describe, expect, test, vi } from "vitest";

// comicaljs draws bubbles on a canvas, which jsdom lacks. The resize code only reads
// bubble specs and writes tails back, so a bubble with no tails is enough.
vi.mock("comicaljs", () => ({
    Bubble: class {
        static getBubbleSpec() {
            return { spec: "none", tails: [] };
        }
        mergeWithNewBubbleProps() {
            // the tests don't look at bubble props
        }
        isTransparent() {
            return true;
        }
    },
    Comical: {
        setSelectorForBubblesWhichTailMidpointMayOverlap: () => {},
        activateElement: () => {},
        update: vi.fn(),
    },
}));
vi.mock("../bloomImages", async (importOriginal) => ({
    ...(await importOriginal<typeof import("../bloomImages")>()),
    getImageFromCanvasElement: (element: HTMLElement) =>
        element.getElementsByTagName("img")[0],
    isPlaceHolderImage: () => false,
}));
vi.mock("./CanvasElementAlternates", () => ({
    adjustCanvasElementAlternates: vi.fn(),
}));

// This import deliberately comes after the vi.mock calls above, so that the module graph
// it pulls in gets the stubbed modules.
import { adjustCanvasElementChildrenIfSizeChanged } from "./CanvasElementResizeAdjustments";

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
    test("does not refit a nested bloom-canvas's background image against the outer bloom-canvas", () => {
        // The outer canvas has no background image of its own; its only child is a
        // canvas element that holds another bloom-canvas, which does have one.
        // jsdom reports a clientWidth/clientHeight of 0, so any recorded size other
        // than "0,0" counts as a size change.
        document.body.innerHTML = `
            <div class="bloom-canvas" id="outer" data-imgsizebasedon="400,300">
                <div class="bloom-canvas-element" style="left: 10px; top: 10px; width: 100px; height: 100px">
                    <div class="bloom-canvas" id="inner">
                        <div class="bloom-canvas-element bloom-backgroundImage" style="left: 0px; top: 0px">
                            <div class="bloom-imageContainer">
                                <img src="rabbit.png" />
                            </div>
                        </div>
                    </div>
                </div>
            </div>`;
        const outer = document.getElementById("outer") as HTMLElement;
        const innerBackground = document.querySelector(
            "#inner .bloom-backgroundImage",
        ) as HTMLElement;
        expect(innerBackground).not.toBeNull();
        const adjustBackgroundImageSize = vi.fn();

        adjustCanvasElementChildrenIfSizeChanged(
            outer,
            adjustBackgroundImageSize,
        );

        // Sanity check that the resize actually ran, rather than stopping at the
        // "size unchanged" check.
        expect(outer.getAttribute("data-imgsizebasedon")).toBe("0,0");
        expect(adjustBackgroundImageSize).not.toHaveBeenCalled();
    });

    test("refits the bloom-canvas's own background image after a resize", () => {
        document.body.innerHTML = `
            <div class="bloom-canvas" id="outer" data-imgsizebasedon="400,300">
                <div class="bloom-canvas-element bloom-backgroundImage" style="left: 0px; top: 0px">
                    <div class="bloom-imageContainer">
                        <img src="rabbit.png" />
                    </div>
                </div>
                <div class="bloom-canvas-element" style="left: 10px; top: 10px; width: 100px; height: 100px"></div>
            </div>`;
        const outer = document.getElementById("outer") as HTMLElement;
        const ownBackground = document.querySelector(
            ".bloom-backgroundImage",
        ) as HTMLElement;
        const adjustBackgroundImageSize = vi.fn();

        adjustCanvasElementChildrenIfSizeChanged(
            outer,
            adjustBackgroundImageSize,
        );

        expect(outer.getAttribute("data-imgsizebasedon")).toBe("0,0");
        expect(adjustBackgroundImageSize).toHaveBeenLastCalledWith(
            outer,
            ownBackground,
            false,
        );
    });

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
