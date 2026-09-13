import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { isBoxLinked, unlinkBox } from "./flowCommands";
import {
    kContinueButtonClass,
    kCreatePagesButtonClass,
    kFlowChainAttr,
} from "./flowConstants";
import { updateContinueButtons } from "./flowContinueButton";
import { updateCreatePagesButtons } from "./flowCreatePagesButton";
import { setFlowTextAvailableForTesting } from "./flowTextAvailable";

/**
 * A page of two boxes that are already one chain, which is what a book made while the
 * collection still had the feature looks like. The first box holds text and the second is
 * empty, so both buttons would have something to consider.
 */
function makeChainedPage(): HTMLElement {
    const page = document.createElement("div");
    page.className = "bloom-page";
    page.id = "page1";
    ["full of text", ""].forEach((text) => {
        const group = document.createElement("div");
        group.className = "bloom-translationGroup";
        group.setAttribute(kFlowChainAttr, "chain1");
        const editable = document.createElement("div");
        editable.className =
            "bloom-editable normal-style bloom-visibility-code-on";
        editable.setAttribute("lang", "en");
        const paragraph = document.createElement("p");
        paragraph.textContent = text;
        editable.appendChild(paragraph);
        group.appendChild(editable);
        page.appendChild(group);
    });
    document.body.appendChild(page);
    return page;
}

function boxes(page: HTMLElement): HTMLElement[] {
    return Array.from(page.querySelectorAll<HTMLElement>(".bloom-editable"));
}

function groupOf(editable: HTMLElement): HTMLElement {
    return editable.closest<HTMLElement>(".bloom-translationGroup")!;
}

describe("a collection that may not use flow text", () => {
    beforeEach(() => {
        document.body.innerHTML = "";
        setFlowTextAvailableForTesting(false);
    });

    afterEach(() => {
        setFlowTextAvailableForTesting(undefined);
    });

    it("offers no continue button", () => {
        const page = makeChainedPage();
        updateContinueButtons(page);
        expect(page.querySelector(`.${kContinueButtonClass}`)).toBe(null);
    });

    it("offers no create-pages button", () => {
        const page = makeChainedPage();
        updateCreatePagesButtons(page);
        expect(page.querySelector(`.${kCreatePagesButtonClass}`)).toBe(null);
    });

    it("does not offer Unlink on a box that is in a chain", () => {
        const page = makeChainedPage();
        // Sanity check: the box really is in a chain, so the answer below comes from the
        // subscription and not from a page that was never chained.
        setFlowTextAvailableForTesting(true);
        expect(isBoxLinked(boxes(page)[0])).toBe(true);

        setFlowTextAvailableForTesting(false);
        expect(isBoxLinked(boxes(page)[0])).toBe(false);
    });

    it("leaves the chain alone when something asks for an unlink anyway", () => {
        const page = makeChainedPage();
        unlinkBox(boxes(page)[0], { unlinkOnOtherPages: () => undefined });
        expect(groupOf(boxes(page)[0]).getAttribute(kFlowChainAttr)).toBe(
            "chain1",
        );
        expect(boxes(page)[0].textContent).toBe("full of text");
    });
});

describe("a collection whose flow text answer has not arrived", () => {
    beforeEach(() => {
        document.body.innerHTML = "";
        setFlowTextAvailableForTesting(undefined);
    });

    it("offers nothing until it has one", () => {
        const page = makeChainedPage();
        updateContinueButtons(page);
        updateCreatePagesButtons(page);
        expect(page.querySelector(`.${kContinueButtonClass}`)).toBe(null);
        expect(page.querySelector(`.${kCreatePagesButtonClass}`)).toBe(null);
        expect(isBoxLinked(boxes(page)[0])).toBe(false);
    });
});
