/// <reference path="../../typings/ckeditor/ckeditor.d.ts" />

// Preserving the user's caret while a tool rewrites the markup around it.
//
// This module exists to give that job a seam. It is deliberately a *pure extraction* of what
// toolbox.ts's keystroke pipeline (handleKeyboardInput) has always done, with no behaviour change,
// so that the CKEditor-retirement project (BL-6681) can swap the implementation without operating
// on the most delicate code in the app. See docs/retire-ckeditor/PLAN.md 4.3 and 5.4, and inventory
// rows G1-G5.
//
// TODAY'S IMPLEMENTATION uses CKEditor "bookmarks": a dummy span is inserted at the caret, and
// selecting the bookmark later both restores the caret and removes the span. That has a known cost,
// documented at the call site for years: while the span is in the DOM the markup routine sees it as
// a word break, so fixing a letter mid-word makes the reader tools briefly mis-analyse the word
// ("hous"-bookmark-"e"). It is corrected when the user clicks away.
//
// THE PLANNED IMPLEMENTATION records the caret as a character offset into the editable's text
// instead, which perturbs nothing and therefore fixes that mis-analysis rather than preserving it.
// When that lands, only the four functions below change; toolbox.ts should not need to.

/**
 * An opaque record of where the caret was. Callers must not inspect it — that is the whole point
 * of the seam. Today it holds CKEditor bookmark objects.
 */
export interface SavedMarkupSelection {
    // Deliberately unknown[]: callers must treat this as opaque, and the planned replacement stores
    // something quite different (a character offset, not DOM markers).
    bookmarks: unknown[];
}

/** The editor object CKEditor attaches to each editable div, if it attached one. */
function getEditorOfBox(editableDiv: HTMLElement): CKEDITOR.editor | undefined {
    return (editableDiv as HTMLElement & { bloomCkEditor?: CKEDITOR.editor })
        .bloomCkEditor;
}

/**
 * Whether this box participates in markup at all.
 *
 * Normally every editable box has a rich-text editor attached, so this is true. Boxes without one
 * are treated as not needing markup: the caller skips the whole update for them.
 *
 * (The comment this replaced claimed such boxes are the ArithmeticTemplate number boxes, "because
 * the logic that invokes WireToCKEditor is looking for classes like bloom-content1 that are not
 * present in ArithmeticTemplate". That explanation is wrong: ckeditableSelector in utils/shared.ts
 * explicitly includes .Equation-style, added for that very template. The real case of a box with no
 * editor is one whose computed cursor is `not-allowed`, which attachToCkEditor skips. Behaviour is
 * unchanged either way; only the explanation is corrected.)
 */
export function boxParticipatesInMarkup(editableDiv: HTMLElement): boolean {
    return !!getEditorOfBox(editableDiv);
}

/**
 * Remember where the caret is, before the markup is rewritten.
 *
 * Returns undefined if the caret could not be recorded, which today means the editor reported no
 * selection — the caller should abandon this markup pass entirely (we may be changing pages).
 *
 * Only call this when boxParticipatesInMarkup() is true.
 */
export function saveSelectionForMarkup(
    editableDiv: HTMLElement,
): SavedMarkupSelection | undefined {
    const editor = getEditorOfBox(editableDiv);
    if (!editor) {
        return undefined;
    }
    const selection = editor.getSelection();
    if (!selection) {
        return undefined; // may be changing pages?
    }
    // There is also createBookmarks2(), which avoids actually inserting anything. That has the
    // advantage that changing a character in the middle of a word would let the whole word be
    // evaluated by the markup routine. However, testing showed that the cursor then doesn't
    // actually go back to where it was: it gets shifted to the right.
    return { bookmarks: selection.createBookmarks(true) };
}

/**
 * Put the caret back where saveSelectionForMarkup() found it.
 *
 * Note that with today's implementation this also removes the marker spans from the DOM, so it must
 * be called exactly once per save, and the saved value must not be reused afterwards.
 *
 * Behaviour note: this re-reads the editor and no-ops if it has gone, whereas the pre-extraction
 * code sat inside `if (ckeditorOfThisBox)` and so would have thrown. Unreachable in practice —
 * `bloomCkEditor` is assigned once per div (BloomField.WireToCKEditor) and never cleared — but the
 * difference is real, so it is written down rather than left to be rediscovered.
 */
export function restoreSelectionAfterMarkup(
    editableDiv: HTMLElement,
    saved: SavedMarkupSelection,
): void {
    const editor = getEditorOfBox(editableDiv);
    if (!editor) {
        return;
    }
    editor.getSelection().selectBookmarks(saved.bookmarks);
}

/**
 * Restore the caret and immediately record it again, returning the new record.
 *
 * Needed only on the asynchronous-markup path. There, the rest of the update happens after an
 * await, which may be after the next keystroke has been processed; if we left fixing the selection
 * until then, the caret would be briefly visible in the wrong place and — much worse — intervening
 * keystrokes would go to that wrong position (BL-10133). So the caret is put right immediately, and
 * recorded afresh for the restore that follows the actual markup change.
 *
 * Returns undefined under the same conditions as saveSelectionForMarkup().
 */
export function restoreAndResaveSelectionForMarkup(
    editableDiv: HTMLElement,
    saved: SavedMarkupSelection,
): SavedMarkupSelection | undefined {
    restoreSelectionAfterMarkup(editableDiv, saved);
    return saveSelectionForMarkup(editableDiv);
}
