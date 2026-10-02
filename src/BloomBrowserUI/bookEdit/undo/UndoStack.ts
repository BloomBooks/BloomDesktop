// The one undo stack (BL-6681, PLAN.md 4.1 / 4.2).
//
// Lives in the workspace frame, because the page iframe is destroyed on every page change and
// reload while the workspace frame is not. Deliberately free of DOM and jQuery dependencies so it
// can be unit-tested directly; everything frame-specific lives in legacyUndoProviders.ts or in the
// factories that build entries.

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
     * Index of the entry that the *next* undo would apply; -1 when there is nothing to undo.
     * Entries above it are the redo branch.
     */
    private currentIndex = -1;

    /** Consulted before our own entries, in registration order. See {@link canUndo}. */
    private legacyProviders: ILegacyUndoProvider[] = [];

    /** The page entries are being recorded against. Set by whoever notices page changes. */
    private currentPageId: string | undefined;

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
     * Counts the times the page frame has been replaced (a navigation to another page, or a reload
     * of the same one). A `runUndoable` scope remembers the generation it opened in; if that has
     * moved on by the time it closes, any page-scoped part describes elements that no longer exist,
     * and the whole gesture is dropped. This catches what the page-id check in {@link record} cannot: a reload that
     * keeps the same page id.
     */
    private pageGeneration = 0;

    /**
     * Counts the times the stack has been cleared. Kept apart from {@link pageGeneration} because
     * the two mean different things to a scope that straddled them: a page change invalidates only
     * page-scoped pushes, a clear invalidates everything.
     */
    private resetGeneration = 0;

    /** The values of the two generations when the outermost open scope began. */
    private scopeGeneration = 0;
    private scopeResetGeneration = 0;

    /**
     * Add an adapter for one of the pre-existing undo mechanisms.
     *
     * Order matters and is the caller's responsibility: providers are consulted in the order
     * registered, which must reproduce the order `workspaceRoot.handleUndo` uses today.
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

    /**
     * Actually add an entry, truncating any redo branch.
     *
     * An entry scoped to a page other than the current one is dropped instead. That is the last
     * line of defence for a push that arrives *after* a page change — an asynchronous gesture on
     * the old page finishing late — which `keepOnly` (run at navigation time) could not have seen.
     * Undoing such an entry would apply the old page's data to whatever page is showing now.
     *
     * It cannot tell a reload of the *same* page from no reload at all, so a late push from an
     * asynchronous gesture must come through a `runUndoable` scope, whose generation check
     * ({@link endUndoableScope}) does catch that case. Every asynchronous gesture is expected to be
     * wrapped that way; a bare push is for synchronous work that cannot straddle a navigation.
     */
    private record(entry: IUndoEntry): void {
        if (
            entry.pageId !== undefined &&
            this.currentPageId !== undefined &&
            entry.pageId !== this.currentPageId
        ) {
            return;
        }
        // Anything the user had undone is now unreachable: they have taken a different branch.
        this.entries.length = this.currentIndex + 1;

        this.entries.push(entry);
        if (this.entries.length > kMaxUndoEntries) {
            this.entries.shift();
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
     * Legacy providers are consulted before our own entries, which reproduces today's behaviour
     * exactly. See the note on {@link undo} about what that ordering does and does not guarantee.
     */
    public canUndo(): boolean {
        return (
            this.legacyProviders.some((p) => p.canUndo()) ||
            this.currentIndex >= 0
        );
    }

    /**
     * Whether anything can be redone. O(1); false at a redo floor (an entry with no `redo`).
     *
     * Unlike {@link canUndo}, this does not ask the legacy providers, because none of them redoes
     * through the stack. Each mechanism that has a redo (origami, the reader tools, CKEditor)
     * handles Ctrl+Y in its own key handler, and there is no Redo button. The only caller is the
     * page frame's last-resort Ctrl+Y binding (redoKeyBinding.ts), which must act only for entries
     * on this stack. Saying yes on a legacy mechanism's behalf would make that binding swallow the
     * keystroke and call {@link redo}, which has nothing of its own to redo.
     */
    public canRedo(): boolean {
        const next = this.entries[this.currentIndex + 1];
        return !!next?.redo;
    }

    /**
     * Undo one step.
     *
     * Order: each legacy provider that has something to undo, in registration order, then our own
     * entries. That is exactly what `workspaceRoot.handleUndo` did before this class existed, so
     * adopting the stack changes nothing while the stack is empty.
     *
     * What that ordering does *not* give us is true chronological order across the boundary: if a
     * user does an operation recorded here and then one still handled by a legacy provider, the
     * legacy one is undone first — which happens to be right — but in the other order it is wrong.
     * That was already true between the old mechanisms (they were consulted in a fixed order too),
     * and it stops being possible as each provider is converted. It is not worth inventing
     * cross-mechanism sequencing for a state we are deleting.
     */
    public undo(): void | Promise<void> {
        if (this.applying) {
            return;
        }
        const provider = this.legacyProviders.find((p) => p.canUndo());
        if (provider) {
            provider.undo();
            return;
        }
        if (this.currentIndex < 0) {
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
     * Note which page we are on, discarding entries that belonged to a previous one.
     *
     * Page-scoped entries capture state within a page, so they are meaningless once the user has
     * moved on; entries with no `pageId` (deleting a page) deliberately survive.
     */
    public setCurrentPageId(pageId: string | undefined): void {
        if (pageId === this.currentPageId) {
            return;
        }
        this.currentPageId = pageId;
        this.pageGeneration++;
        this.keepOnly((e) => e.pageId === undefined || e.pageId === pageId);
    }

    /** The page id entries are currently being recorded against. */
    public getCurrentPageId(): string | undefined {
        return this.currentPageId;
    }

    /**
     * Discard every page-scoped entry, keeping the ones that survive a page change.
     *
     * Called whenever the page frame is about to navigate (see pageFrameUndoHooks.ts). It exists
     * for the reloads that keep the *same* page — leaving origami layout mode, importing a video,
     * changing the topic — where `setCurrentPageId` would see no change, but the captured state is
     * just as stale: the elements it describes have been rebuilt.
     */
    public clearPageScopedEntries(): void {
        this.pageGeneration++;
        this.keepOnly((e) => e.pageId === undefined);
    }

    /**
     * Discard everything. Used when leaving the edit tab, after an undo or redo fails (see
     * {@link apply}), and by tests.
     *
     * Including pushes held by a scope that is still open across an `await`: when that scope
     * closes it must not resurrect an entry this call discarded — whether it pushed before the
     * clear or after — so the reset generation moves on here and {@link endUndoableScope} drops
     * everything the scope holds.
     */
    public clear(): void {
        this.entries = [];
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
            this.scopeGeneration = this.pageGeneration;
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
     *
     * If instead the page frame was replaced while the scope was open, and any part is
     * page-scoped, the whole gesture is dropped. An asynchronous gesture that straddled a reload
     * (which the page-id check in {@link record} cannot detect when the page keeps its id) would
     * otherwise record state describing elements that no longer exist; and recording only its
     * page-independent parts would leave an entry that undoes half the gesture. A gesture made
     * only of page-independent parts survives. Deleting a page is the gesture that needs this: it
     * navigates the frame itself, inside its own scope, so its entry must be page-independent, and
     * it must not include a page-scoped part, or the whole undo is lost.
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
        if (
            this.scopeGeneration !== this.pageGeneration &&
            parts.some((part) => part.pageId !== undefined)
        ) {
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
     * is almost always a bug, which fails the same way again, or a page frame mid-reload, whose
     * page-scoped entries are about to be discarded anyway. And what the failure leaves behind is
     * a document in a state no entry recorded, so the entries below it, each of which assumes the
     * state the ones above it left, could no longer be trusted to undo correctly. Losing undo is
     * better than corrupting the page. The legacy mechanisms do not depend on the stack and keep
     * working.
     *
     * An asynchronous undo or redo that fails late, after the user has moved to another page and
     * recorded something there, discards that newer history too. That is accepted rather than
     * guarded against: it needs an operation still in flight across a page change plus a new edit
     * before the failure, and its only cost is lost undo history, never damage to the page.
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

    /**
     * Filter entries, keeping `currentIndex` pointing at the same entry it did before.
     *
     * Pushes held by an open `runUndoable` scope are left alone. They are parts of one gesture, so
     * they stand or fall together, and the scope decides when it closes: every caller of this
     * moves the page generation on, which {@link endUndoableScope} checks.
     *
     * The redo branch (everything above `currentIndex`) is a sequence that must be replayed in
     * order, so dropping one entry from it invalidates everything after the hole: those entries
     * were undone *before* the dropped one and must be redone *after* it. The branch therefore ends
     * at the first entry it loses, rather than keeping a gap that one Ctrl+Y would step over.
     */
    private keepOnly(predicate: (entry: IUndoEntry) => boolean): void {
        const kept: IUndoEntry[] = [];
        let newIndex = -1;
        for (let i = 0; i < this.entries.length; i++) {
            if (!predicate(this.entries[i])) {
                if (i > this.currentIndex) {
                    // The first entry lost from the redo branch: nothing after it can be redone.
                    break;
                }
                continue;
            }
            kept.push(this.entries[i]);
            if (i <= this.currentIndex) {
                newIndex = kept.length - 1;
            }
        }
        this.entries = kept;
        this.currentIndex = newIndex;
    }
}

/**
 * The one stack. A singleton because C# and the other frames reach undo through a single
 * function pair on the workspace bundle, and because "one consistent Undo stack" is the point.
 */
export const theOneUndoStack = new UndoStack();
