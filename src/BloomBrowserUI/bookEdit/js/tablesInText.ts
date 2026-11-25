// Setting up tables that sit in the text of a text box (see docs/insert-table/README.md). The table
// is a direct child of a bloom-editable, between its paragraphs, and its cells hold text boxes of
// their own. Most of what that needs is done elsewhere: the table library sets up the table itself
// (SetupTableEditing), attachToCkEditor gives the cells no editor and keeps the live table when
// the outer box's editor loads, and replaceContentKeepingLiveImagesAndTables keeps it whenever
// something rewrites the outer box.

import * as React from "react";
import { default as InsertTableIcon } from "@mui/icons-material/TableChartOutlined";
import { refreshStructuralChrome } from "bloom-table";
import { elementOf, kTableInTextClass } from "./tableInTextConstants";
import { kBlockElementSelector } from "../bloomField/BloomField";
import { isUndoOrRedoKey } from "../undo/redoKeyBinding";
import {
    AttachNewTableAndSetUpItsCells,
    removeTableEditingMarkupFromClone,
} from "./tableEditing";
import { removeEditorChromeFromClone } from "./editorChromeCleanup";
import { setupTableInTextDragging } from "./tableInTextDrag";
import { getInlineImageActionTarget } from "./inlineImageInteractions";
import { IMenuItemWithSubmenu } from "./canvasElementManager/canvasControlMenuRendering";

/**
 * The Insert Table command for a right-click on `clickedElement`, or undefined where it is not
 * offered. It is offered in the same text boxes as Insert Image (not in a canvas element, an image
 * description, or a box whose text Bloom stores in the book's data), except that it is not offered
 * inside a table, and not on an inline image.
 *
 * The table goes where the caret is when the menu opens: a right-click in text puts the caret
 * where it was clicked. Call this when the menu opens, not when the command is chosen, because by
 * then the menu has the focus.
 */
export function getInsertTableMenuItem(
    clickedElement: HTMLElement | undefined,
    closeMenu: () => void,
): IMenuItemWithSubmenu | undefined {
    const target = getInlineImageActionTarget(clickedElement);
    if (target.kind !== "add") return undefined;
    // Not in a table's cells, and not on the lines between them, which are in the outer box.
    if (clickedElement?.closest(".bloom-table")) return undefined;
    const editable = target.editable;
    const selection = editable.ownerDocument.getSelection();
    const caret =
        selection && selection.rangeCount > 0
            ? selection.getRangeAt(0).cloneRange()
            : undefined;
    return {
        l10nId: "EditTab.TableInText.InsertTable",
        english: "Insert Table",
        icon: React.createElement(InsertTableIcon, null),
        featureName: "table",
        onClick: () => {
            closeMenu();
            insertTableInText(editable, caret);
        },
    };
}

/**
 * Put `table` into `editable`'s text at `caret`, and set it up for editing. Returns the table.
 * Without `table`, this makes a new two-by-two table; a table given here comes from
 * tableFromClipboardText.
 *
 * The table goes between paragraphs: a caret inside a paragraph splits it, with the table between
 * the two halves. A caret at the start or end of a paragraph puts the table before or after it,
 * with no empty paragraph left behind. A caret that is not in one of the editable's paragraphs
 * puts the table at the end of the text. The editable always ends up with a paragraph before and
 * after the table, so that the caret can reach both sides of it.
 */
export function insertTableInText(
    editable: HTMLElement,
    caret: Range | undefined,
    table: HTMLElement = editable.ownerDocument.createElement("div"),
): HTMLElement {
    const doc = editable.ownerDocument;
    table.classList.add("bloom-table", kTableInTextClass);
    // So that the table can be focused, as for a table in a canvas element.
    table.setAttribute("tabindex", "0");
    // So that the browser and the box's editor treat the table as one thing in the text. The table
    // library makes each cell's text boxes editable again.
    table.setAttribute("contenteditable", "false");
    placeBetweenParagraphs(editable, table, caret);
    ensureParagraphOnEachSide(table);
    AttachNewTableAndSetUpItsCells(table);
    keepPlainKeysInTable(table);
    setupTableInTextDragging(doc);
    return table;
}

/**
 * If `clipboardText` is a table that Copy Table or Cut Table (in a table's Table menu) put on the
 * clipboard, paste it into the text of the box that has the focus, as Insert Table would put a new
 * one there, in place of any text that is selected. Those commands put the table's HTML on the
 * clipboard as plain text, which a paste would otherwise put in the box as text. Does nothing, and
 * returns false, where Insert Table is not offered (in a table's cells, say) or the clipboard holds
 * anything else; the paste is then the caller's to do. Returns true when it pasted the table.
 */
export function pasteTableIntoText(clipboardText: string): boolean {
    const focused = document.activeElement as HTMLElement | null;
    const target = getInlineImageActionTarget(focused ?? undefined);
    if (target.kind !== "add" || focused?.closest(".bloom-table")) return false;
    const editable = target.editable;
    const table = tableFromClipboardText(editable.ownerDocument, clipboardText);
    if (!table) return false;
    const selection = editable.ownerDocument.getSelection();
    let caret: Range | undefined;
    if (selection && selection.rangeCount > 0) {
        caret = selection.getRangeAt(0);
        if (editable.contains(caret.commonAncestorContainer)) {
            caret.deleteContents();
            caret.collapse(true);
        } else caret = undefined;
    }
    insertTableInText(editable, caret, table);
    return true;
}

/**
 * The paste event's part of pasteTableIntoText: paste a table that Copy Table or Cut Table put on
 * the clipboard, and keep the box's CKEditor from also pasting its HTML as text. Listen in the
 * capture phase, so that this runs before CKEditor takes the paste.
 */
export function pasteTableIntoTextFromEvent(e: ClipboardEvent): void {
    if (!pasteTableIntoText(e.clipboardData?.getData("text/plain") ?? ""))
        return;
    e.preventDefault();
    e.stopImmediatePropagation();
}

/**
 * The table that `text` is the HTML of, made ready to go into `doc`, or undefined if `text` is not
 * the HTML of one table. The HTML is a copy of a live table (see pasteTableIntoText), so this takes
 * out what the live table's editing put in it, which would stop it being set up again, and the ids
 * that are already in `doc` (a recorded sentence's, say), which must stay unique. A table that was
 * cut keeps its ids, and with them its recordings.
 */
export function tableFromClipboardText(
    doc: Document,
    text: string,
): HTMLElement | undefined {
    if (!text.includes("bloom-table")) return undefined;
    // A template's content is parsed without running scripts or loading pictures.
    const template = doc.createElement("template");
    template.innerHTML = text.trim();
    const content = template.content;
    const element = content.firstElementChild;
    if (content.childNodes.length !== 1 || !element?.matches(".bloom-table"))
        return undefined;
    const holder = doc.createElement("div");
    const table = doc.importNode(element, true) as HTMLElement;
    holder.appendChild(table);
    removeTableEditingMarkupFromClone(holder);
    removeEditorChromeFromClone(holder);
    // Setting up a cell's text box (wireBloomContentOfNewCells) skips one that already has this.
    table
        .querySelectorAll(".bloom-editable")
        .forEach((box) => box.removeAttribute("contenteditable"));
    holder.querySelectorAll("[id]").forEach((withId) => {
        if (doc.getElementById(withId.id)) withId.removeAttribute("id");
    });
    return table;
}

/**
 * Put `table` into `editable`'s text at `caret`, as insertTableInText describes. A caret directly in
 * the editable, between two of its children, puts the table there.
 */
export function placeBetweenParagraphs(
    editable: HTMLElement,
    table: HTMLElement,
    caret: Range | undefined,
): void {
    if (caret?.startContainer === editable) {
        editable.insertBefore(
            table,
            editable.childNodes[caret.startOffset] ?? null,
        );
        return;
    }
    const paragraph = caret ? paragraphOf(editable, caret) : undefined;
    if (!caret || !paragraph) {
        editable.appendChild(table);
        return;
    }
    const rest = editable.ownerDocument.createRange();
    rest.setStart(caret.startContainer, caret.startOffset);
    rest.setEnd(paragraph, paragraph.childNodes.length);
    const restOfParagraph = rest.extractContents();
    if (isBlank(paragraph)) {
        // The caret was at the start: the table goes before the paragraph, which keeps its text.
        paragraph.replaceChildren(restOfParagraph);
        paragraph.before(table);
        return;
    }
    // An element that the caret was inside (a recorded sentence, say) is now in both halves, and
    // the copy in the second half has the same id. A half that the split left empty is removed,
    // so a sentence that the caret was at the edge of keeps its id, and with it its recording.
    // When both halves show something, the id goes with the words: to the first half, unless
    // only the second has words (the first holding only a line break, say).
    restOfParagraph.querySelectorAll("[id]").forEach((copy) => {
        const original = paragraph.querySelector(
            `[id="${CSS.escape(copy.id)}"]`,
        );
        if (!original) return;
        if (isEmptyElement(original)) original.remove();
        else if (isEmptyElement(copy)) copy.remove();
        else if (!hasWords(original) && hasWords(copy))
            original.removeAttribute("id");
        else copy.removeAttribute("id");
    });
    paragraph.after(table);
    if (!isBlank(restOfParagraph)) {
        // The same kind of paragraph (a heading stays a heading), with the same style.
        const secondHalf = paragraph.cloneNode(false) as HTMLElement;
        secondHalf.removeAttribute("id");
        secondHalf.appendChild(restOfParagraph);
        table.after(secondHalf);
    }
}

/**
 * The paragraph of `editable` that holds the caret, if the caret is in one of its own paragraphs.
 * A heading counts as a paragraph, as it does for the rest of Bloom's text code
 * (kBlockElementSelector).
 */
export function paragraphOf(
    editable: HTMLElement,
    caret: Range,
): HTMLElement | undefined {
    const paragraph = elementOf(caret.startContainer)?.closest<HTMLElement>(
        kBlockElementSelector,
    );
    if (!paragraph || paragraph.parentElement !== editable) return undefined;
    return paragraph;
}

// What shows in a paragraph although it has no text: a picture, and the empty span that
// Shift+Enter puts in.
const kShowsWithoutText = "img, span.bloom-linebreak";

// Whether `element` holds nothing that shows: no text, no line break and no picture. Empty
// formatting elements (an empty <strong>, say) do not count.
function isEmptyElement(element: Element): boolean {
    return (
        element.textContent === "" &&
        !element.querySelector(`br, ${kShowsWithoutText}`)
    );
}

// Whether `content` has text other than white space and the zero-width non-joiner that
// Shift+Enter puts after its line break.
function hasWords(content: Element | DocumentFragment): boolean {
    return (content.textContent ?? "").replace(/\u200C/g, "").trim() !== "";
}

// Whether `content` is no more than white space, empty formatting and a <br>, the browser's
// placeholder in an empty paragraph.
function isBlank(content: Element | DocumentFragment): boolean {
    return !hasWords(content) && !content.querySelector(kShowsWithoutText);
}

/**
 * Make sure `table` has a paragraph (or heading) right before it and right after it, adding empty
 * paragraphs where it does not.
 */
export function ensureParagraphOnEachSide(table: HTMLElement): void {
    const doc = table.ownerDocument;
    const emptyParagraph = () => {
        const p = doc.createElement("p");
        p.appendChild(doc.createElement("br"));
        return p;
    };
    if (!table.previousElementSibling?.matches(kBlockElementSelector))
        table.before(emptyParagraph());
    if (!table.nextElementSibling?.matches(kBlockElementSelector))
        table.after(emptyParagraph());
}

/**
 * Set up the tables in text in `container`: keep the plain keys typed in their cells to the cells
 * (keepPlainKeysInTable), and set up dragging them. Once per document, also guard the tables from
 * keys typed in the text around them, and refresh the tables' buttons when the focus moves.
 */
export function setupTablesInText(container: HTMLElement): void {
    const tables = container.querySelectorAll<HTMLElement>(
        `.${kTableInTextClass}`,
    );
    tables.forEach((table) => keepPlainKeysInTable(table));
    const doc = container.ownerDocument;
    if (tables.length > 0) setupTableInTextDragging(doc);
    if (!documentsWithTableGuard.has(doc)) {
        documentsWithTableGuard.add(doc);
        // Capture on the document, so that it runs before the box's CKEditor sees the key.
        doc.addEventListener("keydown", guardTablesInTextFromKey, true);
        // The library asks tableInTextMayShowItsButtons only as the pointer comes near a table, so
        // when the focus moves into or out of a table it has to be asked again.
        doc.addEventListener("focusin", () => refreshStructuralChrome(), true);
    }
}

/**
 * Whether the table library may show its buttons (add a row or column, the table and row and
 * column menus) on `table` now. A table in a box's text shows them only while it is selected, that
 * is, while the focus is in it: in a cell's text, or on a cell the library has selected, which it
 * gives the focus. (The library leaves a cell marked selected after the focus has gone elsewhere,
 * so that mark is no sign that the table is still selected.) The buttons sit just
 * outside the table, over the lines of text above and below it, so showing them whenever the
 * pointer comes near would put them in the way of clicking that text. Every other table shows them
 * as the library decides.
 */
export function tableInTextMayShowItsButtons(table: HTMLElement): boolean {
    if (!table.classList.contains(kTableInTextClass)) return true;
    const active = table.ownerDocument.activeElement;
    return !!active && table.contains(active);
}

const documentsWithTableGuard = new WeakSet<Document>();

/** What a key typed in the text around a table should do. See guardTablesInTextFromKey. */
type TableGuardDecision = "allow" | "block" | "deleteAroundTables";

/**
 * Decide what `key` (with Ctrl or the Windows key held if `withCommandKey`) should do to the text of
 * `editable` when the selection is `range`. A table in the text is deleted only through its own
 * Table menu, so:
 *
 * - Backspace at the start of the paragraph right after a table, and Delete at the end of the
 *   paragraph right before one, do nothing. Left to itself, the box's CKEditor selects the table on
 *   the first press and deletes it on the second.
 * - Backspace or Delete with a selection that takes in a table deletes the selected text but not
 *   the table ("deleteAroundTables").
 * - Anything else that would replace such a selection (typing, Enter, cut, paste) does nothing.
 */
export function decideTableGuard(
    editable: HTMLElement,
    range: Range,
    key: string,
    withCommandKey: boolean,
): TableGuardDecision {
    const isDeleteKey = key === "Backspace" || key === "Delete";
    if (range.collapsed) {
        if (!isDeleteKey) return "allow";
        const paragraph = paragraphOf(editable, range);
        if (!paragraph) return "allow";
        if (
            key === "Backspace" &&
            isTableInText(paragraph.previousElementSibling) &&
            isAtEdgeOf(paragraph, range, "start")
        )
            return "block";
        if (
            key === "Delete" &&
            isTableInText(paragraph.nextElementSibling) &&
            isAtEdgeOf(paragraph, range, "end")
        )
            return "block";
        return "allow";
    }
    if (tablesTouchedBy(editable, range).length === 0) return "allow";
    if (isDeleteKey) return "deleteAroundTables";
    const replacesSelection =
        key === "Enter" ||
        (key.length === 1 && !withCommandKey) ||
        (withCommandKey && (key === "x" || key === "v"));
    return replacesSelection ? "block" : "allow";
}

function guardTablesInTextFromKey(e: KeyboardEvent): void {
    const target = e.target as HTMLElement | null;
    // Keys typed in a cell are the cell's business.
    if (!target || target.closest?.(`.${kTableInTextClass}`)) return;
    const editable = target.closest?.(".bloom-editable") as HTMLElement | null;
    if (!editable?.querySelector(`:scope > .${kTableInTextClass}`)) return;
    const selection = editable.ownerDocument.getSelection();
    if (!selection || selection.rangeCount === 0) return;
    const range = selection.getRangeAt(0);
    const decision = decideTableGuard(
        editable,
        range,
        e.key,
        e.ctrlKey || e.metaKey,
    );
    if (decision === "allow") return;
    e.preventDefault();
    e.stopPropagation();
    if (decision === "deleteAroundTables")
        changeWithCkEditorSnapshots(editable, () =>
            deleteAllButTables(editable, range),
        );
}

/**
 * Make `change` to `editable`'s content between two undo snapshots of the box's CKEditor, so that
 * the editor counts it as an edit, for saving and for its undo.
 */
export function changeWithCkEditorSnapshots(
    editable: HTMLElement,
    change: () => void,
): void {
    const editor = (
        editable as unknown as {
            bloomCkEditor?: { fire: (name: string) => void };
        }
    ).bloomCkEditor;
    editor?.fire("saveSnapshot");
    change();
    editor?.fire("saveSnapshot");
}

/**
 * Delete what `range` selects in `editable`'s text, except the tables in it, and leave the caret
 * where the selection started. Each table keeps a paragraph on each side.
 */
export function deleteAllButTables(editable: HTMLElement, range: Range): void {
    const doc = editable.ownerDocument;
    const tables = tablesTouchedBy(editable, range);
    // The parts of the selection between the tables. A selection that starts or ends inside a
    // table (in one of its cells) starts after it or ends before it.
    const parts: Range[] = [];
    let start: [Node, number] = [range.startContainer, range.startOffset];
    const startsInFirstTable = tables[0]?.contains(range.startContainer);
    tables.forEach((table, index) => {
        if (index === 0 && startsInFirstTable) {
            start = afterNode(table);
            return;
        }
        const part = doc.createRange();
        part.setStart(start[0], start[1]);
        part.setEndBefore(table);
        parts.push(part);
        start = afterNode(table);
    });
    const lastTable = tables[tables.length - 1];
    if (!lastTable?.contains(range.endContainer)) {
        const part = doc.createRange();
        part.setStart(start[0], start[1]);
        part.setEnd(range.endContainer, range.endOffset);
        parts.push(part);
    }
    // From the end, so that deleting one part does not move the boundaries of the ones before it.
    parts.reverse().forEach((part) => part.deleteContents());
    tables.forEach((table) => ensureParagraphOnEachSide(table));
    const selection = doc.getSelection();
    if (selection && parts.length > 0) {
        const caret = parts[parts.length - 1].cloneRange();
        caret.collapse(true);
        selection.removeAllRanges();
        selection.addRange(caret);
    }
}

/**
 * Whether the page's selection takes in a table in the text of a text box, wholly or partly (not
 * just text inside one of its cells). The top bar's Cut and Paste buttons refuse such a selection,
 * as Ctrl+X and Ctrl+V do (decideTableGuard).
 */
export function selectionTouchesTableInText(): boolean {
    const selection = document.getSelection();
    if (!selection || selection.rangeCount === 0) return false;
    const range = selection.getRangeAt(0);
    const editable = elementOf(range.commonAncestorContainer)?.closest(
        ".bloom-editable",
    ) as HTMLElement | null;
    return !!editable && tablesTouchedBy(editable, range).length > 0;
}

function afterNode(node: Node): [Node, number] {
    const parent = node.parentNode!;
    return [parent, Array.prototype.indexOf.call(parent.childNodes, node) + 1];
}

function isTableInText(element: Element | null | undefined): boolean {
    return !!element?.classList.contains(kTableInTextClass);
}

// The tables in `editable`'s text that `range` takes in, wholly or partly, in document order. A
// range that lies inside one cell does not count: that is an edit in the cell.
function tablesTouchedBy(editable: HTMLElement, range: Range): HTMLElement[] {
    const startCell = cellOf(range.startContainer);
    if (startCell && startCell === cellOf(range.endContainer)) return [];
    return Array.from(
        editable.querySelectorAll<HTMLElement>(
            `:scope > .${kTableInTextClass}`,
        ),
    ).filter((table) => range.intersectsNode(table));
}

function cellOf(node: Node): Element | null {
    return elementOf(node)?.closest(".bloom-cell") ?? null;
}

/** Whether the caret `range` is at the start (or end) of `paragraph`'s text. */
export function isAtEdgeOf(
    paragraph: HTMLElement,
    range: Range,
    edge: "start" | "end",
): boolean {
    const between = paragraph.ownerDocument.createRange();
    between.selectNodeContents(paragraph);
    if (edge === "start")
        between.setEnd(range.startContainer, range.startOffset);
    else between.setStart(range.startContainer, range.startOffset);
    return between.toString() === "";
}

// The tables that already have the listeners. A WeakSet, so that nothing about it reaches the
// saved HTML and it does not keep the tables of pages we have left in memory.
const tablesWithKeyListeners = new WeakSet<HTMLElement>();

/**
 * Keep the plain keys typed in a cell away from the handlers on the text box around the table.
 * Those handlers (the outer box's CKEditor, BloomField, long press) treat every key that reaches
 * them as typing in their own box: the outer CKEditor, for instance, runs its Enter command on top
 * of the Enter the table library has already handled, and the cell gets two new paragraphs.
 *
 * A key held with Ctrl, Alt or the Windows key goes on up, because page-wide shortcuts listen for it
 * on the document (Ctrl+V for pictures, Ctrl+Y for Redo, and others in bloomEditing.ts).
 */
function keepPlainKeysInTable(table: HTMLElement): void {
    if (tablesWithKeyListeners.has(table)) return;
    tablesWithKeyListeners.add(table);
    // The table library handles Enter in a cell by inserting a new paragraph at the caret, which
    // puts it inside the current paragraph. In other tables each cell's own CKEditor handles Enter
    // first; these cells have none, so the browser's own Enter, which splits the paragraph, is the
    // right one. A capture listener on the table runs before the library's listener on the same
    // table, and stopping the key here keeps it from the library.
    table.addEventListener(
        "keydown",
        (e) => {
            // The cells have no CKEditor, so Undo and Redo in a cell are the browser's own. Its
            // history is the whole page's, so it can also take back typing elsewhere; that is
            // accepted until Bloom's new undo replaces this. Stopping the key here, on the way
            // down, keeps it from the outer box's CKEditor, whose undo command is cancelled in a
            // table, and from Bloom's Ctrl+Y binding, leaving the browser to act on it.
            if (isUndoOrRedoKey(e)) {
                e.stopPropagation();
                return;
            }
            if (
                e.key === "Enter" &&
                !e.shiftKey &&
                !e.ctrlKey &&
                !e.altKey &&
                !e.metaKey
            )
                e.stopPropagation();
        },
        true,
    );
    const stopPlainKey = (e: KeyboardEvent) => {
        if (e.ctrlKey || e.altKey || e.metaKey) return;
        e.stopPropagation();
    };
    table.addEventListener("keydown", stopPlainKey);
    table.addEventListener("keypress", stopPlainKey);
    table.addEventListener("keyup", stopPlainKey);
}
