// Which of two independent undo stacks should answer the next Undo.
//
// A table's structural operations (add a row, change a cell's content type, ...) go through the
// bloom-table library's own history, and typing inside a cell goes through CKEditor's. Neither
// knows about the other, and the table's history keeps whatever it holds until it is undone. So
// "add a row, then type" leaves both stacks non-empty at once, and a caller that always asks the
// table first takes the row back off while the typing, which came later, is what the person meant.
//
// The two stacks cannot be merged, so we record the order they were last written in and let that
// decide. The counter is a bare sequence number rather than a clock because two changes in the
// same millisecond are ordinary and a tie has no right answer.

let changeCounter = 0;
// One entry per operation in the table library's history, oldest first, so that undoing one
// hands the "last changed" answer back to the operation before it rather than leaving the table
// marked as more recent than typing it came before.
let tableChangeOrders: number[] = [];
let ckeditorChangeOrder = 0;

/**
 * Record what the table library just did to its history, given the `operation` its
 * tableHistoryUpdated event names: an operation of its own or a redo adds an entry, an undo
 * ("Undo <label>") takes the latest off, and "Clear History" empties it.
 */
export function noteTableHistoryUpdate(operation: string | undefined): void {
    if (operation?.startsWith("Undo ")) tableChangeOrders.pop();
    else if (operation === "Clear History") tableChangeOrders = [];
    // Detaching a table drops only redo entries, which have no place in this list.
    else if (operation !== "Detach Table")
        tableChangeOrders.push(++changeCounter);
}

/** Record that CKEditor has just recorded a change of its own (a keystroke, a paste, ...). */
export function noteCkeditorChange(): void {
    ckeditorChangeOrder = ++changeCounter;
}

/** When the operation now at the top of the table library's history was made, on the shared counter. */
export function getTableChangeOrder(): number {
    return tableChangeOrders[tableChangeOrders.length - 1] ?? 0;
}

/** When CKEditor last recorded a change, on the shared counter. */
export function getCkeditorChangeOrder(): number {
    return ckeditorChangeOrder;
}

/**
 * Whether the next Undo belongs to the table rather than to CKEditor.
 *
 * Pure, and exported so it can be tested on its own. A table with nothing to undo never wins; a
 * table with something to undo wins unless CKEditor also has something to undo AND changed more
 * recently than the table did.
 */
export function shouldUndoGoToTable(state: {
    tableCanUndo: boolean;
    ckeditorCanUndo: boolean;
    tableChangeOrder: number;
    ckeditorChangeOrder: number;
}): boolean {
    if (!state.tableCanUndo) return false;
    if (!state.ckeditorCanUndo) return true;
    return state.tableChangeOrder > state.ckeditorChangeOrder;
}

/** Forget both stacks' recorded order. For tests, and for tearing table editing down. */
export function resetUndoOrderingForTests(): void {
    changeCounter = 0;
    tableChangeOrders = [];
    ckeditorChangeOrder = 0;
}
