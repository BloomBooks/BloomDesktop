import { describe, expect, it } from "vitest";
import { getBoxMetrics } from "./flowFit";

// jsdom lays nothing out, so clientWidth and clientHeight are always 0. Give them the
// values a real box would report.
function makeBox(clientWidth: number, clientHeight: number): HTMLElement {
    const editable = document.createElement("div");
    Object.defineProperty(editable, "clientWidth", { value: clientWidth });
    Object.defineProperty(editable, "clientHeight", { value: clientHeight });
    document.body.appendChild(editable);
    return editable;
}

describe("getBoxMetrics", () => {
    it("takes the padding off the client box", () => {
        const editable = makeBox(300, 200);
        editable.style.padding = "5px 10px 7px 20px";

        const metrics = getBoxMetrics(editable);

        expect(metrics.width).toBe(270);
        expect(metrics.height).toBe(188);
    });

    it("never reports a width or height below one pixel", () => {
        const editable = makeBox(0, 0);
        editable.style.padding = "12px";

        const metrics = getBoxMetrics(editable);

        expect(metrics.width).toBe(1);
        expect(metrics.height).toBe(1);
    });

    it("reports the font size and family the box uses", () => {
        const editable = makeBox(100, 100);
        editable.style.fontSize = "20px";
        editable.style.fontFamily = "Andika";

        const metrics = getBoxMetrics(editable);

        expect(metrics.font).toContain("20px");
        expect(metrics.font).toContain("Andika");
    });

    it("uses an explicit line height", () => {
        const editable = makeBox(100, 100);
        editable.style.fontSize = "20px";
        editable.style.lineHeight = "26px";

        expect(getBoxMetrics(editable).lineHeight).toBe(26);
    });

    it("falls back to 1.2 times the font size when the line height is normal", () => {
        const editable = makeBox(100, 100);
        editable.style.fontSize = "20px";
        editable.style.lineHeight = "normal";

        expect(getBoxMetrics(editable).lineHeight).toBeCloseTo(24);
    });
});
