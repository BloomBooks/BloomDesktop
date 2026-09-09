// Moving text between two boxes of a chain, and keeping the markup of a chained box in the
// shape the rest of the flow code expects.
//
// The one structural rule: a box holds top-level <p> elements only, and text that spilled
// out of the middle of a paragraph in the previous box arrives as the FIRST <p> of this
// box, marked with the continuation attribute. That marker is what lets us join the two
// halves back together before the next fit, so the words never get re-ordered.

import { EditableDivUtils } from "../js/editableDivUtils";
import { supportsChainedPair } from "./flowChain";
import {
    kContinuationAttr,
    kContinuationAttrValue,
    kOverflowMarkerSelector,
} from "./flowConstants";
import { suppressesOverflowMarking } from "./flowIndicators";
import {
    getLastTopLevelParagraph,
    getParagraphSegments,
    getTopLevelParagraphs,
    isIgnorableNode,
    linearizeEditable,
    ParagraphSegment,
    resolveEndBoundary,
    resolveStartBoundary,
} from "./flowLinearize";

export type ExtractedFlowFragment = {
    fragment: DocumentFragment;
    /** The extraction began in the middle of a paragraph, not at its start. */
    startInsideParagraph: boolean;
    /** The extraction ended in the middle of a paragraph, not at its end. */
    endInsideParagraph: boolean;
};

/**
 * Move everything after keepOffset out of currentEditable and into the front of
 * nextEditable. keepOffset counts characters of the linearized text of currentEditable.
 */
export function pushOverflowForward(
    currentEditable: HTMLElement,
    nextEditable: HTMLElement,
    keepOffset: number,
): boolean {
    if (!supportsChainedPair(currentEditable, nextEditable)) {
        return false;
    }

    const currentContent = linearizeEditable(currentEditable);
    if (keepOffset < 0 || keepOffset >= currentContent.text.length) {
        return false;
    }

    const fragment = extractEditableFragment(
        currentEditable,
        keepOffset,
        currentContent.text.length,
    );
    if (!fragment.fragment.hasChildNodes()) {
        return false;
    }

    prependFragmentToEditable(nextEditable, fragment);
    normalizeChainedEditable(currentEditable);
    normalizeChainedEditable(nextEditable);
    return true;
}

/**
 * Move the first pullCount characters of nextEditable back onto the end of currentEditable.
 */
export function pullOverflowBackward(
    currentEditable: HTMLElement,
    nextEditable: HTMLElement,
    pullCount: number,
): boolean {
    if (!supportsChainedPair(currentEditable, nextEditable) || pullCount <= 0) {
        return false;
    }

    const nextContent = linearizeEditable(nextEditable);
    const clampedCount = Math.min(pullCount, nextContent.text.length);
    if (clampedCount <= 0) {
        return false;
    }

    const fragment = extractEditableFragment(nextEditable, 0, clampedCount);
    if (!fragment.fragment.hasChildNodes()) {
        return false;
    }

    appendFragmentToEditable(currentEditable, nextEditable, fragment);
    normalizeChainedEditable(currentEditable);
    normalizeChainedEditable(nextEditable);
    return true;
}

/**
 * Put the first fitOffset characters of the two boxes' combined text in currentEditable and
 * the rest in nextEditable. fitOffset counts characters of the text that
 * getCombinedChainText() returns for the same pair. This both pushes overflow forward and
 * pulls text back, so one call settles a pair in either direction.
 */
export function rebalanceAdjacentBoxes(
    currentEditable: HTMLElement,
    nextEditable: HTMLElement,
    fitOffset: number,
): boolean {
    normalizeChainedEditable(currentEditable);
    normalizeChainedEditable(nextEditable);

    // Build the result away from the live boxes, and only touch them if it differs. Replacing
    // a box's children while the caret is inside drops the caret, and it happens on every
    // keystroke, so a pass that changes nothing must leave the boxes alone.
    const combined = buildCombinedEditable(currentEditable, nextEditable);
    const combinedContent = linearizeEditable(combined);
    const splitOffset = Math.min(fitOffset, combinedContent.text.length);

    const currentFragment = extractEditableFragment(combined, 0, splitOffset);
    const newCurrent = document.createElement("div");
    setEditableContentFromFragment(newCurrent, currentFragment.fragment);

    if (currentFragment.endInsideParagraph) {
        markFirstParagraphAsContinuation(combined);
    }

    // newCurrent becomes a box that has another box after it on the page, so nothing in it
    // can be the character at which the chain's text stops fitting.
    Array.from(newCurrent.querySelectorAll(kOverflowMarkerSelector)).forEach(
        (marker) => marker.remove(),
    );
    normalizeChainedEditable(newCurrent);
    normalizeChainedEditable(combined);

    const currentChanged = currentEditable.innerHTML !== newCurrent.innerHTML;
    const nextChanged = nextEditable.innerHTML !== combined.innerHTML;
    if (currentChanged) {
        setEditableContentFromContainer(currentEditable, newCurrent);
    }
    if (nextChanged) {
        setEditableContentFromContainer(nextEditable, combined);
    }

    return currentChanged || nextChanged;
}

/**
 * The text of the two boxes as one string, with a continuation paragraph at the head of
 * nextEditable joined back onto the tail paragraph of currentEditable. This is the text
 * that a fitOffset for rebalanceAdjacentBoxes() must be measured against.
 */
export function getCombinedChainText(
    currentEditable: HTMLElement,
    nextEditable: HTMLElement,
): string {
    const combined = buildCombinedEditable(currentEditable, nextEditable);
    return linearizeEditable(combined).text;
}

/**
 * A detached copy of the two boxes' content, with a continuation paragraph joined back onto
 * the paragraph it broke off from. Neither box is changed.
 */
function buildCombinedEditable(
    currentEditable: HTMLElement,
    nextEditable: HTMLElement,
): HTMLElement {
    // A box that holds nothing but its placeholder <p><br></p> contributes no text at all.
    // Cloning that paragraph in would make it a real, empty paragraph of the combined
    // content, and the split would then hand one of the boxes a paragraph break the user
    // never typed; the caret lands after it, and everything typed next goes into the wrong
    // paragraph.
    const combined = document.createElement("div");
    if (!isEmptyChainedEditable(currentEditable)) {
        currentEditable.childNodes.forEach((child) => {
            combined.appendChild(child.cloneNode(true));
        });
    }

    const nextClone = document.createElement("div");
    if (!isEmptyChainedEditable(nextEditable)) {
        nextEditable.childNodes.forEach((child) => {
            nextClone.appendChild(child.cloneNode(true));
        });
    }
    const nextParagraphs = getTopLevelParagraphs(nextClone);
    const combinedLastParagraph = getLastTopLevelParagraph(combined);
    if (
        combinedLastParagraph &&
        nextParagraphs[0]?.hasAttribute(kContinuationAttr)
    ) {
        const firstContinuationParagraph = nextParagraphs[0];
        clearPlaceholderLineBreak(combinedLastParagraph);
        clearPlaceholderLineBreak(firstContinuationParagraph);
        while (firstContinuationParagraph.firstChild) {
            combinedLastParagraph.appendChild(
                firstContinuationParagraph.firstChild,
            );
        }

        firstContinuationParagraph.remove();
    }

    nextClone.childNodes.forEach((child) => {
        combined.appendChild(child);
    });
    return combined;
}

/**
 * Take the characters from startOffset to endOffset out of the editable and return them as
 * paragraphs. A partly emptied paragraph keeps its own attributes; a paragraph left with
 * nothing in it is removed.
 */
export function extractEditableFragment(
    editable: HTMLElement,
    startOffset: number,
    endOffset: number,
): ExtractedFlowFragment {
    if (startOffset >= endOffset) {
        return makeEmptyExtraction();
    }

    const segments = getParagraphSegments(editable);
    const startBoundary = resolveStartBoundary(segments, startOffset);
    const endBoundary = resolveEndBoundary(segments, endOffset);
    if (!startBoundary || !endBoundary) {
        return makeEmptyExtraction();
    }

    const fragment = document.createDocumentFragment();
    const startSegment = segments[startBoundary.segmentIndex];
    const endSegment = segments[endBoundary.segmentIndex];
    const startInsideParagraph = startBoundary.localOffset > 0;
    const endInsideParagraph = endBoundary.localOffset < endSegment.textLength;

    if (startBoundary.segmentIndex === endBoundary.segmentIndex) {
        appendPartialParagraphExtraction(
            fragment,
            startSegment,
            startBoundary.localOffset,
            endBoundary.localOffset,
        );
        removeParagraphIfEmpty(startSegment.paragraph);
        return { fragment, startInsideParagraph, endInsideParagraph };
    }

    appendPartialParagraphExtraction(
        fragment,
        startSegment,
        startBoundary.localOffset,
        startSegment.textLength,
    );
    removeParagraphIfEmpty(startSegment.paragraph);

    for (
        let index = startBoundary.segmentIndex + 1;
        index < endBoundary.segmentIndex;
        index++
    ) {
        fragment.appendChild(segments[index].paragraph);
    }

    if (endBoundary.localOffset > 0) {
        appendPartialParagraphExtraction(
            fragment,
            endSegment,
            0,
            endBoundary.localOffset,
        );
        removeParagraphIfEmpty(endSegment.paragraph);
    }

    return { fragment, startInsideParagraph, endInsideParagraph };
}

function makeEmptyExtraction(): ExtractedFlowFragment {
    return {
        fragment: document.createDocumentFragment(),
        startInsideParagraph: false,
        endInsideParagraph: false,
    };
}

/** Put an extraction at the front of a box, joining paragraphs that belong together. */
export function prependFragmentToEditable(
    editable: HTMLElement,
    extracted: ExtractedFlowFragment,
): void {
    ensureFragmentParagraphStructure(extracted.fragment);
    clearEditablePlaceholderParagraph(editable);
    const existingFirstParagraph = getTopLevelParagraphs(editable)[0];
    const insertedParagraphs = getTopLevelParagraphs(extracted.fragment);
    const lastInsertedParagraph =
        insertedParagraphs[insertedParagraphs.length - 1];
    if (extracted.startInsideParagraph) {
        markFirstParagraphAsContinuation(extracted.fragment);
    }

    editable.insertBefore(extracted.fragment, editable.firstChild);
    if (
        existingFirstParagraph?.hasAttribute(kContinuationAttr) &&
        lastInsertedParagraph
    ) {
        mergeAdjacentParagraphs(
            lastInsertedParagraph,
            existingFirstParagraph,
            true,
        );
    }
}

/**
 * Put an extraction at the end of a box. sourceEditable is the box the text came out of; if
 * the extraction stopped in the middle of one of its paragraphs, what is left of that
 * paragraph becomes a continuation.
 */
export function appendFragmentToEditable(
    editable: HTMLElement,
    sourceEditable: HTMLElement,
    extracted: ExtractedFlowFragment,
): void {
    ensureFragmentParagraphStructure(extracted.fragment);
    clearEditablePlaceholderParagraph(editable);
    const currentLastParagraph = getLastTopLevelParagraph(editable);
    const fragmentFirstParagraph = getTopLevelParagraphs(extracted.fragment)[0];

    while (extracted.fragment.firstChild) {
        editable.appendChild(extracted.fragment.firstChild);
    }

    if (
        currentLastParagraph &&
        fragmentFirstParagraph?.hasAttribute(kContinuationAttr)
    ) {
        mergeAdjacentParagraphs(currentLastParagraph, fragmentFirstParagraph);
    }

    if (extracted.endInsideParagraph) {
        markFirstParagraphAsContinuation(sourceEditable);
    }
}

/**
 * Pour nextParagraph into previousParagraph and remove it. Set keepContinuationOnMerged
 * when previousParagraph is itself the tail of a paragraph that starts in an earlier box.
 */
export function mergeAdjacentParagraphs(
    previousParagraph: HTMLParagraphElement,
    nextParagraph: HTMLParagraphElement,
    keepContinuationOnMerged?: boolean,
): void {
    clearPlaceholderLineBreak(previousParagraph);
    clearPlaceholderLineBreak(nextParagraph);
    while (nextParagraph.firstChild) {
        previousParagraph.appendChild(nextParagraph.firstChild);
    }

    if (keepContinuationOnMerged) {
        markParagraphAsContinuation(previousParagraph);
    } else {
        clearParagraphContinuation(previousParagraph);
    }
    nextParagraph.remove();
}

export function markFirstParagraphAsContinuation(container: ParentNode): void {
    const firstParagraph = getTopLevelParagraphs(container)[0];
    if (firstParagraph) {
        markParagraphAsContinuation(firstParagraph);
    }
}

export function startsWithContinuationParagraph(
    editable: HTMLElement,
): boolean {
    return (
        getTopLevelParagraphs(editable)[0]?.hasAttribute(kContinuationAttr) ===
        true
    );
}

/**
 * A continuation paragraph must not take the first-line indent of its style: the line it
 * continues already started in the previous box. The attribute alone carries that; the
 * stylesheet rule for p[data-flow-continuation] does the rest. Never add or remove the
 * "No Indent" class here, because it is the user's own choice and we cannot tell it apart
 * from one of ours.
 */
function markParagraphAsContinuation(paragraph: HTMLParagraphElement): void {
    paragraph.setAttribute(kContinuationAttr, kContinuationAttrValue);
}

function clearParagraphContinuation(paragraph: HTMLParagraphElement): void {
    paragraph.removeAttribute(kContinuationAttr);
}

/** Bring a chained box back to the markup shape the flow code relies on. */
export function normalizeChainedEditable(editable: HTMLElement): void {
    EditableDivUtils.normalizeBloomLineBreakSpansInElement(editable);
    ensureChainedParagraphStructure(editable);
    stripStrayContinuationMarkers(editable);
    pruneOverflowMarkers(editable);
    pruneEmptyInlineElements(editable);
    mergeAdjacentEquivalentInlineElements(editable);
    ensureEmptyParagraphsHaveLineBreaks(editable);
    EditableDivUtils.fixUpEmptyishParagraphs(editable);
    editable.normalize();
}

function ensureChainedParagraphStructure(editable: HTMLElement): void {
    if (
        editable.classList.contains("bloom-noParagraphs") ||
        editable.classList.contains("WordFind-style")
    ) {
        return;
    }

    const meaningfulNodes = Array.from(editable.childNodes).filter(
        (child) => !isIgnorableNode(child),
    );
    if (meaningfulNodes.length === 0) {
        const paragraph = document.createElement("p");
        paragraph.appendChild(document.createElement("br"));
        editable.replaceChildren(paragraph);
        return;
    }

    if (getTopLevelParagraphs(editable).length > 0) {
        return;
    }

    const newParagraph = document.createElement("p");
    while (editable.firstChild) {
        newParagraph.appendChild(editable.firstChild);
    }
    editable.appendChild(newParagraph);
}

/**
 * Only the first paragraph of a box can be the tail of a paragraph in the previous box, so
 * a marker anywhere else is stale.
 */
function stripStrayContinuationMarkers(editable: HTMLElement): void {
    getTopLevelParagraphs(editable)
        .slice(1)
        .forEach((paragraph) => paragraph.removeAttribute(kContinuationAttr));
}

/**
 * A box holds at most one overflow marker, and only the last linked box on the page holds
 * one at all: a box that hands its extra text to a following box has no character at which
 * its text stops fitting. A detached container is not on any page, so we cannot tell where
 * it belongs and keep the first marker; the caller strips it if the container is the head of
 * a pair.
 */
function pruneOverflowMarkers(editable: HTMLElement): void {
    const markers = Array.from(
        editable.querySelectorAll(kOverflowMarkerSelector),
    );
    const keep =
        editable.isConnected && suppressesOverflowMarking(editable)
            ? undefined
            : markers[0];
    markers.forEach((marker) => {
        if (marker !== keep) {
            marker.remove();
        }
    });
}

function ensureEmptyParagraphsHaveLineBreaks(editable: HTMLElement): void {
    getTopLevelParagraphs(editable).forEach((paragraph) => {
        const hasMeaningfulContent = Array.from(paragraph.childNodes).some(
            (child) => {
                if (child instanceof HTMLBRElement) {
                    return true;
                }

                if (child.nodeType === Node.TEXT_NODE) {
                    return !!child.textContent;
                }

                return (
                    child instanceof HTMLElement &&
                    hasMeaningfulElementContent(child)
                );
            },
        );
        if (!hasMeaningfulContent) {
            paragraph.replaceChildren();
            paragraph.appendChild(document.createElement("br"));
        }
    });
}

function pruneEmptyInlineElements(root: ParentNode): void {
    Array.from(root.childNodes).forEach((child) => {
        if (!(child instanceof HTMLElement)) {
            return;
        }

        pruneEmptyInlineElements(child);
        if (
            child.tagName !== "P" &&
            child.tagName !== "BR" &&
            !child.id.startsWith("cke_bm_") &&
            !hasMeaningfulElementContent(child)
        ) {
            child.remove();
        }
    });
}

function hasMeaningfulElementContent(element: HTMLElement): boolean {
    if (element instanceof HTMLBRElement) {
        return true;
    }

    return Array.from(element.childNodes).some((child) => {
        if (child instanceof HTMLBRElement) {
            return true;
        }

        if (child.nodeType === Node.TEXT_NODE) {
            return !!child.textContent;
        }

        return (
            child instanceof HTMLElement && hasMeaningfulElementContent(child)
        );
    });
}

/**
 * A split inside <strong>world</strong> leaves <strong>wo</strong><strong>rld</strong> when
 * the halves come back together. Join runs that carry the same markup.
 */
function mergeAdjacentEquivalentInlineElements(root: ParentNode): void {
    const childNodes = Array.from(root.childNodes);
    for (let index = 0; index < childNodes.length - 1; index++) {
        const current = childNodes[index];
        const next = childNodes[index + 1];
        if (
            current instanceof HTMLElement &&
            next instanceof HTMLElement &&
            canMergeInlineElements(current, next)
        ) {
            while (next.firstChild) {
                current.appendChild(next.firstChild);
            }

            next.remove();
            mergeAdjacentEquivalentInlineElements(current);
            mergeAdjacentEquivalentInlineElements(root);
            return;
        }

        if (current instanceof HTMLElement) {
            mergeAdjacentEquivalentInlineElements(current);
        }
    }

    const lastChild = childNodes[childNodes.length - 1];
    if (lastChild instanceof HTMLElement) {
        mergeAdjacentEquivalentInlineElements(lastChild);
    }
}

function canMergeInlineElements(
    current: HTMLElement,
    next: HTMLElement,
): boolean {
    if (
        current.tagName !== next.tagName ||
        current.tagName === "BR" ||
        current.tagName === "P"
    ) {
        return false;
    }

    // Compare the start and end tags by taking the content out of the serialization.
    return (
        current.outerHTML.replace(current.innerHTML, "") ===
        next.outerHTML.replace(next.innerHTML, "")
    );
}

function setEditableContentFromFragment(
    editable: HTMLElement,
    fragment: DocumentFragment,
): void {
    editable.replaceChildren();
    while (fragment.firstChild) {
        editable.appendChild(fragment.firstChild);
    }
}

function setEditableContentFromContainer(
    editable: HTMLElement,
    container: HTMLElement,
): void {
    editable.replaceChildren();
    while (container.firstChild) {
        editable.appendChild(container.firstChild);
    }
}

function clearPlaceholderLineBreak(paragraph: HTMLParagraphElement): void {
    if (isPlaceholderParagraph(paragraph)) {
        paragraph.replaceChildren();
    }
}

function clearEditablePlaceholderParagraph(editable: HTMLElement): void {
    const paragraphs = getTopLevelParagraphs(editable);
    if (paragraphs.length === 1 && isPlaceholderParagraph(paragraphs[0])) {
        editable.replaceChildren();
    }
}

/** The box holds no text at all: one paragraph, and nothing in it but a line break. */
function isEmptyChainedEditable(editable: HTMLElement): boolean {
    const paragraphs = getTopLevelParagraphs(editable);
    return paragraphs.length === 1 && isPlaceholderParagraph(paragraphs[0]);
}

function isPlaceholderParagraph(paragraph: HTMLParagraphElement): boolean {
    return (
        paragraph.childNodes.length === 1 &&
        paragraph.firstChild instanceof HTMLBRElement
    );
}

/** Wrap anything in the fragment that is not already in a paragraph. */
function ensureFragmentParagraphStructure(fragment: DocumentFragment): void {
    let currentParagraph: HTMLParagraphElement | undefined;
    Array.from(fragment.childNodes).forEach((child) => {
        if (child instanceof HTMLParagraphElement) {
            currentParagraph = undefined;
            return;
        }

        if (isIgnorableNode(child)) {
            child.remove();
            return;
        }

        if (!currentParagraph) {
            currentParagraph = document.createElement("p");
            fragment.insertBefore(currentParagraph, child);
        }

        currentParagraph.appendChild(child);
    });
}

function appendPartialParagraphExtraction(
    destination: DocumentFragment,
    segment: ParagraphSegment,
    startLocalOffset: number,
    endLocalOffset: number,
): void {
    if (startLocalOffset >= endLocalOffset) {
        return;
    }

    const range = document.createRange();
    const startPoint = segment.points[startLocalOffset];
    const endPoint = segment.points[endLocalOffset];
    range.setStart(startPoint.container, startPoint.offset);
    range.setEnd(endPoint.container, endPoint.offset);

    const extractedContent = range.extractContents();
    if (!extractedContent.hasChildNodes()) {
        return;
    }

    const paragraph = segment.paragraph.cloneNode(
        false,
    ) as HTMLParagraphElement;
    paragraph.appendChild(extractedContent);
    destination.appendChild(paragraph);
}

function removeParagraphIfEmpty(paragraph: HTMLParagraphElement): void {
    if (!hasMeaningfulElementContent(paragraph)) {
        paragraph.remove();
    }
}
