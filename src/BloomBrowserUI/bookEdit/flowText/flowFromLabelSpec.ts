import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// What C# would say about the box before the page being edited, and what it was asked.
let previousBox: { pageId: string; pageNumber: string } | undefined;
const peekPreviousCalls: Array<{ chainId: string; beforePageId: string }> = [];

vi.mock("./flowBoundaryClient", () => ({
    peekPrevious: (chainId: string, beforePageId: string) => {
        peekPreviousCalls.push({ chainId, beforePageId });
        return Promise.resolve(previousBox);
    },
}));

import {
    getFlowFromLabelText,
    removeFlowFromLabels,
    resetFlowFromCache,
    updateFlowFromLabels,
} from "./flowFromLabel";
import {
    kFlowChainAttr,
    kFlowFromPageEnglish,
    kFlowFromPreviousPageEnglish,
    kFlowFromTestId,
} from "./flowConstants";

type BoxSpec = {
    /** The text of each language's editable, by language tag. */
    text: Record<string, string>;
    /** The chain id the group carries, if it is linked. */
    chainId?: string;
};

function makePage(boxes: BoxSpec[], pageId = "page-2"): HTMLElement {
    const page = document.createElement("div");
    page.className = "bloom-page";
    page.id = pageId;
    const marginBox = document.createElement("div");
    marginBox.className = "marginBox";
    page.appendChild(marginBox);

    boxes.forEach((box) => {
        const group = document.createElement("div");
        group.className = "bloom-translationGroup";
        if (box.chainId) {
            group.setAttribute(kFlowChainAttr, box.chainId);
        }
        Object.entries(box.text).forEach(([language, text]) => {
            const editable = document.createElement("div");
            editable.className =
                "bloom-editable normal-style bloom-visibility-code-on";
            editable.setAttribute("lang", language);
            const paragraph = document.createElement("p");
            paragraph.textContent = text;
            editable.appendChild(paragraph);
            group.appendChild(editable);
        });

        marginBox.appendChild(group);
    });

    document.body.appendChild(page);
    return page;
}

/** The visible editable of one language in the box at `index`. */
function box(page: HTMLElement, index: number, language = "en"): HTMLElement {
    return page.querySelectorAll<HTMLElement>(
        `.bloom-translationGroup > .bloom-editable[lang="${language}"]`,
    )[index];
}

function labelsOn(page: HTMLElement): HTMLElement[] {
    return Array.from(
        page.querySelectorAll<HTMLElement>(
            `[data-testid="${kFlowFromTestId}"]`,
        ),
    );
}

/** Let the round trip to C# and the update it schedules finish. */
async function flushAnswers(): Promise<void> {
    for (let tick = 0; tick < 5; tick++) {
        await Promise.resolve();
    }
}

describe("flowFromLabel", () => {
    beforeEach(() => {
        document.body.innerHTML = "";
        resetFlowFromCache();
        previousBox = undefined;
        peekPreviousCalls.length = 0;
    });

    afterEach(() => {
        document.body.innerHTML = "";
    });

    it("labels the first box of a chain whose text comes from an earlier page", async () => {
        previousBox = { pageId: "page-1", pageNumber: "3" };
        const page = makePage([
            { text: { en: "arrived text" }, chainId: "c1" },
        ]);

        // Sanity check: the first call knows nothing yet, so it puts up no label.
        updateFlowFromLabels(page);
        expect(labelsOn(page).length).toBe(0);

        await flushAnswers();

        const labels = labelsOn(page);
        expect(labels.length).toBe(1);
        expect(labels[0].textContent).toBe(
            kFlowFromPageEnglish.replace("{0}", "3"),
        );
        expect(getFlowFromLabelText(box(page, 0))).toBe(
            kFlowFromPageEnglish.replace("{0}", "3"),
        );
        expect(peekPreviousCalls).toEqual([
            { chainId: "c1", beforePageId: "page-2" },
        ]);
    });

    it("names no page when the earlier page has no number", async () => {
        previousBox = { pageId: "page-1", pageNumber: "" };
        const page = makePage([
            { text: { en: "arrived text" }, chainId: "c1" },
        ]);

        updateFlowFromLabels(page);
        await flushAnswers();

        expect(labelsOn(page)[0].textContent).toBe(
            kFlowFromPreviousPageEnglish,
        );
    });

    it("puts the label above the box, clear of its text", async () => {
        previousBox = { pageId: "page-1", pageNumber: "3" };
        const page = makePage([
            { text: { en: "arrived text" }, chainId: "c1" },
        ]);

        updateFlowFromLabels(page);
        await flushAnswers();

        // jsdom lays nothing out, so every offset is zero; what the placement can be held to
        // here is that the label's top is above the box's own top.
        expect(parseFloat(labelsOn(page)[0].style.top)).toBeLessThan(
            box(page, 0).offsetTop,
        );
    });

    it("labels nothing when C# says the chain starts on this page", async () => {
        previousBox = undefined;
        const page = makePage([{ text: { en: "own text" }, chainId: "c1" }]);

        updateFlowFromLabels(page);
        await flushAnswers();

        expect(labelsOn(page).length).toBe(0);
        expect(getFlowFromLabelText(box(page, 0))).toBeUndefined();
    });

    it("labels nothing on a box that is not in a chain", async () => {
        previousBox = { pageId: "page-1", pageNumber: "3" };
        const page = makePage([{ text: { en: "own text" } }]);

        updateFlowFromLabels(page);
        await flushAnswers();

        expect(labelsOn(page).length).toBe(0);
        expect(peekPreviousCalls.length).toBe(0);
    });

    it("labels only the first box of the chain on the page", async () => {
        previousBox = { pageId: "page-1", pageNumber: "3" };
        const page = makePage([
            { text: { en: "first here" }, chainId: "c1" },
            { text: { en: "and on" }, chainId: "c1" },
        ]);

        updateFlowFromLabels(page);
        await flushAnswers();

        const labels = labelsOn(page);
        expect(labels.length).toBe(1);
        expect(getFlowFromLabelText(box(page, 0))).toBe(
            kFlowFromPageEnglish.replace("{0}", "3"),
        );
        expect(getFlowFromLabelText(box(page, 1))).toBeUndefined();
    });

    it("asks C# once per page, chain and language", async () => {
        previousBox = { pageId: "page-1", pageNumber: "3" };
        const page = makePage([
            { text: { en: "arrived text" }, chainId: "c1" },
        ]);

        updateFlowFromLabels(page);
        await flushAnswers();
        updateFlowFromLabels(page);
        await flushAnswers();

        expect(peekPreviousCalls.length).toBe(1);

        // Forgetting the answer is what makes the next pass ask again.
        resetFlowFromCache();
        updateFlowFromLabels(page);
        await flushAnswers();
        expect(peekPreviousCalls.length).toBe(2);
    });

    it("gives each language of a group its own label", async () => {
        previousBox = { pageId: "page-1", pageNumber: "3" };
        const page = makePage([
            { text: { en: "arrived text", fr: "texte" }, chainId: "c1" },
        ]);

        updateFlowFromLabels(page);
        await flushAnswers();

        expect(labelsOn(page).length).toBe(2);
        expect(peekPreviousCalls.map((call) => call.chainId)).toEqual([
            "c1",
            "c1",
        ]);
        expect(getFlowFromLabelText(box(page, 0, "fr"))).toBe(
            kFlowFromPageEnglish.replace("{0}", "3"),
        );
    });

    it("takes the label off a box that leaves the chain", async () => {
        previousBox = { pageId: "page-1", pageNumber: "3" };
        const page = makePage([
            { text: { en: "arrived text" }, chainId: "c1" },
        ]);

        updateFlowFromLabels(page);
        await flushAnswers();
        expect(labelsOn(page).length).toBe(1);

        box(page, 0)
            .closest(".bloom-translationGroup")!
            .removeAttribute(kFlowChainAttr);
        updateFlowFromLabels(page);

        expect(labelsOn(page).length).toBe(0);
    });

    it("removeFlowFromLabels takes every label out", async () => {
        previousBox = { pageId: "page-1", pageNumber: "3" };
        const page = makePage([
            { text: { en: "arrived text" }, chainId: "c1" },
        ]);

        updateFlowFromLabels(page);
        await flushAnswers();
        expect(labelsOn(page).length).toBe(1);

        removeFlowFromLabels(document);

        expect(labelsOn(page).length).toBe(0);
    });
});
