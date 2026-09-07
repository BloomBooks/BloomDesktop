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
     * Non-empty means a push is held until the outermost scope closes. See {@link push}.
     */
    private openScopeLabels: string[] = [];

    /**
     * Entries pushed while a scope was open, with the scope depth each arrived at, in order. One
     * of them is recorded when the outermost scope closes; see {@link endUndoableScope}.
     */
    private heldPushes: { entry: IUndoEntry; depth: number }[] = [];

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
     * If a `runUndoable` scope is open the entry is not recorded yet but *held*: one user gesture
     * must produce exactly one entry, however many layers of code it passes through, and which of
     * the held entries that is can only be decided once the whole gesture has run. See
     * {@link endUndoableScope} for the rule, and PLAN.md 4.13.
     */
    public push(entry: IUndoEntry): void {
        if (this.openScopeLabels.length > 0) {
            this.heldPushes.push({ entry, depth: this.openScopeLabels.length });
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
        // If the entry fails to undo, it becomes the next thing to undo again (so the user can
        // retry, or see that it is stuck), instead of being silently skipped and offered as a Redo
        // of something that never happened.
        return this.apply(
            () => {
                entry.prepareRedo?.();
                return entry.undo();
            },
            () => this.makeNextToUndo(entry),
        );
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
        // As in undo(): a redo that fails is still the next Redo rather than being treated as done.
        return this.apply(
            () => entry.redo!(),
            () => this.makeNextToRedo(entry),
        );
    }

    /**
     * After a failed undo, point the index back at `entry` — by identity, not by the number it had
     * before. An asynchronous undo can be in flight while the page changes, and `keepOnly` may have
     * dropped entries (including this one) and renumbered the rest meanwhile; restoring the old
     * number would then point past the end, and `canUndo` would advertise an entry that is not
     * there. If the entry is gone, the index `keepOnly` computed is already right.
     */
    private makeNextToUndo(entry: IUndoEntry): void {
        const i = this.entries.indexOf(entry);
        if (i >= 0) {
            this.currentIndex = i;
        }
    }

    /** The redo counterpart of {@link makeNextToUndo}. */
    private makeNextToRedo(entry: IUndoEntry): void {
        const i = this.entries.indexOf(entry);
        if (i >= 0) {
            this.currentIndex = i - 1;
        }
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
     * Called whenever the page frame is about to navigate (see pageFrameUndoHooks.ts). It exists
     * for the reloads that keep the *same* page — leaving origami layout mode, importing a video,
     * changing the topic — where `setCurrentPageId` would see no change, but the captured state is
     * just as stale: the elements it describes have been rebuilt.
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
            this.heldPushes = [];
        }
        this.openScopeLabels.push(label);
    }

    /**
     * Close the innermost `runUndoable` scope. Closing the *outermost* one records exactly one of
     * the entries pushed while it was open, labelled with the outermost scope's label:
     *
     * - the first entry the outermost operation pushed **itself** (at depth 1), if it pushed one —
     *   that entry describes the whole gesture, which is what a single Ctrl+Z must reverse; or
     * - failing that, the first entry pushed by anything nested inside it, since a scope that
     *   records nothing of its own is just a wrapper saying "these inner steps are one gesture".
     *
     * "First push wins" alone would be wrong: an inner layer usually runs, and pushes, *before* the
     * outer operation gets to record its own entry, and keeping the inner one would leave an undo
     * that reverses only part of the gesture (an image reverting to a placeholder, say, but not the
     * canvas element coming back). The corollary is a discipline for inner layers: an operation
     * that records its own undo does so inside its own `runUndoable`, so that its push sits at
     * depth 2 or more when it happens inside a larger gesture. See PLAN.md 4.13.
     */
    public endUndoableScope(): void {
        const label = this.openScopeLabels[0];
        this.openScopeLabels.pop();
        if (this.openScopeLabels.length > 0 || this.heldPushes.length === 0) {
            return;
        }
        const chosen =
            this.heldPushes.find((held) => held.depth === 1) ??
            this.heldPushes[0];
        this.heldPushes = [];
        chosen.entry.label = label;
        this.record(chosen.entry);
    }

    /** Whether a `runUndoable` scope is currently open. */
    public isInUndoableScope(): boolean {
        return this.openScopeLabels.length > 0;
    }

    /**
     * Run an entry's undo/redo, holding the re-entrancy guard until it finishes, and calling
     * `onFailure` (after releasing the guard) if it throws or rejects. The failure itself is still
     * propagated to the caller.
     */
    private apply(
        action: () => void | Promise<void>,
        onFailure: () => void,
    ): void | Promise<void> {
        this.applying = true;
        let result: void | Promise<void>;
        try {
            result = action();
        } catch (e) {
            this.applying = false;
            onFailure();
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
                onFailure();
                throw e;
            },
        );
    }

    /**
     * Filter entries, keeping `currentIndex` pointing at the same entry it did before.
     *
     * Pushes held by an open `runUndoable` scope are filtered too: an asynchronous gesture can be
     * awaiting while the page changes, and without this its held entry, scoped to the page just
     * left, would be recorded when the scope closes and later undone against the new page.
     */
    private keepOnly(predicate: (entry: IUndoEntry) => boolean): void {
        this.heldPushes = this.heldPushes.filter((held) =>
            predicate(held.entry),
        );
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
