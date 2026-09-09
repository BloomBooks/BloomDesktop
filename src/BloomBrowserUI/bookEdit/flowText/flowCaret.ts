// Keeping the caret where the user put it while text moves between the boxes of a chain.
// The caret is recorded as one offset into the whole chain's text, so it survives however
// much of the text moves from one box to another.

import { startsWithContinuationParagraph } from "./flowDomMove";
import {
    BoundaryPoint,
    getBoundaryAfterNode,
    getComparableEditableLength,
    getComparableLinearizedLength,
    linearizeEditable,
} from "./flowLinearize";

export type CollapsedSelectionState = {
    /** Characters from the start of the first box of the chain. */
    offset: number;
    /**
     * The caret sat at the very end of its box and the next box continues the same
     * paragraph, so on restore it belongs at the start of that next box.
     */
    preferForwardAtBoundary?: boolean;
};

/**
 * Where the caret is, as an offset into the chain's text, or undefined if there is no
 * collapsed selection inside the chain.
 */
export function getCollapsedSelectionOffsetInChain(
    chain: HTMLElement[],
): CollapsedSelectionState | undefined {
    const selection = window.getSelection();
    if (
        !selection ||
        !selection.isCollapsed ||
        selection.rangeCount === 0 ||
        !selection.anchorNode
    ) {
        return undefined;
    }

    const ownerIndex = chain.findIndex(
        (editable) =>
            editable === selection.anchorNode ||
            editable.contains(selection.anchorNode),
    );
    if (ownerIndex < 0) {
        return undefined;
    }

    let offset = 0;
    for (let index = 0; index < ownerIndex; index++) {
        offset += getComparableEditableLength(chain[index]);
    }

    const ownerContent = linearizeEditable(chain[ownerIndex]);
    const ownerComparableLength = getComparableLinearizedLength(
        ownerContent.text,
    );
    const localOffset =
        findBoundaryOffset(
            ownerContent.points,
            selection.anchorNode,
            selection.anchorOffset,
        ) ??
        getLogicalOffsetWithinEditable(
            chain[ownerIndex],
            selection.anchorNode,
            selection.anchorOffset,
        );
    if (localOffset === undefined) {
        return undefined;
    }

    return {
        offset: offset + localOffset,
        preferForwardAtBoundary:
            ownerIndex < chain.length - 1 &&
            localOffset === ownerComparableLength,
    };
}

/**
 * Put the caret back at the offset that getCollapsedSelectionOffsetInChain() gave.
 *
 * This works from the boundary table rather than from EditableDivUtils.makeSelectionIn(),
 * which counts textContent: the linearized text has a character for each paragraph break
 * and none for the zero-width character of an overflow marker, so the two ways of counting
 * disagree in any box with more than one paragraph or with a marker.
 */
export function restoreCollapsedSelectionInChain(
    chain: HTMLElement[],
    selectionState: CollapsedSelectionState,
): void {
    let remaining = Math.max(0, selectionState.offset);
    for (let index = 0; index < chain.length; index++) {
        const editable = chain[index];
        const comparableLength = getComparableEditableLength(editable);
        // The last box takes whatever is left: a move can leave the chain a character shorter
        // than it was, when the space at a seam goes into an attribute, and a caret that was at
        // the end of the text still belongs at the end of the text.
        if (remaining > comparableLength && index < chain.length - 1) {
            remaining -= comparableLength;
            continue;
        }

        if (
            selectionState.preferForwardAtBoundary &&
            remaining === comparableLength &&
            index < chain.length - 1 &&
            startsWithContinuationParagraph(chain[index + 1])
        ) {
            // The word the caret follows now continues in the next box, so the caret goes
            // to the front of that box rather than to the end of this one.
            remaining = 0;
            continue;
        }

        editable.focus();
        restoreCollapsedSelectionInEditable(editable, remaining);
        return;
    }
}

/**
 * Where the caret is inside this one box, counted the way linearizeEditable() counts, or
 * undefined if the caret is not in the box. This is the form the overflow marker needs: it
 * changes one box's markup and has to put the caret back in the same box.
 */
export function getCollapsedSelectionOffsetInEditable(
    editable: HTMLElement,
): number | undefined {
    return getCollapsedSelectionOffsetInChain([editable])?.offset;
}

/** Put the caret back at an offset that getCollapsedSelectionOffsetInEditable() gave. */
export function restoreCollapsedSelectionInEditable(
    editable: HTMLElement,
    offset: number,
): void {
    const content = linearizeEditable(editable);
    const comparableLength = getComparableLinearizedLength(content.text);
    const wanted = Math.max(0, Math.min(offset, comparableLength));
    const point =
        content.points[wanted] ??
        getLastSelectableBoundary(editable) ??
        content.points[comparableLength];
    const selection = editable.ownerDocument.defaultView?.getSelection();
    if (!point || !selection) {
        return;
    }

    const range = editable.ownerDocument.createRange();
    range.setStart(point.container, point.offset);
    range.collapse(true);
    selection.removeAllRanges();
    selection.addRange(range);
}

/**
 * The offset whose recorded DOM position is exactly this container and offset, or undefined
 * if no offset maps to it.
 */
function findBoundaryOffset(
    points: BoundaryPoint[],
    container: Node,
    offset: number,
): number | undefined {
    for (let index = points.length - 1; index >= 0; index--) {
        const point = points[index];
        if (point && point.container === container && point.offset === offset) {
            return index;
        }
    }

    return undefined;
}

/**
 * The fallback for a DOM position that the boundary table does not hold, such as one inside
 * an element that the walk skipped: count the text before it instead.
 */
function getLogicalOffsetWithinEditable(
    editable: HTMLElement,
    container: Node,
    offset: number,
): number | undefined {
    const result = getLogicalTextToBoundary(editable, container, offset);
    return result.found ? result.text.length : undefined;
}

function getLogicalTextToBoundary(
    node: Node,
    container: Node,
    offset: number,
): { text: string; found: boolean } {
    if (node === container) {
        if (node.nodeType === Node.TEXT_NODE) {
            return {
                text: (node.textContent ?? "").slice(0, offset),
                found: true,
            };
        }

        let text = "";
        const childNodes = Array.from(node.childNodes);
        for (
            let index = 0;
            index < Math.min(offset, childNodes.length);
            index++
        ) {
            text += getLogicalTextToBoundary(
                childNodes[index],
                container,
                offset,
            ).text;
        }

        return { text, found: true };
    }

    if (node.nodeType === Node.TEXT_NODE) {
        return { text: node.textContent ?? "", found: false };
    }

    if (!(node instanceof HTMLElement)) {
        return { text: "", found: false };
    }

    if (node.tagName === "BR" || node.classList.contains("bloom-linebreak")) {
        return { text: "\n", found: false };
    }

    let text = "";
    for (const child of Array.from(node.childNodes)) {
        const childResult = getLogicalTextToBoundary(child, container, offset);
        text += childResult.text;
        if (childResult.found) {
            return { text, found: true };
        }
    }

    if (node.tagName === "P" && !text.endsWith("\n")) {
        text += "\n";
    }

    return { text, found: false };
}

/** The last place in the box that the caret can actually go. */
function getLastSelectableBoundary(
    editable: HTMLElement,
): BoundaryPoint | undefined {
    for (let index = editable.childNodes.length - 1; index >= 0; index--) {
        const boundary = getLastSelectableBoundaryFromNode(
            editable.childNodes[index],
        );
        if (boundary) {
            return boundary;
        }
    }

    return undefined;
}

function getLastSelectableBoundaryFromNode(
    node: Node,
): BoundaryPoint | undefined {
    if (node.nodeType === Node.TEXT_NODE) {
        return {
            container: node,
            offset: node.textContent?.length ?? 0,
        };
    }

    if (node instanceof HTMLBRElement) {
        return getBoundaryAfterNode(node);
    }

    if (!(node instanceof HTMLElement)) {
        return undefined;
    }

    for (let index = node.childNodes.length - 1; index >= 0; index--) {
        const boundary = getLastSelectableBoundaryFromNode(
            node.childNodes[index],
        );
        if (boundary) {
            return boundary;
        }
    }

    return node.tagName === "P"
        ? { container: node, offset: node.childNodes.length }
        : undefined;
}
