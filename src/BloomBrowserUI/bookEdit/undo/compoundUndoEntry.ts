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
 * - **A part whose undo fails** stops the undo where it is and propagates the failure. The
 *   compound remembers how many parts are still in effect, and the stack keeps a failed entry as
 *   the next thing to undo, so a retry continues from the part that failed instead of repeating
 *   the parts already undone.
 * - **A part whose redo fails** makes the compound undo the parts this redo had already
 *   re-applied, and then propagates the failure. The stack keeps a failed entry on the redo
 *   branch, where a new edit would discard it; parts left applied there could never be undone.
 *   Rolling them back leaves the gesture wholly undone, which is what that position means. (If the
 *   rollback itself fails there is nothing better to do; its failure is what propagates.)
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

    // Undo parts, last applied first, until only `floor` of them are still in effect.
    const undoDownTo = (floor: number): void | Promise<void> => {
        while (inEffect > floor) {
            const part = parts[inEffect - 1];
            part.prepareRedo?.();
            const pending = part.undo();
            if (pending) {
                return pending.then(() => {
                    inEffect--;
                    return undoDownTo(floor);
                });
            }
            inEffect--;
        }
    };

    const redo = (): void | Promise<void> => {
        const start = inEffect;
        const rollBack = (failure: unknown): void | Promise<void> => {
            const pending = undoDownTo(start);
            if (pending) {
                return pending.then(() => {
                    throw failure;
                });
            }
            throw failure;
        };
        let pending: void | Promise<void>;
        try {
            pending = redoRemaining();
        } catch (failure) {
            return rollBack(failure);
        }
        return pending?.catch(rollBack);
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
        undo: () => undoDownTo(0),
    };
    if (parts.every((part) => part.redo)) {
        entry.redo = redo;
    }
    return entry;
}
