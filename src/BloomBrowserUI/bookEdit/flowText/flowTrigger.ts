// When a flow pass runs. Everything that decides what a pass does lives in the other
// modules; this one only watches the page and coalesces the work into one animation frame.

import {
    addRequestPageContentDelay,
    removeRequestPageContentDelay,
} from "../js/bloomEditing";
import { deleteEmptiedPage, jumpToPage } from "./flowBoundaryClient";
import { getLanguageChainOnPage } from "./flowChain";
import {
    removeContinueButtons,
    resetPendingOverflowCache,
    setFlowPassRunner,
    updateContinueButtons,
} from "./flowContinueButton";
import {
    kChainedGroupSelector,
    kFlowChainAttr,
    kMeasuringFlowFitAttr,
    kReflowingAttr,
} from "./flowConstants";
import {
    removeCreatePagesButtons,
    updateCreatePagesButtons,
} from "./flowCreatePagesButton";
import {
    applyRefitResult,
    areBoundaryRetriesWaiting,
    areWalksWanted,
    beginCrossPageRun,
    onWalkFinished,
    placePendingCaret,
    resetCrossPageCache,
    requestQueuedWalks,
    settleCrossPageBoundary,
} from "./flowCrossPage";
import { rebalanceChain } from "./flowEngine";
import {
    refreshReflowBubbles,
    removeReflowBubbles,
    setFlowSettleWaiter,
} from "./flowReflowBubble";
import { getRefitResult } from "./flowReflowClient";
import {
    removeFlowFromLabels,
    resetFlowFromCache,
    updateFlowFromLabels,
} from "./flowFromLabel";
import {
    removeFlowToLabels,
    resetFlowToCache,
    updateFlowToLabels,
} from "./flowToLabel";
import { LineMeasurer } from "./flowFit";
import { stripTransientFlowMarkup, updateIndicators } from "./flowIndicators";
import {
    placeOverflowMarker,
    removeOverflowMarker,
    textUpToOffsetFitsInBox,
} from "./flowOverflowMarker";
import { getPretextMeasurer } from "./flowPretextMeasurer";
import { handleSeamKey } from "./flowSeamKeys";
import { markRefusals } from "./flowSupport";
import { timePass } from "./flowTiming";
import { OverflowMeasurer, verifyAndNudge } from "./flowVerify";
import WebSocketManager, {
    IBloomWebSocketEvent,
} from "../../utils/WebSocketManager";

export type FlowTextOptions = {
    /** Test seam: decide where the text breaks without a layout engine. */
    measurer?: LineMeasurer;
    /** Test seam: script what the real layout would report. */
    measureOverflow?: OverflowMeasurer;
    requestFrame?: (callback: () => void) => number;
    cancelFrame?: (handle: number) => void;
    /**
     * Show or clear Bloom's own overflow warning on a box whose text this code has changed.
     * The warning is OverflowChecker's, and it otherwise runs only on the user's keystrokes;
     * text that arrives from another page arrives without one.
     */
    markOverflow?: (editable: HTMLElement) => void;
    /**
     * Bring the page's own overflow warning into line with the boxes on it now. It is the
     * page-level half of the warning above (OverflowChecker.UpdatePageOverflow), and what a page
     * thumbnail is drawn from.
     */
    updatePageOverflow?: (page: HTMLElement) => void;
};

const kPageSelector = ".bloom-page";
const kChainedEditableSelector = `${kChainedGroupSelector} > .bloom-editable.bloom-visibility-code-on`;

// The id we hand requestPageContent, so that a save waits for the pass to finish.
const kDelayId = "flowText";

let observer: MutationObserver | undefined;
let observedContainer: HTMLElement | undefined;
let activeOptions: FlowTextOptions = {};

const pendingTriggers = new Set<HTMLElement>();
// The last box of a chain on this page, whose next box is on a later page. Settling that
// boundary is a round trip, so it happens after the pass, one box at a time.
const pendingBoundaries = new Set<HTMLElement>();
let boundaryWork: Promise<void> | undefined;
let boundaryWorkStarted = false;
// The wait for the text to stand still before the pages after this one are refitted.
let walkWait: Promise<void> | undefined;
let walkWaitStarted = false;
// True while a boundary Bloom refused during a refit waits for word that the refit is over.
// The page counts as reflowing and holds a save delay for as long as it does: the text on it
// is not where it belongs yet.
let retryWaiting = false;
let retryTimeout: number | undefined;
// True while the content a refit made for a box of this page is being taken from Bloom and put
// in. Bloom hands that content over once, so the page is not settled until it is in.
let takingRefitResult = false;
// The websocket Bloom sends walkFinished on. FlowTextWalk.cs uses the same name.
const kWebSocketContext = "flowText";
// How long a boundary waits for word that the refit has finished before settling anyway, so
// that an event that never arrives cannot leave the page marked as reflowing for ever.
const kMaxWaitForWalkFinishedMs = 60000;
// A push can leave text that still does not fit, and a pull can free room for more, so the
// boundary settles again after a move. This caps that, in case the two disagree.
const kMaxBoundaryRounds = 6;
let pendingFrame: number | undefined;
let passReason = "mutation";
// True from the moment we owe a pass until the moment it has run: it is what owns the save
// delay and the reflowing attribute, both of which must survive a pass held for composition.
let passOutstanding = false;
// The engine's own mutations must not schedule another pass.
let isFlowing = false;
let isComposing = false;

export function setupFlowText(
    container: HTMLElement,
    overrides: FlowTextOptions = {},
): void {
    suspendFlowText();
    if (document.body.hasAttribute(kMeasuringFlowFitAttr)) {
        // This page is here to be measured, not edited: the box it is being asked about holds
        // more text than fits it because that is the question. Watching it and settling it
        // would take that text out of the box before it is measured, and the box on the next
        // page, which is where it would go, is not in this document at all.
        return;
    }
    observedContainer = container;
    activeOptions = overrides;

    container.addEventListener("compositionstart", onCompositionStart, true);
    container.addEventListener("compositionend", onCompositionEnd, true);
    container.addEventListener("keydown", handleSeamKey, true);
    WebSocketManager.addListener<IBloomWebSocketEvent>(
        kWebSocketContext,
        onFlowTextEvent,
    );

    setFlowPassRunner({
        applyWithoutPass,
        requestPassFor,
        markOverflow,
        updatePageOverflow,
    });
    setFlowSettleWaiter(waitForThisPageToSettle);
    // What the other pages hold is read afresh for each page the reader edits.
    resetCrossPageCache();
    resetPendingOverflowCache();
    resetFlowFromCache();
    resetFlowToCache();

    observer = new MutationObserver(onMutations);
    observer.observe(container, {
        childList: true,
        characterData: true,
        subtree: true,
        attributes: true,
        attributeFilter: ["class", kFlowChainAttr],
    });

    // C# fills the boxes of a chain without measuring anything, so a page can arrive
    // over-full. Settle it now, and again once the real fonts are in place, because the
    // fallback font breaks the text somewhere else.
    reflowAllChainsOnPage("load");
    refreshContinueButtons();
    // Bloom may already be holding refits for the chains on this page, asked for while another
    // page was being edited, so the bubble that offers to run them goes up as the page arrives.
    void refreshReflowBubbles(container);
    // A refit that changed a box of this page can have finished while the page was loading, so
    // its word about that box is waiting rather than on its way.
    void takeAndApplyRefitResult();
    const fonts = (document as Document & { fonts?: FontFaceSet }).fonts;
    const watchedObserver = observer;
    fonts?.ready?.then(() => {
        if (observer !== watchedObserver) {
            return;
        }

        reflowAllChainsOnPage("fontsReady");
        // The text the user was typing in moved onto this page, so the caret follows it here.
        // It goes in after the fonts, because which character is on which line depends on them.
        void placePendingCaret(observedContainer ?? document);
    });
}

export function suspendFlowText(): void {
    observer?.disconnect();
    observer = undefined;
    observedContainer?.removeEventListener(
        "compositionstart",
        onCompositionStart,
        true,
    );
    observedContainer?.removeEventListener(
        "compositionend",
        onCompositionEnd,
        true,
    );
    observedContainer?.removeEventListener("keydown", handleSeamKey, true);
    WebSocketManager.removeListener(kWebSocketContext, onFlowTextEvent);
    stopWaitingForWalkFinished();

    if (pendingFrame !== undefined) {
        cancelFrame(pendingFrame);
        pendingFrame = undefined;
    }

    finishPass();
    pendingTriggers.clear();
    pendingBoundaries.clear();
    setFlowPassRunner(undefined);
    setFlowSettleWaiter(undefined);
    resetCrossPageCache();
    resetPendingOverflowCache();
    resetFlowFromCache();
    resetFlowToCache();
    isComposing = false;
    removeContinueButtons(document);
    removeCreatePagesButtons(document);
    removeFlowFromLabels(document);
    removeFlowToLabels(document);
    removeReflowBubbles(document);
    stripTransientFlowMarkup(document);
    observedContainer = undefined;
    activeOptions = {};
}

/**
 * Settle every chain on the page now, without waiting for a frame. This is for a change that
 * alters where the text breaks without touching the text, such as a new style.
 */
export function reflowAllChainsOnPage(reason: string): void {
    const root = observedContainer ?? document.body;
    flowTriggers(
        Array.from(
            root.querySelectorAll<HTMLElement>(kChainedEditableSelector),
        ),
        reason,
    );
}

function onMutations(records: MutationRecord[]): void {
    if (isFlowing) {
        return;
    }

    let found = false;
    records.forEach((record) => {
        getTriggerEditables(record).forEach((editable) => {
            pendingTriggers.add(editable);
            found = true;
        });
    });

    if (found) {
        requestPass("mutation");
    }
}

/** The chained boxes, if any, that this one mutation record calls into question. */
function getTriggerEditables(record: MutationRecord): HTMLElement[] {
    const element =
        record.target instanceof HTMLElement
            ? record.target
            : record.target.parentElement;
    if (!element) {
        return [];
    }

    // A group that has just joined or left a chain changes what all of its boxes must do.
    if (
        record.type === "attributes" &&
        record.attributeName === kFlowChainAttr
    ) {
        return Array.from(
            element.querySelectorAll<HTMLElement>(
                ":scope > .bloom-editable.bloom-visibility-code-on",
            ),
        );
    }

    const editable = element.closest<HTMLElement>(".bloom-editable");
    if (!editable) {
        return [];
    }

    if (!editable.closest(kChainedGroupSelector)) {
        // An edit in a box outside every chain moves no text. A change to the box's own
        // classes can still change what the flow says about it, such as whether it is a box
        // the flow refuses, and the offer on the empty box after it has to follow suit; the
        // pass such a trigger starts does nothing but bring the offers up to date.
        return record.type === "attributes" && element === editable
            ? [editable]
            : [];
    }

    return [editable];
}

function requestPass(reason: string): void {
    passReason = reason;
    if (!passOutstanding) {
        passOutstanding = true;
        addRequestPageContentDelay(kDelayId);
        setPageReflowing(true);
    }

    scheduleFrame();
}

function scheduleFrame(): void {
    // A pass in the middle of a composition would move the text the input method is still
    // working on. compositionend schedules the frame we skip here.
    if (pendingFrame !== undefined || isComposing) {
        return;
    }

    pendingFrame = requestFrame(() => {
        pendingFrame = undefined;
        if (isComposing) {
            return;
        }

        const triggers = Array.from(pendingTriggers);
        pendingTriggers.clear();
        try {
            flowTriggers(triggers, passReason);
        } finally {
            finishPass();
        }
    });
}

function finishPass(): void {
    if (!passOutstanding) {
        return;
    }

    passOutstanding = false;
    updateReflowingAttr();
    removeRequestPageContentDelay(kDelayId);
}

/**
 * Run the work of a pass without the observer treating our own changes as the user's editing.
 * flowContinueButton uses this when it puts text C# handed back into a box.
 */
export function applyWithoutPass(work: () => void): void {
    const wasFlowing = isFlowing;
    isFlowing = true;
    try {
        work();
    } finally {
        observer?.takeRecords();
        isFlowing = wasFlowing;
    }
}

/** Settle these boxes now: text has arrived in them that nobody has measured. */
export function requestPassFor(editables: HTMLElement[], reason: string): void {
    flowTriggers(editables, reason);
    editables.forEach(markOverflow);
}

/** Settles when the boundary work asked for so far has finished. For a test to await. */
export function waitForBoundaryWork(): Promise<void> {
    return boundaryWork ?? Promise.resolve();
}

/** One pass over the chains that these boxes belong to. */
function flowTriggers(triggers: HTMLElement[], reason: string): void {
    if (!triggers.length) {
        refreshContinueButtons();
        return;
    }

    isFlowing = true;
    try {
        timePass(reason, () => {
            const settled = new Set<HTMLElement>();
            triggers.forEach((trigger) => {
                if (settled.has(trigger) || !trigger.isConnected) {
                    return;
                }

                // A chain of one group on this page still has work to do: its text may have
                // to go on to a box on a later page, or come back from one.
                const chain = getLanguageChainOnPage(trigger);
                if (!chain.length) {
                    return;
                }

                chain.forEach((box) => settled.add(box));
                if (markRefusals(chain)) {
                    return;
                }

                flowOneChain(trigger, chain);
                pendingBoundaries.add(chain[chain.length - 1]);
            });

            // A box that gained or lost a following box has gained or lost somewhere for its
            // extra text to go, and an emptied box may now be able to take another box's.
            refreshContinueButtons();
        });
    } finally {
        // Our own mutations are queued by now; drop them rather than let them start a pass.
        observer?.takeRecords();
        isFlowing = false;
    }

    // The pass has settled the boxes on this page. Whether the text goes on to the next page
    // is a question for C#, so it is asked after the pass rather than in it.
    startBoundaryWork();
}

function startBoundaryWork(): void {
    if (!pendingBoundaries.size || boundaryWorkStarted) {
        return;
    }

    boundaryWorkStarted = true;
    beginCrossPageRun();
    addRequestPageContentDelay(kDelayId);
    updateReflowingAttr();
    boundaryWork = runBoundaryWork().finally(() => {
        boundaryWork = undefined;
        boundaryWorkStarted = false;
        updateReflowingAttr();
        removeRequestPageContentDelay(kDelayId);
    });
}

async function runBoundaryWork(): Promise<void> {
    for (let round = 0; round < kMaxBoundaryRounds; round++) {
        const boxes = Array.from(pendingBoundaries);
        pendingBoundaries.clear();
        if (!boxes.length) {
            // Nothing left to settle. Not a return: the walks the rounds asked for are still
            // to be requested, below.
            break;
        }

        for (const box of boxes) {
            if (!box.isConnected || !observer) {
                continue;
            }

            const moved = await settleCrossPageBoundary(box, {
                measurer: getMeasurer(),
                measureOverflow: activeOptions.measureOverflow,
                fitProbe: textUpToOffsetFitsInBox,
                applyWithoutPass,
            });
            if (moved) {
                // The box holds different text now, and nothing has measured it: the marker,
                // the indicators, the overflow warning and the offers on the other boxes are
                // all out of date.
                flowTriggers([box], "crossPage");
            }

            // Bloom refuses a move while a refit holds the chain, and says so. That boundary is
            // settled again when the refit reports it is over.
            holdForWalkFinished();

            // A box whose extra text has gone to a later page fits, and its page is not
            // overflowing either. Only a measurement can say so, and the overflow checker does
            // not run again until the user's next keystroke, so ask for one whether or not this
            // pass was the one that moved the text.
            markOverflow(box);
        }
    }

    pendingBoundaries.clear();
    requestWalksWhenQuiet();
}

/**
 * Bloom's word about the flow. walkFinished says a refit of a chain has ended, which is what a
 * boundary Bloom refused during that refit is waiting for.
 */
function onFlowTextEvent(event: IBloomWebSocketEvent): void {
    if (event.id === "walkFinished") {
        void onWalkFinishedFromBloom(event.message);
    }

    // walkQueued says a refit is now waiting to be run, and walkFinished says the ones that
    // were waiting have run: either way, what the bubbles say about this page is out of date.
    if (event.id === "walkQueued" || event.id === "walkFinished") {
        void refreshReflowBubbles(observedContainer ?? document);
    }
}

/** What Bloom sends with walkFinished when the refit leaves the browser something to do. */
interface IWalkFinished {
    /** The page the run of text now ends on, when the refit made pages for it. */
    pageIdToShow?: string | null;
    /** The page being edited, when the refit left it holding nothing but an empty box. */
    pageIdToDelete?: string | null;
}

/**
 * A refit has ended. What it changed in the boxes of this page goes in first, and the page then
 * settles around it: everything below works from what the boxes hold, so a box holding text the
 * refit has replaced would be settled against text that is not in the book.
 *
 * Then one of two things, and both come last because both save the page being edited: what the
 * refit made for a box of this page has to be in that box before it is written to the book.
 * Either the page goes, because the refit left it holding nothing, or the author is taken to
 * the page their text now ends on. A refit that emptied the page being edited names no page to
 * show, because taking the page away moves the Edit tab to the page beside it.
 *
 * The message is empty when the refit left the browser nothing to do.
 */
async function onWalkFinishedFromBloom(message?: string): Promise<void> {
    const finished: IWalkFinished = message ? JSON.parse(message) : {};
    await takeAndApplyRefitResult();
    await settleRefusedBoundaries();

    const page = getPages()[0];
    if (finished.pageIdToDelete && finished.pageIdToDelete === page?.id) {
        // Bloom refuses this when the page is no longer one that may go, and a refusal is the
        // right answer rather than something to report.
        void deleteEmptiedPage(finished.pageIdToDelete).catch(() => undefined);
        return;
    }
    if (finished.pageIdToShow && finished.pageIdToShow !== page?.id) {
        jumpToPage(finished.pageIdToShow);
    }
}

/**
 * Take from Bloom whatever a refit made for the boxes of this page, and put it in. The refit
 * saves the page it changed but leaves the editor alone, so what is on screen is the browser's
 * to bring up to date, and Bloom hands the content over once: it holds a save delay and the
 * page's reflowing mark from before it asks until the content is in.
 */
async function takeAndApplyRefitResult(): Promise<void> {
    const page = getPages()[0];
    if (!(page instanceof HTMLElement) || !page.id) {
        return;
    }

    takingRefitResult = true;
    addRequestPageContentDelay(kDelayId);
    updateReflowingAttr();
    try {
        const boxes = await getRefitResult(page.id);
        if (!boxes.length || !page.isConnected) {
            return;
        }

        const changed = applyRefitResult(page, boxes, {
            measurer: getMeasurer(),
            measureOverflow: activeOptions.measureOverflow,
            fitProbe: textUpToOffsetFitsInBox,
            applyWithoutPass,
        });
        if (changed.length) {
            // The boxes hold text nothing in the browser has measured, so they are settled the
            // way a box is after any other move: the marker, the indicators, the overflow
            // warning and the boundary to the next page are all in question.
            requestPassFor(changed, "refitResult");
        }
    } finally {
        takingRefitResult = false;
        removeRequestPageContentDelay(kDelayId);
        updateReflowingAttr();
    }
}

/**
 * Settles when this page has finished moving its own text and has asked for the refits it
 * wants. The bubble's "reflow now" waits for this, because a refit that ran while the browser
 * still had text to hand to the next page would refuse that move.
 */
async function waitForThisPageToSettle(): Promise<void> {
    await waitForBoundaryWork();
    await waitForWalkRequests();
}

/**
 * Take the save delay and the reflowing mark for a boundary that is waiting for a refit to
 * finish. The text on this page is not where it belongs until that boundary has been settled,
 * so nothing may read the page as settled in the meantime.
 */
function holdForWalkFinished(): void {
    if (retryWaiting || !areBoundaryRetriesWaiting()) {
        return;
    }

    retryWaiting = true;
    addRequestPageContentDelay(kDelayId);
    updateReflowingAttr();
    retryTimeout = window.setTimeout(
        () => void settleRefusedBoundaries(),
        kMaxWaitForWalkFinishedMs,
    );
}

/** Give up the wait without settling anything, because the page is going away. */
function stopWaitingForWalkFinished(): void {
    if (!retryWaiting) {
        return;
    }

    if (retryTimeout !== undefined) {
        clearTimeout(retryTimeout);
        retryTimeout = undefined;
    }

    retryWaiting = false;
    removeRequestPageContentDelay(kDelayId);
}

/**
 * Settle the boundaries Bloom refused while a refit held their chain. The refit has finished,
 * or has taken so long that we have stopped waiting for it.
 *
 * Each pass can move text and want another refit, and that is how it converges: a boundary
 * Bloom refuses again simply waits for the next walkFinished.
 */
async function settleRefusedBoundaries(): Promise<void> {
    if (!retryWaiting) {
        return;
    }

    if (retryTimeout !== undefined) {
        clearTimeout(retryTimeout);
        retryTimeout = undefined;
    }

    // A pass that is still running would drop boxes added to it as it finishes, so the retry
    // goes in after it.
    await waitForBoundaryWork();
    onWalkFinished().forEach((box) => {
        if (box.isConnected) {
            pendingBoundaries.add(box);
        }
    });

    // The delay this wait holds goes back only once the retry pass holds one of its own.
    retryWaiting = false;
    startBoundaryWork();
    removeRequestPageContentDelay(kDelayId);
    updateReflowingAttr();
}

// How long the text has to stand still before the pages after this one are refitted, and how
// often that is checked.
const kQuietBeforeWalkMs = 500;
const kQuietPollMs = 50;

/**
 * Ask Bloom to refit the pages after this one, once this page has stopped moving text.
 *
 * Whatever arrived on the next page has to go on breaking correctly to the end of the chain,
 * and only Bloom can measure the pages the browser is not editing. Two things make the timing
 * of the ask matter, so every ask comes through here:
 *
 *  - A walk in progress refuses the browser's own move, so a walk asked for while this page is
 *    still handing text on would leave this page holding text that does not fit it.
 *  - Moving a page's worth of text is a burst of passes, one boundary each, and a walk reloads
 *    every page it touches. Asked for after each pass, the walks would queue up behind a page
 *    that is still moving.
 *
 * So the ask waits for the text to stand still, and work that starts while it waits inherits
 * the wait: what is wanted (walksWanted, in flowCrossPage) outlives a pass.
 *
 * This returns at once. While it waits, the page carries the reflowing mark and holds a save
 * delay, so nothing reads the page as settled before the refits have been asked for.
 */
export function requestWalksWhenQuiet(): void {
    if (walkWaitStarted || !areWalksWanted()) {
        return;
    }

    walkWaitStarted = true;
    addRequestPageContentDelay(kDelayId);
    updateReflowingAttr();
    walkWait = waitForQuietThenRequestWalks().finally(() => {
        walkWait = undefined;
        walkWaitStarted = false;
        updateReflowingAttr();
        removeRequestPageContentDelay(kDelayId);
    });
}

/** Settles when the refits this page needs have been asked for. */
export function waitForWalkRequests(): Promise<void> {
    return walkWait ?? Promise.resolve();
}

async function waitForQuietThenRequestWalks(): Promise<void> {
    let quietFor = 0;
    while (quietFor < kQuietBeforeWalkMs) {
        await new Promise((resolve) => setTimeout(resolve, kQuietPollMs));
        const busy =
            pendingBoundaries.size > 0 ||
            pendingTriggers.size > 0 ||
            passOutstanding ||
            boundaryWorkStarted ||
            // A boundary still to be settled is text still to move, and a refit asked for now
            // would refuse that move.
            retryWaiting;
        quietFor = busy ? 0 : quietFor + kQuietPollMs;
    }

    await requestQueuedWalks();
    // Those requests are what Bloom now holds as waiting, so the bubbles follow them at once
    // rather than waiting for Bloom to say so. The event arrives as well, and a second refresh
    // costs one read and changes nothing.
    void refreshReflowBubbles(observedContainer ?? document);
}

function markOverflow(editable: HTMLElement): void {
    if (editable.isConnected) {
        activeOptions.markOverflow?.(editable);
    }
}

function updatePageOverflow(page: HTMLElement): void {
    if (page.isConnected) {
        activeOptions.updatePageOverflow?.(page);
    }
}

function flowOneChain(trigger: HTMLElement, chain: HTMLElement[]): void {
    // A marker names the character where a box's text stops fitting, so it means nothing once
    // the text moves: the pass is about to change what each box holds, and a marker left in
    // place would travel with the text it precedes and then mark a point in the middle of
    // another box's text.
    chain.forEach(removeOverflowMarker);

    if (rebalanceChain(trigger, getMeasurer())) {
        // The measurer works from font metrics, so every box it touched needs the real
        // layout's opinion. A box that fits costs one measurement and nothing more.
        verifyAndNudge(
            chain,
            chain.map((_box, index) => index),
            activeOptions.measureOverflow,
        );
    }

    updateIndicators(chain);

    // Only the last box of the chain on this page can have a character at which its text
    // runs out; every earlier one hands its extra text to the box after it. So the marker
    // belongs to the last box alone, and it goes back on only if that box is still
    // overflowing, which on this page means the text needs a box on a later page.
    placeOverflowMarker(
        chain[chain.length - 1],
        getMeasurer(),
        activeOptions.measureOverflow,
    );
}

/**
 * Put the "continue text from the box above" offer on the empty boxes that can take an
 * earlier box's overflow, and take it off the rest. OverflowChecker calls this as well,
 * because a box can start or stop overflowing without any chain existing yet.
 */
function refreshContinueButtons(): void {
    const root = observedContainer ?? document;
    updateContinueButtons(root);
    // The same changes decide which box, if any, is where the run of text ends with more still
    // to place, and so offers to make the pages the rest of it needs.
    updateCreatePagesButtons(root);
    // The same changes decide which box, if any, says its text flows in from an earlier page or
    // out to a later one: a group joins or leaves a chain, or another group of its chain arrives
    // on this page.
    updateFlowFromLabels(root);
    updateFlowToLabels(root);
}

function getMeasurer(): LineMeasurer {
    return activeOptions.measurer ?? getPretextMeasurer();
}

function requestFrame(callback: () => void): number {
    return activeOptions.requestFrame
        ? activeOptions.requestFrame(callback)
        : window.requestAnimationFrame(callback);
}

function cancelFrame(handle: number): void {
    if (activeOptions.cancelFrame) {
        activeOptions.cancelFrame(handle);
    } else {
        window.cancelAnimationFrame(handle);
    }
}

function onCompositionStart(): void {
    isComposing = true;
}

function onCompositionEnd(): void {
    isComposing = false;
    if (passOutstanding) {
        scheduleFrame();
    }
}

/**
 * The attribute says a pass is in progress, and it has to stay on while the boundary work
 * runs: that work is a pass that is waiting for an answer from C#. A boundary waiting for a
 * refit to end counts too, because its text is still to move, and so does taking the content a
 * refit made for a box of this page, which is not in the box until it has been taken.
 */
function updateReflowingAttr(): void {
    setPageReflowing(
        passOutstanding ||
            boundaryWorkStarted ||
            walkWaitStarted ||
            retryWaiting ||
            takingRefitResult,
    );
}

function setPageReflowing(reflowing: boolean): void {
    getPages().forEach((page) => {
        if (reflowing) {
            page.setAttribute(kReflowingAttr, "true");
        } else {
            page.removeAttribute(kReflowingAttr);
        }
    });
}

function getPages(): Element[] {
    if (!observedContainer) {
        return [];
    }

    const ownPage = observedContainer.closest(kPageSelector);
    return ownPage
        ? [ownPage]
        : Array.from(observedContainer.querySelectorAll(kPageSelector));
}
