// The offer the end of a flow makes when the text still does not fit and there is nowhere for the
// rest of it to go: Bloom makes the pages the rest needs and flows the text into them.
//
// Bloom never adds pages by itself, so up to here the author has had to add each page and take
// the offer on it (flowContinueButton). That is the right thing when the author is deciding what
// each page looks like, and a chore when they have pasted several pages of text into one box and
// want the rest of the book's pages to be more of the same. This button is that second case.
//
// It goes on the box where the run of text ends: a box holding the mark that says its text stops
// fitting (flowOverflowMarker), with no box after it in its chain on this page and none on a
// later page either. A box that is in no chain at all and overflows qualifies too, and clicking
// it is what makes the chain.
//
// The button sits below its box, clear of the text: the box it is on is full to the bottom, so
// there is no room inside it that is not covering something the author wrote.
//
// Clicking calls C# (flowBoundaryClient.createPagesAndContinue), which makes the pages, divides
// the text among them, and hands back what this box keeps. The button is a bloom-ui element, so
// HtmlDom.RemoveAllUiElements takes it out of the page that Bloom saves, and Cleanup takes it out
// again when the page is loaded.
//
// This module must not import flowTrigger: the trigger calls into here, and the two importing
// each other is the cycle the comment on FlowPassRunner is about.

import { kBloomCanvasSelector } from "../toolbox/canvas/canvasElementConstants";
import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import { pageScrollsInsteadOfOverflowing } from "../js/scrollingLayouts";
import { createPagesAndContinue, jumpToPage } from "./flowBoundaryClient";
import { getFlowGroupsOfPage } from "./flowChain";
import {
    kCreatePagesButtonClass,
    kCreatePagesButtonEnglish,
    kCreatePagesButtonL10nId,
    kCreatePagesButtonTestId,
    kFlowChainAttr,
    kOverflowMarkerSelector,
} from "./flowConstants";
import { getFlowPassRunner } from "./flowContinueButton";
import {
    getNextBoxOnLaterPage,
    isNextBoxAnswerKnown,
    resetNextBoxCache,
} from "./flowNextBox";
import { getRefusalReason } from "./flowSupport";

const kTranslationGroupSelector = ".bloom-translationGroup";
const kPageSelector = ".bloom-page";
const kVisibleEditableSelector = ".bloom-editable.bloom-visibility-code-on";
const kButtonSelector = `.${kCreatePagesButtonClass}`;
// Which language's box the button belongs to. A group can hold a box per language, and each
// language's text flows through its own boxes, so each one gets its own offer.
const kButtonLanguageAttr = "data-flow-create-pages-lang";
// How far below the box's bottom edge the button sits, so that it clears the text.
const kGapBelowBox = 4;

/**
 * Put the button on every box where the run of text ends unfinished, and take it off every box
 * where it does not. Call this after a flow pass and after an overflow-checker pass: both change
 * which boxes hold more text than fits.
 *
 * Whether a chained box has a box on a later page is a question for C#, and that is a round trip.
 * So this call puts up what it already knows, asks about the rest, and runs again for that page
 * when the answer comes.
 *
 * `root` is a page, or anything holding pages.
 */
export function updateCreatePagesButtons(root: ParentNode = document): void {
    getPages(root).forEach((page) => {
        Array.from(
            page.querySelectorAll<HTMLElement>(kVisibleEditableSelector),
        ).forEach((editable) => {
            if (canOfferCreatePages(editable, page)) {
                addButton(editable);
            } else {
                removeButtonFor(editable);
            }
        });
    });
}

/** Take every create-pages button out, whether or not the box still qualifies for one. */
export function removeCreatePagesButtons(root: ParentNode): void {
    Array.from(root.querySelectorAll(kButtonSelector)).forEach((button) =>
        button.remove(),
    );
}

/** Does this box have the button on it at the moment? */
export function hasCreatePagesButton(editable: HTMLElement): boolean {
    return getButtonFor(editable) !== undefined;
}

/** The words on the box's button, or undefined when it has none. */
export function getCreatePagesButtonLabel(
    editable: HTMLElement,
): string | undefined {
    return getButtonFor(editable)?.textContent ?? undefined;
}

/**
 * Is this the box where the run of text ends unfinished? Four things have to hold:
 *
 *  - the box holds the mark that says where its text stops fitting, so there IS more text;
 *  - no box of its chain follows it on this page, which would be where the rest goes;
 *  - no box of its chain is on a later page either, which C# alone can say;
 *  - the page reports an overflow at all. The screen-sized layouts scroll instead
 *    (scrollingLayouts), so on those there is no "does not fit" to act on.
 *
 * A box in no chain qualifies on the first and last of those: clicking it is what makes a chain.
 */
export function canOfferCreatePages(
    editable: HTMLElement,
    page: HTMLElement,
): boolean {
    const group = editable.closest<HTMLElement>(kTranslationGroupSelector);
    if (!group || !page.id) {
        return false;
    }

    // The same test the pass uses. Flow cannot move text through this box, so making pages for
    // its text would make pages that stayed empty.
    if (getRefusalReason(editable)) {
        return false;
    }

    if (
        editable.closest(kBloomCanvasSelector) ||
        page.classList.contains("bloom-frontMatter") ||
        page.classList.contains("bloom-backMatter") ||
        !isInPageLayout(editable) ||
        pageScrollsInsteadOfOverflowing(page)
    ) {
        return false;
    }

    if (editable.querySelector(kOverflowMarkerSelector) === null) {
        return false;
    }

    const chainId = group.getAttribute(kFlowChainAttr);
    if (!chainId) {
        return true;
    }

    if (hasLaterGroupOfSameChain(group, page, chainId)) {
        return false;
    }

    const language = editable.getAttribute("lang");
    if (!language) {
        return false;
    }

    // Asking starts the round trip. Until the answer is in we do not know that this box is the
    // end of the chain, and an offer that appeared and then vanished would be worse than one
    // that appears a moment late.
    const next = getNextBoxOnLaterPage(
        page,
        chainId,
        language,
        updateCreatePagesButtons,
    );
    return next === undefined && isNextBoxAnswerKnown(page, chainId, language);
}

/**
 * Take the offer: have Bloom make the pages the rest of this box's text needs, flow the text into
 * them, and put in place what this box keeps. The Edit tab then moves to the last page made, so
 * that the author sees where the text ended up.
 *
 * C# owns the pages it makes and saves them itself. This box is on the page being edited, which
 * the browser owns, so its content comes back here to be applied rather than written under the
 * user.
 */
export async function createPagesFor(editable: HTMLElement): Promise<void> {
    const group = editable.closest<HTMLElement>(kTranslationGroupSelector);
    const page = editable.closest<HTMLElement>(kPageSelector);
    const language = editable.getAttribute("lang");
    if (!group || !page?.id || !language) {
        return;
    }

    const indexInPage = getFlowGroupsOfPage(page).indexOf(group);
    if (indexInPage < 0) {
        return;
    }

    const result = await createPagesAndContinue({
        pageId: page.id,
        indexInPage,
        lang: language,
        chainId: group.getAttribute(kFlowChainAttr) ?? "",
        html: editable.innerHTML,
    });
    if (!result || !group.isConnected) {
        return;
    }

    const runner = getFlowPassRunner();
    const apply = () => {
        editable.innerHTML = result.sourceHtml;
        group.setAttribute(kFlowChainAttr, result.chainId);
        removeButtonFor(editable);
        // The box now holds only what fits it, so the warning it and its page carry is out of
        // date. The jump below saves this page at once, and the page's warning is what its
        // thumbnail draws its red triangle from, so both are brought up to date here rather than
        // on the overflow checker's own deferred timer.
        editable.classList.remove("overflow", "thisOverflowingParent");
        runner?.markOverflow(editable);
        runner?.updatePageOverflow(page);
    };
    if (runner) {
        runner.applyWithoutPass(apply);
    } else {
        apply();
    }

    // The chain now runs on into pages that were not there when C# was last asked about it.
    resetNextBoxCache();
    // Nothing is asked to settle here. C# has just divided the whole run among these pages with
    // the same off-screen fit a pass would use, so a pass would only divide it again, and one
    // that ran while these pages were still being saved would divide a run it read too early.
    updateCreatePagesButtons(page);

    // Go to where the text now ends, so that the author sees what Bloom made rather than a page
    // that looks unchanged. This is last: C# saves the page being edited on its way there, which
    // is what puts the box's new content above into the book, and every page Bloom made is saved
    // already.
    jumpToPage(result.lastPageId);
}

function addButton(editable: HTMLElement): void {
    const group = editable.closest<HTMLElement>(kTranslationGroupSelector)!;
    const existing = getButtonFor(editable);
    const button =
        existing ??
        makeButton(editable.ownerDocument, editable.getAttribute("lang") ?? "");
    button.onclick = (event: MouseEvent) => {
        event.preventDefault();
        event.stopPropagation();
        void createPagesFor(editable);
    };
    if (!existing) {
        setLabel(button);
        group.appendChild(button);
    }

    placeBelowBox(button, editable);
}

function makeButton(document: Document, language: string): HTMLElement {
    const button = document.createElement("div");
    button.className = `bloom-ui ${kCreatePagesButtonClass}`;
    button.setAttribute("contenteditable", "false");
    button.setAttribute("role", "button");
    button.setAttribute("data-testid", kCreatePagesButtonTestId);
    button.setAttribute(kButtonLanguageAttr, language);
    return button;
}

/**
 * Put the button just below the box it belongs to, ending flush with the box's right edge. It
 * covers no text there, which matters for this offer above all: the box it is on is full to the
 * bottom, so a button inside its bounds would hide the last words the author wrote.
 *
 * The button sits in the translation group rather than in the box, because a box is a
 * contenteditable and everything in it is content, and a group can hold a box per language; so
 * where the box is has to be measured. editMode.less makes the group the positioning context and
 * pulls the button's own width back from the left it is given.
 *
 * A box at the foot of the page has no room below it. There the button sits inside the box's own
 * bottom edge instead, because staying on the page matters more than clearing the text.
 */
function placeBelowBox(button: HTMLElement, editable: HTMLElement): void {
    button.style.left = `${editable.offsetLeft + editable.offsetWidth}px`;
    const belowBox = editable.offsetTop + editable.offsetHeight + kGapBelowBox;
    button.style.top = `${belowBox}px`;

    const page = editable.closest<HTMLElement>(kPageSelector)!;
    if (
        button.getBoundingClientRect().bottom >
        page.getBoundingClientRect().bottom
    ) {
        button.style.top = `${belowBox - kGapBelowBox - button.offsetHeight}px`;
    }
}

function setLabel(button: HTMLElement): void {
    button.textContent = kCreatePagesButtonEnglish;

    // A unit test has no localization manager, and the English label is right there already, so
    // a failure to localize must not stop the button from appearing.
    try {
        theOneLocalizationManager
            .asyncGetText(
                kCreatePagesButtonL10nId,
                kCreatePagesButtonEnglish,
                "",
            )
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
            `:scope > ${kButtonSelector}[${kButtonLanguageAttr}="${language}"]`,
        ) ?? undefined
    );
}

function removeButtonFor(editable: HTMLElement): void {
    getButtonFor(editable)?.remove();
}

/**
 * Is there a group of the same chain after this one on this page? Such a group is where this
 * one's text goes, so this box is not the end of the run.
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

/**
 * Is this box part of the page's own layout? Every laid-out page keeps its content in a
 * marginBox, so a box outside one is not one of the page's own text boxes.
 */
function isInPageLayout(editable: HTMLElement): boolean {
    return editable.closest(".marginBox") !== null;
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
