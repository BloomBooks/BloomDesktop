import { beforeEach, describe, expect, it } from "vitest";
import { BoxMetrics, LineMeasurer } from "./flowFit";
import { rebalanceChain } from "./flowEngine";

/** A measurer that fits a fixed number of characters in a box, whatever the box is. */
function measurerThatFits(characterCount: number): LineMeasurer {
    return {
        measureFit: (text: string, _metrics: BoxMetrics) =>
            Math.min(text.length, characterCount),
    };
}

function makeChainedPage(
    boxContents: string[],
    chainId = "chain-1",
): HTMLElement[] {
    const page = document.createElement("div");
    page.className = "bloom-page";
    document.body.appendChild(page);

    return boxContents.map((content) => {
        const group = document.createElement("div");
        group.className = "bloom-translationGroup normal-style";
        group.setAttribute("data-flow-chain", chainId);

        const editable = document.createElement("div");
        editable.className = "bloom-editable bloom-visibility-code-on";
        editable.setAttribute("lang", "xkal");
        editable.setAttribute("contenteditable", "true");
        editable.innerHTML = content;
        group.appendChild(editable);

        page.appendChild(group);
        return editable;
    });
}

function textOf(editable: HTMLElement): string {
    return Array.from(editable.querySelectorAll("p"))
        .map((paragraph) => (paragraph.textContent ?? "").trim())
        .filter(Boolean)
        .join(" | ");
}

describe("rebalanceChain", () => {
    beforeEach(() => {
        document.body.innerHTML = "";
    });

    it("pushes what does not fit through box 1 into box 2 and on into box 3", () => {
        const chain = makeChainedPage([
            "<p>one two three four five six</p>",
            "<p><br></p>",
            "<p><br></p>",
        ]);

        const changed = rebalanceChain(chain[0], measurerThatFits(7));

        expect(changed).toBe(true);
        expect(textOf(chain[0])).toBe("one two");
        expect(textOf(chain[1])).toBe("three");
        expect(textOf(chain[2])).toBe("four five six");
    });

    it("pulls the text back when the boxes grow", () => {
        const chain = makeChainedPage([
            "<p>one two three four five six</p>",
            "<p><br></p>",
            "<p><br></p>",
        ]);
        rebalanceChain(chain[0], measurerThatFits(7));

        const changed = rebalanceChain(chain[1], measurerThatFits(100));

        expect(changed).toBe(true);
        expect(textOf(chain[0])).toBe("one two three four five six");
        expect(textOf(chain[1])).toBe("");
        expect(textOf(chain[2])).toBe("");
    });

    it("settles: a second pass with the same measurer changes nothing", () => {
        const chain = makeChainedPage([
            "<p>one two three four five six</p>",
            "<p><br></p>",
            "<p><br></p>",
        ]);
        rebalanceChain(chain[0], measurerThatFits(7));

        expect(rebalanceChain(chain[0], measurerThatFits(7))).toBe(false);
    });

    it("keeps a paragraph break when the text moves on", () => {
        const chain = makeChainedPage([
            "<p>one two</p><p>three four</p>",
            "<p><br></p>",
        ]);

        rebalanceChain(chain[0], measurerThatFits(8));

        expect(textOf(chain[0])).toBe("one two");
        expect(textOf(chain[1])).toBe("three four");
    });

    it("never measures the last box on the page", () => {
        const chain = makeChainedPage([
            "<p>one two three four five six</p>",
            "<p><br></p>",
        ]);
        const measuredBoxes: number[] = [];
        const measurer: LineMeasurer = {
            measureFit: (text: string, metrics: BoxMetrics) => {
                measuredBoxes.push(metrics.width);
                return Math.min(text.length, 7);
            },
        };
        // jsdom lays nothing out, so give the two boxes widths we can tell apart.
        Object.defineProperty(chain[0], "clientWidth", { value: 111 });
        Object.defineProperty(chain[1], "clientWidth", { value: 222 });

        rebalanceChain(chain[0], measurer);

        // Every measurement asked about box 1's box, never box 2's.
        expect(measuredBoxes).toContain(111);
        expect(measuredBoxes).not.toContain(222);
    });

    it("leaves the marker in the last box and takes it out of the box before it", () => {
        const marker = `<span class="bloom-overflowStart">${String.fromCharCode(
            0x200c,
        )}</span>`;
        const chain = makeChainedPage([
            `<p>one two ${marker}three four five six</p>`,
            `<p>seven ${marker}eight</p>`,
        ]);

        rebalanceChain(chain[0], measurerThatFits(100));

        expect(
            chain[0].querySelectorAll("span.bloom-overflowStart"),
        ).toHaveLength(0);
        expect(
            chain[1].querySelectorAll("span.bloom-overflowStart").length,
        ).toBeLessThanOrEqual(1);
    });

    it("does nothing when the trigger's group is not chained", () => {
        const chain = makeChainedPage(["<p>one two three</p>"], "");
        chain[0]
            .closest(".bloom-translationGroup")
            ?.removeAttribute("data-flow-chain");

        expect(rebalanceChain(chain[0], measurerThatFits(2))).toBe(false);
        expect(textOf(chain[0])).toBe("one two three");
    });
});
