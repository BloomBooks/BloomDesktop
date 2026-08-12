// One user gesture, one undo entry (BL-6681, PLAN.md 4.13).
//
// Wrapping an operation in runUndoable() says "everything in here is one undoable step". It exists
// because nesting is not hypothetical: deleting a canvas element whose content is a background
// image already records an image undo of its own, so wrapping the delete naively would leave two
// entries for one gesture and the first Ctrl+Z would half-undo it. Call sites accrete, so the
// semantics are fixed here from the start rather than patched in when a bug turns up.

import { theOneUndoStack, UndoStack } from "./UndoStack";

/**
 * Run `operation` as a single undoable step, however many nested operations record undos inside it.
 *
 * The outermost scope wins: the first entry pushed inside it is kept and takes `label` as its
 * label, and any further pushes within the scope are dropped. `label` is what the user would call
 * the whole gesture — "Delete canvas element" — not what the innermost layer of code calls it.
 *
 * Works for a synchronous or an asynchronous operation: if `operation` returns a promise the scope
 * stays open until it settles, and the promise is passed through. Two *independent* asynchronous
 * undoables must not overlap in time — the scope depth is global, so an unrelated operation
 * starting while another is awaiting would be treated as nested. Every intended use is a single
 * user gesture, so overlap does not arise; it is written down because it would be invisible.
 *
 * @param stack defaults to the one real stack; a parameter only so tests need not use a singleton.
 */
export function runUndoable<T>(
    label: string,
    operation: () => T,
    stack: UndoStack = theOneUndoStack,
): T {
    stack.beginUndoableScope(label);
    let result: T;
    try {
        result = operation();
    } catch (e) {
        stack.endUndoableScope();
        throw e;
    }
    const promise = result as unknown as Promise<unknown> | undefined;
    if (typeof promise?.finally === "function") {
        return promise.finally(() => {
            stack.endUndoableScope();
        }) as unknown as T;
    }
    stack.endUndoableScope();
    return result;
}
