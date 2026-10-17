// Tests that adding a canvas element (CanvasElementManager.addCanvasElement, which the Canvas
// tool's palette calls at the end of a drag) is one undo step of its own, with a real page history
// and the real edit-time markup filter, wired to the manager by listenToPageHistory as in the
// Edit tab.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// Comical wants paper.js and a real <canvas>, which jsdom doesn't give us. Adding and selecting an
// element only asks Comical to draw the bubbles, activate the element, find its family, read its
// bubble style and tails, and store a default bubble spec in data-bubble, as the real one does.
vi.mock("comicaljs", () => ({
    Bubble: class {
        public constructor(private readonly content: HTMLElement) {}
        public static getBubbleSpec() {
            return { style: "none", tails: [] };
        }
        public static getDefaultBubbleSpec(
            _element: HTMLElement,
            style: string,
        ) {
            return { version: "1.0", style, tails: [], level: 1 };
        }
        public getBubbleSpec() {
            return { style: "none", tails: [] };
        }
        public setBubbleSpec(spec: object) {
            this.content.setAttribute("data-bubble", JSON.stringify(spec));
        }
        public persistBubbleSpec() {
            // setBubbleSpec has already written data-bubble.
        }
    },
    Node: vi.fn(),
    Parser: vi.fn(),
    BubbleSpec: {},
    TailSpec: {},
    Comical: {
        setSelectorForBubblesWhichTailMidpointMayOverlap: () => {},
        setUserInterfaceProperties: () => {},
        activateBubble: () => {},
        activateElement: () => {},
        update: () => {},
        startEditing: () => {},
        findAncestors: () => [],
    },
}));

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
import {
    initializeCanvasElementManager,
    theOneCanvasElementManager,
} from "./CanvasElementManager";
import { listenToPageHistory } from "../bloomEditing";
import { createPageHistory } from "../../pageHistory/pageHistory";
import { createEditTimeMarkupFilter } from "../../pageHistory/editTimeMarkup";
import { attachInputTransactions } from "../../pageHistory/pageHistoryInput";
import {
    kGenericUndoDescriptionL10nKey,
    type PageHistory,
} from "../../pageHistory/pageHistoryTypes";
import { kAddCanvasElementUndoL10nKey } from "../../toolbox/canvas/canvasElementConstants";

const kMoveDescription = "EditTab.Undo.Move";

let page: HTMLElement;
let history: PageHistory;

beforeEach(() => {
    document.body.innerHTML = `
        <div class="bloom-page" id="page">
            <div class="bloom-canvas bloom-has-canvas-element" id="canvas">
                <div class="bloom-canvas-element bloom-backgroundImage" id="background" style="left: 0px; top: 0px; width: 300px; height: 200px;">
                    <div class="bloom-imageContainer"><img src="placeHolder.png" alt=""></div>
                </div>
                <div class="bloom-canvas-element" id="ce1" style="left: 10px; top: 10px; width: 50px; height: 30px;">
                    <div class="bloom-translationGroup">
                        <div class="bloom-editable bloom-visibility-code-on" lang="en"><p>One</p></div>
                    </div>
                </div>
            </div>
        </div>`;
    page = document.getElementById("page")!;
    // jsdom does no layout, so it has no elementsFromPoint; the drop point is always on the
    // bloom-canvas.
    document.elementsFromPoint = () => [document.getElementById("canvas")!];
    initializeCanvasElementManager();
    theOneCanvasElementManager.isCanvasElementEditingOn = true;
    // Setting up the new element's editing (CKEditor, the edit key handlers) needs the whole
    // editor, and it is not what these tests are about.
    theOneCanvasElementManager.refreshCanvasElementEditing = () => {};
    history = createPageHistory({
        root: page,
        filter: createEditTimeMarkupFilter(),
    });
    pageFrame.history = history;
    listenToPageHistory(history);
    history.doOutsideHistory(() =>
        theOneCanvasElementManager.setActiveElement(undefined),
    );
});

afterEach(() => {
    pageFrame.history = undefined;
    history.dispose();
    document.body.innerHTML = "";
});

// The ids of the canvas elements on the page, with "(new)" for the one that has none, sorted:
// where the add puts the new element among the others is not what these tests are about.
function canvasElementIds(): string[] {
    return Array.from(
        document.querySelectorAll<HTMLElement>(".bloom-canvas-element"),
    )
        .map((element) => element.id || "(new)")
        .sort();
}

// What the Canvas tool's palette does at the end of a drag.
function addRectangle(): HTMLElement {
    const added = theOneCanvasElementManager.addCanvasElement(
        20,
        20,
        "rectangle",
    );
    if (!added) throw new Error("Test setup: the add made no canvas element");
    return added;
}

// A MutationObserver delivers its records in a microtask, so by the time this resolves the
// history has every change made before the call.
async function letObserverDeliver(): Promise<void> {
    await Promise.resolve();
}

describe("adding a canvas element", () => {
    it("is one undo step; undo removes it, and redo puts it back and selects it", async () => {
        expect(history.undoDescriptionL10nKey).toBeUndefined();

        const added = addRectangle();
        await letObserverDeliver();

        expect(added.isConnected).toBe(true);
        expect(canvasElementIds()).toEqual(["(new)", "background", "ce1"]);
        expect(history.undoDescriptionL10nKey).toBe(
            kAddCanvasElementUndoL10nKey,
        );

        await history.undo();

        expect(added.isConnected).toBe(false);
        expect(canvasElementIds()).toEqual(["background", "ce1"]);
        // The add was the only step.
        expect(history.undoDescriptionL10nKey).toBeUndefined();
        expect(theOneCanvasElementManager.getActiveElement()).toBeUndefined();
        expect(history.redoDescriptionL10nKey).toBe(
            kAddCanvasElementUndoL10nKey,
        );

        await history.redo();

        expect(added.isConnected).toBe(true);
        expect(theOneCanvasElementManager.getActiveElement()).toBe(added);
        expect(history.redoDescriptionL10nKey).toBeUndefined();
    });

    it("after a move, is a step of its own, so that undo takes out only the add", async () => {
        const ce1 = document.getElementById("ce1")!;
        await history.doTransaction(kMoveDescription, () => {
            ce1.style.left = "40px";
        });

        const added = addRectangle();
        await letObserverDeliver();

        await history.undo();

        expect(added.isConnected).toBe(false);
        // The move is still done, and is the next step to undo.
        expect(ce1.style.left).toBe("40px");
        expect(history.undoDescriptionL10nKey).toBe(kMoveDescription);

        await history.undo();

        expect(ce1.style.left).toBe("10px");
        expect(history.undoDescriptionL10nKey).toBeUndefined();

        await history.redo();
        await history.redo();

        expect(ce1.style.left).toBe("40px");
        expect(added.isConnected).toBe(true);
        expect(history.redoDescriptionL10nKey).toBeUndefined();
    });

    it("inside a transaction that is open, joins it with the changes made after it", async () => {
        // As the palette's ondragend does: the add, then its own changes to the new element.
        let added: HTMLElement | undefined;
        await history.doTransaction(kGenericUndoDescriptionL10nKey, () => {
            added = addRectangle();
            added.classList.add("bloom-from-palette-test");
        });
        await letObserverDeliver();

        await history.undo();

        expect(added!.isConnected).toBe(false);
        expect(history.undoDescriptionL10nKey).toBeUndefined();

        await history.redo();

        expect(added!.isConnected).toBe(true);
        expect(added!.classList.contains("bloom-from-palette-test")).toBe(true);
    });

    // The order of events of a drag from the toolbox: a press on the palette item in the
    // toolbox, then pointercancel as the drag begins (HTML's drag-and-drop processing model),
    // then, in later tasks, drop in the page frame and dragend in the toolbox, where the add
    // happens. A step that a press in another frame made with only the general description
    // would end the history (pageHistoryInput.ts); this checks that the add does not.
    it("at the end of a drag from the toolbox, makes a step and keeps the history", async () => {
        const ce1 = document.getElementById("ce1")!;
        await history.doTransaction(kMoveDescription, () => {
            ce1.style.left = "40px";
        });
        const toolboxFrame = document.createElement("iframe");
        document.body.appendChild(toolboxFrame);
        const toolboxWindow = toolboxFrame.contentWindow!;
        const tasks: Array<() => void> = [];
        const detach = attachInputTransactions(
            history,
            window,
            [toolboxWindow],
            {
                isFromUser: () => true,
                afterCurrentTask: (fn) => tasks.push(fn),
            },
        );
        const finishTask = () => tasks.splice(0).forEach((fn) => fn());
        try {
            toolboxWindow.document.body.dispatchEvent(
                new toolboxWindow.Event("pointerdown", { bubbles: true }),
            );
            expect(history.hasOpenTransaction).toBe(true);
            toolboxWindow.document.body.dispatchEvent(
                new toolboxWindow.Event("pointercancel", { bubbles: true }),
            );
            finishTask();
            expect(history.hasOpenTransaction).toBe(false);

            page.dispatchEvent(new Event("drop", { bubbles: true }));
            finishTask();

            const added = addRectangle();
            await letObserverDeliver();

            // The move is still there to undo after the add: the history was not ended.
            expect(history.undoDescriptionL10nKey).toBe(
                kAddCanvasElementUndoL10nKey,
            );
            await history.undo();
            expect(added.isConnected).toBe(false);
            expect(history.undoDescriptionL10nKey).toBe(kMoveDescription);
        } finally {
            detach();
            toolboxFrame.remove();
        }
    });
});
