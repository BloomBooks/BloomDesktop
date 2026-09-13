import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// What C# would say about the pages the browser cannot see, and what it was asked to do.
let pendingOverflow:
    | {
          pageId: string;
          pageNumber: string;
          indexInPage: number;
          previewText: string;
      }
    | undefined;
let continueIntoResult:
    | { chainId: string; targetHtmlByLang: Record<string, string> }
    | undefined;
const continueIntoCalls: Record<string, unknown>[] = [];

vi.mock("./flowBoundaryClient", () => ({
    getPendingOverflow: () => Promise.resolve(pendingOverflow),
    continueInto: (args: Record<string, unknown>) => {
        continueIntoCalls.push(args);
        return Promise.resolve(continueIntoResult);
    },
}));

import {
    canOfferContinue,
    findContinueSourceOnPage,
    getContinueButtonLabel,
    hasContinueButton,
    joinToSourceBox,
    removeContinueButtons,
    resetPendingOverflowCache,
    updateContinueButtons,
} from "./flowContinueButton";
import {
    kContinueButtonEnglish,
    kContinueButtonTestId,
    kFlowChainAttr,
    kOverflowStartClass,
} from "./flowConstants";
import { setFlowTextAvailableForTesting } from "./flowTextAvailable";

/**
 * A page of origami split panes holding one translation group per box. `boxes` says, per box,
 * what each language's editable holds: the text, and whether it carries the marker that says
 * its text stops fitting.
 */
type BoxSpec = {
    /** The text of each language's editable, by language tag. */
    text: Record<string, string>;
    /** The languages whose editable carries the overflow marker. */
    overflowing?: string[];
    /** The chain id the group carries, if it is already linked. */
    chainId?: string;
};

function makePage(boxes: BoxSpec[], pageClasses = ""): HTMLElement {
    const page = document.createElement("div");
    page.className = `bloom-page ${pageClasses}`.trim();
    const marginBox = document.createElement("div");
    marginBox.className = "marginBox";
    page.appendChild(marginBox);

    boxes.forEach((box) => {
        const pane = document.createElement("div");
        pane.className = "split-pane-component";
        const inner = document.createElement("div");
        inner.className = "split-pane-component-inner";
        pane.appendChild(inner);

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
            editable.setAttribute("contenteditable", "true");
            const paragraph = document.createElement("p");
            paragraph.textContent = text;
            if (box.overflowing?.includes(language)) {
                const marker = document.createElement("span");
                marker.className = kOverflowStartClass;
                marker.textContent = "‌";
                paragraph.appendChild(marker);
            }
            editable.appendChild(paragraph);
            group.appendChild(editable);
        });

        inner.appendChild(group);
        marginBox.appendChild(pane);
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

function groupOf(editable: HTMLElement): HTMLElement {
    return editable.closest<HTMLElement>(".bloom-translationGroup")!;
}

function buttonsOn(page: HTMLElement): HTMLElement[] {
    return Array.from(
        page.querySelectorAll<HTMLElement>(
            `[data-testid="${kContinueButtonTestId}"]`,
        ),
    );
}

/** Let the round trip to C# and the update it schedules finish. */
async function flushAnswers(): Promise<void> {
    for (let tick = 0; tick < 5; tick++) {
        await Promise.resolve();
    }
}

describe("flowContinueButton", () => {
    beforeEach(() => {
        // The collection is allowed to use flow text; nothing here has a Bloom to ask.
        setFlowTextAvailableForTesting(true);
        document.body.innerHTML = "";
        resetPendingOverflowCache();
        pendingOverflow = undefined;
        continueIntoResult = undefined;
        continueIntoCalls.length = 0;
    });

    afterEach(() => {
        document.body.innerHTML = "";
    });

    describe("updateContinueButtons", () => {
        it("offers the empty box below an overflowing box", () => {
            const page = makePage([
                { text: { en: "long text" }, overflowing: ["en"] },
                { text: { en: "" } },
            ]);

            updateContinueButtons(page);

            const buttons = buttonsOn(page);
            expect(buttons.length).toBe(1);
            expect(buttons[0].textContent).toBe(kContinueButtonEnglish);
            expect(groupOf(box(page, 1))).toBe(buttons[0].parentElement);
            expect(hasContinueButton(box(page, 1))).toBe(true);
        });

        it("offers nothing when no earlier box overflows", () => {
            const page = makePage([
                { text: { en: "short" } },
                { text: { en: "" } },
            ]);

            updateContinueButtons(page);

            expect(buttonsOn(page).length).toBe(0);
        });

        it("offers nothing to a box that has text of its own", () => {
            const page = makePage([
                { text: { en: "long text" }, overflowing: ["en"] },
                { text: { en: "already something" } },
            ]);

            updateContinueButtons(page);

            expect(buttonsOn(page).length).toBe(0);
        });

        it("offers nothing above the overflowing box", () => {
            const page = makePage([
                { text: { en: "" } },
                { text: { en: "long text" }, overflowing: ["en"] },
            ]);

            updateContinueButtons(page);

            expect(buttonsOn(page).length).toBe(0);
        });

        it("takes the button off again once the box above fits", () => {
            const page = makePage([
                { text: { en: "long text" }, overflowing: ["en"] },
                { text: { en: "" } },
            ]);
            updateContinueButtons(page);
            expect(buttonsOn(page).length).toBe(1);

            page.querySelector(`.${kOverflowStartClass}`)!.remove();
            updateContinueButtons(page);

            expect(buttonsOn(page).length).toBe(0);
        });

        it("puts up one button, not one per pass", () => {
            const page = makePage([
                { text: { en: "long text" }, overflowing: ["en"] },
                { text: { en: "" } },
            ]);

            updateContinueButtons(page);
            updateContinueButtons(page);
            updateContinueButtons(page);

            expect(buttonsOn(page).length).toBe(1);
        });

        it("offers each language of a bilingual page its own button", () => {
            const page = makePage([
                {
                    text: { en: "long English", fr: "long French" },
                    overflowing: ["en", "fr"],
                },
                { text: { en: "", fr: "" } },
            ]);

            updateContinueButtons(page);

            expect(buttonsOn(page).length).toBe(2);
            expect(hasContinueButton(box(page, 1, "en"))).toBe(true);
            expect(hasContinueButton(box(page, 1, "fr"))).toBe(true);
        });

        it("offers only the language whose box overflows", () => {
            const page = makePage([
                {
                    text: { en: "long English", fr: "short" },
                    overflowing: ["en"],
                },
                { text: { en: "", fr: "" } },
            ]);

            updateContinueButtons(page);

            expect(hasContinueButton(box(page, 1, "en"))).toBe(true);
            expect(hasContinueButton(box(page, 1, "fr"))).toBe(false);
        });

        it("offers nothing on a front matter page", () => {
            const page = makePage(
                [
                    { text: { en: "long text" }, overflowing: ["en"] },
                    { text: { en: "" } },
                ],
                "bloom-frontMatter",
            );

            updateContinueButtons(page);

            expect(buttonsOn(page).length).toBe(0);
        });

        it("offers nothing to a box that is already the continuation of one on this page", () => {
            const page = makePage([
                {
                    text: { en: "long text" },
                    overflowing: ["en"],
                    chainId: "chain-1",
                },
                { text: { en: "" }, chainId: "chain-1" },
            ]);

            updateContinueButtons(page);

            expect(buttonsOn(page).length).toBe(0);
        });

        it("offers the nearest earlier overflowing box, not the first", () => {
            const page = makePage([
                { text: { en: "first long" }, overflowing: ["en"] },
                { text: { en: "second long" }, overflowing: ["en"] },
                { text: { en: "" } },
            ]);

            expect(findContinueSourceOnPage(box(page, 2))).toBe(box(page, 1));
        });
    });

    describe("canOfferContinue", () => {
        it("refuses a box that is not normal-style", () => {
            const page = makePage([
                { text: { en: "long text" }, overflowing: ["en"] },
                { text: { en: "" } },
            ]);
            const target = box(page, 1);
            target.classList.remove("normal-style");

            expect(canOfferContinue(target)).toBe(false);
        });

        it("refuses a box whose overflowing box above it has a recording", () => {
            const page = makePage([
                { text: { en: "long text" }, overflowing: ["en"] },
                { text: { en: "" } },
            ]);
            // Moving the text of a recorded box would leave the recording pointing at words
            // that are no longer there, so the flow refuses such a box, and the offer to
            // continue it would do nothing.
            box(page, 0).setAttribute("data-audiorecordingmode", "TextBox");
            box(page, 0).classList.add("audio-sentence");
            box(page, 0).setAttribute("id", "recorded-box");

            expect(findContinueSourceOnPage(box(page, 1))).toBeUndefined();
        });

        it("refuses a box that is not part of the page's own layout", () => {
            const page = makePage([
                { text: { en: "long text" }, overflowing: ["en"] },
                { text: { en: "" } },
            ]);
            // Everything the page lays out is inside its marginBox. A box outside one is
            // something laid over the page, and its size is not the page's business.
            page.querySelectorAll(".marginBox").forEach((marginBox) =>
                marginBox.classList.remove("marginBox"),
            );

            expect(canOfferContinue(box(page, 1))).toBe(false);
        });

        it("accepts an empty box whose only content is the overflow marker", () => {
            const page = makePage([
                { text: { en: "long text" }, overflowing: ["en"] },
                { text: { en: "" }, overflowing: ["en"] },
            ]);

            expect(canOfferContinue(box(page, 1))).toBe(true);
        });
    });

    describe("clicking the button", () => {
        it("puts one new chain id on both groups and takes the button away", () => {
            const page = makePage([
                { text: { en: "long text" }, overflowing: ["en"] },
                { text: { en: "" } },
            ]);
            updateContinueButtons(page);

            buttonsOn(page)[0].click();

            const sourceChain = groupOf(box(page, 0)).getAttribute(
                kFlowChainAttr,
            );
            expect(sourceChain).toBeTruthy();
            expect(groupOf(box(page, 1)).getAttribute(kFlowChainAttr)).toBe(
                sourceChain,
            );
            expect(buttonsOn(page).length).toBe(0);
        });

        it("keeps the chain id the source already has", () => {
            const page = makePage([
                { text: { en: "first" }, chainId: "chain-7" },
                {
                    text: { en: "long text" },
                    overflowing: ["en"],
                    chainId: "chain-7",
                },
                { text: { en: "" } },
            ]);

            joinToSourceBox(box(page, 1), box(page, 2));

            expect(groupOf(box(page, 2)).getAttribute(kFlowChainAttr)).toBe(
                "chain-7",
            );
        });

        it("links only the language whose button was clicked", () => {
            const page = makePage([
                {
                    text: { en: "long English", fr: "short" },
                    overflowing: ["en"],
                },
                { text: { en: "", fr: "" } },
            ]);
            updateContinueButtons(page);

            buttonsOn(page)[0].click();

            // The chain is a property of the group, so both languages' boxes are in it. What
            // differs per language is which boxes hold text, and that the pass sorts out.
            expect(
                groupOf(box(page, 1, "fr")).getAttribute(kFlowChainAttr),
            ).toBeTruthy();
        });
    });

    describe("a source on an earlier page", () => {
        function makeEmptyPage(): HTMLElement {
            const page = makePage([{ text: { en: "" } }]);
            page.id = "page-4";
            return page;
        }

        it("offers the page C# names, and says which page it is", async () => {
            pendingOverflow = {
                pageId: "page-2",
                pageNumber: "3",
                indexInPage: 1,
                previewText: "and the rest of it",
            };
            const page = makeEmptyPage();

            // The first call has no answer to work from; it asks, and puts the button up when
            // the answer arrives.
            updateContinueButtons(page);
            expect(buttonsOn(page).length).toBe(0);
            await flushAnswers();

            expect(buttonsOn(page).length).toBe(1);
            expect(getContinueButtonLabel(box(page, 0))).toBe(
                "flow text here from page 3",
            );
        });

        it("offers nothing when C# says no earlier page has text to spare", async () => {
            const page = makeEmptyPage();

            updateContinueButtons(page);
            await flushAnswers();

            expect(buttonsOn(page).length).toBe(0);
        });

        it("brings the text across and links the group when it is clicked", async () => {
            pendingOverflow = {
                pageId: "page-2",
                pageNumber: "3",
                indexInPage: 1,
                previewText: "and the rest of it",
            };
            continueIntoResult = {
                chainId: "chain-9",
                targetHtmlByLang: { en: "<p>and the rest of it</p>" },
            };
            const page = makeEmptyPage();
            updateContinueButtons(page);
            await flushAnswers();

            buttonsOn(page)[0].click();
            await flushAnswers();

            expect(continueIntoCalls).toEqual([
                {
                    sourcePageId: "page-2",
                    sourceIndexInPage: 1,
                    targetPageId: "page-4",
                    targetIndexInPage: 0,
                    lang: "en",
                },
            ]);
            expect(box(page, 0).textContent).toBe("and the rest of it");
            expect(groupOf(box(page, 0)).getAttribute(kFlowChainAttr)).toBe(
                "chain-9",
            );
            expect(buttonsOn(page).length).toBe(0);
        });

        it("prefers a source on this page: text must not jump over a box that could hold it", async () => {
            pendingOverflow = {
                pageId: "page-2",
                pageNumber: "3",
                indexInPage: 1,
                previewText: "and the rest of it",
            };
            const page = makePage([
                { text: { en: "long text" }, overflowing: ["en"] },
                { text: { en: "" } },
            ]);
            page.id = "page-4";

            updateContinueButtons(page);
            await flushAnswers();

            expect(getContinueButtonLabel(box(page, 1))).toBe(
                kContinueButtonEnglish,
            );
        });
    });

    describe("removeContinueButtons", () => {
        it("takes every button out whether or not the box still qualifies", () => {
            const page = makePage([
                { text: { en: "long text" }, overflowing: ["en"] },
                { text: { en: "" } },
            ]);
            updateContinueButtons(page);
            expect(buttonsOn(page).length).toBe(1);

            removeContinueButtons(page);

            expect(buttonsOn(page).length).toBe(0);
        });
    });
});
