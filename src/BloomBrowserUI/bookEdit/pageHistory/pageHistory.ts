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
    // undefined when the element has no such attribute
    before: string | undefined;
    after: string | undefined;
}

/** What one step, or the open transaction, changed in one node. */
interface NodeChange {
    attributes?: Map<string, AttributeValues>;
    text?: { before: string; after: string };
    // Children for which the filter's ignoreNode is true are left out of both lists.
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
 * the end of the batch differs from its value at the start, the value at the start.
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
    changes: ChangeSet;
    // Every record since the outermost transaction opened, filtered or not, oldest first, so that
    // a rollback can reverse them back to the point where any of the nested transactions began.
    records: MutationRecord[];
    selectionBefore?: SavedSelection;
}

/**
 * One call of doTransaction or beginTransaction, outermost or nested. It is the BegunTransaction
 * that beginTransaction returns.
 */
class TransactionFrame implements BegunTransaction {
    public ended = false;

    public constructor(
        private readonly history: PageHistoryImpl,
        public readonly descriptionL10nKey: string,
        // How many records the open transaction held when this one began: a rollback of this
        // one reverses the records from this index on.
        public readonly firstRecordIndex: number,
    ) {}

    /** Close this transaction; see BegunTransaction.end. */
    public end(): void {
        this.history.endFrame(this);
    }

    /** Put back this transaction's changes; see BegunTransaction.cancel. */
    public cancel(): void {
        this.history.cancelFrame(this);
    }
}

/** Where the records of a batch go, apart from what an open transaction decides. */
type CallContext = "addToLatestTransaction" | "doOutsideHistory" | undefined;

/** The PageHistory for one page. See pageHistoryTypes.ts for what each member must do. */
class PageHistoryImpl implements PageHistory {
    private readonly root: Element;
    private readonly filter: PageHistoryFilter | undefined;
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
    private disposed = false;

    public constructor(options: PageHistoryOptions) {
        this.root = options.root;
        this.filter = options.filter;
        this.maxUndoSteps = options.maxUndoSteps ?? kDefaultMaxUndoSteps;
        this.onUnmarkedChange =
            options.onUnmarkedChange ?? warnInDevelopmentBuilds;
        this.observer = new MutationObserver((records) =>
            this.noteRecords(records),
        );
        this.observer.observe(this.root, kObserverOptions);
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
            value = isThenable(result) ? await result : (result as T);
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
        const latestDescription =
            this.openTransaction?.descriptionL10nKey ??
            this.latestStep()?.descriptionL10nKey;
        // With no latest transaction, fn's changes become starting state, and its transaction
        // records nothing: there is nothing in it to record yet.
        const transaction: Transaction = {
            descriptionL10nKey:
                latestDescription ?? kGenericUndoDescriptionL10nKey,
        };
        return this.runInContext("addToLatestTransaction", () =>
            fn(transaction),
        );
    }

    /** See PageHistory.doOutsideHistory. */
    public doOutsideHistory<T>(fn: () => T): T {
        this.assertNotDisposed();
        this.processPendingRecords();
        return this.runInContext("doOutsideHistory", fn);
    }

    /** See PageHistory.clearUndoAndRedo. */
    public clearUndoAndRedo(): void {
        this.assertNotDisposed();
        this.processPendingRecords();
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
    public async undo(): Promise<void> {
        this.undoOrRedo("undo");
    }

    /** See PageHistory.redo. */
    public async redo(): Promise<void> {
        this.undoOrRedo("redo");
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
     * Close frame (BegunTransaction.end, or doTransaction's fn settling). Closing the outermost
     * transaction makes the undo step. Ending a transaction while one begun inside it is still
     * open is a bug in the caller: the inner ones end with it, and in development it throws.
     * Does nothing if frame has already ended.
     */
    public endFrame(frame: TransactionFrame): void {
        if (frame.ended) return;
        this.processPendingRecords();
        const index = this.frames.indexOf(frame);
        const innerStillOpen = index < this.frames.length - 1;
        this.endFramesFrom(index);
        if (index === 0) this.closeOpenTransaction();
        if (innerStillOpen && import.meta.env.DEV)
            throw new Error(
                `Page history: the transaction "${frame.descriptionL10nKey}" ended while a transaction begun inside it was still open.`,
            );
    }

    /**
     * Put back every change since frame began (BegunTransaction.cancel, or doTransaction's fn
     * throwing). The transactions begun inside it end with it, without complaint, since putting
     * everything back since frame began leaves nothing of theirs to get wrong; and a doTransaction
     * whose fn threw must rethrow fn's error, not a new one. Does nothing if frame has already
     * ended.
     */
    public cancelFrame(frame: TransactionFrame): void {
        if (frame.ended) return;
        this.processPendingRecords();
        const index = this.frames.indexOf(frame);
        this.endFramesFrom(index);
        const open = this.openTransaction!;
        const records = open.records.splice(frame.firstRecordIndex);
        for (let i = records.length - 1; i >= 0; i--) reverseRecord(records[i]);
        // The rollback's own changes are not edits.
        this.discardPendingRecords();
        // The open transaction's changes may still name values that the rollback put back; they
        // have no net change now and go when the outermost transaction closes.
        if (index === 0) {
            this.openTransaction = undefined;
            this.stopObservingRemovedNodes();
        }
    }

    /** Open a transaction, nested inside the open one if there is one. */
    private openFrame(descriptionL10nKey: string): TransactionFrame {
        this.processPendingRecords();
        if (!this.openTransaction) {
            this.openTransaction = {
                descriptionL10nKey,
                changes: new Map(),
                records: [],
                selectionBefore: saveSelection(this.root.ownerDocument),
            };
        }
        const frame = new TransactionFrame(
            this,
            descriptionL10nKey,
            this.openTransaction.records.length,
        );
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
        this.endFramesFrom(0);
        this.openTransaction = undefined;
        this.stopObservingRemovedNodes();
    }

    /**
     * Make the undo step of the outermost transaction, which has just ended, from its before
     * values and the page's values now. A transaction with no net change makes no step.
     */
    private closeOpenTransaction(): void {
        const open = this.openTransaction!;
        this.openTransaction = undefined;
        this.stopObservingRemovedNodes();
        this.readAfterValues(open.changes, open.changes);
        removeEntriesWithNoNetChange(open.changes);
        if (open.changes.size === 0) return;
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
        this.observeRemovedNodes(this.observer.takeRecords());
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
     * Send one batch of records where it belongs: nowhere inside doOutsideHistory; to the open
     * transaction if there is one; otherwise to the latest step (rule 1), which clears redo,
     * warning unless inside addToLatestTransaction. With no latest step, the changes become
     * starting state. A batch with no net change changes nothing here.
     */
    private noteRecords(records: MutationRecord[]): void {
        if (records.length === 0) return;
        this.observeRemovedNodes(records);
        if (this.callContext === "doOutsideHistory") return;
        const batch = this.netChanges(records);
        if (this.openTransaction) {
            for (const record of records)
                this.openTransaction.records.push(record);
            addBeforeValues(this.openTransaction.changes, batch);
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
        if (this.callContext !== "addToLatestTransaction") {
            describeBatch(batch).forEach((whatChanged) =>
                this.onUnmarkedChange(whatChanged),
            );
        }
    }

    /**
     * The net effect of a batch of records, which must be all the records up to now, leaving out
     * what the filter ignores. The value at the start of the batch comes from the first record
     * for each attribute and text, and for a child list from working back from the children now
     * through that parent's records.
     */
    private netChanges(records: MutationRecord[]): BatchChanges {
        const firstAttributeValues = new Map<
            Element,
            Map<string, string | undefined>
        >();
        const firstTexts = new Map<Node, string>();
        const childListRecords = new Map<Node, MutationRecord[]>();
        for (const record of records) {
            if (this.isIgnored(record.target)) continue;
            switch (record.type) {
                case "attributes": {
                    const element = record.target as Element;
                    const name = record.attributeName!;
                    if (this.filter?.ignoreAttribute(element, name)) continue;
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
                    if (!firstTexts.has(record.target))
                        firstTexts.set(record.target, record.oldValue ?? "");
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
            values.forEach((before, name) => {
                if (before === attributeValue(element, name)) return;
                const entry = entryFor(element);
                if (!entry.attributes) entry.attributes = new Map();
                entry.attributes.set(name, before);
            });
        });
        firstTexts.forEach((before, node) => {
            if (before !== (node as CharacterData).data)
                entryFor(node).text = before;
        });
        childListRecords.forEach((parentRecords, parent) => {
            const before = childrenBeforeRecords(parent, parentRecords).filter(
                (child) => !this.isIgnoredChild(child),
            );
            if (!sameNodes(before, this.filteredChildren(parent)))
                entryFor(parent).children = before;
        });
        return batch;
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
                change.attributes!.get(name)!.after = attributeValue(
                    node as Element,
                    name,
                );
            });
            if (key.text !== undefined)
                change.text!.after = (node as CharacterData).data;
            if (key.children)
                change.children!.after = this.filteredChildren(node);
        });
    }

    /**
     * The nodes that moving the page from the `from` values of changes to the `to` values took
     * out of the page, each serialized: those in a `from` child list, in no `to` child list, and
     * not in the page now. A node already in earlier keeps the text from there, so that a change
     * to it made while it was detached, which the observer cannot see, still fails the check.
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
                    earlier?.get(child) ?? serializeNode(child),
                );
            }),
        );
        return detached;
    }

    /**
     * The check of rule 4: true when the page still has every `expected` value of step, and every
     * subtree that the step's last move took out is still out and serializes as it did then.
     */
    private pageHasValues(step: Step, expected: Values): boolean {
        for (const [node, change] of step.changes) {
            if (change.attributes) {
                for (const [name, values] of change.attributes) {
                    if (
                        attributeValue(node as Element, name) !==
                        values[expected]
                    )
                        return false;
                }
            }
            if (
                change.text &&
                (node as CharacterData).data !== change.text[expected]
            )
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
        for (const [node, text] of step.detachedAsText) {
            if (this.root.contains(node) || serializeNode(node) !== text)
                return false;
        }
        return true;
    }

    /** Give the page the `values` values of every entry of step. */
    private putBackValues(step: Step, values: Values): void {
        const childLists = new Map<Node, Node[]>();
        step.changes.forEach((change, node) => {
            change.attributes?.forEach((attributeValues, name) => {
                const value = attributeValues[values];
                if (value === undefined)
                    (node as Element).removeAttribute(name);
                else (node as Element).setAttribute(name, value);
            });
            if (change.text) (node as CharacterData).data = change.text[values];
            if (change.children) childLists.set(node, change.children[values]);
        });
        putBackChildLists(childLists, (child) => this.isIgnoredChild(child));
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
     * 11.8.1: the onBeforeUndoOrRedo listeners, the check, putting values back, the
     * onAfterUndoOrRedo listeners, the selection.
     */
    private undoOrRedo(direction: "undo" | "redo"): void {
        this.assertNotDisposed();
        // Undo and redo while a transaction is open do nothing. This choice is provisional: the
        // design leaves open whether they should end the transaction first instead.
        if (this.frames.length > 0) return;
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
        // Undo's own changes are not edits.
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
        restoreSelection(
            this.root.ownerDocument,
            this.root,
            isUndo ? step.selectionBefore : step.selectionAfter,
        );
    }

    /** The newest undo step, or undefined when there is none. */
    private latestStep(): Step | undefined {
        return this.undoSteps[this.undoSteps.length - 1];
    }

    /** True for a node that the filter ignores, or that is inside one, up to the root. */
    private isIgnored(node: Node): boolean {
        if (!this.filter) return false;
        for (let n: Node | null = node; n; n = n.parentNode) {
            if (this.filter.ignoreNode(n)) return true;
            if (n === this.root) return false;
        }
        return false;
    }

    /** True for a child, of a node that is not ignored, that the filter ignores. */
    private isIgnoredChild(child: Node): boolean {
        return !!this.filter && this.filter.ignoreNode(child);
    }

    /** The children of node that the filter does not ignore. */
    private filteredChildren(node: Node): Node[] {
        return Array.from(node.childNodes).filter(
            (child) => !this.isIgnoredChild(child),
        );
    }

    /** Fail fast on a call after dispose(). */
    private assertNotDisposed(): void {
        if (this.disposed)
            throw new Error("Page history: used after it was disposed.");
    }
}

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
