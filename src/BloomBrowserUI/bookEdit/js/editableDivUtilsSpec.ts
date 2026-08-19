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
    function makeParagraphSplitByABookmark(
        text: string,
        offsetOfBookmark: number,
    ): HTMLDivElement {
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
    }

    it("mergeAdjacentTextNodes rejoins the text a removed bookmark split", () => {
        const div = makeParagraphSplitByABookmark("overfflow", 5);
        const p = div.firstChild as HTMLParagraphElement;
        // sanity check: the removed bookmark really did leave two text nodes.
        expect(p.childNodes.length).toBe(2);
        expect(p.childNodes[0].textContent).toBe("overf");
        expect(p.childNodes[1].textContent).toBe("flow");

        EditableDivUtils.mergeAdjacentTextNodes(div);

        expect(p.childNodes.length).toBe(1);
        expect(p.childNodes[0].textContent).toBe("overfflow");

        div.remove();
    });

    it("mergeAdjacentTextNodes keeps the original text node rather than replacing it", () => {
        // Not a detail. Replacing the text node would collapse any live Range whose boundaries
        // were inside it, and the reader tools' and Talking Book's highlights are exactly that:
        // live Ranges over these text nodes, registered by the markup pass moments before this
        // runs. Replacing them makes the highlighting silently vanish from the paragraph the
        // user is typing in.
        const div = makeParagraphSplitByABookmark("overfflow", 5);
        const p = div.firstChild as HTMLParagraphElement;
        const originalFirstNode = p.childNodes[0];
        // sanity check the setup
        expect(originalFirstNode.textContent).toBe("overf");

        EditableDivUtils.mergeAdjacentTextNodes(div);

        expect(p.childNodes.length).toBe(1);
        expect(p.childNodes[0].textContent).toBe("overfflow");
        expect(p.childNodes[0]).toBe(originalFirstNode);

        div.remove();
    });

    it("mergeAdjacentTextNodes leaves no trace in the markup that gets saved", () => {
        // It has to make Chromium re-shape the merged text, but whatever it does to achieve
        // that must not survive into the html: this runs on every pause in typing, and the
        // box's innerHTML is what Bloom saves into the book. Checking the serialized markup
        // rather than a property, because an emptied-out attribute still serializes.
        const div = makeParagraphSplitByABookmark("overfflow", 5);
        const p = div.firstChild as HTMLParagraphElement;
        // sanity check the setup
        expect(p.hasAttribute("style")).toBe(false);

        EditableDivUtils.mergeAdjacentTextNodes(div);

        expect(div.innerHTML).toBe("<p>overfflow</p>");
        expect(p.hasAttribute("style")).toBe(false);

        div.remove();
    });

    it("mergeAdjacentTextNodes keeps a paragraph's own inline style", () => {
        const div = makeParagraphSplitByABookmark("overfflow", 5);
        const p = div.firstChild as HTMLParagraphElement;
        p.style.display = "inline-block";

        EditableDivUtils.mergeAdjacentTextNodes(div);

        expect(p.style.display).toBe("inline-block");
        expect(p.childNodes[0].textContent).toBe("overfflow");

        div.remove();
    });

    it("mergeAdjacentTextNodes merges only within a parent, and merges every run", () => {
        const div = document.createElement("div");
        const p = document.createElement("p");
        p.appendChild(document.createTextNode("over"));
        p.appendChild(document.createTextNode("f"));
        const em = document.createElement("em");
        em.appendChild(document.createTextNode("fl"));
        em.appendChild(document.createTextNode("ow"));
        p.appendChild(em);
        p.appendChild(document.createTextNode(" and"));
        p.appendChild(document.createTextNode(" more"));
        div.appendChild(p);
        document.body.appendChild(div);
        // sanity check the setup: two runs in the p, one inside the em
        expect(p.childNodes.length).toBe(5);
        expect(em.childNodes.length).toBe(2);

        const originalEmFirstNode = em.childNodes[0];

        EditableDivUtils.mergeAdjacentTextNodes(div);

        // "over"+"f" merged, " and"+" more" merged, the <em> still between them
        expect(em.childNodes[0]).toBe(originalEmFirstNode); // ranges must survive here too
        expect(p.childNodes.length).toBe(3);
        expect(p.childNodes[0].textContent).toBe("overf");
        expect(p.childNodes[1]).toBe(em);
        expect(p.childNodes[2].textContent).toBe(" and more");
        expect(em.childNodes.length).toBe(1);
        expect(em.childNodes[0].textContent).toBe("flow");
        expect(div.textContent).toBe("overfflow and more");

        div.remove();
    });

    // Where the user's cursor ends up is NOT asserted here, deliberately. Keeping live ranges
    // (which includes the selection) on the same characters across a text-node merge is the
    // browser's job per the DOM spec, and Chromium does it: verified in a running Bloom, with
    // the caret both at the split itself and immediately after a <strong>. jsdom does not
    // implement those range fixups, so a test here would fail on correct code and, worse, pass
    // on the hand-rolled save/restore this code used to have - which was itself the bug.
    it("mergeAdjacentTextNodes leaves the text byte-for-byte unchanged, whatever it ends with", () => {
        // It forces a repaint by appending a space and immediately deleting it. A text node's
        // data is an exact string - html whitespace collapsing is a rendering rule, not a data
        // one - so that has to round-trip exactly even when the text already ends in
        // whitespace, and the appended space must never survive into what Bloom saves.
        const endings = [
            "flow", // ordinary
            "flow ", // already ends with a space
            "flow  ", // ...with two
            "flow ", // ...with a non-breaking space
            "flow\n", // ...with a newline
            "flow\t", // ...with a tab
            "   ", // nothing but spaces
        ];
        for (const ending of endings) {
            const div = document.createElement("div");
            const p = document.createElement("p");
            p.appendChild(document.createTextNode("overf"));
            p.appendChild(document.createTextNode(ending));
            div.appendChild(p);
            document.body.appendChild(div);
            // sanity check the setup
            expect(p.childNodes.length).toBe(2);

            EditableDivUtils.mergeAdjacentTextNodes(div);

            expect(p.childNodes.length).toBe(1);
            expect((p.firstChild as Text).data).toBe("overf" + ending);
            div.remove();
        }
    });

    it("mergeAdjacentTextNodes copes with an empty text node at the head of a run", () => {
        // normalize() deletes empty text nodes outright, so the node a run collapses into is
        // its first NON-empty node. Getting that wrong would leave us holding a detached node
        // and the repaint would silently do nothing - the original bug all over again.
        const div = document.createElement("div");
        const p = document.createElement("p");
        p.appendChild(document.createTextNode("")); // empty node heads the run
        p.appendChild(document.createTextNode("overf"));
        p.appendChild(document.createTextNode("flow"));
        div.appendChild(p);
        document.body.appendChild(div);
        // sanity check the setup
        expect(p.childNodes.length).toBe(3);

        EditableDivUtils.mergeAdjacentTextNodes(div);

        expect(p.childNodes.length).toBe(1);
        expect(p.childNodes[0].textContent).toBe("overfflow");
        // the surviving node is the first non-empty one, still attached
        expect((p.childNodes[0] as Text).isConnected).toBe(true);
        expect(div.innerHTML).toBe("<p>overfflow</p>");

        div.remove();
    });

    it("mergeAdjacentTextNodes copes with a run that is entirely empty nodes", () => {
        const div = document.createElement("div");
        const p = document.createElement("p");
        p.appendChild(document.createTextNode(""));
        p.appendChild(document.createTextNode(""));
        div.appendChild(p);
        document.body.appendChild(div);
        expect(p.childNodes.length).toBe(2);

        EditableDivUtils.mergeAdjacentTextNodes(div);

        // normalize() removes them all; we must not throw trying to poke a detached node.
        expect(p.childNodes.length).toBe(0);

        div.remove();
    });

    it("mergeAdjacentTextNodes only touches parents that actually have a run to merge", () => {
        // What we can assert is scope: every other node in the box comes out untouched, so
        // there is nothing for the browser to have to fix up in the first place.
        const div = makeParagraphSplitByABookmark("overfflow", 5);
        const untouched = document.createElement("p");
        untouched.appendChild(document.createTextNode("def"));
        div.appendChild(untouched);
        const untouchedTextNode = untouched.firstChild;

        EditableDivUtils.mergeAdjacentTextNodes(div);

        // the merge happened in the first paragraph...
        expect(div.firstChild!.childNodes.length).toBe(1);
        expect(div.firstChild!.textContent).toBe("overfflow");
        // ...and the second paragraph still holds the very same text node object.
        expect(untouched.childNodes.length).toBe(1);
        expect(untouched.firstChild).toBe(untouchedTextNode);
        expect(untouched.textContent).toBe("def");

        div.remove();
    });

    it("mergeAdjacentTextNodes merges a split it did not cause", () => {
        // Nothing about the repair is specific to ckeditor bookmarks: Chromium's own
        // backspace, and long-press inserting the character it composed, split a paragraph
        // the same way and used to be left unrepaired (BL-16717).
        const div = document.createElement("div");
        const p = document.createElement("p");
        p.appendChild(document.createTextNode("waf"));
        p.appendChild(document.createTextNode("fle"));
        div.appendChild(p);
        document.body.appendChild(div);
        // sanity check the setup
        expect(p.childNodes.length).toBe(2);

        EditableDivUtils.mergeAdjacentTextNodes(div);

        expect(p.childNodes.length).toBe(1);
        expect(p.childNodes[0].textContent).toBe("waffle");

        div.remove();
    });

    it("mergeAdjacentTextNodes leaves the box alone while ckeditor holds a filling char", () => {
        // ckeditor's filling char is a zero-width space in a text node of its own, and
        // ckeditor remembers that node so it can take the character out again. Merging the
        // node away would leave ckeditor writing to a detached node, and the zero-width space
        // would survive into the saved book - the BL-16490 hazard. The split is left for the
        // next keystroke to repair, once the filling char has gone.
        const div = document.createElement("div");
        const p = document.createElement("p");
        p.appendChild(document.createTextNode("waffle"));
        p.appendChild(document.createTextNode(String.fromCharCode(0x200b)));
        div.appendChild(p);
        document.body.appendChild(div);
        const fillingCharNode = p.childNodes[1];
        // Stand in for the live ckeditor: what matters is that it is TRACKING the node, which
        // is what it means for the character to be one we must not disturb. (A stray
        // zero-width space nobody is tracking is a different thing, and is fine to merge.)
        (div as HTMLElement & { bloomCkEditor?: object }).bloomCkEditor = {
            editable: () => ({
                getCustomData: (key: string) =>
                    key === "cke-fillingChar" ? fillingCharNode : undefined,
            }),
        };
        // sanity check the setup: there IS a split here, so without the filling char this
        // would certainly merge.
        expect(p.childNodes.length).toBe(2);

        EditableDivUtils.mergeAdjacentTextNodes(div);

        expect(p.childNodes.length).toBe(2);
        expect(p.childNodes[1]).toBe(fillingCharNode);

        div.remove();
    });

    it("mergeAdjacentTextNodes merges a zero-width space nobody is tracking", () => {
        // The mirror image of the test above, and the reason it asks ckeditor rather than
        // searching the text: Thai, Khmer and Myanmar use U+200B in real text as a word break,
        // and treating the character itself as a filling char would have turned the repair off
        // for a whole book in those languages.
        const div = document.createElement("div");
        const p = document.createElement("p");
        p.appendChild(document.createTextNode("waf"));
        p.appendChild(
            document.createTextNode(`fle${String.fromCharCode(0x200b)}word`),
        );
        div.appendChild(p);
        document.body.appendChild(div);
        // sanity check the setup
        expect(p.childNodes.length).toBe(2);

        EditableDivUtils.mergeAdjacentTextNodes(div);

        expect(p.childNodes.length).toBe(1);
        expect(p.textContent).toBe(`waffle${String.fromCharCode(0x200b)}word`);

        div.remove();
    });

    it("mergeAdjacentTextNodes leaves an already-normal paragraph alone", () => {
        const div = document.createElement("div");
        div.innerHTML = "<p>over<em>ff</em>low</p>";
        const before = div.innerHTML;

        EditableDivUtils.mergeAdjacentTextNodes(div);

        // In particular, it must not merge text across an element boundary.
        expect(div.innerHTML).toBe(before);
    });
});
