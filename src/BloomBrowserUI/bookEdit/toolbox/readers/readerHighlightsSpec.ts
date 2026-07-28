import { describe, it, expect } from "vitest";
import { isPointOverRange, trimSpan } from "./readerHighlights";

describe("readerHighlights", () => {
    describe("isPointOverRange", () => {
        // jsdom has no layout, so a real Range reports no client rects. These stand-ins let us
        // test the hit test itself with known geometry.
        function rangeWithRects(
            rects: {
                left: number;
                top: number;
                right: number;
                bottom: number;
            }[],
        ): Range {
            return {
                getClientRects: () => rects,
            } as unknown as Range;
        }

        // A word on one line: x 100-140, y 50-70.
        const oneLine = rangeWithRects([
            { left: 100, top: 50, right: 140, bottom: 70 },
        ]);

        it("is true for a point inside the text", () => {
            expect(isPointOverRange(oneLine, 120, 60)).toBe(true);
        });

        it("is true on the edge of the text", () => {
            expect(isPointOverRange(oneLine, 100, 50)).toBe(true);
            expect(isPointOverRange(oneLine, 140, 70)).toBe(true);
        });

        // This is the case that used to show a tip wrongly: the mouse is in the white space
        // below the last paragraph, but caretPositionFromPoint() still reports a position at the
        // end of the highlighted line above it.
        it("is false for a point below the text", () => {
            expect(isPointOverRange(oneLine, 120, 200)).toBe(false);
        });

        it("is false for a point beside or above the text", () => {
            expect(isPointOverRange(oneLine, 300, 60)).toBe(false);
            expect(isPointOverRange(oneLine, 120, 10)).toBe(false);
        });

        it("checks every line of a range that wraps", () => {
            // A sentence wrapping onto a second line gets a rect per line, and the gap to the
            // right of the shorter second line is not over the text.
            const twoLines = rangeWithRects([
                { left: 100, top: 50, right: 300, bottom: 70 },
                { left: 100, top: 70, right: 180, bottom: 90 },
            ]);
            expect(isPointOverRange(twoLines, 250, 60)).toBe(true);
            expect(isPointOverRange(twoLines, 150, 80)).toBe(true);
            expect(isPointOverRange(twoLines, 250, 80)).toBe(false);
        });

        it("is false when the range occupies nothing", () => {
            expect(isPointOverRange(rangeWithRects([]), 120, 60)).toBe(false);
        });
    });

    describe("trimSpan", () => {
        it("leaves a span that is already tight alone", () => {
            expect(trimSpan("A cat sat.", { start: 2, end: 5 })).toEqual({
                start: 2,
                end: 5,
            });
        });

        it("excludes the space a sentence fragment carries with it", () => {
            const text = "One. Two.  ";
            expect(trimSpan(text, { start: 4, end: text.length })).toEqual({
                start: 5,
                end: 9,
            });
        });

        it("collapses a span that is all whitespace", () => {
            const span = trimSpan("a   b", { start: 1, end: 4 });
            expect(span.end).toBe(span.start);
        });
    });
});
