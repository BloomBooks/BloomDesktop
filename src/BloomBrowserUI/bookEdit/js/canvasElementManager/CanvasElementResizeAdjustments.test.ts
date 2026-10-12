import { describe, expect, test, vi } from "vitest";

// Comical wants paper.js and a real <canvas>, which jsdom doesn't give us. The resize code
// only reads bubble specs and writes tails back, so a bubble with no tails is enough.
vi.mock("comicaljs", () => ({
    Bubble: class {
        static getBubbleSpec() {
            return { tails: [], spec: "none" };
        }
        mergeWithNewBubbleProps() {
            // the tests don't look at bubble props
        }
    },
    Comical: {
        setSelectorForBubblesWhichTailMidpointMayOverlap: () => {},
        activateElement: () => {},
        update: () => {},
    },
}));

// This import deliberately comes after the vi.mock call above, so that the module graph
// it pulls in gets the stubbed module.
import { adjustCanvasElementChildrenIfSizeChanged } from "./CanvasElementResizeAdjustments";

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
});
