// Dragging a table that sits in the text of a text box to another place in the same text (see
// docs/insert-table/README.md).
//
// Each table in text has a grip just left of its first row, which shows whenever the page's
// other editing marks do (tableEditing.less). Dragging the grip shows where the table would land:
// a horizontal line at the start or end of a paragraph, or a text-cursor mark inside one, which the
// drop splits. The drop moves the table element itself, so the table library and its cells' text
// boxes keep working on it.
//
// The grip and the mark are on the page's body, outside the bloom-page, so they are never saved.

import { refreshStructuralChrome } from "bloom-table";
import { elementOf, kTableInTextClass } from "./tableInTextConstants";
import {
    changeWithCkEditorSnapshots,
    ensureParagraphOnEachSide,
    isAtEdgeOf,
    paragraphOf,
    placeBetweenParagraphs,
} from "./tablesInText";

const kGripClass = "bloom-tableInTextGrip";
const kDropMarkClass = "bloom-tableInTextDropMark";
// The grip's height in tableEditing.less.
const kGripHeight = 24;

/** Where a drop would put the table, and how to show it. */
interface ITableDropTarget {
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
    if (
        elementOf(node)?.closest(
            `.${kTableInTextClass}, [contenteditable='false']`,
        )
    )
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
    // Taking the table out of the page takes the focus out of it too, which would deselect it
    // and hide its buttons, so the focus goes back where it was once the table is in place.
    const doc = editable.ownerDocument;
    const focused = doc.activeElement as HTMLElement | null;
    const focusWasInTable = !!focused && table.contains(focused);
    changeWithCkEditorSnapshots(editable, () => {
        // The caret is a live Range, so taking the table out of the text does not move it.
        table.remove();
        placeBetweenParagraphs(editable, table, caret);
        ensureParagraphOnEachSide(table);
    });
    if (focusWasInTable) focused.focus({ preventScroll: true });
    // The library draws its buttons where the table was; this has it ask again and move them.
    refreshStructuralChrome();
}

/**
 * Set up the grips for the tables in text of `doc`. Call it once `doc` has a table in text, so that
 * pages without one pay nothing for the grips. Safe to call again: it does its setup once per
 * document.
 */
export function setupTableInTextDragging(doc: Document): void {
    if (documentsWithGrips.has(doc)) return;
    documentsWithGrips.add(doc);
    const mark = doc.createElement("div");
    mark.className = `${kDropMarkClass} bloom-ui`;
    mark.style.display = "none";
    doc.body.appendChild(mark);

    // Anything that adds, moves or removes a table moves or removes its grip: inserting or
    // dragging one, typing above it, scrolling something inside the page, a change of window size.
    // Batched to one update a frame, since typing changes the page with every key.
    let updatePending = false;
    const update = () => {
        if (updatePending) return;
        updatePending = true;
        doc.defaultView!.requestAnimationFrame(() => {
            updatePending = false;
            placeGrips(doc, mark);
        });
    };
    new MutationObserver((mutations) => {
        // The grips are on the body, and placing them is a change too.
        if (mutations.some((m) => !isGripOrMark(m.target))) update();
    }).observe(doc.body, {
        childList: true,
        subtree: true,
        characterData: true,
        attributes: true,
        attributeFilter: ["class", "style"],
    });
    // The grips are placed in the document's own coordinates, so scrolling the whole document
    // does not move them relative to their tables.
    doc.addEventListener(
        "scroll",
        (e) => {
            if (e.target !== doc) update();
        },
        { capture: true, passive: true },
    );
    doc.defaultView?.addEventListener("resize", update, { passive: true });
    update();
}

const documentsWithGrips = new WeakSet<Document>();

// The grip of each table in text that has one.
const grips = new Map<HTMLElement, HTMLElement>();

function isGripOrMark(node: Node): boolean {
    return !!elementOf(node)?.closest(`.${kGripClass}, .${kDropMarkClass}`);
}

/**
 * Give each table in text of `doc` that shows a grip just left of its first row, and take away the
 * grips of tables that are gone or hidden (their language is not shown).
 */
function placeGrips(doc: Document, mark: HTMLElement): void {
    const view = doc.defaultView!;
    // All the measuring first, then all the placing, so that the browser lays the page out once.
    const placements = Array.from(
        doc.querySelectorAll<HTMLElement>(`.bloom-page .${kTableInTextClass}`),
    )
        .map((table) => ({
            table,
            rect: table.getBoundingClientRect(),
            firstCell: table
                .querySelector(".bloom-cell")!
                .getBoundingClientRect(),
        }))
        .filter((placement) => placement.rect.width > 0);
    const shown = new Set<HTMLElement>();
    placements.forEach(({ table, rect, firstCell }) => {
        shown.add(table);
        let grip = grips.get(table);
        if (!grip) {
            grip = makeGrip(doc, table, mark);
            grips.set(table, grip);
        }
        grip.style.left = `${rect.left + view.scrollX - 22}px`;
        // Level with the middle of the first row, as Notion puts its handle level with a
        // block's first line.
        grip.style.top = `${firstCell.top + firstCell.height / 2 - kGripHeight / 2 + view.scrollY}px`;
    });
    grips.forEach((grip, table) => {
        if (shown.has(table)) return;
        grip.remove();
        grips.delete(table);
    });
}

function makeGrip(
    doc: Document,
    table: HTMLElement,
    mark: HTMLElement,
): HTMLElement {
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
    doc.body.appendChild(grip);
    grip.addEventListener("pointerdown", (e) =>
        startDrag(e, doc, table, grip, mark),
    );
    // Pressing the grip leaves the focus where it was, and so the table's buttons as they were.
    grip.addEventListener("mousedown", (e) => e.preventDefault());
    return grip;
}

function startDrag(
    e: PointerEvent,
    doc: Document,
    table: HTMLElement,
    grip: HTMLElement,
    mark: HTMLElement,
): void {
    const editable = table.parentElement!;
    e.preventDefault();
    grip.setPointerCapture(e.pointerId);
    let target: ITableDropTarget | undefined;
    const view = doc.defaultView!;

    const onMove = (move: PointerEvent) => {
        target = dropTargetFor(
            editable,
            table,
            doc.caretRangeFromPoint(move.clientX, move.clientY),
        );
        showDropMark(mark, editable, target, view);
    };
    const finish = (drop: boolean) => {
        grip.removeEventListener("pointermove", onMove);
        grip.removeEventListener("pointerup", onUp);
        grip.removeEventListener("pointercancel", onCancel);
        mark.style.display = "none";
        if (drop && target) moveTableInText(editable, table, target.caret);
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
        mark.style.left = `${caretRect.left + view.scrollX - 1.5}px`;
        mark.style.top = `${caretRect.top + view.scrollY}px`;
        mark.style.width = "3px";
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
    mark.style.top = `${lineY + view.scrollY - 2}px`;
    mark.style.width = `${boxRect.width}px`;
    mark.style.height = "4px";
}

// The rectangle of a caret. A caret in an empty paragraph has an empty rectangle, so that one uses
// the paragraph's.
function caretRectangle(caret: Range): DOMRect {
    const rect = caret.getBoundingClientRect();
    if (rect.height > 0) return rect;
    return elementOf(caret.startContainer)!.getBoundingClientRect();
}
