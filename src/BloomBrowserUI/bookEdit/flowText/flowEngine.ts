// One pass of flow over the boxes of a chain that are on one page.

import {
    getCollapsedSelectionOffsetInChain,
    restoreCollapsedSelectionInChain,
} from "./flowCaret";
import { getLanguageChainOnPage, supportsChainedEditable } from "./flowChain";
import {
    getCombinedChainText,
    normalizeChainedEditable,
    rebalanceAdjacentBoxes,
} from "./flowDomMove";
import { getBoxMetrics, LineMeasurer } from "./flowFit";

/**
 * Settle the text of the trigger's chain across the boxes on the page: what does not fit in
 * a box moves on to the next one, and what now fits comes back. Returns true if any markup
 * changed. The caller supplies the measurer, so a test can decide where the text breaks.
 */
export function rebalanceChain(
    triggerEditable: HTMLElement,
    measurer: LineMeasurer,
): boolean {
    const chain = getLanguageChainOnPage(triggerEditable);
    if (chain.length < 2 || !chain.every(supportsChainedEditable)) {
        return false;
    }

    const selectionState = getCollapsedSelectionOffsetInChain(chain);
    let changed = false;
    // Text pushed out of one box can spill on out of the next, and text pulled back can
    // free room in the box after it, so keep going until a whole sweep changes nothing.
    // Every box can only give and take once per direction, hence the cap.
    for (let pass = 0; pass < chain.length * 2; pass++) {
        let passChanged = false;
        // The loop stops one short of the end on purpose: the last box on the page is never
        // measured, because nothing can move out of it here. Where its text stops fitting is
        // recorded by the overflow marker, and the box after it is on another page.
        for (let index = 0; index < chain.length - 1; index++) {
            passChanged =
                rebalancePair(chain[index], chain[index + 1], measurer) ||
                passChanged;
        }

        changed = changed || passChanged;
        if (!passChanged) {
            break;
        }
    }

    // Normalization can rewrite text nodes even when no text moved, and that drops the
    // caret, so put it back whenever we know where it was.
    if (selectionState !== undefined) {
        restoreCollapsedSelectionInChain(chain, selectionState);
    }

    return changed;
}

function rebalancePair(
    currentEditable: HTMLElement,
    nextEditable: HTMLElement,
    measurer: LineMeasurer,
): boolean {
    // Normalize before measuring: the offset we hand to rebalanceAdjacentBoxes counts
    // characters of the normalized markup, which is what that function works on.
    normalizeChainedEditable(currentEditable);
    normalizeChainedEditable(nextEditable);
    const combinedText = getCombinedChainText(currentEditable, nextEditable);
    const fitOffset = measurer.measureFit(
        combinedText,
        getBoxMetrics(currentEditable),
    );
    return rebalanceAdjacentBoxes(currentEditable, nextEditable, fitOffset);
}
