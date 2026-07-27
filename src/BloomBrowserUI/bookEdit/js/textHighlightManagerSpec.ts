import { describe, it, expect, beforeEach } from "vitest";
import {
    makeRangeForNodeContents,
    makeRangeFromTextOffsets,
    mapVisibleText,
    TextHighlightManager,
} from "./textHighlightManager";
import {
    FakeHighlight,
    getHighlightRegistry,
    installHighlightPolyfill,
} from "../test/highlightTestSupport";

describe("textHighlightManager", () => {
    let root: HTMLElement;

    beforeEach(() => {
        document.body.innerHTML = "";
        installHighlightPolyfill(window);
        root = document.createElement("div");
        document.body.appendChild(root);
    });

    // Set root's content and return the map of its visible text.
    function mapOf(html: string, shouldSkip?: (element: Element) => boolean) {
        root.innerHTML = html;
        return mapVisibleText(root, shouldSkip);
    }

    describe("mapVisibleText", () => {
        it("joins the text of nested inline elements with nothing between", () => {
            expect(mapOf("A sti<em>tch</em> in time").text).toBe(
                "A stitch in time",
            );
        });

        it("puts a separator where a <br> visually breaks the text", () => {
            // Without this, "Hello" and "world" would run together into one word.
            expect(mapOf("Hello<br>world").text).toBe("Hello world");
        });

        it("puts a separator at the boundaries of block elements", () => {
            expect(mapOf("<p>Cat</p><p>Dog</p>").text).toBe("Cat Dog ");
        });

        it("does not start with, or double up, a separator", () => {
            expect(mapOf("<p>Cat<br><br>Dog</p>").text).toBe("Cat Dog ");
        });

        it("leaves out the text of elements the caller skips", () => {
            // This is how the reader tools see through CKEditor's hidden selection bookmarks:
            // "Thr" + "ee" must read as the single word "Three".
            const map = mapOf(
                'Thr<span id="cke_bm_1" style="display: none;">x</span>ee blind mice',
                (element) => element.id.startsWith("cke_"),
            );
            expect(map.text).toBe("Three blind mice");
        });
    });

    describe("makeRangeFromTextOffsets", () => {
        it("makes a range covering a word within a single text node", () => {
            const map = mapOf("A stitch in time");
            const range = makeRangeFromTextOffsets(map, 2, 8);
            expect(range?.toString()).toBe("stitch");
        });

        it("makes a range spanning several text nodes", () => {
            const map = mapOf("A sti<em>tch</em> in time");
            // sanity check that the offsets really do straddle the <em>
            expect(map.text.substring(2, 8)).toBe("stitch");

            const range = makeRangeFromTextOffsets(map, 2, 8);
            expect(range?.toString()).toBe("stitch");
            expect(range!.startContainer.textContent).toBe("A sti");
            expect(range!.endContainer.textContent).toBe("tch");
        });

        it("snaps a boundary that lands on a synthetic separator onto real text", () => {
            // "Cat Dog": the space at offset 3 exists in the map but not in the DOM, because it
            // stands in for the <br>. A range over "Dog" must start in the second text node.
            const map = mapOf("Cat<br>Dog");
            expect(map.text).toBe("Cat Dog");

            const range = makeRangeFromTextOffsets(map, 3, 7);
            expect(range?.toString()).toBe("Dog");
            expect(range!.startContainer.textContent).toBe("Dog");
            expect(range!.startOffset).toBe(0);
        });

        it("gives no range for a span that is only synthetic separators", () => {
            const map = mapOf("Cat<br>Dog");
            expect(makeRangeFromTextOffsets(map, 3, 4)).toBeUndefined();
        });

        it("gives no range for an empty or backwards span", () => {
            const map = mapOf("A stitch in time");
            expect(makeRangeFromTextOffsets(map, 4, 4)).toBeUndefined();
            expect(makeRangeFromTextOffsets(map, 8, 2)).toBeUndefined();
        });
    });

    describe("makeRangeForNodeContents", () => {
        it("covers all of the node's text", () => {
            root.innerHTML = "<p>A <em>stitch</em> in time</p>";
            const range = makeRangeForNodeContents(root.firstElementChild!);
            expect(range?.toString()).toBe("A stitch in time");
        });

        it("gives no range for a node with no text", () => {
            root.innerHTML = "<p></p>";
            expect(
                makeRangeForNodeContents(root.firstElementChild!),
            ).toBeUndefined();
        });
    });

    describe("TextHighlightManager", () => {
        const kFirst = "test-highlight-1";
        const kSecond = "test-highlight-2";

        function makeManager(): TextHighlightManager {
            return new TextHighlightManager([kFirst, kSecond]);
        }

        function rangeOver(text: string): Range {
            root.innerHTML = text;
            return makeRangeForNodeContents(root)!;
        }

        it("registers and unregisters a named highlight", () => {
            const manager = makeManager();
            const registry = getHighlightRegistry(window);
            const range = rangeOver("Cat");

            manager.setHighlight(kFirst, [range], root);
            expect(registry.get(kFirst)?.ranges).toEqual([range]);

            // An empty list of ranges means "paint nothing", i.e. remove the entry rather than
            // leave an empty highlight registered.
            manager.setHighlight(kFirst, [], root);
            expect(registry.has(kFirst)).toBe(false);
        });

        it("passes the priority on, so overlapping highlights paint in the right order", () => {
            const manager = makeManager();
            manager.setHighlight(kFirst, [rangeOver("Cat")], root, 3);
            expect(getHighlightRegistry(window).get(kFirst)?.priority).toBe(3);
        });

        it("clears only the named highlights", () => {
            const manager = makeManager();
            const registry = getHighlightRegistry(window);
            manager.setHighlight(kFirst, [rangeOver("Cat")], root);
            manager.setHighlight(kSecond, [rangeOver("Cat")], root);

            manager.clearHighlights([kSecond], root);
            expect(registry.has(kFirst)).toBe(true);
            expect(registry.has(kSecond)).toBe(false);
        });

        it("clears every highlight it manages, and nothing else", () => {
            const manager = makeManager();
            const registry = getHighlightRegistry(window);
            manager.setHighlight(kFirst, [rangeOver("Cat")], root);
            manager.setHighlight(kSecond, [rangeOver("Cat")], root);
            registry.set("someone-elses-highlight", new FakeHighlight());

            manager.clearAllManagedHighlights(root);
            expect(registry.has(kFirst)).toBe(false);
            expect(registry.has(kSecond)).toBe(false);
            expect(registry.has("someone-elses-highlight")).toBe(true);
        });

        it("says it cannot highlight a document with no custom highlight support", () => {
            const manager = makeManager();
            expect(manager.canHighlight(root)).toBe(true);

            const windowWithoutHighlights = window as Window & {
                Highlight?: unknown;
            };
            const savedConstructor = windowWithoutHighlights.Highlight;
            windowWithoutHighlights.Highlight = undefined;
            try {
                expect(manager.canHighlight(root)).toBe(false);
                // ...and asking for a highlight anyway is a harmless no-op.
                manager.setHighlight(kFirst, [rangeOver("Cat")], root);
                expect(getHighlightRegistry(window).has(kFirst)).toBe(false);
            } finally {
                windowWithoutHighlights.Highlight = savedConstructor;
            }
        });

        // BL-15300: a highlight whose ranges no longer cover live content stays registered but
        // paints nothing, so callers need to be able to detect that and re-establish it.
        describe("hasDeadRanges", () => {
            it("is false for a highlight over live content", () => {
                const manager = makeManager();
                root.innerHTML = "<p>Cat</p>";
                manager.setHighlight(
                    kFirst,
                    [makeRangeForNodeContents(root.firstElementChild!)!],
                    root,
                );
                expect(manager.hasDeadRanges(kFirst, root)).toBe(false);
            });

            it("is true when the highlighted node has been replaced", () => {
                const manager = makeManager();
                root.innerHTML = "<p>Cat</p>";
                const paragraph = root.firstElementChild!;
                manager.setHighlight(
                    kFirst,
                    [makeRangeForNodeContents(paragraph)!],
                    root,
                );
                expect(manager.hasDeadRanges(kFirst, root)).toBe(false); // sanity check

                // As CKEditor's initialization does: replace the paragraph the range points at.
                paragraph.remove();
                root.innerHTML = "<p>Cat</p>";
                expect(manager.hasDeadRanges(kFirst, root)).toBe(true);
            });

            it("is true when an ancestor of the highlighted node was removed", () => {
                // Removing an ancestor collapses the live Range onto the still-connected former
                // parent, so a connectedness check alone would miss it.
                const manager = makeManager();
                root.innerHTML = "<div><p>Cat</p></div>";
                const wrapper = root.firstElementChild!;
                manager.setHighlight(
                    kFirst,
                    [makeRangeForNodeContents(wrapper.firstElementChild!)!],
                    root,
                );
                expect(manager.hasDeadRanges(kFirst, root)).toBe(false); // sanity check

                wrapper.innerHTML = "";
                expect(manager.hasDeadRanges(kFirst, root)).toBe(true);
            });

            it("is false when nothing is registered under that name", () => {
                expect(makeManager().hasDeadRanges(kFirst, root)).toBe(false);
            });
        });
    });
});
