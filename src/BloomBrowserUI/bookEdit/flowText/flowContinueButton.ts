// The offer an empty text box makes to take the text that does not fit in an earlier box.
//
// The affordance is on the TARGET, not on the source: there is no command that links a full
// box to a following one. When a normal-style box overflows, the overflow checker marks where
// its text stops fitting (flowOverflowMarker), and any later empty box of the same language
// then shows a button. Clicking it joins the two groups into one chain, and the flow pass that
// the new attribute triggers moves the tail across.
//
// This phase offers only a source on the SAME page. A source on an earlier page needs C# to
// split the paragraph and move the nodes, which is a later phase; findContinueSourceOnPage is
// where that second kind of source will join in.
//
// The button is a bloom-ui element, so HtmlDom.RemoveAllUiElements takes it out of the page
// that Bloom saves, and Cleanup takes it out again when the page is loaded.

import { kBloomCanvasSelector } from "../toolbox/canvas/canvasElementConstants";
import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import { EditableDivUtils } from "../js/editableDivUtils";
import {
    kContinueButtonClass,
    kContinueButtonEnglish,
    kContinueButtonL10nId,
    kContinueButtonTestId,
    kFlowChainAttr,
    kOverflowMarkerSelector,
} from "./flowConstants";
import { getComparableEditableLength } from "./flowLinearize";
import { getRefusalReason } from "./flowSupport";

const kTranslationGroupSelector = ".bloom-translationGroup";
const kPageSelector = ".bloom-page";
const kVisibleEditableSelector = ".bloom-editable.bloom-visibility-code-on";
const kContinueButtonSelector = `.${kContinueButtonClass}`;
// Which language's box the button belongs to. A group can hold a box per language, and each
// language's text flows through its own boxes, so each empty box gets its own offer.
const kContinueButtonLanguageAttr = "data-flow-continue-lang";

/**
 * Put the button on every empty box that can take an earlier box's overflow, and take it off
 * every box that cannot. Call this after a flow pass and after an overflow-checker pass: both
 * change which boxes overflow and which are empty.
 *
 * `root` is a page, or anything holding pages.
 */
export function updateContinueButtons(root: ParentNode = document): void {
    getPages(root).forEach((page) => {
        Array.from(
            page.querySelectorAll<HTMLElement>(kVisibleEditableSelector),
        ).forEach((editable) => {
            const source = canOfferContinue(editable)
                ? findContinueSourceOnPage(editable)
                : undefined;
            if (source) {
                addButton(editable, source);
            } else {
                removeButtonFor(editable);
            }
        });
    });
}

/** Take every continue button out, whether or not the box still qualifies for one. */
export function removeContinueButtons(root: ParentNode): void {
    Array.from(root.querySelectorAll(kContinueButtonSelector)).forEach(
        (button) => button.remove(),
    );
}

/** Does this box have the button on it at the moment? */
export function hasContinueButton(editable: HTMLElement): boolean {
    return getButtonFor(editable) !== undefined;
}

/**
 * Could this box take another box's overflow? It has to be an empty, visible, normal-style box
 * of the page's own layout, and it must not already be the continuation of a box on this page:
 * a box that is already taking text has nothing to offer.
 */
export function canOfferContinue(editable: HTMLElement): boolean {
    const group = editable.closest<HTMLElement>(kTranslationGroupSelector);
    const page = editable.closest<HTMLElement>(kPageSelector);
    if (!group || !page) {
        return false;
    }

    // The same test the pass uses. A box the flow refuses cannot take another box's text,
    // and the reader would have no way to know why the offer did nothing.
    if (getRefusalReason(editable)) {
        return false;
    }

    if (
        editable.closest(kBloomCanvasSelector) ||
        page.classList.contains("bloom-frontMatter") ||
        page.classList.contains("bloom-backMatter") ||
        !isOrigamiPage(page)
    ) {
        return false;
    }

    if (getComparableEditableLength(editable) > 0) {
        return false;
    }

    return !hasEarlierGroupOfSameChain(group, page);
}

/**
 * The box whose overflow this empty box would take: the nearest box of the same language,
 * earlier on the same page, that is a normal-style box of the page's own layout and holds the
 * marker that says its text stops fitting. Undefined when there is none.
 */
export function findContinueSourceOnPage(
    target: HTMLElement,
): HTMLElement | undefined {
    const targetGroup = target.closest<HTMLElement>(kTranslationGroupSelector);
    const page = target.closest<HTMLElement>(kPageSelector);
    const language = target.getAttribute("lang");
    if (!targetGroup || !page || !language) {
        return undefined;
    }

    const candidates = Array.from(
        page.querySelectorAll<HTMLElement>(
            `${kVisibleEditableSelector}[lang="${language}"]`,
        ),
    ).filter((candidate) => {
        const group = candidate.closest<HTMLElement>(kTranslationGroupSelector);
        return (
            !!group &&
            group !== targetGroup &&
            comesBefore(group, targetGroup) &&
            !candidate.closest(kBloomCanvasSelector) &&
            !getRefusalReason(candidate) &&
            candidate.querySelector(kOverflowMarkerSelector) !== null
        );
    });

    // The nearest earlier box, so that text does not jump over a box that could hold it.
    return candidates[candidates.length - 1];
}

/**
 * Join the empty box's group to the overflowing box's group, which is what the button does.
 * The source keeps the chain id it already has, so joining a third box to a chain does not
 * break the first two apart. Returns the chain id both groups now carry.
 *
 * Nothing is moved here. Setting the attribute is what the flow trigger watches for, and the
 * pass it schedules is what carries the text across.
 */
export function joinToSourceBox(
    source: HTMLElement,
    target: HTMLElement,
): string | undefined {
    const sourceGroup = source.closest<HTMLElement>(kTranslationGroupSelector);
    const targetGroup = target.closest<HTMLElement>(kTranslationGroupSelector);
    if (!sourceGroup || !targetGroup) {
        return undefined;
    }

    const chainId =
        sourceGroup.getAttribute(kFlowChainAttr) ||
        EditableDivUtils.createUuid();
    sourceGroup.setAttribute(kFlowChainAttr, chainId);
    targetGroup.setAttribute(kFlowChainAttr, chainId);
    removeButtonFor(target);
    return chainId;
}

function addButton(editable: HTMLElement, source: HTMLElement): void {
    const group = editable.closest<HTMLElement>(kTranslationGroupSelector);
    if (!group) {
        return;
    }

    const existing = getButtonFor(editable);
    const button =
        existing ??
        makeButton(editable.ownerDocument, editable.getAttribute("lang") ?? "");
    // The source can change while the button is up, e.g. when the user types into the box
    // between this one and the one that was overflowing, so re-attach the handler each time.
    button.onclick = (event: MouseEvent) => {
        event.preventDefault();
        event.stopPropagation();
        joinToSourceBox(source, editable);
    };
    if (!existing) {
        group.appendChild(button);
    }

    centerOverBox(button, editable);
}

function makeButton(document: Document, language: string): HTMLElement {
    const button = document.createElement("div");
    button.className = `bloom-ui ${kContinueButtonClass}`;
    button.setAttribute("contenteditable", "false");
    button.setAttribute("role", "button");
    button.setAttribute("data-testid", kContinueButtonTestId);
    button.setAttribute(kContinueButtonLanguageAttr, language);
    button.textContent = kContinueButtonEnglish;
    localizeLabel(button);
    return button;
}

/**
 * Put the button in the middle of the box it belongs to. The button sits in the translation
 * group rather than in the box, because a box is a contenteditable and everything in it is
 * content, and a group can hold a box per language; so where the box is has to be measured.
 * The group is a positioning context already: Bloom's stylesheet makes every element one.
 */
function centerOverBox(button: HTMLElement, editable: HTMLElement): void {
    button.style.top = `${editable.offsetTop + editable.offsetHeight / 2}px`;
    button.style.left = `${editable.offsetLeft + editable.offsetWidth / 2}px`;
}

function localizeLabel(button: HTMLElement): void {
    // A unit test has no localization manager, and the English label is right there already,
    // so a failure to localize must not stop the button from appearing.
    try {
        theOneLocalizationManager
            .asyncGetText(kContinueButtonL10nId, kContinueButtonEnglish, "")
            .done((text: string) => {
                if (text && button.isConnected) {
                    button.textContent = text;
                }
            });
    } catch {
        // Keep the English label.
    }
}

function getButtonFor(editable: HTMLElement): HTMLElement | undefined {
    const group = editable.closest(kTranslationGroupSelector);
    const language = editable.getAttribute("lang") ?? "";
    return (
        group?.querySelector<HTMLElement>(
            `:scope > ${kContinueButtonSelector}[${kContinueButtonLanguageAttr}="${language}"]`,
        ) ?? undefined
    );
}

function removeButtonFor(editable: HTMLElement): void {
    getButtonFor(editable)?.remove();
}

/**
 * Is this group already taking text from a box before it on this page? Such a box is the
 * continuation of another, so it must not offer to become the continuation of a third.
 */
function hasEarlierGroupOfSameChain(
    group: HTMLElement,
    page: HTMLElement,
): boolean {
    const chainId = group.getAttribute(kFlowChainAttr);
    if (!chainId) {
        return false;
    }

    return Array.from(
        page.querySelectorAll<HTMLElement>(
            `${kTranslationGroupSelector}[${kFlowChainAttr}="${chainId}"]`,
        ),
    ).some((other) => other !== group && comesBefore(other, group));
}

/** A page laid out in origami split panes, which is the only kind flow works on. */
function isOrigamiPage(page: HTMLElement): boolean {
    return page.querySelector(".split-pane-component") !== null;
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
