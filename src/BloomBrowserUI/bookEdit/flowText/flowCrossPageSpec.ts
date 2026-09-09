import { beforeEach, describe, expect, it, vi } from "vitest";
import { BoxMetrics, LineMeasurer } from "./flowFit";
import { MarkerFitProbe } from "./flowOverflowMarker";
import { kOverflowMarkerContent } from "./flowConstants";

// What C# would answer, and what it was asked. The next box lives on a page the browser cannot
// see, so this is the only view of it a test has.
let nextBox: { pageId: string; indexInPage: number; html: string } | undefined;
let acceptNextContent = true;
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
            return Promise.resolve(false);
        }

        sentContent.push({ chainId, afterPageId, html });
        return Promise.resolve(true);
    },
    getPendingCaret: () => Promise.resolve(pendingCaret),
    postPendingCaret: (caret) => {
        postedCarets.push(caret);
        return Promise.resolve();
    },
    jumpToPage: (pageId: string) => {
        jumps.push(pageId);
    },
}));

import {
    beginCrossPageRun,
    placePendingCaret,
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
    beforeEach(() => {
        document.body.innerHTML = "";
        window.getSelection()?.removeAllRanges();
        resetCrossPageCache();
        nextBox = { pageId: "page-3", indexInPage: 0, html: "<p>next</p>" };
        acceptNextContent = true;
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
