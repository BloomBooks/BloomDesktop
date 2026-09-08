// Check the split against the real layout, and nudge it by a word where the prediction was
// off. The measurer works from font metrics, so it can be a pixel or two out on a line that
// is nearly full; the browser is the only authority on where the text really broke.

import OverflowChecker from "../OverflowChecker/OverflowChecker";
import { pushOverflowForward } from "./flowDomMove";
import {
    getComparableLinearizedLength,
    linearizeEditable,
} from "./flowLinearize";

/** How much of the box's own content sticks out below it, in CSS pixels. */
export type OverflowMeasurer = (editable: HTMLElement) => number;

// A nudge moves one word, and a box holds at most a few words per line, so a prediction that
// needs more than this many nudges is wrong about something other than the last word.
const kMaxNudgesPerBox = 6;

export function measureOverflowWithLayout(editable: HTMLElement): number {
    return OverflowChecker.getSelfOverflowAmounts(editable)[1];
}

/**
 * Make sure that every box the pass changed, other than the last box on the page, really
 * holds what it was given. Returns true if any text moved.
 *
 * changedIndexes are indexes into chain. The caller may pass every index of the chain; a box
 * that does not overflow costs one measurement and nothing else.
 */
export function verifyAndNudge(
    chain: HTMLElement[],
    changedIndexes: number[],
    measureOverflow: OverflowMeasurer = measureOverflowWithLayout,
): boolean {
    let moved = false;
    // In chain order, so that a word this box hands on can travel further in the same call.
    const indexes = Array.from(new Set(changedIndexes)).sort((a, b) => a - b);
    indexes.forEach((index) => {
        if (index < 0 || index >= chain.length - 1) {
            return;
        }

        moved =
            nudgeUntilItFits(chain[index], chain[index + 1], measureOverflow) ||
            moved;
    });

    return moved;
}

function nudgeUntilItFits(
    editable: HTMLElement,
    nextEditable: HTMLElement,
    measureOverflow: OverflowMeasurer,
): boolean {
    let moved = false;
    for (let nudge = 0; nudge < kMaxNudgesPerBox; nudge++) {
        if (measureOverflow(editable) <= 0) {
            return moved;
        }

        if (!moveLastWordForward(editable, nextEditable)) {
            return moved;
        }

        moved = true;
    }

    return moved;
}

/**
 * Move the last word of the box into the next box. Returns false when there is nothing left
 * to move, which stops the loop rather than emptying the box one grapheme at a time.
 */
function moveLastWordForward(
    editable: HTMLElement,
    nextEditable: HTMLElement,
): boolean {
    const text = linearizeEditable(editable).text;
    const keepOffset = findLastWordStart(
        text.slice(0, getComparableLinearizedLength(text)),
    );
    if (keepOffset === undefined || keepOffset <= 0) {
        return false;
    }

    return pushOverflowForward(editable, nextEditable, keepOffset);
}

/**
 * The offset at which the box's last word begins, counting the whitespace that precedes the
 * word as part of what the box keeps. With no whitespace at all the box holds one word, so
 * the last grapheme is what moves.
 */
function findLastWordStart(text: string): number | undefined {
    const withoutTrailingSpace = text.replace(/\s+$/, "");
    if (!withoutTrailingSpace.length) {
        return undefined;
    }

    for (let index = withoutTrailingSpace.length - 1; index >= 0; index--) {
        if (/\s/.test(withoutTrailingSpace[index])) {
            return index + 1;
        }
    }

    return findLastGraphemeStart(withoutTrailingSpace);
}

function findLastGraphemeStart(text: string): number | undefined {
    let lastStart: number | undefined = undefined;
    for (const part of new Intl.Segmenter(undefined, {
        granularity: "grapheme",
    }).segment(text)) {
        lastStart = part.index;
    }

    return lastStart;
}
