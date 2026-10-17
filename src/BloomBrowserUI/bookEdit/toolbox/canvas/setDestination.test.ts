// The Set Destination command of a navigation button, as an undo step of the page history. The
// command shows the link target chooser in the root window, and the destination changes when the
// user clicks OK there, after the command's own step has closed. The tests attach the input
// transactions with this window, where the OK button is, as one of the other windows, and an empty
// frame as the page's. As in pageHistoryInputSpec.ts, the user's events are the ones in userEvents,
// and a test says where the browser's task ends by calling finishTask().
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createPageHistory } from "../../pageHistory/pageHistory";
import { attachInputTransactions } from "../../pageHistory/pageHistoryInput";
import type { PageHistory } from "../../pageHistory/pageHistoryTypes";

const frames = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
    onSetUrl: undefined as ((url: string) => void) | undefined,
}));
// The registry reaches the page history through the page frame's exports (pageHistoryAccess.ts),
// and shows the link target chooser through the root window's. Set Destination needs no
// CanvasElementManager.
vi.mock("../../js/workspaceFrames", async (importOriginal) => ({
    ...(await importOriginal<object>()),
    getEditablePageBundleExports: () =>
        frames.history
            ? {
                  pageHistory: () => frames.history,
                  getTheOneCanvasElementManager: () => undefined,
              }
            : null,
    getWorkspaceBundleExports: () => ({
        showLinkTargetChooserDialog: (
            _currentUrl: string,
            onSetUrl: (url: string) => void,
        ) => (frames.onSetUrl = onSetUrl),
    }),
}));

import { buildCanvasElementControlRegistryContext } from "./buildCanvasElementControlRegistryContext";
import {
    controlRegistry,
    runCommandControlAction,
} from "./canvasControlRegistry";
import type { ICommandControlDefinition } from "./canvasControlTypes";

const kSetDestKey = "EditTab.Toolbox.CanvasTool.SetDest";
const kEarlierKey = "EditTab.Toolbox.ComicTool.Options.ElementType";

let history: PageHistory;
let canvasElement: HTMLElement;
let other: HTMLElement;
let okButton: HTMLButtonElement;
let okUrl: string;
let detach: () => void;
let userEvents: WeakSet<Event>;
let endOfTask: (() => void)[];
let onUnmarkedChange: ReturnType<typeof vi.fn>;

beforeEach(async () => {
    const page = document.createElement("div");
    page.className = "bloom-page";
    page.innerHTML =
        '<div class="bloom-canvas-element bloom-canvas-button" data-href="#page1"></div><div id="other"></div>';
    document.body.appendChild(page);
    canvasElement = page.querySelector(".bloom-canvas-element")!;
    other = page.querySelector("#other")!;
    onUnmarkedChange = vi.fn();
    history = createPageHistory({ root: page, onUnmarkedChange });
    frames.history = history;
    // A step before the new destination, which must survive it.
    await history.doTransaction(kEarlierKey, () =>
        other.setAttribute("data-earlier", "true"),
    );
    expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);

    const pageFrame = document.createElement("iframe");
    document.body.appendChild(pageFrame);
    userEvents = new WeakSet();
    endOfTask = [];
    detach = attachInputTransactions(
        history,
        pageFrame.contentWindow!,
        [window],
        {
            isFromUser: (event) => userEvents.has(event),
            afterCurrentTask: (fn) => endOfTask.push(fn),
        },
    );
    // What LinkTargetChooserDialog's OK does: call onSetUrl in the click's handler.
    okButton = document.createElement("button");
    okButton.addEventListener("click", () => frames.onSetUrl!(okUrl));
    document.body.appendChild(okButton);
});

afterEach(() => {
    detach();
    history.dispose();
    frames.history = undefined;
    frames.onSetUrl = undefined;
    document.body.innerHTML = "";
});

function finishTask(): void {
    while (endOfTask.length > 0) endOfTask.shift()!();
}

function mouse(target: Element, type: string, buttons: number): void {
    const event = new MouseEvent(type, {
        bubbles: true,
        cancelable: true,
        buttons,
    });
    userEvents.add(event);
    target.dispatchEvent(event);
}

/** A click on OK by the user: the press, then the release as one task, and the end of that task. */
function clickOk(url: string): void {
    okUrl = url;
    mouse(okButton, "pointerdown", 1);
    mouse(okButton, "mousedown", 1);
    mouse(okButton, "pointerup", 0);
    mouse(okButton, "mouseup", 0);
    mouse(okButton, "click", 0);
    finishTask();
}

/** Choose Set Destination for the canvas element, which shows the link target chooser. */
async function chooseSetDestination(): Promise<void> {
    await runCommandControlAction(
        controlRegistry.setDestination as ICommandControlDefinition,
        buildCanvasElementControlRegistryContext(canvasElement),
        { closeMenu: () => {} },
    );
    if (!frames.onSetUrl)
        fail("Set Destination showed no link target chooser.");
    // Showing the dialog changed nothing.
    expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
}

describe("Set Destination", () => {
    it("makes a new destination one step, which undo and redo reverse, after the earlier step", async () => {
        await chooseSetDestination();

        clickOk("#page2");

        expect(canvasElement.getAttribute("data-href")).toBe("#page2");
        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(kSetDestKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();

        await history.undo();
        expect(canvasElement.getAttribute("data-href")).toBe("#page1");
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
        expect(history.redoDescriptionL10nKey).toBe(kSetDestKey);

        await history.redo();
        expect(canvasElement.getAttribute("data-href")).toBe("#page2");

        await history.undo();
        await history.undo();
        expect(other.hasAttribute("data-earlier")).toBe(false);
    });

    it("makes removing the destination one step, which undo reverses", async () => {
        await chooseSetDestination();

        clickOk("");

        expect(canvasElement.hasAttribute("data-href")).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(kSetDestKey);
        await history.undo();
        expect(canvasElement.getAttribute("data-href")).toBe("#page1");
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
    });
});
