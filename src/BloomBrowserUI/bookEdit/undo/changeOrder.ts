// Which came last: an entry on the one undo stack, or a change CKEditor recorded (BL-6681).
//
// Typing is still CKEditor's to undo, in its own per-box history, while operations converted to the
// stack (a table's structural operations, deleting a canvas element) are the stack's. Both can hold
// something at once: "add a row, then type in a cell" leaves the row on the stack and the typing in
// CKEditor, and the next Undo belongs to the typing. So the two are numbered from one counter, and
// the CKEditor provider stands aside when the stack's newest entry is the more recent
// (UndoStack.canUndo). A bare sequence number rather than a clock, because two changes in the same
// millisecond are ordinary and a tie has no right answer.
//
// This goes away in Stage 3, when typing moves onto the stack itself.

let changeCounter = 0;
let ckeditorChangeOrder = 0;

/** The next number in the sequence. The stack takes one for each entry it records. */
export function nextChangeOrder(): number {
    return ++changeCounter;
}

/** Record that CKEditor has just recorded a change of its own (a keystroke, a paste, ...). */
export function noteCkeditorChange(): void {
    ckeditorChangeOrder = nextChangeOrder();
}

/** When CKEditor last recorded a change, on the shared sequence; 0 if it never has. */
export function getCkeditorChangeOrder(): number {
    return ckeditorChangeOrder;
}

/** Forget the sequence. For tests. */
export function resetChangeOrderForTests(): void {
    changeCounter = 0;
    ckeditorChangeOrder = 0;
}
