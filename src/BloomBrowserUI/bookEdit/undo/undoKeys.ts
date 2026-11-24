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
// entries (getEntryForNextUndo, which leaves the key to CKEditor while typing is newer). It takes
// Ctrl+Y (or Ctrl+Shift+Z) when the stack has something to redo and the focused block's CKEditor
// has nothing of its own: typing discards what the stack could redo, and a picture change
// clears the history of the CKEditors in its group (see restartTypingHistories in
// inlineImageInteractions.ts). Either way it stops the event, so CKEditor and the other key
// handlers never see it.
//
// In Change Layout mode origami owns both keys, so this leaves them alone. So do the reader
// tools, in a text block, while one of their markup tools is selected: they keep their own
// history of the marked text and take both keys on the editable. That history starts again after
// a picture change (see restartTypingHistories), so their Ctrl+Z cannot put an old picture back.

import { getToolboxBundleExports } from "../js/workspaceFrames";
import { isInChangeLayoutMode } from "./redoKeyBinding";
import { theOneUndoStack } from "./UndoStack";

// The documents that already have the listener, so that setting up twice does not undo twice.
const documentsWithUndoKeys = new WeakSet<Document>();

/**
 * Take Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z in `doc` for the entries on the one undo stack, before
 * CKEditor sees them (see the comment at the top of this file). Installing it twice on one
 * document does nothing more.
 */
export function installUndoKeys(doc: Document): void {
    if (documentsWithUndoKeys.has(doc)) return;
    documentsWithUndoKeys.add(doc);
    doc.addEventListener("keydown", onUndoOrRedoKeyDown, true);
}

function onUndoOrRedoKeyDown(event: KeyboardEvent): void {
    // This sees every keystroke in the page, so the key comes first, before anything that
    // searches the page or reaches into the toolbox frame.
    const isUndo = isUndoKeystroke(event);
    if (!isUndo && !isRedoKeystroke(event) && !isShiftedUndoKeystroke(event))
        return;
    const doc = (event.target as Node).ownerDocument ?? document;
    if (isInChangeLayoutMode(doc)) return;
    // A form field has its own undo of what was typed in it.
    if ((event.target as Element).closest?.("input, textarea, select")) return;
    if (
        (event.target as Element).closest?.(".bloom-editable") &&
        getToolboxBundleExports()?.readerToolsHandleUndoKeys()
    )
        return;
    if (isUndo) {
        if (!theOneUndoStack.getEntryForNextUndo()) return;
        claimKeystroke(event);
        void theOneUndoStack.undo();
    } else {
        if (!theOneUndoStack.canRedo()) return;
        if (focusedCkeditorCanRedo(doc)) return;
        claimKeystroke(event);
        void theOneUndoStack.redo();
    }
}

// Whether this is the Z (or Y) key. CKEditor recognizes its undo and redo keys by keyCode, which
// is the key's place on the keyboard, so it gets Ctrl+Z on any layout; on a Russian layout, for
// one, event.key is "я". This has to recognize every Ctrl+Z that CKEditor would, or CKEditor
// would undo the focused copy of a picture alone.
function isZKey(event: KeyboardEvent): boolean {
    return event.key === "z" || event.key === "Z" || event.keyCode === 90;
}
function isYKey(event: KeyboardEvent): boolean {
    return event.key === "y" || event.key === "Y" || event.keyCode === 89;
}

// Ctrl+Z with no other modifier.
function isUndoKeystroke(event: KeyboardEvent): boolean {
    return (
        event.ctrlKey &&
        !event.altKey &&
        !event.metaKey &&
        !event.shiftKey &&
        isZKey(event)
    );
}

// Ctrl+Y with no other modifier.
function isRedoKeystroke(event: KeyboardEvent): boolean {
    return (
        event.ctrlKey &&
        !event.altKey &&
        !event.metaKey &&
        !event.shiftKey &&
        isYKey(event)
    );
}

// Ctrl+Shift+Z, which CKEditor also treats as Redo.
function isShiftedUndoKeystroke(event: KeyboardEvent): boolean {
    return (
        event.ctrlKey &&
        event.shiftKey &&
        !event.altKey &&
        !event.metaKey &&
        isZKey(event)
    );
}

function claimKeystroke(event: KeyboardEvent): void {
    event.preventDefault();
    event.stopImmediatePropagation();
}

// Whether the CKEditor of the text block that has the focus has a redo of its own: a snapshot
// after the current one. This reads the snapshots rather than CKEditor's redoable(), whose flag
// can say there is a redo after a save has dropped it.
function focusedCkeditorCanRedo(doc: Document): boolean {
    const editable = (doc.activeElement as HTMLElement | null)?.closest(
        ".bloom-editable",
    ) as
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
