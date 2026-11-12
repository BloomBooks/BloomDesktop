import { afterEach, describe, expect, it } from "vitest";
import { placementOf } from "./overflowIndicators";

// placementOf needs real layout (offsets and client rectangles of a rotated element), so this
// runs in the "browser" vitest project, not jsdom.

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
});

describe("placementOf", () => {
    it("uses the box's own rectangle when it is not rotated", () => {
        const { page, box } = makePage(0);

        const placement = placementOf(box, page);

        expect(placement.rotation).toBeUndefined();
        expect(placement.rect.left).toBeCloseTo(112);
        expect(placement.rect.top).toBeCloseTo(72);
        expect(placement.rect.width).toBeCloseTo(50);
        expect(placement.rect.height).toBeCloseTo(30);
    });

    it("gives where a box in a rotated canvas element would be upright, and the rotation", () => {
        const { page, box } = makePage(90);
        // Sanity check: the browser's rectangle for the box is the turned one, 30 wide and 50
        // tall, so it can't be used as it is.
        expect(box.getBoundingClientRect().width).toBeCloseTo(30);
        expect(box.getBoundingClientRect().height).toBeCloseTo(50);

        const placement = placementOf(box, page);

        expect(placement.rect.left).toBeCloseTo(112);
        expect(placement.rect.top).toBeCloseTo(72);
        expect(placement.rect.width).toBeCloseTo(50);
        expect(placement.rect.height).toBeCloseTo(30);
        expect(placement.rotation!.degrees).toBe(90);
        expect(placement.rotation!.frame.left).toBeCloseTo(100);
        expect(placement.rotation!.frame.top).toBeCloseTo(50);
        expect(placement.rotation!.frame.width).toBeCloseTo(200);
        expect(placement.rotation!.frame.height).toBeCloseTo(100);
    });
});
