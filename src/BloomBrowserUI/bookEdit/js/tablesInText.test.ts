import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import {
    decideTableGuard,
    deleteAllButTables,
    getInsertTableMenuItem,
    insertTableInText,
    selectionTouchesTableInText,
    tableInTextMayShowItsButtons,
} from "./tablesInText";
import { EditableDivUtils } from "./editableDivUtils";
import { kTableInTextClass } from "./tableInTextConstants";

// Attaching a table needs the bloom-table library and Bloom's page setup, which these tests are
// not about; they check where the table goes and what the menu offers.
vi.mock("./tableEditing", () => ({
    AttachNewTableAndSetUpItsCells: vi.fn(),
}));

function setupPage(): void {
    document.body.innerHTML = `
        <div class="bloom-page" data-page-id="tables-in-text-test">
            <div class="bloom-translationGroup normal-style" id="group">
                <div class="bloom-editable normal-style bloom-content1" lang="en" id="text"><p id="first">First half and second half.</p><p id="second">Last paragraph.</p></div>
            </div>
            <div class="bloom-canvas">
                <div class="bloom-canvas-element">
                    <div class="bloom-translationGroup">
                        <div class="bloom-editable"><p id="canvasParagraph">Canvas text</p></div>
                    </div>
                </div>
            </div>
        </div>`;
}

function element(id: string): HTMLElement {
    const result = document.getElementById(id);
    if (!result)
        throw new Error(`test setup is broken: no element with id ${id}`);
    return result;
}

// A caret at `offset` characters into the text of the paragraph with this id.
function caretIn(paragraphId: string, offset: number): Range {
    const text = element(paragraphId).firstChild as Text;
    const range = document.createRange();
    range.setStart(text, offset);
    range.collapse(true);
    return range;
}

// The editable's children, with a table shown as [table] and a paragraph as its text.
function childrenOfText(): string[] {
    return Array.from(element("text").children).map((child) =>
        child.classList.contains(kTableInTextClass)
            ? "[table]"
            : `${child.tagName}:${child.textContent}`,
    );
}

describe("insertTableInText", () => {
    beforeEach(setupPage);
    afterEach(() => (document.body.innerHTML = ""));

    it("splits the paragraph at a caret in the middle of it", () => {
        expect(element("first").textContent).toBe(
            "First half and second half.",
        );
        insertTableInText(element("text"), caretIn("first", 10));
        expect(childrenOfText()).toEqual([
            "P:First half",
            "[table]",
            "P: and second half.",
            "P:Last paragraph.",
        ]);
    });

    it("leaves an id only in the first half of a recorded sentence that the table splits", () => {
        element("first").innerHTML =
            "<span id='sentence' class='audio-sentence'>First half and second half.</span>";
        insertTableInText(element("text"), caretIn("sentence", 10));

        const halves = Array.from(
            element("text").querySelectorAll<HTMLElement>(
                "span.audio-sentence",
            ),
        );
        expect(halves.map((half) => half.textContent)).toEqual([
            "First half",
            " and second half.",
        ]);
        expect(halves.map((half) => half.id)).toEqual(["sentence", ""]);
    });

    it("keeps the id of a recorded sentence that the table goes just before", () => {
        element("first").innerHTML =
            "<span id='one' class='audio-sentence'>First sentence. </span><span id='two' class='audio-sentence'>Second sentence.</span>";

        insertTableInText(element("text"), caretIn("two", 0));

        const sentences = Array.from(
            element("text").querySelectorAll<HTMLElement>(
                "span.audio-sentence",
            ),
        );
        expect(sentences.map((s) => `${s.id}:${s.textContent}`)).toEqual([
            "one:First sentence. ",
            "two:Second sentence.",
        ]);
        expect(childrenOfText()).toEqual([
            "P:First sentence. ",
            "[table]",
            "P:Second sentence.",
            "P:Last paragraph.",
        ]);
    });

    it("keeps the id of a recorded sentence that the table goes just after", () => {
        element("first").innerHTML =
            "<span id='one' class='audio-sentence'>First sentence.</span><span id='two' class='audio-sentence'> Second sentence.</span>";

        insertTableInText(element("text"), caretIn("one", 15));

        const sentences = Array.from(
            element("text").querySelectorAll<HTMLElement>(
                "span.audio-sentence",
            ),
        );
        expect(sentences.map((s) => `${s.id}:${s.textContent}`)).toEqual([
            "one:First sentence.",
            "two: Second sentence.",
        ]);
        expect(childrenOfText()).toEqual([
            "P:First sentence.",
            "[table]",
            "P: Second sentence.",
            "P:Last paragraph.",
        ]);
    });

    it("gives a recorded sentence's id to the half with its words when the other half is only a line break", () => {
        element("first").innerHTML =
            "<span id='one' class='audio-sentence'><span class='bloom-linebreak'></span>Good morning.</span>";
        const words = element("one").lastChild as Text;
        const caret = document.createRange();
        caret.setStart(words, 0);
        caret.collapse(true);

        const table = insertTableInText(element("text"), caret);

        expect(
            table.previousElementSibling!.querySelector("span.bloom-linebreak"),
        ).not.toBeNull();
        expect(element("one").textContent).toBe("Good morning.");
        expect(table.nextElementSibling!.contains(element("one"))).toBe(true);
    });

    it.each([
        [
            "after",
            "Words.<span class='bloom-inlineImage'><img src='a.png'></span>",
        ],
        [
            "before",
            "<span class='bloom-inlineImage'><img src='a.png'></span>Words.",
        ],
    ])(
        "keeps an inline picture that is all of the paragraph %s the caret",
        (side, html) => {
            element("first").innerHTML = html;
            const words = Array.from(element("first").childNodes).find(
                (node) => node.nodeType === Node.TEXT_NODE,
            ) as Text;
            const caret = document.createRange();
            caret.setStart(words, side === "after" ? words.length : 0);
            caret.collapse(true);

            insertTableInText(element("text"), caret);

            expect(element("text").querySelectorAll("img").length).toBe(1);
            expect(childrenOfText()).toContain("[table]");
        },
    );

    it.each([
        ["<br>", "br"],
        ["<span class='bloom-linebreak'></span>", "span.bloom-linebreak"],
    ])(
        "keeps a line break (%s) that is all the split leaves of a recorded sentence",
        (lineBreak, lineBreakSelector) => {
            element("first").innerHTML =
                `<span id='one' class='audio-sentence'>First sentence.${lineBreak}</span><span id='two' class='audio-sentence'>Second.</span>`;
            const sentenceText = element("one").firstChild as Text;
            const caret = document.createRange();
            caret.setStart(sentenceText, sentenceText.length);
            caret.collapse(true);

            const table = insertTableInText(element("text"), caret);

            // The line break was after the caret, so it is in the second half, in that half's part of
            // the sentence, and not a placeholder added for an empty paragraph.
            expect(
                table.nextElementSibling!.querySelector(
                    `span.audio-sentence ${lineBreakSelector}`,
                ),
            ).not.toBeNull();
            expect(
                element("text").querySelectorAll(lineBreakSelector).length,
            ).toBe(1);
        },
    );

    it("keeps the id of a recorded sentence when the table goes in at the start of its bold text", () => {
        element("first").innerHTML =
            "<span id='one' class='audio-sentence'>First sentence. </span><span id='two' class='audio-sentence'><strong id='bold'>Bold</strong> rest.</span>";

        insertTableInText(element("text"), caretIn("bold", 0));

        const sentences = Array.from(
            element("text").querySelectorAll<HTMLElement>(
                "span.audio-sentence",
            ),
        );
        expect(sentences.map((s) => `${s.id}:${s.textContent}`)).toEqual([
            "one:First sentence. ",
            "two:Bold rest.",
        ]);
        expect(childrenOfText()).toEqual([
            "P:First sentence. ",
            "[table]",
            "P:Bold rest.",
            "P:Last paragraph.",
        ]);
    });

    it("splits a heading like a paragraph, and the second half stays a heading", () => {
        element("text").innerHTML =
            "<h2 id='heading'>Heading text</h2><p id='second'>Last paragraph.</p>";

        insertTableInText(element("text"), caretIn("heading", 7));

        expect(childrenOfText()).toEqual([
            "H2:Heading",
            "[table]",
            "H2: text",
            "P:Last paragraph.",
        ]);
    });

    it("adds no empty paragraph between a table and a heading next to it", () => {
        element("text").innerHTML =
            "<h2 id='heading'>Heading text</h2><p id='second'>Last paragraph.</p>";

        insertTableInText(element("text"), caretIn("heading", 12));

        expect(childrenOfText()).toEqual([
            "H2:Heading text",
            "[table]",
            "P:Last paragraph.",
        ]);
    });

    it("puts the table before the paragraph for a caret at its start", () => {
        insertTableInText(element("text"), caretIn("second", 0));
        // A paragraph before the table already exists, so no empty one is added.
        expect(childrenOfText()).toEqual([
            "P:First half and second half.",
            "[table]",
            "P:Last paragraph.",
        ]);
    });

    it("puts the table after the paragraph for a caret at its end, with no empty paragraph between", () => {
        insertTableInText(element("text"), caretIn("first", 27));
        expect(childrenOfText()).toEqual([
            "P:First half and second half.",
            "[table]",
            "P:Last paragraph.",
        ]);
    });

    it("adds an empty paragraph after a table that ends the text, so the caret can get there", () => {
        insertTableInText(element("text"), caretIn("second", 15));
        expect(childrenOfText()).toEqual([
            "P:First half and second half.",
            "P:Last paragraph.",
            "[table]",
            "P:",
        ]);
    });

    it("adds an empty paragraph before a table that starts the text", () => {
        insertTableInText(element("text"), caretIn("first", 0));
        expect(childrenOfText()).toEqual([
            "P:",
            "[table]",
            "P:First half and second half.",
            "P:Last paragraph.",
        ]);
    });

    it("puts the table at the end of the text when there is no caret", () => {
        insertTableInText(element("text"), undefined);
        expect(childrenOfText()).toEqual([
            "P:First half and second half.",
            "P:Last paragraph.",
            "[table]",
            "P:",
        ]);
    });

    it("makes the table one unit in the text, so the browser does not edit into it", () => {
        const table = insertTableInText(element("text"), caretIn("first", 10));
        expect(table.getAttribute("contenteditable")).toBe("false");
        expect(table.classList.contains("bloom-table")).toBe(true);
    });
});

describe("getInsertTableMenuItem", () => {
    beforeEach(setupPage);
    afterEach(() => (document.body.innerHTML = ""));

    it("is offered in the text of an ordinary text box", () => {
        expect(getInsertTableMenuItem(element("first"), () => {})?.l10nId).toBe(
            "EditTab.TableInText.InsertTable",
        );
    });

    it("is not offered in a canvas element", () => {
        expect(
            getInsertTableMenuItem(element("canvasParagraph"), () => {}),
        ).toBeUndefined();
    });

    it("is not offered in a table's cells", () => {
        const table = insertTableInText(element("text"), caretIn("first", 10));
        // The table library builds the cells; this one stands in for what it builds.
        table.innerHTML = `<div class="bloom-cell"><div class="bloom-translationGroup"><div class="bloom-editable" lang="en"><p id="cellParagraph">Cell</p></div></div></div>`;
        expect(
            getInsertTableMenuItem(element("cellParagraph"), () => {}),
        ).toBeUndefined();
    });

    it("puts the table where the caret was when the menu opened, not where it is when chosen", () => {
        const selection = document.getSelection()!;
        selection.removeAllRanges();
        selection.addRange(caretIn("first", 10));
        const item = getInsertTableMenuItem(element("first"), () => {});
        // Choosing a command from the menu moves the focus; the caret is no longer in the text.
        selection.removeAllRanges();
        item!.onClick!(undefined as never);
        expect(childrenOfText()).toEqual([
            "P:First half",
            "[table]",
            "P: and second half.",
            "P:Last paragraph.",
        ]);
    });
});

describe("replaceContentKeepingLiveImagesAndTables", () => {
    beforeEach(setupPage);
    afterEach(() => (document.body.innerHTML = ""));

    it("fills an empty scratch div with HTML that holds a table, as Talking Book does with CKEditor's data", () => {
        insertTableInText(element("text"), caretIn("first", 10));
        const html = element("text").innerHTML;
        const scratch = document.createElement("div");
        // Sanity check: the HTML has a table and the scratch div has none to keep.
        expect(html).toContain(kTableInTextClass);

        EditableDivUtils.replaceContentKeepingLiveImagesAndTables(
            scratch,
            html,
        );

        expect(scratch.querySelectorAll(`.${kTableInTextClass}`).length).toBe(
            1,
        );
        expect(scratch.textContent).toContain("Last paragraph.");
    });

    it("keeps the live table, with what was typed in its cells, in place of the copy in the new HTML", () => {
        const table = insertTableInText(element("text"), caretIn("first", 10));
        table.innerHTML = `<div class="bloom-cell"><p>typed in the cell</p></div>`;
        // HTML from before the cell was typed in, as CKEditor's copy of the box would be.
        const olderHtml = element("text").innerHTML.replace(
            "typed in the cell",
            "",
        );
        expect(olderHtml).not.toContain("typed in the cell");

        EditableDivUtils.replaceContentKeepingLiveImagesAndTables(
            element("text"),
            olderHtml.replace("Last paragraph.", "Last paragraph, edited."),
        );

        // The same element is back, so the table library and the cells' setup still work on it.
        expect(element("text").querySelector("." + kTableInTextClass)).toBe(
            table,
        );
        expect(table.textContent).toBe("typed in the cell");
        // And the rest of the HTML did replace the box's content.
        expect(childrenOfText()).toEqual([
            "P:First half",
            "[table]",
            "P: and second half.",
            "P:Last paragraph, edited.",
        ]);
    });

    it("refuses HTML whose tables do not match the box's", () => {
        insertTableInText(element("text"), caretIn("first", 10));
        expect(() =>
            EditableDivUtils.replaceContentKeepingLiveImagesAndTables(
                element("text"),
                "<p>No table here</p>",
            ),
        ).toThrow();
    });
});

describe("decideTableGuard", () => {
    // The box after insertTableInText at "First half|": First half, [table], " and second half.",
    // Last paragraph.
    let table: HTMLElement;
    beforeEach(() => {
        setupPage();
        table = insertTableInText(element("text"), caretIn("first", 10));
        table.innerHTML = `<div class="bloom-cell"><div class="bloom-translationGroup"><div class="bloom-editable" lang="en"><p id="cellParagraph">Cell text</p></div></div></div>`;
    });
    afterEach(() => (document.body.innerHTML = ""));

    const paragraphAfter = () => table.nextElementSibling as HTMLElement;
    const caretAt = (node: Node, offset: number) => {
        const range = document.createRange();
        range.setStart(node, offset);
        range.collapse(true);
        return range;
    };

    it("blocks Backspace at the start of the paragraph after a table", () => {
        expect(paragraphAfter().textContent).toBe(" and second half.");
        expect(
            decideTableGuard(
                element("text"),
                caretAt(paragraphAfter().firstChild!, 0),
                "Backspace",
                false,
            ),
        ).toBe("block");
    });

    it("allows Backspace anywhere else in that paragraph", () => {
        expect(
            decideTableGuard(
                element("text"),
                caretAt(paragraphAfter().firstChild!, 4),
                "Backspace",
                false,
            ),
        ).toBe("allow");
    });

    it("blocks Delete at the end of the paragraph before a table", () => {
        expect(
            decideTableGuard(
                element("text"),
                caretAt(element("first").firstChild!, 10),
                "Delete",
                false,
            ),
        ).toBe("block");
    });

    it("allows Backspace at the start of a paragraph that does not follow a table", () => {
        expect(
            decideTableGuard(
                element("text"),
                caretAt(element("second").firstChild!, 0),
                "Backspace",
                false,
            ),
        ).toBe("allow");
    });

    it("deletes around the table for Backspace on a selection that takes it in, and blocks typing over it", () => {
        const range = document.createRange();
        range.setStart(element("first").firstChild!, 5);
        range.setEnd(paragraphAfter().firstChild!, 4);
        expect(
            decideTableGuard(element("text"), range, "Backspace", false),
        ).toBe("deleteAroundTables");
        expect(decideTableGuard(element("text"), range, "a", false)).toBe(
            "block",
        );
        expect(decideTableGuard(element("text"), range, "x", true)).toBe(
            "block",
        );
        // Copying does not change the text.
        expect(decideTableGuard(element("text"), range, "c", true)).toBe(
            "allow",
        );
    });

    it("leaves a selection inside one cell alone", () => {
        const cellText = element("cellParagraph").firstChild!;
        const range = document.createRange();
        range.setStart(cellText, 0);
        range.setEnd(cellText, 4);
        expect(
            decideTableGuard(element("text"), range, "Backspace", false),
        ).toBe("allow");
    });
});

describe("deleteAllButTables", () => {
    beforeEach(setupPage);
    afterEach(() => (document.body.innerHTML = ""));

    it("deletes the selected text on both sides of the table and keeps the table", () => {
        const table = insertTableInText(element("text"), caretIn("first", 10));
        const range = document.createRange();
        // From "First" through " and" in the paragraph after the table.
        range.setStart(element("first").firstChild!, 5);
        range.setEnd(table.nextElementSibling!.firstChild!, 4);

        deleteAllButTables(element("text"), range);

        expect(childrenOfText()).toEqual([
            "P:First",
            "[table]",
            "P: second half.",
            "P:Last paragraph.",
        ]);
        expect(element("text").querySelector("." + kTableInTextClass)).toBe(
            table,
        );
    });

    it("keeps a paragraph on each side of the table when everything is selected", () => {
        const table = insertTableInText(element("text"), caretIn("first", 10));
        const range = document.createRange();
        range.selectNodeContents(element("text"));

        deleteAllButTables(element("text"), range);

        expect(element("text").textContent).toBe("");
        expect(element("text").querySelector("." + kTableInTextClass)).toBe(
            table,
        );
        expect(table.previousElementSibling?.tagName).toBe("P");
        expect(table.nextElementSibling?.tagName).toBe("P");
    });
});

describe("tableInTextMayShowItsButtons", () => {
    beforeEach(setupPage);
    afterEach(() => (document.body.innerHTML = ""));

    function tableWithACell(): HTMLElement {
        const table = insertTableInText(element("text"), caretIn("first", 10));
        table.innerHTML = `<div class="bloom-cell"><div class="bloom-translationGroup"><div class="bloom-editable" lang="en" contenteditable="true" id="cellBox"><p>Cell</p></div></div></div>`;
        return table;
    }

    it("hides the buttons of a table in text that is not selected", () => {
        const table = tableWithACell();
        element("text").setAttribute("tabindex", "0");
        element("text").focus();
        expect(table.contains(document.activeElement)).toBe(false);
        expect(tableInTextMayShowItsButtons(table)).toBe(false);
    });

    it("shows them while the focus is in one of its cells", () => {
        const table = tableWithACell();
        element("cellBox").focus();
        expect(document.activeElement).toBe(element("cellBox"));
        expect(tableInTextMayShowItsButtons(table)).toBe(true);
    });

    it("hides them once the focus has left, even with a cell still marked selected", () => {
        const table = tableWithACell();
        // The library leaves this mark on a cell after the focus has moved elsewhere.
        table.querySelector(".bloom-cell")!.classList.add("cell--selected");
        element("text").setAttribute("tabindex", "0");
        element("text").focus();
        expect(tableInTextMayShowItsButtons(table)).toBe(false);
    });

    it("leaves every other table to the library", () => {
        const otherTable = document.createElement("div");
        otherTable.className = "bloom-table";
        document.body.appendChild(otherTable);
        expect(tableInTextMayShowItsButtons(otherTable)).toBe(true);
    });
});

describe("selectionTouchesTableInText", () => {
    beforeEach(setupPage);
    afterEach(() => (document.body.innerHTML = ""));

    const select = (
        start: Node,
        startOffset: number,
        end: Node,
        endOffset: number,
    ) => {
        const range = document.createRange();
        range.setStart(start, startOffset);
        range.setEnd(end, endOffset);
        document.getSelection()!.removeAllRanges();
        document.getSelection()!.addRange(range);
    };

    it("is true for a selection from above a table to below it, and false inside a cell or in plain text", () => {
        const table = insertTableInText(element("text"), caretIn("first", 10));
        table.innerHTML = `<div class="bloom-cell"><div class="bloom-translationGroup"><div class="bloom-editable" lang="en"><p id="cellParagraph">Cell text</p></div></div></div>`;
        const cellText = element("cellParagraph").firstChild!;
        const lastText = element("second").firstChild!;

        select(element("first").firstChild!, 2, lastText, 2);
        expect(selectionTouchesTableInText()).toBe(true);
        select(cellText, 0, cellText, 4);
        expect(selectionTouchesTableInText()).toBe(false);
        select(lastText, 0, lastText, 4);
        expect(selectionTouchesTableInText()).toBe(false);
    });
});
