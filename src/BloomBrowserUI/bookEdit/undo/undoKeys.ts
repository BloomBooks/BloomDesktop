// Ctrl+Z and Ctrl+Y for the entries on the one undo stack: picture changes, a table's structural
// operations (tableUndo.ts), and the rest (BL-6681).
//
// Inside a text block, CKEditor's undo plugin claims both keys: it prevents their default on
// keydown and runs its own "undo" or "redo" command, which restores the saved HTML of the focused
// editable alone. CKEditor knows nothing of the stack's entries: when it has nothing of its own to
// undo or redo, it runs no command at all, so the key would do nothing. And an inline image has a
// copy in every language's editable in the group, so CKEditor would put back the copy in the
// focused block and leave the others, including the lang="z" one that a language added later is
// copied from. Outside a text block (a picture clicked when the caret was never in its block),
// nothing else handles Ctrl+Z.
//
// So this listens on the page's document in the capture phase, which runs before CKEditor's
// listeners on the editable. It takes Ctrl+Z when the one undo stack's next undo is one of its own
// entries (getEntryForNextUndo, which leaves the key to CKEditor while typing is newer), and when
// the next undo is typing in a box other than the focused one ("add a row, type in it, click
// another cell"): the focused box's CKEditor would do nothing, and the stack's CKEditor provider
// undoes the typing where it was done. It takes Ctrl+Y (or Ctrl+Shift+Z) when the stack has
// something to redo and the focused block's CKEditor has nothing of its own: typing discards what
// the stack could redo, and a picture change clears the history of the CKEditors in its group
// (see restartTypingHistories in inlineImageInteractions.ts). Either way it stops the event, so
// CKEditor and the other key handlers never see it.
//
// In Change Layout mode origami owns both keys, so this leaves them alone. So do the reader
// tools, in a text block, while one of their markup tools is selected: they keep their own
// history of the marked text and take both keys on the editable. That history starts again after
// a picture change (see restartTypingHistories), so their Ctrl+Z cannot put an old picture back.
// A form field keeps both keys for its own undo of what was typed in it.

import { getToolboxBundleExports } from "../js/workspaceFrames";
import { getCkeditorToUndo } from "./ckeditorChanges";
import { theOneUndoStack } from "./UndoStack";

// The documents that already have the listener, so that setting up twice does not undo twice.
const documentsWithUndoKeys = new WeakSet<Document>();

/**
 * Take Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z in `doc` for the entries on the one undo stack, before
 * CKEditor sees them (see the comment at the top of this file). Installing it twice on one
 * document does nothing more.
 *
 * @param redo what Ctrl+Y does once this has taken it. The page passes its bundle's handleRedo,
 * the function the e2e tests watch; by default it redoes on the stack directly.
 */
export function installUndoKeys(
    doc: Document,
    redo: () => void = () => void theOneUndoStack.redo(),
): void {
    if (documentsWithUndoKeys.has(doc)) return;
    documentsWithUndoKeys.add(doc);
    doc.addEventListener(
        "keydown",
        (event: KeyboardEvent) => onUndoOrRedoKeyDown(event, redo),
        true,
    );
}

/** Whether the page is in Change Layout mode, where origami owns Ctrl+Z and Ctrl+Y. */
export function isInChangeLayoutMode(doc: Document): boolean {
    return !!doc.querySelector(".marginBox.origami-layout-mode");
}

function onUndoOrRedoKeyDown(event: KeyboardEvent, redo: () => void): void {
    // This sees every keystroke in the page, so the key comes first, before anything that
    // searches the page or reaches into the toolbox frame.
    const action = undoKeyAction(event);
    if (!action) return;
    const doc = (event.target as Node).ownerDocument ?? document;
    if (isInChangeLayoutMode(doc)) return;
    if ((event.target as Element).closest?.("input, textarea, select")) return;
    if (
        (event.target as Element).closest?.(".bloom-editable") &&
        getToolboxBundleExports()?.readerToolsHandleUndoKeys()
    )
        return;
    if (action === "undo") {
        if (
            !theOneUndoStack.getEntryForNextUndo() &&
            !nextUndoIsTypingInAnotherBox(doc)
        )
            return;
        claimKeystroke(event);
        void theOneUndoStack.undo();
    } else {
        if (!theOneUndoStack.canRedo()) return;
        if (focusedCkeditorCanRedo(doc)) return;
        claimKeystroke(event);
        redo();
    }
}

// Whether the next undo is typing that CKEditor recorded in a box other than the one with the
// focus, whose CKEditor would therefore do nothing with Ctrl+Z.
function nextUndoIsTypingInAnotherBox(doc: Document): boolean {
    if (theOneUndoStack.getLegacyProviderForNextUndo() !== "ckeditor")
        return false;
    const editor = getCkeditorToUndo();
    return !!editor && editor.element?.$ !== focusedEditable(doc);
}

// What this keystroke asks for: "undo" for Ctrl+Z, "redo" for Ctrl+Y or Ctrl+Shift+Z (which
// CKEditor also treats as Redo), undefined for anything else, including either with Alt or Meta.
// CKEditor recognizes its undo and redo keys by keyCode, which is the key's place on the keyboard,
// so it gets Ctrl+Z on any layout; on a Russian layout, for one, event.key is "я". This has to
// recognize every Ctrl+Z that CKEditor would, or CKEditor would undo the focused copy of a picture
// alone.
function undoKeyAction(event: KeyboardEvent): "undo" | "redo" | undefined {
    if (!event.ctrlKey || event.altKey || event.metaKey) return undefined;
    const key = event.key?.toLowerCase();
    if (key === "z" || event.keyCode === 90)
        return event.shiftKey ? "redo" : "undo";
    if ((key === "y" || event.keyCode === 89) && !event.shiftKey) return "redo";
    return undefined;
}

function claimKeystroke(event: KeyboardEvent): void {
    event.preventDefault();
    event.stopImmediatePropagation();
}

// The text block that has the focus, if any.
function focusedEditable(doc: Document): HTMLElement | null {
    return (
        (doc.activeElement as HTMLElement | null)?.closest<HTMLElement>(
            ".bloom-editable",
        ) ?? null
    );
}

// Whether the CKEditor of the text block that has the focus has a redo of its own: a snapshot
// after the current one. This reads the snapshots rather than CKEditor's redoable(), whose flag
// can say there is a redo after a save has dropped it.
function focusedCkeditorCanRedo(doc: Document): boolean {
    const editable = focusedEditable(doc) as
        | (HTMLElement & {
              bloomCkEditor?: {
                  undoManager?: { index: number; snapshots: object[] };
              };
          })
        | null;
    const undoManager = editable?.bloomCkEditor?.undoManager;
    return (
        !!undoManager && undoManager.index < undoManager.snapshots.length - 1
    );
}
