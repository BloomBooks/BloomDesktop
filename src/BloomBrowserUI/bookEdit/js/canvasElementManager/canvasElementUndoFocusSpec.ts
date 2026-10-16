// Tests that the keyboard focus and the caret follow the selection after an undo or redo
// (CanvasElementManager.updateAfterUndoOrRedo): neither may be left in a canvas element that is
// not the active one, where typing would go into a bubble that the user cannot see is selected.
// Uses a real page history and the real edit-time markup filter, wired to the manager by
// listenToPageHistory as in the Edit tab.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// Comical wants paper.js and a real <canvas>, which jsdom doesn't give us. Selecting an element
// only asks Comical to activate it, draw the bubbles again, find the element's family, and read
// its bubble style and tails; these canvas elements have none.
vi.mock("comicaljs", () => ({
    Bubble: class {
        public static getBubbleSpec() {
            return { style: "none", tails: [] };
        }
        public getBubbleSpec() {
            return { style: "none", tails: [] };
        }
    },
    Node: vi.fn(),
    Parser: vi.fn(),
    BubbleSpec: {},
    TailSpec: {},
    Comical: {
        setSelectorForBubblesWhichTailMidpointMayOverlap: () => {},
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
import type { PageHistory } from "../../pageHistory/pageHistoryTypes";

let page: HTMLElement;
let history: PageHistory;

// A text box outside the bloom-canvas, and two text bubbles in it. The editables have tabindex
// because jsdom does not treat contenteditable as focusable.
beforeEach(() => {
    document.body.innerHTML = `
        <div class="bloom-page" id="page">
            <div class="bloom-translationGroup">
                <div class="bloom-editable bloom-visibility-code-on" contenteditable="true" tabindex="0" id="box" lang="en"><p id="para">Hello</p></div>
            </div>
            <div class="bloom-canvas" id="canvas">
                <div class="bloom-canvas-element" id="ce1" style="left: 10px; top: 10px; width: 50px; height: 30px;">
                    <div class="bloom-translationGroup">
                        <div class="bloom-editable bloom-visibility-code-on" contenteditable="true" tabindex="0" id="text1" lang="en"><p id="p1">One</p></div>
                    </div>
                </div>
                <div class="bloom-canvas-element" id="ce2" style="left: 100px; top: 10px; width: 50px; height: 30px;">
                    <div class="bloom-translationGroup">
                        <div class="bloom-editable bloom-visibility-code-on" contenteditable="true" tabindex="0" id="text2" lang="en"><p id="p2">Two</p></div>
                    </div>
                </div>
            </div>
        </div>`;
    page = document.getElementById("page")!;
    initializeCanvasElementManager();
    theOneCanvasElementManager.isCanvasElementEditingOn = true;
    history = createPageHistory({
        root: page,
        filter: createEditTimeMarkupFilter(),
    });
    pageFrame.history = history;
    listenToPageHistory(history);
    select(undefined);
});

afterEach(() => {
    pageFrame.history = undefined;
    history.dispose();
    window.getSelection()?.removeAllRanges();
    document.body.innerHTML = "";
});

function byId(id: string): HTMLElement {
    const element = document.getElementById(id);
    if (!element) throw new Error(`Test setup: no element with id ${id}`);
    return element;
}

// Select an element as a click does, outside the history: a click is not an undo step.
function select(element: HTMLElement | undefined): void {
    history.doOutsideHistory(() =>
        theOneCanvasElementManager.setActiveElement(element),
    );
}

// Put the focus and the caret in the text of the editable with this id, as typing there does.
function putCaretIn(editableId: string): void {
    const editable = byId(editableId);
    editable.focus();
    const text = editable.querySelector("p")!.firstChild!;
    window.getSelection()!.setBaseAndExtent(text, 1, text, 1);
}

// The canvas element that holds the keyboard focus, and the one that holds the caret, if any.
function focusAndCaretCanvasElements(): {
    focus: string | undefined;
    caret: string | undefined;
} {
    const canvasElementOf = (node: Node | null | undefined) => {
        const element =
            node?.nodeType === Node.ELEMENT_NODE
                ? (node as Element)
                : node?.parentElement;
        return element?.closest(".bloom-canvas-element")?.id;
    };
    return {
        focus: canvasElementOf(document.activeElement),
        caret: canvasElementOf(window.getSelection()?.focusNode),
    };
}

describe("the focus after an undo or redo", () => {
    it("after undoing an add, is in no canvas element", async () => {
        const ce1 = byId("ce1");
        select(ce1);
        putCaretIn("text1");
        const added = document.createElement("div");
        added.className = "bloom-canvas-element";
        added.id = "ce3";
        await history.doTransaction("EditTab.Undo.Add", () => {
            byId("canvas").appendChild(added);
            theOneCanvasElementManager.setActiveElement(added);
        });
        // Sanity check: the step began with the caret in ce1, which undo puts back.
        putCaretIn("text1");

        await history.undo();

        expect(added.isConnected).toBe(false);
        expect(theOneCanvasElementManager.getActiveElement()).toBeUndefined();
        expect(focusAndCaretCanvasElements()).toEqual({
            focus: undefined,
            caret: undefined,
        });
        expect(history.redoDescriptionL10nKey).toBe("EditTab.Undo.Add");
    });

    it("after redoing a delete, is in no canvas element", async () => {
        const ce1 = byId("ce1");
        select(ce1);
        await history.doTransaction("EditTab.Undo.Delete", () => {
            ce1.remove();
            theOneCanvasElementManager.setActiveElement(undefined);
            // Where the focus went after the delete, which redo puts back.
            putCaretIn("text2");
        });
        await history.undo();
        expect(theOneCanvasElementManager.getActiveElement()).toBe(ce1);

        await history.redo();

        expect(ce1.isConnected).toBe(false);
        expect(theOneCanvasElementManager.getActiveElement()).toBeUndefined();
        expect(focusAndCaretCanvasElements()).toEqual({
            focus: undefined,
            caret: undefined,
        });
        expect(history.undoDescriptionL10nKey).toBe("EditTab.Undo.Delete");
    });

    it("after undoing typing in the selected bubble, stays in that bubble", async () => {
        const ce1 = byId("ce1");
        select(ce1);
        putCaretIn("text1");
        const text = byId("p1").firstChild as Text;
        await history.doTransaction("EditTab.Undo.Typing", () => {
            text.data = "Once";
        });

        await history.undo();

        expect(text.data).toBe("One");
        expect(theOneCanvasElementManager.getActiveElement()).toBe(ce1);
        expect(focusAndCaretCanvasElements().caret).toBe("ce1");
        expect(history.redoDescriptionL10nKey).toBe("EditTab.Undo.Typing");
    });

    it("after undoing typing in a text box outside the canvas, stays in that box", async () => {
        select(byId("ce2"));
        putCaretIn("box");
        const text = byId("para").firstChild as Text;
        await history.doTransaction("EditTab.Undo.Typing", () => {
            text.data = "Hello there";
        });

        await history.undo();

        expect(text.data).toBe("Hello");
        expect(window.getSelection()?.focusNode).toBe(text);
        expect(document.activeElement).toBe(byId("box"));
    });
});
