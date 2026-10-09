// A table's structural operations as entries on the one undo stack (BL-6681, BL-16818).
//
// The bloom-table library keeps its own history of what it did to each table (add a row, change a
// cell's content type, ...) and knows how to undo and redo it. What it cannot know is how those
// operations interleave with everything else on the page. So each operation it announces becomes
// an entry on the one stack, and the entry asks the library to undo or redo it: the library still
// does the work, and the stack decides when. Typing in a cell is CKEditor's, and is ordered against
// these entries by changeOrder.ts.
//
// The library's history and ours have to stay in step: the library's newest operation must be the
// one our next table entry stands for. Nothing else in Bloom undoes a table operation, and an entry
// checks before acting that the library's newest operation is its own, throwing (so the stack
// discards itself, PLAN.md 4.1) if not.
//
// Two gaps, left until typing moves onto the stack in Stage 3: CKEditor's side keeps only its
// latest change, so undoing typing does not hand the turn back to an earlier table operation
// ("type, add a row, type, Undo, Undo" takes the first typing before the row); and a table Undo
// rebuilds the cells, which discards their CKEditor typing history.

import { tableHistoryManager } from "bloom-table";
import { theOneUndoStack, UndoStack } from "./UndoStack";
import { IUndoEntry } from "./undoTypes";

/**
 * Counts the times the library has dropped history our entries may stand for: it clears its
 * history, or forgets a table's, when tables are detached as the page is left. An entry made
 * before then can no longer be undone by the library, and says so rather than undoing something
 * else.
 */
let historyGeneration = 0;

/** True while one of our entries is having the library undo or redo, whose announcement we expect. */
let applyingOurEntry = false;

/**
 * Note what the table library just did to its history, given the `operation` its
 * tableHistoryUpdated event names. An operation of its own becomes an entry on the stack. The
 * library announces our entries' undos and redos as "Undo <label>" and "Redo <label>"; those, and
 * the library dropping history ("Clear History", "Detach Table"), add nothing.
 *
 * @param stack defaults to the one real stack; a parameter only so tests need not use a singleton.
 */
export function noteTableHistoryUpdate(
    operation: string | undefined,
    stack: UndoStack = theOneUndoStack,
): void {
    if (!operation) {
        return;
    }
    if (operation === "Clear History" || operation === "Detach Table") {
        historyGeneration++;
        return;
    }
    if (operation.startsWith("Undo ") || operation.startsWith("Redo ")) {
        if (!applyingOurEntry) {
            // Something other than the stack undid or redid a table operation, so the library's
            // history and ours no longer match. Nothing in Bloom does this today.
            historyGeneration++;
        }
        return;
    }
    stack.push(makeTableUndoEntry(operation));
}

/**
 * Whether the bloom-table library has an operation in its history it could undo, and an Undo may
 * reach it. In Change Layout mode it may not: the tables stay attached there, so their history
 * still holds what was done before the mode was entered, and an Undo in the mode belongs to
 * origami. Taking a row off a faded table the person cannot edit would be invisible and wrong.
 */
export function tableCanUndo(): boolean {
    return !isInChangeLayoutMode() && tableHistoryManager.canUndo();
}

/** The stack entry for one operation the library has just recorded, labelled as the library labels it. */
function makeTableUndoEntry(operation: string): IUndoEntry {
    const generation = historyGeneration;
    // What the library calls this operation. Kept here because the stack may relabel the entry
    // when it is recorded as part of a larger gesture.
    const libraryLabel = operation;
    const checkStillInStep = (newestLibraryLabel: string | null) => {
        if (
            generation !== historyGeneration ||
            newestLibraryLabel !== libraryLabel
        ) {
            throw new Error(
                `The table's own history no longer holds "${libraryLabel}" where the undo stack ` +
                    `expects it (it holds "${newestLibraryLabel}").`,
            );
        }
    };
    const runInLibrary = (action: () => boolean, what: string) => {
        applyingOurEntry = true;
        try {
            if (!action()) {
                throw new Error(
                    `The table library could not ${what} "${libraryLabel}".`,
                );
            }
        } finally {
            applyingOurEntry = false;
        }
    };
    return {
        label: operation,
        kind: "custom",
        isAvailable: () => !isInChangeLayoutMode(),
        undo: () => {
            checkStillInStep(tableHistoryManager.getLastOperationLabel());
            runInLibrary(() => tableHistoryManager.undoLast(), "undo");
        },
        redo: () => {
            checkStillInStep(tableHistoryManager.getNextRedoLabel());
            runInLibrary(() => tableHistoryManager.redoLast(), "redo");
        },
    };
}

/** Whether the page is in Change Layout mode, which origami signals on `.marginBox`. */
function isInChangeLayoutMode(): boolean {
    return !!document.querySelector(".origami-layout-mode");
}

/** Forget what the library has done. For tests. */
export function resetTableUndoForTests(): void {
    historyGeneration = 0;
    applyingOurEntry = false;
}
