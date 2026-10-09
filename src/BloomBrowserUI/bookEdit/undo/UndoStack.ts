// The one undo stack (BL-6681, PLAN.md 4.1 / 4.2).
//
// Lives in the page frame and dies with it: undo covers changes to the page as it is currently
// loaded, and every page change or same-page reload starts with an empty stack (PLAN.md 4.2).
// Deliberately free of DOM and jQuery dependencies so it can be unit-tested directly; everything
// frame-specific lives in legacyUndoProviders.ts, pageUndo.ts, or the factories that build entries.

import { nextChangeOrder } from "./changeOrder";
import { makeCompoundUndoEntry } from "./compoundUndoEntry";
import { ILegacyUndoProvider, IUndoEntry, kMaxUndoEntries } from "./undoTypes";

/**
 * An index-based undo/redo stack, plus the arbitration between it and Bloom's pre-existing undo
 * mechanisms.
 *
 * Index-based rather than pop-based because Redo is in scope: `undo()` steps the index back,
 * `redo()` steps it forward, and any new push truncates everything above the index — so typing
 * after an undo discards the redo branch, which is what every editor does.
 */
export class UndoStack {
    private entries: IUndoEntry[] = [];

    /**
     * When each entry in `entries` was recorded, on the sequence in changeOrder.ts, so that a legacy
     * provider that reports its own changes on it can tell whether it or our newest entry came last.
     */
    private entryOrders: number[] = [];

    /**
     * Index of the entry that the *next* undo would apply; -1 when there is nothing to undo.
     * Entries above it are the redo branch.
     */
    private currentIndex = -1;

    /** Consulted before our own entries, in registration order. See {@link canUndo}. */
    private legacyProviders: ILegacyUndoProvider[] = [];

    /**
     * Labels of the `runUndoable` scopes currently open, outermost first.
     * Non-empty means a push is held until the outermost scope closes. See {@link push}.
     */
    private openScopeLabels: string[] = [];

    /**
     * Entries pushed while a scope was open, in order. They become one entry when the outermost
     * scope closes; see {@link endUndoableScope}.
     */
    private heldPushes: IUndoEntry[] = [];

    /** True while an undo or redo is being applied, to stop a re-entrant one interleaving. */
    private applying = false;

    /**
     * Counts the times the stack has been cleared, and the count when the outermost open scope
     * began. An asynchronous gesture can still be running when the stack is cleared (an undo
     * failed meanwhile); comparing the two tells its scope, when it closes, not to record anything.
     */
    private resetGeneration = 0;
    private scopeResetGeneration = 0;

    /**
     * Add an adapter for one of the pre-existing undo mechanisms.
     *
     * Order matters and is the caller's responsibility: providers are consulted in the order
     * registered, which must reproduce the order the Undo button used before this class existed.
     */
    public registerLegacyProvider(provider: ILegacyUndoProvider): void {
        this.legacyProviders.push(provider);
    }

    /** Drop all legacy providers. For tests; also what Stage 5's deletions leave behind. */
    public clearLegacyProviders(): void {
        this.legacyProviders = [];
    }

    /**
     * Record an undoable step.
     *
     * If a `runUndoable` scope is open the entry is not recorded yet but *held*: one user gesture
     * must produce exactly one entry, however many layers of code it passes through, so the held
     * entries become parts of a single entry once the whole gesture has run. See
     * {@link endUndoableScope} for the rule, and PLAN.md 4.13.
     */
    public push(entry: IUndoEntry): void {
        if (this.openScopeLabels.length > 0) {
            this.heldPushes.push(entry);
            return;
        }
        this.record(entry);
    }

    /** Actually add an entry, truncating any redo branch. */
    private record(entry: IUndoEntry): void {
        // Anything the user had undone is now unreachable: they have taken a different branch.
        this.entries.length = this.currentIndex + 1;
        this.entryOrders.length = this.currentIndex + 1;

        this.entries.push(entry);
        this.entryOrders.push(nextChangeOrder());
        if (this.entries.length > kMaxUndoEntries) {
            this.entries.shift();
            this.entryOrders.shift();
        }
        this.currentIndex = this.entries.length - 1;
    }

    /**
     * Whether anything can be undone.
     *
     * Cheap and synchronous by contract: C# polls this on a timer to set the Undo button's enabled
     * state (`WebView2Browser.UpdateEditButtonsAsync`), so it must not walk entries or touch
     * layout.
     *
     * Legacy providers are consulted before our own entries; see the note on {@link undo} for the
     * exceptions and what the ordering does and does not guarantee.
     */
    public canUndo(): boolean {
        return (
            this.legacyProviders.some((p) => this.providerHasTheNextUndo(p)) ||
            this.ownEntryCanUndo()
        );
    }

    /**
     * Whether a legacy provider should answer the next Undo: it has something to undo, and, if it
     * reports when it last changed, that was not before our newest entry (see changeOrder.ts).
     */
    private providerHasTheNextUndo(provider: ILegacyUndoProvider): boolean {
        if (!provider.canUndo()) {
            return false;
        }
        if (!provider.lastChangeOrder || this.currentIndex < 0) {
            return true;
        }
        return this.entryOrders[this.currentIndex] < provider.lastChangeOrder();
    }

    /** Whether our own newest entry can be undone just now. */
    private ownEntryCanUndo(): boolean {
        const entry = this.entries[this.currentIndex];
        return !!entry && (entry.isAvailable?.() ?? true);
    }

    /**
     * Whether anything can be redone. O(1); false at a redo floor (an entry with no `redo`).
     *
     * Unlike {@link canUndo}, this does not ask the legacy providers, because none of them redoes
     * through the stack. Each mechanism that has a redo (origami, the reader tools, CKEditor)
     * handles Ctrl+Y in its own key handler, and there is no Redo button. The only caller is the
     * last-resort Ctrl+Y binding (redoKeyBinding.ts), which must act only for entries on this
     * stack. Saying yes on a legacy mechanism's behalf would make that binding swallow the
     * keystroke and call {@link redo}, which has nothing of its own to redo.
     */
    public canRedo(): boolean {
        const next = this.entries[this.currentIndex + 1];
        return !!next?.redo && (next.isAvailable?.() ?? true);
    }

    /**
     * Undo one step.
     *
     * Order: each legacy provider that has something to undo, in registration order, then our own
     * entries, except that CKEditor, which reports when it last changed, stands aside while our
     * newest entry is more recent (changeOrder.ts). A provider whose undo finds nothing to undo
     * passes the turn on. An own entry that is not available just now (IUndoEntry.isAvailable)
     * means nothing is undone.
     *
     * The other providers have no such ordering: if a user does an operation recorded here and
     * then one still handled by one of them, that one is undone first, which happens to be right,
     * but in the other order it is wrong. They are each confined to a context (Change Layout mode,
     * an active reader tool, a selected picture), so it rarely arises, and it stops being possible
     * as each is converted.
     */
    public undo(): void | Promise<void> {
        if (this.applying) {
            return;
        }
        for (const provider of this.legacyProviders) {
            // A provider that turns out to have nothing to undo passes the turn on, so the
            // person's Undo still reaches whatever is next.
            if (this.providerHasTheNextUndo(provider) && provider.undo()) {
                return;
            }
        }
        if (!this.ownEntryCanUndo()) {
            return;
        }
        const entry = this.entries[this.currentIndex];
        this.currentIndex--;
        return this.apply(() => {
            entry.prepareRedo?.();
            return entry.undo();
        });
    }

    /**
     * Redo the step that was last undone.
     *
     * Legacy providers take no part: the only pre-existing Redo is origami's, which keeps using
     * its own Ctrl+Y handler until it is converted.
     */
    public redo(): void | Promise<void> {
        if (this.applying || !this.canRedo()) {
            return;
        }
        const entry = this.entries[this.currentIndex + 1];
        this.currentIndex++;
        return this.apply(() => entry.redo!());
    }

    /**
     * Discard everything. Used after an undo or redo fails (see {@link apply}), and by tests.
     *
     * Including pushes held by a scope that is still open across an `await`: when that scope
     * closes it must not resurrect an entry this call discarded — whether it pushed before the
     * clear or after — so the reset generation moves on here and {@link endUndoableScope} drops
     * everything the scope holds.
     */
    public clear(): void {
        this.entries = [];
        this.entryOrders = [];
        this.currentIndex = -1;
        this.heldPushes = [];
        this.resetGeneration++;
    }

    /** How many entries are held. Tests and diagnostics only — not part of the undo contract. */
    public getEntryCount(): number {
        return this.entries.length;
    }

    /** The label of the entry the next undo would apply, or undefined. For tooltips and tests. */
    public peekUndoLabel(): string | undefined {
        return this.entries[this.currentIndex]?.label;
    }

    /** The label of the entry the next redo would apply, or undefined. */
    public peekRedoLabel(): string | undefined {
        return this.entries[this.currentIndex + 1]?.label;
    }

    /**
     * Open a `runUndoable` scope. Call `endUndoableScope` in a `finally`.
     *
     * Only `runUndoable` should call this; it is public because it lives in another module.
     */
    public beginUndoableScope(label: string): void {
        if (this.openScopeLabels.length === 0) {
            this.heldPushes = [];
            this.scopeResetGeneration = this.resetGeneration;
        }
        this.openScopeLabels.push(label);
    }

    /**
     * Close the innermost `runUndoable` scope. Closing a nested one does nothing more: the
     * outermost scope defines the gesture. Closing the *outermost* one records a single entry,
     * labelled with that scope's label, made of everything pushed while it was open, in order
     * (see makeCompoundUndoEntry): one Ctrl+Z undoes the whole gesture, last part first, and
     * Ctrl+Y redoes it, if every part can redo. A single push is recorded as it is, relabelled.
     *
     * If the stack was cleared while the scope was open, nothing it holds is recorded: the clear
     * meant "forget everything", and a gesture that settles afterwards must not repopulate the
     * stack.
     */
    public endUndoableScope(): void {
        const label = this.openScopeLabels[0];
        this.openScopeLabels.pop();
        if (this.openScopeLabels.length > 0 || this.heldPushes.length === 0) {
            return;
        }
        const parts = this.heldPushes;
        this.heldPushes = [];
        if (this.scopeResetGeneration !== this.resetGeneration) {
            return;
        }
        const entry =
            parts.length === 1 ? parts[0] : makeCompoundUndoEntry(label, parts);
        entry.label = label;
        this.record(entry);
    }

    /** Whether a `runUndoable` scope is currently open. */
    public isInUndoableScope(): boolean {
        return this.openScopeLabels.length > 0;
    }

    /**
     * Run an entry's undo/redo, holding the re-entrancy guard until it finishes.
     *
     * If it throws or rejects, the whole stack is discarded ({@link clear}) and the failure is
     * propagated, so that it reaches Bloom's error reporting. A retry would rarely help: a failure
     * is almost always a bug, which fails the same way again. And what the failure leaves behind is
     * a document in a state no entry recorded, so the entries below it, each of which assumes the
     * state the ones above it left, could no longer be trusted to undo correctly. Losing undo is
     * better than corrupting the page. The legacy mechanisms do not depend on the stack and keep
     * working.
     */
    private apply(action: () => void | Promise<void>): void | Promise<void> {
        this.applying = true;
        let result: void | Promise<void>;
        try {
            result = action();
        } catch (e) {
            this.applying = false;
            this.clear();
            throw e;
        }
        if (!result) {
            this.applying = false;
            return;
        }
        return result.then(
            () => {
                this.applying = false;
            },
            (e) => {
                this.applying = false;
                this.clear();
                throw e;
            },
        );
    }
}

/**
 * The one stack: the instance in the page frame, set up by pageUndo.ts. Other frames load this
 * module too (it shares a chunk), but their copies are never set up or used: the workspace frame
 * reaches undo through the page frame's bundle exports.
 */
export const theOneUndoStack = new UndoStack();
