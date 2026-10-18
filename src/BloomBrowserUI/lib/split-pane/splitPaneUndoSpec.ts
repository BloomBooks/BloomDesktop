// Tests that the undo step of a drag of a split pane's divider says that it moved the splitter.
// Uses a real page history, and stands in for the transaction with the general description that
// the press opens (pageHistoryInput.ts).
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// Code in the page frame finds the page history through the page frame's bundle exports; give it
// this test's.
const pageFrame = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
}));
vi.mock("../../bookEdit/js/workspaceFrames", async (importOriginal) => ({
    ...(await importOriginal<
        typeof import("../../bookEdit/js/workspaceFrames")
    >()),
    getEditablePageBundleExports: () => ({
        pageHistory: () => pageFrame.history,
    }),
}));

// These imports deliberately come after the vi.mock call above, so that the module graph they
// pull in gets the stubbed module.
import jQuery from "jquery";
import { splitPane } from "./split-pane";
import { createPageHistory } from "../../bookEdit/pageHistory/pageHistory";
import {
    formatUndoDescription,
    kGenericUndoDescriptionL10nKey,
    type PageHistory,
} from "../../bookEdit/pageHistory/pageHistoryTypes";

let history: PageHistory;

beforeEach(() => {
    document.body.innerHTML = `
        <div class="bloom-page" id="page">
            <div class="marginBox">
                <div class="split-pane horizontal-percent" id="pane">
                    <div class="split-pane-component position-top" id="top" style="bottom: 50%"></div>
                    <div class="split-pane-divider horizontal-divider" id="divider" style="bottom: 50%"></div>
                    <div class="split-pane-component position-bottom" id="bottom" style="height: 50%"></div>
                </div>
            </div>
        </div>`;
    history = createPageHistory({ root: document.getElementById("page")! });
    pageFrame.history = history;
    splitPane(jQuery("#pane"));
});

afterEach(() => {
    pageFrame.history = undefined;
    history.dispose();
    document.body.innerHTML = "";
});

describe("the undo step of a drag of a split pane's divider", () => {
    it("is Move with the splitter as the thing moved", async () => {
        const divider = document.getElementById("divider")!;
        const top = document.getElementById("top")!;
        expect(
            history.getStepDescriptions().undo,
            "test setup: there should be no undo step yet",
        ).toEqual([]);

        // As Bloom runs the handlers of a press: inside the transaction with the general
        // description that it opens, which makes a step only if something changed.
        const transaction = history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        divider.dispatchEvent(
            new MouseEvent("mousedown", { bubbles: true, buttons: 1 }),
        );
        // What the drag writes as the divider moves. jsdom has no layout, so the test writes it.
        top.style.bottom = "40%";
        divider.style.bottom = "40%";
        document.dispatchEvent(new MouseEvent("mouseup", { bubbles: true }));
        // A MutationObserver delivers its records in a microtask.
        await Promise.resolve();
        transaction.end();

        const undo = history.getStepDescriptions().undo;
        expect(undo.length).toBe(1);
        expect(formatUndoDescription(undo[0].l10nId, undo[0].args)).toBe(
            "Move(Splitter)",
        );
    });
});
