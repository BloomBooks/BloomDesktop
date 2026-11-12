import { afterEach, describe, expect, it } from "vitest";
import {
    countLines,
    describeTextPastEdge,
    measureTextPastEdge,
} from "./overflowIndicators";

// measureTextPastEdge needs real layout (the client rects of words), so this runs in the
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

describe("measureTextPastEdge", () => {
    it("counts the lines and words below the edge", () => {
        const box = makeBox(["one two", "three", "four five six", "seven"]);
        const top = box.getBoundingClientRect().top;
        // Sanity check: each paragraph is one line.
        expect(box.getBoundingClientRect().height).toBe(4 * kLineHeight);

        const m = measureTextPastEdge(box, top + 2 * kLineHeight);

        expect(m.lineCount).toBe(2);
        expect(m.wordCount).toBe(4); // four five six seven
    });

    it("counts a line the edge cuts through on the side where most of it is", () => {
        const box = makeBox(["one", "two", "three"]);
        const top = box.getBoundingClientRect().top;

        // The edge is a quarter of the way into the second line, so most of it is below.
        expect(
            measureTextPastEdge(box, top + 1.25 * kLineHeight).lineCount,
        ).toBe(2);
        // The edge is three quarters of the way into it, so most of it is above.
        expect(
            measureTextPastEdge(box, top + 1.75 * kLineHeight).lineCount,
        ).toBe(1);
    });

    it("counts empty paragraphs below the edge as lines with no words", () => {
        const box = makeBox(["one", "", ""]);
        const top = box.getBoundingClientRect().top;

        const m = measureTextPastEdge(box, top + kLineHeight);

        expect(m.lineCount).toBe(2);
        expect(m.wordCount).toBe(0);
    });

    it("adds back how far the box is scrolled", () => {
        const box = makeBox(["one", "two", "three", "four"]);
        const top = box.getBoundingClientRect().top;
        // Content scrolled up by one line is measured where it would be unscrolled, one line
        // lower, so one more line is past the edge.
        const edge = top + 2 * kLineHeight;

        expect(measureTextPastEdge(box, edge).lineCount).toBe(2);
        expect(measureTextPastEdge(box, edge, kLineHeight).lineCount).toBe(3);
    });

    it("finds nothing when all the text is above the edge", () => {
        const box = makeBox(["one", "two"]);
        const bottom = box.getBoundingClientRect().bottom;

        const m = measureTextPastEdge(box, bottom);

        expect(m.lineCount).toBe(0);
        expect(m.wordCount).toBe(0);
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

describe("describeTextPastEdge", () => {
    it("says how many lines do not fit", () => {
        expect(describeTextPastEdge({ lineCount: 3, wordCount: 9 })).toBe(
            "+3 lines",
        );
        expect(describeTextPastEdge({ lineCount: 1, wordCount: 2 })).toBe(
            "+1 line",
        );
    });

    it("says one line when less than half a line is past the edge", () => {
        expect(describeTextPastEdge({ lineCount: 0, wordCount: 0 })).toBe(
            "+1 line",
        );
    });
});
