import { describe, expect, it } from "vitest";
import { getLanguageChainOnPage, supportsChainedEditable } from "./flowChain";

type GroupOptions = {
    chainId?: string;
    inCanvas?: boolean;
    visible?: boolean;
    language?: string;
};

function makeGroup(label: string, options: GroupOptions = {}): HTMLElement {
    const group = document.createElement("div");
    group.className = "bloom-translationGroup";
    if (options.chainId) {
        group.setAttribute("data-flow-chain", options.chainId);
    }

    const editable = document.createElement("div");
    editable.className = `bloom-editable ${
        options.visible === false
            ? "bloom-visibility-code-off"
            : "bloom-visibility-code-on"
    }`;
    editable.setAttribute("lang", options.language ?? "xkal");
    editable.setAttribute("contenteditable", "true");
    editable.setAttribute("data-label", label);
    editable.innerHTML = `<p>${label}</p>`;
    group.appendChild(editable);

    // A second box of another language, which must never join the chain.
    const otherLanguage = document.createElement("div");
    otherLanguage.className = "bloom-editable bloom-visibility-code-on";
    otherLanguage.setAttribute("lang", "en");
    otherLanguage.innerHTML = "<p>English</p>";
    group.appendChild(otherLanguage);

    if (options.inCanvas) {
        const canvas = document.createElement("div");
        canvas.className = "bloom-canvas";
        canvas.appendChild(group);
        return canvas;
    }

    return group;
}

function makePage(...groupsOrCanvases: HTMLElement[]): HTMLElement {
    const page = document.createElement("div");
    page.className = "bloom-page";
    groupsOrCanvases.forEach((element) => page.appendChild(element));
    return page;
}

function firstEditable(container: HTMLElement): HTMLElement {
    return container.querySelector(
        ".bloom-editable[data-label]",
    ) as HTMLElement;
}

function labelsOf(chain: HTMLElement[]): string[] {
    return chain.map((editable) => editable.getAttribute("data-label") ?? "");
}

describe("getLanguageChainOnPage", () => {
    it("returns the boxes of both chained groups in document order", () => {
        const first = makeGroup("one", { chainId: "chain-a" });
        const second = makeGroup("two", { chainId: "chain-a" });
        makePage(first, second);

        expect(labelsOf(getLanguageChainOnPage(firstEditable(first)))).toEqual([
            "one",
            "two",
        ]);
    });

    it("leaves out a group whose chain id is different", () => {
        const first = makeGroup("one", { chainId: "chain-a" });
        const other = makeGroup("other", { chainId: "chain-b" });
        const second = makeGroup("two", { chainId: "chain-a" });
        makePage(first, other, second);

        expect(labelsOf(getLanguageChainOnPage(firstEditable(first)))).toEqual([
            "one",
            "two",
        ]);
    });

    it("returns nothing when the trigger's group is not chained", () => {
        const unchained = makeGroup("one");
        const chained = makeGroup("two", { chainId: "chain-a" });
        makePage(unchained, chained);

        expect(getLanguageChainOnPage(firstEditable(unchained))).toEqual([]);
    });

    it("leaves out a chained group that sits inside a bloom-canvas", () => {
        const first = makeGroup("one", { chainId: "chain-a" });
        const canvas = makeGroup("canvas", {
            chainId: "chain-a",
            inCanvas: true,
        });
        const second = makeGroup("two", { chainId: "chain-a" });
        makePage(first, canvas, second);

        expect(labelsOf(getLanguageChainOnPage(firstEditable(first)))).toEqual([
            "one",
            "two",
        ]);
    });

    it("leaves out a box that is not visible in this collection", () => {
        const first = makeGroup("one", { chainId: "chain-a" });
        const hidden = makeGroup("hidden", {
            chainId: "chain-a",
            visible: false,
        });
        const second = makeGroup("two", { chainId: "chain-a" });
        makePage(first, hidden, second);

        expect(labelsOf(getLanguageChainOnPage(firstEditable(first)))).toEqual([
            "one",
            "two",
        ]);
    });

    it("returns nothing when the trigger is not on a page", () => {
        const orphan = makeGroup("one", { chainId: "chain-a" });

        expect(getLanguageChainOnPage(firstEditable(orphan))).toEqual([]);
    });
});

describe("supportsChainedEditable", () => {
    it("accepts a box of paragraphs and CKEditor bookmarks", () => {
        const editable = document.createElement("div");
        editable.innerHTML =
            '<p>One</p><span id="cke_bm_1"></span><p>Two <em>three</em></p>';

        expect(supportsChainedEditable(editable)).toBe(true);
    });

    it("accepts an empty box", () => {
        expect(supportsChainedEditable(document.createElement("div"))).toBe(
            true,
        );
    });

    it("refuses a box that holds something we cannot lay out", () => {
        const editable = document.createElement("div");
        editable.innerHTML = "<p>One <img src=''></p>";

        expect(supportsChainedEditable(editable)).toBe(false);
    });

    it("refuses a box with a top-level element that is not a paragraph", () => {
        const editable = document.createElement("div");
        editable.innerHTML = "<div>One</div>";

        expect(supportsChainedEditable(editable)).toBe(false);
    });
});
