// splitCombinedAcrossPages divides the text of the box being edited and the text of a box on
// another page at one offset. Every character has to end up on exactly one side of that offset:
// the two halves are then written to two different pages, and anything dropped or copied here is
// lost or repeated in the book itself.
//
// A continuation paragraph records the space that stood at the point the text was cut in the
// data-flow-seam-space attribute, so the fixtures here set it. The offsets are word boundaries
// of the combined text, because choosing where a word may be cut is the caller's job, not this
// function's.

import { describe, expect, it } from "vitest";
import {
    getCombinedTextAcrossPages,
    splitCombinedAcrossPages,
} from "./flowDomMove";

function makeEditable(innerHtml: string): HTMLElement {
    const editable = document.createElement("div");
    editable.className = "bloom-editable normal-style";
    editable.setAttribute("contenteditable", "true");
    editable.setAttribute("lang", "en");
    editable.innerHTML = innerHtml;
    return editable;
}

/** Words that say where they come, so a gap or a repeat in the result is plain to read. */
function indexedWords(from: number, count: number): string {
    return Array.from(
        { length: count },
        (_unused, i) => "w" + String(from + i).padStart(4, "0"),
    ).join(" ");
}

/** The text of an HTML fragment, with its paragraphs separated by a space. */
function textOfHtml(html: string): string {
    const holder = document.createElement("div");
    holder.innerHTML = html;
    const paragraphs = Array.from(holder.querySelectorAll("p"));
    const parts = paragraphs.length
        ? paragraphs.map((p) => p.textContent ?? "")
        : [holder.textContent ?? ""];
    return collapse(parts.join(" "));
}

function collapse(text: string): string {
    return text.replace(/\s+/g, " ").trim();
}

describe("splitCombinedAcrossPages", () => {
    it("keeps every word when the split falls in the middle of one long paragraph", () => {
        const current = makeEditable(`<p>${indexedWords(1, 200)}</p>`);
        const nextHtml = `<p data-flow-continuation="true" data-flow-seam-space="true">${indexedWords(201, 100)}</p>`;
        const expected = indexedWords(1, 300);
        const combined = getCombinedTextAcrossPages(current, nextHtml);
        // Sanity check the start state: the two boxes hold one run of 300 words.
        expect(collapse(combined)).toBe(expected);
        // Split at the space before w0101, which is a word boundary of that combined text.
        const at = combined.indexOf(" w0101");
        expect(at).toBeGreaterThan(0);

        const split = splitCombinedAcrossPages(current, nextHtml, at);

        expect(
            collapse(
                `${textOfHtml(split.currentHtml)} ${textOfHtml(split.nextHtml)}`,
            ),
        ).toBe(expected);
        expect(textOfHtml(split.currentHtml)).toBe(indexedWords(1, 100));
    });

    it("keeps every word when the boxes hold several paragraphs each", () => {
        const current = makeEditable(
            `<p>${indexedWords(1, 40)}</p><p>${indexedWords(41, 40)}</p><p>${indexedWords(81, 40)}</p>`,
        );
        const nextHtml =
            `<p data-flow-continuation="true" data-flow-seam-space="true">${indexedWords(121, 40)}</p>` +
            `<p>${indexedWords(161, 40)}</p>`;
        const expected = indexedWords(1, 200);
        const combined = getCombinedTextAcrossPages(current, nextHtml);
        expect(collapse(combined)).toBe(expected);
        // A split inside the second paragraph of the box being edited.
        const at = combined.indexOf(" w0061");
        expect(at).toBeGreaterThan(0);

        const split = splitCombinedAcrossPages(current, nextHtml, at);

        expect(
            collapse(
                `${textOfHtml(split.currentHtml)} ${textOfHtml(split.nextHtml)}`,
            ),
        ).toBe(expected);
    });

    it("keeps every word when the split falls past the end of the box being edited", () => {
        const current = makeEditable(`<p>${indexedWords(1, 50)}</p>`);
        const nextHtml = `<p data-flow-continuation="true" data-flow-seam-space="true">${indexedWords(51, 100)}</p>`;
        const expected = indexedWords(1, 150);
        const combined = getCombinedTextAcrossPages(current, nextHtml);
        expect(collapse(combined)).toBe(expected);
        // Pulling back: more fits here than this box holds, so the split is inside the other
        // page's text.
        const at = combined.indexOf(" w0081");
        expect(at).toBeGreaterThan(0);

        const split = splitCombinedAcrossPages(current, nextHtml, at);

        expect(
            collapse(
                `${textOfHtml(split.currentHtml)} ${textOfHtml(split.nextHtml)}`,
            ),
        ).toBe(expected);
        expect(textOfHtml(split.currentHtml)).toBe(indexedWords(1, 80));
    });

    it("keeps the seam space when the box being edited ends with the editor's filler", () => {
        // What a box holds once the user has edited it: the paragraph ends with a zero-width
        // filler instead of a space, and the box on the other page records the seam space in
        // the attribute on its continuation paragraph.
        const filler = String.fromCharCode(0x200b);
        const current = makeEditable(`<p>${indexedWords(1, 80)}${filler}</p>`);
        const nextHtml = `<p data-flow-continuation="true" data-flow-seam-space="true">${indexedWords(81, 40)}</p>`;
        const expected = indexedWords(1, 120);
        const combined = getCombinedTextAcrossPages(current, nextHtml);
        expect(collapse(combined.replace(filler, ""))).toBe(expected);
        const at = combined.indexOf(" w0021");
        expect(at).toBeGreaterThan(0);

        const split = splitCombinedAcrossPages(current, nextHtml, at);

        expect(
            collapse(
                `${textOfHtml(split.currentHtml)} ${textOfHtml(split.nextHtml).replace(filler, "")}`,
            ),
        ).toBe(expected);
        // The seam the editor left inside the moved text is still a separation.
        expect(
            collapse(textOfHtml(split.nextHtml).replace(filler, " ")),
        ).toContain("w0080 w0081");
    });

    it("keeps every paragraph of the next box when a few words come back from it", () => {
        // The next box holds three paragraphs, the first of them a continuation. Only the
        // first is joined onto the box being edited; the other two must both come through.
        const current = makeEditable(`<p>${indexedWords(1, 47)}</p>`);
        const nextHtml =
            `<p data-flow-continuation="true" data-flow-seam-space="true">${indexedWords(48, 45)}</p>` +
            `<p>${indexedWords(93, 92)}</p>` +
            `<p>${indexedWords(185, 90)}</p>`;
        const expected = indexedWords(1, 274);
        const combined = getCombinedTextAcrossPages(current, nextHtml);
        expect(collapse(combined)).toBe(expected);
        const at = combined.indexOf(" w0052");
        expect(at).toBeGreaterThan(0);

        const split = splitCombinedAcrossPages(current, nextHtml, at);

        expect(textOfHtml(split.currentHtml)).toBe(indexedWords(1, 51));
        expect(
            collapse(
                `${textOfHtml(split.currentHtml)} ${textOfHtml(split.nextHtml)}`,
            ),
        ).toBe(expected);
    });
});
