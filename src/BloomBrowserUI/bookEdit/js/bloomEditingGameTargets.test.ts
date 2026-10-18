// What listenToPageHistory does for game targets after an undo or redo: the page history leaves a
// target's content out, since it is a copy of its draggable's, so the page frame copies every
// draggable to its target again, whichever tool is current, except on the Play tab. These run
// against a real page history on a small game page.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createPageHistory } from "../pageHistory/pageHistory";
import type { PageHistory } from "../pageHistory/pageHistoryTypes";
import { playTabIndex } from "../toolbox/games/GameTool";

const access = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
}));

// doOutsideHistory reaches the history through the page frame's exports.
vi.mock("./workspaceFrames", async (importOriginal) => ({
    ...(await importOriginal<object>()),
    getEditablePageBundleExports: () =>
        access.history ? { pageHistory: () => access.history } : null,
}));

import { listenToPageHistory } from "./bloomEditing";

let page: HTMLElement;
let history: PageHistory;
let target: HTMLElement;

beforeEach(() => {
    page = document.createElement("div");
    page.className = "bloom-page";
    // The target shows "dog" though its draggable has "cat", as after a copy that was missed.
    page.innerHTML = `<div class="bloom-canvas">
        <div class="bloom-canvas-element" data-draggable-id="d1"
            ><div class="bloom-translationGroup"
                ><div class="bloom-editable" lang="en"><p>cat</p></div></div></div>
        <div data-target-of="d1"
            ><div class="bloom-translationGroup"
                ><div class="bloom-editable" lang="en"><p>dog</p></div></div></div>
    </div>`;
    document.body.appendChild(page);
    target = page.querySelector<HTMLElement>("[data-target-of]")!;
    history = createPageHistory({ root: page });
    access.history = history;
    listenToPageHistory(history);
});

afterEach(() => {
    history.dispose();
    access.history = undefined;
    page.remove();
    delete (window.top as unknown as Record<string, unknown>)[
        "dragActivityPage"
    ];
});

/** A step that changes nothing in the game's draggables or targets. */
async function makeUnrelatedStep(): Promise<void> {
    await history.doTransaction("Test.Unrelated", () =>
        page.setAttribute("data-unrelated", "true"),
    );
    expect(target.textContent).toBe("dog");
}

describe("listenToPageHistory and game targets", () => {
    it("copies every draggable to its target after an undo and a redo, outside the history", async () => {
        await makeUnrelatedStep();

        await history.undo();
        expect(page.hasAttribute("data-unrelated")).toBe(false);
        expect(target.textContent).toBe("cat");
        // The copy kept redo.
        expect(history.redoDescriptionL10nKey).toBe("Test.Unrelated");

        history.doOutsideHistory(() => {
            target.querySelector("p")!.textContent = "dog";
        });
        await history.redo();
        expect(page.getAttribute("data-unrelated")).toBe("true");
        expect(target.textContent).toBe("cat");
        expect(history.undoDescriptionL10nKey).toBe("Test.Unrelated");
    });

    it("leaves the targets alone on the Play tab", async () => {
        (window.top as unknown as Record<string, unknown>)["dragActivityPage"] =
            playTabIndex;
        await makeUnrelatedStep();

        await history.undo();

        expect(page.hasAttribute("data-unrelated")).toBe(false);
        expect(target.textContent).toBe("dog");
    });
});
