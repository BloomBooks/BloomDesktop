import { beforeEach, describe, expect, it } from "vitest";
import {
    clearPageOverflowsIfNoBoxOverflows,
    stripTransientFlowMarkup,
    suppressesOverflowMarking,
    updateIndicators,
} from "./flowIndicators";

/** A page of chained groups, each with one visible box of the chain's language. */
function makeChainedPage(boxCount: number, chainId = "chain-1"): HTMLElement[] {
    const page = document.createElement("div");
    page.className = "bloom-page";
    document.body.appendChild(page);

    const boxes: HTMLElement[] = [];
    for (let index = 0; index < boxCount; index++) {
        const group = document.createElement("div");
        group.className = "bloom-translationGroup normal-style";
        group.setAttribute("data-flow-chain", chainId);

        const editable = document.createElement("div");
        editable.className = "bloom-editable bloom-visibility-code-on";
        editable.setAttribute("lang", "xkal");
        editable.innerHTML = `<p>Box ${index + 1}</p>`;
        group.appendChild(editable);

        page.appendChild(group);
        boxes.push(editable);
    }

    return boxes;
}

function groupOf(editable: HTMLElement): HTMLElement {
    return editable.parentElement as HTMLElement;
}

function classesOf(editable: HTMLElement): string[] {
    return Array.from(groupOf(editable).classList).filter((className) =>
        className.startsWith("bloom-flow-"),
    );
}

describe("updateIndicators", () => {
    beforeEach(() => {
        document.body.innerHTML = "";
    });

    it("says which groups carry text on and which carry it from an earlier box", () => {
        const chain = makeChainedPage(3);

        updateIndicators(chain);

        expect(classesOf(chain[0])).toEqual(["bloom-flow-hasNext"]);
        expect(classesOf(chain[1]).sort()).toEqual([
            "bloom-flow-hasNext",
            "bloom-flow-hasPrev",
        ]);
        expect(classesOf(chain[2])).toEqual(["bloom-flow-hasPrev"]);
    });

    it("takes the classes off a group that is no longer in the middle", () => {
        const chain = makeChainedPage(3);
        updateIndicators(chain);

        updateIndicators(chain.slice(0, 2));

        expect(classesOf(chain[1])).toEqual(["bloom-flow-hasPrev"]);
    });
});

describe("the page's own overflow warning", () => {
    beforeEach(() => {
        document.body.innerHTML = "";
    });

    it("comes off when the only overflowing box hands its text to the box after it", () => {
        const chain = makeChainedPage(2);
        const page = chain[0].closest(".bloom-page") as HTMLElement;
        chain[0].classList.add("overflow");
        page.classList.add("pageOverflows");

        updateIndicators(chain);

        expect(chain[0].classList.contains("overflow")).toBe(false);
        expect(page.classList.contains("pageOverflows")).toBe(false);
    });

    it("stays while the last box of the chain on the page overflows", () => {
        const chain = makeChainedPage(2);
        const page = chain[0].closest(".bloom-page") as HTMLElement;
        chain[1].classList.add("overflow");
        page.classList.add("pageOverflows");

        updateIndicators(chain);

        expect(chain[1].classList.contains("overflow")).toBe(true);
        expect(page.classList.contains("pageOverflows")).toBe(true);
    });

    it("stays while a box outside the chain is pushed past its container", () => {
        const chain = makeChainedPage(2);
        const page = chain[0].closest(".bloom-page") as HTMLElement;
        chain[0].classList.add("overflow");
        const other = document.createElement("div");
        other.className = "bloom-editable thisOverflowingParent";
        page.appendChild(other);
        page.classList.add("pageOverflows");

        updateIndicators(chain);

        expect(page.classList.contains("pageOverflows")).toBe(true);
    });

    it("is left alone when there is no page to clear it on", () => {
        // Nothing to assert but that it does not throw: a box can be settled after its page
        // has gone, when the user has switched pages.
        clearPageOverflowsIfNoBoxOverflows(null);
    });
});

describe("suppressesOverflowMarking", () => {
    beforeEach(() => {
        document.body.innerHTML = "";
    });

    it("is true for every box but the last one on the page", () => {
        const chain = makeChainedPage(3);

        expect(suppressesOverflowMarking(chain[0])).toBe(true);
        expect(suppressesOverflowMarking(chain[1])).toBe(true);
        expect(suppressesOverflowMarking(chain[2])).toBe(false);
    });

    it("is false for a box that is in no chain", () => {
        const page = document.createElement("div");
        page.className = "bloom-page";
        const group = document.createElement("div");
        group.className = "bloom-translationGroup normal-style";
        const editable = document.createElement("div");
        editable.className = "bloom-editable bloom-visibility-code-on";
        editable.setAttribute("lang", "xkal");
        group.appendChild(editable);
        page.appendChild(group);
        document.body.appendChild(page);

        expect(suppressesOverflowMarking(editable)).toBe(false);
    });
});

describe("stripTransientFlowMarkup", () => {
    beforeEach(() => {
        document.body.innerHTML = "";
    });

    it("removes every mark the flow code adds while editing", () => {
        const chain = makeChainedPage(2);
        updateIndicators(chain);
        groupOf(chain[1]).classList.add("bloom-flow-refused");
        groupOf(chain[1]).setAttribute("data-flow-refused-reason", "wordFind");
        const page = chain[0].closest(".bloom-page") as HTMLElement;
        page.setAttribute("data-flow-reflowing", "true");

        stripTransientFlowMarkup(document);

        expect(classesOf(chain[0])).toEqual([]);
        expect(classesOf(chain[1])).toEqual([]);
        expect(groupOf(chain[1]).hasAttribute("data-flow-refused-reason")).toBe(
            false,
        );
        expect(page.hasAttribute("data-flow-reflowing")).toBe(false);
    });
});
