// The marks that tell the user, and the overflow checker, what the flow is doing.
//
// This module must import nothing but flowChain and flowConstants: OverflowChecker imports
// it, so anything it pulls in becomes part of the overflow checker's import graph.

import { getLanguageChainOnPage } from "./flowChain";
import {
    kHasNextClass,
    kHasPrevClass,
    kRefusedClass,
    kRefusedReasonAttr,
    kReflowingAttr,
} from "./flowConstants";

const kTranslationGroupSelector = ".bloom-translationGroup";

/**
 * Show, on each group of the chain, whether its text carries on into a following box and
 * whether it carries on from an earlier one.
 */
export function updateIndicators(chain: HTMLElement[]): void {
    chain.forEach((editable, index) => {
        const group = editable.closest(kTranslationGroupSelector);
        if (!group) {
            return;
        }

        setClass(group, kHasNextClass, index < chain.length - 1);
        setClass(group, kHasPrevClass, index > 0);
    });
}

/**
 * True when the normal overflow warning must stay off this box. A chained box that has
 * another box after it on the page is meant to be full: its extra text has somewhere to go,
 * so the red overflow marking would be telling the user about a problem they do not have.
 * The last box on the page keeps its warning, because that is where text really runs out.
 */
export function suppressesOverflowMarking(editable: HTMLElement): boolean {
    const chain = getLanguageChainOnPage(editable);
    if (chain.length < 2) {
        return false;
    }

    return chain[chain.length - 1] !== editable;
}

/** Take off everything the flow code adds while editing, so none of it reaches the saved page. */
export function stripTransientFlowMarkup(root: ParentNode): void {
    Array.from(
        root.querySelectorAll(
            `.${kHasNextClass}, .${kHasPrevClass}, .${kRefusedClass}`,
        ),
    ).forEach((element) => {
        element.classList.remove(kHasNextClass);
        element.classList.remove(kHasPrevClass);
        element.classList.remove(kRefusedClass);
    });

    Array.from(
        root.querySelectorAll(`[${kRefusedReasonAttr}], [${kReflowingAttr}]`),
    ).forEach((element) => {
        element.removeAttribute(kRefusedReasonAttr);
        element.removeAttribute(kReflowingAttr);
    });
}

function setClass(element: Element, className: string, wanted: boolean): void {
    if (wanted) {
        element.classList.add(className);
    } else {
        element.classList.remove(className);
    }
}
