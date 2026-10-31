// A command chosen from a canvas element's menu, or from its toolbar, is one undo step of its own
// however it was chosen. A pointer or key press opens a transaction (pageHistoryInput.ts), but a
// click with no press before it opens none: a script's click, or the click that assistive
// technology sends when it invokes a menu row. runControlCallback gives the command a transaction
// of its own for that case.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { PageHistory } from "../../pageHistory/pageHistoryTypes";
import type {
    IControlContext,
    IControlMenuRow,
    IControlRuntime,
} from "../../toolbox/canvas/canvasControlTypes";

const access = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
}));

vi.mock("../../pageHistory/pageHistoryAccess", () => ({
    currentPageHistory: () => access.history,
    doOutsideHistory: <T>(fn: () => T): T =>
        access.history ? access.history.doOutsideHistory(fn) : fn(),
}));

import { startPageHistoryForThisPage } from "../../pageHistory/pageHistorySetup";
import { convertControlMenuRows } from "./canvasControlMenuRendering";

let page: HTMLElement;
let canvasElement: HTMLElement;

beforeEach(() => {
    page = document.createElement("div");
    page.className = "bloom-page";
    page.innerHTML =
        '<div class="bloom-canvas"><div class="bloom-canvas-element"></div></div>';
    document.body.appendChild(page);
    canvasElement = page.querySelector<HTMLElement>(".bloom-canvas-element")!;
    startPageHistoryForThisPage(
        {
            noPageContentDelays: () => Promise.resolve(),
            toolboxPassesHaveRun: () => Promise.resolve(),
        },
        [],
        (history) => (access.history = history),
    );
});

afterEach(() => {
    // What the page unload does: pageHistorySetup ends the history.
    window.dispatchEvent(new Event("pagehide"));
    access.history = undefined;
    page.remove();
});

// Two menu rows, as Rotate right and Flip horizontal are: each changes the canvas element.
function makeMenu() {
    const rows: IControlMenuRow[] = [
        {
            id: "rotateImageRight",
            l10nId: "EditTab.Image.RotateRight",
            onSelect: () => {
                canvasElement.style.transform = "rotate(90deg)";
            },
        },
        {
            id: "flipHorizontal",
            l10nId: "EditTab.Image.FlipHorizontal",
            onSelect: () => {
                canvasElement.setAttribute("data-flipped", "true");
            },
        },
    ];
    return convertControlMenuRows(
        rows,
        {} as IControlContext,
        {
            closeMenu: vi.fn(),
        } as IControlRuntime,
    );
}

// Choose a row as a click with no press before it does: its onClick, with no transaction open.
async function choose(row: { onClick: () => void }): Promise<void> {
    expect(
        access.history!.hasOpenTransaction,
        "test setup: no press has opened a transaction",
    ).toBe(false);
    row.onClick();
    await vi.waitFor(() =>
        expect(access.history!.hasOpenTransaction).toBe(false),
    );
}

describe("a menu command chosen with no press before the click", () => {
    it("is one undo step, and the next command is another", async () => {
        const history = access.history!;
        const [rotate, flip] = makeMenu();

        await choose(rotate);
        await choose(flip);
        // Sanity check: both commands changed the page.
        expect(canvasElement.style.transform).toBe("rotate(90deg)");
        expect(canvasElement.getAttribute("data-flipped")).toBe("true");

        await history.undo();
        expect(canvasElement.hasAttribute("data-flipped")).toBe(false);
        expect(
            canvasElement.style.transform,
            "the first undo should take back only the second command",
        ).toBe("rotate(90deg)");

        await history.undo();
        expect(canvasElement.style.transform).toBe("");
        expect(history.undoDescriptionL10nKey).toBeUndefined();
    });
});
