// A canvas command that fails part way is put back by the page history, its data-bubble among the
// rest. Comical draws from its own copy of each bubble's spec, so the code that runs a command must
// then have the bubbles drawn again from the page. These tests use a real page history, and a
// stand-in for the page frame's CanvasElementManager that records what data-bubble said each time
// it was asked to draw the bubbles again.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createPageHistory } from "../../pageHistory/pageHistory";
import type { PageHistory } from "../../pageHistory/pageHistoryTypes";

const frames = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
    element: undefined as HTMLElement | undefined,
    dataBubbleWhenRedrawn: [] as (string | null)[],
}));
// The command runners reach the page history and the CanvasElementManager through the page
// frame's exports (pageHistoryAccess.ts, canvasElementPageBridge.ts).
vi.mock("../../js/workspaceFrames", async (importOriginal) => ({
    ...(await importOriginal<object>()),
    getEditablePageBundleExports: () =>
        frames.history
            ? {
                  pageHistory: () => frames.history,
                  getTheOneCanvasElementManager: () => ({
                      redrawBubblesFromPage: () =>
                          frames.dataBubbleWhenRedrawn.push(
                              frames.element!.getAttribute("data-bubble"),
                          ),
                  }),
              }
            : null,
}));

import { runCommandControlAction } from "./canvasControlRegistry";
import type {
    ICommandControlDefinition,
    IControlContext,
    IControlRuntime,
} from "./canvasControlTypes";
import { runControlCallback } from "../../js/canvasElementManager/canvasControlMenuRendering";
import {
    addRequestPageContentDelay,
    removeRequestPageContentDelay,
} from "../../js/bloomEditing";

const kOriginalBubble = "{`version`:`1.0`,`style`:`caption`}";
const kTestDescription = "EditTab.Toolbox.ComicTool.Options.ElementType";

let history: PageHistory;
let element: HTMLElement;

beforeEach(() => {
    const page = document.createElement("div");
    page.className = "bloom-page";
    page.innerHTML = `<div class="bloom-canvas-element" data-bubble="${kOriginalBubble}"></div>`;
    document.body.appendChild(page);
    element = page.querySelector(".bloom-canvas-element")!;
    frames.element = element;
    frames.dataBubbleWhenRedrawn = [];
    history = createPageHistory({ root: page });
    frames.history = history;
});

afterEach(() => {
    history.dispose();
    frames.history = undefined;
    frames.element = undefined;
    document.body.innerHTML = "";
    vi.restoreAllMocks();
});

/** A change to the bubble that then fails, as a command might. */
function changeBubbleThenFail(): void {
    element.setAttribute("data-bubble", "{`version`:`1.0`,`style`:`speech`}");
    throw new Error("Test: the command failed part way");
}

describe("a canvas command that fails part way", () => {
    it("run by runCommandControlAction, has the bubbles drawn again once it is put back", async () => {
        const control = {
            kind: "command",
            id: "testCommand",
            l10nId: kTestDescription,
            englishLabel: "Test",
            action: changeBubbleThenFail,
        } as unknown as ICommandControlDefinition;

        await expect(
            runCommandControlAction(
                control,
                {} as IControlContext,
                {} as IControlRuntime,
            ),
        ).rejects.toThrow("Test: the command failed part way");

        expect(element.getAttribute("data-bubble")).toBe(kOriginalBubble);
        expect(frames.dataBubbleWhenRedrawn).toEqual([kOriginalBubble]);
        expect(history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("run by runControlCallback, has the bubbles drawn again once it is put back", async () => {
        const consoleError = vi
            .spyOn(console, "error")
            .mockImplementation(() => {});

        runControlCallback("test", kTestDescription, changeBubbleThenFail);
        // runControlCallback reports the failure once the transaction has been put back.
        await vi.waitFor(() => expect(consoleError).toHaveBeenCalled());

        expect(element.getAttribute("data-bubble")).toBe(kOriginalBubble);
        expect(frames.dataBubbleWhenRedrawn).toEqual([kOriginalBubble]);
        expect(history.undoDescriptionL10nKey).toBeUndefined();
    });
});

// A command can start a change that it does not await, such as fitting a canvas element to a new
// picture once the picture has loaded, which holds a page content delay until then. The command's
// step stays open until the delay clears, so that the change is in the step.
describe("a canvas command that starts a change that waits", () => {
    const kChangedBubble = "{`version`:`1.0`,`style`:`speech`}";
    const kDelayId = "testFitToNewPicture";
    // Makes the change that the command started, and releases its delay.
    let finishChange: (() => void) | undefined;

    beforeEach(() => {
        finishChange = undefined;
    });

    /** What the command does: change the bubble, and start a change that waits. */
    function changeBubbleAndStartFit(): void {
        element.setAttribute("data-bubble", kChangedBubble);
        addRequestPageContentDelay(kDelayId);
        finishChange = () => {
            // Like the fit of a background image, this is made outside any call into the
            // history, after the command's own code has returned.
            element.style.width = "160px";
            removeRequestPageContentDelay(kDelayId);
        };
    }

    /** Check that the command's change and the change that waited are one undo step. */
    async function expectOneStepWithTheChangeThatWaited(): Promise<void> {
        expect(history.hasOpenTransaction).toBe(true);
        finishChange!();
        await vi.waitFor(() => expect(history.hasOpenTransaction).toBe(false));
        expect(history.undoDescriptionL10nKey).toBe(kTestDescription);

        await history.undo();

        expect(element.getAttribute("data-bubble")).toBe(kOriginalBubble);
        expect(element.style.width).toBe("");
        expect(history.undoDescriptionL10nKey).toBeUndefined();
    }

    it("run by runCommandControlAction", async () => {
        const control = {
            kind: "command",
            id: "testCommand",
            l10nId: kTestDescription,
            englishLabel: "Test",
            action: changeBubbleAndStartFit,
        } as unknown as ICommandControlDefinition;

        const done = runCommandControlAction(
            control,
            {} as IControlContext,
            {} as IControlRuntime,
        );
        await vi.waitFor(() => expect(finishChange).toBeDefined());

        await expectOneStepWithTheChangeThatWaited();
        await done;
    });

    it("run by runControlCallback", async () => {
        runControlCallback("test", kTestDescription, changeBubbleAndStartFit);
        await vi.waitFor(() => expect(finishChange).toBeDefined());

        await expectOneStepWithTheChangeThatWaited();
    });
});
