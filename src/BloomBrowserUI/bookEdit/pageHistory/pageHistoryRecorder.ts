// What the Show Page History dialog shows: a record of what the page history has done, kept in the
// root window, which outlives the page frames. Each page load makes a new page frame and a new
// history (pageHistorySetup.ts); each sends its events here, through the workspace bundle's
// pageHistoryRecorder, so the record runs on across page changes. Steps, undos, redos, refused
// undos and redos, clears and the emptying of redo are kept at all times, so that opening the dialog after a problem
// still shows them. The rest (rollbacks, page loads, the page becoming ready, every transaction and
// every change that the history sees) is recorded only while the dialog is open, which turns
// detailed logging on.
//
// It also keeps the current page's stack of undo and redo steps, as the page frame last sent it
// (PageHistoryStack), for the dialog's Stack tab.
//
// Only strings, numbers and booleans are kept here, copied into this window's own objects, so the
// record holds nothing of a page frame that has gone.

import type { PageHistoryLogKind } from "./pageHistoryTypes";

/**
 * What an event is: one of the kinds that the page history logs (PageHistoryLogKind), of which
 * "rollback" is recorded only while logging is detailed, or
 * - pageLoad: a page frame started a history (detailed only);
 * - pageReady: the history was marked ready (markPageReady) (detailed only);
 * - transaction: a transaction opened, ended, waited or was cancelled (detailed only);
 * - input: a click or key press opened a transaction (detailed only);
 * - change: a record that the history's observer saw (detailed only); those of a rollback are
 *   "rollback" instead, so they stand out.
 */
export type PageHistoryEventKind =
    | PageHistoryLogKind
    | "pageLoad"
    | "pageReady"
    | "transaction"
    | "input"
    | "change";

export interface PageHistoryEvent {
    /** Increases by one for each event, for React keys. */
    readonly id: number;
    /** Seconds since the page that the event belongs to loaded. */
    readonly seconds: number;
    readonly kind: PageHistoryEventKind;
    readonly text: string;
}

/** How many events the recorder keeps; the oldest go first. */
export const kMaxPageHistoryEvents = 20000;

/**
 * The undo history of the page open now, for the dialog's Stack tab, as the page frame sends it
 * (PageHistoryRecorder.setStack).
 */
export interface PageHistoryStack {
    /** The descriptions of the undo steps, oldest first: the last is the one Undo would undo. */
    readonly undo: readonly string[];
    /** The descriptions of the redo steps: the first is the one Redo would redo. */
    readonly redo: readonly string[];
    /**
     * True while a transaction is open: a step in progress, such as typing, which stays open
     * between key presses and becomes an undo step only when something ends it.
     */
    readonly hasOpenTransaction: boolean;
    /** True while the page is in Change Layout mode, where Undo and Redo use origami's own undo. */
    readonly isInChangeLayoutMode: boolean;
}

/** The label that each kind of event shows, in the dialog and in the copied text. */
export const kPageHistoryEventLabels: Record<PageHistoryEventKind, string> = {
    pageLoad: "PAGE",
    pageReady: "READY",
    step: "STEP",
    undo: "UNDO",
    redo: "REDO",
    refused: "REFUSED",
    cleared: "CLEARED",
    redoDropped: "REDO DROPPED",
    rollback: "ROLLBACK",
    transaction: "TXN",
    input: "INPUT",
    change: "CHANGE",
};

const kLogLinePrefix = "Page history: ";

export class PageHistoryRecorder {
    private events: PageHistoryEvent[] = [];
    private nextId = 1;
    private pageLoadTime: number;
    private detailed = false;
    // Counts the page loads, so that a page frame that has gone cannot set or remove the stack of
    // the page that came after it.
    private pageLoadNumber = 0;
    // False once the page of the latest page load has gone.
    private isPageOpen = false;
    private stack: PageHistoryStack | undefined;
    // The latest "cleared" event since the latest page load.
    private latestClear: PageHistoryEvent | undefined;
    private changeCount = 0;
    private readonly listeners = new Set<() => void>();
    private readonly detailedListeners = new Set<(detailed: boolean) => void>();

    /** now gives the time in milliseconds; tests supply their own. */
    public constructor(
        private readonly now: () => number = () => performance.now(),
    ) {
        this.pageLoadTime = now();
    }

    /** Record an event, timed from the latest page load. */
    public add(kind: PageHistoryEventKind, text: string): void {
        this.keep(kind, text);
        this.changed();
    }

    /** Keep an event, timed from the latest page load, without telling the listeners. */
    private keep(kind: PageHistoryEventKind, text: string): PageHistoryEvent {
        const event: PageHistoryEvent = {
            id: this.nextId++,
            seconds: (this.now() - this.pageLoadTime) / 1000,
            kind,
            text,
        };
        this.events.push(event);
        if (this.events.length > kMaxPageHistoryEvents)
            this.events.splice(0, this.events.length - kMaxPageHistoryEvents);
        return event;
    }

    /**
     * Record a line that the page history logged, without its "Page history: " prefix. A
     * rollback is recorded only while logging is detailed. A clear is also kept as the latest
     * clear (getLatestClear).
     */
    public addLogLine(line: string, kind: PageHistoryLogKind): void {
        if (kind === "rollback" && !this.detailed) return;
        const event = this.keep(
            kind,
            line.startsWith(kLogLinePrefix)
                ? line.substring(kLogLinePrefix.length)
                : line,
        );
        if (kind === "cleared") this.latestClear = event;
        this.changed();
    }

    /**
     * The event of the latest clear of the history since the latest page load, whose text says
     * what cleared it, or undefined when there has been none. The log's Clear button leaves it.
     */
    public getLatestClear(): PageHistoryEvent | undefined {
        return this.latestClear;
    }

    /**
     * Note that a page frame started a history for the page with this id: times start again, a
     * row says so while logging is detailed, there is no stack until the page frame sends one,
     * and there has been no clear. Returns the number of this page load, which the page frame
     * passes to setStack and notePageGone.
     */
    public notePageLoad(pageId: string): number {
        this.pageLoadTime = this.now();
        this.pageLoadNumber++;
        this.isPageOpen = true;
        this.stack = undefined;
        this.latestClear = undefined;
        if (this.detailed) this.add("pageLoad", `Page loaded: ${pageId}`);
        else this.changed();
        return this.pageLoadNumber;
    }

    /**
     * Keep a copy of the stack of undo and redo steps of the page loaded as pageLoadNumber
     * (notePageLoad), unless another page has loaded since. Copies steps into this window's own
     * objects, since steps belongs to the page frame.
     */
    public setStack(pageLoadNumber: number, steps: PageHistoryStack): void {
        if (pageLoadNumber !== this.pageLoadNumber || !this.isPageOpen) return;
        const stack: PageHistoryStack = {
            undo: Array.from(steps.undo),
            redo: Array.from(steps.redo),
            hasOpenTransaction: steps.hasOpenTransaction,
            isInChangeLayoutMode: steps.isInChangeLayoutMode,
        };
        if (this.stack && areStacksEqual(this.stack, stack)) return;
        this.stack = stack;
        this.changed();
    }

    /**
     * Note that the page loaded as pageLoadNumber has gone, so that there is no stack, unless
     * another page has loaded since.
     */
    public notePageGone(pageLoadNumber: number): void {
        if (pageLoadNumber !== this.pageLoadNumber) return;
        this.isPageOpen = false;
        this.stack = undefined;
        this.changed();
    }

    /** The stack of the page open now, or undefined when there is no page or it has sent none. */
    public getStack(): PageHistoryStack | undefined {
        return this.stack;
    }

    /** Record, while logging is detailed, that the history was marked ready. */
    public notePageReady(): void {
        if (this.detailed) this.add("pageReady", "Page ready (markPageReady)");
    }

    /** True while the dialog is open, which asks for detailed logging. */
    public get isDetailed(): boolean {
        return this.detailed;
    }

    /**
     * Turn detailed logging on or off, telling each history that listens (onDetailedChange). The
     * dialog turns it on when it opens and off when it closes.
     */
    public setDetailed(detailed: boolean): void {
        if (detailed === this.detailed) return;
        this.detailed = detailed;
        Array.from(this.detailedListeners).forEach((listener) =>
            listener(detailed),
        );
        this.changed();
    }

    /**
     * Call listener now with whether detailed logging is on, and again whenever that changes.
     * Returns an unsubscribe, which a page frame must call when its page goes.
     */
    public onDetailedChange(listener: (detailed: boolean) => void): () => void {
        this.detailedListeners.add(listener);
        listener(this.detailed);
        return () => this.detailedListeners.delete(listener);
    }

    /** The events kept, oldest first. Do not change the array. */
    public getEvents(): readonly PageHistoryEvent[] {
        return this.events;
    }

    /** Forget every event. */
    public clear(): void {
        this.events = [];
        this.changed();
    }

    /**
     * A number that changes whenever the events, the stack or isDetailed change, for React's
     * useSyncExternalStore.
     */
    public getChangeCount(): number {
        return this.changeCount;
    }

    /**
     * Call listener after every change to the events, the stack or isDetailed. Returns an
     * unsubscribe.
     */
    public subscribe(listener: () => void): () => void {
        this.listeners.add(listener);
        return () => this.listeners.delete(listener);
    }

    /** Every event as plain text, one per line, as the Log tab's Copy button copies them. */
    public asText(): string {
        return this.events.map(formatPageHistoryEvent).join("\n");
    }

    /** Count the change and tell the listeners. */
    private changed(): void {
        this.changeCount++;
        Array.from(this.listeners).forEach((listener) => listener());
    }
}

/** An event's time, to the millisecond, formatted the same on every machine. */
export function formatPageHistoryEventTime(event: PageHistoryEvent): string {
    // toFixed always uses "." whatever the user's culture.
    return event.seconds.toFixed(3);
}

/** One event as one line of plain text: time, label, text. */
export function formatPageHistoryEvent(event: PageHistoryEvent): string {
    return `${formatPageHistoryEventTime(event)}\t${kPageHistoryEventLabels[event.kind]}\t${event.text}`;
}

/** What the Stack tab shows between the undo steps and the redo steps. */
export const kPageHistoryStackMarker = "you are here";
/** What the Stack tab shows just above the marker while a transaction is open. */
export const kPageHistoryStepInProgress = "(a step is in progress)";
/** What the Stack tab shows in Change Layout mode. */
export const kPageHistoryChangeLayoutModeNote =
    "In Change Layout mode, Undo and Redo use the mode's own undo, which is not shown here.";
/** What the Stack tab shows when there is no page history. */
export const kPageHistoryNoStack = "(no page history)";

/**
 * The Stack tab's line for the latest clear (getLatestClear): when, and what cleared the history,
 * e.g. "History cleared at 12.345 s by request editview/settopic".
 */
export function formatPageHistoryClear(clear: PageHistoryEvent): string {
    const what = clear.text.startsWith("cleared ")
        ? clear.text.substring("cleared ".length)
        : clear.text;
    return `History cleared at ${formatPageHistoryEventTime(clear)} s ${what}`;
}

/**
 * A stack as plain text, as the Stack tab's Copy button copies it: the latest clear if
 * there has been one, then one line per step, the undo steps oldest first, the step in progress
 * if there is one, the marker, and the redo steps, the one that Redo would redo first.
 */
export function formatPageHistoryStack(
    stack: PageHistoryStack | undefined,
    latestClear?: PageHistoryEvent,
): string {
    const clearLines = latestClear ? [formatPageHistoryClear(latestClear)] : [];
    if (!stack) return [...clearLines, kPageHistoryNoStack].join("\n");
    const lines = [...clearLines];
    if (stack.isInChangeLayoutMode)
        lines.push(kPageHistoryChangeLayoutModeNote);
    stack.undo.forEach((step) => lines.push(`UNDO\t${step}`));
    if (stack.hasOpenTransaction) lines.push(`\t${kPageHistoryStepInProgress}`);
    lines.push(`-------- ${kPageHistoryStackMarker} --------`);
    stack.redo.forEach((step) => lines.push(`REDO\t${step}`));
    return lines.join("\n");
}

/** True when two stacks show the same. */
function areStacksEqual(a: PageHistoryStack, b: PageHistoryStack): boolean {
    return (
        a.hasOpenTransaction === b.hasOpenTransaction &&
        a.isInChangeLayoutMode === b.isInChangeLayoutMode &&
        areListsEqual(a.undo, b.undo) &&
        areListsEqual(a.redo, b.redo)
    );
}

/** True when two lists hold the same strings in the same order. */
function areListsEqual(a: readonly string[], b: readonly string[]): boolean {
    return a.length === b.length && a.every((item, i) => item === b[i]);
}

/**
 * The recorder of the window that loaded this module. Only the root window's matters: the page
 * frame reaches it through the workspace bundle (workspaceRoot.ts), and the dialog in the root
 * window imports it.
 */
export const pageHistoryRecorder = new PageHistoryRecorder();
