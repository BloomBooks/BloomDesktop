// The page history, implementing PageHistory (pageHistoryTypes.ts) with recorded DOM mutations:
// docs/undo/APPROACHES.md sections 3.0.2, 6.2 and 11.8.1, and docs/undo/MINIMUM.md item 1.
//
// One MutationObserver watches the page. Every entry point first takes the records that are
// pending, so that one batch of records never spans two transactions; the observer's callback
// handles the records of changes made outside any call. A batch goes to the open transaction if
// there is one, or else joins the latest undo step (rule 1). An undo step holds, for each node
// that it changed, the attribute values, text and child list from before and after it, as the
// same node objects, so undo and redo can put back either set after checking that the page still
// has the other (rule 4).

/// <reference types="vite/client" />
import {
    BegunTransaction,
    kGenericUndoDescriptionL10nKey,
    PageHistory,
    PageHistoryFilter,
    PageHistoryOptions,
    RestoredContent,
    Transaction,
} from "./pageHistoryTypes";
import {
    childrenBeforeRecords,
    describeNode,
    putBackChildLists,
    restoreSelection,
    reverseRecord,
    sameNodes,
    saveSelection,
    SavedSelection,
    serializeNode,
} from "./pageHistoryDom";

const kDefaultMaxUndoSteps = 50;

const kObserverOptions: MutationObserverInit = {
    subtree: true,
    childList: true,
    characterData: true,
    characterDataOldValue: true,
    attributes: true,
    attributeOldValue: true,
};

type Values = "before" | "after";

interface AttributeValues {
    // The part of the value that the filter keeps; undefined when the element has no such
    // attribute or the filter keeps none of it. Texts likewise hold the kept part.
    before: string | undefined;
    after: string | undefined;
}

/** What one step, or the open transaction, changed in one node. */
interface NodeChange {
    attributes?: Map<string, AttributeValues>;
    text?: { before: string; after: string };
    // Children that the filter ignores are left out of both lists (filterChildList).
    children?: { before: Node[]; after: Node[] };
}

type ChangeSet = Map<Node, NodeChange>;

/** One undo step: the changes of one outermost transaction, with what rule 1 joined to them. */
interface Step {
    descriptionL10nKey: string;
    changes: ChangeSet;
    // The subtrees that the step's last move (doing it, undoing it or redoing it) took out of the
    // page, each serialized when it was taken out. The observer cannot see a change to a detached
    // node, so before the next undo or redo each must still serialize the same (rule 4).
    detachedAsText: Map<Node, string>;
    selectionBefore?: SavedSelection;
    selectionAfter?: SavedSelection;
}

/**
 * The net effect of one batch of records: for each attribute, text and child list whose value at
 * the end of the batch differs from its value at the start, the value at the start. Attributes
 * and texts are compared and given by the part that the filter keeps.
 */
interface BatchChange {
    attributes?: Map<string, string | undefined>;
    text?: string;
    children?: Node[];
}

type BatchChanges = Map<Node, BatchChange>;

/** The outermost open transaction, and every transaction nested inside it, together. */
interface OpenTransaction {
    descriptionL10nKey: string;
    // The nested transaction that gave the step its description, in place of the general one.
    descriptionGivenBy?: TransactionFrame;
    changes: ChangeSet;
    // Every record since the outermost transaction opened, filtered or not, oldest first, so that
    // a rollback can reverse them back to the point where any of the nested transactions began.
    records: MutationRecord[];
    // For the first characterData record of each text node in each batch, the kept text of its
    // old value. A rollback writes this and not the raw old value, which may hold a character
    // that only the editor's state at that time explains.
    keptOldTexts: Map<MutationRecord, string>;
    selectionBefore?: SavedSelection;
    // The listeners of BegunTransaction.onClose, called once when this closes.
    closeListeners: ((stepDescriptionL10nKey: string | undefined) => void)[];
    // Set when this closes: the description of the undo step that it made, if it made one.
    closedWith?: { stepDescriptionL10nKey: string | undefined };
}

/**
 * One call of doTransaction or beginTransaction, outermost or nested. It is the BegunTransaction
 * that beginTransaction returns.
 */
class TransactionFrame implements BegunTransaction {
    public ended = false;
    // True for a transaction that may wait for transactions begun inside it (endFrame), once its
    // end() has been called, or its async fn has settled, while they were still open: it ends
    // when the last of them ends.
    public endRequested = false;
    // True for a doTransaction whose fn returned a promise. When the promise settles, the
    // transactions begun since are not inside it in any sense that the caller controls.
    public isAsyncDoTransaction = false;

    public constructor(
        private readonly history: PageHistoryImpl,
        public readonly descriptionL10nKey: string,
        // The outermost open transaction that this one is part of.
        public readonly openTransaction: OpenTransaction,
        // How many records the open transaction held when this one began: a rollback of this
        // one reverses the records from this index on.
        public readonly firstRecordIndex: number,
    ) {}

    /** Close this transaction; see BegunTransaction.end. */
    public end(): boolean {
        return this.history.endFrame(this);
    }

    /** Put back this transaction's changes; see BegunTransaction.cancel. */
    public cancel(): void {
        this.history.cancelFrame(this);
    }

    /** See BegunTransaction.onClose. */
    public onClose(
        listener: (stepDescriptionL10nKey: string | undefined) => void,
    ): void {
        const closedWith = this.openTransaction.closedWith;
        if (closedWith) listener(closedWith.stepDescriptionL10nKey);
        else this.openTransaction.closeListeners.push(listener);
    }
}

/** An undo or redo asked for while a transaction was open, waiting until none is. */
interface QueuedRequest {
    direction: "undo" | "redo";
    // Settle the promise that undo() or redo() returned.
    resolve: () => void;
    reject: (error: unknown) => void;
}

/**
 * Where the records of a batch go, apart from what an open transaction decides.
 * "addToLatestStep" is addToLatestTransaction while the only transaction open is a stand-in that
 * has changed nothing yet: the batch joins the latest step, as if no transaction were open.
 */
type CallContext =
    | "addToLatestTransaction"
    | "addToLatestStep"
    | "doOutsideHistory"
    | undefined;

/** The PageHistory for one page. See pageHistoryTypes.ts for what each member must do. */
class PageHistoryImpl implements PageHistory {
    private readonly root: Element;
    private readonly filter: PageHistoryFilter;
    private readonly maxUndoSteps: number;
    private readonly onUnmarkedChange: (whatChanged: string) => void;
    private readonly observer: MutationObserver;
    private undoSteps: Step[] = [];
    private redoSteps: Step[] = [];
    // The transactions open now, outermost first. They share one OpenTransaction.
    private frames: TransactionFrame[] = [];
    private openTransaction: OpenTransaction | undefined;
    private callContext: CallContext;
    // True when the observer observes removed nodes besides the root (observeRemovedNodes).
    private isObservingRemovedNodes = false;
    private readonly beforeUndoOrRedoListeners = new Set<() => void>();
    private readonly afterUndoOrRedoListeners = new Set<
        (restored: RestoredContent) => void
    >();
    // Undo and redo requests made while a transaction was open, oldest first.
    private queuedRequests: QueuedRequest[] = [];
    // True while runQueuedRequests is running one, so that a transaction that one of undo's
    // listeners opens and closes does not start the next request in the middle of it.
    private isRunningQueuedRequest = false;
    private disposed = false;
    // False until markPageReady: a change outside any transaction is then not recorded.
    private pageReady: boolean;

    public constructor(options: PageHistoryOptions) {
        this.root = options.root;
        this.pageReady = options.startsReady ?? true;
        this.filter = options.filter ?? kKeepEverythingFilter;
        this.maxUndoSteps = options.maxUndoSteps ?? kDefaultMaxUndoSteps;
        if (!Number.isInteger(this.maxUndoSteps) || this.maxUndoSteps < 0)
            throw new Error(
                `Page history: maxUndoSteps must be a whole number of zero or more, not ${this.maxUndoSteps}.`,
            );
        this.onUnmarkedChange =
            options.onUnmarkedChange ?? warnInDevelopmentBuilds;
        this.observer = new MutationObserver((records) =>
            this.noteRecords(records),
        );
        this.observer.observe(this.root, kObserverOptions);
        this.filter.noteRecords(this.root, []);
    }

    /** See PageHistory.doTransaction. */
    public async doTransaction<T>(
        descriptionL10nKey: string,
        fn: (transaction: Transaction) => T | Promise<T>,
    ): Promise<T> {
        this.assertNotDisposed();
        const frame = this.openFrame(descriptionL10nKey);
        let value: T;
        try {
            const result = fn(frame);
            // A function that returns a plain value has its transaction closed in this same
            // task, without waiting for a microtask, so that nothing else can join it.
            if (isThenable(result)) {
                frame.isAsyncDoTransaction = true;
                value = await result;
            } else value = result as T;
        } catch (error) {
            this.cancelFrame(frame);
            throw error;
        }
        this.endFrame(frame);
        return value;
    }

    /** See PageHistory.beginTransaction. */
    public beginTransaction(descriptionL10nKey: string): BegunTransaction {
        this.assertNotDisposed();
        return this.openFrame(descriptionL10nKey);
    }

    /** See PageHistory.addToLatestTransaction. */
    public addToLatestTransaction<T>(fn: (transaction: Transaction) => T): T {
        this.assertNotDisposed();
        this.processPendingRecords();
        // A stand-in that has changed nothing is not yet the latest transaction: the user's
        // press opened it, but the latest action that changed the page is the latest step, and
        // fn is usually Bloom's delayed reaction to that action, such as the toolbox's markup
        // pass after typing. Joined to the stand-in, it would make a step of its own, and in
        // another window end the history (pageHistoryInput.ts).
        const joinsLatestStep = this.isStandInThatHasChangedNothing();
        if (joinsLatestStep) {
            // Its entries have no net change. Dropped, they cannot set the values that fn
            // changes back to those from before fn when the stand-in closes; the next records
            // give their before values afresh.
            this.openTransaction!.changes.clear();
        }
        const latestDescription = joinsLatestStep
            ? this.latestStep()?.descriptionL10nKey
            : (this.openTransaction?.descriptionL10nKey ??
              this.latestStep()?.descriptionL10nKey);
        // With no latest transaction, fn's changes become starting state, and its transaction
        // records nothing: there is nothing in it to record yet.
        const transaction: Transaction = {
            descriptionL10nKey:
                latestDescription ?? kGenericUndoDescriptionL10nKey,
        };
        return this.runInContext(
            joinsLatestStep ? "addToLatestStep" : "addToLatestTransaction",
            () => fn(transaction),
        );
    }

    /**
     * True when the only transaction open is an outermost one with the general description, the
     * stand-in that Bloom opens on a real click or key press, and nothing it has recorded has a
     * net change yet.
     */
    private isStandInThatHasChangedNothing(): boolean {
        const open = this.openTransaction;
        if (!open || this.frames.length !== 1) return false;
        if (
            this.frames[0].descriptionL10nKey !== kGenericUndoDescriptionL10nKey
        )
            return false;
        return this.pageHasValuesOf(open.changes, "before");
    }

    /** See PageHistory.doOutsideHistory. */
    public doOutsideHistory<T>(fn: () => T): T {
        this.assertNotDisposed();
        this.processPendingRecords();
        return this.runInContext("doOutsideHistory", fn);
    }

    /** See PageHistory.markPageReady. */
    public markPageReady(): void {
        this.assertNotDisposed();
        // The changes made so far, before ready, must be taken as made before ready, though the
        // observer has not delivered them yet.
        this.processPendingRecords();
        this.pageReady = true;
    }

    /** See PageHistory.clearUndoAndRedo. */
    public clearUndoAndRedo(): void {
        this.assertNotDisposed();
        this.processPendingRecords();
        this.dropQueuedRequests();
        this.abandonOpenTransactions();
        this.undoSteps = [];
        this.redoSteps = [];
    }

    /** See PageHistory.onBeforeUndoOrRedo. */
    public onBeforeUndoOrRedo(cancelPendingWrites: () => void): () => void {
        this.assertNotDisposed();
        this.beforeUndoOrRedoListeners.add(cancelPendingWrites);
        return () => this.beforeUndoOrRedoListeners.delete(cancelPendingWrites);
    }

    /** See PageHistory.onAfterUndoOrRedo. */
    public onAfterUndoOrRedo(
        listener: (restored: RestoredContent) => void,
    ): () => void {
        this.assertNotDisposed();
        this.afterUndoOrRedoListeners.add(listener);
        return () => this.afterUndoOrRedoListeners.delete(listener);
    }

    /** See PageHistory.undo. */
    public undo(): Promise<void> {
        return this.requestUndoOrRedo("undo");
    }

    /** See PageHistory.redo. */
    public redo(): Promise<void> {
        return this.requestUndoOrRedo("redo");
    }

    /**
     * Queue an undo or redo behind any that are waiting, and run them if no transaction is open.
     * The promise settles when this one has run or been dropped.
     */
    private requestUndoOrRedo(direction: "undo" | "redo"): Promise<void> {
        return new Promise((resolve, reject) => {
            this.assertNotDisposed();
            this.queuedRequests.push({ direction, resolve, reject });
            this.runQueuedRequests();
        });
    }

    /**
     * Run the queued undo and redo requests, oldest first, while no transaction is open. A
     * transaction that one of undo's listeners leaves open stops them; they go on when it closes.
     * Called whenever a request is made and whenever a transaction ends.
     */
    private runQueuedRequests(): void {
        if (this.isRunningQueuedRequest) return;
        while (
            !this.disposed &&
            this.frames.length === 0 &&
            this.queuedRequests.length > 0
        ) {
            const request = this.queuedRequests.shift()!;
            this.isRunningQueuedRequest = true;
            try {
                this.undoOrRedo(request.direction);
                request.resolve();
            } catch (error) {
                request.reject(error);
            } finally {
                this.isRunningQueuedRequest = false;
            }
        }
    }

    /** Drop the queued undo and redo requests, settling their promises, for a boundary. */
    private dropQueuedRequests(): void {
        const dropped = this.queuedRequests;
        this.queuedRequests = [];
        dropped.forEach((request) => request.resolve());
    }

    /** See PageHistory.undoDescriptionL10nKey. */
    public get undoDescriptionL10nKey(): string | undefined {
        this.processPendingRecords();
        return this.latestStep()?.descriptionL10nKey;
    }

    /** See PageHistory.redoDescriptionL10nKey. */
    public get redoDescriptionL10nKey(): string | undefined {
        this.processPendingRecords();
        return this.redoSteps[this.redoSteps.length - 1]?.descriptionL10nKey;
    }

    /** See PageHistory.hasOpenTransaction. */
    public get hasOpenTransaction(): boolean {
        return this.frames.length > 0;
    }

    /** See PageHistory.dispose. */
    public dispose(): void {
        this.assertNotDisposed();
        this.dropQueuedRequests();
        this.abandonOpenTransactions();
        this.observer.disconnect();
        this.undoSteps = [];
        this.redoSteps = [];
        this.beforeUndoOrRedoListeners.clear();
        this.afterUndoOrRedoListeners.clear();
        this.disposed = true;
    }

    /** True once dispose() has run. */
    public get isDisposed(): boolean {
        return this.disposed;
    }

    /**
     * Close frame (BegunTransaction.end, or doTransaction's fn settling), and return true when
     * that made an undo step. Closing the outermost transaction makes the step. Two kinds of
     * transaction, ended while transactions begun after them are still open, wait for those
     * instead, and end when the last of them ends: the outermost stand-in with the general
     * description, and an async doTransaction, which may overlap in time with another async
     * one that began while it awaited, since neither awaits the other. Ending any other
     * transaction while one begun inside it is still open, a beginTransaction or a doTransaction
     * whose fn returned a plain value, is a bug in the caller: the inner ones end with it, and in
     * development it throws. Does nothing if frame has already ended. Once no transaction is
     * open, runs the undo and redo requests that waited for that.
     */
    public endFrame(frame: TransactionFrame): boolean {
        if (frame.ended || frame.endRequested) return false;
        this.processPendingRecords();
        const index = this.frames.indexOf(frame);
        const innerStillOpen = index < this.frames.length - 1;
        const waitsForInner =
            (index === 0 &&
                frame.descriptionL10nKey === kGenericUndoDescriptionL10nKey) ||
            frame.isAsyncDoTransaction;
        if (innerStillOpen && waitsForInner) {
            frame.endRequested = true;
            return false;
        }
        this.endFramesFrom(index);
        let madeStep = false;
        if (index === 0) madeStep = this.closeOpenTransaction();
        else madeStep = this.endFramesThatWaitForNoOne();
        this.runQueuedRequests();
        if (innerStillOpen && import.meta.env.DEV)
            throw new Error(
                `Page history: the transaction "${frame.descriptionL10nKey}" ended while a transaction begun inside it was still open.`,
            );
        return madeStep;
    }

    /**
     * Put back every change since frame began (BegunTransaction.cancel, or doTransaction's fn
     * throwing). The transactions begun inside it end with it, without complaint, since putting
     * everything back since frame began leaves nothing of theirs to get wrong; and a doTransaction
     * whose fn threw must rethrow fn's error, not a new one. Does nothing if frame has already
     * ended, or is waiting for transactions begun after it (endFrame). Cancelling the last
     * transaction that such a one waits for lets it end, and the outermost close with whatever
     * else changed. Once no transaction is open, runs the undo and redo requests that waited.
     *
     * Every record since frame began is reversed, whichever code made the change. So when frame
     * is an async doTransaction that overlaps another, a cancel also puts back what the other
     * wrote after frame began: records cannot be attributed to one of two interleaved async
     * functions.
     */
    public cancelFrame(frame: TransactionFrame): void {
        if (frame.ended || frame.endRequested) return;
        this.processPendingRecords();
        const index = this.frames.indexOf(frame);
        const open = this.openTransaction!;
        if (
            open.descriptionGivenBy &&
            this.frames.indexOf(open.descriptionGivenBy) >= index
        ) {
            // The nested transaction that described the step is putting back all its changes.
            open.descriptionL10nKey = kGenericUndoDescriptionL10nKey;
            open.descriptionGivenBy = undefined;
        }
        this.endFramesFrom(index);
        const records = open.records.splice(frame.firstRecordIndex);
        for (let i = records.length - 1; i >= 0; i--) reverseRecord(records[i]);
        this.putBackKeptOldTexts(records, open.keptOldTexts);
        // The rollback's own changes are not edits.
        this.discardPendingRecords();
        // The open transaction's changes may still name values that the rollback put back; they
        // have no net change now and go when the outermost transaction closes.
        if (index === 0) {
            this.openTransaction = undefined;
            this.stopObservingRemovedNodes();
            this.noteClosed(open, undefined);
        } else {
            this.endFramesThatWaitForNoOne();
        }
        this.runQueuedRequests();
    }

    /**
     * End each transaction, newest first, that waits only for transactions that have now ended
     * (endRequested). When that ends the outermost, close it, and return true when that made an
     * undo step.
     */
    private endFramesThatWaitForNoOne(): boolean {
        let last = this.frames.length - 1;
        while (last >= 0 && this.frames[last].endRequested) last--;
        if (last === this.frames.length - 1) return false;
        this.endFramesFrom(last + 1);
        if (this.frames.length > 0) return false;
        return this.closeOpenTransaction();
    }

    /**
     * Record that open has closed, with the description of the undo step that it made, if any,
     * and call the listeners of BegunTransaction.onClose.
     */
    private noteClosed(
        open: OpenTransaction,
        stepDescriptionL10nKey: string | undefined,
    ): void {
        open.closedWith = { stepDescriptionL10nKey };
        const listeners = open.closeListeners;
        open.closeListeners = [];
        listeners.forEach((listener) => listener(stepDescriptionL10nKey));
    }

    /**
     * Open a transaction, nested inside the open one if there is one. An outermost transaction
     * with the general description is a stand-in, such as the one that Bloom opens on a real
     * click or key press, so the first transaction nested inside it with a description of its
     * own gives the step that description (APPROACHES 11.8.1, "Transactions inside
     * transactions").
     */
    private openFrame(descriptionL10nKey: string): TransactionFrame {
        this.processPendingRecords();
        if (!this.openTransaction) {
            this.openTransaction = {
                descriptionL10nKey,
                changes: new Map(),
                records: [],
                keptOldTexts: new Map(),
                selectionBefore: saveSelection(this.root.ownerDocument),
                closeListeners: [],
            };
        }
        const open = this.openTransaction;
        const frame = new TransactionFrame(
            this,
            descriptionL10nKey,
            open,
            open.records.length,
        );
        if (
            this.frames.length > 0 &&
            open.descriptionL10nKey === kGenericUndoDescriptionL10nKey &&
            descriptionL10nKey !== kGenericUndoDescriptionL10nKey
        ) {
            open.descriptionL10nKey = descriptionL10nKey;
            open.descriptionGivenBy = frame;
        }
        this.frames.push(frame);
        return frame;
    }

    /** Mark the frame at index, and every frame begun inside it, ended, and forget them. */
    private endFramesFrom(index: number): void {
        this.frames.slice(index).forEach((frame) => (frame.ended = true));
        this.frames.length = index;
    }

    /**
     * End every open transaction, leaving its changes on the page in no transaction, for
     * clearUndoAndRedo and dispose.
     */
    private abandonOpenTransactions(): void {
        const open = this.openTransaction;
        this.endFramesFrom(0);
        this.openTransaction = undefined;
        this.stopObservingRemovedNodes();
        if (open) this.noteClosed(open, undefined);
    }

    /**
     * Make the undo step of the outermost transaction, which has just ended, from its before
     * values and the page's values now. A transaction with no net change makes no step. Returns
     * true when it made one. The listeners of BegunTransaction.onClose run last.
     */
    private closeOpenTransaction(): boolean {
        const open = this.openTransaction!;
        this.openTransaction = undefined;
        this.stopObservingRemovedNodes();
        this.readAfterValues(open.changes, open.changes);
        removeEntriesWithNoNetChange(open.changes);
        if (open.changes.size === 0) {
            this.noteClosed(open, undefined);
            return false;
        }
        this.undoSteps.push({
            descriptionL10nKey: open.descriptionL10nKey,
            changes: open.changes,
            detachedAsText: this.serializeDetachedNodes(
                open.changes,
                "before",
                "after",
            ),
            selectionBefore: open.selectionBefore,
            selectionAfter: saveSelection(this.root.ownerDocument),
        });
        while (this.undoSteps.length > this.maxUndoSteps)
            this.undoSteps.shift();
        this.redoSteps = [];
        this.noteClosed(open, open.descriptionL10nKey);
        return true;
    }

    /** Run fn with the records of its changes going where context says. */
    private runInContext<T>(context: CallContext, fn: () => T): T {
        const outerContext = this.callContext;
        this.callContext = context;
        try {
            return fn();
        } finally {
            this.processPendingRecords();
            this.callContext = outerContext;
        }
    }

    /** Take the records that the observer holds and note them, so that none is left pending. */
    private processPendingRecords(): void {
        if (this.disposed) return;
        this.noteRecords(this.observer.takeRecords());
    }

    /** Take the records that the observer holds and drop them: they are not edits. */
    private discardPendingRecords(): void {
        const records = this.observer.takeRecords();
        if (records.length === 0) return;
        this.filter.noteRecords(this.root, records);
        this.observeRemovedNodes(records);
    }

    /**
     * While a transaction is open, observe each node that records took out of the page, with
     * what is inside it, so that a change the transaction makes to a removed node is recorded
     * and undo puts it back. The observer itself sees a change to a removed node only until it
     * delivers its records (in jsdom, not at all), and a transaction may change the node after
     * an await. Outside a transaction, a change to a removed node stays unseen, as the design
     * intends (APPROACHES.md section 4.4, "Changes to detached nodes"): the check of rule 4
     * refuses an undo that would reinsert it.
     */
    private observeRemovedNodes(records: MutationRecord[]): void {
        if (!this.openTransaction) return;
        for (const record of records) {
            if (record.type !== "childList" || this.isIgnored(record.target))
                continue;
            record.removedNodes.forEach((removed) => {
                if (!this.root.contains(removed)) {
                    this.observer.observe(removed, kObserverOptions);
                    this.isObservingRemovedNodes = true;
                }
            });
        }
    }

    /**
     * Go back to observing only the root, when the outermost transaction has ended. A
     * MutationObserver cannot stop observing one node, so this starts again on the root alone.
     * Disconnecting drops pending records, so the caller must have taken them already.
     */
    private stopObservingRemovedNodes(): void {
        if (!this.isObservingRemovedNodes) return;
        this.observer.disconnect();
        this.observer.observe(this.root, kObserverOptions);
        this.isObservingRemovedNodes = false;
    }

    /**
     * Send one batch of records where it belongs: nowhere inside doOutsideHistory, or outside any
     * transaction before markPageReady; to the open transaction if there is one, unless
     * addToLatestTransaction found only a stand-in that has changed nothing ("addToLatestStep");
     * otherwise to the latest step (rule 1), which clears redo, warning unless inside
     * addToLatestTransaction. With no latest step, the changes become starting state. A batch
     * with no net change changes nothing here.
     */
    private noteRecords(records: MutationRecord[]): void {
        if (records.length === 0) return;
        this.filter.noteRecords(this.root, records);
        this.observeRemovedNodes(records);
        if (this.callContext === "doOutsideHistory") return;
        const open =
            this.callContext === "addToLatestStep"
                ? undefined
                : this.openTransaction;
        if (!open && !this.pageReady) return;
        const keptOldTexts = new Map<MutationRecord, string>();
        const batch = this.netChanges(records, keptOldTexts);
        if (open) {
            for (const record of records) open.records.push(record);
            keptOldTexts.forEach((kept, record) =>
                open.keptOldTexts.set(record, kept),
            );
            addBeforeValues(open.changes, batch);
            return;
        }
        if (batch.size === 0) return;
        this.redoSteps = [];
        const latest = this.latestStep();
        if (!latest) return;
        addBeforeValues(latest.changes, batch);
        this.readAfterValues(latest.changes, batch);
        removeEntriesWithNoNetChange(latest.changes);
        if (latest.changes.size === 0) {
            // What joined undid everything the step did, so the step has nothing left to undo.
            this.undoSteps.pop();
        } else {
            latest.detachedAsText = this.serializeDetachedNodes(
                latest.changes,
                "before",
                "after",
                latest.detachedAsText,
            );
        }
        if (this.callContext === undefined) {
            describeBatch(batch).forEach((whatChanged) =>
                this.onUnmarkedChange(whatChanged),
            );
        }
    }

    /**
     * The net effect of a batch of records, which must be all the records up to now, leaving out
     * what the filter ignores. The value at the start of the batch comes from the first record
     * for each attribute and text, and for a child list from working back from the children now
     * through that parent's records. A parent with no childList record is compared too when a text
     * change made one of its text nodes start or stop being a child that the filter ignores.
     * Attributes and texts are compared, and their values given, by the part that the filter
     * keeps. Adds to keptOldTexts the kept text of the old value of the first record of each text
     * node.
     */
    private netChanges(
        records: MutationRecord[],
        keptOldTexts: Map<MutationRecord, string>,
    ): BatchChanges {
        const firstAttributeValues = new Map<
            Element,
            Map<string, string | undefined>
        >();
        const firstTextRecords = new Map<Node, MutationRecord>();
        const childListRecords = new Map<Node, MutationRecord[]>();
        for (const record of records) {
            if (this.isIgnored(record.target)) continue;
            switch (record.type) {
                case "attributes": {
                    const element = record.target as Element;
                    const name = record.attributeName!;
                    let values = firstAttributeValues.get(element);
                    if (!values) {
                        values = new Map();
                        firstAttributeValues.set(element, values);
                    }
                    if (!values.has(name))
                        values.set(name, record.oldValue ?? undefined);
                    break;
                }
                case "characterData":
                    if (!firstTextRecords.has(record.target))
                        firstTextRecords.set(record.target, record);
                    break;
                case "childList": {
                    let list = childListRecords.get(record.target);
                    if (!list) {
                        list = [];
                        childListRecords.set(record.target, list);
                    }
                    list.push(record);
                    break;
                }
            }
        }

        // Whether a child was left out of child lists at the start of the batch. A text node may
        // be left out for one value and not another, so this uses its text at that time.
        const wasIgnoredChild = (child: Node): boolean => {
            if (this.filter.ignoreNode(child)) return true;
            if (!isCharacterData(child)) return false;
            const record = firstTextRecords.get(child);
            const textAtStart = record ? (record.oldValue ?? "") : child.data;
            return this.filter.isIgnoredText(child, textAtStart, "earlier");
        };

        // Each parent's children at the start of the batch, ignored ones included.
        const childrenAtStart = new Map<Node, Node[]>();
        childListRecords.forEach((parentRecords, parent) =>
            childrenAtStart.set(
                parent,
                childrenBeforeRecords(parent, parentRecords),
            ),
        );
        // A text node whose new text made it start or stop being a child that the filter ignores
        // changed its parent's child list with no childList record, as when typing fills the node
        // that holds CKEditor's filling character. The step must hold that list, so that redo puts
        // the node back if something takes it out of the page while it is ignored, as merging
        // empty text nodes does; otherwise redo writes its text into a node that is no longer on
        // the page.
        firstTextRecords.forEach((_record, node) => {
            const parent = node.parentNode;
            if (!parent || childrenAtStart.has(parent)) return;
            if (wasIgnoredChild(node) === this.isIgnoredChild(node)) return;
            childrenAtStart.set(parent, Array.from(parent.childNodes));
        });
        const mayBeNeeded = this.nodesThatUndoMayNeed(childrenAtStart);

        const batch: BatchChanges = new Map();
        const entryFor = (node: Node): BatchChange => {
            let entry = batch.get(node);
            if (!entry) {
                entry = {};
                batch.set(node, entry);
            }
            return entry;
        };
        firstAttributeValues.forEach((values, element) => {
            if (!mayBeNeeded(element)) return;
            values.forEach((oldValue, name) => {
                const before = this.filter.keptAttributeValue(
                    element,
                    name,
                    oldValue,
                );
                if (before === this.keptAttributeValue(element, name)) return;
                const entry = entryFor(element);
                if (!entry.attributes) entry.attributes = new Map();
                entry.attributes.set(name, before);
            });
        });
        firstTextRecords.forEach((record, node) => {
            const before = this.filter.keptText(
                node as CharacterData,
                record.oldValue ?? "",
                "earlier",
            );
            keptOldTexts.set(record, before);
            if (mayBeNeeded(node) && before !== this.keptText(node))
                entryFor(node).text = before;
        });
        childrenAtStart.forEach((children, parent) => {
            if (!mayBeNeeded(parent)) return;
            const before = this.filterChildList(
                parent,
                children,
                wasIgnoredChild,
            );
            if (!sameNodes(before, this.filteredChildren(parent)))
                entryFor(parent).children = before;
        });
        return batch;
    }

    /**
     * A test of whether undo or redo may ever need a node that a batch of records changed. A node
     * in the page does. A node out of the page at the end of the batch does only if it, or a node
     * that contains it, was in the page at the start of the batch, or is in a child list of the
     * open transaction or of an undo or redo step. A node was in the page at the start when it is
     * among the children at the start (childrenAtStart) of a parent that undo may need. A node
     * made and thrown away within the batch, such as a hidden clone that measures text, never was
     * part of the page, and its changes are no change.
     */
    private nodesThatUndoMayNeed(
        childrenAtStart: Map<Node, Node[]>,
    ): (node: Node) => boolean {
        let held: Set<Node> | undefined;
        return (node: Node) => {
            if (this.root.contains(node)) return true;
            if (!held) held = this.heldNodes(childrenAtStart);
            return isInsideOneOf(node, held);
        };
    }

    /**
     * The nodes in a child list of the open transaction or of an undo or redo step, and the
     * children at the start of the batch of each parent that is in the page or inside a node
     * already found, repeated until no more are found.
     */
    private heldNodes(childrenAtStart: Map<Node, Node[]>): Set<Node> {
        const held = new Set<Node>();
        const addChildLists = (changes: ChangeSet) =>
            changes.forEach((change) => {
                change.children?.before.forEach((child) => held.add(child));
                change.children?.after.forEach((child) => held.add(child));
            });
        if (this.openTransaction) addChildLists(this.openTransaction.changes);
        this.undoSteps.forEach((step) => addChildLists(step.changes));
        this.redoSteps.forEach((step) => addChildLists(step.changes));
        const parentsLeft = new Set(childrenAtStart.keys());
        let found = true;
        while (found) {
            found = false;
            parentsLeft.forEach((parent) => {
                if (!this.root.contains(parent) && !isInsideOneOf(parent, held))
                    return;
                childrenAtStart
                    .get(parent)!
                    .forEach((child) => held.add(child));
                parentsLeft.delete(parent);
                found = true;
            });
        }
        return held;
    }

    /**
     * Read from the page the after values of the entries in changes that keys names: every
     * attribute, text and child list of each of its nodes.
     */
    private readAfterValues(
        changes: ChangeSet,
        keys: Map<Node, BatchChange | NodeChange>,
    ): void {
        keys.forEach((key, node) => {
            const change = changes.get(node)!;
            key.attributes?.forEach((_value, name: string) => {
                change.attributes!.get(name)!.after = this.keptAttributeValue(
                    node as Element,
                    name,
                );
            });
            if (key.text !== undefined)
                change.text!.after = this.keptText(node);
            if (key.children)
                change.children!.after = this.filteredChildren(node);
        });
    }

    /**
     * The nodes that moving the page from the `from` values of changes to the `to` values took
     * out of the page, each serialized by serializeKept: those in a `from` child list, in no `to`
     * child list, and not in the page now. A node already in earlier keeps the text from there, so
     * that a change to it made while it was detached, which the observer cannot see, still fails
     * the check.
     */
    private serializeDetachedNodes(
        changes: ChangeSet,
        from: Values,
        to: Values,
        earlier?: Map<Node, string>,
    ): Map<Node, string> {
        const placed = new Set<Node>();
        changes.forEach((change) =>
            change.children?.[to].forEach((child) => placed.add(child)),
        );
        const detached = new Map<Node, string>();
        changes.forEach((change) =>
            change.children?.[from].forEach((child) => {
                if (placed.has(child) || this.root.contains(child)) return;
                detached.set(
                    child,
                    earlier?.get(child) ?? this.serializeKept(child),
                );
            }),
        );
        return detached;
    }

    /**
     * A subtree as text, as the filter sees it, for the check of rule 4 on a detached subtree:
     * only the children that the filter does not ignore, the kept part of each attribute in name
     * order, and the kept part of each text. These are the values that the check compares for a
     * node in the page, so markup that exists only while editing, such as the classes CKEditor
     * adds when it finishes attaching to an editable that has already left the page, never fails
     * the check, while any change that a save keeps does. A child that the filter leaves out of
     * child lists alone (isLeftOutOfChildList) is in the text, since the history records what is
     * inside it.
     */
    private serializeKept(node: Node): string {
        if (isCharacterData(node)) return JSON.stringify(this.keptText(node));
        if (node.nodeType !== Node.ELEMENT_NODE) return serializeNode(node);
        const element = node as Element;
        const attributes = element
            .getAttributeNames()
            .sort()
            .map((name) => {
                const kept = this.keptAttributeValue(element, name);
                return kept === undefined
                    ? ""
                    : ` ${name}=${JSON.stringify(kept)}`;
            })
            .join("");
        const listed = new Set(this.filteredChildren(element));
        const children = Array.from(element.childNodes)
            .filter(
                (child) =>
                    listed.has(child) ||
                    (!this.isIgnoredChild(child) &&
                        this.filter.isLeftOutOfChildList(child, element)),
            )
            .map((child) => this.serializeKept(child))
            .join("");
        const tag = element.tagName.toLowerCase();
        return `<${tag}${attributes}>${children}</${tag}>`;
    }

    /**
     * The check of rule 4: true when the page still has every `expected` value of step, every
     * node that the step names is in the page or inside a node that the step puts back into it,
     * and every subtree that the step's last move took out is still out and serializes as it did
     * then. A node that a change outside the history took out of the page fails the check, since
     * putting back its values would change nothing that the user can see.
     */
    private pageHasValues(step: Step, expected: Values): boolean {
        const toPutBack: Values = expected === "after" ? "before" : "after";
        const putBack = new Set<Node>();
        step.changes.forEach((change) =>
            change.children?.[toPutBack].forEach((child) => putBack.add(child)),
        );
        for (const node of step.changes.keys()) {
            if (!this.root.contains(node) && !isInsideOneOf(node, putBack))
                return false;
        }
        if (!this.pageHasValuesOf(step.changes, expected)) return false;
        for (const [node, text] of step.detachedAsText) {
            if (this.root.contains(node) || this.serializeKept(node) !== text)
                return false;
        }
        return true;
    }

    /** True when the page has the `expected` value of every entry of changes. */
    private pageHasValuesOf(changes: ChangeSet, expected: Values): boolean {
        for (const [node, change] of changes) {
            if (change.attributes) {
                for (const [name, values] of change.attributes) {
                    if (
                        this.keptAttributeValue(node as Element, name) !==
                        values[expected]
                    )
                        return false;
                }
            }
            if (change.text && this.keptText(node) !== change.text[expected])
                return false;
            if (
                change.children &&
                !sameNodes(
                    this.filteredChildren(node),
                    change.children[expected],
                )
            )
                return false;
        }
        return true;
    }

    /**
     * Give the page the `values` values of every entry of step. An attribute gets back its kept
     * part, and keeps the rest of its value now, as the filter says.
     */
    private putBackValues(step: Step, values: Values): void {
        const childLists = new Map<Node, Node[]>();
        step.changes.forEach((change, node) => {
            change.attributes?.forEach((attributeValues, name) => {
                const element = node as Element;
                const current = attributeValue(element, name);
                const value = this.filter.valueToPutBack(
                    element,
                    name,
                    attributeValues[values],
                    current,
                );
                if (value === current) return;
                if (value === undefined) element.removeAttribute(name);
                else element.setAttribute(name, value);
            });
            if (change.text) this.putBackText(node, change.text[values]);
            if (change.children) childLists.set(node, change.children[values]);
        });
        // A child that the filter leaves out when it is the only one stays where it is when its
        // parent's list is put back empty, as it would if the list were all ignored children.
        const leftInPlace = new Set<Node>();
        childLists.forEach((wanted, parent) => {
            if (wanted.length > 0) return;
            const current = Array.from(parent.childNodes).filter(
                (child) => !this.isLeftOutChildOf(child, parent),
            );
            if (
                current.length === 1 &&
                this.filter.isLeftOutWhenOnlyChild(current[0], parent)
            )
                leftInPlace.add(current[0]);
        });
        // putBackChildLists asks only about a child of a parent in childLists, where it is now.
        putBackChildLists(
            childLists,
            (child) =>
                this.isLeftOutChildOf(child, child.parentNode!) ||
                leftInPlace.has(child),
        );
    }

    /** Write the kept text `kept` into a text node, in the form that the filter gives it. */
    private putBackText(node: Node, kept: string): void {
        const textNode = node as CharacterData;
        const data = this.filter.textToPutBack(textNode, kept);
        if (textNode.data !== data) textNode.data = data;
    }

    /**
     * After a rollback has reversed records, give each text node that they changed the kept text
     * of its oldest record's old value, in place of the raw old value that reversing wrote.
     */
    private putBackKeptOldTexts(
        records: MutationRecord[],
        keptOldTexts: Map<MutationRecord, string>,
    ): void {
        const done = new Set<Node>();
        for (const record of records) {
            if (record.type !== "characterData" || done.has(record.target))
                continue;
            done.add(record.target);
            const kept = keptOldTexts.get(record);
            if (kept !== undefined) this.putBackText(record.target, kept);
        }
    }

    /**
     * The elements whose attributes, text or children step put back, as onAfterUndoOrRedo passes
     * them: only those in the page, and none inside another.
     */
    private restoredRoots(step: Step): Element[] {
        const elements = new Set<Element>();
        step.changes.forEach((change, node) => {
            const element =
                node.nodeType === Node.ELEMENT_NODE
                    ? (node as Element)
                    : node.parentElement;
            if (element && this.root.contains(element)) elements.add(element);
        });
        return Array.from(elements).filter(
            (element) =>
                !Array.from(elements).some(
                    (other) => other !== element && other.contains(element),
                ),
        );
    }

    /**
     * Undo the latest step or redo the one undone last, in the order of APPROACHES.md section
     * 11.8.1: the onBeforeUndoOrRedo listeners, the check, putting values back, the selection,
     * the onAfterUndoOrRedo listeners. The selection comes before the listeners so that they see
     * the final caret and focus, and so that what restoring it writes (a focusin handler that it
     * fires) is dropped with undo's own records. Only runQueuedRequests calls this, with no
     * transaction open.
     */
    private undoOrRedo(direction: "undo" | "redo"): void {
        const isUndo = direction === "undo";
        if ((isUndo ? this.undoSteps : this.redoSteps).length === 0) return;

        Array.from(this.beforeUndoOrRedoListeners).forEach((listener) =>
            listener(),
        );
        // What the listeners changed joins the latest step (rule 1), which may clear redo or
        // leave the latest step with nothing to undo, so look again for the step.
        this.processPendingRecords();
        const steps = isUndo ? this.undoSteps : this.redoSteps;
        const step = steps[steps.length - 1];
        if (!step) return;

        const from: Values = isUndo ? "after" : "before";
        const to: Values = isUndo ? "before" : "after";
        if (!this.pageHasValues(step, from)) {
            // The step is the newest on its stack, so it and every older one is the whole stack.
            if (isUndo) this.undoSteps = [];
            else this.redoSteps = [];
            return;
        }
        this.putBackValues(step, to);
        // Undo's own changes are not edits, nor are those of handlers that the restored
        // selection set off. Restoring the selection can focus a box, and a focus handler may
        // call into the history, which takes the pending records first; so the put-back's
        // records go before that, and the restore runs as if inside doOutsideHistory.
        this.discardPendingRecords();
        this.runInContext("doOutsideHistory", () =>
            restoreSelection(
                this.root.ownerDocument,
                this.root,
                isUndo ? step.selectionBefore : step.selectionAfter,
            ),
        );
        this.discardPendingRecords();
        step.detachedAsText = this.serializeDetachedNodes(
            step.changes,
            from,
            to,
        );
        steps.pop();
        if (isUndo) this.redoSteps.push(step);
        else this.undoSteps.push(step);

        const restored: RestoredContent = {
            roots: this.restoredRoots(step),
            direction,
        };
        Array.from(this.afterUndoOrRedoListeners).forEach((listener) =>
            listener(restored),
        );
        // A listener's writes join the latest step and clear redo (rules 1 and 8).
        this.processPendingRecords();
    }

    /** The newest undo step, or undefined when there is none. */
    private latestStep(): Step | undefined {
        return this.undoSteps[this.undoSteps.length - 1];
    }

    /** The part of an element's attribute value now that the filter keeps. */
    private keptAttributeValue(
        element: Element,
        name: string,
    ): string | undefined {
        return this.filter.keptAttributeValue(
            element,
            name,
            attributeValue(element, name),
        );
    }

    /** The part of a text node's data now that the filter keeps. */
    private keptText(node: Node): string {
        const textNode = node as CharacterData;
        return this.filter.keptText(textNode, textNode.data, "now");
    }

    /** True for a node that the filter ignores, or that is inside one, up to the root. */
    private isIgnored(node: Node): boolean {
        for (let n: Node | null = node; n; n = n.parentNode) {
            if (this.filter.ignoreNode(n)) return true;
            if (n === this.root) return false;
        }
        return false;
    }

    /** True for a child, of a node that is not ignored, that the filter ignores. */
    private isIgnoredChild(child: Node): boolean {
        if (this.filter.ignoreNode(child)) return true;
        return (
            isCharacterData(child) &&
            this.filter.isIgnoredText(child, child.data, "now")
        );
    }

    /**
     * True for a child of parent that the filter leaves out of parent's child lists now: one
     * that it ignores, or one that it leaves out of the lists alone (isLeftOutOfChildList).
     */
    private isLeftOutChildOf(child: Node, parent: Node): boolean {
        return (
            this.isIgnoredChild(child) ||
            this.filter.isLeftOutOfChildList(child, parent)
        );
    }

    /** The children of node that the filter does not ignore. */
    private filteredChildren(node: Node): Node[] {
        return this.filterChildList(
            node,
            Array.from(node.childNodes),
            (child) => this.isIgnoredChild(child),
        );
    }

    /**
     * The children in a child list of parent that the filter does not ignore: those for which
     * isIgnored is false and that the filter does not leave out of parent's child lists
     * (isLeftOutOfChildList), less the one that is left if the filter leaves it out when it is
     * the only child (isLeftOutWhenOnlyChild).
     */
    private filterChildList(
        parent: Node,
        children: Node[],
        isIgnored: (child: Node) => boolean,
    ): Node[] {
        const kept = children.filter(
            (child) =>
                !isIgnored(child) &&
                !this.filter.isLeftOutOfChildList(child, parent),
        );
        if (
            kept.length === 1 &&
            this.filter.isLeftOutWhenOnlyChild(kept[0], parent)
        )
            return [];
        return kept;
    }

    /** Fail fast on a call after dispose(). */
    private assertNotDisposed(): void {
        if (this.disposed)
            throw new Error("Page history: used after it was disposed.");
    }
}

/** True when node, or a node that contains it, is in nodes. */
function isInsideOneOf(node: Node, nodes: Set<Node>): boolean {
    for (let n: Node | null = node; n; n = n.parentNode) {
        if (nodes.has(n)) return true;
    }
    return false;
}

/** True for a text node. */
function isCharacterData(node: Node): node is CharacterData {
    return node.nodeType === Node.TEXT_NODE;
}

/** The filter of a history given none: it ignores nothing and keeps every value whole. */
export const kKeepEverythingFilter: PageHistoryFilter = {
    ignoreNode: () => false,
    isLeftOutOfChildList: () => false,
    isLeftOutWhenOnlyChild: () => false,
    isIgnoredText: () => false,
    keptAttributeValue: (_element, _name, value) => value,
    valueToPutBack: (_element, _name, kept) => kept,
    keptText: (_node, value) => value,
    textToPutBack: (_node, kept) => kept,
    noteRecords: () => undefined,
};

/** An element's attribute value, undefined when it has none. */
function attributeValue(element: Element, name: string): string | undefined {
    return element.getAttribute(name) ?? undefined;
}

/**
 * Add to changes the before values from batch for what changes does not hold yet. The after
 * values start as the before values until readAfterValues reads them.
 */
function addBeforeValues(changes: ChangeSet, batch: BatchChanges): void {
    batch.forEach((batchChange, node) => {
        let change = changes.get(node);
        if (!change) {
            change = {};
            changes.set(node, change);
        }
        const nodeChange = change;
        batchChange.attributes?.forEach((before, name) => {
            if (!nodeChange.attributes) nodeChange.attributes = new Map();
            if (!nodeChange.attributes.has(name))
                nodeChange.attributes.set(name, { before, after: before });
        });
        if (batchChange.text !== undefined && !nodeChange.text)
            nodeChange.text = {
                before: batchChange.text,
                after: batchChange.text,
            };
        if (batchChange.children && !nodeChange.children)
            nodeChange.children = {
                before: batchChange.children,
                after: batchChange.children,
            };
    });
}

/** Remove from changes every value whose after is the same as its before. */
function removeEntriesWithNoNetChange(changes: ChangeSet): void {
    changes.forEach((change, node) => {
        change.attributes?.forEach((values, name) => {
            if (values.before === values.after) change.attributes!.delete(name);
        });
        if (change.attributes?.size === 0) delete change.attributes;
        if (change.text && change.text.before === change.text.after)
            delete change.text;
        if (
            change.children &&
            sameNodes(change.children.before, change.children.after)
        )
            delete change.children;
        if (!change.attributes && !change.text && !change.children)
            changes.delete(node);
    });
}

/** One line for each value that batch changed, for onUnmarkedChange. */
function describeBatch(batch: BatchChanges): string[] {
    const lines: string[] = [];
    batch.forEach((change, node) => {
        change.attributes?.forEach((_before, name) =>
            lines.push(`attribute "${name}" of ${describeNode(node)}`),
        );
        if (change.text !== undefined)
            lines.push(`the text of ${describeNode(node)}`);
        if (change.children)
            lines.push(`the children of ${describeNode(node)}`);
    });
    return lines;
}

/** The default onUnmarkedChange: a console warning in development builds, nothing otherwise. */
function warnInDevelopmentBuilds(whatChanged: string): void {
    if (import.meta.env.DEV)
        console.warn(
            `Page history: ${whatChanged} changed outside any transaction, with no addToLatestTransaction or doOutsideHistory, and joined the latest undo step.`,
        );
}

/** True for a promise, including one made in another frame, whose Promise is a different class. */
function isThenable<T>(value: T | Promise<T>): value is Promise<T> {
    return (
        typeof value === "object" &&
        value !== null &&
        typeof (value as { then?: unknown }).then === "function"
    );
}

let currentHistory: PageHistoryImpl | undefined;

/** A new page history observing options.root. It does not become the current one. */
export function createPageHistory(options: PageHistoryOptions): PageHistory {
    return new PageHistoryImpl(options);
}

/**
 * Make a new page history the current one, which pageHistory() returns, disposing the one that
 * was current, if any.
 */
export function startPageHistory(options: PageHistoryOptions): PageHistory {
    if (currentHistory && !currentHistory.isDisposed) currentHistory.dispose();
    currentHistory = new PageHistoryImpl(options);
    return currentHistory;
}

/** The current page history, from the last startPageHistory. Throws if there is none. */
export function pageHistory(): PageHistory {
    if (!currentHistory)
        throw new Error(
            "Page history: there is none; startPageHistory has not been called.",
        );
    return currentHistory;
}
