import { describe, it, expect, beforeEach, afterEach } from "vitest";
import {
    canToggleNoIndent,
    findParagraphForTextContextMenu,
    isNoIndentOn,
    kNoIndentClass,
    toggleNoIndent,
} from "./noIndent";

// Builds a page with an ordinary text box (two paragraphs) and a canvas element that also
// contains text, so we can check which right-clicks the text context menu claims.
function setupPage(): void {
    document.body.innerHTML = `
        <div class="bloom-page">
            <div class="bloom-translationGroup normal-style">
                <div class="bloom-editable normal-style" id="ordinaryText">
                    <p id="first">First paragraph <em id="emphasis">with emphasis</em></p>
                    <p id="second">Second paragraph</p>
                </div>
            </div>
            <div class="bloom-canvas">
                <div class="bloom-canvas-element">
                    <div class="bloom-translationGroup">
                        <div class="bloom-editable">
                            <p id="canvasParagraph">Canvas text</p>
                        </div>
                    </div>
                </div>
            </div>
            <div id="notText">Not in a text box at all</div>
        </div>`;
}

function element(id: string): HTMLElement {
    const result = document.getElementById(id);
    if (!result)
        throw new Error(`test setup is broken: no element with id ${id}`);
    return result;
}

describe("findParagraphForTextContextMenu", () => {
    beforeEach(setupPage);
    afterEach(() => (document.body.innerHTML = ""));

    it("finds the paragraph when the click is on the paragraph itself", () => {
        expect(findParagraphForTextContextMenu(element("first"))).toBe(
            element("first"),
        );
    });

    it("finds the enclosing paragraph when the click is on something inside it", () => {
        expect(findParagraphForTextContextMenu(element("emphasis"))).toBe(
            element("first"),
        );
    });

    it("ignores a click on a paragraph inside a canvas element", () => {
        // sanity check: it really is a paragraph of a bloom-editable, so only the
        // canvas-element test can be what rejects it
        expect(
            element("canvasParagraph").closest(".bloom-editable"),
        ).toBeTruthy();
        expect(
            findParagraphForTextContextMenu(element("canvasParagraph")),
        ).toBeUndefined();
    });

    it("ignores a click that is not in a paragraph of a bloom-editable", () => {
        expect(
            findParagraphForTextContextMenu(element("notText")),
        ).toBeUndefined();
        expect(
            findParagraphForTextContextMenu(element("ordinaryText")),
        ).toBeUndefined();
    });

    it("ignores a non-element target", () => {
        expect(findParagraphForTextContextMenu(null)).toBeUndefined();
        expect(findParagraphForTextContextMenu(document)).toBeUndefined();
    });
});

describe("toggleNoIndent", () => {
    beforeEach(setupPage);
    afterEach(() => (document.body.innerHTML = ""));

    it("adds and removes the class on just the one paragraph", () => {
        const first = element("first");
        const second = element("second");
        expect(isNoIndentOn(first)).toBe(false); // sanity check

        toggleNoIndent(first);
        expect(first.classList.contains(kNoIndentClass)).toBe(true);
        expect(isNoIndentOn(first)).toBe(true);
        expect(isNoIndentOn(second)).toBe(false);

        toggleNoIndent(first);
        expect(first.classList.contains(kNoIndentClass)).toBe(false);
        expect(isNoIndentOn(first)).toBe(false);
    });
});

describe("canToggleNoIndent", () => {
    beforeEach(setupPage);
    afterEach(() => (document.body.innerHTML = ""));

    it("is false for a paragraph that is not indented and not marked", () => {
        expect(canToggleNoIndent(element("first"))).toBe(false);
    });

    it("is true for an indented paragraph, so that it can be turned on", () => {
        const first = element("first");
        first.style.textIndent = "20pt";
        expect(canToggleNoIndent(first)).toBe(true);
    });

    it("is false for a paragraph whose indent is explicitly zero", () => {
        const first = element("first");
        first.style.textIndent = "0pt";
        expect(canToggleNoIndent(first)).toBe(false);
    });

    it("is true for a hanging indent, which the command also cancels", () => {
        const first = element("first");
        first.style.textIndent = "-20pt";
        expect(canToggleNoIndent(first)).toBe(true);
    });

    it("is true for an already-marked paragraph, so that it can be turned off", () => {
        const first = element("first");
        // Once the class is on, the computed indent is zero (the real rule lives in
        // basePage-sharedRules.less); the item must stay enabled anyway.
        first.classList.add(kNoIndentClass);
        expect(canToggleNoIndent(first)).toBe(true);
    });
});
