// The Impairment Visualizer and the page history: after an undo or redo that changed a picture,
// the tool makes its color-blindness overlays again, outside the history, while it is on the page.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createPageHistory } from "../../pageHistory/pageHistory";
import { createEditTimeMarkupFilter } from "../../pageHistory/editTimeMarkup";
import type { PageHistory } from "../../pageHistory/pageHistoryTypes";

const mocks = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
}));
// The history that the code under test finds, with doOutsideHistoryIfAny doing what the real
// one does when there is a history.
vi.mock("../../pageHistory/pageHistoryAccess", () => ({
    currentPageHistory: () => mocks.history,
    doOutsideHistoryIfAny: <T>(fn: () => T): T =>
        mocks.history ? mocks.history.doOutsideHistory(fn) : fn(),
    clearUndoAndRedoIfAny: () => mocks.history?.clearUndoAndRedo(),
}));

import { ImpairmentVisualizerAdaptor } from "./impairmentVisualizer";

const kPictureKey = "Test.ChangePicture";
const kTypingKey = "Test.Typing";

let history: PageHistory;
let onUnmarkedChange: ReturnType<typeof vi.fn>;
let page: HTMLElement;
let img: HTMLImageElement;
let adapter: ImpairmentVisualizerAdaptor;
let updateSimulations: ReturnType<typeof vi.fn>;

beforeEach(() => {
    page = document.createElement("div");
    page.className = "bloom-page";
    page.innerHTML =
        '<div class="bloom-canvas"><div class="bloom-canvas-element bloom-backgroundImage"><div class="bloom-imageContainer"><img src="a.png"></div></div></div><div id="other">before</div>';
    document.body.appendChild(page);
    img = page.querySelector("img")!;
    onUnmarkedChange = vi.fn();
    history = createPageHistory({
        root: page,
        filter: createEditTimeMarkupFilter(),
        onUnmarkedChange,
    });
    mocks.history = history;
    adapter = new ImpairmentVisualizerAdaptor();
    // A stand-in for the React control, which these tests do not render. Like the real one,
    // it writes to the page, here something that the history would record.
    updateSimulations = vi.fn(() =>
        img.setAttribute(
            "data-simulated",
            String(updateSimulations.mock.calls.length),
        ),
    );
    (
        adapter as unknown as {
            controlsElement: { updateSimulations: typeof updateSimulations };
        }
    ).controlsElement = { updateSimulations };
});

afterEach(() => {
    adapter.detachFromPage();
    history.dispose();
    mocks.history = undefined;
    page.remove();
});

describe("The Impairment Visualizer and the page history", () => {
    it("makes the overlays again after an undo or redo of a picture change, outside the history", async () => {
        history.doOutsideHistory(() => adapter.newPageReady());
        await history.doTransaction(kPictureKey, () =>
            img.setAttribute("src", "b.png"),
        );
        updateSimulations.mockClear();

        await history.undo();

        expect(img.getAttribute("src")).toBe("a.png");
        expect(updateSimulations).toHaveBeenCalledTimes(1);
        expect(updateSimulations).toHaveBeenCalledWith(undefined);
        // What it wrote did not clear redo, nor join a step.
        expect(history.redoDescriptionL10nKey).toBe(kPictureKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();

        await history.redo();
        expect(img.getAttribute("src")).toBe("b.png");
        expect(updateSimulations).toHaveBeenCalledTimes(2);
        expect(history.undoDescriptionL10nKey).toBe(kPictureKey);
    });

    it("leaves the overlays alone after an undo that touched no picture", async () => {
        history.doOutsideHistory(() => adapter.newPageReady());
        await history.doTransaction(kTypingKey, () => {
            page.querySelector("#other")!.textContent = "typed";
        });
        updateSimulations.mockClear();

        await history.undo();

        expect(page.querySelector("#other")!.textContent).toBe("before");
        expect(updateSimulations).not.toHaveBeenCalled();
    });

    it("stops after it leaves the page", async () => {
        history.doOutsideHistory(() => adapter.newPageReady());
        await history.doTransaction(kPictureKey, () =>
            img.setAttribute("src", "b.png"),
        );
        adapter.detachFromPage();
        updateSimulations.mockClear();

        await history.undo();

        expect(img.getAttribute("src")).toBe("a.png");
        expect(updateSimulations).not.toHaveBeenCalled();
    });
});
