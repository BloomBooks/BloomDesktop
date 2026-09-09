// The marker that records where a box's text stops fitting in it.
//
// OverflowChecker puts one in every normal-style box that overflows, chained or not, and
// takes it out again when the box fits. It is saved with the page, so C# can split the
// paragraph at that character without measuring any text.
//
// The point is found the way the flow engine finds a split: the measurer proposes an offset
// from font metrics, and the real layout confirms it, one word at a time.

import {
    getCollapsedSelectionOffsetInEditable,
    restoreCollapsedSelectionInEditable,
} from "./flowCaret";
import { isCkEditorBookmark } from "./flowChain";
import {
    kOverflowMarkerContent,
    kOverflowMarkerSelector,
    kOverflowStartClass,
} from "./flowConstants";
import { getBoxMetrics, LineMeasurer } from "./flowFit";
import {
    BoundaryPoint,
    findNextWordStart,
    getBoundaryAfterNode,
    getComparableLinearizedLength,
    isOverflowMarkerNode,
    linearizeEditable,
} from "./flowLinearize";
// A type-only import, so that this module adds no load-time edge back to OverflowChecker.
import type { OverflowMeasurer } from "./flowVerify";

/**
 * Whether the box's text up to this offset really fits, according to the browser's own
 * layout. A test supplies its own, because jsdom lays nothing out.
 */
export type MarkerFitProbe = (editable: HTMLElement, offset: number) => boolean;

// The overflow checker allows a line to sit a fraction of a pixel below the content box, so
// we allow the same rather than reporting a line as unfitting that Bloom calls fitting.
const kFitTolerancePixels = 1;

/**
 * Put the marker at the character where the box's own text stops fitting, and return that
 * offset. Removes the marker and returns undefined when the box does not overflow.
 *
 * The caret keeps its place: the offset is captured before the markup changes and restored
 * after, and the marker itself goes in with Range operations on the existing text node, so
 * nothing else in the box is rewritten.
 */
export function placeOverflowMarker(
    editable: HTMLElement,
    measurer: LineMeasurer,
    measureOverflow?: OverflowMeasurer,
    fitProbe: MarkerFitProbe = textUpToOffsetFitsInBox,
): number | undefined {
    const content = linearizeEditable(editable);
    const comparableLength = getComparableLinearizedLength(content.text);
    // A caller that has already measured the box hands its answer in. Without one, asking
    // the probe whether all of the text fits answers the same question, and keeps the
    // decision to mark and the choice of where to mark on one measurement.
    const overflows = measureOverflow
        ? measureOverflow(editable) > 0
        : !fitProbe(editable, comparableLength);
    if (comparableLength <= 0 || !overflows) {
        removeOverflowMarker(editable);
        return undefined;
    }

    const proposal = Math.max(
        0,
        Math.min(
            measurer.measureFit(content.text, getBoxMetrics(editable)),
            comparableLength,
        ),
    );
    const offset = findFitOffsetInBox(
        editable,
        content.text,
        proposal,
        comparableLength,
        fitProbe,
    );
    if (offset <= 0 || offset >= comparableLength) {
        // Not one word fits, or all of it does. Either way there is no character inside the
        // text at which the fit ends, so there is nothing to mark.
        removeOverflowMarker(editable);
        return undefined;
    }

    if (getOverflowMarkerOffset(editable) === offset) {
        return offset;
    }

    insertMarkerAtOffset(editable, offset);
    return getOverflowMarkerOffset(editable);
}

/** Take every marker out of the box. */
export function removeOverflowMarker(editable: HTMLElement): void {
    const markers = Array.from(
        editable.querySelectorAll(kOverflowMarkerSelector),
    );
    if (!markers.length) {
        return;
    }

    const caretOffset = getCollapsedSelectionOffsetInEditable(editable);
    markers.forEach((marker) => marker.remove());
    if (caretOffset !== undefined) {
        restoreCollapsedSelectionInEditable(editable, caretOffset);
    }
}

export function hasOverflowMarker(editable: HTMLElement): boolean {
    return editable.querySelector(kOverflowMarkerSelector) !== null;
}

/**
 * How many characters of the box's text come before the marker, or undefined when the box
 * holds no marker.
 */
export function getOverflowMarkerOffset(
    editable: HTMLElement,
): number | undefined {
    return linearizeEditable(editable).markerOffsets[0];
}

export function createOverflowMarker(document: Document): HTMLSpanElement {
    const marker = document.createElement("span");
    marker.className = kOverflowStartClass;
    marker.appendChild(document.createTextNode(kOverflowMarkerContent));
    return marker;
}

/**
 * The last offset at which the box's text still fits, according to the real layout.
 *
 * The prediction from font metrics is only a starting point: it can be several words out,
 * because it lays the text out from advance widths rather than from the box the browser
 * actually built. So the search starts there and brackets the answer by doubling its step,
 * then closes the bracket by halving it. That is sound because the height a range occupies
 * never falls as the range grows, so exactly one boundary separates the offsets that fit
 * from the ones that do not.
 *
 * The candidates are the word starts, plus the prediction itself: a box whose text has no
 * whitespace at all has no word starts, and there the prediction is all we have.
 *
 * The text must be the text the box currently holds, because the probe measures the box: a
 * caller that wants the fit offset for other text puts that text in the box first.
 */
export function findFitOffsetInBox(
    editable: HTMLElement,
    text: string,
    proposal: number,
    comparableLength: number,
    fitProbe: MarkerFitProbe = textUpToOffsetFitsInBox,
): number {
    const candidates = getFitCandidates(text, proposal, comparableLength);
    if (!candidates.length) {
        return proposal;
    }

    const fits = (index: number) => fitProbe(editable, candidates[index]);
    // low is the last index known to fit, or -1 for "not even the first candidate"; high is
    // the first index known not to fit, or the length for "all of them fit".
    let low = -1;
    let high = candidates.length;
    const start = getNearestCandidateIndex(candidates, proposal);

    if (fits(start)) {
        low = start;
        let step = 1;
        while (low + step < candidates.length && fits(low + step)) {
            low += step;
            step *= 2;
        }
        high = Math.min(low + step, candidates.length);
    } else {
        high = start;
        let step = 1;
        while (high - step >= 0 && !fits(high - step)) {
            high -= step;
            step *= 2;
        }
        low = Math.max(high - step, -1);
    }

    while (high - low > 1) {
        const middle = low + Math.floor((high - low) / 2);
        if (fits(middle)) {
            low = middle;
        } else {
            high = middle;
        }
    }

    return low < 0 ? 0 : candidates[low];
}

/**
 * The offsets the marker may take, in ascending order: the start of every word but the first.
 * The prediction only says where to start looking; it is a count of characters, and a marker
 * put at it would cut a word in two. It is a candidate of its own only when the text has no
 * word starts at all, so that one unbroken run of characters can still be cut.
 */
function getFitCandidates(
    text: string,
    proposal: number,
    comparableLength: number,
): number[] {
    const candidates: number[] = [];
    let offset = findNextWordStart(text, 0);
    while (offset !== undefined && offset < comparableLength) {
        candidates.push(offset);
        offset = findNextWordStart(text, offset);
    }

    if (!candidates.length && proposal > 0 && proposal < comparableLength) {
        candidates.push(proposal);
    }

    return candidates;
}

/** The last candidate at or before the prediction, or the first of them. */
function getNearestCandidateIndex(
    candidates: number[],
    proposal: number,
): number {
    let index = 0;
    while (index + 1 < candidates.length && candidates[index + 1] <= proposal) {
        index++;
    }

    return index;
}

function insertMarkerAtOffset(editable: HTMLElement, offset: number): void {
    const caretOffset = getCollapsedSelectionOffsetInEditable(editable);
    Array.from(editable.querySelectorAll(kOverflowMarkerSelector)).forEach(
        (marker) => marker.remove(),
    );

    // The offsets of the remaining text do not change when a marker comes out, but the DOM
    // positions do, so read them again.
    const point = getInsertionPoint(editable, offset);
    if (point) {
        const range = editable.ownerDocument.createRange();
        range.setStart(point.container, point.offset);
        range.collapse(true);
        // insertNode splits the text node for us, which is the whole point of using a Range
        // rather than rewriting the markup.
        range.insertNode(createOverflowMarker(editable.ownerDocument));
    }

    if (caretOffset !== undefined) {
        restoreCollapsedSelectionInEditable(editable, caretOffset);
    }
}

/**
 * Where offset sits in the DOM, moved out of any span that must stay whole: a soft line
 * break carries meaning as an empty element, and a CKEditor bookmark is the editor's own.
 */
function getInsertionPoint(
    editable: HTMLElement,
    offset: number,
): BoundaryPoint | undefined {
    const point = linearizeEditable(editable).points[offset];
    if (!point) {
        return undefined;
    }

    const element =
        point.container.nodeType === Node.ELEMENT_NODE
            ? (point.container as HTMLElement)
            : point.container.parentElement;
    const unsplittable = element?.closest<HTMLElement>(
        "span.bloom-linebreak, span[id^='cke_bm_']",
    );
    if (
        unsplittable &&
        (unsplittable.classList.contains("bloom-linebreak") ||
            isCkEditorBookmark(unsplittable))
    ) {
        return getBoundaryAfterNode(unsplittable);
    }

    return point;
}

/**
 * End the measuring range at this offset, but on the near side of a marker that already sits
 * there. A marker of the previous pass is laid out at the start of the line that does not
 * fit, and a range that reaches it takes that line's bottom as its own; the measurement then
 * says the box overflows at the very offset the marker records, and each pass moves the
 * marker back a word for the next one to move it forward again.
 */
export function setFitRangeEnd(range: Range, point: BoundaryPoint): void {
    let container = point.container;
    let offset = point.offset;
    if (container.nodeType === Node.TEXT_NODE && offset === 0) {
        const previous = container.previousSibling;
        const parent = container.parentNode;
        if (previous && parent && isOverflowMarkerNode(previous)) {
            offset = Array.from(parent.childNodes).indexOf(previous);
            container = parent;
        }
    }

    while (
        container.nodeType === Node.ELEMENT_NODE &&
        offset > 0 &&
        isOverflowMarkerNode(container.childNodes[offset - 1])
    ) {
        offset--;
    }

    range.setEnd(container, offset);
}

/**
 * Does the text up to this offset fit in the box, as the browser laid it out? The rects of
 * a Range give one box per line, so the bottom of the last of them is the bottom of the
 * line the offset falls on.
 */
function textUpToOffsetFitsInBox(
    editable: HTMLElement,
    offset: number,
): boolean {
    const point = linearizeEditable(editable).points[offset];
    if (!point) {
        return true;
    }

    const range = editable.ownerDocument.createRange();
    range.setStart(editable, 0);
    setFitRangeEnd(range, point);

    let lineBottom: number | undefined = undefined;
    Array.from(range.getClientRects()).forEach((rect) => {
        if (
            rect.height > 0 &&
            (lineBottom === undefined || rect.bottom > lineBottom)
        ) {
            lineBottom = rect.bottom;
        }
    });
    if (lineBottom === undefined) {
        return true;
    }

    const computed = window.getComputedStyle(editable);
    const contentTop =
        editable.getBoundingClientRect().top +
        (parseFloat(computed.borderTopWidth) || 0) +
        (parseFloat(computed.paddingTop) || 0);
    const used = lineBottom - contentTop + editable.scrollTop;
    return used <= getBoxMetrics(editable).height + kFitTolerancePixels;
}
