// The only way the browser reaches a text box on a page it is not editing.
//
// Only one page is in the edit iframe, so the browser can measure and move text among the boxes
// of that page and nothing else. A chain of linked boxes usually crosses pages, and the boxes on
// the other pages live in the book's DOM in C#. Every call here goes to FlowTextApi, which reads
// or writes one of those boxes and saves its page.
//
// Two things every call has in common:
//
//  - Calls are SERIALIZED. Two round trips in flight at once would each work from the state
//    before the other, and the second answer would undo the first. A pass that wants a round trip
//    while one is running waits for the queue.
//  - Each round trip holds a save delay, so that a save asked for while text is in mid-move waits
//    for the move to finish rather than writing half of it.

import {
    addRequestPageContentDelay,
    removeRequestPageContentDelay,
} from "../js/bloomEditing";
import { getAsync, postJsonAsync, postString } from "../../utils/bloomApi";

// The id we hand the save delay. The trigger uses the same one for its own passes: they are the
// same piece of work as far as a save is concerned.
const kDelayId = "flowText";

/** A box on an earlier page whose text does not all fit in it. */
export interface IPendingOverflow {
    pageId: string;
    /** What the reader calls that page, e.g. "2". It goes into the button's label. */
    pageNumber: string;
    /** Which translation group of that page, counting the ones outside a canvas. */
    indexInPage: number;
    /** The first characters that do not fit. */
    previewText: string;
}

/** The box of a chain that comes before the boxes of this chain on the page being edited. */
export interface IPreviousBox {
    pageId: string;
    /** What the reader calls that page, e.g. "2". It can be empty: not every page has one. */
    pageNumber: string;
}

/** The next box of a chain, on a later page, and what it holds now. */
export interface INextBox {
    pageId: string;
    /** What the reader calls that page, e.g. "6". It can be empty: not every page has one. */
    pageNumber: string;
    indexInPage: number;
    html: string;
}

/** Where the caret goes when a page loads, because the text it was in moved onto that page. */
export interface IPendingCaret {
    pageId: string;
    chainId: string;
    lang: string;
    charOffset: number;
    /**
     * What was typed after the caret left its page and before the next page was showing. It
     * goes in at the caret when the caret is placed, so that nothing typed in between is lost
     * or out of order.
     */
    typedText?: string;
}

export interface IContinueIntoResult {
    chainId: string;
    /** The new content of the target box, per language, for the browser to put in place. */
    targetHtmlByLang: Record<string, string>;
}

// The tail of the queue: every call waits for it and then becomes it.
let queueTail: Promise<unknown> = Promise.resolve();
let inFlight = 0;

/** Is a round trip running or waiting? A pass that needs one waits rather than starting a second. */
export function isBoundaryBusy(): boolean {
    return inFlight > 0;
}

/** Settles when everything asked for so far has finished. */
export function waitForBoundaryQueue(): Promise<void> {
    return queueTail.then(
        () => undefined,
        () => undefined,
    );
}

/**
 * The box before this page whose text does not all fit, which is what an empty box on this page
 * offers to continue. Undefined when there is none.
 */
export function getPendingOverflow(
    beforePageId: string,
    lang: string,
): Promise<IPendingOverflow | undefined> {
    return enqueue(async () => {
        const data = await getJson<IPendingOverflow>(
            `flowText/pendingOverflow?beforePageId=${encodeURIComponent(
                beforePageId,
            )}&lang=${encodeURIComponent(lang)}`,
        );
        return data?.pageId ? data : undefined;
    });
}

/**
 * Join an empty box on the page being edited to an overflowing box on an earlier page and bring
 * the text that does not fit across. C# saves the earlier page and hands back the new content of
 * the box on this page, because this page belongs to the browser.
 */
export function continueInto(args: {
    sourcePageId: string;
    sourceIndexInPage: number;
    targetPageId: string;
    targetIndexInPage: number;
    lang: string;
}): Promise<IContinueIntoResult | undefined> {
    return enqueue(() =>
        postForJson<IContinueIntoResult>("flowText/continueInto", args),
    );
}

/**
 * The box of this chain just before its first box on this page, which is where the text on this
 * page comes from. Undefined when the chain starts on this page.
 */
export function peekPrevious(
    chainId: string,
    beforePageId: string,
): Promise<IPreviousBox | undefined> {
    return enqueue(async () => {
        const data = await getJson<IPreviousBox>(
            `flowText/previous?chainId=${encodeURIComponent(
                chainId,
            )}&beforePageId=${encodeURIComponent(beforePageId)}`,
        );
        return data?.pageId ? data : undefined;
    });
}

/** What the next box of this chain, on a later page, holds at the moment. */
export function peekNext(
    chainId: string,
    afterPageId: string,
    lang: string,
): Promise<INextBox | undefined> {
    return enqueue(async () => {
        const data = await getJson<INextBox>(
            `flowText/peekNext?chainId=${encodeURIComponent(
                chainId,
            )}&afterPageId=${encodeURIComponent(
                afterPageId,
            )}&lang=${encodeURIComponent(lang)}`,
        );
        return data?.pageId ? data : undefined;
    });
}

/** What Bloom made of an offer to put text in the next box of the chain. */
export interface ISetNextContentResult {
    accepted: boolean;
    /**
     * True when a refit of the whole chain is what stopped Bloom taking the text. The move is
     * worth making again as soon as that refit has finished, and Bloom says so over the
     * flowText websocket (walkFinished).
     */
    walkInProgress?: boolean;
}

/**
 * Put this content in the next box of the chain, on a later page, and save that page.
 *
 * expectedHtml is what that box held when the browser read it. Bloom refuses the write if the
 * box holds something else now, because then a refit has moved that text on and this content was
 * worked out from where it used to be. A refusal is reported, not thrown.
 */
export function setNextContent(
    chainId: string,
    afterPageId: string,
    lang: string,
    html: string,
    expectedHtml: string,
): Promise<ISetNextContentResult> {
    return enqueue(async () => {
        const answer = await postForJson<ISetNextContentResult>(
            "flowText/setNextContent",
            { chainId, afterPageId, lang, html, expectedHtml },
        );
        return answer ?? { accepted: false };
    }).then(
        (answer) => answer,
        // A refusal is not a failure of the pass: the caller keeps the text where it is.
        () => ({ accepted: false }),
    );
}

/** What making pages for the rest of a box's text left the book holding. */
export interface ICreatePagesResult {
    /** The chain the source group now carries, made here when the box was not linked before. */
    chainId: string;
    /**
     * The new content of the source box: what fits in it, its tail taken out. The browser puts
     * this in place itself, because it owns the page it is editing.
     */
    sourceHtml: string;
    /** How many pages Bloom made. */
    pagesCreated: number;
    /** The last page Bloom made, which is where the run of text now ends. */
    lastPageId: string;
}

/**
 * Make the pages the rest of this box's text needs and flow the text into them.
 *
 * html is what the box holds in the browser, mark and all. The box is on the page being edited,
 * so the book's own copy of it is behind the browser's, and Bloom cannot ask the browser for the
 * page from inside this call: the browser is waiting for the answer. So the content comes with
 * the request, and the new content of the box comes back for the browser to put in place, the
 * same division of labour continueInto uses.
 *
 * This does not return until every page has been made and filled, which takes a second or so per
 * page: Bloom lays each new page out off-screen to find where its text stops fitting. The
 * external-processing overlay is up for as long as it runs.
 */
export function createPagesAndContinue(args: {
    pageId: string;
    indexInPage: number;
    lang: string;
    chainId?: string;
    html: string;
}): Promise<ICreatePagesResult | undefined> {
    return enqueue(() =>
        postForJson<ICreatePagesResult>("flowText/createPagesAndContinue", {
            ...args,
            chainId: args.chainId ?? "",
            // Measured with the older rules the book holds, the new pages would break their
            // text where nothing breaks it any more. See requestWalk below.
            styles: getUserModifiedStyles(),
        }),
    );
}

/**
 * Ask Bloom to refit this chain from this page on, off-screen, so that the pages the browser
 * cannot see hold what they would hold if the user had opened each of them.
 *
 * fromPageId may be the page being edited: Bloom starts at the first page of the chain AFTER
 * that one, because the browser owns the page it is editing and settles it itself.
 *
 * This does not wait for the refit, only for Bloom to take the request. The work runs on a
 * background thread and counts as work in progress while it does, so a test can wait for it.
 */
export function requestWalk(
    chainId: string,
    fromPageId: string,
    lang: string,
): Promise<void> {
    return enqueue(async () => {
        await postJsonAsync("flowText/walk", {
            chainId,
            fromPageId,
            lang,
            // A style the user has just changed is in this page and nowhere else until the page
            // is saved, and Bloom lays the other pages out to measure them. So it goes with the
            // request: measured with the older rules the book holds, the other pages would
            // break their text where nothing breaks it any more.
            styles: getUserModifiedStyles(),
        });
    }).then(
        () => undefined,
        // A refusal is not a failure of the pass: the text is where the browser put it, and
        // the later pages are no worse off than before.
        () => undefined,
    );
}

/**
 * The style rules of the page being edited, as they stand in the browser. Read from the
 * stylesheet, not from the style element's text: the style editor changes the rules in the
 * sheet and leaves the element's text as it was when the page loaded, so the text does not
 * carry a change the user has just made. This is the same reading Bloom saves
 * (bloomEditing.userStylesheetContent).
 */
function getUserModifiedStyles(): string {
    const sheet = Array.from(document.styleSheets).find(
        (candidate) => candidate.title === "userModifiedStyles",
    ) as CSSStyleSheet | undefined;
    if (!sheet) {
        return "";
    }
    return Array.from(sheet.cssRules)
        .map((rule) => rule.cssText)
        .join("\n");
}

/** Take every box of this chain from this one on out of the chain, on the other pages. */
export function unlinkFrom(
    chainId: string,
    fromPageId: string,
    fromIndexInPage: number,
): Promise<void> {
    return enqueue(async () => {
        await postJsonAsync("flowText/unlinkFrom", {
            chainId,
            fromPageId,
            fromIndexInPage,
        });
    }).then(() => undefined);
}

/**
 * Say where the caret should go when the next page loads. The text the user was typing in has
 * just moved onto that page, so the caret follows it there.
 */
export function postPendingCaret(caret: IPendingCaret): Promise<void> {
    return enqueue(async () => {
        await postJsonAsync("flowText/pendingCaret", caret);
    }).then(() => undefined);
}

/**
 * The caret waiting for this page, if any. Reading it clears it in C#, so a caret is placed once.
 */
export function getPendingCaret(
    pageId: string,
): Promise<IPendingCaret | undefined> {
    return enqueue(async () => {
        const data = await getJson<IPendingCaret>(
            `flowText/pendingCaret?pageId=${encodeURIComponent(pageId)}`,
        );
        return data?.pageId ? data : undefined;
    });
}

/**
 * Show the page the text has just moved to. C# saves the page being edited on its way there, so
 * this must be the last thing a boundary move does.
 */
export function jumpToPage(pageId: string): void {
    void postString("editView/jumpToPage", pageId);
}

/**
 * Run this round trip after everything asked for before it, holding a save delay while it runs.
 *
 * A failure does not break the queue: the next call runs regardless. The caller decides what a
 * failure means for the text, and the text is still where it was, because nothing here moves it.
 */
function enqueue<T>(work: () => Promise<T>): Promise<T> {
    inFlight++;
    addRequestPageContentDelay(kDelayId);
    const result = queueTail.then(work, work);
    // The queue goes on after a failure; the caller sees the failure through its own promise.
    queueTail = result.then(
        () => undefined,
        () => undefined,
    );
    return result.finally(() => {
        inFlight--;
        removeRequestPageContentDelay(kDelayId);
    });
}

async function getJson<T>(endpoint: string): Promise<T | undefined> {
    const response = await getAsync(endpoint);
    return response?.data as T | undefined;
}

async function postForJson<T>(
    endpoint: string,
    body: unknown,
): Promise<T | undefined> {
    const response = await postJsonAsync(endpoint, body);
    return (response && (response as { data?: T }).data) || undefined;
}
