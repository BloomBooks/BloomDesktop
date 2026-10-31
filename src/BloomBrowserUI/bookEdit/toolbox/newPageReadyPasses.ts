// What the page frame waits for before it starts its undo history: the running tool's two
// newPageReady passes for the page. useToolLifecycle.ts runs the passes and says when they have
// run; the page frame asks, through the toolbox bundle's exports, when they will have.
import { getTheOneToolbox } from "./toolbox";
import { isToolOffered } from "./toolboxState";

// The page frame starts its undo history only after the tools' newPageReady passes for its page
// have run, so that what they change is part of the page as the user found it (whenPageIsReady
// in pageHistory/pageReadiness.ts). These are the page documents whose second pass has run, and
// the page frames waiting for theirs.
const pagesWhoseNewPageReadyPassesHaveRun = new WeakSet<Document>();
const waitersForNewPageReadyPasses: {
    pageDocument: Document;
    resolve: () => void;
}[] = [];

// How long a page frame waits for passes that the toolbox expected to run but that never came: a
// tool switched while its page was loading, for instance, abandons them.
const kMaxWaitForNewPageReadyPassesMs = 3000;

/**
 * Says that the running tool's newPageReady passes for the page in pageDocument have run, or
 * will not run. useToolLifecycle.ts calls it once the second pass has finished, or once the tool
 * has stopped running before that pass.
 */
export function noteNewPageReadyPassesHaveRun(
    pageDocument: Document | undefined,
): void {
    if (!pageDocument) return;
    pagesWhoseNewPageReadyPassesHaveRun.add(pageDocument);
    for (let i = waitersForNewPageReadyPasses.length - 1; i >= 0; i--) {
        const waiter = waitersForNewPageReadyPasses[i];
        if (waiter.pageDocument !== pageDocument) continue;
        waitersForNewPageReadyPasses.splice(i, 1);
        waiter.resolve();
    }
}

/**
 * Resolves once the running tool has run its two newPageReady passes for the page in
 * pageDocument: at once if it has, or if there is no tool that will (the toolbox is hidden, or no
 * tool is running), and otherwise when the second pass has finished, or after
 * kMaxWaitForNewPageReadyPassesMs if it never comes. Called from the page frame.
 */
export function whenNewPageReadyPassesHaveRun(
    pageDocument: Document,
): Promise<void> {
    if (pagesWhoseNewPageReadyPassesHaveRun.has(pageDocument))
        return Promise.resolve();
    const runningTool = getTheOneToolbox().getRunningTool();
    if (!runningTool || !isToolOffered(runningTool.id()))
        return Promise.resolve();
    return new Promise((resolve) => {
        const waiter = { pageDocument, resolve };
        waitersForNewPageReadyPasses.push(waiter);
        window.setTimeout(() => {
            const index = waitersForNewPageReadyPasses.indexOf(waiter);
            if (index < 0) return;
            waitersForNewPageReadyPasses.splice(index, 1);
            resolve();
        }, kMaxWaitForNewPageReadyPassesMs);
    });
}
