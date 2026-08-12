// The one undo stack (BL-6681, PLAN.md 4.1 / 4.2).
//
// Lives in the workspace frame, because the page iframe is destroyed on every page change and
// reload while the workspace frame is not. Deliberately free of DOM and jQuery dependencies so it
// can be unit-tested directly; everything frame-specific lives in legacyUndoProviders.ts or in the
// factories that build entries.

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
     * Non-empty means a push should be folded into the outermost scope rather than added.
     */
    private openScopeLabels: string[] = [];

    /** Whether the outermost open scope has already claimed an entry. See {@link push}. */
    private pushedInOutermostScope = false;

    /** True while an undo or redo is being applied, to stop a re-entrant one interleaving. */
    private applying = false;

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
     * If a `runUndoable` scope is open this does *not* add a second entry — one user gesture must
     * produce exactly one entry, however many layers of code it passes through. The outermost
     * scope wins: the first push inside it is kept and relabelled with the scope's label, and
     * later pushes within the same scope are ignored. See PLAN.md 4.13.
     */
    public push(entry: IUndoEntry): void {
        if (this.openScopeLabels.length > 0) {
            if (this.pushedInOutermostScope) {
                // A nested operation recording its own undo. Deliberately dropped: undoing the
                // outermost operation already covers it, and keeping both would make the first
                // Ctrl+Z half-undo the gesture.
                return;
            }
            this.pushedInOutermostScope = true;
            entry.label = this.openScopeLabels[0];
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

    /** Whether anything can be redone. O(1); false at a redo floor (an entry with no `redo`). */
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
        entry.prepareRedo?.();
        return this.apply(() => entry.undo());
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
        this.keepOnly((e) => e.pageId === undefined || e.pageId === pageId);
    }

    /** The page id entries are currently being recorded against. */
    public getCurrentPageId(): string | undefined {
        return this.currentPageId;
    }

    /**
     * Discard every page-scoped entry, keeping the ones that survive a page change.
     *
     * Called when the page frame reloads *without* the page changing — ctrl+wheel zoom and leaving
     * origami layout mode both do that. The page id is the same, so `setCurrentPageId` would not
     * notice, but the captured state is just as stale: the elements it describes have been rebuilt.
     */
    public clearPageScopedEntries(): void {
        this.keepOnly((e) => e.pageId === undefined);
    }

    /** Discard everything. Used when leaving the edit tab, and by tests. */
    public clear(): void {
        this.entries = [];
        this.currentIndex = -1;
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
            this.pushedInOutermostScope = false;
        }
        this.openScopeLabels.push(label);
    }

    /** Close the innermost `runUndoable` scope. */
    public endUndoableScope(): void {
        this.openScopeLabels.pop();
    }

    /** Whether a `runUndoable` scope is currently open. */
    public isInUndoableScope(): boolean {
        return this.openScopeLabels.length > 0;
    }

    /** Run an entry's undo/redo, holding the re-entrancy guard until it finishes. */
    private apply(action: () => void | Promise<void>): void | Promise<void> {
        this.applying = true;
        let result: void | Promise<void>;
        try {
            result = action();
        } catch (e) {
            this.applying = false;
            throw e;
        }
        if (!result) {
            this.applying = false;
            return;
        }
        return result.finally(() => {
            this.applying = false;
        });
    }

    /** Filter entries, keeping `currentIndex` pointing at the same entry it did before. */
    private keepOnly(predicate: (entry: IUndoEntry) => boolean): void {
        const kept: IUndoEntry[] = [];
        let newIndex = -1;
        for (let i = 0; i < this.entries.length; i++) {
            if (!predicate(this.entries[i])) {
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
