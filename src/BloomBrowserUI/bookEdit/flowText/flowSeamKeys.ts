// Backspace and Delete at the seam between two boxes of a chain.
//
// A box is its own contenteditable, so the browser can only delete inside the box the caret
// is in: Backspace at the very start of a box does nothing, and Delete at the very end does
// nothing. To the reader the chain is one run of text, so those two keys have to reach across
// the seam and take the character on the other side of it. The pass that the edit triggers is
// what pulls the following text back.
//
// Only a seam on the same page is handled here. Backspace at the start of the FIRST linked
// box on a page, and Delete at the end of the LAST one, keep today's behavior of doing
// nothing; the box on the other side of that seam is on another page, which is a later phase.

import {
    getCollapsedSelectionOffsetInEditable,
    restoreCollapsedSelectionInEditable,
} from "./flowCaret";
import { getLanguageChainOnPage } from "./flowChain";
import {
    extractEditableFragment,
    normalizeChainedEditable,
} from "./flowDomMove";
import {
    getComparableEditableLength,
    getComparableLinearizedLength,
    linearizeEditable,
} from "./flowLinearize";
import { removeOverflowMarker } from "./flowOverflowMarker";

const kEditableSelector = ".bloom-editable";
// What can sit between the caret and the end of the box while the caret still counts as
// being at the end: whitespace, which the browser gives no caret position after at the end
// of a line, and CKEditor's own filler, U+200B ZERO WIDTH SPACE.
const kNothingButTheEnd = new RegExp(`^[\\s${String.fromCharCode(0x200b)}]*$`);

/**
 * Take the character on the other side of the seam, when the caret is at a seam and the key
 * is one of the two that would otherwise do nothing. Does nothing at all otherwise, so this
 * can sit on the container as a capture-phase keydown listener.
 */
export function handleSeamKey(event: KeyboardEvent): void {
    if (
        event.defaultPrevented ||
        event.altKey ||
        event.ctrlKey ||
        event.metaKey ||
        (event.key !== "Backspace" && event.key !== "Delete")
    ) {
        return;
    }

    const target = event.target as HTMLElement | null;
    const editable = target?.closest<HTMLElement>(kEditableSelector);
    if (!editable) {
        return;
    }

    const chain = getLanguageChainOnPage(editable);
    const index = chain.indexOf(editable);
    if (chain.length < 2 || index < 0) {
        return;
    }

    const caretOffset = getCollapsedSelectionOffsetInEditable(editable);
    if (caretOffset === undefined) {
        return;
    }

    if (event.key === "Backspace") {
        deleteCharacterBeforeBox(event, chain, index, caretOffset);
    } else {
        deleteCharacterAfterBox(event, chain, index, caretOffset);
    }
}

function deleteCharacterBeforeBox(
    event: KeyboardEvent,
    chain: HTMLElement[],
    index: number,
    caretOffset: number,
): void {
    if (caretOffset !== 0 || index === 0) {
        return;
    }

    const previous = chain[index - 1];
    const length = getComparableEditableLength(previous);
    if (length <= 0) {
        return;
    }

    event.preventDefault();
    removeCharacterRange(previous, length - 1, length);
    // The caret goes where the character it deleted was, which is the end of that box. The
    // pass then carries it along with the text it sits in.
    restoreCollapsedSelectionInEditable(
        previous,
        getComparableEditableLength(previous),
    );
}

function deleteCharacterAfterBox(
    event: KeyboardEvent,
    chain: HTMLElement[],
    index: number,
    caretOffset: number,
): void {
    const editable = chain[index];
    if (
        !isCaretAfterLastCharacter(editable, caretOffset) ||
        index === chain.length - 1
    ) {
        return;
    }

    const next = chain[index + 1];
    if (getComparableEditableLength(next) <= 0) {
        return;
    }

    event.preventDefault();
    removeCharacterRange(next, 0, 1);
    // The caret stays at the end of the box the user was typing in, as it does for a Delete
    // anywhere else in the text.
    restoreCollapsedSelectionInEditable(editable, caretOffset);
}

/**
 * Is there nothing left in the box after the caret? Trailing whitespace counts as nothing:
 * the split falls at a word boundary, so a box usually ends with a space, and the browser
 * gives the user no caret position after a space at the end of a line. The space stays where
 * it is; what the key takes is the next character the reader can see, in the box after this
 * one.
 */
function isCaretAfterLastCharacter(
    editable: HTMLElement,
    caretOffset: number,
): boolean {
    const text = linearizeEditable(editable).text;
    const end = getComparableLinearizedLength(text);
    const remainder = text.slice(Math.min(caretOffset, end), end);
    return kNothingButTheEnd.test(remainder);
}

/**
 * Take the characters between two offsets out of a box. The marker goes first: it holds a
 * character of its own, so an offset means one thing in a box that has one and another in a
 * box that does not.
 */
function removeCharacterRange(
    editable: HTMLElement,
    start: number,
    end: number,
): void {
    removeOverflowMarker(editable);
    extractEditableFragment(editable, start, end);
    normalizeChainedEditable(editable);
}
