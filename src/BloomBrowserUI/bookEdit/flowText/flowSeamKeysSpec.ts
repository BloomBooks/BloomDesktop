import { beforeEach, describe, expect, it } from "vitest";
import { restoreCollapsedSelectionInEditable } from "./flowCaret";
import {
    kContinuationAttr,
    kContinuationAttrValue,
    kFlowChainAttr,
} from "./flowConstants";
import { handleSeamKey } from "./flowSeamKeys";
import { setFlowTextAvailableForTesting } from "./flowTextAvailable";

/** A page of boxes, each in its own translation group, all in one chain. */
function makeChainedPage(texts: string[], chainId = "chain-1"): HTMLElement[] {
    const page = document.createElement("div");
    page.className = "bloom-page";
    const editables: HTMLElement[] = [];
    texts.forEach((text, index) => {
        const group = document.createElement("div");
        group.className = "bloom-translationGroup";
        group.setAttribute(kFlowChainAttr, chainId);
        const editable = document.createElement("div");
        editable.className =
            "bloom-editable normal-style bloom-visibility-code-on";
        editable.setAttribute("lang", "en");
        editable.setAttribute("contenteditable", "true");
        const paragraph = document.createElement("p");
        paragraph.textContent = text;
        if (index > 0) {
            paragraph.setAttribute(kContinuationAttr, kContinuationAttrValue);
        }
        editable.appendChild(paragraph);
        group.appendChild(editable);
        page.appendChild(group);
        editables.push(editable);
    });
    document.body.appendChild(page);
    // The same wiring setupFlowText uses: one capture-phase listener on the container.
    page.addEventListener("keydown", handleSeamKey, true);
    return editables;
}

function pressKey(
    editable: HTMLElement,
    key: string,
    caretOffset: number,
): KeyboardEvent {
    restoreCollapsedSelectionInEditable(editable, caretOffset);
    const event = new KeyboardEvent("keydown", {
        key,
        bubbles: true,
        cancelable: true,
    });
    editable.dispatchEvent(event);
    return event;
}

describe("flowSeamKeys", () => {
    beforeEach(() => {
        // The collection is allowed to use flow text; nothing here has a Bloom to ask.
        setFlowTextAvailableForTesting(true);
        document.body.innerHTML = "";
    });

    describe("Backspace at the start of a box", () => {
        it("takes the last character of the box before it", () => {
            const [first, second] = makeChainedPage(["one two", "three"]);

            const event = pressKey(second, "Backspace", 0);

            expect(event.defaultPrevented).toBe(true);
            expect(first.textContent).toBe("one tw");
            expect(second.textContent).toBe("three");
        });

        it("does nothing in the first box of the chain", () => {
            const [first, second] = makeChainedPage(["one two", "three"]);

            const event = pressKey(first, "Backspace", 0);

            expect(event.defaultPrevented).toBe(false);
            expect(first.textContent).toBe("one two");
            expect(second.textContent).toBe("three");
        });

        it("does nothing when the caret is not at the start", () => {
            const [first, second] = makeChainedPage(["one two", "three"]);

            const event = pressKey(second, "Backspace", 2);

            expect(event.defaultPrevented).toBe(false);
            expect(first.textContent).toBe("one two");
        });

        it("does nothing in a box that is in no chain", () => {
            const [only] = makeChainedPage(["one two"]);
            only.closest(".bloom-translationGroup")!.removeAttribute(
                kFlowChainAttr,
            );

            const event = pressKey(only, "Backspace", 0);

            expect(event.defaultPrevented).toBe(false);
            expect(only.textContent).toBe("one two");
        });

        it("leaves the box before it alone when that box is empty", () => {
            const [, second] = makeChainedPage(["", "three"]);

            const event = pressKey(second, "Backspace", 0);

            expect(event.defaultPrevented).toBe(false);
            expect(second.textContent).toBe("three");
        });
    });

    describe("Delete at the end of a box", () => {
        it("takes the first character of the box after it", () => {
            const [first, second] = makeChainedPage(["one two", "three"]);

            const event = pressKey(first, "Delete", "one two".length);

            expect(event.defaultPrevented).toBe(true);
            expect(first.textContent).toBe("one two");
            expect(second.textContent).toBe("hree");
        });

        it("does nothing in the last box of the chain", () => {
            const [first, second] = makeChainedPage(["one two", "three"]);

            const event = pressKey(second, "Delete", "three".length);

            expect(event.defaultPrevented).toBe(false);
            expect(first.textContent).toBe("one two");
            expect(second.textContent).toBe("three");
        });

        it("does nothing when the caret is not at the end", () => {
            const [first, second] = makeChainedPage(["one two", "three"]);

            const event = pressKey(first, "Delete", 3);

            expect(event.defaultPrevented).toBe(false);
            expect(second.textContent).toBe("three");
        });
    });

    it("leaves a key it has no rule for alone", () => {
        const [first, second] = makeChainedPage(["one two", "three"]);

        const event = pressKey(second, "ArrowLeft", 0);

        expect(event.defaultPrevented).toBe(false);
        expect(first.textContent).toBe("one two");
    });

    it("leaves Ctrl+Backspace to the browser, which deletes a whole word", () => {
        const [first, second] = makeChainedPage(["one two", "three"]);
        restoreCollapsedSelectionInEditable(second, 0);
        const event = new KeyboardEvent("keydown", {
            key: "Backspace",
            ctrlKey: true,
            bubbles: true,
            cancelable: true,
        });

        second.dispatchEvent(event);

        expect(event.defaultPrevented).toBe(false);
        expect(first.textContent).toBe("one two");
    });
});
