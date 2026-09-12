// The boundary between the last linked box on the page being edited and the next box of the
// same chain, which is on a later page and so is not in the edit iframe at all.
//
// Everything on this side of the boundary is measured by the browser, as usual. What is on the
// other side arrives as HTML from C# (flowBoundaryClient.peekNext), is combined with this box's
// content in a detached container, split there, and written back (setNextContent). The box on
// the other page is never measured: C# does not lay anything out, and whichever page the reader
// goes to next settles its own boxes when it loads.
//
// Two directions, the same split:
//
//  - PUSH. The last box holds the marker that says where its text stops fitting, so the text
//    from the marker on belongs to the next box.
//  - PULL BACK. The last box has no marker, and the combined text says more of the next box's
//    text fits here than the box holds, so that much comes back.

import {
    getCollapsedSelectionOffsetInEditable,
    restoreCollapsedSelectionInChain,
    restoreCollapsedSelectionInEditable,
} from "./flowCaret";
import {
    getPendingCaret,
    INextBox,
    IPendingCaret,
    jumpToPage,
    peekNext,
    postPendingCaret,
    requestWalk,
    requestWalkOfEveryChain,
    setNextContent,
} from "./flowBoundaryClient";
import { getFlowGroupsOfPage, getLanguageChainOnPage } from "./flowChain";
import type { IRefitBox } from "./flowReflowClient";
import { waitForEditorReady } from "./flowEditorReady";
import { kFlowChainAttr } from "./flowConstants";
import {
    getCombinedTextAcrossPages,
    splitCombinedAcrossPages,
} from "./flowDomMove";
import { getBoxMetrics, LineMeasurer } from "./flowFit";
import {
    findLastWordStart,
    getComparableEditableLength,
    getComparableLinearizedLength,
} from "./flowLinearize";
import {
    findFitOffsetInBox,
    getOverflowMarkerOffset,
    MarkerFitProbe,
    removeOverflowMarker,
} from "./flowOverflowMarker";
import { OverflowMeasurer } from "./flowVerify";

const kPageSelector = ".bloom-page";
const kVisibleEditableSelector = ".bloom-editable.bloom-visibility-code-on";

export type CrossPageOptions = {
    /** Where the text breaks. The trigger's own measurer, so a test can script it. */
    measurer: LineMeasurer;
    /** What the real layout reports. A test scripts this, because jsdom lays nothing out. */
    measureOverflow?: OverflowMeasurer;
    /**
     * Whether the text up to an offset fits in the box, as the browser laid it out. A test
     * scripts this, because jsdom lays nothing out.
     */
    fitProbe?: MarkerFitProbe;
    /**
     * Make these changes to the page without the trigger treating them as the user's editing:
     * the changes are ours, and reacting to them would start a pass inside this one.
     */
    applyWithoutPass?: (work: () => void) => void;
};

// What the next box of each chain holds, so that a pass costs no round trip. Only this code
// writes that box, so the entry stays right until the page being edited changes.
const nextBoxByChainAndPage = new Map<string, INextBox | undefined>();

// The boundaries whose text has moved since the last call to beginCrossPageRun. A boundary
// settles once per pass, because a second move at one boundary can duplicate text: the move
// writes this box first and the box on the other page second, so between the two writes the two
// halves are each other's complement; a second move that reads this box before it has settled,
// and the other page after, sends text that is already there.
const settledThisRun = new Set<string>();

// The chains whose later pages need refitting because text has crossed the boundary. The
// request goes in once the text on this page has stopped moving, not at each boundary: one walk
// settles everything from there to the end of the chain, a walk asked for in the middle of a
// pass would refuse the pass's own next move, and a walk costs seconds. So this outlives a
// single pass: a burst of passes, which is what moving a page's worth of text is, asks once.
const walksWanted = new Map<
    string,
    {
        chainId: string;
        pageId: string;
        lang: string;
    }
>();

// Whether every chain in the book is to be refitted from its first page, which covers anything
// the map could ask for. A change that alters where the text breaks on every page at once, such
// as a style change, wants this rather than a walk from one page on.
let everyChainWanted = false;

// The boxes whose boundary Bloom would not settle because a refit of the whole chain held it.
// A refit refuses the browser's move, and only word that the refit has finished brings the
// browser back to it: without this the page the user has just emptied keeps its text, and the
// next page keeps its overflow warning, until the user's next keystroke.
export const boundariesToRetryAfterWalk = new Set<HTMLElement>();

/** Is a boundary waiting for a refit to finish before it can be settled? */
export function areBoundaryRetriesWaiting(): boolean {
    return boundariesToRetryAfterWalk.size > 0;
}

/**
 * The refit has finished: the boxes whose boundary is to be settled again. What the boxes on
 * the later pages held is worth nothing now, because the refit rewrote them, so all of it is
 * read afresh.
 */
export function onWalkFinished(): HTMLElement[] {
    nextBoxByChainAndPage.clear();
    const boxes = Array.from(boundariesToRetryAfterWalk);
    boundariesToRetryAfterWalk.clear();
    return boxes;
}

/**
 * Put into the page the content a refit made for the boxes of the page being edited. A refit
 * measures the pages the browser is not editing, but the chain can reach back into the page on
 * screen; Bloom saves that page and leaves the editor alone, so this is what brings what the
 * user is looking at up to date.
 *
 * A box is found by the place of its group among the page's flow groups, which is how Bloom
 * names a group as well, and its chain id has to match: a page whose chains have changed since
 * the refit began is not the page the content was made for. Returns the boxes it changed, for
 * the caller to settle.
 */
export function applyRefitResult(
    page: HTMLElement,
    boxes: IRefitBox[],
    options: CrossPageOptions,
): HTMLElement[] {
    const groups = getFlowGroupsOfPage(page);
    const changed: HTMLElement[] = [];
    boxes.forEach((box) => {
        const group = groups[box.indexInPage];
        if (!group || group.getAttribute(kFlowChainAttr) !== box.chainId) {
            return;
        }

        const editable = group.querySelector<HTMLElement>(
            `:scope > .bloom-editable[lang="${box.lang}"]`,
        );
        if (!editable || editable.innerHTML === box.html) {
            return;
        }

        // The caret sits at a character, and the refit has replaced every character in the box,
        // so there is nowhere in the new text that it belongs: it goes to the start of the box.
        const hadCaret =
            getCollapsedSelectionOffsetInEditable(editable) !== undefined;
        apply(editable, box.html, options);
        if (hadCaret) {
            restoreCaretHere(editable, 0, options);
        }

        changed.push(editable);
    });

    return changed;
}

/** Start a new pass over the boundaries. Every boundary may move text again. */
export function beginCrossPageRun(): void {
    settledThisRun.clear();
}

/** Is a refit of the pages the browser cannot see still to be asked for? */
export function areWalksWanted(): boolean {
    return everyChainWanted || walksWanted.size > 0;
}

/**
 * Ask Bloom for the refitting this pass has made necessary. Text that arrived on the next page
 * has to go on breaking correctly all the way to the end of the chain, and only Bloom can
 * measure the pages the browser is not editing.
 *
 * A wanted refit of every chain covers every chain from its first page, so it takes the place of
 * whatever single chains had asked for.
 */
export async function requestQueuedWalks(): Promise<void> {
    if (everyChainWanted) {
        everyChainWanted = false;
        walksWanted.clear();
        // Bloom writes the boxes of every chain on the later pages, so nothing we remember
        // about a next box is what it holds.
        nextBoxByChainAndPage.clear();
        await requestWalkOfEveryChain();
        return;
    }

    const wanted = Array.from(walksWanted.values());
    walksWanted.clear();
    for (const walk of wanted) {
        forgetNextBoxesOfChain(walk.chainId);
        await requestWalk(walk.chainId, walk.pageId, walk.lang);
    }
}

// Bloom writes the boxes of the chain on the later pages while it refits them, so what we
// remember about the next box is no longer what it holds. A pass that went on believing it
// would combine this page's text with text that has moved on, and write it back.
function forgetNextBoxesOfChain(chainId: string): void {
    for (const key of Array.from(nextBoxByChainAndPage.keys())) {
        if (key.startsWith(`${chainId}|`)) {
            nextBoxByChainAndPage.delete(key);
        }
    }
}

/**
 * Want a refit of every chain in the book, each from its first page. This is for a change that
 * alters where the text breaks without moving any of it and does so on every page at once, such
 * as a new style: a page earlier in the chain has room it did not have, and only Bloom can
 * measure the pages the browser is not editing.
 *
 * This only says what is wanted. requestQueuedWalks sends it, once this page has stopped moving
 * text: a walk asked for while the browser still has text to hand to the next page would refuse
 * that move, and the page being edited would keep text that no longer fits it.
 */
export function queueWalksForEveryChain(): void {
    everyChainWanted = true;
}

/** Forget what the boxes on the later pages hold. Call this when the page being edited changes. */
export function resetCrossPageCache(): void {
    everyChainWanted = false;
    nextBoxByChainAndPage.clear();
    settledThisRun.clear();
    boundariesToRetryAfterWalk.clear();
}

/**
 * Settle the boundary between this box, the last one of its chain on the page being edited, and
 * the next box of the chain on a later page. Returns true if any text moved.
 *
 * The text is only ever in one place: the box here is changed first, and if C# refuses the other
 * half, the box goes back to what it held.
 */
export async function settleCrossPageBoundary(
    last: HTMLElement,
    options: CrossPageOptions,
): Promise<boolean> {
    const group = last.closest<HTMLElement>(".bloom-translationGroup");
    const page = last.closest<HTMLElement>(kPageSelector);
    const chainId = group?.getAttribute(kFlowChainAttr);
    const lang = last.getAttribute("lang");
    if (!group || !page?.id || !chainId || !lang) {
        return false;
    }

    // Only the last box of the chain on this page has the next page after it.
    const chainHere = getLanguageChainOnPage(last);
    if (chainHere.length && chainHere[chainHere.length - 1] !== last) {
        return false;
    }

    const key = makeKey(chainId, page.id, lang);
    if (settledThisRun.has(key)) {
        return false;
    }

    // Everything below writes both this box and a box on another page, so it waits until the
    // box is the editor's rather than the page setup's.
    await waitForEditorReady(last);
    if (!last.isConnected) {
        return false;
    }

    const next = await getNextBox(chainId, page.id, lang);
    if (!next) {
        return false;
    }

    const caretOffset = getCollapsedSelectionOffsetInEditable(last);
    let markerOffset = getOverflowMarkerOffset(last);
    const lengthHere = getComparableEditableLength(last);
    // The marker can be older than the layout: the overflow checker places it before the
    // fonts arrive, and the text that then overflowed may fit once they have. Pushing at
    // such a marker would move text that fits, so the real layout has the last word.
    if (
        markerOffset !== undefined &&
        options.fitProbe &&
        options.fitProbe(last, lengthHere)
    ) {
        removeOverflowMarker(last);
        markerOffset = undefined;
    }
    const combinedText = getCombinedTextAcrossPages(last, next.html);
    const isPush = markerOffset !== undefined;
    const originalHtml = last.innerHTML;
    let fitOffset = markerOffset ?? 0;

    if (!isPush) {
        const proposal = options.measurer.measureFit(
            combinedText,
            getBoxMetrics(last),
        );
        if (proposal <= lengthHere) {
            // Nothing more fits here, and nothing here has to leave.
            return false;
        }

        // A push takes its offset from the marker, which the real layout placed. A pull back
        // asks the real layout the same question, so the two directions agree on how much
        // fits: the box holds all of the combined text while the layout measures it, and the
        // prediction from font metrics only says where to start looking.
        const combinedLength = getComparableLinearizedLength(combinedText);
        apply(
            last,
            splitCombinedAcrossPages(last, next.html, combinedLength)
                .currentHtml,
            options,
        );
        fitOffset = findFitOffsetInBox(
            last,
            combinedText,
            proposal,
            combinedLength,
            options.fitProbe,
        );

        // Back to what the box held, so the split below reads the same text the push does.
        apply(last, originalHtml, options);
        if (fitOffset <= lengthHere) {
            restoreCaretHere(last, caretOffset, options);
            return false;
        }
    }

    // Text moves a whole word at a time, wherever the marker sits.
    if (isPush) {
        fitOffset = snapToWordStart(combinedText, fitOffset);
    }

    const split = splitCombinedAcrossPages(last, next.html, fitOffset);
    apply(last, split.currentHtml, options);
    // The linearized text has a separator between the two boxes' text, and a fit offset that
    // lands on it, or on trailing white space, leaves every character where it was. That is
    // not a move: asking C# to write the same content would count as one, and the pages after
    // this one would be refitted for nothing.
    // A push whose marker has nothing but white space or an empty paragraph after it moves no
    // characters either, and the next box would be written with what it already holds.
    const nothingMoved = isPush
        ? split.nextHtml === next.html
        : getComparableEditableLength(last) === lengthHere;
    if (nothingMoved) {
        apply(last, originalHtml, options);
        restoreCaretHere(last, caretOffset, options);
        return false;
    }
    // The rewrite has dropped the caret, and the box keeps the focus while C# is asked to take
    // the text: a key pressed in that time must land where the caret was, not at the start of
    // the box. So the caret goes back now, before the wait, and stays wherever the typing
    // takes it: the caret is not put back again after the wait.
    //
    // A caret that was in the text that leaves goes with it, to the next page. Until that page
    // is showing, the caret here sits at the end of what stays, and what is typed is carried
    // to the next page rather than put in here, where it would come before the text that left.
    const caretLeaves =
        isPush && caretOffset !== undefined && caretOffset > fitOffset;
    restoreCaretHere(last, caretLeaves ? fitOffset : caretOffset, options);
    const carrier = caretLeaves
        ? carryTypingToNextPage(last, {
              pageId: next.pageId,
              chainId,
              lang,
              charOffset: caretOffset! - fitOffset,
          })
        : undefined;

    const answer = await setNextContent(
        chainId,
        page.id,
        lang,
        split.nextHtml,
        next.html,
    );
    if (!answer.accepted) {
        // Bloom would not take it, and one reason is that the box holds something else now. So
        // what we remember about it is worth nothing: the next pass reads it again.
        forgetNextBoxesOfChain(chainId);
        if (answer.walkInProgress) {
            // A refit holds the chain. Nothing on this page will move the text again on its
            // own, so this boundary is settled again as soon as the refit reports it is done.
            boundariesToRetryAfterWalk.add(last);
        }

        const typed = carrier?.stop() ?? "";
        apply(last, originalHtml, options);
        restoreCaretHere(last, caretOffset, options);
        if (typed) {
            insertTextAtSelection(last, typed);
        }
        return false;
    }

    nextBoxByChainAndPage.set(key, {
        ...next,
        html: split.nextHtml,
    });
    settledThisRun.add(key);
    // The next page holds different text now, so whatever follows it in the chain breaks
    // somewhere else. Only Bloom can measure those pages; requestQueuedWalks asks it to, once
    // this pass is over.
    walksWanted.set(`${chainId}|${lang}`, {
        chainId,
        pageId: next.pageId,
        lang,
    });

    if (carrier) {
        carrier.startPosting();
        await postPendingCaret(carrier.caretNow());
        jumpToPage(next.pageId);
    }

    return true;
}

/**
 * The start of the word that offset falls inside, or offset itself when it is at a word start,
 * at whitespace, or at either end of the text.
 */
/**
 * The start of the word this offset falls in, so that text moves a whole word at a time. An
 * offset already at a word boundary is left where it is.
 */
export function snapToWordStart(text: string, offset: number): number {
    if (
        offset <= 0 ||
        offset >= text.length ||
        /\s/.test(text[offset]) ||
        /\s/.test(text[offset - 1])
    ) {
        return offset;
    }

    const head = text.slice(0, offset + 1);
    if (!/\s/.test(head)) {
        return offset;
    }

    return findLastWordStart(head) ?? offset;
}

// How long the box the caret left goes on carrying what is typed to the next page. The next
// page is normally showing well within this; if it never comes, what was typed goes into this
// box, so that nothing is lost.
const kCarryTypingMs = 4000;

/** Typing carried from the box the caret has left to the page the caret is going to. */
interface ITypingCarrier {
    /** The pending caret with everything typed so far. */
    caretNow(): IPendingCaret;
    /** Once C# has the text, every later keystroke is also sent along, as it comes. */
    startPosting(): void;
    /** Stop carrying; what was typed so far comes back for the caller to put in. */
    stop(): string;
}

/**
 * Until the next page is showing, this box still has the focus, but the caret's text has
 * gone: a character typed now belongs after that text, on the other page. So what is typed is
 * kept out of this box and travels with the pending caret, for placePendingCaret to put in at
 * the caret when the next page places it. Only insertions travel; a deletion or a command in
 * this short time does nothing.
 *
 * Carrying begins before C# is asked to take the text, because the keystrokes can all arrive
 * while that request is in flight. Nothing is posted until the caller says C# has the text.
 */
function carryTypingToNextPage(
    last: HTMLElement,
    caret: IPendingCaret,
): ITypingCarrier {
    let typed = "";
    let posting = false;
    let stopped = false;
    const caretNow = () => (typed ? { ...caret, typedText: typed } : caret);
    const onBeforeInput = (event: InputEvent) => {
        event.preventDefault();
        if (event.inputType === "insertText" && event.data) {
            typed += event.data;
        } else if (
            event.inputType === "insertParagraph" ||
            event.inputType === "insertLineBreak"
        ) {
            typed += "\n";
        } else {
            return;
        }

        if (posting) {
            void postPendingCaret(caretNow());
        }
    };
    last.addEventListener("beforeinput", onBeforeInput, true);
    const stop = () => {
        if (stopped) {
            return "";
        }
        stopped = true;
        last.removeEventListener("beforeinput", onBeforeInput, true);
        return typed;
    };

    setTimeout(() => {
        const leftBehind = stop();
        if (leftBehind && last.isConnected) {
            void postPendingCaret(caret);
            insertTextAtSelection(last, leftBehind);
        }
    }, kCarryTypingMs);

    return {
        caretNow,
        startPosting: () => {
            posting = true;
        },
        stop,
    };
}

/**
 * Put text in at the selection the way typing would, so that the editor and the flow both see
 * it as the user's editing. Where the browser's editing commands do nothing (jsdom), the text
 * goes in as a text node, with a space for each paragraph break.
 */
function insertTextAtSelection(editable: HTMLElement, text: string): void {
    const doc = editable.ownerDocument;
    const command = (name: string, value?: string): boolean =>
        typeof doc.execCommand === "function" &&
        doc.execCommand(name, false, value);

    text.split("\n").forEach((segment, index) => {
        if (index > 0 && !command("insertParagraph")) {
            insertTextNodeAtSelection(doc, " ");
        }
        if (segment && !command("insertText", segment)) {
            insertTextNodeAtSelection(doc, segment);
        }
    });
}

function insertTextNodeAtSelection(doc: Document, text: string): void {
    const selection = doc.defaultView?.getSelection();
    if (!selection?.rangeCount) {
        return;
    }

    const range = selection.getRangeAt(0);
    range.deleteContents();
    const node = doc.createTextNode(text);
    range.insertNode(node);
    range.setStartAfter(node);
    range.collapse(true);
    selection.removeAllRanges();
    selection.addRange(range);
}

/**
 * Put the caret where the text it was in has arrived, if C# is holding one for this page. The
 * caller waits for the fonts first: the caret goes to a character, and which character is on
 * which line is not settled until the real fonts are in place.
 */
export async function placePendingCaret(
    root: ParentNode = document,
): Promise<boolean> {
    const page = getPage(root);
    if (!page?.id) {
        return false;
    }

    const caret = await getPendingCaret(page.id);
    if (!caret || !page.isConnected) {
        return false;
    }

    const editable = page.querySelector<HTMLElement>(
        `.bloom-translationGroup[${kFlowChainAttr}="${caret.chainId}"] > ${kVisibleEditableSelector}[lang="${caret.lang}"]`,
    );
    if (!editable) {
        return false;
    }

    // CKEditor rewrites the box as it attaches, and would take out anything put in before that.
    await waitForEditorReady(editable);
    if (!editable.isConnected) {
        return false;
    }

    const chain = getLanguageChainOnPage(editable);
    // The focus goes first: taking it moves the selection, and the caret has to end up where
    // the text it was in has arrived.
    editable.focus({ preventScroll: true });
    restoreCollapsedSelectionInChain(chain.length ? chain : [editable], {
        offset: caret.charOffset,
    });
    if (caret.typedText) {
        // Typed after the caret left its page and before this one was showing, so it belongs
        // here, after the text the caret came with. Putting it in is editing, so the pass that
        // follows settles this box like any other.
        insertTextAtSelection(editable, caret.typedText);
    }
    return true;
}

async function getNextBox(
    chainId: string,
    pageId: string,
    lang: string,
): Promise<INextBox | undefined> {
    const key = makeKey(chainId, pageId, lang);
    if (nextBoxByChainAndPage.has(key)) {
        return nextBoxByChainAndPage.get(key);
    }

    const found = await peekNext(chainId, pageId, lang);
    nextBoxByChainAndPage.set(key, found);
    return found;
}

function makeKey(chainId: string, pageId: string, lang: string): string {
    return `${chainId}|${pageId}|${lang}`;
}

function apply(
    editable: HTMLElement,
    html: string,
    options: CrossPageOptions,
): void {
    withoutPass(options, () => {
        editable.innerHTML = html;
    });
}

function restoreCaretHere(
    editable: HTMLElement,
    caretOffset: number | undefined,
    options: CrossPageOptions,
): void {
    if (caretOffset === undefined) {
        return;
    }

    withoutPass(options, () => {
        restoreCollapsedSelectionInEditable(editable, caretOffset);
    });
}

function withoutPass(options: CrossPageOptions, work: () => void): void {
    if (options.applyWithoutPass) {
        options.applyWithoutPass(work);
    } else {
        work();
    }
}

function getPage(root: ParentNode): HTMLElement | null {
    if (root instanceof HTMLElement && root.matches(kPageSelector)) {
        return root;
    }

    return root.querySelector<HTMLElement>(kPageSelector);
}
