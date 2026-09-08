import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { BoxMetrics, LineMeasurer } from "./flowFit";
import { getFlowPassSamples, kMetricsPropertyName } from "./flowTiming";
import {
    FlowTextOptions,
    reflowAllChainsOnPage,
    setupFlowText,
    suspendFlowText,
} from "./flowTrigger";

// jsdom lays nothing out, so the real measurer would have nothing to work from. This one
// fits eight characters in every box, which is enough to make the text move.
const kFakeMeasurer: LineMeasurer = {
    measureFit: (text: string, _metrics: BoxMetrics) =>
        Math.min(text.length, 8),
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
        document.body.innerHTML = "";
        frameQueue = [];
        clearSamples();
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

    it("marks the page while a pass is owed, and unmarks it afterwards", async () => {
        const { page, boxes } = makePage(2);
        setupFlowText(page, makeOptions());

        boxes[0].querySelector("p")!.textContent = "far too long for one box";
        await flushMutations();

        expect(page.getAttribute("data-flow-reflowing")).toBe("true");
        runFrames();

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
});
