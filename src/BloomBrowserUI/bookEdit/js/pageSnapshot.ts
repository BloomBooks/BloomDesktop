import { postStringQuietly, postThatMightNavigate } from "../../utils/bloomApi";
import { reportError } from "../../lib/errorHandler";
import { onDelayRegisterChanged } from "./pageContentDelays";

// Keep C# supplied with the current content of the page being edited, so that a save can take it
// synchronously (see PageSnapshot.cs) instead of asking the browser for it and waiting. Gathering
// the page is cheap (~0.7 ms) and works on a clone, so we can afford to do it after every change.
//
// We post the page once as soon as it has loaded, whether or not anyone touches it, because loading
// can itself change the page (after a change of page size or appearance, image sizing and
// canvas-element layout recompute), some of it before the observer starts. C#
// (Book.UpdateDomFromEditedPage) decides whether a snapshot actually changes the book, so a page
// the user only looked at writes nothing to disk. After that we post only when a gather differs
// from the last thing we sent; tools constantly add and remove decorations that the gather strips.
//
// The same endpoint carries a BUSY notice: the page has asynchronous work whose results belong in
// the saved page (see pageContentDelays.ts). C# then waits, for a bounded time, for the next
// snapshot, which we send as soon as the work is done even if the content did not change, because
// that snapshot is what tells C# the page is idle again.
//
// Everything goes out one at a time, in order (see postInOrder), so C# needs no sequence numbers.

const kApi = "editView/pageSnapshot";

// Identifies THIS load of THIS page. A module-level constant is the right scope: the page frame
// gets a fresh document, and so a fresh module, on every page load.
//
// The snapshot endpoint is deliberately unsynchronised (a keystroke should not queue behind a
// save), so a post sent just before a navigation can arrive after C# has moved on. Change Layout,
// importing a video and changing the topic reload the SAME page id, and without the load id a
// snapshot of the pre-reload page would be merged over what the reload built.
const pageLoadId =
    Date.now().toString(36) + "-" + Math.random().toString(36).slice(2, 10);

/**
 * Identifies this load of this page. Sent with the "page is ready" notification and with every
 * snapshot, so C# can ignore anything from a load it has superseded.
 */
export function getPageLoadId(): string {
    return pageLoadId;
}

// How long the page must be quiet before we take a snapshot. Small on purpose: it bounds how much
// typing an exit could lose. It only has to coalesce the ~nine MutationObserver batches CKEditor
// produces per keystroke; below about 25 ms the lag is dominated by the POST anyway. The cost, one
// cheap localhost POST per keystroke, is measured in Edit/SavingWithoutReloading.md.
const kQuietMs = 25;

let observer: MutationObserver | undefined;
let timer: number | undefined;
// What we last handed to postInOrder: the content C# will hold once the queue has drained.
let lastPosted: string | undefined;
let pageIdBeingWatched: string | undefined;
// Passed in rather than imported, so this module does not depend on bloomEditing (which depends on
// it) and a test can drive it without a real page.
let gatherPageContent: (() => Promise<string>) | undefined;
// What the delay register said the page was busy with when it became busy; undefined when idle.
let busyWith: string | undefined;
// Set when a busy spell ends: C# is waiting for a snapshot to tell it the page is idle, so the next
// one must be sent even if the page's saved form did not change.
let snapshotOwed = false;
let unsubscribeFromDelayRegister: (() => void) | undefined;
// So that a page which fails every time reports once rather than on every keystroke.
let pageWeReportedAFailureFor: string | undefined;
// The tail of the queue of posts; see postInOrder.
let postQueue: Promise<void> = Promise.resolve();
// Posts queued or in flight, and gathers under way; see isSnapshotStreamIdle.
let postsPending = 0;
let gathersPending = 0;
// Whether C# took the latest snapshot we queued; see sendSnapshotNow.
let lastSnapshotTaken: Promise<boolean> = Promise.resolve(true);

// Tell the user, at most once for this page; this is why snapshot posts use postStringQuietly.
// There is no retry: posting to localhost should never fail, and if it does, something is badly
// wrong and the user needs to know their work may not be saved.
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

// Send one message to C#, after every message sent before it has been answered. Two POSTs in
// flight to the unsynchronised endpoint could be processed in either order: an older snapshot
// could overwrite a newer one, or a busy notice could be cleared by a snapshot taken before the
// work began.
//
// C# answers false for a load it is no longer showing (we start only once it has accepted this
// load; see startWatchingPageForSnapshots), so nobody wants the message and we drop it.
//
// Resolves to whether C# took the message.
function postInOrder(
    pageId: string,
    url: string,
    body: string,
): Promise<boolean> {
    postsPending++;
    const taken = postQueue.then(async () => {
        let reply: unknown;
        try {
            reply = await postStringQuietly(url, body);
        } catch {
            reply = undefined;
        }
        // wrapAxios turns a rejected request into a resolved promise carrying nothing, so no
        // response is what failure looks like.
        if (!reply) {
            reportFailureOncePerPage(
                pageId,
                "Bloom could not keep track of your changes to this page: the request to save them did not get through.",
                undefined,
            );
            // C# does not hold this content, so the next gather must not skip it as already sent
            // (unless something newer has been queued since).
            if (lastPosted === body) lastPosted = undefined;
            return false;
        }
        return (reply as { data?: unknown }).data !== false;
    });
    postQueue = taken.then(() => {
        postsPending--;
    });
    return taken;
}

// Resolves to false only if the page could not be read (which has been reported).
async function takeSnapshot(): Promise<boolean> {
    const pageId = pageIdBeingWatched;
    const gather = gatherPageContent;
    if (!pageId || !gather) return true;
    let content: string;
    gathersPending++;
    try {
        // Waits for the delay register to empty (see pageContentDelays).
        content = await gather();
    } catch (error) {
        // Gathering can legitimately throw (the BL-13120 origami guard, a missing marginBox, the
        // canvas-element count checks). If we stayed quiet, C# would save whatever it last
        // received and the user's edits would be dropped without a word; the global
        // unhandledrejection handler is commented out in lib/errorHandler.ts, so nothing else
        // would report it.
        reportFailureOncePerPage(
            pageId,
            "Bloom could not keep track of your changes to this page: " +
                (error instanceof Error ? error.message : String(error)),
            error instanceof Error ? error.stack : undefined,
        );
        return false;
    } finally {
        gathersPending--;
    }

    // The page may have been unloaded, or navigated, while we were waiting.
    if (pageIdBeingWatched !== pageId) return true;
    if (content === lastPosted && !snapshotOwed) return true;
    lastPosted = content;
    snapshotOwed = false;
    let url = snapshotUrl(pageId);
    // The page can be busy even so: the gather gave up waiting for the work (see kMaxWaitTimeMs),
    // or the work began just after the gather read the page. C# takes a snapshot to mean idle
    // unless it says otherwise, so say so in the same message; a separate busy notice afterwards
    // would leave a moment in which a save took this for the finished page.
    if (busyWith !== undefined)
        url += "&stillBusyWith=" + encodeURIComponent(busyWith);
    lastSnapshotTaken = postInOrder(pageId, url, content);
    return true;
}

/**
 * Send C# the page as it is now, without waiting for the page to be quiet, and resolve once C#
 * has answered. For a request that makes C# save a page the caller has only just changed. Resolves
 * to false if C# does not have the page as it is now: it could not be read, or the post failed
 * (both reported to the user), or C# has moved on from this page.
 */
export async function sendSnapshotNow(): Promise<boolean> {
    if (!(await takeSnapshot())) return false;
    return lastSnapshotTaken;
}

// The delay register has gone busy or idle. A save C# makes from the snapshot cannot wait for the
// register the way a gather here does, so we tell it; it waits a bounded time for the snapshot
// that follows (PageSnapshot.WaitUntilIdle), logging what the page was busy with if none comes.
function handleDelayRegisterChange(nowBusyWith: string | undefined): void {
    const pageId = pageIdBeingWatched;
    if (!pageId) return;
    busyWith = nowBusyWith;
    if (nowBusyWith !== undefined) {
        void postInOrder(
            pageId,
            snapshotUrl(pageId) + "&busy=true",
            nowBusyWith,
        );
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
 * The MutationObserver watches the body. The user's style definitions are gathered too, but they
 * live in a <style> in the head and the style editor changes them through the CSSOM, which mutates
 * no DOM node, so a style change alone would produce no snapshot.
 *
 * Calling this unnecessarily is cheap (an unchanged snapshot is not posted), so err towards it.
 */
export function notePageContentMayHaveChanged(): void {
    scheduleSnapshot();
}

/**
 * Start watching the page that has just become editable. Safe to call again; it restarts on the
 * new page.
 *
 * Call it only once C# has answered the "page is ready" notification (editView/pageDomLoaded):
 * until then C# refuses messages from this load, and we never resend one.
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
    unsubscribeFromDelayRegister = onDelayRegisterChanged(
        handleDelayRegisterChange,
    );

    // Send the page as loaded (see the top of this file). Scheduled rather than immediate, so that
    // asynchronous load-time work (image sizing, mainly) has registered with the delay register
    // and the gather waits for it.
    scheduleSnapshot();

    // Not input/keyup handlers: much of what changes a page (a tool rewriting markup, a canvas
    // element being dragged, an image replaced, a paste) is not a keyboard event.
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
    lastSnapshotTaken = Promise.resolve(true);
    unsubscribeFromDelayRegister?.();
    unsubscribeFromDelayRegister = undefined;
}

/**
 * True when nothing about the page is waiting to reach Bloom: no change waiting out the quiet
 * time, no gather under way, and no post queued or in flight. For the e2e suite, which waits on
 * this rather than guessing how long a snapshot takes to arrive.
 */
export function isSnapshotStreamIdle(): boolean {
    return timer === undefined && gathersPending === 0 && postsPending === 0;
}

/**
 * Exported for tests: the interval the page must be quiet before a snapshot is taken.
 */
export const quietMsForTests = kQuietMs;

// Save the page and have C# rebuild it from the updated book DOM. Unlike an ordinary save, the
// page IS reloaded: these callers have restructured the page in ways that have never been through
// SetupElements (a new origami layout, an imported video, a translation group replaced by a
// derived field).
//
// The caller has only just changed the page, so we send the snapshot now rather than after the
// usual quiet time. If the page cannot be read, the user has been told, and we leave the page as
// it is rather than reload it from a book without the change.
//
// The post itself might navigate this very frame out from under us, hence postThatMightNavigate.
export function saveChangesAndRethinkPage(): Promise<void> {
    return postAfterSendingSnapshot("common/saveChangesAndRethinkPageEvent");
}

/**
 * Post a request that makes C# save this page and reload it, once C# has the page as it is now
 * (see saveChangesAndRethinkPage, the usual such request).
 */
export async function postAfterSendingSnapshot(
    urlSuffix: string,
): Promise<void> {
    if (!(await sendSnapshotNow())) return;
    await postThatMightNavigate(urlSuffix);
}
