// The page frame's undo entry points (BL-6681).
//
// The one undo stack lives in the page frame and dies with it (PLAN.md 4.2), so undo covers changes
// to the page as it is currently loaded. This file sets the stack up when the page loads and offers
// the four functions the rest of Bloom reaches undo through, all exported on the page bundle
// (editablePage.ts):
// - the Undo button: C# calls topBarButtonClick("undo") in this frame, which calls handleUndo;
// - its enabled state: C# polls workspaceBundle.canUndo(), which asks this frame's canUndo;
// - Ctrl+Y and Ctrl+Shift+Z: the last-resort binding in this frame (redoKeyBinding.ts) uses
//   canRedo and handleRedo.

import { registerLegacyUndoProviders } from "./legacyUndoProviders";
import { installRedoKeyBinding, IRedoTarget } from "./redoKeyBinding";
import { theOneUndoStack } from "./UndoStack";

/**
 * Set up undo for the page that has just loaded: register the pre-existing undo mechanisms with the
 * stack, and bind the Redo keys. Call once per page load, from the page frame.
 *
 * The Redo keys reach the stack through the page bundle (`window.editablePageBundle`) rather than
 * directly, so that they go through the same exported function as everything else, which is the
 * one the e2e tests watch.
 */
export function setUpPageUndo(): void {
    registerLegacyUndoProviders();
    installRedoKeyBinding(
        document,
        () =>
            (window as unknown as { editablePageBundle?: IRedoTarget })
                .editablePageBundle ?? null,
    );
}

/** Undo one step: the Undo button. */
export function handleUndo(): void {
    void theOneUndoStack.undo();
}

/** Whether there is anything to undo. Cheap: C# polls it on a timer, through the workspace frame. */
export function canUndo(): boolean {
    return theOneUndoStack.canUndo();
}

/** Redo one step: Ctrl+Y or Ctrl+Shift+Z, from redoKeyBinding.ts. There is no Redo button. */
export function handleRedo(): void {
    void theOneUndoStack.redo();
}

/** Whether a Redo key would do anything. O(1): asked on every Redo keydown. */
export function canRedo(): boolean {
    return theOneUndoStack.canRedo();
}
