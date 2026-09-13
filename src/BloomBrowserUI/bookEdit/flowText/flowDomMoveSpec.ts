import { describe, expect, it } from "vitest";
import {
    getCombinedChainText,
    normalizeChainedEditable,
    pullOverflowBackward,
    pushOverflowForward,
    rebalanceAdjacentBoxes,
} from "./flowDomMove";

// CKEditor's own end-of-paragraph filler, U+200B ZERO WIDTH SPACE.
const kCkEditorFiller = String.fromCharCode(0x200b);

function makeEditable(innerHtml: string): HTMLElement {
    const editable = document.createElement("div");
    editable.className = "bloom-editable";
    editable.setAttribute("contenteditable", "true");
    editable.innerHTML = innerHtml;
    return editable;
}

describe("flowDomMove", () => {
    it("pushOverflowForward moves inline overflow into the next box without creating line paragraphs", () => {
        const current = makeEditable("<p>Hello <strong>world</strong></p>");
        const next = makeEditable(
            '<p data-flow-continuation="true"> again</p>',
        );

        const changed = pushOverflowForward(current, next, 8);

        expect(changed).toBe(true);
        expect(current.innerHTML).toBe("<p>Hello <strong>wo</strong></p>");
        expect(next.innerHTML).toBe(
            '<p data-flow-continuation="true"><strong>rld</strong> again</p>',
        );
        expect(next.querySelectorAll("p")).toHaveLength(1);
    });

    it("pushOverflowForward preserves multiple semantic paragraphs while spilling only the split paragraph", () => {
        const current = makeEditable(
            "<p>Alpha beta</p><p>Gamma <em>delta</em></p><p>Epsilon</p>",
        );
        const next = makeEditable("<p><br></p>");

        const changed = pushOverflowForward(current, next, 8);

        expect(changed).toBe(true);
        expect(current.innerHTML).toBe("<p>Alpha be</p>");
        expect(next.innerHTML).toBe(
            '<p data-flow-continuation="true">ta</p><p>Gamma <em>delta</em></p><p>Epsilon</p>',
        );
        expect(next.querySelectorAll("p")).toHaveLength(3);
    });

    it("pullOverflowBackward pulls a prefix from the next box into the current paragraph", () => {
        const current = makeEditable("<p>Hello <strong>wo</strong></p>");
        const next = makeEditable(
            '<p data-flow-continuation="true"><strong>rld</strong> again</p>',
        );

        const changed = pullOverflowBackward(current, next, 3);

        expect(changed).toBe(true);
        expect(current.innerHTML).toBe("<p>Hello <strong>world</strong></p>");
        expect(next.innerHTML).toBe(
            '<p data-flow-continuation="true"> again</p>',
        );
        expect(current.querySelectorAll("p")).toHaveLength(1);
    });

    it("pushOverflowForward leaves empty boxes with a structural paragraph instead of line wrappers", () => {
        const current = makeEditable("<p>Hi</p>");
        const next = makeEditable("<p><br></p>");

        const changed = pushOverflowForward(current, next, 0);

        expect(changed).toBe(true);
        expect(current.innerHTML).toBe("<p><br></p>");
        expect(next.innerHTML).toBe("<p>Hi</p>");
        expect(current.querySelectorAll("p")).toHaveLength(1);
        expect(next.querySelectorAll("p")).toHaveLength(1);
    });

    it("rebalanceAdjacentBoxes preserves semantic word order when more text spills into an existing continuation paragraph", () => {
        const current = makeEditable("<p>One two three four five</p>");
        const next = makeEditable('<p data-flow-continuation="true">six.</p>');

        const changed = rebalanceAdjacentBoxes(current, next, 9);

        expect(changed).toBe(true);
        expect(current.innerHTML).toBe("<p>One two t</p>");
        expect(next.innerHTML).toBe(
            '<p data-flow-continuation="true">hree four fivesix.</p>',
        );
    });

    it("marks a continuation paragraph with the attribute alone, never with the No Indent class", () => {
        const current = makeEditable("<p>One two three</p>");
        const next = makeEditable("<p><br></p>");

        pushOverflowForward(current, next, 4);

        const continuationParagraph = next.querySelector("p");
        expect(
            continuationParagraph?.getAttribute("data-flow-continuation"),
        ).toBe("true");
        expect(
            continuationParagraph?.classList.contains("bloom-noIndent"),
        ).toBe(false);
    });

    it("keeps the user's No Indent class on a paragraph that stops being a continuation", () => {
        const current = makeEditable("<p>One two </p>");
        const next = makeEditable(
            '<p data-flow-continuation="true" class="bloom-noIndent">three</p>',
        );

        pullOverflowBackward(current, next, 5);

        const merged = current.querySelector("p");
        expect(merged?.hasAttribute("data-flow-continuation")).toBe(false);
        expect(current.innerHTML).toContain("One two three");
    });

    it("normalizeChainedEditable keeps the continuation marker only on the first paragraph", () => {
        const editable = makeEditable(
            '<p data-flow-continuation="true">one</p>' +
                '<p data-flow-continuation="true">two</p>',
        );

        normalizeChainedEditable(editable);

        const paragraphs = editable.querySelectorAll("p");
        expect(paragraphs).toHaveLength(2);
        expect(paragraphs[0].hasAttribute("data-flow-continuation")).toBe(true);
        expect(paragraphs[1].hasAttribute("data-flow-continuation")).toBe(
            false,
        );
    });

    it("treats an empty next box as contributing no text at all", () => {
        const current = makeEditable("<p>One two three</p>");
        const next = makeEditable("<p><br></p>");

        expect(getCombinedChainText(current, next)).toBe("One two three\n");
    });

    it("does not turn an empty box's placeholder paragraph into a paragraph break", () => {
        // Everything fits, so nothing should move and neither box should gain a paragraph.
        const current = makeEditable("<p>One two three</p>");
        const next = makeEditable("<p><br></p>");

        const changed = rebalanceAdjacentBoxes(current, next, 500);

        expect(changed).toBe(false);
        expect(current.innerHTML).toBe("<p>One two three</p>");
        expect(next.innerHTML).toBe("<p><br></p>");
        expect(current.querySelectorAll("p")).toHaveLength(1);
    });

    it("leaves no trailing empty paragraph in the box that receives the tail", () => {
        const current = makeEditable("<p>One two three four</p>");
        const next = makeEditable("<p><br></p>");

        rebalanceAdjacentBoxes(current, next, 8);

        // The cut falls on a space, and neither half can hold it, so the attribute records it.
        expect(current.innerHTML).toBe("<p>One two</p>");
        expect(next.innerHTML).toBe(
            '<p data-flow-continuation="true" data-flow-seam-space="true">three four</p>',
        );
        expect(next.querySelectorAll("p")).toHaveLength(1);
    });

    it("records the seam space in the attribute wherever the caller asked to cut", () => {
        const current = makeEditable("<p>One two three four</p>");
        const next = makeEditable("<p><br></p>");
        // Sanity check: the caller asks to cut one character past the space, and the cut has to
        // move back onto it, because neither half of the paragraph can hold the space itself.
        expect(getCombinedChainText(current, next)).toBe(
            "One two three four\n",
        );

        rebalanceAdjacentBoxes(current, next, 8);

        expect(current.textContent).toBe("One two");
        expect(next.textContent).toBe("three four");
        expect(
            next.querySelector("p")!.getAttribute("data-flow-seam-space"),
        ).toBe("true");
    });

    it("leaves a paragraph break where it is", () => {
        const current = makeEditable("<p>One two</p><p>three four</p>");
        const next = makeEditable("<p><br></p>");

        // Cut at the paragraph break: "One two" is 7 characters and the break is the eighth.
        rebalanceAdjacentBoxes(current, next, 8);

        expect(current.textContent).toBe("One two");
        expect(next.textContent).toBe("three four");
        expect(current.querySelectorAll("p")).toHaveLength(1);
        expect(next.querySelectorAll("p")).toHaveLength(1);
        // The text after a break starts its own paragraph, so it continues nothing.
        expect(
            next.querySelector("p")?.hasAttribute("data-flow-continuation"),
        ).toBe(false);
    });

    it("joins the halves back with one space when the box has CKEditor's filler at its end", () => {
        // What the box holds after the user has edited it: the real space at the end of the
        // paragraph is gone, and a zero-width filler stands there instead.
        const current = makeEditable(`<p>One two${kCkEditorFiller}</p>`);
        const next = makeEditable(
            '<p data-flow-continuation="true" data-flow-seam-space="true">three four</p>',
        );

        expect(
            getCombinedChainText(current, next).replace(kCkEditorFiller, ""),
        ).toBe("One two three four\n");
    });

    it("keeps a continuation marker when the box before it is empty", () => {
        const current = makeEditable("<p><br></p>");
        const next = makeEditable(
            '<p data-flow-continuation="true">tail text</p>',
        );

        expect(getCombinedChainText(current, next)).toBe("tail text\n");

        rebalanceAdjacentBoxes(current, next, 5);

        expect(
            current.querySelector("p")?.getAttribute("data-flow-continuation"),
        ).toBe("true");
        expect(
            next.querySelector("p")?.getAttribute("data-flow-continuation"),
        ).toBe("true");
    });
});
