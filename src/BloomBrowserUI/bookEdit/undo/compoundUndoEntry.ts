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
 * - **Page scope.** The compound belongs to a page if any part does, so a page change discards the
 *   whole gesture rather than leaving the parts that survive it, which would undo half a gesture.
 *   (Within one scope that did not straddle a page change, every page-scoped part was recorded
 *   against the same page; see UndoStack.endUndoableScope.)
 * - **A part that fails** stops the run where it is and propagates the failure. The compound
 *   remembers how many parts are still in effect, so when the stack offers it again (it keeps a
 *   failed entry as the next thing to undo or redo), a retry continues from the part that failed
 *   instead of repeating the parts that already ran.
 *
 * Parts may be synchronous or asynchronous. When every part is synchronous, so is the compound,
 * which keeps an ordinary gesture's undo synchronous.
 */
export function makeCompoundUndoEntry(
    label: string,
    parts: IUndoEntry[],
): IUndoEntry {
    // parts[0 .. inEffect) are applied; the rest have been undone.
    let inEffect = parts.length;

    const undoRemaining = (): void | Promise<void> => {
        while (inEffect > 0) {
            const part = parts[inEffect - 1];
            part.prepareRedo?.();
            const pending = part.undo();
            if (pending) {
                return pending.then(() => {
                    inEffect--;
                    return undoRemaining();
                });
            }
            inEffect--;
        }
    };

    const redoRemaining = (): void | Promise<void> => {
        while (inEffect < parts.length) {
            const pending = parts[inEffect].redo!();
            if (pending) {
                return pending.then(() => {
                    inEffect++;
                    return redoRemaining();
                });
            }
            inEffect++;
        }
    };

    const entry: IUndoEntry = {
        label,
        pageId: parts.find((part) => part.pageId !== undefined)?.pageId,
        kind: "custom",
        undo: undoRemaining,
    };
    if (parts.every((part) => part.redo)) {
        entry.redo = redoRemaining;
    }
    return entry;
}
