import { describe, it, expect } from "vitest";
import { EditableDivUtils } from "./editableDivUtils";

describe("EditableDivUtils Tests", () => {
    it("normalizeBloomLineBreakSpansInElement preserves a simple linebreak span", () => {
        const div = document.createElement("div");
        div.innerHTML =
            "<p>A<span id='lb' class='bloom-linebreak'></span>B</p>";

        const before = div.querySelector("#lb") as HTMLSpanElement;
        expect(before).toBeTruthy();
        expect(before.innerHTML).toBe("");

        EditableDivUtils.normalizeBloomLineBreakSpansInElement(div);

        const after = div.querySelector("#lb") as HTMLSpanElement;
        expect(after).toBeTruthy();
        expect(after.innerHTML).toBe("");
        expect(div.textContent).toBe("AB");
    });

    it("normalizeBloomLineBreakSpansInElement does nothing when no linebreak spans exist", () => {
        const div = document.createElement("div");
        div.innerHTML = "<p>A<em>normal</em>B</p>";

        const beforeHtml = div.innerHTML;
        expect(div.querySelectorAll("span.bloom-linebreak").length).toBe(0);

        EditableDivUtils.normalizeBloomLineBreakSpansInElement(div);

        expect(div.querySelectorAll("span.bloom-linebreak").length).toBe(0);
        expect(div.innerHTML).toBe(beforeHtml);
    });

    it("normalizeBloomLineBreakSpansInElement moves text out of a simple linebreak span", () => {
        const div = document.createElement("div");
        div.innerHTML =
            "<p>A<span id='lb' class='bloom-linebreak'>bad text</span>B</p>";

        const before = div.querySelector("#lb") as HTMLSpanElement;
        expect(before).toBeTruthy();
        expect(before.innerHTML).toBe("bad text");

        EditableDivUtils.normalizeBloomLineBreakSpansInElement(div);

        const after = div.querySelector("#lb") as HTMLSpanElement;
        expect(after).toBeTruthy();
        expect(after.innerHTML).toBe("");
        expect(after.previousSibling?.textContent).toContain("bad text");
        expect(div.textContent).toBe("Abad textB");
    });

    it("normalizeBloomLineBreakSpansInElement moves markup out of a simple linebreak span", () => {
        const div = document.createElement("div");
        div.innerHTML =
            "<p>A<span id='lb' class='bloom-linebreak'><em>bad</em> text</span>B</p>";

        const before = div.querySelector("#lb") as HTMLSpanElement;
        expect(before).toBeTruthy();
        expect(before.querySelector("em")?.textContent).toBe("bad");

        EditableDivUtils.normalizeBloomLineBreakSpansInElement(div);

        const after = div.querySelector("#lb") as HTMLSpanElement;
        expect(after).toBeTruthy();
        expect(after.innerHTML).toBe("");
        expect(after.querySelector("em")).toBeNull();
        expect(div.querySelector("em")?.textContent).toBe("bad");
        expect(div.textContent).toBe("Abad textB");
    });

    it("normalizeBloomLineBreakSpansInElement moves content before nested linebreak spans", () => {
        const div = document.createElement("div");
        div.innerHTML =
            "<p>A<span id='outer' class='bloom-linebreak'><span id='inner' class='bloom-linebreak'>nested text</span></span>B</p>";

        expect(div.querySelector("#inner")?.innerHTML).toContain("nested text");
        EditableDivUtils.normalizeBloomLineBreakSpansInElement(div);

        const outer = div.querySelector("#outer") as HTMLSpanElement;
        const inner = div.querySelector("#inner") as HTMLSpanElement;
        expect(outer).toBeTruthy();
        expect(inner).toBeTruthy();

        expect(outer.innerHTML).toBe("");
        expect(inner.innerHTML).toBe("");

        expect(outer.previousSibling).toBe(inner);
        expect(inner.previousSibling?.textContent).toContain("nested text");
        expect(div.textContent).toBe("Anested textB");
    });

    it("fixUpEmptyishParagraphs does not modify paragraphs with content", () => {
        const testCases = [
            "<p>A</p>",
            "<p>A&nbsp;</p>",
            "<p>&nbsp;A</p>",
            "<p>&nbsp;<span>A</span></p>",
        ];

        for (const testCase of testCases) {
            const div = document.createElement("div");
            div.innerHTML = testCase;

            EditableDivUtils.fixUpEmptyishParagraphs(div);

            expect(div.innerHTML).toEqual(testCase);
        }
    });

    it("fixUpEmptyishParagraphs corrects paragraphs with only &nbsp; to have only <br>", () => {
        // [0] is the input, [1] is the expected output
        const testCases = [
            ["<p>&nbsp;</p>", "<p><br></p>"],
            ["<p>&nbsp;</p><p>&nbsp;</p>", "<p><br></p><p><br></p>"],
            [
                '<p>&nbsp;<span id="cke_bm_49C" style="display: none;">&nbsp;</span></p>',
                '<p><br><span id="cke_bm_49C" style="display: none;">&nbsp;</span></p>',
            ],
            [
                '<p><span id="cke_bm_49C" style="display: none;">&nbsp;</span>&nbsp;</p>',
                '<p><span id="cke_bm_49C" style="display: none;">&nbsp;</span><br></p>',
            ],
        ];

        for (const testCase of testCases) {
            const div = document.createElement("div");
            div.innerHTML = testCase[0];

            EditableDivUtils.fixUpEmptyishParagraphs(div);

            expect(div.innerHTML).toEqual(testCase[1]);
        }
    });

    it("fixUpEmptyishParagraphs handles empty text node", () => {
        const div = document.createElement("div");
        const p = document.createElement("p");
        div.appendChild(p);
        const emptyTextNode = document.createTextNode("");
        p.appendChild(emptyTextNode);
        const nbspTextNode = document.createTextNode("\u00A0");
        p.appendChild(nbspTextNode);

        // Verify setup
        expect(div.innerHTML).toEqual("<p>&nbsp;</p>");

        EditableDivUtils.fixUpEmptyishParagraphs(div);

        // As far as we know, ckeditor's getData() only replaces
        // a single <br> with a single &nbsp; (which is what we are trying to reverse).
        // So we think we want to leave the p alone in this case.
        expect(div.innerHTML).toEqual("<p>&nbsp;</p>");
    });

    it("safelyReplaceContentWithCkEditorData ensures no initial blank paragraph", () => {
        // [0]:input div html, [1]:input ckeditor data, [2]:expected output
        const testCases = [
            // The main scenario we are trying to fix: ckeditor wraps lone initial bookmark in a paragraph; don't let it.
            [
                '<span id="cke_bm_49C" style="display: none;">&nbsp;</span><p>A</p>',
                '<p><span id="cke_bm_49C" style="display: none;">&nbsp;</span>&nbsp;</p><p>A</p>',
                '<span id="cke_bm_49C" style="display: none;">&nbsp;</span><p>A</p>',
            ],
            // Ensures we leave well enough alone
            [
                '<span id="cke_bm_49C" style="display: none;">&nbsp;</span><p>A</p>',
                '<span id="cke_bm_49C" style="display: none;">&nbsp;</span><p>A</p>',
                '<span id="cke_bm_49C" style="display: none;">&nbsp;</span><p>A</p>',
            ],
            // If ckeditor wants to wrap a non bookmark for some reason, leave it alone
            [
                "<span>&nbsp;</span><p>A</p>",
                "<p><span>&nbsp;</span></p><p>A</p>",
                "<p><span>&nbsp;</span></p><p>A</p>",
            ],
            // Not sure this can really happen, but prove we leave paragraph wrapping alone if there is content besides just nbsp
            [
                '<span id="cke_bm_49C" style="display: none;">&nbsp;</span>Z<p>A</p>',
                '<p><span id="cke_bm_49C" style="display: none;">&nbsp;</span>Z</p><p>A</p>',
                '<p><span id="cke_bm_49C" style="display: none;">&nbsp;</span>Z</p><p>A</p>',
            ],
        ];

        for (const testCase of testCases) {
            const div = document.createElement("div");
            div.innerHTML = testCase[0];

            EditableDivUtils.safelyReplaceContentWithCkEditorData(
                div,
                testCase[1],
            );

            expect(div.innerHTML).toEqual(testCase[2]);
        }
    });

    it("removeCkEditorFillingChars removes U+200B but preserves U+200C and U+200D", () => {
        const zwsp = String.fromCharCode(0x200b); // filling char we want gone
        const zwnj = String.fromCharCode(0x200c); // legitimate; must be kept
        const zwj = String.fromCharCode(0x200d); // legitimate; must be kept

        // sanity check the test data
        expect(zwsp).not.toEqual(zwnj);
        const input = `a${zwsp}b${zwnj}c${zwj}d${zwsp}`;
        expect(input.indexOf(zwsp)).toBeGreaterThan(-1);

        const result = EditableDivUtils.removeCkEditorFillingChars(input);

        expect(result.indexOf(zwsp)).toEqual(-1);
        expect(result).toEqual(`ab${zwnj}c${zwj}d`);
    });

    it("safelyReplaceContentWithCkEditorData strips orphaned filling chars", () => {
        const zwsp = String.fromCharCode(0x200b);
        const ckEditorData = `<p>ca${zwsp}t${zwsp}</p>`;
        // sanity check: our input really does contain the filling char we expect to be stripped.
        expect(ckEditorData.indexOf(zwsp)).toBeGreaterThan(-1);

        const div = document.createElement("div");
        EditableDivUtils.safelyReplaceContentWithCkEditorData(
            div,
            ckEditorData,
        );

        expect(div.innerHTML.indexOf(zwsp)).toEqual(-1);
        expect(div.innerHTML).toEqual("<p>cat</p>");
    });

    // Build a div (attached, so it has a real selection) whose paragraph has been split into
    // two adjacent text nodes the way ckeditor's createBookmarks() followed by
    // selectBookmarks() splits it: a hidden span is inserted at the insertion point, then
    // removed again.
    const makeParagraphSplitByABookmark = (
        text: string,
        offsetOfBookmark: number,
    ): HTMLDivElement => {
        const div = document.createElement("div");
        const p = document.createElement("p");
        p.appendChild(document.createTextNode(text));
        div.appendChild(p);
        document.body.appendChild(div);

        const secondHalf = (p.firstChild as Text).splitText(offsetOfBookmark);
        const bookmark = document.createElement("span");
        bookmark.id = "cke_bm_1S";
        bookmark.setAttribute("style", "display: none;");
        p.insertBefore(bookmark, secondHalf);
        bookmark.remove();

        return div;
    };

    it("mergeTextNodesSplitByBookmarks rejoins the text a removed bookmark split", () => {
        const div = makeParagraphSplitByABookmark("overfflow", 5);
        const p = div.firstChild as HTMLParagraphElement;
        // sanity check: the removed bookmark really did leave two text nodes.
        expect(p.childNodes.length).toBe(2);
        expect(p.childNodes[0].textContent).toBe("overf");
        expect(p.childNodes[1].textContent).toBe("flow");

        EditableDivUtils.mergeTextNodesSplitByBookmarks(div);

        expect(p.childNodes.length).toBe(1);
        expect(p.childNodes[0].textContent).toBe("overfflow");

        div.remove();
    });

    it("mergeTextNodesSplitByBookmarks keeps the insertion point on the same characters", () => {
        const div = makeParagraphSplitByABookmark("overfflow", 5);
        const p = div.firstChild as HTMLParagraphElement;
        // Where ckeditor leaves the insertion point: the start of the second text node,
        // which the user sees as being between the "ff" and the "low".
        const selection = window.getSelection();
        if (!selection) {
            throw new Error("no selection available; cannot run this test");
        }
        selection.removeAllRanges();
        const range = document.createRange();
        range.setStart(p.childNodes[1], 0);
        range.collapse(true);
        selection.addRange(range);
        // sanity check the setup
        expect(selection.anchorNode).toBe(p.childNodes[1]);
        expect(selection.anchorOffset).toBe(0);

        EditableDivUtils.mergeTextNodesSplitByBookmarks(div);

        // Now there is only one text node, so the same insertion point is offset 5 in it.
        expect(selection.rangeCount).toBe(1);
        expect(selection.anchorNode).toBe(p.childNodes[0]);
        expect(selection.anchorOffset).toBe(5);
        expect(selection.isCollapsed).toBe(true);

        div.remove();
    });

    it("mergeTextNodesSplitByBookmarks keeps a selected range on the same characters", () => {
        const div = makeParagraphSplitByABookmark("overfflow", 5);
        const p = div.firstChild as HTMLParagraphElement;
        const selection = window.getSelection();
        if (!selection) {
            throw new Error("no selection available; cannot run this test");
        }
        selection.removeAllRanges();
        const range = document.createRange();
        // "rffl", which straddles the two text nodes.
        range.setStart(p.childNodes[0], 3);
        range.setEnd(p.childNodes[1], 2);
        selection.addRange(range);
        // sanity check the setup
        expect(selection.toString()).toBe("rffl");

        EditableDivUtils.mergeTextNodesSplitByBookmarks(div);

        expect(selection.toString()).toBe("rffl");
        expect(selection.anchorNode).toBe(p.childNodes[0]);
        expect(selection.anchorOffset).toBe(3);
        expect(selection.focusOffset).toBe(7);

        div.remove();
    });

    it("mergeTextNodesSplitByBookmarks does not move the insertion point across a paragraph boundary", () => {
        // A split left in the first paragraph, but the user is now typing at the start of the
        // second one. Counting characters over the whole box would put the caret at the end of
        // the first paragraph, which is the same number of characters in but a different place.
        const div = makeParagraphSplitByABookmark("overfflow", 5);
        const secondParagraph = document.createElement("p");
        secondParagraph.appendChild(document.createTextNode("def"));
        div.appendChild(secondParagraph);

        const selection = window.getSelection();
        if (!selection) {
            throw new Error("no selection available; cannot run this test");
        }
        selection.removeAllRanges();
        const range = document.createRange();
        range.setStart(secondParagraph.firstChild!, 0);
        range.collapse(true);
        selection.addRange(range);
        // sanity check the setup
        expect(selection.anchorNode).toBe(secondParagraph.firstChild);

        EditableDivUtils.mergeTextNodesSplitByBookmarks(div);

        // the merge still happened in the first paragraph...
        expect(div.firstChild!.childNodes.length).toBe(1);
        // ...and the caret is still at the start of the second one.
        expect(selection.anchorNode).toBe(secondParagraph.firstChild);
        expect(selection.anchorOffset).toBe(0);

        div.remove();
    });

    it("mergeTextNodesSplitByBookmarks leaves an already-normal paragraph alone", () => {
        const div = document.createElement("div");
        div.innerHTML = "<p>over<em>ff</em>low</p>";
        const before = div.innerHTML;

        EditableDivUtils.mergeTextNodesSplitByBookmarks(div);

        // In particular, it must not merge text across an element boundary.
        expect(div.innerHTML).toBe(before);
    });
});
