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
// We post the page once as soon as it has loaded, whether or not anyone touches it, and then again
// whenever its SAVED FORM changes. The first post is deliberate: loading can itself change the
// page -- after a change of page size or appearance, image sizing and canvas-element layout
// recompute, and those results belong in the book -- and some of that is done before we start
// watching, where the observer cannot see it. Whether a snapshot actually changes the book is
// decided by C# (Book.UpdateDomFromEditedPage), after its own processing, so a page the user only
// looked at writes nothing to disk.
//
// After that we post only when a gather differs from the last thing we sent. Tools constantly add
// and remove editing decorations, which the gather strips anyway, so without the comparison they
// would produce a stream of identical posts.
//
// The same endpoint carries one other message: that the page has become BUSY with asynchronous work
// whose results belong in the saved page (see pageContentDelays.ts). A save C# makes from the
// snapshot meanwhile would miss that work, so C# waits, for a bounded time, for the next snapshot,
// which we send as soon as the work is done -- even if the page's saved form did not change, since
// that snapshot is also what tells C# the page is idle again. A gather always waits for the
// register to empty, so every snapshot is of an idle page.
//
// Everything we post goes out one at a time, in order (see postInOrder), so C# needs no sequence
// numbers to tell an old message from a new one.

const kApi = "editView/pageSnapshot";

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

let observer: MutationObserver | undefined;
let timer: number | undefined;
// What we last handed to postInOrder: the content C# will hold once the queue has drained.
let lastPosted: string | undefined;
let pageIdBeingWatched: string | undefined;
// How we read the page. Passed in by the caller rather than imported, so this module does not
// depend on bloomEditing (which depends on it, for the teardown) -- and so a test can drive it
// without a real page.
let gatherPageContent: (() => Promise<string>) | undefined;
// What the delay register said the page was busy with when it last became busy, or undefined when
// it is empty. Only a clue for C#'s log (see onDelayRegisterChanged): work added later in the same
// busy spell is not in it.
let busyWith: string | undefined;
// Set when a busy spell ends: C# is waiting for a snapshot to tell it the page is idle, so the next
// one must be sent even if the page's saved form did not change.
let snapshotOwed = false;
let unsubscribeFromDelayRegister: (() => void) | undefined;
// The page we have already complained about, so that a page which fails every time reports once
// rather than on every keystroke.
let pageWeReportedAFailureFor: string | undefined;
// The tail of the queue of posts; see postInOrder.
let postQueue: Promise<void> = Promise.resolve();

// Tell the user, at most once for this page. Reporting is the whole reason a snapshot post is
// made quietly (see postStringQuietly): so that WE decide when to speak, rather than the request
// layer speaking on every attempt.
//
// There is no retry. Posting a string to localhost should never fail, and if it does, something is
// badly wrong and the user needs to know that their work may not be saved.
function reportFailureOncePerPage(
    pageId: string,
    message: string,
    stack: string | undefined,
): void {
    if (pageWeReportedAFailureFor === pageId) return;
    pageWeReportedAFailureFor = pageId;
    reportError(message, stack);
}

function currentPageId(): string | undefined {
    return document.querySelector(".bloom-page")?.id || undefined;
}

// Send one message to C#, after every message sent before it has been answered.
//
// HTTP does not promise that two outstanding POSTs arrive in the order they were sent, and the
// endpoint is unsynchronised, so two in flight could be processed in either order: an OLDER
// snapshot could overwrite a newer one, or a busy notice could be cleared by a snapshot taken
// before the work began. One at a time rules both out. On a slow machine, messages simply queue;
// each is tiny for C# to handle.
//
// C# answers false for a load it is not showing. That only happens once it has moved on from this
// page (we do not start posting until it has accepted this load; see startWatchingPageForSnapshots),
// so there is nobody left who wants the message, and we drop it.
function postInOrder(pageId: string, url: string, body: string): void {
    postQueue = postQueue.then(async () => {
        let reply: unknown;
        try {
            reply = await postStringQuietly(url, body);
        } catch {
            reply = undefined;
        }
        // A failed post goes through wrapAxios, which turns a rejected request into a resolved
        // promise carrying nothing -- so no response at all is what failure looks like.
        if (!reply) {
            reportFailureOncePerPage(
                pageId,
                "Bloom could not keep track of your changes to this page: the request to save them did not get through.",
                undefined,
            );
            // C# does not hold this content, so do not let the next gather skip it as already
            // sent: the user's next change sends the whole page again. (Unless something newer
            // has been queued since, which will carry it anyway.)
            if (lastPosted === body) lastPosted = undefined;
        }
    });
}

// Resolves to false only if the page could not be read (which has been reported).
async function takeSnapshot(): Promise<boolean> {
    const pageId = pageIdBeingWatched;
    const gather = gatherPageContent;
    if (!pageId || !gather) return true;
    let content: string;
    try {
        // Waits for any in-flight work that belongs in the page (see pageContentDelays), then
        // reads the page the same way a real save does, so a snapshot can never differ from what
        // a save would have produced at the same moment.
        content = await gather();
    } catch (error) {
        // Gathering the page can legitimately throw -- the BL-13120 origami guard, a missing
        // marginBox, the canvas-element count checks. This is the one failure the whole design
        // cannot afford to be quiet about: C# saves whatever it last received, or nothing at all,
        // and the user's edits are dropped without a word. (The global unhandledrejection handler
        // is commented out in lib/errorHandler.ts, so nothing else would report it.) Before
        // BL-13502 the equivalent failure came back through the state machine as "Bloom had
        // trouble saving a page"; this keeps that promise.
        //
        // Once per page: a page that fails will fail again on the very next keystroke.
        reportFailureOncePerPage(
            pageId,
            "Bloom could not keep track of your changes to this page: " +
                (error instanceof Error ? error.message : String(error)),
            error instanceof Error ? error.stack : undefined,
        );
        return false;
    }

    // The page may have been unloaded, or navigated, while we were waiting.
    if (pageIdBeingWatched !== pageId) return true;
    // Work began between the gather's read and our getting here, and we have already told C# the
    // page is busy. What we read predates the work; the snapshot that ends the busy spell will
    // carry it, and sending this one would tell C# the page is idle when it is not.
    if (busyWith !== undefined) return true;
    if (content === lastPosted && !snapshotOwed) return true;
    lastPosted = content;
    snapshotOwed = false;
    postInOrder(pageId, snapshotUrl(pageId), content);
    return true;
}

/**
 * Send C# the page as it is now, without waiting for the page to be quiet, and resolve once C#
 * has answered. For a request that makes C# save a page the caller has only just changed. Resolves
 * to false if the page could not be read; that has already been reported to the user.
 */
export async function sendSnapshotNow(): Promise<boolean> {
    const ok = await takeSnapshot();
    await postQueue;
    return ok;
}

// The delay register (pageContentDelays.ts) has gone busy or idle. C# needs to know, because a
// save it makes from the snapshot -- leaving the Edit tab, quitting, a page-list command -- cannot
// wait for the register the way a gather here does: the snapshot it holds simply predates the
// work. So we tell it what the page is busy with, and it waits a bounded time for the snapshot
// that follows (PageSnapshot.WaitUntilIdle), logging the culprit if it does not come.
function handleDelayRegisterChange(nowBusyWith: string | undefined): void {
    const pageId = pageIdBeingWatched;
    if (!pageId) return;
    busyWith = nowBusyWith;
    if (nowBusyWith !== undefined) {
        postInOrder(pageId, snapshotUrl(pageId) + "&busy=true", nowBusyWith);
    } else {
        snapshotOwed = true;
        void takeSnapshot();
    }
}

function snapshotUrl(pageId: string): string {
    return `${kApi}?pageId=${encodeURIComponent(pageId)}&loadId=${encodeURIComponent(
        pageLoadId,
    )}`;
}

function scheduleSnapshot(): void {
    if (timer !== undefined) window.clearTimeout(timer);
    timer = window.setTimeout(() => {
        timer = undefined;
        void takeSnapshot();
    }, kQuietMs);
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
 * one sent and is not posted if the saved form has not actually changed. So callers should err
 * towards calling it.
 */
export function notePageContentMayHaveChanged(): void {
    scheduleSnapshot();
}

/**
 * Start watching the page that has just become editable. Safe to call again; it restarts on the
 * new page.
 *
 * Call it only once C# has answered the "page is ready" notification (editView/pageDomLoaded).
 * Until then C# refuses messages from this load, and since we never offer one twice, an early
 * snapshot would simply be lost.
 */
export function startWatchingPageForSnapshots(
    gather: () => Promise<string>,
): void {
    stopWatchingPageForSnapshots();
    const pageId = currentPageId();
    if (!pageId) return; // no page to watch (e.g. the off-screen capture path)
    gatherPageContent = gather;
    pageIdBeingWatched = pageId;
    pageWeReportedAFailureFor = undefined;
    // Tells us at once if the register is already busy (see onDelayRegisterChanged).
    unsubscribeFromDelayRegister = onDelayRegisterChanged(
        handleDelayRegisterChange,
    );

    // Send the page as it has loaded, changed or not; see the top of this file for why. After the
    // usual quiet time rather than at once, which gives the load-time work that finishes
    // asynchronously (image sizing, mainly) the chance to register with the delay register first,
    // so the gather waits for it and this one post usually carries the settled page.
    scheduleSnapshot();

    // A MutationObserver rather than input/keyup handlers, because plenty of what changes a page
    // never goes through a keyboard event: a tool rewriting the markup, a canvas element being
    // dragged, an image being replaced, a paste. Anything that changes the DOM is a change we owe
    // C# a snapshot of.
    observer = new MutationObserver(scheduleSnapshot);
    observer.observe(document.body, {
        subtree: true,
        childList: true,
        characterData: true,
        attributes: true,
    });
}

/**
 * Stop watching, and forget what we last sent. Called from pageUnloading(). Messages already
 * queued still go out; C# refuses them if it has moved on.
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
    busyWith = undefined;
    snapshotOwed = false;
    unsubscribeFromDelayRegister?.();
    unsubscribeFromDelayRegister = undefined;
}

/**
 * Exported for tests: the interval the page must be quiet before a snapshot is taken.
 */
export const quietMsForTests = kQuietMs;
