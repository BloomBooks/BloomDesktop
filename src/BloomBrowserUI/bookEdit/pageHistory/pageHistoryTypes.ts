// The page history: Bloom's one undo history for the page that is open in the Edit tab.
//
// The design is docs/undo/APPROACHES.md: recorded DOM mutations (section 4.4), the rules of section
// 3.0.2, and the API of sections 6.1 and 11.8.1. docs/undo/MINIMUM.md says what the first version
// builds. A MutationObserver on the page records every change; a transaction groups the changes of
// one action; undo puts back the same node objects with their earlier attributes, text and
// children, after checking that the page still has the values that the transaction left
// (rule 4). Nothing here touches C#, files or the keyboard.

/** The l10n key of the description that a transaction gets when no code has given it one. */
export const kGenericUndoDescriptionL10nKey = "EditTab.Undo.Edit";

/**
 * One transaction, as doTransaction and addToLatestTransaction pass it to their function and
 * beginTransaction returns it. Section 11.8.1 adds `addChangeOutsidePage` here, for state that the
 * observer cannot see; the first version has none (MINIMUM.md, "Left out of the minimum").
 */
export interface Transaction {
    readonly descriptionL10nKey: string;
}

/** What beginTransaction returns: only the code that holds it can end or cancel it. */
export interface BegunTransaction extends Transaction {
    /**
     * Close the transaction. If nothing changed, no undo step is made. Does nothing if the
     * transaction has already ended, including when clearUndoAndRedo() ended it.
     */
    end(): void;
    /**
     * Put back every change made since beginTransaction, and make no undo step. Does nothing if
     * the transaction has already ended.
     */
    cancel(): void;
}

/** What onAfterUndoOrRedo passes its listeners. */
export interface RestoredContent {
    /** The elements whose attributes, text or children were put back. */
    roots: Element[];
    direction: "undo" | "redo";
}

/**
 * What the history leaves out. Nodes and attributes that it ignores are never recorded and never
 * put back (rule 6). The real filter, shared with the save path's cleanup, comes in a later step;
 * until then a caller may pass none.
 */
export interface PageHistoryFilter {
    /** True for a node that the history ignores, together with everything inside it. */
    ignoreNode(node: Node): boolean;
    /** True for an attribute of a node that is not ignored, whose changes the history ignores. */
    ignoreAttribute(element: Element, attributeName: string): boolean;
}

export interface PageHistoryOptions {
    /** The element whose subtree the history observes: the page's `.bloom-page`. */
    root: Element;
    filter?: PageHistoryFilter;
    /** How many undo steps to keep; the oldest go first. Default 50, as the one stack today. */
    maxUndoSteps?: number;
    /**
     * Called when a change made outside any transaction, and not inside addToLatestTransaction or
     * doOutsideHistory, joins the latest transaction (rule 1). The text names the node and
     * attribute, or the child list, that changed. Default: console.warn in development builds,
     * nothing otherwise.
     */
    onUnmarkedChange?: (whatChanged: string) => void;
}

/** One per page load, in the page frame. */
export interface PageHistory {
    /**
     * Run fn as one undoable transaction. An async fn keeps the transaction open until it
     * settles, and every change made meanwhile joins it, the user's typing included (rule 7).
     * If fn throws, the changes made since this call are put back at once and the error is
     * rethrown (rule 9). Called while another transaction is open, it nests inside it: it makes
     * no undo step of its own, and a failure puts back only the changes since it began
     * (APPROACHES 11.8.1, "Transactions inside transactions").
     */
    doTransaction<T>(
        descriptionL10nKey: string,
        fn: (transaction: Transaction) => T | Promise<T>,
    ): Promise<T>;

    /**
     * For a drag, a slider or a dialog that previews live. The caller keeps the returned object
     * and later calls end() or cancel(). Nests inside an open transaction, as doTransaction does.
     */
    beginTransaction(descriptionL10nKey: string): BegunTransaction;

    /**
     * Bloom's own change, joined to the latest transaction, where an unmarked change made outside
     * any transaction goes anyway (rule 1), but without the warning. fn gets that transaction,
     * available only inside fn; with no latest transaction, fn still runs, gets a transaction
     * that records nothing, and its changes become part of the starting state.
     */
    addToLatestTransaction<T>(fn: (transaction: Transaction) => T): T;

    /**
     * Bloom's own change, not recorded: undo leaves it in place. Only for a value that Bloom
     * recomputes from other content (rule 3).
     */
    doOutsideHistory<T>(fn: () => T): T;

    /**
     * A boundary: drop every undo and redo step. A transaction open at the time ends with them:
     * its changes stay on the page and belong to no transaction, and its end() and cancel() do
     * nothing.
     */
    clearUndoAndRedo(): void;

    /**
     * Called at the start of every undo and redo, before the check. A component ends or abandons
     * work that would write to the page later. A change that a listener makes joins the latest
     * transaction (rule 1) before the check runs. Returns an unsubscribe.
     */
    onBeforeUndoOrRedo(cancelPendingWrites: () => void): () => void;

    /**
     * Called after an undo or redo has put the page back, in the same task. Listeners must
     * re-read synchronously. A listener that writes the page makes a change outside any
     * transaction, which joins the latest transaction and clears redo (rule 8). Returns an
     * unsubscribe.
     */
    onAfterUndoOrRedo(
        listener: (restored: RestoredContent) => void,
    ): () => void;

    /**
     * Undo the latest step. If the page no longer has the values that the step left (rule 4), the
     * undo is refused: that step and every older one are discarded, and redo is kept. While a
     * transaction is open, does nothing.
     */
    undo(): Promise<void>;

    /** Redo the step undone last. A refused redo discards every redo step. */
    redo(): Promise<void>;

    /** The description of the step that undo would undo, or undefined when there is none. */
    readonly undoDescriptionL10nKey: string | undefined;
    /** The description of the step that redo would redo, or undefined when there is none. */
    readonly redoDescriptionL10nKey: string | undefined;
    /** True while a transaction is open, including one that began inside another. */
    readonly hasOpenTransaction: boolean;

    /**
     * Stop observing, for the page unload. An open transaction ends; every step is dropped. Any
     * later call throws.
     */
    dispose(): void;
}
