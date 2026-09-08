// When a flow pass runs. Everything that decides what a pass does lives in the other
// modules; this one only watches the page and coalesces the work into one animation frame.

import {
    addRequestPageContentDelay,
    removeRequestPageContentDelay,
} from "../js/bloomEditing";
import { getLanguageChainOnPage } from "./flowChain";
import {
    kChainedGroupSelector,
    kFlowChainAttr,
    kReflowingAttr,
} from "./flowConstants";
import { rebalanceChain } from "./flowEngine";
import { LineMeasurer } from "./flowFit";
import { stripTransientFlowMarkup, updateIndicators } from "./flowIndicators";
import { createPretextMeasurer } from "./flowPretextMeasurer";
import { markRefusals } from "./flowSupport";
import { timePass } from "./flowTiming";
import { OverflowMeasurer, verifyAndNudge } from "./flowVerify";

export type FlowTextOptions = {
    /** Test seam: decide where the text breaks without a layout engine. */
    measurer?: LineMeasurer;
    /** Test seam: script what the real layout would report. */
    measureOverflow?: OverflowMeasurer;
    requestFrame?: (callback: () => void) => number;
    cancelFrame?: (handle: number) => void;
};

const kPageSelector = ".bloom-page";
const kChainedEditableSelector = `${kChainedGroupSelector} > .bloom-editable.bloom-visibility-code-on`;

// The id we hand requestPageContent, so that a save waits for the pass to finish.
const kDelayId = "flowText";

let observer: MutationObserver | undefined;
let observedContainer: HTMLElement | undefined;
let activeOptions: FlowTextOptions = {};
let defaultMeasurer: LineMeasurer | undefined;

const pendingTriggers = new Set<HTMLElement>();
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
    observedContainer = container;
    activeOptions = overrides;

    container.addEventListener("compositionstart", onCompositionStart, true);
    container.addEventListener("compositionend", onCompositionEnd, true);

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
    const fonts = (document as Document & { fonts?: FontFaceSet }).fonts;
    const watchedObserver = observer;
    fonts?.ready?.then(() => {
        if (observer === watchedObserver) {
            reflowAllChainsOnPage("fontsReady");
        }
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

    if (pendingFrame !== undefined) {
        cancelFrame(pendingFrame);
        pendingFrame = undefined;
    }

    finishPass();
    pendingTriggers.clear();
    isComposing = false;
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
    if (!editable || !editable.closest(kChainedGroupSelector)) {
        return [];
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
    setPageReflowing(false);
    removeRequestPageContentDelay(kDelayId);
}

/** One pass over the chains that these boxes belong to. */
function flowTriggers(triggers: HTMLElement[], reason: string): void {
    if (!triggers.length) {
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

                const chain = getLanguageChainOnPage(trigger);
                if (chain.length < 2) {
                    return;
                }

                chain.forEach((box) => settled.add(box));
                if (markRefusals(chain)) {
                    return;
                }

                flowOneChain(trigger, chain);
            });
        });
    } finally {
        // Our own mutations are queued by now; drop them rather than let them start a pass.
        observer?.takeRecords();
        isFlowing = false;
    }
}

function flowOneChain(trigger: HTMLElement, chain: HTMLElement[]): void {
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
}

function getMeasurer(): LineMeasurer {
    if (activeOptions.measurer) {
        return activeOptions.measurer;
    }

    defaultMeasurer = defaultMeasurer ?? createPretextMeasurer();
    return defaultMeasurer;
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
