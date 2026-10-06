// The register of asynchronous work that must finish before the page can be saved, and the gate
// every page-content-gathering path waits on.
//
// Much of the editor changes the DOM asynchronously (sizing an image, fitting a canvas element's
// background, pasting, building a custom xmatter page); a page read while one of those is half done
// is what gets written into the book. So such work registers here for its duration (preferably via
// wrapWithRequestPageContentDelay), and every route that gathers page content waits on
// whenNoActiveDelays() first: getPageContentForSaveWhenReady in bloomEditing.ts (used by the page
// snapshot) and the off-screen book processor (captureContentForExternalProcessing). C#, which
// saves from the snapshot, is told when the register is busy (see onDelayRegisterChanged).

// Upper bound (not a fixed wait) on how long we wait for in-flight async DOM work to finish before
// gathering anyway; generous to give slow computers with complex pages headroom.
export const kMaxWaitTimeMs = 4000;

const activeDelays: string[] = [];

// Callbacks waiting for activeDelays to empty; see whenNoActiveDelays().
const delayWaiters: (() => void)[] = [];

// Told when the register goes from empty to busy (with describeBusyRegister()) and back to empty
// (with undefined). See onDelayRegisterChanged.
const registerListeners: ((busyWith: string | undefined) => void)[] = [];

// Be told when the register becomes busy and when it empties again (only those transitions; the
// listener needs to know WHETHER the page is busy). Returns a function that unsubscribes.
//
// What the busy notice says the page is busy with is only a clue for the log: it is the work
// registered at that moment, not a running record, which would cost a request per add and remove.
//
// If the register is already busy, the listener is told at once: the page snapshot subscribes
// after bootstrap(), by which time load-time work (image sizing, CKEditor attaching) has usually
// registered.
export function onDelayRegisterChanged(
    listener: (busyWith: string | undefined) => void,
): () => void {
    registerListeners.push(listener);
    if (activeDelays.length > 0) listener(describeBusyRegister());
    return () => {
        const index = registerListeners.indexOf(listener);
        if (index >= 0) registerListeners.splice(index, 1);
    };
}

// Register asynchronous work whose results belong in the saved page. The caller must pass the same
// id to removeRequestPageContentDelay when the work finishes -- see wrapWithRequestPageContentDelay,
// which does that for you. IDs do not need to be unique; the same ID can be added multiple times.
export function addRequestPageContentDelay(id: string): void {
    activeDelays.push(id);
    if (activeDelays.length === 1) {
        const busyWith = describeBusyRegister();
        registerListeners.forEach((listener) => listener(busyWith));
    }
}

function describeBusyRegister(): string {
    return activeDelays.join(", ");
}

// Deregister work, releasing anyone waiting if this was the last of it.
export function removeRequestPageContentDelay(id: string): void {
    const index = activeDelays.indexOf(id);
    if (index === -1) {
        console.error(
            `removeRequestPageContentDelay: ID "${id}" not found in active delays. Active delays: [${activeDelays.join(
                ", ",
            )}]`,
        );
        return;
    }
    activeDelays.splice(index, 1);

    if (activeDelays.length === 0) {
        // Take the list before calling anyone, so that a waiter which starts new work (and so
        // registers a new delay) does not get released a second time by that work finishing.
        delayWaiters.splice(0).forEach((release) => release());
        registerListeners.forEach((listener) => listener(undefined));
    }
}

// Run some asynchronous work with its delay registered for the duration, whether it succeeds or
// throws. Prefer this to the add/remove pair: a delay that is never removed blocks every save for
// kMaxWaitTimeMs and then gets overridden anyway.
export async function wrapWithRequestPageContentDelay<T>(
    fn: () => Promise<T>,
    delayId: string,
): Promise<T> {
    addRequestPageContentDelay(delayId);
    try {
        return await fn();
    } finally {
        removeRequestPageContentDelay(delayId);
    }
}

// Resolves once no registered work is outstanding: immediately if there is none, otherwise as soon
// as the last of it finishes, and after maxWaitMs regardless (saving a slightly stale page beats
// blocking the user forever). The off-screen capture, with nobody waiting at the keyboard, passes a
// longer cap and then checks getActiveDelayIds() to decide whether it can capture what is pending.
export function whenNoActiveDelays(
    maxWaitMs: number = kMaxWaitTimeMs,
): Promise<void> {
    if (activeDelays.length === 0) return Promise.resolve();
    return new Promise<void>((resolve) => {
        let timeout: number | undefined;
        const release = () => {
            if (timeout !== undefined) window.clearTimeout(timeout);
            resolve();
        };
        delayWaiters.push(release);
        timeout = window.setTimeout(() => {
            console.warn(
                `Waited the maximum ${maxWaitMs}ms for in-flight page changes [${activeDelays.join(
                    ", ",
                )}]. Gathering the page content anyway.`,
            );
            const index = delayWaiters.indexOf(release);
            if (index >= 0) delayWaiters.splice(index, 1);
            resolve();
        }, maxWaitMs);
    });
}

// What is currently registered. For tests, diagnostics, and the off-screen capture's decision
// about what it may capture half-done (see whenNoActiveDelays).
export function getActiveDelayIds(): string[] {
    return [...activeDelays];
}
