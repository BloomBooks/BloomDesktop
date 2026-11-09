// The iframe that holds the page being edited, and how the Edit tab changes the page in it without
// the view blanking and rebuilding.
//
// This module's state lives in the root window. Code in other frames (the page frame, the page
// list) must reach these functions through getWorkspaceBundleExports(), never by importing this
// module: an import would give that frame its own load counter, which never matches the one here.

import { kPageLoadNumberAttribute } from "./workspaceFrames";
import { isShowingOtherPages } from "./pageViewModeSetting";

const kPageFrameId = "page";
const kOutgoingPageFrameId = "page-outgoing";
const kPageLoadingCoverId = "page-loading-cover";
const kMaxWaitForPageReadyAfterLoadMs = 3000;
const kMaxWaitForPageChangeToStartMs = 5000;

// The cursor shown while the Edit tab changes pages: a plain clock face, rather than the spinning
// circle Windows draws for the standard "wait" cursor. Falls back to that standard cursor.
const kClockSvg =
    '<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24">' +
    '<circle cx="12" cy="12" r="10" fill="white" stroke="#333" stroke-width="2"/>' +
    '<path d="M12 12V6.5M12 12l3.5 2" stroke="#333" stroke-width="2" stroke-linecap="round"/>' +
    "</svg>";
const kWaitCursor = `url("data:image/svg+xml,${encodeURIComponent(
    kClockSvg,
)}") 12 12, wait`;

// What to do once the page loading into this frame is there; see loadPageIntoFrame.
let onPageArrivedInFrame:
    | { frame: HTMLIFrameElement; onPageArrived: () => void }
    | undefined;
// The number of the latest load of the page frame (see numberPageLoad).
let pageLoadNumber = 0;

/**
 * Cover the page frame with the wait cursor, and take its clicks, from a click on another page until
 * that page shows (see pageFrameIsReadyToShow). If no page change follows, take the cover away again.
 */
export function showPageLoadingCover(): void {
    if (document.getElementById(kPageLoadingCoverId)) return;
    const host = document.getElementById(kPageFrameId)!.parentElement!;
    const cover = document.createElement("div");
    cover.id = kPageLoadingCoverId;
    cover.style.position = "absolute";
    cover.style.inset = "0";
    cover.style.zIndex = "2";
    cover.style.cursor = kWaitCursor;
    host.appendChild(cover);
    window.setTimeout(() => {
        if (!document.getElementById(kOutgoingPageFrameId)) cover.remove();
    }, kMaxWaitForPageChangeToStartMs);
}

/**
 * Give the page about to load into this frame the next load number. The page passes it back when it
 * is ready to show, so that a page that has since been replaced is ignored: in One Page the frame is
 * reused, and its window is the same object for the page being left and the page replacing it.
 */
export function numberPageLoad(frame: HTMLIFrameElement): void {
    frame.setAttribute(kPageLoadNumberAttribute, String(++pageLoadNumber));
}

// Take away the page being left, and the cover over it, so the new page shows.
function takeDownPageBeingLeft(): void {
    document.getElementById(kOutgoingPageFrameId)?.remove();
    document.getElementById(kPageLoadingCoverId)?.remove();
}

/**
 * Make the iframe that holds the page being edited. The Edit tab has one, except while it changes
 * pages, when the page being left stays on screen over the new one until the new one is ready
 * (see loadPageIntoFrame), so the view holds steady instead of blanking and rebuilding.
 */
export function createPageFrame(src: string): HTMLIFrameElement {
    const frame = document.createElement("iframe");
    frame.id = kPageFrameId;
    // The name must be set before the frame is in the document, so the frame is created with it.
    frame.name = kPageFrameId;
    frame.title = "page";
    frame.src = src;
    frame.style.display = "block";
    frame.style.width = "100%";
    frame.style.height = "100%";
    frame.style.border = "0 none";
    return frame;
}

// Put a new page frame, loading newSource, underneath the one on screen, and return it. The old
// frame keeps showing, frozen, until the new page calls pageFrameIsReadyToShow(). If an earlier
// switch is still waiting, the frame the user sees is already the outgoing one, so the frame that
// was loading underneath is simply replaced.
function startLoadingPageFrameBehindCurrentOne(
    newSource: string,
): HTMLIFrameElement {
    const current = document.getElementById(kPageFrameId) as HTMLIFrameElement;
    const host = current.parentElement!;
    if (document.getElementById(kOutgoingPageFrameId)) {
        current.remove();
    } else {
        // Over both frames until the new page shows: the wait cursor, and no clicks on a page that
        // is going away or one that is not ready. (It finds its place by the frame's id, so before
        // that changes.)
        showPageLoadingCover();
        current.id = kOutgoingPageFrameId;
        current.name = kOutgoingPageFrameId;
        current.style.position = "absolute";
        current.style.left = "0";
        current.style.top = "0";
        current.style.zIndex = "1";
        current.style.pointerEvents = "none";
        // What the page's own beforeunload handlers would do if it were really leaving now.
        current.contentWindow?.dispatchEvent(new Event("beforeunload"));
    }
    const frame = createPageFrame(newSource);
    host.appendChild(frame);
    return frame;
}

// Load newSource into the page frame on screen, replacing the page in it, and return the frame.
function navigatePageFrame(newSource: string): HTMLIFrameElement {
    const frame = document.getElementById(kPageFrameId) as HTMLIFrameElement;
    // The page really leaves, so its own beforeunload handlers run without help.
    frame.src = newSource;
    return frame;
}

/**
 * Called by a page frame once it looks the way it will (laid out, scrolled, and with the other
 * pages drawn if they are showing), to replace the page that was covering it. loadNumber is the
 * one the frame was given for loading this page (see numberPageLoad).
 */
export function pageFrameIsReadyToShow(loadNumber: number): void {
    if (loadNumber !== pageLoadNumber) {
        // A page that has already been replaced by a newer one, or is being replaced.
        return;
    }
    takeDownPageBeingLeft();
    const current = document.getElementById(kPageFrameId);
    if (onPageArrivedInFrame?.frame === current) {
        onPageArrivedInFrame.onPageArrived();
        onPageArrivedInFrame = undefined;
    }
}

/**
 * Show the page at newSource in the page frame, and call onPageArrived once that page is in the
 * frame's document: when its load event fires, or when the page says it is ready to show (see
 * pageFrameIsReadyToShow), whichever comes first. The page says so even in the cases where its load
 * event never fires (see the comment at the end). A fixed wait is no good: on a slow machine a page
 * can take several seconds to load. onPageArrived is not called if another page change starts first.
 */
export function loadPageIntoFrame(
    newSource: string,
    onPageArrived: () => void,
): void {
    // Showing one page, the new page replaces the old one in the same frame and is seen as soon as
    // it is drawn, while it finishes setting itself up. Holding the old page on screen until the new
    // one was ready made every page change feel slow. With the other pages showing, the old page
    // stays until the new one and the pages around it are drawn, so the whole view does not flash.
    // (A change already under way has two frames, and keeps them until it is done.)
    const holdOldPage =
        isShowingOtherPages() ||
        !!document.getElementById(kOutgoingPageFrameId);
    const iframe = holdOldPage
        ? startLoadingPageFrameBehindCurrentOne(newSource)
        : navigatePageFrame(newSource);
    numberPageLoad(iframe);
    // In One Page the frame stays the same from one change to the next, so what is set up below
    // must only act on this change: a later one has its own.
    const thisLoad = pageLoadNumber;
    const isStillThisChange = () => thisLoad === pageLoadNumber;
    let handlerCalled = false;
    const handler = () => {
        if (handlerCalled || !isStillThisChange()) return;
        handlerCalled = true;
        iframe.removeEventListener("load", handler);
        onPageArrived();
    };
    iframe.addEventListener("load", handler);
    onPageArrivedInFrame = { frame: iframe, onPageArrived: handler };
    iframe.addEventListener(
        "load",
        () =>
            // If the page never says it is ready (say an error stopped its script), don't leave
            // the old page covering it for good.
            window.setTimeout(() => {
                if (isStillThisChange()) takeDownPageBeingLeft();
            }, kMaxWaitForPageReadyAfterLoadMs),
        { once: true },
    );
    // When we don't already have a video (either a new page, or it has been deleted),
    // and record a new one, we switchContentPage to make the new video show up.
    // And for no known reason, the load event never fires. There may possibly be
    // other cases since I have no explanation. Taking down the page being left waits for load
    // too, in the fallback above, so if the load event is delayed much longer than expected it
    // gets the same rescue from here.
    window.setTimeout(() => {
        if (!handlerCalled && isStillThisChange()) {
            window.setTimeout(() => {
                if (isStillThisChange()) takeDownPageBeingLeft();
            }, kMaxWaitForPageReadyAfterLoadMs);
        }
    }, 1500);
}
