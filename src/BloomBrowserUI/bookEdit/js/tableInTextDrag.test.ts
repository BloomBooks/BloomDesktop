import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { dropTargetFor, moveTableInText } from "./tableInTextDrag";
import { insertTableInText } from "./tablesInText";
import { kTableInTextClass } from "./tableInTextConstants";

// Attaching a table needs the bloom-table library and Bloom's page setup, which these tests are
// not about; they check where a dragged table goes.
vi.mock("./tableEditing", () => ({
    AttachNewTableAndSetUpItsCells: vi.fn(),
}));

let table: HTMLElement;

// A box whose text is "First half", a table, " and second half.", "Last paragraph.", and a second
// box with text of its own.
beforeEach(() => {
    document.body.innerHTML = `
        <div class="bloom-page">
            <div class="bloom-translationGroup">
                <div class="bloom-editable" lang="en" id="text"><p id="first">First half and second half.</p><p id="last">Last paragraph.</p></div>
                <div class="bloom-editable" lang="fr" id="otherBox"><p id="otherText">Autre texte.</p></div>
            </div>
        </div>`;
    const caret = document.createRange();
    caret.setStart(element("first").firstChild!, 10);
    caret.collapse(true);
    table = insertTableInText(element("text"), caret);
    // Stands in for what the table library builds.
    table.innerHTML = `<div class="bloom-cell"><div class="bloom-translationGroup"><div class="bloom-editable" lang="en" tabindex="0" id="cellBox"><p id="cellText">In the cell</p></div></div></div>`;
});
afterEach(() => (document.body.innerHTML = ""));

function element(id: string): HTMLElement {
    const result = document.getElementById(id);
    if (!result)
        throw new Error(`test setup is broken: no element with id ${id}`);
    return result;
}

function caretIn(id: string, offset: number): Range {
    const range = document.createRange();
    range.setStart(element(id).firstChild!, offset);
    range.collapse(true);
    return range;
}

function childrenOfText(): string[] {
    return Array.from(element("text").children).map((child) =>
        child.classList.contains(kTableInTextClass)
            ? "[table]"
            : `${child.tagName}:${child.textContent}`,
    );
}

describe("dropTargetFor", () => {
    it("splits a paragraph for a caret in the middle of it", () => {
        expect(
            dropTargetFor(element("text"), table, caretIn("last", 4))?.kind,
        ).toBe("split");
    });

    it("shows a line for a caret at the start or end of a paragraph", () => {
        expect(
            dropTargetFor(element("text"), table, caretIn("last", 0))?.kind,
        ).toBe("line");
        expect(
            dropTargetFor(element("text"), table, caretIn("last", 15))?.kind,
        ).toBe("line");
    });

    it("refuses a drop inside the table itself", () => {
        expect(
            dropTargetFor(element("text"), table, caretIn("cellText", 3)),
        ).toBeUndefined();
    });

    it("refuses a drop in another box", () => {
        expect(
            dropTargetFor(element("text"), table, caretIn("otherText", 3)),
        ).toBeUndefined();
    });
});

describe("moveTableInText", () => {
    it("moves the same table into the middle of a later paragraph, splitting it", () => {
        expect(childrenOfText()).toEqual([
            "P:First half",
            "[table]",
            "P: and second half.",
            "P:Last paragraph.",
        ]);

        moveTableInText(element("text"), table, caretIn("last", 4));

        expect(childrenOfText()).toEqual([
            "P:First half",
            "P: and second half.",
            "P:Last",
            "[table]",
            "P: paragraph.",
        ]);
        // The very element, so the table library and its cells' text boxes still work on it.
        expect(element("text").querySelector("." + kTableInTextClass)).toBe(
            table,
        );
        expect(element("cellText").textContent).toBe("In the cell");
    });

    it("keeps the focus in the table, so that it stays selected", () => {
        element("cellBox").focus();
        expect(document.activeElement).toBe(element("cellBox"));

        moveTableInText(element("text"), table, caretIn("last", 4));

        expect(document.activeElement).toBe(element("cellBox"));
    });

    it("moves the table to the end of the text, with a paragraph after it", () => {
        moveTableInText(element("text"), table, caretIn("last", 15));

        expect(childrenOfText()).toEqual([
            "P:First half",
            "P: and second half.",
            "P:Last paragraph.",
            "[table]",
            "P:",
        ]);
    });
});
