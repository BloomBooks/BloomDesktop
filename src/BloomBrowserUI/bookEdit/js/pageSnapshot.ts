import { postStringQuietly } from "../../utils/bloomApi";
import { reportError } from "../../lib/errorHandler";
import { onDelayRegisterChanged } from "./pageContentDelays";

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
// Where we tell C# that asynchronous work belonging in the saved page has begun (the body names
// it) and that it has finished. See tellCSharpBusy / tellCSharpIdle.
const kBusyApi = "editView/pageBusy";
const kIdleApi = "editView/pageIdle";

// Identifies THIS load of THIS page, so C# can tell our snapshots from those of a load it has
// already moved on from. A module-level constant is exactly the right scope: the page frame gets a
// fresh document, and so a fresh module, on every page load.
//
// It exists because the snapshot endpoint is deliberately unsynchronised (a keystroke has no
// business queueing behind a save), so a post sent moments before a navigation can be processed
// after C# has cleared the snapshot for it. Moving to a DIFFERENT page was harmless -- the stale
// entry is filed under a page id nobody asks about again -- but reloading the SAME page is not:
// Change Layout, importing a video and changing the topic all rebuild the page under its own id,
// and a snapshot of the pre-reload page would then be merged over what the reload built.
const pageLoadId =
    Date.now().toString(36) + "-" + Math.random().toString(36).slice(2, 10);

/**
 * Identifies this load of this page. Sent with the "page is ready" notification and with every
 * snapshot, so C# can ignore anything from a load it has superseded.
 */
export function getPageLoadId(): string {
    return pageLoadId;
}

// How long the page must be quiet before we take a snapshot.
//
// This is small on purpose, and the size of it decides how much typing an exit could lose. What
// it has to buy is coalescing: ONE keystroke produces about nine MutationObserver batches, because
// CKEditor does a lot of DOM work per key. 25 ms collapses those into a single gather, and no lower
// value would buy anything more -- below about 25 ms the lag is dominated by the POST, not by us.
//
// The cost of being this eager is one POST per keystroke instead of one per pause, and one extra
// snapshot per page visit (a short debounce catches the page mid-settle as well as settled). Both
// are cheap: the gather takes well under a millisecond, the POST goes to localhost and C# only
// stores the string, replacing the last one. Measurements are in Edit/SavingWithoutReloading.md.
const kQuietMs = 25;

// How long to wait before offering the content again when C# did not take it. Longer than the
// debounce on purpose: nothing the user did causes these, so there is nothing to be responsive to,
// and a 25ms retry against a server that is not answering would be a busy loop. A real change
// reschedules at kQuietMs and so overtakes this.
const kRetryAfterRefusalMs = 1000;

// A run of failures backs off from kRetryAfterRefusalMs up to this. We never give up: the browser
// holding content C# has not got is exactly the state that loses the user's work at exit, so it
// has to keep offering until something takes it. What made giving up look attractive was the
// noise, and that is dealt with separately -- the post is made quietly and we report once per
// page, rather than once per attempt.
const kMaxRetryMs = 30000;

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
// Settles when the gather-and-post under way finishes; see tellCSharpIdle, which has to let the
// post go out before it says the page is idle.
let runDone: Promise<void> = Promise.resolve();
// What the delay register says the page is busy with, or undefined when it is empty. Kept so that
// a refused busy notice is offered again only while it is still true.
let busyWith: string | undefined;
let unsubscribeFromDelayRegister: (() => void) | undefined;
// Bumped every time a change arrives. The async gather checks it afterwards, so a change that
// lands while we were gathering schedules another pass instead of being lost.
let changeCount = 0;
// The page we have already complained about, so that a page which fails every time reports once
// rather than on every keystroke.
let pageWeReportedAFailureFor: string | undefined;
// How many posts in a row have failed outright. Governs the backoff.
let consecutiveFailedPosts = 0;
// Tell the user, at most once for this page. Reporting is the whole reason a snapshot post is
// made quietly (see postStringQuietly): so that WE decide when to speak, rather than the request
// layer speaking on every attempt.
function reportFailureOncePerPage(
    pageId: string,
    message: string,
    stack: string | undefined,
): void {
    if (pageWeReportedAFailureFor === pageId) return;
    pageWeReportedAFailureFor = pageId;
    reportError(message, stack);
}

// Offer the content again after a failure, backing off 1s, 2s, 4s... to kMaxRetryMs and then
// staying there. Never gives up: while C# has not got this content, quitting writes what it still
// holds. A change the user makes reschedules at kQuietMs and overtakes this.
function retryAfterFailure(): void {
    consecutiveFailedPosts++;
    scheduleSnapshot(
        Math.min(
            kRetryAfterRefusalMs * Math.pow(2, consecutiveFailedPosts - 1),
            kMaxRetryMs,
        ),
    );
}

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
    let markRunDone: () => void = () => {};
    runDone = new Promise<void>((resolve) => {
        markRunDone = resolve;
    });
    const countWhenStarted = changeCount;
    try {
        // Waits for any in-flight work that belongs in the page (see pageContentDelays), then
        // reads the page the same way a real save does, so a snapshot can never differ from what
        // a save would have produced at the same moment.
        const content = await gatherPageContent();

        // The page may have been unloaded, or navigated, while we were waiting.
        if (pageIdBeingWatched !== pageId) return;

        if (content !== lastPosted) {
            const reply = await postStringQuietly(
                snapshotUrl(kApi, pageId),
                content,
            );
            // Two different things can mean C# does not have this content, and both must count as
            // NOT sent:
            //
            // * C# refused it, answering false. It refuses a snapshot from a load it is not
            //   showing, including in the moment before this page has reported itself ready, since
            //   the two APIs are not ordered with respect to each other. A refusal is not a
            //   failure; it just means try again.
            // * The POST failed and we got no answer at all. The post goes through wrapAxios,
            //   which turns a rejected request into a resolved promise carrying nothing -- so a
            //   failed post looks exactly like a successful one apart from the missing response.
            //   Reading only `.data` would therefore take a failure for an acceptance, record the
            //   content as sent, and never offer it again; the next save would write what C# still
            //   held, losing everything typed since the snapshot before.
            //
            // They are retried differently. A refusal costs nothing and ends by itself the moment
            // the page reports ready, so we simply keep offering. A failure means the server is
            // not answering, so that one backs off (and is reported once, see
            // reportFailureOncePerPage, rather than on every attempt).
            const response = reply as { data?: boolean | string } | void;
            const refused = !!response && response.data === false;
            const failed = !response;
            if (refused) {
                consecutiveFailedPosts = 0;
                scheduleSnapshot(kRetryAfterRefusalMs);
                return;
            }
            if (failed) {
                reportFailureOncePerPage(
                    pageId,
                    "Bloom could not keep track of your changes to this page: the request to save them did not get through.",
                    undefined,
                );
                retryAfterFailure();
                return;
            }
            consecutiveFailedPosts = 0;
            // Only once the post has actually resolved AND been taken. Recording it earlier would
            // mean content C# never received still counted as sent: we would never retry it, and
            // the next save would write what C# still held, losing everything typed since.
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
        // Once per page: a page that fails will fail again on the very next keystroke, and we
        // also retry on a timer, so without this the same error would be put in front of the user
        // over and over.
        reportFailureOncePerPage(
            pageId,
            "Bloom could not keep track of your changes to this page: " +
                (error instanceof Error ? error.message : String(error)),
            error instanceof Error ? error.stack : undefined,
        );
        // Keep offering, on the same backoff as a failed post. A gather is deterministic, so this
        // will usually fail the same way -- but it costs no further reports now, and if the
        // failure did depend on something transient in the page, this is what recovers from it.
        retryAfterFailure();
    } finally {
        // Only release the lock if we are still the run that took it. If the page was unloaded
        // and another started while we were awaiting, this run belongs to the old page, and
        // clearing the flag here would unlock the NEW page's in-flight post -- allowing two at
        // once, which is the one thing the flag exists to prevent. Not reachable today, because
        // each page load is a fresh document with its own module state, but the module claims to
        // be safe to restart and this is what makes that true.
        if (pageIdBeingWatched === pageId) busy = false;
        markRunDone();
    }
    // Something changed while we were gathering or posting: that change is not in what we just
    // sent, so go round again.
    if (changeCount !== countWhenStarted) scheduleSnapshot();
}

// The delay register (pageContentDelays.ts) has gone busy or idle. C# needs to know, because a
// save it makes from the snapshot -- leaving the Edit tab, quitting, a command from a separate
// dialog -- cannot wait for the register the way a gather here does: the snapshot it holds simply
// predates the work. So we tell it what the page is busy with, and it waits a bounded time for us
// to say the page is idle again (PageSnapshot.WaitUntilIdle), logging the culprit if we do not.
function handleDelayRegisterChange(nowBusyWith: string | undefined): void {
    const pageId = pageIdBeingWatched;
    if (!pageId) return;
    busyWith = nowBusyWith;
    if (nowBusyWith !== undefined) void tellCSharpBusy(pageId, nowBusyWith);
    else void tellCSharpIdle(pageId);
}

function snapshotUrl(api: string, pageId: string): string {
    return `${api}?pageId=${encodeURIComponent(pageId)}&loadId=${encodeURIComponent(
        pageLoadId,
    )}`;
}

// C# refuses a notice about a load it is not showing, exactly as it refuses such a snapshot, and
// for the same reason the refusal must not be the end of it: this page may simply not have
// reported itself ready yet. Offer it again while the work is still going.
async function tellCSharpBusy(pageId: string, what: string): Promise<void> {
    const reply = await postStringQuietly(snapshotUrl(kBusyApi, pageId), what);
    const response = reply as { data?: boolean | string } | void;
    const refused = !!response && response.data === false;
    if (refused && busyWith === what && pageIdBeingWatched === pageId) {
        window.setTimeout(() => {
            if (busyWith === what && pageIdBeingWatched === pageId)
                void tellCSharpBusy(pageId, what);
        }, kRetryAfterRefusalMs);
    }
}

// Idle means "and you already have the page as it is now", so the snapshot goes first: either
// the gather that was parked behind the register (see takeSnapshot) finishes and posts, or we
// take one now, which posts only if the page actually changed. Only then do we say idle, and
// only if the page has not gone busy again meanwhile -- the next idle will speak for that.
async function tellCSharpIdle(pageId: string): Promise<void> {
    if (busy) await runDone;
    else await takeSnapshot();
    if (pageIdBeingWatched !== pageId || busyWith !== undefined) return;
    await postStringQuietly(snapshotUrl(kIdleApi, pageId), "");
}

function scheduleSnapshot(delayMs: number = kQuietMs): void {
    if (timer !== undefined) window.clearTimeout(timer);
    timer = window.setTimeout(() => {
        timer = undefined;
        void takeSnapshot();
    }, delayMs);
}

function noteChange(): void {
    changeCount++;
    scheduleSnapshot();
}

/**
 * Tell the watcher that the saved form of the page may have changed in a way it cannot see.
 *
 * The MutationObserver covers everything in the body, which is nearly all of what we gather. It
 * does NOT cover the user's own style definitions: those are gathered too (see
 * getPageContentForSave), they live in a <style> in the HEAD, and the style editor changes them
 * through the CSSOM -- deleteRule/insertRule and setProperty -- which mutates no DOM node at all,
 * in the head or anywhere else. So changing a style's font, size, spacing or colour without
 * touching the text could produce no snapshot, and leaving the tab or quitting would then write
 * the styles as they were.
 *
 * Calling this more often than necessary costs nothing: a snapshot is compared against the last
 * one delivered and is not posted if the saved form has not actually changed. So callers should
 * err towards calling it.
 */
export function notePageContentMayHaveChanged(): void {
    noteChange();
}

// Read the page once, and treat that as already sent. See the long note in
// startWatchingPageForSnapshots for why a baseline is needed at all.
function takeBaseline(pageId: string): void {
    void gatherPageContent!().then(
        (baseline) => {
            if (pageIdBeingWatched !== pageId) return; // moved on while we waited
            baselineTaken = true;
            if (changeCount > 0) {
                // The page changed while we were reading it -- the gather waits for the delay
                // register, and the user can type in that time -- so the baseline may already
                // contain an edit. Treating it as "already sent" would swallow that edit: the
                // follow-up snapshot would match it and post nothing, and quitting would write
                // what C# holds. So the baseline counts for nothing and the follow-up posts
                // whatever is there. That costs one redundant post on a page the user did not
                // touch, which C# then finds unchanged.
                lastPosted = undefined;
                scheduleSnapshot();
            } else {
                lastPosted = baseline;
            }
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
    consecutiveFailedPosts = 0;
    busyWith = undefined;
    unsubscribeFromDelayRegister = onDelayRegisterChanged(
        handleDelayRegisterChange,
    );

    // Take a baseline of the page as it ends up once it has finished loading, and treat that as
    // "already sent". Without it every page posts a snapshot within a second of being opened, even
    // if the user never touches it -- because loading is not finished when bootstrap() returns.
    // Image sizing and canvas-element layout complete asynchronously afterwards and mutate the
    // page, and the observer cannot tell those from the user's own edits.
    //
    // That mattered: it broke the property C# depends on, that no snapshot means no unsaved
    // changes.
    //
    // The gather waits for the delay register -- but that is NOT enough to make this the settled
    // page: at the moment we run, the asynchronous fix-ups have not registered their delays yet,
    // so the register is empty and the gather returns immediately. So an untouched page still
    // posts one snapshot, byte-identical to the settled page.
    //
    // Deliberately NOT "fixed" by delaying the baseline until the page is quiet. That would make
    // the baseline include any edit the user managed in the meantime, and since load-time
    // settling is indistinguishable from typing, we would then have no way to tell we owed C# a
    // snapshot of it -- trading a harmless duplicate for a lost edit. One post per page visit,
    // carrying exactly what a save would have written, is the better end of that trade.
    takeBaseline(pageId);

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
    busyWith = undefined;
    unsubscribeFromDelayRegister?.();
    unsubscribeFromDelayRegister = undefined;
}

/**
 * Exported for tests: the interval the page must be quiet before a snapshot is taken.
 */
export const quietMsForTests = kQuietMs;
export const retryMsForTests = kRetryAfterRefusalMs;
