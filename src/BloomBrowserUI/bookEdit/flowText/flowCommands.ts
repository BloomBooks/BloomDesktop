// The commands the user gives the flow directly, as opposed to the passes it runs by itself.
//
// There is only one: Unlink. Linking is done by the button an empty box offers (see
// flowContinueButton), so there is no command for it.

import { unlinkFrom } from "./flowBoundaryClient";
import { getFlowGroupsOfPage } from "./flowChain";
import {
    kContinuationAttr,
    kFlowChainAttr,
    kHasNextClass,
    kHasPrevClass,
    kSeamSpaceAttr,
} from "./flowConstants";

const kTranslationGroupSelector = ".bloom-translationGroup";
const kPageSelector = ".bloom-page";

export interface UnlinkOptions {
    /**
     * Clear the chain from the groups that are on other pages, which only C# can reach. Without
     * one, unlinkBox asks C# itself (flowBoundaryClient.unlinkFrom); a test supplies its own.
     */
    unlinkOnOtherPages?: (chainId: string, fromGroup: HTMLElement) => void;
    /**
     * Every box whose overflow state the unlink may have changed: the boxes that left the
     * chain, and the ones that stayed in it on this page. A box that used to hand its extra
     * text to a following box now has to show that the text does not fit, and the box that
     * used to receive it no longer has more text coming, so the caller re-checks them.
     */
    onUnlinked?: (editables: HTMLElement[]) => void;
}

/** Is the box's group part of a chain? This is what decides whether Unlink is offered. */
export function isBoxLinked(element: HTMLElement): boolean {
    const group = element.closest(kTranslationGroupSelector);
    return !!group?.getAttribute(kFlowChainAttr);
}

/**
 * Separate this box, and every box after it in the same chain, from that chain. The text stays
 * where it is: what was flowed into a box is now that box's own text, so the paragraph that
 * was a continuation of the box before it becomes an ordinary paragraph.
 *
 * `element` is the box, or anything inside it, such as the paragraph a right-click was on.
 */
export function unlinkBox(
    element: HTMLElement,
    options: UnlinkOptions = {},
): void {
    const group = element.closest<HTMLElement>(kTranslationGroupSelector);
    const page = element.closest<HTMLElement>(kPageSelector);
    const chainId = group?.getAttribute(kFlowChainAttr);
    if (!group || !page || !chainId) {
        return;
    }

    const groupsOfChain = Array.from(
        page.querySelectorAll<HTMLElement>(
            `${kTranslationGroupSelector}[${kFlowChainAttr}="${chainId}"]`,
        ),
    );
    const affected = groupsOfChain.flatMap(getEditables);

    groupsOfChain
        .filter((other) => other === group || comesAfter(other, group))
        .forEach(unlinkGroup);

    (options.unlinkOnOtherPages ?? unlinkOnLaterPages)(chainId, group);

    // A chain needs two boxes to be a chain: one box on its own has nowhere to send its extra
    // text and nowhere to get any from, and getLanguageChainOnPage passes over it. So a group
    // the unlink has left alone keeps no chain id.
    const stillLinked = groupsOfChain.filter(
        (other) => other.getAttribute(kFlowChainAttr) === chainId,
    );
    if (stillLinked.length === 1) {
        unlinkGroup(stillLinked[0]);
    }
    options.onUnlinked?.(affected);
}

/**
 * Take the groups of this chain that are on the later pages out of it. Only C# can reach them,
 * and it names a group by its page and its place among the flow groups of that page.
 */
function unlinkOnLaterPages(chainId: string, fromGroup: HTMLElement): void {
    const page = fromGroup.closest<HTMLElement>(kPageSelector);
    if (!page?.id) {
        return;
    }

    const indexInPage = getFlowGroupsOfPage(page).indexOf(fromGroup);
    if (indexInPage < 0) {
        return;
    }

    void unlinkFrom(chainId, page.id, indexInPage);
}

function unlinkGroup(group: HTMLElement): void {
    group.removeAttribute(kFlowChainAttr);
    group.classList.remove(kHasNextClass);
    group.classList.remove(kHasPrevClass);
    getEditables(group).forEach((editable) => {
        // The first paragraph carried the attribute that says it is the tail of a paragraph
        // that began in the box before. Nothing begins it elsewhere now, so it is an
        // ordinary paragraph, and it takes its style's indent and top margin back. The space
        // that stood at the seam has no place at the start of a paragraph, so its attribute
        // goes too.
        const tail = editable.querySelector(`:scope > p[${kContinuationAttr}]`);
        tail?.removeAttribute(kContinuationAttr);
        tail?.removeAttribute(kSeamSpaceAttr);
    });
}

function getEditables(group: HTMLElement): HTMLElement[] {
    return Array.from(
        group.querySelectorAll<HTMLElement>(":scope > .bloom-editable"),
    );
}

function comesAfter(candidate: Node, reference: Node): boolean {
    return (
        (candidate.compareDocumentPosition(reference) &
            Node.DOCUMENT_POSITION_PRECEDING) !==
        0
    );
}
