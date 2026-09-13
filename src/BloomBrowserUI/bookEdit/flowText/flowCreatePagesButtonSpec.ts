import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// What C# would say about the pages the browser cannot see, and what it was asked to do.
let nextBox:
    | { pageId: string; pageNumber: string; indexInPage: number; html: string }
    | undefined;
let createPagesResult:
    | {
          chainId: string;
          sourceHtml: string;
          pagesCreated: number;
          lastPageId: string;
      }
    | undefined;
const createPagesCalls: Record<string, unknown>[] = [];
// The pages the module asked Bloom to open, in the order it asked.
const pagesShown: string[] = [];

vi.mock("./flowBoundaryClient", () => ({
    // Used by this module.
    peekNext: () => Promise.resolve(nextBox),
    createPagesAndContinue: (args: Record<string, unknown>) => {
        createPagesCalls.push(args);
        return Promise.resolve(createPagesResult);
    },
    jumpToPage: (pageId: string) => {
        pagesShown.push(pageId);
    },
    // Used by flowContinueButton, which this module imports for the pass runner.
    getPendingOverflow: () => Promise.resolve(undefined),
    continueInto: () => Promise.resolve(undefined),
}));

import {
    canOfferCreatePages,
    createPagesFor,
    getCreatePagesButtonLabel,
    hasCreatePagesButton,
    removeCreatePagesButtons,
    updateCreatePagesButtons,
} from "./flowCreatePagesButton";
import {
    kCreatePagesButtonEnglish,
    kCreatePagesButtonTestId,
    kFlowChainAttr,
    kOverflowStartClass,
} from "./flowConstants";
import { setFlowPassRunner } from "./flowContinueButton";
import { resetNextBoxCache } from "./flowNextBox";
import { setFlowTextAvailableForTesting } from "./flowTextAvailable";

/**
 * A page of origami split panes holding one translation group per box. `boxes` says, per box,
 * what each language's editable holds and whether it carries the mark that says its text stops
 * fitting.
 */
type BoxSpec = {
    /** The text of each language's editable, by language tag. */
    text: Record<string, string>;
    /** The languages whose editable carries the overflow mark. */
    overflowing?: string[];
    /** The chain id the group carries, if it is already linked. */
    chainId?: string;
    /** Extra classes for each editable, e.g. a style other than normal-style. */
    editableClasses?: string;
};

function makePage(
    boxes: BoxSpec[],
    pageClasses = "",
    pageId = "page-1",
): HTMLElement {
    const page = document.createElement("div");
    page.className = `bloom-page ${pageClasses}`.trim();
    page.id = pageId;
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
            editable.className = `bloom-editable ${
                box.editableClasses ?? "normal-style"
            } bloom-visibility-code-on`;
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
            `[data-testid="${kCreatePagesButtonTestId}"]`,
        ),
    );
}

/** Let the round trip to C# and the update it schedules finish. */
async function flushAnswers(): Promise<void> {
    for (let tick = 0; tick < 5; tick++) {
        await Promise.resolve();
    }
}

/**
 * Give a box a place and a size, which jsdom does not work out for itself. The button is placed
 * from these, so a test about placement has to say what they are.
 */
function giveBoxABox(
    editable: HTMLElement,
    left: number,
    top: number,
    width: number,
    height: number,
): void {
    Object.defineProperty(editable, "offsetLeft", { value: left });
    Object.defineProperty(editable, "offsetTop", { value: top });
    Object.defineProperty(editable, "offsetWidth", { value: width });
    Object.defineProperty(editable, "offsetHeight", { value: height });
}

describe("flowCreatePagesButton", () => {
    beforeEach(() => {
        // The collection is allowed to use flow text; nothing here has a Bloom to ask.
        setFlowTextAvailableForTesting(true);
        document.body.innerHTML = "";
        resetNextBoxCache();
        nextBox = undefined;
        createPagesResult = undefined;
        createPagesCalls.length = 0;
        pagesShown.length = 0;
    });

    afterEach(() => {
        document.body.innerHTML = "";
        setFlowPassRunner(undefined);
    });

    describe("which boxes get the offer", () => {
        it("offers an unlinked box whose text does not fit", () => {
            const page = makePage([
                { text: { en: "more than fits" }, overflowing: ["en"] },
            ]);

            updateCreatePagesButtons(page);

            const buttons = buttonsOn(page);
            expect(buttons.length).toBe(1);
            expect(buttons[0].textContent).toBe(kCreatePagesButtonEnglish);
            expect(getCreatePagesButtonLabel(box(page, 0))).toBe(
                kCreatePagesButtonEnglish,
            );
        });

        it("does not offer a box whose text fits", () => {
            const page = makePage([{ text: { en: "short" } }]);

            updateCreatePagesButtons(page);

            expect(buttonsOn(page).length).toBe(0);
        });

        it("does not offer a box with a later box of its chain on this page", () => {
            const page = makePage([
                {
                    text: { en: "more than fits" },
                    overflowing: ["en"],
                    chainId: "chain",
                },
                { text: { en: "" }, chainId: "chain" },
            ]);

            updateCreatePagesButtons(page);

            // The first box's extra text goes to the second box, so the run does not end here.
            expect(hasCreatePagesButton(box(page, 0))).toBe(false);
            // The second box holds no mark, so it is not where the run ends either.
            expect(hasCreatePagesButton(box(page, 1))).toBe(false);
        });

        it("offers the last box of a chain once Bloom says no page after this one holds one", async () => {
            const page = makePage([
                {
                    text: { en: "more than fits" },
                    overflowing: ["en"],
                    chainId: "chain",
                },
            ]);

            // The first call starts the round trip, so it cannot know yet: an offer that
            // appeared and then vanished would be worse than one a moment late.
            updateCreatePagesButtons(page);
            expect(buttonsOn(page).length).toBe(0);

            await flushAnswers();

            expect(buttonsOn(page).length).toBe(1);
        });

        it("does not offer a linked box whose chain runs on to a later page", async () => {
            nextBox = {
                pageId: "page-2",
                pageNumber: "3",
                indexInPage: 0,
                html: "<p>the rest</p>",
            };
            const page = makePage([
                {
                    text: { en: "more than fits" },
                    overflowing: ["en"],
                    chainId: "chain",
                },
            ]);

            updateCreatePagesButtons(page);
            await flushAnswers();

            expect(buttonsOn(page).length).toBe(0);
        });

        it("does not offer a box on a layout that scrolls instead of overflowing", () => {
            const page = makePage(
                [{ text: { en: "more than fits" }, overflowing: ["en"] }],
                "Device16x9Portrait",
            );

            updateCreatePagesButtons(page);

            expect(buttonsOn(page).length).toBe(0);
        });

        it("does not offer a box the flow refuses", () => {
            const page = makePage([
                {
                    text: { en: "more than fits" },
                    overflowing: ["en"],
                    editableClasses: "Heading1-style",
                },
            ]);

            // Sanity check: the box would qualify but for its style.
            expect(page.querySelector(`span.${kOverflowStartClass}`)).not.toBe(
                null,
            );

            updateCreatePagesButtons(page);

            expect(buttonsOn(page).length).toBe(0);
        });

        it("does not offer a box in the front matter", () => {
            const page = makePage(
                [{ text: { en: "more than fits" }, overflowing: ["en"] }],
                "bloom-frontMatter",
            );

            expect(canOfferCreatePages(box(page, 0), page)).toBe(false);
        });

        it("takes the offer away when the box stops overflowing", () => {
            const page = makePage([
                { text: { en: "more than fits" }, overflowing: ["en"] },
            ]);
            updateCreatePagesButtons(page);
            expect(hasCreatePagesButton(box(page, 0))).toBe(true);

            page.querySelector(`span.${kOverflowStartClass}`)!.remove();
            updateCreatePagesButtons(page);

            expect(hasCreatePagesButton(box(page, 0))).toBe(false);
        });

        it("gives each language of a group its own offer", () => {
            const page = makePage([
                {
                    text: { en: "more than fits", fr: "plus qu'il n'en tient" },
                    overflowing: ["en", "fr"],
                },
            ]);

            updateCreatePagesButtons(page);

            expect(buttonsOn(page).length).toBe(2);
            expect(hasCreatePagesButton(box(page, 0, "en"))).toBe(true);
            expect(hasCreatePagesButton(box(page, 0, "fr"))).toBe(true);
        });

        it("removeCreatePagesButtons takes every offer out", () => {
            const page = makePage([
                { text: { en: "more than fits" }, overflowing: ["en"] },
            ]);
            updateCreatePagesButtons(page);
            expect(buttonsOn(page).length).toBe(1);

            removeCreatePagesButtons(page);

            expect(buttonsOn(page).length).toBe(0);
        });
    });

    describe("where the offer sits", () => {
        it("sits below its own box, ending at the box's right edge", () => {
            const page = makePage([
                { text: { en: "short" } },
                { text: { en: "more than fits" }, overflowing: ["en"] },
            ]);
            giveBoxABox(box(page, 1), 30, 200, 400, 150);

            updateCreatePagesButtons(page);

            // The box's right edge as the left; the stylesheet's translate is what pulls the
            // button's own width back from there, so it ends flush with that edge. The top is
            // below the box, so the button covers none of the text.
            const button = buttonsOn(page)[0];
            expect(button.style.left).toBe(`${30 + 400}px`);
            expect(button.style.top).toBe(`${200 + 150 + 4}px`);
        });

        it("sits inside the box when there is no room below it on the page", () => {
            const page = makePage([
                { text: { en: "more than fits" }, overflowing: ["en"] },
            ]);
            const editable = box(page, 0);
            giveBoxABox(editable, 30, 200, 400, 150);

            // Place it once to have a button to measure, then say that below the box is off the
            // page and place it again. jsdom lays nothing out, so both the button's height and
            // every rectangle have to be said here.
            updateCreatePagesButtons(page);
            const button = buttonsOn(page)[0];
            Object.defineProperty(button, "offsetHeight", { value: 20 });
            button.getBoundingClientRect = () => ({ bottom: 900 }) as DOMRect;
            page.getBoundingClientRect = () => ({ bottom: 500 }) as DOMRect;

            updateCreatePagesButtons(page);

            expect(
                button.style.top,
                "The button has to stay on the page, so it sits inside the box's bottom edge.",
            ).toBe(`${200 + 150 - 20}px`);
        });

        it("is a bloom-ui element that takes no part in the text", () => {
            const page = makePage([
                { text: { en: "more than fits" }, overflowing: ["en"] },
            ]);

            updateCreatePagesButtons(page);

            const button = buttonsOn(page)[0];
            expect(button.classList.contains("bloom-ui")).toBe(true);
            expect(button.getAttribute("contenteditable")).toBe("false");
            expect(button.getAttribute("role")).toBe("button");
            // It hangs off the group, not inside the contenteditable box.
            expect(button.parentElement).toBe(groupOf(box(page, 0)));
        });
    });

    describe("taking the offer", () => {
        it("tells Bloom which box it is, and puts back what Bloom says the box keeps", async () => {
            createPagesResult = {
                chainId: "made-here",
                sourceHtml: "<p>what fits</p>",
                pagesCreated: 3,
                lastPageId: "made-last",
            };
            const page = makePage([
                { text: { en: "more than fits" }, overflowing: ["en"] },
            ]);
            const editable = box(page, 0);
            const htmlSent = editable.innerHTML;

            await createPagesFor(editable);

            expect(createPagesCalls.length).toBe(1);
            expect(createPagesCalls[0]).toEqual({
                pageId: "page-1",
                indexInPage: 0,
                lang: "en",
                chainId: "",
                html: htmlSent,
            });
            expect(editable.innerHTML).toBe("<p>what fits</p>");
            // An unlinked box is linked by taking the offer: that is what makes the chain.
            expect(groupOf(editable).getAttribute(kFlowChainAttr)).toBe(
                "made-here",
            );
            // The box now holds only what fits, so it makes no offer any more.
            expect(hasCreatePagesButton(editable)).toBe(false);
            // The author is taken to where the text now ends.
            expect(pagesShown).toEqual(["made-last"]);
        });

        it("takes the overflow warning off the box and the page before the jump", async () => {
            // The jump saves this page at once, and a page thumbnail draws its red warning
            // triangle from the pageOverflows class, so a page still marked when the save
            // happens goes on warning about text that has moved to another page.
            createPagesResult = {
                chainId: "made-here",
                sourceHtml: "<p>what fits</p>",
                pagesCreated: 2,
                lastPageId: "made-last",
            };
            const page = makePage([
                { text: { en: "more than fits" }, overflowing: ["en"] },
            ]);
            const editable = box(page, 0);
            editable.classList.add("overflow");
            page.classList.add("pageOverflows");
            // What the trigger hands over in Bloom is OverflowChecker: marking a box that has
            // just been measured, and then the page's own class from the boxes left on it. jsdom
            // measures nothing, so the rule the page-level half applies is written out here.
            const marked: HTMLElement[] = [];
            const pagesUpdated: HTMLElement[] = [];
            setFlowPassRunner({
                applyWithoutPass: (work) => work(),
                requestPassFor: () => undefined,
                markOverflow: (measured) => marked.push(measured),
                updatePageOverflow: (updated) => {
                    pagesUpdated.push(updated);
                    if (
                        !updated.querySelector(
                            ".overflow, .thisOverflowingParent",
                        )
                    ) {
                        updated.classList.remove("pageOverflows");
                    }
                },
            });

            await createPagesFor(editable);

            expect(
                editable.classList.contains("overflow"),
                "The box holds only what fits it now, so it must not still be marked.",
            ).toBe(false);
            expect(
                page.classList.contains("pageOverflows"),
                "No box on the page is overfull, so the page must not still warn.",
            ).toBe(false);
            expect(marked).toEqual([editable]);
            expect(pagesUpdated).toEqual([page]);
            // The page was brought up to date before the jump that saves it.
            expect(pagesShown).toEqual(["made-last"]);
        });

        it("sends the chain id of a box that is already linked", async () => {
            createPagesResult = {
                chainId: "chain",
                sourceHtml: "<p>what fits</p>",
                pagesCreated: 1,
                lastPageId: "made-last",
            };
            const page = makePage([
                {
                    text: { en: "more than fits" },
                    overflowing: ["en"],
                    chainId: "chain",
                },
            ]);

            await createPagesFor(box(page, 0));

            expect(createPagesCalls[0].chainId).toBe("chain");
        });

        it("clicking the button is what takes the offer", async () => {
            createPagesResult = {
                chainId: "made-here",
                sourceHtml: "<p>what fits</p>",
                pagesCreated: 2,
                lastPageId: "made-last",
            };
            const page = makePage([
                { text: { en: "more than fits" }, overflowing: ["en"] },
            ]);
            updateCreatePagesButtons(page);

            buttonsOn(page)[0].click();
            await flushAnswers();

            expect(createPagesCalls.length).toBe(1);
            expect(box(page, 0).innerHTML).toBe("<p>what fits</p>");
        });

        it("changes nothing when Bloom made no pages", async () => {
            createPagesResult = undefined;
            const page = makePage([
                { text: { en: "more than fits" }, overflowing: ["en"] },
            ]);
            const editable = box(page, 0);
            const before = editable.innerHTML;

            await createPagesFor(editable);

            expect(editable.innerHTML).toBe(before);
            expect(groupOf(editable).hasAttribute(kFlowChainAttr)).toBe(false);
        });
    });
});
