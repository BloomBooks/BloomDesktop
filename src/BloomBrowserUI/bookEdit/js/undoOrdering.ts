// Which of the independent undo stacks should answer the next Undo.
//
// A table's structural operations (add a row, change a cell's content type, ...) go through the
// bloom-table library's own history, and typing inside a cell goes through CKEditor's. Neither
// knows about the other, and the table's history keeps whatever it holds until it is undone. So
// "add a row, then type" leaves both stacks non-empty at once, and a caller that always asks the
// table first takes the row back off while the typing, which came later, is what the person meant.
//
// Inline (Word-style) images have a third stack of their own (see inlineImages.ts), which takes
// its numbers from the same counter. Without that, "add a row, then resize a picture in a text
// block" would have the Undo button take the row back off, because the table is asked first.
//
// The stacks cannot be merged, so we record the order they were last written in and let that
// decide. The counter is a bare sequence number rather than a clock because two changes in the
// same millisecond are ordinary and a tie has no right answer.
//
// Two known gaps, left until BL-16900 (one undo stack behind the Undo button) is in master, which
// is when this file should be revisited: CKEditor's side keeps only its latest change, so undoing
// typing does not hand the turn back to an earlier table operation ("type, add a row, type, Undo,
// Undo" takes the first typing before the row); and a table Undo rebuilds the cells, which
// discards their CKEditor typing history.

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

/**
 * Take the next number on the shared counter for an inline image change that has just been put on
 * the inline image undo stack. That stack keeps the number with the undo point, so undoing the
 * point takes its number off with it, as noteTableHistoryUpdate does for the table.
 */
export function takeInlineImageChangeOrder(): number {
    return ++changeCounter;
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
 * Whether the next Undo belongs to the table rather than to CKEditor or an inline image.
 *
 * Pure, and exported so it can be tested on its own. A table with nothing to undo never wins; a
 * table with something to undo wins unless CKEditor or the inline image stack also has something
 * to undo AND changed more recently than the table did.
 */
export function shouldUndoGoToTable(state: {
    tableCanUndo: boolean;
    ckeditorCanUndo: boolean;
    inlineImageCanUndo: boolean;
    tableChangeOrder: number;
    ckeditorChangeOrder: number;
    inlineImageChangeOrder: number;
}): boolean {
    if (!state.tableCanUndo) return false;
    if (
        state.ckeditorCanUndo &&
        state.ckeditorChangeOrder > state.tableChangeOrder
    )
        return false;
    if (
        state.inlineImageCanUndo &&
        state.inlineImageChangeOrder > state.tableChangeOrder
    )
        return false;
    return true;
}

/** Forget the recorded order of the table's and CKEditor's changes. For tests, and for tearing table editing down. */
export function resetUndoOrderingForTests(): void {
    changeCounter = 0;
    tableChangeOrders = [];
    ckeditorChangeOrder = 0;
}
