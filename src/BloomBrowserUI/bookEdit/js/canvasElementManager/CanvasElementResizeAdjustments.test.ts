import { describe, it, expect } from "vitest";
import { BubbleSpec } from "comicaljs";
import { comicalDrawsSomethingFor } from "./CanvasElementResizeAdjustments";

// A plain text box: style "none", no tails, nothing chosen for its colours.
function plainTextBox(extra: Partial<BubbleSpec> = {}): BubbleSpec {
    return { version: "1.0", style: "none", tails: [], level: 1, ...extra };
}

describe("comicalDrawsSomethingFor", () => {
    it("is false for a plain text box, which Comical draws nothing for", () => {
        expect(comicalDrawsSomethingFor(plainTextBox())).toBe(false);
    });

    it("is true for any bubble style", () => {
        expect(
            comicalDrawsSomethingFor(plainTextBox({ style: "speech" })),
        ).toBe(true);
        expect(
            comicalDrawsSomethingFor(plainTextBox({ style: "caption" })),
        ).toBe(true);
    });

    it("is true for a plain text box with a tail", () => {
        const tail = { tipX: 1, tipY: 2, midpointX: 3, midpointY: 4 };
        expect(comicalDrawsSomethingFor(plainTextBox({ tails: [tail] }))).toBe(
            true,
        );
    });

    it("is true for a plain text box with a background colour, which Comical paints in a box", () => {
        expect(
            comicalDrawsSomethingFor(
                plainTextBox({ backgroundColors: ["#ffff00"] }),
            ),
        ).toBe(true);
    });

    it("is false for a plain text box whose background is explicitly transparent", () => {
        expect(
            comicalDrawsSomethingFor(
                plainTextBox({ backgroundColors: ["transparent"] }),
            ),
        ).toBe(false);
    });

    it("is true for a plain text box with an outer border colour", () => {
        expect(
            comicalDrawsSomethingFor(plainTextBox({ outerBorderColor: "red" })),
        ).toBe(true);
    });
});
