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
// loses the caret. So before refreshing the copy we put a marker element at the caret; the rewrite
// carries it through like any other markup, and once the editor is ready (instanceReady fires at
// the end of that same handler, before any further keystroke) we put the caret where the marker is
// and remove it. Marking the caret in the markup, rather than recording a position, means we need
// not predict how CKEditor's rewrite tidies the markup (it drops some whitespace, for instance).
//
// The marker is made the way CKEditor makes its own bookmarks, which its parser keeps; Bloom turns
// CKEditor's content filter off (allowedContent in lib/ckeditor/config.js), so nothing strips it.
// CKEditor takes no undo snapshot until it is ready, and our instanceReady listener is registered
// before the undo plugin's, so no snapshot ever holds the marker.

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

/** The attribute that tells our caret marker apart from any CKEditor bookmark. */
const kCaretMarkerAttribute = "data-bloom-caret-marker";

/**
 * Make sure that whatever `element` holds when `editor` finishes loading, which may include text
 * typed after CKEDITOR.inline(), is what the editor starts with, and that the caret stays put.
 * Call it straight after CKEDITOR.inline(element).
 */
export function keepTypingWhenCkEditorLoads(
    editor: ILoadingCkEditor,
    element: HTMLElement,
): void {
    let caretWasInBox = false;
    editor.on("loaded", () => {
        caretWasInBox = markCaretIn(element);
        editor.setData(element.innerHTML, null, true);
    });
    editor.on("instanceReady", () => {
        if (caretWasInBox) restoreCaretIn(element);
        caretWasInBox = false;
    });
}

/**
 * If the document's selection is a caret inside `element`, put a marker element there and return
 * true. A caret that sits directly in a box that holds paragraphs (between two of them, say) gets
 * no marker, because there CKEditor would wrap the marker in a paragraph of its own; it still
 * counts as in the box, and restoreCaretIn() puts it at the end.
 */
function markCaretIn(element: HTMLElement): boolean {
    const selection = element.ownerDocument.getSelection();
    if (!selection || selection.rangeCount === 0 || !selection.isCollapsed)
        return false;
    const range = selection.getRangeAt(0);
    const container = range.startContainer;
    if (!element.contains(container)) return false;
    const host =
        container.nodeType === Node.TEXT_NODE
            ? container.parentNode
            : container;
    if (host === element && element.querySelector(":scope > p, :scope > div"))
        return true;
    const marker = element.ownerDocument.createElement("span");
    marker.setAttribute("data-cke-bookmark", "1");
    marker.setAttribute(kCaretMarkerAttribute, "1");
    marker.style.display = "none";
    marker.innerHTML = "&nbsp;";
    range.insertNode(marker);
    return true;
}

/**
 * Put the caret where markCaretIn() left its marker, and remove the marker, joining the text on
 * either side of it again. If there is no marker (the editor's markup came from somewhere else, or
 * the caret was between paragraphs), the caret goes to the end of the box, where the person was
 * most likely typing.
 */
function restoreCaretIn(element: HTMLElement): void {
    const range = element.ownerDocument.createRange();
    const marker = element.querySelector(`[${kCaretMarkerAttribute}]`);
    if (marker) {
        const before = marker.previousSibling;
        const after = marker.nextSibling;
        const parent = marker.parentNode!;
        const index = Array.prototype.indexOf.call(parent.childNodes, marker);
        marker.remove();
        if (before?.nodeType === Node.TEXT_NODE) {
            const beforeText = before as Text;
            const caretOffset = beforeText.length;
            if (after?.nodeType === Node.TEXT_NODE) {
                beforeText.appendData((after as Text).data);
                after.remove();
            }
            range.setStart(beforeText, caretOffset);
        } else if (after?.nodeType === Node.TEXT_NODE) {
            range.setStart(after, 0);
        } else {
            range.setStart(parent, index);
        }
    } else {
        range.selectNodeContents(element);
    }
    range.collapse(false);
    const selection = element.ownerDocument.getSelection()!;
    selection.removeAllRanges();
    selection.addRange(range);
}
