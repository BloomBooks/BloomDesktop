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
 * The l10n key of the description of a paste: the top bar's Paste button (pasteClipboard in
 * bloomEditing.ts), Ctrl+V, or the browser's own paste in a text box (pageHistoryInput.ts).
 */
export const kPasteUndoDescriptionL10nKey = "EditTab.PasteButton";

/** The l10n key of the description of a cut: the top bar's Cut button, or Ctrl+X in a text box. */
export const kCutUndoDescriptionL10nKey = "EditTab.CutButton";

/**
 * A step's description made of parts: the l10n id of a template with {0}, {1} and so on in it,
 * and the l10n ids that fill them, in order. For example, { l10nId: "EditTab.Undo.Delete", args:
 * ["EditTab.Undo.Object.Table"] } is "Delete {0}" filled with "Table". Every rule that looks at a
 * description (kGenericUndoDescriptionL10nKey above all) looks at l10nId alone.
 */
export interface UndoDescription {
    readonly l10nId: string;
    readonly args?: readonly string[];
}

/**
 * A description as doTransaction, beginTransaction and describeOpenStep take it: a plain l10n id,
 * or an UndoDescription.
 */
export type UndoDescriptionInput = string | UndoDescription;

/** The UndoDescription of a description given either way. */
export function toUndoDescription(
    description: UndoDescriptionInput,
): UndoDescription {
    return typeof description === "string"
        ? { l10nId: description }
        : description;
}

// The start of the l10n id of most step descriptions, which formatUndoDescription leaves out.
const kUndoDescriptionL10nPrefix = "EditTab.Undo.";
// The start of the l10n id of most things that fill a description's template, also left out.
const kUndoObjectL10nPrefix = "EditTab.Undo.Object.";

/**
 * A description as Bloom's log and the Show Page History dialog show it: the l10n id alone, or
 * with the ids that fill its template in brackets. It stays the same whatever the UI language, so
 * a program can read it. To keep it short, "EditTab.Undo." is left off the front of the id and
 * "EditTab.Undo.Object." off the front of each id in the brackets, so
 * "EditTab.Undo.Delete" filled with "EditTab.Undo.Object.Table" is "Delete(Table)". An id that
 * starts some other way, such as "EditTab.CutButton", is written whole.
 */
export function formatUndoDescription(
    l10nId: string,
    args: readonly string[] | undefined,
): string {
    const name = withoutPrefix(l10nId, kUndoDescriptionL10nPrefix);
    return args && args.length > 0
        ? `${name}(${args.map((arg) => withoutPrefix(arg, kUndoObjectL10nPrefix)).join(",")})`
        : name;
}

/** The id with prefix taken off its front, or the whole id if it does not start with prefix. */
function withoutPrefix(id: string, prefix: string): string {
    return id.startsWith(prefix) ? id.substring(prefix.length) : id;
}

/**
 * One transaction, as doTransaction and addToLatestTransaction pass it to their function and
 * beginTransaction returns it. Section 11.8.1 adds `addChangeOutsidePage` here, for state that the
 * observer cannot see; the first version has none (MINIMUM.md, "Left out of the minimum").
 */
export interface Transaction {
    /** The l10n id of the description, or of its template (UndoDescription). */
    readonly descriptionL10nKey: string;
    /** The l10n ids that fill the description's template, if it has one (UndoDescription). */
    readonly descriptionArgs?: readonly string[];
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
     * the description of the undo step that closing it made (its l10n id, and the ids that fill
     * its template, if any), or undefined when it made none: nothing changed, it was cancelled, or
     * clearUndoAndRedo() or dispose() ended it. If it has closed already, calls listener at once.
     * For the stand-in that waits for transactions begun inside it, this is the one way to learn
     * what its end() led to.
     */
    onClose(
        listener: (
            stepDescriptionL10nKey: string | undefined,
            stepDescriptionArgs?: readonly string[],
        ) => void,
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
    /**
     * True for a node that the history ignores, together with everything inside it. The answer
     * for a node must not change while the node is in the page, except straight after the code
     * that inserts it, in the same task and with no call to the history between: the history
     * skips every record of an ignored node, and nothing records its parent's child list, so a
     * node that an attribute change makes ignored later stays in the child lists of the steps
     * that hold it, and their undo is refused. Bloom gives such an element its class (bloom-ui
     * and the like) before it inserts it, or straight after in the same code.
     */
    ignoreNode(node: Node): boolean;

    /**
     * True for a child that the history leaves out of parent's child lists, as it does an ignored
     * node, while it still records, compares and puts back everything inside the child: its
     * insertion into parent and its removal are no change, and putting back parent's child list
     * leaves it where it is. The history asks this of a child of parent, or of a node that was one
     * at the start of a batch of records and may have left parent since, so the answer may depend
     * on parent but not on where the child is now (its parentNode). As with ignoreNode, the answer
     * for a child must not change while it is a child of parent: nothing records parent's child
     * list when it does, so the child lists of the steps that hold parent no longer match the
     * page, and their undo is refused. A subtree out of the page that the check of rule 4
     * compares as text keeps such a child, with what is inside it.
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
    /**
     * How many undo steps to keep, a whole number of zero or more; the oldest go first. Default
     * 50, as the one stack today.
     */
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
    /**
     * Called with one line for Bloom's log when an undo step is made, undone or redone, when an
     * undo or redo is refused, when the history is cleared, with its reason, when a change that
     * no user action made empties redo, and when cancelling a transaction reverses changes. kind says which. The lines name steps by their
     * description, as formatUndoDescription writes it, and never hold the page's text. Default:
     * nothing.
     */
    log?: (line: string, kind: PageHistoryLogKind) => void;
}

/** What a line that PageHistoryOptions.log gets is about. */
export type PageHistoryLogKind =
    | "step"
    | "undo"
    | "redo"
    // An undo or redo that found the page changed since its step, and dropped that stack.
    | "refused"
    | "cleared"
    // A change made outside any transaction, which no user action made, joined the latest step
    // and so emptied redo (rule 1).
    | "redoDropped"
    // A cancelled transaction reversed the changes made since it began.
    | "rollback";

/**
 * Where the history sent a batch of changes that its observer saw, for PageHistoryDetail.
 * - transaction: into the open transaction; descriptionL10nKey and depth (1 for the outermost)
 *   are those of the innermost transaction open.
 * - addToLatestTransaction: made inside addToLatestTransaction, joining the open transaction or
 *   the latest step, whose description is given; undefined when there is no step, and the
 *   changes become starting state.
 * - unmarked: made outside any transaction and outside every call (rule 1), joining the latest
 *   step, as for addToLatestTransaction, and reported to onUnmarkedChange.
 * - outsideHistory: made inside doOutsideHistory, or by restoring the selection after an undo or
 *   redo; not recorded.
 * - beforeReady: made outside any transaction before markPageReady; not recorded.
 * - undo, redo: the history's own changes, putting back a step.
 * - rollback: the history's own changes, reversing what was changed since a cancelled
 *   transaction began.
 */
export type PageHistoryChangeContext =
    | {
          kind: "transaction";
          descriptionL10nKey: string;
          descriptionArgs?: readonly string[];
          depth: number;
      }
    | {
          kind: "addToLatestTransaction" | "unmarked";
          joins: "openTransaction" | "latestStep";
          descriptionL10nKey: string | undefined;
          descriptionArgs?: readonly string[];
      }
    | { kind: "outsideHistory" }
    | { kind: "beforeReady" }
    | {
          kind: "undo" | "redo" | "rollback";
          descriptionL10nKey: string;
          descriptionArgs?: readonly string[];
      };

/**
 * One thing that the history did, for a developer's tool that shows everything (the Show Page
 * History dialog). depth is 1 for an outermost transaction, 2 for one begun inside it, and so on.
 * - opened: a transaction began.
 * - ended: a transaction ended (end(), or doTransaction's fn settling, or the end of the last
 *   transaction that it waited for).
 * - endWaits: end() of a transaction that waits for innerOpen transactions begun inside it.
 * - closedWithNoStep: the outermost transaction closed with no net change, and made no step.
 * - cancelled: a transaction was cancelled; recordsReversed is how many records it reverses.
 * - changes: a batch of records, in the order the observer gave them, and where they went.
 *   ignored[i] is true when the history's filter ignores the node of records[i], or when
 *   records[i] changes an attribute only in a part that the filter does not keep (an edit-time
 *   class, for instance).
 */
export type PageHistoryDetail =
    | {
          kind: "opened" | "ended";
          descriptionL10nKey: string;
          descriptionArgs?: readonly string[];
          depth: number;
      }
    | {
          kind: "endWaits";
          descriptionL10nKey: string;
          descriptionArgs?: readonly string[];
          depth: number;
          innerOpen: number;
      }
    | {
          kind: "closedWithNoStep";
          descriptionL10nKey: string;
          descriptionArgs?: readonly string[];
      }
    | {
          kind: "cancelled";
          descriptionL10nKey: string;
          descriptionArgs?: readonly string[];
          depth: number;
          recordsReversed: number;
      }
    | {
          kind: "changes";
          records: MutationRecord[];
          ignored: boolean[];
          context: PageHistoryChangeContext;
      };

/**
 * Called synchronously with each PageHistoryDetail, while the history is in the middle of the
 * work that it reports. It must not change the page or call the history, and must not keep the
 * records: it reads what it needs from them, and from the page, before it returns.
 */
export type PageHistoryDetailListener = (detail: PageHistoryDetail) => void;

/** What PageHistory.getStepDescriptions returns. */
export interface PageHistoryStepDescriptions {
    /** The undo steps, oldest first: the last is the one that undo would undo. */
    readonly undo: readonly UndoDescription[];
    /** The redo steps in the order that redo would take them: the first is the one it would redo. */
    readonly redo: readonly UndoDescription[];
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
     * it is cancelled or fails. The description is a plain l10n id or an UndoDescription.
     *
     * Two async calls may overlap in time, when the second begins while the first awaits and
     * the first settles before the second: the second nests in the first, the first settles
     * without error, and the step is made when the last of them ends. If the second then fails,
     * every change since it began is put back, including what the first wrote after the second
     * began, since the history cannot tell which of two interleaved async functions made a change.
     */
    doTransaction<T>(
        description: UndoDescriptionInput,
        fn: (transaction: Transaction) => T | Promise<T>,
    ): Promise<T>;

    /**
     * For a drag, a slider or a dialog that previews live. The caller keeps the returned object
     * and later calls end() or cancel(). Nests inside an open transaction, as doTransaction does.
     */
    beginTransaction(description: UndoDescriptionInput): BegunTransaction;

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
     * nothing. reason says why, for Bloom's log (PageHistoryOptions.log).
     */
    clearUndoAndRedo(reason: string): void;

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
     * undo is refused: that step and every older one are discarded, and redo is kept.
     *
     * Asked for while a transaction is open, such as a picture change that waits for the picture
     * to load, the undo waits, and runs as soon as no transaction is open; undo and redo requests
     * made meanwhile run after it, in the order they were made. It then undoes the step that the
     * transaction made, or, if that made none, the step before. clearUndoAndRedo() and dispose()
     * drop the requests that are waiting. The promise settles when the undo has run or been
     * dropped, so code inside a transaction must not await it.
     */
    undo(): Promise<void>;

    /**
     * Redo the step undone last. A refused redo discards every redo step. While a transaction is
     * open, it waits as undo() does; a transaction that makes a step empties the redo steps, so a
     * redo that waited for it does nothing.
     */
    redo(): Promise<void>;

    /**
     * The l10n id of the description of the step that undo would undo (of its template, for an
     * UndoDescription with args), or undefined when there is none.
     */
    readonly undoDescriptionL10nKey: string | undefined;
    /**
     * The l10n id of the description of the step that redo would redo (of its template, for an
     * UndoDescription with args), or undefined when there is none.
     */
    readonly redoDescriptionL10nKey: string | undefined;
    /**
     * The descriptions of every undo and redo step, for the Show Page History dialog. Unlike
     * undoDescriptionL10nKey, it does not first take in the changes that the observer has not yet
     * delivered, so it reads the steps as they are and changes nothing.
     */
    getStepDescriptions(): PageHistoryStepDescriptions;
    /**
     * The page as the history compares it: every attribute and text as far as the filter keeps
     * it, without the children that the filter leaves out. Two pages with the same kept markup
     * are the same to undo and redo; for tests that check that a redo put the page back.
     */
    keptMarkupOfPage(): string;
    /** True while a transaction is open, including one that began inside another. */
    readonly hasOpenTransaction: boolean;
    /** True once dispose() has run; every other call then throws. */
    readonly isDisposed: boolean;

    /**
     * Report everything the history does to listener, or stop reporting when listener is
     * undefined; for the Show Page History dialog. Reporting changes nothing in what the history
     * does, and with no listener it costs nothing beyond a check.
     */
    setDetailListener(listener: PageHistoryDetailListener | undefined): void;

    /**
     * Stop observing, for the page unload. An open transaction ends; every step is dropped. Any
     * later call throws.
     */
    dispose(): void;
}
