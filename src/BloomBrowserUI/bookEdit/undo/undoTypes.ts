// The contract for Bloom's single undo stack (BL-6681).
//
// Today Bloom has five poorly-coordinated undo mechanisms; see docs/retire-ckeditor/PLAN.md 3.
// This file defines the one entry type they will all eventually become, plus the adapter interface
// that lets the old mechanisms take part before they are converted. Nothing here touches the DOM,
// so it can be unit-tested and imported from any frame.

/**
 * How an entry restores.
 *
 * - `custom` — the entry carries its own `undo()`. Fully supported now.
 * - `pageSnapshot` / `subtreeSnapshot` — the entry carries captured HTML and restores it through
 *   the tiered restore paths of PLAN.md 4.11. Declared here so the kind field is stable, but no
 *   snapshot entries exist until Stage 3; the stack treats them exactly like any other entry (it
 *   just calls `undo()`), so the factory that builds one owns the restore logic.
 */
export type UndoEntryKind = "pageSnapshot" | "subtreeSnapshot" | "custom";

/**
 * One undoable step.
 *
 * ## The rule that shapes this interface: an entry must not close over page-frame objects
 *
 * The page iframe's JS context dies not only when the user changes page but on same-page
 * *reloads* — ctrl+wheel zoom regenerates the page, leaving origami layout mode posts
 * `saveChangesAndRethinkPageEvent`, and several tools navigate. A function object created in that
 * frame dies with it, so an entry built by page-frame code becomes a live grenade: `undo()` would
 * mutate a detached document, or simply throw.
 *
 * So entries are **built in the workspace frame** (which survives), out of **pure data** — HTML
 * strings, indices, ids. Anything an entry needs from the page frame it must re-acquire *inside*
 * `undo()` via `getEditablePageBundleExports()`. Page-frame code that wants to record an undo
 * therefore sends a *description* of what happened across the frame boundary and lets the
 * workspace frame build the entry; it never sends a closure. See PLAN.md 4.1 and 4.2.
 */
export interface IUndoEntry {
    /** Human-readable, e.g. "Delete canvas element". For tooltips and logging, not identity. */
    label: string;

    /**
     * The page this entry belongs to, or `undefined` if it survives a page change.
     *
     * `undefined` is for workspace-owned operations — deleting a page being the main one, where
     * the whole point is that the page is gone. Everything else is page-scoped and is discarded
     * when the user moves to another page, because its captured state would no longer mean
     * anything.
     */
    pageId: string | undefined;

    /** Which restore strategy this entry represents. See {@link UndoEntryKind}. */
    kind: UndoEntryKind;

    /** Reverse the operation. May be async (a restore that has to wait for the page frame). */
    undo(): void | Promise<void>;

    /**
     * Re-apply the operation. Optional, so Redo can arrive one entry kind at a time: an entry
     * with no `redo` acts as a redo floor (`canRedo()` is false when the next entry can't redo).
     * That lets the one case needing real C# work — redoing a page deletion — be deferred without
     * holding up the rest.
     */
    redo?(): void | Promise<void>;

    /**
     * Capture whatever `redo()` will need, called by the stack immediately before `undo()` runs.
     *
     * Capturing the "after" state lazily like this is what keeps Redo nearly free: nothing extra
     * is paid on the common path (every typing transaction), only when the user actually undoes.
     * Bloom already does exactly this — `origamiUndo` stashes a fresh clone before stepping its
     * index back.
     */
    prepareRedo?(): void;
}

/**
 * An adapter round one of Bloom's pre-existing undo mechanisms.
 *
 * Stage 1 wraps all of them rather than converting any, so that the single entry point can land
 * with no behaviour change at all: the stack consults these in exactly the order
 * `workspaceRoot.handleUndo` used to. Each one disappears as its mechanism is converted to push
 * real {@link IUndoEntry}s, and the last one to go takes this interface with it.
 */
export interface ILegacyUndoProvider {
    /** Identifies the provider in logs and test failures, e.g. "origami". */
    name: string;

    /**
     * Whether this mechanism has something to undo *right now*.
     *
     * Must be cheap and synchronous: C# polls the aggregate `canUndo` on a timer to decide
     * whether the Undo button is enabled, so anything that walks a stack or forces layout here
     * makes the button flicker.
     */
    canUndo(): boolean;

    /** Undo one step. Only called when `canUndo()` has just returned true. */
    undo(): void;
}

/**
 * How many entries the stack keeps.
 *
 * Bounded by count rather than bytes: the worst case is ~50 page-HTML strings, which is
 * single-digit MB. Revisit only if something proves byte accounting is needed.
 */
export const kMaxUndoEntries = 50;
