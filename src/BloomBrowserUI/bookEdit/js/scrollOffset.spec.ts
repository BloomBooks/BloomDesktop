import { describe, expect, it } from "vitest";
import {
    kScrollTopVariable,
    removeScrollOffsets,
    trackScrollOffsets,
} from "./scrollOffset";

describe("trackScrollOffsets", () => {
    // Scrolls the box to `scrollTop`; jsdom does no layout, so the value is set directly.
    function scroll(box: HTMLElement, scrollTop: number) {
        Object.defineProperty(box, "scrollTop", {
            value: scrollTop,
            configurable: true,
        });
        box.dispatchEvent(new Event("scroll"));
    }

    it("records how far a text box is scrolled, and removes it when the box is back at the top", () => {
        const box = document.createElement("div");
        box.className = "bloom-editable";
        document.body.appendChild(box);
        trackScrollOffsets(document);

        scroll(box, 44);
        expect(box.style.getPropertyValue(kScrollTopVariable)).toBe("44px");

        scroll(box, 0);
        expect(box.style.getPropertyValue(kScrollTopVariable)).toBe("");
        box.remove();
    });

    it("corrects a copy of a scrolled box when the copy is added to the page", async () => {
        trackScrollOffsets(document);
        // A copy made with cloneNode or innerHTML carries the original's offset but is not
        // scrolled.
        const container = document.createElement("div");
        container.innerHTML = `<div class="bloom-canvas-element"><div class="bloom-editable" style="${kScrollTopVariable}: 44px; color: red;"></div></div>`;
        const copy = container.querySelector<HTMLElement>(".bloom-editable")!;
        expect(copy.style.getPropertyValue(kScrollTopVariable)).toBe("44px");

        document.body.appendChild(container);
        // MutationObserver callbacks run as a microtask after the change.
        await Promise.resolve();

        expect(copy.style.getPropertyValue(kScrollTopVariable)).toBe("");
        expect(copy.style.color).toBe("red");
        container.remove();
    });
});

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
