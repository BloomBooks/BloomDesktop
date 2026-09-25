import { beforeEach, describe, expect, test, vi } from "vitest";

// Comical wants paper.js and a real <canvas>, which jsdom doesn't give us. Nothing in the
// code under test needs it to do anything.
vi.mock("comicaljs", () => ({
    Bubble: class {},
    Comical: {
        setSelectorForBubblesWhichTailMidpointMayOverlap: () => {},
        activateElement: () => {},
        update: () => {},
    },
}));

// jsdom gives every element a zero bounding rectangle, so the real getExactClientSize
// could only ever report the zero-area case. We control the reported size instead, so
// the test can also show what happens when the bloom-canvas does have a size.
const reportedSize = { width: 0, height: 0 };
vi.mock("../../../utils/elementUtils", async (importOriginal) => {
    const actual =
        await importOriginal<typeof import("../../../utils/elementUtils")>();
    return {
        ...actual,
        getExactClientSize: () => ({
            width: reportedSize.width,
            height: reportedSize.height,
        }),
    };
});

// These imports deliberately come after the vi.mock calls above, so that the module graph
// they pull in gets the stubbed modules.
import {
    adjustBackgroundImageSize,
    getBackgroundCanvasElement,
} from "./CanvasElementBackgroundImageManager";
import type { BackgroundImageManagerState } from "./CanvasElementBackgroundImageManager";

// The inline styles a background image already has when something asks for a refit.
const initialCanvasElementStyle = {
    width: "300px",
    height: "200px",
    left: "10px",
    top: "20px",
};

function setUpBackgroundImage(): {
    bloomCanvas: HTMLElement;
    bgCanvasElement: HTMLElement;
    img: HTMLImageElement;
} {
    document.body.innerHTML = `
        <div class="bloom-page">
            <div class="bloom-canvas">
                <div class="bloom-canvas-element bloom-backgroundImage">
                    <div class="bloom-imageContainer">
                        <img src="rabbit.png" class="bloom-imageLoadError" />
                    </div>
                </div>
            </div>
        </div>`;
    const bloomCanvas = document.querySelector(".bloom-canvas") as HTMLElement;
    const bgCanvasElement = document.querySelector(
        ".bloom-backgroundImage",
    ) as HTMLElement;
    bgCanvasElement.style.width = initialCanvasElementStyle.width;
    bgCanvasElement.style.height = initialCanvasElementStyle.height;
    bgCanvasElement.style.left = initialCanvasElementStyle.left;
    bgCanvasElement.style.top = initialCanvasElementStyle.top;
    return {
        bloomCanvas,
        bgCanvasElement,
        img: bgCanvasElement.getElementsByTagName("img")[0],
    };
}

function makeState(): BackgroundImageManagerState {
    return { bgImageLoadListeners: new WeakMap() };
}

function refit(
    bloomCanvas: HTMLElement,
    bgCanvasElement: HTMLElement,
): Promise<void> {
    return adjustBackgroundImageSize(
        makeState(),
        bloomCanvas,
        bgCanvasElement,
        false,
        () => undefined, // nothing is the active element, so no controls get rendered
        () => {},
    );
}

describe("adjustBackgroundImageSize", () => {
    beforeEach(() => {
        reportedSize.width = 0;
        reportedSize.height = 0;
    });

    test("leaves the background image alone when the bloom-canvas has no area", async () => {
        const { bloomCanvas, bgCanvasElement, img } = setUpBackgroundImage();
        // Sanity check: the styles we expect to survive the call are there to start with.
        expect(bgCanvasElement.style.width).toBe(
            initialCanvasElementStyle.width,
        );
        expect(bgCanvasElement.style.left).toBe(initialCanvasElementStyle.left);
        expect(img.style.width).toBe("");

        await refit(bloomCanvas, bgCanvasElement);

        expect(bgCanvasElement.style.width).toBe(
            initialCanvasElementStyle.width,
        );
        expect(bgCanvasElement.style.height).toBe(
            initialCanvasElementStyle.height,
        );
        expect(bgCanvasElement.style.left).toBe(initialCanvasElementStyle.left);
        expect(bgCanvasElement.style.top).toBe(initialCanvasElementStyle.top);
        // and it did not start cropping the image either
        expect(img.style.width).toBe("");
    });

    test("leaves the background image alone when the bloom-canvas has width but no height", async () => {
        const { bloomCanvas, bgCanvasElement } = setUpBackgroundImage();
        reportedSize.width = 400;
        reportedSize.height = 0;

        await refit(bloomCanvas, bgCanvasElement);

        expect(bgCanvasElement.style.width).toBe(
            initialCanvasElementStyle.width,
        );
        expect(bgCanvasElement.style.height).toBe(
            initialCanvasElementStyle.height,
        );
    });

    // A hidden bloom-canvas that has a border reports a negative size, because
    // getExactClientSize subtracts the border from a zero bounding rectangle.
    test("leaves the background image alone when the bloom-canvas reports a negative size", async () => {
        const { bloomCanvas, bgCanvasElement } = setUpBackgroundImage();
        reportedSize.width = -2;
        reportedSize.height = -2;

        await refit(bloomCanvas, bgCanvasElement);

        expect(bgCanvasElement.style.width).toBe(
            initialCanvasElementStyle.width,
        );
        expect(bgCanvasElement.style.height).toBe(
            initialCanvasElementStyle.height,
        );
    });

    // This is the guard against the tests above passing for the wrong reason: given a
    // bloom-canvas that does have a size, the same call really does resize the background
    // image. (The image here has failed to load, which is the one case the code can size
    // synchronously, since it then fills the container to show the error message.)
    test("fits the background image to a bloom-canvas that has a size", async () => {
        const { bloomCanvas, bgCanvasElement } = setUpBackgroundImage();
        reportedSize.width = 400;
        reportedSize.height = 500;

        await refit(bloomCanvas, bgCanvasElement);

        expect(bgCanvasElement.style.width).toBe("400px");
        expect(bgCanvasElement.style.height).toBe("500px");
        expect(bgCanvasElement.style.left).toBe("0px");
        expect(bgCanvasElement.style.top).toBe("0px");
    });
});

describe("getBackgroundCanvasElement", () => {
    test("finds the background image that is a direct child of the bloom-canvas", () => {
        const { bloomCanvas, bgCanvasElement } = setUpBackgroundImage();
        expect(getBackgroundCanvasElement(bloomCanvas)).toBe(bgCanvasElement);
    });

    test("returns undefined when the bloom-canvas has no background image", () => {
        document.body.innerHTML = `
            <div class="bloom-canvas">
                <div class="bloom-canvas-element"><div class="bloom-imageContainer"><img src="a.png"/></div></div>
            </div>`;
        const bloomCanvas = document.querySelector(
            ".bloom-canvas",
        ) as HTMLElement;
        expect(getBackgroundCanvasElement(bloomCanvas)).toBeUndefined();
    });

    test("does not take a nested bloom-canvas's background image for the outer one", () => {
        document.body.innerHTML = `
            <div class="bloom-canvas" id="outer">
                <div class="bloom-canvas-element">
                    <div class="bloom-canvas" id="inner">
                        <div class="bloom-canvas-element bloom-backgroundImage" id="innerBg">
                            <div class="bloom-imageContainer"><img src="inner.png"/></div>
                        </div>
                    </div>
                </div>
            </div>`;
        const outer = document.getElementById("outer") as HTMLElement;
        const inner = document.getElementById("inner") as HTMLElement;
        const innerBg = document.getElementById("innerBg") as HTMLElement;
        // Sanity check: a plain descendant search would have found the inner one.
        expect(outer.getElementsByClassName("bloom-backgroundImage")[0]).toBe(
            innerBg,
        );

        expect(getBackgroundCanvasElement(outer)).toBeUndefined();
        expect(getBackgroundCanvasElement(inner)).toBe(innerBg);
    });

    test("finds each bloom-canvas's own background image when both have one", () => {
        document.body.innerHTML = `
            <div class="bloom-canvas" id="outer">
                <div class="bloom-canvas-element bloom-backgroundImage" id="outerBg">
                    <div class="bloom-imageContainer"><img src="outer.png"/></div>
                </div>
                <div class="bloom-canvas-element">
                    <div class="bloom-canvas" id="inner">
                        <div class="bloom-canvas-element bloom-backgroundImage" id="innerBg">
                            <div class="bloom-imageContainer"><img src="inner.png"/></div>
                        </div>
                    </div>
                </div>
            </div>`;
        const outer = document.getElementById("outer") as HTMLElement;
        const inner = document.getElementById("inner") as HTMLElement;
        expect(getBackgroundCanvasElement(outer)).toBe(
            document.getElementById("outerBg"),
        );
        expect(getBackgroundCanvasElement(inner)).toBe(
            document.getElementById("innerBg"),
        );
    });

    // The Image Description tool wraps the background image in a bloom-describedImage
    // while it is active, so the background image is not a direct child then.
    test("still finds the background image when the Image Description tool has wrapped it", () => {
        document.body.innerHTML = `
            <div class="bloom-canvas">
                <div class="bloom-describedImage">
                    <div class="bloom-canvas-element bloom-backgroundImage" id="bg">
                        <div class="bloom-imageContainer"><img src="a.png"/></div>
                    </div>
                </div>
            </div>`;
        const bloomCanvas = document.querySelector(
            ".bloom-canvas",
        ) as HTMLElement;
        expect(getBackgroundCanvasElement(bloomCanvas)).toBe(
            document.getElementById("bg"),
        );
    });
});
