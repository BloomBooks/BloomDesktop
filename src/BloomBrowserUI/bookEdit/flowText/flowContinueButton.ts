// The offer an empty text box makes to take the text that does not fit in an earlier box.
//
// The affordance is on the TARGET, not on the source: there is no command that links a full
// box to a following one. When a normal-style box overflows, the overflow checker marks where
// its text stops fitting (flowOverflowMarker), and any later empty box of the same language
// then shows a button. Clicking it joins the two groups into one chain, and the text arrives.
//
// A source can be on the same page or on an earlier one. A source on the same page is entirely
// the browser's work: setting the chain attribute is enough, and the pass it triggers carries the
// text across. A source on an earlier page is not in the edit iframe at all, so C# splits the
// paragraph and moves the nodes (flowBoundaryClient.continueInto) and hands back the new content
// of the box on this page for the browser to put in place.
//
// The button is a bloom-ui element, so HtmlDom.RemoveAllUiElements takes it out of the page
// that Bloom saves, and Cleanup takes it out again when the page is loaded.

import { kBloomCanvasSelector } from "../toolbox/canvas/canvasElementConstants";
import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import { EditableDivUtils } from "../js/editableDivUtils";
import {
    continueInto,
    getPendingOverflow,
    IPendingOverflow,
} from "./flowBoundaryClient";
import {
    kContinueButtonClass,
    kContinueButtonEnglish,
    kContinueButtonL10nId,
    kContinueButtonTestId,
    kContinueFromPageEnglish,
    kContinueFromPageL10nId,
    kFlowChainAttr,
    kOverflowMarkerSelector,
} from "./flowConstants";
import { getComparableEditableLength } from "./flowLinearize";
import { getFlowGroupsOfPage } from "./flowChain";
import { getRefusalReason } from "./flowSupport";

export { getFlowGroupsOfPage };

const kTranslationGroupSelector = ".bloom-translationGroup";
const kPageSelector = ".bloom-page";
const kVisibleEditableSelector = ".bloom-editable.bloom-visibility-code-on";
const kContinueButtonSelector = `.${kContinueButtonClass}`;
// Which language's box the button belongs to. A group can hold a box per language, and each
// language's text flows through its own boxes, so each empty box gets its own offer.
const kContinueButtonLanguageAttr = "data-flow-continue-lang";

/** The box whose overflow an empty box is offering to take. */
export type ContinueSource =
    | { onThisPage: true; editable: HTMLElement }
    | { onThisPage: false; pending: IPendingOverflow };

/**
 * What the button needs from the flow pass once it has changed the page. The trigger supplies it
 * (see setFlowPassRunner); this module never imports the trigger, so that the two do not import
 * each other.
 */
export interface FlowPassRunner {
    /**
     * Make these changes to the page without the trigger treating them as the user's editing.
     * The changes are ours, and reacting to them would start a pass in the middle of this one.
     */
    applyWithoutPass: (work: () => void) => void;
    /** Settle these boxes now: text has arrived that nobody has measured. */
    requestPassFor: (editables: HTMLElement[], reason: string) => void;
}

let passRunner: FlowPassRunner | undefined;

export function setFlowPassRunner(runner?: FlowPassRunner): void {
    passRunner = runner;
}

/**
 * The runner the trigger registered, for the other things drawn on a box that have to change the
 * page and then have it settled (flowCreatePagesButton). It is undefined in a unit test and
 * before the trigger has started.
 */
export function getFlowPassRunner(): FlowPassRunner | undefined {
    return passRunner;
}

// What C# said about each page and language, so that a pass does not cost a round trip. The
// answer can only change when an earlier page's text changes, and that means the user has left
// this page, which clears the cache.
const pendingOverflowByPageAndLang = new Map<
    string,
    IPendingOverflow | undefined
>();
const asking = new Set<string>();

/**
 * Forget what C# said about the boxes on the other pages. Call this when the page being edited
 * changes, and after anything that changes what an earlier box holds.
 */
export function resetPendingOverflowCache(): void {
    pendingOverflowByPageAndLang.clear();
    asking.clear();
}

/**
 * Put the button on every empty box that can take an earlier box's overflow, and take it off
 * every box that cannot. Call this after a flow pass and after an overflow-checker pass: both
 * change which boxes overflow and which are empty.
 *
 * A box with no source on this page needs C#'s opinion about the earlier pages, and that is a
 * round trip. So this call puts up what it can decide by itself, asks C# about the rest, and
 * runs again for that page when the answer comes.
 *
 * `root` is a page, or anything holding pages.
 */
export function updateContinueButtons(root: ParentNode = document): void {
    getPages(root).forEach((page) => {
        Array.from(
            page.querySelectorAll<HTMLElement>(kVisibleEditableSelector),
        ).forEach((editable) => {
            const source = canOfferContinue(editable)
                ? findContinueSource(editable)
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

/** The words on the box's button, or undefined when it has none. */
export function getContinueButtonLabel(
    editable: HTMLElement,
): string | undefined {
    return getButtonFor(editable)?.textContent ?? undefined;
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
        !isInPageLayout(editable)
    ) {
        return false;
    }

    if (getComparableEditableLength(editable) > 0) {
        return false;
    }

    return !hasEarlierGroupOfSameChain(group, page);
}

/**
 * The box whose overflow this empty box would take. The nearest earlier box on the same page
 * comes first, because text must not jump over a box that could hold it. Failing that, the box
 * C# names on an earlier page, which needs a round trip: the first call for a page starts it and
 * reports nothing, and updateContinueButtons runs again when the answer arrives.
 */
export function findContinueSource(
    target: HTMLElement,
): ContinueSource | undefined {
    const onThisPage = findContinueSourceOnPage(target);
    if (onThisPage) {
        return { onThisPage: true, editable: onThisPage };
    }

    const language = target.getAttribute("lang");
    const page = target.closest<HTMLElement>(kPageSelector);
    if (!language || !page?.id) {
        return undefined;
    }

    const pending = getPendingOverflowOnEarlierPage(page, language);
    return pending ? { onThisPage: false, pending } : undefined;
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
 * Join the empty box's group to the overflowing box's group, which is what the button does for a
 * source on the same page. The source keeps the chain id it already has, so joining a third box
 * to a chain does not break the first two apart. Returns the chain id both groups now carry.
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

/**
 * Take the offer when the overflowing box is on an earlier page. C# splits the paragraph there,
 * moves the nodes, saves that page, and hands back what this page's box now holds; the browser
 * puts that in place, records the chain, and settles the page, which may find that the text does
 * not all fit here either.
 */
export async function continueFromEarlierPage(
    target: HTMLElement,
    pending: IPendingOverflow,
): Promise<void> {
    const targetGroup = target.closest<HTMLElement>(kTranslationGroupSelector);
    const page = target.closest<HTMLElement>(kPageSelector);
    const language = target.getAttribute("lang");
    if (!targetGroup || !page?.id || !language) {
        return;
    }

    const targetIndexInPage = getFlowGroupsOfPage(page).indexOf(targetGroup);
    if (targetIndexInPage < 0) {
        return;
    }

    const result = await continueInto({
        sourcePageId: pending.pageId,
        sourceIndexInPage: pending.indexInPage,
        targetPageId: page.id,
        targetIndexInPage,
        lang: language,
    });
    if (!result || !targetGroup.isConnected) {
        return;
    }

    const filled: HTMLElement[] = [];
    applyWithoutPass(() => {
        Object.keys(result.targetHtmlByLang).forEach((lang) => {
            const editable = targetGroup.querySelector<HTMLElement>(
                `:scope > .bloom-editable[lang="${lang}"]`,
            );
            if (!editable) {
                return;
            }

            editable.innerHTML = result.targetHtmlByLang[lang];
            filled.push(editable);
        });
        targetGroup.setAttribute(kFlowChainAttr, result.chainId);
        removeButtonFor(target);
    });

    // What the earlier page holds has changed, so what it can offer has changed with it.
    resetPendingOverflowCache();
    // C# moved the text without measuring anything, so this box may hold more than fits. The
    // pass puts the marker in, and a later empty page can then offer to continue in its turn.
    passRunner?.requestPassFor(
        filled.length ? filled : [target],
        "continueInto",
    );
    updateContinueButtons(page);
}

/** The page in the edit iframe. There is only ever one, and its id is what C# calls it. */
export function getCurrentPageId(
    root: ParentNode = document,
): string | undefined {
    return root.querySelector<HTMLElement>(kPageSelector)?.id || undefined;
}

/**
 * What C# says about the earlier pages, for this page and language. The first call for a page and
 * language starts the round trip and reports nothing; the answer arrives later and puts the
 * buttons up.
 */
function getPendingOverflowOnEarlierPage(
    page: HTMLElement,
    language: string,
): IPendingOverflow | undefined {
    const key = `${page.id}|${language}`;
    if (pendingOverflowByPageAndLang.has(key)) {
        return pendingOverflowByPageAndLang.get(key);
    }

    if (asking.has(key)) {
        return undefined;
    }

    asking.add(key);
    getPendingOverflow(page.id, language)
        .then((found) => {
            asking.delete(key);
            pendingOverflowByPageAndLang.set(key, found);
            if (page.isConnected) {
                updateContinueButtons(page);
            }
        })
        .catch(() => {
            // Without an answer there is no offer. The next pass asks again.
            asking.delete(key);
        });
    return undefined;
}

function addButton(editable: HTMLElement, source: ContinueSource): void {
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
        if (source.onThisPage) {
            joinToSourceBox(source.editable, editable);
        } else {
            void continueFromEarlierPage(editable, source.pending);
        }
    };
    setLabel(button, source);
    if (!existing) {
        group.appendChild(button);
    }

    placeAtTopLeftOfBox(button, editable);
}

function makeButton(document: Document, language: string): HTMLElement {
    const button = document.createElement("div");
    button.className = `bloom-ui ${kContinueButtonClass}`;
    button.setAttribute("contenteditable", "false");
    button.setAttribute("role", "button");
    button.setAttribute("data-testid", kContinueButtonTestId);
    button.setAttribute(kContinueButtonLanguageAttr, language);
    return button;
}

// How far in from the box's top left corner the button sits.
const kButtonInsetPixels = 4;

/**
 * Put the button at the top left of the box it belongs to. The button sits in the translation
 * group rather than in the box, because a box is a contenteditable and everything in it is
 * content, and a group can hold a box per language; so where the box is has to be measured.
 * The group is a positioning context already: Bloom's stylesheet makes every element one.
 */
function placeAtTopLeftOfBox(button: HTMLElement, editable: HTMLElement): void {
    button.style.top = `${editable.offsetTop + kButtonInsetPixels}px`;
    button.style.left = `${editable.offsetLeft + kButtonInsetPixels}px`;
}

/**
 * Say which box this one would continue: the box above it, or a page by name. The reader is
 * about to make the two boxes one run of text, so the label has to say where that text is now.
 */
function setLabel(button: HTMLElement, source: ContinueSource): void {
    const english = source.onThisPage
        ? kContinueButtonEnglish
        : kContinueFromPageEnglish.replace("{0}", source.pending.pageNumber);
    const l10nId = source.onThisPage
        ? kContinueButtonL10nId
        : kContinueFromPageL10nId;
    button.textContent = english;

    // A unit test has no localization manager, and the English label is right there already,
    // so a failure to localize must not stop the button from appearing.
    try {
        theOneLocalizationManager
            .asyncGetText(
                l10nId,
                source.onThisPage
                    ? kContinueButtonEnglish
                    : kContinueFromPageEnglish,
                "",
            )
            .done((text: string) => {
                if (!text || !button.isConnected) {
                    return;
                }

                if (source.onThisPage) {
                    button.textContent = text;
                    return;
                }

                // The label has to say which page the text is on, so a translation that has
                // lost the {0} is no use: keep the English label, which still names the page.
                if (text.includes("{0}")) {
                    button.textContent = text.replace(
                        "{0}",
                        source.pending.pageNumber,
                    );
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

function applyWithoutPass(work: () => void): void {
    if (passRunner) {
        passRunner.applyWithoutPass(work);
    } else {
        work();
    }
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

/**
 * Is this box part of the page's own layout? Every laid-out page keeps its content in a
 * marginBox, so a box outside one is not one of the page's own text boxes.
 */
function isInPageLayout(editable: HTMLElement): boolean {
    return editable.closest(".marginBox") !== null;
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
