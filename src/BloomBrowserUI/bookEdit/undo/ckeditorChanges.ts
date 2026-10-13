// What a change CKEditor records means for the one undo stack (BL-6681).
//
// Typing is still CKEditor's to undo, in its own per-box history, so the stack hears about it only
// through CKEditor's "change" event. Two things follow from a change the person made:
// - it is the most recent change, which the next Undo belongs to (changeOrder.ts);
// - anything the stack could redo is now on an abandoned branch, so it is discarded, as typing
//   after an undo discards the redo in every editor. Otherwise "change a picture, Undo, type,
//   Ctrl+Y" would put the picture change back on top of the typing.
//
// CKEditor fires "change" for its own Undo and Redo too (its undo manager's restoreImage does), and
// those are not new edits: undoing typing must not discard a picture change the stack could still
// redo. So a change that happens while CKEditor runs its "undo" or "redo" command counts for the
// ordering, as before, but leaves the redo branch alone.
//
// This goes away in Stage 3, when typing moves onto the stack itself.

import { noteCkeditorChange } from "./changeOrder";
import { theOneUndoStack, UndoStack } from "./UndoStack";

/** The part of a CKEditor instance this needs. Kept small so a test can fake it. */
export interface ICkeditorChangeSource {
    on(
        eventName: string,
        listener: (evt: { data?: { name?: string } }) => void,
        scope?: unknown,
        listenerData?: unknown,
        priority?: number,
    ): void;
    undoManager?: { undoable(): boolean };
}

// Run after every other beforeCommandExec listener. One that runs earlier can cancel the command
// (the inline image Ctrl+Z handler does), which stops the listeners after it; this one then never
// marks a command that did not run.
const kAfterOtherListeners = 999;

/**
 * Tell the one undo stack about the changes `editor` records: their order against the stack's
 * entries, and, for a new edit, that the stack's redo branch is abandoned. Call once per editor.
 */
export function trackCkeditorChanges(
    editor: ICkeditorChangeSource,
    stack: UndoStack = theOneUndoStack,
): void {
    let runningUndoOrRedo = false;
    const isUndoOrRedo = (evt: { data?: { name?: string } }) =>
        evt.data?.name === "undo" || evt.data?.name === "redo";

    editor.on(
        "beforeCommandExec",
        (evt) => {
            if (isUndoOrRedo(evt)) runningUndoOrRedo = true;
        },
        null,
        null,
        kAfterOtherListeners,
    );
    editor.on("afterCommandExec", (evt) => {
        if (isUndoOrRedo(evt)) runningUndoOrRedo = false;
    });

    // The undoable() test keeps the changes CKEditor makes while it attaches itself to a box out
    // of the reckoning: only a change a person could undo counts as a change the person made.
    editor.on("change", () => {
        if (!editor.undoManager?.undoable()) return;
        noteCkeditorChange();
        if (!runningUndoOrRedo) stack.discardRedo();
    });
}
