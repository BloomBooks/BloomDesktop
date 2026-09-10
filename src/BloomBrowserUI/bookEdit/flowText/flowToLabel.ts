// The label a box wears when its text continues into a linked box on a LATER PAGE.
//
// Within a page the reader can follow the arrow on the group (kHasNextClass) to the box the text
// runs on into. Across a page boundary there is nothing to follow, so the last box of a chain on
// this page says in words which page its text goes to. Which page that is only C# knows, because
// the boxes of the other pages are not in the edit iframe (flowBoundaryClient.peekNext).
//
// The label is a bloom-ui element, so HtmlDom.RemoveAllUiElements takes it out of the page that
// Bloom saves, and Cleanup takes it out again when the page is loaded.
//
// This module must not import flowTrigger: the trigger calls into here, and the two importing
// each other is the cycle the comment on FlowPassRunner is about.

import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import { INextBox } from "./flowBoundaryClient";
import { getNextBoxOnLaterPage, resetNextBoxCache } from "./flowNextBox";
import {
    kFlowChainAttr,
    kFlowToClass,
    kFlowToNextPageEnglish,
    kFlowToNextPageL10nId,
    kFlowToPageEnglish,
    kFlowToPageL10nId,
    kFlowToTestId,
} from "./flowConstants";

const kTranslationGroupSelector = ".bloom-translationGroup";
const kPageSelector = ".bloom-page";
const kVisibleEditableSelector = ".bloom-editable.bloom-visibility-code-on";
const kLabelSelector = `.${kFlowToClass}`;
// Which language's box the label belongs to. A group can hold a box per language, and each
// language's text flows through its own boxes, so each one gets its own label.
const kLabelLanguageAttr = "data-flow-to-lang";
// How far the label sits clear of the box's edge, in pixels.
const kGapFromBox = 2;

/**
 * Forget what C# said about the box after this page. Call this when the page being edited
 * changes, and after anything that changes which boxes are in which chain.
 */
export function resetFlowToCache(): void {
    resetNextBoxCache();
}

/**
 * Put the label on every box whose text flows out to a later page, and take it off every box
 * whose does not. Call this after a flow pass and after the page loads.
 *
 * Whether there is such a box is a question for C#, and that is a round trip. So this call puts
 * up what it already knows, asks about the rest, and runs again for that page when the answer
 * comes.
 *
 * `root` is a page, or anything holding pages.
 */
export function updateFlowToLabels(root: ParentNode = document): void {
    getPages(root).forEach((page) => {
        Array.from(
            page.querySelectorAll<HTMLElement>(kVisibleEditableSelector),
        ).forEach((editable) => {
            const next = findNextPage(editable, page);
            if (next) {
                addLabel(editable, next);
            } else {
                removeLabelFor(editable);
            }
        });
    });
}

/** Take every label out, whether or not the box still qualifies for one. */
export function removeFlowToLabels(root: ParentNode): void {
    Array.from(root.querySelectorAll(kLabelSelector)).forEach((label) =>
        label.remove(),
    );
}

/** The words on the box's label, or undefined when it has none. */
export function getFlowToLabelText(editable: HTMLElement): string | undefined {
    return getLabelFor(editable)?.textContent ?? undefined;
}

/**
 * The page this box's text flows out to, when there is one. There is only when the box is in a
 * chained group that is the LAST group of its chain on this page: a group with a later group of
 * the same chain on the page hands its text to that group, which the reader can see.
 */
function findNextPage(
    editable: HTMLElement,
    page: HTMLElement,
): INextBox | undefined {
    const group = editable.closest<HTMLElement>(kTranslationGroupSelector);
    const language = editable.getAttribute("lang");
    const chainId = group?.getAttribute(kFlowChainAttr);
    if (!group || !language || !chainId || !page.id) {
        return undefined;
    }

    if (hasLaterGroupOfSameChain(group, page, chainId)) {
        return undefined;
    }

    return getNextBoxOnLaterPage(page, chainId, language, updateFlowToLabels);
}

function addLabel(editable: HTMLElement, next: INextBox): void {
    const group = editable.closest<HTMLElement>(kTranslationGroupSelector);
    if (!group) {
        return;
    }

    const existing = getLabelFor(editable);
    const label =
        existing ??
        makeLabel(editable.ownerDocument, editable.getAttribute("lang") ?? "");
    setLabel(label, next);
    if (!existing) {
        group.appendChild(label);
    }

    placeBelowBox(label, editable);
}

function makeLabel(document: Document, language: string): HTMLElement {
    const label = document.createElement("div");
    label.className = `bloom-ui ${kFlowToClass}`;
    label.setAttribute("contenteditable", "false");
    label.setAttribute("data-testid", kFlowToTestId);
    label.setAttribute(kLabelLanguageAttr, language);
    return label;
}

/**
 * Put the label just below the bottom right corner of the box it belongs to, clear of the box's
 * text. The label sits in the translation group rather than in the box, because a box is a
 * contenteditable and everything in it is content, and a group can hold a box per language; so
 * where the box is has to be measured. editMode.less makes the group the positioning context and
 * pulls the label's own width back from the left it is given, so it ends flush with the box's
 * right edge.
 *
 * A box at the foot of the page has no room below it. There the label sits at the box's own
 * bottom instead, because staying inside the page matters more than clearing the last line of
 * text.
 */
function placeBelowBox(label: HTMLElement, editable: HTMLElement): void {
    label.style.left = `${editable.offsetLeft + editable.offsetWidth}px`;
    const belowBox = editable.offsetTop + editable.offsetHeight + kGapFromBox;
    label.style.top = `${belowBox}px`;

    const page = editable.closest<HTMLElement>(kPageSelector)!;
    if (
        label.getBoundingClientRect().bottom >
        page.getBoundingClientRect().bottom
    ) {
        label.style.top = `${belowBox - kGapFromBox - label.offsetHeight}px`;
    }
}

/**
 * Say which page the text flows out to. A page without a number of its own, such as one before
 * the numbering starts, gets the wording that names no page.
 */
function setLabel(label: HTMLElement, next: INextBox): void {
    const hasNumber = !!next.pageNumber;
    const english = hasNumber
        ? kFlowToPageEnglish.replace("{0}", next.pageNumber)
        : kFlowToNextPageEnglish;
    const l10nId = hasNumber ? kFlowToPageL10nId : kFlowToNextPageL10nId;
    label.textContent = english;

    // A unit test has no localization manager, and the English label is right there already,
    // so a failure to localize must not stop the label from appearing.
    try {
        theOneLocalizationManager
            .asyncGetText(
                l10nId,
                hasNumber ? kFlowToPageEnglish : kFlowToNextPageEnglish,
                "",
            )
            .done((text: string) => {
                if (!text || !label.isConnected) {
                    return;
                }

                if (!hasNumber) {
                    label.textContent = text;
                    return;
                }

                // The label has to say which page the text goes to, so a translation that has
                // lost the {0} is no use: keep the English label, which still names the page.
                if (text.includes("{0}")) {
                    label.textContent = text.replace("{0}", next.pageNumber);
                }
            });
    } catch {
        // Keep the English label.
    }
}

function getLabelFor(editable: HTMLElement): HTMLElement | undefined {
    const group = editable.closest(kTranslationGroupSelector);
    const language = editable.getAttribute("lang") ?? "";
    return (
        group?.querySelector<HTMLElement>(
            `:scope > ${kLabelSelector}[${kLabelLanguageAttr}="${language}"]`,
        ) ?? undefined
    );
}

function removeLabelFor(editable: HTMLElement): void {
    getLabelFor(editable)?.remove();
}

/**
 * Is there a group of the same chain after this one on this page? Such a group is where this
 * one's text goes, so the text does not flow out to another page.
 */
function hasLaterGroupOfSameChain(
    group: HTMLElement,
    page: HTMLElement,
    chainId: string,
): boolean {
    return Array.from(
        page.querySelectorAll<HTMLElement>(
            `${kTranslationGroupSelector}[${kFlowChainAttr}="${chainId}"]`,
        ),
    ).some((other) => other !== group && comesAfter(other, group));
}

function comesAfter(first: Node, second: Node): boolean {
    return (
        (first.compareDocumentPosition(second) &
            Node.DOCUMENT_POSITION_PRECEDING) !==
        0
    );
}

function getPages(root: ParentNode): HTMLElement[] {
    if (root instanceof HTMLElement && root.matches(kPageSelector)) {
        return [root];
    }

    return Array.from(root.querySelectorAll<HTMLElement>(kPageSelector));
}
