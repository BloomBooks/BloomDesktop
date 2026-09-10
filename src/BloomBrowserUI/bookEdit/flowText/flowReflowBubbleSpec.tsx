import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// What Bloom would say about the refits it is holding, and what it was asked to do. The real
// module talks to FlowTextApi; flowBoundaryClient is mocked too, because the real one reaches
// bloomEditing and the whole edit page with it.
let pendingWalks:
    | { pending: boolean; chainIds: string[]; reflowOnPageChange?: boolean }
    | undefined;
let reflowOnPageChangeAnswer = false;
let reflowNowCalls = 0;
const reflowOnPageChangePosts: boolean[] = [];

vi.mock("./flowReflowClient", () => ({
    getPendingWalks: () => Promise.resolve(pendingWalks),
    getReflowOnPageChange: () => Promise.resolve(reflowOnPageChangeAnswer),
    postReflowNow: () => {
        reflowNowCalls++;
        return Promise.resolve();
    },
    postReflowOnPageChange: (value: boolean) => {
        reflowOnPageChangePosts.push(value);
        return Promise.resolve();
    },
}));

vi.mock("./flowBoundaryClient", () => ({
    waitForBoundaryQueue: () => Promise.resolve(),
}));

import {
    kReflowBubbleClass,
    kReflowNowEnglish,
    kReflowNowTestId,
    kReflowOnPageChangeEnglish,
    kReflowOnPageChangeTestId,
    kReflowPendingEnglish,
} from "./flowConstants";
import {
    computeReflowBubblePlacement,
    getReflowBubbleFor,
    refreshReflowBubbles,
    removeReflowBubbles,
    setFlowSettleWaiter,
} from "./flowReflowBubble";

/**
 * A page holding one translation group per entry of `chainIds`. An undefined entry makes a
 * group that is in no chain, which is what a group with nothing to reflow looks like. The page
 * sits inside the zoom container the real edit page has, which is where the panel is placed.
 */
function makePage(chainIds: (string | undefined)[]): HTMLElement {
    const scalingContainer = document.createElement("div");
    scalingContainer.id = "page-scaling-container";

    const page = document.createElement("div");
    page.className = "bloom-page";
    page.id = "page-1";
    const marginBox = document.createElement("div");
    marginBox.className = "marginBox";
    page.appendChild(marginBox);

    chainIds.forEach((chainId) => {
        const group = document.createElement("div");
        group.className = "bloom-translationGroup";
        if (chainId) {
            group.setAttribute("data-flow-chain", chainId);
        }

        const editable = document.createElement("div");
        editable.className = "bloom-editable bloom-visibility-code-on";
        editable.setAttribute("lang", "en");
        editable.setAttribute("contenteditable", "true");
        group.appendChild(editable);
        marginBox.appendChild(group);
    });

    scalingContainer.appendChild(page);
    document.body.appendChild(scalingContainer);
    return page;
}

function getGroup(page: HTMLElement, index: number): HTMLElement {
    return page.querySelectorAll<HTMLElement>(".bloom-translationGroup")[index];
}

function getCheckbox(bubble: HTMLElement): HTMLInputElement {
    const box = bubble.querySelector<HTMLInputElement>(
        `[data-testid="${kReflowOnPageChangeTestId}"]`,
    );
    if (!box) {
        fail("the panel has no reflow-on-page-change checkbox");
    }
    return box;
}

function getReflowNowButton(bubble: HTMLElement): HTMLButtonElement {
    const button = bubble.querySelector<HTMLButtonElement>(
        `[data-testid="${kReflowNowTestId}"]`,
    );
    if (!button) {
        fail("the panel has no reflow-now button");
    }
    return button;
}

/** Refresh, letting React commit and the promises the panel makes settle. */
async function refresh(page: HTMLElement): Promise<void> {
    await act(async () => {
        await refreshReflowBubbles(page);
    });
}

describe("flowReflowBubble", () => {
    beforeEach(() => {
        pendingWalks = undefined;
        reflowOnPageChangeAnswer = false;
        reflowNowCalls = 0;
        reflowOnPageChangePosts.length = 0;
        setFlowSettleWaiter(undefined);
        document.body.innerHTML = "";
    });

    afterEach(() => {
        removeReflowBubbles(document);
        document.body.innerHTML = "";
    });

    it("says a refit is waiting, and lets it be run, on the group whose chain is waiting", async () => {
        const page = makePage(["chainA", "chainB", undefined]);
        pendingWalks = {
            pending: true,
            chainIds: ["chainA"],
            reflowOnPageChange: false,
        };
        // Sanity check: nothing is up before the refresh.
        expect(getReflowBubbleFor(getGroup(page, 0))).toBeUndefined();

        await refresh(page);

        const bubble = getReflowBubbleFor(getGroup(page, 0));
        if (!bubble) {
            fail("the waiting chain's group got no panel");
        }
        expect(bubble.textContent).toContain(kReflowPendingEnglish);
        expect(bubble.textContent).toContain(kReflowOnPageChangeEnglish);
        expect(getReflowNowButton(bubble).textContent).toBe(kReflowNowEnglish);
        expect(getReflowNowButton(bubble).disabled).toBe(false);
        // It must never be saved with the page.
        expect(bubble.classList.contains("bloom-ui")).toBe(true);
        expect(bubble.getAttribute("contenteditable")).toBe("false");
        // The second group's chain has nothing waiting, and the third is in no chain at all.
        expect(getReflowBubbleFor(getGroup(page, 1))).toBeUndefined();
        expect(getReflowBubbleFor(getGroup(page, 2))).toBeUndefined();
    });

    it("puts no panel on a chained group whose own chain has nothing waiting", async () => {
        const page = makePage(["chainA"]);
        pendingWalks = {
            pending: true,
            chainIds: ["chainB"],
            reflowOnPageChange: false,
        };

        await refresh(page);

        expect(getReflowBubbleFor(getGroup(page, 0))).toBeUndefined();
        expect(document.querySelectorAll(`.${kReflowBubbleClass}`).length).toBe(
            0,
        );
    });

    it("places the panel in the zoom container beside the page, not inside the page", async () => {
        const page = makePage(["chainA"]);
        pendingWalks = {
            pending: true,
            chainIds: ["chainA"],
            reflowOnPageChange: false,
        };

        await refresh(page);

        const bubble = getReflowBubbleFor(getGroup(page, 0));
        if (!bubble) {
            fail("the waiting chain's group got no panel");
        }
        expect(bubble.parentElement?.id).toBe("page-scaling-container");
        expect(page.contains(bubble)).toBe(false);
        // Placed, rather than left where the stylesheet's own position would put it.
        expect(bubble.style.left).not.toBe("");
        expect(bubble.style.top).not.toBe("");
    });

    it("takes the checkbox's state from what Bloom said", async () => {
        const page = makePage(["chainA"]);
        pendingWalks = {
            pending: true,
            chainIds: ["chainA"],
            reflowOnPageChange: true,
        };

        await refresh(page);

        const bubble = getReflowBubbleFor(getGroup(page, 0));
        if (!bubble) {
            fail("the waiting chain's group got no panel");
        }
        expect(getCheckbox(bubble).checked).toBe(true);
    });

    it("asks for the setting on its own when the reply does not carry it", async () => {
        const page = makePage(["chainA"]);
        pendingWalks = { pending: true, chainIds: ["chainA"] };
        reflowOnPageChangeAnswer = true;

        await refresh(page);

        const bubble = getReflowBubbleFor(getGroup(page, 0));
        if (!bubble) {
            fail("the waiting chain's group got no panel");
        }
        expect(getCheckbox(bubble).checked).toBe(true);
    });

    it("takes the panel away once the waiting refit has run", async () => {
        const page = makePage(["chainA"]);
        pendingWalks = {
            pending: true,
            chainIds: ["chainA"],
            reflowOnPageChange: false,
        };
        await refresh(page);
        // Sanity check: the panel this test is about removing is there, saying a refit waits.
        const before = getReflowBubbleFor(getGroup(page, 0));
        if (!before) {
            fail("the waiting chain's group got no panel");
        }

        pendingWalks = {
            pending: false,
            chainIds: [],
            reflowOnPageChange: false,
        };
        await refresh(page);

        expect(getReflowBubbleFor(getGroup(page, 0))).toBeUndefined();
        expect(document.querySelectorAll(`.${kReflowBubbleClass}`).length).toBe(
            0,
        );
    });

    it("takes the panel away from a group that has left its chain", async () => {
        const page = makePage(["chainA"]);
        pendingWalks = {
            pending: true,
            chainIds: ["chainA"],
            reflowOnPageChange: false,
        };
        await refresh(page);
        // Sanity check: the panel this test is about removing is there.
        expect(getReflowBubbleFor(getGroup(page, 0))).toBeDefined();

        getGroup(page, 0).removeAttribute("data-flow-chain");
        await refresh(page);

        expect(getReflowBubbleFor(getGroup(page, 0))).toBeUndefined();
        expect(document.querySelectorAll(`.${kReflowBubbleClass}`).length).toBe(
            0,
        );
    });

    it("leaves the panels alone when Bloom cannot be asked", async () => {
        const page = makePage(["chainA"]);
        pendingWalks = {
            pending: true,
            chainIds: ["chainA"],
            reflowOnPageChange: false,
        };
        await refresh(page);
        // Sanity check: there is a panel for the failed read to leave alone.
        expect(getReflowBubbleFor(getGroup(page, 0))).toBeDefined();

        pendingWalks = undefined;
        await refresh(page);

        expect(getReflowBubbleFor(getGroup(page, 0))).toBeDefined();
    });

    it("runs the waiting refits when the button is clicked, after this page has settled", async () => {
        const page = makePage(["chainA"]);
        pendingWalks = {
            pending: true,
            chainIds: ["chainA"],
            reflowOnPageChange: false,
        };
        let settleWaited = false;
        setFlowSettleWaiter(() => {
            settleWaited = true;
            return Promise.resolve();
        });
        await refresh(page);
        // Sanity check: nothing has been run before the click.
        expect(reflowNowCalls).toBe(0);

        const bubble = getReflowBubbleFor(getGroup(page, 0));
        if (!bubble) {
            fail("the waiting chain's group got no panel");
        }
        await act(async () => {
            getReflowNowButton(bubble).click();
        });

        expect(reflowNowCalls).toBe(1);
        expect(settleWaited).toBe(true);
    });

    it("posts the new setting as soon as the checkbox is changed", async () => {
        const page = makePage(["chainA"]);
        pendingWalks = {
            pending: true,
            chainIds: ["chainA"],
            reflowOnPageChange: false,
        };
        await refresh(page);

        const bubble = getReflowBubbleFor(getGroup(page, 0));
        if (!bubble) {
            fail("the waiting chain's group got no panel");
        }
        // Sanity check: the checkbox starts off, and nothing has been posted.
        expect(getCheckbox(bubble).checked).toBe(false);
        expect(reflowOnPageChangePosts).toEqual([]);

        await act(async () => {
            getCheckbox(bubble).click();
        });

        expect(reflowOnPageChangePosts).toEqual([true]);
        expect(getCheckbox(bubble).checked).toBe(true);
    });

    it("removes every panel when the page is let go", async () => {
        const page = makePage(["chainA", "chainB"]);
        pendingWalks = {
            pending: true,
            chainIds: ["chainA", "chainB"],
            reflowOnPageChange: false,
        };
        await refresh(page);
        // Sanity check: both groups have a panel to remove.
        expect(getReflowBubbleFor(getGroup(page, 0))).toBeDefined();
        expect(getReflowBubbleFor(getGroup(page, 1))).toBeDefined();

        removeReflowBubbles(document);

        expect(getReflowBubbleFor(getGroup(page, 0))).toBeUndefined();
        expect(getReflowBubbleFor(getGroup(page, 1))).toBeUndefined();
        expect(document.querySelectorAll(`.${kReflowBubbleClass}`).length).toBe(
            0,
        );
    });
});

describe("computeReflowBubblePlacement", () => {
    // A container at the viewport origin, so that a viewport pixel and a container pixel are
    // the same thing and the numbers below read as the rule they are testing.
    const atOrigin = { containerLeft: 0, containerTop: 0, zoom: 1 };

    it("puts the panel clear of the page's right edge", () => {
        const placement = computeReflowBubblePlacement({
            ...atOrigin,
            pageRight: 500,
            groupTop: 100,
            groupBottom: 400,
            bubbleHeight: 80,
            sourceBubbleHeight: 0,
        });

        // The gap is the source bubbles' own tip width.
        expect(placement.left).toBe(510);
    });

    it("lines the panel's bottom up with the bottom of the group", () => {
        const placement = computeReflowBubblePlacement({
            ...atOrigin,
            pageRight: 500,
            groupTop: 100,
            groupBottom: 400,
            bubbleHeight: 80,
            sourceBubbleHeight: 0,
        });

        expect(placement.top).toBe(320);
        expect(placement.top + 80).toBe(400);
    });

    it("leaves a tall group's source bubble alone, because the bottom is already clear of it", () => {
        const withoutSourceBubble = computeReflowBubblePlacement({
            ...atOrigin,
            pageRight: 500,
            groupTop: 100,
            groupBottom: 500,
            bubbleHeight: 80,
            sourceBubbleHeight: 0,
        });
        // Sanity check: bottom alignment puts the panel below where the source bubble ends,
        // which is what makes this the tall-group case.
        expect(withoutSourceBubble.top).toBeGreaterThan(100 + 160);

        const withSourceBubble = computeReflowBubblePlacement({
            ...atOrigin,
            pageRight: 500,
            groupTop: 100,
            groupBottom: 500,
            bubbleHeight: 80,
            sourceBubbleHeight: 160,
        });

        expect(withSourceBubble.top).toBe(withoutSourceBubble.top);
    });

    it("moves the panel below a short group's source bubble", () => {
        const bottomAligned = computeReflowBubblePlacement({
            ...atOrigin,
            pageRight: 500,
            groupTop: 100,
            groupBottom: 200,
            bubbleHeight: 80,
            sourceBubbleHeight: 0,
        });
        // Sanity check: bottom alignment alone would put the panel over the source bubble,
        // which occupies the 160 pixels below the group's top.
        expect(bottomAligned.top).toBeLessThan(100 + 160);

        const placement = computeReflowBubblePlacement({
            ...atOrigin,
            pageRight: 500,
            groupTop: 100,
            groupBottom: 200,
            bubbleHeight: 80,
            sourceBubbleHeight: 160,
        });

        // Below where the source bubble ends, by the gap between the two.
        expect(placement.top).toBe(266);
        expect(placement.top).toBeGreaterThan(100 + 160);
    });

    it("turns viewport pixels into the zoomed container's own coordinates", () => {
        const placement = computeReflowBubblePlacement({
            containerLeft: 40,
            containerTop: 20,
            zoom: 2,
            pageRight: 540,
            groupTop: 220,
            groupBottom: 820,
            bubbleHeight: 80,
            sourceBubbleHeight: 0,
        });

        // (540 - 40) / 2 = 250, plus the gap; (820 - 20) / 2 = 400, less the panel's height.
        expect(placement.left).toBe(260);
        expect(placement.top).toBe(320);
    });
});
