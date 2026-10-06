// One undo entry made of several (BL-6681, PLAN.md 4.13).
//
// A user gesture can pass through several layers of code that each record an undo: deleting a
// canvas element whose content is a background image records an image undo as well as the removal.
// The gesture must still be ONE entry on the stack, or the first Ctrl+Z would undo only part of it.
// So everything pushed while an outermost `runUndoable` scope is open becomes a part of one compound
// entry, which undoes the parts in reverse order and redoes them in the original order.

import { IUndoEntry } from "./undoTypes";

/**
 * Combine `parts`, in the order they were pushed, into one entry labelled `label`.
 *
 * - **Undo** runs the parts last to first, calling each part's `prepareRedo` just before its own
 *   `undo`, so that each part captures the state it will need to redo when that state is current.
 * - **Redo** runs the parts first to last. The compound can redo only if every part can; otherwise
 *   it has no `redo`, which makes it a redo floor like any other entry that cannot redo.
 * - **A part that fails** stops the run and propagates the failure, and the stack then discards
 *   everything (UndoStack.apply), so nothing here tries to resume or roll back.
 *
 * Parts may be synchronous or asynchronous. When every part is synchronous, so is the compound,
 * which keeps an ordinary gesture's undo synchronous.
 */
export function makeCompoundUndoEntry(
    label: string,
    parts: IUndoEntry[],
): IUndoEntry {
    const entry: IUndoEntry = {
        label,
        kind: "custom",
        undo: () =>
            runInSequence(
                [...parts].reverse().map((part) => () => {
                    part.prepareRedo?.();
                    return part.undo();
                }),
            ),
    };
    if (parts.every((part) => part.redo)) {
        entry.redo = () =>
            runInSequence(parts.map((part) => () => part.redo!()));
    }
    return entry;
}

/**
 * Run `steps` one after another, each starting only when the one before has finished. Stays
 * synchronous until a step returns a promise, and returns a promise from then on.
 */
function runInSequence(
    steps: (() => void | Promise<void>)[],
    from = 0,
): void | Promise<void> {
    for (let i = from; i < steps.length; i++) {
        const pending = steps[i]();
        if (pending) {
            return pending.then(() => runInSequence(steps, i + 1));
        }
    }
}
