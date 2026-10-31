// Typing after an undo discards the redo (rule 1). The typing is a step that stays open between key
// presses, and the redo goes when that step closes; Ctrl+Y ends it first, so it never redoes the
// old step over the typing. While the typing step is still open, canRedo must already say that
// there is nothing to redo.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { PageHistory } from "./pageHistoryTypes";
import { kGenericUndoDescriptionL10nKey } from "./pageHistoryTypes";

const access = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
}));

vi.mock("./pageHistoryAccess", () => ({
    currentPageHistory: () => access.history,
    doOutsideHistory: <T>(fn: () => T): T =>
        access.history ? access.history.doOutsideHistory(fn) : fn(),
}));

import { createPageHistory } from "./pageHistory";
import {
    attachInputTransactions,
    kTypingUndoDescriptionL10nKey,
} from "./pageHistoryInput";
import { attachUndoAndRedoKeys } from "./pageHistoryKeys";
import { canRedo, startPageHistoryForThisPage } from "./pageHistorySetup";

let page: HTMLElement;
let box: HTMLElement;
let paragraph: HTMLElement;

beforeEach(() => {
    page = document.createElement("div");
    page.className = "bloom-page";
    page.innerHTML =
        '<div class="bloom-editable" contenteditable="true"><p>Hello</p></div>' +
        '<div class="bloom-canvas"><img src="a.png"></div>';
    document.body.appendChild(page);
    box = page.querySelector<HTMLElement>(".bloom-editable")!;
    paragraph = box.querySelector("p")!;
});

afterEach(() => {
    page.remove();
});

// A step, then its undo, so that there is something to redo.
async function stepAndUndo(history: PageHistory): Promise<HTMLImageElement> {
    const img = page.querySelector("img")!;
    await history.doTransaction(kGenericUndoDescriptionL10nKey, () =>
        img.setAttribute("src", "b.png"),
    );
    await history.undo();
    expect(img.getAttribute("src"), "test setup").toBe("a.png");
    expect(history.redoDescriptionL10nKey, "test setup").toBeDefined();
    return img;
}

describe("typing after an undo", () => {
    it("leaves Ctrl+Y nothing to redo, so the old step does not come back over the typing", async () => {
        const history = createPageHistory({ root: page });
        const endOfTask: (() => void)[] = [];
        const detachInput = attachInputTransactions(history, window, [], {
            isFromUser: () => true,
            afterCurrentTask: (fn) => endOfTask.push(fn),
        });
        const detachKeys = attachUndoAndRedoKeys(window, [], {
            undo: () => void history.undo(),
            redo: () => void history.redo(),
            isInChangeLayoutMode: () => false,
        });
        try {
            const img = await stepAndUndo(history);
            const key = (init: KeyboardEventInit) =>
                box.dispatchEvent(
                    new KeyboardEvent("keydown", {
                        bubbles: true,
                        cancelable: true,
                        ...init,
                    }),
                );

            key({ key: "a" });
            paragraph.textContent = "Helloa";
            // Sanity check: the typing step is still open.
            expect(history.hasOpenTransaction).toBe(true);
            key({ key: "y", ctrlKey: true });
            await new Promise((resolve) => setTimeout(resolve, 0));
            endOfTask.forEach((fn) => fn());

            expect(img.getAttribute("src")).toBe("a.png");
            expect(paragraph.textContent).toBe("Helloa");
            expect(history.redoDescriptionL10nKey).toBeUndefined();
        } finally {
            detachKeys();
            detachInput();
            history.dispose();
        }
    });

    describe("canRedo", () => {
        beforeEach(() => {
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
        });

        it("says there is nothing to redo while the typing step is still open", async () => {
            const history = access.history!;
            await stepAndUndo(history);
            expect(canRedo(), "test setup").toBe(true);

            // The typing step, which stays open between key presses.
            const typing = history.beginTransaction(
                kTypingUndoDescriptionL10nKey,
            );
            paragraph.textContent = "Helloa";

            expect(canRedo()).toBe(false);
            typing.end();
            expect(canRedo()).toBe(false);
        });
    });
});
