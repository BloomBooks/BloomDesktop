// Keeping what a person types into a box while its CKEditor is still loading (BL-17022).
//
// CKEDITOR.inline() returns before its editor is ready. At that moment it takes a copy of the box's
// content (`editor.setData(element.getHtml(), null, true)` in ckeditor.js), and when the editor has
// loaded its config, language file and plugins, the "loaded" handler that inline() registered
// writes that copy back into the box (`editor.setData(editor.getData(1))`). Anything typed in
// between would be replaced by what the box held when the page opened. A slow machine makes that
// window a second or two wide.
//
// inline() registers its handler at priority 10000, so a handler at the default priority runs
// first. Ours refreshes the editor's copy from the box, with the same call inline() uses, so the
// rewrite writes back what the person typed. The rewrite still replaces the box's nodes, which
// loses the caret, so we note where the caret is first and put it back once the editor is ready
// (instanceReady fires at the end of that same handler, before any further keystroke).

/** The part of a CKEditor instance this module uses. */
export interface ILoadingCkEditor {
    on(
        eventName: string,
        listener: () => void,
        scope?: unknown,
        listenerData?: unknown,
        priority?: number,
    ): unknown;
    setData(data: string, options: null, internal: boolean): void;
}

/**
 * Where the caret was: the path from the box to its node, and the offset in it. CKEditor's rewrite
 * drops whitespace-only text nodes at block boundaries (such as the line breaks between paragraphs
 * in a book's HTML), so the path's indexes, and the offset when the node is an element, count only
 * the other nodes, which the rewrite keeps.
 */
interface ISavedCaret {
    path: number[];
    nodeName: string;
    offset: number;
}

const kBlockElements = new Set([
    "P",
    "DIV",
    "H1",
    "H2",
    "H3",
    "H4",
    "H5",
    "H6",
    "UL",
    "OL",
    "LI",
    "TABLE",
    "TBODY",
    "THEAD",
    "TR",
    "TD",
    "TH",
    "BLOCKQUOTE",
]);

/** Whether `node` is missing or a block element: what ends a run of inline content. */
function isBlockBoundary(node: Node | null): boolean {
    return !node || kBlockElements.has(node.nodeName);
}

/**
 * Whether CKEditor's rewrite may drop this node: a text node of nothing but whitespace at the start
 * or end of a run of inline content. A space between two inline elements, such as the spans of two
 * recorded sentences, is kept.
 */
function isDroppable(node: Node): boolean {
    return (
        node.nodeType === Node.TEXT_NODE &&
        !/\S/.test(node.textContent!) &&
        (isBlockBoundary(node.previousSibling) ||
            isBlockBoundary(node.nextSibling))
    );
}

/** The children of `node` that CKEditor's rewrite keeps. */
function keptChildren(node: Node): Node[] {
    return Array.from(node.childNodes).filter((n) => !isDroppable(n));
}

/**
 * Make sure that whatever `element` holds when `editor` finishes loading, which may include text
 * typed after CKEDITOR.inline(), is what the editor starts with, and that the caret stays put.
 * Call it straight after CKEDITOR.inline(element).
 */
export function keepTypingWhenCkEditorLoads(
    editor: ILoadingCkEditor,
    element: HTMLElement,
): void {
    let savedCaret: ISavedCaret | undefined;
    editor.on("loaded", () => {
        editor.setData(element.innerHTML, null, true);
        savedCaret = saveCaretIn(element);
    });
    editor.on("instanceReady", () => {
        if (savedCaret) restoreCaretIn(element, savedCaret);
        savedCaret = undefined;
    });
}

/** Record where the caret is, if the document's selection is a caret inside `element`. */
function saveCaretIn(element: HTMLElement): ISavedCaret | undefined {
    const selection = element.ownerDocument.getSelection();
    if (!selection || selection.rangeCount === 0 || !selection.isCollapsed)
        return undefined;
    let node = selection.focusNode;
    if (!node || !element.contains(node)) return undefined;
    let offset = selection.focusOffset;
    if (isDroppable(node)) {
        // Stand in the parent instead, just before where this node is.
        offset = keptChildren(node.parentNode!).filter(
            (n) =>
                n.compareDocumentPosition(node!) &
                Node.DOCUMENT_POSITION_FOLLOWING,
        ).length;
        node = node.parentNode!;
    } else if (node.nodeType !== Node.TEXT_NODE) {
        offset = Array.from(node.childNodes)
            .slice(0, offset)
            .filter((n) => !isDroppable(n)).length;
    }
    const path: number[] = [];
    for (let n: Node = node; n !== element; n = n.parentNode!) {
        path.unshift(keptChildren(n.parentNode!).indexOf(n));
    }
    return { path, nodeName: node.nodeName, offset };
}

/**
 * Put the caret back where saveCaretIn() found it. The box should hold the same markup it did then;
 * if its structure has changed so that the path no longer leads to a node of the same kind with
 * room for the offset, the caret goes to the end of the box, where the person was most likely
 * typing.
 */
function restoreCaretIn(element: HTMLElement, saved: ISavedCaret): void {
    let node: Node | undefined = element;
    for (const index of saved.path) {
        node = keptChildren(node)[index];
        if (!node) break;
    }
    const range = element.ownerDocument.createRange();
    if (
        node &&
        node.nodeName === saved.nodeName &&
        saved.offset <= keptLength(node)
    ) {
        range.setStart(node, domOffset(node, saved.offset));
    } else {
        range.selectNodeContents(element);
        range.collapse(false);
    }
    range.collapse(true);
    const selection = element.ownerDocument.getSelection()!;
    selection.removeAllRanges();
    selection.addRange(range);
}

/** The largest offset a saved caret can have in `node`. */
function keptLength(node: Node): number {
    return node.nodeType === Node.TEXT_NODE
        ? (node as Text).length
        : keptChildren(node).length;
}

/** The range offset in `node` for a saved caret offset, which counts only kept children. */
function domOffset(node: Node, savedOffset: number): number {
    if (node.nodeType === Node.TEXT_NODE) return savedOffset;
    const kept = keptChildren(node);
    return savedOffset < kept.length
        ? Array.from(node.childNodes).indexOf(kept[savedOffset] as ChildNode)
        : node.childNodes.length;
}
