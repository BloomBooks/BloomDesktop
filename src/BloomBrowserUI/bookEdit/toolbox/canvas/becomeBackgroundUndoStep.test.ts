// Become Background on a canvas whose background has no real picture moves the overlay's picture
// into the background, deletes the overlay, and then, once the selection has settled, makes the
// background fill the canvas. That fill is part of the command, so it must land inside the
// command's undo step. A change after the step closes is a change outside any transaction: the
// page history adds it to the latest step and drops the redo steps.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { PageHistory } from "../../pageHistory/pageHistoryTypes";
import type { IControlRuntime } from "./canvasControlTypes";

const access = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
}));

vi.mock("../../pageHistory/pageHistoryAccess", () => ({
    currentPageHistory: () => access.history,
    doOutsideHistory: <T>(fn: () => T): T =>
        access.history ? access.history.doOutsideHistory(fn) : fn(),
}));

// What the fill saw: whether a transaction was open when it ran.
const fill = vi.hoisted(() => ({
    ranInsideTransaction: [] as boolean[],
}));

// The canvas element manager, reduced to what Become Background calls. Its fill makes a change
// that the page history keeps, as the real one does to the background's size.
vi.mock("./canvasElementPageBridge", async (importOriginal) => {
    const original =
        await importOriginal<typeof import("./canvasElementPageBridge")>();
    const manager = {
        turnOnCanvasElementEditing: () => {},
        deleteCurrentCanvasElement: () =>
            document.getElementById("overlay")!.remove(),
        updateCanvasElementForChangedImage: () => {},
        setActiveElement: () => {},
        canExpandToFillSpace: () => true,
        expandImageToFillSpace: () => {
            fill.ranInsideTransaction.push(access.history!.hasOpenTransaction);
            document
                .querySelector<HTMLElement>(".bloom-backgroundImage")!
                .style.setProperty("width", "500px");
        },
    };
    return { ...original, getCanvasElementManager: () => manager };
});

import { startPageHistoryForThisPage } from "../../pageHistory/pageHistorySetup";
import { runControlCallback } from "../../js/canvasElementManager/canvasControlMenuRendering";
import { buildCanvasElementControlRegistryContext } from "./buildCanvasElementControlRegistryContext";
import { controlRegistry } from "./canvasControlRegistry";

let page: HTMLElement;

beforeEach(() => {
    fill.ranInsideTransaction = [];
    page = document.createElement("div");
    page.className = "bloom-page";
    page.innerHTML =
        '<div class="bloom-canvas">' +
        '<div class="bloom-canvas-element bloom-backgroundImage" style="width: 200px">' +
        '<div class="bloom-imageContainer"><img src="placeHolder.png"></div></div>' +
        '<div class="bloom-canvas-element" id="overlay">' +
        '<div class="bloom-imageContainer"><img src="bird.png"></div></div>' +
        "</div>";
    document.body.appendChild(page);
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

describe("Become Background on a canvas with no real background picture", () => {
    it("fills the canvas inside the command's own undo step", async () => {
        const history = access.history!;
        const background = page.querySelector<HTMLElement>(
            ".bloom-backgroundImage",
        )!;
        const ctx = buildCanvasElementControlRegistryContext(
            document.getElementById("overlay")!,
        );
        const command = controlRegistry.becomeBackground;
        if (command.kind !== "command")
            throw new Error("becomeBackground is no longer a command");

        // As a menu row runs it: through runControlCallback, with no press before the click.
        runControlCallback("menu:becomeBackground", () =>
            command.action(ctx, { closeMenu: () => {} } as IControlRuntime),
        );
        await vi.waitFor(() =>
            expect(fill.ranInsideTransaction.length).toBe(1),
        );
        await vi.waitFor(() => expect(history.hasOpenTransaction).toBe(false));
        // Sanity check: the command did its work.
        expect(document.getElementById("overlay")).toBeNull();
        expect(background.querySelector("img")!.getAttribute("src")).toBe(
            "bird.png",
        );
        expect(background.style.width).toBe("500px");

        expect(
            fill.ranInsideTransaction[0],
            "the fill ran after the command's undo step had closed",
        ).toBe(true);

        // One undo takes back the whole command, the fill with it.
        await history.undo();
        expect(background.style.width).toBe("200px");
        expect(background.querySelector("img")!.getAttribute("src")).toBe(
            "placeHolder.png",
        );
        expect(document.getElementById("overlay")).not.toBeNull();
    });
});
