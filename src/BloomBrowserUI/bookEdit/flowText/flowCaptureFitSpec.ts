// divideBoxAtFit cuts one box's text where that text stops fitting, for a box on a page nobody
// is looking at (FlowTextWalk drives it through captureFlowFit). The two halves go to two
// different pages, so every word has to end up on exactly one side of the cut: a word dropped
// here is dropped from the book, and a word on both sides is a word the reader sees twice.
//
// The measuring itself belongs to the real layout and is not tested here. What is tested is the
// arithmetic around it: the offset the mark sits at, and the division at that offset.

import { afterEach, describe, expect, it, vi } from "vitest";
import { captureFlowFit, divideBoxAtFit } from "./flowCaptureFit";
import { createOverflowMarker } from "./flowOverflowMarker";

// The real fit probe asks a Range for its rectangles, and jsdom's Range has no such method.
// An answer of no rectangles is what jsdom means: nothing is laid out, so all of it "fits".
(
    Range.prototype as unknown as { getClientRects: () => DOMRect[] }
).getClientRects = () => [];

/** Words that say where they come, so a gap or a repeat in the result is plain to read. */
function indexedWords(from: number, count: number): string {
    return Array.from(
        { length: count },
        (_unused, i) => "w" + String(from + i).padStart(4, "0"),
    ).join(" ");
}

function makeEditable(innerHtml: string): HTMLElement {
    const editable = document.createElement("div");
    editable.className = "bloom-editable normal-style";
    editable.setAttribute("contenteditable", "true");
    editable.setAttribute("lang", "en");
    editable.innerHTML = innerHtml;
    document.body.appendChild(editable);
    return editable;
}

/** Put the mark where this word starts, and say how many characters come before it. */
function markBeforeWord(editable: HTMLElement, word: string): number {
    const paragraphs = Array.from(editable.querySelectorAll("p"));
    let before = 0;
    for (const paragraph of paragraphs) {
        const text = paragraph.firstChild as Text;
        const at = (text.data ?? "").indexOf(word);
        if (at >= 0) {
            const rest = text.splitText(at);
            paragraph.insertBefore(createOverflowMarker(document), rest);
            return before + at;
        }
        // The linearized text of a box has one space between paragraphs.
        before += (text.data ?? "").length + 1;
    }
    throw new Error(`This box holds no "${word}".`);
}

function textOf(html: string): string {
    const holder = document.createElement("div");
    holder.innerHTML = html;
    const paragraphs = Array.from(holder.querySelectorAll("p"));
    const parts = paragraphs.length
        ? paragraphs.map((paragraph) => paragraph.textContent ?? "")
        : [holder.textContent ?? ""];
    return parts.join(" ").replace(/[​‌]/g, "").replace(/\s+/g, " ").trim();
}

describe("divideBoxAtFit", () => {
    it("keeps every word when the cut falls in one long paragraph", () => {
        const editable = makeEditable(`<p>${indexedWords(1, 300)}</p>`);
        const at = markBeforeWord(editable, "w0101");

        const result = divideBoxAtFit(editable, at);

        expect(textOf(result.head)).toBe(indexedWords(1, 100));
        expect(textOf(result.tail)).toBe(indexedWords(101, 200));
    });

    it("keeps every word when the box holds many paragraphs", () => {
        const editable = makeEditable(
            `<p>${indexedWords(1, 40)}</p><p>${indexedWords(41, 40)}</p>` +
                `<p>${indexedWords(81, 40)}</p><p>${indexedWords(121, 40)}</p>`,
        );
        const at = markBeforeWord(editable, "w0095");

        const result = divideBoxAtFit(editable, at);

        expect(textOf(result.head)).toBe(indexedWords(1, 94));
        expect(textOf(result.tail)).toBe(indexedWords(95, 66));
    });

    it("cuts at the start of the word the mark falls inside", () => {
        const editable = makeEditable(`<p>${indexedWords(1, 60)}</p>`);
        // Three characters into w0031, which no word may be cut at.
        const at = markBeforeWord(editable, "w0031") + 3;

        const result = divideBoxAtFit(editable, at);

        expect(textOf(result.head)).toBe(indexedWords(1, 30));
        expect(textOf(result.tail)).toBe(indexedWords(31, 30));
    });

    it("keeps every word when the paragraph before the cut is a continuation", () => {
        const editable = makeEditable(
            `<p data-flow-continuation="true" data-flow-seam-space="true">` +
                `${indexedWords(1, 50)}</p><p>${indexedWords(51, 50)}</p>`,
        );
        const at = markBeforeWord(editable, "w0030");

        const result = divideBoxAtFit(editable, at);

        expect(textOf(result.head)).toBe(indexedWords(1, 29));
        expect(textOf(result.tail)).toBe(indexedWords(30, 71));
    });
});

describe("captureFlowFit", () => {
    afterEach(() => {
        document.body.innerHTML = "";
        window.__bloomFlowFit = undefined;
    });

    it("stashes the style attribute of the box's translation group", async () => {
        // The page being measured writes the group's font size into that attribute, and a page
        // thumbnail is drawn in the page list's own document, where the inline size is the only
        // word on how big the text is. So it travels back to C# with the two halves of the text.
        const page = document.createElement("div");
        page.className = "bloom-page";
        const group = document.createElement("div");
        group.className = "bloom-translationGroup";
        group.setAttribute("style", "font-size: 24px;");
        const editable = document.createElement("div");
        editable.className =
            "bloom-editable normal-style bloom-visibility-code-on";
        editable.setAttribute("lang", "en");
        editable.setAttribute("contenteditable", "true");
        editable.innerHTML = "<p>some text</p>";
        group.appendChild(editable);
        page.appendChild(group);
        document.body.appendChild(page);

        captureFlowFit(0, "en", true);
        await vi.waitUntil(() => window.__bloomFlowFit !== undefined, {
            timeout: 5000,
        });

        const stashed = JSON.parse(window.__bloomFlowFit as string);
        expect(
            stashed.groupStyle,
            `captureFlowFit stashed ${window.__bloomFlowFit}`,
        ).toBe("font-size: 24px;");
    });
});
