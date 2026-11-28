// The page frame's undo entry points (BL-6681).
//
// The one undo stack lives in the page frame and dies with it (PLAN.md 4.2), so undo covers changes
// to the page as it is currently loaded. This file sets the stack up when the page loads and offers
// the four functions the rest of Bloom reaches undo through, all exported on the page bundle
// (editablePage.ts):
// - the Undo button: C# calls topBarButtonClick("undo") in this frame, which calls handleUndo;
// - its enabled state: C# polls workspaceBundle.canUndo(), which asks this frame's canUndo;
// - Ctrl+Y: the page's undo keys (undoKeys.ts) redo through handleRedo. There is no Redo button,
//   so canRedo is for the e2e tests, which have no other way to see whether there is a redo.
// Ctrl+Z reaches the stack from the page's undo keys directly.

import { registerLegacyUndoProviders } from "./legacyUndoProviders";
import { installUndoKeys } from "./undoKeys";
import { theOneUndoStack } from "./UndoStack";

/**
 * Set up undo for the page that has just loaded: register the pre-existing undo mechanisms with the
 * stack, and bind Ctrl+Z and Ctrl+Y. Call once per page load, from the page frame.
 *
 * Ctrl+Y reaches the stack through the page bundle (`window.editablePageBundle`) rather than
 * directly, so that it goes through the same exported function as everything else, which is the
 * one the e2e tests watch.
 */
export function setUpPageUndo(): void {
    registerLegacyUndoProviders();
    installUndoKeys(document, () =>
        (
            window as unknown as {
                editablePageBundle: { handleRedo(): void };
            }
        ).editablePageBundle.handleRedo(),
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

/** Redo one step: Ctrl+Y, from the page's undo keys (undoKeys.ts). There is no Redo button. */
export function handleRedo(): void {
    void theOneUndoStack.redo();
}

/** Whether the stack has anything to redo. O(1). For the e2e tests; see the top of this file. */
export function canRedo(): boolean {
    return theOneUndoStack.canRedo();
}
