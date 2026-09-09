// The label a box wears when its text continues from a linked box on an EARLIER PAGE.
//
// Within a page the reader can see where the text comes from: the box above carries the arrow
// that says its text runs on (kHasNextClass), and this box's group carries kHasPrevClass. Across
// a page boundary there is nothing to see, so the first box of a chain on this page says in words
// which page its text flows in from. Which page that is only C# knows, because the boxes of the
// other pages are not in the edit iframe (flowBoundaryClient.peekPrevious).
//
// The label is a bloom-ui element, so HtmlDom.RemoveAllUiElements takes it out of the page that
// Bloom saves, and Cleanup takes it out again when the page is loaded.
//
// This module must not import flowTrigger: the trigger calls into here, and the two importing
// each other is the cycle the comment on FlowPassRunner is about.

import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import { IPreviousBox, peekPrevious } from "./flowBoundaryClient";
import {
    kFlowChainAttr,
    kFlowFromClass,
    kFlowFromPageEnglish,
    kFlowFromPageL10nId,
    kFlowFromPreviousPageEnglish,
    kFlowFromPreviousPageL10nId,
    kFlowFromTestId,
} from "./flowConstants";

const kTranslationGroupSelector = ".bloom-translationGroup";
const kPageSelector = ".bloom-page";
const kVisibleEditableSelector = ".bloom-editable.bloom-visibility-code-on";
const kLabelSelector = `.${kFlowFromClass}`;
// Which language's box the label belongs to. A group can hold a box per language, and each
// language's text flows through its own boxes, so each one gets its own label.
const kLabelLanguageAttr = "data-flow-from-lang";

// What C# said about the box before this page, per page, chain and language, so that a pass does
// not cost a round trip. The answer can only change when the chain itself changes, and the pass
// that changes it clears this.
const previousByPageChainAndLang = new Map<string, IPreviousBox | undefined>();
const asking = new Set<string>();

/**
 * Forget what C# said about the box before this page. Call this when the page being edited
 * changes, and after anything that changes which boxes are in which chain.
 */
export function resetFlowFromCache(): void {
    previousByPageChainAndLang.clear();
    asking.clear();
}

/**
 * Put the label on every box whose text flows in from an earlier page, and take it off every box
 * whose does not. Call this after a flow pass and after the page loads.
 *
 * Whether there is such a box is a question for C#, and that is a round trip. So this call puts
 * up what it already knows, asks about the rest, and runs again for that page when the answer
 * comes.
 *
 * `root` is a page, or anything holding pages.
 */
export function updateFlowFromLabels(root: ParentNode = document): void {
    getPages(root).forEach((page) => {
        Array.from(
            page.querySelectorAll<HTMLElement>(kVisibleEditableSelector),
        ).forEach((editable) => {
            const previous = findPreviousPage(editable, page);
            if (previous) {
                addLabel(editable, previous);
            } else {
                removeLabelFor(editable);
            }
        });
    });
}

/** Take every label out, whether or not the box still qualifies for one. */
export function removeFlowFromLabels(root: ParentNode): void {
    Array.from(root.querySelectorAll(kLabelSelector)).forEach((label) =>
        label.remove(),
    );
}

/** The words on the box's label, or undefined when it has none. */
export function getFlowFromLabelText(
    editable: HTMLElement,
): string | undefined {
    return getLabelFor(editable)?.textContent ?? undefined;
}

/**
 * The page this box's text flows in from, when there is one. There is only when the box is in a
 * chained group that is the FIRST group of its chain on this page: a group with an earlier group
 * of the same chain on the page takes its text from that group, which the reader can see.
 */
function findPreviousPage(
    editable: HTMLElement,
    page: HTMLElement,
): IPreviousBox | undefined {
    const group = editable.closest<HTMLElement>(kTranslationGroupSelector);
    const language = editable.getAttribute("lang");
    const chainId = group?.getAttribute(kFlowChainAttr);
    if (!group || !language || !chainId || !page.id) {
        return undefined;
    }

    if (hasEarlierGroupOfSameChain(group, page, chainId)) {
        return undefined;
    }

    return getPreviousOnEarlierPage(page, chainId, language);
}

/**
 * What C# says about the box before this page, for this page, chain and language. The first call
 * for the three starts the round trip and reports nothing; the answer arrives later and puts the
 * label up.
 */
function getPreviousOnEarlierPage(
    page: HTMLElement,
    chainId: string,
    language: string,
): IPreviousBox | undefined {
    const key = `${page.id}|${chainId}|${language}`;
    if (previousByPageChainAndLang.has(key)) {
        return previousByPageChainAndLang.get(key);
    }

    if (asking.has(key)) {
        return undefined;
    }

    asking.add(key);
    peekPrevious(chainId, page.id)
        .then((found) => {
            asking.delete(key);
            previousByPageChainAndLang.set(key, found);
            if (page.isConnected) {
                updateFlowFromLabels(page);
            }
        })
        .catch(() => {
            // Without an answer there is no label. The next pass asks again.
            asking.delete(key);
        });
    return undefined;
}

function addLabel(editable: HTMLElement, previous: IPreviousBox): void {
    const group = editable.closest<HTMLElement>(kTranslationGroupSelector);
    if (!group) {
        return;
    }

    const existing = getLabelFor(editable);
    const label =
        existing ??
        makeLabel(editable.ownerDocument, editable.getAttribute("lang") ?? "");
    setLabel(label, previous);
    if (!existing) {
        group.appendChild(label);
    }

    placeOverBox(label, editable);
}

function makeLabel(document: Document, language: string): HTMLElement {
    const label = document.createElement("div");
    label.className = `bloom-ui ${kFlowFromClass}`;
    label.setAttribute("contenteditable", "false");
    label.setAttribute("data-testid", kFlowFromTestId);
    label.setAttribute(kLabelLanguageAttr, language);
    return label;
}

/**
 * Put the label at the top right of the box it belongs to. The label sits in the translation
 * group rather than in the box, because a box is a contenteditable and everything in it is
 * content, and a group can hold a box per language; so where the box is has to be measured.
 * The group is a positioning context already: Bloom's stylesheet makes every element one.
 */
function placeOverBox(label: HTMLElement, editable: HTMLElement): void {
    label.style.top = `${editable.offsetTop}px`;
    label.style.left = `${editable.offsetLeft + editable.offsetWidth}px`;
}

/**
 * Say which page the text flows in from. A page without a number of its own, such as one before
 * the numbering starts, gets the wording that names no page.
 */
function setLabel(label: HTMLElement, previous: IPreviousBox): void {
    const hasNumber = !!previous.pageNumber;
    const english = hasNumber
        ? kFlowFromPageEnglish.replace("{0}", previous.pageNumber)
        : kFlowFromPreviousPageEnglish;
    const l10nId = hasNumber
        ? kFlowFromPageL10nId
        : kFlowFromPreviousPageL10nId;
    label.textContent = english;

    // A unit test has no localization manager, and the English label is right there already,
    // so a failure to localize must not stop the label from appearing.
    try {
        theOneLocalizationManager
            .asyncGetText(
                l10nId,
                hasNumber ? kFlowFromPageEnglish : kFlowFromPreviousPageEnglish,
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

                // The label has to say which page the text is on, so a translation that has
                // lost the {0} is no use: keep the English label, which still names the page.
                if (text.includes("{0}")) {
                    label.textContent = text.replace(
                        "{0}",
                        previous.pageNumber,
                    );
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
 * Is there a group of the same chain before this one on this page? Such a group is where this
 * one's text comes from, so the text does not flow in from another page.
 */
function hasEarlierGroupOfSameChain(
    group: HTMLElement,
    page: HTMLElement,
    chainId: string,
): boolean {
    return Array.from(
        page.querySelectorAll<HTMLElement>(
            `${kTranslationGroupSelector}[${kFlowChainAttr}="${chainId}"]`,
        ),
    ).some((other) => other !== group && comesBefore(other, group));
}

function comesBefore(first: Node, second: Node): boolean {
    return (
        (first.compareDocumentPosition(second) &
            Node.DOCUMENT_POSITION_FOLLOWING) !==
        0
    );
}

function getPages(root: ParentNode): HTMLElement[] {
    if (root instanceof HTMLElement && root.matches(kPageSelector)) {
        return [root];
    }

    return Array.from(root.querySelectorAll<HTMLElement>(kPageSelector));
}
