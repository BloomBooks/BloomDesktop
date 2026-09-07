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
let tableChangeOrder = 0;
let ckeditorChangeOrder = 0;

/** Record that the table library has just finished an operation of its own. */
export function noteTableChange(): void {
    tableChangeOrder = ++changeCounter;
}

/** Record that CKEditor has just recorded a change of its own (a keystroke, a paste, ...). */
export function noteCkeditorChange(): void {
    ckeditorChangeOrder = ++changeCounter;
}

/** When the table library last finished an operation, on the shared counter. */
export function getTableChangeOrder(): number {
    return tableChangeOrder;
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
    tableChangeOrder = 0;
    ckeditorChangeOrder = 0;
}
