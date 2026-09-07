// Undo for deleting a canvas element (BL-6681, PLAN.md 6 Stage 2b) — the workspace-frame half.
//
// The page frame describes what it deleted (an ICanvasElementDeletionRecord: pure data) and hands
// it across; this module builds the undo entry and pushes it. The entry's closures live in THIS
// frame, which survives page changes, and re-acquire the page frame at undo time — the pattern
// undoTypes.ts prescribes. This is also the first realization of DEFERRED-EDITS.md 1f: what
// crosses the frame boundary is a description, never a closure and never `push` itself.

import { getEditablePageBundleExports } from "../js/workspaceFrames";
import type { ICanvasElementDeletionRecord } from "./canvasElementDeletion";
import { theOneUndoStack, UndoStack } from "./UndoStack";

/** The page frame's exports, or a thrown error the stack will treat as a failed undo/redo. */
function pageFrame() {
    const exports = getEditablePageBundleExports();
    if (!exports) {
        throw new Error(
            "Cannot undo or redo a canvas element deletion: there is no page frame",
        );
    }
    return exports;
}

/**
 * Record that a canvas element was deleted, so Undo can bring it back and Redo can delete it
 * again. Called from the page frame (via the workspace bundle) just before the deletion.
 *
 * @param stack defaults to the one real stack; a parameter only so tests need not use a singleton.
 */
export function recordCanvasElementDeletion(
    record: ICanvasElementDeletionRecord,
    stack: UndoStack = theOneUndoStack,
): void {
    stack.push({
        label: "Delete canvas element",
        pageId: record.pageId,
        kind: "subtreeSnapshot",
        undo: () => pageFrame().restoreDeletedCanvasElement(record),
        redo: () => pageFrame().redeleteCanvasElement(record),
    });
}
