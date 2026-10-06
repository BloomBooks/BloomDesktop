import { afterEach, describe, expect, it } from "vitest";
import { adjustMoveCropHandleVisibility } from "./CanvasElementSelectionUi";

const kShowMoveCropHandleClass =
    "bloom-ui-canvas-element-show-move-crop-handle";

// jsdom does no layout, so give an element the sizes a browser would report for it.
function setLaidOutSize(
    element: HTMLElement,
    width: number,
    height: number,
): void {
    Object.defineProperty(element, "clientWidth", { value: width });
    Object.defineProperty(element, "clientHeight", { value: height });
    Object.defineProperty(element, "offsetWidth", { value: width });
    Object.defineProperty(element, "offsetHeight", { value: height });
}

// A page background element 300 wide and 400 tall holding a landscape picture 400 by 300 that
// Rotate Right has rotated 90 degrees, and the control frame that shows the move-crop handle.
function makeRotatedBackground(): {
    controlFrame: HTMLElement;
    element: HTMLElement;
    img: HTMLImageElement;
} {
    document.body.innerHTML = `
        <div id="canvas-element-control-frame"></div>
        <div class="bloom-canvas-element bloom-backgroundImage">
            <div class="bloom-imageContainer">
                <img src="picture.png" style="width: 400px; left: -50px; top: 50px; transform: rotate(90deg)">
            </div>
        </div>`;
    const controlFrame = document.getElementById(
        "canvas-element-control-frame",
    )!;
    const element = document.querySelector(
        ".bloom-canvas-element",
    ) as HTMLElement;
    const img = document.querySelector("img") as HTMLImageElement;
    setLaidOutSize(element, 300, 400);
    setLaidOutSize(img, 400, 300);
    return { controlFrame, element, img };
}

describe("adjustMoveCropHandleVisibility", () => {
    afterEach(() => {
        document.body.innerHTML = "";
    });

    it("shows no move-crop handle for an uncropped picture rotated 90 degrees", () => {
        const { controlFrame, element, img } = makeRotatedBackground();
        // Sanity check: the picture's own box is wider than its element, which is what a
        // comparison that ignores the rotation would take for a crop.
        expect(img.offsetWidth).toBeGreaterThan(element.clientWidth + 1);
        controlFrame.classList.add(kShowMoveCropHandleClass);

        adjustMoveCropHandleVisibility(element);

        expect(controlFrame.classList.contains(kShowMoveCropHandleClass)).toBe(
            false,
        );
    });

    it("keeps the layout of an uncropped picture rotated 90 degrees when told to remove unneeded crop values", () => {
        const { element, img } = makeRotatedBackground();

        adjustMoveCropHandleVisibility(element, true);

        expect(img.style.width).toBe("400px");
        expect(img.style.left).toBe("-50px");
        expect(img.style.top).toBe("50px");
    });

    it("still shows the move-crop handle for a cropped picture that is not rotated", () => {
        const { controlFrame, element, img } = makeRotatedBackground();
        img.style.transform = "";
        // Not rotated, a box 400 wide in an element 300 wide is a crop.
        adjustMoveCropHandleVisibility(element);
        expect(controlFrame.classList.contains(kShowMoveCropHandleClass)).toBe(
            true,
        );
    });
});
