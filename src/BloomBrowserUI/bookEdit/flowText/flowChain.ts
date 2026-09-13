// Which boxes make up one chain, and whether we are willing to flow text through a box.

import { kBloomCanvasSelector } from "../toolbox/canvas/canvasElementConstants";
import { kChainedGroupSelector, kFlowChainAttr } from "./flowConstants";

const kTranslationGroupSelector = ".bloom-translationGroup";
const kEditableClass = "bloom-editable";
const kVisibleEditableClass = "bloom-visibility-code-on";

/**
 * The boxes of the trigger's chain that are on the trigger's page, in the order the text
 * flows through them, which is document order.
 *
 * A box qualifies when its translation group carries the same chain id as the trigger's
 * group, the group is not inside a bloom-canvas (a canvas element has its own layout and
 * cannot be part of a chain), and the box itself is the visible box of the trigger's
 * language. Returns an empty array if the trigger's group is not chained at all.
 */
export function getLanguageChainOnPage(
    triggerEditable: HTMLElement,
): HTMLElement[] {
    const triggerGroup = triggerEditable.closest(kTranslationGroupSelector);
    const chainId = triggerGroup?.getAttribute(kFlowChainAttr);
    if (!triggerGroup || !chainId) {
        return [];
    }

    const page = triggerEditable.closest(".bloom-page");
    const language = triggerEditable.getAttribute("lang");
    if (!page || !language) {
        return [];
    }

    const chain: HTMLElement[] = [];
    Array.from(page.querySelectorAll(kChainedGroupSelector)).forEach(
        (group) => {
            if (
                group.getAttribute(kFlowChainAttr) !== chainId ||
                group.closest(kBloomCanvasSelector)
            ) {
                return;
            }

            Array.from(group.children).forEach((child) => {
                if (isChainBoxForLanguage(child, language)) {
                    chain.push(child as HTMLElement);
                }
            });
        },
    );

    return chain;
}

function isChainBoxForLanguage(child: Element, language: string): boolean {
    return (
        child instanceof HTMLElement &&
        child.classList.contains(kEditableClass) &&
        child.classList.contains(kVisibleEditableClass) &&
        child.getAttribute("lang") === language
    );
}

/**
 * The translation groups of a page that flow text can use, in document order. C# counts the same
 * ones (HtmlDom.GetEltsWithClassNotInBloomCanvas), and the place of a group in this list is how
 * the browser and C# name the same group.
 */
export function getFlowGroupsOfPage(page: HTMLElement): HTMLElement[] {
    return Array.from(
        page.querySelectorAll<HTMLElement>(kTranslationGroupSelector),
    ).filter(
        (group) =>
            !group.closest(kBloomCanvasSelector) &&
            !group.classList.contains("box-header-off"),
    );
}

/**
 * Can we move text into and out of this box? We only handle a box whose top-level children
 * are paragraphs, and which holds nothing whose position we would have to reason about.
 */
export function supportsChainedEditable(editable: HTMLElement): boolean {
    const childElements = Array.from(editable.children).filter(
        (child) => !isIgnorableElement(child as HTMLElement),
    ) as HTMLElement[];
    if (childElements.length === 0) {
        return true;
    }

    return (
        childElements.every(
            (child) => child.tagName === "P" || isCkEditorBookmark(child),
        ) &&
        editable.querySelector(
            "img, audio, video, iframe, canvas, table, textarea, input, select, object, math, svg",
        ) === null
    );
}

export function supportsChainedPair(
    currentEditable: HTMLElement,
    nextEditable: HTMLElement,
): boolean {
    return (
        supportsChainedEditable(currentEditable) &&
        supportsChainedEditable(nextEditable)
    );
}

function isIgnorableElement(element: HTMLElement): boolean {
    return element.tagName === "BR" || isCkEditorBookmark(element);
}

/** CKEditor parks the caret in a span of its own while it works. */
export function isCkEditorBookmark(element: HTMLElement): boolean {
    return element.tagName === "SPAN" && element.id.startsWith("cke_bm_");
}
