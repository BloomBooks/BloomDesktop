// The register of asynchronous work that must finish before the page can be saved, and the gate
// every page-content-gathering path waits on.
//
// The problem it solves: saving means reading the page's DOM, and quite a lot of the editor changes
// that DOM asynchronously -- sizing an image, fitting a canvas element's background, pasting from
// the clipboard, building a custom xmatter page. Read the page while one of those is half done and
// that is what gets written into the user's book.
//
// So any code doing such work registers here for its duration (preferably via
// wrapWithRequestPageContentDelay, which cannot forget to deregister), and every route that gathers
// page content goes through whenNoActiveDelays() first:
//   - the C#-initiated save (requestPageContent in bloomEditing.ts). This is the route the register
//     really exists for: C# picks the moment, so in-flight work has no other way to hold it off.
//   - the browser-initiated ones (getPageContentForSaveWhenReady, used by savePageWithoutReloading
//     and by the page list's commands, via collectCurrentPageContent). Javascript could in
//     principle await its own work instead, but it cannot know about work someone else started, so
//     it waits here too. That also means the *command* does not begin -- C# is not asked to
//     duplicate or delete a page until the page has settled.
//   - the off-screen book processor (captureContentForExternalProcessing).

// Upper bound (not a fixed wait) on how long we wait for in-flight async DOM work to finish before
// gathering anyway. The wait ends as soon as the register empties, so simple pages are unaffected
// by this value; it only gives slower computers with complex pages more headroom before we give up.
export const kMaxWaitTimeMs = 4000;

const activeDelays: string[] = [];

// Callbacks waiting for activeDelays to empty; see whenNoActiveDelays().
const delayWaiters: (() => void)[] = [];

// Register asynchronous work whose results belong in the saved page. The caller must pass the same
// id to removeRequestPageContentDelay when the work finishes -- see wrapWithRequestPageContentDelay,
// which does that for you. IDs do not need to be unique; the same ID can be added multiple times.
export function addRequestPageContentDelay(id: string): void {
    activeDelays.push(id);
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
// as the last of it finishes, and after kMaxWaitTimeMs regardless -- saving a slightly stale page
// beats not saving at all, so we warn and go on rather than block the user forever.
export function whenNoActiveDelays(): Promise<void> {
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
                `Waited the maximum ${kMaxWaitTimeMs}ms for in-flight page changes [${activeDelays.join(
                    ", ",
                )}]. Gathering the page content anyway.`,
            );
            const index = delayWaiters.indexOf(release);
            if (index >= 0) delayWaiters.splice(index, 1);
            resolve();
        }, kMaxWaitTimeMs);
    });
}

// For tests and diagnostics only: what is currently registered.
export function getActiveDelayIdsForTesting(): string[] {
    return [...activeDelays];
}
