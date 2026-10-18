import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import $ from "jquery";
import {
    isPointOverRange,
    kWordNotDecodableHighlight,
    makeRangesForSpans,
    mapReaderText,
    theOneReaderHighlightManager,
    trimSpan,
} from "./readerHighlights";
import "./libSynphony/jquery.text-markup";
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

    // CKEditor's filling character (U+200B) sits in the box while the user types next to an inline
    // element. The reader tools no longer rewrite the box from CKEditor's getData(), which left it
    // out, so mapReaderText leaves it out of the text instead, and makeRangesForSpans still finds
    // the right characters in the box.
    describe("mapReaderText and CKEditor's filling character", () => {
        const kZeroWidthSpace = String.fromCharCode(0x200b);
        let box: HTMLDivElement;
        let boldText: Text;
        let typedText: Text;
        let trackedNode: Node | undefined;

        beforeEach(() => {
            document.body.innerHTML = "";
            // As after the user put the caret after a bold "big" and typed "ger ran".
            box = document.createElement("div");
            box.className = "bloom-editable";
            box.innerHTML = "<p><strong>big</strong></p>";
            boldText = box.querySelector("strong")!.firstChild as Text;
            typedText = document.createTextNode(`${kZeroWidthSpace}ger ran`);
            box.querySelector("p")!.appendChild(typedText);
            document.body.appendChild(box);
            trackedNode = typedText;
            // A stand-in for the box's CKEditor, which remembers the node of its filling character.
            (box as unknown as { bloomCkEditor: object }).bloomCkEditor = {
                editable: () => ({
                    getCustomData: (key: string) =>
                        key === "cke-fillingChar" && trackedNode
                            ? { $: trackedNode }
                            : undefined,
                    removeCustomData: () => undefined,
                }),
            };
            // sanity check: the character really is in the box
            expect(box.textContent).toBe(`big${kZeroWidthSpace}ger ran`);
        });

        it("leaves the tracked filling character out of the text, so it does not split a word", () => {
            const map = mapReaderText(box);
            // The paragraph's end makes the newline.
            expect(map.text).toBe("bigger ran\n");
            expect($(box).getTotalWordCount()).toBe(2);
        });

        it("keeps a U+200B that CKEditor does not track, which may be a real word break", () => {
            trackedNode = undefined;
            const map = mapReaderText(box);
            expect(map.text).toBe(`big${kZeroWidthSpace}ger ran\n`);
            expect($(box).getTotalWordCount()).toBe(3);
        });

        it("makes Ranges over the right characters on either side of the filling character", () => {
            const map = mapReaderText(box);
            const [bigger, ran, big] = makeRangesForSpans(map, [
                { start: 0, end: 6 },
                { start: 7, end: 10 },
                { start: 0, end: 3 },
            ]);

            expect(bigger.startContainer).toBe(boldText);
            expect(bigger.startOffset).toBe(0);
            expect(bigger.endContainer).toBe(typedText);
            expect(bigger.endOffset).toBe(4);
            expect(bigger.toString()).toBe(`big${kZeroWidthSpace}ger`);

            expect(ran.toString()).toBe("ran");
            expect(ran.startContainer).toBe(typedText);

            // A span that ends just before the filling character does not run on over it.
            expect(big.endContainer).toBe(boldText);
            expect(big.toString()).toBe("big");
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
