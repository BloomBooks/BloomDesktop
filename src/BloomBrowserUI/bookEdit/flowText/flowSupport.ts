// Whether we are willing to flow text through a box, and why not when we are not.

import { supportsChainedEditable } from "./flowChain";
import { kRefusedClass, kRefusedReasonAttr } from "./flowConstants";

const kTranslationGroupSelector = ".bloom-translationGroup";
const kNormalStyleClass = "normal-style";
const kNoParagraphsClass = "bloom-noParagraphs";
const kWordFindStyleSuffix = "WordFind-style";
const kTalkingBookSelector =
    ".audio-sentence, [data-audiorecordingmode], .bloom-postAudioSplit";

/**
 * The reasons flow refuses a box. The value goes into kRefusedReasonAttr, so the user
 * interface can say which one it is.
 */
export const kRefusalReasons = {
    noGroup: "noGroup",
    notNormalStyle: "notNormalStyle",
    rightToLeft: "rightToLeft",
    talkingBook: "talkingBook",
    noParagraphs: "noParagraphs",
    wordFind: "wordFind",
    hyphenated: "hyphenated",
    unsupportedContent: "unsupportedContent",
} as const;

/**
 * Why flow will not move text through this box, or undefined if it will. The order of the
 * checks decides which reason a box with more than one problem reports.
 */
export function getRefusalReason(editable: HTMLElement): string | undefined {
    const group = editable.closest(kTranslationGroupSelector);
    if (!group) {
        return kRefusalReasons.noGroup;
    }

    // Bloom puts the style class on the editable; a template may put it on the group instead.
    if (
        !editable.classList.contains(kNormalStyleClass) &&
        !group.classList.contains(kNormalStyleClass)
    ) {
        return kRefusalReasons.notNormalStyle;
    }

    if (isRightToLeft(editable) || isRightToLeft(group)) {
        return kRefusalReasons.rightToLeft;
    }

    if (hasTalkingBookMarkup(editable)) {
        return kRefusalReasons.talkingBook;
    }

    if (
        editable.classList.contains(kNoParagraphsClass) ||
        group.classList.contains(kNoParagraphsClass)
    ) {
        return kRefusalReasons.noParagraphs;
    }

    if (hasWordFindStyle(editable) || hasWordFindStyle(group)) {
        return kRefusalReasons.wordFind;
    }

    if (isHyphenatedAutomatically(editable)) {
        return kRefusalReasons.hyphenated;
    }

    if (!supportsChainedEditable(editable)) {
        return kRefusalReasons.unsupportedContent;
    }

    return undefined;
}

/**
 * Put the refusal class and reason on the group of every box the flow refuses, and take them
 * off the groups of the rest. Returns true if any box of the chain is refused, in which case
 * the caller must not flow the chain at all: text that cannot come back out of one box would
 * be stranded there.
 */
export function markRefusals(chain: HTMLElement[]): boolean {
    let anyRefused = false;
    chain.forEach((editable) => {
        const group = editable.closest(kTranslationGroupSelector);
        const reason = getRefusalReason(editable);
        anyRefused = anyRefused || reason !== undefined;
        if (!group) {
            return;
        }

        if (reason === undefined) {
            group.classList.remove(kRefusedClass);
            group.removeAttribute(kRefusedReasonAttr);
        } else {
            group.classList.add(kRefusedClass);
            group.setAttribute(kRefusedReasonAttr, reason);
        }
    });

    return anyRefused;
}

function isRightToLeft(element: Element): boolean {
    if (element.getAttribute("dir")?.toLowerCase() === "rtl") {
        return true;
    }

    return getComputedStyleSafely(element)?.direction === "rtl";
}

function hasTalkingBookMarkup(editable: HTMLElement): boolean {
    return (
        editable.matches(kTalkingBookSelector) ||
        editable.querySelector(kTalkingBookSelector) !== null
    );
}

function hasWordFindStyle(element: Element): boolean {
    return Array.from(element.classList).some((className) =>
        className.endsWith(kWordFindStyleSuffix),
    );
}

function isHyphenatedAutomatically(editable: HTMLElement): boolean {
    const computed = getComputedStyleSafely(editable);
    if (!computed) {
        return false;
    }

    const hyphens =
        computed.hyphens ||
        computed.getPropertyValue("hyphens") ||
        computed.getPropertyValue("-webkit-hyphens");
    return hyphens === "auto";
}

function getComputedStyleSafely(
    element: Element,
): CSSStyleDeclaration | undefined {
    // An element that is not in a document has no computed style in every engine.
    return element.ownerDocument?.defaultView?.getComputedStyle(element);
}
