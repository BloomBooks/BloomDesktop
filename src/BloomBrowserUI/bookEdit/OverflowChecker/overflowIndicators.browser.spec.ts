import { afterEach, describe, expect, it } from "vitest";
import {
    countLines,
    describeLinesPastEdge,
    countLinesPastEdge,
} from "./overflowIndicators";

// countLinesPastEdge needs real layout (the client rects of words), so this runs in the
// "browser" vitest project, not jsdom.

const kLineHeight = 20;

// Makes a box of one-line paragraphs, each with the given text (empty for an empty paragraph),
// laid out kLineHeight apart from the top of the box.
function makeBox(paragraphs: string[]): HTMLElement {
    const box = document.createElement("div");
    box.id = "overflowIndicatorsTestBox";
    box.style.cssText = `position: absolute; top: 0; left: 0; width: 400px; font: 14px Arial; line-height: ${kLineHeight}px; margin: 0; padding: 0;`;
    box.innerHTML = paragraphs
        .map(
            (text) =>
                `<p style="margin: 0; min-height: ${kLineHeight}px">${text}</p>`,
        )
        .join("");
    document.body.appendChild(box);
    return box;
}

afterEach(() => {
    document.getElementById("overflowIndicatorsTestBox")?.remove();
});

describe("countLinesPastEdge", () => {
    it("counts the lines below the edge", () => {
        const box = makeBox(["one two", "three", "four five six", "seven"]);
        const top = box.getBoundingClientRect().top;
        // Sanity check: each paragraph is one line.
        expect(box.getBoundingClientRect().height).toBe(4 * kLineHeight);

        const lines = countLinesPastEdge(box, top + 2 * kLineHeight);

        expect(lines).toBe(2);
    });

    it("counts a line the edge cuts through on the side where most of it is", () => {
        const box = makeBox(["one", "two", "three"]);
        const top = box.getBoundingClientRect().top;

        // The edge is a quarter of the way into the second line, so most of it is below.
        expect(countLinesPastEdge(box, top + 1.25 * kLineHeight)).toBe(2);
        // The edge is three quarters of the way into it, so most of it is above.
        expect(countLinesPastEdge(box, top + 1.75 * kLineHeight)).toBe(1);
    });

    it("counts the blank lines that line breaks make in a paragraph with text", () => {
        const box = makeBox(["Title<br><br><br>"]);
        const top = box.getBoundingClientRect().top;
        // Sanity check: the trailing break adds no line, so the paragraph is three lines.
        expect(box.getBoundingClientRect().height).toBe(3 * kLineHeight);

        expect(countLinesPastEdge(box, top + kLineHeight)).toBe(2);
        // The break that ends the line with the title is on that line, not a line of its own.
        expect(countLinesPastEdge(box, top)).toBe(3);
    });

    it("counts empty paragraphs below the edge as lines", () => {
        const box = makeBox(["one", "", ""]);
        const top = box.getBoundingClientRect().top;

        const lines = countLinesPastEdge(box, top + kLineHeight);

        expect(lines).toBe(2);
    });

    it("adds back how far the box is scrolled", () => {
        const box = makeBox(["one", "two", "three", "four"]);
        const top = box.getBoundingClientRect().top;
        // Content scrolled up by one line is measured where it would be unscrolled, one line
        // lower, so one more line is past the edge.
        const edge = top + 2 * kLineHeight;

        expect(countLinesPastEdge(box, edge)).toBe(2);
        expect(countLinesPastEdge(box, edge, kLineHeight)).toBe(3);
    });

    it("finds nothing when all the text is above the edge", () => {
        const box = makeBox(["one", "two"]);
        const bottom = box.getBoundingClientRect().bottom;

        const lines = countLinesPastEdge(box, bottom);

        expect(lines).toBe(0);
    });
});

describe("countLines", () => {
    const rect = (top: number, height = 20) => ({
        top,
        bottom: top + height,
        height,
    });

    it("treats rects whose middles are close as one line", () => {
        expect(
            countLines([rect(0), rect(2), rect(20), rect(21), rect(40)]),
        ).toBe(3);
    });

    it("does not depend on the order of the rects", () => {
        expect(countLines([rect(40), rect(0), rect(20), rect(1)])).toBe(3);
    });

    it("counts no lines for no rects", () => {
        expect(countLines([])).toBe(0);
    });
});

describe("describeLinesPastEdge", () => {
    it("says how many lines do not fit", () => {
        expect(describeLinesPastEdge(3)).toBe("+3 lines");
        expect(describeLinesPastEdge(1)).toBe("+1 line");
    });

    it("says one line when less than half a line is past the edge", () => {
        expect(describeLinesPastEdge(0)).toBe("+1 line");
    });
});
