// Tests that each layer command (Bring Forward, Bring to Front, Send Backward, Send to Back) is one
// undo step, named by the command, whether it comes from the Layer submenu or a keyboard shortcut,
// and that undo and redo put back the stacking order of the canvas elements and the levels in
// their data-bubble specs together, redraw the bubbles and select the moved element. Uses a real
// page history and the real edit-time markup filter, wired to the canvas element manager by
// listenToPageHistory as in the Edit tab.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// Comical wants paper.js and a real <canvas>, which jsdom doesn't give us. The layer commands read
// and write each element's spec in data-bubble, as JSON with backticks for quotes, exactly as the
// real Bubble does, and ask Comical to draw again; selecting an element asks it to activate it.
vi.mock("comicaljs", () => {
    interface ISpec {
        style: string;
        tails: unknown[];
        level?: number;
        order?: number;
    }
    function readSpec(element: HTMLElement): ISpec {
        const raw = element.getAttribute("data-bubble");
        return raw
            ? JSON.parse(raw.replace(/`/g, '"'))
            : { style: "none", tails: [] };
    }
    class Bubble {
        private spec: ISpec;
        constructor(private element: HTMLElement) {
            this.spec = readSpec(element);
        }
        public static getBubbleSpec(element: HTMLElement): ISpec {
            return readSpec(element);
        }
        public getBubbleSpec(): ISpec {
            return this.spec;
        }
        public persistBubbleSpec(): void {
            this.element.setAttribute(
                "data-bubble",
                JSON.stringify(this.spec).replace(/"/g, "`"),
            );
        }
    }
    return {
        Bubble,
        Node: vi.fn(),
        Parser: vi.fn(),
        BubbleSpec: {},
        TailSpec: {},
        Comical: {
            setSelectorForBubblesWhichTailMidpointMayOverlap: () => {},
            activateElement: vi.fn(),
            update: vi.fn(),
            startEditing: vi.fn(),
            findAncestors: () => [],
        },
    };
});

// In the Edit tab, code in the page frame finds the canvas element manager and the page history
// through the page frame's bundle exports; give it this test's.
const pageFrame = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
}));
vi.mock("../workspaceFrames", async (importOriginal) => ({
    ...(await importOriginal<typeof import("../workspaceFrames")>()),
    getEditablePageBundleExports: () => ({
        getTheOneCanvasElementManager: () => theOneCanvasElementManager,
        // Like the real one, this throws when no history has been started.
        pageHistory: () => {
            if (!pageFrame.history) throw new Error("No page history");
            return pageFrame.history;
        },
    }),
}));

// These imports deliberately come after the vi.mock calls above, so that the module graph they
// pull in gets the stubbed modules.
import { Comical } from "comicaljs";
import {
    initializeCanvasElementManager,
    theOneCanvasElementManager,
} from "./CanvasElementManager";
import { listenToPageHistory } from "../bloomEditing";
import { createPageHistory } from "../../pageHistory/pageHistory";
import { createEditTimeMarkupFilter } from "../../pageHistory/editTimeMarkup";
import {
    kGenericUndoDescriptionL10nKey,
    type PageHistory,
} from "../../pageHistory/pageHistoryTypes";
import { kZOrderMoveL10nIds } from "./CanvasElementZOrder";
import { makeLayerMenuItem } from "../../toolbox/canvas/canvasControlRegistry";
import { convertControlMenuRows } from "./canvasControlMenuRendering";
import type {
    IControlContext,
    IControlRuntime,
} from "../../toolbox/canvas/canvasControlTypes";

let history: PageHistory;
let onUnmarkedChange: ReturnType<typeof vi.fn<(whatChanged: string) => void>>;

// A canvas element holding some text, with the given bubble level and, for a member of a
// ComicalJS family, its order in the family.
function canvasElementHtml(id: string, level: number, order?: number): string {
    const spec: Record<string, unknown> = {
        version: "1.0",
        style: "none",
        tails: [],
        level,
    };
    if (order !== undefined) spec.order = order;
    const bubble = JSON.stringify(spec).replace(/"/g, "`");
    return `
        <div class="bloom-canvas-element" id="${id}" data-bubble="${bubble}" style="left: 10px; top: 10px; width: 50px; height: 30px;">
            <div class="bloom-translationGroup">
                <div class="bloom-editable bloom-visibility-code-on" lang="en"><p>${id}</p></div>
            </div>
        </div>`;
}

// Make the page with one bloom-canvas, Comical's drawing surface first, then a background image
// at level 1, then the given canvas elements in their stacking order, bottom-most first.
function makePage(canvasElementsHtml: string): void {
    document.body.innerHTML = `
        <div class="bloom-page" id="page">
            <div class="bloom-canvas" id="canvas">
                <canvas class="comical-generated"></canvas>
                <div class="bloom-canvas-element bloom-backgroundImage" id="background" data-bubble="{\`version\`:\`1.0\`,\`style\`:\`none\`,\`tails\`:[],\`level\`:1}" style="left: 0px; top: 0px; width: 300px; height: 200px;">
                    <div class="bloom-imageContainer"><img src="a.png" alt=""></div>
                </div>
                ${canvasElementsHtml}
            </div>
        </div>`;
    initializeCanvasElementManager();
    theOneCanvasElementManager.isCanvasElementEditingOn = true;
    onUnmarkedChange = vi.fn<(whatChanged: string) => void>();
    history = createPageHistory({
        root: byId("page"),
        filter: createEditTimeMarkupFilter(),
        onUnmarkedChange,
    });
    pageFrame.history = history;
    listenToPageHistory(history);
    select(undefined);
}

// Three text canvas elements above the background image, with levels that match their order.
beforeEach(() => {
    makePage(
        canvasElementHtml("a", 2) +
            canvasElementHtml("b", 3) +
            canvasElementHtml("c", 4),
    );
});

afterEach(() => {
    pageFrame.history = undefined;
    history.dispose();
    document.body.innerHTML = "";
    vi.clearAllMocks();
});

function byId(id: string): HTMLElement {
    const element = document.getElementById(id);
    if (!element) throw new Error(`Test setup: no element with id ${id}`);
    return element;
}

// Select an element as a click does. A click is not an undo step, so it is made outside the
// history, as the stand-in transaction that Bloom opens on a click makes no step for it.
function select(element: HTMLElement | undefined): void {
    history.doOutsideHistory(() =>
        theOneCanvasElementManager.setActiveElement(element),
    );
}

function activeElement(): HTMLElement | undefined {
    return theOneCanvasElementManager.getActiveElement();
}

/** The ids of the canvas elements in the bloom-canvas, in their stacking order, bottom-most first. */
function stackingOrder(): string[] {
    return Array.from(byId("canvas").children)
        .filter((child) => child.classList.contains("bloom-canvas-element"))
        .map((child) => child.id);
}

/** The level in each canvas element's data-bubble, by id. */
function levels(): Record<string, number> {
    const result: Record<string, number> = {};
    stackingOrder().forEach((id) => {
        const raw = byId(id).getAttribute("data-bubble")!;
        result[id] = JSON.parse(raw.replace(/`/g, '"')).level;
    });
    return result;
}

// Press a layer shortcut on the selected canvas element, as the user does. In Bloom the key press
// first opens a stand-in transaction with the general description (pageHistoryInput.ts), which
// ends once the task is over; the layer command nests inside it.
function pressLayerShortcut(key: "]" | "[", shift: boolean): void {
    const standIn = history.beginTransaction(kGenericUndoDescriptionL10nKey);
    activeElement()!.dispatchEvent(
        new KeyboardEvent("keydown", {
            key,
            code: key === "]" ? "BracketRight" : "BracketLeft",
            ctrlKey: true,
            shiftKey: shift,
            bubbles: true,
        }),
    );
    standIn.end();
}

// Choose a command of the Layer submenu, through the same conversion and click handler as the
// canvas element's menu (CanvasElementContextControls).
async function chooseFromLayerMenu(
    rowId: "bringForward" | "bringToFront" | "sendBackward" | "sendToBack",
): Promise<void> {
    const runtime = { closeMenu: vi.fn() } as unknown as IControlRuntime;
    const ctx = {} as IControlContext;
    const [layerRow] = convertControlMenuRows(
        [makeLayerMenuItem(ctx, runtime)],
        ctx,
        runtime,
    );
    const rowIndex = [
        "bringForward",
        "bringToFront",
        "sendBackward",
        "sendToBack",
    ].indexOf(rowId);
    // The handler does not look at the click event.
    layerRow.subMenu![rowIndex].onClick(undefined as never);
    // runControlCallback runs the command in a transaction whose promise it does not return.
    await vi.waitFor(() => expect(history.hasOpenTransaction).toBe(false));
}

describe("a layer command is one undo step", () => {
    it("Bring Forward from its shortcut: undo puts back the order and levels, and redo moves it again", async () => {
        const a = byId("a");
        select(a);
        // Sanity check of the starting state.
        expect(stackingOrder()).toEqual(["background", "a", "b", "c"]);
        expect(levels()).toEqual({ background: 1, a: 2, b: 3, c: 4 });

        pressLayerShortcut("]", false);

        expect(stackingOrder()).toEqual(["background", "b", "a", "c"]);
        expect(levels()).toEqual({ background: 1, b: 2, a: 3, c: 4 });
        expect(history.undoDescriptionL10nKey).toBe(kZOrderMoveL10nIds.forward);
        vi.mocked(Comical.update).mockClear();

        await history.undo();

        expect(stackingOrder()).toEqual(["background", "a", "b", "c"]);
        expect(levels()).toEqual({ background: 1, a: 2, b: 3, c: 4 });
        expect(Comical.update).toHaveBeenCalledWith(byId("canvas"));
        expect(activeElement()).toBe(a);
        expect(history.undoDescriptionL10nKey).toBeUndefined();
        expect(history.redoDescriptionL10nKey).toBe(kZOrderMoveL10nIds.forward);
        vi.mocked(Comical.update).mockClear();

        await history.redo();

        expect(stackingOrder()).toEqual(["background", "b", "a", "c"]);
        expect(levels()).toEqual({ background: 1, b: 2, a: 3, c: 4 });
        expect(Comical.update).toHaveBeenCalledWith(byId("canvas"));
        expect(activeElement()).toBe(a);
        expect(history.undoDescriptionL10nKey).toBe(kZOrderMoveL10nIds.forward);
        expect(history.redoDescriptionL10nKey).toBeUndefined();
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });

    it("Send to Back from its shortcut: undo and redo reverse it exactly", async () => {
        const c = byId("c");
        select(c);

        pressLayerShortcut("[", true);

        expect(stackingOrder()).toEqual(["background", "c", "a", "b"]);
        expect(levels()).toEqual({ background: 1, c: 2, a: 3, b: 4 });
        expect(history.undoDescriptionL10nKey).toBe(kZOrderMoveL10nIds.back);

        await history.undo();

        expect(stackingOrder()).toEqual(["background", "a", "b", "c"]);
        expect(levels()).toEqual({ background: 1, a: 2, b: 3, c: 4 });
        expect(activeElement()).toBe(c);

        await history.redo();

        expect(stackingOrder()).toEqual(["background", "c", "a", "b"]);
        expect(levels()).toEqual({ background: 1, c: 2, a: 3, b: 4 });
        expect(activeElement()).toBe(c);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });

    it("Bring to Front from the Layer menu: one step with the command's name, which undo and redo reverse", async () => {
        const a = byId("a");
        select(a);

        await chooseFromLayerMenu("bringToFront");

        expect(stackingOrder()).toEqual(["background", "b", "c", "a"]);
        expect(levels()).toEqual({ background: 1, b: 2, c: 3, a: 4 });
        expect(history.undoDescriptionL10nKey).toBe(kZOrderMoveL10nIds.front);

        await history.undo();

        expect(stackingOrder()).toEqual(["background", "a", "b", "c"]);
        expect(levels()).toEqual({ background: 1, a: 2, b: 3, c: 4 });
        expect(activeElement()).toBe(a);
        expect(history.undoDescriptionL10nKey).toBeUndefined();

        await history.redo();

        expect(stackingOrder()).toEqual(["background", "b", "c", "a"]);
        expect(activeElement()).toBe(a);
        expect(history.redoDescriptionL10nKey).toBeUndefined();
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });

    it("Send Backward from the Layer menu: one step with the command's name", async () => {
        const b = byId("b");
        select(b);

        await chooseFromLayerMenu("sendBackward");

        expect(stackingOrder()).toEqual(["background", "b", "a", "c"]);
        expect(history.undoDescriptionL10nKey).toBe(
            kZOrderMoveL10nIds.backward,
        );

        await history.undo();

        expect(stackingOrder()).toEqual(["background", "a", "b", "c"]);
        expect(levels()).toEqual({ background: 1, a: 2, b: 3, c: 4 });
        expect(activeElement()).toBe(b);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });

    it("two moves are two steps, undone one at a time", async () => {
        select(byId("a"));
        pressLayerShortcut("]", false);
        pressLayerShortcut("]", false);
        expect(stackingOrder()).toEqual(["background", "b", "c", "a"]);

        await history.undo();

        expect(stackingOrder()).toEqual(["background", "b", "a", "c"]);
        expect(levels()).toEqual({ background: 1, b: 2, a: 3, c: 4 });
        expect(history.undoDescriptionL10nKey).toBe(kZOrderMoveL10nIds.forward);

        await history.undo();

        expect(stackingOrder()).toEqual(["background", "a", "b", "c"]);
        expect(history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("a shortcut that cannot move the element makes no step", () => {
        select(byId("c"));

        pressLayerShortcut("]", false);

        expect(stackingOrder()).toEqual(["background", "a", "b", "c"]);
        expect(history.undoDescriptionL10nKey).toBeUndefined();
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });
});

describe("moving past a ComicalJS family", () => {
    // A parent bubble p and its child q share level 2; q was made after x, so the family straddles
    // x in the DOM. Moving x gathers the family; undo must put q back after x as well.
    beforeEach(() => {
        history.dispose();
        makePage(
            canvasElementHtml("p", 2, 1) +
                canvasElementHtml("x", 3) +
                canvasElementHtml("q", 2, 2),
        );
    });

    it("undo puts every member back where it was", async () => {
        const x = byId("x");
        select(x);

        pressLayerShortcut("[", false);

        expect(stackingOrder()).toEqual(["background", "x", "p", "q"]);
        expect(levels()).toEqual({ background: 1, x: 2, p: 3, q: 3 });
        expect(history.undoDescriptionL10nKey).toBe(
            kZOrderMoveL10nIds.backward,
        );

        await history.undo();

        expect(stackingOrder()).toEqual(["background", "p", "x", "q"]);
        expect(levels()).toEqual({ background: 1, p: 2, x: 3, q: 2 });
        expect(activeElement()).toBe(x);

        await history.redo();

        expect(stackingOrder()).toEqual(["background", "x", "p", "q"]);
        expect(levels()).toEqual({ background: 1, x: 2, p: 3, q: 3 });
        expect(activeElement()).toBe(x);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });
});
