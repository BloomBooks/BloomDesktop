import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import {
    isPointOverRange,
    kWordNotDecodableHighlight,
    theOneReaderHighlightManager,
    trimSpan,
} from "./readerHighlights";
import { installHighlightPolyfill } from "../../test/highlightTestSupport";

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

    // The tip needs a mousemove handler on the page, but there is nothing to hover over once the
    // highlights are gone, and an idle handler would do work (including forcing a layout) on
    // every mouse move for the rest of the page's life.
    describe("the hover handler's lifetime", () => {
        let added: string[];
        let removed: string[];
        let root: HTMLElement;

        beforeEach(() => {
            document.body.innerHTML = "";
            installHighlightPolyfill(window);
            root = document.createElement("div");
            document.body.appendChild(root);
            added = [];
            removed = [];
            vi.spyOn(document, "addEventListener").mockImplementation(
                (type: string) => {
                    added.push(type);
                },
            );
            vi.spyOn(document, "removeEventListener").mockImplementation(
                (type: string) => {
                    removed.push(type);
                },
            );
        });

        afterEach(() => {
            vi.restoreAllMocks();
            theOneReaderHighlightManager.clearAll(document.body);
        });

        // Something to hover over: one range under a layer that has a tip.
        function paintOneHighlight(): void {
            root.textContent = "Cat";
            const range = document.createRange();
            range.selectNodeContents(root);
            theOneReaderHighlightManager.beginPass();
            theOneReaderHighlightManager.addRanges(kWordNotDecodableHighlight, [
                range,
            ]);
            theOneReaderHighlightManager.endPass(root);
        }

        it("is attached while highlights are painted and detached when they are cleared", () => {
            paintOneHighlight();
            expect(added).toContain("mousemove");
            expect(removed).not.toContain("mousemove");

            // The user turns the reader tool off, or moves to a page with nothing to mark.
            theOneReaderHighlightManager.clearAll(root);
            expect(removed).toContain("mousemove");
        });

        it("is not attached at all by a pass that finds no violations", () => {
            theOneReaderHighlightManager.beginPass();
            theOneReaderHighlightManager.endPass(root);
            expect(added).not.toContain("mousemove");
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
