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
// redo. So a change that happens while CKEditor runs its "undo" or "redo" command (Ctrl+Z and
// Ctrl+Y in a box) counts for the ordering, as before, but leaves the redo branch alone. The Undo
// button reaches CKEditor's history another way, through the stack's CKEditor provider, which
// calls the undo manager directly; the stack itself ignores discardRedo while that runs.
//
// Each box keeps its own history, so this also records which boxes changed and when, so that an
// Undo of typing goes to the box typed in most recently, wherever the focus is now, and the stack
// compares its entries with when that typing was done (getCkeditorsToUndo, getCkeditorUndoOrder).
//
// This goes away in Stage 3, when typing moves onto the stack itself.

import { getCkeditorChangeOrder, noteCkeditorChange } from "./changeOrder";
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
    undoManager?: { undoable(): boolean; undo?(): boolean };
    /** The editable the editor edits, wrapped as CKEditor wraps elements. */
    element?: { $: HTMLElement };
}

// The editor of the box that has the focus: CKEditor's current instance. C# may call into this
// module before the module that defines CKEDITOR is even loaded, so it is read from the window,
// where a missing one is undefined rather than an error.
function focusedCkeditor(): ICkeditorChangeSource | undefined {
    return (
        (
            window as unknown as {
                CKEDITOR?: { currentInstance?: ICkeditorChangeSource | null };
            }
        ).CKEDITOR?.currentInstance ?? undefined
    );
}

// The editors that recorded a change a person could undo, the most recent first, each with when it
// last did, on the sequence the stack's entries share (changeOrder.ts). Typing is undone in the box
// it was typed in, which is not always the box that has the focus: "add a row, type in it, click
// another cell" leaves the typing in the new row's editor while the focus is elsewhere.
// It has no size limit: it never holds more than the page's text boxes, and getCkeditorsToUndo
// drops the ones with nothing left to undo. A box dropped while it still had typing would lose
// its place in the order, and its older typing would be undone ahead of newer changes.
let changedEditors: Array<{ editor: ICkeditorChangeSource; order: number }> =
    [];

/**
 * The CKEditors whose history an Undo of typing may take from, in the order to try them: the one
 * whose typing is the most recent first (orderOf). Each has something to undo and is still in the
 * page; an editor whose box a table undo rebuilt, for instance, is left out, since undoing there
 * would change nothing anyone can see.
 *
 * The editor that has the focus (CKEditor's current instance) is among them even when none of its
 * changes was recorded. Such an editor counts as having made the latest change, and on a tie the
 * focused editor goes first, so a box with something to undo of its own keeps its Undo.
 */
export function getCkeditorsToUndo(): ICkeditorChangeSource[] {
    const focusedEditor = focusedCkeditor();
    // Editors with nothing left to undo, or whose boxes have left the page, are dropped here, so
    // removed boxes' editors are not kept alive.
    changedEditors = changedEditors.filter((e) => canUndoIn(e.editor));
    const candidates = changedEditors
        .map((e) => e.editor)
        .filter((e) => e !== focusedEditor);
    if (focusedEditor && canUndoIn(focusedEditor))
        candidates.unshift(focusedEditor);
    // A stable sort, so the focused editor, put first above, stays first among equals.
    return candidates.sort((a, b) => orderOf(b) - orderOf(a));
}

// Whether `editor` has something to undo and its box is still in the page.
function canUndoIn(editor: ICkeditorChangeSource): boolean {
    return !!editor.undoManager?.undoable() && !!editor.element?.$.isConnected;
}

/** The CKEditor an Undo of typing would try first (see getCkeditorsToUndo), if any. */
export function getCkeditorToUndo(): ICkeditorChangeSource | undefined {
    return getCkeditorsToUndo()[0];
}

/**
 * When the typing an Undo would take back first was done, on the sequence the stack's entries
 * share, so that the stack can tell whether it or its own newest entry is the more recent.
 */
export function getCkeditorUndoOrder(): number {
    return orderOf(getCkeditorToUndo());
}

// When `editor` recorded its latest change; for an editor with no change recorded, or none, the
// last change CKEditor recorded anywhere.
function orderOf(editor: ICkeditorChangeSource | undefined): number {
    return (
        changedEditors.find((e) => e.editor === editor)?.order ??
        getCkeditorChangeOrder()
    );
}

/**
 * Undo typing in the first of getCkeditorsToUndo whose undo finds something to restore, and say
 * whether any did. Typing done no later than `newerThan` (by default, when the stack's newest entry
 * was recorded) is left alone: that entry comes before it.
 *
 * An editor's undo() answers false when it finds nothing to restore, which happens even when its
 * undoable() said yes: CKEditor sets its hasUndo flag on the first keystroke of a group and clears
 * it only when it next refreshes its state, so a manager whose snapshots have already been
 * restored keeps claiming an undo it cannot perform. Such an editor is forgotten and the next one
 * gets its turn, rather than the Undo passing over newer typing to an older change on the stack;
 * and the caller learns when none undid anything, so that an Undo the person pressed is not
 * swallowed. (See the CKEditor provider in legacyUndoProviders.ts.)
 */
export function undoCkeditorTyping(
    newerThan: number = theOneUndoStack.getNewestEntryOrder(),
): boolean {
    for (const editor of getCkeditorsToUndo()) {
        if (orderOf(editor) <= newerThan) return false;
        if (editor.undoManager?.undo?.()) return true;
        changedEditors = changedEditors.filter((e) => e.editor !== editor);
    }
    return false;
}

/** Forget which editors changed. For tests. */
export function resetCkeditorChangesForTests(): void {
    changedEditors = [];
}

// Run after every other beforeCommandExec listener. One that runs earlier can cancel the command,
// which stops the listeners after it; this one then never marks a command that did not run.
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

    // Only a change a person could undo counts as a change the person made; that keeps out the
    // changes CKEditor makes while it attaches itself to a box. But CKEditor fires "change" before
    // it saves the snapshot that makes a box undoable, so the first change in a box that no
    // keystroke led up to (a paste, a cut) is not yet undoable when reported. For the box that has
    // the focus, where a person makes such changes, it is checked again once CKEditor has saved
    // it. A box CKEditor is attaching itself to, such as the cells a table operation builds, does
    // not have the focus.
    editor.on("change", () => {
        const duringUndoOrRedo = runningUndoOrRedo;
        const recordIfUndoable = () => {
            if (editor.undoManager?.undoable())
                recordChange(editor, duringUndoOrRedo, stack);
        };
        if (!editor.undoManager?.undoable() && editor === focusedCkeditor())
            queueMicrotask(recordIfUndoable);
        else recordIfUndoable();
    });
}

// Note a change `editor` recorded: its place in the order (changeOrder.ts), the editor as the most
// recently changed, and, for a new edit rather than CKEditor's own undo or redo, that the stack's
// redo branch is abandoned.
function recordChange(
    editor: ICkeditorChangeSource,
    duringUndoOrRedo: boolean,
    stack: UndoStack,
): void {
    noteCkeditorChange();
    const order = getCkeditorChangeOrder();
    // Usually the box typed in is already the most recent.
    if (changedEditors[0]?.editor === editor) {
        changedEditors[0].order = order;
    } else {
        changedEditors = [
            { editor, order },
            ...changedEditors.filter((e) => e.editor !== editor),
        ];
    }
    if (!duringUndoOrRedo) stack.discardRedo();
}
