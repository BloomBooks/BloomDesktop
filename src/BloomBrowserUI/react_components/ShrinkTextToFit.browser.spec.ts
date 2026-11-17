import { afterEach, describe, it, expect } from "vitest";
import { fitTextToWidth } from "./ShrinkTextToFit";

// These need real layout (scrollWidth, clientWidth), so they run in the "browser"
// vitest project, not jsdom.

let host: HTMLDivElement | undefined;

// Makes a block of the given width and inherited font size holding the given text. The
// font size is on the parent because fitTextToWidth owns the block's own inline font size.
function makeBlock(widthPx: number, fontSizePx: number, text: string) {
    host = document.createElement("div");
    host.style.font = `${fontSizePx}px Arial`;
    host.innerHTML = `<div style="width:${widthPx}px; overflow: hidden"><span>${text}</span></div>`;
    document.body.appendChild(host);
    return host.firstElementChild as HTMLElement;
}

afterEach(() => {
    host?.remove();
    host = undefined;
});

describe("fitTextToWidth", () => {
    it("leaves text that fits at its stylesheet size", () => {
        const block = makeBlock(200, 16, "Short");
        fitTextToWidth(block, 8);
        expect(block.style.fontSize).toBe("");
        expect(getComputedStyle(block).fontSize).toBe("16px");
    });

    it("lets words wrap rather than shrinking them", () => {
        const block = makeBlock(60, 16, "one two three four five");
        expect(block.scrollWidth).toBeLessThanOrEqual(block.clientWidth);
        fitTextToWidth(block, 8);
        expect(getComputedStyle(block).fontSize).toBe("16px");
    });

    it("shrinks a word too long for the width until it fits", () => {
        const block = makeBlock(60, 16, "Supercalifragilistic");
        expect(block.scrollWidth).toBeGreaterThan(block.clientWidth);
        fitTextToWidth(block, 4);
        const size = parseFloat(getComputedStyle(block).fontSize);
        expect(size).toBeLessThan(16);
        expect(size).toBeGreaterThanOrEqual(4);
        expect(block.scrollWidth).toBeLessThanOrEqual(block.clientWidth);
    });

    it("does not shrink below the minimum", () => {
        const block = makeBlock(20, 16, "Supercalifragilistic");
        fitTextToWidth(block, 10);
        expect(getComputedStyle(block).fontSize).toBe("10px");
        expect(block.scrollWidth).toBeGreaterThan(block.clientWidth);
    });

    it("breaks words that are still too wide at the minimum, when asked to", () => {
        const block = makeBlock(20, 16, "Supercalifragilistic");
        fitTextToWidth(block, 10, true);
        expect(getComputedStyle(block).fontSize).toBe("10px");
        expect(block.style.overflowWrap).toBe("anywhere");
        expect(block.scrollWidth).toBeLessThanOrEqual(block.clientWidth);
    });

    it("does not break words that fit once shrunk, even when allowed to", () => {
        const block = makeBlock(60, 16, "Supercalifragilistic");
        fitTextToWidth(block, 4, true);
        expect(parseFloat(getComputedStyle(block).fontSize)).toBeLessThan(16);
        expect(block.style.overflowWrap).toBe("");
        expect(block.scrollWidth).toBeLessThanOrEqual(block.clientWidth);
    });

    it("stops breaking words when given room again", () => {
        const block = makeBlock(20, 16, "Supercalifragilistic");
        fitTextToWidth(block, 10, true);
        expect(block.style.overflowWrap).toBe("anywhere");
        block.style.width = "400px";
        fitTextToWidth(block, 10, true);
        expect(block.style.overflowWrap).toBe("");
        expect(getComputedStyle(block).fontSize).toBe("16px");
    });

    it("grows back to the stylesheet size when given room", () => {
        const block = makeBlock(60, 16, "Supercalifragilistic");
        fitTextToWidth(block, 4);
        expect(parseFloat(getComputedStyle(block).fontSize)).toBeLessThan(16);
        block.style.width = "400px";
        fitTextToWidth(block, 4);
        expect(getComputedStyle(block).fontSize).toBe("16px");
    });
});
