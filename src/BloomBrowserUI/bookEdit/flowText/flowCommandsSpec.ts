import { beforeEach, describe, expect, it, vi } from "vitest";
import { isBoxLinked, unlinkBox } from "./flowCommands";
import {
    kContinuationAttr,
    kContinuationAttrValue,
    kFlowChainAttr,
    kHasNextClass,
    kHasPrevClass,
} from "./flowConstants";

/** One box of the page: its text, and the chain id its group carries. */
type BoxSpec = { text: string; chainId?: string; continuation?: boolean };

function makePage(boxes: BoxSpec[]): HTMLElement {
    const page = document.createElement("div");
    page.className = "bloom-page";
    boxes.forEach((spec) => {
        const group = document.createElement("div");
        group.className = "bloom-translationGroup";
        if (spec.chainId) {
            group.setAttribute(kFlowChainAttr, spec.chainId);
        }
        const editable = document.createElement("div");
        editable.className =
            "bloom-editable normal-style bloom-visibility-code-on";
        editable.setAttribute("lang", "en");
        const paragraph = document.createElement("p");
        paragraph.textContent = spec.text;
        if (spec.continuation) {
            paragraph.setAttribute(kContinuationAttr, kContinuationAttrValue);
        }
        editable.appendChild(paragraph);
        group.appendChild(editable);
        page.appendChild(group);
    });
    document.body.appendChild(page);
    return page;
}

function box(page: HTMLElement, index: number): HTMLElement {
    return page.querySelectorAll<HTMLElement>(".bloom-editable")[index];
}

function groupOf(editable: HTMLElement): HTMLElement {
    return editable.closest<HTMLElement>(".bloom-translationGroup")!;
}

function chainIdOf(page: HTMLElement, index: number): string | null {
    return groupOf(box(page, index)).getAttribute(kFlowChainAttr);
}

describe("flowCommands", () => {
    beforeEach(() => {
        document.body.innerHTML = "";
    });

    describe("isBoxLinked", () => {
        it("is true for a box whose group carries a chain id", () => {
            const page = makePage([{ text: "a", chainId: "chain-1" }]);
            expect(isBoxLinked(box(page, 0))).toBe(true);
        });

        it("is false for a box whose group carries none", () => {
            const page = makePage([{ text: "a" }]);
            expect(isBoxLinked(box(page, 0))).toBe(false);
        });

        it("is true when asked about a paragraph inside a linked box", () => {
            const page = makePage([{ text: "a", chainId: "chain-1" }]);
            const paragraph = page.querySelector<HTMLElement>("p")!;
            expect(isBoxLinked(paragraph)).toBe(true);
        });
    });

    describe("unlinkBox", () => {
        it("takes the chain id off this group and every later one", () => {
            const page = makePage([
                { text: "first", chainId: "chain-1" },
                { text: "second", chainId: "chain-1", continuation: true },
                { text: "third", chainId: "chain-1", continuation: true },
                { text: "fourth", chainId: "chain-1", continuation: true },
            ]);

            unlinkBox(box(page, 2));

            expect(chainIdOf(page, 0)).toBe("chain-1");
            expect(chainIdOf(page, 1)).toBe("chain-1");
            expect(chainIdOf(page, 2)).toBeNull();
            expect(chainIdOf(page, 3)).toBeNull();
        });

        it("unlinks the group it would have left alone in the chain", () => {
            const page = makePage([
                { text: "first", chainId: "chain-1" },
                { text: "second", chainId: "chain-1", continuation: true },
            ]);

            unlinkBox(box(page, 1));

            expect(chainIdOf(page, 0)).toBeNull();
            expect(chainIdOf(page, 1)).toBeNull();
        });

        it("leaves the text where it is", () => {
            const page = makePage([
                { text: "first", chainId: "chain-1" },
                { text: "second", chainId: "chain-1", continuation: true },
            ]);

            unlinkBox(box(page, 1));

            expect(box(page, 0).textContent).toBe("first");
            expect(box(page, 1).textContent).toBe("second");
        });

        it("makes the continuation paragraph an ordinary one", () => {
            const page = makePage([
                { text: "first", chainId: "chain-1" },
                { text: "second", chainId: "chain-1", continuation: true },
            ]);

            unlinkBox(box(page, 1));

            expect(
                box(page, 1)
                    .querySelector("p")!
                    .hasAttribute(kContinuationAttr),
            ).toBe(false);
        });

        it("leaves a group of another chain alone", () => {
            const page = makePage([
                { text: "first", chainId: "chain-1" },
                { text: "other", chainId: "chain-2" },
                { text: "second", chainId: "chain-1", continuation: true },
            ]);

            unlinkBox(box(page, 0));

            expect(chainIdOf(page, 1)).toBe("chain-2");
            expect(chainIdOf(page, 0)).toBeNull();
            expect(chainIdOf(page, 2)).toBeNull();
        });

        it("takes the indicator classes off the groups it unlinks", () => {
            const page = makePage([
                { text: "first", chainId: "chain-1" },
                { text: "second", chainId: "chain-1" },
            ]);
            groupOf(box(page, 0)).classList.add(kHasNextClass);
            groupOf(box(page, 1)).classList.add(kHasPrevClass);

            unlinkBox(box(page, 1));

            expect(
                groupOf(box(page, 1)).classList.contains(kHasPrevClass),
            ).toBe(false);
        });

        it("does nothing to a box that is not linked", () => {
            const page = makePage([{ text: "alone" }]);
            const onUnlinked = vi.fn();

            unlinkBox(box(page, 0), { onUnlinked });

            expect(onUnlinked).not.toHaveBeenCalled();
        });

        it("hands the other pages of the chain to the caller's own unlinker", () => {
            const page = makePage([
                { text: "first", chainId: "chain-1" },
                { text: "second", chainId: "chain-1" },
            ]);
            const unlinkOnOtherPages = vi.fn();

            unlinkBox(box(page, 1), { unlinkOnOtherPages });

            expect(unlinkOnOtherPages).toHaveBeenCalledWith(
                "chain-1",
                groupOf(box(page, 1)),
            );
        });

        it("reports every box of the chain on this page, so the caller can re-check them", () => {
            const page = makePage([
                { text: "first", chainId: "chain-1" },
                { text: "second", chainId: "chain-1" },
                { text: "third", chainId: "chain-1" },
            ]);
            const onUnlinked = vi.fn();

            unlinkBox(box(page, 2), { onUnlinked });

            expect(onUnlinked).toHaveBeenCalledTimes(1);
            expect(onUnlinked.mock.calls[0][0]).toEqual([
                box(page, 0),
                box(page, 1),
                box(page, 2),
            ]);
        });
    });
});
