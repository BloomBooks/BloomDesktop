import { describe, expect, it } from "vitest";
import {
    normalizeChainedEditable,
    pullOverflowBackward,
    pushOverflowForward,
    rebalanceAdjacentBoxes,
} from "./flowDomMove";

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
            '<p data-flow-continuation="true" class="bloom-noIndent"> again</p>',
        );

        const changed = pushOverflowForward(current, next, 8);

        expect(changed).toBe(true);
        expect(current.innerHTML).toBe("<p>Hello <strong>wo</strong></p>");
        expect(next.innerHTML).toBe(
            '<p data-flow-continuation="true" class="bloom-noIndent"><strong>rld</strong> again</p>',
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
            '<p data-flow-continuation="true" class="bloom-noIndent">ta</p><p>Gamma <em>delta</em></p><p>Epsilon</p>',
        );
        expect(next.querySelectorAll("p")).toHaveLength(3);
    });

    it("pullOverflowBackward pulls a prefix from the next box into the current paragraph", () => {
        const current = makeEditable("<p>Hello <strong>wo</strong></p>");
        const next = makeEditable(
            '<p data-flow-continuation="true" class="bloom-noIndent"><strong>rld</strong> again</p>',
        );

        const changed = pullOverflowBackward(current, next, 3);

        expect(changed).toBe(true);
        expect(current.innerHTML).toBe("<p>Hello <strong>world</strong></p>");
        expect(next.innerHTML).toBe(
            '<p data-flow-continuation="true" class="bloom-noIndent"> again</p>',
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
        const next = makeEditable(
            '<p data-flow-continuation="true" class="bloom-noIndent">six.</p>',
        );

        const changed = rebalanceAdjacentBoxes(current, next, 9);

        expect(changed).toBe(true);
        expect(current.innerHTML).toBe("<p>One two t</p>");
        expect(next.innerHTML).toBe(
            '<p data-flow-continuation="true" class="bloom-noIndent">hree four fivesix.</p>',
        );
    });

    it("normalizeChainedEditable keeps the continuation marker only on the first paragraph", () => {
        const editable = makeEditable(
            '<p data-flow-continuation="true" class="bloom-noIndent">one</p>' +
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
});
