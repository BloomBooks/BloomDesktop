import { beforeEach, describe, expect, it, vi } from "vitest";
import { BoxMetrics, LineMeasurer } from "./flowFit";
import {
    getOverflowMarkerOffset,
    hasOverflowMarker,
    MarkerFitProbe,
} from "./flowOverflowMarker";
import { kOverflowMarkerContent } from "./flowConstants";

// What C# would answer, and what it was asked. The next box lives on a page the browser cannot
// see, so this is the only view of it a test has.
let nextBox: { pageId: string; indexInPage: number; html: string } | undefined;
let acceptNextContent = true;
// What C# says stopped it taking the text: a refit of the whole chain holds it, which is the
// one refusal the browser comes back to.
let refuseBecauseWalkInProgress = false;
type PendingCaret = {
    pageId: string;
    chainId: string;
    lang: string;
    charOffset: number;
    typedText?: string;
};
let pendingCaret: PendingCaret | undefined;
const sentContent: { chainId: string; afterPageId: string; html: string }[] =
    [];
const postedCarets: PendingCaret[] = [];
const jumps: string[] = [];
let peekCount = 0;
// The refits of the later pages the browser has asked C# for.
const walkRequests: { chainId: string; fromPageId: string; lang: string }[] =
    [];
// What happens while C# is still being asked to take the text.
let whileNextContentPending: (() => void) | undefined;

vi.mock("./flowBoundaryClient", () => ({
    peekNext: () => {
        peekCount++;
        return Promise.resolve(nextBox);
    },
    setNextContent: (chainId: string, afterPageId: string, _lang, html) => {
        whileNextContentPending?.();
        if (!acceptNextContent) {
            return Promise.resolve({
                accepted: false,
                walkInProgress: refuseBecauseWalkInProgress,
            });
        }

        sentContent.push({ chainId, afterPageId, html });
        return Promise.resolve({ accepted: true });
    },
    getPendingCaret: () => Promise.resolve(pendingCaret),
    postPendingCaret: (caret) => {
        postedCarets.push(caret);
        return Promise.resolve();
    },
    jumpToPage: (pageId: string) => {
        jumps.push(pageId);
    },
    requestWalk: (chainId: string, fromPageId: string, lang: string) => {
        walkRequests.push({ chainId, fromPageId, lang });
        return Promise.resolve();
    },
}));

import {
    applyRefitResult,
    areBoundaryRetriesWaiting,
    areWalksWanted,
    beginCrossPageRun,
    onWalkFinished,
    placePendingCaret,
    requestQueuedWalks,
    resetCrossPageCache,
    settleCrossPageBoundary,
} from "./flowCrossPage";

/** Fits a set number of characters in every box, because jsdom lays nothing out. */
function makeMeasurer(fitCount: number): LineMeasurer {
    return {
        measureFit: (text: string, _metrics: BoxMetrics) =>
            Math.min(text.length, fitCount),
    };
}

/** Says the text up to a set offset fits, because jsdom lays nothing out. */
function makeFitProbe(fitCount: number): MarkerFitProbe {
    return (_editable: HTMLElement, offset: number) => offset <= fitCount;
}

function makePage(innerHtml: string): {
    page: HTMLElement;
    editable: HTMLElement;
} {
    const page = document.createElement("div");
    page.className = "bloom-page";
    page.id = "page-2";
    const group = document.createElement("div");
    group.className = "bloom-translationGroup normal-style";
    group.setAttribute("data-flow-chain", "chain-1");
    const editable = document.createElement("div");
    editable.className = "bloom-editable bloom-visibility-code-on";
    editable.setAttribute("lang", "en");
    editable.setAttribute("contenteditable", "true");
    editable.innerHTML = innerHtml;
    group.appendChild(editable);
    page.appendChild(group);
    document.body.appendChild(page);
    return { page, editable };
}

/** A box whose text stops fitting after "keep ". */
function makeMarkedPage(): { page: HTMLElement; editable: HTMLElement } {
    return makePage(
        `<p>keep <span class="bloom-overflowStart">${kOverflowMarkerContent}</span>tail text</p>`,
    );
}

function putCaretIn(editable: HTMLElement, offset: number): void {
    const walker = document.createTreeWalker(editable, NodeFilter.SHOW_TEXT);
    let remaining = offset;
    while (walker.nextNode()) {
        const node = walker.currentNode as Text;
        if (node.parentElement?.classList.contains("bloom-overflowStart")) {
            continue;
        }

        if (remaining <= node.length) {
            const range = document.createRange();
            range.setStart(node, remaining);
            range.collapse(true);
            const selection = window.getSelection()!;
            selection.removeAllRanges();
            selection.addRange(range);
            return;
        }

        remaining -= node.length;
    }
}

const kOptions = { measurer: makeMeasurer(8), measureOverflow: () => 0 };

describe("flowCrossPage", () => {
    beforeEach(async () => {
        document.body.innerHTML = "";
        window.getSelection()?.removeAllRanges();
        resetCrossPageCache();
        // What one test's move asked for outlives that test, so it is sent and forgotten here.
        await requestQueuedWalks();
        walkRequests.length = 0;
        nextBox = { pageId: "page-3", indexInPage: 0, html: "<p>next</p>" };
        acceptNextContent = true;
        refuseBecauseWalkInProgress = false;
        pendingCaret = undefined;
        sentContent.length = 0;
        postedCarets.length = 0;
        jumps.length = 0;
        peekCount = 0;
        whileNextContentPending = undefined;
    });

    it("leaves the text alone when the chain ends on this page", async () => {
        nextBox = undefined;
        const { editable } = makeMarkedPage();

        expect(await settleCrossPageBoundary(editable, kOptions)).toBe(false);
        expect(sentContent).toHaveLength(0);
        expect(editable.textContent).toContain("tail text");
    });

    it("pushes the text after the marker onto the next page", async () => {
        const { editable } = makeMarkedPage();

        expect(await settleCrossPageBoundary(editable, kOptions)).toBe(true);

        // The space at the cut goes with the text that moves, so this box ends at "keep".
        expect(editable.textContent).toBe("keep");
        expect(sentContent).toHaveLength(1);
        expect(sentContent[0].chainId).toBe("chain-1");
        expect(sentContent[0].afterPageId).toBe("page-2");
        // The text that left this page comes first, and the text that was there follows it.
        expect(sentContent[0].html).toContain("tail text");
        expect(sentContent[0].html.indexOf("tail")).toBeLessThan(
            sentContent[0].html.indexOf("next"),
        );
    });

    it("pulls nothing back at a boundary that pushed in the same pass", async () => {
        const { editable } = makeMarkedPage();
        // The push is the start state of this test: the box now holds only "keep", and a
        // generous measurer says much more of the next page's text fits here.
        expect(await settleCrossPageBoundary(editable, kOptions)).toBe(true);
        expect(editable.textContent).toBe("keep");
        expect(sentContent).toHaveLength(1);

        const pulled = await settleCrossPageBoundary(editable, {
            measurer: makeMeasurer(50),
            fitProbe: () => true,
        });

        expect(pulled).toBe(false);
        expect(editable.textContent).toBe("keep");
        expect(sentContent).toHaveLength(1);
    });

    it("pulls back again at that boundary in the next pass", async () => {
        const { editable } = makeMarkedPage();
        expect(await settleCrossPageBoundary(editable, kOptions)).toBe(true);

        beginCrossPageRun();
        const pulled = await settleCrossPageBoundary(editable, {
            measurer: makeMeasurer(50),
            fitProbe: () => true,
        });

        expect(pulled).toBe(true);
        expect(editable.textContent).not.toBe("keep");
    });

    it("marks the text it pushed as continuing the paragraph it came from", async () => {
        const { editable } = makeMarkedPage();

        await settleCrossPageBoundary(editable, kOptions);

        expect(sentContent[0].html).toContain('data-flow-continuation="true"');
    });

    it("takes no marker across: where the text stops fitting is measured afresh", async () => {
        const { editable } = makeMarkedPage();

        await settleCrossPageBoundary(editable, kOptions);

        expect(sentContent[0].html).not.toContain("bloom-overflowStart");
        expect(editable.querySelector("span.bloom-overflowStart")).toBeNull();
    });

    it("sends the caret after the text it was in", async () => {
        const { editable } = makeMarkedPage();
        // Two characters into "tail text", which is going to the next page.
        putCaretIn(editable, 7);

        await settleCrossPageBoundary(editable, kOptions);

        expect(postedCarets).toEqual([
            {
                pageId: "page-3",
                chainId: "chain-1",
                lang: "en",
                charOffset: 2,
            },
        ]);
        expect(jumps).toEqual(["page-3"]);
    });

    it("keeps the caret at the end of what stays while C# is asked to take the rest", async () => {
        const { editable } = makeMarkedPage();
        putCaretIn(editable, 7);

        await settleCrossPageBoundary(editable, kOptions);

        const selection = window.getSelection()!;
        expect(selection.isCollapsed).toBe(true);
        expect(editable.contains(selection.anchorNode)).toBe(true);
        expect(selection.anchorNode?.textContent).toBe("keep");
        expect(selection.anchorOffset).toBe("keep".length);
    });

    it("carries what is typed before the next page shows along with the caret", async () => {
        const { editable } = makeMarkedPage();
        putCaretIn(editable, 7);
        await settleCrossPageBoundary(editable, kOptions);
        expect(postedCarets).toHaveLength(1);

        const typing = new InputEvent("beforeinput", {
            inputType: "insertText",
            data: "xy",
            bubbles: true,
            cancelable: true,
        });
        const wentIn = editable.dispatchEvent(typing);

        expect(wentIn, "the typing must not go into this box").toBe(false);
        expect(editable.textContent).toBe("keep");
        expect(postedCarets[postedCarets.length - 1]).toEqual({
            pageId: "page-3",
            chainId: "chain-1",
            lang: "en",
            charOffset: 2,
            typedText: "xy",
        });
    });

    it("carries what is typed while C# is still being asked to take the text", async () => {
        const { editable } = makeMarkedPage();
        putCaretIn(editable, 7);
        whileNextContentPending = () => {
            const wentIn = editable.dispatchEvent(
                new InputEvent("beforeinput", {
                    inputType: "insertText",
                    data: "xy",
                    bubbles: true,
                    cancelable: true,
                }),
            );
            expect(wentIn, "the typing must not go into this box").toBe(false);
        };

        await settleCrossPageBoundary(editable, kOptions);

        expect(editable.textContent).toBe("keep");
        expect(postedCarets).toEqual([
            {
                pageId: "page-3",
                chainId: "chain-1",
                lang: "en",
                charOffset: 2,
                typedText: "xy",
            },
        ]);
        expect(jumps).toEqual(["page-3"]);
    });

    it("gives back what was typed when C# refuses the text the caret went with", async () => {
        acceptNextContent = false;
        const { editable } = makeMarkedPage();
        putCaretIn(editable, 7);
        whileNextContentPending = () => {
            editable.dispatchEvent(
                new InputEvent("beforeinput", {
                    inputType: "insertText",
                    data: "xy",
                    bubbles: true,
                    cancelable: true,
                }),
            );
        };

        expect(await settleCrossPageBoundary(editable, kOptions)).toBe(false);

        // The caret was two characters into "tail text", and the typing lands there.
        expect(editable.textContent?.replace(kOverflowMarkerContent, "")).toBe(
            "keep taxyil text",
        );
        expect(postedCarets).toEqual([]);
        expect(jumps).toEqual([]);
    });

    it("keeps the caret here when the text it was in stayed", async () => {
        const { editable } = makeMarkedPage();
        putCaretIn(editable, 2);

        await settleCrossPageBoundary(editable, kOptions);

        expect(postedCarets).toHaveLength(0);
        expect(jumps).toHaveLength(0);
    });

    it("pulls back as much of the next page's text as fits", async () => {
        nextBox = { pageId: "page-3", indexInPage: 0, html: "<p>de fghij</p>" };
        const { editable } = makePage("<p>abc</p>");

        expect(
            await settleCrossPageBoundary(editable, {
                measurer: makeMeasurer(7),
                fitProbe: makeFitProbe(7),
            }),
        ).toBe(true);

        // The paragraph break between the two boxes counts as one character of the combined
        // text, the same as it does within a box, so seven characters is "abc", the break, "de"
        // and the space after it. Text moves a whole word at a time, so "fghij" stays.
        expect(editable.textContent).toBe("abcde");
        expect(sentContent[0].html).toContain("fghij");
        expect(sentContent[0].html).not.toContain("de");
    });

    it("removes a marker the real layout contradicts, and moves nothing", async () => {
        const { editable } = makeMarkedPage();
        expect(hasOverflowMarker(editable)).toBe(true);

        // The layout says the whole text fits, so the marker is stale.
        expect(
            await settleCrossPageBoundary(editable, {
                measurer: makeMeasurer(8),
                fitProbe: makeFitProbe(500),
            }),
        ).toBe(false);

        expect(hasOverflowMarker(editable)).toBe(false);
        expect(editable.textContent).toBe("keep tail text");
        expect(sentContent).toHaveLength(0);
    });

    it("pulls back nothing when the box is already full", async () => {
        nextBox = { pageId: "page-3", indexInPage: 0, html: "<p>defghij</p>" };
        const { editable } = makePage("<p>abc</p>");

        expect(
            await settleCrossPageBoundary(editable, {
                measurer: makeMeasurer(3),
                fitProbe: makeFitProbe(3),
            }),
        ).toBe(false);

        expect(editable.textContent).toBe("abc");
        expect(sentContent).toHaveLength(0);
    });

    it("keeps only what the real layout says fits, not what the measurer predicted", async () => {
        nextBox = {
            pageId: "page-3",
            indexInPage: 0,
            html: "<p>two words</p>",
        };
        const { editable } = makePage("<p>one </p>");

        // The measurer says all of "one two words" fits; the layout allows only as far as
        // the start of "words", and that is where the text is cut.
        await settleCrossPageBoundary(editable, {
            measurer: makeMeasurer(50),
            fitProbe: makeFitProbe(9),
        });

        expect(editable.textContent).toBe("one two");
        expect(sentContent[0].html).toContain("words");
    });

    it("pulls back nothing when the real layout says no more fits, even though the measurer predicted room", async () => {
        nextBox = { pageId: "page-3", indexInPage: 0, html: "<p>defghij</p>" };
        const { editable } = makePage("<p>abc</p>");
        const before = editable.innerHTML;

        expect(
            await settleCrossPageBoundary(editable, {
                measurer: makeMeasurer(50),
                fitProbe: makeFitProbe(3),
            }),
        ).toBe(false);

        expect(editable.innerHTML).toBe(before);
        expect(sentContent).toHaveLength(0);
    });

    it("puts the text back when C# refuses the other half", async () => {
        acceptNextContent = false;
        const { editable } = makeMarkedPage();
        const before = editable.innerHTML;

        expect(await settleCrossPageBoundary(editable, kOptions)).toBe(false);
        expect(editable.innerHTML).toBe(before);
    });

    it("keeps the text and waits for the refit when C# says one is running", async () => {
        acceptNextContent = false;
        refuseBecauseWalkInProgress = true;
        const { editable } = makeMarkedPage();
        const before = editable.innerHTML;
        // Sanity check: nothing is waiting before the refused move.
        expect(areBoundaryRetriesWaiting()).toBe(false);

        expect(await settleCrossPageBoundary(editable, kOptions)).toBe(false);

        expect(editable.innerHTML).toBe(before);
        expect(sentContent).toHaveLength(0);
        expect(areBoundaryRetriesWaiting()).toBe(true);
    });

    it("waits for nothing when C# refuses for a reason a refit will not mend", async () => {
        acceptNextContent = false;
        refuseBecauseWalkInProgress = false;
        const { editable } = makeMarkedPage();

        expect(await settleCrossPageBoundary(editable, kOptions)).toBe(false);

        expect(areBoundaryRetriesWaiting()).toBe(false);
    });

    it("hands back the boundary to settle again once the refit has finished", async () => {
        acceptNextContent = false;
        refuseBecauseWalkInProgress = true;
        const { editable } = makeMarkedPage();
        await settleCrossPageBoundary(editable, kOptions);
        // Sanity check: the box is recorded, and C# has been asked what the next box holds.
        expect(areBoundaryRetriesWaiting()).toBe(true);
        expect(peekCount).toBe(1);

        expect(onWalkFinished()).toEqual([editable]);

        expect(areBoundaryRetriesWaiting()).toBe(false);
        // The refit rewrote the later pages, so the next pass reads that box afresh.
        acceptNextContent = true;
        beginCrossPageRun();
        await settleCrossPageBoundary(editable, kOptions);
        expect(peekCount).toBe(2);
    });

    it("moves nothing when the fit lands on the join between the two boxes", async () => {
        // The linearized text has a separator between this box's text and the next box's, so a
        // fit offset one past this box's own length leaves every character where it is. Sending
        // that to C# would count as a move and have the pages after this one refitted for
        // nothing, which is a page load's worth of work on every page turn.
        const { editable } = makePage("<p>keep</p>");
        const before = editable.innerHTML;
        // Sanity check: nothing is waiting to be asked for before the boundary is settled.
        expect(areWalksWanted()).toBe(false);

        const moved = await settleCrossPageBoundary(editable, {
            measurer: makeMeasurer("keep".length + 1),
            fitProbe: makeFitProbe("keep".length + 1),
        });

        expect(moved).toBe(false);
        expect(sentContent).toHaveLength(0);
        expect(editable.innerHTML).toBe(before);
        expect(areWalksWanted()).toBe(false);
    });

    it("moves nothing when the marker sits at the end of the box's text", async () => {
        // The overflow checker can mark the very end of the text, where the paragraph the box
        // ends with is empty or is nothing but white space. There is no character after the
        // mark to hand on, so the next box would be written with what it already holds, and
        // the pages after this one refitted for nothing.
        const { editable } = makePage(
            `<p>keep<span class="bloom-overflowStart">${kOverflowMarkerContent}</span></p>`,
        );
        const before = editable.innerHTML;
        // Sanity check: this is a push, at the last character of the box, and nothing is
        // waiting to be asked for.
        expect(hasOverflowMarker(editable)).toBe(true);
        expect(getOverflowMarkerOffset(editable)).toBe("keep".length);
        expect(areWalksWanted()).toBe(false);

        const moved = await settleCrossPageBoundary(editable, kOptions);

        expect(moved).toBe(false);
        expect(sentContent).toHaveLength(0);
        expect(editable.innerHTML).toBe(before);
        expect(areWalksWanted()).toBe(false);
    });

    it("asks C# what the next box holds once per page", async () => {
        const { editable } = makeMarkedPage();

        await settleCrossPageBoundary(editable, kOptions);
        await settleCrossPageBoundary(editable, kOptions);

        expect(peekCount).toBe(1);
    });

    it("puts the caret where C# says the text it was in has arrived", async () => {
        pendingCaret = {
            pageId: "page-2",
            chainId: "chain-1",
            lang: "en",
            charOffset: 3,
        };
        const { page, editable } = makePage("<p>hello world</p>");

        expect(await placePendingCaret(page)).toBe(true);

        const selection = window.getSelection()!;
        expect(editable.contains(selection.anchorNode)).toBe(true);
    });

    it("puts in what was typed while this page was on its way, at the caret", async () => {
        pendingCaret = {
            pageId: "page-2",
            chainId: "chain-1",
            lang: "en",
            charOffset: 3,
            typedText: "xy",
        };
        const { page, editable } = makePage("<p>hello world</p>");

        expect(await placePendingCaret(page)).toBe(true);

        expect(editable.textContent).toBe("helxylo world");
        const selection = window.getSelection()!;
        expect(selection.isCollapsed).toBe(true);
        expect(editable.contains(selection.anchorNode)).toBe(true);
    });

    it("places no caret when C# is holding none for this page", async () => {
        const { page } = makePage("<p>hello</p>");

        expect(await placePendingCaret(page)).toBe(false);
    });
});

/**
 * A page of three groups: one in no chain, then two of chain-1. Each group holds a box per
 * language. A refit names a group by its place in this list, so the group in no chain is there
 * to make the place and the chain two different things.
 */
function makeMultiGroupPage(): { page: HTMLElement; groups: HTMLElement[] } {
    const page = document.createElement("div");
    page.className = "bloom-page";
    page.id = "page-2";
    const groups = [
        addGroupTo(page, undefined),
        addGroupTo(page, "chain-1"),
        addGroupTo(page, "chain-1"),
    ];
    document.body.appendChild(page);
    return { page, groups };
}

function addGroupTo(
    page: HTMLElement,
    chainId: string | undefined,
): HTMLElement {
    const group = document.createElement("div");
    group.className = "bloom-translationGroup normal-style";
    if (chainId) {
        group.setAttribute("data-flow-chain", chainId);
    }

    ["en", "fr"].forEach((lang) => {
        const editable = document.createElement("div");
        editable.className = "bloom-editable bloom-visibility-code-on";
        editable.setAttribute("lang", lang);
        editable.setAttribute("contenteditable", "true");
        editable.innerHTML = `<p>${lang} text</p>`;
        group.appendChild(editable);
    });
    page.appendChild(group);
    return group;
}

describe("applyRefitResult", () => {
    // How many times the caller was asked to make a change without starting a pass. Every write
    // this code makes goes through that, so a count of zero is a page nothing was written to.
    let writeCount = 0;
    let options: {
        measurer: LineMeasurer;
        applyWithoutPass: (work: () => void) => void;
    };

    beforeEach(() => {
        document.body.innerHTML = "";
        window.getSelection()?.removeAllRanges();
        writeCount = 0;
        options = {
            measurer: makeMeasurer(8),
            applyWithoutPass: (work: () => void) => {
                writeCount++;
                work();
            },
        };
    });

    it("puts the refit's content in the box of the named group and language", () => {
        const { page, groups } = makeMultiGroupPage();

        const changed = applyRefitResult(
            page,
            [
                {
                    chainId: "chain-1",
                    lang: "fr",
                    indexInPage: 2,
                    html: "<p>refitted</p>",
                },
            ],
            options,
        );

        const expected = groups[2].querySelector('[lang="fr"]');
        expect(changed).toEqual([expected]);
        expect(expected!.innerHTML).toBe("<p>refitted</p>");
        // Every other box is as it was: the place in the list and the language both matter.
        expect(groups[2].querySelector('[lang="en"]')!.innerHTML).toBe(
            "<p>en text</p>",
        );
        expect(groups[1].querySelector('[lang="fr"]')!.innerHTML).toBe(
            "<p>fr text</p>",
        );
    });

    it("leaves a group whose chain is not the one the refit named alone", () => {
        // The page's chains can have changed since the refit began, and then the content it made
        // is not for this box.
        const { page, groups } = makeMultiGroupPage();

        const changed = applyRefitResult(
            page,
            [
                {
                    chainId: "chain-2",
                    lang: "en",
                    indexInPage: 1,
                    html: "<p>refitted</p>",
                },
            ],
            options,
        );

        expect(changed).toEqual([]);
        expect(writeCount).toBe(0);
        expect(groups[1].querySelector('[lang="en"]')!.innerHTML).toBe(
            "<p>en text</p>",
        );
    });

    it("writes nothing when the box already holds what the refit made", () => {
        const { page } = makeMultiGroupPage();

        const changed = applyRefitResult(
            page,
            [
                {
                    chainId: "chain-1",
                    lang: "en",
                    indexInPage: 1,
                    html: "<p>en text</p>",
                },
            ],
            options,
        );

        expect(changed).toEqual([]);
        expect(writeCount).toBe(0);
    });

    it("puts the caret at the start of a box it rewrote under the caret", () => {
        const { page, groups } = makeMultiGroupPage();
        const editable = groups[1].querySelector<HTMLElement>('[lang="en"]')!;
        putCaretIn(editable, 4);
        // Sanity check: the caret is where the test put it, not at the start.
        expect(window.getSelection()!.anchorOffset).toBe(4);

        applyRefitResult(
            page,
            [
                {
                    chainId: "chain-1",
                    lang: "en",
                    indexInPage: 1,
                    html: "<p>refitted</p>",
                },
            ],
            options,
        );

        const selection = window.getSelection()!;
        expect(editable.contains(selection.anchorNode)).toBe(true);
        expect(selection.isCollapsed).toBe(true);
        expect(selection.anchorOffset).toBe(0);
    });

    it("leaves a caret that was in another box where it is", () => {
        const { page, groups } = makeMultiGroupPage();
        const elsewhere = groups[0].querySelector<HTMLElement>('[lang="en"]')!;
        putCaretIn(elsewhere, 4);

        applyRefitResult(
            page,
            [
                {
                    chainId: "chain-1",
                    lang: "en",
                    indexInPage: 1,
                    html: "<p>refitted</p>",
                },
            ],
            options,
        );

        const selection = window.getSelection()!;
        expect(elsewhere.contains(selection.anchorNode)).toBe(true);
        expect(selection.anchorOffset).toBe(4);
    });
});
