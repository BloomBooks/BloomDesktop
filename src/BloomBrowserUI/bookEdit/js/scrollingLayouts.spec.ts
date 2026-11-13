import { describe, expect, it } from "vitest";
import { kScrollTopVariable, removeScrollOffsets } from "./scrollingLayouts";

describe("removeScrollOffsets", () => {
    it("removes how far a box was scrolled, keeping its other inline styles", () => {
        const root = document.createElement("div");
        root.innerHTML = `<div class="bloom-editable" style="color: red;"><p>Text</p></div>`;
        const box = root.querySelector<HTMLElement>(".bloom-editable")!;
        box.style.setProperty(kScrollTopVariable, "44px");
        expect(box.style.getPropertyValue(kScrollTopVariable)).toBe("44px");

        removeScrollOffsets(root);

        expect(box.style.getPropertyValue(kScrollTopVariable)).toBe("");
        expect(box.style.color).toBe("red");
    });

    it("leaves no empty style attribute when the scroll offset was the only style", () => {
        const root = document.createElement("div");
        root.innerHTML = `<div class="bloom-editable"><p>Text</p></div>`;
        const box = root.querySelector<HTMLElement>(".bloom-editable")!;
        box.style.setProperty(kScrollTopVariable, "10px");

        removeScrollOffsets(root);

        expect(box.hasAttribute("style")).toBe(false);
    });
});
