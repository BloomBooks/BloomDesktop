// What the Show Page History dialog shows: a record of what the page history has done, kept in the
// root window, which outlives the page frames. Each page load makes a new page frame and a new
// history (pageHistorySetup.ts); each sends its events here, through the workspace bundle's
// pageHistoryRecorder, so the record runs on across page changes. Steps, undos, redos, refused
// undos and redos, and clears are kept at all times, so that opening the dialog after a problem
// still shows them. The rest (rollbacks, page loads, the page becoming ready, every transaction and
// every change that the history sees) is recorded only while the dialog's "Detailed logging" box
// is checked.
//
// Only strings and numbers come in here, so the record holds nothing of a page frame that has
// gone.

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
export const kMaxPageHistoryEvents = 2000;

/** The label that each kind of event shows, in the dialog and in the copied text. */
export const kPageHistoryEventLabels: Record<PageHistoryEventKind, string> = {
    pageLoad: "PAGE",
    pageReady: "READY",
    step: "STEP",
    undo: "UNDO",
    redo: "REDO",
    refused: "REFUSED",
    cleared: "CLEARED",
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
        this.events.push({
            id: this.nextId++,
            seconds: (this.now() - this.pageLoadTime) / 1000,
            kind,
            text,
        });
        if (this.events.length > kMaxPageHistoryEvents)
            this.events.splice(0, this.events.length - kMaxPageHistoryEvents);
        this.changed();
    }

    /**
     * Record a line that the page history logged, without its "Page history: " prefix. A
     * rollback is recorded only while logging is detailed.
     */
    public addLogLine(line: string, kind: PageHistoryLogKind): void {
        if (kind === "rollback" && !this.detailed) return;
        this.add(
            kind,
            line.startsWith(kLogLinePrefix)
                ? line.substring(kLogLinePrefix.length)
                : line,
        );
    }

    /**
     * Note that a page frame started a history for the page with this id: times start again, and
     * a row says so while logging is detailed.
     */
    public notePageLoad(pageId: string): void {
        this.pageLoadTime = this.now();
        if (this.detailed) this.add("pageLoad", `Page loaded: ${pageId}`);
    }

    /** Record, while logging is detailed, that the history was marked ready. */
    public notePageReady(): void {
        if (this.detailed) this.add("pageReady", "Page ready (markPageReady)");
    }

    /** True while the dialog asks for detailed logging. */
    public get isDetailed(): boolean {
        return this.detailed;
    }

    /** Turn detailed logging on or off, telling each history that listens (onDetailedChange). */
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
     * A number that changes whenever the events or isDetailed change, for React's
     * useSyncExternalStore.
     */
    public getChangeCount(): number {
        return this.changeCount;
    }

    /** Call listener after every change to the events or to isDetailed. Returns an unsubscribe. */
    public subscribe(listener: () => void): () => void {
        this.listeners.add(listener);
        return () => this.listeners.delete(listener);
    }

    /** Every event as plain text, one per line, as the dialog's Copy button copies them. */
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

/**
 * The recorder of the window that loaded this module. Only the root window's matters: the page
 * frame reaches it through the workspace bundle (workspaceRoot.ts), and the dialog in the root
 * window imports it.
 */
export const pageHistoryRecorder = new PageHistoryRecorder();
