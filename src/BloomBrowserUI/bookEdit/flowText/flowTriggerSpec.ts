import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { BoxMetrics, LineMeasurer } from "./flowFit";
import { getFlowPassSamples, kMetricsPropertyName } from "./flowTiming";
// What C# would answer. Most of what a boundary does is exercised in flowCrossPageSpec; here
// the next box exists only for the tests about when a boundary is settled.
let nextBox: { pageId: string; indexInPage: number; html: string } | undefined;
let nextContentAnswer: { accepted: boolean; walkInProgress?: boolean } = {
    accepted: true,
};
let setNextContentCount = 0;
// The content the browser offered C# for the box on the next page, in order.
const sentContent: string[] = [];
// The pages the trigger asked Bloom to open, in the order it asked.
const pagesShown: string[] = [];
// The pages the trigger asked Bloom to take away, in the order it asked.
const pagesDeleted: string[] = [];
// What the boxes of the page held at the moment the trigger asked for the page to be taken
// away. Taking it away saves it, so what a refit made has to be in them by then.
let boxesWhenDeleteAsked: string[] = [];

vi.mock("./flowBoundaryClient", () => ({
    peekNext: () => Promise.resolve(nextBox),
    peekPrevious: () => Promise.resolve(undefined),
    setNextContent: (
        _chainId: string,
        _afterPageId: string,
        _lang: string,
        html: string,
    ) => {
        setNextContentCount++;
        sentContent.push(html);
        return Promise.resolve(nextContentAnswer);
    },
    getPendingCaret: () => Promise.resolve(undefined),
    getPendingOverflow: () => Promise.resolve(undefined),
    postPendingCaret: () => Promise.resolve(),
    requestWalk: () => Promise.resolve(),
    requestWalkOfEveryChain: () => Promise.resolve(),
    unlinkFrom: () => Promise.resolve(),
    jumpToPage: (pageId: string) => {
        pagesShown.push(pageId);
    },
    deleteEmptiedPage: (pageId: string) => {
        pagesDeleted.push(pageId);
        boxesWhenDeleteAsked = Array.from(
            document.querySelectorAll<HTMLElement>(".bloom-editable"),
        ).map((box) => box.innerHTML);
        return Promise.resolve();
    },
}));

// What a refit changed in the boxes of the page being edited, which Bloom hands over once, and
// how many times the browser has asked for it.
let refitResult: {
    chainId: string;
    lang: string;
    indexInPage: number;
    html: string;
}[] = [];
let refitResultAsks = 0;

vi.mock("./flowReflowClient", () => ({
    getPendingWalks: () => Promise.resolve(undefined),
    getRefitResult: () => {
        refitResultAsks++;
        const taken = refitResult;
        refitResult = [];
        return Promise.resolve(taken);
    },
    getReflowOnPageChange: () => Promise.resolve(false),
    postReflowNow: () => Promise.resolve(),
    postReflowOnPageChange: () => Promise.resolve(),
}));

// The listener flowTrigger puts on the flowText websocket, so that a test can send it the
// event C# sends when a refit finishes.
let socketListener:
    | ((event: { id: string; message?: string }) => void)
    | undefined;

vi.mock("../../utils/WebSocketManager", () => ({
    default: {
        addListener: (
            _context: string,
            listener: (event: { id: string }) => void,
        ) => {
            socketListener = listener;
        },
        removeListener: () => {
            socketListener = undefined;
        },
    },
}));

import { kReflowingAttr } from "./flowConstants";
import {
    FlowTextOptions,
    reflowAllChainsOnPage,
    requestPassFor,
    setupFlowText,
    suspendFlowText,
    waitForBoundaryWork,
} from "./flowTrigger";
import { setFlowTextAvailableForTesting } from "./flowTextAvailable";

// The real fit probe asks a Range for its rectangles, and jsdom's Range has no such method.
// An answer of no rectangles is what jsdom means: nothing is laid out, so all of it "fits".
(
    Range.prototype as unknown as { getClientRects: () => DOMRect[] }
).getClientRects = () => [];

// jsdom lays nothing out, so the real measurer would have nothing to work from. This one
// fits eight characters in every box, which is enough to make the text move.
const kFakeMeasurer: LineMeasurer = {
    measureFit: (text: string, _metrics: BoxMetrics) =>
        Math.min(text.length, 8),
};

// Fits the whole of whatever it is given, so that a pull back across the boundary brings a
// word back rather than stopping at the separator between the two boxes' text.
const kRoomForEverythingMeasurer: LineMeasurer = {
    measureFit: (text: string, _metrics: BoxMetrics) => text.length,
};

let frameQueue: Array<(() => void) | undefined> = [];

function makeOptions(): FlowTextOptions {
    return {
        measurer: kFakeMeasurer,
        measureOverflow: () => 0,
        requestFrame: (callback) => frameQueue.push(callback),
        cancelFrame: (handle) => {
            frameQueue[handle - 1] = undefined;
        },
    };
}

function runFrames(): void {
    const queued = frameQueue;
    frameQueue = [];
    queued.forEach((callback) => callback?.());
}

/** Let every round of boundary work, and any retry a refusal asked for, run to a standstill. */
async function settleAllWork(): Promise<void> {
    for (let tick = 0; tick < 10; tick++) {
        await new Promise((resolve) => setTimeout(resolve, 0));
        await waitForBoundaryWork();
    }
}

/** Let the MutationObserver deliver its records. */
function flushMutations(): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, 0));
}

function clearSamples(): void {
    (window as unknown as Record<string, unknown>)[kMetricsPropertyName] = [];
}

function makePage(chainedGroupCount: number, unchainedGroup = false) {
    const page = document.createElement("div");
    page.className = "bloom-page";

    const boxes: HTMLElement[] = [];
    for (let index = 0; index < chainedGroupCount; index++) {
        boxes.push(addGroup(page, "chain-1"));
    }

    const loneBox = unchainedGroup ? addGroup(page, undefined) : undefined;
    document.body.appendChild(page);
    return { page, boxes, loneBox };
}

function addGroup(page: HTMLElement, chainId: string | undefined): HTMLElement {
    const group = document.createElement("div");
    group.className = "bloom-translationGroup normal-style";
    if (chainId) {
        group.setAttribute("data-flow-chain", chainId);
    }

    const editable = document.createElement("div");
    editable.className = "bloom-editable bloom-visibility-code-on";
    editable.setAttribute("lang", "xkal");
    editable.setAttribute("contenteditable", "true");
    editable.innerHTML = "<p>short</p>";
    group.appendChild(editable);
    page.appendChild(group);
    return editable;
}

function reasonsOfPasses(): string[] {
    return getFlowPassSamples().map((sample) => sample.reason);
}

describe("flowTrigger", () => {
    beforeEach(() => {
        // The collection is allowed to use flow text; nothing here has a Bloom to ask.
        setFlowTextAvailableForTesting(true);
        document.body.innerHTML = "";
        frameQueue = [];
        clearSamples();
        nextBox = undefined;
        nextContentAnswer = { accepted: true };
        setNextContentCount = 0;
        sentContent.length = 0;
        refitResult = [];
        refitResultAsks = 0;
        pagesShown.length = 0;
        pagesDeleted.length = 0;
        boxesWhenDeleteAsked = [];
    });

    afterEach(() => {
        suspendFlowText();
    });

    it("settles the chains once as the page opens", () => {
        const { page } = makePage(2);

        setupFlowText(page, makeOptions());

        expect(reasonsOfPasses()).toEqual(["load"]);
    });

    it("runs exactly one pass for the edits made before the frame arrives", async () => {
        const { page, boxes } = makePage(2);
        setupFlowText(page, makeOptions());
        clearSamples();

        boxes[0].querySelector("p")!.textContent =
            "text that is much too long for this box";
        boxes[0].querySelector("p")!.textContent += " and longer still";
        await flushMutations();

        expect(reasonsOfPasses()).toEqual([]);
        runFrames();

        expect(reasonsOfPasses()).toEqual(["mutation"]);
        expect(boxes[1].textContent).not.toBe("short");
    });

    it("ignores an edit in a box that is in no chain", async () => {
        const { page, loneBox } = makePage(2, true);
        setupFlowText(page, makeOptions());
        clearSamples();

        loneBox!.querySelector("p")!.textContent = "a much longer text";
        await flushMutations();

        expect(frameQueue).toHaveLength(0);
        runFrames();
        expect(reasonsOfPasses()).toEqual([]);
    });

    it("brings the offers up to date when a box in no chain changes class", async () => {
        const { page, loneBox } = makePage(2, true);
        setupFlowText(page, makeOptions());
        clearSamples();

        // The talking book tool marks a box this way, and the flow refuses such a box.
        loneBox!.classList.add("audio-sentence");
        await flushMutations();
        runFrames();

        expect(reasonsOfPasses()).toEqual(["mutation"]);
    });

    it("marks the page while a pass is owed, and unmarks it afterwards", async () => {
        const { page, boxes } = makePage(2);
        setupFlowText(page, makeOptions());

        boxes[0].querySelector("p")!.textContent = "far too long for one box";
        await flushMutations();

        expect(page.getAttribute("data-flow-reflowing")).toBe("true");
        runFrames();

        // The pass goes on after the frame: whether the text needs a box on a later page is a
        // question for C#, and the page stays marked until the answer has been acted on.
        expect(page.getAttribute("data-flow-reflowing")).toBe("true");
        await waitForBoundaryWork();

        expect(page.hasAttribute("data-flow-reflowing")).toBe(false);
    });

    it("holds the pass until the input method has finished composing", async () => {
        const { page, boxes } = makePage(2);
        setupFlowText(page, makeOptions());
        clearSamples();

        boxes[0].dispatchEvent(
            new CompositionEvent("compositionstart", { bubbles: true }),
        );
        boxes[0].querySelector("p")!.textContent = "far too long for one box";
        await flushMutations();
        runFrames();

        expect(reasonsOfPasses()).toEqual([]);
        expect(page.getAttribute("data-flow-reflowing")).toBe("true");

        boxes[0].dispatchEvent(
            new CompositionEvent("compositionend", { bubbles: true }),
        );
        runFrames();

        expect(reasonsOfPasses()).toEqual(["mutation"]);
    });

    it("does not react to its own mutations", async () => {
        const { page, boxes } = makePage(2);
        setupFlowText(page, makeOptions());
        clearSamples();

        boxes[0].querySelector("p")!.textContent =
            "text that is much too long for this box";
        await flushMutations();
        runFrames();
        await flushMutations();

        expect(frameQueue).toHaveLength(0);
        expect(reasonsOfPasses()).toEqual(["mutation"]);
    });

    it("reflowAllChainsOnPage settles every chain under its own reason", () => {
        const { page, boxes } = makePage(2);
        setupFlowText(page, makeOptions());
        clearSamples();
        boxes[0].querySelector("p")!.textContent =
            "text that is much too long for this box";

        reflowAllChainsOnPage("styleChange");

        expect(reasonsOfPasses()).toEqual(["styleChange"]);
        expect(boxes[1].textContent).not.toBe("short");
    });

    it("suspendFlowText takes the transient marks off the page", async () => {
        const { page, boxes } = makePage(2);
        setupFlowText(page, makeOptions());
        boxes[0].querySelector("p")!.textContent = "far too long for one box";
        await flushMutations();
        runFrames();
        expect(
            page.querySelectorAll(".bloom-flow-hasNext, .bloom-flow-hasPrev")
                .length,
        ).toBe(2);

        suspendFlowText();

        expect(
            page.querySelectorAll(".bloom-flow-hasNext, .bloom-flow-hasPrev")
                .length,
        ).toBe(0);
    });

    it("stops watching the page once it is suspended", async () => {
        const { page, boxes } = makePage(2);
        setupFlowText(page, makeOptions());
        suspendFlowText();
        clearSamples();

        boxes[0].querySelector("p")!.textContent = "far too long for one box";
        await flushMutations();

        expect(frameQueue).toHaveLength(0);
    });

    it("asks for the overflow warning on a box that text has arrived in", () => {
        const { page, boxes } = makePage(2);
        const marked: HTMLElement[] = [];
        setupFlowText(page, {
            ...makeOptions(),
            markOverflow: (editable) => marked.push(editable),
        });
        // Sanity check: opening the page settles the chain without asking for the warning,
        // because OverflowChecker checks every box itself as the page opens.
        expect(marked).toEqual([]);

        requestPassFor([boxes[1]], "continueInto");

        expect(marked).toEqual([boxes[1]]);
    });

    it("settles the boundary again when Bloom says its refit has finished", async () => {
        // C# refuses a move while it is refitting the chain off-screen, and nothing on this
        // page moves the text again on its own: the page would stay as the user emptied it.
        nextBox = { pageId: "page-3", indexInPage: 0, html: "<p>next</p>" };
        nextContentAnswer = { accepted: false, walkInProgress: true };
        const { page } = makePage(2);
        // The boundary is between this page and the next, so the page has to be one C# can name.
        page.id = "page-2";

        setupFlowText(page, {
            ...makeOptions(),
            measurer: kRoomForEverythingMeasurer,
        });
        await settleAllWork();
        // Sanity check: the move was offered once and refused, and the page says so. Room for
        // everything means the next page's word comes back here, so this is a real move.
        expect(setNextContentCount).toBe(1);
        expect(sentContent[0]).not.toContain("next");
        expect(page.getAttribute(kReflowingAttr)).toBe("true");

        nextContentAnswer = { accepted: true };
        socketListener!({ id: "walkFinished" });
        await settleAllWork();

        expect(setNextContentCount).toBe(2);
        // The move was taken, so nothing is waiting for the next refit to end.
        socketListener!({ id: "walkFinished" });
        await settleAllWork();
        expect(setNextContentCount).toBe(2);
    });

    it("leaves a refusal a refit will not mend alone", async () => {
        nextBox = { pageId: "page-3", indexInPage: 0, html: "<p>next</p>" };
        nextContentAnswer = { accepted: false };
        const { page } = makePage(2);
        page.id = "page-2";

        setupFlowText(page, {
            ...makeOptions(),
            measurer: kRoomForEverythingMeasurer,
        });
        await settleAllWork();
        expect(setNextContentCount).toBe(1);

        socketListener!({ id: "walkFinished" });
        await settleAllWork();

        expect(setNextContentCount).toBe(1);
    });

    it("puts what a refit changed in a box of this page into that box", async () => {
        // A refit runs off-screen and saves the pages it changes, but it does not reload the
        // editor, so the box on screen would go on showing text that is not in the book.
        const { page, boxes } = makePage(2);
        page.id = "page-2";
        setupFlowText(page, makeOptions());
        await settleAllWork();
        clearSamples();
        refitResult = [
            {
                chainId: "chain-1",
                lang: "xkal",
                indexInPage: 1,
                html: "<p>fixed</p>",
            },
        ];

        socketListener!({ id: "walkFinished" });
        await settleAllWork();

        expect(boxes[1].innerHTML).toBe("<p>fixed</p>");
        // The box holds text nothing in the browser has measured, so the page settles around it.
        expect(reasonsOfPasses()).toContain("refitResult");
    });

    it("goes to the page a refit names, once what it made for this page is in", async () => {
        // A refit that added pages leaves the run of text ending on one of them, and the author
        // asked for the refit: they are taken to where their text went. The jump saves the page
        // being edited, so what the refit made for a box of this page has to be in it first.
        const { page, boxes } = makePage(2);
        page.id = "page-2";
        setupFlowText(page, makeOptions());
        await settleAllWork();
        refitResult = [
            {
                chainId: "chain-1",
                lang: "xkal",
                indexInPage: 1,
                html: "<p>fixed</p>",
            },
        ];

        socketListener!({
            id: "walkFinished",
            message: JSON.stringify({ pageIdToShow: "page-9" }),
        });
        await settleAllWork();

        expect(boxes[1].innerHTML).toBe("<p>fixed</p>");
        expect(pagesShown).toEqual(["page-9"]);
    });

    it("goes nowhere when a refit names no page", async () => {
        const { page } = makePage(2);
        page.id = "page-2";
        setupFlowText(page, makeOptions());
        await settleAllWork();

        socketListener!({ id: "walkFinished" });
        await settleAllWork();

        expect(pagesShown).toEqual([]);

        // Nor when the page it names is the one already being edited.
        socketListener!({
            id: "walkFinished",
            message: JSON.stringify({ pageIdToShow: "page-2" }),
        });
        await settleAllWork();

        expect(pagesShown).toEqual([]);
    });

    it("asks for the emptied page to go only once what the refit made is in", async () => {
        // A refit that moved this page's text away leaves the page holding nothing, and the page
        // is taken away by the Delete Page route, which saves it. So the box has to hold what the
        // refit made of it before the request goes: otherwise the text the box held before the
        // refit is saved, and moved again into the box that already holds the whole run.
        const { page, boxes } = makePage(2);
        page.id = "page-2";
        setupFlowText(page, makeOptions());
        await settleAllWork();
        const heldBeforeTheRefit = boxes[1].innerHTML;
        refitResult = [
            {
                chainId: "chain-1",
                lang: "xkal",
                indexInPage: 1,
                html: "",
            },
        ];

        socketListener!({
            id: "walkFinished",
            message: JSON.stringify({ pageIdToDelete: "page-2" }),
        });
        await settleAllWork();

        expect(pagesDeleted).toEqual(["page-2"]);
        expect(heldBeforeTheRefit).not.toBe("");
        expect(boxesWhenDeleteAsked[1]).toBe(boxes[1].innerHTML);
        expect(boxesWhenDeleteAsked[1]).not.toBe(heldBeforeTheRefit);
        // A refit that empties the page being edited names no page to show, and none is shown.
        expect(pagesShown).toEqual([]);
    });

    it("leaves a page alone when the refit names some other page to go", async () => {
        const { page } = makePage(2);
        page.id = "page-2";
        setupFlowText(page, makeOptions());
        await settleAllWork();

        socketListener!({
            id: "walkFinished",
            message: JSON.stringify({ pageIdToDelete: "page-7" }),
        });
        await settleAllWork();

        expect(pagesDeleted).toEqual([]);
    });

    it("takes what a refit left for this page as the page opens", async () => {
        // The refit can finish while the page is still loading, and then its word about the box
        // is waiting for the page rather than on its way to it.
        refitResult = [
            {
                chainId: "chain-1",
                lang: "xkal",
                indexInPage: 0,
                html: "<p>fixed</p>",
            },
        ];
        const { page, boxes } = makePage(1);
        page.id = "page-2";

        setupFlowText(page, makeOptions());
        await settleAllWork();

        expect(refitResultAsks).toBe(1);
        expect(boxes[0].innerHTML).toBe("<p>fixed</p>");
    });

    it("asks for what a refit changed here before settling a boundary it refused", async () => {
        // The boundary is settled against what the box holds, so the refit's content goes in
        // first: a box settled against text the refit has replaced would send C# text that is
        // not in the book.
        nextBox = { pageId: "page-3", indexInPage: 0, html: "<p>next</p>" };
        nextContentAnswer = { accepted: false, walkInProgress: true };
        const { page } = makePage(2);
        page.id = "page-2";
        setupFlowText(page, {
            ...makeOptions(),
            measurer: kRoomForEverythingMeasurer,
        });
        await settleAllWork();
        expect(setNextContentCount).toBe(1);
        const asksBefore = refitResultAsks;

        nextContentAnswer = { accepted: true };
        socketListener!({ id: "walkFinished" });
        await settleAllWork();

        expect(refitResultAsks).toBe(asksBefore + 1);
        expect(setNextContentCount).toBe(2);
    });

    it("asks for the overflow warning on the last box of the page even when no text moves", async () => {
        // The box's extra text has already gone to a later page, so the box fits and its page
        // is not overflowing. Nothing measures that unless we ask: the boundary settles without
        // moving anything, and the overflow checker waits for the user's next keystroke.
        const { page, boxes } = makePage(2);
        const marked: HTMLElement[] = [];
        setupFlowText(page, {
            ...makeOptions(),
            markOverflow: (editable) => marked.push(editable),
        });

        await waitForBoundaryWork();

        expect(marked).toEqual([boxes[1]]);
    });
});
