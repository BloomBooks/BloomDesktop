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
 * The stack lives in the page frame and dies with it, so an entry only ever has to work on the
 * page as it is currently loaded, and it may hold references to that page's elements. Two rules
 * for writing one (PLAN.md 4.1):
 *
 * - **Check before undoing.** Before reversing its change, an entry should check that what it
 *   changed is still the way it left it, for instance that an element's HTML still matches what
 *   the change produced. Something the stack never recorded may have changed it since. If it has
 *   changed, the entry should throw rather than apply: the stack then discards itself, so the
 *   user loses undo rather than having the page damaged.
 * - **Prefer data that would survive a reload.** Where it costs little, capture state as data
 *   (HTML strings, structural positions) and find the target again inside `undo()`, rather than
 *   holding elements, ranges or closures over page objects. Then letting undo survive a same-page
 *   reload later would not mean rewriting the entry. This is a preference, not a rule: where
 *   holding a reference is clearly simpler, do so, and say so in a comment where the entry is
 *   built, so the cost of changing course stays visible.
 */
export interface IUndoEntry {
    /** Human-readable, e.g. "Delete canvas element". For tooltips and logging, not identity. */
    label: string;

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
     * Whether this entry can be undone or redone just now. Optional; an entry without it always
     * can. When the entry the next Undo (or Redo) would apply says no, the stack reports nothing to
     * undo (or redo) rather than skipping past it: entries depend on the state the ones above them
     * left. For an entry that belongs to a mode, such as a table operation, which must not be
     * undone in Change Layout mode, where the table is faded and cannot be edited.
     */
    isAvailable?(): boolean;

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

    /**
     * Undo one step, and say whether anything was undone. Only called when `canUndo()` has just
     * returned true, but CKEditor can say it has something to undo when it has not (see
     * editablePage.ckeditorUndo); answering false lets the stack go on to the next provider and
     * then to its own entries, so the person's Undo is not swallowed.
     */
    undo(): boolean;

    /**
     * When this mechanism last recorded a change, on the sequence in changeOrder.ts. Optional:
     * only CKEditor reports it. A provider that does stands aside while the stack's newest entry
     * is more recent than its last change, so that "add a table row, then type" undoes the typing
     * first and "type, then add a row" the row first.
     */
    lastChangeOrder?(): number;
}

/**
 * How many entries the stack keeps.
 *
 * Bounded by count rather than bytes: the worst case is ~50 page-HTML strings, which is
 * single-digit MB. Revisit only if something proves byte accounting is needed.
 */
export const kMaxUndoEntries = 50;
