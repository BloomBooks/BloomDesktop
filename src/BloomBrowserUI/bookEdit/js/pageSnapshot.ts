import { postString } from "../../utils/bloomApi";
import { reportError } from "../../lib/errorHandler";

// Keep C# supplied with the current content of the page being edited, so that a save never has to
// ask for it and wait.
//
// The old arrangement was a round trip: C# wanted the page, told the browser to send it, and then
// had to have somewhere to wait until the answer arrived on a separate API call. That wait is what
// the editing state machine's SavePending state exists for, and it is why everything that has to
// save first -- leaving the Edit tab, closing the collection, a page-list command -- had to be
// split into a "before" and an "after" around an asynchronous gap.
//
// Since BL-13502 gathering the page is cheap (~0.7 ms) and does not touch the live page at all, so
// the browser can simply volunteer it: after any change that settles, post the current content.
// C# stores the string (see PageSnapshot.cs) and a save then takes it synchronously.
//
// What makes this safe to rely on is that we post only when the page's SAVED FORM has actually
// changed, so "no snapshot" on the C# side means "no unsaved changes" rather than "we have not
// been told yet". Two things are needed for that, and neither is optional:
//
// * A baseline taken once the page has finished loading. Loading is not over when bootstrap()
//   returns -- image sizing and canvas layout finish afterwards and mutate the page -- so without
//   one, every page posts a snapshot seconds after opening even if nobody touches it.
// * Comparing each gather against the last thing we sent. Tools constantly add and remove editing
//   decorations, which the gather strips anyway, so without this they produce a stream of
//   identical posts.

const kApi = "editView/pageSnapshot";

// How long the page must be quiet before we take a snapshot.
//
// This is small on purpose, and the size of it decides how much typing an exit could lose. What
// it has to buy is coalescing: measured on a real page, ONE keystroke produces about nine
// MutationObserver batches, because CKEditor does a lot of DOM work per key. 25 ms collapses those
// into a single gather, and no lower value would buy anything more -- below about 25 ms the lag is
// dominated by the POST, not by us.
//
// Measured on a 26 KB page (see Edit/SavingWithoutReloading.md):
//   gather                                    0.4 ms median (0.2 - 1.6)
//   keystroke -> C# has the content           ~49 ms  (25 debounce + gather + POST)
//   posts while typing                        one per keystroke
//
// The cost of being this eager is one POST per keystroke instead of one per pause, and one extra
// snapshot per page visit (a short debounce catches the page mid-settle as well as settled). Both
// are cheap: the gather is off the critical path at 0.4 ms, the POST goes to localhost and C#
// only stores the string, replacing the last one.
const kQuietMs = 25;

let observer: MutationObserver | undefined;
let timer: number | undefined;
let lastPosted: string | undefined;
let pageIdBeingWatched: string | undefined;
// How we read the page. Passed in by the caller rather than imported, so this module does not
// depend on bloomEditing (which depends on it, for the teardown) -- and so a test can drive it
// without a real page.
let gatherPageContent: (() => Promise<string>) | undefined;
// Until the post-load baseline is in, we do not know which of the mutations we are seeing are the
// page finishing loading and which are the user, so we hold off posting. See startWatching...
let baselineTaken = false;
// True while a gather-and-post is under way. See takeSnapshot: overlapping posts could arrive out
// of order, which would let an older snapshot overwrite a newer one on the C# side.
let busy = false;
// Bumped every time a change arrives. The async gather checks it afterwards, so a change that
// lands while we were gathering schedules another pass instead of being lost.
let changeCount = 0;
// The page we have already complained about, so that a page which fails every time reports once
// rather than on every keystroke.
let pageWeReportedAFailureFor: string | undefined;

function currentPageId(): string | undefined {
    return document.querySelector(".bloom-page")?.id || undefined;
}

async function takeSnapshot(): Promise<void> {
    const pageId = pageIdBeingWatched;
    if (!pageId || !gatherPageContent) return;
    if (!baselineTaken) {
        // The page is still finishing loading. Come back once we know what "unchanged" looks like.
        scheduleSnapshot();
        return;
    }
    // Only ever one gather-and-post at a time.
    //
    // Two would be a correctness bug, not just waste: HTTP does not promise that two outstanding
    // POSTs arrive in the order they were sent, so an OLDER snapshot could land after a newer one
    // and C# would keep the older content -- silently dropping the newest edits. It takes a slow
    // enough machine, or a big enough page, for a post to still be in flight when the next
    // keystroke's snapshot comes round, which is exactly the case this has to survive.
    //
    // Returning here loses nothing: the run that is already going re-schedules if anything changed
    // while it worked, and it reads changeCount after it finishes, so it sees those changes. The
    // effect on a slow machine is that snapshots coalesce by themselves rather than piling up.
    if (busy) return;
    busy = true;
    const countWhenStarted = changeCount;
    try {
        // Waits for any in-flight work that belongs in the page (see pageContentDelays), then
        // reads the page the same way a real save does, so a snapshot can never differ from what
        // a save would have produced at the same moment.
        const content = await gatherPageContent();

        // The page may have been unloaded, or navigated, while we were waiting.
        if (pageIdBeingWatched !== pageId) return;

        if (content !== lastPosted) {
            await postString(
                `${kApi}?pageId=${encodeURIComponent(pageId)}`,
                content,
            );
            // Only once the post has actually resolved. Recording it before would mean that a
            // post which failed still counted as sent: we would never retry it, and C# would go
            // on holding the content from before the failure -- so the next save would write
            // that, losing everything typed since, not merely the latest keystroke.
            lastPosted = content;
        }
    } catch (error) {
        // Gathering the page can legitimately throw -- the BL-13120 origami guard, a missing
        // marginBox, the canvas-element count checks -- and so can the post. Either way this is
        // the one failure the whole design cannot afford to be quiet about: C# concludes "no
        // snapshot, so nothing to save", and the user's edits are dropped without a word. (The
        // global unhandledrejection handler is commented out in lib/errorHandler.ts, so nothing
        // else would report it.) Before BL-13502 the equivalent failure came back through the
        // state machine as "Bloom had trouble saving a page"; this keeps that promise.
        //
        // Once per page: a page that fails will fail again on the very next keystroke.
        if (pageWeReportedAFailureFor !== pageId) {
            pageWeReportedAFailureFor = pageId;
            reportError(
                "Bloom could not keep track of your changes to this page: " +
                    (error instanceof Error ? error.message : String(error)),
                error instanceof Error ? error.stack : undefined,
            );
        }
        // Try again on the next change: a transient failure should not stop us for good.
        scheduleSnapshot();
    } finally {
        busy = false;
    }
    // Something changed while we were gathering or posting: that change is not in what we just
    // sent, so go round again.
    if (changeCount !== countWhenStarted) scheduleSnapshot();
}

function scheduleSnapshot(): void {
    if (timer !== undefined) window.clearTimeout(timer);
    timer = window.setTimeout(() => {
        timer = undefined;
        void takeSnapshot();
    }, kQuietMs);
}

function noteChange(): void {
    changeCount++;
    scheduleSnapshot();
}

/**
 * Start watching the page that has just become editable. Safe to call again; it restarts on the
 * new page.
 */
export function startWatchingPageForSnapshots(
    gather: () => Promise<string>,
): void {
    stopWatchingPageForSnapshots();
    const pageId = currentPageId();
    if (!pageId) return; // no page to watch (e.g. the off-screen capture path)
    gatherPageContent = gather;
    pageIdBeingWatched = pageId;
    lastPosted = undefined;
    changeCount = 0;
    baselineTaken = false;
    pageWeReportedAFailureFor = undefined;

    // Take a baseline of the page as it ends up once it has finished loading, and treat that as
    // "already sent". Without it every page posts a snapshot within a second of being opened, even
    // if the user never touches it -- because loading is not finished when bootstrap() returns.
    // Image sizing and canvas-element layout complete asynchronously afterwards and mutate the
    // page, and the observer cannot tell those from the user's own edits.
    //
    // That mattered: it broke the property C# depends on, that no snapshot means no unsaved
    // changes. (Found by watching the real app: a page nobody had touched posted one anyway.)
    //
    // The gather waits for the delay register -- but that is NOT enough to make this the settled
    // page, and measurement says so: the baseline still differs from the settled content, and the
    // one snapshot an untouched page posts is byte-identical to the settled page. At the moment
    // we run, the asynchronous fix-ups have not registered their delays yet, so the register is
    // empty and the gather returns immediately.
    //
    // Deliberately NOT "fixed" by delaying the baseline until the page is quiet. That would make
    // the baseline include any edit the user managed in the meantime, and since load-time
    // settling is indistinguishable from typing, we would then have no way to tell we owed C# a
    // snapshot of it -- trading a harmless duplicate for a lost edit. One post per page visit,
    // carrying exactly what a save would have written, is the better end of that trade.
    void gather().then(
        (baseline) => {
            if (pageIdBeingWatched !== pageId) return; // moved on while we waited
            lastPosted = baseline;
            baselineTaken = true;
            // If the page changed while we were taking the baseline, that change may or may not
            // be in it; go round again rather than assume.
            if (changeCount > 0) scheduleSnapshot();
        },
        () => {
            if (pageIdBeingWatched !== pageId) return;
            // We could not read the page. Fail towards reporting too much rather than too little:
            // an extra snapshot costs a redundant save, a missing one costs the user's typing.
            lastPosted = undefined;
            baselineTaken = true;
            scheduleSnapshot();
        },
    );

    // A MutationObserver rather than input/keyup handlers, because plenty of what changes a page
    // never goes through a keyboard event: a tool rewriting the markup, a canvas element being
    // dragged, an image being replaced, a paste. Anything that changes the DOM is a change we owe
    // C# a snapshot of.
    observer = new MutationObserver(noteChange);
    observer.observe(document.body, {
        subtree: true,
        childList: true,
        characterData: true,
        attributes: true,
    });
}

/**
 * Stop watching, and forget what we last sent. Called from pageUnloading().
 */
export function stopWatchingPageForSnapshots(): void {
    observer?.disconnect();
    observer = undefined;
    if (timer !== undefined) {
        window.clearTimeout(timer);
        timer = undefined;
    }
    pageIdBeingWatched = undefined;
    lastPosted = undefined;
    gatherPageContent = undefined;
    baselineTaken = false;
    busy = false;
}

/**
 * Exported for tests: the interval the page must be quiet before a snapshot is taken.
 */
export const quietMsForTests = kQuietMs;
