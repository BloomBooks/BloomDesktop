// Dragging a table that sits in the text of a text box to another place in the same text (see
// docs/insert-table/README.md).
//
// While a table in text is selected (the focus is in it), a grip shows just left of its top-left
// corner. Dragging the grip shows where the table would land: a horizontal line at the start or
// end of a paragraph, or a text-cursor mark inside one, which the drop splits. The drop moves the
// table element itself, so the table library and its cells' text boxes keep working on it.
//
// The grip and the mark are on the page's body, outside the bloom-page, so they are never saved.

import { refreshStructuralChrome } from "bloom-table";
import { kTableInTextClass } from "./tableInTextConstants";
import {
    ensureParagraphOnEachSide,
    isAtEdgeOf,
    paragraphOf,
    placeBetweenParagraphs,
} from "./tablesInText";

const kGripClass = "bloom-tableInTextGrip";
const kDropMarkClass = "bloom-tableInTextDropMark";

/** Where a drop would put the table, and how to show it. */
export interface ITableDropTarget {
    // The caret the table goes at; see placeBetweenParagraphs.
    caret: Range;
    // "line" for a drop at the start or end of a paragraph, or between paragraphs; "split" for one
    // inside a paragraph.
    kind: "line" | "split";
}

/**
 * Where a drop of `table` at the caret `caret` (the caret position under the pointer) would put it,
 * or undefined where it cannot go: outside `editable`'s own text, or inside the table itself.
 */
export function dropTargetFor(
    editable: HTMLElement,
    table: HTMLElement,
    caret: Range | null | undefined,
): ITableDropTarget | undefined {
    if (!caret) return undefined;
    const node = caret.startContainer;
    if (!editable.contains(node) || table.contains(node)) return undefined;
    // Not into another table in the same text, nor into an inline image.
    const element =
        node.nodeType === Node.ELEMENT_NODE
            ? (node as Element)
            : node.parentElement;
    if (element?.closest(`.${kTableInTextClass}, [contenteditable='false']`))
        return undefined;
    if (node === editable) return { caret, kind: "line" };
    const paragraph = paragraphOf(editable, caret);
    if (!paragraph) return undefined;
    const atAnEdge =
        isAtEdgeOf(paragraph, caret, "start") ||
        isAtEdgeOf(paragraph, caret, "end");
    return { caret, kind: atAnEdge ? "line" : "split" };
}

/**
 * Move `table` to `caret` in `editable`'s text, as insertTableInText would put a new one there, and
 * tell the box's CKEditor that its content changed.
 */
export function moveTableInText(
    editable: HTMLElement,
    table: HTMLElement,
    caret: Range,
): void {
    const editor = (
        editable as unknown as {
            bloomCkEditor?: { fire: (name: string) => void };
        }
    ).bloomCkEditor;
    editor?.fire("saveSnapshot");
    // Taking the table out of the page takes the focus out of it too, which would deselect it
    // and hide its buttons, so the focus goes back where it was once the table is in place.
    const doc = editable.ownerDocument;
    const focused = doc.activeElement as HTMLElement | null;
    const focusWasInTable = !!focused && table.contains(focused);
    // The caret is a live Range, so taking the table out of the text does not move it.
    table.remove();
    placeBetweenParagraphs(editable, table, caret);
    ensureParagraphOnEachSide(table);
    editor?.fire("saveSnapshot");
    if (focusWasInTable) focused.focus({ preventScroll: true });
    // The library draws its buttons where the table was; this has it ask again and move them.
    refreshStructuralChrome();
}

/**
 * Set up the grip for the tables in text of `doc`. Safe to call again: it does its setup once per
 * document.
 */
export function setupTableInTextDragging(doc: Document): void {
    if (documentsWithGrip.has(doc)) return;
    documentsWithGrip.add(doc);
    const grip = doc.createElement("div");
    grip.className = `${kGripClass} bloom-ui`;
    grip.setAttribute("aria-hidden", "true");
    // Six dots, the usual sign of something that can be dragged.
    grip.innerHTML = `<svg viewBox="0 0 10 16" width="10" height="16">${[
        2, 8, 14,
    ]
        .flatMap((y) => [
            `<circle cx="2.5" cy="${y}" r="1.5"/>`,
            `<circle cx="7.5" cy="${y}" r="1.5"/>`,
        ])
        .join("")}</svg>`;
    grip.style.display = "none";
    doc.body.appendChild(grip);
    const mark = doc.createElement("div");
    mark.className = `${kDropMarkClass} bloom-ui`;
    mark.style.display = "none";
    doc.body.appendChild(mark);

    const update = () => showGripForSelectedTable(doc, grip);
    // A focus change can select a table or leave one; anything that moves the table moves the
    // grip.
    doc.addEventListener("focusin", update, true);
    doc.addEventListener("focusout", () => setTimeout(update, 0), true);
    doc.addEventListener("scroll", update, { capture: true, passive: true });
    doc.defaultView?.addEventListener("resize", update, { passive: true });
    doc.addEventListener("input", update, true);

    grip.addEventListener("pointerdown", (e) => startDrag(e, doc, grip, mark));
    // Pressing the grip must not take the focus out of the table, which would hide the grip.
    grip.addEventListener("mousedown", (e) => e.preventDefault());
}

const documentsWithGrip = new WeakSet<Document>();

// The table the grip belongs to, while it shows.
let gripTable: HTMLElement | undefined;

function selectedTableInText(doc: Document): HTMLElement | undefined {
    const active = doc.activeElement;
    return (
        (active?.closest(`.${kTableInTextClass}`) as HTMLElement | null) ??
        undefined
    );
}

function showGripForSelectedTable(doc: Document, grip: HTMLElement): void {
    const table = selectedTableInText(doc);
    gripTable = table;
    if (!table) {
        grip.style.display = "none";
        return;
    }
    const rect = table.getBoundingClientRect();
    const view = doc.defaultView!;
    grip.style.display = "block";
    grip.style.left = `${rect.left + view.scrollX - 16}px`;
    grip.style.top = `${rect.top + view.scrollY}px`;
}

function caretAtPoint(doc: Document, x: number, y: number): Range | null {
    return doc.caretRangeFromPoint(x, y);
}

function startDrag(
    e: PointerEvent,
    doc: Document,
    grip: HTMLElement,
    mark: HTMLElement,
): void {
    const table = gripTable;
    const editable = table?.parentElement;
    if (!table || !editable) return;
    e.preventDefault();
    grip.setPointerCapture(e.pointerId);
    let target: ITableDropTarget | undefined;
    const view = doc.defaultView!;

    const onMove = (move: PointerEvent) => {
        target = dropTargetFor(
            editable,
            table,
            caretAtPoint(doc, move.clientX, move.clientY),
        );
        showDropMark(mark, editable, target, view);
    };
    const finish = (drop: boolean) => {
        grip.removeEventListener("pointermove", onMove);
        grip.removeEventListener("pointerup", onUp);
        grip.removeEventListener("pointercancel", onCancel);
        mark.style.display = "none";
        if (drop && target) moveTableInText(editable, table, target.caret);
        showGripForSelectedTable(doc, grip);
    };
    const onUp = () => finish(true);
    const onCancel = () => finish(false);
    grip.addEventListener("pointermove", onMove);
    grip.addEventListener("pointerup", onUp);
    grip.addEventListener("pointercancel", onCancel);
}

function showDropMark(
    mark: HTMLElement,
    editable: HTMLElement,
    target: ITableDropTarget | undefined,
    view: Window,
): void {
    if (!target) {
        mark.style.display = "none";
        return;
    }
    const caretRect = caretRectangle(target.caret);
    const boxRect = editable.getBoundingClientRect();
    mark.style.display = "block";
    if (target.kind === "split") {
        // A text cursor where the paragraph would split.
        mark.style.left = `${caretRect.left + view.scrollX - 1}px`;
        mark.style.top = `${caretRect.top + view.scrollY}px`;
        mark.style.width = "2px";
        mark.style.height = `${Math.max(caretRect.height, 12)}px`;
        return;
    }
    // A line across the box, above the paragraph whose start the caret is at, or below the one
    // whose end it is at.
    const paragraph = paragraphOf(editable, target.caret);
    let lineY = caretRect.top;
    if (paragraph) {
        const paragraphRect = paragraph.getBoundingClientRect();
        lineY = isAtEdgeOf(paragraph, target.caret, "start")
            ? paragraphRect.top
            : paragraphRect.bottom;
    }
    mark.style.left = `${boxRect.left + view.scrollX}px`;
    mark.style.top = `${lineY + view.scrollY - 1}px`;
    mark.style.width = `${boxRect.width}px`;
    mark.style.height = "2px";
}

// The rectangle of a caret. A caret in an empty paragraph has an empty rectangle, so that one uses
// the paragraph's.
function caretRectangle(caret: Range): DOMRect {
    const rect = caret.getBoundingClientRect();
    if (rect.height > 0) return rect;
    const node = caret.startContainer;
    const element =
        node.nodeType === Node.ELEMENT_NODE
            ? (node as Element)
            : node.parentElement!;
    return element.getBoundingClientRect();
}
