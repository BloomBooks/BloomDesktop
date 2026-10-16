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
     *
     * An outermost transaction with kGenericUndoDescriptionL10nKey, the stand-in that Bloom opens
     * on a real click or key press (rule 2), may be ended while transactions begun inside it are
     * still open, such as a dialog's: it then stays open, and closes, making its step, when the
     * last of them ends or is cancelled, as if end() were called then. (An async doTransaction
     * waits the same way; see doTransaction.) Ending any other transaction while one begun inside
     * it is open is a bug in the caller: the inner ones end with it, and in development it
     * throws.
     *
     * Returns true when this call made an undo step, false otherwise: nothing changed, an
     * outermost transaction is still open around this one, the stand-in waits for transactions
     * begun inside it, or the transaction had already ended. Only the call that closes the
     * outermost transaction can make a step, and that is the stand-in's own end() or the end() of
     * the last transaction that it waited for. onClose says what happened in every case.
     */
    end(): boolean;
    /**
     * Put back every change made since beginTransaction, and make no undo step. Does nothing if
     * the transaction has already ended, or if it is the stand-in and its end() has been called.
     */
    cancel(): void;
    /**
     * Call listener once, when the outermost transaction that this one is part of closes, with
     * the description of the undo step that closing it made, or undefined when it made none:
     * nothing changed, it was cancelled, or clearUndoAndRedo() or dispose() ended it. If it has
     * closed already, calls listener at once. For the stand-in that waits for transactions begun
     * inside it, this is the one way to learn what its end() led to.
     */
    onClose(
        listener: (stepDescriptionL10nKey: string | undefined) => void,
    ): void;
}

/** What onAfterUndoOrRedo passes its listeners. */
export interface RestoredContent {
    /** The elements whose attributes, text or children were put back. */
    roots: Element[];
    direction: "undo" | "redo";
}

/**
 * What the history leaves out (rule 6): markup that exists only while editing and that a save
 * strips. The history records, compares and puts back only the part of each value that a save
 * keeps, so a change made only to the rest is no change at all, and putting a value back leaves
 * the rest as it is on the page. editTimeMarkup.ts makes the real one; a caller may pass none, and
 * then the history keeps everything.
 */
export interface PageHistoryFilter {
    /** True for a node that the history ignores, together with everything inside it. */
    ignoreNode(node: Node): boolean;

    /**
     * True for a child that the history leaves out of parent's child lists, as it does an ignored
     * node, while it still records, compares and puts back everything inside the child: its
     * insertion into parent and its removal are no change, and putting back parent's child list
     * leaves it where it is. The history asks this only of a child of parent, so the answer may
     * depend on parent. A subtree out of the page that the check of rule 4 compares as text keeps
     * such a child, with what is inside it.
     */
    isLeftOutOfChildList(child: Node, parent: Node): boolean;

    /**
     * True for a child that the history leaves out of parent's child list when it is the only
     * child left in that list once the other rules have left theirs out. The history asks this of
     * a list as a whole (the children now, the children at the start of a batch, the children of
     * a subtree out of the page), never of the child where it happens to be. Putting back an
     * empty child list leaves such a child where it is.
     */
    isLeftOutWhenOnlyChild(child: Node, parent: Node): boolean;

    /**
     * True for a text node that the history leaves out of child lists while it has this value,
     * as it does an ignored node: its insertion and removal are no change, and putting back a
     * child list leaves it where it is. Its text is still recorded and put back, and whether it
     * is left out must follow from its identity and its text alone, so that putting back its text
     * puts back whether it is in the child lists. `when` is as for keptText.
     */
    isIgnoredText(
        node: CharacterData,
        value: string,
        when: "now" | "earlier",
    ): boolean;

    /**
     * The part of an attribute's value that the history records and compares, or undefined when
     * there is none: the attribute is absent, or a save would drop all of it. Two values that
     * differ only in what this leaves out must give the same result.
     */
    keptAttributeValue(
        element: Element,
        attributeName: string,
        value: string | undefined,
    ): string | undefined;

    /**
     * The value to give an attribute when undo or redo puts back the kept part `kept`, while the
     * attribute's value now is `current`: kept, with the part of current that keptAttributeValue
     * leaves out. Undefined removes the attribute. keptAttributeValue of the result must be kept.
     */
    valueToPutBack(
        element: Element,
        attributeName: string,
        kept: string | undefined,
        current: string | undefined,
    ): string | undefined;

    /**
     * The part of a text node's value that the history records and compares. `when` says whether
     * value is the node's data now, or the node's data at the start of the batch of records most
     * recently passed to noteRecords.
     */
    keptText(
        node: CharacterData,
        value: string,
        when: "now" | "earlier",
    ): string;

    /**
     * The data to write into a text node when undo, redo or a rollback puts back the kept text
     * `kept`. Called just before the history writes it, so a filter may first bring state
     * outside the DOM in line with the write.
     */
    textToPutBack(node: CharacterData, kept: string): string;

    /**
     * Called once with no records when the history starts observing root, and then with every
     * batch of records the history takes, before it reads any value from them. A filter whose
     * rules depend on state outside the DOM notes that state here.
     */
    noteRecords(root: Element, records: MutationRecord[]): void;
}

export interface PageHistoryOptions {
    /** The element whose subtree the history observes: the page's `.bloom-page`. */
    root: Element;
    filter?: PageHistoryFilter;
    /** How many undo steps to keep; the oldest go first. Default 50, as the one stack today. */
    maxUndoSteps?: number;
    /**
     * False for a history that starts before the page is ready, while Bloom is still setting the
     * page up: until markPageReady() is called, a change made outside any transaction is not
     * recorded, as if made inside doOutsideHistory. Default true.
     */
    startsReady?: boolean;
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
     * (APPROACHES 11.8.1, "Transactions inside transactions"). Its description is not used, with
     * one exception: when the outermost transaction has kGenericUndoDescriptionL10nKey, the first
     * nested transaction with a different description gives the step that description, unless
     * it is cancelled or fails.
     *
     * Two async calls may overlap in time, when the second begins while the first awaits and
     * the first settles before the second: the second nests in the first, the first settles
     * without error, and the step is made when the last of them ends. If the second then fails,
     * every change since it began is put back, including what the first wrote after the second
     * began, since the history cannot tell which of two interleaved async functions made a change.
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
     *
     * The latest transaction is the open one, with one exception: when the only transaction open
     * is the stand-in with kGenericUndoDescriptionL10nKey and nothing has changed in it yet, fn's
     * changes join the latest step, as if no transaction were open, and the stand-in still makes
     * no step if nothing else changes in it. So Bloom's delayed reaction to an earlier action,
     * such as the toolbox's markup pass after typing, never makes a step of its own, nor ends the
     * history when the stand-in is one that a press in another window opened. Limits:
     * - the choice is made when addToLatestTransaction is called, so a reaction that runs in two
     *   calls with an await between them may have its two parts in different steps;
     * - when the stand-in has changed something already, fn's changes join the stand-in, and
     *   undoing its step undoes the reaction too;
     * - a reaction that does not use addToLatestTransaction joins whatever transaction is open;
     * - if the stand-in is later cancelled, reversing its records can overwrite what fn wrote to
     *   the same values; the stand-ins that pageHistoryInput.ts opens are never cancelled.
     */
    addToLatestTransaction<T>(fn: (transaction: Transaction) => T): T;

    /**
     * Run fn without recording what it changes: its changes make no undo step, join none, give no
     * warning and keep redo, and undo leaves them in place. Use it for a value that Bloom
     * recomputes from other content (rule 3), and for a change that is deliberately not undoable
     * but must not end the history. If a later undo or redo needs a value that such a change
     * altered, the check of rule 4 refuses it: that stack of steps is emptied and the page is left
     * as it is.
     */
    doOutsideHistory<T>(fn: () => T): T;

    /**
     * Say that Bloom has finished setting the page up, for a history made with startsReady false.
     * Before this, a change made outside any transaction is not recorded: it makes no step, joins
     * none, gives no warning and keeps redo, as inside doOutsideHistory, so Bloom's setup is part
     * of the page as the user found it. Transactions record as usual from the start, so the user's
     * actions are undoable at once. A setup change to a value that a step holds makes that step's
     * undo refuse (rule 4). After this, rule 1 applies to every change outside a transaction.
     * Does nothing more if the history is already ready.
     */
    markPageReady(): void;

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
     * Called after an undo or redo has put the page back and restored the selection, in the same
     * task, so listeners see the restored caret and focus. Listeners must re-read synchronously.
     * Writes that the restoring of the selection itself causes (a focusin handler, for instance)
     * are undo's own and are not recorded. A listener that writes the page makes a change outside any
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
    /** True once dispose() has run; every other call then throws. */
    readonly isDisposed: boolean;

    /**
     * Stop observing, for the page unload. An open transaction ends; every step is dropped. Any
     * later call throws.
     */
    dispose(): void;
}
