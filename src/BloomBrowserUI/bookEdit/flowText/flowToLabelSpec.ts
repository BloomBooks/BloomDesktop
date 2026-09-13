import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// What C# would say about the box after the page being edited, and what it was asked.
let nextBox:
    | { pageId: string; pageNumber: string; indexInPage: number; html: string }
    | undefined;
const peekNextCalls: Array<{
    chainId: string;
    afterPageId: string;
    lang: string;
}> = [];

vi.mock("./flowBoundaryClient", () => ({
    peekNext: (chainId: string, afterPageId: string, lang: string) => {
        peekNextCalls.push({ chainId, afterPageId, lang });
        return Promise.resolve(nextBox);
    },
}));

import {
    getFlowToLabelText,
    removeFlowToLabels,
    resetFlowToCache,
    updateFlowToLabels,
} from "./flowToLabel";
import {
    kFlowChainAttr,
    kFlowToNextPageEnglish,
    kFlowToPageEnglish,
    kFlowToTestId,
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
        page.querySelectorAll<HTMLElement>(`[data-testid="${kFlowToTestId}"]`),
    );
}

/** Let the round trip to C# and the update it schedules finish. */
async function flushAnswers(): Promise<void> {
    for (let tick = 0; tick < 5; tick++) {
        await Promise.resolve();
    }
}

describe("flowToLabel", () => {
    beforeEach(() => {
        document.body.innerHTML = "";
        resetFlowToCache();
        nextBox = undefined;
        peekNextCalls.length = 0;
    });

    afterEach(() => {
        document.body.innerHTML = "";
    });

    it("labels the last box of a chain whose text goes on to a later page", async () => {
        nextBox = {
            pageId: "page-3",
            pageNumber: "6",
            indexInPage: 0,
            html: "<p>tail</p>",
        };
        const page = makePage([{ text: { en: "long text" }, chainId: "c1" }]);

        // Sanity check: the first call knows nothing yet, so it puts up no label.
        updateFlowToLabels(page);
        expect(labelsOn(page).length).toBe(0);

        await flushAnswers();

        const labels = labelsOn(page);
        expect(labels.length).toBe(1);
        expect(labels[0].textContent).toBe(
            kFlowToPageEnglish.replace("{0}", "6"),
        );
        expect(getFlowToLabelText(box(page, 0))).toBe(
            kFlowToPageEnglish.replace("{0}", "6"),
        );
        expect(peekNextCalls).toEqual([
            { chainId: "c1", afterPageId: "page-2", lang: "en" },
        ]);
    });

    it("names no page when the later page has no number", async () => {
        nextBox = {
            pageId: "page-3",
            pageNumber: "",
            indexInPage: 0,
            html: "<p>tail</p>",
        };
        const page = makePage([{ text: { en: "long text" }, chainId: "c1" }]);

        updateFlowToLabels(page);
        await flushAnswers();

        expect(labelsOn(page)[0].textContent).toBe(kFlowToNextPageEnglish);
    });

    it("puts the label below the box, clear of its text", async () => {
        nextBox = {
            pageId: "page-3",
            pageNumber: "6",
            indexInPage: 0,
            html: "<p>tail</p>",
        };
        const page = makePage([{ text: { en: "long text" }, chainId: "c1" }]);

        updateFlowToLabels(page);
        await flushAnswers();

        // jsdom lays nothing out, so every offset is zero; what the placement can be held to
        // here is that the label's top is past the box's own bottom.
        const editable = box(page, 0);
        const top = parseFloat(labelsOn(page)[0].style.top);
        expect(top).toBeGreaterThan(editable.offsetTop + editable.offsetHeight);
    });

    it("labels nothing when C# says the chain ends on this page", async () => {
        nextBox = undefined;
        const page = makePage([{ text: { en: "own text" }, chainId: "c1" }]);

        updateFlowToLabels(page);
        await flushAnswers();

        expect(labelsOn(page).length).toBe(0);
        expect(getFlowToLabelText(box(page, 0))).toBeUndefined();
    });

    it("labels nothing on a box that is not in a chain", async () => {
        nextBox = {
            pageId: "page-3",
            pageNumber: "6",
            indexInPage: 0,
            html: "<p>tail</p>",
        };
        const page = makePage([{ text: { en: "own text" } }]);

        updateFlowToLabels(page);
        await flushAnswers();

        expect(labelsOn(page).length).toBe(0);
        expect(peekNextCalls.length).toBe(0);
    });

    it("labels only the last box of the chain on the page", async () => {
        nextBox = {
            pageId: "page-3",
            pageNumber: "6",
            indexInPage: 0,
            html: "<p>tail</p>",
        };
        const page = makePage([
            { text: { en: "first here" }, chainId: "c1" },
            { text: { en: "and on" }, chainId: "c1" },
        ]);

        updateFlowToLabels(page);
        await flushAnswers();

        const labels = labelsOn(page);
        expect(labels.length).toBe(1);
        expect(getFlowToLabelText(box(page, 0))).toBeUndefined();
        expect(getFlowToLabelText(box(page, 1))).toBe(
            kFlowToPageEnglish.replace("{0}", "6"),
        );
    });

    it("asks C# once per page, chain and language", async () => {
        nextBox = {
            pageId: "page-3",
            pageNumber: "6",
            indexInPage: 0,
            html: "<p>tail</p>",
        };
        const page = makePage([{ text: { en: "long text" }, chainId: "c1" }]);

        updateFlowToLabels(page);
        await flushAnswers();
        updateFlowToLabels(page);
        await flushAnswers();

        expect(peekNextCalls.length).toBe(1);

        // Forgetting the answer is what makes the next pass ask again.
        resetFlowToCache();
        updateFlowToLabels(page);
        await flushAnswers();
        expect(peekNextCalls.length).toBe(2);
    });

    it("gives each language of a group its own label", async () => {
        nextBox = {
            pageId: "page-3",
            pageNumber: "6",
            indexInPage: 0,
            html: "<p>tail</p>",
        };
        const page = makePage([
            { text: { en: "long text", fr: "texte" }, chainId: "c1" },
        ]);

        updateFlowToLabels(page);
        await flushAnswers();

        expect(labelsOn(page).length).toBe(2);
        expect(peekNextCalls.map((call) => call.lang)).toEqual(["en", "fr"]);
        expect(getFlowToLabelText(box(page, 0, "fr"))).toBe(
            kFlowToPageEnglish.replace("{0}", "6"),
        );
    });

    it("takes the label off a box that leaves the chain", async () => {
        nextBox = {
            pageId: "page-3",
            pageNumber: "6",
            indexInPage: 0,
            html: "<p>tail</p>",
        };
        const page = makePage([{ text: { en: "long text" }, chainId: "c1" }]);

        updateFlowToLabels(page);
        await flushAnswers();
        expect(labelsOn(page).length).toBe(1);

        box(page, 0)
            .closest(".bloom-translationGroup")!
            .removeAttribute(kFlowChainAttr);
        updateFlowToLabels(page);

        expect(labelsOn(page).length).toBe(0);
    });

    it("removeFlowToLabels takes every label out", async () => {
        nextBox = {
            pageId: "page-3",
            pageNumber: "6",
            indexInPage: 0,
            html: "<p>tail</p>",
        };
        const page = makePage([{ text: { en: "long text" }, chainId: "c1" }]);

        updateFlowToLabels(page);
        await flushAnswers();
        expect(labelsOn(page).length).toBe(1);

        removeFlowToLabels(document);

        expect(labelsOn(page).length).toBe(0);
    });
});
