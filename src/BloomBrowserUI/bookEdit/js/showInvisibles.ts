import bloomQtipUtils from "./bloomQtipUtils";
import { isTrackedFillingCharNode } from "../pageHistory/editTimeMarkup";
import {
    currentPageHistory,
    doOutsideHistory,
} from "../pageHistory/pageHistoryAccess";
import $ from "jquery";

// For testing and debugging, functionality to replace invisible characters with symbols.
//
// Showing the invisibles is a view of the page, not an edit, so it happens outside the page
// history and leaves the box with the same nodes that it had before. Each text node that holds
// invisible characters keeps the text before the first one, and after it go a span with the
// symbol of each invisible character and a new text node for the text after each one. Hiding puts
// back the text node's data and removes every node that showing added. Nothing rewrites the box's
// html, so the caret, CKEditor's filling character and anything else that holds on to the box's
// nodes stay as they were.

// The name and the symbol (in the SymChar font's private use area) of each invisible character.
const invisibles: { [character: string]: { name: string; symbol: string } } = {
    "\u200D": { name: "Zero Width Joiner", symbol: "\uE077" },
    "\u2060": { name: "Word Joiner", symbol: "\uE078" },
    "\u200C": { name: "Zero Width Non-Joiner", symbol: "\uE079" },
    "\u034F": { name: "Combining Grapheme Joiner", symbol: "\uE07A" },
    "\u200B": { name: "Zero Width Space", symbol: "\uE081" },
    "\u00A0": { name: "No-Break Space", symbol: "\uE082" },
    "\u202F": { name: "Narrow No-Break Space", symbol: "\uE083" },
    "\u2000": { name: "En Quad", symbol: "\uE084" },
    "\u2001": { name: "Em Quad", symbol: "\uE085" },
    "\u2002": { name: "En Space", symbol: "\uE086" },
    "\u2003": { name: "Em Space", symbol: "\uE087" },
    "\u2004": { name: "Three-Per-Em Space", symbol: "\uE088" },
    "\u2005": { name: "Four-Per-Em Space", symbol: "\uE089" },
    "\u2006": { name: "Six-Per-Em Space", symbol: "\uE08A" },
    "\u2007": { name: "Figure Space", symbol: "\uE08B" },
    "\u2008": { name: "Punctuation Space", symbol: "\uE08C" },
    "\u2009": { name: "Thin Space", symbol: "\uE08D" },
    "\u200A": { name: "Hair Space", symbol: "\uE08E" },
    "\u200E": { name: "Left-To-Right Mark", symbol: "\uE090" },
    "\u200F": { name: "Right-To-Left Mark", symbol: "\uE091" },
    "\u00AD": { name: "Soft Hyphen", symbol: "\uE0A1" },
    "\u0009": { name: "Horizontal Tabulation", symbol: "\uE0A2" },
};

const kCkEditorFillingChar = "\u200B";

/** A point in the page: a node and an offset in it. */
type Point = [Node, number];

/** What showInvisibles changed, for hideInvisibles to put back. */
interface Showing {
    box: HTMLElement;
    // The data that each split text node had before.
    originals: Map<Text, string>;
    // For each split text node, the nodes that hold its text now, in order, starting with itself,
    // each with where its text starts in the original data.
    pieces: Map<Node, { node: Node; start: number }[]>;
    // The spans and text nodes that showInvisibles added.
    added: Node[];
    // The selection's anchor and focus before showing.
    selection?: Point[];
    stopListeningToHistory: () => void;
}

let showing: Showing | undefined;

/**
 * Show the invisible characters of a text box in container while the user holds Ctrl+Shift+Space
 * in it. Hide them when the user releases any key (listening only for the release of the space
 * would miss a release of the keys in another order), presses any other key, or leaves the box.
 */
export function listenForShowInvisiblesKeys(container: HTMLElement): void {
    $(container)
        .find("div.bloom-editable")
        .on("keydown", (e) => {
            const isCtrlShiftSpace =
                e.ctrlKey && e.shiftKey && !e.altKey && e.key === " ";
            if (!isCtrlShiftSpace) {
                // Such as Ctrl+Shift+V, which must act on the real text.
                hideInvisibles();
                return;
            }
            e.preventDefault();
            showInvisibles(e.currentTarget as HTMLElement);
        })
        .on("keyup blur", () => hideInvisibles());
}

/** Put a symbol in place of each invisible character in box, and explain each kind of symbol. */
function showInvisibles(box: HTMLElement): void {
    // The key repeats while it is held.
    if (showing) return;
    const shown: Showing = {
        box,
        originals: new Map(),
        pieces: new Map(),
        added: [],
        selection: selectionEnds(box.ownerDocument),
        stopListeningToHistory: () => {},
    };
    doOutsideHistory(() => {
        const namesWithTooltip = new Set<string>();
        textNodesIn(box).forEach((node) => splitAtInvisibles(node, shown));
        shown.added.forEach((node) => {
            const name = (node as HTMLElement).dataset?.nameOfInvisible;
            if (!name || namesWithTooltip.has(name)) return;
            namesWithTooltip.add(name);
            addTooltip(node as HTMLElement, name);
        });
        // Splitting moved any end of the selection that was in a split text node to its start.
        const now = selectionEnds(box.ownerDocument);
        if (shown.selection && now) {
            const [anchor, focus] = shown.selection.map((point, i) =>
                placeWhileShowing(shown, point, now[i]),
            );
            box.ownerDocument
                .getSelection()!
                .setBaseAndExtent(...anchor, ...focus);
        }
    });
    // An undo or redo, such as a Ctrl+Shift+Z while the keys are held, must find the page as the
    // history knows it.
    shown.stopListeningToHistory =
        currentPageHistory()?.onBeforeUndoOrRedo(hideInvisibles) ?? (() => {});
    showing = shown;
}

/** Put back the text nodes that showInvisibles split, as they were, and remove what it added. */
function hideInvisibles(): void {
    const shown = showing;
    if (!shown) return;
    showing = undefined;
    shown.stopListeningToHistory();
    doOutsideHistory(() => {
        // On blur the selection has already left the box; don't yank it back.
        const now = selectionEnds(shown.box.ownerDocument);
        const restoreSelection = now && shown.box.contains(now[0][0]);
        $(shown.added).filter(".invisibles").qtip("destroy");
        shown.originals.forEach((data, node) => (node.data = data));
        shown.added.forEach((node) => node.parentNode?.removeChild(node));
        if (restoreSelection && shown.selection) {
            const [anchor, focus] = shown.selection;
            shown.box.ownerDocument
                .getSelection()!
                .setBaseAndExtent(...anchor, ...focus);
        }
    });
}

/** Split node at its invisible characters, as Showing describes, unless it has none. */
function splitAtInvisibles(node: Text, shown: Showing): void {
    const original = node.data;
    // CKEditor's filling character is CKEditor's, not the user's. Left as it is, CKEditor still
    // finds it when it takes it out, and never leaves it in the text.
    const fillingCharIndex = isTrackedFillingCharNode(node)
        ? original.indexOf(kCkEditorFillingChar)
        : -1;
    const offsets: number[] = [];
    for (let offset = 0; offset < original.length; offset++)
        if (offset !== fillingCharIndex && invisibles[original[offset]])
            offsets.push(offset);
    if (offsets.length === 0) return;

    const doc = node.ownerDocument;
    const after = node.nextSibling;
    const pieces = [{ node: node as Node, start: 0 }];
    const add = (piece: Node, start: number) => {
        node.parentNode!.insertBefore(piece, after);
        pieces.push({ node: piece, start });
        shown.added.push(piece);
    };
    shown.originals.set(node, original);
    shown.pieces.set(node, pieces);
    node.data = original.substring(0, offsets[0]);
    offsets.forEach((offset, i) => {
        const invisible = invisibles[original[offset]];
        const span = doc.createElement("span");
        span.className = "invisibles";
        span.dataset.nameOfInvisible = invisible.name;
        span.textContent = invisible.symbol;
        add(span, offset);
        const restEnd = offsets[i + 1] ?? original.length;
        if (restEnd > offset + 1)
            add(
                doc.createTextNode(original.substring(offset + 1, restEnd)),
                offset + 1,
            );
    });
}

/**
 * Where an end of the selection that was at before, before splitting, is now. The browser has
 * already moved an end that was not in a split text node to now.
 */
function placeWhileShowing(shown: Showing, before: Point, now: Point): Point {
    const [node, offset] = before;
    const pieces = shown.pieces.get(node);
    if (!pieces) return now;
    for (const piece of pieces) {
        const length = piece.node.textContent!.length;
        const inPiece = offset >= piece.start && offset <= piece.start + length;
        if (piece.node.nodeType === Node.TEXT_NODE && inPiece)
            return [piece.node, offset - piece.start];
    }
    // Just after a symbol that ends the text.
    const last = pieces[pieces.length - 1].node;
    const parent = last.parentNode!;
    return [
        parent,
        Array.from(parent.childNodes).indexOf(last as ChildNode) + 1,
    ];
}

/**
 * Explain the symbol in span with a tooltip. The tooltip itself is in #page-scaling-container,
 * beside the page.
 */
function addTooltip(span: HTMLElement, name: string): void {
    $(span).qtip({
        position: {
            my: "top left",
            at: "bottom right",
            container: bloomQtipUtils.qtipZoomContainer(),
        },
        content: name,
        show: {
            ready: true,
            // Now, inside doOutsideHistory: showing writes an attribute to the span, which is in
            // the page, and the history would record it later.
            delay: 0,
        },
        hide: {
            inactive: 2000,
            effect: function () {
                $(this).fadeOut(250);
            },
        },
    });
}

/** The selection's anchor and focus, or undefined when there is no selection. */
function selectionEnds(doc: Document): Point[] | undefined {
    const selection = doc.getSelection();
    if (!selection || selection.rangeCount === 0) return undefined;
    return [
        [selection.anchorNode!, selection.anchorOffset],
        [selection.focusNode!, selection.focusOffset],
    ];
}

/** The text nodes inside root, in document order. */
function textNodesIn(root: Node): Text[] {
    const nodes: Text[] = [];
    const walker = root.ownerDocument!.createTreeWalker(
        root,
        NodeFilter.SHOW_TEXT,
    );
    while (walker.nextNode()) nodes.push(walker.currentNode as Text);
    return nodes;
}
