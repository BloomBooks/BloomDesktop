// Showing image descriptions wraps the rest of each bloom-canvas in a bloom-describedImage, which
// the page history does not leave out of the child lists that it compares. These tests show that
// an undo or redo of a step that holds a bloom-canvas child list still works while the
// descriptions are shown, and that hiding them puts the children back in their order.
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
vi.mock("../canvas/canvasElementPageBridge", () => ({
    getCanvasElementManager: () => ({
        suspendComicEditing: () => undefined,
        resumeComicEditing: () => undefined,
    }),
}));

import {
    hideImageDescriptions,
    showImageDescriptions,
} from "./imageDescriptionUtils";

const kEarlierKey = "Test.EarlierStep";
const kAddKey = "Test.AddCanvasElement";

let history: PageHistory;
let onUnmarkedChange: ReturnType<typeof vi.fn>;
let body: HTMLElement;
let bloomCanvas: HTMLElement;

beforeEach(() => {
    body = document.createElement("div");
    // The description sits between the other children, and there is text between them, so that
    // a change of order shows.
    body.innerHTML = `<div class="bloom-page"><div class="bloom-canvas"><div id="background" class="bloom-canvas-element bloom-backgroundImage"><div class="bloom-imageContainer"><img src="a.png"></div></div>
<div id="description" class="bloom-translationGroup bloom-imageDescription"><div class="bloom-editable" lang="en">A dog.</div></div><div id="bubble" class="bloom-canvas-element"></div></div><div id="other"></div></div>`;
    document.body.appendChild(body);
    bloomCanvas = body.querySelector(".bloom-canvas")!;
    onUnmarkedChange = vi.fn();
    history = createPageHistory({
        root: body.querySelector(".bloom-page")!,
        filter: createEditTimeMarkupFilter(),
        onUnmarkedChange,
    });
    mocks.history = history;
});

afterEach(() => {
    hideImageDescriptions(body);
    history.dispose();
    mocks.history = undefined;
    body.remove();
});

function describedImage(): Element | null {
    return bloomCanvas.querySelector(":scope > .bloom-describedImage");
}

/** The ids of the bloom-canvas's element children, with "wrapper" for a bloom-describedImage. */
function childIds(): string[] {
    return Array.from(bloomCanvas.children).map((child) =>
        child.classList.contains("bloom-describedImage") ? "wrapper" : child.id,
    );
}

describe("Showing image descriptions and the page history", () => {
    it("lets an earlier step that added a canvas element be undone and redone while they show", async () => {
        await history.doTransaction(kAddKey, () => {
            const added = document.createElement("div");
            added.id = "added";
            added.className = "bloom-canvas-element";
            bloomCanvas.appendChild(added);
        });
        const nodesBefore = Array.from(bloomCanvas.childNodes);

        showImageDescriptions(body);
        expect(childIds()).toEqual(["description", "wrapper"]);

        await history.undo();
        expect(body.querySelector("#added")).toBeNull();
        expect(history.redoDescriptionL10nKey).toBe(kAddKey);
        // The descriptions still show, wrapped as before.
        expect(childIds()).toEqual(["description", "wrapper"]);
        expect(
            Array.from(describedImage()!.children).map((child) => child.id),
        ).toEqual(["background", "bubble"]);

        await history.redo();
        expect(describedImage()!.querySelector("#added")).not.toBeNull();
        expect(history.undoDescriptionL10nKey).toBe(kAddKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();

        hideImageDescriptions(body);
        expect(Array.from(bloomCanvas.childNodes)).toEqual(nodesBefore);
    });

    it("puts the children back in their order when they are hidden", () => {
        const nodesBefore = Array.from(bloomCanvas.childNodes);
        expect(childIds()).toEqual(["background", "description", "bubble"]);

        showImageDescriptions(body);
        hideImageDescriptions(body);

        expect(Array.from(bloomCanvas.childNodes)).toEqual(nodesBefore);
        expect(describedImage()).toBeNull();
    });

    it("wraps again after an undo that the history refuses", async () => {
        await history.doTransaction(kEarlierKey, () =>
            body.querySelector("#other")!.setAttribute("data-earlier", "1"),
        );
        // A change outside the history to the value that the step left makes its undo refused.
        history.doOutsideHistory(() =>
            body.querySelector("#other")!.setAttribute("data-earlier", "2"),
        );
        showImageDescriptions(body);

        await history.undo();

        expect(history.undoDescriptionL10nKey).toBeUndefined();
        expect(childIds()).toEqual(["description", "wrapper"]);
    });

    it("stops taking the wrappers away for an undo once the descriptions are hidden", async () => {
        await history.doTransaction(kEarlierKey, () =>
            body.querySelector("#other")!.setAttribute("data-earlier", "1"),
        );
        showImageDescriptions(body);
        hideImageDescriptions(body);
        const nodesBefore = Array.from(bloomCanvas.childNodes);

        await history.undo();

        expect(body.querySelector("#other")!.hasAttribute("data-earlier")).toBe(
            false,
        );
        expect(Array.from(bloomCanvas.childNodes)).toEqual(nodesBefore);
        expect(describedImage()).toBeNull();
    });
});
