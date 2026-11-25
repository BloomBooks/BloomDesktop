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
import { isUndoOrRedoKey } from "../undo/redoKeyBinding";
import { AttachNewTableAndSetUpItsCells } from "./tableEditing";
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
 * Put a new two-by-two table into `editable`'s text at `caret`, and set it up for editing. Returns
 * the table.
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
): HTMLElement {
    const doc = editable.ownerDocument;
    const table = doc.createElement("div");
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
    // the copy in the second half has the same id. Only the first half keeps it.
    restOfParagraph.querySelectorAll("[id]").forEach((copy) => {
        if (paragraph.querySelector(`[id="${CSS.escape(copy.id)}"]`))
            copy.removeAttribute("id");
    });
    paragraph.after(table);
    if (!isBlank(restOfParagraph)) {
        const secondHalf = editable.ownerDocument.createElement("p");
        secondHalf.appendChild(restOfParagraph);
        table.after(secondHalf);
    }
}

/** The paragraph of `editable` that holds the caret, if the caret is in one of its own paragraphs. */
export function paragraphOf(
    editable: HTMLElement,
    caret: Range,
): HTMLElement | undefined {
    const paragraph = elementOf(caret.startContainer)?.closest("p");
    if (!paragraph || paragraph.parentElement !== editable) return undefined;
    return paragraph;
}

function isBlank(content: Node): boolean {
    return (content.textContent ?? "").trim() === "";
}

/** Make sure `table` has a paragraph right before it and right after it, adding empty ones. */
export function ensureParagraphOnEachSide(table: HTMLElement): void {
    const doc = table.ownerDocument;
    const emptyParagraph = () => {
        const p = doc.createElement("p");
        p.appendChild(doc.createElement("br"));
        return p;
    };
    if (table.previousElementSibling?.tagName !== "P")
        table.before(emptyParagraph());
    if (table.nextElementSibling?.tagName !== "P")
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
