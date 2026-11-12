import { describe, expect, it } from "vitest";
import { cloneCanvasElementHtmlStructure } from "./canvasElementCloneCleanup";
import { kScrollTopVariable } from "../scrollingLayouts";

describe("cloneCanvasElementHtmlStructure", () => {
    it("does not copy how far a text box is scrolled, since the copy starts unscrolled", () => {
        const canvasElement = document.createElement("div");
        canvasElement.innerHTML = `<div class="bloom-editable" style="color: red;"><p>Text</p></div>`;
        const box =
            canvasElement.querySelector<HTMLElement>(".bloom-editable")!;
        box.style.setProperty(kScrollTopVariable, "44px");
        expect(box.style.getPropertyValue(kScrollTopVariable)).toBe("44px");

        const copy = document.createElement("div");
        copy.innerHTML = cloneCanvasElementHtmlStructure(canvasElement);

        const copiedBox = copy.querySelector<HTMLElement>(".bloom-editable")!;
        expect(copiedBox.style.getPropertyValue(kScrollTopVariable)).toBe("");
        expect(copiedBox.style.color).toBe("red");
        // The original keeps its own scroll offset.
        expect(box.style.getPropertyValue(kScrollTopVariable)).toBe("44px");
    });

    it("leaves no empty style attribute when the scroll offset was the only style", () => {
        const canvasElement = document.createElement("div");
        canvasElement.innerHTML = `<div class="bloom-editable"><p>Text</p></div>`;
        canvasElement
            .querySelector<HTMLElement>(".bloom-editable")!
            .style.setProperty(kScrollTopVariable, "10px");

        const copy = document.createElement("div");
        copy.innerHTML = cloneCanvasElementHtmlStructure(canvasElement);

        expect(
            copy.querySelector(".bloom-editable")!.hasAttribute("style"),
        ).toBe(false);
    });
});
